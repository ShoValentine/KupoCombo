using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KupoCombo.Models;

namespace KupoCombo.Services;

public static class RulePolicyLoader
{
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static IReadOnlyList<RulePolicyDefinition> Load(
        string filePath,
        string expectedJob)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "The KupoCombo policy file could not be found.",
                filePath);
        }

        return Parse(
            File.ReadAllText(filePath),
            Path.GetFileName(filePath),
            expectedJob);
    }

    public static IReadOnlyList<RulePolicyDefinition> Parse(
        string json,
        string sourceName,
        string expectedJob)
    {
        var policyFile = JsonSerializer.Deserialize<RulePolicyFile>(
                json,
                JsonOptions)
            ?? throw new InvalidDataException(
                $"{sourceName} could not be deserialized.");

        if (policyFile.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported policy schema version " +
                $"{policyFile.SchemaVersion} in {sourceName}. " +
                $"Expected {SupportedSchemaVersion}.");
        }

        ValidatePolicies(policyFile.Policies, expectedJob);
        return policyFile.Policies;
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

    private static void ValidatePolicies(
        IReadOnlyCollection<RulePolicyDefinition> policies,
        string expectedJob)
    {
        if (policies.Count == 0)
        {
            throw new InvalidDataException(
                "The policy file contains no policies.");
        }

        EnsureNoDuplicateNames(
            policies.Select(policy => policy.Id),
            "policy ID");

        foreach (var policy in policies)
        {
            ValidatePolicy(policy, expectedJob);
        }
    }

    private static void ValidatePolicy(
        RulePolicyDefinition policy,
        string expectedJob)
    {
        RequireText(policy.Id, "A policy is missing its ID.");
        RequireText(
            policy.Name,
            $"Policy '{policy.Id}' is missing its name.");
        RequireText(
            policy.Job,
            $"Policy '{policy.Id}' is missing its job.");

        if (!policy.Job.Equals(
                expectedJob,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' belongs to '{policy.Job}', " +
                $"but it was loaded as '{expectedJob}'.");
        }

        if (policy.MinimumLevel < 1)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' has an invalid minimum level.");
        }

        if (policy.MaximumLevel.HasValue &&
            policy.MaximumLevel.Value < policy.MinimumLevel)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' has a maximum level below its minimum level.");
        }

        ValidateProfile(policy);
        ValidateActions(policy);
        ValidateStatuses(policy);
        ValidateStateInputs(policy);
        ValidateCombos(policy);
        ValidateRules(policy);
    }

    private static void ValidateProfile(RulePolicyDefinition policy)
    {
        var profile = policy.Profile;

        if (profile.MinimumTargetCount < 1 ||
            profile.MaximumTargetCount < profile.MinimumTargetCount)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' has an invalid target-count range.");
        }

        if (profile.BurstCycleSeconds <= 0)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' must use a positive burst cycle.");
        }
    }

    private static void ValidateActions(RulePolicyDefinition policy)
    {
        if (policy.Actions.Count == 0)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' defines no actions.");
        }

        EnsureNoDuplicateNames(policy.Actions.Keys, "action alias");

        foreach (var (alias, action) in policy.Actions)
        {
            RequireText(
                alias,
                $"Policy '{policy.Id}' contains an empty action alias.");

            if (action.ActionId == 0)
            {
                throw new InvalidDataException(
                    $"Action '{alias}' in policy '{policy.Id}' uses action ID 0.");
            }

            if (action.MinimumLevel < 1)
            {
                throw new InvalidDataException(
                    $"Action '{alias}' in policy '{policy.Id}' has an invalid minimum level.");
            }

            if (action.MaximumLevel.HasValue &&
                action.MaximumLevel.Value < action.MinimumLevel)
            {
                throw new InvalidDataException(
                    $"Action '{alias}' in policy '{policy.Id}' has an invalid level range.");
            }

            if (!string.IsNullOrWhiteSpace(action.AdjustedFrom))
            {
                RequireAction(policy, action.AdjustedFrom, $"action '{alias}' adjustedFrom");
            }
        }
    }

    private static void ValidateStatuses(RulePolicyDefinition policy)
    {
        EnsureNoDuplicateNames(policy.Statuses.Keys, "status alias");

        foreach (var (alias, statusId) in policy.Statuses)
        {
            RequireText(
                alias,
                $"Policy '{policy.Id}' contains an empty status alias.");

            if (statusId == 0)
            {
                throw new InvalidDataException(
                    $"Status '{alias}' in policy '{policy.Id}' uses status ID 0.");
            }
        }
    }

    private static void ValidateStateInputs(RulePolicyDefinition policy)
    {
        EnsureNoDuplicateNames(policy.StateInputs.Keys, "state input alias");

        foreach (var (alias, input) in policy.StateInputs)
        {
            RequireText(
                alias,
                $"Policy '{policy.Id}' contains an empty state input alias.");
            RequireText(
                input.Provider,
                $"State input '{alias}' in policy '{policy.Id}' is missing its provider.");

            if (input.Minimum.HasValue &&
                input.Maximum.HasValue &&
                input.Maximum.Value < input.Minimum.Value)
            {
                throw new InvalidDataException(
                    $"State input '{alias}' in policy '{policy.Id}' has an invalid range.");
            }
        }
    }

    private static void ValidateCombos(RulePolicyDefinition policy)
    {
        EnsureNoDuplicateNames(policy.Combos.Keys, "combo alias");

        foreach (var (alias, combo) in policy.Combos)
        {
            RequireText(
                alias,
                $"Policy '{policy.Id}' contains an empty combo alias.");

            if (combo.Steps.Count < 2)
            {
                throw new InvalidDataException(
                    $"Combo '{alias}' in policy '{policy.Id}' needs at least two steps.");
            }

            if (combo.MinimumLevel < 1)
            {
                throw new InvalidDataException(
                    $"Combo '{alias}' in policy '{policy.Id}' has an invalid minimum level.");
            }

            foreach (var actionAlias in combo.Steps)
            {
                RequireAction(policy, actionAlias, $"combo '{alias}' step");
            }
        }
    }

    private static void ValidateRules(RulePolicyDefinition policy)
    {
        if (policy.Rules.Count == 0)
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' defines no rules.");
        }

        EnsureNoDuplicateNames(
            policy.Rules.Select(rule => rule.Id),
            "rule ID");

        foreach (var rule in policy.Rules)
        {
            ValidateRule(policy, rule);
        }
    }

    private static void ValidateRule(
        RulePolicyDefinition policy,
        PolicyRuleDefinition rule)
    {
        RequireText(
            rule.Id,
            $"Policy '{policy.Id}' contains a rule without an ID.");

        if (rule.Priority < 0)
        {
            throw new InvalidDataException(
                $"Rule '{rule.Id}' in policy '{policy.Id}' has a negative priority.");
        }

        if (!string.IsNullOrWhiteSpace(rule.Action))
        {
            RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
        }

        foreach (var actionAlias in rule.AcceptableActions)
        {
            RequireAction(policy, actionAlias, $"rule '{rule.Id}' acceptable action");
        }

        foreach (var actionAlias in rule.AdjustedActions)
        {
            RequireAction(policy, actionAlias, $"rule '{rule.Id}' adjusted action");
        }

        ValidateRuleShape(policy, rule);
        ValidateConditions(policy, rule);
    }

    private static void ValidateRuleShape(
        RulePolicyDefinition policy,
        PolicyRuleDefinition rule)
    {
        switch (rule.Type)
        {
            case PolicyRuleType.ContinueCombo:
                RequireCombo(policy, rule.Combo, rule.Id);
                break;

            case PolicyRuleType.FollowAdjustedAction:
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");

                if (rule.AdjustedActions.Count == 0)
                {
                    throw MissingRuleField(policy, rule, "adjustedActions");
                }
                break;

            case PolicyRuleType.PreventResourceOvercap:
                RequireStateInput(policy, rule.Resource, rule.Id);
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                RequireNumber(policy, rule, rule.Threshold, "threshold");
                break;

            case PolicyRuleType.PreventChargeOvercap:
                RequireAction(policy, rule.Cooldown, $"rule '{rule.Id}' cooldown");
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                RequireNumber(policy, rule, rule.Threshold, "threshold");
                break;

            case PolicyRuleType.MaintainStatus:
                RequireStatus(policy, rule.Status, rule.Id);
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                RequireNumber(
                    policy,
                    rule,
                    rule.MinimumRemainingSeconds,
                    "minimumRemainingSeconds");
                break;

            case PolicyRuleType.SpendStatusStacks:
            case PolicyRuleType.FollowProc:
                RequireStatus(policy, rule.Status, rule.Id);
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                break;

            case PolicyRuleType.UseCooldown:
                RequireAction(policy, rule.Cooldown, $"rule '{rule.Id}' cooldown");
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                break;

            case PolicyRuleType.UseAction:
                RequireAction(policy, rule.Action, $"rule '{rule.Id}' action");
                break;

            default:
                throw new InvalidDataException(
                    $"Rule '{rule.Id}' in policy '{policy.Id}' uses an unsupported rule type.");
        }
    }

    private static void ValidateConditions(
        RulePolicyDefinition policy,
        PolicyRuleDefinition rule)
    {
        foreach (var condition in rule.Conditions.All
                     .Concat(rule.Conditions.Any)
                     .Concat(rule.Conditions.None))
        {
            if (condition.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException(
                    $"A condition in rule '{rule.Id}' in policy '{policy.Id}' " +
                    "is missing its value.");
            }

            switch (condition.Source)
            {
                case PolicyConditionSource.StateValue:
                    RequireStateInput(policy, condition.Key, rule.Id);
                    break;

                case PolicyConditionSource.StatusActive:
                case PolicyConditionSource.StatusStacks:
                case PolicyConditionSource.StatusRemainingSeconds:
                    RequireStatus(policy, condition.Key, rule.Id);
                    break;

                case PolicyConditionSource.CooldownReady:
                case PolicyConditionSource.CooldownCharges:
                case PolicyConditionSource.AdjustedAction:
                case PolicyConditionSource.LastAction:
                    RequireAction(policy, condition.Key, $"rule '{rule.Id}' condition key");
                    break;

                case PolicyConditionSource.Level:
                case PolicyConditionSource.ComboAction:
                case PolicyConditionSource.ComboRemainingSeconds:
                case PolicyConditionSource.TargetCount:
                case PolicyConditionSource.CombatTimeSeconds:
                case PolicyConditionSource.AcceptedActionCount:
                    break;

                default:
                    throw new InvalidDataException(
                        $"Rule '{rule.Id}' in policy '{policy.Id}' contains an unsupported condition source.");
            }
        }
    }

    private static void RequireAction(
        RulePolicyDefinition policy,
        string alias,
        string context)
    {
        RequireText(
            alias,
            $"Policy '{policy.Id}' is missing {context}.");

        if (!ContainsKey(policy.Actions, alias))
        {
            throw new InvalidDataException(
                $"Policy '{policy.Id}' references unknown action '{alias}' in {context}.");
        }
    }

    private static void RequireStatus(
        RulePolicyDefinition policy,
        string alias,
        string ruleId)
    {
        RequireText(
            alias,
            $"Rule '{ruleId}' in policy '{policy.Id}' is missing its status.");

        if (!ContainsKey(policy.Statuses, alias))
        {
            throw new InvalidDataException(
                $"Rule '{ruleId}' in policy '{policy.Id}' references unknown status '{alias}'.");
        }
    }

    private static void RequireStateInput(
        RulePolicyDefinition policy,
        string alias,
        string ruleId)
    {
        RequireText(
            alias,
            $"Rule '{ruleId}' in policy '{policy.Id}' is missing its state input.");

        if (!ContainsKey(policy.StateInputs, alias))
        {
            throw new InvalidDataException(
                $"Rule '{ruleId}' in policy '{policy.Id}' references unknown state input '{alias}'.");
        }
    }

    private static void RequireCombo(
        RulePolicyDefinition policy,
        string alias,
        string ruleId)
    {
        RequireText(
            alias,
            $"Rule '{ruleId}' in policy '{policy.Id}' is missing its combo.");

        if (!ContainsKey(policy.Combos, alias))
        {
            throw new InvalidDataException(
                $"Rule '{ruleId}' in policy '{policy.Id}' references unknown combo '{alias}'.");
        }
    }

    private static void RequireNumber(
        RulePolicyDefinition policy,
        PolicyRuleDefinition rule,
        double? value,
        string fieldName)
    {
        if (!value.HasValue)
        {
            throw MissingRuleField(policy, rule, fieldName);
        }
    }

    private static InvalidDataException MissingRuleField(
        RulePolicyDefinition policy,
        PolicyRuleDefinition rule,
        string fieldName)
    {
        return new InvalidDataException(
            $"Rule '{rule.Id}' in policy '{policy.Id}' requires '{fieldName}'.");
    }

    private static bool ContainsKey<T>(
        IReadOnlyDictionary<string, T> values,
        string key)
    {
        return values.Keys.Any(
            candidate => candidate.Equals(
                key,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureNoDuplicateNames(
        IEnumerable<string> names,
        string description)
    {
        var duplicate = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate != null)
        {
            throw new InvalidDataException(
                $"Duplicate {description}: {duplicate.Key}");
        }
    }

    private static void RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(message);
        }
    }
}
