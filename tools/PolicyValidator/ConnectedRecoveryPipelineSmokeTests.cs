using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class ConnectedRecoveryPipelineSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        DefersRecoveryUntilLiveStateSettles();

        Console.WriteLine(
            "Training sessions deferred mistakes until live state settled, " +
            "adopted the legal recovery route, and cleared recovery on rejoin.");
    }

    private static void DefersRecoveryUntilLiveStateSettles()
    {
        var policy = new RecoverySmokePolicy();
        var session = new TrainingSession();
        session.Start(policy, 100);
        session.RefreshState(state => state.SetStateValue("route", 0d));

        var originalActions = session.CurrentPlan.Steps
            .Select(step => step.GcdActionId)
            .ToArray();

        var mistake = session.ProcessAction(90);

        Require(
            mistake.Outcome == TrainingActionOutcome.Incorrect,
            "The recovery smoke action should begin as a mistake.");
        Require(
            session.IsRecoveryPending &&
            !session.CurrentRecoveryPlan.IsAvailable,
            "The session should defer recovery until live state settles.");
        Require(
            session.CurrentPlan.Steps
                .Select(step => step.GcdActionId)
                .SequenceEqual(originalActions),
            "The committed plan moved before the post-action state settled.");

        session.RefreshState(state => state.SetStateValue("route", 1d));

        Require(
            session.IsRecoveryPending,
            "One live refresh should not resolve a recovery route yet.");

        session.RefreshState(state => state.SetStateValue("route", 1d));

        Require(
            !session.IsRecoveryPending &&
            session.IsRecovering &&
            session.RecoveryStepsRemaining == 2,
            "The settled state did not activate the guided recovery route.");
        Require(
            session.CurrentRecoveryPlan.Disposition ==
                RecoveryPlanDisposition.GuidedRecovery,
            "The connected session should expose a guided recovery plan.");
        Require(
            session.CurrentRecoveryPlan.RecoverySteps
                .Select(step => step.GcdActionId)
                .SequenceEqual(new uint[] { 90, 91 }),
            "The connected recovery prefix was not the shortest legal route.");
        Require(
            session.CurrentDecision?.PreferredActionId == 90 &&
            session.CurrentPlan.Steps.First().GcdActionId == 90,
            "The legal recovery plan was not adopted as the authoritative route.");

        var firstRecoveryAction = session.ProcessAction(90);

        Require(
            firstRecoveryAction.Outcome == TrainingActionOutcome.Correct &&
            session.IsRecovering &&
            session.RecoveryStepsRemaining == 1 &&
            session.CurrentDecision?.PreferredActionId == 91,
            "The first recovery GCD did not advance recovery progress.");

        var secondRecoveryAction = session.ProcessAction(91);

        Require(
            secondRecoveryAction.Outcome == TrainingActionOutcome.Correct &&
            !session.IsRecovering &&
            !session.CurrentRecoveryPlan.IsAvailable &&
            session.CurrentDecision?.PreferredActionId == 30,
            "The session did not clear recovery after rejoining the stable route.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed class RecoverySmokePolicy :
        ITrainingPolicy,
        ITrainingForecastPolicy,
        IPracticePlanPolicy
    {
        private static readonly uint[] AllActions =
        {
            10,
            20,
            30,
            40,
            50,
            90,
            91
        };

        public string Id => "connected-recovery-smoke";

        public string Name => "Connected recovery smoke policy";

        public string Job => "TEST";

        public int? ExpectedLength => null;

        public IReadOnlyCollection<uint> TrackedActionIds => AllActions;

        public IReadOnlyCollection<uint> AdvisoryActionIds =>
            Array.Empty<uint>();

        public IReadOnlyCollection<string> TrackedResources =>
            Array.Empty<string>();

        public bool IgnoreUntrackedActions => true;

        public TrainingDecision Evaluate(TrainingState state)
        {
            return new TrainingDecision
            {
                PreferredActionId = state.GetStateValue("route") >= 1d
                    ? 90u
                    : 10u,
                MistakeResponse = TrainingMistakeResponse.KeepProgress,
                Reason = "Follow the active smoke-test route."
            };
        }

        public PracticePlan BuildPracticePlan(TrainingState state)
        {
            var actions = state.GetStateValue("route") >= 1d
                ? new uint[] { 90, 91, 30, 40, 50 }
                : new uint[] { 10, 20, 30, 40, 50 };

            return new PracticePlan
            {
                Job = Job,
                HorizonSeconds = actions.Length * 2.5d,
                Steps = actions
                    .Select(
                        (actionId, index) => new TrainingForecastStep
                        {
                            Offset = index,
                            StartsAtSeconds = index * 2.5d,
                            DurationSeconds = 2.5f,
                            Phase = index < 2
                                ? RotationPhase.Recovery
                                : RotationPhase.Filler,
                            GcdActionId = actionId,
                            Reason = "Smoke-test route step."
                        })
                    .ToArray()
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

        public IReadOnlyDictionary<string, int> GetExpectedResourceDeltas(
            uint actionId,
            TrainingState state)
        {
            return new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
