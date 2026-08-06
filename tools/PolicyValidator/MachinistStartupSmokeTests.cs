using System.Runtime.CompilerServices;
using KupoCombo.Models;
using KupoCombo.Services;

internal static class MachinistStartupSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();

        if (arguments.Length < 2)
        {
            return;
        }

        var policyDirectory = Path.GetFullPath(arguments[^1]);
        var dataDirectory = Directory.GetParent(policyDirectory);

        if (dataDirectory == null)
        {
            return;
        }

        var definition = RulePolicyLoader
            .Load(Path.Combine(policyDirectory, "MCH.json"), "MCH")
            .Single();
        var catalogue = PveActionCatalogLoader.Load(
            Path.Combine(
                dataDirectory.FullName,
                "Actions",
                "pve-actions.json"));
        PveActionCatalogLoader.Apply(definition, catalogue);

        var session = new TrainingSession();
        session.Start(new RuleSetTrainingPolicy(definition), 100);

        if (session.IsFaulted)
        {
            throw new InvalidDataException(
                "MCH priority practice faulted before its first live-state refresh: " +
                session.LastRejectedPlanValidation.Summary);
        }

        if (session.CurrentForecast.Count == 0)
        {
            throw new InvalidDataException(
                "MCH priority practice started without an initial forecast.");
        }

        Console.WriteLine(
            "MCH startup smoke test passed: the priority session arms before live state without an empty overlay.");
    }
}
