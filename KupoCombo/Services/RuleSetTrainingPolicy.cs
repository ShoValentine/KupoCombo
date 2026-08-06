using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class RuleSetTrainingPolicy :
    ITrainingPolicy,
    ITrainingForecastPolicy,
    IPracticePlanPolicy
{
    private const float AssumedGcdSeconds = 2.5f;
    private const float AssumedComboSeconds = 30f;
    private const int MaximumForecastWeavesPerWindow = 2;
    private const int MaximumForecastGcds = 256;

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

    public IReadOnlyCollection<string> TrackedResources =>
        Definition.StateInputs
            .Where(item =>
                item.Value.Kind == PolicyStateValueKind.Resource &&
                item.Value.TrackTransactions)
            .Select(item => item.Key)
            .ToArray();

    public bool IgnoreUntrackedActions => true;

    public TrainingDecision Evaluate(TrainingState state)
    {
        return EvaluateCore(state).Decision;
    }

    public IReadOnlyList<TrainingForecastStep> Forecast(
        TrainingState state,
        int maximumGcds)
    {
        return ForecastCore(state, maximumGcds, null);
    }

    public PracticePlan BuildPracticePlan(TrainingState state)
    {
        var horizonSeconds = Math.Max(
            30,
            Definition.Profile.BurstCycleSeconds);
        var steps = ForecastCore(
            state,
            MaximumForecastGcds,
            horizonSeconds);

        return new PracticePlan
        {
            Job = Job,
            StartsAtCombatTimeSeconds = state.CombatTimeSeconds,
            HorizonSeconds = horizonSeconds,
            TimingProfile = state.TimingProfile.Clone(),
            Steps = steps
        };
    }

    public IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
        uint actionId,
        TrainingState state)
    {
        var actionAlias = FindActionAlias(actionId, state);

        if (string.IsNullOrWhiteSpace(actionAlias))
        {
            return new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
        }

        var before = CaptureResourceValues(state);
        var simulatedState = state.Clone();
        ApplyForecastActionEffects(simulatedState, actionAlias);
        var after = CaptureResourceValues(simulatedState);
        var deltas = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var resource in before.Keys)
        {
            var delta = after[resource] - before[resource];

            if (delta != 0)
            {
                deltas[resource] = delta;
            }
        }

        return deltas;
    }

    private IReadOnlyList<TrainingForecastStep> ForecastCore(
        TrainingState state,
        int maximumGcds,
        double? horizonSeconds)
    {
        var stepLimit = Math.Clamp(maximumGcds, 1, MaximumForecastGcds);
        var simulatedState = state.Clone();
        var planStartSeconds = simulatedState.CombatTimeSeconds;
        var forecast = new List<TrainingForecastStep>(stepLimit);

        for (var offset = 0; offset < stepLimit; offset++)
        {
            var startsAtSeconds =
                simulatedState.CombatTimeSeconds - planStartSeconds;

            if (horizonSeconds.HasValue &&
                startsAtSeconds >= horizonSeconds.Value)
            {
                break;
            }

            var initialEvaluation = EvaluateCore(simulatedState);

            if (initialEvaluation.Decision.IsComplete)
            {
                break;
            }

            var phase = DetermineRotationPhase(simulatedState);
            var resourcesBefore = CaptureResourceValues(simulatedState);
            var appliedWeaves = ApplyForecastWeaveWindow(simulatedState);
            var evaluation = EvaluateCore(simulatedState);

            if (evaluation.Decision.IsComplete ||
                evaluation.GcdMatch == null)
            {
                break;
            }

            var weaveOverflow =
                initialEvaluation.WeaveMatches.Count > appliedWeaves.Count;
            var confidence = Math.Clamp(
                1f -
                (offset * 0.015f) -
                (weaveOverflow ? 0.08f : 0f),
                0.35f,
                1f);
            var elapsedSeconds = ApplyForecastTransition(
                simulatedState,
                evaluation.GcdMatch,
                isGcd: true);
            var resourcesAfter = CaptureResourceValues(simulatedState);

            forecast.Add(
                new TrainingForecastStep
                {
                    Offset = offset,
                    StartsAtSeconds = startsAtSeconds,
                    DurationSeconds = elapsedSeconds,
                    Phase = phase,
                    GcdActionId = evaluation.Decision.PreferredActionId,
                    SuggestedActionIds = appliedWeaves,
                    ResourceProjections = BuildResourceProjections(
                        resourcesBefore,
                        resourcesAfter),
                    Reason = evaluation.Decision.Reason,
                    SuggestionReason = string.Join(
                        " ",
                        appliedWeaves.Select(actionId =>
                            initialEvaluation.WeaveMatches
                                .FirstOrDefault(match => match.ActionId == actionId)
                                ?.Reason ?? string.Empty)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))),
                    Confidence = confidence
                });
        }

        return forecast;
    }

    private IReadOnlyList<uint> ApplyForecastWeaveWindow(
        TrainingState state)
    {
        var appliedActionIds = new List<uint>();
        var initialEvaluation = EvaluateCore(state);

        if (initialEvaluation.GcdMatch == null)
        {
            return appliedActionIds;
        }

        var maximumWeaves = GetMaximumForecastWeaves(
            state,
            initialEvaluation.GcdMatch);

        for (var slot = 0; slot < maximumWeaves; slot++)
        {
            var evaluation = EvaluateCore(state);

            if (evaluation.GcdMatch == null)
            {
                break;
            }

            var match = evaluation.WeaveMatches.FirstOrDefault(candidate =>
                !appliedActionIds.Contains(candidate.ActionId) &&
                CanScheduleWeaveBeforeGcd(
                    candidate,
                    evaluation.GcdMatch));

            if (match == null)
            {
                break;
            }

            appliedActionIds.Add(match.ActionId);
            ApplyForecastTransition(state, match, isGcd: false);
        }

        return appliedActionIds;
    }

    private int GetMaximumForecastWeaves(
        TrainingState state,
        RuleMatch gcdMatch)
    {
        var gcdSeconds = GetForecastElapsedSeconds(state, gcdMatch);

        return gcdSeconds < 2f
            ? 1
            : MaximumForecastWeavesPerWindow;
    }

    private bool CanScheduleWeaveBeforeGcd(
        RuleMatch weaveMatch,
        RuleMatch gcdMatch)
    {
        var weaveAction = context.GetAction(weaveMatch.ActionAlias);

        if (weaveAction.ExcludedNextGcdActions.Any(alias =>
                alias.Equals(
                    gcdMatch.ActionAlias,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!weaveAction.MinimumNextGcdPotency.HasValue)
        {
            return true;
        }

        var gcdAction = context.GetAction(gcdMatch.ActionAlias);
        var effectivePotency = Math.Max(
            gcdAction.Potency ?? 0,
            gcdAction.ComboPotency ?? 0);

        return effectivePotency >=
            weaveAction.MinimumNextGcdPotency.Value;
    }

    private EvaluationResult EvaluateCore(TrainingState state)
    {
        if (!IsProfileApplicable(state))
        {
            return new EvaluationResult(
                TrainingDecision.Complete(
                    $"Policy '{Definition.Name}' does not apply to the current level or target count."),
                null,
                Array.Empty<RuleMatch>());
        }

        RuleMatch? gcdMatch = null;
        var weaveMatches = new List<RuleMatch>();
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
                    weaveMatches.Add(match);
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
            return new EvaluationResult(
                TrainingDecision.Complete(
                    "No GCD rule matched the current training state."),
                null,
                weaveMatches);
        }

        return new EvaluationResult(
            new TrainingDecision
            {
                PreferredActionId = gcdMatch.ActionId,
                AcceptableActionIds = gcdMatch.AcceptableActionIds,
                SuggestedActionIds = suggestedActions,
                Reason = gcdMatch.Reason,
                SuggestionReason = string.Join(" ", suggestionReasons),
                MistakeResponse = gcdMatch.MistakeResponse
            },
            gcdMatch,
            weaveMatches);
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
                !context.IsActionAvailable(alias, state.Level) ||
                !CanUseActionWithinResourceReserves(rule, alias, state))
            {
                continue;
            }

            match = CreateMatch(
                rule,
                alias,
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

        if (!context.IsActionAvailable(actionAlias, state.Level) ||
            !CanUseActionWithinResourceReserves(rule, actionAlias, state))
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
            actionAlias,
            context.GetActionId(actionAlias, state),
            acceptableActions);
        return true;
    }

    private bool CanUseActionWithinResourceReserves(
        PolicyRuleDefinition rule,
        string actionAlias,
        TrainingState state)
    {
        if (DetermineRotationPhase(state) != RotationPhase.Pooling ||
            rule.AllowBelowResourceReserve ||
            rule.Type == PolicyRuleType.PreventResourceOvercap)
        {
            return true;
        }

        var guardedResources = Definition.StateInputs
            .Where(item =>
                item.Value.Kind == PolicyStateValueKind.Resource &&
                item.Value.PoolingReserve.HasValue)
            .ToArray();

        if (guardedResources.Length == 0)
        {
            return true;
        }

        var simulatedState = state.Clone();
        ApplyForecastActionEffects(simulatedState, actionAlias);

        foreach (var (resource, definition) in guardedResources)
        {
            var before = context.GetStateValue(resource, state);
            var after = context.GetStateValue(resource, simulatedState);
            var reserve = definition.PoolingReserve!.Value;

            if (after < before && after < reserve)
            {
                return false;
            }
        }

        return true;
    }

    private RuleMatch CreateMatch(
        PolicyRuleDefinition rule,
        string actionAlias,
        uint actionId,
        IReadOnlyList<uint> acceptableActions)
    {
        var filteredAcceptableActions = acceptableActions
            .Where(candidate => candidate != 0 && candidate != actionId)
            .Distinct()
            .ToArray();

        return new RuleMatch(
            rule,
            actionAlias,
            actionId,
            filteredAcceptableActions,
            !string.IsNullOrWhiteSpace(rule.Reason)
                ? rule.Reason
                : !string.IsNullOrWhiteSpace(rule.SuggestionReason)
                    ? rule.SuggestionReason
                    : $"Rule '{rule.Id}' selected this action.",
            rule.MistakeResponse);
    }

    private float ApplyForecastTransition(
        TrainingState state,
        RuleMatch match,
        bool isGcd)
    {
        if (isGcd)
        {
            state.RecordAcceptedAction(match.ActionId);
        }
        else
        {
            state.RecordObservedAction(match.ActionId);
        }

        ConsumeForecastCooldown(state, match);
        ApplyForecastRuleEffect(state, match);
        ApplyForecastActionEffects(state, match.ActionAlias);

        if (!isGcd)
        {
            return 0f;
        }

        var elapsedSeconds = GetForecastElapsedSeconds(state, match);
        UpdateForecastCombo(state, match.ActionId);
        AdvanceForecastTimers(state, elapsedSeconds);
        state.AdvanceForecastTime(elapsedSeconds);
        return elapsedSeconds;
    }

    private float GetForecastElapsedSeconds(
        TrainingState state,
        RuleMatch match)
    {
        var action = context.GetAction(match.ActionAlias);
        var fallbackSeconds = action.TimelineLockSeconds > 0d
            ? action.TimelineLockSeconds
            : action.RecastSeconds;

        if (fallbackSeconds <= 0d)
        {
            fallbackSeconds = AssumedGcdSeconds;
        }

        var adjustedSeconds = state.GetAdjustedRecastSeconds(
            match.ActionId,
            state.GetAdjustedRecastSeconds(
                action.ActionId,
                (float)fallbackSeconds));

        return Math.Clamp(adjustedSeconds, 0.5f, 10f);
    }

    private RotationPhase DetermineRotationPhase(TrainingState state)
    {
        var profile = Definition.Profile;
        var combatTime = Math.Max(0d, state.CombatTimeSeconds);

        if (combatTime < Math.Max(0, profile.OpenerDurationSeconds))
        {
            return RotationPhase.Opener;
        }

        var cycleSeconds = profile.MinorBurstCycleSeconds > 0
            ? profile.MinorBurstCycleSeconds
            : Math.Max(1, profile.BurstCycleSeconds);
        var pointInCycle = combatTime % cycleSeconds;
        var burstWindow = Math.Clamp(
            profile.BurstWindowSeconds,
            0,
            cycleSeconds);
        var poolingWindow = Math.Clamp(
            profile.PoolingWindowSeconds,
            0,
            cycleSeconds);

        if (pointInCycle < burstWindow)
        {
            return RotationPhase.Burst;
        }

        if (poolingWindow > 0 &&
            pointInCycle >= cycleSeconds - poolingWindow)
        {
            return RotationPhase.Pooling;
        }

        return RotationPhase.Filler;
    }

    private void ConsumeForecastCooldown(
        TrainingState state,
        RuleMatch match)
    {
        state.ConsumeCooldown(match.ActionId);

        var action = context.GetAction(match.ActionAlias);

        if (string.IsNullOrWhiteSpace(action.AdjustedFrom))
        {
            return;
        }

        state.ConsumeCooldown(
            context.GetAction(action.AdjustedFrom).ActionId);
    }

    private void ApplyForecastRuleEffect(
        TrainingState state,
        RuleMatch match)
    {
        switch (match.Rule.Type)
        {
            case PolicyRuleType.FollowProc:
                state.RemoveStatus(
                    context.GetStatusId(match.Rule.Status));
                break;

            case PolicyRuleType.SpendStatusStacks:
                state.DecrementStatusStacks(
                    context.GetStatusId(match.Rule.Status));
                break;

            case PolicyRuleType.FollowAdjustedAction:
                ResetForecastAdjustedAction(state, match.ActionAlias);
                break;

            case PolicyRuleType.PreventResourceOvercap:
                ApplyFallbackResourceSpend(state, match);
                break;
        }
    }

    private void ApplyFallbackResourceSpend(
        TrainingState state,
        RuleMatch match)
    {
        if (string.IsNullOrWhiteSpace(match.Rule.Resource))
        {
            return;
        }

        var actionModelsResource = context
            .GetAction(match.ActionAlias)
            .ForecastEffects
            .Any(effect =>
                (effect.Type == PolicyForecastEffectType.AddStateValue ||
                 effect.Type == PolicyForecastEffectType.SetStateValue) &&
                effect.State.Equals(
                    match.Rule.Resource,
                    StringComparison.OrdinalIgnoreCase) &&
                conditionEvaluator.Matches(effect.Conditions, state));

        if (!actionModelsResource)
        {
            LowerForecastResource(state, match.Rule);
        }
    }

    private void ApplyForecastActionEffects(
        TrainingState state,
        string actionAlias)
    {
        foreach (var effect in context.GetAction(actionAlias).ForecastEffects)
        {
            if (!conditionEvaluator.Matches(effect.Conditions, state))
            {
                continue;
            }

            switch (effect.Type)
            {
                case PolicyForecastEffectType.AddStateValue:
                    ApplyForecastStateValue(state, effect, add: true);
                    break;

                case PolicyForecastEffectType.SetStateValue:
                    ApplyForecastStateValue(state, effect, add: false);
                    break;

                case PolicyForecastEffectType.AddStatus:
                    state.SetStatus(
                        context.GetStatusId(effect.Status),
                        Math.Max(1, effect.Stacks),
                        effect.DurationSeconds);
                    break;

                case PolicyForecastEffectType.RemoveStatus:
                    state.RemoveStatus(context.GetStatusId(effect.Status));
                    break;

                case PolicyForecastEffectType.SetAdjustedAction:
                    state.SetAdjustedAction(
                        context.GetAction(effect.Action).ActionId,
                        context.GetAction(effect.AdjustedAction).ActionId);
                    break;

                case PolicyForecastEffectType.ResetAdjustedAction:
                    var baseAction = context.GetAction(effect.Action);
                    state.SetAdjustedAction(
                        baseAction.ActionId,
                        baseAction.ActionId);
                    break;
            }
        }
    }

    private void ApplyForecastStateValue(
        TrainingState state,
        PolicyForecastEffectDefinition effect,
        bool add)
    {
        var value = add
            ? context.GetStateValue(effect.State, state) + effect.Value
            : effect.Value;

        if (effect.Minimum.HasValue)
        {
            value = Math.Max(effect.Minimum.Value, value);
        }

        if (effect.Maximum.HasValue)
        {
            value = Math.Min(effect.Maximum.Value, value);
        }

        context.SetStateValue(effect.State, state, value);
    }

    private Dictionary<string, int> CaptureResourceValues(
        TrainingState state)
    {
        return Definition.StateInputs
            .Where(item => item.Value.Kind == PolicyStateValueKind.Resource)
            .ToDictionary(
                item => item.Key,
                item => (int)Math.Round(
                    context.GetStateValue(item.Key, state)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, ResourceProjection>
        BuildResourceProjections(
            IReadOnlyDictionary<string, int> before,
            IReadOnlyDictionary<string, int> after)
    {
        var projections = new Dictionary<string, ResourceProjection>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (resource, beforeValue) in before)
        {
            projections[resource] = new ResourceProjection
            {
                Resource = resource,
                Before = beforeValue,
                After = after.TryGetValue(resource, out var afterValue)
                    ? afterValue
                    : beforeValue
            };
        }

        return projections;
    }

    private string FindActionAlias(
        uint actionId,
        TrainingState state)
    {
        foreach (var (alias, action) in Definition.Actions)
        {
            if (action.ActionId == actionId ||
                context.GetActionId(alias, state) == actionId)
            {
                return alias;
            }
        }

        return string.Empty;
    }

    private void AdvanceForecastTimers(
        TrainingState state,
        float elapsedSeconds)
    {
        foreach (var (alias, input) in Definition.StateInputs)
        {
            if (input.Kind != PolicyStateValueKind.Timer)
            {
                continue;
            }

            var elapsed = input.Unit.Equals(
                "milliseconds",
                StringComparison.OrdinalIgnoreCase)
                ? elapsedSeconds * 1000d
                : elapsedSeconds;

            context.SetStateValue(
                alias,
                state,
                Math.Max(0d, context.GetStateValue(alias, state) - elapsed));
        }
    }

    private void ResetForecastAdjustedAction(
        TrainingState state,
        string actionAlias)
    {
        var action = context.GetAction(actionAlias);

        if (string.IsNullOrWhiteSpace(action.AdjustedFrom))
        {
            return;
        }

        var baseAction = context.GetAction(action.AdjustedFrom);
        state.SetAdjustedAction(baseAction.ActionId, baseAction.ActionId);
    }

    private void LowerForecastResource(
        TrainingState state,
        PolicyRuleDefinition rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Resource))
        {
            return;
        }

        var current = context.GetStateValue(rule.Resource, state);
        var threshold = rule.Threshold ?? current;
        var incomingGain = Math.Max(1d, rule.IncomingGain ?? 1d);
        var predicted = Math.Max(
            0d,
            Math.Min(current, threshold - incomingGain));

        context.SetStateValue(rule.Resource, state, predicted);
    }

    private void UpdateForecastCombo(
        TrainingState state,
        uint actionId)
    {
        foreach (var combo in Definition.Combos.Values)
        {
            foreach (var actionAlias in combo.Steps)
            {
                var action = context.GetAction(actionAlias);
                var resolvedActionId = context.GetActionId(
                    actionAlias,
                    state);

                if (actionId != action.ActionId &&
                    actionId != resolvedActionId)
                {
                    continue;
                }

                state.SetCombo(actionId, AssumedComboSeconds);
                return;
            }
        }
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
        PolicyRuleDefinition Rule,
        string ActionAlias,
        uint ActionId,
        IReadOnlyList<uint> AcceptableActionIds,
        string Reason,
        TrainingMistakeResponse MistakeResponse);

    private sealed record EvaluationResult(
        TrainingDecision Decision,
        RuleMatch? GcdMatch,
        IReadOnlyList<RuleMatch> WeaveMatches);
}
