using System;
using System.Collections.Generic;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class PracticePlanValidator
{
    private const double TimeToleranceSeconds = 0.01d;
    private const int MaximumWeavesPerWindow = 2;
    private const float MinimumGcdDurationSeconds = 0.5f;
    private const float MaximumGcdDurationSeconds = 10f;

    public PlanValidationResult Validate(
        PlanValidationRequest request,
        ITrainingPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(request.State);

        var issues = new List<PlanValidationIssue>();
        var plan = request.Plan;
        var state = request.State;
        var rulePolicy = policy as RuleSetTrainingPolicy;

        ValidatePlanHeader(plan, state, policy, issues);

        if (plan.IsEmpty)
        {
            AddError(
                issues,
                PlanValidationCode.EmptyPlan,
                "A non-complete policy produced an empty practice plan.");

            return Result(issues);
        }

        ValidateSteps(plan, issues);
        ValidateCommitment(request, issues);
        ValidateResources(request, rulePolicy, issues);

        if (rulePolicy != null)
        {
            var usages = ValidateActions(plan, state, rulePolicy, issues);
            ValidateCooldowns(state, usages, issues);
        }

        return Result(issues);
    }

    private static void ValidatePlanHeader(
        PracticePlan plan,
        TrainingState state,
        ITrainingPolicy? policy,
        List<PlanValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(plan.Job))
        {
            AddError(
                issues,
                PlanValidationCode.MissingJob,
                "The practice plan does not declare a job.");
        }

        if (!string.IsNullOrWhiteSpace(state.Job) &&
            !plan.Job.Equals(state.Job, StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                issues,
                PlanValidationCode.JobMismatch,
                $"Plan job '{plan.Job}' does not match live job '{state.Job}'.");
        }

        if (policy != null &&
            !plan.Job.Equals(policy.Job, StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                issues,
                PlanValidationCode.JobMismatch,
                $"Plan job '{plan.Job}' does not match policy job '{policy.Job}'.");
        }

        if (!double.IsFinite(plan.StartsAtCombatTimeSeconds) ||
            plan.StartsAtCombatTimeSeconds < 0d)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidPlanStart,
                $"Plan start {plan.StartsAtCombatTimeSeconds} is not a valid combat time.");
        }

        if (!double.IsFinite(plan.HorizonSeconds) ||
            plan.HorizonSeconds <= 0d)
        {
            AddError(
                issues,
                PlanValidationCode.InvalidHorizon,
                $"Plan horizon {plan.HorizonSeconds} must be positive and finite.");
        }
    }

    private static void ValidateSteps(
        PracticePlan plan,
        List<PlanValidationIssue> issues)
    {
        var expectedStart = 0d;

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];

            if (step.Offset != index)
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidOffset,
                    $"Step {index} declares offset {step.Offset}; offsets must be contiguous.",
                    index);
            }

            if (!double.IsFinite(step.StartsAtSeconds) ||
                step.StartsAtSeconds < 0d ||
                Math.Abs(step.StartsAtSeconds - expectedStart) >
                TimeToleranceSeconds)
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidStepStart,
                    $"Step {index} starts at {step.StartsAtSeconds:0.###}s; expected {expectedStart:0.###}s.",
                    index);
            }

            if (!float.IsFinite(step.DurationSeconds) ||
                step.DurationSeconds < MinimumGcdDurationSeconds ||
                step.DurationSeconds > MaximumGcdDurationSeconds)
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidDuration,
                    $"Step {index} duration {step.DurationSeconds:0.###}s is outside the supported GCD range.",
                    index);
            }

            if (double.IsFinite(plan.HorizonSeconds) &&
                step.StartsAtSeconds >= plan.HorizonSeconds +
                TimeToleranceSeconds)
            {
                AddError(
                    issues,
                    PlanValidationCode.StepOutsideHorizon,
                    $"Step {index} begins beyond the {plan.HorizonSeconds:0.###}s plan horizon.",
                    index);
            }

            if (!Enum.IsDefined(step.Phase))
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidPhase,
                    $"Step {index} has undefined phase value {(int)step.Phase}.",
                    index);
            }

            if (!float.IsFinite(step.Confidence) ||
                step.Confidence < 0f ||
                step.Confidence > 1f)
            {
                AddError(
                    issues,
                    PlanValidationCode.InvalidConfidence,
                    $"Step {index} confidence {step.Confidence} must be between 0 and 1.",
                    index);
            }

            if (step.GcdActionId == 0)
            {
                AddError(
                    issues,
                    PlanValidationCode.MissingGcdAction,
                    $"Step {index} does not contain a GCD action.",
                    index);
            }

            if (step.SuggestedActionIds.Count > MaximumWeavesPerWindow)
            {
                AddError(
                    issues,
                    PlanValidationCode.TooManySuggestedActions,
                    $"Step {index} contains {step.SuggestedActionIds.Count} suggested actions; at most {MaximumWeavesPerWindow} fit in one window.",
                    index);
            }

            var seenSuggestions = new HashSet<uint>();

            foreach (var actionId in step.SuggestedActionIds)
            {
                if (actionId == 0 || actionId == step.GcdActionId)
                {
                    AddError(
                        issues,
                        PlanValidationCode.InvalidSuggestedAction,
                        $"Step {index} contains invalid suggested action {actionId}.",
                        index,
                        actionId);
                }

                if (!seenSuggestions.Add(actionId))
                {
                    AddError(
                        issues,
                        PlanValidationCode.DuplicateSuggestedAction,
                        $"Step {index} suggests action {actionId} more than once.",
                        index,
                        actionId);
                }
            }

            expectedStart += Math.Max(0f, step.DurationSeconds);
        }
    }

    private static void ValidateCommitment(
        PlanValidationRequest request,
        List<PlanValidationIssue> issues)
    {
        var committed = request.CommittedPlan;
        var requestedDepth = Math.Max(0, request.CommittedDepth);

        if (committed == null || requestedDepth == 0)
        {
            return;
        }

        if (request.Plan.Steps.Count < requestedDepth ||
            committed.Steps.Count < requestedDepth)
        {
            AddError(
                issues,
                PlanValidationCode.CommitmentChanged,
                $"The plan cannot preserve the requested {requestedDepth}-step commitment edge.");
            return;
        }

        for (var index = 0; index < requestedDepth; index++)
        {
            var expected = committed.Steps[index];
            var actual = request.Plan.Steps[index];

            if (expected.GcdActionId != actual.GcdActionId ||
                expected.Phase != actual.Phase ||
                !expected.SuggestedActionIds.SequenceEqual(
                    actual.SuggestedActionIds))
            {
                AddError(
                    issues,
                    PlanValidationCode.CommitmentChanged,
                    $"Step {index} changed inside the committed execution edge.",
                    index,
                    actual.GcdActionId);
            }
        }
    }

    private static void ValidateResources(
        PlanValidationRequest request,
        RuleSetTrainingPolicy? rulePolicy,
        List<PlanValidationIssue> issues)
    {
        var declaredResources = rulePolicy?.Definition.StateInputs
            .Where(item => item.Value.Kind == PolicyStateValueKind.Resource)
            .ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, PolicyStateInputDefinition>(
                StringComparer.OrdinalIgnoreCase);
        var previousAfter = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.Plan.Steps.Count; index++)
        {
            var step = request.Plan.Steps[index];

            foreach (var resource in declaredResources.Keys)
            {
                if (!step.ResourceProjections.ContainsKey(resource))
                {
                    AddError(
                        issues,
                        PlanValidationCode.MissingResourceProjection,
                        $"Step {index} is missing projection for resource '{resource}'.",
                        index);
                }
            }

            foreach (var (key, projection) in step.ResourceProjections)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(projection.Resource) ||
                    !key.Equals(
                        projection.Resource,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddError(
                        issues,
                        PlanValidationCode.ResourceNameMismatch,
                        $"Step {index} resource projection key '{key}' does not match its resource name '{projection.Resource}'.",
                        index);
                    continue;
                }

                if (declaredResources.Count > 0 &&
                    !declaredResources.ContainsKey(key))
                {
                    AddError(
                        issues,
                        PlanValidationCode.UnknownResourceProjection,
                        $"Step {index} projects undeclared resource '{key}'.",
                        index);
                }

                if (index == 0 &&
                    request.RequireStateOriginMatch &&
                    request.State.TryGetStateValue(key, out var liveValue) &&
                    projection.Before != (int)Math.Round(liveValue))
                {
                    AddError(
                        issues,
                        PlanValidationCode.ResourceOriginMismatch,
                        $"Resource '{key}' begins at {projection.Before}, but live state is {(int)Math.Round(liveValue)}.",
                        index);
                }

                if (previousAfter.TryGetValue(key, out var expectedBefore) &&
                    projection.Before != expectedBefore)
                {
                    AddError(
                        issues,
                        PlanValidationCode.ResourceDiscontinuity,
                        $"Resource '{key}' jumps from {expectedBefore} to {projection.Before} before step {index}.",
                        index);
                }

                if (declaredResources.TryGetValue(key, out var definition))
                {
                    ValidateResourceBound(
                        key,
                        projection.Before,
                        definition,
                        index,
                        issues);
                    ValidateResourceBound(
                        key,
                        projection.After,
                        definition,
                        index,
                        issues);
                }

                previousAfter[key] = projection.After;
            }
        }
    }

    private static void ValidateResourceBound(
        string resource,
        int value,
        PolicyStateInputDefinition definition,
        int stepOffset,
        List<PlanValidationIssue> issues)
    {
        if ((definition.Minimum.HasValue &&
             value < definition.Minimum.Value) ||
            (definition.Maximum.HasValue &&
             value > definition.Maximum.Value))
        {
            AddError(
                issues,
                PlanValidationCode.ResourceOutOfBounds,
                $"Resource '{resource}' reaches {value} outside its declared bounds " +
                $"[{definition.Minimum?.ToString() ?? "-∞"}, {definition.Maximum?.ToString() ?? "∞"}].",
                stepOffset);
        }
    }

    private static IReadOnlyList<ActionUsage> ValidateActions(
        PracticePlan plan,
        TrainingState state,
        RuleSetTrainingPolicy policy,
        List<PlanValidationIssue> issues)
    {
        var usages = new List<ActionUsage>();

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];

            foreach (var actionId in step.SuggestedActionIds)
            {
                ValidateAction(
                    policy,
                    state,
                    actionId,
                    PolicyLane.Weave,
                    index,
                    step.StartsAtSeconds,
                    index == 0,
                    usages,
                    issues);
            }

            ValidateAction(
                policy,
                state,
                step.GcdActionId,
                PolicyLane.Gcd,
                index,
                step.StartsAtSeconds,
                index == 0,
                usages,
                issues);
        }

        return usages;
    }

    private static void ValidateAction(
        RuleSetTrainingPolicy policy,
        TrainingState state,
        uint actionId,
        PolicyLane expectedLane,
        int stepOffset,
        double startsAtSeconds,
        bool validateCurrentAdjustment,
        List<ActionUsage> usages,
        List<PlanValidationIssue> issues)
    {
        if (actionId == 0)
        {
            return;
        }

        var resolved = ResolveAction(policy.Definition, state, actionId);

        if (resolved == null)
        {
            AddError(
                issues,
                PlanValidationCode.UnknownAction,
                $"Action {actionId} is not declared by policy '{policy.Id}'.",
                stepOffset,
                actionId);
            return;
        }

        var (alias, action) = resolved.Value;

        if (action.Lane != expectedLane)
        {
            AddError(
                issues,
                PlanValidationCode.WrongActionLane,
                $"Action {actionId} is declared for {action.Lane} but appears in the {expectedLane} lane.",
                stepOffset,
                actionId);
        }

        var roleIsInvalid = expectedLane == PolicyLane.Gcd
            ? action.Role != PolicyActionRole.Graded
            : action.Role == PolicyActionRole.Observed;

        if (roleIsInvalid)
        {
            AddError(
                issues,
                PlanValidationCode.UngradedAction,
                $"Action {actionId} has role {action.Role} and cannot appear in the {expectedLane} plan lane.",
                stepOffset,
                actionId);
        }

        if (state.Level < action.MinimumLevel ||
            (action.MaximumLevel.HasValue &&
             state.Level > action.MaximumLevel.Value))
        {
            AddError(
                issues,
                PlanValidationCode.ActionUnavailableAtLevel,
                $"Action {actionId} is unavailable at level {state.Level}.",
                stepOffset,
                actionId);
        }

        if (validateCurrentAdjustment)
        {
            ValidateAdjustedAction(
                policy.Definition,
                state,
                alias,
                action,
                actionId,
                stepOffset,
                issues);
        }

        // A transformed or proc follow-up is unlocked by state, not by spending
        // another charge from the action named by AdjustedFrom. Its own recast
        // describes whether it participates in cooldown scheduling.
        var rechargeSeconds = action.RecastSeconds;
        var maximumCharges = Math.Max(1, action.MaximumCharges);

        if (maximumCharges > 1 || rechargeSeconds > 5d)
        {
            usages.Add(
                new ActionUsage(
                    action.ActionId,
                    actionId,
                    startsAtSeconds,
                    rechargeSeconds,
                    maximumCharges,
                    stepOffset));
        }
    }

    private static void ValidateAdjustedAction(
        RulePolicyDefinition definition,
        TrainingState state,
        string alias,
        PolicyActionDefinition action,
        uint actionId,
        int stepOffset,
        List<PlanValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(action.AdjustedFrom) &&
            TryGetAction(definition, action.AdjustedFrom, out var baseAction))
        {
            var adjusted = state.GetAdjustedAction(
                baseAction.ActionId,
                baseAction.ActionId);

            if (adjusted != actionId)
            {
                AddError(
                    issues,
                    PlanValidationCode.AdjustedActionMismatch,
                    $"Action {actionId} is not the live adjusted form of {baseAction.ActionId}; client reports {adjusted}.",
                    stepOffset,
                    actionId);
            }

            return;
        }

        var currentAdjusted = state.GetAdjustedAction(
            action.ActionId,
            action.ActionId);

        if (currentAdjusted == action.ActionId)
        {
            return;
        }

        var adjustedDefinition = definition.Actions.FirstOrDefault(item =>
            item.Value.ActionId == currentAdjusted);

        if (!string.IsNullOrWhiteSpace(adjustedDefinition.Key) &&
            adjustedDefinition.Value.AdjustedFrom.Equals(
                alias,
                StringComparison.OrdinalIgnoreCase))
        {
            AddError(
                issues,
                PlanValidationCode.AdjustedActionMismatch,
                $"Action {actionId} has already adjusted to {currentAdjusted} in the live client.",
                stepOffset,
                actionId);
        }
    }

    private static void ValidateCooldowns(
        TrainingState state,
        IReadOnlyList<ActionUsage> usages,
        List<PlanValidationIssue> issues)
    {
        var pools = new Dictionary<uint, CooldownPool>();

        foreach (var usage in usages
                     .OrderBy(item => item.StartsAtSeconds)
                     .ThenBy(item => item.StepOffset))
        {
            if (!pools.TryGetValue(usage.CooldownActionId, out var pool))
            {
                var snapshot = state.GetCooldown(usage.ObservedActionId)
                    ?? state.GetCooldown(usage.CooldownActionId);
                pool = CooldownPool.Create(usage, snapshot);
                pools[usage.CooldownActionId] = pool;
            }

            if (!pool.TryConsume(usage.StartsAtSeconds))
            {
                AddError(
                    issues,
                    PlanValidationCode.CooldownUnavailable,
                    $"Action {usage.ObservedActionId} is scheduled at {usage.StartsAtSeconds:0.###}s before a cooldown charge is available.",
                    usage.StepOffset,
                    usage.ObservedActionId);
            }
        }
    }

    private static (string Alias, PolicyActionDefinition Action)? ResolveAction(
        RulePolicyDefinition definition,
        TrainingState state,
        uint actionId)
    {
        foreach (var item in definition.Actions)
        {
            if (item.Value.ActionId == actionId)
            {
                return (item.Key, item.Value);
            }
        }

        foreach (var item in definition.Actions)
        {
            if (state.GetAdjustedAction(item.Value.ActionId) == actionId)
            {
                return (item.Key, item.Value);
            }
        }

        return null;
    }

    private static bool TryGetAction(
        RulePolicyDefinition definition,
        string alias,
        out PolicyActionDefinition action)
    {
        if (definition.Actions.TryGetValue(alias, out action!))
        {
            return true;
        }

        foreach (var item in definition.Actions)
        {
            if (item.Key.Equals(alias, StringComparison.OrdinalIgnoreCase))
            {
                action = item.Value;
                return true;
            }
        }

        action = null!;
        return false;
    }

    private static PlanValidationResult Result(
        IReadOnlyList<PlanValidationIssue> issues)
    {
        return new PlanValidationResult
        {
            Issues = issues.ToArray()
        };
    }

    private static void AddError(
        List<PlanValidationIssue> issues,
        PlanValidationCode code,
        string message,
        int? stepOffset = null,
        uint actionId = 0)
    {
        issues.Add(
            new PlanValidationIssue
            {
                Severity = PlanValidationSeverity.Error,
                Code = code,
                StepOffset = stepOffset,
                ActionId = actionId,
                Message = message
            });
    }

    private readonly record struct ActionUsage(
        uint CooldownActionId,
        uint ObservedActionId,
        double StartsAtSeconds,
        double RechargeSeconds,
        int MaximumCharges,
        int StepOffset);

    private sealed class CooldownPool
    {
        private readonly List<double> availableAtSeconds;
        private readonly double rechargeSeconds;

        private CooldownPool(
            IEnumerable<double> availableAtSeconds,
            double rechargeSeconds)
        {
            this.availableAtSeconds = availableAtSeconds
                .OrderBy(value => value)
                .ToList();
            this.rechargeSeconds = Math.Max(0.001d, rechargeSeconds);
        }

        public static CooldownPool Create(
            ActionUsage usage,
            CooldownSnapshot? snapshot)
        {
            var maximumCharges = Math.Max(
                1,
                Math.Max(
                    usage.MaximumCharges,
                    snapshot?.MaximumCharges ?? 0));
            var currentCharges = snapshot == null
                ? maximumCharges
                : Math.Clamp(snapshot.Charges, 0, maximumCharges);

            if (snapshot is { RemainingSeconds: <= 0f } &&
                currentCharges == 0)
            {
                currentCharges = 1;
            }

            var recharge = snapshot?.RechargeSeconds > 0f
                ? snapshot.RechargeSeconds
                : usage.RechargeSeconds;
            var times = new List<double>(maximumCharges);

            for (var index = 0; index < currentCharges; index++)
            {
                times.Add(0d);
            }

            var nextCharge = snapshot?.RemainingSeconds > 0f
                ? snapshot.RemainingSeconds
                : recharge;

            for (var index = currentCharges; index < maximumCharges; index++)
            {
                times.Add(nextCharge);
                nextCharge += recharge;
            }

            return new CooldownPool(times, recharge);
        }

        public bool TryConsume(double startsAtSeconds)
        {
            availableAtSeconds.Sort();
            var earliest = availableAtSeconds[0];
            var legal = earliest <= startsAtSeconds + TimeToleranceSeconds;
            availableAtSeconds.RemoveAt(0);
            availableAtSeconds.Add(
                Math.Max(earliest, startsAtSeconds) + rechargeSeconds);
            return legal;
        }
    }
}
