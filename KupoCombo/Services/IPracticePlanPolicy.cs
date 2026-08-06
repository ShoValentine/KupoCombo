using KupoCombo.Models;

namespace KupoCombo.Services;

public interface IPracticePlanPolicy
{
    PracticePlan BuildPracticePlan(TrainingState state);

    bool TracksMpAction(uint actionId, TrainingState state)
    {
        return GetExpectedMpDelta(actionId, state) != 0;
    }

    int GetExpectedMpDelta(uint actionId, TrainingState state);
}
