using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KupoCombo.Models;

public readonly record struct BurstTimelineAlignment(
    string PolicyId,
    uint AnchorActionId,
    double CycleSeconds,
    double TimelineCombatTimeSeconds,
    double EffectiveCombatTimeSeconds,
    double SecondsSinceBurst,
    double SecondsUntilBurst,
    bool AnchorIsReady)
{
    public double DriftSeconds =>
        EffectiveCombatTimeSeconds - TimelineCombatTimeSeconds;

    public int ObservedAnchorCount { get; init; }

    public int AgreementCount { get; init; }

    public double AnchorSpreadSeconds { get; init; }

    public bool UsedPrimaryAnchor { get; init; }
}

internal sealed class BurstTimelineProfileFile
{
    public List<BurstTimelineProfileDefinition> Policies { get; set; } = new();
}

internal sealed class BurstTimelineProfileDefinition
{
    public string PolicyId { get; set; } = string.Empty;

    public string Job { get; set; } = string.Empty;

    public int MinimumLevel { get; set; }

    public int? MaximumLevel { get; set; }

    public int? MinorBurstCycleSeconds { get; set; }

    public List<uint> BurstAnchorActionIds { get; set; } = new();

    public List<BurstTimelineAnchorDefinition> BurstAnchors { get; set; } = new();

    public IReadOnlyList<BurstTimelineAnchorDefinition> ResolveAnchors()
    {
        var configured = BurstAnchors
            .Where(anchor => anchor.ActionId != 0)
            .GroupBy(anchor => anchor.ActionId)
            .Select(group => group.First())
            .ToArray();

        if (configured.Length > 0)
        {
            return configured;
        }

        return BurstAnchorActionIds
            .Where(actionId => actionId != 0)
            .Distinct()
            .Select(actionId => new BurstTimelineAnchorDefinition
            {
                ActionId = actionId
            })
            .ToArray();
    }
}

internal sealed class BurstTimelineAnchorDefinition
{
    public uint ActionId { get; set; }

    public int? CycleSeconds { get; set; }
}

internal static class BurstTimelineProfileRegistry
{
    private const string FileName = "burst-resource-targets.json";
    private const double AnchorAgreementToleranceSeconds = 3d;

    private static readonly Lazy<IReadOnlyList<BurstTimelineProfileDefinition>>
        Profiles = new(LoadProfiles);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryAlign(
        string job,
        int level,
        double timelineCombatTimeSeconds,
        IReadOnlyDictionary<uint, CooldownSnapshot> cooldowns,
        double? anchorReadySinceTimelineSeconds,
        out BurstTimelineAlignment alignment)
    {
        alignment = default;

        if (!TryGetProfile(job, level, out var profile))
        {
            return false;
        }

        var anchors = profile.ResolveAnchors();

        if (anchors.Count == 0)
        {
            return false;
        }

        var cycleSeconds = Math.Max(
            1d,
            profile.MinorBurstCycleSeconds ?? 120);
        var timeline = Math.Max(0d, timelineCombatTimeSeconds);
        var candidates = new List<AnchorCandidate>();
        var observedAnchorCount = 0;

        for (var index = 0; index < anchors.Count; index++)
        {
            var anchor = anchors[index];

            if (!cooldowns.TryGetValue(anchor.ActionId, out var cooldown))
            {
                continue;
            }

            observedAnchorCount++;
            AddAnchorCandidates(
                candidates,
                index,
                anchor,
                cooldown,
                cycleSeconds,
                timeline,
                index == 0
                    ? anchorReadySinceTimelineSeconds
                    : null);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var cluster = SelectBestCluster(candidates, timeline);
        var effectiveTime = Median(
            cluster.Candidates.Select(candidate => candidate.EffectiveTime));
        var representative = cluster.Candidates
            .OrderBy(candidate =>
                Math.Abs(candidate.EffectiveTime - effectiveTime))
            .ThenBy(candidate => candidate.AnchorIndex)
            .First();
        var phaseSeconds = PositiveModulo(effectiveTime, cycleSeconds);
        var anchorIsReady = representative.IsReady;
        var secondsSinceBurst = phaseSeconds;
        var secondsUntilBurst = anchorIsReady
            ? 0d
            : phaseSeconds <= 0.001d
                ? cycleSeconds
                : cycleSeconds - phaseSeconds;
        var spread = cluster.Candidates.Count <= 1
            ? 0d
            : cluster.Candidates.Max(candidate => candidate.EffectiveTime) -
              cluster.Candidates.Min(candidate => candidate.EffectiveTime);

        alignment = new BurstTimelineAlignment(
            profile.PolicyId,
            representative.ActionId,
            cycleSeconds,
            timeline,
            effectiveTime,
            secondsSinceBurst,
            secondsUntilBurst,
            anchorIsReady)
        {
            ObservedAnchorCount = observedAnchorCount,
            AgreementCount = cluster.Candidates.Count,
            AnchorSpreadSeconds = spread,
            UsedPrimaryAnchor = cluster.Candidates.Any(candidate =>
                candidate.AnchorIndex == 0)
        };
        return true;
    }

    public static bool IsPrimaryAnchor(
        string job,
        int level,
        uint actionId)
    {
        return TryGetProfile(job, level, out var profile) &&
            profile.ResolveAnchors().FirstOrDefault()?.ActionId == actionId;
    }

    private static void AddAnchorCandidates(
        ICollection<AnchorCandidate> candidates,
        int anchorIndex,
        BurstTimelineAnchorDefinition anchor,
        CooldownSnapshot cooldown,
        double profileCycleSeconds,
        double timeline,
        double? readySinceTimelineSeconds)
    {
        var anchorCycleSeconds = Math.Clamp(
            anchor.CycleSeconds ?? (int)profileCycleSeconds,
            1,
            (int)Math.Ceiling(profileCycleSeconds));
        var occurrenceCount = Math.Max(
            1,
            (int)Math.Ceiling(profileCycleSeconds / anchorCycleSeconds));
        double elapsedInAnchorCycle;

        if (cooldown.IsReady)
        {
            elapsedInAnchorCycle = readySinceTimelineSeconds.HasValue
                ? Math.Max(0d, timeline - readySinceTimelineSeconds.Value)
                : 0d;
        }
        else
        {
            var remaining = Math.Clamp(
                cooldown.RemainingSeconds,
                0f,
                (float)anchorCycleSeconds);
            elapsedInAnchorCycle = remaining <= 0.001f
                ? 0d
                : anchorCycleSeconds - remaining;
        }

        for (var occurrence = 0; occurrence < occurrenceCount; occurrence++)
        {
            var phaseSeconds = PositiveModulo(
                elapsedInAnchorCycle +
                (occurrence * anchorCycleSeconds),
                profileCycleSeconds);
            var cycleIndex = Math.Max(
                0d,
                Math.Round(
                    (timeline - phaseSeconds) / profileCycleSeconds,
                    MidpointRounding.AwayFromZero));
            var effectiveTime = phaseSeconds +
                (cycleIndex * profileCycleSeconds);

            candidates.Add(new AnchorCandidate(
                anchorIndex,
                anchor.ActionId,
                effectiveTime,
                cooldown.IsReady));
        }
    }

    private static AnchorCluster SelectBestCluster(
        IReadOnlyList<AnchorCandidate> candidates,
        double timeline)
    {
        return candidates
            .Select(seed => BuildCluster(seed, candidates))
            .OrderByDescending(cluster => cluster.Candidates.Count)
            .ThenByDescending(cluster => cluster.Candidates.Any(candidate =>
                candidate.AnchorIndex == 0))
            .ThenBy(cluster => cluster.SpreadSeconds)
            .ThenBy(cluster => Math.Abs(cluster.CentreSeconds - timeline))
            .ThenBy(cluster => cluster.Candidates.Min(candidate =>
                candidate.AnchorIndex))
            .First();
    }

    private static AnchorCluster BuildCluster(
        AnchorCandidate seed,
        IReadOnlyList<AnchorCandidate> candidates)
    {
        var selected = candidates
            .Where(candidate =>
                Math.Abs(candidate.EffectiveTime - seed.EffectiveTime) <=
                AnchorAgreementToleranceSeconds)
            .GroupBy(candidate => candidate.ActionId)
            .Select(group => group
                .OrderBy(candidate =>
                    Math.Abs(candidate.EffectiveTime - seed.EffectiveTime))
                .ThenBy(candidate => candidate.AnchorIndex)
                .First())
            .ToArray();

        return new AnchorCluster(selected);
    }

    private static bool TryGetProfile(
        string job,
        int level,
        out BurstTimelineProfileDefinition profile)
    {
        profile = Profiles.Value
            .Where(candidate =>
                candidate.Job.Equals(
                    job,
                    StringComparison.OrdinalIgnoreCase) &&
                level >= candidate.MinimumLevel &&
                (!candidate.MaximumLevel.HasValue ||
                 level <= candidate.MaximumLevel.Value) &&
                candidate.ResolveAnchors().Count > 0)
            .OrderByDescending(candidate => candidate.MinimumLevel)
            .FirstOrDefault()!;

        return profile != null;
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static double PositiveModulo(double value, double modulus)
    {
        var result = value % modulus;
        return result < 0d
            ? result + modulus
            : result;
    }

    private static IReadOnlyList<BurstTimelineProfileDefinition> LoadProfiles()
    {
        var path = FindDataFile();

        if (path == null)
        {
            return Array.Empty<BurstTimelineProfileDefinition>();
        }

        var file = JsonSerializer.Deserialize<BurstTimelineProfileFile>(
            File.ReadAllText(path),
            JsonOptions);

        return file?.Policies
            .Where(profile =>
                !string.IsNullOrWhiteSpace(profile.PolicyId) &&
                !string.IsNullOrWhiteSpace(profile.Job) &&
                profile.ResolveAnchors().Count > 0)
            .ToArray() ??
            Array.Empty<BurstTimelineProfileDefinition>();
    }

    private static string? FindDataFile()
    {
        var candidates = new[]
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "BurstTargets",
                FileName),
            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "BurstTargets",
                FileName),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "Data",
                "BurstTargets",
                FileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed record AnchorCandidate(
        int AnchorIndex,
        uint ActionId,
        double EffectiveTime,
        bool IsReady);

    private sealed class AnchorCluster
    {
        public AnchorCluster(IReadOnlyList<AnchorCandidate> candidates)
        {
            Candidates = candidates;
            CentreSeconds = Median(candidates.Select(candidate =>
                candidate.EffectiveTime));
            SpreadSeconds = candidates.Count <= 1
                ? 0d
                : candidates.Max(candidate => candidate.EffectiveTime) -
                  candidates.Min(candidate => candidate.EffectiveTime);
        }

        public IReadOnlyList<AnchorCandidate> Candidates { get; }

        public double CentreSeconds { get; }

        public double SpreadSeconds { get; }
    }
}
