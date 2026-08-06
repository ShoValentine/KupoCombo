using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public interface IPracticePlanPolicy
{
    IReadOnlyCollection<string> TrackedResources { get; }

    PracticePlan BuildPracticePlan(TrainingState state);

    IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
        uint actionId,
        TrainingState state);

    bool TracksResourceAction(uint actionId, TrainingState state)
    {
        return GetExpectedResourceDeltas(actionId, state).Count > 0;
    }
}
