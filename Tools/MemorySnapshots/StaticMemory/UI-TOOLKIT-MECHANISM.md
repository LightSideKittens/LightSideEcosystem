# How UI Toolkit gets into a player, and what can take it out

The mechanism only. Measurements, package names, file paths and version numbers live in
[NOTES.md](NOTES.md); this document is what a reader needs in order to understand those numbers, and
what to believe when a new project behaves differently.

Everything below was established on Unity 6000.6, IL2CPP, Android, managed stripping High, unless a
paragraph says otherwise. Claims are marked **measured** when a build or a device produced them and
**derived** when they follow from something measured but were not themselves observed.

---

## 1. The question

A project that never draws a single UI Toolkit element still ships the whole UIElements runtime in
its player, costing a large, fixed amount of code, metadata and resident memory. Every obvious cure —
removing the package, disabling the module, guarding your own references — appears to do nothing.
The community conclusion is that built-in modules cannot be removed. That conclusion is wrong, but
the reason it looks right is worth understanding, because it is four separate mechanisms being
mistaken for one.

---

## 2. Four layers, and why they get confused

Unity decides what reaches a player in four passes that run at different times, answer different
questions, and are wired to each other only loosely.

| layer | question it answers | inputs |
|---|---|---|
| **package resolution** | which packages, and therefore which engine modules, exist in this project | manifest, lock, package manifests |
| **compilation** | which assemblies exist, and what each may reference | the resolved module set, asmdefs, defines |
| **managed stripping** | which managed types and members survive into the player | root sets and reachability |
| **native module stripping** | which engine modules are linked into the native player | what the managed side kept |

The confusion is that all four speak of "including a module", and a change made at one layer is
invisible at the others until the *last* obstacle at *every* layer is gone. Nothing reports partial
progress. This is why someone removes a package, sees no change, and concludes the removal did
nothing — when in fact it worked and three other holders remained.

---

## 3. Package resolution

**Built-in engine modules are packages.** They appear in the manifest like any other, and they can
be depended on by other packages.

**The resolution inputs are the project manifest, the lock file, and the manifests of *embedded*
packages.** The `package.json` of a package that came from a registry is *not* an input: Unity takes
that package's dependency list from registry metadata and writes it into the lock.

Three consequences, all **measured**:

- Editing a registry package's `package.json` inside the project's package cache has no effect on
  resolution. The edit survives on disk and is ignored; the lock is regenerated and the dependency
  returns.
- Editing a registry package's *source files* in the package cache **does** take effect, and survives
  a recompile — until Unity has a reason to restore the package, at which point it is gone.
- Therefore a patch mechanism can change a package's **code** but never its **dependencies**. For a
  dependency the only project-level override is **embedding** the package, where its manifest becomes
  authoritative.

**A module stays as long as anything declares it.** The manifest, any embedded package, and any
resolved third-party package are all declarers, and they are silent about each other. Unity's package
window shows such a module with a dependency marker and refuses to remove it, which is the visible
symptom of the same thing. Removing declarers one at a time produces no observable change until the
last one goes — the single most misleading property of this whole area.

---

## 4. Compilation

**One assembly definition produces two assemblies**: one compiled for the editor and one for the
player. They differ in exactly one useful way — `UNITY_EDITOR` is defined for the first and not the
second. Scripting defines and `versionDefines` apply to both.

**Engine modules are auto-referenced from the resolved module set.** An assembly does not list them;
it gets whatever the project resolved. So the package set decides what any assembly in the project is
allowed to compile against — *including editor assemblies of other packages*.

**Therefore a built-in module package governs both compilations with one switch.** There is no
"present for the editor, absent for the player" setting. This is the load-bearing fact of the whole
problem, and it was **measured** the hard way: removing the package broke the editor assemblies of a
third-party package whose inspector UI is itself written in UI Toolkit. The engine's own editor
assemblies keep the module regardless, which is what makes the opposite conclusion look plausible for
a while.

**`versionDefines` answer "is this package present", not "does this player want it".** The ecosystem
convention is for a package to gate its UI Toolkit integration on the UIElements package with a
version define. That convention is correct and widespread — and it is useless for our purpose,
because the gate opens whenever the package is installed, and the package has to stay installed for
the editor. A project-level scripting define is the switch that can express "this player does not use
UI Toolkit", because it is ours to set and it is not tied to the package's presence.

**A reference is not a use, and a use is not a root.** Three distinct things:

- A `using` of a namespace whose types are never used produces no assembly reference at all. Stray
  usings are therefore harmless and also invisible in reference scans.
- An assembly reference means some type was named. It costs metadata, nothing more.
- Whether that type's *implementation* reaches the player is decided later, by stripping.

Confusing these three is the second most common way to reach a wrong conclusion here, and it is the
reason two projects with the same number of referencing assemblies can differ enormously in what
they ship.

---

## 5. Managed stripping

UnityLinker keeps a type when something roots it. Roots come from:

- **root assemblies** — the project's own and its packages' assemblies, whose engine-callable members
  are rooted wholesale;
- **link XML** — files that name types to preserve, including ones the build pipeline generates;
- **reachability** — anything the above can reach.

Two of the generated link XML files are the crux of this document.

**The editor seeds its own UI Toolkit types into the player's root set.** The pipeline writes those
files from the editor's runtime class registry, and an editor always has UI Toolkit, so a small
number of UI Toolkit types are rooted in every player regardless of what the project contains. From
those roots, reachability drags in most of the module. This is the phantom that makes the module look
unremovable: it is not the project's fault, it is not in the project's code, and nothing the project
can configure affects it.

**The cost of a reference depends entirely on what it touches.** A constant or a small value type
costs essentially nothing; the module shrinks to a shell. A member of the style or panel graph pulls
the layout, style, event and rendering systems behind it, which is most of the module. So "how many
assemblies reference UI Toolkit" is a poor predictor and "what do they touch" is the real one. This
is why a bare project and a full one can both have several referencing assemblies and yet differ by
two orders of magnitude in what survives.

**There is exactly one public point between the generated roots and the linker.** The build pipeline
offers packages a callback whose stated purpose is to *add* link XML, and it is invoked after the
generated root files are written and before the linker reads them. Rewriting those files there is how
the phantom roots are removed. Since the guarantee is only about adding files, a future editor may
reorder the two — in which case the edit is overwritten, the build proceeds normally, and the only
loss is the saving. **Derived**: the failure mode is a fatter player, never a broken one.

---

## 6. Native engine module stripping

**The native side follows the managed side.** When the managed roots for a module are gone, the
native module leaves too, even with the package installed. So the managed root set, not the package
list, is what ultimately decides whether the engine carries a module.

**The build report's stripping graph points the opposite way from what one expects.** Asking it why a
*module* is included returns the module's own native classes — its contents, not its dependents. To
find who is responsible you have to walk further down, and the leaves are coarse categories rather
than assembly names. Reading one level and calling it "the reason" produces a confident, wrong
answer; this happened.

**Module inclusion can be forced.** The editor exposes a per-module include/exclude switch. Three
properties, all **measured**:

- It lives for the editor session. Nothing is written to project settings, and a fresh editor reports
  the default again.
- Applied from a build's preprocess callback, it affects the **player** compilation only — editor
  assemblies are already compiled by then and are untouched.
- It removes the module from the compilation's reference set but does not change any define. Code
  that compiles under a version define therefore still compiles, and now fails.

That last property was first read as a defect — the lever "does not work" because it produces
compiler errors. Under a different goal it is the feature: the errors are an exact, file-and-line map
of every place in player code that uses the module, produced by the compiler rather than by a
heuristic scan. See §8.

---

## 7. What happens during a build, in order

Roughly, and only the parts that matter here:

1. **Preprocess callbacks.** Editor assemblies are already compiled; the project's own player
   assemblies are not. This is the window for anything that must affect player compilation and
   nothing else.
2. **Player script assemblies are produced.** Compilation errors here fail the build.
3. **Link XML is generated**, including the files seeded from the editor's registry, and package
   callbacks are invited to contribute more. This is the window for changing the root set.
4. **UnityLinker runs** and writes the stripped assemblies.
5. **IL2CPP converts** the stripped assemblies to C++; the native player is built with the engine
   module set the earlier stages settled on.
6. **Postprocess callbacks**, on success only.

Two consequences worth remembering: anything set in step 1 and restored in step 6 is *not* restored
when the build fails, and any window between steps is only as stable as the callback that opens it.

---

## 8. The levers, and the honest limit of each

**Trimming the phantom roots.** Removes what the editor seeded. Sufficient by itself only in a
project whose own player code never reaches UI Toolkit; in that case the module leaves entirely and
the native module with it. In any project that does reach it, this saves nothing at all, silently —
so the tool must report which of the two happened rather than assume success.

**Forcing the module out of the player compilation.** Turns every player-side use into a compiler
error with a file and a line. It does not fix anything; it is a diagnosis. Two caveats: errors arrive
in dependency layers, so fixing the first layer reveals the next, and the build fails by design,
which means the restore in postprocess does not run.

**What neither lever can do.** Neither removes a reference that the player's code legitimately makes.
That requires changing the code: our own packages by a project-level define we control, third-party
packages by a patch. Neither lever can remove the package, because the editor needs it.

**Why patching is bounded work, not an open-ended system.** The ecosystem convention means most
packages already isolate their UI Toolkit integration behind a gate, in a small number of files. The
ones that need patching are few, their use sites are few, and the diagnosis names them exactly. A
general-purpose patch framework would be far larger than the problem.

---

## 9. What we believed and disproved

Recorded because each was plausible, each cost time, and each will look plausible again.

- **"Code that is never executed does not occupy memory."** It does. Presence costs; execution is
  free. Mapped code and metadata are paid for at load.
- **"An editor-only serialized field drags its asset into the build."** It does not; the field does
  not exist in the player's class. Asset inclusion and type preservation are different passes.
- **"Removing the module package is free for the editor."** It is not. This was concluded from
  compiled assemblies that were stale — they had been built before the removal and never rebuilt.
  Reading build artifacts without checking that they postdate the change is the single most
  productive way to be wrong in this area.
- **"The root trim alone is enough."** True in a bare project, false as a generalisation. The
  measurement was correct; the generalisation from one project was not.
- **"Patching a package's manifest in the package cache removes its dependency."** It does not; only
  embedding does.
- **"The stripping report names who holds a module."** It names the module's own classes.
- **"Forcing the module out is the wrong lever because it produces errors."** Wrong criterion, not
  wrong lever.

---

## 10. Measurement hygiene

The traps that produced false results here, all of them cheap to avoid:

- **Compare builds that differ in one thing.** A development build carries a much larger engine than
  a release build; comparing one of each attributes the engine difference to whatever else changed.
- **Check that a log or artifact postdates the change.** A build script truncates its log only once
  the editor starts, so for the first seconds the previous run's log is complete and convincing.
- **Container size is not payload size.** Read the uncompressed sizes of the code, metadata and
  engine entries inside the package, not the package's own length.
- **One device measurement at a time.** Two interleaved runs install over each other and produce
  crossed arms.
- **Distinguish reference from use from root.** Count types actually named, not assemblies that
  reference, and remember that neither is the same as what survives stripping.

---

## 11. Still open

- Everything here is from one editor version. The oldest version we claim to support has the same
  public hooks, but the ordering that makes the root trim possible has not been checked there.
- The trim's failure mode is derived, not measured: no editor has been observed writing the roots
  after the callback.
- A managed shell of the module can survive even when the native module leaves; what keeps it, and
  whether it costs anything at run time, is not established.
