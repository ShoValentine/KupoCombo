using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KupoCombo.Models;

namespace KupoCombo.Services;

public static class PveActionCatalogLoader
{
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public static PveActionCatalogFile Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "The KupoCombo PvE action catalogue could not be found.",
                filePath);
        }

        var catalogue = JsonSerializer.Deserialize<PveActionCatalogFile>(
                File.ReadAllText(filePath),
                JsonOptions)
            ?? throw new InvalidDataException(
                $"{Path.GetFileName(filePath)} could not be deserialized.");

        Validate(catalogue);
        return catalogue;
    }

    public static void Apply(
        RulePolicyDefinition policy,
        PveActionCatalogFile catalogue)
    {
        var entries = catalogue.Actions.ToDictionary(
            entry => entry.ActionId);

        foreach (var (alias, action) in policy.Actions)
        {
            if (!entries.TryGetValue(action.ActionId, out var entry))
            {
                continue;
            }

            if (!entry.Job.Equals(policy.Job, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Action {entry.ActionId} ({entry.Name}) belongs to {entry.Job}, " +
                    $"but policy '{policy.Id}' uses it as {policy.Job}.");
            }

            var policyEffects = action.ForecastEffects
                .Select(CloneEffect)
                .ToList();

            if (string.IsNullOrWhiteSpace(action.DisplayName))
            {
                action.DisplayName = entry.Name;
            }

            if (action.MinimumLevel <= 0)
            {
                action.MinimumLevel = entry.MinimumLevel;
            }

            action.Kind = entry.Kind;
            action.CastSeconds = entry.CastSeconds;
            action.RecastSeconds = entry.RecastSeconds;
            action.TimelineLockSeconds = entry.TimelineLockSeconds;
            action.MaximumCharges = entry.MaximumCharges;
            action.Potency = entry.Potency;
            action.ComboPotency = entry.ComboPotency;
            action.MpCost = entry.MpCost;

            var catalogueEffects = entry.ForecastEffects
                .Select(CloneEffect);

            action.ForecastEffects = action.OverrideCatalogueForecastEffects
                ? policyEffects
                : catalogueEffects
                    .Concat(policyEffects)
                    .ToList();

            if (string.IsNullOrWhiteSpace(action.AdjustedFrom) &&
                entry.AdjustedFromActionId.HasValue)
            {
                action.AdjustedFrom = policy.Actions
                    .FirstOrDefault(candidate =>
                        candidate.Value.ActionId == entry.AdjustedFromActionId.Value)
                    .Key ?? string.Empty;
            }

            ValidateEffectsForPolicy(policy, alias, action.ForecastEffects);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void Validate(PveActionCatalogFile catalogue)
    {
        if (catalogue.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported PvE action catalogue schema version " +
                $"{catalogue.SchemaVersion}. Expected {SupportedSchemaVersion}.");
        }

        if (catalogue.Actions.Count == 0)
        {
            throw new InvalidDataException(
                "The PvE action catalogue contains no actions.");
        }

        var duplicate = catalogue.Actions
            .GroupBy(action => action.ActionId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidDataException(
                $"The PvE action catalogue contains duplicate action ID " +
                $"{duplicate.Key}.");
        }

        foreach (var action in catalogue.Actions)
        {
            if (action.ActionId == 0 ||
                string.IsNullOrWhiteSpace(action.Name) ||
                string.IsNullOrWhiteSpace(action.Job) ||
                action.MinimumLevel < 1 ||
                action.CastSeconds < 0d ||
                action.RecastSeconds < 0d ||
                action.TimelineLockSeconds < 0d ||
                action.MaximumCharges < 1)
            {
                throw new InvalidDataException(
                    $"The PvE action catalogue contains an invalid entry for " +
                    $"action ID {action.ActionId}.");
            }
        }
    }

    private static void ValidateEffectsForPolicy(
        RulePolicyDefinition policy,
        string actionAlias,
        IReadOnlyCollection<PolicyForecastEffectDefinition> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect.Type)
            {
                case PolicyForecastEffectType.AddStateValue:
                case PolicyForecastEffectType.SetStateValue:
                    RequireKey(
                        policy.StateInputs.Keys,
                        effect.State,
                        policy,
                        actionAlias,
                        "state input");
                    break;

                case PolicyForecastEffectType.AddStatus:
                case PolicyForecastEffectType.RemoveStatus:
                    RequireKey(
                        policy.Statuses.Keys,
                        effect.Status,
                        policy,
                        actionAlias,
                        "status");
                    break;

                case PolicyForecastEffectType.SetAdjustedAction:
                    RequireKey(
                        policy.Actions.Keys,
                        effect.Action,
                        policy,
                        actionAlias,
                        "base action");
                    RequireKey(
                        policy.Actions.Keys,
                        effect.AdjustedAction,
                        policy,
                        actionAlias,
                        "adjusted action");
                    break;

                case PolicyForecastEffectType.ResetAdjustedAction:
                    RequireKey(
                        policy.Actions.Keys,
                        effect.Action,
                        policy,
                        actionAlias,
                        "reset action");
                    break;
            }

            ValidateEffectConditions(
                policy,
                actionAlias,
                effect.Conditions);
        }
    }

    private static void ValidateEffectConditions(
        RulePolicyDefinition policy,
        string actionAlias,
        PolicyConditionSet conditions)
    {
        foreach (var condition in conditions.All
                     .Concat(conditions.Any)
                     .Concat(conditions.None))
        {
            if (condition.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException(
                    $"A forecast effect for action '{actionAlias}' in policy " +
                    $"'{policy.Id}' contains a condition without a value.");
            }

            switch (condition.Source)
            {
                case PolicyConditionSource.StateValue:
                    RequireKey(
                        policy.StateInputs.Keys,
                        condition.Key,
                        policy,
                        actionAlias,
                        "condition state input");
                    break;

                case PolicyConditionSource.StatusActive:
                case PolicyConditionSource.StatusStacks:
                case PolicyConditionSource.StatusRemainingSeconds:
                    RequireKey(
                        policy.Statuses.Keys,
                        condition.Key,
                        policy,
                        actionAlias,
                        "condition status");
                    break;

                case PolicyConditionSource.CooldownReady:
                case PolicyConditionSource.CooldownCharges:
                case PolicyConditionSource.AdjustedAction:
                    RequireKey(
                        policy.Actions.Keys,
                        condition.Key,
                        policy,
                        actionAlias,
                        "condition action");
                    break;

                case PolicyConditionSource.ComboAction:
                case PolicyConditionSource.LastAction:
                    ValidateActionConditionValue(
                        policy,
                        actionAlias,
                        condition);
                    break;

                case PolicyConditionSource.Level:
                case PolicyConditionSource.ComboRemainingSeconds:
                case PolicyConditionSource.TargetCount:
                case PolicyConditionSource.CombatTimeSeconds:
                case PolicyConditionSource.AcceptedActionCount:
                    break;

                default:
                    throw new InvalidDataException(
                        $"A forecast effect for action '{actionAlias}' in policy " +
                        $"'{policy.Id}' uses an unsupported condition source.");
            }
        }
    }

    private static void ValidateActionConditionValue(
        RulePolicyDefinition policy,
        string actionAlias,
        PolicyConditionDefinition condition)
    {
        if (condition.Value.ValueKind == JsonValueKind.Number)
        {
            return;
        }

        if (condition.Value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"A forecast effect for action '{actionAlias}' in policy " +
                $"'{policy.Id}' uses a non-action condition value.");
        }

        RequireKey(
            policy.Actions.Keys,
            condition.Value.GetString() ?? string.Empty,
            policy,
            actionAlias,
            "condition action value");
    }

    private static void RequireKey(
        IEnumerable<string> keys,
        string value,
        RulePolicyDefinition policy,
        string actionAlias,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !keys.Any(key => key.Equals(
                value,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Catalogue effects for action '{actionAlias}' in policy " +
                $"'{policy.Id}' reference unknown {description} '{value}'.");
        }
    }

    private static PolicyForecastEffectDefinition CloneEffect(
        PolicyForecastEffectDefinition effect)
    {
        return new PolicyForecastEffectDefinition
        {
            Type = effect.Type,
            State = effect.State,
            Value = effect.Value,
            Minimum = effect.Minimum,
            Maximum = effect.Maximum,
            Status = effect.Status,
            DurationSeconds = effect.DurationSeconds,
            Stacks = effect.Stacks,
            Action = effect.Action,
            AdjustedAction = effect.AdjustedAction,
            Conditions = CloneConditions(effect.Conditions)
        };
    }

    private static PolicyConditionSet CloneConditions(
        PolicyConditionSet conditions)
    {
        return new PolicyConditionSet
        {
            All = conditions.All.Select(CloneCondition).ToList(),
            Any = conditions.Any.Select(CloneCondition).ToList(),
            None = conditions.None.Select(CloneCondition).ToList()
        };
    }

    private static PolicyConditionDefinition CloneCondition(
        PolicyConditionDefinition condition)
    {
        return new PolicyConditionDefinition
        {
            Source = condition.Source,
            Key = condition.Key,
            Operator = condition.Operator,
            Value = condition.Value.Clone()
        };
    }
}
