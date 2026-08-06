using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KupoCombo.Models;

namespace KupoCombo.Services;

internal static class BurstResourceTargetLoader
{
    private const int SupportedSchemaVersion = 1;
    private const string FileName = "burst-resource-targets.json";

    private static readonly Lazy<IReadOnlyDictionary<string, BurstResourcePolicyTargetDefinition>>
        Targets = new(LoadTargets);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static void Apply(RulePolicyDefinition definition)
    {
        if (!Targets.Value.TryGetValue(definition.Id, out var target))
        {
            return;
        }

        ApplyTiming(definition, target);
        ApplyResources(definition, target);
        ApplyRuleOverrides(definition, target);
        ApplyAdditionalRules(definition, target);
    }

    private static void ApplyTiming(
        RulePolicyDefinition definition,
        BurstResourcePolicyTargetDefinition target)
    {
        if (target.MinorBurstCycleSeconds.HasValue)
        {
            definition.Profile.MinorBurstCycleSeconds =
                target.MinorBurstCycleSeconds.Value;
        }

        if (target.BurstWindowSeconds.HasValue)
        {
            definition.Profile.BurstWindowSeconds =
                target.BurstWindowSeconds.Value;
        }

        if (target.PoolingWindowSeconds.HasValue)
        {
            definition.Profile.PoolingWindowSeconds =
                target.PoolingWindowSeconds.Value;
        }
    }

    private static void ApplyResources(
        RulePolicyDefinition definition,
        BurstResourcePolicyTargetDefinition target)
    {
        foreach (var (resource, resourceTarget) in target.Resources)
        {
            if (!TryGetValue(
                    definition.StateInputs,
                    resource,
                    out var input))
            {
                throw new InvalidDataException(
                    $"Burst target '{target.PolicyId}' references unknown resource '{resource}'.");
            }

            if (input.Kind != PolicyStateValueKind.Resource)
            {
                throw new InvalidDataException(
                    $"Burst target '{target.PolicyId}' references '{resource}', " +
                    "but that state input is not a resource.");
            }

            if (!resourceTarget.PoolingReserve.HasValue)
            {
                throw new InvalidDataException(
                    $"Burst target '{target.PolicyId}' does not provide a reserve for '{resource}'.");
            }

            var reserve = resourceTarget.PoolingReserve.Value;

            if (input.Minimum.HasValue && reserve < input.Minimum.Value ||
                input.Maximum.HasValue && reserve > input.Maximum.Value)
            {
                throw new InvalidDataException(
                    $"Burst target '{target.PolicyId}' sets '{resource}' reserve to " +
                    $"{reserve}, outside its declared range.");
            }

            input.PoolingReserve = reserve;

            if (!string.IsNullOrWhiteSpace(resourceTarget.DisplayName))
            {
                input.DisplayName = resourceTarget.DisplayName;
            }
        }
    }

    private static void ApplyRuleOverrides(
        RulePolicyDefinition definition,
        BurstResourcePolicyTargetDefinition target)
    {
        foreach (var ruleOverride in target.RuleOverrides)
        {
            var rule = definition.Rules.FirstOrDefault(candidate =>
                candidate.Id.Equals(
                    ruleOverride.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (rule == null)
            {
                throw new InvalidDataException(
                    $"Burst target '{target.PolicyId}' references unknown rule " +
                    $"'{ruleOverride.Id}'.");
            }

            if (ruleOverride.Type.HasValue)
            {
                rule.Type = ruleOverride.Type.Value;
            }

            if (ruleOverride.Threshold.HasValue)
            {
                rule.Threshold = ruleOverride.Threshold.Value;
            }

            if (ruleOverride.AllowBelowResourceReserve.HasValue)
            {
                rule.AllowBelowResourceReserve =
                    ruleOverride.AllowBelowResourceReserve.Value;
            }

            if (ruleOverride.Conditions != null)
            {
                ValidateConditions(
                    definition,
                    target.PolicyId,
                    ruleOverride.Id,
                    ruleOverride.Conditions);
                rule.Conditions = ruleOverride.Conditions;
            }
        }
    }

    private static void ApplyAdditionalRules(
        RulePolicyDefinition definition,
        BurstResourcePolicyTargetDefinition target)
    {
        foreach (var additionalRule in target.AdditionalRules)
        {
            ValidateRule(definition, target.PolicyId, additionalRule);

            var existingIndex = definition.Rules.FindIndex(candidate =>
                candidate.Id.Equals(
                    additionalRule.Id,
                    StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                definition.Rules[existingIndex] = additionalRule;
            }
            else
            {
                definition.Rules.Add(additionalRule);
            }
        }
    }

    private static void ValidateRule(
        RulePolicyDefinition definition,
        string policyId,
        PolicyRuleDefinition rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            throw new InvalidDataException(
                $"Burst target '{policyId}' contains a rule without an ID.");
        }

        if (!string.IsNullOrWhiteSpace(rule.Action) &&
            !ContainsKey(definition.Actions, rule.Action))
        {
            throw new InvalidDataException(
                $"Burst target '{policyId}' rule '{rule.Id}' references " +
                $"unknown action '{rule.Action}'.");
        }

        ValidateConditions(definition, policyId, rule.Id, rule.Conditions);
    }

    private static void ValidateConditions(
        RulePolicyDefinition definition,
        string policyId,
        string ruleId,
        PolicyConditionSet conditions)
    {
        foreach (var condition in conditions.All
                     .Concat(conditions.Any)
                     .Concat(conditions.None))
        {
            switch (condition.Source)
            {
                case PolicyConditionSource.StateValue:
                    RequireKey(
                        definition.StateInputs,
                        condition.Key,
                        policyId,
                        ruleId,
                        "state input");
                    break;

                case PolicyConditionSource.StatusActive:
                case PolicyConditionSource.StatusStacks:
                case PolicyConditionSource.StatusRemainingSeconds:
                    RequireKey(
                        definition.Statuses,
                        condition.Key,
                        policyId,
                        ruleId,
                        "status");
                    break;

                case PolicyConditionSource.CooldownReady:
                case PolicyConditionSource.CooldownCharges:
                case PolicyConditionSource.AdjustedAction:
                    RequireKey(
                        definition.Actions,
                        condition.Key,
                        policyId,
                        ruleId,
                        "action");
                    break;

                case PolicyConditionSource.LastAction:
                    if (condition.Value.ValueKind == JsonValueKind.String)
                    {
                        RequireKey(
                            definition.Actions,
                            condition.Value.GetString() ?? string.Empty,
                            policyId,
                            ruleId,
                            "action value");
                    }
                    break;
            }
        }
    }

    private static IReadOnlyDictionary<string, BurstResourcePolicyTargetDefinition>
        LoadTargets()
    {
        var path = ResolveTargetPath();
        var file = JsonSerializer.Deserialize<BurstResourceTargetFile>(
                File.ReadAllText(path),
                JsonOptions)
            ?? throw new InvalidDataException(
                $"{FileName} could not be deserialized.");

        if (file.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported burst-resource target schema version " +
                $"{file.SchemaVersion}. Expected {SupportedSchemaVersion}.");
        }

        var duplicate = file.Policies
            .Where(policy => !string.IsNullOrWhiteSpace(policy.PolicyId))
            .GroupBy(policy => policy.PolicyId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidDataException(
                $"Duplicate burst-resource target policy: {duplicate.Key}");
        }

        foreach (var policy in file.Policies)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId))
            {
                throw new InvalidDataException(
                    "A burst-resource target is missing its policy ID.");
            }

            if (policy.MinorBurstCycleSeconds is <= 0 ||
                policy.BurstWindowSeconds is < 0 ||
                policy.PoolingWindowSeconds is < 0)
            {
                throw new InvalidDataException(
                    $"Burst target '{policy.PolicyId}' contains invalid timing values.");
            }
        }

        return file.Policies.ToDictionary(
            policy => policy.PolicyId,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveTargetPath()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "BurstTargets",
                FileName),
            Path.Combine(
                Environment.CurrentDirectory,
                "Data",
                "BurstTargets",
                FileName),
            Path.Combine(
                Environment.CurrentDirectory,
                "BurstTargets",
                FileName)
        };

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; depth < 8 && directory != null; depth++)
        {
            candidates.Add(
                Path.Combine(
                    directory.FullName,
                    "Data",
                    "BurstTargets",
                    FileName));
            directory = directory.Parent;
        }

        var path = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);

        return path ?? throw new FileNotFoundException(
            "The KupoCombo burst-resource target file could not be found.",
            FileName);
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

    private static void RequireKey<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        string policyId,
        string ruleId,
        string description)
    {
        if (!ContainsKey(values, key))
        {
            throw new InvalidDataException(
                $"Burst target '{policyId}' rule '{ruleId}' references " +
                $"unknown {description} '{key}'.");
        }
    }

    private static bool TryGetValue<T>(
        IReadOnlyDictionary<string, T> values,
        string key,
        out T value)
    {
        foreach (var item in values)
        {
            if (item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    private static bool ContainsKey<T>(
        IReadOnlyDictionary<string, T> values,
        string key)
    {
        return values.Keys.Any(candidate =>
            candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class BurstResourceTargetFile
{
    public int SchemaVersion { get; set; }

    public List<BurstResourcePolicyTargetDefinition> Policies { get; set; } =
        new();
}

internal sealed class BurstResourcePolicyTargetDefinition
{
    public string PolicyId { get; set; } = string.Empty;

    public int? MinorBurstCycleSeconds { get; set; }

    public int? BurstWindowSeconds { get; set; }

    public int? PoolingWindowSeconds { get; set; }

    public Dictionary<string, BurstResourceTargetDefinition> Resources { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<BurstResourceRuleOverrideDefinition> RuleOverrides { get; set; } =
        new();

    public List<PolicyRuleDefinition> AdditionalRules { get; set; } = new();

    public List<string> Sources { get; set; } = new();
}

internal sealed class BurstResourceTargetDefinition
{
    public string DisplayName { get; set; } = string.Empty;

    public double? PoolingReserve { get; set; }

    public string Rationale { get; set; } = string.Empty;
}

internal sealed class BurstResourceRuleOverrideDefinition
{
    public string Id { get; set; } = string.Empty;

    public PolicyRuleType? Type { get; set; }

    public double? Threshold { get; set; }

    public bool? AllowBelowResourceReserve { get; set; }

    public PolicyConditionSet? Conditions { get; set; }
}
