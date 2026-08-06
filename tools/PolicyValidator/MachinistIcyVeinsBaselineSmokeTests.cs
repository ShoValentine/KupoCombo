using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class MachinistIcyVeinsBaselineSmokeTests
{
    private const uint SplitShot = 2866;
    private const uint SlugShot = 2868;
    private const uint CleanShot = 2873;
    private const uint HotShot = 2872;
    private const uint Reassemble = 2876;
    private const uint GaussRound = 2874;
    private const uint Ricochet = 2890;
    private const uint RookAutoturret = 2864;
    private const uint HeatedSplitShot = 7411;
    private const uint HeatedSlugShot = 7412;
    private const uint HeatedCleanShot = 7413;
    private const uint HeatBlast = 7410;
    private const uint BarrelStabilizer = 7414;
    private const uint Wildfire = 2878;
    private const uint Hypercharge = 17209;
    private const uint Drill = 16498;
    private const uint AirAnchor = 16500;
    private const uint AutomatonQueen = 16501;
    private const uint ChainSaw = 25788;
    private const uint BlazingShot = 36978;
    private const uint DoubleCheck = 36979;
    private const uint Checkmate = 36980;
    private const uint Excavator = 36981;
    private const uint FullMetalField = 36982;

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

        var policy = new RuleSetTrainingPolicy(definition);
        ValidateOpeningRibbon(policy);
        ValidateEightSecondHyperchargeGate(policy);

        Console.WriteLine(
            "MCH Icy Veins baseline smoke test passed: the fixed tool spine, " +
            "opening weave placement, exact eight-second Hypercharge gate, " +
            "delayed burst, and short-GCD weave cap held.");
    }

    private static void ValidateOpeningRibbon(
        RuleSetTrainingPolicy policy)
    {
        var forecast = policy.Forecast(CreateFreshOpenerState(), 8);
        var expectedGcdPrefix = new uint[]
        {
            AirAnchor,
            Drill,
            ChainSaw,
            Excavator,
            FullMetalField,
            BlazingShot
        };

        Require(
            forecast.Count >= expectedGcdPrefix.Length,
            "The MCH baseline forecast ended before the opening burst began.");
        Require(
            forecast.Take(expectedGcdPrefix.Length)
                .Select(step => step.GcdActionId)
                .SequenceEqual(expectedGcdPrefix),
            "The MCH baseline GCD spine diverged. Expected " +
            $"[{Join(expectedGcdPrefix)}], got " +
            $"[{Join(forecast.Select(step => step.GcdActionId))}].");

        RequireSuggestions(forecast[0], Reassemble);
        RequireSuggestions(forecast[1], Reassemble, DoubleCheck);
        RequireSuggestions(forecast[2], BarrelStabilizer, Checkmate);
        RequireSuggestions(forecast[3], DoubleCheck);
        RequireSuggestions(forecast[4], AutomatonQueen, Checkmate);
        RequireSuggestions(forecast[5], Wildfire, Hypercharge);

        var flattenedPrefix = forecast
            .Take(6)
            .SelectMany(step =>
                step.SuggestedActionIds.Concat(new[] { step.GcdActionId }))
            .ToArray();
        var fullMetalIndex = Array.IndexOf(flattenedPrefix, FullMetalField);
        var hyperchargeIndex = Array.IndexOf(flattenedPrefix, Hypercharge);

        Require(
            fullMetalIndex >= 0 &&
            hyperchargeIndex > fullMetalIndex,
            "Hypercharge entered before the core tool block completed.");

        foreach (var step in forecast.Where(step =>
                     step.GcdActionId == BlazingShot))
        {
            Require(
                step.SuggestedActionIds.Count <= 1,
                "A Blazing Shot window received more than one weave.");
        }
    }

    private static void ValidateEightSecondHyperchargeGate(
        RuleSetTrainingPolicy policy)
    {
        var state = CreateFreshOpenerState();

        for (var index = 0; index < 5; index++)
        {
            state.RecordAcceptedAction(HeatedSplitShot);
        }

        state.SetGauge("heat", 50);
        state.SetCooldown(Wildfire, UnreadyCooldown(60f, 120f));
        state.SetCooldown(DoubleCheck, UnreadyCooldown(20f, 30f, 3, 1));
        state.SetCooldown(Checkmate, UnreadyCooldown(20f, 30f, 3, 1));
        SetToolCooldowns(state, 8f);

        Require(
            !policy.Evaluate(state).SuggestedActionIds.Contains(Hypercharge),
            "Hypercharge was allowed with a tool exactly eight seconds away.");

        SetToolCooldowns(state, 8.1f);

        Require(
            policy.Evaluate(state).SuggestedActionIds.Contains(Hypercharge),
            "Hypercharge remained blocked with every tool more than eight seconds away.");
    }

    private static void SetToolCooldowns(
        TrainingState state,
        float remainingSeconds)
    {
        state.SetCooldown(
            AirAnchor,
            UnreadyCooldown(remainingSeconds, 40f));
        state.SetCooldown(
            Drill,
            UnreadyCooldown(remainingSeconds, 20f, 2, 1));
        state.SetCooldown(
            ChainSaw,
            UnreadyCooldown(remainingSeconds, 60f));
    }

    private static TrainingState CreateFreshOpenerState()
    {
        var state = new TrainingState();
        state.Begin("MCH", 100);
        state.SetGauge("heat", 0);
        state.SetGauge("battery", 0);
        state.SetStateValue("overheated", 0d);
        state.SetStateValue("overheat_ms", 0d);
        state.SetStateValue("overheatShots", 0d);
        state.SetStateValue("robot_active", 0d);
        state.SetStateValue("summon_ms", 0d);

        state.SetAdjustedAction(SplitShot, HeatedSplitShot);
        state.SetAdjustedAction(SlugShot, HeatedSlugShot);
        state.SetAdjustedAction(CleanShot, HeatedCleanShot);
        state.SetAdjustedAction(HotShot, AirAnchor);
        state.SetAdjustedAction(HeatBlast, BlazingShot);
        state.SetAdjustedAction(RookAutoturret, AutomatonQueen);
        state.SetAdjustedAction(GaussRound, DoubleCheck);
        state.SetAdjustedAction(Ricochet, Checkmate);
        state.SetAdjustedAction(ChainSaw, ChainSaw);

        state.SetCooldown(AirAnchor, ReadyCooldown(40f));
        state.SetCooldown(ChainSaw, ReadyCooldown(60f));
        state.SetCooldown(Drill, ReadyCooldown(20f, 2));
        state.SetCooldown(BarrelStabilizer, ReadyCooldown(120f));
        state.SetCooldown(Wildfire, ReadyCooldown(120f));
        state.SetCooldown(DoubleCheck, ReadyCooldown(30f, 3));
        state.SetCooldown(Checkmate, ReadyCooldown(30f, 3));
        state.SetCooldown(Reassemble, ReadyCooldown(55f, 2));
        return state;
    }

    private static CooldownSnapshot ReadyCooldown(
        float rechargeSeconds,
        int maximumCharges = 1)
    {
        return new CooldownSnapshot
        {
            Charges = maximumCharges,
            MaximumCharges = maximumCharges,
            RechargeSeconds = rechargeSeconds
        };
    }

    private static CooldownSnapshot UnreadyCooldown(
        float remainingSeconds,
        float rechargeSeconds,
        int maximumCharges = 1,
        int charges = 0)
    {
        return new CooldownSnapshot
        {
            RemainingSeconds = remainingSeconds,
            RechargeSeconds = rechargeSeconds,
            Charges = charges,
            MaximumCharges = maximumCharges
        };
    }

    private static void RequireSuggestions(
        TrainingForecastStep step,
        params uint[] expected)
    {
        Require(
            step.SuggestedActionIds.SequenceEqual(expected),
            $"GCD {step.GcdActionId} expected weaves [{Join(expected)}], " +
            $"got [{Join(step.SuggestedActionIds)}].");
    }

    private static string Join(IEnumerable<uint> values)
    {
        var array = values.ToArray();
        return array.Length == 0
            ? "none"
            : string.Join(", ", array);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
