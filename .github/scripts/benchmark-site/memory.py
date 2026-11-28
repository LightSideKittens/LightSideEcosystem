"""Memory of the cases: what each arm's process holds, and UniText against its rival on the same phone.

Each memory launch runs one arm in a process of its own. Per launch: steady = the measured window's mean footprint plus
what the system read sees beyond it (Android's GPU driver memory); peak = the highest footprint from the start of the
scene's setup plus the same; GPU = the system read's graphics part (Android only); dirty = steady minus GPU (Android
only: an iPhone's footprint holds both). RAM, the headline, is steady (dirty plus GPU: one RAM on a phone). A phone's
value is the mean of at least three launches with their range. Per arm the page shows what the scene adds, measured
inside each launch: steady and peak above the process's own memory at the start of the scene's setup, minus the same
growth of the empty scene (the memory launches of `reference`, which may run on another phone of the model). The
sampled footprint (`dirtyAdded`; on an iPhone it holds the GPU memory too) is taken in one process, so a unit's
baseline cancels. On Android the GPU part of steady and peak comes from one system read per process, which has no
in-launch start, so `steadyAdded` and `peakAdded` carry its launch-to-launch steps: they are the median over the
launches with their range, and the flagged phones stay flagged. UniText against its rival compares absolute values, which the part
grouping keeps paired on one phone within each repeat, so no floor enters that difference.

A difference counts on a phone when the launch ranges of the two arms do not overlap. Android phones and iPhones are
summarized apart (an iPhone's footprint holds its GPU memory). A direction is claimed across a group's phones when at
least 80 % of the phones that passed agree in sign and most of them separate; no separation anywhere is equal, both
directions separated is mixed, anything else inconclusive. Known device anomalies keep their points visible but out of the summary."""
import math
import statistics

import compare
import devices

MIN_LAUNCHES = 3
MIN_PHONES = 3
AGREEMENT = 0.8
QUANTITIES = ('steady', 'dirty', 'gpu', 'peak')
FLAGS = (
    (lambda d, q, e: d == 'dm3q' and q in ('gpu', 'steady') and e == 'unitext',
     'Galaxy S23 Ultra: the GPU part of UniText launches steps by ~9.4 MiB between launches of one case (unexplained)'),
    (lambda d, q, e: d == 'shiba' and q in ('gpu', 'steady'),
     'Pixel 8: UniText adds 0-2 MiB of GPU memory against ~11 on other phones (stable, unattributed)'),
    (lambda d, q, e: devices.phone(d)['group'] == 'ios' and q == 'peak',
     'iPhone: TextMesh Pro setup transients of 180-390 MiB are unattributed; iPhone peaks are withheld'),
)


def flag(device, quantity, engine):
    for test, reason in FLAGS:
        if test(device, quantity, engine):
            return reason
    return None


def arm_values(launches, quantity):
    values = [getattr(l, quantity) for l in launches]
    return None if any(v is None for v in values) else values


def per_arm(memory):
    """Per case and arm, per phone: the mean and range of each quantity and of what the scene adds."""
    floors = {}
    for l in memory:
        if l.arm == 'reference':
            floors.setdefault(l.device, []).append(l)
    groups = {}
    for l in memory:
        if l.arm != 'reference':
            groups.setdefault((l.case, l.arm, l.device), []).append(l)
    rows = {}
    for (case, arm, device), launches in sorted(groups.items()):
        entry = devices.phone(device)
        floor = floors.get(device, [])
        point = {'device': device, 'label': entry['label'], 'tier': entry['tier'], 'group': entry['group'],
                 'launches': len(launches)}
        if len(launches) < MIN_LAUNCHES:
            point['excluded'] = f'{len(launches)} memory launches, {MIN_LAUNCHES} needed'
        for q in QUANTITIES:
            values = arm_values(launches, q)
            if values:
                point[q] = {'mean': statistics.fmean(values), 'min': min(values), 'max': max(values)}
        if len(floor) >= MIN_LAUNCHES:
            for name, of in (('dirtyAdded', lambda l: l.footprint - l.start), ('steadyAdded', lambda l: l.steady - l.start),
                             ('peakAdded', lambda l: l.peak - l.start)):
                base = statistics.median(of(l) for l in floor)
                values = [of(l) - base for l in launches]
                point[name] = {'median': statistics.median(values), 'min': min(values), 'max': max(values)}
            point['flags'] = sorted({reason for q in ('steady', 'gpu', 'peak')
                                     for reason in [flag(device, q, launches[0].info.engine)] if reason})
        else:
            point['addedExcluded'] = f'{len(floor)} launches of the empty scene, {MIN_LAUNCHES} needed'
        rows.setdefault((case, arm), []).append(point)
    info = {(l.case, l.arm): l.info for l in memory}
    return [{'case': case, 'arm': arm, 'info': info[(case, arm)], 'engine': info[(case, arm)].engine, 'points': points}
            for (case, arm), points in sorted(rows.items())]


def separation(a, b):
    if a['min'] > b['max']:
        return 'higher'
    if a['max'] < b['min']:
        return 'lower'
    return 'overlap'


def twins(arm_rows, arms_of, park):
    """UniText against each rival arm of the same style and load that meets its threads (`compare.meets`), per quantity
    and platform group: per phone the difference of means with both launch ranges and whether they separate; across
    the group's phones the median difference and the class. `park[group]` is the number of the group's phones in the run: a summary needs three of
    them, or all of them where the run has fewer."""
    by_case = {}
    for row in arm_rows:
        by_case.setdefault(row['case'], {})[row['arm']] = row
    out = []
    for case, rows in sorted(by_case.items()):
        own = compare.ours(arms_of(case))
        described = [row['info'] for row in rows.values() if not row['info'].control]
        for u in (a for a in described if a.engine == own):
            for r in (a for a in described if a.engine not in (own, 'reference') and (a.style, a.load) == (u.style, u.load)
                      and compare.meets(u.threads, a.threads)):
                mine = {p['device']: p for p in rows[u.id]['points']}
                theirs = {p['device']: p for p in rows[r.id]['points']}
                for group in sorted(park):
                    for q in QUANTITIES:
                        points = []
                        for device in sorted(set(mine) & set(theirs)):
                            a, b = mine[device], theirs[device]
                            if a['group'] != group or q not in a or q not in b:
                                continue
                            reason = (a.get('excluded') or b.get('excluded') or flag(device, q, own)
                                      or flag(device, q, r.engine))
                            points.append({'device': device, 'label': a['label'], 'tier': a['tier'], 'group': group,
                                           'unitext': a[q], 'rival': b[q], 'difference': a[q]['mean'] - b[q]['mean'],
                                           'ratio': a[q]['mean'] / b[q]['mean'], 'separation': separation(a[q], b[q]),
                                           'excluded': reason})
                        if points:
                            out.append({'case': case, 'a': u.id, 'b': r.id, 'rival': r.engine,
                                        'rivalName': compare.arm_name(r), 'rivalThreads': r.threads, 'style': u.style,
                                        'load': u.load, 'threads': u.threads, 'group': group, 'quantity': q,
                                        'points': points, 'summary': summary(points, min(MIN_PHONES, park[group]))})
    return out


def summary(points, need_phones):
    excluded = [{'device': p['device'], 'reason': p['excluded']} for p in points if p['excluded']]
    kept = [p for p in points if not p['excluded']]
    if len(kept) < need_phones:
        return {'phones': len(kept), 'class': 'unmeasured', 'sentence': f'{len(kept)} phones passed, {need_phones} needed',
                'excluded': excluded}
    n = len(kept)
    diffs = [p['difference'] for p in kept]
    higher = sum(1 for d in diffs if d > 0)
    lower = n - higher
    separated = {s: sum(1 for p in kept if p['separation'] == s) for s in ('higher', 'lower')}
    spread = max(max(p['unitext']['max'] - p['unitext']['min'], p['rival']['max'] - p['rival']['min']) for p in kept)
    out = {'phones': n, 'unitext': statistics.fmean(p['unitext']['mean'] for p in kept),
           'rival': statistics.fmean(p['rival']['mean'] for p in kept), 'medianDifference': statistics.median(diffs),
           'minDifference': min(diffs), 'maxDifference': max(diffs), 'medianRatio': statistics.median(p['ratio'] for p in kept),
           'higherOn': higher, 'lowerOn': lower, 'separatedHigher': separated['higher'], 'separatedLower': separated['lower'],
           'within': n - separated['higher'] - separated['lower'], 'spread': spread, 'excluded': excluded}
    need = math.ceil(AGREEMENT * n)
    if higher >= need and separated['higher'] * 2 > n:
        out['class'], out['sentence'] = 'higher', f'UniText holds more on {higher} of {n} phones'
    elif lower >= need and separated['lower'] * 2 > n:
        out['class'], out['sentence'] = 'lower', f'UniText holds less on {lower} of {n} phones'
    elif separated['higher'] == 0 and separated['lower'] == 0:
        out['class'], out['sentence'] = 'equal', f'the same within the launch spread on all {n} phones'
    elif separated['higher'] and separated['lower']:
        out['class'], out['sentence'] = 'mixed', f'more on {separated["higher"]}, less on {separated["lower"]} of {n} phones'
    else:
        out['class'], out['sentence'] = 'inconclusive', f'no direction on most of {n} phones'
    if not any(p['tier'] == 'entry' for p in kept):
        out['noEntryPhone'] = True
    return out
