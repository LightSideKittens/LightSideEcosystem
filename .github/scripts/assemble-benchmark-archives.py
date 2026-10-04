#!/usr/bin/env python3
import sys
import os
import re
import zipfile

from benchmark_streams import known_commit, parse_stream, run_identity

ART_RE = re.compile(r"^BenchmarkResults-(?P<suite>[^-]+)-(?P<unity>[^-]+)-(?P<platform>.+)$")
SHOT_RE = re.compile(
    r"bench-(?P<ord>\d+)-glyph-UniText-(?P<mode>.+)-(?P<tag>warmup|iter-\d+)\.png$", re.IGNORECASE
)


def device_of(png_path):
    parent = os.path.basename(os.path.dirname(png_path))
    return "" if parent in ("screenshots", "") else parent


def unique_runs(paths):
    """One stream per suite run. The same run arrives several times — the device's own stream, its copy among
    the raw device results, and the stream CI derives from the result document — and only one may reach the
    viewer, or its trends count the run more than once. A copy that knows its commit wins."""
    chosen = {}
    for path in sorted(paths):
        with open(path, encoding="utf-8") as f:
            text = f.read()
        doc = parse_stream(text)
        key = ("unparsed", text) if doc is None else run_identity(doc)
        current = chosen.get(key)
        if current is None or (doc is not None and known_commit(doc) and not current[1]):
            chosen[key] = (path, doc is not None and known_commit(doc))
    return sorted(path for path, _ in chosen.values())


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)

    platforms = {}

    for entry in sorted(os.listdir(src) if os.path.isdir(src) else []):
        full = os.path.join(src, entry)
        m = ART_RE.match(entry)
        if not os.path.isdir(full) or not m:
            continue
        unity, platform = m.group("unity"), m.group("platform")
        p = platforms.setdefault(platform, {"runs": [], "shots": {}})
        for root, _dirs, files in os.walk(full):
            for fn in files:
                fp = os.path.join(root, fn)
                if fn.startswith("run-") and fn.endswith(".js"):
                    p["runs"].append(fp)
                    continue
                sm = SHOT_RE.search(fn)
                if sm:
                    key = (unity, device_of(fp), sm.group("mode"))
                    ordv = int(sm.group("ord"))
                    cur = p["shots"].get(key)
                    if cur is None or ordv > cur[0]:
                        p["shots"][key] = (ordv, fp)

    if not platforms:
        print("No benchmark artifacts matched — nothing to assemble.")
        return

    for platform, data in sorted(platforms.items()):
        zpath = os.path.join(dst, f"{platform}.zip")
        runs = unique_runs(data["runs"])
        with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
            seen = set()
            for fp in runs:
                name = os.path.basename(fp)
                arc = f"runs/{name}"
                n = 1
                while arc in seen:
                    stem, ext = os.path.splitext(name)
                    arc = f"runs/{stem}-{n}{ext}"
                    n += 1
                seen.add(arc)
                z.write(fp, arc)
            for (unity, device, mode), (_ord, fp) in sorted(data["shots"].items()):
                tag = f"{unity}-{device}" if device else unity
                z.write(fp, f"screenshots/{tag}-UniText-{mode}-last.png")
        print(f"{platform}.zip: {len(runs)} run files ({len(data['runs']) - len(runs)} duplicates dropped), "
              f"{len(data['shots'])} UniText screenshots")


if __name__ == "__main__":
    main()
