using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class RuleSetTrainingPolicy : ITrainingPolicy
{
    private readonly PolicyEvaluationContext context;
    private readonly PolicyConditionEvaluator conditionEvaluator;
    private readonly IReadOnlyList<PolicyRuleDefinition> orderedRules;

    public RuleSetTrainingPolicy(RulePolicyDefinition definition)
    {
        Definition = definition;
        context = new PolicyEvaluationContext(definition);
        conditionEvaluator = new PolicyConditionEvaluator(context);
        orderedRules = definition.Rules
            .Select((rule, index) => new { rule, index })
            .OrderByDescending(item => item.rule.Priority)
            .ThenBy(item => item.index)
            .Select(item => item.rule)
            .ToArray();
    }

    public RulePolicyDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => $"{Definition.Job} — {Definition.Name}";

    public string Job => Definition.Job;

    public int? ExpectedLength => null;

    public IReadOnlyCollection<uint> TrackedActionIds =>
        context.TrackedActionIds;

    public IReadOnlyCollection<uint> AdvisoryActionIds =>
        context.AdvisoryActionIds;

    public bool IgnoreUntrackedActions => true;

    public TrainingDecision Evaluate(TrainingState state)
    {
        if (!IsProfileApplicable(state))
        {
            return TrainingDecision.Complete(
                $"Policy '{Definition.Name}' does not apply to the current level or target count.");
        }

        RuleMatch? gcdMatch = null;
        var suggestedActions = new List<uint>();
        var suggestionReasons = new List<string>();

        foreach (var rule in orderedRules)
        {
            if (!rule.Enabled ||
                !conditionEvaluator.Matches(rule.Conditions, state) ||
                !TryEvaluateRule(rule, state, out var match))
            {
                continue;
            }

            if (rule.Lane == PolicyLane.Weave)
            {
                if (!suggestedActions.Contains(match.ActionId))
                {
                    suggestedActions.Add(match.ActionId);
                }

                var suggestionReason = !string.IsNullOrWhiteSpace(rule.SuggestionReason)
                    ? rule.SuggestionReason
                    : match.Reason;

                if (!string.IsNullOrWhiteSpace(suggestionReason) &&
                    !suggestionReasons.Contains(suggestionReason))
                {
                    suggestionReasons.Add(suggestionReason);
                }

                continue;
            }

            gcdMatch ??= match;
        }

        if (gcdMatch == null)
        {
            return TrainingDecision.Complete(
                "No GCD rule matched the current training state.");
        }

        return new TrainingDecision
        {
            PreferredActionId = gcdMatch.ActionId,
            AcceptableActionIds = gcdMatch.AcceptableActionIds,
            SuggestedActionIds = suggestedActions,
            Reason = gcdMatch.Reason,
            SuggestionReason = string.Join(" ", suggestionReasons),
            MistakeResponse = gcdMatch.MistakeResponse
        };
    }

    private bool IsProfileApplicable(TrainingState state)
    {
        return state.Level >= Definition.MinimumLevel &&
            (!Definition.MaximumLevel.HasValue ||
             state.Level <= Definition.MaximumLevel.Value) &&
            state.TargetCount >= Definition.Profile.MinimumTargetCount &&
            state.TargetCount <= Definition.Profile.MaximumTargetCount;
    }

    private bool TryEvaluateRule(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;

        return rule.Type switch
        {
            PolicyRuleType.ContinueCombo =>
                TryContinueCombo(rule, state, out match),
            PolicyRuleType.FollowAdjustedAction =>
                TryFollowAdjustedAction(rule, state, out match),
            PolicyRuleType.PreventResourceOvercap =>
                TryPreventResourceOvercap(rule, state, out match),
            PolicyRuleType.PreventChargeOvercap =>
                TryPreventChargeOvercap(rule, state, out match),
            PolicyRuleType.MaintainStatus =>
                TryMaintainStatus(rule, state, out match),
            PolicyRuleType.SpendStatusStacks =>
                TrySpendStatusStacks(rule, state, out match),
            PolicyRuleType.FollowProc =>
                TryFollowProc(rule, state, out match),
            PolicyRuleType.UseCooldown =>
                TryUseCooldown(rule, state, out match),
            PolicyRuleType.UseAction =>
                TryUseAction(rule, state, out match),
            _ => false
        };
    }

    private bool TryContinueCombo(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;
        var combo = context.GetCombo(rule.Combo);

        if (state.Level < combo.MinimumLevel ||
            !TryGetNextComboAction(rule.Combo, state, out var nextAlias))
        {
            return false;
        }

        return TryCreateMatch(rule, nextAlias, state, out match);
    }

    private bool TryFollowAdjustedAction(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;
        var baseAction = context.GetAction(rule.Action);
        var adjustedActionId = state.GetAdjustedAction(
            baseAction.ActionId,
            baseAction.ActionId);

        foreach (var alias in rule.AdjustedActions)
        {
            var action = context.GetAction(alias);

            if (action.ActionId != adjustedActionId ||
                !context.IsActionAvailable(alias, state.Level))
            {
                continue;
            }

            match = CreateMatch(
                rule,
                adjustedActionId,
                Array.Empty<uint>());
            return true;
        }

        return false;
    }

    private bool TryPreventResourceOvercap(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;
        var currentValue = context.GetStateValue(rule.Resource, state);
        var threshold = rule.Threshold ?? double.MaxValue;
        var nextComboAlias = FindNextComboAction(state);
        var incomingActionIsNext =
            !string.IsNullOrWhiteSpace(rule.IncomingAction) &&
            !string.IsNullOrWhiteSpace(nextComboAlias) &&
            rule.IncomingAction.Equals(
                nextComboAlias,
                StringComparison.OrdinalIgnoreCase);

        var maximum = context.GetStateMaximum(rule.Resource);
        var wouldOvercap = incomingActionIsNext &&
            rule.IncomingGain.HasValue &&
            maximum.HasValue &&
            currentValue + rule.IncomingGain.Value > maximum.Value;

        if (currentValue < threshold && !wouldOvercap)
        {
            return false;
        }

        var dynamicAcceptableActions = new List<uint>();

        if (!wouldOvercap &&
            !string.IsNullOrWhiteSpace(nextComboAlias) &&
            context.IsActionAvailable(nextComboAlias, state.Level))
        {
            dynamicAcceptableActions.Add(
                context.GetActionId(nextComboAlias, state));
        }

        return TryCreateMatch(
            rule,
            rule.Action,
            state,
            out match,
            dynamicAcceptableActions);
    }

    private bool TryPreventChargeOvercap(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;
        var cooldown = context.GetCooldown(rule.Cooldown, state);

        if (cooldown == null ||
            cooldown.Charges < (rule.Threshold ?? cooldown.MaximumCharges))
        {
            return false;
        }

        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TryMaintainStatus(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;
        var status = state.GetStatus(context.GetStatusId(rule.Status));
        var minimumRemaining = rule.MinimumRemainingSeconds ?? 0d;

        if (status != null && status.RemainingSeconds > minimumRemaining)
        {
            return false;
        }

        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TrySpendStatusStacks(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;

        if (state.GetStatusStacks(context.GetStatusId(rule.Status)) <= 0)
        {
            return false;
        }

        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TryFollowProc(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;

        if (!state.HasStatus(context.GetStatusId(rule.Status)))
        {
            return false;
        }

        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TryUseCooldown(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        match = default!;

        if (context.GetCooldown(rule.Cooldown, state)?.IsReady != true)
        {
            return false;
        }

        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TryUseAction(
        PolicyRuleDefinition rule,
        TrainingState state,
        out RuleMatch match)
    {
        return TryCreateMatch(rule, rule.Action, state, out match);
    }

    private bool TryCreateMatch(
        PolicyRuleDefinition rule,
        string actionAlias,
        TrainingState state,
        out RuleMatch match,
        IEnumerable<uint>? dynamicAcceptableActions = null)
    {
        match = default!;

        if (!context.IsActionAvailable(actionAlias, state.Level))
        {
            return false;
        }

        var acceptableActions = new List<uint>();

        foreach (var acceptableAlias in rule.AcceptableActions)
        {
            if (context.IsActionAvailable(acceptableAlias, state.Level))
            {
                AddUnique(
                    acceptableActions,
                    context.GetActionId(acceptableAlias, state));
            }
        }

        if (dynamicAcceptableActions != null)
        {
            foreach (var actionId in dynamicAcceptableActions)
            {
                AddUnique(acceptableActions, actionId);
            }
        }

        match = CreateMatch(
            rule,
            context.GetActionId(actionAlias, state),
            acceptableActions);
        return true;
    }

    private RuleMatch CreateMatch(
        PolicyRuleDefinition rule,
        uint actionId,
        IReadOnlyList<uint> acceptableActions)
    {
        var filteredAcceptableActions = acceptableActions
            .Where(candidate => candidate != 0 && candidate != actionId)
            .Distinct()
            .ToArray();

        return new RuleMatch(
            actionId,
            filteredAcceptableActions,
            !string.IsNullOrWhiteSpace(rule.Reason)
                ? rule.Reason
                : $"Rule '{rule.Id}' selected this action.",
            rule.MistakeResponse);
    }

    private string FindNextComboAction(TrainingState state)
    {
        foreach (var comboAlias in Definition.Combos.Keys)
        {
            if (TryGetNextComboAction(comboAlias, state, out var nextAlias))
            {
                return nextAlias;
            }
        }

        return string.Empty;
    }

    private bool TryGetNextComboAction(
        string comboAlias,
        TrainingState state,
        out string nextAlias)
    {
        nextAlias = string.Empty;
        var combo = context.GetCombo(comboAlias);

        if (combo.Steps.Count == 0)
        {
            return false;
        }

        if (state.ComboRemainingSeconds > 0f &&
            TryFindFollowingStep(
                combo,
                state.NativeComboActionId,
                out nextAlias))
        {
            return true;
        }

        if (TryFindFollowingStep(
                combo,
                state.LastAcceptedActionId,
                out nextAlias))
        {
            return true;
        }

        nextAlias = combo.Steps[0];
        return true;
    }

    private bool TryFindFollowingStep(
        PolicyComboDefinition combo,
        uint actionId,
        out string nextAlias)
    {
        nextAlias = string.Empty;

        for (var index = 0; index < combo.Steps.Count - 1; index++)
        {
            var stepActionId = context.GetAction(combo.Steps[index]).ActionId;

            if (stepActionId != actionId)
            {
                continue;
            }

            nextAlias = combo.Steps[index + 1];
            return true;
        }

        return false;
    }

    private static void AddUnique(ICollection<uint> values, uint value)
    {
        if (value != 0 && !values.Contains(value))
        {
            values.Add(value);
        }
    }

    private sealed record RuleMatch(
        uint ActionId,
        IReadOnlyList<uint> AcceptableActionIds,
        string Reason,
        TrainingMistakeResponse MistakeResponse);
}
