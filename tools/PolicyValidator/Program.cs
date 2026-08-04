using KupoCombo.Services;

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "Usage: PolicyValidator <policy-directory>");
    return 2;
}

var policyDirectory = Path.GetFullPath(args[0]);

if (!Directory.Exists(policyDirectory))
{
    Console.Error.WriteLine(
        $"Policy directory not found: {policyDirectory}");
    return 2;
}

var policyFiles = Directory
    .EnumerateFiles(policyDirectory, "*.json", SearchOption.TopDirectoryOnly)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (policyFiles.Length == 0)
{
    Console.Error.WriteLine(
        $"No policy files found in {policyDirectory}");
    return 2;
}

var failed = false;

foreach (var policyFile in policyFiles)
{
    try
    {
        var expectedJob = Path.GetFileNameWithoutExtension(policyFile);
        var policies = RulePolicyLoader.Load(policyFile, expectedJob);

        Console.WriteLine(
            $"Validated {Path.GetFileName(policyFile)}: " +
            $"{policies.Count} policy profile(s).");
    }
    catch (Exception exception)
    {
        failed = true;
        Console.Error.WriteLine(
            $"Policy validation failed for {policyFile}: " +
            exception.Message);
    }
}

return failed ? 1 : 0;
