using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class MachinistWeaveSchedulingSmokeTests
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

    private const uint FullMetalMachinistStatus = 3866;

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

        ValidateFiniteOverheatWindow(policy);
        ValidateSingleWeaveShortGcd(policy);
        ValidateReassembleTargets(policy);

        Console.WriteLine(
            "MCH weave scheduling smoke test passed: Hypercharge produces five Blazing Shots, " +
            "short GCDs carry one weave, and Reassemble is reserved for eligible 660-potency tools.");
    }

    private static void ValidateFiniteOverheatWindow(
        RuleSetTrainingPolicy policy)
    {
        var state = CreateState();
        state.SetGauge("heat", 90);

        var forecast = policy.Forecast(state, 8);
        var blazingPrefix = forecast
            .TakeWhile(step => step.GcdActionId == BlazingShot)
            .Count();

        Require(
            forecast.Count >= 6,
            "The MCH forecast ended before it could leave Overheat.");
        Require(
            forecast[0].SuggestedActionIds.Contains(Hypercharge),
            "The high-Heat forecast did not enter Hypercharge.");
        Require(
            blazingPrefix == 5,
            $"Hypercharge forecast {blazingPrefix} Blazing Shots instead of five.");
        Require(
            forecast[5].GcdActionId != BlazingShot,
            "The forecast remained trapped in Overheat after five shots.");
    }

    private static void ValidateSingleWeaveShortGcd(
        RuleSetTrainingPolicy policy)
    {
        var state = CreateState();
        state.SetStateValue("overheated", 1d);
        state.SetStateValue("overheat_ms", 10000d);
        state.SetStateValue("overheatShots", 5d);
        state.SetCooldown(DoubleCheck, ReadyCooldown(30f, 3));
        state.SetCooldown(Checkmate, ReadyCooldown(30f, 3));
        state.SetCooldown(Reassemble, ReadyCooldown(55f, 2));

        var step = policy.Forecast(state, 1).Single();

        Require(
            step.GcdActionId == BlazingShot,
            "The short-GCD fixture did not forecast Blazing Shot.");
        Require(
            step.SuggestedActionIds.Count == 1,
            $"Blazing Shot received {step.SuggestedActionIds.Count} weave suggestions instead of one.");
        Require(
            step.SuggestedActionIds[0] == DoubleCheck,
            "The short-GCD window did not prioritise the capped Double Check charge.");
    }

    private static void ValidateReassembleTargets(
        RuleSetTrainingPolicy policy)
    {
        var toolState = CreateState();
        toolState.SetCooldown(Drill, ReadyCooldown(20f, 2));
        toolState.SetCooldown(Reassemble, ReadyCooldown(55f, 2));

        var toolStep = policy.Forecast(toolState, 1).Single();

        Require(
            toolStep.GcdActionId == Drill,
            "The Reassemble tool fixture did not select Drill.");
        Require(
            toolStep.SuggestedActionIds.Contains(Reassemble),
            "Reassemble was not paired with the 660-potency Drill.");

        var comboState = CreateState();
        comboState.SetCooldown(Reassemble, ReadyCooldown(55f, 2));

        var comboStep = policy.Forecast(comboState, 1).Single();

        Require(
            comboStep.GcdActionId == HeatedSplitShot,
            "The low-potency fixture did not select Heated Split Shot.");
        Require(
            !comboStep.SuggestedActionIds.Contains(Reassemble),
            "Reassemble was spent on a low-potency combo shot.");

        var fullMetalState = CreateState();
        fullMetalState.SetCooldown(Reassemble, ReadyCooldown(55f, 2));
        fullMetalState.ReplaceStatuses(new[]
        {
            new StatusSnapshot
            {
                StatusId = FullMetalMachinistStatus,
                RemainingSeconds = 20f
            }
        });

        var fullMetalStep = policy.Forecast(fullMetalState, 1).Single();

        Require(
            fullMetalStep.GcdActionId == FullMetalField,
            "The Full Metal fixture did not select Full Metal Field.");
        Require(
            !fullMetalStep.SuggestedActionIds.Contains(Reassemble),
            "Reassemble was incorrectly paired with Full Metal Field.");
    }

    private static TrainingState CreateState()
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

        state.SetCooldown(AirAnchor, UnreadyCooldown(20f, 40f));
        state.SetCooldown(ChainSaw, UnreadyCooldown(30f, 60f));
        state.SetCooldown(Drill, UnreadyCooldown(10f, 20f, 2));
        state.SetCooldown(DoubleCheck, UnreadyCooldown(10f, 30f, 3, 1));
        state.SetCooldown(Checkmate, UnreadyCooldown(10f, 30f, 3, 1));
        state.SetCooldown(Reassemble, UnreadyCooldown(10f, 55f, 2, 1));
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
