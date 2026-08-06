using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using KupoCombo.Models;
using KupoCombo.Services;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace KupoCombo.Windows;

public sealed class OverlayWindow : Window, IDisposable
{
    private const float BaseIconSize = 64f;
    private const float BaseCellWidth = 112f;
    private const float WeaveIconScale = 0.68f;
    private const int MaximumLabelLength = 18;

    private readonly Plugin plugin;

    private bool transparentStylesPushed;

    public OverlayWindow(Plugin plugin)
        : base(
            "KupoCombo Overlay###KupoComboOverlay",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNavInputs |
            ImGuiWindowFlags.NoNavFocus)
    {
        this.plugin = plugin;

        IsOpen = false;
        ShowCloseButton = false;
        AllowPinning = false;

        Size = new Vector2(860, 300);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 160),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        transparentStylesPushed = false;

        if (plugin.Configuration.OverlayTransparent)
        {
            Flags |= ImGuiWindowFlags.NoBackground;
            BgAlpha = 0f;

            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Vector4.Zero);

            transparentStylesPushed = true;
        }
        else
        {
            Flags &= ~ImGuiWindowFlags.NoBackground;
            BgAlpha = null;
        }
    }

    public override void PostDraw()
    {
        if (transparentStylesPushed)
        {
            ImGui.PopStyleColor(3);
        }
    }

    public override void Draw()
    {
        var textScale = Math.Clamp(
            plugin.Configuration.OverlayTextScale,
            0.5f,
            2.0f);

        ImGui.SetWindowFontScale(textScale);

        var selectedSequence = plugin.SelectedSequence;

        if (selectedSequence != null)
        {
            ImGui.Text(selectedSequence.DisplayName);
            ImGui.Spacing();

            DrawActionGrid(
                selectedSequence.Actions,
                plugin.CurrentStep);
            return;
        }

        DrawDynamicPractice();
    }

    private void DrawDynamicPractice()
    {
        var session = plugin.TrainingSession;
        var decision = session.CurrentDecision;

        if (!plugin.IsDynamicPractice ||
            decision == null ||
            decision.IsComplete)
        {
            ImGui.Text("No sequence selected.");
            return;
        }

        ImGui.Text(plugin.SelectedSequenceName);

        var forecast = session.CurrentForecast;
        var currentGcdSeconds = forecast.FirstOrDefault()?.DurationSeconds ?? 0f;
        var timing = session.Snapshot.TimingProfile;
        var plan = session.CurrentPlan;
        var headerParts = new List<string>
        {
            FormatPhase(session.CurrentPhase)
        };
        var resourceSummary = FormatResourceSummary(session);

        if (!string.IsNullOrWhiteSpace(resourceSummary))
        {
            headerParts.Add(resourceSummary);
        }

        headerParts.Add($"GCD {currentGcdSeconds:0.00}s");
        headerParts.Add($"SkS {timing.SkillSpeed:N0}");
        ImGui.TextDisabled(string.Join(" | ", headerParts));

        if (!plan.IsEmpty)
        {
            ImGui.TextDisabled(
                $"Plan: {plan.Steps.Count} GCD windows across " +
                $"{plan.HorizonSeconds:0}s | " +
                $"Spell Speed {timing.SpellSpeed:N0} | Haste {timing.Haste:N0}");
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Committed action ribbon");

        if (forecast.Count > 0)
        {
            DrawForecastRibbon(forecast);
        }
        else
        {
            var fallbackActions = decision.SuggestedActionIds
                .Concat(new[] { decision.PreferredActionId })
                .ToArray();
            var fallbackScales = decision.SuggestedActionIds
                .Select(_ => WeaveIconScale)
                .Concat(new[] { 1f })
                .ToArray();
            var fallbackWeaves = decision.SuggestedActionIds
                .Select(_ => true)
                .Concat(new[] { false })
                .ToArray();

            DrawActionGrid(
                fallbackActions,
                completedCount: 0,
                itemScale: fallbackScales,
                itemIsWeave: fallbackWeaves);
        }

        if (decision.AcceptableActionIds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Also acceptable now");

            DrawActionGrid(
                decision.AcceptableActionIds,
                completedCount: 0);
        }

        if (!string.IsNullOrWhiteSpace(decision.Reason))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(decision.Reason);
        }

        if (forecast.Count > 0)
        {
            ImGui.TextDisabled(
                "Smaller outlined icons are weaves. The next two GCDs stay committed; the full-cycle plan adapts behind them.");
        }
    }

    private static string FormatResourceSummary(TrainingSession session)
    {
        if (session.Policy is not RuleSetTrainingPolicy policy)
        {
            return string.Empty;
        }

        return string.Join(
            " | ",
            policy.Definition.StateInputs
                .Where(item => item.Value.Kind == PolicyStateValueKind.Resource)
                .Select(item =>
                {
                    var label = FormatResourceName(
                        item.Key,
                        item.Value.DisplayName);
                    var current = session.Snapshot.GetGauge(item.Key);

                    return item.Value.Maximum.HasValue
                        ? $"{label} {current:N0}/{item.Value.Maximum.Value:N0}"
                        : $"{label} {current:N0}";
                }));
    }

    private static string FormatResourceName(
        string alias,
        string configuredName)
    {
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            return configuredName;
        }

        if (alias.Length <= 3)
        {
            return alias.ToUpperInvariant();
        }

        return char.ToUpperInvariant(alias[0]) + alias[1..];
    }

    private void DrawForecastRibbon(
        IReadOnlyList<TrainingForecastStep> forecast)
    {
        var actions = new List<uint>();
        var alpha = new List<float>();
        var scale = new List<float>();
        var isWeave = new List<bool>();

        foreach (var step in forecast)
        {
            foreach (var weaveActionId in step.SuggestedActionIds)
            {
                actions.Add(weaveActionId);
                alpha.Add(step.Confidence);
                scale.Add(WeaveIconScale);
                isWeave.Add(true);
            }

            actions.Add(step.GcdActionId);
            alpha.Add(step.Confidence);
            scale.Add(1f);
            isWeave.Add(false);
        }

        DrawActionGrid(
            actions,
            completedCount: 0,
            itemAlpha: alpha,
            itemScale: scale,
            itemIsWeave: isWeave);
    }

    private void DrawActionGrid(
        IReadOnlyList<uint> actions,
        int completedCount,
        IReadOnlyList<float>? itemAlpha = null,
        IReadOnlyList<float>? itemScale = null,
        IReadOnlyList<bool>? itemIsWeave = null)
    {
        if (actions.Count == 0)
        {
            return;
        }

        var globalScale = ImGuiHelpers.GlobalScale;
        var iconScale = Math.Clamp(
            plugin.Configuration.OverlayIconScale,
            0.5f,
            2.0f);
        var textScale = Math.Clamp(
            plugin.Configuration.OverlayTextScale,
            0.5f,
            2.0f);
        var configuredSpacing = Math.Clamp(
            plugin.Configuration.OverlayIconSpacing,
            -60f,
            60f);

        var iconSpacing = configuredSpacing * globalScale;
        var iconLength = BaseIconSize * globalScale * iconScale;
        var baseIconSize = new Vector2(iconLength, iconLength);
        var iconBasedCellWidth = BaseCellWidth * globalScale * iconScale;
        var textBasedCellWidth = BaseCellWidth * globalScale * textScale;
        var cellWidth = Math.Max(iconBasedCellWidth, textBasedCellWidth);
        var availableWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var horizontalAdvance = Math.Max(1f, cellWidth + iconSpacing);
        var remainingWidth = Math.Max(0f, availableWidth - cellWidth);
        var itemsPerRow = Math.Max(
            1,
            1 + (int)Math.Floor(remainingWidth / horizontalAdvance));

        var labelHeight = ImGui.GetTextLineHeight();
        var cellHeight =
            baseIconSize.Y +
            ImGui.GetStyle().ItemSpacing.Y +
            labelHeight;
        var verticalSpacing = Math.Max(0f, iconSpacing);
        var verticalAdvance = cellHeight + verticalSpacing;
        var gridStartX = ImGui.GetCursorPosX();
        var gridStartY = ImGui.GetCursorPosY();

        for (var step = 0; step < actions.Count; step++)
        {
            var column = step % itemsPerRow;
            var row = step / itemsPerRow;

            ImGui.SetCursorPosX(
                gridStartX + column * horizontalAdvance);
            ImGui.SetCursorPosY(
                gridStartY + row * verticalAdvance);

            var actionId = actions[step];
            var stepCompleted = step < completedCount;
            var alpha = itemAlpha != null && step < itemAlpha.Count
                ? Math.Clamp(itemAlpha[step], 0.2f, 1f)
                : 1f;
            var scale = itemScale != null && step < itemScale.Count
                ? Math.Clamp(itemScale[step], 0.45f, 1f)
                : 1f;
            var isWeave = itemIsWeave != null &&
                step < itemIsWeave.Count &&
                itemIsWeave[step];

            DrawActionCell(
                actionId,
                step,
                stepCompleted,
                alpha,
                baseIconSize * scale,
                cellWidth,
                isWeave);
        }

        var rowCount = Math.Max(
            1,
            (actions.Count + itemsPerRow - 1) / itemsPerRow);
        var gridHeight =
            cellHeight +
            (rowCount - 1) * verticalAdvance;

        ImGui.SetCursorPosX(gridStartX);
        ImGui.SetCursorPosY(gridStartY);
        ImGui.Dummy(new Vector2(availableWidth, gridHeight));
    }

    private void DrawActionCell(
        uint actionId,
        int step,
        bool stepCompleted,
        float alpha,
        Vector2 iconSize,
        float cellWidth,
        bool isWeave)
    {
        var effectiveAlpha = alpha * (stepCompleted ? 0.35f : 1f);
        var alphaPushed = effectiveAlpha < 0.999f;

        if (alphaPushed)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, effectiveAlpha);
        }

        ImGui.BeginGroup();

        var cellStartX = ImGui.GetCursorPosX();

        if (TryGetAction(actionId, out var action) && action.Icon != 0)
        {
            DrawKnownAction(
                action,
                actionId,
                step,
                iconSize,
                cellStartX,
                cellWidth,
                isWeave);
        }
        else
        {
            DrawUnknownAction(
                actionId,
                step,
                iconSize,
                cellStartX,
                cellWidth,
                isWeave);
        }

        ImGui.SetCursorPosX(cellStartX);
        ImGui.Dummy(new Vector2(cellWidth, 0));
        ImGui.EndGroup();

        if (alphaPushed)
        {
            ImGui.PopStyleVar();
        }
    }

    private void DrawKnownAction(
        LuminaAction action,
        uint actionId,
        int step,
        Vector2 iconSize,
        float cellStartX,
        float cellWidth,
        bool isWeave)
    {
        var actionName = action.Name.ToString();
        var icon = Plugin.TextureProvider
            .GetFromGameIcon(new GameIconLookup(action.Icon))
            .GetWrapOrEmpty();

        CentreNextItem(cellStartX, cellWidth, iconSize.X);
        ImGui.Image(icon.Handle, iconSize);

        if (isWeave)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRect(
                ImGui.GetItemRectMin(),
                ImGui.GetItemRectMax(),
                ImGui.GetColorU32(ImGuiCol.CheckMark),
                4f,
                ImDrawFlags.None,
                2f);
        }

        if (ImGui.IsItemHovered())
        {
            DrawActionTooltip(
                actionName,
                actionId,
                step,
                isWeave,
                plugin.SelectedSequence != null
                    ? plugin.GetStepGuidance(step)
                    : null);
        }

        DrawCentredLabel(
            isWeave ? $"↳ {actionName}" : actionName,
            cellStartX,
            cellWidth);
    }

    private static void DrawActionTooltip(
        string actionName,
        uint actionId,
        int step,
        bool isWeave,
        StepGuidance? guidance)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34f);

        ImGui.TextUnformatted(actionName);
        ImGui.TextDisabled(
            $"{(isWeave ? "Weave" : "GCD")} | Position {step + 1} | Action ID {actionId}");

        if (guidance != null)
        {
            ImGui.Separator();

            if (!string.IsNullOrWhiteSpace(guidance.Advice))
            {
                ImGui.TextWrapped(guidance.Advice);
            }

            if (!string.IsNullOrWhiteSpace(guidance.Timing))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Timing");
                ImGui.TextWrapped(guidance.Timing);
            }

            if (!string.IsNullOrWhiteSpace(guidance.CommonMistake))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("Common mistake");
                ImGui.TextWrapped(guidance.CommonMistake);
            }
        }

        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static void DrawUnknownAction(
        uint actionId,
        int step,
        Vector2 iconSize,
        float cellStartX,
        float cellWidth,
        bool isWeave)
    {
        CentreNextItem(cellStartX, cellWidth, iconSize.X);

        ImGui.Button($"?##MissingAction{step}", iconSize);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                $"Unknown action\n{(isWeave ? "Weave" : "GCD")}\nPosition: {step + 1}\nAction ID: {actionId}");
        }

        DrawCentredLabel(
            isWeave ? "↳ Unknown" : "Unknown",
            cellStartX,
            cellWidth);
    }

    private static void DrawCentredLabel(
        string actionName,
        float cellStartX,
        float cellWidth)
    {
        var label = ShortenLabel(actionName);
        var textWidth = ImGui.CalcTextSize(label).X;

        ImGui.SetCursorPosX(
            cellStartX + Math.Max(0, (cellWidth - textWidth) / 2));
        ImGui.TextUnformatted(label);

        if (label != actionName && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(actionName);
        }
    }

    private static void CentreNextItem(
        float cellStartX,
        float cellWidth,
        float itemWidth)
    {
        ImGui.SetCursorPosX(
            cellStartX + Math.Max(0, (cellWidth - itemWidth) / 2));
    }

    private static string FormatPhase(RotationPhase phase)
    {
        return phase switch
        {
            RotationPhase.PrePull => "Pre-pull",
            RotationPhase.Opener => "Opener",
            RotationPhase.Burst => "Burst",
            RotationPhase.Filler => "Filler",
            RotationPhase.Pooling => "Pooling",
            RotationPhase.Recovery => "Recovery",
            _ => phase.ToString()
        };
    }

    private static string ShortenLabel(string actionName)
    {
        if (actionName.Length <= MaximumLabelLength)
        {
            return actionName;
        }

        return string.Concat(
            actionName.AsSpan(0, MaximumLabelLength - 3),
            "...");
    }

    private static bool TryGetAction(
        uint actionId,
        out LuminaAction action)
    {
        var actionSheet = Plugin.DataManager.GetExcelSheet<LuminaAction>();
        return actionSheet.TryGetRow(actionId, out action);
    }
}
