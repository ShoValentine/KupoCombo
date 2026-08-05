using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KupoCombo.Models;

public sealed class RulePolicyFile
{
    public int SchemaVersion { get; set; }

    public List<RulePolicyDefinition> Policies { get; set; } = new();
}

public sealed class RulePolicyDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Job { get; set; } = string.Empty;

    public int MinimumLevel { get; set; }

    public int? MaximumLevel { get; set; }

    public PolicyProfileDefinition Profile { get; set; } = new();

    public Dictionary<string, PolicyActionDefinition> Actions { get; set; } =
        new();

    public Dictionary<string, uint> Statuses { get; set; } = new();

    public Dictionary<string, PolicyStateInputDefinition> StateInputs { get; set; } =
        new();

    public Dictionary<string, PolicyComboDefinition> Combos { get; set; } =
        new();

    public List<PolicyRuleDefinition> Rules { get; set; } = new();
}

public sealed class PolicyProfileDefinition
{
    public int MinimumTargetCount { get; set; } = 1;

    public int MaximumTargetCount { get; set; } = 1;

    public bool AssumesContinuousUptime { get; set; } = true;

    public int BurstCycleSeconds { get; set; } = 120;

    public int MinorBurstCycleSeconds { get; set; } = 60;

    public int OpenerDurationSeconds { get; set; } = 25;

    public int BurstWindowSeconds { get; set; } = 20;

    public int PoolingWindowSeconds { get; set; } = 15;

    public string Notes { get; set; } = string.Empty;
}

public sealed class PolicyActionDefinition
{
    public uint ActionId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public PolicyLane Lane { get; set; }

    public PolicyActionRole Role { get; set; } = PolicyActionRole.Graded;

    public int MinimumLevel { get; set; }

    public int? MaximumLevel { get; set; }

    public string AdjustedFrom { get; set; } = string.Empty;

    [JsonIgnore]
    public PveActionKind? Kind { get; set; }

    [JsonIgnore]
    public double CastSeconds { get; set; }

    [JsonIgnore]
    public double RecastSeconds { get; set; }

    [JsonIgnore]
    public double TimelineLockSeconds { get; set; } = 2.5d;

    [JsonIgnore]
    public int MaximumCharges { get; set; } = 1;

    [JsonIgnore]
    public int? Potency { get; set; }

    [JsonIgnore]
    public int? ComboPotency { get; set; }

    [JsonIgnore]
    public int? MpCost { get; set; }

    public List<PolicyForecastEffectDefinition> ForecastEffects { get; set; } =
        new();
}

public sealed class PolicyForecastEffectDefinition
{
    public PolicyForecastEffectType Type { get; set; }

    public string State { get; set; } = string.Empty;

    public double Value { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public string Status { get; set; } = string.Empty;

    public float DurationSeconds { get; set; }

    public int Stacks { get; set; } = 1;

    public string Action { get; set; } = string.Empty;

    public string AdjustedAction { get; set; } = string.Empty;
}

public sealed class PolicyStateInputDefinition
{
    public PolicyStateValueKind Kind { get; set; }

    public string Provider { get; set; } = string.Empty;

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public string Unit { get; set; } = string.Empty;
}

public sealed class PolicyComboDefinition
{
    public List<string> Steps { get; set; } = new();

    public int MinimumLevel { get; set; }

    public bool BreaksOnOtherGcd { get; set; } = true;
}

public sealed class PolicyRuleDefinition
{
    public string Id { get; set; } = string.Empty;

    public PolicyRuleType Type { get; set; }

    public PolicyLane Lane { get; set; }

    public int Priority { get; set; }

    public string Action { get; set; } = string.Empty;

    public List<string> AcceptableActions { get; set; } = new();

    public string Combo { get; set; } = string.Empty;

    public string Resource { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Cooldown { get; set; } = string.Empty;

    public string IncomingAction { get; set; } = string.Empty;

    public double? Threshold { get; set; }

    public double? IncomingGain { get; set; }

    public double? MinimumRemainingSeconds { get; set; }

    public int? MinimumCharges { get; set; }

    public List<string> AdjustedActions { get; set; } = new();

    public PolicyConditionSet Conditions { get; set; } = new();

    public string Reason { get; set; } = string.Empty;

    public string SuggestionReason { get; set; } = string.Empty;

    public TrainingMistakeResponse MistakeResponse { get; set; } =
        TrainingMistakeResponse.KeepProgress;

    public bool Enabled { get; set; } = true;
}

public sealed class PolicyConditionSet
{
    public List<PolicyConditionDefinition> All { get; set; } = new();

    public List<PolicyConditionDefinition> Any { get; set; } = new();

    public List<PolicyConditionDefinition> None { get; set; } = new();
}

public sealed class PolicyConditionDefinition
{
    public PolicyConditionSource Source { get; set; }

    public string Key { get; set; } = string.Empty;

    public PolicyComparisonOperator Operator { get; set; }

    public JsonElement Value { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyLane>))]
public enum PolicyLane
{
    Gcd,
    Weave
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyActionRole>))]
public enum PolicyActionRole
{
    Graded,
    Advisory,
    Observed
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyForecastEffectType>))]
public enum PolicyForecastEffectType
{
    AddStateValue,
    SetStateValue,
    AddStatus,
    RemoveStatus,
    SetAdjustedAction,
    ResetAdjustedAction
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyStateValueKind>))]
public enum PolicyStateValueKind
{
    Integer,
    Number,
    Boolean,
    Timer,
    Resource
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyRuleType>))]
public enum PolicyRuleType
{
    ContinueCombo,
    FollowAdjustedAction,
    PreventResourceOvercap,
    PreventChargeOvercap,
    MaintainStatus,
    SpendStatusStacks,
    FollowProc,
    UseCooldown,
    UseAction
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyConditionSource>))]
public enum PolicyConditionSource
{
    Level,
    StateValue,
    StatusActive,
    StatusStacks,
    StatusRemainingSeconds,
    CooldownReady,
    CooldownCharges,
    ComboAction,
    ComboRemainingSeconds,
    AdjustedAction,
    TargetCount,
    CombatTimeSeconds,
    AcceptedActionCount,
    LastAction
}

[JsonConverter(typeof(JsonStringEnumConverter<PolicyComparisonOperator>))]
public enum PolicyComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}
