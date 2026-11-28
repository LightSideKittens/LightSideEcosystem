#!/usr/bin/env python3
"""Fails the job when UniText's glyph delivery failed on a device.

  validate-unitext-delivery.py [<dir>]

Reads the glyph suite's records of every benchmarkResults*.json in <dir> (the per-device files where they exist, the
plain copy otherwise). A glyph case record (`kind` oneShot) that did not measure means a round's UniText delivery
failed or its glyphs differed, which fails the case: fatal. A memory record that did not measure is fatal for a UniText
arm and reported for a rival; an engine a case could not run (`unsupported`) is reported. A record of neither shape,
such as the old engine-by-font layout, cannot be read and is fatal."""
import glob
import json
import os
import sys


def files(src):
    per_device = sorted(glob.glob(os.path.join(src, "benchmarkResults_*.json")))
    return per_device or sorted(glob.glob(os.path.join(src, "benchmarkResults.json")))


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "."
    paths = files(src)
    if not paths:
        print("No benchmarkResults*.json found: nothing to validate.")
        return 0
    fatal, reported, cases = [], [], 0
    for path in paths:
        with open(path, encoding="utf-8-sig") as fh:
            data = json.load(fh)
        device = (data.get("systemInfo") or {}).get("deviceModel") or os.path.basename(path)
        for key, record in (data.get("glyphRasterization") or {}).items():
            where = f"{device} · {key}"
            if not isinstance(record, dict):
                fatal.append(f"{where}: not a record")
                continue
            status = record.get("status")
            for engine, reason in (record.get("unsupported") or {}).items():
                reported.append(f"{where}: {engine} unsupported: {reason}")
            if record.get("kind") == "oneShot":
                cases += 1
                if status != "measured":
                    fatal.append(f"{where}: {status}: {record.get('reason', '')}")
            elif "armInfo" in record:
                if status in ("measured", "unsupported"):
                    if status == "unsupported":
                        reported.append(f"{where}: unsupported: {record.get('reason', '')}")
                elif (record["armInfo"] or {}).get("engine") == "unitext":
                    fatal.append(f"{where}: {status}: {record.get('reason', '')}")
                else:
                    reported.append(f"{where}: {status}: {record.get('reason', '')}")
            else:
                fatal.append(f"{where}: neither a glyph case record nor a memory record (an old result layout?)")
    for line in reported:
        print(f"  - {line}")
    if fatal:
        print(f"::error::UniText glyph delivery FAILED or unreadable in {len(fatal)} record(s):")
        for line in fatal:
            print(f"  ::error::  {line}")
        return 1
    print(f"UniText glyph delivery OK: {cases} glyph case record(s) measured across {len(paths)} device file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
