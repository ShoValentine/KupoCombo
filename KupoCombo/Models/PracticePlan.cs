using System;
using System.Collections.Generic;
using System.Linq;

namespace KupoCombo.Models;

public enum RotationPhase
{
    PrePull,
    Opener,
    Burst,
    Filler,
    Pooling,
    Recovery
}

public enum ResourceTransactionKind
{
    ActionWindow,
    UnattributedGain,
    Reconciliation
}

public sealed class PlayerTimingProfile
{
    private readonly Dictionary<uint, float> adjustedRecastSeconds = new();

    public int SkillSpeed { get; private set; }

    public int SpellSpeed { get; private set; }

    public int Haste { get; private set; }

    public IReadOnlyDictionary<uint, float> AdjustedRecastSeconds =>
        adjustedRecastSeconds;

    internal void SetAttributes(
        int skillSpeed,
        int spellSpeed,
        int haste)
    {
        SkillSpeed = Math.Max(0, skillSpeed);
        SpellSpeed = Math.Max(0, spellSpeed);
        Haste = Math.Max(0, haste);
    }

    internal void SetAdjustedRecastSeconds(
        uint actionId,
        float seconds)
    {
        if (actionId == 0 || seconds <= 0f)
        {
            return;
        }

        adjustedRecastSeconds[actionId] = seconds;
    }

    public float GetAdjustedRecastSeconds(
        uint actionId,
        float fallback = 0f)
    {
        return adjustedRecastSeconds.TryGetValue(actionId, out var seconds)
            ? seconds
            : fallback;
    }

    public PlayerTimingProfile Clone()
    {
        var clone = new PlayerTimingProfile();
        clone.SetAttributes(SkillSpeed, SpellSpeed, Haste);

        foreach (var item in adjustedRecastSeconds)
        {
            clone.adjustedRecastSeconds[item.Key] = item.Value;
        }

        return clone;
    }
}

public sealed class ResourceProjection
{
    public string Resource { get; init; } = string.Empty;

    public int Before { get; init; }

    public int After { get; init; }

    public int Delta => After - Before;
}

public sealed class ResourceTransaction
{
    public DateTime RecordedAtUtc { get; init; } = DateTime.UtcNow;

    public ResourceTransactionKind Kind { get; init; }

    public string Resource { get; init; } = string.Empty;

    public IReadOnlyList<uint> ActionIds { get; init; } =
        Array.Empty<uint>();

    public int Before { get; init; }

    public int After { get; init; }

    public int ExpectedDelta { get; init; }

    public int ObservedDelta => After - Before;

    public int UnattributedDelta => ObservedDelta - ExpectedDelta;
}

public sealed class PracticePlan
{
    public static PracticePlan Empty { get; } = new();

    public string Job { get; init; } = string.Empty;

    public double StartsAtCombatTimeSeconds { get; init; }

    public double HorizonSeconds { get; init; }

    public PlayerTimingProfile TimingProfile { get; init; } = new();

    public IReadOnlyList<TrainingForecastStep> Steps { get; init; } =
        Array.Empty<TrainingForecastStep>();

    public bool IsEmpty => Steps.Count == 0;

    public RotationPhase CurrentPhase =>
        Steps.FirstOrDefault()?.Phase ?? RotationPhase.PrePull;

    public PracticePlan WithSteps(
        IEnumerable<TrainingForecastStep> steps)
    {
        return new PracticePlan
        {
            Job = Job,
            StartsAtCombatTimeSeconds = StartsAtCombatTimeSeconds,
            HorizonSeconds = HorizonSeconds,
            TimingProfile = TimingProfile.Clone(),
            Steps = steps.ToArray()
        };
    }
}
