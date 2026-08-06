using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public sealed class TrainingForecastStep
{
    public int Offset { get; init; }

    public double StartsAtSeconds { get; init; }

    public float DurationSeconds { get; init; }

    public RotationPhase Phase { get; init; } = RotationPhase.Filler;

    public uint GcdActionId { get; init; }

    public IReadOnlyList<uint> SuggestedActionIds { get; init; } =
        Array.Empty<uint>();

    public IReadOnlyDictionary<string, ResourceProjection> ResourceProjections
    {
        get;
        init;
    } = new Dictionary<string, ResourceProjection>(
        StringComparer.OrdinalIgnoreCase);

    public string Reason { get; init; } = string.Empty;

    public string SuggestionReason { get; init; } = string.Empty;

    public float Confidence { get; init; } = 1f;

    public ResourceProjection? GetResourceProjection(string resource)
    {
        return ResourceProjections.TryGetValue(resource, out var projection)
            ? projection
            : null;
    }
}
