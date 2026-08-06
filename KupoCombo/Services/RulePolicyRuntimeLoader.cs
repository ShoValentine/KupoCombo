using System;
using System.IO;
using System.Linq;
using KupoCombo.Models;

namespace KupoCombo.Services;

internal static class RulePolicyRuntimeLoader
{
    public static RulePolicyDefinition LoadBestProfile(
        string job,
        int level,
        int targetCount = 1)
    {
        var normalisedJob = job.Trim().ToUpperInvariant();
        var policyPath = ResolveDataPath(
            "Policies",
            $"{normalisedJob}.json");
        var cataloguePath = ResolveDataPath(
            "Actions",
            Path.Combine("Jobs", $"{normalisedJob}.json"));
        var policies = RulePolicyLoader.Load(policyPath, normalisedJob);
        var catalogue = PveActionCatalogLoader.Load(cataloguePath);

        foreach (var policy in policies)
        {
            PveActionCatalogLoader.Apply(policy, catalogue);
        }

        var effectiveLevel = level > 0
            ? level
            : int.MaxValue;

        var selected = policies
            .Where(policy =>
                effectiveLevel >= policy.MinimumLevel &&
                (!policy.MaximumLevel.HasValue ||
                 effectiveLevel <= policy.MaximumLevel.Value) &&
                targetCount >= policy.Profile.MinimumTargetCount &&
                targetCount <= policy.Profile.MaximumTargetCount)
            .OrderByDescending(policy => policy.MinimumLevel)
            .FirstOrDefault();

        return selected ?? throw new InvalidDataException(
            $"No {normalisedJob} rule policy applies at level {level} " +
            $"for {targetCount} target(s).");
    }

    public static bool TryLoadBestProfile(
        string job,
        int level,
        int targetCount,
        out RulePolicyDefinition? definition)
    {
        try
        {
            definition = LoadBestProfile(job, level, targetCount);
            return true;
        }
        catch (FileNotFoundException)
        {
            definition = null;
            return false;
        }
        catch (InvalidDataException)
        {
            definition = null;
            return false;
        }
    }

    private static string ResolveDataPath(
        string directoryName,
        string fileName)
    {
        var pluginDirectory =
            Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not determine the KupoCombo plugin directory.");

        var directory = new DirectoryInfo(pluginDirectory);

        for (var level = 0; level < 6 && directory != null; level++)
        {
            var developmentPath = Path.Combine(
                directory.FullName,
                "Data",
                directoryName,
                fileName);

            if (File.Exists(developmentPath))
            {
                return developmentPath;
            }

            directory = directory.Parent;
        }

        var packagedPath = Path.Combine(
            pluginDirectory,
            directoryName,
            fileName);

        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

        throw new FileNotFoundException(
            $"No {directoryName} data file was found for {fileName}.",
            packagedPath);
    }
}
