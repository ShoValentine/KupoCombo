using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using KupoCombo.Models;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace KupoCombo.Windows;

public sealed class OverlayWindow : Window, IDisposable
{
    private const float BaseIconSize = 64f;
    private const float BaseCellWidth = 120f;
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

        Size = new Vector2(650, 240);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(220, 120),
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

        var decision = plugin.TrainingSession.CurrentDecision;

        if (!plugin.IsDynamicPractice ||
            decision == null ||
            decision.IsComplete)
        {
            ImGui.Text("No sequence selected.");
            return;
        }

        ImGui.Text(plugin.SelectedSequenceName);

        if (!string.IsNullOrWhiteSpace(decision.Reason))
        {
            ImGui.TextWrapped(decision.Reason);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Preferred");

        DrawActionGrid(
            new[] { decision.PreferredActionId },
            completedCount: 0);

        if (decision.AcceptableActionIds.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Also acceptable");

        DrawActionGrid(
            decision.AcceptableActionIds,
            completedCount: 0);
    }

    private void DrawActionGrid(
        IReadOnlyList<uint> actions,
        int completedCount)
    {
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
        var iconSize = new Vector2(iconLength, iconLength);
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
            iconSize.Y +
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

            DrawActionCell(
                actionId,
                step,
                stepCompleted,
                iconSize,
                cellWidth);
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
        Vector2 iconSize,
        float cellWidth)
    {
        if (stepCompleted)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
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
                cellWidth);
        }
        else
        {
            DrawUnknownAction(
                actionId,
                step,
                iconSize,
                cellStartX,
                cellWidth);
        }

        ImGui.SetCursorPosX(cellStartX);
        ImGui.Dummy(new Vector2(cellWidth, 0));
        ImGui.EndGroup();

        if (stepCompleted)
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
        float cellWidth)
    {
        var actionName = action.Name.ToString();
        var icon = Plugin.TextureProvider
            .GetFromGameIcon(new GameIconLookup(action.Icon))
            .GetWrapOrEmpty();

        CentreNextItem(cellStartX, cellWidth, iconSize.X);
        ImGui.Image(icon.Handle, iconSize);

        if (ImGui.IsItemHovered())
        {
            DrawActionTooltip(
                actionName,
                actionId,
                step,
                plugin.GetStepGuidance(step));
        }

        DrawCentredLabel(actionName, cellStartX, cellWidth);
    }

    private static void DrawActionTooltip(
        string actionName,
        uint actionId,
        int step,
        StepGuidance? guidance)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 34f);

        ImGui.TextUnformatted(actionName);
        ImGui.TextDisabled(
            $"Step {step + 1} | Action ID {actionId}");

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
        float cellWidth)
    {
        CentreNextItem(cellStartX, cellWidth, iconSize.X);

        ImGui.Button($"?##MissingAction{step}", iconSize);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                $"Unknown action\nStep: {step + 1}\nAction ID: {actionId}");
        }

        DrawCentredLabel("Unknown", cellStartX, cellWidth);
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
