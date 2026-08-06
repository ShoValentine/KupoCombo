using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class RecoveryPlanValidator
{
    public PlanValidationResult Validate(
        RecoveryPlan recoveryPlan,
        PracticePlan originalPlan)
    {
        ArgumentNullException.ThrowIfNull(recoveryPlan);
        ArgumentNullException.ThrowIfNull(originalPlan);

        var issues = new List<PlanValidationIssue>();

        if (recoveryPlan.Disposition == RecoveryPlanDisposition.Unavailable)
        {
            if (recoveryPlan.IsAvailable ||
                recoveryPlan.RecoverySteps.Count > 0 ||
                recoveryPlan.OriginalRejoinOffset.HasValue ||
                recoveryPlan.RevisedRejoinOffset.HasValue ||
                recoveryPlan.ConvergenceStepCount != 0)
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidRecoveryConvergence,
                    "An unavailable recovery plan contains executable or rejoin metadata.");
            }

            return Result(issues);
        }

        if (!recoveryPlan.IsAvailable)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryPrefix,
                "An available recovery disposition must contain a revised plan.");
            return Result(issues);
        }

        if (!recoveryPlan.Job.Equals(
                recoveryPlan.RevisedPlan.Job,
                StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                issues,
                PlanValidationCode.JobMismatch,
                $"Recovery job '{recoveryPlan.Job}' does not match revised plan job '{recoveryPlan.RevisedPlan.Job}'.");
        }

        if (recoveryPlan.UsedActionId == 0 ||
            recoveryPlan.ExpectedActionId == 0)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryPrefix,
                "Recovery trigger action IDs must both be non-zero.");
        }

        ValidateRecoveryPrefix(recoveryPlan, issues);

        switch (recoveryPlan.Disposition)
        {
            case RecoveryPlanDisposition.ImmediateRejoin:
                ValidateImmediateRejoin(recoveryPlan, originalPlan, issues);
                break;

            case RecoveryPlanDisposition.GuidedRecovery:
                ValidateGuidedRejoin(recoveryPlan, originalPlan, issues);
                break;

            case RecoveryPlanDisposition.ReplaceRemainingPlan:
                if (recoveryPlan.OriginalRejoinOffset.HasValue ||
                    recoveryPlan.RevisedRejoinOffset.HasValue ||
                    recoveryPlan.ConvergenceStepCount != 0)
                {
                    AddError(
                        issues,
                        PlanValidationCode.InvalidRecoveryConvergence,
                        "A replacement recovery route must not claim an original-plan convergence point.");
                }
                break;
        }

        return Result(issues);
    }

    private static void ValidateRecoveryPrefix(
        RecoveryPlan recoveryPlan,
        List<PlanValidationIssue> issues)
    {
        if (recoveryPlan.RecoverySteps.Count >
            recoveryPlan.RevisedPlan.Steps.Count)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryPrefix,
                "Recovery prefix is longer than the revised plan.");
            return;
        }

        for (var index = 0;
             index < recoveryPlan.RecoverySteps.Count;
             index++)
        {
            if (!SameExecutionStep(
                    recoveryPlan.RecoverySteps[index],
                    recoveryPlan.RevisedPlan.Steps[index]))
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidRecoveryPrefix,
                    $"Recovery step {index} is not the corresponding prefix step of the revised plan.",
                    index,
                    recoveryPlan.RecoverySteps[index].GcdActionId);
            }
        }
    }

    private static void ValidateImmediateRejoin(
        RecoveryPlan recoveryPlan,
        PracticePlan originalPlan,
        List<PlanValidationIssue> issues)
    {
        if (recoveryPlan.RecoverySteps.Count != 0 ||
            recoveryPlan.RevisedRejoinOffset != 0)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryPrefix,
                "An immediate rejoin must have no recovery prefix and must rejoin at revised offset zero.");
        }

        ValidateConvergence(recoveryPlan, originalPlan, issues);
    }

    private static void ValidateGuidedRejoin(
        RecoveryPlan recoveryPlan,
        PracticePlan originalPlan,
        List<PlanValidationIssue> issues)
    {
        if (!recoveryPlan.RevisedRejoinOffset.HasValue ||
            recoveryPlan.RevisedRejoinOffset.Value <= 0 ||
            recoveryPlan.RecoverySteps.Count !=
            recoveryPlan.RevisedRejoinOffset.Value)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryPrefix,
                "A guided recovery prefix must end exactly at its revised rejoin offset.");
        }

        ValidateConvergence(recoveryPlan, originalPlan, issues);
    }

    private static void ValidateConvergence(
        RecoveryPlan recoveryPlan,
        PracticePlan originalPlan,
        List<PlanValidationIssue> issues)
    {
        if (!recoveryPlan.OriginalRejoinOffset.HasValue ||
            !recoveryPlan.RevisedRejoinOffset.HasValue ||
            recoveryPlan.ConvergenceStepCount <= 0)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryConvergence,
                "A rejoining recovery plan must declare both offsets and a positive convergence window.");
            return;
        }

        var originalOffset = recoveryPlan.OriginalRejoinOffset.Value;
        var revisedOffset = recoveryPlan.RevisedRejoinOffset.Value;
        var count = recoveryPlan.ConvergenceStepCount;

        if (originalOffset < 0 ||
            revisedOffset < 0 ||
            originalOffset + count > originalPlan.Steps.Count ||
            revisedOffset + count > recoveryPlan.RevisedPlan.Steps.Count)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidRecoveryConvergence,
                "Recovery convergence metadata points outside the original or revised plan.");
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var original = originalPlan.Steps[originalOffset + index];
            var revised = recoveryPlan.RevisedPlan.Steps[revisedOffset + index];

            if (!SameExecutionStep(original, revised))
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidRecoveryConvergence,
                    $"Recovery convergence diverges at window step {index}.",
                    revisedOffset + index,
                    revised.GcdActionId);
            }
        }
    }

    private static bool SameExecutionStep(
        TrainingForecastStep left,
        TrainingForecastStep right)
    {
        return left.Phase == right.Phase &&
            left.GcdActionId == right.GcdActionId &&
            left.SuggestedActionIds.SequenceEqual(right.SuggestedActionIds);
    }

    private static PlanValidationResult Result(
        IReadOnlyList<PlanValidationIssue> issues)
    {
        return new PlanValidationResult
        {
            Issues = issues.ToArray()
        };
    }

    private static void AddError(
        List<PlanValidationIssue> issues,
        PlanValidationCode code,
        string message,
        int? stepOffset = null,
        uint actionId = 0)
    {
        issues.Add(
            new PlanValidationIssue
            {
                Severity = PlanValidationSeverity.Error,
                Code = code,
                StepOffset = stepOffset,
                ActionId = actionId,
                Message = message
            });
    }
}
