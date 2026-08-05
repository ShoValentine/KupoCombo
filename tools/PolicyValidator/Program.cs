using KupoCombo.Models;
using KupoCombo.Services;

const uint Unmend = 3624;
const uint HardSlash = 3617;
const uint SyphonStrike = 3623;
const uint Souleater = 3632;
const uint Delirium = 7390;
const uint Bloodspiller = 7392;
const uint EdgeOfDarkness = 16467;
const uint EdgeOfShadow = 16470;
const uint LivingShadow = 16472;
const uint CarveAndSpit = 3639;
const uint SaltedEarth = 3643;
const uint SaltAndDarkness = 25755;
const uint Shadowbringer = 25757;
const uint ScarletDelirium = 36928;
const uint Comeuppance = 36929;
const uint Torcleaver = 36930;
const uint Disesteem = 36932;

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
    Console.Error.WriteLine("Usage: PolicyValidator <policy-directory>");
    return 2;
}

var policyDirectory = Path.GetFullPath(args[0]);

if (!Directory.Exists(policyDirectory))
{
    Console.Error.WriteLine($"Policy directory not found: {policyDirectory}");
    return 2;
}

var policyFiles = Directory
    .EnumerateFiles(policyDirectory, "*.json", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (policyFiles.Length == 0)
{
    Console.Error.WriteLine($"No policy files found in {policyDirectory}");
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
                ValidateDarkKnightOpenerForecast(policy);
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
            $"Policy validation failed for {policyFile}: {exception.Message}");
    }
}

return failed ? 1 : 0;

static void ValidateDarkKnightEvaluator(RulePolicyDefinition definition)
{
    var policy = new RuleSetTrainingPolicy(definition);
    var scenarios = new List<DiagnosticScenario>();

    scenarios.Add(new DiagnosticScenario(
        "Starts the standard opener with Unmend",
        CreateDarkKnightState(),
        Unmend));

    var comboState = CreateDarkKnightState();
    comboState.RecordAcceptedAction(HardSlash);
    comboState.SetCombo(HardSlash, 20f);
    scenarios.Add(new DiagnosticScenario(
        "Continues native combo state",
        comboState,
        SyphonStrike));

    var overcapState = CreateDarkKnightState();
    overcapState.RecordAcceptedAction(SyphonStrike);
    overcapState.SetGauge("blood", 90);
    overcapState.SetCombo(SyphonStrike, 20f);
    scenarios.Add(new DiagnosticScenario(
        "Prevents Souleater Blood overcap",
        overcapState,
        Bloodspiller));

    var deliriumState = CreateDarkKnightState();
    deliriumState.RecordAcceptedAction(Souleater);
    deliriumState.SetCombo(Souleater, 20f);
    deliriumState.SetAdjustedAction(Bloodspiller, ScarletDelirium);
    scenarios.Add(new DiagnosticScenario(
        "Follows the enhanced Delirium chain",
        deliriumState,
        ScarletDelirium));

    var edgeState = CreateDarkKnightState();
    edgeState.RecordAcceptedAction(Unmend);
    scenarios.Add(new DiagnosticScenario(
        "Suggests Edge after Unmend",
        edgeState,
        HardSlash,
        Suggestions: new[] { EdgeOfShadow }));

    var shadowState = CreateDarkKnightState();
    shadowState.RecordAcceptedAction(HardSlash);
    shadowState.SetCombo(HardSlash, 20f);
    scenarios.Add(new DiagnosticScenario(
        "Suggests Living Shadow after Hard Slash",
        shadowState,
        SyphonStrike,
        Suggestions: new[] { LivingShadow }));

    var burstState = CreateDarkKnightState();
    burstState.RecordAcceptedAction(Souleater);
    burstState.SetCombo(Souleater, 20f);
    burstState.SetAdjustedAction(LivingShadow, Disesteem);
    scenarios.Add(new DiagnosticScenario(
        "Pairs Delirium with the Disesteem burst GCD",
        burstState,
        Disesteem,
        Suggestions: new[] { Delirium }));

    ValidateScenarios("DRK", policy, scenarios);
}

static void ValidateDarkKnightOpenerForecast(
    RulePolicyDefinition definition)
{
    var policy = new RuleSetTrainingPolicy(definition);
    var forecast = policy.Forecast(CreateDarkKnightState(), 6);
    var ribbon = forecast
        .SelectMany(step =>
            step.SuggestedActionIds.Concat(new[] { step.GcdActionId }))
        .ToArray();
    var expectedPrefix = new uint[]
    {
        Unmend,
        EdgeOfShadow,
        HardSlash,
        LivingShadow,
        SyphonStrike,
        Souleater,
        Delirium,
        Disesteem,
        CarveAndSpit,
        EdgeOfShadow,
        ScarletDelirium
    };

    if (ribbon.Length < expectedPrefix.Length ||
        !ribbon.Take(expectedPrefix.Length).SequenceEqual(expectedPrefix))
    {
        throw new InvalidDataException(
            "DRK opener forecast diverged. Expected prefix " +
            $"[{Join(expectedPrefix)}], got [{Join(ribbon)}].");
    }

    Console.WriteLine(
        "DRK forecast reproduced the stored opener's opening burst sequence " +
        "using cooldowns and action effects.");
}

static void ValidateMachinistEvaluator(RulePolicyDefinition definition)
{
    var policy = new RuleSetTrainingPolicy(definition);
    var scenarios = new List<DiagnosticScenario>
    {
        new(
            "Starts the heated combo",
            CreateMachinistState(),
            HeatedSplitShot)
    };

    var comboState = CreateMachinistState();
    comboState.RecordAcceptedAction(HeatedSplitShot);
    comboState.SetCombo(HeatedSplitShot, 20f);
    scenarios.Add(new DiagnosticScenario(
        "Continues the heated combo",
        comboState,
        HeatedSlugShot));

    var overheatState = CreateMachinistState();
    overheatState.SetStateValue("overheated", 1d);
    overheatState.SetStateValue("overheat_ms", 10000d);
    scenarios.Add(new DiagnosticScenario(
        "Uses Blazing Shot while Overheated",
        overheatState,
        BlazingShot));

    var fullMetalState = CreateMachinistState();
    fullMetalState.ReplaceStatuses(new[]
    {
        new StatusSnapshot
        {
            StatusId = FullMetalMachinistStatus,
            RemainingSeconds = 20f
        }
    });
    scenarios.Add(new DiagnosticScenario(
        "Uses Full Metal Field from its proc",
        fullMetalState,
        FullMetalField));

    var excavatorState = CreateMachinistState();
    excavatorState.SetAdjustedAction(ChainSaw, Excavator);
    scenarios.Add(new DiagnosticScenario(
        "Follows Chain Saw into Excavator",
        excavatorState,
        Excavator));

    var airAnchorState = CreateMachinistState();
    airAnchorState.SetCooldown(AirAnchor, ReadyCooldown(40f));
    scenarios.Add(new DiagnosticScenario(
        "Uses Air Anchor when ready",
        airAnchorState,
        AirAnchor));

    var heatState = CreateMachinistState();
    heatState.SetGauge("heat", 90);
    scenarios.Add(new DiagnosticScenario(
        "Suggests Hypercharge before Heat overcap",
        heatState,
        HeatedSplitShot,
        Suggestions: new[] { Hypercharge }));

    var batteryState = CreateMachinistState();
    batteryState.SetGauge("battery", 90);
    scenarios.Add(new DiagnosticScenario(
        "Suggests Queen before Battery overcap",
        batteryState,
        HeatedSplitShot,
        Suggestions: new[] { AutomatonQueen }));

    var wildfireState = CreateMachinistState();
    wildfireState.ReplaceStatuses(new[]
    {
        new StatusSnapshot
        {
            StatusId = WildfirePlayerStatus,
            RemainingSeconds = 8f
        }
    });
    scenarios.Add(new DiagnosticScenario(
        "Pairs Hypercharge with Wildfire",
        wildfireState,
        HeatedSplitShot,
        Suggestions: new[] { Hypercharge }));

    var chargeState = CreateMachinistState();
    chargeState.SetCooldown(DoubleCheck, ReadyCooldown(30f, 3, 3));
    chargeState.SetCooldown(Checkmate, ReadyCooldown(30f, 3, 3));
    chargeState.SetCooldown(Reassemble, ReadyCooldown(55f, 2, 2));
    scenarios.Add(new DiagnosticScenario(
        "Surfaces capped weave charges",
        chargeState,
        HeatedSplitShot,
        Suggestions: new[] { DoubleCheck, Checkmate, Reassemble }));

    ValidateScenarios("MCH", policy, scenarios);
}

static void ValidateScenarios(
    string job,
    RuleSetTrainingPolicy policy,
    IReadOnlyList<DiagnosticScenario> scenarios)
{
    foreach (var scenario in scenarios)
    {
        var decision = policy.Evaluate(scenario.State);
        var matches =
            decision.PreferredActionId == scenario.ExpectedPreferred &&
            scenario.ExpectedAcceptable.All(
                actionId => decision.AcceptableActionIds.Contains(actionId)) &&
            scenario.ExpectedSuggestions.All(
                actionId => decision.SuggestedActionIds.Contains(actionId));

        if (!matches)
        {
            throw new InvalidDataException(
                $"{job} scenario '{scenario.Name}' failed. " +
                $"Preferred {decision.PreferredActionId}, " +
                $"acceptable [{Join(decision.AcceptableActionIds)}], " +
                $"suggestions [{Join(decision.SuggestedActionIds)}].");
        }
    }

    Console.WriteLine(
        $"Executed {scenarios.Count} {job} evaluator scenarios for " +
        $"'{policy.Id}'.");
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
    state.SetAdjustedAction(LivingShadow, LivingShadow);
    state.SetAdjustedAction(SaltedEarth, SaltedEarth);

    state.SetCooldown(Delirium, ReadyCooldown(60f));
    state.SetCooldown(LivingShadow, ReadyCooldown(120f));
    state.SetCooldown(CarveAndSpit, ReadyCooldown(60f));
    state.SetCooldown(Shadowbringer, ReadyCooldown(60f, 2, 2));
    state.SetCooldown(SaltedEarth, ReadyCooldown(90f));
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

    state.SetCooldown(AirAnchor, UnreadyCooldown(10f, 40f));
    state.SetCooldown(ChainSaw, UnreadyCooldown(30f, 60f));
    state.SetCooldown(Drill, UnreadyCooldown(10f, 20f, 2));
    state.SetCooldown(BarrelStabilizer, UnreadyCooldown(30f, 120f));
    state.SetCooldown(Wildfire, UnreadyCooldown(30f, 120f));
    state.SetCooldown(DoubleCheck, UnreadyCooldown(10f, 30f, 3, 1));
    state.SetCooldown(Checkmate, UnreadyCooldown(10f, 30f, 3, 1));
    state.SetCooldown(Reassemble, UnreadyCooldown(10f, 55f, 2, 1));
    return state;
}

static CooldownSnapshot ReadyCooldown(
    float rechargeSeconds,
    int maximumCharges = 1,
    int? charges = null)
{
    return new CooldownSnapshot
    {
        Charges = charges ?? maximumCharges,
        MaximumCharges = maximumCharges,
        RechargeSeconds = rechargeSeconds
    };
}

static CooldownSnapshot UnreadyCooldown(
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

static string Join(IEnumerable<uint> values)
{
    var array = values.ToArray();
    return array.Length == 0
        ? "none"
        : string.Join(", ", array);
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
