"""Validate and merge Georgian raw-clip classification checkpoints."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("inputs", nargs="+", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    clips_dir = args.root / "wwwroot" / "audio" / "clips"
    output = args.output or clips_dir / "auto-language-labels.json"
    raw_clips = json.loads((clips_dir / "clips-manifest.json").read_text(encoding="utf-8"))
    valid_ids = {clip["Id"] for clip in raw_clips}

    merged = {}
    for source in args.inputs:
        for label in json.loads(source.read_text(encoding="utf-8-sig")):
            clip_id = label["ClipId"]
            if clip_id not in valid_ids:
                raise ValueError(f"Unknown raw clip id: {clip_id}")
            if label.get("Language") != "Georgian" or label.get("LinkedWordId"):
                raise ValueError(f"Invalid Georgian language label: {clip_id}")
            current = merged.get(clip_id)
            if current is None or label.get("Confidence", 0) > current.get("Confidence", 0):
                merged[clip_id] = label

    labels = sorted(merged.values(), key=lambda label: label["ClipId"])
    output.write_text(json.dumps(labels, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Merged {len(labels)} Georgian raw-clip labels into {output}")


if __name__ == "__main__":
    main()
