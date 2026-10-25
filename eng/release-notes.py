#!/usr/bin/env python3
"""Extracts the notes of one version from CHANGELOG.md (Keep a Changelog format).

    python -I eng/release-notes.py 1.2.0 [--changelog CHANGELOG.md] [--require-date] > notes.md

Prints the body of the "## [<version>]" section and fails when it is missing or empty, so nothing ships without notes.
--require-date additionally insists on a dated heading ("## [1.2.0] - 2026-10-15"), which a real release needs.
"""

import argparse
import os
import re
import sys
from pathlib import Path

MAX_LENGTH = 20_000
ROOT = os.path.realpath(Path(__file__).resolve().parent.parent)


def repository_file(name: str) -> Path:
    """A file named on the command line; only files of this repository are read."""
    path = os.path.realpath(name)
    if not path.startswith(ROOT + os.sep):
        raise SystemExit(f"::error::{name} is outside the repository")
    return Path(path)


def find_section(changelog: str, version: str) -> tuple[str, str] | None:
    heading = re.compile(rf"^## \[{re.escape(version)}\][^\n]*$", re.MULTILINE)
    match = heading.search(changelog)
    if match is None:
        return None
    following = re.compile(r"^## \[", re.MULTILINE).search(changelog, match.end())
    body = changelog[match.end():following.start() if following else len(changelog)]
    # Link reference definitions ([1.2.0]: https://...) belong to the whole file.
    body = re.sub(r"^\[[^\]]+\]:\s*\S+\s*$", "", body, flags=re.MULTILINE)
    # A horizontal rule separating versions is not part of the notes.
    body = re.sub(r"\n-{3,}\s*$", "", body.strip())
    return match.group(0), body.strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("version")
    parser.add_argument("--changelog", default="CHANGELOG.md")
    parser.add_argument("--require-date", action="store_true")
    args = parser.parse_args()

    section = find_section(repository_file(args.changelog).read_text(encoding="utf-8"), args.version)
    if section is None or not section[1]:
        print(f"::error::CHANGELOG has no notes for {args.version}", file=sys.stderr)
        return 1
    heading, notes = section
    if args.require_date and not re.search(r"- \d{4}-\d{2}-\d{2}\s*$", heading):
        print(f"::error::The CHANGELOG heading '{heading}' has no release date", file=sys.stderr)
        return 1
    if len(notes) > MAX_LENGTH:
        print(f"::error::Release notes for {args.version} are {len(notes)} characters (limit {MAX_LENGTH})", file=sys.stderr)
        return 1
    sys.stdout.reconfigure(encoding="utf-8")
    print(notes)
    return 0


if __name__ == "__main__":
    sys.exit(main())
