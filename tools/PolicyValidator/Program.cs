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

        if (expectedJob.Equals("DRK", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var policy in policies)
            {
                ValidateDarkKnightEvaluator(policy);
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

        var expectedMatches =
            current.PreferredActionId == scenario.ExpectedPreferred &&
            scenario.ExpectedAcceptable.All(
                actionId => current.AcceptableActionIds.Contains(actionId)) &&
            scenario.ExpectedSuggestions.All(
                actionId => current.SuggestedActionIds.Contains(actionId));

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
    overcapState.SetCombo(3623, 20f);
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
        CreateAdjustedScenario(
            "Recognises Scarlet Delirium",
            ScarletDelirium));
    scenarios.Add(
        CreateAdjustedScenario(
            "Recognises Comeuppance",
            Comeuppance));
    scenarios.Add(
        CreateAdjustedScenario(
            "Recognises Torcleaver",
            Torcleaver));

    var mpState = CreateDarkKnightState();
    mpState.SetGauge("mp", 9000);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Edge before MP overcap",
            mpState,
            HardSlash,
            suggestions: new[] { EdgeOfShadow }));

    var darkArtsState = CreateDarkKnightState();
    darkArtsState.SetGauge("dark_arts", 1);
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests the free Dark Arts Edge",
            darkArtsState,
            HardSlash,
            suggestions: new[] { EdgeOfShadow }));

    var deliriumState = CreateDarkKnightState();
    deliriumState.RecordAcceptedAction(HardSlash);
    deliriumState.SetCooldown(
        Delirium,
        new CooldownSnapshot
        {
            Charges = 1,
            MaximumCharges = 1
        });
    scenarios.Add(
        new DiagnosticScenario(
            "Suggests Delirium when ready",
            deliriumState,
            SyphonStrike,
            suggestions: new[] { Delirium }));

    return scenarios;
}

static DiagnosticScenario CreateAdjustedScenario(
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
    state.SetCooldown(
        Delirium,
        new CooldownSnapshot
        {
            RemainingSeconds = 30f,
            Charges = 0,
            MaximumCharges = 1
        });
    return state;
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
