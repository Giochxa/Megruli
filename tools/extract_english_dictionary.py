"""Build English translations for course words from the TSU five-language dictionary.

The PDF embeds Georgian glyphs behind a legacy Latin keyboard encoding.  This
script converts the app's Unicode Georgian spelling to that encoding, matches
the Georgian headword column, and writes only unambiguous matches.
"""

from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path

import pdfplumber


ROOT = Path(__file__).resolve().parents[1]
PDF = ROOT / "tmp" / "pdfs" / "kartvelian-dictionary.pdf"
OUTPUT = ROOT / "wwwroot" / "data" / "english-translations.json"

CARDINALS = {
    1: "one", 2: "two", 3: "three", 4: "four", 5: "five", 6: "six", 7: "seven",
    8: "eight", 9: "nine", 10: "ten", 11: "eleven", 12: "twelve", 13: "thirteen",
    14: "fourteen", 15: "fifteen", 16: "sixteen", 17: "seventeen", 18: "eighteen",
    19: "nineteen", 20: "twenty", 21: "twenty-one", 22: "thirty", 23: "forty",
    24: "fifty", 25: "sixty", 26: "seventy", 27: "eighty", 28: "ninety",
    29: "one hundred", 30: "one hundred and one", 31: "one hundred and ten",
    32: "two hundred", 33: "one thousand", 34: "one thousand nine hundred and ninety-nine",
    35: "two thousand", 36: "ten thousand",
}

ORDINALS = {
    1: "first", 2: "second", 3: "third", 4: "fourth", 5: "fifth", 6: "sixth",
    7: "seventh", 8: "eighth", 9: "ninth", 10: "tenth", 11: "eleventh",
    12: "twelfth", 13: "twentieth", 14: "twenty-first", 15: "thirtieth",
    16: "one hundredth", 17: "one hundred and first", 18: "one hundred and second",
    19: "one hundred and tenth", 20: "two hundredth", 21: "five hundredth",
    22: "one thousandth",
}

CURATED = {
    **{f"numbers-cardinal-{number}": meaning for number, meaning in CARDINALS.items()},
    **{f"numbers-ordinal-{number}": meaning for number, meaning in ORDINALS.items()},
    "lexicon-325": "I", "lexicon-529": "you", "lexicon-401": "he/she/it",
    "lexicon-701": "we", "lexicon-252": "you all", "lexicon-421": "they",
    "lexicon-212": "I am", "lexicon-513": "you are", "lexicon-508": "he/she/it is",
    "lexicon-509": "you all are", "lexicon-511": "they are",
    "grammar-plural-1": "men", "grammar-plural-2": "women", "grammar-plural-3": "sisters",
    "grammar-plural-4": "trees", "grammar-plural-5": "days", "grammar-plural-6": "forests",
    "grammar-plural-7": "mothers", "grammar-plural-8": "fathers",
}

LEGACY = str.maketrans({
    "ა": "a", "ბ": "b", "გ": "g", "დ": "d", "ე": "e", "ვ": "v",
    "ზ": "z", "თ": "T", "ი": "i", "კ": "k", "ლ": "l", "მ": "m",
    "ნ": "n", "ო": "o", "პ": "p", "ჟ": "J", "რ": "r", "ს": "s",
    "ტ": "t", "უ": "u", "ფ": "f", "ქ": "q", "ღ": "R", "ყ": "y",
    "შ": "S", "ჩ": "C", "ც": "c", "ძ": "Z", "წ": "w", "ჭ": "W",
    "ხ": "x", "ჯ": "j", "ჰ": "h", "ჲ": "I", "ჷ": "F", "ჸ": "Y",
})


def normalize_legacy(value: str) -> str:
    value = re.sub(r"\([^)]*\)", "", value)
    return re.sub(r"[^A-Za-z]", "", value)


def app_keys(value: str) -> set[str]:
    keys: set[str] = set()
    for part in re.split(r"[/,;]|\s+[–—-]\s+", value):
        encoded = normalize_legacy(part.translate(LEGACY))
        if encoded:
            keys.add(encoded)
    encoded_all = normalize_legacy(value.translate(LEGACY))
    if encoded_all:
        keys.add(encoded_all)
    return keys


def extract_entries() -> dict[str, list[str]]:
    meanings: dict[str, list[str]] = defaultdict(list)
    with pdfplumber.open(PDF) as pdf:
        for page in pdf.pages[12:]:
            words = page.extract_words(x_tolerance=1, y_tolerance=2)
            lines: dict[float, list[dict]] = defaultdict(list)
            for word in words:
                lines[round(float(word["top"]), 1)].append(word)

            current_key: str | None = None
            current_english: list[str] = []

            def finish() -> None:
                if not current_key or not current_english:
                    return
                meaning = re.sub(r"\s+", " ", " ".join(current_english)).strip()
                meaning = re.sub(r"(?<=\w)- (?=\w)", "", meaning)
                meaning = (meaning.replace("â€™", "'").replace("â€œ", '"')
                           .replace("â€", '"').replace("â€“", "-")
                           .replace("â€”", "-"))
                if meaning and meaning not in meanings[current_key]:
                    meanings[current_key].append(meaning)

            for _, line in sorted(lines.items()):
                line.sort(key=lambda word: float(word["x0"]))
                first = " ".join(w["text"] for w in line if float(w["x0"]) < 140).strip()
                english = " ".join(w["text"] for w in line if float(w["x0"]) >= 420).strip()
                is_headword = bool(first) and float(line[0]["x0"]) < 80
                clean_first = normalize_legacy(first)
                if is_headword and clean_first not in {"qarTuli", "Georgian"} and not first.startswith("("):
                    finish()
                    current_key = clean_first
                    current_english = [english] if english else []
                elif current_key and english:
                    current_english.append(english)
            finish()
    return meanings


def main() -> int:
    if not PDF.exists():
        print(f"Dictionary PDF not found: {PDF}", file=sys.stderr)
        return 1

    dictionary = extract_entries()
    translated: dict[str, str] = {}
    ambiguous = 0
    totals = 0
    for filename in ("vocabulary.json", "phrases.json", "proverbs.json"):
        rows = json.loads((ROOT / "wwwroot" / "data" / filename).read_text(encoding="utf-8-sig"))
        for row in rows:
            totals += 1
            candidates: list[str] = []
            for key in app_keys(row["georgian"]):
                candidates.extend(dictionary.get(key, []))
            unique = list(dict.fromkeys(candidates))
            if len(unique) == 1:
                translated[row["id"]] = unique[0]
            elif len(unique) > 1:
                ambiguous += 1

    translated.update(CURATED)

    OUTPUT.write_text(
        json.dumps(translated, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Matched {len(translated)}/{totals}; ambiguous {ambiguous}; output {OUTPUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
