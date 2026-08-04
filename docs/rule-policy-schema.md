# KupoCombo rule policy schema

KupoCombo rule policies describe job behaviour as data. The evaluator is shared by every job; a policy supplies action aliases, state inputs, combos, conditions and prioritised rules.

The current schema version is `1`. The formal JSON Schema is stored at `Data/Schemas/rule-policy.schema.json`.

## Design boundary

A policy defines **what decisions mean**. It does not read game memory directly.

State inputs reference named providers such as:

- `player.mp`
- `drk.blood`
- `drk.darkside_ms`
- `mch.heat`
- `mch.battery`

The runtime resolves those provider names into values placed in `TrainingState`. Most policy behaviour should therefore be data-driven. A job may still need a small state-provider adapter for gauges that Dalamud exposes through job-specific structures, but it should not need a bespoke rotation-policy class.

## File structure

```json
{
  "schemaVersion": 1,
  "policies": []
}
```

A file may contain multiple policies for the same job, for example:

- a level-100 single-target profile
- a lower-level profile
- an AoE profile
- a classic-mode profile

## Policy sections

### `profile`

Declares the assumptions under which the policy is correct:

- target-count range
- continuous or interrupted uptime
- expected burst-cycle length
- explanatory notes

The evaluator must not claim that a policy is universally optimal outside its declared profile.

### `actions`

Maps stable, human-readable aliases to game action IDs.

```json
"actions": {
  "hardSlash": {
    "actionId": 3617,
    "lane": "gcd",
    "role": "graded",
    "minimumLevel": 1
  }
}
```

Aliases are case-insensitive inside KupoCombo policy loading.

Action roles:

- `graded`: using the wrong action may be evaluated as a training mistake
- `advisory`: displayed as guidance without corrupting GCD grading
- `observed`: tracked as state but not directly graded or suggested

`adjustedFrom` links an upgraded or transformed action to the base action the game replaces.

### `statuses`

Maps aliases to status IDs. Rules and conditions use aliases rather than embedding numeric IDs repeatedly.

### `stateInputs`

Declares named values supplied by the runtime state-provider registry.

Kinds:

- `integer`
- `number`
- `boolean`
- `timer`
- `resource`

Optional minimum, maximum and unit metadata support validation, debugging and future simulation.

### `combos`

Declares ordered GCD chains.

```json
"combos": {
  "souleater": {
    "steps": ["hardSlash", "syphonStrike", "souleater"],
    "minimumLevel": 26,
    "breaksOnOtherGcd": true
  }
}
```

### `rules`

Rules are evaluated in descending priority order.

For the GCD lane, the highest-priority matching rule supplies the preferred action. Explicit `acceptableActions` may be used without treating them as equally preferred.

For the weave lane, matching advisory rules may contribute suggestions in priority order. A weave suggestion must not change the current GCD decision or reset combo progress.

Rules may be disabled without deleting them by setting `enabled` to `false`.

## Condition groups

Every rule may contain:

```json
"conditions": {
  "all": [],
  "any": [],
  "none": []
}
```

Evaluation semantics:

- every condition in `all` must pass
- when `any` is non-empty, at least one condition in it must pass
- no condition in `none` may pass

An omitted or empty group imposes no restriction.

Supported condition sources include level, named state values, status state, cooldown state, combo state, adjusted actions, target count, combat time, accepted-action count and the last action.

## Rule types

### `continueCombo`

Returns the next valid step of a named combo. It understands the native game combo timer and can fall back to accepted training history when needed.

Required field: `combo`.

### `followAdjustedAction`

Uses the game-adjusted form of a base action when that form appears in an allowed list. This supports transformed actions and proc chains without job-specific branching code.

Required fields: `action`, `adjustedActions`.

### `preventResourceOvercap`

Recommends a spender when a named resource reaches a threshold. `incomingAction` and `incomingGain` may describe an impending gain, such as Souleater adding Blood.

Required fields: `action`, `resource`, `threshold`.

### `preventChargeOvercap`

Recommends an action before its cooldown charges cap.

Required fields: `action`, `cooldown`, `threshold`.

### `maintainStatus`

Recommends an action when a named status has less than the configured remaining duration.

Required fields: `action`, `status`, `minimumRemainingSeconds`.

### `spendStatusStacks`

Recommends an action while a named stacked status is active.

Required fields: `action`, `status`.

### `followProc`

Recommends an action while a named proc status is active.

Required fields: `action`, `status`.

### `useCooldown`

Recommends an action when its named cooldown is ready, subject to conditions and profile assumptions.

Required fields: `action`, `cooldown`.

### `useAction`

A general conditional action rule. This is intentionally the escape hatch for behaviour that does not yet justify a new reusable rule primitive.

Required field: `action`.

Repeated use of similar `useAction` rules across jobs is a signal to introduce a new generic rule type.

## Versioning

Breaking changes increment `schemaVersion`. Loaders must reject versions they do not understand rather than guessing.

Additive changes should remain compatible when possible. New rule types require evaluator support before profiles may use them.

## Reference profile

`Data/Policies/DRK.json` expresses the current DRK proof-of-concept through this schema. It is the first migration target for the generic evaluator and should eventually replace `DarkKnightComboPolicy.cs` without changing the existing DRK diagnostic outcomes.
