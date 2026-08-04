using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class PolicyDiagnosticResult
{
    public string Name { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public string Detail { get; init; } = string.Empty;
}

public sealed class PolicyDiagnosticReport
{
    public List<PolicyDiagnosticResult> Results { get; } = new();

    public int PassedCount { get; private set; }

    public int FailedCount => Results.Count - PassedCount;

    public bool Passed => FailedCount == 0;

    public void Add(string name, bool passed, string detail)
    {
        Results.Add(
            new PolicyDiagnosticResult
            {
                Name = name,
                Passed = passed,
                Detail = detail
            });

        if (passed)
        {
            PassedCount++;
        }
    }
}

public static class TrainingPolicyDiagnostics
{
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;
    private const uint Delirium = 7390;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint ScarletDelirium = 36928;
    private const uint Comeuppance = 36929;
    private const uint Torcleaver = 36930;

    public static PolicyDiagnosticReport RunDarkKnight()
    {
        return RunDarkKnight(new DarkKnightComboPolicy());
    }

    public static PolicyDiagnosticReport RunDarkKnight(
        RulePolicyDefinition definition)
    {
        return RunDarkKnight(new RuleSetTrainingPolicy(definition));
    }

    public static PolicyDiagnosticReport RunDarkKnight(
        ITrainingPolicy policy)
    {
        var report = new PolicyDiagnosticReport();
        var scenarios = CreateScenarios();

        foreach (var scenario in scenarios)
        {
            var decision = policy.Evaluate(scenario.State);
            var passed = decision.PreferredActionId == scenario.ExpectedPreferred &&
                scenario.ExpectedAcceptable.All(
                    actionId => Contains(decision.AcceptableActionIds, actionId)) &&
                scenario.ExpectedSuggestions.All(
                    actionId => Contains(decision.SuggestedActionIds, actionId));

            report.Add(
                scenario.Name,
                passed,
                $"Preferred {decision.PreferredActionId}; " +
                $"acceptable [{Join(decision.AcceptableActionIds)}]; " +
                $"suggestions [{Join(decision.SuggestedActionIds)}].");
        }

        AddLegacyParityResult(report, policy, scenarios);
        return report;
    }

    private static IReadOnlyList<DiagnosticScenario> CreateScenarios()
    {
        var scenarios = new List<DiagnosticScenario>();

        scenarios.Add(
            new DiagnosticScenario(
                "Starts the Souleater combo",
                CreateState(),
                HardSlash));

        var syphonState = CreateState();
        syphonState.SetCombo(HardSlash, 20f);
        scenarios.Add(
            new DiagnosticScenario(
                "Continues native combo state",
                syphonState,
                SyphonStrike));

        var overcapState = CreateState();
        overcapState.SetGauge("blood", 90);
        overcapState.SetCombo(SyphonStrike, 20f);
        scenarios.Add(
            new DiagnosticScenario(
                "Prevents Souleater Blood overcap",
                overcapState,
                Bloodspiller));

        var safeContinuationState = CreateState();
        safeContinuationState.SetGauge("blood", 90);
        safeContinuationState.SetCombo(HardSlash, 20f);
        scenarios.Add(
            new DiagnosticScenario(
                "Allows safe combo continuation near Blood cap",
                safeContinuationState,
                Bloodspiller,
                new[] { SyphonStrike }));

        scenarios.Add(
            CreateAdjustedDeliriumScenario(
                "Recognises Scarlet Delirium",
                ScarletDelirium));
        scenarios.Add(
            CreateAdjustedDeliriumScenario(
                "Recognises Comeuppance",
                Comeuppance));
        scenarios.Add(
            CreateAdjustedDeliriumScenario(
                "Recognises Torcleaver",
                Torcleaver));

        var mpState = CreateState();
        mpState.SetGauge("mp", 9000);
        scenarios.Add(
            new DiagnosticScenario(
                "Suggests Edge before MP overcap",
                mpState,
                HardSlash,
                expectedSuggestions: new[] { EdgeOfShadow }));

        var darkArtsState = CreateState();
        darkArtsState.SetGauge("dark_arts", 1);
        scenarios.Add(
            new DiagnosticScenario(
                "Suggests the free Dark Arts Edge",
                darkArtsState,
                HardSlash,
                expectedSuggestions: new[] { EdgeOfShadow }));

        var deliriumState = CreateState();
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
                expectedSuggestions: new[] { Delirium }));

        return scenarios;
    }

    private static DiagnosticScenario CreateAdjustedDeliriumScenario(
        string name,
        uint adjustedActionId)
    {
        var state = CreateState();
        state.SetAdjustedAction(Bloodspiller, adjustedActionId);

        return new DiagnosticScenario(
            name,
            state,
            adjustedActionId);
    }

    private static TrainingState CreateState()
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

    private static void AddLegacyParityResult(
        PolicyDiagnosticReport report,
        ITrainingPolicy policy,
        IReadOnlyList<DiagnosticScenario> scenarios)
    {
        if (policy is LegacyDarkKnightComboPolicy)
        {
            return;
        }

        var legacy = new LegacyDarkKnightComboPolicy();
        var mismatches = new List<string>();

        foreach (var scenario in scenarios)
        {
            var current = policy.Evaluate(scenario.State);
            var previous = legacy.Evaluate(scenario.State);

            if (current.PreferredActionId != previous.PreferredActionId ||
                !SameSet(
                    current.AcceptableActionIds,
                    previous.AcceptableActionIds) ||
                !SameSet(
                    current.SuggestedActionIds,
                    previous.SuggestedActionIds))
            {
                mismatches.Add(scenario.Name);
            }
        }

        report.Add(
            "Matches the preserved legacy policy",
            mismatches.Count == 0,
            mismatches.Count == 0
                ? $"All {scenarios.Count} reference scenarios match."
                : $"Mismatches: {string.Join(", ", mismatches)}.");
    }

    private static bool SameSet(
        IReadOnlyList<uint> left,
        IReadOnlyList<uint> right)
    {
        return left.Count == right.Count &&
            left.OrderBy(value => value)
                .SequenceEqual(right.OrderBy(value => value));
    }

    private static bool Contains(
        IReadOnlyList<uint> actionIds,
        uint actionId)
    {
        foreach (var candidateActionId in actionIds)
        {
            if (candidateActionId == actionId)
            {
                return true;
            }
        }

        return false;
    }

    private static string Join(IReadOnlyList<uint> actionIds)
    {
        return actionIds.Count == 0
            ? "none"
            : string.Join(", ", actionIds);
    }

    private sealed record DiagnosticScenario(
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
}
