"""Gates a number passes before the page may show it. A failing gate never drops a number silently: a phone that fails a
device gate stays in its row with the reason it was excluded, a row that fails a row gate keeps its phones and loses
its summary, and a run without the control arms the gates need publishes nothing.

- CPU, per phone and case (WP1 v1.8, v1.10), over the rounds from 1 on of every timing launch of the case on the
  phone, pooled, at least 5 of them: the median A/A ratio (net CPU of frames, operation CPU of one-shots) lies within
  0.97-1.03, and the median recovery of the busy loop added to the control's base arm, (control - base) / its own time
  in the slot, within 0.90-1.10; rounds that recovered outside 0.5-1.5 are counted beside it, and a few such rounds
  cannot flip the median. A case
  without a control (all arms parallel) passes on a phone when every case with one passes there in the same run. A
  failed phone shows no CPU comparison of that case and enters no summary of it.
- CPU over all threads (`processCpuMs`, WP1 v1.9), per phone and case: the CPU gate passes, the busy loop on the main
  thread is recovered in the process total within 0.90-1.10, and every launch ran under the hint session, which keeps
  every thread at one clock; a case without a control follows the phone's controlled cases.
- GPU, per phone and case: the median GPU A/A ratio lies within 0.97-1.03, and on that phone a styled arm costs
  measurably more GPU than the same arm with the default material (the counter sees the effects).
- The A/A launch spread also widens phones with fewer than three launches.
- Physical: per engine, styled is never measurably cheaper than plain, and the largest load of a ladder never measurably
  cheaper than the smallest.
- Look: every engine of a styled comparison passed the look calibration (residuals within their tolerances).
- Phones: a row needs intervals from at least three phones that passed."""
import math
import statistics

import compare
import stats

MIN_PHONES = 3
AA_RANGE = (0.97, 1.03)
RECOVERY_RANGE = (0.90, 1.10)
DISTURBED_RANGE = (0.5, 1.5)
MIN_ROUNDS = 5
CPU_METRICS = ('cpu', 'render', 'sync', 'committed')
GATED = {'processCpu': 'process', 'gpu': 'gpu'}
CONTROL_KINDS = ('aa', 'positive', 'gpuBaseline')
PHYSICAL_KINDS = ('effects', 'growth')


def band(group):
    return stats.BANDS[group]


def aa_spread(rows):
    """Per (device, metric): the pooled launch-to-launch spread of the A/A log ratios, or None with a single launch."""
    spread = {}
    for row in rows:
        if row['comparison'] != 'aa':
            continue
        for p in row['points']:
            values = p.get('launchValues') or []
            if len(values) > 1:
                mean = statistics.fmean(values)
                acc = spread.setdefault((p['device'], row['metric']), [0.0, 0])
                acc[0] += sum((v - mean) ** 2 for v in values)
                acc[1] += len(values) - 1
    return {key: math.sqrt(total / df) for key, (total, df) in spread.items() if df}


def clock_text(modes):
    def fill(f):
        return f', fill {f["loadMs"]:g} ms per frame' if 'loadMs' in f else f', no fill ({f["reason"]})'
    return '; '.join(m['mode'] + (f' ({m["reason"]})' if m.get('reason') else '') + fill(m['fill']) for m in modes)


def case_gates(timing, gpu_results):
    """Per (device, case): the `cpu`, `process` (CPU over all threads, `processCpuMs`) and `gpu` verdicts with the
    medians they rest on, the clock modes, the clock scale (median `controlBusyMs / controlMsAtCalibration`) and the
    threads the clock session covered."""
    pooled = {}
    for case, launches in compare.by_case(timing).items():
        pairs = {kind: (a, b) for kind, a, b, _, _ in compare.pairs(launches[0].arms) if kind in ('aa', 'positive')}
        for launch in launches:
            cpu = 'committed' if launch.kind == 'oneShot' else 'cpu'
            entry = pooled.setdefault((launch.device, case), {
                'aa': {}, 'recovery': [], 'processRecovery': [], 'scale': [], 'runs': set(), 'modes': [],
                'threads': set(), 'unavailable': set(), 'cpu': cpu, 'controlled': 'positive' in pairs,
                'frames': launch.kind == 'frames'})
            entry['runs'].add(launch.run)
            entry['threads'].add(launch.clock_threads)
            mode = {k: launch.clock[k] for k in ('mode', 'reason', 'fill') if launch.clock.get(k)}
            if mode not in entry['modes']:
                entry['modes'].append(mode)
            if launch.process_cpu['status'] != 'measured':
                entry['unavailable'].add(launch.process_cpu['reason'])
            if 'aa' in pairs:
                for metric in (cpu, 'gpu') if launch.kind == 'frames' else (cpu,):
                    entry['aa'].setdefault(metric, []).extend(
                        compare.round_values(launch, *pairs['aa'], metric, 'ratio', True)[0])
            if 'positive' in pairs:
                control, base = pairs['positive']
                entry['recovery'] += compare.round_values(launch, control, base, cpu, 'recovery', True)[0]
                entry['processRecovery'] += compare.round_values(launch, control, base, 'processCpu', 'recovery', True)[0]
                entry['scale'] += [r.slots[control]['controlBusy'] / r.slots[control]['controlAt']
                                   for r in compare.measured(launch, True)
                                   if control in r.slots and r.slots[control]['controlAt']]

    def aa_verdict(entry, metric):
        values = entry['aa'].get(metric) or []
        if len(values) < MIN_ROUNDS:
            return None, f'{len(values)} A/A rounds, {MIN_ROUNDS} needed'
        ratio = math.exp(statistics.median(values))
        inside = AA_RANGE[0] <= ratio <= AA_RANGE[1]
        return ratio, None if inside else f'A/A {ratio:.3f}, outside {AA_RANGE[0]:.2f}-{AA_RANGE[1]:.2f}'

    def recovery_verdict(values, what):
        if len(values) < MIN_ROUNDS:
            return None, f'{len(values)} rounds of the positive control in {what}, {MIN_ROUNDS} needed'
        median = statistics.median(values)
        inside = RECOVERY_RANGE[0] <= median <= RECOVERY_RANGE[1]
        return median, None if inside else (f'the busy loop recovered {median * 100:.0f} % in {what} '
                                            f'({RECOVERY_RANGE[0]:.2f}-{RECOVERY_RANGE[1]:.2f} needed)')

    def disturbed(values):
        return sum(1 for v in values if not DISTURBED_RANGE[0] <= v <= DISTURBED_RANGE[1])

    out = {}
    for (device, case), e in pooled.items():
        aa_cpu, cpu_reason = aa_verdict(e, e['cpu'])
        recovery, reason = recovery_verdict(e['recovery'], 'main-thread CPU')
        process_recovery, process_reason = recovery_verdict(e['processRecovery'], 'CPU over all threads')
        if e['controlled']:
            cpu_reason = cpu_reason or reason
        else:
            process_reason = None
        if e['unavailable']:
            process_reason = '; '.join(sorted(e['unavailable']))
        elif any(m['mode'] != 'hint' for m in e['modes']):
            process_reason = 'CPU over all threads counts only under the hint session, which covers every thread'
        aa_gpu, gpu_reason = aa_verdict(e, 'gpu') if e['frames'] else (None, None)
        counter = gpu_results.get(device)
        if e['frames'] and not gpu_reason:
            gpu_reason = 'no GPU counter control on this phone' if counter is None else counter['reason']
        out[(device, case)] = {'device': device, 'case': case, 'runs': sorted(e['runs']), 'controlled': e['controlled'],
                               'aa': aa_cpu, 'recovery': recovery, 'processRecovery': process_recovery, 'gpuAa': aa_gpu,
                               'rounds': len(e['recovery']), 'disturbed': disturbed(e['recovery']),
                               'processDisturbed': disturbed(e['processRecovery']),
                               'clockScale': statistics.median(e['scale']) if e['scale'] else None,
                               'clock': e['modes'], 'clockThreads': sorted(e['threads']),
                               'cpu': {'pass': cpu_reason is None, 'reason': cpu_reason},
                               'process': {'pass': process_reason is None, 'reason': process_reason},
                               'gpu': {'pass': gpu_reason is None, 'reason': gpu_reason} if e['frames'] else None}
    for (device, case), g in out.items():
        if g['controlled']:
            continue
        mates = [m for m in out.values() if m['device'] == device and m['controlled'] and set(m['runs']) & set(g['runs'])]
        for kind in ('cpu', 'process'):
            failed = sorted(m['case'] for m in mates if not m[kind]['pass'])
            if not g[kind]['pass']:
                continue
            if not mates:
                g[kind] = {'pass': False, 'reason': 'no case with a positive control ran on this phone in this run'}
            elif failed:
                g[kind] = {'pass': False, 'reason': f'cases with a positive control failed on this phone: {", ".join(failed)}'}
    for g in out.values():
        if not g['cpu']['pass']:
            g['process'] = {'pass': False, 'reason': g['cpu']['reason']}
        for kind in ('cpu', 'process'):
            if not g[kind]['pass']:
                g[kind]['reason'] += f'; clock {clock_text(g["clock"])}'
    return out


def clocks(gates_by_case):
    """Per device: the clock modes of its launches and the clock scale over every control slot."""
    phones = {}
    for g in gates_by_case.values():
        phone = phones.setdefault(g['device'], {'modes': [], 'scales': []})
        phone['modes'] += [m for m in g['clock'] if m not in phone['modes']]
        if g['clockScale'] is not None:
            phone['scales'].append(g['clockScale'])
    return {d: {'modes': p['modes'], 'scale': statistics.median(p['scales']) if p['scales'] else None}
            for d, p in sorted(phones.items())}


def gpu_counter(rows):
    """Per device: whether the GPU counter sees the cost of effects over the default material."""
    results = {}
    for row in rows:
        if row['comparison'] != 'gpuBaseline' or row['metric'] != 'gpu':
            continue
        for p in row['points']:
            ok = p.get('lo') is not None and p['lo'] > math.log1p(band(p['group']))
            if results.get(p['device'], (True,))[0]:
                results[p['device']] = (ok, None if ok else f'GPU counter does not see the effects in {row["case"]}')
    return {device: {'pass': ok, 'reason': reason} for device, (ok, reason) in results.items()}


def physical(rows):
    """(case, arm name, threads, metric) keys whose styled arm ran measurably cheaper than plain, or whose largest load ran
    measurably cheaper than the smallest, with the phones that showed it."""
    violations = {}
    for row in rows:
        if row['comparison'] not in PHYSICAL_KINDS:
            continue
        phones = [p['device'] for p in row['points'] if p.get('hi') is not None and p['hi'] < 0]
        if phones:
            arm = row['aArm']
            what = 'styled cheaper than plain' if row['comparison'] == 'effects' else 'larger load cheaper than smaller'
            violations[(row['case'], arm['name'], arm['threads'], row['metric'])] = f'{what} on {", ".join(phones)}'
    return violations


def look(calibration):
    """Per engine: whether every reported size on every phone lies within its tolerances. `calibration` maps device to
    the run's `lookCalibration` record: {engines: {engine: {sizes: {size: {measure: {residual, tolerance}}}}}}."""
    results = {}
    for device, record in calibration.items():
        for engine, entry in (record.get('engines') or {}).items():
            for size, measures in (entry.get('sizes') or {}).items():
                for measure, m in measures.items():
                    if abs(m['residual']) > m['tolerance']:
                        results.setdefault(engine, []).append(
                            f'{measure} at {size} on {device}: {m["residual"]:+.3g} beyond ±{m["tolerance"]:.3g}')
                results.setdefault(engine, [])
    return {engine: {'pass': not failures, 'reason': '; '.join(failures) or None} for engine, failures in results.items()}


def device_gate(point, case, metric, gates_by_case):
    """The reason a phone's point of a case may not enter a summary, or None."""
    kind = 'cpu' if metric in CPU_METRICS else GATED.get(metric)
    if kind is None:
        return None
    verdict = (gates_by_case.get((point['device'], case)) or {}).get(kind)
    if verdict is None:
        return f'no {kind} gate for this case on this phone'
    return None if verdict['pass'] else verdict['reason']


def apply(rows, gates_by_case, physical_violations, look_results):
    """Marks every non-control row: excluded phones with their reason, `blocked` reasons, and the summary across the
    phones that passed (absent when blocked)."""
    for row in rows:
        if row['comparison'] in CONTROL_KINDS:
            continue
        blocked = []
        lower, higher = direction_words(row)
        for p in row['points']:
            p['excluded'] = device_gate(p, row['case'], row['metric'], gates_by_case)
            if not p['excluded'] and p.get('belowReference'):
                p['excluded'] = (f'net work at or below the in-launch reference in {p["belowReference"]} rounds: '
                                 f'the ratio is not defined, see the absolute cost')
            if not p['excluded']:
                p['claim'] = stats.wording(p, band(p['group']), lower, higher)
        for arm in (row['aArm'], row['bArm']):
            violation = physical_violations.get((row['case'], arm['name'], arm['threads'], row['metric']))
            if violation:
                blocked.append(f'physical gate: {violation}')
            if arm.get('style') == 'styled':
                verdict = look_results.get(arm.get('engine'))
                if verdict is None:
                    blocked.append(f'look gate: no calibration for {arm.get("engine")}')
                elif not verdict['pass']:
                    blocked.append(f'look gate: {verdict["reason"]}')
        kept = [p for p in row['points'] if not p['excluded'] and p.get('lo') is not None]
        if len(kept) < MIN_PHONES:
            blocked.append(f'{len(kept)} phones passed the gates, {MIN_PHONES} needed')
        row['blocked'] = sorted(set(blocked))
        row['summary'] = None if row['blocked'] else stats.across_phones(kept, band, lower, higher)
        if row['summary'] is not None and not any(p['tier'] == 'entry' for p in kept):
            row['summary']['noEntryPhone'] = True


def direction_words(row):
    if row['comparison'] == 'effects':
        return 'cheaper with effects', 'dearer with effects'
    if row['comparison'] == 'growth':
        return 'cheaper at the larger load', 'dearer at the larger load'
    return 'faster', 'slower'


def run_level(rows):
    """Reasons the whole run may not publish: control arms missing altogether."""
    reasons = []
    if not any(row['comparison'] == 'aa' for row in rows):
        reasons.append('the run has no A/A arms')
    if not any(row['comparison'] == 'positive' for row in rows):
        reasons.append('the run has no positive control arm')
    return reasons
