#!/usr/bin/env python3
"""Maintains the translation files in src/HashTool.App/Lang.

Run with `python -I eng/loc.py <command>` from the repository root:

  check                    Validate every Resources.*.resx against the neutral (English) file: same keys, same
                           placeholders, no empty values or stray indentation; Languages.json, the resx files and the
                           Culture entries in HashTool.App.csproj must agree. Exits with 1 when something is wrong.
  normalize                Rewrite every resx in canonical form (neutral key order, one line per header, trimmed
                           multi-line values). Values never get re-wrapped.
  set FILE.json [...]      Set values from JSON files shaped {"<culture>": {"Key": "value"}}. Use "neutral" for the
                           English source; new keys are appended to the neutral file in the order given.
  remove KEY [...]         Delete keys from every resx.
  untranslated CULTURE     List keys whose value is still identical to the English text.
  export CULTURE           Print a culture's strings as JSON ("neutral" for English).

Cultures are matched case-insensitively against the file names (vi-vn, zh-hans-cn, ...).
"""

from __future__ import annotations

import html
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LANG = ROOT / "src" / "HashTool.App" / "Lang"
NEUTRAL = LANG / "Resources.resx"
LANGUAGES_JSON = LANG / "Languages.json"
CSPROJ = ROOT / "src" / "HashTool.App" / "HashTool.App.csproj"

# Cultures that intentionally have no resx of their own and fall back to the neutral English file.
FALLBACK_TO_NEUTRAL = {"en-us"}

DATA_RE = re.compile(
    r'<data\s+name="(?P<name>[^"]+)"[^>]*?(?:/>|>\s*(?:<value\s*/>|<value>(?P<value>.*?)</value>)\s*'
    r'(?:<comment>(?P<comment>.*?)</comment>\s*)?</data>)',
    re.S,
)
PLACEHOLDER_RE = re.compile(r"\{\d+(?:,[^}:]+)?(?::[^}]*)?\}")

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
    <resheader name="resmimetype">
        <value>text/microsoft-resx</value>
    </resheader>
    <resheader name="version">
        <value>2.0</value>
    </resheader>
    <resheader name="reader">
        <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
    </resheader>
    <resheader name="writer">
        <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
    </resheader>
"""


class ResxFile:
    def __init__(self, path: Path):
        self.path = path
        self.entries: dict[str, str] = {}
        self.comments: dict[str, str] = {}
        self.duplicates: list[str] = []
        text = path.read_text(encoding="utf-8-sig")
        for match in DATA_RE.finditer(text):
            name = match.group("name")
            if name in self.entries:
                self.duplicates.append(name)
            self.entries[name] = html.unescape(match.group("value") or "")
            if match.group("comment"):
                self.comments[name] = html.unescape(match.group("comment"))

    @property
    def culture(self) -> str:
        parts = self.path.name.split(".")
        return "neutral" if len(parts) == 2 else parts[1]

    def save(self, order: list[str]) -> None:
        keys = [k for k in order if k in self.entries] + [k for k in self.entries if k not in order]
        out = [HEADER]
        for key in keys:
            out.append(f'    <data name="{escape(key)}" xml:space="preserve">\n')
            out.append(f"        <value>{escape(self.entries[key])}</value>\n")
            if key in self.comments:
                out.append(f"        <comment>{escape(self.comments[key])}</comment>\n")
            out.append("    </data>\n")
        out.append("</root>\n")
        self.path.write_text("".join(out), encoding="utf-8", newline="\n")


def escape(value: str) -> str:
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def normalize_value(value: str) -> str:
    """Removes indentation an XML formatter baked into multi-line values; single-line values are left alone."""
    if "\n" not in value and "\r" not in value:
        return value
    lines = value.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    return "\n".join([lines[0].rstrip()] + [line.strip() for line in lines[1:]]).strip()


def placeholders(value: str) -> list[str]:
    return sorted(PLACEHOLDER_RE.findall(value))


def load_all() -> tuple[ResxFile, list[ResxFile]]:
    neutral = ResxFile(NEUTRAL)
    cultures = [ResxFile(p) for p in sorted(LANG.glob("Resources.*.resx"))]
    return neutral, cultures


def find_culture(cultures: list[ResxFile], neutral: ResxFile, code: str) -> ResxFile:
    if code.lower() in ("neutral", "en", ""):
        return neutral
    for resx in cultures:
        if resx.culture == code.lower():
            return resx
    sys.exit(f"Unknown culture {code}")


def cmd_check() -> int:
    neutral, cultures = load_all()
    problems: list[str] = []
    keys = set(neutral.entries)
    for resx in [neutral, *cultures]:
        name = resx.path.name
        for dup in resx.duplicates:
            problems.append(f"{name}: duplicate key {dup}")
        for key in sorted(keys - set(resx.entries)):
            problems.append(f"{name}: missing {key}")
        for key in sorted(set(resx.entries) - keys):
            problems.append(f"{name}: unknown key {key}")
        for key, value in resx.entries.items():
            if not value.strip():
                problems.append(f"{name}: {key} is empty")
                continue
            if value != value.strip() or any(line != line.strip() for line in value.split("\n")):
                problems.append(f"{name}: {key} has leading/trailing whitespace or indentation")
            if key in neutral.entries and placeholders(value) != placeholders(neutral.entries[key]):
                problems.append(f"{name}: {key} placeholders {placeholders(value)} != {placeholders(neutral.entries[key])}")

    languages = json.loads(LANGUAGES_JSON.read_text(encoding="utf-8"))
    codes = {entry["Code"].lower(): entry["Code"] for entry in languages}
    files = {resx.culture for resx in cultures}
    for code in sorted(set(codes) - files - FALLBACK_TO_NEUTRAL):
        problems.append(f"Languages.json: {codes[code]} has no Resources.{code}.resx")
    for code in sorted(files - set(codes)):
        problems.append(f"Resources.{code}.resx is not listed in Languages.json")

    csproj = CSPROJ.read_text(encoding="utf-8")
    declared = {m.group(1).lower(): m.group(2) for m in
                re.finditer(r'<EmbeddedResource Update="Lang\\Resources\.([^"]+)\.resx" Culture="([^"]+)"', csproj)}
    for code in sorted(files):
        if code not in declared:
            problems.append(f"HashTool.App.csproj: no Culture entry for Resources.{code}.resx")
        elif code in codes and declared[code] != codes[code]:
            problems.append(f"HashTool.App.csproj: Culture {declared[code]} != Languages.json {codes[code]}")
    for code in sorted(set(declared) - files):
        problems.append(f"HashTool.App.csproj: Culture entry for missing Resources.{code}.resx")

    for problem in problems:
        print(problem)
    print(f"{len(cultures)} cultures, {len(keys)} keys, {len(problems)} problem(s)")
    return 1 if problems else 0


def cmd_normalize() -> int:
    neutral, cultures = load_all()
    order = list(neutral.entries)
    for resx in [neutral, *cultures]:
        resx.entries = {k: normalize_value(v) for k, v in resx.entries.items()}
        resx.save(order)
    print(f"Normalized {len(cultures) + 1} files")
    return 0


def cmd_set(files: list[str]) -> int:
    neutral, cultures = load_all()
    changed: set[Path] = set()
    for file in files:
        data = json.loads(Path(file).read_text(encoding="utf-8"))
        # Neutral first, so new keys get their position before cultures are written.
        for code in sorted(data, key=lambda c: c.lower() != "neutral"):
            resx = find_culture(cultures, neutral, code)
            for key, value in data[code].items():
                resx.entries[key] = normalize_value(value)
            changed.add(resx.path)
    order = list(neutral.entries)
    for resx in [neutral, *cultures]:
        if resx.path in changed:
            resx.save(order)
    print(f"Updated {len(changed)} files")
    return 0


def cmd_remove(keys: list[str]) -> int:
    neutral, cultures = load_all()
    order = list(neutral.entries)
    count = 0
    for resx in [neutral, *cultures]:
        removed = [k for k in keys if resx.entries.pop(k, None) is not None]
        if removed:
            count += len(removed)
            resx.save(order)
    print(f"Removed {count} entries")
    return 0


def cmd_untranslated(code: str) -> int:
    neutral, cultures = load_all()
    resx = find_culture(cultures, neutral, code)
    for key, value in resx.entries.items():
        if neutral.entries.get(key) == value:
            print(key)
    return 0


def cmd_export(code: str) -> int:
    neutral, cultures = load_all()
    resx = find_culture(cultures, neutral, code)
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(resx.entries, ensure_ascii=False, indent=2))
    return 0


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 2
    command, args = argv[0], argv[1:]
    if command == "check":
        return cmd_check()
    if command == "normalize":
        return cmd_normalize()
    if command == "set" and args:
        return cmd_set(args)
    if command == "remove" and args:
        return cmd_remove(args)
    if command == "untranslated" and len(args) == 1:
        return cmd_untranslated(args[0])
    if command == "export" and len(args) == 1:
        return cmd_export(args[0])
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
