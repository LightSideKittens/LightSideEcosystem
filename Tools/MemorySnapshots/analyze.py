"""Reads Unity queried snapshots and exports independent accounting views."""

import argparse
import csv
import hashlib
import json
import re
import struct
from collections import defaultdict
from pathlib import Path
from managed import ManagedHeap


def unpack(fmt, data, offset=0):
    return struct.unpack_from('<' + fmt, data, offset)


class Snapshot:
    def __init__(self, path, names):
        self.path = path
        self.data = path.read_bytes()
        if unpack('I', self.data)[0] != 0xAEABCDCD or unpack('I', self.data, len(self.data)-4)[0] != 0xABCDCDAE:
            raise ValueError(f'{path}: invalid snapshot signatures')
        directory = unpack('Q', self.data, len(self.data)-12)[0]
        signature, version, block_dir, count = unpack('IIQI', self.data, directory)
        if signature != 0xCDCDAEAB or version != 0x20170724:
            raise ValueError(f'{path}: unsupported directory')
        block_version, block_count = unpack('II', self.data, block_dir)
        if block_version != 0x20170724:
            raise ValueError(f'{path}: unsupported block format')
        self.blocks = []
        for addr in unpack('Q'*block_count, self.data, block_dir+8):
            chunk, size = unpack('QQ', self.data, addr)
            offsets = unpack('Q'*((size+chunk-1)//chunk), self.data, addr+16)
            self.blocks.append((chunk, size, offsets))
        if count > len(names):
            raise ValueError(f'{path}: entry schema is older than snapshot')
        self.entries = {}
        for name, addr in zip(names, unpack('Q'*count, self.data, directory+20)):
            if addr:
                fmt, block, meta, header = unpack('HIIQ', self.data, addr)
                offsets = unpack('Q'*(meta+1), self.data, addr+10) if fmt == 3 else ()
                self.entries[name] = (fmt, block, meta, header, offsets)

    def block_read(self, index, start, size):
        chunk, total, offsets = self.blocks[index]
        if start < 0 or size < 0 or start+size > total:
            raise ValueError('Entry range exceeds its block')
        pieces = []
        while size:
            i, local = divmod(start, chunk)
            take = min(size, chunk-local)
            physical = offsets[i]+local
            if physical+take > len(self.data):
                raise ValueError('Block range exceeds snapshot')
            pieces.append(self.data[physical:physical+take])
            size -= take
            start += take
        return b''.join(pieces)

    def records(self, name):
        entry = self.entries.get(name)
        if not entry or entry[0] == 0:
            return []
        fmt, block, meta, header, offsets = entry
        if fmt == 1:
            return [self.block_read(block, header, meta)]
        if fmt == 2:
            data = self.block_read(block, 0, meta*(header & 0xffffffff))
            return [data[i:i+meta] for i in range(0, len(data), meta)] if meta else []
        if fmt == 3:
            return [self.block_read(block, a, b-a) for a, b in zip(offsets, offsets[1:])]
        raise ValueError(f'Unsupported entry format {fmt}')

    def numbers(self, name, signed=False):
        return [int.from_bytes(v, 'little', signed=signed) for v in self.records(name)]

    def strings(self, name):
        return [v.decode('utf-8').rstrip('\x00') for v in self.records(name)]

    def rows(self, prefix, fields):
        columns = {}
        for field, kind in fields.items():
            key = prefix+'_'+field
            columns[field] = self.strings(key) if kind == 's' else self.numbers(key, kind == 'i')
        lengths = {len(v) for v in columns.values()}
        if len(lengths) != 1:
            raise ValueError(f'{prefix}: inconsistent column lengths {lengths}')
        return [dict(zip(columns, values)) for values in zip(*columns.values())]


def csv_write(path, rows):
    if not rows:
        path.write_text('', encoding='utf-8')
        return
    fields = list(dict.fromkeys(k for row in rows for k in row))
    with path.open('w', newline='', encoding='utf-8-sig') as stream:
        writer = csv.DictWriter(stream, fields)
        writer.writeheader()
        writer.writerows(rows)


def aggregate(rows, group, size):
    sums = defaultdict(lambda: [0, 0])
    for row in rows:
        key = tuple(row[g] for g in group)
        sums[key][0] += 1
        sums[key][1] += row[size]
    return sorted([dict(zip(group, key), count=v[0], bytes=v[1]) for key, v in sums.items()], key=lambda r: -r['bytes'])


def normalized_mapping(name):
    return re.sub(r'/data/app/[^/]+/[^/]+/', '/data/app/<installation>/', name)


def analyze(snapshot, out):
    out.mkdir(parents=True, exist_ok=True)
    info = snapshot.records('ProfileTarget_Info')[0]
    unity_length = unpack('I', info, 48)[0]
    product_length = unpack('I', info, 68)[0]
    summary = dict(file=snapshot.path.name, format=snapshot.numbers('Metadata_Version')[0],
                   sha256=hashlib.sha256(snapshot.data).hexdigest(),
                   capture_flags=snapshot.numbers('Metadata_CaptureFlags')[0],
                   session=unpack('I', info)[0], platform=unpack('i', info, 4)[0],
                   graphics_api=unpack('i', info, 8)[0], scripting_backend=unpack('i', info, 32)[0],
                   time_since_startup=unpack('d', info, 40)[0],
                   unity_version=info[52:52+unity_length].decode('utf-8'),
                   product=info[72:72+product_length].decode('utf-8'))
    summary['scenes'] = snapshot.strings('SceneObjects_Name')
    if not 16 <= summary['format'] <= 18:
        raise ValueError(f'Accounting layouts currently support snapshot versions 16–18, got {summary["format"]}')
    stat_names = ['total_virtual', 'total_used', 'total_reserved', 'temp_used', 'graphics_used', 'audio_used',
                  'gc_used', 'gc_reserved', 'profiler_used', 'profiler_reserved', 'memory_profiler_used', 'memory_profiler_reserved']
    summary['counters'] = dict(zip(stat_names, unpack('Q'*12, snapshot.records('ProfileTarget_MemoryStats')[0])))
    summary['vm'] = unpack('6I', snapshot.records('Metadata_VirtualMachineInformation')[0])
    native_types = snapshot.strings('NativeTypes_Name')
    objects = snapshot.rows('NativeObjects', dict(Name='s', NativeTypeArrayIndex='i', Size='u', NativeObjectAddress='u', RootReferenceId='u', InstanceId='i'))
    gfx = snapshot.rows('NativeGfxResourceReferences', dict(Id='u', Size='u', RootId='u'))
    gfx_by_root = defaultdict(int)
    for row in gfx:
        gfx_by_root[row['RootId']] += row['Size']
    metadata = snapshot.records('ObjectMetaData_MetaDataBuffer')
    indices = snapshot.numbers('ObjectMetaData_MetaDataBufferIndicies', True)
    for i, obj in enumerate(objects):
        obj['Type'] = native_types[obj['NativeTypeArrayIndex']]
        obj['GfxBytes'] = gfx_by_root[obj['RootReferenceId']]
        mi = indices[i] if i < len(indices) else -1
        if mi >= 0:
            blob = metadata[mi]
            if obj['Type'] == 'Texture2D' and len(blob) >= 27 and unpack('i', blob)[0] == 1:
                obj.update(zip(['MetaVersion', 'TextureFormat', 'GraphicsFormat', 'Width', 'Height', 'MipCount'], unpack('6i', blob)))
                obj['Readable'] = blob[24]
            elif obj['Type'] == 'RenderTexture' and len(blob) >= 24:
                version = unpack('i', blob)[0]
                obj['MetaVersion'] = version
                if version == 1:
                    obj.update(zip(['GraphicsFormat', 'Width', 'Height', 'MipCount'], unpack('4i', blob, 8)))
                elif version in (2, 3):
                    obj.update(zip(['Width', 'Height', 'GraphicsFormat', 'DepthFormat', 'MipCount'], unpack('iihhb', blob, 4)))
    csv_write(out/'native_objects.csv', sorted(objects, key=lambda r: -r['Size']))
    native_groups = aggregate(objects, ['Type'], 'Size')
    csv_write(out/'native_types.csv', native_groups)
    roots = snapshot.rows('NativeRootReferences', dict(Id='u', AreaName='s', ObjectName='s', AccumulatedSize='u'))
    root_map = {r['Id']: r for r in roots}
    object_map = {r['RootReferenceId']: r for r in objects}
    for r in gfx:
        root = root_map.get(r['RootId'], {})
        obj = object_map.get(r['RootId'], {})
        r.update(Area=root.get('AreaName', '<unrooted>'), Owner=root.get('ObjectName', '<unrooted>'),
                 Object=obj.get('Name', ''), Type=obj.get('Type', ''))
    csv_write(out/'graphics_resources.csv', sorted(gfx, key=lambda r: -r['Size']))
    allocations = snapshot.rows('NativeAllocations', dict(MemoryRegionIndex='i', RootReferenceId='u', AllocationSiteId='u', Address='u', Size='u', OverheadSize='u', PaddingSize='u'))
    for r in allocations:
        root = root_map.get(r['RootReferenceId'], {})
        r['Area'] = root.get('AreaName', '<unrooted>')
        r['Owner'] = root.get('ObjectName', '<unrooted>')
    allocation_groups = aggregate(allocations, ['Area', 'Owner'], 'Size')
    csv_write(out/'native_allocation_owners.csv', allocation_groups)
    csv_write(out/'native_roots.csv', sorted(roots, key=lambda r: -r['AccumulatedSize']))
    regions = snapshot.rows('SystemMemoryRegions', dict(Address='u', Size='u', Resident='u', Type='u', Name='s'))
    csv_write(out/'system_regions.csv', sorted(regions, key=lambda r: -r['Resident']))
    region_groups = aggregate(regions, ['Type', 'Name'], 'Resident')
    csv_write(out/'system_resident.csv', region_groups)
    normalized_regions = [dict(r, Name=normalized_mapping(r['Name'])) for r in regions]
    normalized_groups = aggregate(normalized_regions, ['Type', 'Name'], 'Resident')
    csv_write(out/'system_resident_normalized.csv', normalized_groups)
    allocators = snapshot.rows('NativeAllocatorInfo', dict(AllocatorName='s', UsedSize='u', ReservedSize='u', OverheadSize='u', PeakUsedSize='u', AllocationCount='u'))
    csv_write(out/'allocators.csv', allocators)
    labels = snapshot.rows('NativeMemoryLabels', dict(Name='s', Size='u'))
    csv_write(out/'memory_labels.csv', sorted(labels, key=lambda r: -r['Size']))
    heap = ManagedHeap(snapshot).walk()
    managed = heap.export_rows()
    csv_write(out/'managed_objects.csv', managed)
    csv_write(out/'pooled_buffers.csv', heap.pooled_buffers())
    csv_write(out/'shared_pool_counts.csv', heap.shared_pool_counts())
    references = [dict(target=target, source=owner, field=field)
                  for target, incoming in heap.incoming.items() for owner, field in incoming]
    csv_write(out/'managed_references.csv', references)
    csv_write(out/'managed_unresolved_references.csv',
              [dict(target=p, source=o, field=f) for p, o, f in heap.invalid_references])
    root_groups = aggregate([dict(r, root=r['path'].split(' -> ')[0]) for r in managed], ['root'], 'bytes')
    csv_write(out/'managed_root_paths.csv', root_groups)
    managed_groups = aggregate(managed, ['assembly', 'type'], 'bytes')
    csv_write(out/'managed_types.csv', managed_groups)
    native_links = heap.native_links(allocations)
    csv_write(out/'native_managed_owners.csv', sorted(native_links, key=lambda r: -r['Size']))
    csv_write(out/'native_allocations.csv', sorted(allocations, key=lambda r: -r['Size']))
    summary['managed_reachable_bytes'] = sum(r['bytes'] for r in managed)
    summary['managed_reachable_objects'] = len(managed)
    summary['managed_unresolved_reference_count'] = len(heap.invalid_references)
    summary['managed_types'] = managed_groups
    summary['managed_root_paths'] = root_groups
    summary['native_allocation_site_count'] = len(snapshot.records('NativeAllocationSites_Id'))
    summary['managed_stack_captured_bytes'] = sum(map(len, snapshot.records('ManagedStacks_Bytes')))
    summary.update(native_object_bytes=sum(r['Size'] for r in objects), gfx_resource_bytes=sum(r['Size'] for r in gfx),
                   native_allocation_bytes=sum(r['Size'] for r in allocations), system_resident=sum(r['Resident'] for r in regions),
                   system_mapped=sum(r['Size'] for r in regions), native_objects=len(objects),
                   native_allocations=len(allocations), managed_heap_captured=sum(map(len, snapshot.records('ManagedHeapSections_Bytes'))),
                   native_types=native_groups, allocation_owners=allocation_groups, resident_regions=normalized_groups)
    (out/'summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures', type=Path)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    package = args.package
    if package is None:
        packages = list((root/'Library/PackageCache').glob('com.unity.memoryprofiler@*'))
        embedded = root/'Packages/com.unity.memoryprofiler'
        if embedded.exists():
            packages = [embedded]
        if len(packages) != 1:
            parser.error('Specify --package with the installed Memory Profiler source directory')
        package = packages[0]
    schema = (package/'Editor/MemorySnapshot/Reader/QueriedSnapshot/EntryType.cs').read_text(encoding='utf-8-sig')
    names = re.findall(r'^\s*(\w+)\s*(?:=\s*\d+)?\s*,', schema, re.M)
    names.remove('Count')
    output = args.output or args.captures/'Analysis'
    paths = sorted(args.captures.glob('*.snap'))
    if not paths:
        parser.error(f'No .snap files in {args.captures}')
    results = []
    for path in paths:
        result = analyze(Snapshot(path, names), output/path.stem)
        results.append(result)
        print(path.name, f'resident={result["system_resident"]:,} B',
              f'managed reachable={result["managed_reachable_bytes"]:,} B',
              f'unresolved references={result["managed_unresolved_reference_count"]}', flush=True)
    output.mkdir(parents=True, exist_ok=True)
    (output/'comparison.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
    provenance = dict(memory_profiler_package=json.loads((package/'package.json').read_text(encoding='utf-8-sig'))['version'],
                      schema_sha256=hashlib.sha256(schema.encode('utf-8')).hexdigest(), schema_path=str(package),
                      files=[dict(file=r['file'], sha256=r['sha256']) for r in results])
    (output/'provenance.json').write_text(json.dumps(provenance, indent=2), encoding='utf-8')
    for category in ('native_types', 'allocation_owners', 'resident_regions', 'managed_types'):
        combined = {}
        for i, result in enumerate(results):
            for row in result[category]:
                identity = tuple((k, v) for k, v in row.items() if k not in ('count', 'bytes'))
                record = combined.setdefault(identity, dict(identity))
                record[f's{i+1}_bytes'] = row['bytes']
                record[f's{i+1}_count'] = row['count']
        for record in combined.values():
            for i in range(len(results)):
                record.setdefault(f's{i+1}_bytes', 0)
                record.setdefault(f's{i+1}_count', 0)
            for i in range(1, len(results)):
                record[f'delta_{i}_{i+1}'] = record[f's{i+1}_bytes'] - record[f's{i}_bytes']
        csv_write(output/(category+'_comparison.csv'), list(combined.values()))


if __name__ == '__main__':
    main()
