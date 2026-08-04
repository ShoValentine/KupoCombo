using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class CooldownSnapshot
{
    public float RemainingSeconds { get; init; }

    public int Charges { get; init; }

    public int MaximumCharges { get; init; }

    public bool IsReady => Charges > 0 || RemainingSeconds <= 0f;
}

public sealed class TrainingState
{
    private readonly List<uint> acceptedActionHistory = new();
    private readonly Dictionary<string, int> gauges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> statuses = new();
    private readonly Dictionary<uint, CooldownSnapshot> cooldowns = new();

    public string Job { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public int TargetCount { get; private set; } = 1;

    public uint NativeComboActionId { get; private set; }

    public float ComboRemainingSeconds { get; private set; }

    public uint LastObservedActionId { get; private set; }

    public uint LastAcceptedActionId { get; private set; }

    public int AcceptedActionCount => acceptedActionHistory.Count;

    public IReadOnlyList<uint> AcceptedActionHistory => acceptedActionHistory;

    public IReadOnlyDictionary<string, int> Gauges => gauges;

    public IReadOnlySet<uint> Statuses => statuses;

    public IReadOnlyDictionary<uint, CooldownSnapshot> Cooldowns => cooldowns;

    public void SetLevel(int level)
    {
        Level = Math.Max(0, level);
    }

    public void SetTargetCount(int targetCount)
    {
        TargetCount = Math.Max(1, targetCount);
    }

    public void SetCombo(uint actionId, float remainingSeconds)
    {
        NativeComboActionId = actionId;
        ComboRemainingSeconds = Math.Max(0f, remainingSeconds);
    }

    public void SetGauge(string name, int value)
    {
        gauges[name] = value;
    }

    public int GetGauge(string name, int fallback = 0)
    {
        return gauges.TryGetValue(name, out var value)
            ? value
            : fallback;
    }

    public void ReplaceStatuses(IEnumerable<uint> statusIds)
    {
        statuses.Clear();

        foreach (var statusId in statusIds)
        {
            if (statusId != 0)
            {
                statuses.Add(statusId);
            }
        }
    }

    public bool HasStatus(uint statusId)
    {
        return statuses.Contains(statusId);
    }

    public void SetCooldown(uint actionId, CooldownSnapshot cooldown)
    {
        cooldowns[actionId] = cooldown;
    }

    public CooldownSnapshot? GetCooldown(uint actionId)
    {
        return cooldowns.TryGetValue(actionId, out var cooldown)
            ? cooldown
            : null;
    }

    internal void Begin(string job, int level)
    {
        Job = job.Trim().ToUpperInvariant();
        Level = Math.Max(0, level);
        TargetCount = 1;
        NativeComboActionId = 0;
        ComboRemainingSeconds = 0f;
        LastObservedActionId = 0;
        LastAcceptedActionId = 0;
        acceptedActionHistory.Clear();
        gauges.Clear();
        statuses.Clear();
        cooldowns.Clear();
    }

    internal void RecordAcceptedAction(uint actionId)
    {
        LastObservedActionId = actionId;
        LastAcceptedActionId = actionId;
        acceptedActionHistory.Add(actionId);
    }

    internal void RecordRejectedAction(uint actionId)
    {
        LastObservedActionId = actionId;
    }

    internal void ResetProgress()
    {
        LastAcceptedActionId = 0;
        acceptedActionHistory.Clear();
    }

    internal void Clear()
    {
        Begin(string.Empty, 0);
    }
}
