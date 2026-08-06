using System.Runtime.CompilerServices;
using KupoCombo.Services;

internal static class CompleteActionCatalogueSmokeTests
{
    private static readonly string[] ExpectedJobs =
    {
        "PLD", "WAR", "DRK", "GNB",
        "WHM", "SCH", "AST", "SGE",
        "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
        "BRD", "MCH", "DNC",
        "BLM", "SMN", "RDM", "PCT", "BLU"
    };

    private static readonly uint[] RequiredDarkKnightTransformations =
    {
        36928, 36929, 36930, 36932
    };

    private static readonly uint[] RequiredMachinistTransformations =
    {
        36978, 36979, 36980, 36981, 36982
    };

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

        var actionsDirectory = Path.Combine(
            dataDirectory.FullName,
            "Actions");
        var jobsDirectory = Path.Combine(actionsDirectory, "Jobs");

        if (!Directory.Exists(jobsDirectory))
        {
            throw new InvalidDataException(
                $"Per-job action catalogue directory not found: {jobsDirectory}");
        }

        var aggregate = PveActionCatalogLoader.Load(
            Path.Combine(actionsDirectory, "pve-actions.json"));
        var aggregateIds = aggregate.Actions
            .Select(action => action.ActionId)
            .ToHashSet();
        var unionIds = new HashSet<uint>();
        var observedJobs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var catalogues = new Dictionary<string, KupoCombo.Models.PveActionCatalogFile>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory
                     .EnumerateFiles(jobsDirectory, "*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var job = Path.GetFileNameWithoutExtension(path)
                .ToUpperInvariant();
            var catalogue = PveActionCatalogLoader.Load(path);

            if (!observedJobs.Add(job))
            {
                throw new InvalidDataException(
                    $"Duplicate per-job action catalogue for {job}.");
            }

            if (!catalogue.GameVersion.Equals(
                    aggregate.GameVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"{job} catalogue targets {catalogue.GameVersion}, but the " +
                    $"aggregate targets {aggregate.GameVersion}.");
            }

            if (catalogue.Actions.Any(action =>
                    !action.Job.Equals(job, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"{job}.json contains an action labelled for another job.");
            }

            unionIds.UnionWith(catalogue.Actions.Select(action => action.ActionId));
            catalogues[job] = catalogue;
        }

        var missingJobs = ExpectedJobs
            .Where(job => !observedJobs.Contains(job))
            .ToArray();
        var unexpectedJobs = observedJobs
            .Where(job => !ExpectedJobs.Contains(
                job,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingJobs.Length > 0 || unexpectedJobs.Length > 0)
        {
            throw new InvalidDataException(
                $"Per-job action catalogue set is incorrect. Missing: " +
                $"{string.Join(", ", missingJobs)}; unexpected: " +
                $"{string.Join(", ", unexpectedJobs)}.");
        }

        if (!aggregateIds.SetEquals(unionIds))
        {
            var absentFromAggregate = unionIds.Except(aggregateIds).Take(10);
            var absentFromJobs = aggregateIds.Except(unionIds).Take(10);

            throw new InvalidDataException(
                "The aggregate and per-job action catalogues disagree. " +
                $"Missing from aggregate: {string.Join(", ", absentFromAggregate)}; " +
                $"missing from jobs: {string.Join(", ", absentFromJobs)}.");
        }

        RequireActions(
            catalogues["DRK"],
            RequiredDarkKnightTransformations,
            "DRK transformed chain");
        RequireActions(
            catalogues["MCH"],
            RequiredMachinistTransformations,
            "MCH transformed actions");

        Console.WriteLine(
            $"Complete action catalogue smoke test passed: " +
            $"{aggregate.Actions.Count} unique {aggregate.GameVersion} PvE actions " +
            $"cover {ExpectedJobs.Length} independently valid job catalogues.");
    }

    private static void RequireActions(
        KupoCombo.Models.PveActionCatalogFile catalogue,
        IEnumerable<uint> requiredActionIds,
        string description)
    {
        var available = catalogue.Actions
            .Select(action => action.ActionId)
            .ToHashSet();
        var missing = requiredActionIds
            .Where(actionId => !available.Contains(actionId))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"{description} is incomplete. Missing action IDs: " +
                string.Join(", ", missing));
        }
    }
}
