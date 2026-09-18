"""Compares APK metadata, ELF segments and captured startup type populations."""

import argparse
import hashlib
import json
import re
import struct
from collections import Counter
from pathlib import Path
from zipfile import ZipFile

from analyze import Snapshot, csv_write


SANITY = 0xFAB11BAF

# Section order and record layouts are transcribed from the emitting editor's
# libil2cpp/vm/GlobalMetadataFileInternals.h; every index type there is int32 and
# kInvalidIndex reads back as 0xffffffff through the unsigned formats below.
# Versions past 31 pack their records narrower than those runtime structs and are
# not covered here: read stripped managed assemblies instead of guessing widths.
LAYOUTS = {
    31: dict(
        sections=(
            'stringLiterals stringLiteralData strings events properties methods '
            'parameterDefaultValues fieldDefaultValues defaultData marshaledSizes '
            'parameters fields genericParameters genericConstraints genericContainers '
            'nestedTypes interfaces vtableMethods interfaceOffsets typeDefinitions '
            'images assemblies fieldRefs referencedAssemblies attributeData attributeRanges '
            'unresolvedTypes unresolvedRanges windowsRuntimeTypes windowsRuntimeStrings exportedTypes'
        ).split(),
        section='2I', type='16I8H2I', method='7I4H', image='10I', assembly='16I',
        generic_container=16,
        type_name=0, type_namespace=1, type_generic=6, type_method_start=9,
        type_methods=16, type_properties=17, type_fields=18,
        assembly_reference_start=2, assembly_reference_count=3, assembly_name=4),
    108: dict(
        sections=(
            'stringLiterals stringLiteralData strings events properties methods '
            'parameterDefaultValues fieldDefaultValues fieldAndParameterDefaultValueData '
            'fieldMarshaledSizes parameters fields genericParameters '
            'genericParameterConstraints genericContainers nestedTypes interfaces '
            'vtableMethods interfaceOffsets typeDefinitions typeInlineArrays images '
            'assemblies fieldRefs referencedAssemblies attributeData attributeDataRanges '
            'unresolvedIndirectCallParameterTypes unresolvedIndirectCallParameterRanges '
            'windowsRuntimeTypeNames windowsRuntimeStrings exportedTypeDefinitions '
            'methodSpecsOnGenericType genericMethodSpecsOnType methodSpecs '
            'genericMethodFunctionsDefinitions genericMethodFunctionsDefinitionsWithAdjustor '
            'invokerIndices rgctxRanges rgctxValues staticConstructorTypeIndices'
        ).split(),
        section='3I', type='15I8H2I', method='7I4H', image='15I', assembly='17I',
        generic_container=12,
        type_name=0, type_namespace=1, type_generic=5, type_method_start=8,
        type_methods=15, type_properties=16, type_fields=17,
        assembly_reference_start=3, assembly_reference_count=4, assembly_name=5),
}


class Metadata:
    def __init__(self, data):
        self.data = data
        sanity, version = struct.unpack_from('<II', data)
        if sanity != SANITY:
            raise ValueError('Not an IL2CPP global-metadata file')
        if version not in LAYOUTS:
            raise ValueError(f'Unsupported IL2CPP metadata version: {version}')
        self.version = version
        self.layout = LAYOUTS[version]
        stride = struct.Struct('<' + self.layout['section'])
        self.sections = {}
        for i, name in enumerate(self.layout['sections']):
            offset, size = stride.unpack_from(data, 8 + i * stride.size)[:2]
            if offset + size > len(data):
                raise ValueError(f'Metadata section exceeds file: {name}')
            self.sections[name] = (offset, size)
        self.types = self.rows('typeDefinitions', self.layout['type'])
        self.methods = self.rows('methods', self.layout['method'])
        self.images = self.rows('images', self.layout['image'])
        self.assemblies = self.rows('assemblies', self.layout['assembly'])
        self.references = [r[0] for r in self.rows('referencedAssemblies', 'i')]

    def rows(self, name, fmt):
        offset, size = self.sections[name]
        layout = struct.Struct('<' + fmt)
        if size % layout.size:
            raise ValueError(f'Misaligned metadata section: {name}')
        return list(layout.iter_unpack(self.data[offset:offset + size]))

    def generic_containers(self):
        size = self.sections['genericContainers'][1]
        record = self.layout['generic_container']
        if size % record:
            raise ValueError('Misaligned metadata section: genericContainers')
        return size // record

    def string(self, index):
        offset, size = self.sections['strings']
        if not 0 <= index < size:
            raise ValueError(f'Invalid metadata string index: {index}')
        end = self.data.index(0, offset + index, offset + size)
        return self.data[offset + index:end].decode('utf-8')

    def inventory(self):
        L = self.layout
        types, methods, images, dependencies = [], [], [], []
        for image in self.images:
            name = self.string(image[0])
            start, count = image[2:4]
            if start + count > len(self.types):
                raise ValueError(f'Invalid type range: {name}')
            total_methods = total_fields = 0
            for ti in range(start, start + count):
                t = self.types[ti]
                ns, tn = self.string(t[L['type_namespace']]), self.string(t[L['type_name']])
                full_name = ns + '.' + tn if ns else tn
                type_methods, type_fields = t[L['type_methods']], t[L['type_fields']]
                method_start = t[L['type_method_start']]
                total_methods += type_methods
                total_fields += type_fields
                types.append(dict(assembly=name, type_index=ti, type=full_name,
                                  methods=type_methods, fields=type_fields,
                                  properties=t[L['type_properties']],
                                  generic=t[L['type_generic']] != 0xffffffff))
                for mi in range(method_start, method_start + type_methods):
                    m = self.methods[mi]
                    if m[1] != ti:
                        raise ValueError(f'Method owner mismatch: {mi}')
                    methods.append(dict(assembly=name, type=full_name,
                                        method=self.string(m[0]), index=mi,
                                        token=m[6], generic=m[5] != 0xffffffff))
            images.append(dict(assembly=name, types=count,
                               methods=total_methods, fields=total_fields))
            a = self.assemblies[image[1]]
            reference_start = a[L['assembly_reference_start']]
            for ref in self.references[reference_start:reference_start + a[L['assembly_reference_count']]]:
                if not 0 <= ref < len(self.assemblies):
                    raise ValueError(f'Invalid assembly reference: {ref}')
                dependencies.append(
                    dict(assembly=name, dependency=self.string(self.assemblies[ref][L['assembly_name']])))
        if len(types) != len(self.types) or len(methods) != len(self.methods):
            raise ValueError('Image/type ranges do not cover the metadata tables')
        return images, types, methods, dependencies


class Elf:
    def __init__(self, data):
        self.data = data
        h = struct.unpack_from('<16sHHIQQQIHHHHHH', data)
        if h[0][:6] != b'\x7fELF\x02\x01' or h[2] != 183:
            raise ValueError('Only little-endian AArch64 ELF64 is supported')
        self.loads = []
        for i in range(h[10]):
            p = struct.unpack_from('<II6Q', data, h[5] + i * h[9])
            if p[0] == 1:
                self.loads.append(dict(flags=p[1], offset=p[2], address=p[3],
                                       file_bytes=p[5], mapped_bytes=p[6], alignment=p[7]))
        raw = [struct.unpack_from('<IIQQQQIIQQ', data, h[6] + i * h[11])
               for i in range(h[12])]
        strings = raw[h[13]]
        names = data[strings[4]:strings[4] + strings[5]]
        self.sections = []
        for s in raw:
            name = names[s[0]:names.index(0, s[0])].decode()
            self.sections.append(dict(section=name, type=s[1], flags=s[2],
                                      address=s[3], offset=s[4], bytes=s[5], stride=s[9]))


def apk_inventory(path, out):
    out.mkdir(parents=True, exist_ok=True)
    with ZipFile(path) as z:
        metadata = z.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
        elf_bytes = z.read('lib/arm64-v8a/libil2cpp.so')
        boot = z.read('assets/bin/Data/boot.config').decode()
        initializers = json.loads(z.read('assets/bin/Data/RuntimeInitializeOnLoads.json'))['root']
        m = Metadata(metadata)
        images, types, methods, dependencies = m.inventory()
        elf = Elf(elf_bytes)
        csv_write(out / 'assemblies.csv', sorted(images, key=lambda r: -r['methods']))
        csv_write(out / 'type_definitions.csv', types)
        csv_write(out / 'method_definitions.csv', methods)
        csv_write(out / 'assembly_references.csv', dependencies)
        csv_write(out / 'runtime_initializers.csv', initializers)
        csv_write(out / 'elf_sections.csv', elf.sections)
        csv_write(out / 'elf_load_segments.csv', elf.loads)
        csv_write(out / 'metadata_sections.csv',
                  [dict(section=k, offset=o, bytes=s) for k, (o, s) in m.sections.items()])
        result = dict(apk=str(path.resolve()), sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                      apk_bytes=path.stat().st_size, metadata_bytes=len(metadata),
                      metadata_sha256=hashlib.sha256(metadata).hexdigest(),
                      il2cpp_bytes=len(elf_bytes), il2cpp_sha256=hashlib.sha256(elf_bytes).hexdigest(),
                      build_guid=re.search(r'^build-guid=(.+)$', boot, re.M)[1].strip(),
                      type_definitions=len(types), method_definitions=len(methods),
                      metadata_version=m.version, generic_containers=m.generic_containers(),
                      initializers=len(initializers))
    (out / 'summary.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    return result, images, elf


def captured_types(path, out, schema, elf):
    s = Snapshot(path, schema)
    regions = s.rows('SystemMemoryRegions', dict(Address='u', Size='u', Resident='u', Type='u', Name='s'))
    mappings = sorted((r for r in regions if r['Name'].endswith('/libil2cpp.so')),
                      key=lambda r: r['Address'])
    loads = sorted(elf.loads, key=lambda r: r['address'])
    if len(mappings) != len(loads):
        raise ValueError('Expected one captured file mapping per ELF LOAD segment')
    classified = []
    for mapping, segment in zip(mappings, loads):
        if abs(mapping['Size'] - segment['file_bytes']) >= segment['alignment']:
            raise ValueError('Captured mapping dimensions do not match APK LOAD segments')
        classified.append(dict(address=mapping['Address'], mapped_bytes=mapping['Size'],
                               resident_bytes=mapping['Resident'], elf_flags=segment['flags'],
                               inferred_executable=bool(segment['flags'] & 1)))
    csv_write(out / 'captured_il2cpp_segments.csv', classified)
    columns = [s.strings('TypeDescriptions_Assembly'), s.strings('TypeDescriptions_Name'),
               s.numbers('TypeDescriptions_TypeInfoAddress'),
               s.records('TypeDescriptions_StaticFieldBytes')]
    if len({len(c) for c in columns}) != 1:
        raise ValueError('Snapshot type columns have different lengths')
    rows = [dict(assembly=a, type=n, address=p, static_bytes=len(b),
                 generic='<' in n, array='[' in n) for a, n, p, b in zip(*columns)]
    csv_write(out / 'captured_types.csv', rows)
    counts = Counter(r['assembly'] for r in rows)
    csv_write(out / 'captured_assemblies.csv',
              [dict(assembly=k, captured_types=v) for k, v in counts.most_common()])
    return rows


def labelled_rows(rows_by_label, key, values):
    maps = {label: {r[key]: r for r in rows} for label, rows in rows_by_label.items()}
    names = set()
    for m in maps.values():
        names |= m.keys()
    result = []
    for name in sorted(names):
        row = {key: name}
        for value in values:
            for label, m in maps.items():
                row[f'{label}_{value}'] = m.get(name, {}).get(value, 0)
        result.append(row)
    return result


def label_path(value):
    label, separator, path = value.partition('=')
    if not label or not separator or not path:
        raise argparse.ArgumentTypeError(f'Expected LABEL=PATH, got {value!r}')
    return label, Path(path)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--apk', required=True, action='append', metavar='LABEL=PATH', type=label_path,
                   help='Build to inventory; repeat for every build being compared.')
    p.add_argument('--capture', action='append', default=[], metavar='LABEL=PATH', type=label_path,
                   help='Snapshot to pair with the build of the same label. Omit for an APK-only inventory.')
    p.add_argument('--memory-profiler', type=Path,
                   help='com.unity.memoryprofiler package root; required when --capture is given.')
    p.add_argument('--output', required=True, type=Path)
    args = p.parse_args()
    apks = dict(args.apk)
    captures = dict(args.capture)
    if len(apks) < 2:
        p.error('At least two distinct --apk labels are required')
    if captures.keys() - apks.keys():
        p.error('Every --capture label must have a matching --apk label')
    schema = None
    if captures:
        if args.memory_profiler is None:
            p.error('--memory-profiler is required when --capture is given')
        schema_file = args.memory_profiler / 'Editor/MemorySnapshot/Reader/QueriedSnapshot/EntryType.cs'
        schema = re.findall(r'^\s*(\w+)\s*(?:=\s*\d+)?\s*,',
                            schema_file.read_text(encoding='utf-8-sig'), re.M)
        schema.remove('Count')
    results, images, type_sets = {}, {}, {}
    for label, apk in apks.items():
        directory = args.output / label
        result, assembly_rows, elf = apk_inventory(apk, directory)
        if label in captures:
            types = captured_types(captures[label], directory, schema, elf)
            result.update(captured_types=len(types),
                          captured_generic_types=sum(r['generic'] for r in types),
                          captured_array_types=sum(r['array'] for r in types))
            type_sets[label] = {(r['assembly'], r['type']) for r in types}
        results[label] = result
        images[label] = assembly_rows
    def method_spread(row):
        counts = [row[f'{label}_methods'] for label in apks]
        return max(counts) - min(counts)

    rows = labelled_rows(images, 'assembly', ('types', 'methods', 'fields'))
    csv_write(args.output / 'assembly_comparison.csv', sorted(rows, key=lambda r: -method_spread(r)))
    reference = next(iter(apks))
    for label, types in type_sets.items():
        if label != reference and reference in type_sets:
            csv_write(args.output / f'additional_captured_types_{label}.csv',
                      [dict(assembly=a, type=n) for a, n in sorted(types - type_sets[reference])])
    (args.output / 'comparison.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    for label, result in results.items():
        print(label, result['type_definitions'], 'defined types,',
              result['method_definitions'], 'defined methods,',
              result.get('captured_types', '-'), 'captured types')


if __name__ == '__main__':
    main()
