using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class RecoveryPlanEngine
{
    public RecoveryPlan Build(
        RecoveryPlanRequest request,
        Func<TrainingForecastStep, TrainingForecastStep, bool>?
            convergenceRule = null)
    {
        var revisedPlan = request.RevisedPlan;

        if (revisedPlan.IsEmpty)
        {
            return new RecoveryPlan
            {
                Job = ResolveJob(request),
                UsedActionId = request.UsedActionId,
                ExpectedActionId = request.ExpectedActionId,
                Disposition = RecoveryPlanDisposition.Unavailable,
                Reason =
                    "The policy could not produce a legal route from the refreshed state."
            };
        }

        var originalPlan = request.OriginalPlan;
        var minimumConvergenceSteps = Math.Max(
            1,
            request.MinimumConvergenceSteps);
        var maximumRecoverySteps = Math.Max(
            0,
            request.MaximumRecoverySteps);

        if (!originalPlan.IsEmpty)
        {
            var convergence = FindConvergence(
                originalPlan.Steps,
                revisedPlan.Steps,
                minimumConvergenceSteps,
                maximumRecoverySteps,
                convergenceRule ?? DefaultConvergenceRule);

            if (convergence != null)
            {
                var recoverySteps = revisedPlan.Steps
                    .Take(convergence.RevisedOffset)
                    .ToArray();
                var disposition = convergence.RevisedOffset == 0
                    ? RecoveryPlanDisposition.ImmediateRejoin
                    : RecoveryPlanDisposition.GuidedRecovery;

                return new RecoveryPlan
                {
                    Job = ResolveJob(request),
                    UsedActionId = request.UsedActionId,
                    ExpectedActionId = request.ExpectedActionId,
                    Disposition = disposition,
                    RevisedPlan = revisedPlan,
                    RecoverySteps = recoverySteps,
                    OriginalRejoinOffset = convergence.OriginalOffset,
                    RevisedRejoinOffset = convergence.RevisedOffset,
                    ConvergenceStepCount = minimumConvergenceSteps,
                    Reason = disposition == RecoveryPlanDisposition.ImmediateRejoin
                        ? "The refreshed route already matches the committed plan."
                        : $"Follow {recoverySteps.Length} recovery step(s), then rejoin " +
                          $"the original plan at step {convergence.OriginalOffset + 1}."
                };
            }
        }

        return new RecoveryPlan
        {
            Job = ResolveJob(request),
            UsedActionId = request.UsedActionId,
            ExpectedActionId = request.ExpectedActionId,
            Disposition = RecoveryPlanDisposition.ReplaceRemainingPlan,
            RevisedPlan = revisedPlan,
            RecoverySteps = revisedPlan.Steps
                .Take(maximumRecoverySteps)
                .ToArray(),
            Reason = originalPlan.IsEmpty
                ? "No committed plan was available, so the refreshed route becomes authoritative."
                : "No stable convergence was found within the recovery horizon; " +
                  "the refreshed route replaces the remaining plan."
        };
    }

    private static ConvergencePoint? FindConvergence(
        IReadOnlyList<TrainingForecastStep> originalSteps,
        IReadOnlyList<TrainingForecastStep> revisedSteps,
        int requiredStepCount,
        int maximumRecoverySteps,
        Func<TrainingForecastStep, TrainingForecastStep, bool> rule)
    {
        if (originalSteps.Count < requiredStepCount ||
            revisedSteps.Count < requiredStepCount)
        {
            return null;
        }

        var maximumRevisedOffset = Math.Min(
            maximumRecoverySteps,
            revisedSteps.Count - requiredStepCount);

        for (var revisedOffset = 0;
             revisedOffset <= maximumRevisedOffset;
             revisedOffset++)
        {
            var maximumOriginalOffset =
                originalSteps.Count - requiredStepCount;

            for (var originalOffset = 0;
                 originalOffset <= maximumOriginalOffset;
                 originalOffset++)
            {
                if (WindowMatches(
                        originalSteps,
                        revisedSteps,
                        originalOffset,
                        revisedOffset,
                        requiredStepCount,
                        rule))
                {
                    return new ConvergencePoint(
                        originalOffset,
                        revisedOffset);
                }
            }
        }

        return null;
    }

    private static bool WindowMatches(
        IReadOnlyList<TrainingForecastStep> originalSteps,
        IReadOnlyList<TrainingForecastStep> revisedSteps,
        int originalOffset,
        int revisedOffset,
        int requiredStepCount,
        Func<TrainingForecastStep, TrainingForecastStep, bool> rule)
    {
        for (var index = 0; index < requiredStepCount; index++)
        {
            if (!rule(
                    originalSteps[originalOffset + index],
                    revisedSteps[revisedOffset + index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DefaultConvergenceRule(
        TrainingForecastStep original,
        TrainingForecastStep revised)
    {
        return original.Phase == revised.Phase &&
               original.GcdActionId == revised.GcdActionId &&
               original.SuggestedActionIds.SequenceEqual(
                   revised.SuggestedActionIds);
    }

    private static string ResolveJob(RecoveryPlanRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.RevisedPlan.Job)
            ? request.RevisedPlan.Job
            : request.OriginalPlan.Job;
    }

    private sealed record ConvergencePoint(
        int OriginalOffset,
        int RevisedOffset);
}
