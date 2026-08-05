using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using KupoCombo.Models;

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
            state.SetAdjustedAction(
                actionId,
                actionManager->GetAdjustedActionId(actionId));

            state.SetCooldown(
                actionId,
                ReadCooldown(actionManager, actionId, state.Level));
        }
    }

    private static CooldownSnapshot ReadCooldown(
        ActionManager* actionManager,
        uint actionId,
        int level)
    {
        var maximumCharges = Math.Max(
            1,
            (int)ActionManager.GetMaxCharges(
                actionId,
                (uint)Math.Max(0, level)));

        var recastGroup = actionManager->GetRecastGroup(
            (int)ActionType.Action,
            actionId);

        if (recastGroup < 0)
        {
            return new CooldownSnapshot
            {
                Charges = maximumCharges,
                MaximumCharges = maximumCharges
            };
        }

        var detail = actionManager->GetRecastGroupDetail(recastGroup);

        if (detail == null || !detail->IsActive)
        {
            return new CooldownSnapshot
            {
                Charges = maximumCharges,
                MaximumCharges = maximumCharges
            };
        }

        var rechargeSeconds = actionManager->GetRecastTime(
            ActionType.Action,
            actionId);

        if (rechargeSeconds <= 0f)
        {
            return new CooldownSnapshot
            {
                RemainingSeconds = Math.Max(0f, detail->Total - detail->Elapsed),
                Charges = 0,
                MaximumCharges = maximumCharges
            };
        }

        var charges = Math.Clamp(
            (int)MathF.Floor(detail->Elapsed / rechargeSeconds),
            0,
            maximumCharges);

        var remainingSeconds = charges >= maximumCharges
            ? 0f
            : rechargeSeconds - (detail->Elapsed % rechargeSeconds);

        return new CooldownSnapshot
        {
            RemainingSeconds = Math.Max(0f, remainingSeconds),
            Charges = charges,
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
            Math.Max(0, gauge.OverheatTimeRemaining));
        state.SetStateValue("robot_active", gauge.IsRobotActive ? 1d : 0d);
        state.SetStateValue(
            "summon_ms",
            Math.Max(0, gauge.SummonTimeRemaining));
        state.SetGauge("last_summon_battery", gauge.LastSummonBatteryPower);
    }
}
