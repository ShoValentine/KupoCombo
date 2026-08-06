namespace KupoCombo.Models;

public enum ActionResolutionKind
{
    Unresolved,
    Exact,
    ClientAdjusted
}

public enum ActionResolutionRole
{
    None,
    Preferred,
    Acceptable,
    Suggested,
    Advisory,
    Tracked
}

public readonly record struct ActionResolution
{
    public ActionResolution(
        ActionObservation observation,
        uint policyActionId,
        uint matchedCandidateActionId,
        uint adjustedCandidateActionId,
        ActionResolutionKind kind,
        ActionResolutionRole role)
    {
        Observation = observation;
        PolicyActionId = policyActionId;
        MatchedCandidateActionId = matchedCandidateActionId;
        AdjustedCandidateActionId = adjustedCandidateActionId;
        Kind = kind;
        Role = role;
    }

    public ActionObservation Observation { get; }

    public uint ExecutedActionId => Observation.ExecutedActionId;

    public uint PolicyActionId { get; }

    public uint MatchedCandidateActionId { get; }

    public uint AdjustedCandidateActionId { get; }

    public ActionResolutionKind Kind { get; }

    public ActionResolutionRole Role { get; }

    public bool WasResolved => Kind != ActionResolutionKind.Unresolved;
}
