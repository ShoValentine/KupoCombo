using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class GuidanceFile
{
    public int SchemaVersion { get; set; }

    public string Job { get; set; } = string.Empty;

    public List<SequenceGuidance> Sequences { get; set; } = new();
}

public sealed class SequenceGuidance
{
    public string SequenceId { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public TrainingPrompt? StartPrompt { get; set; }

    public TrainingPrompt? MistakePrompt { get; set; }

    public TrainingPrompt? CompletionPrompt { get; set; }

    public List<StepGuidance> Steps { get; set; } = new();
}

public sealed class StepGuidance
{
    // One-based sequence position. This intentionally remains independent
    // from the action ID because the same action can occur several times.
    public int Step { get; set; }

    public string Advice { get; set; } = string.Empty;

    public string Timing { get; set; } = string.Empty;

    public string CommonMistake { get; set; } = string.Empty;

    // Displayed when this step becomes the next expected action.
    public TrainingPrompt? Prompt { get; set; }
}

public sealed class TrainingPrompt
{
    public string Text { get; set; } = string.Empty;

    public float DurationSeconds { get; set; } = 4f;
}
