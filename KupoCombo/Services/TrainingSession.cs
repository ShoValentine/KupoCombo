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
    Incorrect,
    Completed
}

public sealed class TrainingActionResult
{
    public TrainingActionOutcome Outcome { get; init; }

    public uint UsedActionId { get; init; }

    public uint ExpectedActionId { get; init; }

    public int CompletedStep { get; init; }
}

public sealed class TrainingSession
{
    public SequenceDefinition? Sequence { get; private set; }

    public TrainingSessionState State { get; private set; } =
        TrainingSessionState.Stopped;

    // Number of sequence actions already completed.
    public int CurrentStep { get; private set; }

    public uint LastIncorrectActionId { get; private set; }

    public uint LastExpectedActionId { get; private set; }

    public bool IsActive =>
        State == TrainingSessionState.Armed ||
        State == TrainingSessionState.Running;

    public bool IsComplete =>
        State == TrainingSessionState.Complete;

    public int Length =>
        Sequence?.Actions.Count ?? 0;

    public void Start(SequenceDefinition sequence)
    {
        Sequence = sequence;
        CurrentStep = 0;
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        State = TrainingSessionState.Armed;
    }

    public void Stop()
    {
        Sequence = null;
        CurrentStep = 0;
        LastIncorrectActionId = 0;
        LastExpectedActionId = 0;
        State = TrainingSessionState.Stopped;
    }

    public TrainingActionResult ProcessAction(uint actionId)
    {
        if (!IsActive ||
            Sequence == null ||
            CurrentStep >= Sequence.Actions.Count)
        {
            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Ignored,
                UsedActionId = actionId
            };
        }

        var expectedActionId = Sequence.Actions[CurrentStep];

        if (actionId != expectedActionId)
        {
            LastIncorrectActionId = actionId;
            LastExpectedActionId = expectedActionId;
            CurrentStep = 0;
            State = TrainingSessionState.Armed;

            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Incorrect,
                UsedActionId = actionId,
                ExpectedActionId = expectedActionId,
                CompletedStep = 0
            };
        }

        State = TrainingSessionState.Running;
        CurrentStep++;

        if (CurrentStep >= Sequence.Actions.Count)
        {
            CurrentStep = Sequence.Actions.Count;
            State = TrainingSessionState.Complete;

            return new TrainingActionResult
            {
                Outcome = TrainingActionOutcome.Completed,
                UsedActionId = actionId,
                ExpectedActionId = expectedActionId,
                CompletedStep = CurrentStep
            };
        }

        return new TrainingActionResult
        {
            Outcome = TrainingActionOutcome.Correct,
            UsedActionId = actionId,
            ExpectedActionId = expectedActionId,
            CompletedStep = CurrentStep
        };
    }
}
