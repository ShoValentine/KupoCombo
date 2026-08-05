using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class TrainingCommitmentSmokeTests
{
    private const uint PlannedWeave = 50;
    private const uint FirstGcd = 100;
    private const uint SecondGcd = 200;
    private const uint ThirdGcd = 300;
    private const uint DistractingGcd = 999;

    [ModuleInitializer]
    internal static void ValidateCommitment()
    {
        var commandLine = Environment.GetCommandLineArgs();

        if (commandLine.Length < 2)
        {
            return;
        }

        var policy = new FlappingForecastPolicy();
        var session = new TrainingSession();

        session.Start(policy, 100);
        session.RefreshState(_ => { });

        AssertCurrent(session, FirstGcd, PlannedWeave);

        policy.UseDistractingPlan = true;
        session.RefreshState(_ => { });

        AssertCurrent(session, FirstGcd, PlannedWeave);

        var weaveResult = session.ProcessAction(PlannedWeave);

        if (weaveResult.Outcome != TrainingActionOutcome.Suggested ||
            session.CurrentDecision?.SuggestedActionIds.Contains(PlannedWeave) == true)
        {
            throw new InvalidDataException(
                "The committed weave was not consumed cleanly from the current window.");
        }

        session.RefreshState(_ => { });

        if (session.CurrentDecision?.PreferredActionId != FirstGcd ||
            session.CurrentDecision.SuggestedActionIds.Contains(PlannedWeave))
        {
            throw new InvalidDataException(
                "Passive refresh reintroduced a consumed weave or replaced the committed GCD.");
        }

        var gcdResult = session.ProcessAction(FirstGcd);

        if (gcdResult.Outcome != TrainingActionOutcome.Correct ||
            session.CurrentDecision?.PreferredActionId != SecondGcd)
        {
            throw new InvalidDataException(
                "Executing the committed GCD did not advance to the next planned GCD.");
        }

        session.RefreshState(_ => { });

        if (session.CurrentDecision?.PreferredActionId != SecondGcd ||
            session.CurrentForecast.Count < 2 ||
            session.CurrentForecast[1].GcdActionId != ThirdGcd)
        {
            throw new InvalidDataException(
                "Passive refresh broke the rolling two-GCD commitment prefix.");
        }

        if (session.CurrentDecision.PreferredActionId == DistractingGcd)
        {
            throw new InvalidDataException(
                "The session changed its mind during the committed practice window.");
        }

        Console.WriteLine(
            "Training commitment smoke test passed: consumed weaves stay consumed and the rolling two-GCD prefix remains stable across passive refreshes.");
    }

    private static void AssertCurrent(
        TrainingSession session,
        uint expectedGcd,
        uint expectedWeave)
    {
        if (session.CurrentDecision?.PreferredActionId != expectedGcd ||
            !session.CurrentDecision.SuggestedActionIds.Contains(expectedWeave))
        {
            throw new InvalidDataException(
                $"Committed window produced GCD {session.CurrentDecision?.PreferredActionId} " +
                $"with weaves [{string.Join(", ", session.CurrentDecision?.SuggestedActionIds ?? Array.Empty<uint>())}].");
        }
    }

    private sealed class FlappingForecastPolicy :
        ITrainingPolicy,
        ITrainingForecastPolicy
    {
        private static readonly uint[] TrackedActions =
        {
            FirstGcd,
            SecondGcd,
            ThirdGcd,
            DistractingGcd
        };

        public bool UseDistractingPlan { get; set; }

        public string Id => "commitment-smoke-test";

        public string Name => "Commitment smoke test";

        public string Job => "TST";

        public int? ExpectedLength => null;

        public IReadOnlyCollection<uint> TrackedActionIds => TrackedActions;

        public IReadOnlyCollection<uint> AdvisoryActionIds =>
            new[] { PlannedWeave };

        public bool IgnoreUntrackedActions => true;

        public TrainingDecision Evaluate(TrainingState state)
        {
            var step = Math.Clamp(state.AcceptedActionCount, 0, 2);
            var plan = GetPlan();

            return new TrainingDecision
            {
                PreferredActionId = plan[step],
                SuggestedActionIds = step == 0
                    ? new[] { PlannedWeave }
                    : Array.Empty<uint>(),
                Reason = "Commit to the current practice window.",
                SuggestionReason = "Use the planned weave.",
                MistakeResponse = TrainingMistakeResponse.KeepProgress
            };
        }

        public IReadOnlyList<TrainingForecastStep> Forecast(
            TrainingState state,
            int maximumGcds)
        {
            var plan = GetPlan();
            var start = Math.Clamp(state.AcceptedActionCount, 0, plan.Length - 1);

            return plan
                .Skip(start)
                .Take(maximumGcds)
                .Select((actionId, index) => new TrainingForecastStep
                {
                    Offset = index,
                    GcdActionId = actionId,
                    SuggestedActionIds = start == 0 && index == 0
                        ? new[] { PlannedWeave }
                        : Array.Empty<uint>(),
                    Reason = "Commit to the current practice window.",
                    SuggestionReason = "Use the planned weave.",
                    Confidence = 1f
                })
                .ToArray();
        }

        private uint[] GetPlan()
        {
            return UseDistractingPlan
                ? new[] { DistractingGcd, 888u, 777u }
                : new[] { FirstGcd, SecondGcd, ThirdGcd };
        }
    }
}
