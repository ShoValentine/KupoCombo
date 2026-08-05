using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KupoCombo.Models;

public sealed class PveActionCatalogFile
{
    public int SchemaVersion { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public string GeneratedFrom { get; set; } = string.Empty;

    public List<PveActionCatalogEntry> Actions { get; set; } = new();
}

public sealed class PveActionCatalogEntry
{
    public uint ActionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Job { get; set; } = string.Empty;

    public PveActionKind Kind { get; set; }

    public int MinimumLevel { get; set; }

    public double CastSeconds { get; set; }

    public double RecastSeconds { get; set; }

    public int MaximumCharges { get; set; } = 1;

    public int? Potency { get; set; }

    public uint? ComboFromActionId { get; set; }

    public int? ComboPotency { get; set; }

    public int? MpCost { get; set; }

    public uint? AdjustedFromActionId { get; set; }

    public string Source { get; set; } = string.Empty;

    public List<PolicyForecastEffectDefinition> ForecastEffects { get; set; } =
        new();
}

[JsonConverter(typeof(JsonStringEnumConverter<PveActionKind>))]
public enum PveActionKind
{
    Weaponskill,
    Spell,
    Ability
}
