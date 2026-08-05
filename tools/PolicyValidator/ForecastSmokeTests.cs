using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class ForecastSmokeTests
{
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;
    private const uint Delirium = 7390;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;

    private const uint SplitShot = 2866;
    private const uint SlugShot = 2868;
    private const uint CleanShot = 2873;
    private const uint HotShot = 2872;
    private const uint HeatBlast = 7410;
    private const uint HeatedSplitShot = 7411;
    private const uint HeatedSlugShot = 7412;
    private const uint HeatedCleanShot = 7413;
    private const uint BlazingShot = 36978;
    private const uint AirAnchor = 16500;

    [ModuleInitializer]
    internal static void ValidateForecasts()
    {
        var commandLine = Environment.GetCommandLineArgs();

        if (commandLine.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(commandLine[^1]);

        if (!Directory.Exists(policyDirectory))
        {
            return;
        }

        ValidateDarkKnightForecast(policyDirectory);
        ValidateMachinistForecast(policyDirectory);

        Console.WriteLine(
            "Forecast smoke tests passed for DRK combo progression and MCH Overheat repetition.");
    }

    private static void ValidateDarkKnightForecast(string policyDirectory)
    {
        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "DRK.json"), "DRK")
            .Single();
        var policy = new RuleSetTrainingPolicy(definition);
        var state = new TrainingState();

        state.Begin("DRK", 100);
        state.SetGauge("blood", 0);
        state.SetGauge("mp", 6000);
        state.SetGauge("darkside_ms", 30000);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);
        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetCooldown(
            Delirium,
            new CooldownSnapshot
            {
                RemainingSeconds = 30f,
                Charges = 0,
                MaximumCharges = 1
            });

        var forecast = policy.Forecast(state, 3);
        var expected = new[]
        {
            HardSlash,
            SyphonStrike,
            Souleater
        };

        AssertForecast("DRK", forecast, expected);
    }

    private static void ValidateMachinistForecast(string policyDirectory)
    {
        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "MCH.json"), "MCH")
            .Single();
        var policy = new RuleSetTrainingPolicy(definition);
        var state = new TrainingState();

        state.Begin("MCH", 100);
        state.SetGauge("heat", 0);
        state.SetGauge("battery", 0);
        state.SetStateValue("overheated", 1d);
        state.SetStateValue("overheat_ms", 10000d);
        state.SetStateValue("robot_active", 0d);
        state.SetStateValue("summon_ms", 0d);
        state.SetAdjustedAction(SplitShot, HeatedSplitShot);
        state.SetAdjustedAction(SlugShot, HeatedSlugShot);
        state.SetAdjustedAction(CleanShot, HeatedCleanShot);
        state.SetAdjustedAction(HotShot, AirAnchor);
        state.SetAdjustedAction(HeatBlast, BlazingShot);

        var forecast = policy.Forecast(state, 3);
        var expected = new[]
        {
            BlazingShot,
            BlazingShot,
            BlazingShot
        };

        AssertForecast("MCH", forecast, expected);
    }

    private static void AssertForecast(
        string job,
        IReadOnlyList<TrainingForecastStep> forecast,
        IReadOnlyList<uint> expected)
    {
        if (forecast.Count < expected.Count)
        {
            throw new InvalidDataException(
                $"{job} forecast produced {forecast.Count} step(s); expected {expected.Count}.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (forecast[index].GcdActionId == expected[index])
            {
                continue;
            }

            throw new InvalidDataException(
                $"{job} forecast step {index + 1} produced " +
                $"{forecast[index].GcdActionId}; expected {expected[index]}.");
        }
    }
}
