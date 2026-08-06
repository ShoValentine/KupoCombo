# Action catalogue generator

`generate_action_catalogues.py` downloads the pinned English FFXIV game-data
sheets from `xivapi/ffxiv-datamining` and produces:

- `Data/Actions/pve-actions.json`, the complete compatibility catalogue.
- `Data/Actions/Jobs/<JOB>.json`, one readable catalogue for each combat job.

The generator includes non-PvP combat actions assigned to a playable job or
base class. This includes visible player actions, role actions, hidden proc and
transformation follow-ups, pet actions, mudra outcomes, and other job-specific
actions that the game still assigns a current acquisition level. Enemy, duty,
mount, crafting, gathering, system, and PvP actions are excluded.

Base-class and role actions are copied into every job catalogue that can use
them. The aggregate stores each action ID once for compatibility and auditing;
runtime policy loading uses the active job's file.

Before writing generated files, the script reads
`Data/Actions/curated-overrides.json` when present, otherwise the existing
`pve-actions.json`. Curated forecast effects, transformations, potency notes,
combo metadata, MP costs, charge corrections, and timeline locks therefore
survive a metadata refresh.

The upstream commit and game version are constants near the top of the script.
Updating those values and running the generator is the intended patch-update
workflow. CI regenerates the catalogues and fails when committed output differs.
