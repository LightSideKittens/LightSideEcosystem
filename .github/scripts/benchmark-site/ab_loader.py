"""Shared loader for two-run comparisons: every device result of a downloaded run folder, keyed by (platform, device).

<dir> holds extracted BenchmarkJson-<suite>-<unity>-<platform> folders (gh run download <id> -p 'BenchmarkJson-*', a few MB:
the results alone) or the full BenchmarkResults-* artifacts of older runs.
Per-device files (benchmarkResults_<n>_<device>.json) win over the plain benchmarkResults.json, which is a copy of the
first device on Android and iOS. The launches of an isolated run (one participant each, `meta.participant`) merge into one
document per device: every suite section holds each participant's records, repeat 0 first."""
import glob
import json
import os


def load(root, suite_glob='*'):
    out, isolated = {}, {}
    folders = glob.glob(os.path.join(root, f'BenchmarkJson-{suite_glob}')) or glob.glob(os.path.join(root, f'BenchmarkResults-{suite_glob}'))
    for folder in sorted(folders):
        parts = os.path.basename(folder).split('-')
        platform = parts[-1] if len(parts) < 4 else f'{parts[1]}:{parts[-1]}'
        files = sorted(glob.glob(os.path.join(folder, 'benchmarkResults_*.json')))
        if not files:
            files = sorted(glob.glob(os.path.join(folder, 'benchmarkResults.json')))
        for f in files:
            try:
                j = json.load(open(f, encoding='utf-8-sig'))
            except (OSError, ValueError) as e:
                print(f'skip {f}: {e}')
                continue
            name = os.path.basename(f)
            device = name[len('benchmarkResults_'):-5].split('_', 1)[-1] if '_' in name else \
                (j.get('systemInfo') or {}).get('deviceModel', platform)
            if (j.get('meta') or {}).get('participant') is None:
                out[(platform, device)] = j
            else:
                isolated.setdefault((platform, device), []).append(j)
    for key, documents in isolated.items():
        out[key] = merge(documents)
    return out


def merge(documents):
    """One document from the launches of one device, each suite section holding every launch's records; where two
    launches carry the same record (the repeats), the lower repeat wins."""
    documents = sorted(documents, key=lambda j: (j.get('meta') or {}).get('repeat', 0), reverse=True)
    merged = dict(documents[-1])
    for j in documents:
        for name, section in j.items():
            if isinstance(section, dict) and name not in ('meta', 'systemInfo', 'config'):
                merged.setdefault(name, {})
                if merged[name] is not section:
                    merged[name] = {**merged[name], **section}
    return merged


def fingerprint(j):
    si = j.get('systemInfo') or {}
    return f"{si.get('deviceModel')} | {si.get('operatingSystem')} | {si.get('graphicsDeviceVersion')}"


def meta(j):
    m = j.get('meta') or {}
    return f"host {str(m.get('commit'))[:8]} pkg {str(m.get('submoduleCommit'))[:8]}"
