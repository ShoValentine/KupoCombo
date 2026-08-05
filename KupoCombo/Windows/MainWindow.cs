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
            MaximumSize = new Vector2(700, 700)
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
                plugin.HasDynamicPractice
                    ? "No fixed sequence data was found for the current job. " +
                      "Data-driven Priority Practice remains available above."
                    : "No sequence or rule-policy data was found for the current job. " +
                      "Check the data files and Dalamud log for errors.");

            DrawTestingAndSettings();
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

        DrawTestingAndSettings();
    }

    private void DrawDynamicPracticePreview()
    {
        var definition = plugin.CurrentRulePolicy;

        if (definition == null)
        {
            return;
        }

        ImGui.Text("Data-driven Priority Practice");
        ImGui.TextWrapped(
            $"{definition.Job} — {definition.Name}. " +
            "The shared evaluator chooses the next GCD separately from " +
            "non-punitive weave suggestions.");

        if (!string.IsNullOrWhiteSpace(definition.Profile.Notes))
        {
            ImGui.TextDisabled(definition.Profile.Notes);
        }

        ImGui.Spacing();

        if (ImGui.Button(
                $"Start {definition.Job} Priority Practice",
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
                    DrawDynamicStateDetails();
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
                        $"Accepted GCDs: {plugin.CurrentStep}");
                    DrawDynamicStateDetails();
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

    private void DrawDynamicStateDetails()
    {
        var state = plugin.TrainingSession.Snapshot;
        var decision = plugin.TrainingSession.CurrentDecision;
        var rulePolicy = plugin.TrainingSession.Policy as RuleSetTrainingPolicy;

        ImGui.Spacing();
        ImGui.TextDisabled(
            $"Combo: {state.NativeComboActionId} " +
            $"({state.ComboRemainingSeconds:0.0}s)");

        if (rulePolicy != null)
        {
            var values = rulePolicy.Definition.StateInputs
                .Keys
                .Select(alias => $"{alias}: {state.GetStateValue(alias):0.##}")
                .ToArray();

            if (values.Length > 0)
            {
                ImGui.TextDisabled(string.Join(" | ", values));
            }
        }

        if (decision == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(decision.Reason))
        {
            ImGui.TextWrapped(
                $"GCD decision: {decision.Reason}");
        }

        if (!string.IsNullOrWhiteSpace(decision.SuggestionReason))
        {
            ImGui.TextWrapped(
                $"Weave advice: {decision.SuggestionReason}");
        }
    }

    private void DrawTestingAndSettings()
    {
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

        if (plugin.CurrentRulePolicy?.Job.Equals(
                "DRK",
                StringComparison.OrdinalIgnoreCase) == true &&
            ImGui.Button(
                "Run DRK Policy Self-Test",
                new Vector2(245, 0)))
        {
            RunDarkKnightPolicyDiagnostics(plugin.CurrentRulePolicy);
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

    private static void RunDarkKnightPolicyDiagnostics(
        KupoCombo.Models.RulePolicyDefinition definition)
    {
        var report = TrainingPolicyDiagnostics.RunDarkKnight(definition);

        foreach (var result in report.Results)
        {
            var message =
                $"{(result.Passed ? "PASS" : "FAIL")}: " +
                $"{result.Name} - {result.Detail}";

            if (result.Passed)
            {
                Plugin.Log.Information(message);
            }
            else
            {
                Plugin.Log.Error(message);
            }
        }

        var summary =
            $"DRK policy self-test: {report.PassedCount} passed, " +
            $"{report.FailedCount} failed.";

        if (report.Passed)
        {
            Plugin.ChatGui.Print(summary, "KupoCombo");
        }
        else
        {
            Plugin.ChatGui.PrintError(
                summary + " Check /xllog for details.",
                "KupoCombo");
        }
    }
}
