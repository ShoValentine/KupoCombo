using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class TrainingSessionValidationSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        FaultsClosedOnIllegalInitialPlan();
        PreservesRibbonOnIllegalPassiveReplan();
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

    private static void PreservesRibbonOnIllegalPassiveReplan()
    {
        var session = new TrainingSession();
        session.Start(new PassiveReplanPolicy(), 100);
        session.RefreshState(state => state.SetStateValue("invalid", 0d));

        Require(
            session.IsActive && session.CurrentForecast.Count > 0,
            "The passive-replan fixture did not begin with a valid ribbon.");

        var originalRibbon = session.CurrentForecast
            .Select(step => step.GcdActionId)
            .ToArray();

        Thread.Sleep(550);
        session.RefreshState(state => state.SetStateValue("invalid", 1d));

        Require(
            session.IsActive && !session.IsFaulted,
            "A rejected passive replan faulted the active session.");
        Require(
            session.CurrentForecast
                .Select(step => step.GcdActionId)
                .SequenceEqual(originalRibbon),
            "A rejected passive replan cleared or replaced the committed ribbon.");
        Require(
            !session.LastRejectedPlanValidation.IsValid &&
            session.LastRejectedPlanValidation.Issues.Any(issue =>
                issue.Code == PlanValidationCode.MissingGcdAction),
            "The passive replan rejection was not retained for diagnostics.");
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
            return CreatePlan(0);
        }

        public IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
            uint actionId,
            TrainingState state)
        {
            return new Dictionary<string, int>();
        }
    }

    private sealed class PassiveReplanPolicy :
        ITrainingPolicy,
        ITrainingForecastPolicy,
        IPracticePlanPolicy
    {
        public string Id => "passive-replan";

        public string Name => "Passive replan";

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

        public IReadOnlyList<TrainingForecastStep> Forecast(
            TrainingState state,
            int maximumGcds)
        {
            return BuildPracticePlan(state).Steps
                .Take(maximumGcds)
                .ToArray();
        }

        public PracticePlan BuildPracticePlan(TrainingState state)
        {
            return CreatePlan(
                state.GetStateValue("invalid") >= 1d
                    ? 0u
                    : 100u);
        }

        public IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
            uint actionId,
            TrainingState state)
        {
            return new Dictionary<string, int>();
        }
    }

    private static PracticePlan CreatePlan(uint gcdActionId)
    {
        return new PracticePlan
        {
            Job = "TST",
            HorizonSeconds = 10d,
            Steps = new[]
            {
                new TrainingForecastStep
                {
                    Offset = 0,
                    StartsAtSeconds = 0d,
                    DurationSeconds = 2.5f,
                    Phase = RotationPhase.Filler,
                    GcdActionId = gcdActionId,
                    Confidence = 1f
                }
            }
        };
    }
}
