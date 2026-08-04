using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class CooldownSnapshot
{
    public float RemainingSeconds { get; init; }

    public int Charges { get; init; }

    public int MaximumCharges { get; init; }
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

    public uint LastObservedActionId { get; private set; }

    public uint LastAcceptedActionId { get; private set; }

    public int AcceptedActionCount => acceptedActionHistory.Count;

    public IReadOnlyList<uint> AcceptedActionHistory => acceptedActionHistory;

    public IReadOnlyDictionary<string, int> Gauges => gauges;

    public IReadOnlySet<uint> Statuses => statuses;

    public IReadOnlyDictionary<uint, CooldownSnapshot> Cooldowns => cooldowns;

    public void SetTargetCount(int targetCount)
    {
        TargetCount = Math.Max(1, targetCount);
    }

    public void SetGauge(string name, int value)
    {
        gauges[name] = value;
    }

    public void SetStatus(uint statusId, bool active)
    {
        if (active)
        {
            statuses.Add(statusId);
            return;
        }

        statuses.Remove(statusId);
    }

    public void SetCooldown(uint actionId, CooldownSnapshot cooldown)
    {
        cooldowns[actionId] = cooldown;
    }

    internal void Begin(string job, int level)
    {
        Job = job.Trim().ToUpperInvariant();
        Level = level;
        TargetCount = 1;
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
