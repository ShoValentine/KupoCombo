using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class RecoveryPlanEngineSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        DetectsImmediateRejoin();
        BuildsShortestGuidedRecovery();
        RejectsFalseConvergenceAcrossPhases();
        ReplacesPlanWhenNoStableRejoinExists();

        Console.WriteLine(
            "Recovery plan engine found stable rejoin points and replaced " +
            "diverged routes when convergence was unsafe.");
    }

    private static void DetectsImmediateRejoin()
    {
        var original = CreatePlan(
            RotationPhase.Filler,
            10,
            20,
            30);
        var revised = CreatePlan(
            RotationPhase.Filler,
            10,
            20,
            30);

        var recovery = new RecoveryPlanEngine().Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = original,
                RevisedPlan = revised,
                UsedActionId = 900,
                ExpectedActionId = 10,
                MinimumConvergenceSteps = 3
            });

        Require(
            recovery.Disposition == RecoveryPlanDisposition.ImmediateRejoin &&
            recovery.RecoverySteps.Count == 0 &&
            recovery.OriginalRejoinOffset == 0 &&
            recovery.RevisedRejoinOffset == 0,
            "An unchanged route should rejoin immediately.");
    }

    private static void BuildsShortestGuidedRecovery()
    {
        var original = CreatePlan(
            RotationPhase.Burst,
            10,
            20,
            30,
            40,
            50);
        var revised = CreatePlan(
            RotationPhase.Burst,
            99,
            98,
            30,
            40,
            50);

        var recovery = new RecoveryPlanEngine().Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = original,
                RevisedPlan = revised,
                UsedActionId = 99,
                ExpectedActionId = 10,
                MinimumConvergenceSteps = 3,
                MaximumRecoverySteps = 8
            });

        Require(
            recovery.Disposition == RecoveryPlanDisposition.GuidedRecovery &&
            recovery.RecoverySteps.Select(step => step.GcdActionId)
                .SequenceEqual(new uint[] { 99, 98 }) &&
            recovery.OriginalRejoinOffset == 2 &&
            recovery.RevisedRejoinOffset == 2,
            "The engine did not choose the shortest stable recovery prefix.");
    }

    private static void RejectsFalseConvergenceAcrossPhases()
    {
        var original = CreatePlan(
            RotationPhase.Filler,
            10,
            20,
            30);
        var revised = CreatePlan(
            RotationPhase.Burst,
            10,
            20,
            30);

        var recovery = new RecoveryPlanEngine().Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = original,
                RevisedPlan = revised,
                MinimumConvergenceSteps = 3
            });

        Require(
            recovery.Disposition ==
                RecoveryPlanDisposition.ReplaceRemainingPlan,
            "Matching buttons in a different phase must not count as convergence.");
    }

    private static void ReplacesPlanWhenNoStableRejoinExists()
    {
        var original = CreatePlan(
            RotationPhase.Filler,
            10,
            20,
            30,
            40);
        var revised = CreatePlan(
            RotationPhase.Recovery,
            90,
            91,
            92,
            93);

        var recovery = new RecoveryPlanEngine().Build(
            new RecoveryPlanRequest
            {
                OriginalPlan = original,
                RevisedPlan = revised,
                MinimumConvergenceSteps = 3,
                MaximumRecoverySteps = 2
            });

        Require(
            recovery.Disposition ==
                RecoveryPlanDisposition.ReplaceRemainingPlan &&
            recovery.RecoverySteps.Select(step => step.GcdActionId)
                .SequenceEqual(new uint[] { 90, 91 }),
            "A divergent route should replace the plan within the stated horizon.");
    }

    private static PracticePlan CreatePlan(
        RotationPhase phase,
        params uint[] actionIds)
    {
        return new PracticePlan
        {
            Job = "TEST",
            HorizonSeconds = actionIds.Length * 2.5d,
            Steps = actionIds
                .Select(
                    (actionId, index) => new TrainingForecastStep
                    {
                        Offset = index,
                        StartsAtSeconds = index * 2.5d,
                        DurationSeconds = 2.5f,
                        Phase = phase,
                        GcdActionId = actionId
                    })
                .ToArray()
        };
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
