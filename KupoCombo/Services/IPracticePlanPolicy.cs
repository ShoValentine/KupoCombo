using KupoCombo.Models;

namespace KupoCombo.Services;

public interface IPracticePlanPolicy
{
    PracticePlan BuildPracticePlan(TrainingState state);

    int GetExpectedMpDelta(uint actionId, TrainingState state);
}
