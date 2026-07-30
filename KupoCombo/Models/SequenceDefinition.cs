using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class SequenceFile
{
    public int SchemaVersion { get; set; }

    public List<SequenceDefinition> Sequences { get; set; } = new();
}

public sealed class SequenceDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Job { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public int MinimumLevel { get; set; }

    public List<uint> Actions { get; set; } = new();

    public string DisplayName => $"{Job} — {Name}";
}
