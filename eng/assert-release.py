#!/usr/bin/env python3
"""Checks the files `vpk pack` produced for one channel before anything is published.

    python -I eng/assert-release.py <releases-dir> <channel> <version> <main-exe> [<bundle-id>]

Verifies that the feed (releases.<channel>.json) lists exactly the new full package with the right size and SHA-256,
that every file in assets.<channel>.json exists, and that the package carries the identity 1.x clients expect:
pack id HashTool, the channel, the main executable and, on macOS, the original bundle identifier.
"""

import hashlib
import json
import os
import plistlib
import re
import sys
import zipfile
from pathlib import Path

PACK_ID = "HashTool"
ROOT = os.path.realpath(Path(__file__).resolve().parent.parent)


def releases_folder(name: str) -> Path:
    """The folder named on the command line; it must be inside the repository (CI passes "Releases")."""
    path = os.path.realpath(name)
    if not path.startswith(ROOT + os.sep):
        fail(f"{name} is outside the repository")
    return Path(path)


def fail(message: str) -> None:
    print(f"::error::{message}")
    sys.exit(1)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def manifest_value(text: str, element: str) -> str | None:
    match = re.search(rf"<{element}>(.*?)</{element}>", text, re.S)
    return match.group(1).strip() if match else None


def main(releases: Path, channel: str, version: str, main_exe: str, bundle_id: str | None) -> None:
    feed_path = releases / f"releases.{channel}.json"
    if not feed_path.exists():
        fail(f"{feed_path.name} is missing")
    feed = json.loads(feed_path.read_text(encoding="utf-8-sig"))
    full = [a for a in feed.get("Assets", []) if a.get("Type") == "Full"]
    if len(full) != 1 or full[0].get("Version") != version or full[0].get("PackageId") != PACK_ID:
        fail(f"{feed_path.name} should list one {PACK_ID} {version} full package, found {full}")
    package = releases / full[0]["FileName"]
    if not package.exists():
        fail(f"{package.name} is missing")
    if package.stat().st_size != full[0]["Size"] or sha256(package) != full[0]["SHA256"].upper():
        fail(f"{package.name} does not match the size/SHA-256 in {feed_path.name}")

    assets_path = releases / f"assets.{channel}.json"
    if not assets_path.exists():
        fail(f"{assets_path.name} is missing")
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    for asset in assets:
        if not (releases / asset["RelativeFileName"]).exists():
            fail(f"{asset['RelativeFileName']} (listed in {assets_path.name}) is missing")
    kinds = {asset["Type"] for asset in assets}
    if "Full" not in kinds or not kinds & {"Installer", "Portable"}:
        fail(f"{assets_path.name} should contain the full package and an installer or portable build, found {sorted(kinds)}")

    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        expected = {"id": PACK_ID, "version": version, "channel": channel, "mainExe": main_exe}
        # The package manifest at the root; Windows and macOS also ship it as sq.version next to the app, while on Linux
        # it lives inside the AppImage.
        manifests = [n for n in names if "/" not in n and n.endswith(".nuspec")]
        manifests += [n for n in names if n.endswith("sq.version")]
        if not manifests:
            fail(f"{package.name} contains no package manifest")
        for name in manifests:
            manifest = archive.read(name).decode("utf-8-sig")
            for element, value in expected.items():
                if manifest_value(manifest, element) != value:
                    fail(f"{name} has {element}={manifest_value(manifest, element)!r}, expected {value!r}")
        if channel == "linux":
            if not any(n.endswith(".AppImage") for n in names):
                fail(f"{package.name} does not contain the AppImage")
        elif not any(n.endswith("sq.version") for n in names):
            fail(f"{package.name} does not contain sq.version")
        elif not any(n.endswith("/" + main_exe) for n in names):
            fail(f"{package.name} does not contain {main_exe}")

        if bundle_id is not None:
            plists = [n for n in names if n.endswith("Contents/Info.plist")]
            if len(plists) != 1:
                fail(f"{package.name} should contain one Info.plist, found {plists}")
            info = plistlib.loads(archive.read(plists[0]))
            for key, value in {"CFBundleIdentifier": bundle_id, "CFBundleExecutable": main_exe,
                               "CFBundleName": "Hash Tool", "CFBundleVersion": version}.items():
                if info.get(key) != value:
                    fail(f"Info.plist has {key}={info.get(key)!r}, expected {value!r}")

    print(f"{channel}: {package.name} ({package.stat().st_size:,} bytes) and {len(assets)} assets look right")
    for path in sorted(releases.iterdir()):
        print(f"  {path.name}")


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6):
        print(__doc__)
        sys.exit(2)
    main(releases_folder(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] if len(sys.argv) == 6 else None)
