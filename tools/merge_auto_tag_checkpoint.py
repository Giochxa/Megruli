"""Merge a completed prefix of an auto-tagging run with the shipped labels."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


def lesson_number(item: dict, field: str) -> int:
    match = re.search(r"Megruli(\d+)", item[field])
    if not match:
        raise ValueError(f"Cannot determine lesson number from {item[field]!r}")
    return int(match.group(1))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--checkpoint-labels", type=Path, required=True)
    parser.add_argument("--checkpoint-manifest", type=Path, required=True)
    parser.add_argument("--baseline-labels", type=Path)
    parser.add_argument("--baseline-manifest", type=Path)
    parser.add_argument("--through-lesson", type=int, required=True)
    args = parser.parse_args()

    clips_dir = args.root / "wwwroot" / "audio" / "clips"
    shipped_labels_path = clips_dir / "auto-labels.json"
    shipped_manifest_path = clips_dir / "auto-clips-manifest.json"
    baseline_labels_path = args.baseline_labels or shipped_labels_path
    baseline_manifest_path = args.baseline_manifest or shipped_manifest_path
    shipped_labels = json.loads(baseline_labels_path.read_text(encoding="utf-8-sig"))
    shipped_manifest = json.loads(baseline_manifest_path.read_text(encoding="utf-8-sig"))
    checkpoint_labels = json.loads(args.checkpoint_labels.read_text(encoding="utf-8-sig"))
    checkpoint_manifest = json.loads(args.checkpoint_manifest.read_text(encoding="utf-8-sig"))

    combined = [
        item for item in checkpoint_labels
        if lesson_number(item, "ClipId") <= args.through_lesson
    ] + shipped_labels
    best_by_clip: dict[str, dict] = {}
    for item in combined:
        clip_id = item["ClipId"]
        existing = best_by_clip.get(clip_id)
        if existing is None or item.get("Confidence", 0) > existing.get("Confidence", 0):
            best_by_clip[clip_id] = item

    best_by_word: dict[str, dict] = {}
    for item in best_by_clip.values():
        word_id = item.get("LinkedWordId")
        if not word_id:
            continue
        existing = best_by_word.get(word_id)
        if existing is None or item.get("Confidence", 0) > existing.get("Confidence", 0):
            best_by_word[word_id] = item
    merged_labels = list(best_by_word.values())

    clip_lookup = {
        item["Id"]: item
        for item in checkpoint_manifest + shipped_manifest
    }
    merged_manifest = [clip_lookup[item["ClipId"]] for item in merged_labels]

    shipped_labels_path.write_text(
        json.dumps(merged_labels, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    shipped_manifest_path.write_text(
        json.dumps(merged_manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    source_count = sum(
        item.get("LinkedWordId", "").startswith("source-") for item in merged_labels
    )
    print(
        f"Merged {len(merged_labels)} unique word labels; "
        f"{source_count} points to the new source vocabulary."
    )


if __name__ == "__main__":
    main()
