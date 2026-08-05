using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class FullDarkKnightOpenerSmokeTest
{
    private const uint Unmend = 3624;
    private const uint HardSlash = 3617;
    private const uint SyphonStrike = 3623;
    private const uint Souleater = 3632;
    private const uint Delirium = 7390;
    private const uint Bloodspiller = 7392;
    private const uint EdgeOfDarkness = 16467;
    private const uint EdgeOfShadow = 16470;
    private const uint LivingShadow = 16472;
    private const uint CarveAndSpit = 3639;
    private const uint SaltedEarth = 3643;
    private const uint SaltAndDarkness = 25755;
    private const uint Shadowbringer = 25757;
    private const uint ScarletDelirium = 36928;
    private const uint Comeuppance = 36929;
    private const uint Torcleaver = 36930;
    private const uint Disesteem = 36932;

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
        var ribbon = policy
            .Forecast(CreateState(), 12)
            .SelectMany(step =>
                step.SuggestedActionIds.Concat(new[] { step.GcdActionId }))
            .ToArray();

        var expectedOpener = new uint[]
        {
            Unmend,
            EdgeOfShadow,
            HardSlash,
            LivingShadow,
            SyphonStrike,
            Souleater,
            Delirium,
            Disesteem,
            CarveAndSpit,
            EdgeOfShadow,
            ScarletDelirium,
            Shadowbringer,
            EdgeOfShadow,
            Comeuppance,
            SaltedEarth,
            EdgeOfShadow,
            Torcleaver,
            Shadowbringer,
            Bloodspiller,
            SaltAndDarkness
        };

        if (ribbon.Length < expectedOpener.Length ||
            !ribbon.Take(expectedOpener.Length).SequenceEqual(expectedOpener))
        {
            throw new InvalidDataException(
                "Complete DRK opener forecast diverged. Expected " +
                $"[{string.Join(", ", expectedOpener)}], got " +
                $"[{string.Join(", ", ribbon)}].");
        }

        Console.WriteLine(
            "Complete DRK opener forecast passed, including both " +
            "Shadowbringer charges and Salt and Darkness.");
    }

    private static TrainingState CreateState()
    {
        var state = new TrainingState();
        state.Begin("DRK", 100);
        state.SetGauge("blood", 0);
        state.SetGauge("mp", 6000);
        state.SetGauge("darkside_ms", 30000);
        state.SetGauge("dark_arts", 0);
        state.SetGauge("delirium_step", 0);

        state.SetAdjustedAction(Bloodspiller, Bloodspiller);
        state.SetAdjustedAction(EdgeOfDarkness, EdgeOfShadow);
        state.SetAdjustedAction(LivingShadow, LivingShadow);
        state.SetAdjustedAction(SaltedEarth, SaltedEarth);

        state.SetCooldown(Delirium, ReadyCooldown(60f));
        state.SetCooldown(LivingShadow, ReadyCooldown(120f));
        state.SetCooldown(CarveAndSpit, ReadyCooldown(60f));
        state.SetCooldown(Shadowbringer, ReadyCooldown(60f, 2));
        state.SetCooldown(SaltedEarth, ReadyCooldown(90f));
        return state;
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
