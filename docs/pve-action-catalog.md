# PvE action catalogue

KupoCombo separates action mechanics from job strategy.

## Responsibilities

### `Data/Actions/pve-actions.json`

The shared action catalogue describes what an action is and what predictable state changes occur when it is used:

- action ID, name, job and minimum level
- weaponskill, spell or ability classification
- cast time, cooldown, timeline lock and maximum charges
- potency, combo potency and MP cost where applicable
- combo and adjusted-action relationships
- predictable gauge, timer, status and action-transformation effects
- game-version and source provenance

### `Data/Policies/{JOB}.json`

A job policy describes when an action should be used:

- priority ordering
- burst alignment and pooling rules
- overcap prevention thresholds
- acceptable alternative actions
- state conditions and training explanations

### Runtime state readers

The state reader reports what is true now:

- current gauges and resources
- active statuses and remaining durations
- native combo state
- current adjusted actions
- live cooldown and charge snapshots

The evaluator combines those three layers to produce the next GCD, weave advice and a short predicted action ribbon.

## Patch maintenance

When a patch changes an action's potency, cooldown, charges, cost, gauge effect, proc or transformation, update the catalogue entry first. Policies only need changes when the optimal decision rule or burst placement changes.

Each catalogue release must update `gameVersion` and preserve source notes. Universal structural facts should come from game/Lumina data where possible. Curated semantic effects should be checked against the current official job guide and then cross-checked with reputable community references.

Community pages are useful research aids but must not be treated as automatically current. Conflicting sources should fail review rather than silently replacing a verified catalogue entry.

## Current coverage

The initial catalogue contains the 37 actions used by the level-100 DRK and MCH training profiles. This is the architectural seed, not a claim of complete roster coverage.

When adding a job:

1. Add every action referenced by the policy to the catalogue.
2. Define state inputs and status aliases in the job policy.
3. Add the job's decision rules.
4. Add deterministic scenarios for core mechanics and burst ordering.
5. Validate the action ribbon against an authoritative opener or rotation reference.
6. Confirm the result against a training dummy.

## Forecast timing

`recastSeconds` records the action's own cooldown. `timelineLockSeconds` records how far the simulated action timeline should advance after a GCD action. These are deliberately separate because actions such as Drill have a long personal cooldown while still occupying a normal GCD, and Overheat weaponskills use a shorter GCD cadence.
