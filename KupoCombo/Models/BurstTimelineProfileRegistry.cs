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
}

internal static class BurstTimelineProfileRegistry
{
    private const string FileName = "burst-resource-targets.json";

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

        var anchor = profile.BurstAnchorActionIds
            .Select(actionId => new
            {
                ActionId = actionId,
                Cooldown = cooldowns.TryGetValue(actionId, out var cooldown)
                    ? cooldown
                    : null
            })
            .FirstOrDefault(item => item.Cooldown != null);

        if (anchor?.Cooldown == null)
        {
            return false;
        }

        var cycleSeconds = Math.Max(
            1d,
            profile.MinorBurstCycleSeconds ?? 120);
        var timeline = Math.Max(0d, timelineCombatTimeSeconds);
        var cooldownSnapshot = anchor.Cooldown;
        double effectiveTime;
        double secondsSinceBurst;
        double secondsUntilBurst;

        if (cooldownSnapshot.IsReady)
        {
            var readySince = Math.Max(
                0d,
                anchorReadySinceTimelineSeconds ?? timeline);
            var alignedBoundary = FindNearestCycleBoundary(
                readySince,
                cycleSeconds);
            var elapsedReadyTime = Math.Max(0d, timeline - readySince);

            effectiveTime = alignedBoundary + elapsedReadyTime;
            secondsSinceBurst = elapsedReadyTime;
            secondsUntilBurst = 0d;
        }
        else
        {
            var remaining = Math.Clamp(
                cooldownSnapshot.RemainingSeconds,
                0f,
                (float)cycleSeconds);
            var pointInCycle = remaining <= 0.001f
                ? 0d
                : cycleSeconds - remaining;
            var cycleIndex = Math.Max(
                0d,
                Math.Round(
                    (timeline - pointInCycle) / cycleSeconds,
                    MidpointRounding.AwayFromZero));

            effectiveTime = pointInCycle + (cycleIndex * cycleSeconds);
            secondsSinceBurst = pointInCycle;
            secondsUntilBurst = remaining;
        }

        alignment = new BurstTimelineAlignment(
            profile.PolicyId,
            anchor.ActionId,
            cycleSeconds,
            timeline,
            effectiveTime,
            secondsSinceBurst,
            secondsUntilBurst,
            cooldownSnapshot.IsReady);
        return true;
    }

    public static bool IsPrimaryAnchor(
        string job,
        int level,
        uint actionId)
    {
        return TryGetProfile(job, level, out var profile) &&
            profile.BurstAnchorActionIds.FirstOrDefault() == actionId;
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
                candidate.BurstAnchorActionIds.Count > 0)
            .OrderByDescending(candidate => candidate.MinimumLevel)
            .FirstOrDefault()!;

        return profile != null;
    }

    private static double FindNearestCycleBoundary(
        double timelineSeconds,
        double cycleSeconds)
    {
        return Math.Max(
            0d,
            Math.Round(
                timelineSeconds / cycleSeconds,
                MidpointRounding.AwayFromZero) * cycleSeconds);
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
                profile.BurstAnchorActionIds.Count > 0)
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
}
