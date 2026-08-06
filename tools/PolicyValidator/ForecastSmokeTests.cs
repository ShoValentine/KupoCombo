using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class ForecastSmokeTests
{
    private const uint Unmend = 3624;
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Delirium = 7390;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint LivingShadow = 16472;
    private const uint CarveAndSpit = 3639;
    private const uint Shadowbringer = 25757;
    private const uint SaltedEarth = 3643;

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

        var catalogue = LoadCatalogue(policyDirectory);
        ValidateDarkKnightForecast(policyDirectory, catalogue);
        ValidateMachinistForecast(policyDirectory, catalogue);

        Console.WriteLine(
            "Forecast smoke tests passed for the catalogue-backed DRK opener ribbon and MCH Overheat repetition.");
    }

    private static void ValidateDarkKnightForecast(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "DRK.json"), "DRK")
            .Single();
        PveActionCatalogLoader.Apply(definition, catalogue);

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
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);
        state.SetCooldown(Delirium, ReadyCooldown(60f));
        state.SetCooldown(LivingShadow, ReadyCooldown(120f));
        state.SetCooldown(CarveAndSpit, ReadyCooldown(60f));
        state.SetCooldown(Shadowbringer, ReadyCooldown(60f, 2));
        state.SetCooldown(SaltedEarth, ReadyCooldown(90f));

        var forecast = policy.Forecast(state, 3);
        var ribbon = forecast
            .SelectMany(step =>
                step.SuggestedActionIds.Concat(new[] { step.GcdActionId }))
            .ToArray();
        var expected = new[]
        {
            Unmend,
            EdgeOfShadow,
            HardSlash,
            LivingShadow,
            SyphonStrike
        };

        AssertRibbon("DRK", ribbon, expected);
    }

    private static void ValidateMachinistForecast(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "MCH.json"), "MCH")
            .Single();
        PveActionCatalogLoader.Apply(definition, catalogue);

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

        AssertGcdForecast("MCH", forecast, expected);
    }

    private static PveActionCatalogFile LoadCatalogue(string policyDirectory)
    {
        var dataDirectory = Directory.GetParent(policyDirectory)
            ?? throw new InvalidDataException(
                "Could not resolve the Data directory for action-catalogue validation.");

        return PveActionCatalogLoader.Load(
            Path.Combine(
                dataDirectory.FullName,
                "Actions",
                "pve-actions.json"));
    }

    private static void AssertRibbon(
        string job,
        IReadOnlyList<uint> ribbon,
        IReadOnlyList<uint> expected)
    {
        if (ribbon.Count < expected.Count ||
            !ribbon.Take(expected.Count).SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"{job} ribbon produced [{string.Join(", ", ribbon)}]; " +
                $"expected prefix [{string.Join(", ", expected)}].");
        }
    }

    private static void AssertGcdForecast(
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

    private static CooldownSnapshot ReadyCooldown(
        float rechargeSeconds,
        int maximumCharges = 1)
    {
        return new CooldownSnapshot
        {
            RechargeSeconds = rechargeSeconds,
            Charges = maximumCharges,
            MaximumCharges = maximumCharges
        };
    }
}
