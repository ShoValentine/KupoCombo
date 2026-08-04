using System;
using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class DarkKnightComboPolicy : ITrainingPolicy
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

    private const uint DeliriumStatusLowLevel = 1972;
    private const uint DeliriumStatusEnhanced = 3836;

    private static readonly uint[] TrackedActions =
    {
        HardSlash,
        SyphonStrike,
        Souleater,
        Bloodspiller,
        ScarletDelirium,
        Comeuppance,
        Torcleaver
    };

    private static readonly uint[] AdvisoryActions =
    {
        Delirium,
        EdgeOfDarkness,
        EdgeOfShadow
    };

    public string Id => "drk-priority-practice";

    public string Name => "DRK Endless Priority Practice";

    public string Job => "DRK";

    public int? ExpectedLength => null;

    public IReadOnlyCollection<uint> TrackedActionIds => TrackedActions;

    public IReadOnlyCollection<uint> AdvisoryActionIds => AdvisoryActions;

    public bool IgnoreUntrackedActions => true;

    public TrainingDecision Evaluate(TrainingState state)
    {
        var suggestions = GetWeaveSuggestions(state, out var suggestionReason);
        var deliriumAction = GetDeliriumGcd(state);

        if (deliriumAction != 0)
        {
            return CreateDecision(
                deliriumAction,
                GetDeliriumReason(deliriumAction),
                suggestions,
                suggestionReason);
        }

        var nextComboAction = GetNextComboAction(state);
        var blood = state.GetGauge("blood");
        var canUseBloodspiller = state.Level >= 62;

        if (canUseBloodspiller &&
            nextComboAction == Souleater &&
            blood > 80)
        {
            return CreateDecision(
                Bloodspiller,
                "Spend Blood before Souleater would overcap the gauge.",
                suggestions,
                suggestionReason);
        }

        if (canUseBloodspiller && blood >= 90)
        {
            return CreateDecision(
                Bloodspiller,
                "Blood is near the cap. Bloodspiller is preferred, " +
                "but continuing the combo is still safe for this GCD.",
                suggestions,
                suggestionReason,
                nextComboAction);
        }

        var reason = nextComboAction switch
        {
            SyphonStrike =>
                "Continue the live combo with Syphon Strike.",
            Souleater =>
                "Finish the live combo with Souleater.",
            _ =>
                "Begin a new combo with Hard Slash."
        };

        return CreateDecision(
            nextComboAction,
            reason,
            suggestions,
            suggestionReason);
    }

    private static uint GetDeliriumGcd(TrainingState state)
    {
        var adjustedBloodspiller = state.GetAdjustedAction(
            Bloodspiller,
            Bloodspiller);

        if (adjustedBloodspiller == ScarletDelirium ||
            adjustedBloodspiller == Comeuppance ||
            adjustedBloodspiller == Torcleaver)
        {
            return adjustedBloodspiller;
        }

        if (state.GetGauge("delirium_step") > 0 ||
            state.GetStatusStacks(DeliriumStatusLowLevel) > 0 ||
            state.GetStatusStacks(DeliriumStatusEnhanced) > 0)
        {
            return Bloodspiller;
        }

        return 0;
    }

    private static string GetDeliriumReason(uint actionId)
    {
        return actionId switch
        {
            ScarletDelirium =>
                "Delirium is active. Begin its GCD chain with Scarlet Delirium.",
            Comeuppance =>
                "Continue the Delirium chain with Comeuppance.",
            Torcleaver =>
                "Finish the Delirium chain with Torcleaver.",
            _ =>
                "Spend a Delirium stack on Bloodspiller."
        };
    }

    private static IReadOnlyList<uint> GetWeaveSuggestions(
        TrainingState state,
        out string reason)
    {
        var suggestions = new List<uint>();
        var reasons = new List<string>();

        AddEdgeSuggestion(state, suggestions, reasons);
        AddDeliriumSuggestion(state, suggestions, reasons);

        reason = string.Join(" ", reasons);
        return suggestions;
    }

    private static void AddEdgeSuggestion(
        TrainingState state,
        ICollection<uint> suggestions,
        ICollection<string> reasons)
    {
        if (state.Level < 40)
        {
            return;
        }

        var mp = state.GetGauge("mp");
        var darksideRemaining = state.GetGauge("darkside_ms");
        var hasDarkArts = state.GetGauge("dark_arts") > 0;

        var shouldSpend = hasDarkArts ||
            mp >= 9000 ||
            (mp >= 3000 && darksideRemaining <= 10000);

        if (!shouldSpend)
        {
            return;
        }

        var edgeAction = state.GetAdjustedAction(
            EdgeOfDarkness,
            state.Level >= 74 ? EdgeOfShadow : EdgeOfDarkness);

        suggestions.Add(edgeAction);

        if (hasDarkArts)
        {
            reasons.Add("Use the free Dark Arts Edge before it is overwritten.");
            return;
        }

        if (mp >= 9000)
        {
            reasons.Add("Weave Edge to avoid overcapping MP.");
            return;
        }

        reasons.Add("Refresh Darkside with Edge before it expires.");
    }

    private static void AddDeliriumSuggestion(
        TrainingState state,
        ICollection<uint> suggestions,
        ICollection<string> reasons)
    {
        if (state.Level < 68 ||
            state.AcceptedActionCount == 0 ||
            GetDeliriumGcd(state) != 0)
        {
            return;
        }

        var cooldown = state.GetCooldown(Delirium);

        if (cooldown?.IsReady != true)
        {
            return;
        }

        suggestions.Add(Delirium);
        reasons.Add("Delirium is ready; weave it to begin the next burst cycle.");
    }

    private static uint GetNextComboAction(TrainingState state)
    {
        if (state.ComboRemainingSeconds > 0f)
        {
            return state.NativeComboActionId switch
            {
                HardSlash => SyphonStrike,
                SyphonStrike => Souleater,
                _ => HardSlash
            };
        }

        return state.LastAcceptedActionId switch
        {
            HardSlash => SyphonStrike,
            SyphonStrike => Souleater,
            _ => HardSlash
        };
    }

    private static TrainingDecision CreateDecision(
        uint preferredActionId,
        string reason,
        IReadOnlyList<uint> suggestedActionIds,
        string suggestionReason,
        params uint[] acceptableActionIds)
    {
        return new TrainingDecision
        {
            PreferredActionId = preferredActionId,
            AcceptableActionIds = acceptableActionIds,
            SuggestedActionIds = suggestedActionIds,
            Reason = reason,
            SuggestionReason = suggestionReason,
            MistakeResponse = TrainingMistakeResponse.KeepProgress
        };
    }
}
