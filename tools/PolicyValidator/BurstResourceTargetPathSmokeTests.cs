using System.Runtime.CompilerServices;
using KupoCombo.Services;

internal static class BurstResourceTargetPathSmokeTests
{
    [ModuleInitializer]
    internal static void Run()
    {
        if (Environment.GetCommandLineArgs().Length < 2)
        {
            return;
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"KupoCombo-BurstTargets-{Guid.NewGuid():N}");

        try
        {
            ValidatePackagedRoot(temporaryRoot);
            ValidateDevelopmentParentWalk(temporaryRoot);

            Console.WriteLine(
                "Burst target path smoke test passed: empty runtime roots are ignored, " +
                "and packaged plus development data layouts resolve safely.");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static void ValidatePackagedRoot(string temporaryRoot)
    {
        var pluginRoot = Path.Combine(temporaryRoot, "packaged-plugin");
        var targetDirectory = Path.Combine(pluginRoot, "BurstTargets");
        var expectedPath = Path.Combine(
            targetDirectory,
            "burst-resource-targets.json");

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(expectedPath, "{}");

        var resolvedPath = BurstResourceTargetLoader.ResolveTargetPath(
            new string?[]
            {
                string.Empty,
                "   ",
                pluginRoot
            });

        AssertSamePath(
            expectedPath,
            resolvedPath,
            "The packaged plugin BurstTargets directory was not resolved.");
    }

    private static void ValidateDevelopmentParentWalk(string temporaryRoot)
    {
        var repositoryRoot = Path.Combine(temporaryRoot, "repository");
        var nestedBuildRoot = Path.Combine(
            repositoryRoot,
            "KupoCombo",
            "bin",
            "x64",
            "Debug");
        var targetDirectory = Path.Combine(
            repositoryRoot,
            "Data",
            "BurstTargets");
        var expectedPath = Path.Combine(
            targetDirectory,
            "burst-resource-targets.json");

        Directory.CreateDirectory(nestedBuildRoot);
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(expectedPath, "{}");

        var resolvedPath = BurstResourceTargetLoader.ResolveTargetPath(
            new string?[]
            {
                string.Empty,
                nestedBuildRoot
            });

        AssertSamePath(
            expectedPath,
            resolvedPath,
            "The development Data/BurstTargets parent walk did not resolve.");
    }

    private static void AssertSamePath(
        string expectedPath,
        string actualPath,
        string message)
    {
        if (!Path.GetFullPath(expectedPath).Equals(
                Path.GetFullPath(actualPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{message} Expected '{expectedPath}', got '{actualPath}'.");
        }
    }
}
