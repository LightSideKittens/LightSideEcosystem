"""Comparisons of the arms of every case, per phone with an interval, and the absolute cost of every arm.

Inside a launch every arm of a case is measured in the same rounds, so each comparison is a list of round values per
launch: the log of arm A's slot median over arm B's, or for the positive control what the scheduler's busy loop adds
to the first arm as a fraction of the loop's cost at the calibrated clock (a difference, so no round is dropped). The main-thread CPU of a per-frame case is taken above the
in-launch reference arm (the empty or held frame of the same round). Warm-up rounds are left out (a one-shot case whose
result is the cold operation keeps round 0 alone), and so are rounds whose thermal status changed or ran hot; each
phone's point counts them."""
import math
import statistics
import zlib

import devices
import stats

PER_FRAME = ('cpu', 'render', 'gpu')
ONE_SHOT = ('sync', 'committed')
ABSOLUTE = ('cpu', 'render', 'gpu', 'canvas', 'objects', 'sync', 'committed', 'frames', 'triangles', 'draws')
NET = {'cpu'}


def measured(launch, gate=False):
    """The rounds a comparison reads. A cold-result case's result is round 0; its gate arms skip round 0, so gates read
    the rounds after it."""
    if launch.cold:
        return [r for r in launch.rounds if (r.index >= 1 if gate else r.index == 0) and not r.hot]
    return [r for r in launch.rounds if not r.warmup and not r.hot]


def arm_name(arm):
    """An arm's engine with its variant, e.g. `tmpTwoPass`."""
    return arm.engine + (arm.variant[:1].upper() + arm.variant[1:] if arm.variant else '')


def ours(arms):
    """The engine of the case's first arm, the product's default configuration (WP1 contract §7); every other engine in
    the case is a rival."""
    return next(a.engine for a in arms.values() if a.engine != 'reference' and not a.control)


def reference_of(arms):
    refs = [a.id for a in arms.values() if a.engine == 'reference' and not a.control]
    if len(refs) > 1:
        raise SystemExit(f'a case has {len(refs)} reference arms: {refs}')
    return refs[0] if refs else None


def slot(rnd, arm, metric, reference):
    v = (rnd.slots.get(arm) or {}).get(metric)
    if v is None or reference is None or metric not in NET:
        return v
    ref = (rnd.slots.get(reference) or {}).get(metric)
    return None if ref is None else v - ref


def round_values(launch, a, b, metric, scale, gate=False):
    """Round values of one launch and the number of rounds whose net value sat at or below the reference. A recovery
    value is what the positive arm adds over its twin divided by the loop's cost at the calibrated clock: per control
    slot in a one-shot case, per record in a frame case."""
    reference = reference_of(launch.arms)
    values, below = [], 0
    for rnd in measured(launch, gate):
        if scale == 'recovery':
            x, y = slot(rnd, a, metric, None), slot(rnd, b, metric, None)
            if x is None or y is None:
                continue
            price = (rnd.slots.get(a) or {}).get('controlAt') if launch.kind == 'oneShot' else launch.control_ms
            if not price:
                raise SystemExit(f'case {launch.case}: the positive control carries no controlMsAtCalibration')
            values.append((x - y) / price)
            continue
        else:
            x, y = slot(rnd, a, metric, reference), slot(rnd, b, metric, reference)
            if x is None or y is None:
                continue
        if x <= 0 or y <= 0:
            below += 1
        else:
            values.append(math.log(x / y))
    return values, below


def config(arm):
    return arm.engine, arm.threads, arm.style, arm.load


def meets(mine, theirs):
    """Whether a rival arm runs on the same kind of threads as an arm of ours: `single` meets `single`, `parallel`
    meets `parallel`, and a rival that declares none (TMP, the main thread alone) meets every arm of ours."""
    return theirs in ('', mine)


def rival_pairs(arms):
    """(arm of ours, rival arm or None, rival name) for every arm of ours and every rival configuration (engine and
    variant) of the same style and load. A rival that declares threads but has no arm meeting ours gives None: that
    comparison was not measured, and the rival's arm of the other kind never stands in for it."""
    base = [a for a in arms.values() if not a.control and a.engine != 'reference']
    own = ours(arms)
    rivals = {}
    for a in base:
        if a.engine != own:
            rivals.setdefault((arm_name(a), a.style, a.load), []).append(a)
    out = []
    for u in (a for a in base if a.engine == own):
        for (name, style, load), members in rivals.items():
            if (style, load) == (u.style, u.load):
                met = [r for r in members if meets(u.threads, r.threads)]
                out += [(u, r, name) for r in met] or [(u, None, name)]
    return out


def pairs(arms):
    """(kind, A, B, scale, metrics) of every comparison the arms of a case define; `metrics` None means all."""
    base = [a for a in arms.values() if not a.control and a.engine != 'reference']
    out = [('rival', u.id, r.id, 'ratio', None) for u, r, _ in rival_pairs(arms) if r]
    for a in base:
        for b in base:
            if (a.engine, a.threads, a.load) == (b.engine, b.threads, b.load) and (a.style, b.style) == ('styled', 'plain'):
                out.append(('effects', a.id, b.id, 'ratio', None))
    for c in (a for a in arms.values() if a.control):
        twin = [b for b in base if config(b) == config(c)]
        if len(twin) != 1:
            raise SystemExit(f'control arm {c.id} has {len(twin)} arms of the same configuration, needs exactly one')
        if c.control == 'aa':
            out.append(('aa', c.id, twin[0].id, 'ratio', None))
        elif c.control == 'positive':
            out.append(('positive', c.id, twin[0].id, 'recovery', ('cpu', 'committed')))
        elif c.control == 'gpuBaseline':
            out.append(('gpuBaseline', twin[0].id, c.id, 'ratio', ('gpu',)))
        else:
            raise SystemExit(f'control arm {c.id} has an unknown control {c.control!r}')
    return out


def metrics_of(launches):
    one_shot = any((rnd.slots.get(a) or {}).get('sync') is not None for l in launches for rnd in l.rounds for a in rnd.slots)
    return ONE_SHOT if one_shot else PER_FRAME


def arm_fields(arm):
    return {'engine': arm.engine, 'name': arm_name(arm), 'threads': arm.threads, 'style': arm.style, 'load': arm.load,
            'control': arm.control}


def hot_rounds(launch):
    return sum(1 for r in launch.rounds if r.hot and not r.warmup)


def point(device, launches_rounds, below, seed, aa_sigma, hot=0):
    entry = devices.phone(device)
    interval = stats.phone_interval(launches_rounds, seed, aa_sigma)
    interval.update({'device': device, 'label': entry['label'], 'tier': entry['tier'], 'group': entry['group'],
                     'belowReference': below, 'hotRounds': hot})
    return interval


def by_case(timing):
    cases = {}
    for launch in timing:
        cases.setdefault(launch.case, []).append(launch)
    for case, launches in cases.items():
        first = launches[0].arms
        for other in launches[1:]:
            if other.arms != first:
                raise SystemExit(f'case {case}: launches disagree on the arms ({other.device}, repeat {other.repeat})')
    return cases


def comparisons(timing, aa_sigma):
    """One row per case, comparison and metric with every phone's interval. `aa_sigma(device, metric)` is the A/A launch
    spread used when a phone has fewer than three launches."""
    rows = []
    for case, launches in sorted(by_case(timing).items()):
        arms = launches[0].arms
        for kind, a, b, scale, only in pairs(arms):
            for metric in (m for m in metrics_of(launches) if only is None or m in only):
                per_device = {}
                for launch in launches:
                    values, below = round_values(launch, a, b, metric, scale, kind in ('aa', 'positive'))
                    entry = per_device.setdefault(launch.device, [[], 0, 0])
                    if values:
                        entry[0].append(values)
                    entry[1] += below
                    entry[2] += hot_rounds(launch)
                points = [point(device, rounds, below, zlib.crc32(f'{case}|{a}|{b}|{metric}|{device}'.encode()),
                                aa_sigma(device, metric), hot)
                          for device, (rounds, below, hot) in sorted(per_device.items())]
                rows.append({'case': case, 'comparison': kind, 'a': a, 'b': b, 'aArm': arm_fields(arms[a]),
                             'bArm': arm_fields(arms[b]), 'metric': metric, 'scale': scale, 'points': points,
                             'workload': launches[0].workload})
    return rows


def ladder_groups(arms):
    groups = {}
    for a in arms.values():
        if not a.control and a.engine != 'reference':
            groups.setdefault((arm_name(a), a.threads, a.style), []).append(a)
    return {k: sorted(v, key=lambda a: a.load) for k, v in groups.items() if len({a.load for a in v}) > 1}


def ladders(timing, aa_sigma):
    """Per arm group of a case with several loads, the growth from the smallest load to the largest, the physical
    gate's input."""
    rows = []
    for case, launches in sorted(by_case(timing).items()):
        arms = launches[0].arms
        groups = ladder_groups(arms)
        if not groups:
            continue
        reference = reference_of(arms)
        for metric in metrics_of(launches):
            for key, members in groups.items():
                growth = {}
                for launch in launches:
                    logs = []
                    for rnd in measured(launch):
                        least = slot(rnd, members[0].id, metric, reference)
                        most = slot(rnd, members[-1].id, metric, reference)
                        if least is not None and most is not None and least > 0 and most > 0:
                            logs.append(math.log(most / least))
                    if logs:
                        growth.setdefault(launch.device, []).append(logs)
                rows.append({'case': case, 'comparison': 'growth', 'a': members[-1].id, 'b': members[0].id,
                             'aArm': arm_fields(members[-1]), 'bArm': arm_fields(members[0]), 'metric': metric,
                             'scale': 'ratio', 'loads': [m.load for m in members],
                             'points': [point(d, r, 0, zlib.crc32(f'{case}|growth|{key}|{metric}|{d}'.encode()),
                                              aa_sigma(d, metric)) for d, r in sorted(growth.items())],
                             'workload': launches[0].workload})
    return rows


def absolute(timing):
    """What every arm costs per phone: per launch the mean over measured rounds (CPU above the reference where there is
    one), then the median and range over launches; across phones the median, range and the dearest phone."""
    rows = []
    for case, launches in sorted(by_case(timing).items()):
        arms = launches[0].arms
        reference = reference_of(arms)
        for arm in arms.values():
            for metric in ABSOLUTE:
                per_device = {}
                for launch in launches:
                    values = [slot(r, arm.id, metric, reference if arm.engine != 'reference' else None) for r in measured(launch)]
                    values = [v for v in values if v is not None]
                    if values:
                        per_device.setdefault(launch.device, []).append(statistics.fmean(values))
                if not per_device:
                    continue
                points = []
                for device, values in sorted(per_device.items()):
                    entry = devices.phone(device)
                    points.append({'device': device, 'label': entry['label'], 'tier': entry['tier'], 'group': entry['group'],
                                   'value': statistics.median(values), 'min': min(values), 'max': max(values),
                                   'launches': len(values)})
                medians = [p['value'] for p in points]
                dearest = max(points, key=lambda p: p['value'])
                rows.append({'case': case, 'arm': arm.id, 'armFields': arm_fields(arm), 'metric': metric,
                             'net': metric in NET and reference is not None and arm.engine != 'reference',
                             'points': points, 'median': statistics.median(medians), 'min': min(medians),
                             'max': max(medians), 'dearestDevice': dearest['device'], 'workload': launches[0].workload})
    return rows
