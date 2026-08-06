using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class BurstReserveProfileSmokeTests
{
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint LivingShadow = 16472;
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
    private const uint AutomatonQueen = 16501;
    private const uint Excavator = 36981;
    private const uint Hypercharge = 17209;

    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(arguments[^1]);

        if (!Directory.Exists(policyDirectory))
        {
            return;
        }

        var catalogue = LoadCatalogue(policyDirectory);
        ValidateDarkKnight(policyDirectory, catalogue);
        ValidateMachinist(policyDirectory, catalogue);

        Console.WriteLine(
            "Burst reserve profile smoke test passed: DRK banks MP and Blood " +
            "for the two-minute window while preserving emergency Darkside, and MCH " +
            "banks paid Hypercharge Heat plus an Air-Anchor-funded 100-Battery Queen.");
    }

    private static void ValidateDarkKnight(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var policy = LoadPolicy(policyDirectory, catalogue, "DRK");
        var definition = policy.Definition;

        AssertReserve(definition, "mp", 9000);
        AssertReserve(definition, "blood", 50);

        if (definition.Profile.MinorBurstCycleSeconds != 120 ||
            definition.Profile.BurstWindowSeconds != 20 ||
            definition.Profile.PoolingWindowSeconds != 15)
        {
            throw new InvalidDataException(
                "DRK did not receive the sourced two-minute burst timing profile.");
        }

        var poolingState = CreateDarkKnightState(
            mp: 9000,
            blood: 50,
            combatTimeSeconds: 110,
            darksideMs: 30000,
            lastActionId: HardSlash);
        var poolingDecision = policy.Evaluate(poolingState);

        if (policy.BuildPracticePlan(poolingState).CurrentPhase !=
                RotationPhase.Pooling ||
            poolingDecision.SuggestedActionIds.Contains(EdgeOfShadow) ||
            poolingDecision.PreferredActionId == Bloodspiller)
        {
            throw new InvalidDataException(
                "DRK pooling did not protect 9,000 MP and 50 Blood before raid buffs.");
        }

        var cappedPoolingState = CreateDarkKnightState(
            mp: 10000,
            blood: 50,
            combatTimeSeconds: 110,
            darksideMs: 30000,
            lastActionId: HardSlash);

        if (policy.Evaluate(cappedPoolingState)
            .SuggestedActionIds.Contains(EdgeOfShadow))
        {
            throw new InvalidDataException(
                "DRK spent capped MP during the final pooling window instead of banking it.");
        }

        var fillerState = CreateDarkKnightState(
            mp: 10000,
            blood: 50,
            combatTimeSeconds: 80,
            darksideMs: 30000,
            lastActionId: HardSlash);

        if (!policy.Evaluate(fillerState)
            .SuggestedActionIds.Contains(EdgeOfShadow))
        {
            throw new InvalidDataException(
                "DRK failed to spend capped MP outside the pooling window.");
        }

        var emergencyState = CreateDarkKnightState(
            mp: 9000,
            blood: 50,
            combatTimeSeconds: 110,
            darksideMs: 0,
            lastActionId: HardSlash);

        if (!policy.Evaluate(emergencyState)
            .SuggestedActionIds.Contains(EdgeOfShadow))
        {
            throw new InvalidDataException(
                "Urgent Darkside maintenance was incorrectly blocked by the MP reserve.");
        }

        var overcapState = CreateDarkKnightState(
            mp: 9000,
            blood: 80,
            combatTimeSeconds: 110,
            darksideMs: 30000,
            lastActionId: SyphonStrike);
        overcapState.SetCombo(SyphonStrike, 30f);

        if (policy.Evaluate(overcapState).PreferredActionId != Bloodspiller)
        {
            throw new InvalidDataException(
                "Blood overcap prevention did not bypass the 50-Blood reserve before Souleater.");
        }
    }

    private static void ValidateMachinist(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var policy = LoadPolicy(policyDirectory, catalogue, "MCH");
        var definition = policy.Definition;

        AssertReserve(definition, "heat", 50);
        AssertReserve(definition, "battery", 80);

        if (definition.Profile.MinorBurstCycleSeconds != 120 ||
            definition.Profile.BurstWindowSeconds != 20 ||
            definition.Profile.PoolingWindowSeconds != 15)
        {
            throw new InvalidDataException(
                "MCH did not receive the sourced two-minute burst timing profile.");
        }

        var preAirAnchorState = CreateMachinistState(
            heat: 90,
            battery: 80,
            combatTimeSeconds: 110,
            lastActionId: HeatedSplitShot);
        preAirAnchorState.SetCooldown(
            AirAnchor,
            ReadyCooldown(40f));

        var preAirAnchorDecision = policy.Evaluate(preAirAnchorState);

        if (policy.BuildPracticePlan(preAirAnchorState).CurrentPhase !=
                RotationPhase.Pooling ||
            preAirAnchorDecision.PreferredActionId != AirAnchor ||
            preAirAnchorDecision.SuggestedActionIds.Contains(Hypercharge) ||
            preAirAnchorDecision.SuggestedActionIds.Contains(AutomatonQueen))
        {
            throw new InvalidDataException(
                "MCH did not hold 50 Heat and 80 Battery for the two-minute burst setup.");
        }

        var cappedHeatState = CreateMachinistState(
            heat: 100,
            battery: 80,
            combatTimeSeconds: 110,
            lastActionId: HeatedSplitShot);

        if (!policy.Evaluate(cappedHeatState)
            .SuggestedActionIds.Contains(Hypercharge))
        {
            throw new InvalidDataException(
                "MCH failed to spend capped Heat while preserving the 50-Heat burst reserve.");
        }

        var cappedBatteryState = CreateMachinistState(
            heat: 50,
            battery: 100,
            combatTimeSeconds: 110,
            lastActionId: AirAnchor);

        if (!policy.Evaluate(cappedBatteryState)
            .SuggestedActionIds.Contains(AutomatonQueen))
        {
            throw new InvalidDataException(
                "MCH failed to deploy the 100-Battery Queen after the burst Air Anchor.");
        }

        var excavatorState = CreateMachinistState(
            heat: 0,
            battery: 60,
            combatTimeSeconds: 10,
            lastActionId: Excavator);

        if (!policy.Evaluate(excavatorState)
            .SuggestedActionIds.Contains(AutomatonQueen))
        {
            throw new InvalidDataException(
                "MCH did not deploy the 60-Battery opener Queen after Excavator.");
        }
    }

    private static RuleSetTrainingPolicy LoadPolicy(
        string policyDirectory,
        PveActionCatalogFile catalogue,
        string job)
    {
        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, $"{job}.json"), job)
            .Single();
        PveActionCatalogLoader.Apply(definition, catalogue);
        return new RuleSetTrainingPolicy(definition);
    }

    private static TrainingState CreateDarkKnightState(
        int mp,
        int blood,
        double combatTimeSeconds,
        int darksideMs,
        uint lastActionId)
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        state.SetGauge("mp", mp);
        state.SetGauge("blood", blood);
        state.SetGauge("darkside_ms", darksideMs);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);
        state.SetStateValue("blood_weapon_stacks", 0);
        state.SetCombatTimeSeconds(combatTimeSeconds);
        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);
        state.RecordAcceptedAction(lastActionId);
        return state;
    }

    private static TrainingState CreateMachinistState(
        int heat,
        int battery,
        double combatTimeSeconds,
        uint lastActionId)
    {
        var state = new TrainingState();
        state.Begin("MCH", 100);
        state.SetGauge("heat", heat);
        state.SetGauge("battery", battery);
        state.SetStateValue("overheated", 0);
        state.SetStateValue("overheatMs", 0);
        state.SetStateValue("robotActive", 0);
        state.SetStateValue("summonMs", 0);
        state.SetCombatTimeSeconds(combatTimeSeconds);
        state.SetAdjustedAction(SplitShot, HeatedSplitShot);
        state.SetAdjustedAction(SlugShot, HeatedSlugShot);
        state.SetAdjustedAction(CleanShot, HeatedCleanShot);
        state.SetAdjustedAction(HotShot, AirAnchor);
        state.SetAdjustedAction(HeatBlast, BlazingShot);
        state.RecordAcceptedAction(lastActionId);
        return state;
    }

    private static PveActionCatalogFile LoadCatalogue(string policyDirectory)
    {
        var dataDirectory = Directory.GetParent(policyDirectory)
            ?? throw new InvalidDataException(
                "Could not resolve the Data directory for burst-reserve validation.");

        return PveActionCatalogLoader.Load(
            Path.Combine(
                dataDirectory.FullName,
                "Actions",
                "pve-actions.json"));
    }

    private static void AssertReserve(
        RulePolicyDefinition definition,
        string resource,
        double expected)
    {
        if (!definition.StateInputs.TryGetValue(resource, out var input) ||
            input.PoolingReserve != expected)
        {
            throw new InvalidDataException(
                $"Policy '{definition.Id}' did not apply the expected " +
                $"{resource} reserve of {expected}.");
        }
    }

    private static CooldownSnapshot ReadyCooldown(float rechargeSeconds)
    {
        return new CooldownSnapshot
        {
            Charges = 1,
            MaximumCharges = 1,
            RechargeSeconds = rechargeSeconds
        };
    }
}
