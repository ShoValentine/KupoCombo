using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using KupoCombo.Models;
using NativePlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using PlayerAttribute = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerAttribute;

namespace KupoCombo.Services;

public unsafe sealed class TrainingStateReader
{
    private readonly IJobGauges jobGauges;
    private readonly IObjectTable objectTable;
    private readonly IPlayerState playerState;

    public TrainingStateReader(
        IJobGauges jobGauges,
        IObjectTable objectTable,
        IPlayerState playerState)
    {
        this.jobGauges = jobGauges;
        this.objectTable = objectTable;
        this.playerState = playerState;
    }

    public void Refresh(
        TrainingState state,
        ITrainingPolicy policy)
    {
        state.SetLevel(
            playerState.IsLoaded
                ? playerState.EffectiveLevel
                : 0);

        RefreshPlayer(state);
        RefreshActionManager(state, policy);

        if (policy.Job.Equals("DRK", StringComparison.OrdinalIgnoreCase))
        {
            RefreshDarkKnight(state);
        }
        else if (policy.Job.Equals("MCH", StringComparison.OrdinalIgnoreCase))
        {
            RefreshMachinist(state);
        }
    }

    private void RefreshPlayer(TrainingState state)
    {
        var nativePlayerState = NativePlayerState.Instance();

        if (nativePlayerState != null && nativePlayerState->IsLoaded)
        {
            state.SetPlayerTiming(
                nativePlayerState->GetAttributeByIndex(PlayerAttribute.SkillSpeed),
                nativePlayerState->GetAttributeByIndex(PlayerAttribute.SpellSpeed),
                nativePlayerState->GetAttributeByIndex(PlayerAttribute.Haste));
        }
        else
        {
            state.SetPlayerTiming(0, 0, 0);
        }

        var localPlayer = objectTable.LocalPlayer;

        if (localPlayer == null)
        {
            state.SetGauge("mp", 0);
            state.ReplaceStatuses(Array.Empty<StatusSnapshot>());
            return;
        }

        state.SetGauge("mp", (int)localPlayer.CurrentMp);

        var statuses = new List<StatusSnapshot>(localPlayer.StatusList.Length);

        foreach (var status in localPlayer.StatusList)
        {
            if (status.StatusId == 0)
            {
                continue;
            }

            statuses.Add(
                new StatusSnapshot
                {
                    StatusId = status.StatusId,
                    Param = status.Param,
                    RemainingSeconds = Math.Max(0f, status.RemainingTime)
                });
        }

        state.ReplaceStatuses(statuses);
    }

    private static void RefreshActionManager(
        TrainingState state,
        ITrainingPolicy policy)
    {
        var actionManager = ActionManager.Instance();

        if (actionManager == null)
        {
            state.SetCombo(0, 0f);
            return;
        }

        state.SetCombo(
            actionManager->Combo.Action,
            actionManager->Combo.Timer);

        var actionIds = new HashSet<uint>(policy.TrackedActionIds);
        actionIds.UnionWith(policy.AdvisoryActionIds);

        foreach (var actionId in actionIds)
        {
            var adjustedActionId = actionManager->GetAdjustedActionId(actionId);
            state.SetAdjustedAction(actionId, adjustedActionId);

            var expectedMaximumCharges = GetExpectedMaximumCharges(
                policy,
                actionId,
                state.Level);
            var expectedRechargeSeconds = GetExpectedRechargeSeconds(
                policy,
                actionId,
                state.Level);

            var adjustedRecastMilliseconds = ActionManager.GetAdjustedRecastTime(
                ActionType.Action,
                adjustedActionId,
                applyClassMechanics: true);
            var adjustedRecastSeconds = adjustedRecastMilliseconds > 0
                ? adjustedRecastMilliseconds / 1000f
                : expectedRechargeSeconds;

            state.SetAdjustedRecastSeconds(
                actionId,
                adjustedRecastSeconds);
            state.SetAdjustedRecastSeconds(
                adjustedActionId,
                adjustedRecastSeconds);

            state.SetCooldown(
                actionId,
                ReadCooldown(
                    actionManager,
                    actionId,
                    state.Level,
                    expectedMaximumCharges,
                    expectedRechargeSeconds));
        }
    }

    private static int GetExpectedMaximumCharges(
        ITrainingPolicy policy,
        uint actionId,
        int level)
    {
        if (policy is not RuleSetTrainingPolicy rulePolicy)
        {
            return 1;
        }

        foreach (var action in rulePolicy.Definition.Actions.Values)
        {
            if (action.ActionId == actionId &&
                level >= action.MinimumLevel &&
                (!action.MaximumLevel.HasValue ||
                 level <= action.MaximumLevel.Value))
            {
                return Math.Max(1, action.MaximumCharges);
            }
        }

        return 1;
    }

    private static float GetExpectedRechargeSeconds(
        ITrainingPolicy policy,
        uint actionId,
        int level)
    {
        if (policy is not RuleSetTrainingPolicy rulePolicy)
        {
            return 0f;
        }

        foreach (var action in rulePolicy.Definition.Actions.Values)
        {
            if (action.ActionId == actionId &&
                level >= action.MinimumLevel &&
                (!action.MaximumLevel.HasValue ||
                 level <= action.MaximumLevel.Value))
            {
                return (float)Math.Max(0d, action.RecastSeconds);
            }
        }

        return 0f;
    }

    private static CooldownSnapshot ReadCooldown(
        ActionManager* actionManager,
        uint actionId,
        int level,
        int expectedMaximumCharges,
        float expectedRechargeSeconds)
    {
        var nativeMaximumCharges = Math.Max(
            1,
            (int)ActionManager.GetMaxCharges(
                actionId,
                (uint)Math.Max(0, level)));
        var maximumCharges = Math.Max(
            nativeMaximumCharges,
            Math.Max(1, expectedMaximumCharges));

        var nativeRechargeSeconds = Math.Max(
            0f,
            actionManager->GetRecastTime(
                ActionType.Action,
                actionId));
        var rechargeSeconds = expectedRechargeSeconds > 0f
            ? expectedRechargeSeconds
            : nativeRechargeSeconds;

        var recastGroup = actionManager->GetRecastGroup(
            (int)ActionType.Action,
            actionId);

        if (recastGroup < 0)
        {
            return new CooldownSnapshot
            {
                Charges = maximumCharges,
                MaximumCharges = maximumCharges,
                RechargeSeconds = rechargeSeconds
            };
        }

        var detail = actionManager->GetRecastGroupDetail(recastGroup);

        if (detail == null || !detail->IsActive)
        {
            return new CooldownSnapshot
            {
                Charges = maximumCharges,
                MaximumCharges = maximumCharges,
                RechargeSeconds = rechargeSeconds
            };
        }

        var currentCharges = Math.Clamp(
            (int)actionManager->GetCurrentCharges(actionId),
            0,
            maximumCharges);

        if (currentCharges >= maximumCharges)
        {
            return new CooldownSnapshot
            {
                Charges = maximumCharges,
                MaximumCharges = maximumCharges,
                RechargeSeconds = rechargeSeconds
            };
        }

        if (rechargeSeconds <= 0f)
        {
            return new CooldownSnapshot
            {
                RemainingSeconds = Math.Max(0f, detail->Total - detail->Elapsed),
                RechargeSeconds = 0f,
                Charges = currentCharges,
                MaximumCharges = maximumCharges
            };
        }

        var elapsedWithinCharge = detail->Elapsed % rechargeSeconds;
        var remainingSeconds = elapsedWithinCharge <= 0f
            ? rechargeSeconds
            : rechargeSeconds - elapsedWithinCharge;

        return new CooldownSnapshot
        {
            RemainingSeconds = Math.Max(0f, remainingSeconds),
            RechargeSeconds = rechargeSeconds,
            Charges = currentCharges,
            MaximumCharges = maximumCharges
        };
    }

    private void RefreshDarkKnight(TrainingState state)
    {
        var gauge = jobGauges.Get<DRKGauge>();

        state.SetGauge("blood", gauge.Blood);
        state.SetGauge("darkside_ms", gauge.DarksideTimeRemaining);
        state.SetGauge("shadow_ms", gauge.ShadowTimeRemaining);
        state.SetGauge("dark_arts", gauge.HasDarkArts ? 1 : 0);
        state.SetGauge(
            "delirium_step",
            Convert.ToInt32(gauge.DeliriumComboStep));
    }

    private void RefreshMachinist(TrainingState state)
    {
        var gauge = jobGauges.Get<MCHGauge>();

        state.SetGauge("heat", gauge.Heat);
        state.SetGauge("battery", gauge.Battery);
        state.SetStateValue("overheated", gauge.IsOverheated ? 1d : 0d);
        state.SetStateValue(
            "overheat_ms",
            Math.Max(0, (int)gauge.OverheatTimeRemaining));
        state.SetStateValue("robot_active", gauge.IsRobotActive ? 1d : 0d);
        state.SetStateValue(
            "summon_ms",
            Math.Max(0, (int)gauge.SummonTimeRemaining));
        state.SetGauge("last_summon_battery", gauge.LastSummonBatteryPower);
    }
}
