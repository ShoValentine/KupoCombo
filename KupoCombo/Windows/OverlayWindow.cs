using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace KupoCombo.Windows;

public sealed class OverlayWindow : Window, IDisposable
{
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

        for (var step = 0;
             step < selectedSequence.Actions.Count;
             step++)
        {
            var actionId = selectedSequence.Actions[step];
            var stepCompleted = step < plugin.CurrentStep;

            if (stepCompleted)
            {
                ImGui.PushStyleVar(
                    ImGuiStyleVar.Alpha,
                    0.35f);
            }

            ImGui.Button(
                $"{step + 1}##KupoComboStep{step}",
                new Vector2(64, 64));

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    $"Step {step + 1}\nAction ID: {actionId}");
            }

            if (stepCompleted)
            {
                ImGui.PopStyleVar();
            }

            if (step < selectedSequence.Actions.Count - 1)
            {
                ImGui.SameLine();
            }
        }

        ImGui.Spacing();

        DrawStatus(selectedSequence.Actions);
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

        var nextActionId = actions[plugin.CurrentStep];

        ImGui.Text(
            $"Next action: {plugin.CurrentStep + 1}/{actions.Count}");

        ImGui.TextDisabled(
            $"Action ID: {nextActionId}");
    }
}
