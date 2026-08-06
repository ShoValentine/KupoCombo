using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public enum RecoveryPlanDisposition
{
    Unavailable,
    ImmediateRejoin,
    GuidedRecovery,
    ReplaceRemainingPlan
}

public sealed class RecoveryPlanRequest
{
    public PracticePlan OriginalPlan { get; init; } = PracticePlan.Empty;

    public PracticePlan RevisedPlan { get; init; } = PracticePlan.Empty;

    public uint UsedActionId { get; init; }

    public uint ExpectedActionId { get; init; }

    public int MinimumConvergenceSteps { get; init; } = 3;

    public int MaximumRecoverySteps { get; init; } = 8;
}

public sealed class RecoveryPlan
{
    public static RecoveryPlan Empty { get; } = new();

    public string Job { get; init; } = string.Empty;

    public uint UsedActionId { get; init; }

    public uint ExpectedActionId { get; init; }

    public RecoveryPlanDisposition Disposition { get; init; }

    public PracticePlan RevisedPlan { get; init; } = PracticePlan.Empty;

    public IReadOnlyList<TrainingForecastStep> RecoverySteps { get; init; } =
        Array.Empty<TrainingForecastStep>();

    public int? OriginalRejoinOffset { get; init; }

    public int? RevisedRejoinOffset { get; init; }

    public int ConvergenceStepCount { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool IsAvailable =>
        Disposition != RecoveryPlanDisposition.Unavailable &&
        !RevisedPlan.IsEmpty;

    public bool RejoinsOriginalPlan =>
        Disposition == RecoveryPlanDisposition.ImmediateRejoin ||
        Disposition == RecoveryPlanDisposition.GuidedRecovery;
}
