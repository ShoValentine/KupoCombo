using System;
using System.Collections.Generic;

namespace KupoCombo.Models;

public enum TrainingMistakeResponse
{
    ResetProgress,
    KeepProgress
}

public sealed class TrainingDecision
{
    public uint PreferredActionId { get; init; }

    public IReadOnlyList<uint> AcceptableActionIds { get; init; } =
        Array.Empty<uint>();

    public string Reason { get; init; } = string.Empty;

    public TrainingMistakeResponse MistakeResponse { get; init; } =
        TrainingMistakeResponse.ResetProgress;

    public bool IsComplete { get; init; }

    public bool IsPreferred(uint actionId)
    {
        return !IsComplete && actionId == PreferredActionId;
    }

    public bool IsActionAccepted(uint actionId)
    {
        if (IsPreferred(actionId))
        {
            return true;
        }

        foreach (var acceptableActionId in AcceptableActionIds)
        {
            if (actionId == acceptableActionId)
            {
                return true;
            }
        }

        return false;
    }

    public static TrainingDecision Complete(string reason)
    {
        return new TrainingDecision
        {
            IsComplete = true,
            Reason = reason
        };
    }
}
