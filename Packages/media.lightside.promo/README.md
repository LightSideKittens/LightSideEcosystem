# Promo Studio

Authoring kit for promo and hero videos, built entirely in code on top of
UniShapes and UniText. Internal — never shipped to customers.

A reel is an ordered list of slides. Each slide builds its own hierarchy from a
`Stage` and reports what it looks like at any point in its own duration;
`Reel.Compose` is a pure function of time. Nothing accumulates between frames,
so the playhead can be dragged backwards, a single frame can be rendered alone
for inspection, and a captured file matches the editor preview exactly.

## Getting started

1. `Tools → LightSide → Promo → Create Reel` — builds a landscape canvas, an orthographic
   camera and an empty reel into the open scene.
2. Add a child GameObject per slide and give it a `Slide` subclass.
3. `Tools → LightSide → Promo → Rebuild Scene`, then scrub the reel's inspector.
4. `Tools → LightSide → Promo → Capture Contact Sheet` before calling anything finished.

To add individual shapes or text to the scene, use **Shift + Right Click** in **Hierarchy**
and choose **UniShapes → UniShape** or **UniText → UniText**.

## Assemblies

| Assembly | Scope |
| --- | --- |
| `LightSide.Promo` | Reel, slides, transitions, motion primitives, the `Stage` builder |
| `LightSide.Promo.Editor` | Scene commands, frame and contact-sheet capture, the reel inspector |

## Dependencies

- `media.lightside.core` — `Easing`, `Gradient`, `CoreLoop`, `ObjectUtils`, and
  the editor inspector primitives the reel's own inspector is built from.
- `media.lightside.unishapes` — `UniShape` and its layer stack: every panel,
  card, field, button and bar here is a layer stack, never a sprite.
- `media.lightside.unitext` — text, and all of its animation. This package
  writes no text-animation code of its own.
- `com.unity.ugui` — the canvas, layout groups and `Graphic` base the shapes and
  text derive from.

## Reference

`Documentation~/PromoReference.md` carries the verified API map, the traps that
cost a session each, and the motion values. Read it before editing anything
here.
