using System;
using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class DarkKnightComboPolicy : ITrainingPolicy
{
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;
    private const uint Bloodspiller = 7392;

    private static readonly uint[] TrackedActions =
    {
        HardSlash,
        SyphonStrike,
        Souleater,
        Bloodspiller
    };

    public string Id => "drk-priority-practice";

    public string Name => "DRK Endless Priority Practice";

    public string Job => "DRK";

    public int? ExpectedLength => null;

    public IReadOnlyCollection<uint> TrackedActionIds => TrackedActions;

    public TrainingDecision Evaluate(TrainingState state)
    {
        var nextComboAction = GetNextComboAction(state);
        var blood = state.GetGauge("blood");
        var canUseBloodspiller = state.Level >= 62;

        if (canUseBloodspiller &&
            nextComboAction == Souleater &&
            blood > 80)
        {
            return CreateDecision(
                Bloodspiller,
                "Spend Blood before Souleater would overcap the gauge.");
        }

        if (canUseBloodspiller && blood >= 90)
        {
            return CreateDecision(
                Bloodspiller,
                "Blood is near the cap. Bloodspiller is preferred, " +
                "but continuing the combo is still safe for this GCD.",
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

        return CreateDecision(nextComboAction, reason);
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
        params uint[] acceptableActionIds)
    {
        return new TrainingDecision
        {
            PreferredActionId = preferredActionId,
            AcceptableActionIds = acceptableActionIds,
            Reason = reason,
            MistakeResponse = TrainingMistakeResponse.KeepProgress
        };
    }
}
