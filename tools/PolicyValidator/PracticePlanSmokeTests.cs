using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class PracticePlanSmokeTests
{
    private const uint Unmend = 3624;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint LivingShadow = 16472;
    private const uint Delirium = 7390;
    private const uint CarveAndSpit = 3639;
    private const uint Shadowbringer = 25757;
    private const uint SaltedEarth = 3643;

    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(arguments[1]);
        var dataDirectory = Directory.GetParent(policyDirectory);

        if (dataDirectory == null)
        {
            return;
        }

        var policyPath = Path.Combine(policyDirectory, "DRK.json");
        var cataloguePath = Path.Combine(
            dataDirectory.FullName,
            "Actions",
            "pve-actions.json");

        if (!File.Exists(policyPath) || !File.Exists(cataloguePath))
        {
            return;
        }

        var catalogue = PveActionCatalogLoader.Load(cataloguePath);
        var definition = RulePolicyLoader
            .Load(policyPath, "DRK")
            .Single(policy => policy.MinimumLevel <= 100);
        PveActionCatalogLoader.Apply(definition, catalogue);

        var policy = new RuleSetTrainingPolicy(definition);
        var state = CreateState(definition);
        var plan = policy.BuildPracticePlan(state);

        if (plan.Steps.Count <= 12)
        {
            throw new InvalidDataException(
                $"Practice plan contained only {plan.Steps.Count} steps.");
        }

        if (Math.Abs(plan.HorizonSeconds - 120d) > 0.001d)
        {
            throw new InvalidDataException(
                $"Expected a 120-second plan, got {plan.HorizonSeconds:0.###}.");
        }

        if (Math.Abs(plan.Steps[0].DurationSeconds - 2.4f) > 0.001f)
        {
            throw new InvalidDataException(
                "Player-adjusted 2.40-second GCD was not used by the plan.");
        }

        if (plan.Steps[0].Phase != RotationPhase.Opener ||
            !plan.Steps.Any(step => step.Phase == RotationPhase.Filler) ||
            !plan.Steps.Any(step => step.Phase == RotationPhase.Pooling) ||
            !plan.Steps.Any(step => step.Phase == RotationPhase.Burst))
        {
            throw new InvalidDataException(
                "Practice plan did not distinguish opener, filler, pooling, and burst phases.");
        }

        var finalStep = plan.Steps[^1];

        if (finalStep.StartsAtSeconds >= 120d ||
            finalStep.StartsAtSeconds + finalStep.DurationSeconds < 119d)
        {
            throw new InvalidDataException(
                "Practice plan did not cover the full burst cycle.");
        }

        var openingEdgeStep = plan.Steps.FirstOrDefault(step =>
            step.SuggestedActionIds.Contains(EdgeOfShadow));

        if (openingEdgeStep == null ||
            openingEdgeStep.ExpectedMpBefore != 6000 ||
            openingEdgeStep.ExpectedMpAfter != 3000)
        {
            throw new InvalidDataException(
                "Opening Edge MP projection was not attached to its weave window.");
        }

        ValidateLiveSession(policy, definition);

        Console.WriteLine(
            "Practice plan smoke test passed: 120-second player-timed plan, " +
            "explicit phases, stable live timeline, MP projection, and delayed live MP attribution are active.");
    }

    private static void ValidateLiveSession(
        RuleSetTrainingPolicy policy,
        RulePolicyDefinition definition)
    {
        var session = new TrainingSession();
        session.Start(policy, 100);
        session.RefreshState(state => CopyState(CreateState(definition), state));
        session.ProcessAction(Unmend);

        if (Math.Abs(session.Snapshot.CombatTimeSeconds - 2.4d) > 0.001d)
        {
            throw new InvalidDataException(
                "Live practice time did not advance by the player's adjusted GCD.");
        }

        session.ObserveAction(EdgeOfShadow);
        session.RefreshState(state => state.SetGauge("mp", 6000));

        if (session.MpTransactions.Any(transaction =>
                transaction.Kind == MpTransactionKind.ActionWindow &&
                transaction.ActionIds.Contains(EdgeOfShadow)))
        {
            throw new InvalidDataException(
                "Edge was closed before its delayed MP spend arrived.");
        }

        session.ProcessAction(EdgeOfShadow);
        session.RefreshState(state => state.SetGauge("mp", 3000));

        var actionTransaction = session.MpTransactions.LastOrDefault();

        if (actionTransaction == null ||
            actionTransaction.Kind != MpTransactionKind.ActionWindow ||
            !actionTransaction.ActionIds.SequenceEqual(new[] { EdgeOfShadow }) ||
            actionTransaction.ExpectedDelta != -3000 ||
            actionTransaction.ObservedDelta != -3000 ||
            actionTransaction.UnattributedDelta != 0)
        {
            throw new InvalidDataException(
                "Delayed Edge MP spend was not attributed to the detected action.");
        }

        session.RefreshState(state => state.SetGauge("mp", 3200));

        if (!session.MpTransactions.Any(transaction =>
                transaction.Kind == MpTransactionKind.PassiveRecovery &&
                transaction.ObservedDelta == 200))
        {
            throw new InvalidDataException(
                "Passive MP recovery was not separated from action MP movement.");
        }
    }

    private static TrainingState CreateState(
        RulePolicyDefinition definition)
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        state.SetGauge("blood", 0);
        state.SetGauge("mp", 6000);
        state.SetGauge("darkside_ms", 30000);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);
        state.SetStateValue("blood_weapon_stacks", 0);
        state.SetPlayerTiming(1234, 567, 0);

        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);

        foreach (var action in definition.Actions.Values)
        {
            if (action.Lane == PolicyLane.Gcd)
            {
                state.SetAdjustedRecastSeconds(action.ActionId, 2.4f);
            }
        }

        state.SetCooldown(Delirium, ReadyCooldown(60f));
        state.SetCooldown(LivingShadow, ReadyCooldown(120f));
        state.SetCooldown(CarveAndSpit, ReadyCooldown(60f));
        state.SetCooldown(Shadowbringer, ReadyCooldown(60f, 2));
        state.SetCooldown(SaltedEarth, ReadyCooldown(90f));
        return state;
    }

    private static void CopyState(
        TrainingState source,
        TrainingState destination)
    {
        destination.SetLevel(source.Level);
        destination.SetPlayerTiming(
            source.TimingProfile.SkillSpeed,
            source.TimingProfile.SpellSpeed,
            source.TimingProfile.Haste);

        foreach (var gauge in source.Gauges)
        {
            destination.SetGauge(gauge.Key, gauge.Value);
        }

        foreach (var stateValue in source.StateValues)
        {
            destination.SetStateValue(stateValue.Key, stateValue.Value);
        }

        foreach (var adjustedAction in source.AdjustedActions)
        {
            destination.SetAdjustedAction(
                adjustedAction.Key,
                adjustedAction.Value);
        }

        foreach (var adjustedRecast in source.AdjustedRecastSeconds)
        {
            destination.SetAdjustedRecastSeconds(
                adjustedRecast.Key,
                adjustedRecast.Value);
        }

        foreach (var cooldown in source.Cooldowns)
        {
            destination.SetCooldown(cooldown.Key, cooldown.Value);
        }
    }

    private static CooldownSnapshot ReadyCooldown(
        float rechargeSeconds,
        int maximumCharges = 1)
    {
        return new CooldownSnapshot
        {
            Charges = maximumCharges,
            MaximumCharges = maximumCharges,
            RechargeSeconds = rechargeSeconds
        };
    }
}
