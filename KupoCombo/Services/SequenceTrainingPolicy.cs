using System;
using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class SequenceTrainingPolicy : ITrainingPolicy
{
    public SequenceTrainingPolicy(SequenceDefinition sequence)
    {
        Sequence = sequence;
    }

    public SequenceDefinition Sequence { get; }

    public string Id => Sequence.Id;

    public string Name => Sequence.DisplayName;

    public string Job => Sequence.Job;

    public int? ExpectedLength => Sequence.Actions.Count;

    public IReadOnlyCollection<uint> TrackedActionIds => Sequence.Actions;

    public IReadOnlyCollection<uint> AdvisoryActionIds => Array.Empty<uint>();

    public bool IgnoreUntrackedActions => false;

    public TrainingDecision Evaluate(TrainingState state)
    {
        if (state.AcceptedActionCount >= Sequence.Actions.Count)
        {
            return TrainingDecision.Complete("The sequence is complete.");
        }

        var nextStep = state.AcceptedActionCount;

        return new TrainingDecision
        {
            PreferredActionId = Sequence.Actions[nextStep],
            Reason = $"Sequence step {nextStep + 1} of {Sequence.Actions.Count}.",
            MistakeResponse = TrainingMistakeResponse.ResetProgress
        };
    }
}
