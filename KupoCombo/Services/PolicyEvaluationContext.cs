using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

internal sealed class PolicyEvaluationContext
{
    private readonly Dictionary<string, PolicyActionDefinition> actions;
    private readonly Dictionary<string, uint> statuses;
    private readonly Dictionary<string, PolicyStateInputDefinition> stateInputs;
    private readonly Dictionary<string, PolicyComboDefinition> combos;

    public PolicyEvaluationContext(RulePolicyDefinition definition)
    {
        Definition = definition;
        actions = new Dictionary<string, PolicyActionDefinition>(
            definition.Actions,
            StringComparer.OrdinalIgnoreCase);
        statuses = new Dictionary<string, uint>(
            definition.Statuses,
            StringComparer.OrdinalIgnoreCase);
        stateInputs = new Dictionary<string, PolicyStateInputDefinition>(
            definition.StateInputs,
            StringComparer.OrdinalIgnoreCase);
        combos = new Dictionary<string, PolicyComboDefinition>(
            definition.Combos,
            StringComparer.OrdinalIgnoreCase);
    }

    public RulePolicyDefinition Definition { get; }

    public IReadOnlyCollection<uint> TrackedActionIds => actions
        .Values
        .Where(action => action.Role == PolicyActionRole.Graded)
        .Select(action => action.ActionId)
        .Distinct()
        .ToArray();

    public IReadOnlyCollection<uint> AdvisoryActionIds => actions
        .Values
        .Where(action => action.Role != PolicyActionRole.Graded)
        .Select(action => action.ActionId)
        .Distinct()
        .ToArray();

    public PolicyActionDefinition GetAction(string alias)
    {
        return actions.TryGetValue(alias, out var action)
            ? action
            : throw new InvalidOperationException(
                $"Policy '{Definition.Id}' does not define action '{alias}'.");
    }

    public uint GetActionId(
        string alias,
        TrainingState state,
        bool resolveAdjustedAlias = true)
    {
        var action = GetAction(alias);

        if (resolveAdjustedAlias &&
            !string.IsNullOrWhiteSpace(action.AdjustedFrom))
        {
            var baseAction = GetAction(action.AdjustedFrom);
            return state.GetAdjustedAction(
                baseAction.ActionId,
                action.ActionId);
        }

        return action.ActionId;
    }

    public bool IsActionAvailable(string alias, int level)
    {
        var action = GetAction(alias);

        return level >= action.MinimumLevel &&
            (!action.MaximumLevel.HasValue ||
             level <= action.MaximumLevel.Value);
    }

    public uint GetStatusId(string alias)
    {
        return statuses.TryGetValue(alias, out var statusId)
            ? statusId
            : throw new InvalidOperationException(
                $"Policy '{Definition.Id}' does not define status '{alias}'.");
    }

    public PolicyComboDefinition GetCombo(string alias)
    {
        return combos.TryGetValue(alias, out var combo)
            ? combo
            : throw new InvalidOperationException(
                $"Policy '{Definition.Id}' does not define combo '{alias}'.");
    }

    public double GetStateValue(string alias, TrainingState state)
    {
        if (!stateInputs.TryGetValue(alias, out var input))
        {
            throw new InvalidOperationException(
                $"Policy '{Definition.Id}' does not define state input '{alias}'.");
        }

        if (state.TryGetStateValue(alias, out var value))
        {
            return value;
        }

        if (state.TryGetStateValue(input.Provider, out value))
        {
            return value;
        }

        var separator = input.Provider.LastIndexOf('.');
        var providerLeaf = separator >= 0
            ? input.Provider[(separator + 1)..]
            : input.Provider;

        return state.GetStateValue(providerLeaf);
    }

    public double? GetStateMaximum(string alias)
    {
        return stateInputs.TryGetValue(alias, out var input)
            ? input.Maximum
            : null;
    }

    public CooldownSnapshot? GetCooldown(
        string actionAlias,
        TrainingState state)
    {
        return state.GetCooldown(GetAction(actionAlias).ActionId);
    }

    public uint ResolveActionValue(JsonComparable value, TrainingState state)
    {
        if (value.ActionAlias != null)
        {
            return GetActionId(value.ActionAlias, state);
        }

        return value.Number.HasValue
            ? Convert.ToUInt32(value.Number.Value)
            : 0;
    }
}

internal readonly record struct JsonComparable(
    double? Number,
    bool? Boolean,
    string? Text,
    string? ActionAlias);
