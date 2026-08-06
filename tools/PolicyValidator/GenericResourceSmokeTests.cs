using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class GenericResourceSmokeTests
{
    private const uint ResourceStrike = 900001;
    private const uint FallbackStrike = 900002;
    private const uint SpendAlpha = 900003;

    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var definition = CreateDefinition();
        var policy = new RuleSetTrainingPolicy(definition);

        ValidateMultiResourceDeltas(policy);
        ValidateMultiResourceLedger(policy);
        ValidateGenericPoolingReserve(policy);

        Console.WriteLine(
            "Generic resource smoke test passed: one action can change multiple " +
            "job resources, each change is independently attributed, and policy-declared " +
            "pooling reserves protect resources without job-specific engine code.");
    }

    private static void ValidateMultiResourceDeltas(
        RuleSetTrainingPolicy policy)
    {
        var state = CreateState(alpha: 50, beta: 10, combatTimeSeconds: 20);
        var deltas = policy.GetExpectedResourceDeltas(ResourceStrike, state);

        if (!policy.TrackedResources.Contains("alpha") ||
            !policy.TrackedResources.Contains("beta") ||
            deltas.GetValueOrDefault("alpha") != -20 ||
            deltas.GetValueOrDefault("beta") != 10)
        {
            throw new InvalidDataException(
                "The generic policy did not expose both resource changes for one action.");
        }

        var firstStep = policy.BuildPracticePlan(state).Steps.FirstOrDefault();
        var alphaProjection = firstStep?.GetResourceProjection("alpha");
        var betaProjection = firstStep?.GetResourceProjection("beta");

        if (alphaProjection == null || betaProjection == null ||
            alphaProjection.Before != 50 || alphaProjection.After != 10 ||
            betaProjection.Before != 10 || betaProjection.After != 20)
        {
            throw new InvalidDataException(
                "The practice plan did not project both resources across the weave and GCD window.");
        }
    }

    private static void ValidateMultiResourceLedger(
        RuleSetTrainingPolicy policy)
    {
        var session = new TrainingSession();
        session.Start(policy, 1);
        session.RefreshState(state => CopyState(
            CreateState(alpha: 50, beta: 10, combatTimeSeconds: 20),
            state));
        session.ProcessAction(ResourceStrike);
        session.RefreshState(state =>
        {
            state.SetGauge("alpha", 30);
            state.SetGauge("beta", 20);
        });

        var alphaTransaction = session.ResourceTransactions.LastOrDefault(
            transaction => transaction.Resource.Equals(
                "alpha",
                StringComparison.OrdinalIgnoreCase));
        var betaTransaction = session.ResourceTransactions.LastOrDefault(
            transaction => transaction.Resource.Equals(
                "beta",
                StringComparison.OrdinalIgnoreCase));

        if (alphaTransaction == null ||
            alphaTransaction.Kind != ResourceTransactionKind.ActionWindow ||
            alphaTransaction.ExpectedDelta != -20 ||
            alphaTransaction.ObservedDelta != -20 ||
            !alphaTransaction.ActionIds.SequenceEqual(new[] { ResourceStrike }))
        {
            throw new InvalidDataException(
                "The generic ledger did not attribute the action's alpha spend.");
        }

        if (betaTransaction == null ||
            betaTransaction.Kind != ResourceTransactionKind.ActionWindow ||
            betaTransaction.ExpectedDelta != 10 ||
            betaTransaction.ObservedDelta != 10 ||
            !betaTransaction.ActionIds.SequenceEqual(new[] { ResourceStrike }))
        {
            throw new InvalidDataException(
                "The generic ledger did not independently attribute the action's beta gain.");
        }
    }

    private static void ValidateGenericPoolingReserve(
        RuleSetTrainingPolicy policy)
    {
        var protectedState = CreateState(
            alpha: 50,
            beta: 10,
            combatTimeSeconds: 50);
        var protectedDecision = policy.Evaluate(protectedState);

        if (protectedDecision.PreferredActionId != FallbackStrike ||
            protectedDecision.SuggestedActionIds.Contains(SpendAlpha))
        {
            throw new InvalidDataException(
                "Pooling did not protect alpha from falling below its declared reserve.");
        }

        var spendableState = CreateState(
            alpha: 70,
            beta: 10,
            combatTimeSeconds: 50);
        var spendableDecision = policy.Evaluate(spendableState);

        if (spendableDecision.PreferredActionId != ResourceStrike ||
            !spendableDecision.SuggestedActionIds.Contains(SpendAlpha))
        {
            throw new InvalidDataException(
                "Pooling blocked resource actions even though the declared reserve remained intact.");
        }
    }

    private static RulePolicyDefinition CreateDefinition()
    {
        return new RulePolicyDefinition
        {
            Id = "generic-two-resource-test",
            Name = "Generic Two Resource Test",
            Job = "TST",
            MinimumLevel = 1,
            Profile = new PolicyProfileDefinition
            {
                BurstCycleSeconds = 120,
                MinorBurstCycleSeconds = 60,
                OpenerDurationSeconds = 0,
                BurstWindowSeconds = 10,
                PoolingWindowSeconds = 15
            },
            StateInputs = new Dictionary<string, PolicyStateInputDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = new()
                {
                    Kind = PolicyStateValueKind.Resource,
                    Provider = "test.alpha",
                    DisplayName = "Alpha",
                    Minimum = 0,
                    Maximum = 100,
                    PoolingReserve = 40
                },
                ["beta"] = new()
                {
                    Kind = PolicyStateValueKind.Resource,
                    Provider = "test.beta",
                    DisplayName = "Beta",
                    Minimum = 0,
                    Maximum = 100
                }
            },
            Actions = new Dictionary<string, PolicyActionDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["resourceStrike"] = new()
                {
                    ActionId = ResourceStrike,
                    Lane = PolicyLane.Gcd,
                    MinimumLevel = 1,
                    RecastSeconds = 2.5,
                    TimelineLockSeconds = 2.5,
                    ForecastEffects =
                    {
                        new PolicyForecastEffectDefinition
                        {
                            Type = PolicyForecastEffectType.AddStateValue,
                            State = "alpha",
                            Value = -20,
                            Minimum = 0
                        },
                        new PolicyForecastEffectDefinition
                        {
                            Type = PolicyForecastEffectType.AddStateValue,
                            State = "beta",
                            Value = 10,
                            Maximum = 100
                        }
                    }
                },
                ["fallbackStrike"] = new()
                {
                    ActionId = FallbackStrike,
                    Lane = PolicyLane.Gcd,
                    MinimumLevel = 1,
                    RecastSeconds = 2.5,
                    TimelineLockSeconds = 2.5
                },
                ["spendAlpha"] = new()
                {
                    ActionId = SpendAlpha,
                    Lane = PolicyLane.Weave,
                    Role = PolicyActionRole.Advisory,
                    MinimumLevel = 1,
                    ForecastEffects =
                    {
                        new PolicyForecastEffectDefinition
                        {
                            Type = PolicyForecastEffectType.AddStateValue,
                            State = "alpha",
                            Value = -20,
                            Minimum = 0
                        }
                    }
                }
            },
            Rules =
            {
                new PolicyRuleDefinition
                {
                    Id = "spend-alpha",
                    Type = PolicyRuleType.UseAction,
                    Lane = PolicyLane.Weave,
                    Priority = 200,
                    Action = "spendAlpha",
                    SuggestionReason = "Spend Alpha when the reserve remains intact."
                },
                new PolicyRuleDefinition
                {
                    Id = "resource-strike",
                    Type = PolicyRuleType.UseAction,
                    Lane = PolicyLane.Gcd,
                    Priority = 150,
                    Action = "resourceStrike",
                    Reason = "Use the multi-resource test action."
                },
                new PolicyRuleDefinition
                {
                    Id = "fallback-strike",
                    Type = PolicyRuleType.UseAction,
                    Lane = PolicyLane.Gcd,
                    Priority = 100,
                    Action = "fallbackStrike",
                    Reason = "Use the resource-neutral fallback."
                }
            }
        };
    }

    private static TrainingState CreateState(
        int alpha,
        int beta,
        double combatTimeSeconds)
    {
        var state = new TrainingState();
        state.Begin("TST", 1);
        state.SetGauge("alpha", alpha);
        state.SetGauge("beta", beta);
        state.SetCombatTimeSeconds(combatTimeSeconds);
        state.SetAdjustedRecastSeconds(ResourceStrike, 2.5f);
        state.SetAdjustedRecastSeconds(FallbackStrike, 2.5f);
        return state;
    }

    private static void CopyState(
        TrainingState source,
        TrainingState destination)
    {
        destination.SetLevel(source.Level);
        destination.SetCombatTimeSeconds(source.CombatTimeSeconds);

        foreach (var gauge in source.Gauges)
        {
            destination.SetGauge(gauge.Key, gauge.Value);
        }

        foreach (var adjustedRecast in source.AdjustedRecastSeconds)
        {
            destination.SetAdjustedRecastSeconds(
                adjustedRecast.Key,
                adjustedRecast.Value);
        }
    }
}
