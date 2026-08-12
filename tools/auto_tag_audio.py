"""Transcribe lesson recordings and create conservative Megruli/Georgian clip tags.

The recognizer runs over each full MP3 once (not thousands of tiny WAVs), then aligns
word timestamps with clips-manifest.json. Exact/high-similarity course-text matches are
linked automatically; uncertain clips remain Unknown for human review.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
import unicodedata
from collections import Counter
from difflib import SequenceMatcher
from pathlib import Path


GEORGIAN_TO_LATIN = str.maketrans({
    "ა": "a", "ბ": "b", "გ": "g", "დ": "d", "ე": "e", "ვ": "v", "ზ": "z",
    "თ": "t", "ი": "i", "კ": "k", "ლ": "l", "მ": "m", "ნ": "n", "ო": "o",
    "პ": "p", "ჟ": "zh", "რ": "r", "ს": "s", "ტ": "t", "უ": "u", "ფ": "p",
    "ქ": "k", "ღ": "g", "ყ": "q", "შ": "sh", "ჩ": "ch", "ც": "ts", "ძ": "dz",
    "წ": "ts", "ჭ": "ch", "ხ": "kh", "ჯ": "j", "ჰ": "h", "ჷ": "e", "ჸ": "q",
})
LATIN_RE = re.compile(r"[^a-z]+")


def normalize(value: str) -> str:
    value = value.lower().translate(GEORGIAN_TO_LATIN)
    value = "".join(character for character in unicodedata.normalize("NFKD", value)
                    if not unicodedata.combining(character))
    # Whisper often chooses a nearby Latin orthography for low-resource Kartvelian
    # speech. Normalizing both it and the course text phonetically makes matching stable.
    value = value.replace("w", "v").replace("y", "i")
    return " ".join(LATIN_RE.sub(" ", value).split())


def variants(value: str) -> list[str]:
    # Parentheses contain notes/alternatives such as an isolated final sound. Treating
    # those fragments as full words can create a confident but incorrect audio link.
    base = value.split("(", 1)[0]
    result = {normalize(base)}
    result.update(normalize(part) for part in base.split("/"))
    return [item for item in result if item]


def load_course(root: Path):
    rows = []
    for filename in ("vocabulary.json", "phrases.json", "proverbs.json"):
        data = json.loads((root / "wwwroot" / "data" / filename).read_text(encoding="utf-8"))
        for item in data:
            for language in ("megruli", "georgian"):
                for text in variants(item[language]):
                    rows.append({"id": item["id"], "language": language, "text": text,
                                 "megruli": item["megruli"], "georgian": item["georgian"]})

    # Grammar content is authored directly in units.json rather than the vocabulary
    # files. Pull its explicit translation pairs into the matcher as well.
    units = json.loads((root / "wwwroot" / "data" / "units.json").read_text(encoding="utf-8"))
    for lesson in (lesson for unit in units for lesson in unit.get("lessons", [])):
        for exercise in lesson.get("fixedExercises") or []:
            pairs = []
            if exercise.get("type") == "matchPairs":
                pairs = [(pair["megruli"], pair["georgian"]) for pair in exercise.get("pairs", [])]
            elif exercise.get("type") == "typeAnswer" and exercise.get("promptIsGeorgian"):
                pairs = [(answer, exercise["prompt"]) for answer in exercise.get("acceptedAnswers", [])]
            elif exercise.get("type") == "multipleChoice" and not exercise.get("promptIsGeorgian"):
                options = exercise.get("options", [])
                index = exercise.get("correctIndex", -1)
                if 0 <= index < len(options):
                    pairs = [(exercise["prompt"], options[index])]
            for megruli, georgian in pairs:
                for language, value in (("megruli", megruli), ("georgian", georgian)):
                    for text in variants(value):
                        rows.append({"id": None, "language": language, "text": text,
                                     "megruli": megruli, "georgian": georgian})
    return rows


def ngrams(text: str, sizes=(2, 3, 4)):
    compact = f" {text.replace(' ', '_')} "
    for size in sizes:
        for index in range(len(compact) - size + 1):
            yield compact[index:index + size]


class LanguageModel:
    def __init__(self, course_rows):
        self.counts = {"megruli": Counter(), "georgian": Counter()}
        for row in course_rows:
            self.counts[row["language"]].update(ngrams(row["text"]))
        self.vocabulary = set(self.counts["megruli"]) | set(self.counts["georgian"])
        self.totals = {key: sum(value.values()) for key, value in self.counts.items()}

    def margin(self, text: str) -> float:
        scores = {}
        vocab_size = len(self.vocabulary)
        for language in ("megruli", "georgian"):
            denominator = self.totals[language] + vocab_size
            scores[language] = sum(
                math.log((self.counts[language][gram] + 1) / denominator)
                for gram in ngrams(text)
            )
        length = max(8, sum(1 for _ in ngrams(text)))
        return (scores["megruli"] - scores["georgian"]) / length


def similarity(source: str, target: str) -> float:
    if not source or not target:
        return 0.0
    ratio = max(SequenceMatcher(None, source, target).ratio(),
                SequenceMatcher(None, source.replace(" ", ""), target.replace(" ", "")).ratio())
    source_tokens, target_tokens = set(source.split()), set(target.split())
    overlap = len(source_tokens & target_tokens) / max(1, len(target_tokens))
    containment = 1.0 if target in source or source in target else 0.0
    return 0.75 * ratio + 0.15 * overlap + 0.1 * containment


def best_matches(text: str, rows):
    # Avoid comparing obviously incompatible lengths; it improves both speed and precision.
    compatible = [row for row in rows if 0.3 <= len(row["text"]) / max(1, len(text)) <= 2.8]
    scored = sorted(((similarity(text, row["text"]), row) for row in compatible),
                    key=lambda item: item[0], reverse=True)
    return scored[:3]


def transcribe(model, source: Path):
    segments, _ = model.transcribe(str(source), language="ka", beam_size=1,
                                   word_timestamps=True, vad_filter=True,
                                   condition_on_previous_text=False)
    result = []
    for segment in segments:
        cleaned = normalize(segment.text)
        if cleaned:
            result.append({"start": float(segment.start), "end": float(segment.end),
                           "text": cleaned, "logprob": float(segment.avg_logprob)})
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--model", default="base")
    parser.add_argument("--model-cache", type=Path)
    parser.add_argument("--lessons", help="Comma-separated lesson numbers; default is all")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--manifest-output", type=Path)
    parser.add_argument("--cpu-threads", type=int, default=0,
                        help="CTranslate2 worker threads; 0 lets it choose automatically")
    args = parser.parse_args()

    from faster_whisper import WhisperModel

    root = args.root.resolve()
    output = args.output or root / "wwwroot" / "audio" / "clips" / "auto-labels.json"
    manifest_output = args.manifest_output or root / "wwwroot" / "audio" / "clips" / "auto-clips-manifest.json"
    selected = {int(value) for value in args.lessons.split(",")} if args.lessons else None
    manifest = json.loads((root / "wwwroot" / "audio" / "clips" / "clips-manifest.json").read_text(encoding="utf-8"))
    course = load_course(root)
    by_language = {language: [row for row in course if row["language"] == language]
                   for language in ("megruli", "georgian")}
    language_model = LanguageModel(course)
    model = WhisperModel(args.model, device="cpu", compute_type="int8",
                         download_root=str(args.model_cache) if args.model_cache else None,
                         cpu_threads=args.cpu_threads)

    labels, auto_clips = [], []
    lesson_number = lambda value: int("".join(filter(str.isdigit, Path(value).stem)))
    source_files = sorted({item["SourceFile"] for item in manifest}, key=lesson_number)
    for source_name in source_files:
        number = lesson_number(source_name)
        if selected is not None and number not in selected:
            continue
        print(f"Transcribing {source_name}...", flush=True)
        source_path = root / "wwwroot" / "audio" / "lessons" / source_name
        segments = transcribe(model, source_path)
        accepted_index = 0
        for segment in segments:
            transcript = segment["text"]
            if len(transcript.replace(" ", "")) < 2 or segment["logprob"] < -1.15:
                continue

            matches = {language: best_matches(transcript, rows)
                       for language, rows in by_language.items()}
            best_m = matches["megruli"][0] if matches["megruli"] else (0.0, None)
            best_g = matches["georgian"][0] if matches["georgian"] else (0.0, None)
            lexical_margin = language_model.margin(transcript)
            match_margin = best_m[0] - best_g[0]

            # Only materialize segments that are both recognizably Megruli and match a
            # concrete course entry. Georgian narration and mixed/uncertain speech never
            # becomes a learner-facing pronunciation clip.
            second_m = matches["megruli"][1][0] if len(matches["megruli"]) > 1 else 0
            if not (best_m[1] and best_m[0] >= 0.72 and match_margin >= 0.055 and
                    (best_m[0] - second_m >= 0.025 or best_m[0] >= 0.9)):
                continue

            accepted_index += 1
            clip_id = f"auto-{Path(source_name).stem}-{accepted_index:03d}"
            confidence = min(0.99, 0.58 + match_margin + max(0, lexical_margin * 2))
            label = {"ClipId": clip_id, "Language": "Megruli",
                     "Transcript": transcript, "Confidence": round(confidence, 3),
                     "AutoAssigned": True, "Megruli": best_m[1]["megruli"],
                     "Georgian": best_m[1]["georgian"], "LinkedWordId": best_m[1]["id"]}
            labels.append(label)
            auto_clips.append({"Id": clip_id, "SourceFile": source_name,
                               "StartMs": round(segment["start"] * 1000),
                               "EndMs": round(segment["end"] * 1000),
                               "ClipFile": ""})
        print(f"  {accepted_index} high-confidence Megruli clips; {len(labels)} accumulated", flush=True)

        # Checkpoint after every recording so a long run is safely resumable/inspectable.
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(labels, ensure_ascii=False, indent=2), encoding="utf-8")
        manifest_output.write_text(json.dumps(auto_clips, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Done: Megruli={len(labels)}, output={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
