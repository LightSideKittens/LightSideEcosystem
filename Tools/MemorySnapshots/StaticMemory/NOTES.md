# Static memory: working notes

Running log for the effort to cut UniText's permanent (startup) memory on Android. Numbers are MiB
unless stated otherwise. Kept because several plausible hypotheses have already been refuted by
measurement, and re-testing them is pure waste.

## Bench

`C:\C-UnityProjects\uni-test-main` — the reporter's MemTest bench, wired to this repository:
`Packages/media.lightside.unitext` and `media.lightside.core` there are directory junctions onto this
working tree, so an edit here is what the bench builds.

Three variants, each an isolated Android IL2CPP ARM64 Development build, Managed Stripping Level
High, Unity 6000.6.0f1:

| Variant | uGUI | Text library |
|---|---|---|
| Baseline | LightSide fork 2.6.1 (no TMP) | none |
| UniText | LightSide fork 2.6.1 (no TMP) | UniText, this working tree |
| TMP | stock 2.6.0 (TMP inside) | TextMeshPro |

The fork must be installed as a local package: `com.unity.ugui` ships inside the editor from Unity 6
on, and a scoped registry cannot override a built-in package.

One iteration: `Tools\MemTest\iterate.ps1 -Label "..."`. Builds, installs, launches, samples the
process at 8 s (empty screen) and 45 s (settled after 54 000 text changes), appends a row to
`Tools\MemTest\results.csv`. About 3.5 minutes.

## Noise floor

Range across three runs of an identical APK:

| metric | range |
|---|---:|
| pss_empty | 0.87 |
| pss_settled | 2.64 |
| il2cpp_empty | 0.31 |
| metadata_empty | 0.13 |
| anon_settled | 1.10 |
| gpu_settled | 0.09 |

Total PSS is too noisy for small changes. Treat `il2cpp_empty` and `metadata_empty` as the primary
signals and call a change real only past 0.6 and 0.3 respectively.

**That range is the usual case, not the bound.** Eight builds whose managed output differed by at
most 1.7% of method count gave `il2cpp_empty` 20.01, 20.14, 20.07, 19.75, 20.18, 20.31, 19.85 — and
once **13.77**, 6.2 below the rest, with `pss_empty` 16 lower and `native_empty` at 0.29 against
0.66–0.78 everywhere else. The same source rebuilt gave 19.85. The sample is taken at a fixed 8 s, so
a run where startup happens to be behind reports pages that have not been faulted in yet.

Rules that follow:

- **One run proves nothing.** Repeat any result before acting on it, and treat a low `native_empty`
  as the tell that the run was sampled early.
- **Prefer static evidence.** What leaves `Library/Bee/artifacts/Android/ManagedStripped`, and the
  type/method/IL counts in it, are exact. Use residency only to size what static counts cannot.
- A result is safe when the before and after populations occupy separated bands across several runs,
  not when two single runs differ.

## Where we stand against TMP — Release builds

The numbers to quote. Both libraries rebuilt from current source as **Release** (no profiler, no
`Inspection` assembly) and alternated three times each through `Tools\MemTest\compare-release.ps1`.
A release build is not debuggable, so `run-as ... /proc/<pid>/smaps` is refused and these come from
`dumpsys meminfo`, which is what a customer reads anyway. Spread across runs is hundredths of a MiB.

Empty screen, before any text exists:

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| **Total PSS** | **142.45** | **150.86** | **+8.41** |
| Code | 39.47 | 47.35 | +7.88 |
| Native Heap | 11.30 | 14.03 | +2.73 |
| Graphics | 43.61 | **41.69** | **−1.92** |

Under load, 300 texts:

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| **Total PSS** | **276.16** | **212.06** | **−64.10** |
| Code | 40.84 | 53.53 | +12.69 |
| Native Heap | 10.96 | 15.82 | +4.86 |
| Graphics | 75.98 | **52.24** | **−23.74** |
| Java Heap | 2.05 | 1.14 | −0.91 |

**Having UniText in a project costs 8.4 MiB; using it saves 64.** Even on the empty screen the GPU
side is already 1.9 lighter; under load that grows to 23.7. The whole of the startup gap is code and
the native shaping stack — HarfBuzz and FreeType, which TMP has no equivalent of and which cannot go.

The release APK is 23.5 MB against 44.8 for the development build, and the +8.41 gap matches the
+9.0 measured on development builds, so the development-mode conclusions in this file hold: the
profiler overhead falls on both libraries alike.

## Where we stand against TMP — development builds

Both libraries rebuilt from the current source and alternated three times each through
`compare-apks.ps1`, which records the device's own memory state with every sample.

Empty screen, before any text exists (8 s):

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| resident code | 13.29 | 19.97 | **+6.68** |
| metadata | 3.20 | 5.40 | **+2.20** |
| our native plugins | 0 | 0.69 | +0.69 |
| process RSS | 226.6 | 235.6 | **+9.0** |

Under load, 300 texts (25 s):

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| process RSS | 349.7 | **305.4** | **−44.3** |

UniText's spread across three runs is 0.3–0.4; TMP's reaches 5. No band overlaps. Having UniText in
a project costs 9 MiB; using it saves 44.

Of the 6.68 code gap, 0.69 is HarfBuzz and FreeType, which TMP has no equivalent of and which cannot
go — the typography rests on them.

## Where the memory is

Empty screen, UniText minus TMP: **+13.6 PSS**, of which `libil2cpp.so` +9.0, `global-metadata.dat`
+3.0, native allocator +2.7, our native plugins +0.6. Under load UniText is **54 lighter** than TMP
(anonymous −56, GPU −23), so the dynamic side is already won; the startup cost is the target.

Of the startup delta roughly 12 is file-backed (code and metadata pages, evictable under pressure)
and 2.7 is anonymous and genuinely unreclaimable.

Retained after stripping, IL KB: `LightSide.UniText` 1801, `System` 588, `LightSide.Core` 327,
`System.Core` 52, `LightSide.Motion` 10. Our own assembly holds 1389 types and 11 974 methods
against TextMeshPro's 109 and 1 100 in the same build. Metadata scales with those counts, and IL2CPP
emits code per method, so the count is the lever, not any single fat type — the heaviest type is 7%
of our IL.

Where those methods live, by top folder under `Runtime` (stripped assembly, method share):
`FontCore` 12.0%, `Core/Component` 10.6%, `StyleCore/*` 32.4% across fourteen folders, `Core` 6.1%,
`Selection` 6.1%, `Editing` 6.1% plus `Editing/*` 3.9%, `NativePlatform` 3.8% plus `NativePlatform/*`
0.9%, `Dropdown` 1.9%, `Text` 2.4%, `Unicode/*` 3.3%, `EmojiCore` 0.9%.

### Reading the dependency dump

`--dump-dependencies` records the edge that **first** marked each item, so the file is a marking
tree, not the full dependency graph. A path in it is a real retention chain, but deleting a node from
it and recounting what becomes unreachable proves nothing: alternative paths were never recorded.
Only a rebuild measures what a cut is worth.

## Confirmed

- **36 automatic startup registrations against TMP's 12**, 24 of them ours (14 in Core, 9 in UniText,
  1 in Inspection). These are executed roots: everything reachable from them survives stripping and
  runs before the first text exists. Includes four input backends, the world-text batcher, the
  embedded font catalog and a file logger.
- `UniTextInspector.InstallHotkeyListener` ships because the bench builds Development. Release will
  measure lower; every Development number here is inflated by the Inspection assembly.
- **The package ships `Samples`, not `Samples~`.** Without the tilde Unity imports the folder as
  ordinary package content: 210 files, 56 scripts behind two assembly definitions
  (`LightSide.UniText.Samples`, `…Samples.EditableText`), four scenes and font assets of which one
  is 17 MB. Every consumer project compiles and imports all of it. The sample assemblies do not
  reach the player — the linker drops them — so this is an editor-side cost: compile time, import
  time and project size. `package.json` points its four sample entries at `Samples/...`; the
  convention is `Samples~/...`, from which Package Manager copies on demand into `Assets/Samples`.
- Extra assemblies over TMP and what holds them: `System.Xml` ← two `XmlReader.Create` calls for
  Android system fonts; `Mono.Security` and `System.Numerics` ← `System.Xml`, through
  `XmlUrlResolver.GetEntity` → `WebRequest` → the TLS stack → `BigInteger`;
  `UnityWebRequestModule` ← `FileDocumentSource`; `AssetBundleModule` ← `EmbeddedFontCatalog`;
  `JSONSerializeModule` ← clipboard fragments; `IMGUIModule` ← clipboard and managed-input
  fallbacks; `LightSide.Motion` ← easing and playback; `PhysicsCore2DModule` ← `WorldPointerRaycaster`.
- **UniText's only unusual BCL entry points are three.** Every edge from a `LightSide.*` method into
  `System.Net`, `System.Xml`, `System.Security.Cryptography`, `System.Numerics`, `Mono.*`,
  `System.Text.RegularExpressions`, `System.Runtime.Serialization` and `System.Linq.Expressions`, read
  off the dependency dump: `XmlReader` (gone), `IncrementalHash`/`HashAlgorithmName`/
  `CryptographicOperations` in `Zstd`, and two `WebUtility.HtmlDecode` calls in the clipboard. Nothing
  else. `FontFileCache`'s `SHA256.Create()` is not reachable in this bench.

## Why the editing surface is retained

Answered by the linker itself, not by inference. Re-run UnityLinker with the response file the build
left in `Library/Bee/artifacts/rsp` (the one mentioning `ManagedStripped`), redirecting `--out` and
adding `--enable-report --dump-dependencies`; it writes `linker-dependencies.xml.gz`, an edge list of
`b` (what marked) → `e` (what got marked). Walking it backwards from any method gives the exact
retention chain.

For `UniTextEditable::Paste`:

```
ROOT  Unity.Linker.Old.UnityDependencyInfo
  →  NativeInputAndroid/MessageReceiver::OnEditorAction(string)
  →  NativeInputReporter::ReportEditorAction
  →  UniTextNativeInput::DispatchEditorAction
  →  INativeInputRecipient::ReceiveEditorAction
  →  NativeInputSession/EditableInputContext::ReceiveEditorAction
  →  NativeInputSession::OnEditorAction
  →  NativeInputSession::ExecuteAction(UniTextEditable, …)
  →  UniTextEditable::ExecuteWithCompletion → PasteAsync → Paste
```

The root is Unity's own dependency info preserving the Android JNI callback, which Java calls. From
there the chain reaches the whole editing pipeline. This is why disabling `NativeInputAndroid.Register`
changed nothing: the registration is not the root, the receiver is.

Marking a rooted type is cheap on its own — `UniTextEditable` yields only its module, its base and
its `.cctor`, and `UniTextDropdown` yields no methods at all, which is why it stays light. The weight
comes from chains like the one above.

That chain is real, and it is redundant. Cutting it alone moves nothing (see *Refuted*). Re-running
the linker on the cut build exposes the second, shorter path:

```
ROOT  Unity.Linker.Old.UnityDependencyInfo
  →  NativeInputSession::Initialize()                    [RuntimeInitializeOnLoadMethod]
  →  UniTextEditable::add_EditingSessionRequested(…)
  →  UniTextEditable in full  →  UniTextSelectable  →  context menu  →  clipboard adapters
```

`NativeInputSession.Initialize` subscribes infrastructure to a feature's static events:

```csharp
UniTextEditable.EditingSessionRequested += Request;
UniTextEditable.EditingSessionReleaseRequested = Release;
UniTextEditable.EditingSessionAbortRequested  = Abort;
UniTextEditable.CompositionCommitRequested    = CommitComposition;
```

The dependency points the wrong way. The session is a platform primitive; `UniTextEditable` is the
feature that needs one. With the arrow inverted — the editable asking the session when it activates —
no startup root names `UniTextEditable`, and it is retained only by projects that actually reference
it. Whether that is worth doing is what the combined cut measures.

## Landed

### 1. `System.Xml` removed — −2.12 code, −0.99 metadata

`AndroidFontsXmlResolver` and `SystemEmojiFont` were the only two `XmlReader` users. Both now read
AOSP `fonts.xml` through `FontCore/Platform/AndroidFontsXml.cs`, a pull scanner written against the
grammar those files use, with the depth and `IsEmptyElement` conventions the callers were already
written for. Cost: 14 types, 30 methods, 8.3 KB IL of our own.

Static evidence, which is exact: `System.Xml.dll`, `Mono.Security.dll` and `System.Numerics.dll` all
left `Library/Bee/artifacts/Android/ManagedStripped` — 590 types, 4 743 methods, 382 KB IL of BCL.

Residency, as separated bands rather than a single pair of runs — ten builds before the change,
eight after, every other difference between them under 2% of method count:

| metric | before (range) | after (range) |
|---|---|---|
| il2cpp_empty | 21.71 – 22.45 | 19.75 – 20.31 |
| metadata_empty | 6.34 – 6.52 | 5.32 – 5.42 |

About −2.1 resident code and −1.0 metadata. The startup gap to TMP on those two mappings went from
+12.02 to about +8.9. One post-change run reported 13.77 / 4.78; it is the outlier described under
*Noise floor* and is excluded.

Unverified by execution: the bench disables `SystemFont` and `EmojiFont`, so neither new parser runs
in it. Correctness rests on reading. The scanner is exercised only on Android device builds.

## Why the editing surface is retained — the answer

Every Unity message on every `MonoBehaviour` in `LightSide.UniText` is a linker **root**, and the
dependency dump records no reason for it. Checked on the build where five separate retention paths
were cut at once:

| type | rootless messages | marked by an edge |
|---|---|---|
| `UniTextEditable` | Awake, OnEnable, OnDisable, OnDestroy, OnApplicationFocus, OnApplicationPause, OnRectTransformDimensionsChange | none |
| `UniTextSelectable` | Awake, OnEnable, OnDisable, OnDestroy | none |
| `UniTextDropdown` | Awake, OnEnable, OnDisable | none |
| `UniTextContextMenu` | Awake, OnDestroy | none |
| `UniTextWorld` | OnEnable, OnDisable, OnDestroy | none |
| `UniTextMagnifier` | Awake, OnDestroy | none |

`UniTextMagnifier` is not in `TypesInScenes.xml`, so this is not the type list doing it. The engine
calls these by name from native code, so Unity preserves them for every `MonoBehaviour` in a root
assembly — and `LightSide.UniText.dll` is passed as `--include-unity-root-assembly`.

Their bodies are the doors: `UniTextEditable::OnEnable` reaches `OnStyleGraphChanged` and from there
499 of its own method nodes, the selection, the clipboard and the context menu.

**This is why every cut measured zero.** `Editing` stayed at exactly 726 methods and `Selection` at
727 through every combination tried: the startup hook's body, the `UniTextEditable` event wiring, the
three static `UniTextEditable` fields, the `List<PlaceholderDecorator>` scratch buffer, the sixteen
`[Preserve]` JNI handlers, the interface dispatch behind them, and the package's default prefabs —
alone and together. Each was a real path, none was the anchor. Only code physically deleted moved the
count.

Correction to an earlier note in this file: "`UniTextDropdown` yields no methods at all" was read off
dump reachability. The stripped assembly keeps 162 of its methods. Reachability in the dump is not
retention.

The consequence: retained size follows **which component types the assembly contains**, not any call
graph inside it. No decoupling within `LightSide.UniText` can change it. Two levers remain, and the
first subsumes the second:

1. The component type must not live in a root assembly. Move editing, selection, dropdown and the
   native input into a package assembly a label-only project never names, and the linker deletes it
   whole — messages included, as it already does for `LightSide.UniText.Inspection` and
   `Unity.VisualScripting.Core` despite their startup attributes.
2. Thin message bodies help only if what they call is itself unreachable, which returns to 1.

The default prefabs are a precondition for 1, not a saving on their own: while one of them names a
type, that type's assembly is a root assembly again.

### 2. Editing, selection and native input moved to their own assembly

`LightSide.UniText.Interaction` now holds `Editing/`, `Selection/` and `NativePlatform/`;
`LightSide.UniText.Dropdown` holds the dropdown. Six types stayed in the core because the label path
uses them: `ITextDocument`, `EditShape`, `GraphemeNavigator`, `SelectionHitTest`, `UniTextFocusable`
and `UniTextCursor`. The one core → editing edge that remained, `UniTextFocusable` asking for the
components on a host and for the key stream, became the receptor `IFocusInteractionSource`.

Retained managed code:

| | types | methods | IL |
|---|---:|---:|---:|
| one assembly | 1389 | 11 974 | 734.2 KB |
| split, default prefabs present | 1325 | 11 363 | 714.7 KB |
| split, nothing naming the split types | **891** | **7 576** | **482.8 KB** |

Measured on device by `Tools\MemTest\compare-apks.ps1`, two prebuilt APKs alternated three times
each. The arms were *one assembly with the default prefabs* against *split with the editing prefabs
moved out*, so this pair measures the split **and** the naming together:

| | il2cpp | metadata | process RSS |
|---|---:|---:|---:|
| before | 24.25 (24.04 – 24.56) | 5.42 | 307.8 |
| after | **22.38** (22.35 – 22.42) | **4.85** | **301.4** |

**−1.87 resident code, −0.57 metadata, −6.4 process RSS** for the pair, and `libil2cpp.so` went from
37.96 to 34.74 MiB on disk.

**The split alone, with the package shipped as it is, is worth nothing.** `il2cpp_empty` reads 20.01
before it and 19.97 after, inside the noise floor: the default prefabs name the editing types, the
assembly stays a root, and there is nothing to delete. The split bought the *possibility* — 3 787
methods leave the moment nothing names those types — plus the 3.2 MiB of binary, which is
unconditional. Turning the possibility into resident memory needs the assemblies fine-grained enough
that one prefab costs one small assembly, which is what `Dropdown` already demonstrates at 122
methods.

Two things this measurement taught:

- **Sampling at 8 s reads the lighter build as heavier.** It reported `pss_empty` +13.6 for the split,
  reproducibly across three runs, because the lighter build reaches further into startup by then —
  `libunity.so` and the GPU shader compiler show more resident pages, not ours. At 25 s the effect is
  gone. Compare builds late, or compare mappings, never total PSS at a fixed early mark.
- **Removed code is not removed memory.** 251 KB of IL left the binary, worth 3.22 MiB of
  `libil2cpp.so` on disk, but only 1.87 MiB of that was ever resident: `libil2cpp.so` sits at ~54%
  residency either way. Metadata is the opposite — 100% resident, so every byte removed is a byte of
  RAM.

Cost, all of it structural rather than behavioural: `LightSide.Core` had to grant internals to each
new assembly, each new assembly needs its own `InternalsVisibleTo` list for the editor, the menu
constants lost `nameof` for the types that moved, and the interaction assembly must be referenced by
anything using those components. One product-visible consequence is recorded in
`IFocusInteractionSource`: without that assembly, interactive ranges keep pointer input but lose Tab,
Return and Escape.

`Editing`, `Selection` and `NativePlatform` are one assembly rather than three because they are
mutually cyclic today: editing uses the clipboard, keyboard and composition from native input, native
input holds `static UniTextEditable`, and selection reaches into the clipboard adapters. Splitting
them further means breaking those two cycles first.

## How UnityLinker treats a whole assembly

Measured on this build's own artifacts, not from documentation. Both halves matter for any plan that
moves code into a separate `.asmdef`.

**An assembly nothing references is deleted, and its startup roots go with it.** Seventeen assemblies
were compiled into `Library/Bee/PlayerScriptAssemblies` and are absent from `ManagedStripped`, among
them `LightSide.UniText.Samples`, `LightSide.UniText.Inspection` and `Unity.VisualScripting.Core`.
`Unity.VisualScripting.Core` carries five `[RuntimeInitializeOnLoadMethod]` and two `[Preserve]`;
`LightSide.UniText.Inspection` carries one. Neither saved its assembly. So those attributes do not
root an assembly that nothing else pulls in.

**Naming one type in a link.xml resurrects the assembly and fires those roots.** Feeding UnityLinker
the same inputs plus

```xml
<assembly fullname="LightSide.UniText.Inspection">
	<type fullname="LightSide.Inspection.UniTextInspector" preserve="nothing"/>
</assembly>
```

brings the assembly back with **79 of its 81 methods**, `InstallHotkeyListener` among them, body
intact. `preserve="nothing"` limits what that *entry* preserves; it does not stop the assembly's own
roots from being collected once the assembly is in the build.

The consequence for splitting editing, selection, dropdown and native input into their own assembly:
it pays only if `TypesInScenes.xml` stops naming `UniTextEditable`, `UniTextSelectable`,
`UniTextDropdown`, `UniTextDropdownItem`, `UniTextContextMenu` and `UniTextSelectionHandles`. While
it names any of them, the new assembly survives, `NativeInputSession.Initialize` runs, and the
editing surface comes back with it.

### What puts a managed type in that list — the package's own default prefabs

`Library/Bee/artifacts/UnityLinkerInputs/EditorToUnityLinkerData.json` is the source; the managed
entries carry `managedAssemblyName` and `fullManagedTypeName` and no `usedInScenes`, so they are not
"types found in scenes". A type is listed when an instance of it exists **anywhere in the asset
database**, which includes the package.

This is not about what ships. The build's own used-asset report shows no `.prefab` reaching the
player at all, and `Assets/UniText/Resources/UniTextSettings.asset` ships at 0.2 kB with its
editor-only prefab references gone. The list is computed in the editor, before stripping, from what
is *imported*.

`Packages/media.lightside.unitext/Defaults/` ships thirteen prefabs — `Text`, `Button`, `Dropdown`,
`World Text`, `Document View`, `DocView Both Axes`, and under `Editing/`: `Editable Text`,
`Selectable Text`, `InputField`, `ContextMenu`, `InsertionHandle`, `SelectionHandle`,
`SelectionHandles`. Every consumer project imports them, because a package is part of the asset
database. Measured by moving them out of the package and rebuilding:

| | LightSide types listed |
|---|---:|
| prefabs present | 19 |
| prefabs removed | **8** |

Gone: `UniText`, `UniTextEditable`, `UniTextSelectable`, `UniTextDropdown`, `UniTextDropdownItem`,
`UniTextContextMenu`, `UniTextSelectionHandles`, `UniTextDocumentView`, `UniTextDocumentLoader`,
`UniTextWorld`, `FocusGuard`. What stays is the eight types with `.asset` instances — fonts,
settings, dictionaries, the modifier preset.

Removing the copies under `Assets/UniText` alone changes nothing, which is why this looked refuted
before: the package's own copies remain, and they are enough.

Verified there and back, one variable at a time: 19 listed with the package prefabs present, 8 with
them moved out, 19 again once they were moved back, all six gate types returning. This is a file
Unity writes during the build, not a sample taken off a device, so it does not carry the variance
that `Noise floor` describes.

The same naming happens when a consumer puts `UniTextEditable` on one of their own scenes, or
references it from their own code — and that is the intended boundary, not a hole. The defect today
is that the cost is unconditional: a project that only draws labels pays it because the package's own
prefabs name the types. The change makes the cost follow actual use.

**By itself this is worth no memory**: 1389 types and 11 974 methods either way, because the entries
are `preserve="nothing"` and the editing surface is held by `NativeInputSession.Initialize` in the
same assembly. It is the gate for the split, not a saving.

### The split, end to end

1. Stop importing the default prefabs — `Samples~`, or another form the owner prefers. The settings
   fields already document the fallback: *"Falls back to code creation if null."*
2. Fix the fourteen marking edges from `UniTextBase`, `Rope`, `InteractiveModifierBase` and
   `UniTextInteractions` into the four folders.
3. Move editing, selection, dropdown and native input behind their own `.asmdef`.

Only all three together pay. Calibrated against the `System.Xml` result — 382 KB of IL removed gave
−2.1 resident code — the 178 KB of IL in those folders is worth roughly −1, plus the 0.68 the startup
roots cost, since the assembly would no longer exist. Call it **−1.5 to −2 of the current +6.9 gap**
to TMP, for 144 moved files and a change in what a consumer gets out of the box.

## Refuted — do not retry

- **Crypto is not why `Mono.Security` is in the build.** In the player BCL
  (`MonoBleedingEdge/lib/mono/unityaot-linux/mscorlib.dll`) `SHA256.Create()` is literally
  `return new SHA256Managed();` with no `CryptoConfig`, and `IncrementalHash.GetHashAlgorithm` is a
  chain of comparisons against concrete types. The September 2026 report concluded otherwise from
  `unityjit-linux` in a different editor version; that does not carry to the AOT player profile.
  They arrived with `System.Xml` and left with it.
- **Tuning `XmlReaderSettings` cannot remove `System.Xml`.** `DtdProcessing` is a property value
  assigned at run time; UnityLinker strips by reachability and cannot fold it. Measured: no movement
  beyond noise. Only deleting the `XmlReader.Create` calls can drop the assembly.
- **The prefab slots in `UniTextSettings` do not root anything in the player.** They are declared
  inside `#if UNITY_EDITOR`. Nulling all nine references in the bench's settings asset changed no
  root, no stripped assembly size and no metric beyond noise.
- **`TypesInScenes.xml` does not explain the retained surface.** Its 17 UniText entries carry
  `preserve="nothing"` and `"usedInScenes": []`: the type declaration is kept so deserialization
  resolves, no member is protected.
- **Severing the JNI callbacks alone is worth nothing.** All fifteen `UniTextNativeInput.Dispatch*`
  bodies were stripped of their `source.Recipient.Receive*` call, cutting every path from the sixteen
  `[Preserve]` handlers into `INativeInputRecipient`. Result: 1389 → 1384 types, 11 974 → 11 852
  methods; `il2cpp_empty` +0.13, `metadata_empty` −0.01, both inside the noise floor. `Editing`,
  `Selection` and `Dropdown` did not move at all. The chain in *Why the editing surface is retained*
  is real but redundant.
- **The native-input startup roots are worth 0.68, not 6.3.** Measured properly: two prebuilt APKs
  alternated three times each through `Tools\MemTest\compare-apks.ps1`, which records `MemAvailable`
  and the pressure counters with every sample. Uncut 20.07 / 20.45 / 20.26 (mean 20.26); all five
  roots cut 19.66 / 19.54 / 19.54 (mean 19.58). Separated bands, so the effect is real and small —
  the same 0.68 the earlier lazy-registration experiment reported. `MemAvailable` fell from 1647 to
  1595 MiB over the session and the stall counter rose monotonically, which is exactly the drift the
  interleaving cancels.
- **The native-input startup roots cost nothing beyond that.** `ManagedInputBackend.Register`,
  `NativeInputAndroid.Register`, `NativeInputSession.Initialize` and `NativeKeyInputSession.Initialize`
  were neutralised in every combination — each alone, both `Initialize` bodies together, and all four
  plus the JNI handlers. Seven of the eight runs land in 19.75–20.31 `il2cpp_empty`. Instrumented
  timings on device: `Register` 0.01–0.02 ms each, `NativeKeyInputSession.Initialize` 0.08 ms,
  `NativeInputSession.Initialize` 2.6–5.8 ms (of which `Reset` almost all, `SetImeEnabled` 0.25 ms).
  No input session is opened and no backend is constructed during startup — `OpenInput` never fires
  in the first 16 s, so `NativeInputAndroid.Setup` and its JNI bridge never run.
- **The one run that showed −6.3 was an outlier, not an effect.** It reported `il2cpp_empty` 13.77
  and `pss_empty` 176.23 with `native_empty` 0.29 against 0.66–0.78 everywhere else. Rebuilding the
  identical cut set gave 19.85 / 192.01 / 0.67. See *Noise floor*.
- **`LightSide.Motion` is not worth removing.** 10.5 KB after stripping.
- **Lazy startup registration does not shrink the build.** Disabling six automatic registrations
  (`UniTextWorldBatcher`, `EmbeddedFontCatalog`, `ManagedInputBackend`, `NativeInputAndroid`,
  `NativeInputSession`, `NativeKeyInputSession`) removed 5 types and 105 methods out of 1387 and
  11 956: those subsystems stay reachable by other paths. It is worth 0.68 of resident code at the
  empty screen, because the code is no longer executed and its pages are not faulted in — a
  different and much smaller mechanism than removing the code.
- **The sample prefabs installed under `Assets/UniText` root nothing.** Removing all thirteen left
  the root list, the stripped assembly and every metric unchanged.
- **Excluding the samples from import changes nothing in the player.** Renaming `Samples` to
  `Samples~` left the 17 roots, 1382 types and 11 851 methods exactly as they were.
- **Interface indirection does not let the linker drop an implementation.** Replacing
  `GetComponent<UniTextEditable>()` / `GetComponent<UniTextSelectable>()` in `UniTextFocusable.Sync`
  with `GetComponents<IUniTextFocusSource>()` left the assembly at 1383 types and 11 858 methods —
  the two extra being the interface itself. `GetComponents<T>` over an interface forces every
  implementation to be kept, so the abstraction buys nothing and costs a per-call component scan.
- **`TypesInScenes.xml` is not driven by anything in the project we could find.** Its 17 UniText
  entries survived nulling the settings' prefab slots, deleting the sample prefabs, excluding the
  package samples and disabling the startup registrations. The build settings list only the three
  MemTest scenes, and none of the project's scenes contains the listed components. The selection
  rule was not identified; it is also not where the weight comes from, so it stopped mattering.

## Open risks

- The bench disables the OS font fallback and emoji (`SystemFont.Disabled`, `EmojiFont.Disabled`) so
  both libraries render from the same TTFs. Any regression in Android system-font or emoji discovery
  is therefore invisible to these measurements. A change to that code needs its own check.
- Correctness is currently judged by reading the code, not by running tests. Each change should note
  what could regress and how a reader would notice.
- One device (SM-G990B, Android 15), one run per configuration, Development builds. Good enough for
  ranking changes against each other, not for a number quoted to a customer.
- The Baseline variant still runs `MemoryTestRunner` while TMP and UniText run
  `StaticSnapshotRunner`, so Baseline is only comparable at the empty-screen point.
- Unity 6 turned `Unity.Mathematics` and `Unity.Burst` into 1-type shims, so the large retained
  surface those packages showed on the customer's 2022.3 build does not exist here.

## Ranked targets

Everything tried so far that is not code removal has measured as zero. Rank by what leaves the
stripped assembly, and verify with static counts before spending a run on residency.

1. **`StyleCore/*` — 32.4% of retained methods** across fourteen folders, the largest block in the
   assembly and never yet examined. Find what roots it and whether any of it is optional.
2. Split editing, selection, native input and dropdown into an assembly a project need not
   reference. 22% of methods, 144 files moved. Only fourteen marking edges cross into those folders
   from outside, from five types (`UniTextBase`→`SelectionHitTest`, `Rope`→`EditShape`,
   `InteractiveModifierBase`→`UniTextCursor`/`CursorType`, `UniTextInteractions`→`UniTextFocusable`),
   so the boundary is genuinely thin. The rest is rooted from inside: six `TypesInScenes` type
   declarations and the startup roots.
3. `WebUtility.HtmlDecode`, two clipboard call sites. Small; pulls `System.Net`'s named-entity table.
4. Decide whether optional features should be opt-in through settings instead of switching
   themselves on because an engine module happens to be present. Product decision, not a fix.
