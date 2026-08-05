using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class TrainingForecastStep
{
    public int Offset { get; init; }

    public uint GcdActionId { get; init; }

    public IReadOnlyList<uint> SuggestedActionIds { get; init; } =
        Array.Empty<uint>();

    public string Reason { get; init; } = string.Empty;

    public string SuggestionReason { get; init; } = string.Empty;

    public float Confidence { get; init; } = 1f;
}
