"""Conservatively mark raw silence-sliced clips that contain Georgian speech.

The recognizer runs once per full lesson recording. Its segments are compared with the
course's Georgian and Megrelian text models, then aligned to clips-manifest.json. Only
clips dominated by strong Georgian evidence are emitted; everything uncertain remains
Unknown for human review.
"""

from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path

from auto_tag_audio import LanguageModel, best_matches, load_course, transcribe


def lesson_number(value: str) -> int:
    return int("".join(filter(str.isdigit, Path(value).stem)))


def classify_segment(segment: dict, by_language: dict, language_model: LanguageModel):
    transcript = segment["text"]
    if len(transcript.replace(" ", "")) < 3 or segment["logprob"] < -1.05:
        return None

    matches = {
        language: best_matches(transcript, rows)
        for language, rows in by_language.items()
    }
    best_m = matches["megruli"][0] if matches["megruli"] else (0.0, None)
    best_g = matches["georgian"][0] if matches["georgian"] else (0.0, None)
    lexical_margin = language_model.margin(transcript)
    georgian_margin = best_g[0] - best_m[0]
    if best_g[1] and best_g[0] >= 0.72 and georgian_margin >= 0.075 and lexical_margin <= 0:
        confidence = min(0.99, 0.62 + georgian_margin + max(0, -lexical_margin * 2))
        return "Georgian", confidence

    second_m = matches["megruli"][1][0] if len(matches["megruli"]) > 1 else 0
    megruli_margin = best_m[0] - best_g[0]
    if (best_m[1] and best_m[0] >= 0.72 and megruli_margin >= 0.055 and
            (best_m[0] - second_m >= 0.025 or best_m[0] >= 0.9)):
        return "Megruli", min(0.99, 0.58 + megruli_margin + max(0, lexical_margin * 2))
    return None


def overlap_ms(clip: dict, segment: dict) -> float:
    start = max(clip["StartMs"], segment["start"] * 1000)
    end = min(clip["EndMs"], segment["end"] * 1000)
    return max(0.0, end - start)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--model", default="base")
    parser.add_argument("--model-cache", type=Path)
    parser.add_argument("--lessons", help="Comma-separated lesson numbers; default is all")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--transcript-cache", type=Path)
    parser.add_argument("--cpu-threads", type=int, default=0)
    args = parser.parse_args()

    from faster_whisper import WhisperModel

    root = args.root.resolve()
    output = args.output or root / "wwwroot" / "audio" / "clips" / "auto-language-labels.json"
    selected = {int(value) for value in args.lessons.split(",")} if args.lessons else None
    raw_clips = json.loads(
        (root / "wwwroot" / "audio" / "clips" / "clips-manifest.json").read_text(encoding="utf-8")
    )
    clips_by_source = defaultdict(list)
    for clip in raw_clips:
        clips_by_source[clip["SourceFile"]].append(clip)

    course = load_course(root)
    by_language = {
        language: [row for row in course if row["language"] == language]
        for language in ("megruli", "georgian")
    }
    language_model = LanguageModel(course)
    model = WhisperModel(
        args.model, device="cpu", compute_type="int8",
        download_root=str(args.model_cache) if args.model_cache else None,
        cpu_threads=args.cpu_threads,
    )

    labels = []
    source_files = sorted(clips_by_source, key=lesson_number)
    for source_name in source_files:
        number = lesson_number(source_name)
        if selected is not None and number not in selected:
            continue
        print(f"Classifying {source_name}...", flush=True)
        source_path = root / "wwwroot" / "audio" / "lessons" / source_name
        cache_path = args.transcript_cache / f"{Path(source_name).stem}.json" if args.transcript_cache else None
        if cache_path and cache_path.exists():
            segments = json.loads(cache_path.read_text(encoding="utf-8"))
        else:
            segments = transcribe(model, source_path)
        classified = []
        for segment in segments:
            result = classify_segment(segment, by_language, language_model)
            if result:
                language, confidence = result
                classified.append({**segment, "language": language, "confidence": confidence})

        if args.transcript_cache:
            args.transcript_cache.mkdir(parents=True, exist_ok=True)
            cache_path = args.transcript_cache / f"{Path(source_name).stem}.json"
            cache_path.write_text(json.dumps(segments, ensure_ascii=False, indent=2), encoding="utf-8")

        accepted = 0
        for clip in clips_by_source[source_name]:
            clip_duration = max(1, clip["EndMs"] - clip["StartMs"])
            evidence = {"Georgian": 0.0, "Megruli": 0.0}
            georgian_segments = []
            weighted_confidence = 0.0
            for segment in classified:
                overlap = overlap_ms(clip, segment)
                if overlap <= 0:
                    continue
                evidence[segment["language"]] += overlap
                if segment["language"] == "Georgian":
                    georgian_segments.append(segment["text"])
                    weighted_confidence += overlap * segment["confidence"]

            georgian_overlap = evidence["Georgian"]
            megruli_overlap = evidence["Megruli"]
            if (georgian_overlap / clip_duration < 0.10 or
                    georgian_overlap < max(450, megruli_overlap * 3)):
                continue
            accepted += 1
            labels.append({
                "ClipId": clip["Id"],
                "Language": "Georgian",
                "Transcript": " ".join(dict.fromkeys(georgian_segments)),
                "Confidence": round(weighted_confidence / georgian_overlap, 3),
                "AutoAssigned": True,
            })

        print(f"  {accepted} raw clips marked Georgian; {len(labels)} accumulated", flush=True)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(labels, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print(f"Done: Georgian={len(labels)}, output={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
