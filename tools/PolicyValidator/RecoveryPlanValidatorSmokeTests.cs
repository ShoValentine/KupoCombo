using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class RecoveryPlanValidatorSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        AcceptsEngineGeneratedRecovery();
        RejectsDishonestRecoveryPrefix();
    }

    private static void AcceptsEngineGeneratedRecovery()
    {
        var original = Plan(10, 20, 30, 40, 50);
        var revised = Plan(99, 98, 30, 40, 50);
        var recovery = new RecoveryPlanEngine().Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = original,
                RevisedPlan = revised,
                UsedActionId = 99,
                ExpectedActionId = 10,
                MinimumConvergenceSteps = 3
            });
        var result = new RecoveryPlanValidator().Validate(
            recovery,
            original);

        Require(result.IsValid, result.Summary);
    }

    private static void RejectsDishonestRecoveryPrefix()
    {
        var original = Plan(10, 20, 30, 40, 50);
        var revised = Plan(99, 98, 30, 40, 50);
        var recovery = new RecoveryPlan
        {
            Job = "TST",
            UsedActionId = 99,
            ExpectedActionId = 10,
            Disposition = RecoveryPlanDisposition.GuidedRecovery,
            RevisedPlan = revised,
            RecoverySteps = new[] { revised.Steps[1], revised.Steps[0] },
            OriginalRejoinOffset = 2,
            RevisedRejoinOffset = 2,
            ConvergenceStepCount = 3
        };
        var result = new RecoveryPlanValidator().Validate(
            recovery,
            original);

        Require(
            result.Issues.Any(issue =>
                issue.Code == PlanValidationCode.InvalidRecoveryPrefix &&
                issue.Severity == PlanValidationSeverity.Error),
            "A reordered recovery prefix was accepted.");
    }

    private static PracticePlan Plan(params uint[] actionIds)
    {
        return new PracticePlan
        {
            Job = "TST",
            StartsAtCombatTimeSeconds = 0d,
            HorizonSeconds = actionIds.Length * 2.5d,
            Steps = actionIds
                .Select((actionId, index) =>
                    new TrainingForecastStep
                    {
                        Offset = index,
                        StartsAtSeconds = index * 2.5d,
                        DurationSeconds = 2.5f,
                        Phase = RotationPhase.Filler,
                        GcdActionId = actionId,
                        Confidence = 1f
                    })
                .ToArray()
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
