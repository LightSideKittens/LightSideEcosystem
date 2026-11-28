"""Reads the launches of a full check: every per-device result file of the extracted `BenchmarkJson-*` folders, one
launch each. A timing launch holds one case record with all its arms measured in rounds; a memory launch holds one
arm's record in a process of its own, naming its timing case (`memoryCase`) and describing itself (`armInfo`); the
memory launches of `reference` hold the empty scene, the floor of what a scene adds. The only module that knows the result schema (wp/WP1-contract.md v1.3, Benchmarks/SPEC.md)."""
import glob
import json
import os
from dataclasses import dataclass, field

import devices

MIB = 1024.0 * 1024.0
HOT = {'severe', 'critical', 'emergency', 'shutdown', 'serious'}
SKIP = ('meta', 'systemInfo', 'config', 'phaseNotes', 'errors')
REFERENCE = 'reference'
SPECIAL = ('cycles', 'hidden')


@dataclass(frozen=True)
class Arm:
    id: str
    engine: str
    threads: str
    style: str
    load: float
    control: str
    variant: str = ''
    comparable: bool = True


@dataclass
class Round:
    index: int
    warmup: bool
    order: list
    hot: bool
    slots: dict


@dataclass
class Timing:
    platform: str
    device: str
    run: str
    case: str
    repeat: int
    seed: int
    clock: dict
    settings: dict
    cold: bool
    control_ms: float
    arms: dict
    rounds: list
    workload: dict = field(default_factory=dict)
    font: dict = field(default_factory=dict)
    suite: str = ''
    kind: str = 'frames'


@dataclass
class Memory:
    platform: str
    device: str
    run: str
    arm: str
    repeat: int
    steady: float
    peak: float
    start: float
    footprint: float
    gpu: float
    case: str
    info: Arm

    @property
    def dirty(self):
        return self.steady - self.gpu if self.gpu is not None else None


@dataclass
class Check:
    timing: list
    memory: list
    provenance: set
    dates: set
    look: dict
    phones: dict
    unmeasured: list
    special: list = field(default_factory=list)


def documents(roots):
    """(run dir, platform, device token, document) of every per-device result file of phones."""
    for root in roots:
        for folder in sorted(glob.glob(os.path.join(root, 'BenchmarkJson-*'))):
            platform = os.path.basename(folder).split('-')[-1]
            if platform not in devices.PHONE_PLATFORMS:
                continue
            for path in sorted(glob.glob(os.path.join(folder, 'benchmarkResults_*.json'))):
                yield root, platform, devices.device_token(os.path.basename(path)), json.load(open(path, encoding='utf-8-sig'))


def record_of(document, key):
    """(section name, record) of the suite record named `key` in whichever suite section of the document carries it."""
    for name, section in document.items():
        if name in SKIP or not isinstance(section, dict):
            continue
        record = section.get(key)
        if isinstance(record, dict):
            return name, record
    raise SystemExit(f'no suite section of the launch holds {key!r}')


def median(summary):
    v = summary.get('median') if isinstance(summary, dict) else summary
    return float(v) if isinstance(v, (int, float)) else None


def slot_values(slot):
    """The values a reducer reads from one slot of a frame or one-shot case."""
    metrics = slot.get('metrics') or {}
    render = metrics.get('render') or {}
    return {'cpu': median(slot.get('cpuMs')), 'render': median(metrics.get('cpuRenderMs')),
            'gpu': median(metrics.get('gpuMs')), 'canvas': median(metrics.get('canvasMs')),
            'objects': median(metrics.get('allocatedObjects')), 'sync': median(slot.get('syncMs')),
            'committed': median(slot.get('workMs')), 'frames': median(slot.get('frames')),
            'triangles': median(render.get('triangles')), 'draws': median(render.get('drawCalls')),
            'controlAt': median(slot.get('controlMsAtCalibration'))}


def describe(arm_id, d):
    """One arm from its descriptor; a case arm drawn with the default material is the GPU counter's `gpuBaseline`."""
    control = d.get('control') or ''
    if control == 'reference':
        return Arm(arm_id, 'reference', '', '', 0.0, '')
    if not control and d.get('baseline'):
        control = 'gpuBaseline'
    return Arm(arm_id, d['engine'], d.get('threads') or '', d['style'], float(d.get('load') or 0), control,
               d.get('variant') or '', d.get('comparable', True) is not False)


def arms_of(record):
    """Arm descriptors of a case record from `armInfo`: the reference arm, the gate arms (`aa`, `positive`) and the case
    arms."""
    info = record['armInfo']
    missing = [a for a in record['arms'] if a not in info]
    if missing:
        raise SystemExit(f'arms {missing} have no armInfo entry')
    return {arm_id: describe(arm_id, info[arm_id]) for arm_id in record['arms']}


def thermal_status(reading):
    return str((reading or {}).get('status', (reading or {}).get('state', ''))).lower()


def timing(root, platform, device, record, meta, config):
    thermal = record.get('thermal') or []
    rounds = []
    for raw in record['rounds']:
        k = raw['round']
        before = thermal_status(thermal[k]) if k < len(thermal) else ''
        after = thermal_status(thermal[k + 1]) if k + 1 < len(thermal) else ''
        rounds.append(Round(k, bool(raw.get('warmup')), list(raw['order']), before != after or before in HOT or after in HOT,
                            {arm: slot_values(slot) for arm, slot in raw['slots'].items()}))
    return Timing(platform, device, root, meta['participant'], int(meta['repeat']), int(record['seed']),
                  config.get('clock') or {}, record.get('settings') or {},
                  bool(record.get('coldResult')), record.get('controlMsAtCalibration'), arms_of(record),
                  rounds, record.get('workload') or {}, record.get('font') or {})


def memory(root, platform, device, record, meta):
    metrics = record['metrics']
    setup = record['setupPeak']['footprint']
    frames = metrics['processPeak']['footprint']
    system = metrics['systemFootprint']
    unseen = system['dirty'] - frames['end']
    graphics = system.get('graphics')
    top = max(setup['peak'], frames['peak'])
    participant = meta['participant']
    if participant != REFERENCE and ('memoryCase' not in record or 'armInfo' not in record):
        raise SystemExit(f'memory launch {participant} names no memoryCase or armInfo')
    return Memory(platform, device, root, participant, int(meta['repeat']), (frames['mean'] + unseen) / MIB,
                  (top + unseen) / MIB, setup['start'] / MIB, frames['mean'] / MIB,
                  graphics / MIB if graphics is not None else None, record.get('memoryCase', ''),
                  describe(participant, record.get('armInfo') or {'control': 'reference'}))


def load(roots):
    """Every launch of the full check. Raises when one launch identity appears twice (two runs feeding one number) or a
    memory launch names a case no timing launch measured."""
    check = Check([], [], set(), set(), {}, {}, [])
    seen = set()
    for root, platform, device, document in documents(roots):
        meta = document.get('meta') or {}
        info = document.get('systemInfo') or {}
        devices.phone(device)
        check.phones.setdefault(device, {'platform': platform, 'model': info.get('deviceModel'),
                                         'os': info.get('operatingSystem'), 'gpu': info.get('graphicsDeviceName'),
                                         'driver': info.get('graphicsDeviceVersion'), 'cores': info.get('processorCount'),
                                         'ramMb': info.get('systemMemorySize'), 'refreshHz': info.get('screenRefreshRateHz'),
                                         'pipeline': info.get('renderPipeline'), 'backend': info.get('scriptingBackend'),
                                         'debug': info.get('isDebugBuild')})
        check.dates.add(str(meta.get('utc', ''))[:10])
        check.provenance.add((meta.get('commit'), meta.get('branch'), meta.get('submoduleCommit'), info.get('unityVersion'),
                              tuple(meta.get('launchArguments') or ())))
        if 'lookCalibration' in document:
            check.look[device] = document['lookCalibration']
        participant = meta.get('participant')
        if participant is None:
            continue
        section, record = record_of(document, participant)
        check.phones[device].setdefault('suites', set()).add(section)
        kind = 'time' if record.get('kind') in ('frames', 'oneShot') else 'memory'
        identity = (device, participant, kind, meta['repeat'])
        if identity in seen:
            raise SystemExit(f'launch {identity} appears twice in the inputs: one number would come from two runs')
        seen.add(identity)
        for engine, reason in (record.get('unsupported') or {}).items():
            check.unmeasured.append({'device': device, 'participant': participant, 'kind': kind, 'repeat': meta['repeat'],
                                     'status': 'unsupported', 'engine': engine, 'reason': reason})
        if record.get('status') != 'measured':
            check.unmeasured.append({'device': device, 'participant': participant, 'kind': kind, 'repeat': meta['repeat'],
                                     'status': record.get('status'), 'reason': record.get('reason')})
        elif kind == 'time':
            launch = timing(root, platform, device, record, meta, document.get('config') or {})
            launch.suite = section
            launch.kind = record['kind']
            check.timing.append(launch)
        elif (record.get('armInfo') or {}).get('variant') in SPECIAL:
            check.special.append({'device': device, 'case': record['memoryCase'], 'variant': record['armInfo']['variant'],
                                  'repeat': meta['repeat'], 'record': record})
        else:
            check.memory.append(memory(root, platform, device, record, meta))
    cases = {launch.case for launch in check.timing}
    for launch in check.memory:
        if launch.arm != REFERENCE and launch.case not in cases:
            raise SystemExit(f'memory launches of {launch.arm} name case {launch.case!r}, which no timing launch measured')
    return check
