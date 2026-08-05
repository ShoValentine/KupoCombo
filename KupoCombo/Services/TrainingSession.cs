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
    private const int ForecastGcdCount = 8;
    private const int CommittedGcdDepth = 2;

    private bool hasReceivedLiveState;

    public ITrainingPolicy? Policy { get; private set; }

    public SequenceDefinition? Sequence =>
        (Policy as SequenceTrainingPolicy)?.Sequence;

    public TrainingState Snapshot { get; } = new();

    public TrainingDecision? CurrentDecision { get; private set; }

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
        RecalculateDecision();
        State = CurrentDecision?.IsComplete == true
            ? TrainingSessionState.Complete
            : TrainingSessionState.Armed;
    }

    public void RefreshState(Action<TrainingState> refresh)
    {
        if (!IsActive || Policy == null)
        {
            return;
        }

        refresh(Snapshot);

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
        Policy = null;
        CurrentDecision = null;
        CurrentForecast = Array.Empty<TrainingForecastStep>();
        Snapshot.Clear();
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        hasReceivedLiveState = false;
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

    private void RecalculateDecision()
    {
        if (Policy == null)
        {
            CurrentDecision = null;
            CurrentForecast = Array.Empty<TrainingForecastStep>();
            return;
        }

        CurrentDecision = Policy.Evaluate(Snapshot);

        if (CurrentDecision.IsComplete ||
            Policy is not ITrainingForecastPolicy forecastPolicy)
        {
            CurrentForecast = Array.Empty<TrainingForecastStep>();
            return;
        }

        CurrentForecast = Reindex(
            forecastPolicy.Forecast(
                Snapshot,
                ForecastGcdCount));
    }

    private void ReconcileForecastWithoutBreakingCommitment()
    {
        if (Policy == null ||
            Policy is not ITrainingForecastPolicy forecastPolicy)
        {
            RecalculateDecision();
            return;
        }

        var freshDecision = Policy.Evaluate(Snapshot);
        var freshForecast = forecastPolicy.Forecast(
            Snapshot,
            ForecastGcdCount);

        if (CurrentDecision == null ||
            CurrentForecast.Count == 0)
        {
            CurrentDecision = freshDecision;
            CurrentForecast = Reindex(freshForecast);
            return;
        }

        if (freshDecision.IsComplete || freshForecast.Count == 0)
        {
            return;
        }

        var committedDepth = Math.Min(
            CommittedGcdDepth,
            Math.Min(CurrentForecast.Count, freshForecast.Count));

        for (var index = 0; index < committedDepth; index++)
        {
            if (CurrentForecast[index].GcdActionId !=
                freshForecast[index].GcdActionId)
            {
                return;
            }
        }

        var merged = CurrentForecast
            .Take(committedDepth)
            .Concat(freshForecast.Skip(committedDepth))
            .Take(ForecastGcdCount)
            .ToArray();

        CurrentForecast = Reindex(merged);

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

        if (CurrentForecast.Count == 0)
        {
            return;
        }

        var head = CurrentForecast[0];
        var replacement = new TrainingForecastStep
        {
            Offset = 0,
            GcdActionId = head.GcdActionId,
            SuggestedActionIds = head.SuggestedActionIds
                .Where(candidate => candidate != actionId)
                .ToArray(),
            Reason = head.Reason,
            SuggestionReason = head.SuggestionReason,
            Confidence = head.Confidence
        };

        CurrentForecast = Reindex(
            new[] { replacement }
                .Concat(CurrentForecast.Skip(1))
                .ToArray());
    }

    private void AdvanceCommittedForecast()
    {
        CurrentForecast = Reindex(
            CurrentForecast.Skip(1).ToArray());

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

    private static IReadOnlyList<TrainingForecastStep> Reindex(
        IEnumerable<TrainingForecastStep> forecast)
    {
        return forecast
            .Select((step, index) => new TrainingForecastStep
            {
                Offset = index,
                GcdActionId = step.GcdActionId,
                SuggestedActionIds = step.SuggestedActionIds,
                Reason = step.Reason,
                SuggestionReason = step.SuggestionReason,
                Confidence = step.Confidence
            })
            .ToArray();
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
}
