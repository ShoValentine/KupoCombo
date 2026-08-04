using System.Collections.Generic;
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
        var report = new PolicyDiagnosticReport();
        var policy = new DarkKnightComboPolicy();

        CheckPreferred(
            report,
            policy,
            "Starts the Souleater combo",
            CreateState(),
            HardSlash);

        var syphonState = CreateState();
        syphonState.SetCombo(HardSlash, 20f);
        CheckPreferred(
            report,
            policy,
            "Continues native combo state",
            syphonState,
            SyphonStrike);

        var overcapState = CreateState();
        overcapState.SetGauge("blood", 90);
        overcapState.SetCombo(SyphonStrike, 20f);
        CheckPreferred(
            report,
            policy,
            "Prevents Souleater Blood overcap",
            overcapState,
            Bloodspiller);

        var safeContinuationState = CreateState();
        safeContinuationState.SetGauge("blood", 90);
        safeContinuationState.SetCombo(HardSlash, 20f);
        var safeDecision = policy.Evaluate(safeContinuationState);
        report.Add(
            "Allows safe combo continuation near Blood cap",
            safeDecision.PreferredActionId == Bloodspiller &&
            Contains(safeDecision.AcceptableActionIds, SyphonStrike),
            $"Preferred {safeDecision.PreferredActionId}; " +
            $"expected Bloodspiller with Syphon Strike acceptable.");

        CheckAdjustedDeliriumAction(
            report,
            policy,
            "Recognises Scarlet Delirium",
            ScarletDelirium);
        CheckAdjustedDeliriumAction(
            report,
            policy,
            "Recognises Comeuppance",
            Comeuppance);
        CheckAdjustedDeliriumAction(
            report,
            policy,
            "Recognises Torcleaver",
            Torcleaver);

        var mpState = CreateState();
        mpState.SetGauge("mp", 9000);
        var mpDecision = policy.Evaluate(mpState);
        report.Add(
            "Suggests Edge before MP overcap",
            Contains(mpDecision.SuggestedActionIds, EdgeOfShadow),
            $"Suggestions: {Join(mpDecision.SuggestedActionIds)}");

        var darkArtsState = CreateState();
        darkArtsState.SetGauge("dark_arts", 1);
        var darkArtsDecision = policy.Evaluate(darkArtsState);
        report.Add(
            "Suggests the free Dark Arts Edge",
            Contains(darkArtsDecision.SuggestedActionIds, EdgeOfShadow),
            $"Suggestions: {Join(darkArtsDecision.SuggestedActionIds)}");

        var deliriumState = CreateState();
        deliriumState.RecordAcceptedAction(HardSlash);
        deliriumState.SetCooldown(
            Delirium,
            new CooldownSnapshot
            {
                Charges = 1,
                MaximumCharges = 1
            });
        var deliriumDecision = policy.Evaluate(deliriumState);
        report.Add(
            "Suggests Delirium when ready",
            Contains(deliriumDecision.SuggestedActionIds, Delirium),
            $"Suggestions: {Join(deliriumDecision.SuggestedActionIds)}");

        return report;
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

    private static void CheckPreferred(
        PolicyDiagnosticReport report,
        ITrainingPolicy policy,
        string name,
        TrainingState state,
        uint expectedActionId)
    {
        var decision = policy.Evaluate(state);

        report.Add(
            name,
            decision.PreferredActionId == expectedActionId,
            $"Preferred {decision.PreferredActionId}; expected {expectedActionId}.");
    }

    private static void CheckAdjustedDeliriumAction(
        PolicyDiagnosticReport report,
        ITrainingPolicy policy,
        string name,
        uint adjustedActionId)
    {
        var state = CreateState();
        state.SetAdjustedAction(Bloodspiller, adjustedActionId);
        CheckPreferred(report, policy, name, state, adjustedActionId);
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
}
