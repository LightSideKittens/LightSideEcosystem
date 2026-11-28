"""The page's data, `page.json` schema 2 (wp/WP5-page-spec.md §8), from the analysis of one full check.

Rows are keyed by the page's logical case names: `PAGE_CASES` maps each harness case id to its page case and its tier,
or, for a case with several loads, to {load: tier}; a load without a tier stays off the page. The map
lives here so the page never sees a harness id. A harness case missing from the map stays out of the page and is named
in the reducer's summary. Every number in the file comes from the analysis of the same run set."""
import math
import os
import statistics
import sys

import compare
import stats

PAGE_CASES = {}
METRICS = {'cpu': ('cpuMain', 'ms'), 'committed': ('cpuOp', 'ms'), 'gpu': ('gpu', 'ms'), 'render': ('render', 'ms'),
           'frames': ('framesToPixels', 'frames'), 'triangles': ('triangles', 'count'), 'draws': ('draws', 'count'),
           'objects': ('objects', 'count')}
VALUE_ONLY = ('frames', 'triangles', 'draws', 'objects')
THREAD_WORDS = {'single': 'on one thread', 'parallel': 'in its parallel default mode'}


def mean_cost(values):
    """Geometric mean of positive costs; the arithmetic mean where a cost is zero (counts)."""
    values = [v for v in values if v is not None]
    if not values:
        return None
    return math.exp(statistics.fmean(math.log(v) for v in values)) if min(values) > 0 else statistics.fmean(values)


def tier_of(case, load):
    tiers = PAGE_CASES[case][1]
    return tiers.get(load) if isinstance(tiers, dict) else tiers


def rival_name(arm):
    return compare.arm_name(arm)


def row_id(*parts):
    return '.'.join(str(p) for p in parts if p)


def absolute_index(absolute):
    return {(r['case'], r['arm'], r['metric']): {p['device']: p for p in r['points'] if not p.get('excluded')}
            for r in absolute}


def values(index, case, a, b, metric, unit, names=('unitext', 'rival')):
    mine, theirs = index.get((case, a, metric), {}), index.get((case, b, metric), {})
    return {names[0]: mean_cost(p['value'] for p in mine.values()),
            names[1]: mean_cost(p['value'] for p in theirs.values()), 'unit': unit}, mine, theirs


def blocked_of(reasons):
    if not reasons:
        return None
    first = reasons[0]
    gate = 'look' if first.startswith('look gate') else 'physical' if first.startswith('physical gate') else 'phones'
    return {'gate': gate, 'reasonText': '; '.join(reasons)}


def unmeasured_rows(comparisons, arms_by_case):
    """A row per metric for every arm of ours whose rival declares threads but ran no arm meeting it
    (`compare.rival_pairs`): the comparison was not measured, and the rival's arm of the other kind does not stand in."""
    out = []
    for case, arms in sorted(arms_by_case.items()):
        if case not in PAGE_CASES:
            continue
        measured = [r for r in comparisons if r['case'] == case and r['comparison'] == 'rival' and r['metric'] in METRICS]
        for u, r, name in compare.rival_pairs(arms):
            tier = tier_of(case, u.load)
            if r or tier is None:
                continue
            for m in sorted({row['metric'] for row in measured}):
                metric = METRICS[m][0]
                text = f'{name} ran no arm {THREAD_WORDS.get(u.threads, "on matching threads")} in this run'
                out.append({'id': row_id(PAGE_CASES[case][0], tier, metric, u.threads, u.style, name),
                            'case': PAGE_CASES[case][0], 'tier': tier,
                            'load': next(((row['workload'].get(u.id) or {}).get('load') for row in measured), None),
                            'metric': metric, 'threads': u.threads or None, 'arm': u.style, 'rival': name,
                            'rivalThreads': u.threads or None, 'comparable': None, 'estimate': None, 'range': None,
                            'claim': stats.claim('unmeasured', None, None, text),
                            'counts': {'n': 0, 'faster': 0, 'slower': 0, 'equivalent': 0, 'inconclusive': 0},
                            'hw': None, 'worst': None, 'values': None, 'phones': [],
                            'blocked': None})
    return out


def rows(comparisons, absolute, arms_by_case, unmapped):
    index = absolute_index(absolute)
    out = []
    for r in comparisons:
        if r['comparison'] not in ('rival', 'effects') or r['metric'] not in METRICS:
            continue
        if r['case'] not in PAGE_CASES:
            unmapped.add(r['case'])
            continue
        page_case = PAGE_CASES[r['case']][0]
        arms = arms_by_case[r['case']]
        a, b = arms[r['a']], arms[r['b']]
        tier = tier_of(r['case'], a.load)
        if tier is None:
            continue
        metric, unit = METRICS[r['metric']]
        effects = r['comparison'] == 'effects'
        rival = a.engine if effects else rival_name(b)
        kept = [p for p in r['points'] if not p.get('excluded') and p.get('lo') is not None]
        summary = r.get('summary') or {}
        worst = max(kept, key=lambda p: p['center']) if kept else None
        names = ('styled', 'plain') if effects else ('unitext', 'rival')
        vals, mine, theirs = values(index, r['case'], r['a'], r['b'], r['metric'], unit, names)
        out.append({
            'id': row_id(page_case, tier, metric, a.threads, 'effects' if effects else a.style, rival),
            'case': page_case, 'tier': tier, 'load': (r['workload'].get(r['a']) or {}).get('load'), 'metric': metric,
            'threads': a.threads or None, 'arm': 'effects' if effects else a.style, 'rival': rival,
            'rivalThreads': None if effects else b.threads or None, 'comparable': a.comparable and b.comparable,
            'estimate': summary.get('geomean'), 'range': [summary['min'], summary['max']] if summary else None,
            'claim': summary.get('claim'),
            'counts': {'n': len(kept), **{c: (summary.get('classes') or {}).get(c, 0)
                                          for c in ('faster', 'slower', 'equivalent', 'inconclusive')}},
            'hw': max(math.exp((p['hi'] - p['lo']) / 2) - 1 for p in kept) if kept else None,
            'worst': {'device': worst['device'], 'estimate': math.exp(worst['center']),
                      'ci': [math.exp(worst['lo']), math.exp(worst['hi'])], 'class': worst.get('class')} if worst else None,
            'values': vals,
            'phones': [{'device': p['device'], 'estimate': math.exp(p['center']) if p.get('center') is not None else None,
                        'ci': [math.exp(p['lo']), math.exp(p['hi'])] if p.get('lo') is not None else None,
                        'class': p.get('class'), 'claim': p.get('claim'), 'launches': p['launches'],
                        'excluded': p.get('excluded'),
                        'values': {names[0]: (mine.get(p['device']) or {}).get('value'),
                                   names[1]: (theirs.get(p['device']) or {}).get('value')}} for p in r['points']],
            'blocked': blocked_of(r.get('blocked'))})
    for case, arms in sorted(arms_by_case.items()):
        if case not in PAGE_CASES:
            continue
        page_case = PAGE_CASES[case][0]
        for kind, a_id, b_id, _, _ in compare.pairs(arms):
            if kind != 'rival':
                continue
            a, b = arms[a_id], arms[b_id]
            tier = tier_of(case, a.load)
            if tier is None:
                continue
            for m in VALUE_ONLY:
                if (case, a_id, m) not in index:
                    continue
                metric, unit = METRICS[m]
                vals, mine, theirs = values(index, case, a_id, b_id, m, unit)
                out.append({'id': row_id(page_case, tier, metric, a.threads, a.style, rival_name(b)), 'case': page_case,
                            'tier': tier, 'metric': metric, 'threads': a.threads or None, 'arm': a.style,
                            'rival': rival_name(b), 'rivalThreads': b.threads or None,
                            'comparable': a.comparable and b.comparable, 'values': vals,
                            'phones': [{'device': d, 'values': {'unitext': mine[d]['value'],
                                                                'rival': (theirs.get(d) or {}).get('value')}}
                                       for d in sorted(mine)], 'blocked': None})
    out += unmeasured_rows(comparisons, arms_by_case)
    ids = [r['id'] for r in out]
    twice = sorted({i for i in ids if ids.count(i) > 1})
    if twice:
        raise SystemExit(f'page rows share ids {twice}: two rival arms of one case need a variant in armInfo')
    return out


def memory_rows(twins, arms_by_case, unmapped):
    parts_of = {'android': (('sum', 'steady'), ('dirty', 'dirty'), ('gpu', 'gpu'), ('peak', 'peak')),
                'ios': (('steady', 'steady'), ('peak', 'peak'))}
    grouped = {}
    for t in twins:
        if t['case'] not in PAGE_CASES:
            unmapped.add(t['case'])
            continue
        grouped.setdefault((t['case'], t['group'], t['a'], t['b']), {})[t['quantity']] = t
    out = []
    for (case, group, _, _), quantities in sorted(grouped.items()):
        any_twin = quantities[next(iter(quantities))]
        page_case, tier = PAGE_CASES[case][0], tier_of(case, any_twin['load'])
        if tier is None:
            continue
        parts, flags, excluded = {}, [], set()
        for name, q in parts_of[group]:
            t = quantities.get(q)
            if not t:
                continue
            s = t['summary']
            for e in s.get('excluded', []):
                excluded.add(e['device'])
                flags.append({'device': e['device'], 'part': name, 'text': e['reason']})
            if s['class'] == 'unmeasured':
                parts[name] = {'class': 'unmeasured', 'text': s['sentence']}
                continue
            parts[name] = {'unitext': s['unitext'], 'rival': s['rival'], 'diff': s['medianDifference'],
                           'range': [s['minDifference'], s['maxDifference']],
                           'signs': {'n': s['phones'], 'higher': s['separatedHigher'], 'lower': s['separatedLower'],
                                     'within': s['within']},
                           'spread': s['spread'], 'class': s['class']}
        out.append({'id': row_id('mem', page_case, tier, group, any_twin['threads'], any_twin['rivalName']),
                    'case': page_case, 'tier': tier, 'platform': group, 'threads': any_twin['threads'] or None,
                    'rival': any_twin['rivalName'], 'rivalThreads': any_twin['rivalThreads'] or None, 'parts': parts,
                    'flags': flags, 'excludedDevices': sorted(excluded)})
    ids = [r['id'] for r in out]
    twice = sorted({i for i in ids if ids.count(i) > 1})
    if twice:
        raise SystemExit(f'memory rows share ids {twice}: two memory participants of one case describe alike')
    return out


def cycles_row(special):
    """The leak check (`memory.cycles`): per phone the dirty memory each create-and-destroy cycle leaves, with text and
    without it, KiB; the median across phones."""
    per_phone = {}
    for s in special:
        if s['variant'] != 'cycles':
            continue
        text, empty = s['record']['growthPerCycleBytes'].get('dirty'), s['record']['emptyGrowthPerCycleBytes'].get('dirty')
        if text is not None and empty is not None:
            per_phone.setdefault(s['device'], []).append((text / 1024, empty / 1024))
    if not per_phone:
        return None
    phones = [{'device': d, 'text': statistics.fmean(v[0] for v in values), 'empty': statistics.fmean(v[1] for v in values),
               'launches': len(values)} for d, values in sorted(per_phone.items())]
    return {'id': 'mem.cycles', 'perCycleKb': {'text': statistics.median(p['text'] for p in phones),
                                               'empty': statistics.median(p['empty'] for p in phones)},
            'phones': phones}


def font_file(font):
    """The name of a glyph case's font file before its extension and any `[axes]` part."""
    return font['file'].rsplit('.', 1)[0].split('[')[0] if font.get('file') else None


def rasterized(launch):
    """Every character the arms of a glyph case rasterized."""
    return {c for w in launch.workload.values() if isinstance(w, dict) for c in (w.get('characters') or '')}


def glyph_key(arm):
    """An arm's key in the ladder: its name with the threads it declares, e.g. `uitkSingle`, `tmp`."""
    return rival_name(arm) + arm.threads.capitalize()


def glyph(absolute, arms_by_case, timing):
    cases = {}
    for launch in timing:
        if launch.font:
            cases.setdefault(launch.case, launch.font)
    index = absolute_index(absolute)
    ladder, bands = [], {}
    for case, font in cases.items():
        arms = arms_by_case[case]
        band = font.get('band')
        if band:
            bands[font['script']] = {'script': font['script'], 'realistic': [band['min'], band['max']], 'source': band.get('source')}
        loads = sorted({a.load for a in arms.values() if a.engine != 'reference' and not a.control})
        points = []
        for n in loads:
            point = {'n': n, 'cpuMs': {}, 'frames': {}}
            for a in arms.values():
                if a.load != n or a.control or a.engine == 'reference':
                    continue
                key = glyph_key(a)
                point['cpuMs'][key] = mean_cost(p['value'] for p in index.get((case, a.id, 'committed'), {}).values())
                point['frames'][key] = mean_cost(p['value'] for p in index.get((case, a.id, 'frames'), {}).values())
            points.append(point)
        tier = next(a.style for a in arms.values() if a.engine != 'reference')
        ladder.append({'case': case, 'font': font_file(font), 'name': font['name'], 'script': font['script'], 'tier': tier,
                       'points': points})
    return {'ladder': ladder, 'bands': list(bands.values())}


def rivals(timing):
    """The rival settings the arms report (`workload[arm].rival`), per rival name, each distinct setting once."""
    out = {}
    for launch in timing:
        for arm_id, arm in launch.arms.items():
            setting = (launch.workload.get(arm_id) or {}).get('rival')
            if setting and setting not in out.setdefault(rival_name(arm), []):
                out[rival_name(arm)].append(setting)
    return out


def single(values, what):
    values = {v for v in values if v is not None}
    if len(values) != 1:
        raise SystemExit(f'the run set holds {len(values)} values for {what}: {sorted(map(str, values))}')
    return values.pop()


def run_block(build, check, gate_report, page_rows):
    timing = check.timing
    per_phone = {}
    for l in timing:
        per_phone.setdefault(l.suite, {}).setdefault((l.case, l.device), 0)
        per_phone[l.suite][(l.case, l.device)] += 1
    suites = sorted(per_phone)

    def per_suite(key, what):
        return {s: single((l.settings.get(key) for l in timing if l.suite == s), f'{what} in {s}') for s in suites}
    clock = single((l.clock.get('mode', 'none') for l in timing), 'the clock mode')
    gates = [
        {'id': 'aa', 'status': 'pass' if all(g['pass'] for g in gate_report['aa']) else 'fail',
         'detail': {'failed': [g for g in gate_report['aa'] if not g['pass']],
                    'sigma': {f'{g["device"]}|{g["metric"]}': g['sigma'] for g in gate_report['aa']}},
         'text': f'{sum(g["pass"] for g in gate_report["aa"])} of {len(gate_report["aa"])} phone and metric A/A checks inside the band'},
        {'id': 'positiveControl', 'status': 'pass' if all(g['pass'] for g in gate_report['positive']) else 'fail',
         'detail': {'failed': [g for g in gate_report['positive'] if not g['pass']]},
         'text': f'the busy loop recovered within 10 % on {sum(g["pass"] for g in gate_report["positive"])} of {len(gate_report["positive"])} phones'},
        {'id': 'counters', 'status': 'pass' if all(g['pass'] for g in gate_report['gpuCounter']) else 'fail',
         'detail': {'failed': [g for g in gate_report['gpuCounter'] if not g['pass']]},
         'text': f'the GPU counter saw the effects on {sum(g["pass"] for g in gate_report["gpuCounter"])} of {len(gate_report["gpuCounter"])} phones'},
        {'id': 'physical', 'status': 'fail' if gate_report['physical'] else 'pass', 'detail': {'violations': gate_report['physical']},
         'text': f'{len(gate_report["physical"])} case and engine pairs measured cheaper with effects or at a larger load'},
        {'id': 'look', 'status': 'pass' if gate_report['look'] and all(g['pass'] for g in gate_report['look']) else 'fail',
         'detail': {'engines': gate_report['look']}, 'text': 'look calibration of every engine within its tolerances'},
    ]
    excluded = [{'device': g['device'], 'metric': g.get('metric', 'gpu'), 'reason': g['reason']}
                for key in ('aa', 'positive', 'gpuCounter') for g in gate_report[key] if not g['pass']]
    phones = check.phones.values()
    return {
        'ids': build['runs'], 'date': max(build['measured']) if build['measured'] else None,
        'unitext': build['unitext'], 'core': build['core'], 'host': build['host'],
        'unity': single(build['unity'], 'the Unity version'),
        'pipeline': single((p['pipeline'] for p in phones), 'the render pipeline'),
        'build': single((f'{p["backend"]} {"development" if p["debug"] else "release"}' for p in phones), 'the player build'),
        'launchesPerPhone': {s: statistics.median(per_phone[s].values()) for s in suites},
        'roundsPerLaunch': per_suite('rounds', 'rounds per launch'),
        'settleFrames': per_suite('settleFrames', 'settle frames'),
        'measuredFrames': per_suite('measuredFrames', 'measured frames'),
        'clock': {'mode': clock, 'perDevice': {l.device: l.clock for l in timing if l.clock.get('mode', 'none') != 'none'}},
        'bands': dict(stats.BANDS), 'gates': gates, 'excluded': excluded, 'rivals': rivals(timing),
        'coverage': {'uitkFrames': any(r['rival'].startswith('uitk') and r['metric'] == 'cpuMain' and r.get('estimate')
                                       is not None for r in page_rows)},
        'rawData': None, 'rawTables': 'raw/',
        'reducer': {'commit': os.environ.get('GITHUB_SHA'), 'command': ' '.join(sys.argv)}}
