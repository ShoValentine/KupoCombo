using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class PracticePlanValidatorSmokeTests
{
    private const uint Strike = 100;
    private const uint AlternateStrike = 101;
    private const uint Burst = 200;

    [ModuleInitializer]
    internal static void Run()
    {
        AcceptsLegalPlan();
        RejectsBrokenStructureAndLanes();
        RejectsImpossibleCooldownSchedule();
        RejectsResourceDiscontinuityAndBounds();
        RejectsCommittedEdgeMutation();
    }

    private static void AcceptsLegalPlan()
    {
        var fixture = CreateFixture();
        var result = fixture.Validator.Validate(
            new PlanValidationRequest
            {
                Plan = CreatePlan(
                    new[]
                    {
                        Step(0, 0d, Strike, new[] { Burst }, 50, 50),
                        Step(1, 2.5d, Strike, Array.Empty<uint>(), 50, 50),
                        Step(2, 5d, Strike, Array.Empty<uint>(), 50, 50)
                    }),
                State = fixture.State
            },
            fixture.Policy);

        Require(result.IsValid, result.Summary);
    }

    private static void RejectsBrokenStructureAndLanes()
    {
        var fixture = CreateFixture();
        var result = fixture.Validator.Validate(
            new PlanValidationRequest
            {
                Plan = CreatePlan(
                    new[]
                    {
                        Step(4, 1d, Burst, new[] { Strike }, 50, 50)
                    }),
                State = fixture.State
            },
            fixture.Policy);

        RequireError(result, PlanValidationCode.InvalidOffset);
        RequireError(result, PlanValidationCode.InvalidStepStart);
        RequireError(result, PlanValidationCode.WrongActionLane);
    }

    private static void RejectsImpossibleCooldownSchedule()
    {
        var fixture = CreateFixture();
        var result = fixture.Validator.Validate(
            new PlanValidationRequest
            {
                Plan = CreatePlan(
                    new[]
                    {
                        Step(0, 0d, Strike, new[] { Burst }, 50, 50),
                        Step(1, 2.5d, Strike, new[] { Burst }, 50, 50)
                    }),
                State = fixture.State
            },
            fixture.Policy);

        RequireError(result, PlanValidationCode.CooldownUnavailable);
    }

    private static void RejectsResourceDiscontinuityAndBounds()
    {
        var fixture = CreateFixture();
        var result = fixture.Validator.Validate(
            new PlanValidationRequest
            {
                Plan = CreatePlan(
                    new[]
                    {
                        Step(0, 0d, Strike, Array.Empty<uint>(), 50, 110),
                        Step(1, 2.5d, Strike, Array.Empty<uint>(), 90, 90)
                    }),
                State = fixture.State
            },
            fixture.Policy);

        RequireError(result, PlanValidationCode.ResourceOutOfBounds);
        RequireError(result, PlanValidationCode.ResourceDiscontinuity);
    }

    private static void RejectsCommittedEdgeMutation()
    {
        var fixture = CreateFixture();
        var committed = CreatePlan(
            new[]
            {
                Step(0, 0d, Strike, new[] { Burst }, 50, 50),
                Step(1, 2.5d, Strike, Array.Empty<uint>(), 50, 50)
            });
        var replacement = CreatePlan(
            new[]
            {
                Step(0, 0d, AlternateStrike, Array.Empty<uint>(), 50, 50),
                Step(1, 2.5d, Strike, Array.Empty<uint>(), 50, 50)
            });
        var result = fixture.Validator.Validate(
            new PlanValidationRequest
            {
                Plan = replacement,
                State = fixture.State,
                CommittedPlan = committed,
                CommittedDepth = 2
            },
            fixture.Policy);

        RequireError(result, PlanValidationCode.CommitmentChanged);
    }

    private static Fixture CreateFixture()
    {
        var definition = new RulePolicyDefinition
        {
            Id = "test-plan-validator",
            Name = "Plan validator fixture",
            Job = "TST",
            MinimumLevel = 1,
            Profile = new PolicyProfileDefinition
            {
                BurstCycleSeconds = 120
            },
            Actions = new Dictionary<string, PolicyActionDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["strike"] = new()
                {
                    ActionId = Strike,
                    DisplayName = "Strike",
                    Lane = PolicyLane.Gcd,
                    Role = PolicyActionRole.Graded,
                    MinimumLevel = 1,
                    RecastSeconds = 2.5d,
                    TimelineLockSeconds = 2.5d,
                    MaximumCharges = 1
                },
                ["alternate_strike"] = new()
                {
                    ActionId = AlternateStrike,
                    DisplayName = "Alternate Strike",
                    Lane = PolicyLane.Gcd,
                    Role = PolicyActionRole.Graded,
                    MinimumLevel = 1,
                    RecastSeconds = 2.5d,
                    TimelineLockSeconds = 2.5d,
                    MaximumCharges = 1
                },
                ["burst"] = new()
                {
                    ActionId = Burst,
                    DisplayName = "Burst",
                    Lane = PolicyLane.Weave,
                    Role = PolicyActionRole.Graded,
                    MinimumLevel = 1,
                    RecastSeconds = 30d,
                    MaximumCharges = 2
                }
            },
            StateInputs = new Dictionary<string, PolicyStateInputDefinition>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["focus"] = new()
                {
                    Kind = PolicyStateValueKind.Resource,
                    Minimum = 0,
                    Maximum = 100
                }
            }
        };
        var state = new TrainingState();
        state.Begin("TST", 100);
        state.SetGauge("focus", 50);
        state.SetCooldown(
            Burst,
            new CooldownSnapshot
            {
                Charges = 1,
                MaximumCharges = 2,
                RemainingSeconds = 30f,
                RechargeSeconds = 30f
            });

        return new Fixture(
            new PracticePlanValidator(),
            new RuleSetTrainingPolicy(definition),
            state);
    }

    private static PracticePlan CreatePlan(
        IReadOnlyList<TrainingForecastStep> steps)
    {
        return new PracticePlan
        {
            Job = "TST",
            StartsAtCombatTimeSeconds = 0d,
            HorizonSeconds = 10d,
            Steps = steps
        };
    }

    private static TrainingForecastStep Step(
        int offset,
        double startsAtSeconds,
        uint gcdActionId,
        IReadOnlyList<uint> suggestedActionIds,
        int resourceBefore,
        int resourceAfter)
    {
        return new TrainingForecastStep
        {
            Offset = offset,
            StartsAtSeconds = startsAtSeconds,
            DurationSeconds = 2.5f,
            Phase = RotationPhase.Filler,
            GcdActionId = gcdActionId,
            SuggestedActionIds = suggestedActionIds,
            ResourceProjections = new Dictionary<string, ResourceProjection>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["focus"] = new()
                {
                    Resource = "focus",
                    Before = resourceBefore,
                    After = resourceAfter
                }
            },
            Confidence = 1f
        };
    }

    private static void RequireError(
        PlanValidationResult result,
        PlanValidationCode code)
    {
        Require(
            result.Issues.Any(issue =>
                issue.Severity == PlanValidationSeverity.Error &&
                issue.Code == code),
            $"Expected validation error {code}, got: " +
            string.Join(
                "; ",
                result.Issues.Select(issue =>
                    $"{issue.Code}: {issue.Message}")));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Fixture(
        PracticePlanValidator Validator,
        RuleSetTrainingPolicy Policy,
        TrainingState State);
}
