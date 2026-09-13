# Memory snapshot analysis

Read-only analysis of Unity queried `.snap` files using Python's standard library. No Unity editor process, package embedding, compilation, or external dependency is required. Input snapshots are never modified.

Run from the host project root:

```powershell
python Tools/MemorySnapshots/analyze.py MemoryCaptures
```

`--output <directory>` selects the report directory. `--package <directory>` selects the installed Memory Profiler source used for the entry schema. Otherwise the reader finds the embedded package or the single cached package. Snapshots are processed in filename order; the caller supplies the meaning of each capture.

The accounting reader supports snapshot formats 16–18 and 64-bit managed heaps. It has read Android/IL2CPP/Vulkan captures from Unity 2022.3.76f1 (format 16) and 6000.5.2f1 (format 18), using Memory Profiler 1.1.12's schema. Other versions require checking their accounting layouts before extending support.

## Outputs

`MemoryCaptures/Analysis` contains cross-capture comparisons and one directory per snapshot. All sizes in CSV/JSON are **bytes**, not rounded display values.

| File | Meaning |
|---|---|
| `comparison.json` | Capture metadata, counters, grouped accounting views |
| `provenance.json` | Input SHA-256 hashes and schema package/version |
| `*_comparison.csv` | Category bytes, counts, and consecutive deltas |
| `summary.json` | One capture's metadata and totals |
| `native_objects.csv` | Native objects, Unity-reported sizes, graphics resources by root, supported texture metadata |
| `graphics_resources.csv` | Captured graphics resources with root labels and associated native objects |
| `native_allocations.csv` | Captured native allocations and their root labels |
| `native_allocation_owners.csv` | Native allocation totals by area and owner label |
| `native_managed_owners.csv` | Typed managed pointer fields that point inside captured native allocations, with pointer offsets |
| `native_roots.csv` | Native root references and their recorded accumulated sizes |
| `managed_objects.csv` | Reachable objects, shallow sizes, types, array lengths, first discovered root paths |
| `managed_references.csv` | All discovered managed reference edges for auditing alternate paths |
| `managed_root_paths.csv` | Shallow object bytes grouped by first discovered root path |
| `managed_unresolved_references.csv` | Non-null strong references that the crawler could not resolve |
| `pooled_buffers.csv` | UniText/Core `PooledBuffer<T>` fields: logical count, array capacity, shallow bytes |
| `shared_pool_counts.csv` | Captured `ArrayPool<T>.sharedCounts` values per bucket |
| `system_regions.csv` | Raw captured OS mappings, mapped sizes, resident sizes |
| `system_resident.csv` | Resident totals by original mapping name/type |
| `system_resident_normalized.csv` | Same view with Android installation directory identifiers normalized |
| `allocators.csv`, `memory_labels.csv` | Raw Unity allocator and memory-label accounting |

Empty tables produce empty files. Texture metadata is decoded only for known Texture2D and RenderTexture layouts. Other objects still retain their captured name, type, size, and graphics references.

## Interpretation limits

- These are **overlapping accounting views**. Do not add managed bytes, native allocations, native object sizes, graphics resource sizes, profiler counters, and OS resident bytes into one total. A native object's recorded size may already include its graphics resources.
- Resident memory is the sum of the snapshot's `SystemMemoryRegions.Resident` values. It is not Android PSS, virtual address space, or the size of all reachable objects.
- Managed traversal starts at captured static fields and GC handles, follows typed references including embedded structs, and counts each resolved object once. Sizes follow the package's object/array/string conventions and exclude allocator rounding. It does not conservatively scan raw heaps, infer stack-only roots, or reconstruct unavailable thread-static roots. Unreachable heap bytes are not automatically garbage: the difference also includes allocator and root-coverage effects.
- Zero-length arrays need no element layout; their captured element type may be absent. Nonempty arrays with an unknown element layout remain unresolved.
- First root paths are evidence of reachability, **not exclusive ownership or dominator retained sizes**. Use `managed_references.csv` for alternate references. `pooled_buffers.csv` covers direct struct fields, including inherited fields; buffers embedded in arrays or deeper value structs are not enumerated there.
- Native pointer matches show a reference into an allocation, not an exclusive owner or an allocation call stack. Numeric integer fields are not treated as pointers. Native allocations outside Unity's recorded allocator tables cannot be reconstructed from these captures.
- The supplied captures contain no native allocation sites/call stacks and no managed stack bytes. A category such as `IL2CPP VM` cannot be reliably subdivided into individual native functions from this data.
- Root and allocation addresses are process-local. Check each capture's session ID before comparing addresses. The six customer captures in `MemoryCaptures/snaps` each come from a separate launch; compare their named phases, not their addresses or filename-order deltas.

## Format sources

The reader derives its entry names and record layouts from the installed package under `Editor/MemorySnapshot/Reader/QueriedSnapshot/`. Managed layout rules are cross-checked against the package's managed crawler and string helpers; texture layouts come from `Editor/Containers/MetaDataHelpers.cs`.

The analysis output stays inside the host project's already ignored `MemoryCaptures` directory. It contains object names, reference paths, and short string previews from the supplied application memory.

## Startup APK comparison

`startup.py` inventories the two APKs associated with the customer captures. Run the accounting reader first to create `Analysis/provenance.json`, then:

```powershell
python Tools/MemorySnapshots/startup.py --tmp-apk <TMP.apk> --unitext-apk <UniText.apk> --captures MemoryCaptures/snaps --output MemoryCaptures/snaps/StartupAnalysis
```

The reader supports IL2CPP metadata version 31 and little-endian AArch64 ELF64. Metadata record layouts are checked against [Il2CppDumper's format definitions](https://github.com/Perfare/Il2CppDumper/blob/master/Il2CppDumper/Il2Cpp/MetadataClass.cs). Unsupported layouts fail explicitly.

Outputs include APK hashes, assembly/type/method inventories, assembly references, runtime initializer declarations, ELF sections and LOAD segments, and captured startup type populations. This capture pairing uses `TMP_berfore_2.snap` and `UT_before_2.snap`; the caller must confirm that the APKs produced those captures.

Method counts are metadata definitions, not unique machine-code bodies or bytes. Type labels do not reconstruct nested declaring-type names. Assembly references are not linker retention paths. Initializer declarations may include stripped entries and do not prove execution. Captured types are not live object counts or proof that their static constructors ran.

`captured_il2cpp_segments.csv` infers executable status from corresponding APK LOAD segments, ordered by address and checked against mapping dimensions. Snapshot mappings do not supply permissions. Resident bytes are measured; this classification is inferred. File sizes, captured types and allocator totals must not be converted into per-assembly Resident estimates.
