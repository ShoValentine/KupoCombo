#!/usr/bin/env python3
"""Generate KupoCombo PvE action catalogues from exported FFXIV game sheets."""

from __future__ import annotations

import argparse
import csv
import io
import json
import shutil
import sys
import time
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable

SOURCE_REPOSITORY = "xivapi/ffxiv-datamining"
SOURCE_COMMIT = "c142b1269a76e9e3fffc42f984a5f193ba565ddc"
GAME_VERSION = "7.55"
SOURCE_BASE = (
    f"https://raw.githubusercontent.com/{SOURCE_REPOSITORY}/"
    f"{SOURCE_COMMIT}/csv/en"
)

JOBS = (
    "PLD", "WAR", "DRK", "GNB",
    "WHM", "SCH", "AST", "SGE",
    "MNK", "DRG", "NIN", "SAM", "RPR", "VPR",
    "BRD", "MCH", "DNC",
    "BLM", "SMN", "RDM", "PCT", "BLU",
)

BASE_CLASS_JOBS = {
    "GLA": ("PLD",),
    "PGL": ("MNK",),
    "MRD": ("WAR",),
    "LNC": ("DRG",),
    "ARC": ("BRD",),
    "CNJ": ("WHM",),
    "THM": ("BLM",),
    "ACN": ("SMN", "SCH"),
    "ROG": ("NIN",),
}

ACTION_KINDS = {
    "Spell": "spell",
    "Weaponskill": "weaponskill",
    "Ability": "ability",
    "Limit Break": "limitBreak",
}

CURATED_KEYS = (
    "timelineLockSeconds",
    "maximumCharges",
    "potency",
    "comboFromActionId",
    "comboPotency",
    "mpCost",
    "adjustedFromActionId",
    "forecastEffects",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path("Data/Actions"),
        help="Directory containing pve-actions.json and the Jobs subdirectory.",
    )
    parser.add_argument(
        "--overrides",
        type=Path,
        default=None,
        help="Optional curated override catalogue. Defaults to curated-overrides.json, then pve-actions.json.",
    )
    return parser.parse_args()


def download_csv(name: str) -> list[dict[str, str]]:
    url = f"{SOURCE_BASE}/{name}.csv"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "KupoCombo-action-catalog-generator/1.0"},
    )

    last_error: Exception | None = None
    for attempt in range(1, 4):
        try:
            with urllib.request.urlopen(request, timeout=90) as response:
                payload = response.read().decode("utf-8-sig")
            return list(csv.DictReader(io.StringIO(payload)))
        except (urllib.error.URLError, TimeoutError) as error:
            last_error = error
            if attempt < 3:
                time.sleep(attempt * 2)

    raise RuntimeError(f"Could not download {url}: {last_error}")


def as_int(value: str | None, fallback: int = 0) -> int:
    try:
        return int(value or fallback)
    except ValueError:
        return fallback


def as_bool(value: str | None) -> bool:
    return (value or "").strip().lower() == "true"


def compact_number(value: float) -> int | float:
    rounded = round(value, 3)
    return int(rounded) if rounded.is_integer() else rounded


def load_override_actions(output_root: Path, explicit: Path | None) -> dict[int, dict[str, Any]]:
    candidates = []
    if explicit is not None:
        candidates.append(explicit)
    candidates.extend(
        (
            output_root / "curated-overrides.json",
            output_root / "pve-actions.json",
        )
    )

    for path in candidates:
        if not path.is_file():
            continue
        document = json.loads(path.read_text(encoding="utf-8"))
        return {
            int(action["actionId"]): action
            for action in document.get("actions", [])
            if as_int(str(action.get("actionId", 0))) > 0
        }

    return {}


def available_jobs(category: dict[str, str]) -> list[str]:
    jobs = [job for job in JOBS if as_bool(category.get(job))]

    for class_name, mapped_jobs in BASE_CLASS_JOBS.items():
        if not as_bool(category.get(class_name)):
            continue
        for job in mapped_jobs:
            if job not in jobs:
                jobs.append(job)

    return [job for job in JOBS if job in jobs]


def timeline_lock(kind: str, recast_seconds: float) -> float:
    if kind in {"ability", "limitBreak"}:
        return 0.0
    if 0 < recast_seconds <= 5:
        return recast_seconds
    return 2.5


def build_entry(
    row: dict[str, str],
    category_names: dict[int, str],
    job_categories: dict[int, dict[str, str]],
    overrides: dict[int, dict[str, Any]],
) -> dict[str, Any] | None:
    action_id = as_int(row.get("#"))
    name = (row.get("Name") or "").strip()
    action_category = category_names.get(as_int(row.get("ActionCategory")), "")
    kind = ACTION_KINDS.get(action_category)
    is_limit_break = action_category == "Limit Break"
    level = as_int(row.get("ClassJobLevel"))

    if (
        action_id <= 0
        or not name
        or kind is None
        or not as_bool(row.get("IsPlayerAction"))
        or as_bool(row.get("IsPvP"))
        or (level < 1 and not is_limit_break)
    ):
        return None

    category = job_categories.get(as_int(row.get("ClassJobCategory")))
    if category is None:
        return None

    jobs = available_jobs(category)
    if not jobs:
        return None

    cast_seconds = as_int(row.get("Cast100ms")) / 10.0
    recast_seconds = as_int(row.get("Recast100ms")) / 10.0
    combo_from = as_int(row.get("ActionCombo"))
    maximum_charges = max(1, as_int(row.get("MaxCharges"), 1))
    primary_cost_type = as_int(row.get("PrimaryCostType"))
    primary_cost_value = as_int(row.get("PrimaryCostValue"))

    entry: dict[str, Any] = {
        "actionId": action_id,
        "name": name,
        "job": jobs[0],
        "_availableJobs": jobs,
        "kind": kind,
        "minimumLevel": max(1, level),
        "castSeconds": compact_number(cast_seconds),
        "recastSeconds": compact_number(recast_seconds),
        "timelineLockSeconds": compact_number(timeline_lock(kind, recast_seconds)),
        "maximumCharges": maximum_charges,
        "source": (
            f"FFXIV {GAME_VERSION} game data Action sheet; "
            f"{SOURCE_REPOSITORY}@{SOURCE_COMMIT}"
        ),
    }

    if combo_from > 0:
        entry["comboFromActionId"] = combo_from

    # PrimaryCostType 3 is the game's MP cost encoding for current combat actions.
    if primary_cost_type == 3 and primary_cost_value > 0:
        entry["mpCost"] = primary_cost_value

    curated = overrides.get(action_id)
    if curated:
        for key in CURATED_KEYS:
            value = curated.get(key)
            if value is not None and value != []:
                entry[key] = value

        curated_source = str(curated.get("source") or "").strip()
        if curated_source:
            entry["source"] = (
                f"{curated_source}; base metadata refreshed from "
                f"{SOURCE_REPOSITORY}@{SOURCE_COMMIT}"
            )

    return entry


def write_catalogue(
    path: Path,
    actions: Iterable[dict[str, Any]],
) -> None:
    document = {
        "schemaVersion": 1,
        "gameVersion": GAME_VERSION,
        "generatedFrom": (
            f"FFXIV game sheets exported by {SOURCE_REPOSITORY} at "
            f"{SOURCE_COMMIT}. Filter: non-PvP player actions in spell, "
            "weaponskill, ability, or limit-break categories."
        ),
        "actions": list(actions),
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(document, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def main() -> int:
    args = parse_args()
    output_root = args.output_root.resolve()
    jobs_directory = output_root / "Jobs"
    overrides = load_override_actions(output_root, args.overrides)

    action_rows = download_csv("Action")
    category_rows = download_csv("ActionCategory")
    class_job_category_rows = download_csv("ClassJobCategory")

    category_names = {
        as_int(row.get("#")): (row.get("Name") or "").strip()
        for row in category_rows
    }
    job_categories = {
        as_int(row.get("#")): row
        for row in class_job_category_rows
    }

    entries: list[dict[str, Any]] = []
    for row in action_rows:
        entry = build_entry(row, category_names, job_categories, overrides)
        if entry is not None:
            entries.append(entry)

    entries.sort(key=lambda action: (action["actionId"], action["name"]))
    duplicate_ids = [
        action_id
        for action_id, count in __import__("collections").Counter(
            action["actionId"] for action in entries
        ).items()
        if count > 1
    ]
    if duplicate_ids:
        raise RuntimeError(f"Generated duplicate action IDs: {duplicate_ids[:10]}")

    if jobs_directory.exists():
        shutil.rmtree(jobs_directory)
    jobs_directory.mkdir(parents=True)

    by_job: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for entry in entries:
        for job in entry["_availableJobs"]:
            job_entry = {
                key: value
                for key, value in entry.items()
                if key != "_availableJobs"
            }
            job_entry["job"] = job
            by_job[job].append(job_entry)

    for job in JOBS:
        write_catalogue(
            jobs_directory / f"{job}.json",
            by_job.get(job, []),
        )

    aggregate_entries = [
        {key: value for key, value in entry.items() if key != "_availableJobs"}
        for entry in entries
    ]
    write_catalogue(
        output_root / "pve-actions.json",
        aggregate_entries,
    )

    missing_jobs = [job for job in JOBS if not by_job.get(job)]
    if missing_jobs:
        raise RuntimeError(f"No actions generated for: {', '.join(missing_jobs)}")

    print(
        f"Generated {len(entries)} unique PvE actions across {len(JOBS)} job "
        f"catalogues from FFXIV {GAME_VERSION}."
    )
    for job in JOBS:
        print(f"  {job}: {len(by_job[job])}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Action catalogue generation failed: {error}", file=sys.stderr)
        raise
