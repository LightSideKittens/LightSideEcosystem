# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Reel` and `Slide`: a seekable film whose every frame is a pure function of
  time, built and rebuilt entirely from code.
- `SlideTransition` with `Cut`, `CrossFade`, `Push` and `Lift`, held in a
  `[SerializeReference]` field so new transitions need no change to the reel.
- `Ease`: built-in curves plus CSS-style cubic Bézier timing, with the
  Material 3 emphasized presets.
- `Spring`: closed-form damped harmonic motion evaluated from elapsed time, so
  it is identical in the editor and in an offline capture.
- `Stage`: rect, layout, paint and widget factories over `UniShape` and
  `UniText`, plus `Theme` carrying the palette, radius, padding and type scales.
- `TitleSlide`, `ScriptsSlide` and `RichTextSlide`.
- `Tools ▸ Promo ▸ Create Reel`, `Rebuild`, `Capture Frames` and `Contact Sheet`,
  with a transport and frame scrubber on the reel's inspector.
- `Documentation~/PromoReference.md`: the verified API map, the traps, and the
  motion values.
- Pointer: `Beat`, `PointerTimeline` and `Pointer`, with Fitts-derived travel,
  bowed legs, held drags, a two-ring click ripple and a keystroke chip. The
  arrow's tip is its rect's origin, so there is no hotspot to measure.
- `Cue`, `Slide.Cue`, `Reel.CueSheet` and a `cues.csv` beside every capture, so
  audio can be laid against the reel without recording it.

### Changed

- `Stage.Reveal` drives its handler through `RevealModifier.GlyphRevealing`
  rather than a serialized handler entry, whose one-shot timeline runs off the
  system stopwatch and cannot be scrubbed or captured.
- Captures render at the canvas's reference resolution instead of a hard-coded
  1920x1080.
- `Create Reel` replaces an existing rig instead of silently handing it back.

### Fixed

- A slide is marked built only after `OnBuild` returns, and one that throws is
  reported by name and left out of the reel rather than burying its exception
  under a per-frame `NullReferenceException`.
- A non-looping reel holds its last frame instead of snapping to the first.
- `Lift` normalizes its spring so the incoming slide reaches its settled pose.
- `Spring.SettleTime` returns when the response entered the band, not 46 ms
  later.
