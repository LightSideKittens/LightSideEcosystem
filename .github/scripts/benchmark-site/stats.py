"""Statistics of the benchmark page: per-launch log ratios, per-phone intervals, classes against a declared band, the
summary across phones and the claim wording each precision allows.

A comparison A / B is worked on log ratios, one per round. A disturbed slot moves every ratio of its round, so a
phone's estimate is the median of the round values pooled over its launches, and a launch's value is the median of its
rounds. The interval is the Student t half-width of the launch values around that median, since rounds of one launch
share its state; a two-level bootstrap of the pooled median (launches, then rounds within a launch) cross-checks it,
and the wider one wins where they disagree by more than a percent. Phones are summarized by the geometric mean of their
estimates, never by folding the spread between phones into an interval."""
import math
import random
import statistics

T975 = (12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131,
        2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042)
Z975 = 1.959964
BOOTSTRAP_RESAMPLES = 4000
CROSS_CHECK_TOLERANCE = math.log(1.01)
BANDS = {'ios': 0.03, 'android': 0.05}
LARGE_FACTOR, LARGE_PRECISION = 2.0, 0.15
SMALL_PRECISION = 0.05


def t975(df):
    """Two-sided 95 % Student t quantile for `df` degrees of freedom (Cornish-Fisher beyond the table)."""
    if df < 1:
        raise ValueError(f'a t interval needs at least one degree of freedom, got {df}')
    if df <= len(T975):
        return T975[df - 1]
    z = Z975
    return z + (z ** 3 + z) / (4 * df) + (5 * z ** 5 + 16 * z ** 3 + 3 * z) / (96 * df ** 2)


def launch_value(round_values):
    """A launch's value: the median of its measured round values (log ratios or differences)."""
    if not round_values:
        raise ValueError('a launch without measured rounds has no value')
    return statistics.median(round_values)


def t_half(values):
    """The 95 % Student t half-width of the mean of `values`; needs two values."""
    return t975(len(values) - 1) * statistics.stdev(values) / math.sqrt(len(values))


def bootstrap_interval(launch_rounds, seed):
    """95 % percentile interval of the pooled median, resampling launches and then rounds within each launch."""
    rng = random.Random(seed)
    medians = []
    for _ in range(BOOTSTRAP_RESAMPLES):
        picked = [launch_rounds[rng.randrange(len(launch_rounds))] for _ in launch_rounds]
        medians.append(statistics.median([rounds[rng.randrange(len(rounds))] for rounds in picked for _ in rounds]))
    medians.sort()
    return medians[int(0.025 * (len(medians) - 1))], medians[int(math.ceil(0.975 * (len(medians) - 1)))]


def phone_interval(launch_rounds, seed, aa_sigma=None):
    """A phone's estimate from its launches, each a list of round values on a log (or additive) scale.

    Returns {center, lo, hi, launches, rounds, method}. With one or two launches the half-width is at least
    1.96 times the launch spread `aa_sigma` the A/A gate measured on this phone; without it such a phone has no
    interval and `method` says why."""
    launch_rounds = [list(r) for r in launch_rounds if r]
    launches = len(launch_rounds)
    rounds = sum(len(r) for r in launch_rounds)
    if launches == 0:
        return {'center': None, 'lo': None, 'hi': None, 'launches': 0, 'rounds': 0, 'method': 'no measured launch'}
    values = [launch_value(r) for r in launch_rounds]
    center = statistics.median(v for r in launch_rounds for v in r)
    if launches < 3:
        if aa_sigma is None:
            return {'center': center, 'lo': None, 'hi': None, 'launches': launches, 'rounds': rounds,
                    'launchValues': values, 'method': 'fewer than 3 launches and no A/A launch spread'}
        half = Z975 * aa_sigma
        if launches == 2:
            half = max(half, t_half(values))
        return {'center': center, 'lo': center - half, 'hi': center + half, 'launches': launches, 'rounds': rounds,
                'launchValues': values, 'method': 'A/A launch spread'}
    half = t_half(values)
    lo, hi = center - half, center + half
    method = 't'
    check_lo, check_hi = bootstrap_interval(launch_rounds, seed)
    if abs(check_lo - lo) > CROSS_CHECK_TOLERANCE or abs(check_hi - hi) > CROSS_CHECK_TOLERANCE:
        lo, hi = min(lo, check_lo), max(hi, check_hi)
        method = 'wider of t and bootstrap'
    return {'center': center, 'lo': lo, 'hi': hi, 'launches': launches, 'rounds': rounds, 'launchValues': values,
            'method': method}


def classify(lo, hi, band):
    """Class of a log-ratio interval against a relative equivalence band: equivalent when the whole interval lies inside
    the band, faster or slower when it excludes 1, inconclusive otherwise. A ratio below 1 means A takes less."""
    if lo is None or hi is None:
        return 'unmeasured'
    edge = math.log1p(band)
    if -edge <= lo and hi <= edge:
        return 'equivalent'
    if hi < 0:
        return 'faster'
    if lo > 0:
        return 'slower'
    return 'inconclusive'


def factor(log_ratio):
    """The factor a log ratio states in copy: 2.4 for both 0.42 and 2.4."""
    return math.exp(abs(log_ratio))


def word_factor(value):
    return f'{value:.2f}×' if value < 1.995 else f'{value:.1f}×' if value < 9.95 else f'{value:.0f}×'


def wording(interval, band, lower='faster', higher='slower'):
    """The claim METH 5.6 allows for one interval: a factor where the precision supports its size, a bound where the
    interval excludes 1 but is too wide for the factor, the band for equivalence, else no measurable difference.
    Returns the claim (see `claim`)."""
    lo, hi, center = interval.get('lo'), interval.get('hi'), interval.get('center')
    if lo is None or hi is None:
        return claim('unmeasured', None, None, interval.get('method', 'not measured'))
    half = (hi - lo) / 2
    cls = classify(lo, hi, band)
    if cls == 'equivalent':
        return claim('parity', None, None, f'within ±{band * 100:.0f} %')
    if cls == 'inconclusive':
        return claim('noDifference', None, None,
                     f'no measurable difference at this precision (±{(math.exp(half) - 1) * 100:.0f} %)')
    direction = lower if cls == 'faster' else higher
    size = factor(center)
    needed = LARGE_PRECISION if size >= LARGE_FACTOR else SMALL_PRECISION
    if half <= math.log1p(needed):
        return claim('factor', direction, size, f'{word_factor(size)} {direction} (±{(math.exp(half) - 1) * 100:.0f} %)')
    bound = factor(hi if cls == 'faster' else lo)
    return claim('bound', direction, bound, f'at least {word_factor(bound)} {direction}')


def geometric_mean(log_values):
    return math.exp(statistics.fmean(log_values))


def across_phones(points, band_of, lower='faster', higher='slower'):
    """Summary of per-phone intervals: geometric mean of the estimates, their range, the phone least favorable to A,
    class counts and a sentence. `points` are dicts with device, group, center, lo, hi; `band_of(group)` gives the band."""
    measured = [p for p in points if p.get('lo') is not None]
    if not measured:
        return {'phones': 0, 'claim': claim('unmeasured', None, None, 'no phone returned an interval')}
    classes = {}
    for p in measured:
        p['class'] = classify(p['lo'], p['hi'], band_of(p['group']))
        classes[p['class']] = classes.get(p['class'], 0) + 1
    centers = [p['center'] for p in measured]
    worst = max(measured, key=lambda p: p['center'])
    best = min(measured, key=lambda p: p['center'])
    summary = {'phones': len(measured), 'geomean': math.exp(statistics.fmean(centers)),
               'min': math.exp(min(centers)), 'max': math.exp(max(centers)), 'worstDevice': worst['device'],
               'worst': math.exp(worst['center']), 'bestDevice': best['device'], 'classes': classes}
    worst_name = worst.get('label', worst['device'])
    n = len(measured)
    faster, slower = classes.get('faster', 0), classes.get('slower', 0)
    if faster == n or slower == n:
        same = lower if faster == n else higher
        claims = [wording(p, band_of(p['group']), lower, higher)['form'] for p in measured]
        if all(c == 'factor' for c in claims):
            factors = [factor(c) for c in centers]
            edge = 'least' if same == lower else 'most'
            value = factor(statistics.fmean(centers))
            summary['claim'] = claim('factor', same, value,
                                     f'{word_factor(value)} {same} ({word_factor(min(factors))}–{word_factor(max(factors))} '
                                     f'across phones; {edge} on {worst_name})')
        else:
            bound = min(factor(p['hi'] if same == lower else p['lo']) for p in measured)
            summary['claim'] = claim('bound', same, bound, f'at least {word_factor(bound)} {same} on every phone')
    elif classes.get('equivalent', 0) == n:
        summary['claim'] = claim('parity', None, None, 'within the band on every phone')
    elif classes.get('inconclusive', 0) == n:
        summary['claim'] = claim('noDifference', None, None, f'no measurable difference on any of {n} phones')
    else:
        parts = [f'{lower} on {faster}' if faster else '', f'{higher} on {slower}' if slower else '',
                 f'within the band on {classes["equivalent"]}' if classes.get('equivalent') else '',
                 f'no measurable difference on {classes["inconclusive"]}' if classes.get('inconclusive') else '']
        summary['claim'] = claim('mixed', None, None, ', '.join(p for p in parts if p) + f' of {n} phones')
    return summary


def claim(form, direction, value, text):
    """What a summary lets the page say: factor, bound, parity, noDifference, mixed or unmeasured, its direction word,
    its number (the geometric mean's factor, or a bound's conservative end) and the sentence."""
    return {'form': form, 'direction': direction, 'value': value, 'text': text}
