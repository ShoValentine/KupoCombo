using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace KupoCombo.Windows;

public sealed class OverlayWindow : Window, IDisposable
{
    private const float BaseIconSize = 64f;
    private const float BaseCellWidth = 120f;
    private const int MaximumLabelLength = 18;

    private readonly Plugin plugin;

    public OverlayWindow(Plugin plugin)
        : base(
            "KupoCombo Overlay##KupoComboOverlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNavInputs |
            ImGuiWindowFlags.NoNavFocus)
    {
        this.plugin = plugin;
        IsOpen = false;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var selectedSequence = plugin.SelectedSequence;

        if (selectedSequence == null)
        {
            ImGui.Text("No sequence selected.");
            return;
        }

        ImGui.Text(selectedSequence.DisplayName);
        ImGui.Spacing();

        var scale = ImGuiHelpers.GlobalScale;

        var iconSize = new Vector2(
            BaseIconSize * scale,
            BaseIconSize * scale);

        var cellWidth = BaseCellWidth * scale;

        for (var step = 0;
             step < selectedSequence.Actions.Count;
             step++)
        {
            if (step > 0)
            {
                ImGui.SameLine();
            }

            var actionId = selectedSequence.Actions[step];
            var stepCompleted = step < plugin.CurrentStep;

            DrawActionCell(
                actionId,
                step,
                stepCompleted,
                iconSize,
                cellWidth);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawStatus(selectedSequence.Actions);
    }

    private static void DrawActionCell(
        uint actionId,
        int step,
        bool stepCompleted,
        Vector2 iconSize,
        float cellWidth)
    {
        if (stepCompleted)
        {
            ImGui.PushStyleVar(
                ImGuiStyleVar.Alpha,
                0.35f);
        }

        ImGui.BeginGroup();

        var cellStartX = ImGui.GetCursorPosX();

        if (TryGetAction(actionId, out var action) &&
            action.Icon != 0)
        {
            var actionName = action.Name.ToString();

            var icon = Plugin.TextureProvider
                .GetFromGameIcon(
                    new GameIconLookup(action.Icon))
                .GetWrapOrEmpty();

            CentreNextItem(
                cellStartX,
                cellWidth,
                iconSize.X);

            ImGui.Image(
                icon.Handle,
                iconSize);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"{actionName}\n" +
                    $"Step: {step + 1}\n" +
                    $"Action ID: {actionId}");
            }

            DrawCentredLabel(
                actionName,
                cellStartX,
                cellWidth);
        }
        else
        {
            CentreNextItem(
                cellStartX,
                cellWidth,
                iconSize.X);

            ImGui.Button(
                $"?##MissingAction{step}",
                iconSize);

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"Unknown action\n" +
                    $"Step: {step + 1}\n" +
                    $"Action ID: {actionId}");
            }

            DrawCentredLabel(
                "Unknown",
                cellStartX,
                cellWidth);
        }

        // Ensures every action cell occupies the same width.
        ImGui.SetCursorPosX(cellStartX);
        ImGui.Dummy(new Vector2(cellWidth, 0));

        ImGui.EndGroup();

        if (stepCompleted)
        {
            ImGui.PopStyleVar();
        }
    }

    private static void DrawCentredLabel(
        string actionName,
        float cellStartX,
        float cellWidth)
    {
        var label = ShortenLabel(actionName);
        var textWidth = ImGui.CalcTextSize(label).X;

        ImGui.SetCursorPosX(
            cellStartX +
            Math.Max(0, (cellWidth - textWidth) / 2));

        ImGui.TextUnformatted(label);

        if (label != actionName &&
            ImGui.IsItemHovered())
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
            cellStartX +
            Math.Max(0, (cellWidth - itemWidth) / 2));
    }

    private static string ShortenLabel(
        string actionName)
    {
        if (actionName.Length <= MaximumLabelLength)
        {
            return actionName;
        }

        return string.Concat(
            actionName.AsSpan(
                0,
                MaximumLabelLength - 3),
            "...");
    }

    private static bool TryGetAction(
        uint actionId,
        out LuminaAction action)
    {
        var actionSheet =
            Plugin.DataManager.GetExcelSheet<LuminaAction>();

        return actionSheet.TryGetRow(
            actionId,
            out action);
    }

    private void DrawStatus(
        IReadOnlyList<uint> actions)
    {
        if (plugin.IsSequenceComplete)
        {
            ImGui.Text("Sequence complete!");
            return;
        }

        if (!plugin.IsTraining)
        {
            ImGui.Text("Training stopped.");
            return;
        }

        if (plugin.CurrentStep >= actions.Count)
        {
            ImGui.Text("Sequence complete!");
            return;
        }

        var nextActionId =
            actions[plugin.CurrentStep];

        if (TryGetAction(
                nextActionId,
                out var nextAction))
        {
            ImGui.Text(
                $"Next: {nextAction.Name}");

            ImGui.TextDisabled(
                $"Step {plugin.CurrentStep + 1}" +
                $"/{actions.Count} | " +
                $"Action ID: {nextActionId}");
        }
        else
        {
            ImGui.Text(
                $"Next action: " +
                $"{plugin.CurrentStep + 1}/{actions.Count}");

            ImGui.TextDisabled(
                $"Unknown action ID: {nextActionId}");
        }
    }
}
