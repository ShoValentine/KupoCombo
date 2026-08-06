using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public enum TrainingSessionState
{
    Stopped,
    Armed,
    Running,
    Complete,
    Faulted
}

public enum TrainingActionOutcome
{
    Ignored,
    Correct,
    Acceptable,
    Suggested,
    Incorrect,
    Completed
}

public sealed class TrainingActionResult
{
    public TrainingActionOutcome Outcome { get; init; }

    public uint UsedActionId { get; init; }

    public uint ExpectedActionId { get; init; }

    public int CompletedStep { get; init; }

    public bool WasPreferred { get; init; }

    public string DecisionReason { get; init; } = string.Empty;
}

public sealed class TrainingSession
{
    private const int ForecastViewportGcdCount = 12;
    private const int CommittedGcdDepth = 2;
    private const int RecoverySettlingRefreshCount = 2;
    private const int RecoveryConvergenceStepCount = 3;
    private const int MaximumRecoveryStepCount = 8;
    private const float DefaultGcdSeconds = 2.5f;

    private static readonly TimeSpan PlanRefreshInterval =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ResourceAttributionGracePeriod =
        TimeSpan.FromMilliseconds(750);

    private readonly Dictionary<string, PendingResourceWindow>
        pendingResourceWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly RecoveryPlanEngine recoveryPlanEngine = new();
    private readonly PracticePlanValidator practicePlanValidator = new();
    private readonly RecoveryPlanValidator recoveryPlanValidator = new();

    private PendingRecoveryContext? pendingRecovery;
    private int recoveryStepsRemaining;
    private bool hasReceivedLiveState;
    private DateTime nextPlanRefreshUtc = DateTime.MinValue;

    public ITrainingPolicy? Policy { get; private set; }

    public SequenceDefinition? Sequence =>
        (Policy as SequenceTrainingPolicy)?.Sequence;

    public TrainingState Snapshot { get; } = new();

    public TrainingDecision? CurrentDecision { get; private set; }

    public PracticePlan CurrentPlan { get; private set; } = PracticePlan.Empty;

    public RecoveryPlan CurrentRecoveryPlan { get; private set; } =
        RecoveryPlan.Empty;

    public PlanValidationResult CurrentPlanValidation { get; private set; } =
        PlanValidationResult.NotRun;

    public PlanValidationResult CurrentRecoveryValidation { get; private set; } =
        PlanValidationResult.NotRun;

    public PlanValidationResult LastRejectedPlanValidation { get; private set; } =
        PlanValidationResult.NotRun;

    public IReadOnlyList<TrainingForecastStep> CurrentForecast
    {
        get;
        private set;
    } = Array.Empty<TrainingForecastStep>();

    public TrainingSessionState State { get; private set; } =
        TrainingSessionState.Stopped;

    public int CurrentStep => Snapshot.AcceptedActionCount;

    public uint LastIncorrectActionId { get; private set; }

    public uint LastExpectedActionId { get; private set; }

    public RotationPhase CurrentPhase =>
        CurrentForecast.FirstOrDefault()?.Phase
        ?? CurrentPlan.CurrentPhase;

    public IReadOnlyList<ResourceTransaction> ResourceTransactions =>
        Snapshot.ResourceTransactions;

    public bool IsActive =>
        State == TrainingSessionState.Armed ||
        State == TrainingSessionState.Running;

    public bool IsComplete =>
        State == TrainingSessionState.Complete;

    public bool IsFaulted =>
        State == TrainingSessionState.Faulted;

    public bool IsEndless =>
        Policy != null && !Policy.ExpectedLength.HasValue;

    public bool IsRecoveryPending => pendingRecovery != null;

    public bool IsRecovering =>
        CurrentRecoveryPlan.IsAvailable &&
        recoveryStepsRemaining > 0;

    public int RecoveryStepsRemaining => recoveryStepsRemaining;

    public int Length =>
        Policy?.ExpectedLength ?? 0;

    public void Start(SequenceDefinition sequence)
    {
        Start(new SequenceTrainingPolicy(sequence));
    }

    public void Start(ITrainingPolicy policy, int level = 0)
    {
        Policy = policy;
        Snapshot.Begin(policy.Job, level);
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        hasReceivedLiveState = false;
        nextPlanRefreshUtc = DateTime.MinValue;
        ClearPendingResourceActions();
        ResetRecoveryState();
        ResetValidationState();
        CurrentPlan = PracticePlan.Empty;
        RecalculateDecision();

        if (!IsFaulted)
        {
            State = CurrentDecision?.IsComplete == true
                ? TrainingSessionState.Complete
                : TrainingSessionState.Armed;
        }
    }

    public void ObserveAction(uint actionId)
    {
        if (!IsActive ||
            Policy is not IPracticePlanPolicy planPolicy)
        {
            return;
        }

        var expectedDeltas = planPolicy.GetExpectedResourceDeltas(
            actionId,
            Snapshot);
        var observedAtUtc = DateTime.UtcNow;

        foreach (var (resource, expectedDelta) in expectedDeltas)
        {
            if (expectedDelta == 0 ||
                !planPolicy.TrackedResources.Contains(
                    resource,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!pendingResourceWindows.TryGetValue(
                    resource,
                    out var window))
            {
                window = new PendingResourceWindow(
                    resource,
                    Snapshot.GetGauge(resource),
                    observedAtUtc);
                pendingResourceWindows[resource] = window;
            }

            window.Actions.Add(
                new PendingResourceAction(actionId, expectedDelta));
        }
    }

    public void RefreshState(Action<TrainingState> refresh)
    {
        if (!IsActive || Policy == null)
        {
            return;
        }

        var hadLiveState = hasReceivedLiveState;
        var planPolicy = Policy as IPracticePlanPolicy;
        var resourceBefore = planPolicy == null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : CaptureResources(planPolicy.TrackedResources);

        refresh(Snapshot);

        if (hadLiveState && planPolicy != null)
        {
            var observedAtUtc = DateTime.UtcNow;

            foreach (var resource in planPolicy.TrackedResources)
            {
                var before = resourceBefore.TryGetValue(
                    resource,
                    out var value)
                    ? value
                    : 0;
                RecordResourceObservation(
                    resource,
                    before,
                    Snapshot.GetGauge(resource),
                    observedAtUtc);
            }
        }

        if (!hasReceivedLiveState)
        {
            hasReceivedLiveState = true;
            RecalculateDecision();
        }
        else if (pendingRecovery != null)
        {
            ContinuePendingRecovery();
        }
        else if (Policy is ITrainingForecastPolicy)
        {
            ReconcileForecastWithoutBreakingCommitment();
        }
        else
        {
            RecalculateDecision();
        }

        if (!IsFaulted && CurrentDecision?.IsComplete == true)
        {
            State = TrainingSessionState.Complete;
        }
    }

    public void Stop()
    {
        FlushPendingResourceActions();
        Policy = null;
        CurrentDecision = null;
        CurrentPlan = PracticePlan.Empty;
        CurrentForecast = Array.Empty<TrainingForecastStep>();
        Snapshot.Clear();
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        hasReceivedLiveState = false;
        nextPlanRefreshUtc = DateTime.MinValue;
        ClearPendingResourceActions();
        ResetRecoveryState();
        ResetValidationState();
        State = TrainingSessionState.Stopped;
    }

    public TrainingActionResult ProcessAction(uint actionId)
    {
        if (!IsActive ||
            Policy == null ||
            CurrentDecision == null ||
            CurrentDecision.IsComplete)
        {
            return Ignored(actionId);
        }

        ObserveAction(actionId);
        var decision = CurrentDecision;

        if (decision.IsSuggested(actionId))
        {
            Snapshot.RecordObservedAction(actionId);
            State = TrainingSessionState.Running;
            ConsumeCommittedSuggestion(actionId);

            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Suggested,
                UsedActionId = actionId,
                ExpectedActionId = decision.PreferredActionId,
                CompletedStep = CurrentStep,
                DecisionReason = decision.SuggestionReason
            };
        }

        if (Contains(Policy.AdvisoryActionIds, actionId))
        {
            return Ignored(actionId);
        }

        if (Policy.IgnoreUntrackedActions &&
            !Contains(Policy.TrackedActionIds, actionId))
        {
            return Ignored(actionId);
        }

        if (!decision.IsActionAccepted(actionId))
        {
            LastIncorrectActionId = actionId;
            LastExpectedActionId = decision.PreferredActionId;
            Snapshot.RecordRejectedAction(actionId);

            var supportsRecovery =
                decision.MistakeResponse == TrainingMistakeResponse.KeepProgress &&
                Policy is IPracticePlanPolicy &&
                !CurrentPlan.IsEmpty;

            if (supportsRecovery)
            {
                if (IsGcdAction(actionId))
                {
                    AdvancePracticeTimeline(actionId);
                }

                BeginPendingRecovery(
                    CurrentPlan,
                    actionId,
                    decision.PreferredActionId);
                State = TrainingSessionState.Running;
            }
            else
            {
                if (decision.MistakeResponse ==
                    TrainingMistakeResponse.ResetProgress)
                {
                    Snapshot.ResetProgress();
                    State = TrainingSessionState.Armed;
                }
                else
                {
                    State = TrainingSessionState.Running;
                }

                ResetRecoveryState();
                RecalculateDecision();
            }

            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Incorrect,
                UsedActionId = actionId,
                ExpectedActionId = decision.PreferredActionId,
                CompletedStep = CurrentStep,
                DecisionReason = decision.Reason
            };
        }

        var wasPreferred = decision.IsPreferred(actionId);

        Snapshot.RecordAcceptedAction(actionId);
        AdvancePracticeTimeline(actionId);
        State = TrainingSessionState.Running;

        if (wasPreferred &&
            Policy is ITrainingForecastPolicy &&
            CurrentForecast.Count > 0 &&
            CurrentForecast[0].GcdActionId == actionId)
        {
            AdvanceCommittedForecast();
            AdvanceRecoveryProgress(actionId);
        }
        else
        {
            ClearResolvedRecoveryPlan();
            RecalculateDecision();
        }

        if (!IsFaulted && CurrentDecision?.IsComplete == true)
        {
            State = TrainingSessionState.Complete;

            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Completed,
                UsedActionId = actionId,
                ExpectedActionId = decision.PreferredActionId,
                CompletedStep = CurrentStep,
                WasPreferred = wasPreferred,
                DecisionReason = decision.Reason
            };
        }

        return new TrainingActionResult
        {
            Outcome = wasPreferred
                ? TrainingActionOutcome.Correct
                : TrainingActionOutcome.Acceptable,
            UsedActionId = actionId,
            ExpectedActionId = decision.PreferredActionId,
            CompletedStep = CurrentStep,
            WasPreferred = wasPreferred,
            DecisionReason = decision.Reason
        };
    }

    private Dictionary<string, int> CaptureResources(
        IEnumerable<string> resources)
    {
        return resources.ToDictionary(
            resource => resource,
            resource => Snapshot.GetGauge(resource),
            StringComparer.OrdinalIgnoreCase);
    }

    private void AdvancePracticeTimeline(uint actionId)
    {
        var forecastDuration = CurrentForecast.FirstOrDefault()?.DurationSeconds
            ?? DefaultGcdSeconds;
        var elapsedSeconds = Snapshot.GetAdjustedRecastSeconds(
            actionId,
            forecastDuration);

        if (elapsedSeconds <= 0f)
        {
            elapsedSeconds = forecastDuration > 0f
                ? forecastDuration
                : DefaultGcdSeconds;
        }

        Snapshot.SetCombatTimeSeconds(
            Snapshot.CombatTimeSeconds +
            Math.Clamp(elapsedSeconds, 0.5f, 10f));
    }

    private bool IsGcdAction(uint actionId)
    {
        if (Policy is RuleSetTrainingPolicy rulePolicy)
        {
            foreach (var action in rulePolicy.Definition.Actions.Values)
            {
                if (action.ActionId == actionId ||
                    Snapshot.GetAdjustedAction(action.ActionId) == actionId)
                {
                    return action.Lane == PolicyLane.Gcd;
                }
            }
        }

        return CurrentDecision?.PreferredActionId == actionId;
    }

    private void BeginPendingRecovery(
        PracticePlan originalPlan,
        uint usedActionId,
        uint expectedActionId)
    {
        pendingRecovery = new PendingRecoveryContext(
            originalPlan,
            usedActionId,
            expectedActionId,
            RecoverySettlingRefreshCount);
        CurrentRecoveryPlan = RecoveryPlan.Empty;
        CurrentRecoveryValidation = PlanValidationResult.NotRun;
        recoveryStepsRemaining = 0;

        // Keep the committed route motionless while the action's live gauge,
        // status, combo, and cooldown consequences settle into the snapshot.
        nextPlanRefreshUtc = DateTime.MaxValue;
    }

    private void ContinuePendingRecovery()
    {
        if (pendingRecovery == null)
        {
            return;
        }

        pendingRecovery.RefreshesRemaining--;

        if (pendingRecovery.RefreshesRemaining > 0)
        {
            return;
        }

        ResolvePendingRecovery();
    }

    private void ResolvePendingRecovery()
    {
        var recovery = pendingRecovery;
        pendingRecovery = null;

        if (recovery == null ||
            Policy is not IPracticePlanPolicy planPolicy)
        {
            ResetRecoveryState();
            RecalculateDecision();
            return;
        }

        var freshDecision = Policy.Evaluate(Snapshot);
        var freshPlan = freshDecision.IsComplete
            ? PracticePlan.Empty
            : ReindexPlan(planPolicy.BuildPracticePlan(Snapshot));

        if (!freshPlan.IsEmpty &&
            !TryValidatePlan(freshPlan, faultOnFailure: true))
        {
            return;
        }

        var candidateRecovery = recoveryPlanEngine.Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = recovery.OriginalPlan,
                RevisedPlan = freshPlan,
                UsedActionId = recovery.UsedActionId,
                ExpectedActionId = recovery.ExpectedActionId,
                MinimumConvergenceSteps = RecoveryConvergenceStepCount,
                MaximumRecoverySteps = MaximumRecoveryStepCount
            });
        CurrentRecoveryValidation = recoveryPlanValidator.Validate(
            candidateRecovery,
            recovery.OriginalPlan);

        if (!CurrentRecoveryValidation.IsValid)
        {
            LastRejectedPlanValidation = CurrentRecoveryValidation;
            RejectInvalidPlan(CurrentRecoveryValidation);
            return;
        }

        CurrentRecoveryPlan = candidateRecovery;
        recoveryStepsRemaining = CurrentRecoveryPlan.Disposition ==
            RecoveryPlanDisposition.GuidedRecovery
            ? CurrentRecoveryPlan.RecoverySteps.Count
            : 0;

        if (freshDecision.IsComplete || freshPlan.IsEmpty)
        {
            CurrentDecision = freshDecision;
            CurrentPlan = freshPlan;
            CurrentForecast = Array.Empty<TrainingForecastStep>();
            nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
            return;
        }

        AdoptFreshPlan(freshDecision, freshPlan);
    }

    private void AdoptFreshPlan(
        TrainingDecision freshDecision,
        PracticePlan freshPlan)
    {
        CurrentPlan = freshPlan;
        CurrentForecast = CurrentPlan.Steps
            .Take(ForecastViewportGcdCount)
            .ToArray();

        if (CurrentForecast.Count == 0)
        {
            CurrentDecision = freshDecision;
            nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
            return;
        }

        var head = CurrentForecast[0];
        CurrentDecision = new TrainingDecision
        {
            PreferredActionId = head.GcdActionId,
            AcceptableActionIds = freshDecision.PreferredActionId ==
                head.GcdActionId
                ? freshDecision.AcceptableActionIds
                : Array.Empty<uint>(),
            SuggestedActionIds = head.SuggestedActionIds,
            Reason = head.Reason,
            SuggestionReason = head.SuggestionReason,
            MistakeResponse = freshDecision.MistakeResponse
        };
        nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
    }

    private void AdvanceRecoveryProgress(uint actionId)
    {
        if (!CurrentRecoveryPlan.IsAvailable)
        {
            return;
        }

        if (recoveryStepsRemaining <= 0)
        {
            ClearResolvedRecoveryPlan();
            return;
        }

        var completedRecoverySteps =
            CurrentRecoveryPlan.RecoverySteps.Count -
            recoveryStepsRemaining;

        if (completedRecoverySteps < 0 ||
            completedRecoverySteps >=
            CurrentRecoveryPlan.RecoverySteps.Count)
        {
            ClearResolvedRecoveryPlan();
            return;
        }

        if (CurrentRecoveryPlan
                .RecoverySteps[completedRecoverySteps]
                .GcdActionId != actionId)
        {
            return;
        }

        recoveryStepsRemaining--;

        if (recoveryStepsRemaining <= 0)
        {
            ClearResolvedRecoveryPlan();
        }
    }

    private void ClearResolvedRecoveryPlan()
    {
        CurrentRecoveryPlan = RecoveryPlan.Empty;
        CurrentRecoveryValidation = PlanValidationResult.NotRun;
        recoveryStepsRemaining = 0;
    }

    private void ResetRecoveryState()
    {
        pendingRecovery = null;
        ClearResolvedRecoveryPlan();
    }

    private void ResetValidationState()
    {
        CurrentPlanValidation = PlanValidationResult.NotRun;
        CurrentRecoveryValidation = PlanValidationResult.NotRun;
        LastRejectedPlanValidation = PlanValidationResult.NotRun;
    }

    private bool TryValidatePlan(
        PracticePlan plan,
        PracticePlan? committedPlan = null,
        int committedDepth = 0,
        bool requireStateOriginMatch = true,
        bool faultOnFailure = true)
    {
        var result = practicePlanValidator.Validate(
            new PlanValidationRequest
            {
                Plan = plan,
                State = Snapshot,
                CommittedPlan = committedPlan,
                CommittedDepth = committedDepth,
                RequireStateOriginMatch = requireStateOriginMatch
            },
            Policy);

        if (result.IsValid)
        {
            CurrentPlanValidation = result;
            return true;
        }

        LastRejectedPlanValidation = result;

        if (faultOnFailure)
        {
            RejectInvalidPlan(result);
        }

        return false;
    }

    private void RejectInvalidPlan(PlanValidationResult validation)
    {
        pendingRecovery = null;
        CurrentRecoveryPlan = RecoveryPlan.Empty;
        recoveryStepsRemaining = 0;
        CurrentPlan = PracticePlan.Empty;
        CurrentForecast = Array.Empty<TrainingForecastStep>();
        CurrentDecision = TrainingDecision.Complete(validation.Summary);
        State = TrainingSessionState.Faulted;
    }

    private void RecordResourceObservation(
        string resource,
        int before,
        int after,
        DateTime observedAtUtc)
    {
        var observedDelta = after - before;

        if (!pendingResourceWindows.TryGetValue(resource, out var window))
        {
            RecordUnattributedResourceMovement(resource, before, after);
            return;
        }

        var expectedDelta = window.Actions.Sum(
            action => action.ExpectedDelta);
        var actionObservedDelta = after - window.Before;
        var signMatched =
            expectedDelta != 0 &&
            actionObservedDelta != 0 &&
            Math.Sign(expectedDelta) == Math.Sign(actionObservedDelta);
        var graceExpired =
            observedAtUtc - window.StartedAtUtc >=
            ResourceAttributionGracePeriod;

        if (signMatched || graceExpired)
        {
            RecordActionResourceTransaction(window, after);
            pendingResourceWindows.Remove(resource);
            return;
        }

        if (observedDelta != 0)
        {
            RecordUnattributedResourceMovement(resource, before, after);
            window.Before = after;
        }
    }

    private void RecordActionResourceTransaction(
        PendingResourceWindow window,
        int after)
    {
        Snapshot.RecordResourceTransaction(
            new ResourceTransaction
            {
                Kind = ResourceTransactionKind.ActionWindow,
                Resource = window.Resource,
                ActionIds = window.Actions
                    .Select(action => action.ActionId)
                    .ToArray(),
                Before = window.Before,
                After = after,
                ExpectedDelta = window.Actions.Sum(
                    action => action.ExpectedDelta)
            });
    }

    private void RecordUnattributedResourceMovement(
        string resource,
        int before,
        int after)
    {
        var observedDelta = after - before;

        if (observedDelta == 0)
        {
            return;
        }

        Snapshot.RecordResourceTransaction(
            new ResourceTransaction
            {
                Kind = observedDelta > 0
                    ? ResourceTransactionKind.UnattributedGain
                    : ResourceTransactionKind.Reconciliation,
                Resource = resource,
                Before = before,
                After = after
            });
    }

    private void FlushPendingResourceActions()
    {
        foreach (var window in pendingResourceWindows.Values.ToArray())
        {
            RecordActionResourceTransaction(
                window,
                Snapshot.GetGauge(window.Resource));
        }

        ClearPendingResourceActions();
    }

    private void ClearPendingResourceActions()
    {
        pendingResourceWindows.Clear();
    }

    private void RecalculateDecision()
    {
        if (Policy == null)
        {
            CurrentDecision = null;
            CurrentPlan = PracticePlan.Empty;
            CurrentForecast = Array.Empty<TrainingForecastStep>();
            return;
        }

        CurrentDecision = Policy.Evaluate(Snapshot);

        if (CurrentDecision.IsComplete)
        {
            CurrentPlan = PracticePlan.Empty;
            CurrentForecast = Array.Empty<TrainingForecastStep>();
            CurrentPlanValidation = PlanValidationResult.NotRun;
            return;
        }

        if (Policy is IPracticePlanPolicy planPolicy)
        {
            var candidatePlan = ReindexPlan(
                planPolicy.BuildPracticePlan(Snapshot));

            if (!TryValidatePlan(candidatePlan, faultOnFailure: true))
            {
                return;
            }

            CurrentPlan = candidatePlan;
            CurrentForecast = CurrentPlan.Steps
                .Take(ForecastViewportGcdCount)
                .ToArray();
            nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
            return;
        }

        if (Policy is ITrainingForecastPolicy forecastPolicy)
        {
            CurrentPlan = PracticePlan.Empty;
            CurrentPlanValidation = PlanValidationResult.NotRun;
            CurrentForecast = Reindex(
                forecastPolicy.Forecast(
                    Snapshot,
                    ForecastViewportGcdCount));
            return;
        }

        CurrentPlan = PracticePlan.Empty;
        CurrentPlanValidation = PlanValidationResult.NotRun;
        CurrentForecast = Array.Empty<TrainingForecastStep>();
    }

    private void ReconcileForecastWithoutBreakingCommitment()
    {
        if (Policy == null ||
            Policy is not ITrainingForecastPolicy forecastPolicy)
        {
            RecalculateDecision();
            return;
        }

        if (Policy is IPracticePlanPolicy &&
            DateTime.UtcNow < nextPlanRefreshUtc)
        {
            return;
        }

        var freshDecision = Policy.Evaluate(Snapshot);
        PracticePlan freshPlan;
        IReadOnlyList<TrainingForecastStep> freshSteps;

        if (Policy is IPracticePlanPolicy planPolicy)
        {
            freshPlan = ReindexPlan(planPolicy.BuildPracticePlan(Snapshot));

            if (!TryValidatePlan(freshPlan, faultOnFailure: true))
            {
                return;
            }

            freshSteps = freshPlan.Steps;
            nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
        }
        else
        {
            freshPlan = PracticePlan.Empty;
            freshSteps = Reindex(
                forecastPolicy.Forecast(
                    Snapshot,
                    ForecastViewportGcdCount));
        }

        var currentSteps = !CurrentPlan.IsEmpty
            ? CurrentPlan.Steps
            : CurrentForecast;

        if (CurrentDecision == null || currentSteps.Count == 0)
        {
            CurrentDecision = freshDecision;
            CurrentPlan = freshPlan;
            CurrentForecast = freshSteps
                .Take(ForecastViewportGcdCount)
                .ToArray();
            return;
        }

        if (freshDecision.IsComplete || freshSteps.Count == 0)
        {
            return;
        }

        var committedDepth = Math.Min(
            CommittedGcdDepth,
            Math.Min(currentSteps.Count, freshSteps.Count));

        for (var index = 0; index < committedDepth; index++)
        {
            if (currentSteps[index].GcdActionId !=
                freshSteps[index].GcdActionId)
            {
                return;
            }
        }

        var mergedSteps = Reindex(
            currentSteps
                .Take(committedDepth)
                .Concat(freshSteps.Skip(committedDepth)));

        if (!freshPlan.IsEmpty)
        {
            var committedPlan = !CurrentPlan.IsEmpty
                ? CurrentPlan
                : new PracticePlan
                {
                    Job = freshPlan.Job,
                    StartsAtCombatTimeSeconds =
                        freshPlan.StartsAtCombatTimeSeconds,
                    HorizonSeconds = freshPlan.HorizonSeconds,
                    TimingProfile = freshPlan.TimingProfile.Clone(),
                    Steps = currentSteps
                };
            var mergedPlan = freshPlan.WithSteps(mergedSteps);

            if (!TryValidatePlan(
                    mergedPlan,
                    committedPlan,
                    committedDepth,
                    requireStateOriginMatch: false,
                    faultOnFailure: false))
            {
                return;
            }

            CurrentPlan = mergedPlan;
            CurrentForecast = CurrentPlan.Steps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }
        else
        {
            CurrentPlan = PracticePlan.Empty;
            CurrentForecast = mergedSteps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }

        var committedHead = CurrentForecast[0];
        CurrentDecision = new TrainingDecision
        {
            PreferredActionId = committedHead.GcdActionId,
            AcceptableActionIds = freshDecision.PreferredActionId ==
                committedHead.GcdActionId
                ? freshDecision.AcceptableActionIds
                : CurrentDecision.AcceptableActionIds,
            SuggestedActionIds = committedHead.SuggestedActionIds,
            Reason = committedHead.Reason,
            SuggestionReason = committedHead.SuggestionReason,
            MistakeResponse = CurrentDecision.MistakeResponse
        };
    }

    private void ConsumeCommittedSuggestion(uint actionId)
    {
        if (CurrentDecision == null)
        {
            return;
        }

        var remainingSuggestions = CurrentDecision.SuggestedActionIds
            .Where(candidate => candidate != actionId)
            .ToArray();

        CurrentDecision = new TrainingDecision
        {
            PreferredActionId = CurrentDecision.PreferredActionId,
            AcceptableActionIds = CurrentDecision.AcceptableActionIds,
            SuggestedActionIds = remainingSuggestions,
            Reason = CurrentDecision.Reason,
            SuggestionReason = CurrentDecision.SuggestionReason,
            MistakeResponse = CurrentDecision.MistakeResponse
        };

        var sourceSteps = !CurrentPlan.IsEmpty
            ? CurrentPlan.Steps
            : CurrentForecast;

        if (sourceSteps.Count == 0)
        {
            return;
        }

        var head = sourceSteps[0];
        var replacement = CopyStep(
            head,
            suggestedActionIds: head.SuggestedActionIds
                .Where(candidate => candidate != actionId)
                .ToArray());
        var updatedSteps = Reindex(
            new[] { replacement }
                .Concat(sourceSteps.Skip(1)));

        if (!CurrentPlan.IsEmpty)
        {
            CurrentPlan = CurrentPlan.WithSteps(updatedSteps);
            CurrentForecast = CurrentPlan.Steps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }
        else
        {
            CurrentForecast = updatedSteps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }
    }

    private void AdvanceCommittedForecast()
    {
        var sourceSteps = !CurrentPlan.IsEmpty
            ? CurrentPlan.Steps
            : CurrentForecast;
        var remainingSteps = Reindex(sourceSteps.Skip(1));

        if (!CurrentPlan.IsEmpty)
        {
            CurrentPlan = CurrentPlan.WithSteps(remainingSteps);
            CurrentForecast = CurrentPlan.Steps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }
        else
        {
            CurrentForecast = remainingSteps
                .Take(ForecastViewportGcdCount)
                .ToArray();
        }

        if (CurrentForecast.Count == 0)
        {
            RecalculateDecision();
            return;
        }

        var head = CurrentForecast[0];
        CurrentDecision = new TrainingDecision
        {
            PreferredActionId = head.GcdActionId,
            SuggestedActionIds = head.SuggestedActionIds,
            Reason = head.Reason,
            SuggestionReason = head.SuggestionReason,
            MistakeResponse = CurrentDecision?.MistakeResponse
                ?? TrainingMistakeResponse.KeepProgress
        };
    }

    private static PracticePlan ReindexPlan(PracticePlan plan)
    {
        return plan.WithSteps(Reindex(plan.Steps));
    }

    private static IReadOnlyList<TrainingForecastStep> Reindex(
        IEnumerable<TrainingForecastStep> forecast)
    {
        var result = new List<TrainingForecastStep>();
        var startsAtSeconds = 0d;

        foreach (var step in forecast)
        {
            var copy = CopyStep(
                step,
                offset: result.Count,
                startsAtSeconds: startsAtSeconds);
            result.Add(copy);
            startsAtSeconds += Math.Max(0f, copy.DurationSeconds);
        }

        return result;
    }

    private static TrainingForecastStep CopyStep(
        TrainingForecastStep step,
        int? offset = null,
        double? startsAtSeconds = null,
        IReadOnlyList<uint>? suggestedActionIds = null)
    {
        return new TrainingForecastStep
        {
            Offset = offset ?? step.Offset,
            StartsAtSeconds = startsAtSeconds ?? step.StartsAtSeconds,
            DurationSeconds = step.DurationSeconds,
            Phase = step.Phase,
            GcdActionId = step.GcdActionId,
            SuggestedActionIds = suggestedActionIds
                ?? step.SuggestedActionIds,
            ResourceProjections = step.ResourceProjections,
            Reason = step.Reason,
            SuggestionReason = step.SuggestionReason,
            Confidence = step.Confidence
        };
    }

    private static bool Contains(
        IReadOnlyCollection<uint> actionIds,
        uint actionId)
    {
        foreach (var candidateActionId in actionIds)
        {
            if (candidateActionId == actionId)
            {
                return true;
            }
        }

        return false;
    }

    private static TrainingActionResult Ignored(uint actionId)
    {
        return new TrainingActionResult
        {
            Outcome = TrainingActionOutcome.Ignored,
            UsedActionId = actionId
        };
    }

    private sealed class PendingRecoveryContext
    {
        public PendingRecoveryContext(
            PracticePlan originalPlan,
            uint usedActionId,
            uint expectedActionId,
            int refreshesRemaining)
        {
            OriginalPlan = originalPlan;
            UsedActionId = usedActionId;
            ExpectedActionId = expectedActionId;
            RefreshesRemaining = refreshesRemaining;
        }

        public PracticePlan OriginalPlan { get; }

        public uint UsedActionId { get; }

        public uint ExpectedActionId { get; }

        public int RefreshesRemaining { get; set; }
    }

    private sealed class PendingResourceWindow
    {
        public PendingResourceWindow(
            string resource,
            int before,
            DateTime startedAtUtc)
        {
            Resource = resource;
            Before = before;
            StartedAtUtc = startedAtUtc;
        }

        public string Resource { get; }

        public int Before { get; set; }

        public DateTime StartedAtUtc { get; }

        public List<PendingResourceAction> Actions { get; } = new();
    }

    private sealed record PendingResourceAction(
        uint ActionId,
        int ExpectedDelta);
}
