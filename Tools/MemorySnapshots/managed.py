"""Traverses captured GC handles and static fields using snapshot type layouts."""

import bisect
import struct
from collections import defaultdict, deque


class ManagedHeap:
    def __init__(self, snapshot):
        self.snapshot = snapshot
        self.pointer, self.header, self.array_header, self.bounds_offset, self.length_offset, self.alignment = struct.unpack('<6I', snapshot.records('Metadata_VirtualMachineInformation')[0])
        if self.pointer != 8:
            raise ValueError('The managed crawler currently supports 64-bit captured heaps')
        self.sections = sorted((a & ~(1 << 63), b) for a, b in zip(snapshot.numbers('ManagedHeapSections_StartAddress'), snapshot.records('ManagedHeapSections_Bytes')))
        self.starts = [a for a, _ in self.sections]
        self.names = snapshot.strings('TypeDescriptions_Name')
        self.assemblies = snapshot.strings('TypeDescriptions_Assembly')
        self.flags = snapshot.numbers('TypeDescriptions_Flags')
        self.sizes = snapshot.numbers('TypeDescriptions_Size', True)
        self.bases = snapshot.numbers('TypeDescriptions_BaseOrElementTypeIndex', True)
        self.indices = snapshot.numbers('TypeDescriptions_TypeIndex', True)
        index_map = {n: i for i, n in enumerate(self.indices)}
        self.bases = [index_map.get(n, -1) for n in self.bases]
        self.type_addresses = dict(zip(snapshot.numbers('TypeDescriptions_TypeInfoAddress'), range(len(self.names))))
        self.field_indices = [struct.unpack('<'+'i'*(len(b)//4), b) for b in snapshot.records('TypeDescriptions_FieldIndices')]
        self.static_bytes = snapshot.records('TypeDescriptions_StaticFieldBytes')
        self.field_names = snapshot.strings('FieldDescriptions_Name')
        self.field_types = [index_map[n] for n in snapshot.numbers('FieldDescriptions_TypeIndex', True)]
        self.field_offsets = snapshot.numbers('FieldDescriptions_Offset', True)
        self.field_static = snapshot.numbers('FieldDescriptions_IsStatic')
        self.layouts = {}
        self.objects = {}
        self.paths = {}
        self.incoming = defaultdict(list)
        self.native_pointers = []
        self.invalid_references = []

    def read(self, address, size):
        i = bisect.bisect_right(self.starts, address)-1
        if i < 0:
            return None
        start, data = self.sections[i]
        offset = address-start
        if offset+size > len(data):
            return None
        return data[offset:offset+size]

    def number(self, address, size, signed=False):
        data = self.read(address, size)
        return int.from_bytes(data, 'little', signed=signed) if data is not None else None

    def layout(self, t, static=False, active=()):
        key = t, static
        if key in self.layouts:
            return self.layouts[key]
        if t in active:
            return []
        result = []
        for f in self.field_indices[t]:
            if bool(self.field_static[f]) != static:
                continue
            ft = self.field_types[f]
            offset = self.field_offsets[f] - (0 if static else self.header)
            if offset < 0:
                continue
            name = self.field_names[f]
            if self.names[ft].endswith('*') or self.names[ft] in ('System.IntPtr', 'System.UIntPtr'):
                result.append((offset, name, 'pointer'))
            elif self.flags[ft] & 1:
                nested = self.layout(ft, active=active+(t,))
                if nested:
                    result.extend((offset+o, name+'.'+n, kind) for o, n, kind in nested)
            else:
                result.append((offset, name, 'reference'))
        base = self.bases[t]
        if not static and not self.flags[t] & 3 and base >= 0:
            result.extend(self.layout(base, active=active+(t,)))
        self.layouts[key] = result
        return result

    def parse(self, address):
        identity = self.number(address, self.pointer)
        t = self.type_addresses.get(identity)
        if t is None and identity:
            t = self.type_addresses.get(self.number(identity, self.pointer))
        if t is None:
            return None
        length = None
        if self.flags[t] & 2:
            length = self.number(address+self.length_offset, 4, True)
            element = self.bases[t]
            if length is None or length < 0 or (length and element < 0):
                return None
            element_size = self.sizes[element] if length and self.flags[element] & 1 else self.pointer
            size = self.array_header + length*element_size
        elif self.names[t] == 'System.String':
            length = self.number(address+self.header, 4, True)
            if length is None or length < 0:
                return None
            size = self.header+4+length*2
        else:
            size = self.sizes[t] + (self.header if self.flags[t] & 1 else 0)
        if size <= 0 or self.read(address+size-1, 1) is None:
            return None
        return dict(address=address, type_index=t, type=self.names[t], assembly=self.assemblies[t], bytes=size, length=length)

    def walk(self):
        queue = deque()

        def add(pointer, owner, field, kind):
            if not pointer:
                return
            if kind == 'pointer':
                self.native_pointers.append((pointer, owner, field))
                return
            self.incoming[pointer].append((owner, field))
            if pointer in self.objects:
                return
            obj = self.parse(pointer)
            if obj is None:
                self.invalid_references.append((pointer, owner, field))
                return
            self.objects[pointer] = obj
            self.paths[pointer] = (owner, field)
            queue.append(pointer)

        for t, data in enumerate(self.static_bytes):
            if not data:
                continue
            for offset, name, kind in self.layout(t, True):
                if offset+self.pointer <= len(data):
                    add(int.from_bytes(data[offset:offset+self.pointer], 'little'), 'static '+self.names[t], name, kind)
        handles = self.snapshot.numbers('GCHandles_Target')
        native_names = self.snapshot.strings('NativeObjects_Name')
        native_types = self.snapshot.strings('NativeTypes_Name')
        native_type_indices = self.snapshot.numbers('NativeObjects_NativeTypeArrayIndex', True)
        handle_names = {index: f'{native_types[native_type_indices[i]]} "{native_names[i]}"'
                        for i, index in enumerate(self.snapshot.numbers('NativeObjects_GCHandleIndex', True))
                        if index >= 0}
        for i, pointer in enumerate(handles):
            add(pointer, f'GCHandle {i} {handle_names.get(i, "")}'.rstrip(), '', 'reference')

        while queue:
            address = queue.popleft()
            obj = self.objects[address]
            t = obj['type_index']
            if self.flags[t] & 2:
                if not obj['length']:
                    continue
                e = self.bases[t]
                if self.flags[e] & 1:
                    layout = self.layout(e)
                    stride = self.sizes[e]
                else:
                    layout = [(0, '', 'reference')]
                    stride = self.pointer
                if not layout:
                    continue
                for i in range(obj['length']):
                    base = address+self.array_header+i*stride
                    for offset, field, kind in layout:
                        pointer = self.number(base+offset, self.pointer)
                        add(pointer, address, f'[{i}]'+('.'+field if field else ''), kind)
            elif self.names[t] != 'System.String':
                for offset, field, kind in self.layout(t):
                    if self.header+offset+self.pointer <= obj['bytes']:
                        add(self.number(address+self.header+offset, self.pointer), address, field, kind)
        return self

    def path(self, address, max_depth=16):
        parts = []
        seen = set()
        while isinstance(address, int) and address in self.paths and address not in seen and len(parts) < max_depth:
            seen.add(address)
            owner, field = self.paths[address]
            if field:
                parts.append(field)
            address = owner
        parts.append(str(address) if not isinstance(address, int) else self.objects.get(address, {}).get('type', hex(address)))
        return ' -> '.join(reversed(parts))

    def export_rows(self):
        rows = []
        for address, obj in self.objects.items():
            row = dict(obj, path=self.path(address), incoming_references=len(self.incoming[address]))
            if obj['type'] == 'System.String' and obj['length'] <= 160:
                row['text'] = self.read(address+self.header+4, obj['length']*2).decode('utf-16-le', errors='replace')
            rows.append(row)
        return sorted(rows, key=lambda r: -r['bytes'])

    def pooled_buffers(self):
        rows = []
        for address, obj in self.objects.items():
            t = obj['type_index']
            if self.flags[t] & 3:
                continue
            visited = set()
            while t >= 0 and t not in visited:
                visited.add(t)
                for f in self.field_indices[t]:
                    ft = self.field_types[f]
                    if self.field_static[f] or not self.flags[ft] & 1 or not self.names[ft].startswith('LightSide.PooledBuffer<'):
                        continue
                    fields = {self.field_names[i]: self.field_offsets[i]-self.header
                              for i in self.field_indices[ft] if not self.field_static[i]}
                    base = address+self.field_offsets[f]
                    data = self.number(base+fields['data'], self.pointer)
                    count = self.number(base+fields['count'], 4, True)
                    array = self.objects.get(data)
                    if array:
                        rows.append(dict(owner=address, path=self.path(address)+' -> '+self.field_names[f],
                                         array=data, type=array['type'], count=count, capacity=array['length'],
                                         bytes=array['bytes']))
                t = self.bases[t]
        return sorted(rows, key=lambda r: -r['bytes'])

    def shared_pool_counts(self):
        rows = []
        for t, name in enumerate(self.names):
            if not name.startswith('LightSide.ArrayPool<') or not self.static_bytes[t]:
                continue
            for f in self.field_indices[t]:
                if self.field_names[f] == 'sharedCounts' and self.field_static[f]:
                    offset = self.field_offsets[f]
                    data = int.from_bytes(self.static_bytes[t][offset:offset+self.pointer], 'little')
                    obj = self.objects.get(data)
                    if obj:
                        for i in range(obj['length']):
                            count = self.number(data+self.array_header+i*4, 4, True)
                            rows.append(dict(pool=name, bucket=i, available_arrays=count))
        return rows

    def native_links(self, allocations):
        allocations = sorted(allocations, key=lambda r: r['Address'])
        starts = [r['Address'] for r in allocations]
        links = defaultdict(set)
        offsets = defaultdict(set)
        for pointer, owner, field in self.native_pointers:
            i = bisect.bisect_right(starts, pointer)-1
            if i >= 0:
                allocation = allocations[i]
                if pointer < allocation['Address']+allocation['Size']:
                    path = self.path(owner) if isinstance(owner, int) else owner
                    links[i].add(path+' -> '+field)
                    offsets[i].add(pointer-allocation['Address'])
        return [dict(allocations[i], managed_paths=' | '.join(sorted(paths)),
                     pointer_offsets=' | '.join(map(str, sorted(offsets[i])))) for i, paths in links.items()]
