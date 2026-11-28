#!/usr/bin/env python3
"""GitHub step summary of benchmark result files, and the viewer's per-suite site streams.

  benchmark-summary.py <benchmarkResults*.json>... [--commit <sha>] [--branch <name>] [--streams-dir <dir>]

The text, glyph and scenario suites write case records (a case with its arms measured in rounds, `kind` frames or
oneShot) and memory records (one arm in a process of its own, `armInfo` and `memoryCase`; `reference`, the empty scene;
the special `cycles` and `hidden` participants); wp/WP1-contract.md and Benchmarks/SPEC.md define them, and the reducer
modules in benchmark-site read them. A suite record of neither shape stops the summary with its name. One launch's
figures carry no interval: the page's numbers come from the reducer over every launch."""

import datetime
import glob
import json
import math
import os
import re
import statistics
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), 'benchmark-site'))
from benchmark_streams import parse_stream, run_identity  # noqa: E402
import compare  # noqa: E402
import load  # noqa: E402

CASE_SECTIONS = ('unitextScenarios', 'textBenchmarks', 'glyphRasterization')
FRAME_COLUMNS = (('cpu', 'CPU ms'), ('processCpu', 'All threads ms'), ('gpu', 'GPU ms'), ('render', 'Render ms'),
                 ('objects', 'Objects'))
ONE_SHOT_COLUMNS = (('sync', 'Call ms'), ('committed', 'Work ms'), ('processCpu', 'All threads ms'), ('frames', 'Frames'))


def fmt_bytes(val):
    """Format bytes to human-readable."""
    if val is None:
        return "—"
    if val == 0:
        return "0 B"
    if val < 0:
        return f"-{fmt_bytes(-val)}"
    if val < 1024:
        return f"{val} B"
    if val < 1024 * 1024:
        return f"{val / 1024:.1f} KB"
    return f"{val / (1024 * 1024):.1f} MB"


SUITES = (
    ("text", "textBenchmarks", "__unitextTextRuns"),
    ("glyph", "glyphRasterization", "__unitextGlyphRuns"),
    ("scenarios", "unitextScenarios", "__unitextScenarioRuns"),
    ("motion", "motionBenchmarks", "__moveitMotionRuns"),
    ("animation", "moveitAnimation", "__moveitAnimationRuns"),
)


def existing_identities(dirpath):
    """Identities of the streams already in dirpath, so a run the device wrote itself is not emitted twice."""
    identities = set()
    for path in glob.glob(os.path.join(dirpath, "run-*.js")):
        with open(path, encoding="utf-8") as f:
            doc = parse_stream(f.read())
        if doc is not None:
            identities.add(run_identity(doc))
    return identities


def emit_streams(data, commit, branch, dirpath):
    """Write the viewer's per-suite site streams (run-<suite>-*.js) — the Python mirror of
    the runtime BenchmarkStreams.Split, so a CI run's combined JSON becomes drop-in files for Benchmarks/runs.
    Backfills the real commit/branch (which the on-device build could not read) when the JSON lacks them.
    A suite run already present in dirpath (the device's own stream) is not written again."""
    os.makedirs(dirpath, exist_ok=True)
    meta = data.setdefault("meta", {})
    if commit and commit not in ("?", "") and meta.get("commit") in (None, "", "unknown"):
        meta["commit"] = commit
    if branch and branch not in ("?", "") and meta.get("branch") in (None, "", "unknown"):
        meta["branch"] = branch

    si = data.get("systemInfo", {})
    plat = si.get("platform", "Unknown")
    dev = re.sub(r"[^A-Za-z0-9_-]", "-", si.get("deviceName") or "unknown")
    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d-%H%M%S") + f"-{plat}-{dev}"

    present = existing_identities(dirpath)
    for suite, keep, g in SUITES:
        section = data.get(keep)
        if not isinstance(section, dict) or len(section) == 0:
            continue
        clone = dict(data)
        for _, section_name, _ in SUITES:
            if section_name != keep:
                clone.pop(section_name, None)
        clone["suite"] = suite
        if run_identity(clone) in present:
            print(f"Kept the device stream of {suite} {clone.get('timestamp')}", file=sys.stderr)
            continue
        body = json.dumps(clone, indent=2)
        content = f"window.{g} = window.{g} || [];\nwindow.{g}.push(\n{body}\n);\n"
        out = os.path.join(dirpath, f"run-{suite}-{stamp}.js")
        with open(out, "w", encoding="utf-8") as f:
            f.write(content)
        print(f"Emitted {out}", file=sys.stderr)


def median_of(node):
    if not isinstance(node, dict):
        return None
    value = node.get("median")
    return value if isinstance(value, (int, float)) else None


def fmt_series(node, digits=2):
    """median / p95 of a summarized series, or why it is missing."""
    if not isinstance(node, dict):
        return "—"
    if node.get("status") == "unavailable":
        return "n/a"
    median, p95 = node.get("median"), node.get("p95")
    if median is None:
        return "—"
    return f"{median:.{digits}f} / {p95:.{digits}f}" if p95 is not None else f"{median:.{digits}f}"


def fmt_count(value):
    if value is None:
        return "—"
    if value >= 1_000_000:
        return f"{value / 1_000_000:.1f}M"
    if value >= 10_000:
        return f"{value / 1000:.0f}k"
    return f"{value:.0f}"


def cpu_node(metrics):
    """The frame's main-thread work: the lower median of the probe's frame window and FrameTimingManager's main-thread
    time, with its source. Each holds a present wait the other excludes somewhere: Android waits inside the frame
    window, macOS and Unity 2022.3 count the wait as FrameTimingManager main-thread time."""
    candidates = [(node, label) for node, label in ((metrics.get("frameMs"), "frame"), (metrics.get("cpuMainMs"), "FTM"))
                  if isinstance(node, dict) and isinstance(node.get("median"), (int, float))]
    if not candidates:
        return None, None
    return min(candidates, key=lambda candidate: candidate[0]["median"])


def cpu_work(metrics):
    node, label = cpu_node(metrics)
    return "—" if node is None else f"{fmt_series(node)} · {label}"


def fmt_setup(entry):
    """Setup milliseconds, with the instantiate and start parts when the record carries them."""
    total = entry.get("setupMs")
    if total is None:
        return "—"
    instantiate = entry.get("setupInstantiateMs")
    start = entry.get("setupStartMs")
    collections = entry.get("setupCollections")
    gc = f", {collections} GC" if collections else ""
    if instantiate is None or start is None:
        return f"{total}{gc}"
    return f"{total} ({instantiate:g} + {start:g}{gc})"


def render_animation_markers(participants):
    """Median milliseconds per frame of every profiler marker a development player recorded, per participant."""
    lines = []
    for key, entry in participants.items():
        markers = entry.get("metrics", {}).get("markers", {})
        values = [(name, median_of(series)) for name, series in markers.items()]
        values = [(name, value) for name, value in values if isinstance(value, (int, float)) and value >= 0.005]
        if values:
            lines.append(f"- {key}: " + ", ".join(f"{name} {value:.2f}" for name, value in values))
    if not lines:
        return
    print("Profiler markers, median ms per frame (development player):")
    print("")
    for line in lines:
        print(line)
    print("")


def render_animation(animation, si, cfg):
    """One table per case: every participant's main-thread work, update and canvas windows, GPU, allocation, setup
    and largest validation error; the memory case as bytes per instance."""
    settings = cfg.get("animation", {})
    print(f"### MoveIt clips vs Unity animation ({settings.get('measuredFrames', '?')} frames each)")
    print("")
    if si.get("softwareRenderer"):
        print(f"> software renderer ({si.get('graphicsDeviceName', '?')}): GPU columns measure the emulation")
        print("")
    for case, record in animation.items():
        participants = record.get("participants", {})
        print(f"#### {case}")
        print("")
        print(record.get("description", ""))
        print("")
        if case == "memory":
            print("| Participant | Managed / instance | Total used / instance | Retained after teardown |")
            print("|---|---|---|---|")
            for key, entry in participants.items():
                if entry.get("status") != "measured":
                    print(f"| {key} | **{entry.get('status')}**: {entry.get('reason', '')} | | |")
                    continue
                per = entry.get("perInstanceBytes", {})
                retained = entry.get("retainedBytes", {})
                print(f"| {key} | {fmt_bytes(per.get('gcUsed'))} | {fmt_bytes(per.get('totalUsed'))} "
                      f"| {fmt_bytes(retained.get('totalUsed'))} |")
            print("")
            continue
        print("| Participant | CPU ms (med / p95) | Update ms | Canvas ms | GPU ms | Alloc B/frame | Setup ms (instantiate + start) | Max error | Peak setup / measured | Warmup |")
        print("|---|---|---|---|---|---|---|---|---|---|")
        for key, entry in participants.items():
            status = entry.get("status")
            if status != "measured":
                print(f"| {key} | **{status}**: {entry.get('reason', '')} | | | | | | | | |")
                continue
            metrics = entry.get("metrics", {})
            alloc = metrics.get("allocatedBytes", {})
            warmup = entry.get("warmup", {})
            errors = entry.get("validation", {}).get("maxError", {})
            worst = ", ".join(f"{name} {value:.2g}" for name, value in errors.items()) or "—"
            print(
                f"| {key} "
                f"| {cpu_work(metrics)} "
                f"| {fmt_series(metrics.get('updateMs'))} "
                f"| {fmt_series(metrics.get('canvasMs'))} "
                f"| {fmt_series(metrics.get('gpuMs'))} "
                f"| {'n/a' if alloc.get('status') == 'unavailable' else fmt_count(median_of(alloc))} "
                f"| {fmt_setup(entry)} "
                f"| {worst} "
                f"| {fmt_peak(entry.get('setupPeak'))} / {fmt_peak(metrics.get('processPeak'))} "
                f"| {warmup.get('frames', '?')}{'' if warmup.get('settled') else ' (unsettled)'} |"
            )
        print("")
        render_animation_markers(participants)


def fmt_peak(window, absolute=False):
    """A peak window, {start, peak, growth} as every suite records it: its growth over the window's start, with
    absolute also the peak itself; a dash where the player tracks no peak."""
    if not isinstance(window, dict) or not isinstance(window.get("peak"), (int, float)):
        return "—"
    growth = f"+{fmt_bytes(window.get('growth'))}"
    return f"{growth} ({fmt_bytes(window['peak'])})" if absolute else growth

def describe(arm):
    if arm.engine == 'reference':
        return 'reference'
    parts = [arm.engine + (f' {arm.threads}' if arm.threads else ''), arm.style, f'load {arm.load:g}']
    if arm.variant:
        parts.append(arm.variant)
    if arm.control:
        parts.append(arm.control)
    return ', '.join(parts)


def fmt_value(value):
    return '—' if value is None else f'{value:.3g}' if abs(value) < 100 else f'{value:.0f}'


def render_case(section, key, record, meta, config):
    """One case record: every arm's mean over the measured rounds and the in-launch ratios its arms define."""
    launch = load.timing('', '', '', record, {'participant': key, 'repeat': meta.get('repeat', 0)}, config)
    launch.kind = record['kind']
    rounds = compare.measured(launch)
    hot = sum(1 for r in launch.rounds if r.hot and not r.warmup)
    reference = compare.reference_of(launch.arms)
    columns = ONE_SHOT_COLUMNS if launch.kind == 'oneShot' else FRAME_COLUMNS
    print(f"#### {section} · {key}")
    print("")
    reason = launch.clock.get('reason')
    print(f"{launch.kind}, {len(rounds)} measured rounds{'' if not hot else f' ({hot} dropped: thermal)'}"
          f"{', round 0 is the result' if launch.cold else ''}, clock {launch.clock['mode']}"
          f"{f' ({reason})' if reason else ''}, {launch.clock_threads} threads in the clock session"
          f"{', CPU above the reference arm' if reference and launch.kind == 'frames' else ''}")
    print("")
    print("| Arm | Configuration | " + " | ".join(title for _, title in columns) + " |")
    print("|---|---|" + "---|" * len(columns))
    for arm in launch.arms.values():
        cells = []
        for metric, _ in columns:
            values = [compare.slot(r, arm.id, metric, reference if arm.engine != 'reference' else None) for r in rounds]
            values = [v for v in values if v is not None]
            cells.append(fmt_value(statistics.fmean(values)) if values else '—')
        print(f"| {arm.id} | {describe(arm)} | " + " | ".join(cells) + " |")
    print("")
    for kind, a, b, scale, only in compare.pairs(launch.arms):
        metric = 'committed' if launch.kind == 'oneShot' else 'cpu'
        if only is not None and metric not in only:
            metric = only[0]
        values, below = compare.round_values(launch, a, b, metric, scale, kind in ('aa', 'positive'))
        if not values:
            print(f"- {kind} {a} ÷ {b} ({metric}): no round with both values above the reference ({below} at or below)")
        elif scale == 'recovery':
            scales = [r.slots[a]['controlBusy'] / r.slots[a]['controlAt'] for r in compare.measured(launch, True)
                      if a in r.slots and r.slots[a]['controlBusy'] and r.slots[a]['controlAt']]
            print(f"- positive control {a} on {b}: recovered {statistics.fmean(values) * 100:.0f} % of the loop ({metric}, "
                  f"{len(values)} rounds)" + (f"; the loop took {statistics.median(scales):.2f}× its calibrated time"
                                               if scales else ""))
        else:
            print(f"- {kind} {a} ÷ {b} ({metric}): {math.exp(statistics.fmean(values)):.3f} over {len(values)} rounds"
                  f"{f', {below} at or below the reference' if below else ''}")
    print("")


def render_memory(section, key, record):
    """One memory record: RAM (steady), peak, GPU and dirty parts in MiB, or the special participant's own figures."""
    info = record.get('armInfo') or {}
    if info.get('variant') == 'cycles':
        text, empty = record['growthPerCycleBytes'], record['emptyGrowthPerCycleBytes']
        print(f"- memory {key}: per cycle with text {fmt_bytes(text.get('dirty'))} dirty, empty {fmt_bytes(empty.get('dirty'))}")
        return
    if info.get('variant') == 'hidden':
        for phase in record.get('phases', []):
            print(f"- memory {key} repeat {phase.get('repeat')}: dirty shown {fmt_bytes((phase.get('shown') or {}).get('dirty'))}, "
                  f"hidden {fmt_bytes((phase.get('hidden') or {}).get('dirty'))}, cleared {fmt_bytes((phase.get('cleared') or {}).get('dirty'))}")
        return
    m = load.memory('', '', '', record, {'participant': key, 'repeat': 0})
    parts = f"steady {m.steady:.1f} MiB, peak {m.peak:.1f}, setup start {m.start:.1f}"
    if m.gpu is not None:
        parts += f", GPU {m.gpu:.1f}, dirty {m.dirty:.1f}"
    print(f"- memory {key} ({record.get('memoryCase') or 'empty scene'}, {describe(m.info)}): {parts}")


def render_cases(data, label):
    """Every case and memory record of the text, glyph and scenario suites in one result document."""
    meta = data.get('meta') or {}
    config = data.get('config') or {}
    for section in CASE_SECTIONS:
        records = data.get(section)
        if not isinstance(records, dict):
            continue
        print(f"### {section}")
        print("")
        for key, record in records.items():
            if not isinstance(record, dict):
                raise SystemExit(f"{label}: {section}.{key} is not a record")
            if record.get('status') != 'measured':
                print(f"- **{key}: {record.get('status')}** {record.get('reason', '')}")
                continue
            for engine, reason in (record.get('unsupported') or {}).items():
                print(f"- {key}: {engine} unsupported: {reason}")
            if record.get('kind') in ('frames', 'oneShot'):
                render_case(section, key, record, meta, config)
            elif 'armInfo' in record:
                render_memory(section, key, record)
            else:
                raise SystemExit(f"{label}: {section}.{key} is neither a case record nor a memory record")
        print("")


def render_header(data, commit, branch):
    si = data.get("systemInfo", {})
    meta = data.get("meta") or {}
    print("## Benchmark Results")
    print("")
    print(f"**Device:** {si.get('deviceModel', '?')} | **OS:** {si.get('operatingSystem', '?')} "
          f"| **GPU:** {si.get('graphicsDeviceName', '?')}")
    print(f"**Unity:** {si.get('unityVersion', '?')} ({si.get('scriptingBackend', '?')}, "
          f"{'development' if si.get('isDebugBuild') else 'release'} player, {si.get('renderPipeline', '?')}) "
          f"| **Screen:** {si.get('screenWidth', '?')}x{si.get('screenHeight', '?')} @ {si.get('screenRefreshRateHz', '?')} Hz")
    print(f"**Built:** host `{str(meta.get('commit', commit))[:8]}` ({meta.get('branch', branch)}), "
          f"UniText `{str(meta.get('submoduleCommit', '?'))[:8]}` | **Launch:** {meta.get('participant', 'all selected cases')}"
          f", repeat {meta.get('repeat', 0)}")
    print("")


def main():
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("json_paths", nargs="+", help="benchmarkResults*.json files")
    parser.add_argument("--commit", default="?", help="Git commit SHA")
    parser.add_argument("--branch", default="?", help="Git branch name")
    parser.add_argument("--streams-dir", default=None, help="If set, emit run-{suite}-*.js here and skip the summary")
    args = parser.parse_args()

    paths = [p for p in args.json_paths if os.path.exists(p)]
    if not paths:
        print("## Benchmark Results")
        print("")
        print("No benchmark results found.")
        return
    for path in paths:
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
        if args.streams_dir:
            emit_streams(data, args.commit, args.branch, args.streams_dir)
            continue
        render_header(data, args.commit, args.branch)
        render_cases(data, path)
        if data.get("moveitAnimation"):
            render_animation(data["moveitAnimation"], data.get("systemInfo", {}), data.get("config", {}))
        errors = data.get("errors", [])
        if errors:
            print("### Errors")
            print("")
            for err in errors:
                print(f"- {err}")
            print("")


if __name__ == "__main__":
    main()
