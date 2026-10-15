#!/usr/bin/env python3
"""Fails when a package's line coverage in Cobertura reports is below a threshold.

    python -I eng/coverage-gate.py <results-dir> <package> <minimum-percent>

Every *.cobertura.xml under <results-dir> is read; the package's best line rate counts (each test project reports the
assemblies it loaded, so HashTool.Core shows up in several reports with different coverage).
"""

import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path


def main(results: str, package: str, minimum: float) -> int:
    rates = []
    for report in Path(results).rglob("*.cobertura.xml"):
        for element in ElementTree.parse(report).getroot().iter("package"):
            if element.get("name") == package:
                rates.append(float(element.get("line-rate", "0")) * 100)
    if not rates:
        print(f"::error::No coverage data for {package} under {results}")
        return 1
    best = max(rates)
    print(f"{package}: {best:.1f}% line coverage (minimum {minimum:.0f}%)")
    if best < minimum:
        print(f"::error::{package} line coverage {best:.1f}% is below {minimum:.0f}%")
        return 1
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2], float(sys.argv[3])))
