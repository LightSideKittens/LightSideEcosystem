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


SECTIONS = (
    'stringLiterals stringLiteralData strings events properties methods '
    'parameterDefaultValues fieldDefaultValues defaultData marshaledSizes '
    'parameters fields genericParameters genericConstraints genericContainers '
    'nestedTypes interfaces vtableMethods interfaceOffsets typeDefinitions '
    'images assemblies fieldRefs referencedAssemblies attributeData attributeRanges '
    'unresolvedTypes unresolvedRanges windowsRuntimeTypes windowsRuntimeStrings exportedTypes'
).split()


class Metadata:
    def __init__(self, data):
        self.data = data
        if struct.unpack_from('<II', data) != (0xFAB11BAF, 31):
            raise ValueError('Only IL2CPP metadata version 31 is supported')
        self.sections = {name: struct.unpack_from('<II', data, 8 + i * 8)
                         for i, name in enumerate(SECTIONS)}
        for name, (offset, size) in self.sections.items():
            if offset + size > len(data):
                raise ValueError(f'Metadata section exceeds file: {name}')
        self.types = self.rows('typeDefinitions', '16I8H2I')
        self.methods = self.rows('methods', '7I4H')
        self.images = self.rows('images', '10I')
        self.assemblies = self.rows('assemblies', '16I')
        self.references = [r[0] for r in self.rows('referencedAssemblies', 'i')]

    def rows(self, name, fmt):
        offset, size = self.sections[name]
        layout = struct.Struct('<' + fmt)
        if size % layout.size:
            raise ValueError(f'Misaligned metadata section: {name}')
        return list(layout.iter_unpack(self.data[offset:offset + size]))

    def string(self, index):
        offset, size = self.sections['strings']
        if not 0 <= index < size:
            raise ValueError(f'Invalid metadata string index: {index}')
        end = self.data.index(0, offset + index, offset + size)
        return self.data[offset + index:end].decode('utf-8')

    def inventory(self):
        types, methods, images, dependencies = [], [], [], []
        for image in self.images:
            name = self.string(image[0])
            start, count = image[2:4]
            if start + count > len(self.types):
                raise ValueError(f'Invalid type range: {name}')
            total_methods = total_fields = 0
            for ti in range(start, start + count):
                t = self.types[ti]
                ns, tn = self.string(t[1]), self.string(t[0])
                full_name = ns + '.' + tn if ns else tn
                total_methods += t[16]
                total_fields += t[18]
                types.append(dict(assembly=name, type_index=ti, type=full_name,
                                  methods=t[16], fields=t[18], properties=t[17],
                                  generic=t[6] != 0xffffffff))
                for mi in range(t[9], t[9] + t[16]):
                    m = self.methods[mi]
                    if m[1] != ti:
                        raise ValueError(f'Method owner mismatch: {mi}')
                    methods.append(dict(assembly=name, type=full_name,
                                        method=self.string(m[0]), index=mi,
                                        token=m[6], generic=m[5] != 0xffffffff))
            images.append(dict(assembly=name, types=count,
                               methods=total_methods, fields=total_fields))
            a = self.assemblies[image[1]]
            for ref in self.references[a[2]:a[2] + a[3]]:
                if not 0 <= ref < len(self.assemblies):
                    raise ValueError(f'Invalid assembly reference: {ref}')
                dependencies.append(dict(assembly=name,
                                         dependency=self.string(self.assemblies[ref][4])))
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
                      generic_containers=m.sections['genericContainers'][1] // 16,
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


def paired_rows(left, right, key, values):
    maps = [{r[key]: r for r in rows} for rows in (left, right)]
    result = []
    for name in sorted(maps[0].keys() | maps[1].keys()):
        row = {key: name}
        for value in values:
            a, b = (m.get(name, {}).get(value, 0) for m in maps)
            row.update({f'tmp_{value}': a, f'unitext_{value}': b, f'delta_{value}': b-a})
        result.append(row)
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--tmp-apk', required=True, type=Path)
    p.add_argument('--unitext-apk', required=True, type=Path)
    p.add_argument('--captures', required=True, type=Path)
    p.add_argument('--output', required=True, type=Path)
    args = p.parse_args()
    provenance = json.loads((args.captures / 'Analysis/provenance.json').read_text(encoding='utf-8'))
    schema_file = Path(provenance['schema_path']) / 'Editor/MemorySnapshot/Reader/QueriedSnapshot/EntryType.cs'
    schema_text = schema_file.read_text(encoding='utf-8-sig')
    schema = re.findall(r'^\s*(\w+)\s*(?:=\s*\d+)?\s*,', schema_text, re.M)
    schema.remove('Count')
    results, images, type_sets = [], [], []
    for label, apk, snapshot in (('TMP', args.tmp_apk, 'TMP_berfore_2.snap'),
                                 ('UniText', args.unitext_apk, 'UT_before_2.snap')):
        directory = args.output / label
        result, assembly_rows, elf = apk_inventory(apk, directory)
        types = captured_types(args.captures / snapshot, directory, schema, elf)
        result.update(captured_types=len(types), captured_generic_types=sum(r['generic'] for r in types),
                      captured_array_types=sum(r['array'] for r in types))
        results.append(result)
        images.append(assembly_rows)
        type_sets.append({(r['assembly'], r['type']) for r in types})
    csv_write(args.output / 'assembly_comparison.csv',
              sorted(paired_rows(*images, 'assembly', ('types', 'methods', 'fields')),
                     key=lambda r: -r['delta_methods']))
    csv_write(args.output / 'additional_captured_types.csv',
              [dict(assembly=a, type=n) for a, n in sorted(type_sets[1] - type_sets[0])])
    (args.output / 'comparison.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    for label, result in zip(('TMP', 'UniText'), results):
        print(label, result['type_definitions'], 'defined types,',
              result['method_definitions'], 'defined methods,', result['captured_types'], 'captured types')


if __name__ == '__main__':
    main()
