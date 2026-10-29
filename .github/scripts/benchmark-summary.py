#!/usr/bin/env python3
"""Generate GitHub Step Summary from benchmarkResults.json, and emit the viewer's per-suite site streams."""

import glob
import json
import sys
import os
import re
import datetime

from benchmark_streams import parse_stream, run_identity


def fmt_ms(val):
    """Format milliseconds."""
    if val is None or val == 0:
        return "—"
    if val >= 1000:
        return f"{val:.0f} ms"
    return f"{val:.1f} ms"


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


def ratio_str(a, b, digits=1):
    """Calculate ratio b/a (higher = b is slower)."""
    if a is None or b is None or a == 0:
        return "—"
    r = b / a
    return f"{r:.{digits}f}x"


def get_median(bench, test_name):
    """Get median from a benchmark test."""
    test = bench.get(test_name)
    if test is None:
        return None
    return test.get("median", 0)


def get_total(bench, test_name):
    """Get totalMs from a benchmark test."""
    test = bench.get(test_name)
    if test is None:
        return None
    return test.get("totalMs", 0)


def get_counter(bench, test_name, field):
    """A phase's allocation counter (managedAlloc, managedObjects, gcCollections), or None where it is missing."""
    value = (bench.get(test_name) or {}).get(field)
    return value if isinstance(value, (int, float)) else None


def net_of(value, floor):
    """value minus the floor measured over the same number of frames; value itself when no floor exists."""
    if value is None:
        return None
    return value - floor if floor is not None else value


def get_repeat_alloc(bench, test_name):
    """Get the allocation traffic of the last identical repeat cycle run after the measured frames, or None."""
    test = bench.get(test_name)
    if test is None:
        return None
    probes = (test.get("memory") or {}).get("probes") or []
    values = [p.get("managedAlloc") for p in probes if isinstance(p.get("managedAlloc"), (int, float))]
    return values[-1] if values and values[-1] >= 0 else None


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


def main():
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("json_path", help="Path to benchmarkResults.json")
    parser.add_argument("--commit", default="?", help="Git commit SHA")
    parser.add_argument("--branch", default="?", help="Git branch name")
    parser.add_argument("--streams-dir", default=None, help="If set, emit run-{suite}-*.js here and skip the summary")
    args = parser.parse_args()

    path = args.json_path

    if not os.path.exists(path):
        print("## Benchmark Results")
        print("")
        print("No benchmark results found.")
        sys.exit(0)

    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    if args.streams_dir:
        emit_streams(data, args.commit, args.branch, args.streams_dir)
        return

    commit_sha = args.commit[:8] if len(args.commit) > 8 else args.commit
    branch = args.branch

    si = data.get("systemInfo", {})
    cfg = data.get("config", {})
    text = data.get("textBenchmarks", {})
    glyph = data.get("glyphRasterization", {})
    scenarios = data.get("unitextScenarios", {})
    animation = data.get("moveitAnimation", {})
    errors = data.get("errors", [])

    # Header
    print("## Benchmark Results")
    print("")
    print(
        f"**Device:** {si.get('deviceModel', '?')} "
        f"| **OS:** {si.get('operatingSystem', '?')} "
        f"| **GPU:** {si.get('graphicsDeviceName', '?')}"
    )
    print(
        f"**CPU:** {si.get('processorType', '?')} ({si.get('processorCount', '?')} cores @ {si.get('processorFrequency', '?')} MHz) "
        f"| **RAM:** {si.get('systemMemorySize', '?')} MB"
    )
    print(
        f"**Unity:** {si.get('unityVersion', '?')} ({si.get('scriptingBackend', '?')}, "
        f"{'development' if si.get('isDebugBuild') else 'release'} player) "
        f"| **Screen:** {si.get('screenWidth', '?')}x{si.get('screenHeight', '?')} @ {si.get('screenDpi', '?')} dpi"
    )
    print(
        f"**Commit:** `{commit_sha}` "
        f"| **Branch:** `{branch}`"
    )
    print("")

    if scenarios:
        render_scenarios(scenarios, si, cfg)

    if animation:
        render_animation(animation, si, cfg)

    if text:
        render_text(text, cfg)

    if glyph:
        render_glyph(glyph)

    # Errors
    if errors:
        print("### Errors")
        print("")
        for err in errors:
            print(f"- {err}")
        print("")


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


def per_frame(metrics, field):
    """Mean of an allocation series (allocatedBytes, allocatedObjects) over the measured frames, or None where the
    player does not count it."""
    node = (metrics or {}).get(field)
    frames = (metrics or {}).get("frames")
    if not isinstance(node, dict) or not isinstance(node.get("total"), (int, float)) or not frames:
        return None
    return node["total"] / frames


def fmt_allocation(metrics, field, floor, digits):
    """Mean per frame minus the idle floor, then how many frames allocated and the largest frame."""
    mean = per_frame(metrics, field)
    if mean is None:
        return "n/a"
    node = metrics[field]
    text = f"{net_of(mean, floor):.{digits}f}"
    frames, peak = node.get("allocatingFrames"), node.get("max")
    if frames:
        text += f" · {frames} fr · max {peak:.0f}" if isinstance(peak, (int, float)) else f" · {frames} fr"
    return text


def render_scenarios(scenarios, si, cfg):
    settings = cfg.get("scenarios", {})
    startup = scenarios.get("startup")
    if startup is not None:
        render_startup(startup)
    print(f"### UniText Scenarios ({settings.get('measuredFrames', '?')} frames each)")
    print("")
    notes = []
    if si.get("softwareRenderer"):
        notes.append(f"software renderer ({si.get('graphicsDeviceName', '?')}): GPU and interval columns measure the emulation, not a GPU")
    if si.get("frameTimingStats") is False:
        notes.append("Frame Timing Stats off: no FrameTimingManager CPU/GPU times")
    if si.get("isDebugBuild"):
        notes.append("development player")
    if notes:
        print("> " + "; ".join(notes))
        print("")
    idle = scenarios.get("idle") or {}
    idle_metrics = idle.get("metrics") if idle.get("status") == "measured" else None
    floors = {field: per_frame(idle_metrics, field) for field in ("allocatedBytes", "allocatedObjects")}
    if idle_metrics is not None:
        idle_bytes, idle_objects = floors["allocatedBytes"], floors["allocatedObjects"]
        bytes_text = "n/a" if idle_bytes is None else f"{idle_bytes:.0f} B"
        objects_text = "n/a" if idle_objects is None else f"{idle_objects:.2f}"
        print(f"> allocation columns: mean per frame minus the idle scenario's ({bytes_text}, {objects_text} objects), "
              "then the frames that allocated anything and the largest frame")
        print("")
    print("| Scenario | CPU ms (med / p95) | Canvas ms | GPU ms | Interval ms | Alloc B/frame | Objects/frame | GC | Draw calls | Vertices | Warmup |")
    print("|---|---|---|---|---|---|---|---|---|---|---|")
    memory = None
    for key, record in scenarios.items():
        if key == "startup":
            continue
        if "growthPerCycleBytes" in record or key.startswith("memory."):
            memory = (key, record)
            continue
        status = record.get("status")
        if status != "measured":
            print(f"| {key} | **{status}**: {record.get('reason', '')} | | | | | | | | | |")
            continue
        metrics = record.get("metrics", {})
        render = metrics.get("render", {})
        warmup = record.get("warmup", {})
        unsettled = "" if warmup.get("settled") else " (unsettled)"
        floor = (lambda field: None) if key == "idle" else floors.get
        print(
            f"| {key} "
            f"| {cpu_work(metrics)} "
            f"| {fmt_series(metrics.get('canvasMs'))} "
            f"| {fmt_series(metrics.get('gpuMs'))} "
            f"| {fmt_series(metrics.get('intervalMs'))} "
            f"| {fmt_allocation(metrics, 'allocatedBytes', floor('allocatedBytes'), 0)} "
            f"| {fmt_allocation(metrics, 'allocatedObjects', floor('allocatedObjects'), 2)} "
            f"| {metrics.get('gcCollections', '—')} "
            f"| {fmt_count(median_of(render.get('drawCalls')))} "
            f"| {fmt_count(median_of(render.get('vertices')))} "
            f"| {warmup.get('frames', '?')}{unsettled} |"
        )
    print("")
    render_pairs(scenarios, floors)
    if memory is None:
        return
    key, record = memory
    print(f"### {key}")
    print("")
    if record.get("status") != "measured":
        print(f"**{record.get('status')}**: {record.get('reason', '')}")
        print("")
        return
    print("| Counter | Before | After first cycle | After last cycle | Growth per cycle |")
    print("|---|---|---|---|---|")
    growth = record.get("growthPerCycleBytes", {})
    for counter, value in growth.items():
        print(
            f"| {counter} "
            f"| {fmt_bytes(record.get('before', {}).get(counter))} "
            f"| {fmt_bytes(record.get('afterFirstCycle', {}).get(counter))} "
            f"| {fmt_bytes(record.get('afterLastCycle', {}).get(counter))} "
            f"| {fmt_bytes(value)} |"
        )
    print("")


def render_startup(record):
    """The first screen of text each engine opened in the process, and the same screen opened again."""
    print("### First screen of text")
    print("")
    if record.get("status") != "measured":
        print(f"**{record.get('status')}**: {record.get('reason', '')}")
        print("")
        return
    if not record.get("player"):
        print("> editor: both engines were already warm")
        print("")
    print("| Engine | Create ms | First frame ms | First 4 frames ms | Second open: first frame ms | Glyphs |")
    print("|---|---|---|---|---|---|")
    for key, entry in record.items():
        if not key.startswith("startup.") or not isinstance(entry, dict):
            continue
        first, second = entry.get("first", {}), entry.get("second", {})
        frames, again = first.get("framesMs") or [None], second.get("framesMs") or [None]
        print(
            f"| {key.split('.', 1)[1]} "
            f"| {fmt_ms(first.get('createMs'))} "
            f"| {fmt_ms(frames[0])} "
            f"| {fmt_ms(first.get('totalMs'))} "
            f"| {fmt_ms(again[0])} "
            f"| {first.get('glyphs', '—')} |"
        )
    print("")


def render_pairs(scenarios, floors):
    """Each scenario X against its TMP twin X.tmp, and the text shader against the default canvas material."""
    measured = {key: record for key, record in scenarios.items()
                if isinstance(record, dict) and record.get("status") == "measured" and "metrics" in record}
    pairs = [(key, record, measured[key + ".tmp"]) for key, record in measured.items() if key + ".tmp" in measured]
    if pairs:
        print("### UniText ÷ TMP")
        print("")
        print("| Scenario | CPU | Canvas | GPU | Objects/frame UniText / TMP | Glyphs UniText / TMP |")
        print("|---|---|---|---|---|---|")
        for key, ours, theirs in pairs:
            ours_metrics, theirs_metrics = ours["metrics"], theirs["metrics"]
            ours_cpu, theirs_cpu = cpu_node(ours_metrics)[0], cpu_node(theirs_metrics)[0]
            ours_objects = net_of(per_frame(ours_metrics, "allocatedObjects"), floors.get("allocatedObjects"))
            theirs_objects = net_of(per_frame(theirs_metrics, "allocatedObjects"), floors.get("allocatedObjects"))
            print(
                f"| {key} "
                f"| {ratio_str(median_of(theirs_cpu), median_of(ours_cpu), 2)} "
                f"| {ratio_str(median_of(theirs_metrics.get('canvasMs')), median_of(ours_metrics.get('canvasMs')), 2)} "
                f"| {ratio_str(median_of(theirs_metrics.get('gpuMs')), median_of(ours_metrics.get('gpuMs')), 2)} "
                f"| {'n/a' if ours_objects is None else f'{ours_objects:.1f}'} / {'n/a' if theirs_objects is None else f'{theirs_objects:.1f}'} "
                f"| {ours.get('workload', {}).get('glyphs', '—')} / {theirs.get('workload', {}).get('glyphs', '—')} |"
            )
        print("")
    screen, default = measured.get("screen"), measured.get("screen.default")
    if screen and default:
        screen_gpu, default_gpu = median_of(screen["metrics"].get("gpuMs")), median_of(default["metrics"].get("gpuMs"))
        print(f"Text shader: screen GPU is {ratio_str(default_gpu, screen_gpu, 2)} the same screen drawn with "
              f"{default.get('workload', {}).get('material', 'the default canvas material')}.")
        print("")


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
        print("| Participant | CPU ms (med / p95) | Update ms | Canvas ms | GPU ms | Alloc B/frame | Setup ms (instantiate + start) | Max error | Warmup |")
        print("|---|---|---|---|---|---|---|---|---|")
        for key, entry in participants.items():
            status = entry.get("status")
            if status != "measured":
                print(f"| {key} | **{status}**: {entry.get('reason', '')} | | | | | | | |")
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
                f"| {warmup.get('frames', '?')}{'' if warmup.get('settled') else ' (unsettled)'} |"
            )
        print("")
        render_animation_markers(participants)


def render_text(text, cfg):
    # Text Pipeline Table
    uni_st = text.get("unitextSingleThreaded", {})
    uni_par = text.get("unitextParallel", {})
    tmp = text.get("tmp", {})
    uitk = text.get("uiToolkit", {})

    tests = [
        ("Creation", "creation"),
        ("Destruction", "destruction"),
        ("Idle", "idle"),
        ("Full Rebuild", "fullRebuild"),
        ("Full Rebuild (Unique)", "fullRebuildUnique"),
        ("Full Rebuild (Rich Text)", "fullRebuildRichText"),
        ("Layout (Wrap+NoAuto)", "layoutWrapNoAuto"),
        ("Layout (Wrap+Auto)", "layoutWrapAuto"),
        ("Layout (NoWrap+NoAuto)", "layoutNoWrapNoAuto"),
        ("Layout (NoWrap+Auto)", "layoutNoWrapAuto"),
        ("Mesh Rebuild", "meshRebuild"),
    ]

    obj_count = cfg.get("objectCount", "?")
    iters = cfg.get("iterations", "?")

    print(
        f"### Text Pipeline ({obj_count} objects x {iters} iterations)"
    )
    print("")
    print("Every text draws an outline and a shadow: UniText stroke and shadow styles, "
          "TMP an outline and underlay material, UI Toolkit text outline and text-shadow.")
    print("")
    print(
        "| Phase | UniText | Parallel | TMP | UIToolkit | vs TMP | vs UIToolkit |"
    )
    print("|-------|---------|----------|-----|-----------|--------|--------------|")

    for label, key in tests:
        u = get_total(uni_st, key)
        p = get_total(uni_par, key)
        t = get_total(tmp, key)
        ui = get_total(uitk, key)

        print(
            f"| {label} "
            f"| {fmt_ms(u)} "
            f"| {fmt_ms(p)} "
            f"| {fmt_ms(t)} "
            f"| {fmt_ms(ui)} "
            f"| {ratio_str(u, t)} "
            f"| {ratio_str(u, ui)} |"
        )

    print("")

    print("### Managed Allocation Traffic")
    print("")
    benches = (uni_st, uni_par, tmp, uitk)
    fields = ("managedAlloc", "managedObjects")
    if all(get_counter(bench, key, field) is None for bench in benches for _, key in tests for field in fields):
        print("Unavailable: managed bytes are published only in development players, managed objects only in IL2CPP players.")
    else:
        print("Each cell, summed over the phase's measured frames and net of the same engine's Idle phase (the same "
              "frame count with nothing changing): bytes, objects and garbage collections, then the bytes of an "
              "identical repeat cycle run right after them (creation's repeat also holds its destruction). "
              "The Idle row is raw: the player's and the harness's own floor.")
        print("")
        print("| Phase | UniText | Parallel | TMP | UIToolkit |")
        print("|-------|---------|----------|-----|-----------|")
        for label, key in tests:
            cells = []
            for bench in benches:
                floors = {field: None if key == "idle" else get_counter(bench, "idle", field) for field in fields}
                net_bytes = net_of(get_counter(bench, key, "managedAlloc"), floors["managedAlloc"])
                net_objects = net_of(get_counter(bench, key, "managedObjects"), floors["managedObjects"])
                collections = get_counter(bench, key, "gcCollections")
                repeat = get_repeat_alloc(bench, key)
                cells.append(" · ".join([
                    fmt_bytes(net_bytes),
                    "—" if net_objects is None else f"{net_objects:.0f} obj",
                    "—" if collections is None else f"{collections:.0f} GC",
                ]) + ("" if repeat is None else f" · repeat {fmt_bytes(repeat)}"))
            print(f"| {label} | " + " | ".join(cells) + " |")

    print("")


def render_glyph(glyph):
    # Glyph Rasterization (nested: engine -> font -> data; tolerates the old flat shape too)
    if glyph:
        print("### Glyph Rasterization")
        print("")
        print("TMP and UI Toolkit rasterize at 64 pt with a 6 px SDF spread; UniText runs with Tile Size Offset -2, "
              "so every glyph takes the 64 px tile class. UniText +stroke compares against the same TMP and UI Toolkit runs. "
              "UI Toolkit only schedules its work at the trigger, so it has no CPU column: compare E2E across engines.")
        print("")
        print("| Engine · Font | Status | Glyphs | CPU Median | E2E Median | Per-glyph (E2E) | Cold allocation (bytes · objects) |")
        print("|---|---|---|---|---|---|---|")

        labels = {
            "unitextSingleThreaded": "UniText ST",
            "unitextParallel": "UniText MT",
            "unitextSingleThreadedMaxStroke": "UniText ST +stroke",
            "unitextParallelMaxStroke": "UniText MT +stroke",
            "tmp": "TMP",
            "uiToolkit": "UI Toolkit",
        }
        for ekey, entry in glyph.items():
            label = labels.get(ekey, ekey)
            is_flat = isinstance(entry, dict) and ("median" in entry or "frameTimes" in entry)
            rows = [("—", entry)] if is_flat else entry.items()
            for font, g in rows:
                if not g:
                    continue
                status = g.get("status", "measured")
                status_cell = status if status == "measured" else f"**{status}**"
                e2e = g.get("e2eMedian")
                e2e_pg = g.get("perGlyphE2eMedianUs")
                print(
                    f"| {label} · {font} "
                    f"| {status_cell} "
                    f"| {g.get('uniqueGlyphs', '?')} "
                    f"| {fmt_ms(g.get('median', 0))} "
                    f"| {fmt_ms(e2e)} "
                    f"| {(f'{e2e_pg:.1f} us' if e2e_pg else '—')} "
                    f"| {fmt_bytes(g.get('managedAlloc'))} · {g['managedObjects'] if isinstance(g.get('managedObjects'), (int, float)) else '—'} |"
                )

        print("")


if __name__ == "__main__":
    main()
