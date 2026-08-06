using System;
using System.Text.Json;
using KupoCombo.Models;

namespace KupoCombo.Services;

internal sealed class PolicyConditionEvaluator
{
    private readonly PolicyEvaluationContext context;

    public PolicyConditionEvaluator(PolicyEvaluationContext context)
    {
        this.context = context;
    }

    public bool Matches(
        PolicyConditionSet conditions,
        TrainingState state)
    {
        foreach (var condition in conditions.All)
        {
            if (!Evaluate(condition, state))
            {
                return false;
            }
        }

        if (conditions.Any.Count > 0)
        {
            var anyMatched = false;

            foreach (var condition in conditions.Any)
            {
                if (Evaluate(condition, state))
                {
                    anyMatched = true;
                    break;
                }
            }

            if (!anyMatched)
            {
                return false;
            }
        }

        foreach (var condition in conditions.None)
        {
            if (Evaluate(condition, state))
            {
                return false;
            }
        }

        return true;
    }

    private bool Evaluate(
        PolicyConditionDefinition condition,
        TrainingState state)
    {
        return condition.Source switch
        {
            PolicyConditionSource.Level =>
                CompareNumber(state.Level, condition),

            PolicyConditionSource.StateValue =>
                CompareStateValue(condition, state),

            PolicyConditionSource.StatusActive =>
                CompareBoolean(
                    state.HasStatus(context.GetStatusId(condition.Key)),
                    condition),

            PolicyConditionSource.StatusStacks =>
                CompareNumber(
                    state.GetStatusStacks(context.GetStatusId(condition.Key)),
                    condition),

            PolicyConditionSource.StatusRemainingSeconds =>
                CompareNumber(
                    state.GetStatus(context.GetStatusId(condition.Key))
                        ?.RemainingSeconds ?? 0f,
                    condition),

            PolicyConditionSource.CooldownReady =>
                CompareBoolean(
                    context.GetCooldown(condition.Key, state)?.IsReady == true,
                    condition),

            PolicyConditionSource.CooldownCharges =>
                CompareNumber(
                    context.GetCooldown(condition.Key, state)?.Charges ?? 0,
                    condition),

            PolicyConditionSource.CooldownRemainingSeconds =>
                CompareNumber(
                    context.GetCooldown(condition.Key, state)
                        ?.RemainingSeconds ?? 0f,
                    condition),

            PolicyConditionSource.ComboAction =>
                CompareAction(
                    state.NativeComboActionId,
                    condition,
                    state),

            PolicyConditionSource.ComboRemainingSeconds =>
                CompareNumber(state.ComboRemainingSeconds, condition),

            PolicyConditionSource.AdjustedAction =>
                CompareAdjustedAction(condition, state),

            PolicyConditionSource.TargetCount =>
                CompareNumber(state.TargetCount, condition),

            PolicyConditionSource.CombatTimeSeconds =>
                CompareNumber(state.CombatTimeSeconds, condition),

            PolicyConditionSource.AcceptedActionCount =>
                CompareNumber(state.AcceptedActionCount, condition),

            PolicyConditionSource.LastAction =>
                CompareAction(
                    state.LastAcceptedActionId,
                    condition,
                    state),

            _ => false
        };
    }

    private bool CompareStateValue(
        PolicyConditionDefinition condition,
        TrainingState state)
    {
        var actual = context.GetStateValue(condition.Key, state);

        if (condition.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return CompareBoolean(actual != 0d, condition);
        }

        return CompareNumber(actual, condition);
    }

    private bool CompareAdjustedAction(
        PolicyConditionDefinition condition,
        TrainingState state)
    {
        var baseActionId = context.GetAction(condition.Key).ActionId;
        var adjustedActionId = state.GetAdjustedAction(
            baseActionId,
            baseActionId);

        return CompareAction(adjustedActionId, condition, state);
    }

    private bool CompareAction(
        uint actualActionId,
        PolicyConditionDefinition condition,
        TrainingState state)
    {
        var expectedActionId = condition.Value.ValueKind switch
        {
            JsonValueKind.Number => condition.Value.GetUInt32(),
            JsonValueKind.String => context.GetActionId(
                condition.Value.GetString() ?? string.Empty,
                state,
                resolveAdjustedAlias: false),
            _ => 0u
        };

        return condition.Operator switch
        {
            PolicyComparisonOperator.Equal =>
                actualActionId == expectedActionId,
            PolicyComparisonOperator.NotEqual =>
                actualActionId != expectedActionId,
            PolicyComparisonOperator.GreaterThan =>
                actualActionId > expectedActionId,
            PolicyComparisonOperator.GreaterThanOrEqual =>
                actualActionId >= expectedActionId,
            PolicyComparisonOperator.LessThan =>
                actualActionId < expectedActionId,
            PolicyComparisonOperator.LessThanOrEqual =>
                actualActionId <= expectedActionId,
            _ => false
        };
    }

    private static bool CompareBoolean(
        bool actual,
        PolicyConditionDefinition condition)
    {
        var expected = condition.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => condition.Value.GetDouble() != 0d,
            JsonValueKind.String => bool.TryParse(
                condition.Value.GetString(),
                out var parsed) && parsed,
            _ => false
        };

        return condition.Operator switch
        {
            PolicyComparisonOperator.Equal => actual == expected,
            PolicyComparisonOperator.NotEqual => actual != expected,
            _ => false
        };
    }

    private static bool CompareNumber(
        double actual,
        PolicyConditionDefinition condition)
    {
        var expected = condition.Value.ValueKind switch
        {
            JsonValueKind.Number => condition.Value.GetDouble(),
            JsonValueKind.True => 1d,
            JsonValueKind.False => 0d,
            JsonValueKind.String when double.TryParse(
                condition.Value.GetString(),
                out var parsed) => parsed,
            _ => double.NaN
        };

        if (double.IsNaN(expected))
        {
            return false;
        }

        return condition.Operator switch
        {
            PolicyComparisonOperator.Equal =>
                Math.Abs(actual - expected) < 0.0001d,
            PolicyComparisonOperator.NotEqual =>
                Math.Abs(actual - expected) >= 0.0001d,
            PolicyComparisonOperator.GreaterThan => actual > expected,
            PolicyComparisonOperator.GreaterThanOrEqual => actual >= expected,
            PolicyComparisonOperator.LessThan => actual < expected,
            PolicyComparisonOperator.LessThanOrEqual => actual <= expected,
            _ => false
        };
    }
}
