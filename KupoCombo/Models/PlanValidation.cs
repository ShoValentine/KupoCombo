using System;
using System.Collections.Generic;
using System.Linq;

namespace KupoCombo.Models;

public enum PlanValidationSeverity
{
    Warning,
    Error
}

public enum PlanValidationCode
{
    EmptyPlan,
    MissingJob,
    JobMismatch,
    InvalidPlanStart,
    InvalidHorizon,
    InvalidOffset,
    InvalidStepStart,
    InvalidDuration,
    StepOutsideHorizon,
    InvalidPhase,
    InvalidConfidence,
    MissingGcdAction,
    UnknownAction,
    ActionUnavailableAtLevel,
    WrongActionLane,
    UngradedAction,
    InvalidSuggestedAction,
    DuplicateSuggestedAction,
    TooManySuggestedActions,
    AdjustedActionMismatch,
    MissingResourceProjection,
    UnknownResourceProjection,
    ResourceNameMismatch,
    ResourceOriginMismatch,
    ResourceDiscontinuity,
    ResourceOutOfBounds,
    CooldownUnavailable,
    CommitmentChanged,
    InvalidRecoveryPrefix,
    InvalidRecoveryConvergence
}

public sealed class PlanValidationIssue
{
    public PlanValidationSeverity Severity { get; init; }

    public PlanValidationCode Code { get; init; }

    public int? StepOffset { get; init; }

    public uint ActionId { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class PlanValidationResult
{
    public static PlanValidationResult NotRun { get; } = new()
    {
        WasRun = false
    };

    public bool WasRun { get; init; } = true;

    public IReadOnlyList<PlanValidationIssue> Issues { get; init; } =
        Array.Empty<PlanValidationIssue>();

    public bool IsValid => Issues.All(issue =>
        issue.Severity != PlanValidationSeverity.Error);

    public int ErrorCount => Issues.Count(issue =>
        issue.Severity == PlanValidationSeverity.Error);

    public int WarningCount => Issues.Count(issue =>
        issue.Severity == PlanValidationSeverity.Warning);

    public string Summary
    {
        get
        {
            if (!WasRun)
            {
                return "Plan validation has not run.";
            }

            if (Issues.Count == 0)
            {
                return "Plan is legal and internally consistent.";
            }

            var firstError = Issues.FirstOrDefault(issue =>
                issue.Severity == PlanValidationSeverity.Error);

            return firstError == null
                ? $"Plan is legal with {WarningCount} warning(s)."
                : $"Plan rejected with {ErrorCount} error(s): " +
                  firstError.Message;
        }
    }
}

public sealed class PlanValidationRequest
{
    public PracticePlan Plan { get; init; } = PracticePlan.Empty;

    public TrainingState State { get; init; } = new();

    public PracticePlan? CommittedPlan { get; init; }

    public int CommittedDepth { get; init; }

    public bool RequireStateOriginMatch { get; init; } = true;
}
