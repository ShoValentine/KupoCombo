using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class DynamicBurstResynchronizationSmokeTests
{
    private const uint HardSlash = 3617;
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
    private const uint Wildfire = 2878;
    private const uint Hypercharge = 17209;
    private const uint AutomatonQueen = 16501;

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
            "Dynamic burst resynchronisation smoke test passed: live anchor " +
            "cooldowns shift filler, pooling, and burst phases while the raw " +
            "practice clock continues advancing.");
    }

    private static void ValidateDarkKnight(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var policy = LoadPolicy(policyDirectory, catalogue, "DRK");

        var midCycle = CreateDarkKnightState(
            timelineSeconds: 0,
            livingShadowRemainingSeconds: 40,
            livingShadowReady: false);
        midCycle.ResetProgress();

        AssertAlignment(
            midCycle,
            expectedTimeline: 0,
            expectedEffective: 80,
            expectedUntilBurst: 40,
            expectedReady: false);

        var midCyclePlan = policy.BuildPracticePlan(midCycle);

        if (midCyclePlan.CurrentPhase != RotationPhase.Filler ||
            midCyclePlan.Steps.FirstOrDefault()?.GcdActionId != HardSlash)
        {
            throw new InvalidDataException(
                "A fresh DRK Practice Mode session 40 seconds before Living " +
                "Shadow did not skip the Unmend opener and enter filler.");
        }

        var pooling = CreateDarkKnightState(
            timelineSeconds: 95,
            livingShadowRemainingSeconds: 10,
            livingShadowReady: false);
        var poolingPlan = policy.BuildPracticePlan(pooling);
        var poolingDecision = policy.Evaluate(pooling);

        AssertAlignment(
            pooling,
            expectedTimeline: 95,
            expectedEffective: 110,
            expectedUntilBurst: 10,
            expectedReady: false);

        if (poolingPlan.CurrentPhase != RotationPhase.Pooling ||
            poolingDecision.SuggestedActionIds.Contains(EdgeOfShadow))
        {
            throw new InvalidDataException(
                "DRK did not shift into pooling when Living Shadow drifted " +
                "to ten seconds from readiness.");
        }

        var delayedReady = CreateDarkKnightState(
            timelineSeconds: 130,
            livingShadowRemainingSeconds: 0,
            livingShadowReady: true);

        AssertAlignment(
            delayedReady,
            expectedTimeline: 130,
            expectedEffective: 120,
            expectedUntilBurst: 0,
            expectedReady: true);

        if (policy.BuildPracticePlan(delayedReady).CurrentPhase !=
            RotationPhase.Burst)
        {
            throw new InvalidDataException(
                "A ready-but-delayed Living Shadow did not reopen the DRK " +
                "burst phase.");
        }

        delayedReady.AdvanceForecastTime(5f);

        AssertAlignment(
            delayedReady,
            expectedTimeline: 135,
            expectedEffective: 125,
            expectedUntilBurst: 0,
            expectedReady: true);

        delayedReady.ConsumeCooldown(LivingShadow);

        AssertAlignment(
            delayedReady,
            expectedTimeline: 135,
            expectedEffective: 120,
            expectedUntilBurst: 120,
            expectedReady: false);

        if (policy.BuildPracticePlan(delayedReady).CurrentPhase !=
            RotationPhase.Burst)
        {
            throw new InvalidDataException(
                "Using a delayed Living Shadow did not establish the new " +
                "DRK burst origin.");
        }

        var timelineFallback = CreateDarkKnightState(
            timelineSeconds: 110,
            livingShadowRemainingSeconds: null,
            livingShadowReady: false);

        if (timelineFallback.TryGetBurstTimelineAlignment(out _) ||
            policy.BuildPracticePlan(timelineFallback).CurrentPhase !=
                RotationPhase.Pooling)
        {
            throw new InvalidDataException(
                "DRK did not fall back to its ordinary timeline when the " +
                "burst-anchor cooldown was unavailable.");
        }
    }

    private static void ValidateMachinist(
        string policyDirectory,
        PveActionCatalogFile catalogue)
    {
        var policy = LoadPolicy(policyDirectory, catalogue, "MCH");
        var pooling = CreateMachinistState(
            timelineSeconds: 95,
            wildfireRemainingSeconds: 10,
            wildfireReady: false);
        var decision = policy.Evaluate(pooling);

        AssertAlignment(
            pooling,
            expectedTimeline: 95,
            expectedEffective: 110,
            expectedUntilBurst: 10,
            expectedReady: false);

        if (policy.BuildPracticePlan(pooling).CurrentPhase !=
                RotationPhase.Pooling ||
            decision.SuggestedActionIds.Contains(Hypercharge) ||
            decision.SuggestedActionIds.Contains(AutomatonQueen))
        {
            throw new InvalidDataException(
                "MCH did not move its Heat and Battery pooling window with " +
                "the live Wildfire cooldown.");
        }

        var delayedReady = CreateMachinistState(
            timelineSeconds: 130,
            wildfireRemainingSeconds: 0,
            wildfireReady: true);

        AssertAlignment(
            delayedReady,
            expectedTimeline: 130,
            expectedEffective: 120,
            expectedUntilBurst: 0,
            expectedReady: true);

        if (policy.BuildPracticePlan(delayedReady).CurrentPhase !=
            RotationPhase.Burst)
        {
            throw new InvalidDataException(
                "A ready-but-delayed Wildfire did not resynchronise MCH into " +
                "the burst phase.");
        }
    }

    private static void AssertAlignment(
        TrainingState state,
        double expectedTimeline,
        double expectedEffective,
        double expectedUntilBurst,
        bool expectedReady)
    {
        if (!state.TryGetBurstTimelineAlignment(out var alignment))
        {
            throw new InvalidDataException(
                $"No burst alignment was available for {state.Job}.");
        }

        if (Math.Abs(
                alignment.TimelineCombatTimeSeconds -
                expectedTimeline) > 0.001d ||
            Math.Abs(
                alignment.EffectiveCombatTimeSeconds -
                expectedEffective) > 0.001d ||
            Math.Abs(
                alignment.SecondsUntilBurst -
                expectedUntilBurst) > 0.001d ||
            alignment.AnchorIsReady != expectedReady ||
            Math.Abs(state.CombatTimeSeconds - expectedEffective) > 0.001d)
        {
            throw new InvalidDataException(
                $"{state.Job} alignment was timeline " +
                $"{alignment.TimelineCombatTimeSeconds:0.###}, effective " +
                $"{alignment.EffectiveCombatTimeSeconds:0.###}, until burst " +
                $"{alignment.SecondsUntilBurst:0.###}, ready " +
                $"{alignment.AnchorIsReady}; expected {expectedTimeline:0.###}, " +
                $"{expectedEffective:0.###}, {expectedUntilBurst:0.###}, " +
                $"{expectedReady}.");
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
        double timelineSeconds,
        float? livingShadowRemainingSeconds,
        bool livingShadowReady)
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        state.SetCombatTimeSeconds(timelineSeconds);
        state.SetGauge("mp", 9000);
        state.SetGauge("blood", 50);
        state.SetGauge("darkside_ms", 30000);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);
        state.SetStateValue("blood_weapon_stacks", 0);
        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);
        state.RecordAcceptedAction(HardSlash);

        if (livingShadowRemainingSeconds.HasValue)
        {
            state.SetCooldown(
                LivingShadow,
                livingShadowReady
                    ? ReadyCooldown(120f)
                    : UnreadyCooldown(
                        livingShadowRemainingSeconds.Value,
                        120f));
        }

        return state;
    }

    private static TrainingState CreateMachinistState(
        double timelineSeconds,
        float wildfireRemainingSeconds,
        bool wildfireReady)
    {
        var state = new TrainingState();
        state.Begin("MCH", 100);
        state.SetCombatTimeSeconds(timelineSeconds);
        state.SetGauge("heat", 90);
        state.SetGauge("battery", 80);
        state.SetStateValue("overheated", 0);
        state.SetStateValue("overheatMs", 0);
        state.SetStateValue("robotActive", 0);
        state.SetStateValue("summonMs", 0);
        state.SetAdjustedAction(SplitShot, HeatedSplitShot);
        state.SetAdjustedAction(SlugShot, HeatedSlugShot);
        state.SetAdjustedAction(CleanShot, HeatedCleanShot);
        state.SetAdjustedAction(HotShot, HotShot);
        state.SetAdjustedAction(HeatBlast, BlazingShot);
        state.RecordAcceptedAction(HeatedSplitShot);
        state.SetCooldown(
            Wildfire,
            wildfireReady
                ? ReadyCooldown(120f)
                : UnreadyCooldown(
                    wildfireRemainingSeconds,
                    120f));
        return state;
    }

    private static PveActionCatalogFile LoadCatalogue(string policyDirectory)
    {
        var dataDirectory = Directory.GetParent(policyDirectory)
            ?? throw new InvalidDataException(
                "Could not resolve the Data directory for dynamic burst " +
                "resynchronisation validation.");

        return PveActionCatalogLoader.Load(
            Path.Combine(
                dataDirectory.FullName,
                "Actions",
                "pve-actions.json"));
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

    private static CooldownSnapshot UnreadyCooldown(
        float remainingSeconds,
        float rechargeSeconds)
    {
        return new CooldownSnapshot
        {
            RemainingSeconds = remainingSeconds,
            RechargeSeconds = rechargeSeconds,
            Charges = 0,
            MaximumCharges = 1
        };
    }
}
