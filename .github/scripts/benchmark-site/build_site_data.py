"""Builds the data of the UniText benchmarks page from the runs of one full check.

  python -I -X utf8 build_site_data.py --out <site data dir> --fonts <font search root> [--archive <zip> --archive-url <url>]
      <run dir> [<run dir>...]

Each <run dir> holds the extracted BenchmarkJson-* folders of one CI run (gh run download <id> -p 'BenchmarkJson-*'), all
runs built from one host commit. Writes into <out>:
  page.json             the page's data (page.py, wp/WP5-page-spec.md §8)
  raw/gates.json        every gate's verdict per phone and the launches that did not measure; written first and always
  raw/comparisons.json  every case's comparisons per metric: per-phone intervals, excluded phones, blocked reasons
  raw/absolute.json     what every arm costs per phone
  raw/memory.json       per arm what the scene adds, and UniText against its rival per quantity and platform
  raw/check.json        the build's commits, Unity versions, run dates and launch counts
  raw/devices.json      the phones with tier and fingerprint
  raw/glyph_shapes.json outlines and outline complexity of the glyph suite's fonts
With --archive, every per-launch result file the reduction read goes into that zip byte for byte, so anyone can re-run
this script on it; --archive-url is where the page links it. When the run lacks the control arms the gates need, or the
inputs do not belong to one build, only raw/gates.json is written and the script exits non-zero."""
import argparse
import glob
import json
import math
import os
import re
import subprocess
import sys
import tempfile
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import compare  # noqa: E402
import devices  # noqa: E402
import gates  # noqa: E402
import glyph_shapes  # noqa: E402
import load  # noqa: E402
import memory  # noqa: E402
import page  # noqa: E402

PACKAGES = {'unitext': 'Packages/media.lightside.unitext', 'core': 'Packages/media.lightside.core'}


def tidy(value):
    if isinstance(value, float):
        return round(value, 4) if math.isfinite(value) else None
    if isinstance(value, dict):
        return {k: tidy(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [tidy(v) for v in value]
    return value


def write(out, name, data):
    os.makedirs(out, exist_ok=True)
    json.dump(tidy(data), open(os.path.join(out, name + '.json'), 'w', encoding='utf-8'), ensure_ascii=False,
              separators=(',', ':'))


def git(*args, cwd=None):
    return subprocess.run(['git', *args], cwd=cwd, check=True, capture_output=True, text=True).stdout.strip()


def fetch_host(commit):
    """Makes host `commit` present, fetched from `origin` when it is not, which also proves `origin` holds it. A shallow
    checkout (CI) fetches that commit alone; a full clone is never made shallow."""
    if subprocess.run(['git', 'cat-file', '-e', f'{commit}^{{commit}}'], capture_output=True).returncode == 0:
        return
    shallow = git('rev-parse', '--is-shallow-repository') == 'true'
    git('fetch', '--quiet', *(['--depth=1'] if shallow else []), 'origin', commit)


def repository():
    """The host repository's web address, from the checkout's `origin`."""
    url = git('remote', 'get-url', 'origin')
    match = re.fullmatch(r'(?:https://(?:[^@/]+@)?github\.com/|git@github\.com:)([\w.-]+/[\w.-]+?)(?:\.git)?/?', url)
    if not match:
        raise SystemExit(f'origin {url} is not a GitHub repository: the page cannot link the run\'s commit')
    return 'https://github.com/' + match.group(1)


def submodule_url(commit, path):
    modules = f'{commit}:.gitmodules'
    for line in git('config', '--blob', modules, '--get-regexp', r'^submodule\..*\.path$').splitlines():
        key, value = line.split(' ', 1)
        if value == path:
            return git('config', '--blob', modules, '--get', key[:-len('path')] + 'url')
    raise SystemExit(f'{commit}: .gitmodules names no submodule at {path}')


def package(commit, path):
    """The package host `commit` pins at `path`: its commit, the `package.json` version there, and where that commit
    stands against the release tags of the package's remote, read from a fresh clone of the remote (local tags are
    never consulted). `text` is `3.50.2` on a release tag, else `3.50.2 + 5 commits (725c658)`; the nearest release
    tag must be `v` plus the version, as the release pipeline writes it."""
    entry = git('ls-tree', commit, path).split()
    if len(entry) < 3 or entry[1] != 'commit':
        raise SystemExit(f'{commit}: no gitlink at {path}')
    sha = entry[2]
    url = submodule_url(commit, path)
    with tempfile.TemporaryDirectory() as scratch:
        clone = os.path.join(scratch, 'package.git')
        git('clone', '--quiet', '--bare', '--filter=tree:0', url, clone, cwd=scratch)
        git('fetch', '--quiet', 'origin', sha, cwd=clone)
        version = json.loads(git('show', f'{sha}:package.json', cwd=clone))['version']
        tag, count, _ = git('describe', '--tags', '--long', '--match', 'v[0-9]*', sha, cwd=clone).rsplit('-', 2)
    if tag != 'v' + version:
        raise SystemExit(f'{path} at {sha[:7]}: package.json says {version}, the nearest release tag of {url} is {tag}')
    n = int(count)
    return {'version': version, 'tag': tag if n == 0 else None, 'baseTag': tag, 'commitsAfterTag': n, 'commit': sha,
            'text': version if n == 0 else f'{version} + {n} commit{"" if n == 1 else "s"} ({sha[:7]})'}


def provenance(check):
    """One build: one host commit, ref and UniText commit, one set of baked launch arguments; the packages from the host
    commit's gitlinks, UniText's checked against the stamp, and the host commit's address in its repository."""
    builds = {(c, b, u) for c, b, u, _, _ in check.provenance}
    if len(builds) != 1:
        raise SystemExit(f'the inputs come from different builds: {sorted(builds)}')
    (commit, ref, stamped), = builds
    if not commit or commit == 'unknown' or not stamped or stamped == 'unknown':
        raise SystemExit(f'the build carries no provenance: commit {commit}, UniText {stamped}')
    arguments = {a for *_, a in check.provenance}
    if len(arguments) != 1:
        raise SystemExit(f'the launches carry different baked arguments: {sorted(arguments)}')
    fetch_host(commit)
    packages = {name: package(commit, path) for name, path in PACKAGES.items()}
    if packages['unitext']['commit'] != stamped:
        raise SystemExit(f'the build stamped UniText {stamped}, the host commit pins {packages["unitext"]["commit"]}')
    return {'host': {'commit': commit, 'ref': ref, 'url': f'{repository()}/tree/{commit}'}, **packages,
            'unity': sorted({u for _, _, _, u, _ in check.provenance}), 'launchArguments': list(next(iter(arguments)))}


def ratios(rows):
    for row in rows:
        if row['scale'] != 'ratio':
            continue
        for p in row['points']:
            for key, name in (('center', 'ratio'), ('lo', 'ratioLo'), ('hi', 'ratioHi')):
                if p.get(key) is not None:
                    p[name] = math.exp(p[key])


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--out', required=True)
    p.add_argument('--fonts', action='append', default=[], help='a folder searched for the glyph suite fonts')
    p.add_argument('--archive', help='zip that receives every per-launch result file the reduction read')
    p.add_argument('--archive-url', help='where the page links the archive')
    p.add_argument('runs', nargs='+')
    a = p.parse_args()

    check = load.load(a.runs)
    if not check.timing:
        raise SystemExit('the inputs hold no timing launch')
    arms = {l.case: l.arms for l in check.timing}

    def arms_of(case):
        if case not in arms:
            raise SystemExit(f'memory launches of {case} have no timing launch that defines its arms')
        return arms[case]

    controls = [r for r in compare.comparisons(check.timing, lambda d, m: None) if r['comparison'] == 'aa']
    spread = gates.aa_spread(controls)

    def sigma(device, metric):
        return spread.get((device, metric))

    rows = controls + [r for r in compare.comparisons(check.timing, sigma) if r['comparison'] != 'aa']
    rows += compare.ladders(check.timing, sigma)
    gpu = gates.gpu_counter(rows)
    by_case = gates.case_gates(check.timing, gpu)
    physical = gates.physical(rows)
    look = gates.look(check.look)
    gates.apply(rows, by_case, physical, look)
    run_reasons = gates.run_level(rows)

    raw = os.path.join(a.out, 'raw')
    report = {
        'run': run_reasons,
        'cases': [g for _, g in sorted(by_case.items())],
        'clock': gates.clocks(by_case),
        'aaSpread': [{'device': d, 'metric': m, 'sigma': s} for (d, m), s in sorted(spread.items())],
        'gpuCounter': [{'device': d, **v} for d, v in sorted(gpu.items())],
        'physical': [{'case': c, 'arm': n, 'threads': t, 'metric': m, 'reason': r}
                     for (c, n, t, m), r in sorted(physical.items())],
        'look': [{'engine': e, **v} for e, v in sorted(look.items())],
        'blocked': [{'case': r['case'], 'comparison': r['comparison'], 'a': r['a'], 'b': r['b'], 'metric': r['metric'],
                     'reasons': r['blocked']} for r in rows if r.get('blocked')],
        'unmeasured': check.unmeasured,
    }
    write(raw, 'gates', report)
    if run_reasons:
        raise SystemExit('nothing published: ' + '; '.join(run_reasons))

    build = provenance(check)
    ratios(rows)
    absolute = compare.absolute(check.timing)
    for row in absolute:
        for point in row['points']:
            point['excluded'] = gates.device_gate(point, row['case'], row['metric'], by_case)
    arm_rows = memory.per_arm(check.memory)
    fonts = {}
    for l in check.timing:
        stem = page.font_file(l.font)
        if stem:
            fonts.setdefault(stem, set()).update(page.rasterized(l))

    build['runs'] = sorted({os.path.basename(os.path.normpath(r)) for r in a.runs})
    build['measured'] = sorted(d for d in check.dates if d)
    build['launches'] = {'timing': len(check.timing), 'memory': len(check.memory), 'unmeasured': len(check.unmeasured)}
    twins = memory.twins(arm_rows, arms_of, {g: sum(1 for d in check.phones if devices.phone(d)['group'] == g)
                                             for g in {devices.phone(d)['group'] for d in check.phones}})
    shapes = glyph_shapes.build(sorted(fonts), a.fonts, {f: ''.join(sorted(c)) for f, c in fonts.items() if c}) if fonts else {}
    if bool(a.archive) != bool(a.archive_url):
        raise SystemExit('--archive and --archive-url go together')
    if a.archive:
        with zipfile.ZipFile(a.archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
            for root in a.runs:
                for path in sorted(glob.glob(os.path.join(root, 'BenchmarkJson-*', 'benchmarkResults_*.json'))):
                    archive.write(path, os.path.relpath(path, os.path.dirname(os.path.normpath(root))))
    unmapped = set()
    page_rows = page.rows(rows, absolute, arms, unmapped)
    run = page.run_block(build, check, report, page_rows)
    run['rawData'] = a.archive_url
    document = {'run': run,
                'devices': [{'id': d, **devices.phone(d), 'os': info['os'], 'gpu': info['gpu'], 'cores': info['cores'],
                             'ramGb': round(info['ramMb'] / 1024, 1) if info['ramMb'] else None, 'refreshHz': info['refreshHz'],
                             'suites': sorted(info.get('suites', ()))}
                            for d, info in sorted(check.phones.items())],
                'rows': page_rows, 'memory': page.memory_rows(twins, arms, unmapped) +
                                            [r for r in [page.cycles_row(check.special)] if r],
                'glyph': page.glyph(absolute, arms, check.timing), 'stills': {}}
    write(raw, 'check', build)
    write(raw, 'devices', [{'device': d, **devices.phone(d), **info, 'suites': sorted(info.get('suites', ()))}
                           for d, info in sorted(check.phones.items())])
    write(raw, 'comparisons', rows)
    write(raw, 'absolute', absolute)
    write(raw, 'memory', {'arms': [{k: v for k, v in r.items() if k != 'info'} for r in arm_rows], 'twins': twins,
                          'special': check.special})
    if shapes:
        write(raw, 'glyph_shapes', shapes)
    write(a.out, 'page', document)
    published = sum(1 for r in rows if r.get('summary'))
    print(f'UniText {build["unitext"]["text"]} / Core {build["core"]["text"]} (host {build["host"]["commit"][:7]}, '
          f'{build["host"]["ref"]}), Unity {", ".join(build["unity"])}: {published} of {len(rows)} comparison rows publishable, '
          f'{len(page_rows)} page rows; {len(check.unmeasured)} launches did not measure'
          + (f'; harness cases without a page name: {", ".join(sorted(unmapped))}' if unmapped else ''))


if __name__ == '__main__':
    main()
