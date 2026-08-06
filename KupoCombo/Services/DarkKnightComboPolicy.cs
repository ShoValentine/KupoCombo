using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class DarkKnightComboPolicy : ITrainingPolicy
{
    private readonly RuleSetTrainingPolicy inner;

    public DarkKnightComboPolicy()
        : this(
            RulePolicyRuntimeLoader.LoadBestProfile(
                "DRK",
                Plugin.PlayerState.IsLoaded
                    ? Plugin.PlayerState.EffectiveLevel
                    : 0))
    {
    }

    internal DarkKnightComboPolicy(RulePolicyDefinition definition)
    {
        inner = new RuleSetTrainingPolicy(definition);
    }

    public RulePolicyDefinition Definition => inner.Definition;

    public string Id => inner.Id;

    public string Name => inner.Name;

    public string Job => inner.Job;

    public int? ExpectedLength => inner.ExpectedLength;

    public IReadOnlyCollection<uint> TrackedActionIds =>
        inner.TrackedActionIds;

    public IReadOnlyCollection<uint> AdvisoryActionIds =>
        inner.AdvisoryActionIds;

    public bool IgnoreUntrackedActions => inner.IgnoreUntrackedActions;

    public TrainingDecision Evaluate(TrainingState state)
    {
        if (state.Level == 0)
        {
            state.SetLevel(Definition.MinimumLevel);
        }

        return inner.Evaluate(state);
    }
}
