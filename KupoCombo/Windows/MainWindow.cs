using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using KupoCombo.Services;

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
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(700, 620)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.Text("Training sequence");

        var currentJob = string.IsNullOrWhiteSpace(plugin.CurrentJob)
            ? "Unavailable"
            : plugin.CurrentJob;

        ImGui.TextDisabled($"Current job: {currentJob}");
        ImGui.Spacing();

        DrawDynamicPracticePreview();

        if (plugin.Sequences.Count == 0)
        {
            ImGui.TextWrapped(
                "No sequence data was found for the current job. " +
                "Check the sequence files and Dalamud log for errors.");

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

        DrawTrainingOptions();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawStatus();

        ImGui.Spacing();

        if (ImGui.Button("Start", new Vector2(120, 0)))
        {
            plugin.StartTraining(plugin.Sequences[selectedSequence]);
        }

        ImGui.SameLine();

        if (ImGui.Button("Stop", new Vector2(120, 0)))
        {
            plugin.StopTraining();
        }

        ImGui.Spacing();

        var overlayButtonText = plugin.OverlayVisible
            ? "Hide Sequence Overlay"
            : "Show Sequence Overlay";

        if (ImGui.Button(overlayButtonText, new Vector2(245, 0)))
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

        if (ImGui.Button(
                "Show Test Moogle Prompt",
                new Vector2(245, 0)))
        {
            plugin.ShowTestPrompt();
        }

        ImGui.Spacing();

        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
    }

    private void DrawDynamicPracticePreview()
    {
        if (!plugin.CurrentJob.Equals("DRK", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ImGui.Text("Conditional practice preview");
        ImGui.TextWrapped(
            "Runs the DRK core combo indefinitely and recalculates the next " +
            "recommended action after every accepted input.");

        ImGui.Spacing();

        if (ImGui.Button(
                "Start Endless DRK Combo",
                new Vector2(245, 0)))
        {
            plugin.StartDynamicPractice();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawTrainingOptions()
    {
        var showPrompts = plugin.Configuration.ShowTrainingPrompts;

        if (ImGui.Checkbox("Show training prompts", ref showPrompts))
        {
            plugin.SetTrainingPromptsEnabled(showPrompts);
        }

        ImGui.TextDisabled(
            "Disable prompts for pure sequence repetition. " +
            "Icon mouseover advice remains available.");
    }

    private void DrawSelectedSequenceDetails()
    {
        var sequence = plugin.Sequences[selectedSequence];

        ImGui.Spacing();
        ImGui.TextDisabled(
            $"{sequence.Category} | Level {sequence.MinimumLevel} | " +
            $"{sequence.Actions.Count} actions");

        var guidance = plugin.Guidance.Sequences.FirstOrDefault(
            item => item.SequenceId.Equals(
                sequence.Id,
                StringComparison.OrdinalIgnoreCase));

        if (guidance != null &&
            !string.IsNullOrWhiteSpace(guidance.Summary))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(guidance.Summary);
        }
    }

    private void DrawStatus()
    {
        ImGui.Text("Status:");
        ImGui.SameLine();

        switch (plugin.TrainingSession.State)
        {
            case TrainingSessionState.Complete:
                ImGui.Text("Sequence complete!");
                return;

            case TrainingSessionState.Armed:
                if (plugin.IsDynamicPractice)
                {
                    ImGui.Text(
                        $"Armed | {plugin.SelectedSequenceName}");
                    return;
                }

                ImGui.Text(
                    $"Armed | Begin with step 1/{plugin.CurrentSequenceLength}");
                return;

            case TrainingSessionState.Running:
                if (plugin.IsDynamicPractice)
                {
                    ImGui.Text(
                        $"Practising {plugin.SelectedSequenceName} | " +
                        $"Accepted actions: {plugin.CurrentStep}");
                    return;
                }

                ImGui.Text(
                    $"Practising {plugin.SelectedSequenceName} | " +
                    $"Next step: {plugin.CurrentStep + 1}" +
                    $"/{plugin.CurrentSequenceLength}");
                return;

            default:
                ImGui.Text("Stopped");
                return;
        }
    }
}
