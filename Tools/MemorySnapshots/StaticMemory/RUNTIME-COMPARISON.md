# UniText vs TMP - memory under 330-second text-change phase

## device and build

| | |
|---|---|
| Device | Samsung SM-G990B, Android 15 |
| Unity | 6000.6.0f1, IL2CPP, ARM64 |
| Build type | Development + Connect with Profiler |
| Managed Stripping Level | High |
| UniText | 3.8.5 (core 3.6.4) |
| uGUI | UniText: LightSide fork 2.6.1 (no TextMeshPro). TMP: stock 2.6.0 |
| Isolation | TMP build contains no UniText; UniText build contains no TextMeshPro |

## scene and workload

| | |
|---|---|
| Scene | `MemTest_*`, `StaticSnapshotRunner` |
| Texts | 300, grid 10×30, font size 14 |
| Corpus | deterministic, seed 12345; English, Russian, Turkish, Arabic, Hebrew |
| Change phase | 594,000 `SetText` calls, 30 per frame |
| Phase end condition | change count, not a timer |
| UniText flags | `SystemFont.Disabled`, `EmojiFont.Disabled`, `UseParallel = false` |

## Work completed

| Variant | Changes | Engine time |
|---|---:|---:|
| TMP | 594,000 | 330.2 s |
| UniText | 594,000 | 330.2 s |

## Process PSS over time, MiB

| s | TMP | UniText | Δ |
|---:|---:|---:|---:|
| 0 | 42.6 | 85.3 | +42.7 |
| 10 | 188.1 | 201.3 | +13.2 |
| 30 | 324.7 | 267.9 | −56.8 |
| 60 | 325.2 | 271.7 | −53.5 |
| 90 | 326.1 | 274.7 | −51.4 |
| 120 | 326.7 | 278.0 | −48.7 |
| 150 | 326.7 | 283.2 | −43.5 |
| 180 | 327.4 | 283.4 | −44.0 |
| 240 | 327.5 | 283.8 | −43.7 |
| 300 | 327.6 | 283.9 | −43.7 |
| 330 | 327.6 | 283.8 | −43.8 |
| 360 | 327.6 | 284.0 | −43.6 |
| 420 | — | 284.0 | |
| 480 | — | 284.0 | |
| 530 | — | 284.0 | |

Sampling window: TMP 0–404 s, UniText 0–529s. Change phase ends at 330s

## Empty screen, no text created, MiB

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| Rss | 242.2 | 254.6 | **+12.4** |
| Pss | 150.6 | 163.2 | **+12.6** |
| Private Dirty | 91.8 | 93.3 | **+1.5** |
| Reserved address space | 21,426.3 | 21,514.7 | +88.4 |

## After the 330-second phase, MiB

| | TMP | UniText | Δ |
|---|---:|---:|---:|
| Rss | 378.9 | 335.2 | **−43.7** |
| Pss | 286.7 | 243.6 | **−43.1** |
| Private Dirty | 227.4 | 167.5 | **−59.9** |
| Reserved address space | 21,552.5 | 21,581.3 | +28.8 |

## End-state difference by mapping — Private Dirty, MiB

| Mapping | TMP | UniText | Δ |
|---|---:|---:|---:|
| `[anon]` — managed heap, buffers, meshes | 185.6 | 140.2 | **−45.4** |
| `kgsl-3d0` — GPU | 38.8 | 22.9 | **−16.0** |
| `libil2cpp.so` | 1.7 | 3.1 | +1.4 |
| `global-metadata.dat` | 0.0 | 0.0 | 0.0 |

## Startup difference by mapping — Rss, MiB

| Mapping | TMP | UniText | Δ |
|---|---:|---:|---:|
| `libil2cpp.so` | 14.8 | 24.2 | **+9.4** |
| `global-metadata.dat` | 3.4 | 6.3 | **+2.9** |
| `libunitext_native.so` | 0.0 | 0.8 | +0.8 |

## Measurement method

- `adb shell dumpsys meminfo <package>` every 10s
- full `/proc/<pid>/smaps` at 8 s and at end of run
- application writes nothing and enumerates nothing; all figures are read from outside the process
- one run per variant, back to back, same device, screen kept on

## Scope

- single run, single device
- Development build. The `Inspection` assembly it carries is absent from Release builds
- Earlier measurements referenced elsewhere used UniText 3.5.0, Unity 2022.3 and stripping level
  Minimal. Figures from different configurations are not comparable with these
- Both engines are measured by pages actually resident. Each holds a high-water mark: TMP in a
  garbage-grown heap, UniText in pooled arrays. Neither number is a live-object count
- UniText OS font fallback and emoji are disabled so both engines render from the same TTFs
