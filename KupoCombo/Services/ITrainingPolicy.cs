using KupoCombo.Models;

namespace KupoCombo.Services;

public interface ITrainingPolicy
{
    string Id { get; }

    string Name { get; }

    string Job { get; }

    int? ExpectedLength { get; }

    TrainingDecision Evaluate(TrainingState state);
}
