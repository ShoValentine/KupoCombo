using KupoCombo.Models;
using KupoCombo.Services;

const uint HardSlash = 3617;
const uint SyphonStrike = 3623;
const uint Delirium = 7390;
const uint Bloodspiller = 7392;
const uint EdgeOfDarkness = 16467;
const uint EdgeOfShadow = 16470;
const uint ScarletDelirium = 36928;
const uint Comeuppance = 36929;
const uint Torcleaver = 36930;

const uint SplitShot = 2866;
const uint SlugShot = 2868;
const uint CleanShot = 2873;
const uint HotShot = 2872;
const uint Reassemble = 2876;
const uint GaussRound = 2874;
const uint Ricochet = 2890;
const uint HeatedSplitShot = 7411;
const uint HeatedSlugShot = 7412;
const uint HeatedCleanShot = 7413;
const uint BarrelStabilizer = 7414;
const uint HeatBlast = 7410;
const uint Hypercharge = 17209;
const uint Wildfire = 2878;
const uint RookAutoturret = 2864;
const uint Drill = 16498;
const uint AirAnchor = 16500;
const uint AutomatonQueen = 16501;
const uint ChainSaw = 25788;
const uint BlazingShot = 36978;
const uint DoubleCheck = 36979;
const uint Checkmate = 36980;
const uint Excavator = 36981;
const uint FullMetalField = 36982;

const uint WildfirePlayerStatus = 1946;
const uint FullMetalMachinistStatus = 3866;

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "Usage: PolicyValidator <policy-directory>");
    return 2;
}

var policyDirectory = Path.GetFullPath(args[0]);

if (!Directory.Exists(policyDirectory))
{
    Console.Error.WriteLine(
        $"Policy directory not found: {policyDirectory}");
    return 2;
}

var policyFiles = Directory
    .EnumerateFiles(policyDirectory, "*.json", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (policyFiles.Length == 0)
{
    Console.Error.WriteLine(
        $"No policy files found in {policyDirectory}");
    return 2;
}

var failed = false;

foreach (var policyFile in policyFiles)
{
    try
    {
        var expectedJob = Path.GetFileNameWithoutExtension(policyFile);
        var policies = RulePolicyLoader.Load(policyFile, expectedJob);

        Console.WriteLine(
            $"Validated {Path.GetFileName(policyFile)}: " +
            $"{policies.Count} policy profile(s).");

        foreach (var policy in policies)
        {
            if (expectedJob.Equals("DRK", StringComparison.OrdinalIgnoreCase))
            {
                ValidateDarkKnightEvaluator(policy);
            }
            else if (expectedJob.Equals("MCH", StringComparison.OrdinalIgnoreCase))
            {
                ValidateMachinistEvaluator(policy);
            }
        }
    }
    catch (Exception exception)
    {
        failed = true;
        Console.Error.WriteLine(
            $"Policy validation failed for {policyFile}: " +
            exception.Message);
    }
}

return failed ? 1 : 0;

static void ValidateDarkKnightEvaluator(RulePolicyDefinition definition)
{
    var generic = new RuleSetTrainingPolicy(definition);
    var legacy = new LegacyDarkKnightComboPolicy();
    var scenarios = CreateDarkKnightScenarios();

    foreach (var scenario in scenarios)
    {
        var current = generic.Evaluate(scenario.State);
        var previous = legacy.Evaluate(scenario.State);

        var expectedMatches = MatchesExpected(current, scenario);
        var parityMatches =
            current.PreferredActionId == previous.PreferredActionId &&
            SameSet(
                current.AcceptableActionIds,
                previous.AcceptableActionIds) &&
            SameSet(
                current.SuggestedActionIds,
                previous.SuggestedActionIds);

        if (!expectedMatches || !parityMatches)
        {
            throw new InvalidDataException(
                $"DRK evaluator scenario '{scenario.Name}' failed. " +
                $"Generic preferred {current.PreferredActionId}, " +
                $"acceptable [{Join(current.AcceptableActionIds)}], " +
                $"suggestions [{Join(current.SuggestedActionIds)}]. " +
                $"Legacy preferred {previous.PreferredActionId}, " +
                $"acceptable [{Join(previous.AcceptableActionIds)}], " +
                $"suggestions [{Join(previous.SuggestedActionIds)}].");
        }
    }

    Console.WriteLine(
        $"Executed {scenarios.Count} DRK evaluator scenarios for " +
        $"'{definition.Id}' with full legacy parity.");
}

static void ValidateMachinistEvaluator(RulePolicyDefinition definition)
{
    var generic = new RuleSetTrainingPolicy(definition);
    var scenarios = CreateMachinistScenarios();

    foreach (var scenario in scenarios)
    {
        var decision = generic.Evaluate(scenario.State);

        if (!MatchesExpected(decision, scenario))
        {
            throw new InvalidDataException(
                $"MCH evaluator scenario '{scenario.Name}' failed. " +
                $"Preferred {decision.PreferredActionId}, " +
                $"acceptable [{Join(decision.AcceptableActionIds)}], " +
                $"suggestions [{Join(decision.SuggestedActionIds)}].");
        }
    }

    Console.WriteLine(
        $"Executed {scenarios.Count} MCH evaluator scenarios for " +
        $"'{definition.Id}' without a job-specific policy class.");
}

static bool MatchesExpected(
    TrainingDecision decision,
    DiagnosticScenario scenario)
{
    return decision.PreferredActionId == scenario.ExpectedPreferred &&
        scenario.ExpectedAcceptable.All(
            actionId => decision.AcceptableActionIds.Contains(actionId)) &&
        scenario.ExpectedSuggestions.All(
            actionId => decision.SuggestedActionIds.Contains(actionId));
}

static IReadOnlyList<DiagnosticScenario> CreateDarkKnightScenarios()
{
    var scenarios = new List<DiagnosticScenario>
    {
        new(
            "Starts the Souleater combo",
            CreateDarkKnightState(),
            HardSlash)
    };

    var syphonState = CreateDarkKnightState();
    syphonState.SetCombo(HardSlash, 20f);
    scenarios.Add(
        new DiagnosticScenario(
            "Continues native combo state",
            syphonState,
            SyphonStrike));

    var overcapState = CreateDarkKnightState();
    overcapState.SetGauge("blood", 90);
    overcapState.SetCombo(SyphonStrike, 20f);
    scenarios.Add(
        new DiagnosticScenario(
            "Prevents Souleater Blood overcap",
            overcapState,
            Bloodspiller));

    var safeContinuationState = CreateDarkKnightState();
    safeContinuationState.SetGauge("blood", 90);
    safeContinuationState.SetCombo(HardSlash, 20f);
    scenarios.Add(
        new DiagnosticScenario(
            "Allows safe combo continuation near Blood cap",
            safeContinuationState,
            Bloodspiller,
            new[] { SyphonStrike }));

    scenarios.Add(
        CreateAdjustedDarkKnightScenario(
            "Recognises Scarlet Delirium",
            ScarletDelirium));
    scenarios.Add(
        CreateAdjustedDarkKnightScenario(
            "Recognises Comeuppance",
            Comeuppance));
    scenarios.Add(
        CreateAdjustedDarkKnightScenario(
            "Recognises Torcleaver",
            Torcleaver));

    var mpState = CreateDarkKnightState();
    mpState.SetGauge("mp", 9000);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Edge before MP overcap",
            mpState,
            HardSlash,
            Suggestions: new[] { EdgeOfShadow }));

    var darkArtsState = CreateDarkKnightState();
    darkArtsState.SetGauge("dark_arts", 1);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests the free Dark Arts Edge",
            darkArtsState,
            HardSlash,
            Suggestions: new[] { EdgeOfShadow }));

    var deliriumState = CreateDarkKnightState();
    deliriumState.RecordAcceptedAction(HardSlash);
    deliriumState.SetCooldown(
        Delirium,
        ReadyCooldown());
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Delirium when ready",
            deliriumState,
            SyphonStrike,
            Suggestions: new[] { Delirium }));

    return scenarios;
}

static IReadOnlyList<DiagnosticScenario> CreateMachinistScenarios()
{
    var scenarios = new List<DiagnosticScenario>
    {
        new(
            "Starts the heated combo",
            CreateMachinistState(),
            HeatedSplitShot)
    };

    var comboState = CreateMachinistState();
    comboState.SetCombo(HeatedSplitShot, 20f);
    scenarios.Add(
        new DiagnosticScenario(
            "Continues the heated combo",
            comboState,
            HeatedSlugShot));

    var overheatState = CreateMachinistState();
    overheatState.SetStateValue("overheated", 1d);
    scenarios.Add(
        new DiagnosticScenario(
            "Uses Blazing Shot while Overheated",
            overheatState,
            BlazingShot));

    var fullMetalState = CreateMachinistState();
    fullMetalState.ReplaceStatuses(
        new[]
        {
            new StatusSnapshot
            {
                StatusId = FullMetalMachinistStatus,
                RemainingSeconds = 20f
            }
        });
    scenarios.Add(
        new DiagnosticScenario(
            "Uses Full Metal Field from its proc",
            fullMetalState,
            FullMetalField));

    var excavatorState = CreateMachinistState();
    excavatorState.SetAdjustedAction(ChainSaw, Excavator);
    scenarios.Add(
        new DiagnosticScenario(
            "Follows Chain Saw into Excavator",
            excavatorState,
            Excavator));

    var airAnchorState = CreateMachinistState();
    airAnchorState.SetCooldown(AirAnchor, ReadyCooldown());
    scenarios.Add(
        new DiagnosticScenario(
            "Uses Air Anchor when ready",
            airAnchorState,
            AirAnchor));

    var drillCapState = CreateMachinistState();
    drillCapState.SetCooldown(
        Drill,
        new CooldownSnapshot
        {
            Charges = 2,
            MaximumCharges = 2
        });
    scenarios.Add(
        new DiagnosticScenario(
            "Prevents Drill charge overcap",
            drillCapState,
            Drill));

    var heatState = CreateMachinistState();
    heatState.SetGauge("heat", 90);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Hypercharge before Heat overcap",
            heatState,
            HeatedSplitShot,
            Suggestions: new[] { Hypercharge }));

    var batteryState = CreateMachinistState();
    batteryState.SetGauge("battery", 90);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Queen before Battery overcap",
            batteryState,
            HeatedSplitShot,
            Suggestions: new[] { AutomatonQueen }));

    var wildfireState = CreateMachinistState();
    wildfireState.ReplaceStatuses(
        new[]
        {
            new StatusSnapshot
            {
                StatusId = WildfirePlayerStatus,
                RemainingSeconds = 8f
            }
        });
    scenarios.Add(
        new DiagnosticScenario(
            "Pairs Hypercharge with Wildfire",
            wildfireState,
            HeatedSplitShot,
            Suggestions: new[] { Hypercharge }));

    var barrelState = CreateMachinistState();
    barrelState.RecordAcceptedAction(HeatedSplitShot);
    barrelState.SetCooldown(BarrelStabilizer, ReadyCooldown());
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Barrel Stabilizer when ready",
            barrelState,
            HeatedSlugShot,
            Suggestions: new[] { BarrelStabilizer }));

    var chargeState = CreateMachinistState();
    chargeState.SetCooldown(
        DoubleCheck,
        new CooldownSnapshot
        {
            Charges = 3,
            MaximumCharges = 3
        });
    chargeState.SetCooldown(
        Checkmate,
        new CooldownSnapshot
        {
            Charges = 3,
            MaximumCharges = 3
        });
    chargeState.SetCooldown(
        Reassemble,
        new CooldownSnapshot
        {
            Charges = 2,
            MaximumCharges = 2
        });
    scenarios.Add(
        new DiagnosticScenario(
            "Surfaces capped weave charges",
            chargeState,
            HeatedSplitShot,
            Suggestions: new[]
            {
                DoubleCheck,
                Checkmate,
                Reassemble
            }));

    return scenarios;
}

static DiagnosticScenario CreateAdjustedDarkKnightScenario(
    string name,
    uint adjustedActionId)
{
    var state = CreateDarkKnightState();
    state.SetAdjustedAction(Bloodspiller, adjustedActionId);

    return new DiagnosticScenario(name, state, adjustedActionId);
}

static TrainingState CreateDarkKnightState()
{
    var state = new TrainingState();
    state.Begin("DRK", 100);
    state.SetGauge("blood", 0);
    state.SetGauge("mp", 6000);
    state.SetGauge("darkside_ms", 30000);
    state.SetGauge("dark_arts", 0);
    state.SetGauge("delirium_step", 0);
    state.SetAdjustedAction(Bloodspiller, Bloodspiller);
    state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
    state.SetCooldown(Delirium, UnreadyCooldown());
    return state;
}

static TrainingState CreateMachinistState()
{
    var state = new TrainingState();
    state.Begin("MCH", 100);
    state.SetGauge("heat", 0);
    state.SetGauge("battery", 0);
    state.SetStateValue("overheated", 0d);
    state.SetStateValue("overheat_ms", 0d);
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

    state.SetCooldown(AirAnchor, UnreadyCooldown());
    state.SetCooldown(ChainSaw, UnreadyCooldown());
    state.SetCooldown(
        Drill,
        new CooldownSnapshot
        {
            RemainingSeconds = 10f,
            Charges = 0,
            MaximumCharges = 2
        });
    state.SetCooldown(BarrelStabilizer, UnreadyCooldown());
    state.SetCooldown(Wildfire, UnreadyCooldown());
    state.SetCooldown(
        DoubleCheck,
        new CooldownSnapshot
        {
            RemainingSeconds = 10f,
            Charges = 1,
            MaximumCharges = 3
        });
    state.SetCooldown(
        Checkmate,
        new CooldownSnapshot
        {
            RemainingSeconds = 10f,
            Charges = 1,
            MaximumCharges = 3
        });
    state.SetCooldown(
        Reassemble,
        new CooldownSnapshot
        {
            RemainingSeconds = 10f,
            Charges = 1,
            MaximumCharges = 2
        });

    return state;
}

static CooldownSnapshot ReadyCooldown()
{
    return new CooldownSnapshot
    {
        Charges = 1,
        MaximumCharges = 1
    };
}

static CooldownSnapshot UnreadyCooldown()
{
    return new CooldownSnapshot
    {
        RemainingSeconds = 30f,
        Charges = 0,
        MaximumCharges = 1
    };
}

static bool SameSet(
    IReadOnlyList<uint> left,
    IReadOnlyList<uint> right)
{
    return left.Count == right.Count &&
        left.OrderBy(value => value)
            .SequenceEqual(right.OrderBy(value => value));
}

static string Join(IReadOnlyList<uint> values)
{
    return values.Count == 0
        ? "none"
        : string.Join(", ", values);
}

sealed record DiagnosticScenario(
    string Name,
    TrainingState State,
    uint ExpectedPreferred,
    IReadOnlyList<uint>? Acceptable = null,
    IReadOnlyList<uint>? Suggestions = null)
{
    public IReadOnlyList<uint> ExpectedAcceptable =>
        Acceptable ?? Array.Empty<uint>();

    public IReadOnlyList<uint> ExpectedSuggestions =>
        Suggestions ?? Array.Empty<uint>();
}
