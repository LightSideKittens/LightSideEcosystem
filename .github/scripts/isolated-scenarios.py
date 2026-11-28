#!/usr/bin/env python3
"""Game-loop scenario numbers of a suite's isolated launches, grouped into parts a Test Lab test runs in its time limit.

  isolated-scenarios.py <isolated-launches.json> <suite id> <launches per part> <timing repeats> <memory repeats>

isolated-launches.json is the list the player build writes beside itself: {suiteId: {scenario, timing: [case ids],
memory: [participant ids], arms: {case id: [participant ids]}}}; `arms` is the only mapping between them (participant
ids are not parsed). Repeat r of timing case i runs as 10000 * scenario + 1000 * (r + 1) + i + 1, repeat r of memory
participant j as 10000 * scenario + 1000 * (r + 1) + 501 + j (BenchmarkRunner).

What must share a physical phone is what is compared across launches. The arms of a timing case are compared inside one
launch, so a case's timing launches form a group of their own. Memory arms are compared across launches, so the
participants connected through the cases that list them (a text suite's engines of one corpus, a scenario's twins) run
together: one group per connected set and repeat, the participants of repeat r rotated by r; `reference`, the empty
scene and the floor of every set, joins the first set. A whole connected set over every repeat could outlast Test Lab's
45-minute limit on a slow phone (the text suite's runs through every phase), while one repeat of it fits; across repeats
the comparison stays paired within each repeat's phone. A group with more launches than a part takes a part of its own.
Prints one comma-separated part per line."""
import json
import sys


def components(participants, cases, arms):
    """Participants connected through the cases that list them, in participant order; `reference` joins the first."""
    parent = {p: p for p in participants}

    def root(p):
        while parent[p] != p:
            parent[p] = parent[parent[p]]
            p = parent[p]
        return p

    for case in cases:
        listed = [p for p in arms.get(case, []) if p in parent]
        for other in listed[1:]:
            parent[root(other)] = root(listed[0])
    sets = {}
    for p in participants:
        if p != 'reference':
            sets.setdefault(root(p), []).append(p)
    out = list(sets.values())
    if 'reference' in parent:
        if out:
            out[0].insert(0, 'reference')
        else:
            out.append(['reference'])
    return out


def main():
    path, suite = sys.argv[1], sys.argv[2]
    per_part, timing_repeats, memory_repeats = (int(v) for v in sys.argv[3:6])
    entry = json.load(open(path, encoding='utf-8')).get(suite) or {}
    cases, participants, arms = entry.get('timing') or [], entry.get('memory') or [], entry.get('arms') or {}
    if not cases and not participants:
        raise SystemExit(f'{path} lists no isolated launches for suite {suite!r}')
    if len(cases) > 499 or len(participants) > 499 or not 1 <= max(timing_repeats, memory_repeats) <= 9:
        raise SystemExit(f'{len(cases)} cases, {len(participants)} participants, repeats {timing_repeats}/{memory_repeats} '
                         'do not fit the scenario numbers')
    unknown = sorted({p for c in cases for p in arms.get(c, []) if p not in participants})
    if unknown:
        raise SystemExit(f'{path}: cases list memory participants the build does not run: {unknown}')
    base = 10000 * entry['scenario']
    index = {p: j for j, p in enumerate(participants)}
    groups = [(case, [base + 1000 * (r + 1) + i + 1 for r in range(timing_repeats)]) for i, case in enumerate(cases)]
    for members in components(participants, cases, arms):
        for r in range(memory_repeats):
            shift = r % len(members)
            groups.append((f'{members[0]} repeat {r}',
                           [base + 1000 * (r + 1) + 501 + index[p] for p in members[shift:] + members[:shift]]))
    parts, part = [], []
    for name, numbers in groups:
        if part and len(part) + len(numbers) > per_part:
            parts.append(part)
            part = []
        part += numbers
        if len(numbers) > per_part:
            print(f'{name}: {len(numbers)} launches exceed {per_part} per part; it runs as a part of its own', file=sys.stderr)
    if part:
        parts.append(part)
    for p in parts:
        print(','.join(str(n) for n in p))


if __name__ == '__main__':
    main()
