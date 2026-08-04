using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class DarkKnightComboPolicy : ITrainingPolicy
{
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;

    public string Id => "drk-core-combo-practice";

    public string Name => "DRK Endless Core Combo";

    public string Job => "DRK";

    public int? ExpectedLength => null;

    public TrainingDecision Evaluate(TrainingState state)
    {
        return state.LastAcceptedActionId switch
        {
            HardSlash => CreateDecision(
                SyphonStrike,
                "Continue the combo with Syphon Strike."),

            SyphonStrike => CreateDecision(
                Souleater,
                "Finish the combo with Souleater."),

            _ => CreateDecision(
                HardSlash,
                "Begin a new combo with Hard Slash.")
        };
    }

    private static TrainingDecision CreateDecision(
        uint preferredActionId,
        string reason)
    {
        return new TrainingDecision
        {
            PreferredActionId = preferredActionId,
            Reason = reason,
            MistakeResponse = TrainingMistakeResponse.ResetProgress
        };
    }
}
