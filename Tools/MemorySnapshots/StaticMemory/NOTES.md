# Static memory: working notes

> ## Never wait on a build task. Poll its log.
>
> A Unity batch build that fails **hangs instead of exiting**: `Start-Process -Wait` keeps waiting on
> `VBCSCompiler`, the Roslyn daemon Unity's pre-warm spawns, which never exits on its own. The task
> notification therefore never arrives, and any wait on it blocks until it is killed by hand.
>
> Read `Builds/MemTest/logs/build_<Variant>.log` instead, in a single non-blocking call:
> compare its mtime against the previous one, then grep for `error CS`, `Scripts have compiler errors`
> and `Build <Variant>:`. Poll in separate turns; never sit in a wait loop.
>
> **Check the mtime before believing a result line.** The script truncates the log only once Unity
> starts, so for the first seconds after launching, the previous run's log is still on disk complete
> with its `Build <Variant>: Succeeded`. A poll started too early matches that instantly and reports
> the old build as the new one. It happened; the tell was that the message text belonged to a version
> of the code that had already been edited.
>
> When a build does hang, `Stop-Process` the `dotnet.exe` running `VBCSCompiler` — the script's
> `finally` then runs and restores the project. Do **not** empty `MemTestHidden~` by hand; that is how
> files were lost once already.

Running log for the effort to cut UniText's permanent (startup) memory on Android. Numbers are MiB
unless stated otherwise. Kept because several plausible hypotheses have already been refuted by
measurement, and re-testing them is pure waste.

The UI Toolkit half of this effort has grown its own explanation:
[UI-TOOLKIT-MECHANISM.md](UI-TOOLKIT-MECHANISM.md) describes how Unity resolves packages, compiles,
strips and links, with no numbers. Read it first if the measurements below look contradictory; this
file holds the evidence, that one holds the model.

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

### How much of that gap is waste

The bench never edits text, yet the editing module is in the build. Two release APKs differing only
in whether the default prefabs exist, measured both ways round so the device's downward drift over a
session cannot favour either arm:

| arm order | editing costs |
|---|---:|
| with editing measured first | 2.88 PSS, 1.23 code |
| without editing measured first | 3.15 PSS, 1.29 code |

**About 3.0 MiB of PSS, of which 1.26 is code and 0.6 the native heap**, plus 1.0 MB of APK. So a
third of the 8.41 startup gap is a module the scene never uses. The remainder is the shaping stack
(+2.73 native heap, which TMP has no equivalent of) and the rendering engine itself.

> **Which prefabs.** The ones in `Packages/media.lightside.unitext/Defaults/`, not the copies the
> package makes under `Assets/UniText/` — see *What puts a managed type in that list*. Removing them
> is a precondition, not a saving on its own: in the one-assembly configuration the assembly stayed a
> root because the label components live in it too. Only the split plus the naming pays, which is the
> pair measured in *The split, end to end*.

Worth noting what this says about residency: 2 132 methods — 19% of the retained managed code —
account for 1.26 MiB of resident code, far less than their share of the binary. Code that never runs
is never paged in. The 0.6 MiB of native heap is the surprise: something in the editing module
allocates during startup even with no editable present, and that has not been traced.

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
`System.Core` 52, `LightSide.Motion` 10. Our own code is 1389 types and 11 974 methods against
TextMeshPro's 109 and 1 100 in the same build — on `ed3806e4` the same code reads
`LightSide.UniText` 1 072 / 9 109 plus `Interaction` 182 / 1 780, `NativeInput` 61 / 352 and
`Dropdown` 9 / 122, spread over four assemblies rather than one.

Metadata scales with those counts, and IL2CPP emits code per method, so the count is the lever, not
any single fat type — the heaviest type is 7% of our IL.

Where those methods live, by top folder under `Runtime` (stripped assembly, method share):
`FontCore` 12.0%, `Core/Component` 10.6%, `StyleCore/*` 32.4% across fourteen folders, `Core` 6.1%,
`Selection` 6.1%, `Editing` 6.1% plus `Editing/*` 3.9%, `NativePlatform` 3.8% plus `NativePlatform/*`
0.9%, `Dropdown` 1.9%, `Text` 2.4%, `Unicode/*` 3.3%, `EmojiCore` 0.9%.

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

Every Unity message on every `MonoBehaviour` in a root assembly is a linker **root**, and the
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

The engine calls these by name from native code, so Unity preserves them for every `MonoBehaviour` in
a root assembly — and `LightSide.UniText.dll` is passed as `--include-unity-root-assembly`.

**`TypesInScenes.xml` roots the assembly, not only the type it names.** `UniTextMagnifier` and
`UniTextPasteControl` appear in no asset and in no list, yet their messages are roots: once any listed
type puts the assembly in the build, every `MonoBehaviour` in it keeps its messages. What fills that
list is under *What puts a managed type in that list*.

Their bodies are the doors: `UniTextEditable::OnEnable` reaches `OnStyleGraphChanged` and from there
499 of its own method nodes, the selection, the clipboard and the context menu.

**This is why every cut measured zero.** `Editing` stayed at exactly 726 methods and `Selection` at
727 through every combination tried: the startup hook's body, the `UniTextEditable` event wiring, the
three static `UniTextEditable` fields, the `List<PlaceholderDecorator>` scratch buffer, the sixteen
`[Preserve]` JNI handlers, the interface dispatch behind them, and the package's default prefabs —
alone and together. Each was a real path, none was the anchor. Only code physically deleted moved the
count.

Retained size follows **which component types the assembly contains**, not any call graph inside it.
No decoupling within one assembly can change it. Two levers remain, and the first subsumes the second:

1. The component type must not live in a root assembly. Move editing, selection, dropdown and the
   native input into a package assembly a label-only project never names, and the linker deletes it
   whole — messages included, as it already does for `LightSide.UniText.Inspection` and
   `Unity.VisualScripting.Core` despite their startup attributes.
2. Thin message bodies help only if what they call is itself unreachable, which returns to 1.

Removing the default prefabs is a precondition for 1, not a saving on its own: while one of them names
a type, that type's assembly is a root assembly again.

### The chains, and why cutting them does nothing

Reachability in the dependency dump is not retention: `UniTextDropdown` yields no methods when walked
from a root, yet the stripped assembly keeps 162 of them.

To walk it: re-run UnityLinker with the response file the build left in `Library/Bee/artifacts/rsp`
(the one mentioning `ManagedStripped`), redirecting `--out` and adding
`--enable-report --dump-dependencies`; it writes `linker-dependencies.xml.gz`, an edge list of `b`
(what marked) → `e` (what got marked). Note it records the edge that **first** marked each item, so
it is a marking tree: deleting a node from it and recounting proves nothing, because alternative paths
were never recorded. Only a rebuild measures what a cut is worth.

Two real chains into `UniTextEditable`, both redundant — cutting either moves nothing (see *Refuted*):

```
ROOT  Unity.Linker.Old.UnityDependencyInfo
  →  NativeInputAndroid/MessageReceiver::OnEditorAction(string)     [JNI callback, called from Java]
  →  NativeInputReporter::ReportEditorAction
  →  UniTextNativeInput::DispatchEditorAction
  →  INativeInputRecipient::ReceiveEditorAction
  →  NativeInputSession/EditableInputContext::ReceiveEditorAction
  →  NativeInputSession::OnEditorAction
  →  NativeInputSession::ExecuteAction(UniTextEditable, …)
  →  UniTextEditable::ExecuteWithCompletion → PasteAsync → Paste
```

The root is Unity preserving the Android JNI callback. This is why disabling
`NativeInputAndroid.Register` changed nothing: the registration is not the root, the receiver is.

```
ROOT  Unity.Linker.Old.UnityDependencyInfo
  →  NativeInputSession::Initialize()                    [RuntimeInitializeOnLoadMethod]
  →  UniTextEditable::add_EditingSessionRequested(…)
  →  UniTextEditable in full  →  UniTextSelectable  →  context menu  →  clipboard adapters
```

The second is an ownership inversion worth fixing on its own merits: `NativeInputSession.Initialize`
subscribes infrastructure to a feature's static events.

```csharp
UniTextEditable.EditingSessionRequested += Request;
UniTextEditable.EditingSessionReleaseRequested = Release;
UniTextEditable.EditingSessionAbortRequested  = Abort;
UniTextEditable.CompositionCommitRequested    = CommitComposition;
```

The session is a platform primitive; `UniTextEditable` is the feature that needs one. With the arrow
inverted — the editable asking the session when it activates — no startup root names `UniTextEditable`.

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

## Build-pipeline mechanics, verified

Written down because each was asserted confidently and wrongly first. The rule that fills
`TypesInScenes.xml` is **not** here — it is under *What puts a managed type in that list*, with a
there-and-back experiment. These are the surrounding mechanics that were repeatedly confused with it.

- **Asset inclusion and type preservation are different passes, and only the second costs us.** A
  `[SerializeField]` under `#if UNITY_EDITOR` does not exist in the player's class, so its reference
  is never written to player data and the asset stays out of the build: the package's own
  `UniTextFont` has carried a `TextAsset` field under that guard across 350 users with no effect on
  build size. The prefab GUIDs in `Defaults/UniTextSettings.asset` are therefore not a build
  dependency. The prefabs still cost the whole editing module, because the type list is computed in
  the editor from the asset database, before and independently of what ships.
- **Removing the copies under `Assets/UniText/` is not the lever.** Confirmed again on
  `ed3806e4`: `Assets/UniText/Editing/`, `Dropdown (UniText).prefab` and `DocView Both Axes.prefab`
  moved out, and the re-copy suppressed by creating a project-local `Assets/UniText/Defaults/`
  (`LightSideSettingsGuard.cs:47` skips `CopyMissingDefaults` when the folder it finds is already
  under `Assets/`). Result: `UniTextContextMenu` and `UniTextSelectionHandles` left
  `TypesInScenes.xml`, `UniTextEditable`, `UniTextSelectable`, `UniTextDropdown` and
  `UniTextDropdownItem` stayed, and the stripped output was unchanged to the byte — `LightSide.UniText`
  1 072 / 9 109, `Interaction` 182 / 1 780, `NativeInput` 61 / 352, `Dropdown` 9 / 122. The package's
  own `Defaults/` prefabs are the load-bearing source.
- **Where the copies come from, and why deleting them does not stick.**
  `UniTextSettingsProvider.EnsureDefaults()` → `LightSideSettingsHome.Ensure<UniTextSettings>()` →
  `LightSideSettingsGuard.Ensure<T>` (`:46-48`) → `CopyMissingDefaults` (`:165-187`), which enumerates
  **every asset** in the package's `Defaults/` and copies each missing one into `Assets/<Product>/`
  with no filter. `:178` skips only what already exists, so anything deleted is copied back, and
  `UniTextBuildProcessor.cs:39` calls `EnsureDefaults()` during the build itself. Measured: a rebuild
  logged `[LightSide] Copied 14 default asset(s) to Assets/UniText/.` before the type scan. The copies
  return with **new GUIDs**, so this churns the project — restore originals after any such experiment.
- **`Defaults/` is not one kind of asset and cannot be relocated wholesale.** Editor-only authoring
  templates — the nine prefabs whose settings slots are guarded, instantiated by
  `Editor/UniTextObjectMenu.cs:112` with `Object.Instantiate` rather than
  `PrefabUtility.InstantiatePrefab`, so the link is deliberately broken and no consumer scene ever
  references them — sit beside genuine runtime assets: `SelectionHandle`, `InsertionHandle`,
  `SelectionHandles`, `ContextMenu` and their sprites, plus `Dictionaries/`, the Noto fonts,
  `Materials/`, `ModifierGraphPresets/` and the settings asset. `ISelectionHandles.cs:63` and
  `UniTextSelectionHandles.cs:43,49,253` hold and instantiate the handle prefabs at runtime; moving
  those breaks selection handles in every consumer build.
- **Do not trust a `ManagedStripped/` folder you did not just build.** A leftover from an
  experimental label-only configuration showed `LightSide.UniText.dll` at 1 220 KB with the three
  assemblies absent, and a conclusion was drawn from it that the editing module strips cleanly. A
  rebuild from the committed tree produced the opposite. Check the artifact's mtime against the build
  log first. UnityLinker runs once and the folder is final before the C++ stage: a dump taken then and
  another after the build completes are identical.
- **Do not measure stripping in `LightSideEcosystem`.** That project's own scenes and imported package
  samples name the editing components, so its `font-test` build keeps the whole surface
  (`LightSide.UniText.dll` at 2 366 KB). It is a development project, not a consumer configuration.

## UI Toolkit ships in every player — the mechanism, traced end to end

Unity 6000.6.0f1, bench `uni-test-main`, Android/IL2CPP/ARM64, stripping High. The UIElements
package is **not** in `Packages/manifest.json` and **not** in `packages-lock.json` (which does track
built-in modules — 83 of them are listed). Every UniText reference to the module is behind
`UNITEXT_HAS_UIELEMENTS`, the hub no longer declares the dependency, and the build succeeds with
`errors=0`. The module ships anyway.

**The native side is already clean.** `EditorToUnityLinkerData.json` lists `UIElements` in
`forceExcludeModules`; `UnityLinkerToEditorData.json` reports 25 included modules and UIElements is
not one of them; `UnityClassRegistration.cpp` registers no UIElements class at all.

**The managed side is not.** `UnityEngine.UIElementsModule.dll` is emitted into `ManagedStripped/`
at 1 520 640 B (1 471 types, 10 559 methods, stripped down from the Editor's 2 521 600 B — so the
linker did process it, it simply kept most of it), handed to `il2cpp.exe` on the command line, and
compiled into **29 569 842 B of C++ across 16 files** — the second-largest managed contributor in
the whole player after `LightSide.UniText` itself (41 925 046 B).

**Who roots it — exactly two types.** `UnityLinker_Diagnostics/Roots.log` (re-run of the build's own
`.rsp` with `--enable-report --dump-dependencies`) contains 572 roots, of which two are UIElements:

```
UnityEngine.UIElementsModule: UnityEngine.UIElements.DynamicAtlasSettings
UnityEngine.UIElementsModule: UnityEngine.UIElements.PanelSettings
```

They come from the two link-XML files the Editor generates into
`Library/Bee/artifacts/UnityLinkerInputs/`: `PanelSettings` from `TypesInScenes.xml`,
`DynamicAtlasSettings` from `SerializedTypes.xml`, both with `preserve="nothing"`. No player
assembly references the module — Cecil over every linker input finds only `UnityEngine.dll` (a
forwarder facade), `HierarchyModule` and `VectorGraphicsModule`, and the latter two are themselves
force-excluded.

**What those two roots are worth.** The build's own linker `.rsp` re-run twice, identical except
that the second run's `TypesInScenes.xml` and `SerializedTypes.xml` had their
`<assembly fullname="UnityEngine.UIElementsModule">` block deleted:

| | with the two roots | without | delta |
|---|---|---|---|
| assemblies | 38 | 35 | −3 |
| managed IL | 7 076 864 | 5 091 840 | **−1 985 024** |

Three assemblies vanish whole — `UnityEngine.UIElementsModule.dll` (−1 520 640),
`UnityEngine.PropertiesModule.dll` (−76 288), `UnityEngine.InputForUIModule.dll` (−23 552) — and
seventeen more shrink: `System.dll` −137 728, `mscorlib.dll` −87 552,
`UnityEngine.TextCoreTextEngineModule.dll` −40 448, `UnityEngine.CoreModule.dll` −39 936,
`UnityEngine.dll` −31 744, `LightSide.UniText.dll` −12 800, the rest ≤6 144 each. Two `preserve="nothing"`
entries carry 28% of the player's managed code.

**Where the roots come from.** `UnityEditorInternal.AssemblyStripper.WriteTypesInScenesBlacklist`
(in `UnityEditor.dll`) writes `TypesInScenes.xml` from
`RuntimeClassRegistry.GetAllManagedTypesInScenes()`. That registry is the Editor's, and the Editor
always has UI Toolkit. So the player's managed link roots are seeded from a scan that runs while
`UNITY_EDITOR` is still true — which is exactly why removing the package, force-excluding the
module and guarding every reference all fail to move it: none of them touch that registry.

Not scene- or asset-driven: `MemTest_UniText.unity` is the build's only scene and contains no
`UIDocument`; the sole `PanelSettings` asset in the project
(`Assets/MemoryTest/UIToolkit/MemSnapPanelSettings.asset`) is referenced only by
`MemSnap_UIToolkit.unity`, which is not in `EditorBuildSettings` and not in the build; and the TMP
build from 06:49 — before that asset existed — already fed the module to IL2CPP.

**Consequence for the tool.** The lever is neither the manifest nor `ModuleMetadata`, both of which
this bench already has set the way they need to be. It is the linker's root set, and the only place
to reach it is between the Editor writing `UnityLinkerInputs/` and Bee invoking UnityLinker.

### The hook that reaches the root set

`AssemblyStripper.GetLinkXmlFiles(BuildPostProcessArgs, NPath)` — called from
`BeeBuildPostprocessor.LinkerConfigFor` — runs, in IL order:

```
WriteMethodsToPreserveBlackList
WriteTypesInScenesBlacklist                     → UnityLinkerInputs/TypesInScenes.xml
WriteSerializedTypesBlacklist                   → UnityLinkerInputs/SerializedTypes.xml
ProcessBuildPipelineGenerateAdditionalLinkXmlFiles   → IUnityLinkerProcessor callbacks
GetUserBlacklistFiles
… then Where(NPath.FileExists) → Select(ToString) → ToArray
```

So **`UnityEditor.Build.IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile` is invoked after both
files exist on disk and before UnityLinker reads them**. It is public, documented and not obsolete in
both 6000.x and 2022.3, carries one member, and extends `IOrderedCallback`.
`media.lightside.core`'s `LightSideInputLinker` already implements it, so the hook is proven in this
codebase — its `Library/LightSide/InputModule.link.xml` appears in the build's linker `.rsp`.

Properties of the point that matter:

- The returned path goes through `Where(NPath.FileExists)`, so a processor that has nothing to add
  can return a path to a minimal `<linker/>` document.
- `UnityLinkerBuildPipelineData` carries only `target` and `inputDirectory` (the staging `/Managed`
  folder), so the inputs directory has to be addressed directly:
  `Library/Bee/artifacts/UnityLinkerInputs`. Same path in 2022.3 — unverified, no 2022.3 editor on
  this machine.
- `OnBeforeRun` / `OnAfterRun` are gone: `ProcessBuildPipelineGenerateAdditionalLinkXmlFiles`
  reflects over each processor and warns *"has a non-empty OnBeforeRun method, but
  IUnityLinkerProcessor.OnBeforeRun is no longer supported"*. Do not reintroduce them.
- If a future editor reorders the writes past the callback, the edit is overwritten and the build
  keeps UI Toolkit. The failure mode is a lost saving, never a broken player.

### Landed — the trim, verified in a real build

`media.lightside.leanbuild/Editor/LinkerRootTrimmer.cs` implements `IUnityLinkerProcessor`, deletes
the `<assembly fullname="UnityEngine.UIElementsModule">` block from both files when the package is
not registered, and returns a minimal `<linker/>`. Build log:

```
[LeanBuild] Dropped 1 UnityEngine.UIElementsModule root(s) from TypesInScenes.xml
[LeanBuild] Dropped 1 UnityEngine.UIElementsModule root(s) from SerializedTypes.xml
[MemTest] Build UniText: Succeeded, errors=0, warnings=0
```

`ManagedStripped/` reproduces the offline experiment byte for byte — 38 → 35 assemblies,
7 076 864 → 5 091 840 B, the same twenty deltas. The player:

| APK entry | rollback | guarded (roots kept) | trimmed |
|---|---|---|---|
| `libil2cpp.so` | 39 967 200 | 38 602 560 | **25 008 216** |
| `global-metadata.dat` | 5 745 230 | 5 561 794 | **3 584 102** |
| `libunity.so` | 36 487 072 | 36 326 760 | 35 836 512 |

guarded → trimmed: **−13 594 344 B of code (−12.96 MiB)** and **−1 977 692 B of metadata
(−1.89 MiB)**. The generated C++ falls much further than UIElements' own 29.5 MB, because its
generic instantiations were inflating the shared files: `Generics` 149 823 352 → 92 600 547,
`GenericMethods` 45 802 132 → 21 435 246.

Controls: the same APK reader returns 9 870 776 / 1 788 044 for `bare_nouitk.apk` and
23 962 312 / 3 568 262 for `bare_withuitk.apk`, matching the bare-app measurement recorded earlier.
Installed and launched on R5CRC3G3H1B: the process is alive at 14 s with no managed exception, only
the usual `AssetPackManager` `ClassNotFoundException` that every build here logs.

Unexplained: `libunity.so` also drops 490 248 B although the engine-module list is identical at 25
modules in both builds. Not the managed side; not chased yet.

**Resident, on the device.** `compare-apks.ps1`, five interleaved rounds, sampled at 9 s,
`guarded.apk` against the trimmed `MemTest_UniText.apk`, MiB:

| | A — roots kept | B — roots trimmed | delta |
|---|---|---|---|
| `libil2cpp` | 18.85 (18.65–19.09) | 14.51 (14.31–14.81) | **−4.34** |
| `global-metadata` | 5.22 (5.18–5.24) | 3.36 (3.36) | **−1.86** |
| process RSS | 237.02 | 227.15 | **−9.87** |

No range overlaps, and `MemAvailable` sits in the same band in both arms (1 484–1 550 against
1 513–1 552), so this is not device drift. Metadata is 1:1 with disk again — 1.89 on disk, 1.86
resident. Code is not: 12.96 MiB left the file, 4.34 MiB left RAM, so the removed UI Toolkit code was
33% resident, below the 48% whole-library figure — a module nothing calls is colder than average.
The 3.67 MiB of RSS beyond code and metadata is unattributed; `libunity` accounts for at most 0.47 of
it, and the rest is the same native-heap arm noted under *Open risks*.

**A measurement run is exclusive.** Two `compare-apks.ps1` runs overlapped on the device and each
installed its APK over the other's between samples, producing crossed arms (an "A" sample reading
14.56 and a "B" reading 18.90). Both runs' numbers were discarded. Check for a live run before
starting one.

### The package does not have to be removed — measured

The trim was expected to be useless while `com.unity.modules.uielements` is installed, because then
`PACKAGE_UITOOLKIT` is defined and uGUI compiles `PanelEventHandler` and `PanelRaycaster`
(`Runtime/UGUI/EventSystem/UIElements/`, gated at `PanelEventHandler.cs:9`) into `UnityEngine.UI` —
a linker **root** assembly, whose every `UIBehaviour` is rooted. That prediction is wrong.

Build with the package restored to the manifest and the trim on:

| | roots kept | trimmed, no package | trimmed, package installed |
|---|---|---|---|
| assemblies | 38 | 35 | 36 |
| managed IL | 7 076 864 | 5 091 840 | 5 125 632 |
| `UnityEngine.UIElementsModule` | 1 520 640 (1 471 types) | absent | **15 872 (26 types, 75 methods)** |
| `UnityEngine.PropertiesModule` | 76 288 | absent | absent |
| `UnityEngine.InputForUIModule` | 23 552 | absent | absent |
| `libil2cpp.so` | 38 602 560 | 25 008 216 | 25 073 440 |
| `global-metadata.dat` | 5 561 794 | 3 584 102 | 3 607 802 |

Keeping the package costs **65 224 B of code and 23 700 B of metadata — 0.08 MiB**. The bridge does
root UI Toolkit, but only its own closure: 26 types instead of 1 471.

The engine-module report still lists **25 modules with UIElements absent** even though the package is
installed and `forceExcludeModules` no longer names it. Native engine-module inclusion follows the
managed roots, so cutting the roots takes the native module out too.

Consequences: the tool needs no manifest edit, no domain-reload state machine, no dependency
chasing, and works unchanged when a third-party package depends on the module. One project setting
is the whole mechanism.

**Refuted — "removing the package does not cost the editor anything".** That was claimed from eleven
assemblies in the bench's `Library/ScriptAssemblies` that still referenced
`UnityEngine.UIElementsModule` with the package absent. They were stale: compiled before the removal
and not rebuilt. The claim is wrong.

Measured properly in `LightSideEcosystem`: removing `com.unity.modules.uielements` from the manifest
produced ~130 errors, every one in a package's **editor** assembly, of the form

```
error CS1069: The type name 'VisualElement' could not be found in the namespace
'UnityEngine.UIElements'. This type has been forwarded to assembly
'UnityEngine.UIElementsModule'. Enable the built in package 'UIElements' in the Package Manager
window to fix this error.
```

all from `Unity.InputSystem.Editor` — its whole action-asset editor is a UI Toolkit window. A
built-in module package governs **both** the editor's and the player's compilation of package
assemblies, with one switch and no way to say "editor yes, player no". `UnityEditor.dll` keeps UI
Toolkit either way, which is what made the stale reading look plausible.

Consequence: **the package cannot be removed from any project whose editor tooling is written in UI
Toolkit**, which today includes any project with Input System. The lever has to be the player's own
references, with the package left installed. The only switch that separates the two compilations of
one runtime assembly is `UNITY_EDITOR`; scripting defines and `versionDefines` apply to both.

Kept for the record; with the trim working against an installed package this no longer decides
anything.

**Patching `Library/PackageCache` cannot change a dependency.** `projectResolution.json` lists
`manifest.json` and `packages-lock.json` as resolution inputs and **not** the `package.json` of a
registry package. Edits to `Library/PackageCache/<pkg>/package.json` survive on disk — verified after
a full recompile — and are ignored: Unity rewrites the lock from registry metadata and the
dependency comes back. Source files patched there do survive and do take effect. So a patch
mechanism can change a package's **code**, never its **dependencies**; for those the only lever is
embedding the package under `Packages/`, where its `package.json` is authoritative.

**`versionDefines` with an empty `expression` works on a built-in module.**
`LightSide.UniText.rsp` for the player build carries `UNITEXT_HAS_UIELEMENTS` among its 157 defines
with `{"name": "com.unity.modules.uielements", "expression": "", "define": …}`. Unity writes
`"expression": "1.0.0"` in `UnityEngine.UI.asmdef`, but the empty form is not required.

**`LightSide.UniText.Inspection` leaves the player either way.** With the package installed it
compiles (its `.rsp` exists, `DEBUG` and `UNITEXT_HAS_UIELEMENTS` are both defined) and the linker
then drops the whole assembly as unreferenced — it is not a root assembly. So the
`UNITEXT_HAS_UIELEMENTS` entry added to its `defineConstraints` buys nothing in the player; it earns
its place only by keeping the assembly from failing to compile when the package is absent.

## What unused code costs — three arms, measured

The question this whole effort turns on: does code that is in the build but used by nothing occupy
RAM? Answered with three builds differing only as stated, each pair alternated five times through
`compare-apks.ps1` at the empty screen (9 s, before `StaticSnapshotRunner` builds its grid at 10 s).

| arm | editing module | its startup registrations | `libil2cpp.so` | `global-metadata.dat` |
|---|---|---|---:|---:|
| A | in the build | run | 39 636 256 | 5 708 214 |
| C | in the build | five commented out | 39 585 760 | 5 699 190 |
| B | stripped out | — | 36 416 688 | 5 146 206 |

C is A minus 7 types and 101 methods — 0.9% — so **A→C is execution at constant code, and C→B is
presence at zero execution.**

**Execution costs nothing. A ≈ C.** 19.53 against 19.45 mean resident code, bands overlapping
(A 18.63–20.20, C 19.34–19.59) against a 0.6 threshold. Disabling `FocusInteractionSource.Install`,
`NativeInputSession.Initialize`, `NativeKeyInputSession.Initialize`, `NativeInputAndroid.Register` and
`ManagedInputBackend.Register` is worth nothing measurable. This refines the earlier *Refuted* entry
that put six registrations at 0.68: that set also contained `UniTextWorldBatcher` and
`EmbeddedFontCatalog`, which live in the core assembly, and the saving was theirs.

**Presence costs. C > B and A > B.** A→B is **−1.17** and, repeated in a later session with six
rounds, **−1.07** (A 19.51–19.95 spread 0.44, B 18.63–18.82 spread 0.19). C→B is −0.73. These do not
add with A→C and should not be: arm A read 19.87, 19.53 and 19.78 across three sessions, so
between-session drift is ~0.3 and only within-run deltas are valid. Call presence **0.7–1.2 MiB of
resident code** for 3.07 MiB removed from disk.

Arm B is the one that gets sampled early — `native = 0` in 5 of its 16 samples, arm A in none. Those
samples were discarded under the rule in *Noise floor*, and the discarding is **conservative**: every
discarded B reading was low (15.88–16.32), so keeping them would make the deltas larger, not smaller.
Counting the repeat run unfiltered gives B 17.39 and A→B −2.39. The finding survives either
treatment.

**Metadata is one-to-one with disk, predicted before measuring.** Disk delta 562 008 B = 0.536;
measured −0.506 at 9 s and −0.544 at 60 s for A→B, and −0.570 for C→B against a 0.527 prediction.
Every arm agrees within 0.04, including samples discarded as early for the code metric — as expected,
since the whole of `global-metadata.dat` is resident well before the sample. Note the mechanism is
demand paging, not a bulk load: the file is `mmap`ed read-only and pages arrive as they are touched.
It reaches ~100% here because startup touches nearly every type — 4.91 MiB of file against 4.79–4.91
measured — so in this app, and only as an observation about this app, a byte removed from the file is
a byte of RAM. Do not restate it as a property of the format.

### What each part of the cost is, per segment

`smaps` reports each ELF segment of `libil2cpp.so` separately; summing them hides the answer. Three
rounds per arm, A against B, means:

| segment | A size → rss | B size → rss | Δ size | **Δ rss** |
|---|---|---|---:|---:|
| `r-xp` code | 22 644 → 10 723 (47%) | 20 744 → 10 284 (50%) | +1 900 | **+439** |
| `r--p` rodata | 13 380 → 6 796 (51%) | 12 356 → 6 337 (51%) | +1 024 | **+459** |
| `r--p` relro | 1 560 → 1 560 (100%) | 1 436 → 1 436 | +124 | **+124** |
| `rw-p` data | 1 136 → 1 136 (100%) | 1 040 → 1 040 | +96 | **+96** |

+1 118 kB, which is the −1.07/−1.17 measured as a total. All sizes are multiples of 4 kB, so the
kernel page is 4 kB here, not 16.

So the editing module's 1.60 MiB of RAM is 0.43 code, 0.45 rodata, 0.21 relocation tables and 0.51
metadata. **77% of its code stays on disk** — its code segment is only 23% resident against the
library's 48% average — and the cost is everything else. A module in the build but used by nothing
costs roughly **44% of its on-disk footprint** (1 630 kB resident against 3 706 kB of file).

TMP shows the same residency profile — 53% code, 52% rodata — so this is not a layout defect of ours;
we simply have 8.7× the methods.

Mechanisms, checked against sources rather than assumed:

- **Relocation tables are dirty by construction.** Chromium's *Native Relocations* documents that the
  dynamic linker writes every page of `.data.rel.ro` while applying relocations, and the pages stay
  dirty afterwards. That is exactly the 100%/dirty `r--p` and `rw-p` above. Android maps the library
  at one address across processes and dedupes those pages through shared memory, so part of this is
  paid once per device rather than per app.
- **`global-metadata.dat` is `mmap`ed and demand-paged**, not bulk-loaded. It reaches ~100% resident
  here because startup touches nearly every type; that is an observation about this app.
- **The 23% residency of cold code is not explained.** Page granularity with hot and cold methods
  interleaved is arithmetically plausible — 4 kB holds 2–3 generated methods, and touching a quarter
  of methods scattered uniformly would touch about half the pages — but nothing tests it. Settling it
  means matching resident pages against the symbol table.
- **Lead on the untraced 0.6 MiB of native heap:** a Unity forum thread reports `IL2CppClass`
  structures and their contents reaching 100 MB of native heap in a large project. Those are built
  per type during type-system initialisation, from the metadata, and live on the heap rather than in
  the mapped file. If that is what our 0.6 is, it is another term that scales with type count alone.

**The conclusion.** Not using code does not keep it out of RAM. Only its absence from the build does.
Lazy initialisation, deferred registration and thinning startup work all measured zero here. The
mechanism behind the resident code is not established — the plausible one is kernel readahead pulling
neighbouring pages of `libil2cpp.so`, where IL2CPP interleaves cold methods with hot ones rather than
grouping them by assembly, but that has not been tested. Testing it means matching resident pages
against the symbol table. The practical consequence does not depend on which mechanism it is.

## The floor: UniText with nothing rooting it, against TMP

Every prefab removed from both `Packages/media.lightside.unitext/Defaults/` and `Assets/UniText/` —
26 files — so nothing but the scene's own label names a component. Both arms rebuilt through the same
pipeline in one sitting, five rounds alternated at 9 s.

**Removing all 26 prefabs gives exactly what removing the seven editing ones gives: 890 types,
7 576 methods.** `UniTextWorld`, `UniTextDocumentView`, `UniTextDocumentLoader` and the button do not
leave, because they live in `LightSide.UniText` and that assembly is a root as long as the scene
holds one `UniText`. **Prefabs are a lever only for what can leave as a whole assembly.** 890 / 7 576
is the floor of the current structure; lowering it means the main assembly no longer being one piece.

| | UniText, nothing rooting | TMP | Δ |
|---|---:|---:|---:|
| types / methods | 1 244 / 9 573 | 108 / 1 100 | ×11.5 / ×8.7 |
| `libil2cpp.so` on disk | 36 416 688 | 25 028 240 | +10.86 |
| `global-metadata.dat` on disk | 5 146 206 | 3 700 054 | +1.38 |
| our native plugins | 3 294 368 | 0 | +3.14 |
| **resident code** | **18.67** | **13.15** | **+5.52** |
| **metadata** | **4.85** | **3.47** | **+1.38** |
| **process RSS** | **237.76** | **231.75** | **+6.01** |

Metadata matched its disk delta for the sixth time: 1 446 152 B = 1.379 predicted, +1.38 measured.
Resident code is 51% of the disk delta, the same residency `libil2cpp.so` shows everywhere.

`native = 0` on the TMP arm is correct here, not the early-sample tell — TMP has none of our plugins,
and its RSS is steady at 231.2–232.3.

**What this settles.** Packaging was worth 3.0 of the 9.0 RSS gap and only helps a project with no
editing at all. The remaining **6.0 is paid by every project regardless**, and it is the target.
The type counts are not directly comparable — TMP does shaping and font work in the engine's
`TextCore` modules inside `libunity.so`, we do it in our own managed code plus HarfBuzz and FreeType —
but for RAM that does not matter: we pay for what sits in our mappings.

### Where the 7 576 methods are

Types attributed to their source folder by declaration site. Percentages of *declared* types retained
are only meaningful where below 100; generated nested types are attributed to their outer type's
folder and inflate the rest.

| folder | declared | kept | kept % | methods | share |
|---|---:|---:|---:|---:|---:|
| `StyleCore` | 485 | 335 | **69%** | 2 469 | **32.6%** |
| `Core` | 177 | 183 | — | 1 973 | 26.0% |
| `FontCore` | 127 | 151 | — | 1 523 | 20.1% |
| `Unicode` | 66 | 72 | — | 305 | 4.0% |
| `Native` | 43 | 22 | 51% | 210 | 2.8% |
| `EmojiCore` | 16 | 10 | 62% | 107 | 1.4% |

**Generated state accessors are 285 types and 2 048 methods — 27.0% of the assembly.**

`StyleCore` by subfolder, 2 469 methods of which 578 generated: root 502, `Interactive` 489,
`States` 387, `Ranges` 201, `Parameters` 197, `Semantics` 137, `Animation` 120, `Media` 99,
`Rules` 81, `Modifiers` 79, `Decoration` 62, `PaintLayers` 60, `Markup` 47, `Paint` 8.
`Interactive` and `States` together are 876 methods — 36% of `StyleCore` — in a bench with plain
labels, no interactive range, no state and no modifier.

Largest survivors: `UniTextInteractions` 93, `AttributeParser` 87, `InteractiveModifierBase` 62 plus
58 in its generated `StateAccess`, `Style` 51, `BaseModifier` 50, `UniTextRanges` 47.

**Not established: what roots it.** None of those are `MonoBehaviour`s — `UniTextInteractions` is a
plain `IDisposable` — so the root-assembly-messages mechanism that explains editing does not apply
here. This is ordinary reachability from the label path and needs the dependency dump to resolve.
`[SerializeReference, TypeSelector]` is used in fifteen places including `StyleCore/Animation/Reveal`
and is an untested candidate; the `SerializedTypes.xml` that would show it was overwritten by the
following build.

## The exchange rate: RAM per method, calibrated twice

Built both libraries at `ManagedStrippingLevel.Minimal` as well as the usual `High`, to see what the
linker is worth and whether resident memory tracks method count linearly. (The enum is
`Disabled=0, Low=1, Medium=2, High=3, Minimal=4` — read from `UnityEditor.dll`, not guessed; Minimal
is 4, not 1.)

| | Minimal | High | linker removes |
|---|---:|---:|---:|
| TMP | 189 types / **1 821** methods | 108 / **1 100** | 43% / **40%** |
| ours, all four assemblies | 2 498 / **21 941** | 1 324 / **11 363** | 47% / **48%** |
| `LightSide.Core` | 587 / 3 847 | 367 / 2 091 | 46% |

| measured at 9 s | Minimal | High |
|---|---:|---:|
| resident code, ours − TMP | **+10.55** | **+5.52** |
| metadata, ours − TMP | **+3.94** | **+1.38** |
| process RSS, ours − TMP | **+19.18** | **+6.01** |

| | method gap | Δ resident code | **MiB per 1 000 methods** |
|---|---:|---:|---:|
| High | 12 354 | 5.52 | **0.447** |
| Minimal | 23 967 | 10.55 | **0.440** |

**The relationship is linear across a 2× range.** Code alone costs ~0.44 MiB per 1 000 methods;
with metadata it is 0.56–0.60. The editing module measured 0.42 because it is colder than average —
23% of its code resident against the library's 48%.

Two conclusions this settles:

- **Stripping already does the heavy lifting and is not where we lose.** It is worth 13 MiB of
  process RSS to us (+19.18 at Minimal against +6.01 at High), and we give the linker a *larger*
  share of our code than TMP gives of hers — 48% against 40%. There is no headroom in stripping
  quality.
- **The whole difference is how much code exists.** All of TextMeshPro before any stripping is 1 821
  methods; we still have 11 363 after it. Not 10× worse at being stripped — 12× bigger.

So the only lever is method count, and it now has a price: **removing 1 000 methods returns about
0.56 MiB.** Halving the gap to TMP means removing roughly five thousand.

## The generated state accessors — 2 048 methods nothing can call

27% of `LightSide.UniText` after stripping is generated by `StateCodeGen`: 285 types, 2 048 methods.
By kind:

| kind | types | methods | avg | share |
|---|---:|---:|---:|---:|
| closures `<>c` | 111 | **1 418** | 12.8 | **69%** |
| `GeneratedPropertyValues.__Value_*` | 18 | 412 | 22.9 | 20% |
| `__LS_*` (StateList/StateProperty) | 20 | 82 | 4.1 | 4% |
| `Members` / `PropertyAccess` / `StateAccess` | 136 | 136 | 1.0 | 7% |

The closures come from `StateSourceGenerator.cs:773-775`, which emits **two static lambdas per
property** — a reader and a writer — into each `PropertyAccess` table entry. The C# compiler turns
each lambda into one method on a `<>c` display class. `UniTextSystemFont/PropertyAccess/<>c` alone
carries 284 of them, `UniTextBase` 90, `UniTextFont` 68.

**Nothing can ever call them.** In the stripped `LightSide.Core.dll`, `PropertyPath` is gone
entirely and `PropertyAccessors` keeps only `Concat`, `PublicName` and `.cctor` — `TryResolve` and
`Find`, the only readers of those tables, are stripped. The tables are retained anyway, and the
dependency report gives the chain:

```
UniTextFont / UniTextSystemFont          linker roots (asset types in a root assembly)
  → every member kept, including IPropertySource.get_PropertyAccessors()
  → its body returns the static field __LS_Table
  → the field initialiser constructs every PropertyAccessor of the type
  → each holds two lambdas  →  1 418 methods on <>c
```

`get_PropertyAccessors()` itself has no incoming edge in the report — the same rootless pattern as
the Unity messages. There is no call anywhere on this path, only field initialiser references.

Worth **≈0.86 MiB** for the whole generated surface at the measured rate, of which **≈0.60** is the
closures. Three directions, by rising risk: emit one index-dispatching method per type instead of two
lambdas per property (1 418 → ~222, no behaviour change); keep the table off the rooted asset types;
generate the accessor surface only where a consumer asks for it.

Standing caveat: the report records only the **first** edge that marked each item, so another path
may hold the same code. That `TryResolve` is stripped is independent and certain; that removing the
table would actually drop the closures is not, and only a rebuild settles it. Eight cuts have already
measured zero on exactly this kind of reasoning.

## Landed — the generated property surface, −1 046 methods

Two changes to `Tools~/StateCodeGen/StateSourceGenerator.cs`, rebuilt into
`media.lightside.core/Analyzers/LightSide.StateCodeGen.dll`.

**1. Projections removed.** `RenderProjectedProperties` expanded every custom-struct field into a
bindable path for each of its public mutable members, recursing without a depth limit or an opt-out.
Built-in animatable types (`Color`, `Vector2`, …) were already excluded as whole values, so the
feature only ever reached configuration structs: `UniTextSystemFont`'s six `PlatformConfig` fields
became 138 paths like `systemFont.windows.weight`. No asset in the package binds such a path and the
capability is undocumented. The method and the orphaned `PropertyGetterPath` are gone.
Breaking: nested paths no longer resolve. Owner cleared it.

**2. `PropertyAccess` gated to the editor.** The generated table exists for `PropertyPath`, whose only
consumers are six editor files and which the linker already strips from players. The table body is
now inside `#if UNITY_EDITOR`; the interface stays and the player branch returns
`PropertyAccessors.None`, the type's own "no properties" value, so the base list needed no
preprocessor surgery. `StateAccess` was left alone — `ModifierFieldsAnimationHandler` and
`ParameterDescriptor` read it at runtime, it is live functionality.

| | baseline | after 1 | after 2 |
|---|---:|---:|---:|
| `LightSide.UniText` types | 1 072 | 1 068 | **972** |
| methods | 9 109 | 8 655 | **8 076** |
| `LightSide.Core` methods | 2 091 | 2 091 | **2 078** |
| `libil2cpp.so` | 39 636 256 | 39 434 608 | **39 062 496** |
| `global-metadata.dat` | 5 708 214 | 5 674 566 | **5 628 534** |

`Interaction`, `NativeInput` and `Dropdown` are untouched to the method.

**Predicted 0.27 from the static delta, measured −0.29**, six rounds alternated: baseline 20.03
(19.76–20.20), changed 19.74 (19.65–19.90). The ranges touch, so this fails the band-separation rule
on its own; the means differ by 3.4 standard errors, and it agrees with a prediction made before the
run. Metadata −0.04 against 0.076 predicted, inside its own noise.

**Lambdas weigh half of an average method — correct any estimate that uses the general rate.** These
454 projection methods cost 444 bytes of `libil2cpp.so` each against the editing module's 850. An
earlier note here priced the whole generated surface at ≈1.15 MiB by applying 0.56 MiB/1 000; the
real figure is ≈0.60 for all 2 048, and `StateAccess`'s 732 cannot go, so the generator's remaining
headroom after these two changes is about 0.16 MiB. Rank targets by IL weight, not method count.

## Owed — stripping the editing module out of a label-only project

Deferred deliberately: the goal of this effort is resident memory, not APK size, and stripping is the
smaller half of that. Recorded so it is not re-derived a third time.

The linker is not at fault. Unity messages are called by name from native code, so preserving them for
any `MonoBehaviour` that might be instantiated is correctness, not waste; and a prefab in the asset
database may legitimately be loaded from `Resources`, a bundle or Addressables built later. What it
does not do is distinguish a prefab reachable from the build from one that merely exists in the
project — and it cannot tell a prefab **we** ship from one the consumer made.

That is the whole difference against TMP, and it is a packaging decision rather than an engineering
one: `TypesInScenes.xml` in the TMP arm lists only `TMP_FontAsset`, `TMP_Settings`,
`TMP_SpriteAsset` and `TMP_StyleSheet` — no component at all. TMP builds its UI objects from code in
the menu, so nothing in the asset database names `TMP_InputField` and it strips. We ship thirteen
prefabs that name ours.

Three forms, all of which keep "the designer takes a ready prefab from the menu":

1. **`Defaults~/`** — a tilde folder is invisible to the AssetDatabase; the menu copies the one prefab
   it needs into the project on first use. A project that never creates an editable never names the
   type. No user-visible scenario changes.
2. **A Package Manager sample** — honest, but needs an explicit import before first use.
3. **Creation from code**, as TMP does. The path already exists: the settings tooltips read
   *"Falls back to code creation if null."* Costs the authored prefab.

Worth about 3.0 MiB PSS by *How much of that gap is waste*, and it is the gate for the split that is
already landed. Form is the owner's call.

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
