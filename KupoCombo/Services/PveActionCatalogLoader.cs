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

            if (string.IsNullOrWhiteSpace(action.DisplayName))
            {
                action.DisplayName = entry.Name;
            }

            if (action.MinimumLevel <= 0)
            {
                action.MinimumLevel = entry.MinimumLevel;
            }

            action.ForecastEffects = entry.ForecastEffects
                .Select(CloneEffect)
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
        }
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
            AdjustedAction = effect.AdjustedAction
        };
    }
}
