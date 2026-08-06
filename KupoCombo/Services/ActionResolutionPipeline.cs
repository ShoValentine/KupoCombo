using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class ActionResolutionPipeline
{
    public ActionResolution Resolve(
        ActionObservation observation,
        TrainingDecision? decision,
        ITrainingPolicy? policy,
        TrainingState state)
    {
        var candidates = BuildCandidates(decision, policy);

        foreach (var candidate in candidates)
        {
            if (candidate.ActionId == observation.ExecutedActionId)
            {
                return new ActionResolution(
                    observation,
                    candidate.ActionId,
                    candidate.ActionId,
                    candidate.ActionId,
                    ActionResolutionKind.Exact,
                    candidate.Role);
            }
        }

        foreach (var candidate in candidates)
        {
            var adjustedActionId = state.GetAdjustedAction(candidate.ActionId);

            if (adjustedActionId == 0 ||
                adjustedActionId != observation.ExecutedActionId)
            {
                continue;
            }

            return new ActionResolution(
                observation,
                candidate.ActionId,
                candidate.ActionId,
                adjustedActionId,
                ActionResolutionKind.ClientAdjusted,
                candidate.Role);
        }

        return new ActionResolution(
            observation,
            observation.ExecutedActionId,
            0,
            observation.ExecutedActionId,
            ActionResolutionKind.Unresolved,
            ActionResolutionRole.None);
    }

    private static IReadOnlyList<ActionCandidate> BuildCandidates(
        TrainingDecision? decision,
        ITrainingPolicy? policy)
    {
        var result = new List<ActionCandidate>();
        var seen = new HashSet<uint>();

        if (decision is { IsComplete: false })
        {
            Add(
                result,
                seen,
                decision.PreferredActionId,
                ActionResolutionRole.Preferred);

            foreach (var actionId in decision.AcceptableActionIds)
            {
                Add(
                    result,
                    seen,
                    actionId,
                    ActionResolutionRole.Acceptable);
            }

            foreach (var actionId in decision.SuggestedActionIds)
            {
                Add(
                    result,
                    seen,
                    actionId,
                    ActionResolutionRole.Suggested);
            }
        }

        if (policy != null)
        {
            foreach (var actionId in policy.AdvisoryActionIds)
            {
                Add(
                    result,
                    seen,
                    actionId,
                    ActionResolutionRole.Advisory);
            }

            foreach (var actionId in policy.TrackedActionIds)
            {
                Add(
                    result,
                    seen,
                    actionId,
                    ActionResolutionRole.Tracked);
            }
        }

        return result;
    }

    private static void Add(
        ICollection<ActionCandidate> result,
        ISet<uint> seen,
        uint actionId,
        ActionResolutionRole role)
    {
        if (actionId == 0 || !seen.Add(actionId))
        {
            return;
        }

        result.Add(new ActionCandidate(actionId, role));
    }

    private readonly record struct ActionCandidate(
        uint ActionId,
        ActionResolutionRole Role);
}
