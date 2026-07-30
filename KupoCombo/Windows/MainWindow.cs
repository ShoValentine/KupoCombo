using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KupoCombo.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private int selectedSequence;

    public MainWindow(Plugin plugin)
        : base("KupoCombo Control##KupoComboControl")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 260),
            MaximumSize = new Vector2(700, 500)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.Text("Practice sequence");
        var currentJob =
    string.IsNullOrWhiteSpace(plugin.CurrentJob)
        ? "Unavailable"
        : plugin.CurrentJob;

        ImGui.TextDisabled(
            $"Current job: {currentJob}");

        ImGui.Spacing();

        if (plugin.Sequences.Count == 0)
        {
            ImGui.Spacing();

            ImGui.TextWrapped(
                "No sequence data found for current job. " +
                "Check Sequences.json and the Dalamud log for errors.");

            ImGui.Spacing();

            if (ImGui.Button("Settings"))
            {
                plugin.ToggleConfigUi();
            }

            return;
        }

        if (selectedSequence >= plugin.Sequences.Count)
        {
            selectedSequence = 0;
        }



        var sequenceLabels = plugin.Sequences
    .Select(sequence => sequence.DisplayName)
    .ToArray();

        ImGui.SetNextItemWidth(-1);

        ImGui.Combo(
            "##Sequence",
            ref selectedSequence,
            sequenceLabels,
            sequenceLabels.Length);

        DrawSelectedSequenceDetails();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawStatus();

        ImGui.Spacing();

        if (ImGui.Button("Start", new Vector2(120, 0)))
        {
            plugin.StartTraining(
                plugin.Sequences[selectedSequence]);
        }

        ImGui.SameLine();

        if (ImGui.Button("Stop", new Vector2(120, 0)))
        {
            plugin.StopTraining();
        }

        ImGui.Spacing();

        var overlayButtonText = plugin.OverlayVisible
            ? "Hide Overlay"
            : "Show Overlay";

        if (ImGui.Button(
                overlayButtonText,
                new Vector2(245, 0)))
        {
            plugin.ToggleOverlay();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Temporary testing controls");

        if (ImGui.Button(
                "Simulate Correct Action",
                new Vector2(245, 0)))
        {
            plugin.SimulateCorrectAction();
        }

        ImGui.Spacing();

        ImGui.TextDisabled(
            "This button will later be replaced by real actions performed in-game.");

        ImGui.Spacing();

        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
    }

    private void DrawSelectedSequenceDetails()
    {
        var sequence = plugin.Sequences[selectedSequence];

        ImGui.Spacing();

        ImGui.TextDisabled(
            $"{sequence.Category} | " +
            $"Level {sequence.MinimumLevel} | " +
            $"{sequence.Actions.Count} actions");
    }

    private void DrawStatus()
    {
        ImGui.Text("Status:");
        ImGui.SameLine();

        if (plugin.IsSequenceComplete)
        {
            ImGui.Text("Sequence complete!");
            return;
        }

        if (plugin.IsTraining)
        {
            var nextStep = plugin.CurrentStep + 1;

            ImGui.Text(
                $"Practising {plugin.SelectedSequenceName} " +
                $"| Next step: {nextStep}/{plugin.CurrentSequenceLength}");

            return;
        }

        ImGui.Text("Stopped");
    }
}
