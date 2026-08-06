using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class ActionResolutionPipelineSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        ResolvesClientAdjustedPreferredAction();
        PrefersExactIdentityOverAdjustedAlias();
        LeavesUnknownActionUnresolved();

        Console.WriteLine(
            "Action resolution pipeline preserved authoritative execution IDs " +
            "across exact, adjusted, and unresolved observations.");
    }

    private static void ResolvesClientAdjustedPreferredAction()
    {
        const uint baseAction = 7392;
        const uint transformedAction = 36928;

        var state = CreateState();
        state.SetAdjustedAction(baseAction, transformedAction);

        var decision = new TrainingDecision
        {
            PreferredActionId = baseAction
        };
        var policy = new StubPolicy(
            trackedActionIds: new[] { baseAction });
        var observation = ActionObservation.ClientExecution(transformedAction);

        var resolution = new ActionResolutionPipeline().Resolve(
            observation,
            decision,
            policy,
            state);

        Require(
            resolution.Kind == ActionResolutionKind.ClientAdjusted &&
            resolution.Role == ActionResolutionRole.Preferred &&
            resolution.ExecutedActionId == transformedAction &&
            resolution.PolicyActionId == baseAction,
            "A transformed preferred action did not resolve to its policy identity.");
    }

    private static void PrefersExactIdentityOverAdjustedAlias()
    {
        const uint preferredBaseAction = 100;
        const uint exactAcceptableAction = 200;

        var state = CreateState();
        state.SetAdjustedAction(
            preferredBaseAction,
            exactAcceptableAction);

        var decision = new TrainingDecision
        {
            PreferredActionId = preferredBaseAction,
            AcceptableActionIds = new[] { exactAcceptableAction }
        };
        var policy = new StubPolicy(
            trackedActionIds: new[]
            {
                preferredBaseAction,
                exactAcceptableAction
            });
        var observation = ActionObservation.ClientExecution(
            exactAcceptableAction);

        var resolution = new ActionResolutionPipeline().Resolve(
            observation,
            decision,
            policy,
            state);

        Require(
            resolution.Kind == ActionResolutionKind.Exact &&
            resolution.Role == ActionResolutionRole.Acceptable &&
            resolution.PolicyActionId == exactAcceptableAction,
            "An exact action identity lost precedence to an adjusted alias.");
    }

    private static void LeavesUnknownActionUnresolved()
    {
        const uint unknownAction = 999999;

        var state = CreateState();
        var decision = new TrainingDecision
        {
            PreferredActionId = 100
        };
        var policy = new StubPolicy(
            trackedActionIds: new[] { 100u });
        var observation = ActionObservation.ClientExecution(unknownAction);

        var resolution = new ActionResolutionPipeline().Resolve(
            observation,
            decision,
            policy,
            state);

        Require(
            resolution.Kind == ActionResolutionKind.Unresolved &&
            resolution.Role == ActionResolutionRole.None &&
            resolution.PolicyActionId == unknownAction,
            "An unknown action should remain unchanged and unresolved.");
    }

    private static TrainingState CreateState()
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        return state;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed class StubPolicy : ITrainingPolicy
    {
        public StubPolicy(
            IReadOnlyCollection<uint> trackedActionIds,
            IReadOnlyCollection<uint>? advisoryActionIds = null)
        {
            TrackedActionIds = trackedActionIds;
            AdvisoryActionIds = advisoryActionIds ?? Array.Empty<uint>();
        }

        public string Id => "action-resolution-smoke";

        public string Name => "Action resolution smoke policy";

        public string Job => "DRK";

        public int? ExpectedLength => null;

        public IReadOnlyCollection<uint> TrackedActionIds { get; }

        public IReadOnlyCollection<uint> AdvisoryActionIds { get; }

        public bool IgnoreUntrackedActions => true;

        public TrainingDecision Evaluate(TrainingState state)
        {
            return TrainingDecision.Complete("Not used by this smoke test.");
        }
    }
}
