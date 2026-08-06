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
    Complete
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
    private const float DefaultGcdSeconds = 2.5f;

    private static readonly TimeSpan PlanRefreshInterval =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MpAttributionGracePeriod =
        TimeSpan.FromMilliseconds(750);

    private readonly Queue<PendingMpAction> pendingMpActions = new();
    private bool hasReceivedLiveState;
    private DateTime nextPlanRefreshUtc = DateTime.MinValue;
    private int pendingMpWindowBefore;
    private DateTime pendingMpWindowStartedUtc = DateTime.MinValue;

    public ITrainingPolicy? Policy { get; private set; }

    public SequenceDefinition? Sequence =>
        (Policy as SequenceTrainingPolicy)?.Sequence;

    public TrainingState Snapshot { get; } = new();

    public TrainingDecision? CurrentDecision { get; private set; }

    public PracticePlan CurrentPlan { get; private set; } = PracticePlan.Empty;

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

    public IReadOnlyList<MpTransaction> MpTransactions =>
        Snapshot.MpTransactions;

    public bool IsActive =>
        State == TrainingSessionState.Armed ||
        State == TrainingSessionState.Running;

    public bool IsComplete =>
        State == TrainingSessionState.Complete;

    public bool IsEndless =>
        Policy != null && !Policy.ExpectedLength.HasValue;

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
        ClearPendingMpActions();
        CurrentPlan = PracticePlan.Empty;
        RecalculateDecision();
        State = CurrentDecision?.IsComplete == true
            ? TrainingSessionState.Complete
            : TrainingSessionState.Armed;
    }

    public void ObserveAction(uint actionId)
    {
        if (!IsActive ||
            Policy is not IPracticePlanPolicy planPolicy ||
            !planPolicy.TracksMpAction(actionId, Snapshot))
        {
            return;
        }

        if (pendingMpActions.Count == 0)
        {
            pendingMpWindowBefore = Snapshot.GetGauge("mp");
            pendingMpWindowStartedUtc = DateTime.UtcNow;
        }

        pendingMpActions.Enqueue(
            new PendingMpAction(
                actionId,
                planPolicy.GetExpectedMpDelta(actionId, Snapshot)));
    }

    public void RefreshState(Action<TrainingState> refresh)
    {
        if (!IsActive || Policy == null)
        {
            return;
        }

        var hadLiveState = hasReceivedLiveState;
        var mpBefore = Snapshot.GetGauge("mp");
        refresh(Snapshot);
        var mpAfter = Snapshot.GetGauge("mp");

        if (hadLiveState)
        {
            RecordMpObservation(
                mpBefore,
                mpAfter,
                DateTime.UtcNow);
        }

        if (!hasReceivedLiveState)
        {
            hasReceivedLiveState = true;
            RecalculateDecision();
        }
        else if (Policy is ITrainingForecastPolicy)
        {
            ReconcileForecastWithoutBreakingCommitment();
        }
        else
        {
            RecalculateDecision();
        }

        if (CurrentDecision?.IsComplete == true)
        {
            State = TrainingSessionState.Complete;
        }
    }

    public void Stop()
    {
        FlushPendingMpActions(Snapshot.GetGauge("mp"));
        Policy = null;
        CurrentDecision = null;
        CurrentPlan = PracticePlan.Empty;
        CurrentForecast = Array.Empty<TrainingForecastStep>();
        Snapshot.Clear();
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        hasReceivedLiveState = false;
        nextPlanRefreshUtc = DateTime.MinValue;
        ClearPendingMpActions();
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

            if (decision.MistakeResponse == TrainingMistakeResponse.ResetProgress)
            {
                Snapshot.ResetProgress();
                State = TrainingSessionState.Armed;
            }
            else
            {
                State = TrainingSessionState.Running;
            }

            RecalculateDecision();

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
        }
        else
        {
            RecalculateDecision();
        }

        if (CurrentDecision?.IsComplete == true)
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

    private void RecordMpObservation(
        int mpBefore,
        int mpAfter,
        DateTime observedAtUtc)
    {
        var observedDelta = mpAfter - mpBefore;

        if (pendingMpActions.Count == 0)
        {
            RecordUnattributedMpMovement(mpBefore, mpAfter);
            return;
        }

        var expectedDelta = pendingMpActions.Sum(
            action => action.ExpectedMpDelta);
        var actionObservedDelta = mpAfter - pendingMpWindowBefore;
        var signMatched =
            expectedDelta != 0 &&
            actionObservedDelta != 0 &&
            Math.Sign(expectedDelta) == Math.Sign(actionObservedDelta);
        var graceExpired =
            observedAtUtc - pendingMpWindowStartedUtc >=
            MpAttributionGracePeriod;

        if (signMatched || graceExpired)
        {
            RecordActionMpTransaction(
                pendingMpActions.ToArray(),
                pendingMpWindowBefore,
                mpAfter);
            ClearPendingMpActions();
            return;
        }

        if (observedDelta != 0)
        {
            RecordUnattributedMpMovement(mpBefore, mpAfter);
            pendingMpWindowBefore = mpAfter;
        }
    }

    private void RecordActionMpTransaction(
        IReadOnlyList<PendingMpAction> actions,
        int mpBefore,
        int mpAfter)
    {
        Snapshot.RecordMpTransaction(
            new MpTransaction
            {
                Kind = MpTransactionKind.ActionWindow,
                ActionIds = actions
                    .Select(action => action.ActionId)
                    .ToArray(),
                BeforeMp = mpBefore,
                AfterMp = mpAfter,
                ExpectedDelta = actions.Sum(action => action.ExpectedMpDelta)
            });
    }

    private void RecordUnattributedMpMovement(
        int mpBefore,
        int mpAfter)
    {
        var observedDelta = mpAfter - mpBefore;

        if (observedDelta == 0)
        {
            return;
        }

        Snapshot.RecordMpTransaction(
            new MpTransaction
            {
                Kind = observedDelta > 0
                    ? MpTransactionKind.PassiveRecovery
                    : MpTransactionKind.Reconciliation,
                BeforeMp = mpBefore,
                AfterMp = mpAfter
            });
    }

    private void FlushPendingMpActions(int currentMp)
    {
        if (pendingMpActions.Count == 0)
        {
            return;
        }

        RecordActionMpTransaction(
            pendingMpActions.ToArray(),
            pendingMpWindowBefore,
            currentMp);
        ClearPendingMpActions();
    }

    private void ClearPendingMpActions()
    {
        pendingMpActions.Clear();
        pendingMpWindowBefore = 0;
        pendingMpWindowStartedUtc = DateTime.MinValue;
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
            return;
        }

        if (Policy is IPracticePlanPolicy planPolicy)
        {
            CurrentPlan = ReindexPlan(
                planPolicy.BuildPracticePlan(Snapshot));
            CurrentForecast = CurrentPlan.Steps
                .Take(ForecastViewportGcdCount)
                .ToArray();
            nextPlanRefreshUtc = DateTime.UtcNow + PlanRefreshInterval;
            return;
        }

        if (Policy is ITrainingForecastPolicy forecastPolicy)
        {
            CurrentPlan = PracticePlan.Empty;
            CurrentForecast = Reindex(
                forecastPolicy.Forecast(
                    Snapshot,
                    ForecastViewportGcdCount));
            return;
        }

        CurrentPlan = PracticePlan.Empty;
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
            CurrentPlan = freshPlan.WithSteps(mergedSteps);
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
            ExpectedMpBefore = step.ExpectedMpBefore,
            ExpectedMpAfter = step.ExpectedMpAfter,
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

    private sealed record PendingMpAction(
        uint ActionId,
        int ExpectedMpDelta);
}
