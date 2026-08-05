using System;
using System.Collections.Generic;
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
    private const int ForecastGcdCount = 4;

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
        RecalculateDecision();

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
            RecalculateDecision();

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
        RecalculateDecision();

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

        CurrentForecast = forecastPolicy.Forecast(
            Snapshot,
            ForecastGcdCount);
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
