# PvE action catalogue

KupoCombo separates action mechanics from job strategy.

## Responsibilities

### `Data/Actions/pve-actions.json`

The generated aggregate catalogue stores every unique PvE combat action ID in
one compatibility and auditing index. It describes what an action is and what
predictable state changes occur when it is used:

- action ID, name, job and minimum level
- weaponskill, spell, ability or limit-break classification
- cast time, cooldown, timeline lock and maximum charges
- potency, combo potency and MP cost where curated or available
- combo and adjusted-action relationships
- predictable gauge, timer, status and action-transformation effects
- game-version and source provenance

### `Data/Actions/Jobs/{JOB}.json`

The runtime catalogue is divided into one file per combat job. Each file
contains every non-PvP combat action assigned to that job, including inherited
base-class actions, role actions, hidden transformations, proc follow-ups, pet
actions and other job-specific action IDs.

Shared actions deliberately appear in each job file that can use them. This
keeps runtime validation strict and lets a policy resolve its entire action set
without loading unrelated jobs.

### `Data/Actions/curated-overrides.json`

Generated sheet metadata cannot express every semantic gauge effect,
transformation or potency detail required by the forecast engine. This file
retains reviewed overrides that are merged into generated entries by action ID.

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

The evaluator combines those layers to produce the next GCD, weave advice and
the predicted Practice Plan.

## Generation and patch maintenance

`tools/ActionCatalogGenerator/generate_action_catalogues.py` reads pinned
English Action, ActionCategory and ClassJobCategory sheets from the maintained
FFXIV game-data export. It excludes PvP, enemies, duties, mounts, crafting,
gathering and system actions, then writes the aggregate and all per-job files.

The source commit and game version are pinned near the top of the generator.
For a patch update:

1. Update those two constants.
2. Regenerate the catalogues.
3. Review additions, removals and changed timing metadata.
4. Update curated overrides where action semantics changed.
5. Run the policy validator and live training-dummy checks.

CI regenerates the files and fails when the committed output has drifted. It
also loads every job file independently and verifies that the union of per-job
action IDs exactly matches the aggregate.

Universal structural facts should come from game data where possible. Curated
semantic effects should be checked against the current official job guide and
then cross-checked with reputable community references. Conflicting sources
should fail review rather than silently replacing a verified override.

## Current coverage

The FFXIV 7.55 catalogue contains 1,070 unique non-PvP combat action IDs across
22 job catalogues:

- all four tanks
- all four healers
- all six melee jobs
- all three physical-ranged jobs
- all four standard magical-ranged jobs
- Blue Mage

This is action-data coverage, not finished Practice Mode policy coverage. DRK
and MCH currently have complete predictive policy profiles; the other job files
provide the action substrate for future policies.

## Forecast timing

`recastSeconds` records the action's own cooldown. `timelineLockSeconds` records
how far the simulated action timeline should advance after a GCD action. These
are deliberately separate because actions such as Drill have a long personal
cooldown while still occupying a normal GCD, and Overheat weaponskills use a
shorter GCD cadence.
