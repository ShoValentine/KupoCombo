# Action catalogue generator

`generate_action_catalogues.py` downloads the pinned English FFXIV game-data
sheets from `xivapi/ffxiv-datamining` and produces:

- `Data/Actions/pve-actions.json`, the complete compatibility catalogue.
- `Data/Actions/Jobs/<JOB>.json`, one readable catalogue for each combat job.

Only non-PvP player actions classified as spells, weaponskills, abilities, or
limit breaks are included. Removed actions with no current acquisition level
are excluded. Base-class and role actions are copied into every job catalogue
that can use them.

Before writing generated files, the script reads
`Data/Actions/curated-overrides.json` when present, otherwise the existing
`pve-actions.json`. Curated forecast effects, transformations, potency notes,
combo metadata, MP costs, charge corrections, and timeline locks therefore
survive a metadata refresh.

The upstream commit and game version are constants near the top of the script.
Updating those values and running the generator is the intended patch-update
workflow.
