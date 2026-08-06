using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class MachinistStartupSmokeTests
{
    private const uint SplitShot = 2866;
    private const uint HeatedSplitShot = 7411;

    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(arguments[^1]);
        var dataDirectory = Directory.GetParent(policyDirectory);

        if (dataDirectory == null)
        {
            return;
        }

        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "MCH.json"), "MCH")
            .Single();
        var catalogue = PveActionCatalogLoader.Load(
            Path.Combine(
                dataDirectory.FullName,
                "Actions",
                "pve-actions.json"));
        PveActionCatalogLoader.Apply(definition, catalogue);

        ValidateUnknownPreLiveAdjustment(definition);
        ValidateContradictoryLiveAdjustment(definition);

        Console.WriteLine(
            "MCH startup smoke test passed: unknown pre-live adjustments do not blank the overlay, " +
            "the real Heated Split mapping validates, and contradictory live mappings still fail closed.");
    }

    private static void ValidateUnknownPreLiveAdjustment(
        RulePolicyDefinition definition)
    {
        var session = new TrainingSession();
        session.Start(new RuleSetTrainingPolicy(definition), 100);

        RequireHealthyForecast(
            session,
            "before its first live-state refresh");

        session.RefreshState(state =>
        {
            state.SetLevel(100);
            state.SetGauge("heat", 0);
            state.SetGauge("battery", 0);
            state.SetStateValue("overheated", 0d);
            state.SetStateValue("overheat_ms", 0d);
            state.SetStateValue("robot_active", 0d);
            state.SetStateValue("summon_ms", 0d);
            state.SetAdjustedAction(SplitShot, HeatedSplitShot);
        });

        RequireHealthyForecast(
            session,
            "after receiving the live Heated Split mapping");
    }

    private static void ValidateContradictoryLiveAdjustment(
        RulePolicyDefinition definition)
    {
        var policy = new RuleSetTrainingPolicy(definition);
        var unknownState = new TrainingState();
        unknownState.Begin("MCH", 100);
        unknownState.SetGauge("heat", 0);
        unknownState.SetGauge("battery", 0);
        var plannedBeforeLiveState = policy.BuildPracticePlan(unknownState);

        if (plannedBeforeLiveState.Steps.FirstOrDefault()?.GcdActionId !=
            HeatedSplitShot)
        {
            throw new InvalidDataException(
                "The MCH startup fixture did not produce the expected Heated Split head.");
        }

        var contradictoryLiveState = unknownState.Clone();
        contradictoryLiveState.SetAdjustedAction(SplitShot, SplitShot);

        var validation = new PracticePlanValidator().Validate(
            new PlanValidationRequest
            {
                Plan = plannedBeforeLiveState,
                State = contradictoryLiveState
            },
            policy);

        if (validation.IsValid ||
            !validation.Issues.Any(issue =>
                issue.Code == PlanValidationCode.AdjustedActionMismatch))
        {
            throw new InvalidDataException(
                "A contradictory live MCH adjustment map did not fail closed.");
        }
    }

    private static void RequireHealthyForecast(
        TrainingSession session,
        string stage)
    {
        if (session.IsFaulted)
        {
            throw new InvalidDataException(
                $"MCH priority practice faulted {stage}: " +
                session.LastRejectedPlanValidation.Summary);
        }

        if (session.CurrentForecast.Count == 0)
        {
            throw new InvalidDataException(
                $"MCH priority practice had no forecast {stage}.");
        }
    }
}
