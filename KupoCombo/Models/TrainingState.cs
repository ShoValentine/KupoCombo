using System;
using System.Collections.Generic;
using System.Linq;

namespace KupoCombo.Models;

public sealed class CooldownSnapshot
{
    public float RemainingSeconds { get; init; }

    public int Charges { get; init; }

    public int MaximumCharges { get; init; }

    public bool IsReady => Charges > 0 || RemainingSeconds <= 0f;
}

public sealed class StatusSnapshot
{
    public uint StatusId { get; init; }

    public ushort Param { get; init; }

    public float RemainingSeconds { get; init; }
}

public sealed class TrainingState
{
    private readonly List<uint> acceptedActionHistory = new();
    private readonly Dictionary<string, int> gauges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> stateValues =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, StatusSnapshot> statuses = new();
    private readonly Dictionary<uint, CooldownSnapshot> cooldowns = new();
    private readonly Dictionary<uint, uint> adjustedActions = new();

    public string Job { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public int TargetCount { get; private set; } = 1;

    public double CombatTimeSeconds { get; private set; }

    public uint NativeComboActionId { get; private set; }

    public float ComboRemainingSeconds { get; private set; }

    public uint LastObservedActionId { get; private set; }

    public uint LastAcceptedActionId { get; private set; }

    public int AcceptedActionCount => acceptedActionHistory.Count;

    public IReadOnlyList<uint> AcceptedActionHistory => acceptedActionHistory;

    public IReadOnlyDictionary<string, int> Gauges => gauges;

    public IReadOnlyDictionary<string, double> StateValues => stateValues;

    public IReadOnlyDictionary<uint, StatusSnapshot> Statuses => statuses;

    public IReadOnlyDictionary<uint, CooldownSnapshot> Cooldowns => cooldowns;

    public IReadOnlyDictionary<uint, uint> AdjustedActions => adjustedActions;

    public void SetLevel(int level)
    {
        Level = Math.Max(0, level);
    }

    public void SetTargetCount(int targetCount)
    {
        TargetCount = Math.Max(1, targetCount);
    }

    public void SetCombatTimeSeconds(double seconds)
    {
        CombatTimeSeconds = Math.Max(0d, seconds);
    }

    public void SetCombo(uint actionId, float remainingSeconds)
    {
        NativeComboActionId = actionId;
        ComboRemainingSeconds = Math.Max(0f, remainingSeconds);
    }

    public void SetGauge(string name, int value)
    {
        gauges[name] = value;
        stateValues[name] = value;
    }

    public int GetGauge(string name, int fallback = 0)
    {
        return gauges.TryGetValue(name, out var value)
            ? value
            : fallback;
    }

    public void SetStateValue(string name, double value)
    {
        stateValues[name] = value;
    }

    public double GetStateValue(string name, double fallback = 0d)
    {
        return stateValues.TryGetValue(name, out var value)
            ? value
            : fallback;
    }

    public bool TryGetStateValue(string name, out double value)
    {
        return stateValues.TryGetValue(name, out value);
    }

    public void ReplaceStatuses(IEnumerable<StatusSnapshot> snapshots)
    {
        statuses.Clear();

        foreach (var snapshot in snapshots)
        {
            if (snapshot.StatusId != 0)
            {
                statuses[snapshot.StatusId] = snapshot;
            }
        }
    }

    public bool HasStatus(uint statusId)
    {
        return statuses.ContainsKey(statusId);
    }

    public StatusSnapshot? GetStatus(uint statusId)
    {
        return statuses.TryGetValue(statusId, out var status)
            ? status
            : null;
    }

    public int GetStatusStacks(uint statusId)
    {
        var status = GetStatus(statusId);

        if (status == null)
        {
            return 0;
        }

        return status.Param == byte.MaxValue
            ? 3
            : status.Param;
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

    public void SetAdjustedAction(uint baseActionId, uint adjustedActionId)
    {
        adjustedActions[baseActionId] = adjustedActionId;
    }

    public uint GetAdjustedAction(uint baseActionId, uint fallback = 0)
    {
        if (adjustedActions.TryGetValue(baseActionId, out var adjustedActionId) &&
            adjustedActionId != 0)
        {
            return adjustedActionId;
        }

        return fallback != 0
            ? fallback
            : baseActionId;
    }

    public TrainingState Clone()
    {
        var clone = new TrainingState
        {
            Job = Job,
            Level = Level,
            TargetCount = TargetCount,
            CombatTimeSeconds = CombatTimeSeconds,
            NativeComboActionId = NativeComboActionId,
            ComboRemainingSeconds = ComboRemainingSeconds,
            LastObservedActionId = LastObservedActionId,
            LastAcceptedActionId = LastAcceptedActionId
        };

        clone.acceptedActionHistory.AddRange(acceptedActionHistory);

        foreach (var item in gauges)
        {
            clone.gauges[item.Key] = item.Value;
        }

        foreach (var item in stateValues)
        {
            clone.stateValues[item.Key] = item.Value;
        }

        foreach (var item in statuses)
        {
            clone.statuses[item.Key] = new StatusSnapshot
            {
                StatusId = item.Value.StatusId,
                Param = item.Value.Param,
                RemainingSeconds = item.Value.RemainingSeconds
            };
        }

        foreach (var item in cooldowns)
        {
            clone.cooldowns[item.Key] = new CooldownSnapshot
            {
                RemainingSeconds = item.Value.RemainingSeconds,
                Charges = item.Value.Charges,
                MaximumCharges = item.Value.MaximumCharges
            };
        }

        foreach (var item in adjustedActions)
        {
            clone.adjustedActions[item.Key] = item.Value;
        }

        return clone;
    }

    internal void Begin(string job, int level)
    {
        Job = job.Trim().ToUpperInvariant();
        Level = Math.Max(0, level);
        TargetCount = 1;
        CombatTimeSeconds = 0d;
        NativeComboActionId = 0;
        ComboRemainingSeconds = 0f;
        LastObservedActionId = 0;
        LastAcceptedActionId = 0;
        acceptedActionHistory.Clear();
        gauges.Clear();
        stateValues.Clear();
        statuses.Clear();
        cooldowns.Clear();
        adjustedActions.Clear();
    }

    internal void RecordAcceptedAction(uint actionId)
    {
        LastObservedActionId = actionId;
        LastAcceptedActionId = actionId;
        acceptedActionHistory.Add(actionId);
    }

    internal void RecordObservedAction(uint actionId)
    {
        LastObservedActionId = actionId;
    }

    internal void RecordRejectedAction(uint actionId)
    {
        LastObservedActionId = actionId;
    }

    internal void RemoveStatus(uint statusId)
    {
        statuses.Remove(statusId);
    }

    internal void DecrementStatusStacks(uint statusId)
    {
        if (!statuses.TryGetValue(statusId, out var status))
        {
            return;
        }

        var stacks = GetStatusStacks(statusId);

        if (stacks <= 1)
        {
            statuses.Remove(statusId);
            return;
        }

        statuses[statusId] = new StatusSnapshot
        {
            StatusId = status.StatusId,
            Param = (ushort)(stacks - 1),
            RemainingSeconds = status.RemainingSeconds
        };
    }

    internal void ConsumeCooldown(uint actionId)
    {
        if (!cooldowns.TryGetValue(actionId, out var cooldown))
        {
            return;
        }

        var charges = Math.Max(0, cooldown.Charges - 1);

        cooldowns[actionId] = new CooldownSnapshot
        {
            Charges = charges,
            MaximumCharges = cooldown.MaximumCharges,
            RemainingSeconds = charges > 0
                ? cooldown.RemainingSeconds
                : Math.Max(cooldown.RemainingSeconds, 999f)
        };
    }

    internal void AdvanceForecastTime(float seconds)
    {
        var elapsed = Math.Max(0f, seconds);
        CombatTimeSeconds += elapsed;
        ComboRemainingSeconds = Math.Max(0f, ComboRemainingSeconds - elapsed);

        foreach (var item in statuses.ToArray())
        {
            if (item.Value.RemainingSeconds <= 0f)
            {
                continue;
            }

            var remaining = Math.Max(
                0f,
                item.Value.RemainingSeconds - elapsed);

            if (remaining <= 0f)
            {
                statuses.Remove(item.Key);
                continue;
            }

            statuses[item.Key] = new StatusSnapshot
            {
                StatusId = item.Value.StatusId,
                Param = item.Value.Param,
                RemainingSeconds = remaining
            };
        }

        foreach (var item in cooldowns.ToArray())
        {
            if (item.Value.RemainingSeconds <= 0f ||
                item.Value.RemainingSeconds >= 900f)
            {
                continue;
            }

            var remaining = Math.Max(
                0f,
                item.Value.RemainingSeconds - elapsed);
            var charges = item.Value.Charges;

            if (remaining <= 0f && charges < item.Value.MaximumCharges)
            {
                charges++;
            }

            cooldowns[item.Key] = new CooldownSnapshot
            {
                Charges = charges,
                MaximumCharges = item.Value.MaximumCharges,
                RemainingSeconds = remaining
            };
        }
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
