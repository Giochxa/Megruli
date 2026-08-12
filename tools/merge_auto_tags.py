"""Merge parallel auto-tagging checkpoints into the files loaded by the app."""

import json
import sys
from pathlib import Path


root = Path(__file__).resolve().parents[1]
clips_dir = root / "wwwroot" / "audio" / "clips"
sys.path.insert(0, str(root / "tools"))
from auto_tag_audio import normalize, similarity


labels = {}
for source in sorted(clips_dir.glob("auto-labels-part*.json")):
    for item in json.loads(source.read_text(encoding="utf-8")):
        canonical = normalize(item.get("Megruli", "").split("(", 1)[0].split("/", 1)[0])
        # Guard against matches to tiny parenthetical notes or unrelated long entries.
        if item.get("Confidence", 0) >= 0.75 and similarity(item.get("Transcript", ""), canonical) >= 0.52:
            labels[item["ClipId"]] = item

# Keep the strongest pronunciation for each course word. This avoids several near-
# identical buttons competing for the same word and removes lower-quality repetitions.
best_by_word = {}
for item in labels.values():
    word_id = item.get("LinkedWordId")
    existing = best_by_word.get(word_id)
    if word_id and (existing is None or item.get("Confidence", 0) > existing.get("Confidence", 0)):
        best_by_word[word_id] = item
labels = {item["ClipId"]: item for item in best_by_word.values()}

clips = {}
for source in sorted(clips_dir.glob("auto-clips-manifest-part*.json")):
    for item in json.loads(source.read_text(encoding="utf-8")):
        if item["Id"] in labels:
            clips[item["Id"]] = item

(clips_dir / "auto-labels.json").write_text(
    json.dumps(list(labels.values()), ensure_ascii=False, indent=2), encoding="utf-8")
(clips_dir / "auto-clips-manifest.json").write_text(
    json.dumps(list(clips.values()), ensure_ascii=False, indent=2), encoding="utf-8")
label_count, clip_count = len(labels), len(clips)
print(f"Merged {label_count} labels and {clip_count} clip records")
