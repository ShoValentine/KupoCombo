using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class TrainingSessionValidationSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        FaultsClosedOnIllegalInitialPlan();
    }

    private static void FaultsClosedOnIllegalInitialPlan()
    {
        var session = new TrainingSession();
        session.Start(new InvalidPlanPolicy(), 100);

        Require(session.IsFaulted, "The session did not enter its faulted state.");
        Require(!session.IsActive, "A faulted session remained active.");
        Require(session.CurrentPlan.IsEmpty, "The illegal plan remained exposed.");
        Require(
            !session.LastRejectedPlanValidation.IsValid &&
            session.LastRejectedPlanValidation.Issues.Any(issue =>
                issue.Code == PlanValidationCode.MissingGcdAction),
            "The rejected plan diagnostics did not identify the missing GCD action.");
        Require(
            session.ProcessAction(100).Outcome == TrainingActionOutcome.Ignored,
            "A faulted session continued processing actions.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class InvalidPlanPolicy :
        ITrainingPolicy,
        IPracticePlanPolicy
    {
        public string Id => "invalid-plan";

        public string Name => "Invalid plan";

        public string Job => "TST";

        public int? ExpectedLength => null;

        public IReadOnlyCollection<uint> TrackedActionIds { get; } =
            new[] { 100u };

        public IReadOnlyCollection<uint> AdvisoryActionIds { get; } =
            Array.Empty<uint>();

        public IReadOnlyCollection<string> TrackedResources { get; } =
            Array.Empty<string>();

        public bool IgnoreUntrackedActions => true;

        public TrainingDecision Evaluate(TrainingState state)
        {
            return new TrainingDecision
            {
                PreferredActionId = 100,
                MistakeResponse = TrainingMistakeResponse.KeepProgress
            };
        }

        public PracticePlan BuildPracticePlan(TrainingState state)
        {
            return new PracticePlan
            {
                Job = Job,
                StartsAtCombatTimeSeconds = state.CombatTimeSeconds,
                HorizonSeconds = 10d,
                Steps = new[]
                {
                    new TrainingForecastStep
                    {
                        Offset = 0,
                        StartsAtSeconds = 0d,
                        DurationSeconds = 2.5f,
                        Phase = RotationPhase.Filler,
                        GcdActionId = 0,
                        Confidence = 1f
                    }
                }
            };
        }

        public IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
            uint actionId,
            TrainingState state)
        {
            return new Dictionary<string, int>();
        }
    }
}
