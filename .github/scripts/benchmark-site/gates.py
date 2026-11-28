"""Gates a number passes before the page may show it. A failing gate never drops a number silently: a phone that fails a
device gate stays in its row with the reason it was excluded, a row that fails a row gate keeps its phones and loses
its summary, and a run without the control arms the gates need publishes nothing.

- A/A: two arms of one configuration must give an interval that lies wholly inside the equivalence band, per phone
  and metric: a pipeline that cannot rule out a difference of the band's size between identical arms cannot claim one
  between engines. Its launch spread also widens phones with fewer than three launches.
- Positive control: the scheduler's busy loop added to the first arm must be recovered within 10 % of its cost at the
  calibrated clock (`controlMsAtCalibration`), per phone, for main-thread CPU of frames and CPU of one-shot operations.
- GPU counter: a styled arm must cost measurably more GPU than the same arm with the default material, per phone.
- Physical: per engine, styled is never measurably cheaper than plain, and the largest load of a ladder never measurably
  cheaper than the smallest.
- Look: every engine of a styled comparison passed the look calibration (residuals within their tolerances).
- Phones: a row needs intervals from at least three phones that passed."""
import math
import statistics

import stats

MIN_PHONES = 3
POSITIVE_TOLERANCE = 0.10
CONTROL_KINDS = ('aa', 'positive', 'gpuBaseline')
PHYSICAL_KINDS = ('effects', 'growth')


def band(group):
    return stats.BANDS[group]


def aa(rows):
    """Per (device, metric): pass, reason and the pooled launch spread of the A/A arms."""
    results, spread = {}, {}
    for row in rows:
        if row['comparison'] != 'aa':
            continue
        for p in row['points']:
            key = (p['device'], row['metric'])
            values = p.get('launchValues') or []
            if len(values) > 1:
                mean = statistics.fmean(values)
                acc = spread.setdefault(key, [0.0, 0])
                acc[0] += sum((v - mean) ** 2 for v in values)
                acc[1] += len(values) - 1
            if p.get('lo') is None:
                verdict = (False, f'A/A of {row["case"]} has no interval: {p["method"]}')
            elif stats.classify(p['lo'], p['hi'], band(p['group'])) != 'equivalent':
                verdict = (False, f'A/A of {row["case"]} is not within ±{band(p["group"]) * 100:.0f} %: '
                                  f'{math.exp(p["lo"]):.3f}-{math.exp(p["hi"]):.3f}')
            else:
                verdict = (True, None)
            previous = results.get(key)
            if previous is None or previous[0]:
                results[key] = verdict
    return {key: {'pass': ok, 'reason': reason,
                  'sigma': math.sqrt(spread[key][0] / spread[key][1]) if spread.get(key, (0, 0))[1] else None}
            for key, (ok, reason) in results.items()}


def positive(rows):
    """Per (device, metric): whether the busy loop added to the first arm was recovered within 10 % of its calibrated
    cost."""
    results = {}
    for row in rows:
        if row['comparison'] != 'positive':
            continue
        for p in row['points']:
            key = (p['device'], row['metric'])
            if p.get('lo') is None:
                verdict = (False, f'positive control has no interval: {p["method"]}')
            elif abs(p['center'] - 1) > POSITIVE_TOLERANCE:
                verdict = (False, f'positive control recovered {p["center"] * 100:.0f} % of the loop '
                                  f'({p["lo"] * 100:.0f}-{p["hi"] * 100:.0f} %): measurement noise, or a clock that is '
                                  f'not the calibrated one')
            else:
                verdict = (True, None)
            if results.get(key, (True,))[0]:
                results[key] = verdict
    return {key: {'pass': ok, 'reason': reason} for key, (ok, reason) in results.items()}


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


def device_gate(point, metric, aa_results, positive_results, gpu_results):
    """The reason a phone's point may not enter a summary, or None."""
    verdict = aa_results.get((point['device'], metric))
    if verdict is None:
        return 'no A/A control for this metric on this phone'
    if not verdict['pass']:
        return verdict['reason']
    if metric in ('cpu', 'committed'):
        verdict = positive_results.get((point['device'], metric))
        if verdict is None:
            return 'no positive control on this phone'
        if not verdict['pass']:
            return verdict['reason']
    if metric == 'gpu':
        verdict = gpu_results.get(point['device'])
        if verdict is None:
            return 'no GPU counter control on this phone'
        if not verdict['pass']:
            return verdict['reason']
    return None


def apply(rows, aa_results, positive_results, gpu_results, physical_violations, look_results):
    """Marks every non-control row: excluded phones with their reason, `blocked` reasons, and the summary across the
    phones that passed (absent when blocked)."""
    for row in rows:
        if row['comparison'] in CONTROL_KINDS:
            continue
        blocked = []
        lower, higher = direction_words(row)
        for p in row['points']:
            p['excluded'] = device_gate(p, row['metric'], aa_results, positive_results, gpu_results)
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


def run_level(aa_results, positive_results, rows):
    """Reasons the whole run may not publish: control arms missing altogether."""
    reasons = []
    if not aa_results:
        reasons.append('the run has no A/A arms')
    if any(row['metric'] == 'cpu' for row in rows) and not positive_results:
        reasons.append('the run has no positive control arm')
    return reasons
