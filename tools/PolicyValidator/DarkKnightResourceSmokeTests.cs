using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class DarkKnightResourceSmokeTests
{
    private const uint Unmend = 3624;
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;
    private const uint LivingShadow = 16472;
    private const uint Bloodspiller = 7392;
    private const uint ScarletDelirium = 36928;
    private const uint Comeuppance = 36929;
    private const uint Torcleaver = 36930;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint SaltedEarth = 3643;

    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(arguments[1]);
        var dataDirectory = Directory.GetParent(policyDirectory);

        if (dataDirectory == null)
        {
            return;
        }

        var policyPath = Path.Combine(policyDirectory, "DRK.json");
        var cataloguePath = Path.Combine(
            dataDirectory.FullName,
            "Actions",
            "pve-actions.json");

        if (!File.Exists(policyPath) || !File.Exists(cataloguePath))
        {
            return;
        }

        var catalogue = PveActionCatalogLoader.Load(cataloguePath);
        var definition = RulePolicyLoader
            .Load(policyPath, "DRK")
            .Single(policy => policy.MinimumLevel <= 100);
        PveActionCatalogLoader.Apply(definition, catalogue);

        var policy = new RuleSetTrainingPolicy(definition);

        ValidateDarkArtsEdge(policy, definition);
        ValidateDeliriumResourceChain(policy, definition);

        Console.WriteLine(
            "DRK resource smoke test passed: Dark Arts Edge is free, " +
            "normal Edge costs MP, and the Delirium chain restores MP and Blood Weapon resources.");
    }

    private static void ValidateDarkArtsEdge(
        RuleSetTrainingPolicy policy,
        RulePolicyDefinition definition)
    {
        var freeState = CreateState(definition);
        freeState.SetGauge("mp", 1000);
        freeState.SetGauge("dark_arts", 1);

        if (policy.GetExpectedMpDelta(EdgeOfShadow, freeState) != 0)
        {
            throw new InvalidDataException(
                "Dark Arts Edge was still predicted to spend MP.");
        }

        var freePlan = policy.BuildPracticePlan(freeState);
        var freeWindow = freePlan.Steps.FirstOrDefault(step =>
            step.SuggestedActionIds.Contains(EdgeOfShadow));

        if (freeWindow == null ||
            freeWindow.ExpectedMpAfter != freeWindow.ExpectedMpBefore)
        {
            throw new InvalidDataException(
                "Dark Arts Edge did not preserve projected MP in the practice plan.");
        }

        var paidState = CreateState(definition);
        paidState.SetGauge("mp", 6000);
        paidState.SetGauge("dark_arts", 0);

        if (policy.GetExpectedMpDelta(EdgeOfShadow, paidState) != -3000)
        {
            throw new InvalidDataException(
                "Normal Edge was not predicted to spend 3,000 MP.");
        }

        var paidPlan = policy.BuildPracticePlan(paidState);
        var paidWindow = paidPlan.Steps.FirstOrDefault(step =>
            step.SuggestedActionIds.Contains(EdgeOfShadow));

        if (paidWindow == null ||
            paidWindow.ExpectedMpAfter != paidWindow.ExpectedMpBefore - 3000)
        {
            throw new InvalidDataException(
                "Normal Edge did not reduce projected MP by 3,000.");
        }
    }

    private static void ValidateDeliriumResourceChain(
        RuleSetTrainingPolicy policy,
        RulePolicyDefinition definition)
    {
        var state = CreateState(definition);
        state.SetGauge("mp", 0);
        state.SetGauge("blood", 20);
        state.SetStateValue("blood_weapon_stacks", 3);
        state.RecordAcceptedAction(Souleater);
        state.SetAdjustedAction(Bloodspiller, ScarletDelirium);

        if (policy.GetExpectedMpDelta(ScarletDelirium, state) != 1200)
        {
            throw new InvalidDataException(
                "Scarlet Delirium did not include its own MP return and the active Blood Weapon return.");
        }

        var forecast = policy.Forecast(state, 3);
        var expectedActions = new[]
        {
            ScarletDelirium,
            Comeuppance,
            Torcleaver
        };

        if (forecast.Count != expectedActions.Length ||
            !forecast.Select(step => step.GcdActionId)
                .SequenceEqual(expectedActions))
        {
            throw new InvalidDataException(
                "The enhanced Delirium chain was not forecast in order.");
        }

        foreach (var step in forecast)
        {
            if (step.ExpectedMpAfter - step.ExpectedMpBefore != 1200)
            {
                throw new InvalidDataException(
                    $"Action {step.GcdActionId} did not restore the expected 1,200 MP while Blood Weapon was active.");
            }
        }

        if (forecast[^1].ExpectedMpAfter != 3600)
        {
            throw new InvalidDataException(
                "The three-step Delirium chain did not restore 3,600 projected MP.");
        }

        state.SetStateValue("blood_weapon_stacks", 0);

        if (policy.GetExpectedMpDelta(ScarletDelirium, state) != 600)
        {
            throw new InvalidDataException(
                "Scarlet Delirium's own MP restoration was not separated from Blood Weapon.");
        }
    }

    private static TrainingState CreateState(
        RulePolicyDefinition definition)
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        state.SetGauge("blood", 0);
        state.SetGauge("mp", 6000);
        state.SetGauge("darkside_ms", 30000);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);
        state.SetStateValue("blood_weapon_stacks", 0);
        state.SetPlayerTiming(400, 400, 0);

        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);

        foreach (var action in definition.Actions.Values)
        {
            if (action.Lane == PolicyLane.Gcd)
            {
                state.SetAdjustedRecastSeconds(action.ActionId, 2.5f);
            }
        }

        state.SetAdjustedRecastSeconds(Unmend, 2.5f);
        state.SetAdjustedRecastSeconds(HardSlash, 2.5f);
        state.SetAdjustedRecastSeconds(SyphonStrike, 2.5f);
        state.SetAdjustedRecastSeconds(Souleater, 2.5f);
        return state;
    }
}
