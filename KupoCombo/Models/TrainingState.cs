using System;
using System.Collections.Generic;
using System.Linq;

namespace KupoCombo.Models;

public sealed class CooldownSnapshot
{
    public float RemainingSeconds { get; init; }

    public float RechargeSeconds { get; init; }

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
    private const int MaximumResourceTransactions = 256;

    private readonly List<uint> acceptedActionHistory = new();
    private readonly Dictionary<string, int> gauges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> stateValues =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, StatusSnapshot> statuses = new();
    private readonly Dictionary<uint, CooldownSnapshot> cooldowns = new();
    private readonly Dictionary<uint, uint> adjustedActions = new();
    private readonly Dictionary<uint, float> adjustedRecastSeconds = new();
    private readonly List<ResourceTransaction> resourceTransactions = new();

    private double timelineCombatTimeSeconds;
    private double? burstAnchorReadySinceTimelineSeconds;

    public string Job { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public int TargetCount { get; private set; } = 1;

    public double CombatTimeSeconds =>
        TryGetBurstTimelineAlignment(out var alignment)
            ? alignment.EffectiveCombatTimeSeconds
            : timelineCombatTimeSeconds;

    public double TimelineCombatTimeSeconds => timelineCombatTimeSeconds;

    public uint NativeComboActionId { get; private set; }

    public float ComboRemainingSeconds { get; private set; }

    public uint LastObservedActionId { get; private set; }

    public uint LastAcceptedActionId { get; private set; }

    public PlayerTimingProfile TimingProfile { get; private set; } = new();

    public int AcceptedActionCount => acceptedActionHistory.Count;

    public IReadOnlyList<uint> AcceptedActionHistory => acceptedActionHistory;

    public IReadOnlyDictionary<string, int> Gauges => gauges;

    public IReadOnlyDictionary<string, double> StateValues => stateValues;

    public IReadOnlyDictionary<uint, StatusSnapshot> Statuses => statuses;

    public IReadOnlyDictionary<uint, CooldownSnapshot> Cooldowns => cooldowns;

    public IReadOnlyDictionary<uint, uint> AdjustedActions => adjustedActions;

    public IReadOnlyDictionary<uint, float> AdjustedRecastSeconds =>
        adjustedRecastSeconds;

    public IReadOnlyList<ResourceTransaction> ResourceTransactions =>
        resourceTransactions;

    public bool TryGetBurstTimelineAlignment(
        out BurstTimelineAlignment alignment)
    {
        return BurstTimelineProfileRegistry.TryAlign(
            Job,
            Level,
            timelineCombatTimeSeconds,
            cooldowns,
            burstAnchorReadySinceTimelineSeconds,
            out alignment);
    }

    public void SetLevel(int level)
    {
        Level = Math.Max(0, level);
        RefreshBurstAnchorTracking();
    }

    public void SetTargetCount(int targetCount)
    {
        TargetCount = Math.Max(1, targetCount);
    }

    public void SetCombatTimeSeconds(double seconds)
    {
        var next = Math.Max(0d, seconds);

        if (next + 0.001d < timelineCombatTimeSeconds)
        {
            burstAnchorReadySinceTimelineSeconds = null;
        }

        timelineCombatTimeSeconds = next;
        RefreshBurstAnchorTracking();
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

    public void SetPlayerTiming(
        int skillSpeed,
        int spellSpeed,
        int haste)
    {
        TimingProfile.SetAttributes(skillSpeed, spellSpeed, haste);
    }

    public void SetAdjustedRecastSeconds(
        uint actionId,
        float seconds)
    {
        if (actionId == 0 || seconds <= 0f)
        {
            return;
        }

        adjustedRecastSeconds[actionId] = seconds;
        TimingProfile.SetAdjustedRecastSeconds(actionId, seconds);
    }

    public float GetAdjustedRecastSeconds(
        uint actionId,
        float fallback = 0f)
    {
        return adjustedRecastSeconds.TryGetValue(actionId, out var seconds)
            ? seconds
            : fallback;
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
        var previousReady = cooldowns.TryGetValue(
            actionId,
            out var previous) &&
            previous.IsReady;

        cooldowns[actionId] = cooldown;
        UpdateBurstAnchorReadyState(
            actionId,
            previousReady,
            cooldown.IsReady);
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
            timelineCombatTimeSeconds = timelineCombatTimeSeconds,
            burstAnchorReadySinceTimelineSeconds =
                burstAnchorReadySinceTimelineSeconds,
            NativeComboActionId = NativeComboActionId,
            ComboRemainingSeconds = ComboRemainingSeconds,
            LastObservedActionId = LastObservedActionId,
            LastAcceptedActionId = LastAcceptedActionId,
            TimingProfile = TimingProfile.Clone()
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
                RechargeSeconds = item.Value.RechargeSeconds,
                Charges = item.Value.Charges,
                MaximumCharges = item.Value.MaximumCharges
            };
        }

        foreach (var item in adjustedActions)
        {
            clone.adjustedActions[item.Key] = item.Value;
        }

        foreach (var item in adjustedRecastSeconds)
        {
            clone.adjustedRecastSeconds[item.Key] = item.Value;
        }

        clone.resourceTransactions.AddRange(resourceTransactions);
        return clone;
    }

    internal void Begin(string job, int level)
    {
        Job = job.Trim().ToUpperInvariant();
        Level = Math.Max(0, level);
        TargetCount = 1;
        timelineCombatTimeSeconds = 0d;
        burstAnchorReadySinceTimelineSeconds = null;
        NativeComboActionId = 0;
        ComboRemainingSeconds = 0f;
        LastObservedActionId = 0;
        LastAcceptedActionId = 0;
        TimingProfile = new PlayerTimingProfile();
        acceptedActionHistory.Clear();
        gauges.Clear();
        stateValues.Clear();
        statuses.Clear();
        cooldowns.Clear();
        adjustedActions.Clear();
        adjustedRecastSeconds.Clear();
        resourceTransactions.Clear();
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

    internal void RecordResourceTransaction(ResourceTransaction transaction)
    {
        resourceTransactions.Add(transaction);

        if (resourceTransactions.Count > MaximumResourceTransactions)
        {
            resourceTransactions.RemoveRange(
                0,
                resourceTransactions.Count - MaximumResourceTransactions);
        }
    }

    internal void SetStatus(
        uint statusId,
        int stacks,
        float remainingSeconds)
    {
        if (statusId == 0)
        {
            return;
        }

        statuses[statusId] = new StatusSnapshot
        {
            StatusId = statusId,
            Param = (ushort)Math.Clamp(stacks, 0, ushort.MaxValue),
            RemainingSeconds = Math.Max(0f, remainingSeconds)
        };
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
        if (!cooldowns.TryGetValue(actionId, out var cooldown) ||
            cooldown.Charges <= 0)
        {
            return;
        }

        var charges = Math.Max(0, cooldown.Charges - 1);
        var remainingSeconds = cooldown.RemainingSeconds;

        if (cooldown.Charges >= cooldown.MaximumCharges &&
            charges < cooldown.MaximumCharges)
        {
            remainingSeconds = cooldown.RechargeSeconds > 0f
                ? cooldown.RechargeSeconds
                : Math.Max(cooldown.RemainingSeconds, 999f);
        }
        else if (charges < cooldown.MaximumCharges && remainingSeconds <= 0f)
        {
            remainingSeconds = cooldown.RechargeSeconds > 0f
                ? cooldown.RechargeSeconds
                : 999f;
        }

        SetCooldown(
            actionId,
            new CooldownSnapshot
            {
                Charges = charges,
                MaximumCharges = cooldown.MaximumCharges,
                RemainingSeconds = remainingSeconds,
                RechargeSeconds = cooldown.RechargeSeconds
            });
    }

    internal void AdvanceForecastTime(float seconds)
    {
        var elapsed = Math.Max(0f, seconds);
        timelineCombatTimeSeconds += elapsed;
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
            var cooldown = item.Value;

            if (cooldown.Charges >= cooldown.MaximumCharges)
            {
                SetCooldown(
                    item.Key,
                    new CooldownSnapshot
                    {
                        Charges = cooldown.MaximumCharges,
                        MaximumCharges = cooldown.MaximumCharges,
                        RemainingSeconds = 0f,
                        RechargeSeconds = cooldown.RechargeSeconds
                    });
                continue;
            }

            if (cooldown.RemainingSeconds >= 900f &&
                cooldown.RechargeSeconds <= 0f)
            {
                continue;
            }

            var remaining = cooldown.RemainingSeconds - elapsed;
            var charges = cooldown.Charges;
            var recharge = cooldown.RechargeSeconds;

            while (remaining <= 0f && charges < cooldown.MaximumCharges)
            {
                charges++;

                if (charges >= cooldown.MaximumCharges)
                {
                    remaining = 0f;
                    break;
                }

                if (recharge <= 0f)
                {
                    remaining = 0f;
                    break;
                }

                remaining += recharge;
            }

            SetCooldown(
                item.Key,
                new CooldownSnapshot
                {
                    Charges = charges,
                    MaximumCharges = cooldown.MaximumCharges,
                    RemainingSeconds = Math.Max(0f, remaining),
                    RechargeSeconds = recharge
                });
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

    private void RefreshBurstAnchorTracking()
    {
        var primaryAnchor = cooldowns.FirstOrDefault(item =>
            BurstTimelineProfileRegistry.IsPrimaryAnchor(
                Job,
                Level,
                item.Key));

        if (primaryAnchor.Key == 0 || !primaryAnchor.Value.IsReady)
        {
            burstAnchorReadySinceTimelineSeconds = null;
            return;
        }

        burstAnchorReadySinceTimelineSeconds ??=
            timelineCombatTimeSeconds;
    }

    private void UpdateBurstAnchorReadyState(
        uint actionId,
        bool wasReady,
        bool isReady)
    {
        if (!BurstTimelineProfileRegistry.IsPrimaryAnchor(
                Job,
                Level,
                actionId))
        {
            return;
        }

        if (!isReady)
        {
            burstAnchorReadySinceTimelineSeconds = null;
            return;
        }

        if (!wasReady || !burstAnchorReadySinceTimelineSeconds.HasValue)
        {
            burstAnchorReadySinceTimelineSeconds =
                timelineCombatTimeSeconds;
        }
    }
}
