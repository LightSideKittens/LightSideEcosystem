# Promo Studio — Reference

Everything a session needs to build a promo shot in this repository without
re-discovering it. Every API below was read from source; `path:line` is the
proof. Read this before touching the package.

---

## 1. Where things live

| | |
|---|---|
| This package | `Packages/media.lightside.promo` — embedded, auto-discovered, **not** a submodule, **not** shipped |
| Core | `Packages/media.lightside.core` — asmdef `LightSide.Core`, namespace `LightSide` |
| Shapes | `Packages/media.lightside.unishapes` — asmdef `LightSide.UniShapes`, namespace `LightSide` |
| Text | `Packages/media.lightside.unitext` — asmdef `LightSide.UniText`, namespace `LightSide` |
| The precedent | `Packages/media.lightside.unishapes/Samples/CalorieCounter/Editor/CalorieUIBuilder.cs` — a whole app built from script. Read it before inventing anything. |

Core and UniText are **git submodules**. Editing them is a cross-repository
change and is the owner's call, not a passing decision.

`Additional/` and `Recordings/` are **gitignored**. Anything valuable that lives
there is one `git clean` from gone — which is why the numbers in §6 are copied
into this file and into `Theme.cs`. Never recursively list
`Additional/HeroVideo/`: it holds ~27,000 files including a bundled headless
Chrome.

---

## 2. The one idea

`Reel.Compose(seconds)` is a **pure function of time**. Nothing accumulates
between frames.

Everything else follows from it: the scrubber can seek backwards, a single frame
can be re-rendered on its own for inspection, and the captured file matches the
editor preview exactly. Three iterations of the predecessor project were tuned
blind and shipped a broken line of text three times running; a seekable reel plus
a contact sheet is the fix.

`Slide.OnRender(float local)` **must** therefore be pure. No `Time.*`, no
`deltaTime`, no coroutines, no self-advancing driver. This is enforced in review,
not in code — a runtime guard would cost more than it catches.

### Three-level transform ownership

| Level | Posed by | Never touched by |
|---|---|---|
| `Slide.Rect` | `SlideTransition` only (`anchoredPosition`, `localScale`, `Group.alpha`) | the slide, the push-in |
| `Slide.Content` | the push-in only | transitions, the slide |
| `Content`'s children | `Slide.OnRender` only | transitions, the push-in |

Because the levels are disjoint, a transition's scale and the push-in's scale
compose instead of clobbering, and `SlideTransition.Restore` never has to know
framing exists.

---

## 3. UniShapes, from code

One component, one outline provider, a stack of layers. Layers paint in list
order; index 0 is the bottom.

```csharp
var rt = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
var s  = rt.gameObject.AddComponent<UniShape>();
s.Shape = new PrimitiveShapeProvider { Kind = ShapeKind.RoundedRect, Radius = 36f, Smoothing = 0.35f };
```

**`ShapeKind`** — `RoundedRect 0, Circle 1, Ellipse 2, Capsule 3, Triangle 4,
Pentagon 5, Hexagon 6, Octagon 7, Star 8, Pie 9, Arc 10, Ring 11, CutDisk 12,
Parallelogram 13, Trapezoid 14, Rhombus 15, Cross 16, Heart 17`, plus three the
providers produce and `PrimitiveShapeProvider` refuses: `Polygon 18` (a closed
vector path), `Polyline 19` (an open one, stroked) and `Composite 20`. Every
analytic kind sizes itself from the `RectTransform`, so switching kind never
resizes anything.

**`PrimitiveShapeProvider`** — `Kind`, `UniformRadius` (default `true`; while true
`Radius` drives all four corners and `CornerRadii` is ignored), `Radius`,
`CornerRadii` (`x`=TL, `y`=TR, `z`=BR, `w`=BL), `Smoothing` (0 circular →
1 squircle), `CornerStyle` (`Round | Bevel | Scoop`), `Rounding` (local units,
on the cornered kinds), `StarPoints` (≥ 3), `StarSharpness`, `Start` / `End`
(degrees CCW from +x, Pie/Arc — hold `Start` and drive `End` for a progress arc;
there is no `Aperture`), `Cap` (`Round | Flat | Square`, Arc), `Thickness`
(fraction of the half-size, Arc/Ring/Cross), `Chord` (CutDisk), `Skew`
(Parallelogram), `Taper` (Trapezoid), `Fit` (`ShapeFitMode`).

**`CompositeShapeProvider`** — `Elements`, up to eight `CompositeElement`s folded
in list order: `Shape` (any provider but another composite), `Operation`
(`Union | Subtract | Intersect | Exclude | Morph`), `Progress` (Morph only: how
far this outline has replaced everything before it), `Blend` (seam fillet
radius), `Seam`, `Padding`, `Offset`, `Rotation`. **A chain of `Morph` elements
is a seekable morph sequence**: drive each `Progress` 0 → 1 in turn and a
rounded rectangle becomes a circle, a star, a hand-drawn path — one shape, one
layer stack, no cut. An element at `Progress` 0 is skipped entirely, so a chain
costs only its active members.

**`VectorShapeProvider`** — `Path` (a `BezierPath`, rendered at its authored
position: path space is the rect's local space with the origin on its pivot),
`FlattenTolerance`, `Rounding` (closed only). An **open** path is stroked to a
band: `Width`, `Cap`, and `Profile` (an `Ease` — thickness along the path as a
multiple of `Width`). Move a knot with `path[i] = knot` and carry both handles
with the anchor, or a corner turns into a curve. `Stage.Contour` builds one from
authored points at a tolerance solved against the atlas budget.

**Layers** — every layer carries a `ShapePaint` named `Paint` (its colour is
`Paint.Color`; there is no `.Color` on the layer). `StrokeLayer.Width/.Align`
(−1 inside, 0 centred, +1 outside); `ShadowLayer.Offset/.Blur/.Spread`;
`InnerShadowLayer.Offset/.Blur`;
`BevelLayer.LightAngle/.Width/.Strength/.ShadowSide`;
`NoiseLayer.Frequency/.Seed/.Contrast`; `FilterLayer.Filter/.Strength` recolours
every layer below it and emits nothing. Every layer also has `Enabled`, `Shift`,
`Rotation` and `Padding` (`x`=left, `y`=bottom, `z`=right, `w`=top; positive
shrinks inward, and it insets corner radii **concentrically**).

**The stack is `UniShape.Layers`, a `StateList`** — `Add`, `Insert(0, shadow)`,
`Remove`, `Clear`; there is no `AddLayer<T>` (the package README is behind the
code). Shadows go under the fill: insert at 0. Everything else appends. A layer
at zero magnitude — stroke `Width` 0, shadow `Blur` 0 at alpha 0 — draws
nothing, which is how a reel keeps every layer alive from its first frame and
grows it in when its beat comes.

### Traps

- **Public properties on `UniShape`, `PrimitiveShapeProvider`, `ShapeLayer` and
  `UniTextBase` do not exist in source.** They are generated by the Core Roslyn
  state generator from `[SerializeField, StateProperty] private T camelCase`.
  Grepping for `public float Radius` finds nothing. Read the *field* and
  PascalCase it. `api.json` omits every generated property.
- **Never subclass `UniShape`, `ShapeLayer`, `ShapePaint`, `UniTextBase` or
  `BaseModifier`.** Doing so opts the type into the state analyzer, whose 24
  `LSST` diagnostics are all **errors**: every `[SerializeField]` then needs a
  state attribute, the type and all containing types must be `partial`, and no
  tracked field may be written directly. Compose, never inherit.
- **A `ShapeLayer`, a `ShapePaint` or a `BezierPath` belongs to exactly one
  owner.** Attaching one twice throws `InvalidOperationException`. Every widget
  factory constructs fresh instances.
- One draw call per shape as long as every layer is `LayerBlend.Normal` and
  untextured. A bevel pair needs Multiply + Additive and costs two more — fine
  when deliberate, and never on a frame with a draw-call counter pinned to it.
- `UniShape.defaultMaterial` is `Resources.Load<Material>("LightSide/Defaults/UniShape")`
  and **throws** if absent.
- **A `UniShape` under a scaled or rotated `RectTransform` renders wrong — and that
  includes an animated `localScale` and the slide's own push-in.** uGUI transforms
  the NORMAL and TANGENT streams by the element's local-to-canvas matrix when it
  batches — undocumented, reported on the Unity forum since 2015, and reproduced
  in this reel twice — and `UniShape.AddSdfQuad` carries the shape's parameters
  in TANGENT (`geo.Params`). Under a scale `s`: a star's point count becomes `n · s` and its
  angular fold no longer closes (a forked bottom spike on an even count, a stray
  spike in the bottom notch on an odd one); a polygon's or composite's atlas row
  becomes `row · s` and the field reads a neighbouring row — another frame's
  outline, or another shape; corner radii are scaled a second time. UV channels are
  never transformed. Until the contract moves the parameters off TANGENT, a shot
  **resizes** a shape's rect and path instead of scaling it, and the shapes rig runs
  with push-in off. The symptom hides behind everything that scales gently — a pop
  from 0.86, a 0.93 rest, a 3.5 % push-in — and shows only where a parameter must be
  exact.

---

## 4. Gradients

`LightSide.Gradient` (Core, `Runtime/Graphics/Gradient.cs:44`) — unlimited stops,
`GradientInterpolation.Smooth | Linear | Perceptual | Stepped`. **Perceptual is
Oklab and is the right default for a brand ramp on camera.**

It reaches a shape through `ShapePaint`: `Kind` (`Solid|Gradient|Texture`),
`Color` (solid colour, or a tint over the sampled ramp), `Gradient`, `Texture`,
`Blend`, `ProjectionKind` (`Linear|Radial|Angular`), `Fit`, `Angle`, `Scale`,
`Offset`, and `FollowShape` — the distance projection. With it on, the ramp runs
inward from the outline: `t = -d / (min(halfWidth, halfHeight) · Scale)`, so
`Scale` 1 reaches the centre of a square and `Scale` 0.2 is an edge band a fifth
of the half-size deep. Spread lives on the `Paint` struct, not on a property —
`var p = paint.Paint; p.projection.spread = PaintSpread.Repeat; paint.Paint = p;`
— and with `Repeat`, a short distance ramp is concentric neon rings.

Two conversions are not guessable and will read as bugs — they are lifted
verbatim from `CalorieUIBuilder.cs:1009-1031`:

- **Radial**: `Scale = 1f / Mathf.Max(scale, 0.001f)` — the paint's scale is an
  inverse zoom.
- **Angular**: `Angle = Mathf.Repeat(180f - angleDeg, 360f)`, and the ramp needs
  **three** stops `a → b → a` so the sweep closes without a seam.

`LightSide.Gradient` **shadows** `UnityEngine.Gradient`. Alias at the top of any
file that touches both:

```csharp
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;
```

**Animating a gradient leaks atlas rows.** `ShapePaint` caches the gradient and
re-acquires an atlas row only when the value differs, so assigning an *equal*
gradient is free — but building a new one with different stops every frame burns
a row per frame, reclaimed only by a sweep every 300 frames. **Animate the
projection (`Angle`, `Offset`, `Scale`), never the stops.** It is cheaper and it
looks better.

An empty gradient evaluates to **opaque white**, not transparent. A missing
texture renders **magenta**.

---

## 5. UniText, from code

```csharp
var t = rt.gameObject.AddComponent<UniText>();
t.raycastTarget = false;
t.FontSize = 64f;
t.color = Color.white;
t.HorizontalAlignment = HorizontalAlignment.Center;
t.VerticalAlignment = VerticalAlignment.Middle;
t.SetText("Every writing system on Earth");
```

No font asset, no material, no atlas: `SystemFont.Default` is the implicit
primary and it renders with zero setup.

**Styles.** `Style.WholeText(modifier)`, `Style.Range(modifier, start, end)`,
`Style.Tag(modifier, "tagName")`, `Style.FromSource(source, modifier)`. Add to
`text.Styles`.

**Animation — promo writes none of it.**

```csharp
// build
var reveal = stage.Reveal(t, new FadeRevealHandler());
t.Styles.Add(Style.Range(new WaveModifier(), start, end));

// render
reveal.Fill                           = ease.Window(local, 0.2f, 0.8f);
t.GetModifier<WaveModifier>().Phase   = local;
```

`GetModifier<T>()` returns the live instance and its setters route the minimum
dirty flag. Nineteen `RevealHandler`s (Fade, Scale, Slide, Spin, Tint, Pop, Drop,
Domino, Flip, Stretch, Swing, Spiral, Burst, Shake, Glitch, Rain, Wave, Skew,
Chaos) and eleven phase-driven glyph modifiers come free.

**`RevealModifier.Front` decides *which* glyphs are visible; a `RevealHandler`
decides what each one *does* as it arrives — and without one the reveal is a
hard cut.**

There are two ways to attach a handler and **only one of them works in a
seekable reel**:

| | how it is timed | usable here |
|---|---|---|
| `RevealHandlerEntry` in the modifier's `Provider` | a one-shot timeline off `Stopwatch.GetTimestamp()`, armed when a glyph crosses the frontier (`RevealModifier.cs:793`) | **no** |
| `RevealModifier.GlyphRevealing` event | `Progress = clamp01(front - ordinal)`, derived from `Fill` alone | **yes** |

The serialized-entry path cannot be scrubbed backwards, and under a capture that
steps frames faster than the wall clock every glyph freezes at the start of its
animation. `Stage.Reveal` therefore subscribes the handler to the event:

```csharp
var modifier = new RevealModifier { Front = UnitValue.Percent(100f), Collapse = false };
modifier.GlyphRevealing += reveal.Apply;
text.Styles.Add(Style.WholeText(modifier));
```

`RevealHandler.Apply` is public and `EasedRevealHandler.Progress` reads only
`info.Progress`, so every shipped handler works unchanged on the event path.
`RevealHandler.Duration` does **not** — it exists solely to arm the timeline
above, so setting it on this path is a lie in the source.

The event fires **mid mesh build, possibly on a worker thread**. No Unity API
calls inside a handler; touching the quad is all it may do.

A glyph's animation is **one ordinal of the frontier wide** in the engine's own
model — its length is the reveal's duration divided by its glyph count, which is
a few dozen milliseconds on any real sentence and reads as a pop.
`Stage.Reveal(text, handler, spread)` widens it (`Theme.RevealSpread`, default
4.5): neighbours overlap and each glyph moves for longer without the reveal
itself slowing down.

**Overlap cannot ride on `RevealModifier.Front`, and the reason is worth knowing
before trying it again.** That frontier does two jobs at once — it decides
visibility *and* it feeds `Progress = clamp01(front - ordinal)` — and it stops at
`count`. A glyph at ordinal `k` needs it to reach `k + spread`:

| frontier | last glyph | tail |
|---|---|---|
| unstretched, ends at `count` | reaches `1 / spread` | **freezes half-played** |
| stretched by `(count + spread - 1) / count` | reaches `1` | **pops in already ~70 % done**, because the stretched frontier crosses `k` before the visibility gate opens at `k` |

Both were shipped and both were wrong. The fix is to stop having two frontiers:
`GlyphReveal` pins the modifier at `Fill = 1` so it hides nothing, and derives
every glyph's progress from its own `Fill` over a runway of `count + spread - 1`.
The cost is a hard requirement — **the handler must render nothing at progress
0**, which every shipped handler does (they all end in `ApplyFade(info, t)`) and
one with `fade = false` does not.

Handler offsets are in **pixels and do not scale with the type**. The shipped
`SlideRevealHandler` default is 12 px, which is invisible under a 168 px
headline.

**Drive `Fill` with `GlyphReveal.Frontier` (linear), never with an object curve.** A
frontier is a counter, not a body in motion: easing it crams the letters into
the opening moments and then crawls, so the last glyph lands long after the eye
stopped waiting. The character belongs to the per-glyph handler.

Do not fade the whole text at the same time as revealing it — the two fades
multiply and the glyphs arrive twice.

Paint layers: `FillModifier`, `StrokeModifier`, `ShadowModifier`,
`GlowModifier`, `InnerShadowModifier`, plus `ExtrudeModifier` and
`HighlightModifier`. A gradient or texture on text is reachable **only** through
a named swatch — use `InlinePaintProvider` and `PaintRef.Named(...)`; an inline
colour token cannot express one.

### Traps

- **Never attach `UniTextPhaseDriver`.** It advances `Phase` from its own
  `Update` off `deltaTime`, which destroys seekability. Slides write
  `modifier.Phase = local`. Same reason: no `UniTextAnimationBridge`, no
  `Animator`.
- **Use `SetText`, never the `Text` setter.** The setter writes the serialized
  field and dirties the scene every frame under `[ExecuteAlways]`.
  `SetText(ReadOnlyMemory<char>)` and `SetText(char[],int,int)` **retain** the
  caller's buffer; the `ReadOnlySpan<char>` and `StringBuilder` overloads copy
  into a pooled buffer and are the safe ones.
- `CaptureTextSnapshot()` **throws** until the component has completed its first
  parse. Do not open a mutable range update in the same `OnBuild` that created
  the text.
- `SetAllDirty()`, `SetVerticesDirty()` and `SetMaterialDirty()` are deliberate
  no-ops. Use `SetDirty(UniTextDirty)`.
- **Not all text animation is cheap.** `RevealModifier.Front` is mesh-only while
  `Collapse == false`; with `Collapse == true` it reflows every frame.
  `VariationModifier`'s axis setters (`Weight`, `Width`, `Italic`, `Slant`,
  `OpticalSize`), `RollingModifier.Wheel` and `ScrambleModifier.Charset` re-parse,
  re-shape and re-rasterize. Animate those in coarse steps or not at all.

---

## 6. Motion, and what was learned the hard way

These numbers were paid for once, in a project that is gitignored. They are the
reason this file exists.

**Easing.** Core's `EasingType.Evaluate(float)` extension ships 35 built-in curves
and no custom curve. It does not clamp its input, the back and elastic families
overshoot past 0 and 1, and it **throws** on an undefined enum value. Always go
through `Ease`, which clamps.

The minimum-jerk quintic `10t³ − 15t⁴ + 6t⁵` is the scientifically correct human
reaching curve and it is **dead on screen** — symmetric, broad velocity bell,
indistinguishable from linear over a short move. Motion design drags the handles
out instead. Ship Material 3 emphasized. At t = 0.5: linear 0.50, min-jerk 0.50,
emphasized 0.86.

| Token | Curve | Use |
|---|---|---|
| `Ease.Emphasized` | `(0.2, 0, 0, 1)` | begins and ends on screen — the default |
| `Ease.EmphasizedIn` | `(0.05, 0.7, 0.1, 1)` | entering the frame |
| `Ease.EmphasizedOut` | `(0.3, 0, 0.8, 0.15)` | leaving the frame |
| `Ease.StandardIn` | `(0, 0, 0, 1)` | utility enter |
| `Ease.StandardOut` | `(0.3, 0, 1, 1)` | utility exit |

**Springs.** `Spring` is closed-form and evaluated from elapsed seconds, never
integrated. That is a recording requirement: an offline render steps time in
fixed increments unrelated to wall clock, and any `deltaTime` integrator produces
different motion in the captured file than in the preview. Presets: `Pop`
(260/24), `Bouncy` (180/12), `Stiff` (210/20), `Smooth` (158/25.1), `Heavy`
(95/30/2.2), `Rise` (120/critical), `Exit` (300/35).

A spring that overshoots **travels backwards**. For anything that rises and
fades, keep the damping ratio at or above 1 — `Rise` and `Smooth` do.

Do **not** copy the `ButtonJuice` idiom
(`Mathf.Lerp(cur, want, 1f - Mathf.Exp(-k * dt))`,
`CalorieCounter/Runtime/ButtonJuice.cs:26`). It consumes `deltaTime`, cannot
overshoot, and is not seekable.

**No global tempo multiplier.** One was tried. Scaling every duration by 0.66
turned a 340 ms ripple into 7 frames and a 70 ms press into **one frame** — the
clicks did not get faster, they disappeared. Pacing may be scaled; perception
floors (a press, a ripple, a hold on a payoff) never may. Dynamics is contrast,
not uniform speed: a shot reads as dynamic when motion is quick and then *stops
dead* on the thing it just did.

**No shot is ever still.** Push-in is default-on at 0.035 over a slide. A frame
with zero motion reads as a screenshot.

**Push-in eats the margins, and it eats the outermost one first.** It scales the
whole content from the centre, so by a slide's last frame every edge has moved
outward by 1.75% of the half-frame — 17 px at 1920 wide. An element placed 38 px
from the edge is 6 px from it when the shot ends. Size an outer margin from where
it lands at full push-in, not from where it is built.

**The caveat that outranks it:** anything the eye or a cursor is aiming at enters
with **opacity only**, never a transform. A moving target means the pointer and
its target disagree about where the target is for as long as the animation runs.
Push-in is safe precisely because it moves the whole frame, not one element.

**Stagger.** Two elements arriving on the same frame land as one flat slab; six
to twelve frames apart they read as two objects and hand the eye an order.
`ease.Window(local, start + i * step, dur)`.

**Concentric corners.** `inner = max(4, outer − gap)`. Equal radii on parent and
child swell the corner gap to `gap × √2` — a 41% bulge nobody can name but
everybody sees. `Theme.Inner` does this; never type the second radius.

---

## 7. Theme values

Brand ramp: coral `#F5726B`, orange `#F58E4A`, magenta `#F03CD0`, violet
`#7B3FF2`, glow `#C13BE0`. Sampled from the shipped mark at
`Packages/media.lightside.unitext/Editor/Resources/UniText/Icons/unitext-icon.png`.

Product surface (dark): bg `#14101F`, bar `#1D1730`, surface `#221B38`, line
`#332A50`, text `#F2EEFF`, textSoft `#A79BC8`.

Everything that is **not** UniText: ink `#141420`, inkSoft `#54546A`, paper
`#FFFFFF`, line `#E3E5F2`, canvas `#F6F6FC`, accent `#7A5BFF`.

> **The contrast is the message.** The product surface is dark and carries the
> brand ramp; every other surface is neutral paper. Two identical windows
> swapping text tells a first-time viewer nothing about which one is for sale.

Radii: xs 10, sm 14, md 20, lg 28, xl 36, xxl 48.
Padding: xs 6, sm 10, md 16, lg 24, xl 34.
Type at 1920×1080: hero 104/800, title 64/800, head 42/800, body 34/500,
small 28/600.

Those type sizes are **too small**. Remotion's own layout guidance specifies an
84 px headline at 1080 px width, scaled with composition width — 168 px at 1920.
Treat the table above as the floor, not the target, and give every frame one
dominant element.

Parse hex through `LightSide.ColorParsing.TryParse`, not by hand.

**A livery is a `Theme` subclass, picked on the reel.** `Reel.theme` is
`[SerializeReference, TypeSelector]`, so the palette is chosen and edited in the
reel's own inspector; every value is a serialized field behind a property, and
every stage derived by `Stage.For` shares the one instance. `DaylightTheme` is
the second livery: warm paper `#FDE3B7`, one violet `#480D8B`, and every other
product-surface value derived from those two through `Theme.Mix`, `Lift`, `Sink`
and `Fade`. Its neutral set stays cool on purpose — on a dark livery the product
is told apart by being the lit thing in the frame, which a light one cannot
offer, so temperature carries the distinction instead.

**Colours are baked into shapes at build time.** A livery reaches the frame
through `Rebuild`, never on its own; there is no `OnValidate` sledgehammer,
because a 24-slide rebuild per colour-picker frame locks the editor.

A brand ramp authored for near-black does not survive a light ground — the
orange end all but vanishes. `DaylightTheme` deepens the whole ramp with `Sink`.

**The livery owns the faces too.** `Theme.DisplayFace` is worn by `Claim`'s
headline, `Headline` and a `Metric` figure; `Theme.BodyFace` by everything else,
applied in `Stage.Label`. Both unassigned leaves the reel on the OS cascade,
which is what it shipped as. One family in two weights — Fira Sans ExtraBold
over Medium — rather than two families: two sans faces at 32 px read as an
accident, and the contrast a frame needs is available in weight and width.

**A livery dresses chrome, never the subject.** `Showcase.Body` and
`UniTextSpecimen` clear the face after `Label` builds them. The body of a
showcase and the text in a comparison are what the shot is arguing about — a
panel claiming a project needs no font assets, itself set in one, argues against
itself. Titles, claims, captions, ledger rows and chips are chrome and take the
livery.

**A single-script face is safe as either.** The provider resolves the assigned
font first, then the font stack, then `SystemFont.Default` as an always-on final
fallback (`UniTextFontProvider.cs:152`), so a Latin display face on the
eight-script frame shapes the other seven through the OS exactly as no face at
all would. Fonts live in `Assets/Fonts/Promo/` with their licence beside them.

---

## 8. Editor and lifecycle

- `CoreLoop` (`Core/Runtime/CoreLoop.cs:15`) is the only clock that ticks in both
  play mode and edit mode: `Updating`, `EditorUpdating`, `DeltaTime`,
  `RequestEditorFrame()`. `Time.deltaTime` under `[ExecuteAlways]` is meaningless
  in edit mode and only fires on inspector repaint.
  **`CoreLoop.DeltaTime` in edit mode is raw wall clock with no stall clamp** —
  clamp it yourself (~34 ms) or a domain reload jumps the playhead by seconds.
- Destroy with `ObjectUtils.SafeDestroy`, never a hand-rolled
  `Destroy`/`DestroyImmediate` pair.
- Editor UI: build from `InspectorVisuals.CreateRoot/CreateCard/CreateRailSection/CreateRow`
  and `InspectorControls.InspectorSlider/InspectorIconButton/InspectorPillButton`
  (all in `LightSide.Core.Editor`, namespace `LightSide`). No hand-rolled IMGUI.
- Any cached `RenderTexture`/`Texture2D` must be released from
  `EditorLifecycle.UnmanagedCleaning`, registered in a static constructor —
  `SharedMeshes.cs:37` is the template. `EditorLifecycle` is inside
  `#if UNITY_EDITOR`; guard the subscription too.
- **Do not copy `CalorieUIBuilder.EnsureCanvas`.** It latches onto any
  non-world-space `Canvas` via `ObjectUtils.FindAny<Canvas>()` and will silently
  parent a shot into an unrelated portrait 1080×1920 canvas. Promo owns its
  canvas by name, landscape, `ScreenSpaceCamera` so there is a camera to capture
  from.
- There is **no** `InternalsVisibleTo` for this package and there must not be —
  it would mean editing another submodule. Everything used here is public.

---

## 9. Capture

`com.unity.recorder 5.1.6` is installed as a host-project dependency and is
referenced by zero scripts. This package deliberately does **not** depend on it:
the reel is already a pure function of time, so stepping `reel.Frame` and reading
pixels is deterministic, dependency-free, and works in edit mode where Recorder's
play-mode assumptions do not. Leave mp4 encoding to ffmpeg outside Unity.

**The contact sheet is the load-bearing feature.** A future session cannot watch
a video. It can look at one tiled still. Nothing is reported as finished until a
sheet has been rendered and read.

---

## 10. This package's own surface

| Type | File | What it is |
|---|---|---|
| `Reel` | `Runtime/Reel/Reel.cs` | `Fps`, `Frame`, `Duration`, `FrameCount`, `Playing`, `Theme`, `Seek(seconds)`, `Rebuild()`. Collects `Slide` children in sibling order. |
| `Slide` | `Runtime/Reel/Slide.cs` | `Seconds`, `Enter`, `PushIn`, `Rect`, `Content`, `Group`; override `OnBuild(Stage)` and `OnRender(float local)`. |
| `SlideTransition` | `Runtime/Reel/SlideTransition.cs` | `Seconds`, `Apply(from, to, t)`, `Restore(slide)`. Concrete: `Cut`, `CrossFade`, `Push`, `Lift`. |
| `Ease` | core, `Runtime/Math/Ease.cs` | `Of`, `Cubic`, `Evaluate`, **`Window(local, start, seconds)`**, `Lerp`, and the M3 presets. |
| `Spring` | core, `Runtime/Math/Spring.cs` | `Evaluate(seconds)`, `Lerp`, `Ratio`, `SettleTime()`, `Critical()`, seven presets. |
| `Stage` | `Runtime/Stage/Stage*.cs` | Context (`Root`, `Theme`, `Frame`, `For`, `Fit`, anchors); layout (`Node`, `Stretch`, `Anchor`, `Box`, `Row`, `Column`, `Size`, `Pad`); paint (`FillOf`, `Solid`, `Linear`, `Radial`, `Angular`, `Ramped`, `Textured`, `AddStroke/Shadow/InnerShadow/Bevel`); widgets (`Shape`, `Backdrop`, `Panel`, `Card`, `Field`, `Button`, `Bar`, `Label`, `Headline`, `Caption`, `BrandFill`); pose (`Alpha`, `Progress`, `Scale`). |
| `Theme` | `Runtime/Stage/Theme.cs` | Palette, `Brand` ramp, radius / padding / type scales, `Inner(outer, gap)`, and the derivation statics `Mix` / `Lift` / `Sink` / `Fade`. Subclass it for a livery; `Brand` is its one virtual. |
| `ShapesTheme` | `Runtime/Stage/ShapesTheme.cs` | The UniShapes livery: warm charcoal, `Brand` overridden to the mark's yellow → pink ramp. |
| `Slider` | `Runtime/Shapes/Stage_Slider.cs` | `stage.Slider(well, size, min, max, format)`; `Pose(t)`, `At(t)`, `KnobAt(t, space)` for aiming a pointer, `KnobSize`. |
| `Stage.Picker` | `Runtime/Showreel/StyleStack.cs` | A dropdown chip with a rewritable label; the caller anchors it. |
| `Stage.Contour` | `Runtime/Stage/Stage_Vector.cs` | A `VectorShapeProvider` through authored points, open or closed, at a tolerance solved against the atlas budget. |
| `Widget` | `Runtime/Stage/Widget.cs` | `Shape`, `Rect`, `Fill`, `Outline`; converts implicitly to `RectTransform`. |
| `Beat` / `PointerTimeline` / `Pointer` | `Runtime/Pointer/` | `Beat.To/Wait/Click/Drag/Key`; the timeline compiles them into a path, presses, keystrokes and named spans; `Pointer.Pose(seconds)` draws it. Build with `stage.Pointer(start, beats)` — **last**, so it is above what it operates. |
| `PointerTiming` | `Runtime/Pointer/PointerTiming.cs` | Fitts constants, travel clamp, peak-speed floor, bow, dwells, press and ripple floors. |
| `Cue` | `Runtime/Reel/Cue.cs` | A named moment. `Slide.Cue(...)` declares one, `Reel.CueSheet()` collects them onto the reel's clock, capture writes `cues.csv`. |
| `Mark` | `Runtime/Stage/Mark.cs` | `stage.Mark(panel, spec)` — a `Ring` around a glyph or an `Underline` beneath it. `MarkSpec` is serialized data: `centre` and `size` are fractions of the panel, and become the mark's anchors, so nothing is measured. |

### A comparison teaches one rule at a time

`VersusSlide` is authored once per case and added to the reel several times.
A paragraph exercising eight rules at once shows everything and teaches nothing:
the difference is on screen and nobody who has not already been told what to
look for finds it. Give a case a short string, set it large in
`VersusFlow.Rows`, and circle the glyph that moved; the whole postcard comes
last, in `Columns`, as the payoff rather than the argument.

**Where a mark goes is not derivable.** Which glyph an engine misplaced is
visible in a rendered frame and nowhere else — build the shot, read the contact
sheet, then fill `marks` in the inspector and scrub. That loop is the reason
they are serialized data and not code.

### The four shapes a shot is built from

Almost every slide is one of these plus a `Claim`. Reach for one before writing
layout code; each sizes itself, and each poses per row so the viewer watches
things *arrive* rather than appear.

| Builder | Handle | The shot it makes |
|---|---|---|
| `stage.Claim(parent, headline, sub, top)` | `Claim` | The lines the shot argues with. **Reserves its band** — build it first, then lay content out against `ContentCentre` / `ContentHeight`. |
| `stage.Showcase(name, parent, title, markup, …)` | `Showcase` | A product panel with one titled well of live text. `Panel`, `Group`, `Title`, `Well`, `Body`, `Ring`. The default for any feature shot. |
| `stage.Ledger(name, parent, title, entries, …)` | `Ledger` | Titled rows with a badge and a trailing value. Facts, file lists, layer stacks. |
| `stage.Meters(name, parent, title, entries, …)` | `Meters` | Titled labelled bars. **A ratio only** — see below. Fractions are authored, never derived from the values. |
| `stage.Metric(name, parent, figure, caption, …)` | `Metric` | One large brand-painted figure with words under it. |

`Showcase` carries **markup**, so the slide binds the tags it uses before the
first render — a tag with no `Style` behind it renders as the literal text it is
written as, which on a marketing frame is worse than showing nothing.

### A bar is an instrument for a proportion

Reach for `Meters` only when the claim *is* a ratio — 80–250 MB against a few,
12 MB against 4.4. Anything else and the bar answers a question the shot never
asked.

The conformance panel taught this the hard way: four suites, every one passed in
full, drawn as four bars under four unequal counts. Every viewer read the same
thing — *some fraction of the tests passed* — which is the exact opposite of the
claim, and no amount of labelling fixes it, because a bar is read before a label
is. Pass and fail is a `Ledger` of ticks; the counts live in the trailing column,
where a number reads as a size and not as a score.

### Nothing on a frame may decide its own height

A slide knows its well's height when it builds it and cannot know its text's,
because that depends on where the words happen to wrap. Guessing produced the
same failure four times — a body spilling over its panel — so the guess is gone
from both directions:

- **`Showcase` bodies auto-size.** The `bodySize` argument is a **ceiling**, not
  a size: `MaxFontSize` is what you asked for, `MinFontSize` is 42 % of it, and
  the text shrinks into the well on its own. Past that floor the honest fix is
  less text, not smaller text.
- **Fixed-height slots never wrap.** `Claim` lines, `Ledger` and `Meters` rows,
  and `Metric` figures each live in a slot whose height was reserved before the
  text arrived, so a wrap would grow the element past its own slot and onto its
  neighbour. `WordWrap` is off on all of them: too-long copy runs off the edge,
  which is visible in one glance, instead of silently overlapping what is under
  it, which is not.

### Two reveals, and picking the wrong one is visible

| | `stage.Reveal(text, handler, spread)` | `stage.Typewriter(text, handler)` |
|---|---|---|
| Frontier | `GlyphReveal.Fill`, its own, running past the glyph count | `RevealModifier.Front`, the engine's |
| Unreached text | drawn at alpha 0 — **still occupies its line** | collapsed out of shaping and layout |
| Overlap | several glyphs mid-flight (`Theme.RevealSpread`) | one, the frontier cluster |
| Cost | mesh rebuild | reflow |
| Text moves as it arrives | no | yes |

**A body carrying a range decoration must use `Typewriter`.** `HighlightModifier`
— and everything built on it: mention chips, spoiler covers, find hits — derives
from `BaseRangeDecorationModifier` and draws a surface *behind* a range without
ever entering the per-glyph pipeline. It therefore takes no notice of the alpha
of the glyphs above it, and over text merely faded to nothing it appears as a
bare gradient shape floating in an empty panel. Collapsed text has no range left
to decorate, so the decoration cannot outrun its words.

The line decorations are **not** in this class and are safe under either:
`UnderlineModifier` and `StrikethroughModifier` derive from `BaseLineModifier`,
whose quads run through `onGlyph` with `isVirtualGlyph` set, so a reveal handler
fades them exactly like a face glyph.

Inline media is the uncertain case — it emits its own quad rather than a face —
so the slides that carry a sprite use `Typewriter` by default rather than
assume.

**A centred line must never collapse.** Collapsed text is removed from layout, so
a centred paragraph re-centres itself on every character the frontier adds and
the words slide sideways out from under the reader. `Typewriter` is for text
aligned to an edge that stays put; a centred line types with `Reveal`, whose
layout is settled before the first glyph arrives.

Everything else — plain type, where the point is the letters — wants `Reveal`,
whose wave is calmer and whose lines do not shift under the eye.

**A collapsed text cannot be measured. Measure first, collapse second.**
`Typewriter` starts at `Fill = 0` with `Collapse` on, which takes every cluster
out of shaping and layout — so `MeasureText` has nothing to shape and answers
with the padding, which for a layout query is zero. Anything positioned from
that lands on the element's left edge, a place plausible enough to read as a
position rather than a refusal. A slide that aims a pointer at a word inside a
collapsing body must compute the point **before** it attaches the typewriter.
`Stage.Advance` throws rather than return the zero, and its message names this
cause first.

### Slide inventory

| # | Slide | What it proves |
|---|---|---|
| 01 | `ScriptsSlide` | Eight writing systems, shaped |
| 02a–c | `VersusSlide` ×3 | TMP / RTLTMPro / UniText: one Arabic line, one Hindi line, then the whole postcard |
| 03 | `SystemFontsSlide` | A project with no font files in it |
| 03b | `CjkSlide` | A wall of Han, Hangul and kana, none of it in the project |
| 04 | `ConformanceSlide` | 891 757 official tests, zero failures |
| 05 | `EmojiSlide` | Composed sequences from the OS, 0 KB |
| 06 | `AssetChurnSlide` | The repository stays clean |
| 07 | `BuildSizeSlide` | No baked atlas; Zstd-22 on what ships |
| 08 | `RichTextSlide` | Bold, colour, gradient, link — real modifiers |
| 09 | `HighlightPaintsSlide` | One highlight, four geometry/paint frames |
| 10 | `KitStackSlide` | Layer stack, one draw call |
| 11 | `ExtrudeSlide` | Extrude and bevel — a look, in the glyph's plane |
| 12 | `LivingTextSlide` | Eleven kinetic tags + a scrub-exact reveal |
| 13 | `WorldTextSlide` | Damage numbers, TMP vs UniText, draw calls counted |
| 14 | `MentionsSlide` | `@` / `#` from parse rules, e-mail left plain |
| 15 | `SpoilersSlide` | Tap-to-reveal, covers following a wrap |
| 16 | `FindSlide` | `FindAll` over mixed scripts, styled by range sources |
| 17 | `FieldCopySlide` | Formatted copy and paste between fields |
| 18 | `InlineMediaSlide` | A picture laid out as a glyph |
| 19 | `InputKitSlide` | Mask, password and limit behaviours, typed live |
| 20 | `MathSlide` | OpenType MATH typesetting |
| 21 | `TypographySlide` | Ruby, lists, small caps, scripts |
| 22 | `SpeedSlide` | 2–20× faster than TextMeshPro and UI Toolkit |
| 23 | `TitleSlide` | End card |

### World text inside a canvas reel

`UniTextWorld` never produces canvas geometry — its `UpdateGeometry` is empty.
The world batcher packs instances into a hidden `MeshRenderer` that **inherits
the component's own Unity layer**, which is the only reason the reel's camera
(culling mask = UI layer) sees it at all. Three rules follow:

- **Depth must be negative.** The screen-space canvas sits at the origin and
  both are in the transparent queue, so camera distance decides the order. A
  sign at z = 0 fights the backdrop; a positive one is hidden by it.
- **A `CanvasGroup` cannot fade it.** Fade each instance's own `color` instead.
- **The slide after it must not cross-fade.** `Lift` and `CrossFade` dim the
  outgoing slide through its group, which world text ignores — it would sit at
  full opacity over the dissolve. Use `Push`, which translates both slides.
- **Turn `WordWrap` off.** A world text lays out inside its own rect like any
  other. A figure wide enough to break loses its tail below a rect sized for one
  line, and the result reads as a clipping fault rather than as wrapping.

Nothing else is needed: no second camera, no render texture, no layer change.

The same three rules apply to `TextMeshPro` (the 3D one, not the UGUI one),
which is also a `MeshRenderer` on its own object's layer — the reason the
draw-call shot can put both engines in the same frame. It needs one more:

**Set `isOrthographic = true` on world-space TextMeshPro.** Its glyph scale
carries a `m_isOrthographic ? 1 : 0.1f` multiplier, because 3D TMP assumes a
scene measured in metres while the reel's camera measures in pixels. Left at its
default the text renders at **one tenth** the size asked for — beside a
`UniTextWorld` at the same `fontSize` the comparison is meaningless, and the
mistake reads as "TextMeshPro draws smaller" rather than as a unit error.

**`RollingModifier.Roll` is a distance, not a phase.** Zero is the settled
reading; the value is how many wheel positions back the character is currently
showing, and a shot drives it *toward* zero. Feeding it a rising value — the
reflex, because every other animated modifier here takes `Phase = local` —
spins the wheel forever and it never lands, which is the one thing an odometer
must do.

It also rolls only characters present in its `Wheel`, which ships holding the
ten digits. Roll a number; a word of letters sits perfectly still under the
default wheel, and swapping in an alphabet buys a split-flap board rather than
the odometer the effect is named for.

### The showreel is one scene

`Tools / Promo / Create Showreel` builds the second film: a single
`ShowreelScene` slide (~21 s), not a slide list. Its phases hand off inside one
timeline — the card grows out of the pressed button, the wall pours beside the
card, the word slams in where the wall was — and every element that leaves is
**thrown** (`Motion.Whip`) off-frame by position, never faded in place.

Rules it lives by:

- **The card lands once and never leaves.** It is the film's causal spine: each
  effect a specimen takes has a row that drops into the card's Styles list on
  the same beat, so the viewer sees a modifier being added rather than a video
  effect. A row that runs ahead of, or behind, the layer it names breaks the
  only claim the reel is making. Rows leave with the specimen they described.

- Every phase time derives from `press = Aim + timeline.Mark("press")` in
  `Schedule()`; the scene sets its own `Seconds`. Never type a phase time.
- `Motion` (Showreel folder) is its curve vocabulary — `Back`, `Punch`, `Whip`,
  `Snap`, `Meter`. Every curve overshoots or anticipates; the argument reel's
  Material tokens are deliberately not used here.
- The `world` node under Content is the camera: strike knocks scale it ±1.2 %.
  The backdrop stays on `stage.Root`, outside the shake.
- Clusters park off-frame when inactive (`payload` at −0.55 W) and their pose
  writes run every frame, so any frame scrubs correctly in isolation.

### The shapes reel is one scene

`Tools / Promo / Create Shapes Reel` builds the UniShapes film: a single
`ShapesReelScene` (~28 s) in the `ShapesTheme` livery. One big shape in the
middle of the frame, a headline above it, and under it exactly the one control
the beat is about — no cursor, no component card, no cuts. Every phase time is
derived from the one before it in `Schedule()`.

| Beat | What happens | The one control under the shape |
|---|---|---|
| Cold open | A rounded rectangle pops in and runs through circle, star, heart, hexagon and back — one shape, morphing | A pill naming the outline it is becoming |
| Dial | The corners round out to a circle, then square off into a squircle | `Radius`, then `Smoothing`: a slider plate each, knob and figure moving |
| Layers | Shadow, Stroke, Inner Shadow land one strike at a time | `+ Shadow`, `+ Stroke`, `+ Inner Shadow`, one pill per strike |
| Paint | Radial, Angular, Distance — neon rings from the outline inward — one beat each | `Fill · Radial ▾`, the picker reading each name |
| Combine | One pulse of a circle per boolean op: it grows out of the shape, drifts while the op works — union with a liquid seam, subtract, exclude — and shrinks away before the op switches; intersect runs the pulse the other way, from a circle larger than the shape in to the lens and back, so the picture never changes on a switch | `Operation · Union ▾` |
| Vector | The shape morphs into a bolt, its anchors appear, one anchor travels and the outline follows | `Shape · Bézier ▾` |
| Zoom | The whole world scales 5× about the bolt's tip and back; the edge stays crisp | — |
| Motion | The bolt morphs into a star that grows a point at a time | `star.StarPointsTo(12, 0.4f)` |
| Screen | The hero is thrown off; a dashboard assembles from 25 shapes in a 1.2 s cascade, its rings, bars, toggle, slider and segments then coming alive; `1 draw call · 0 sprites` arrive last, above it | — |
| End | The shipped mark on a plate, the wordmark revealed, the site | — |

Rules it lives by, beyond the showreel's:

- **A composite is a transition, never a resting state.** The hero wears a
  `CompositeShapeProvider` only while a morph runs — the cold open's chain of
  six outlines, then a two-element composite for rectangle → bolt and another
  for bolt → star — or while the combine beat is folding a second outline into
  it, and every steady phase puts the plain provider on the shape
  (`ProviderAt`). The combine beat's circle lives on its element's `Padding`:
  at half the hero's size the element's rect is a point, a zero circle folds in
  nothing, and the hand-over to the plain rectangle draws the same pixels. Each composite ends at exactly the outline the plain provider
  starts from (same radius, same smoothing, the bolt with its dragged anchor),
  so the swap draws the same pixels on both sides of it. Eight elements is
  `MaxElements`; a ninth is silently ignored.
- **Nothing recoils.** No squash on a morph, no knock on a strike: a shape that
  jerks on every beat reads as broken, not as struck. The blow is in the layer's
  own spring overshoot.
- **Every layer exists from the first frame at magnitude zero** (stroke `Width`
  0, shadow `Blur` 0 at alpha 0) and grows in on its strike — the `KitStackSlide`
  rule, for the same reason.
- **The counters may only claim what the stack can prove.** Bevel needs Multiply
  and Additive and starts new sub-meshes, so no bevel appears on a counted
  frame; the dashboard is all-Normal and untextured for the same reason. The
  screen's "one draw call" also counts its labels, on the package's own claim
  that shapes and text share one material — confirm it in the Frame Debugger
  before the frame ships.
- **Knot positions are path coordinates.** A vector outline renders at its
  authored position with the path origin on the rect pivot, so an anchor's
  marker is a child of the hero at `point * scale` and needs no measuring. A
  drag moves the anchor *and both handles*; a corner whose handles stay behind
  becomes a curve.
- **A conic sweep of a multi-stop ramp needs the ramp folded onto itself**
  (`Seamless`): the brand ramp does not end where it starts, and an `Angular`
  projection shows the seam at the wrap.
- **Distance rings are `FollowShape` + `PaintSpread.Repeat`** on a ramp that is
  dark at both ends, with `Scale` as the ring pitch.
- **Nothing on the hero is ever scaled through a transform.** The pop is a rect
  resize, the zoom rebuilds the bolt five times larger — the path's knots, the
  markers on them and every layer's magnitude multiplied by the same factor — and
  the frame is held on the tip by moving the hero, not the world. See the TANGENT
  trap in §3: a scaled shape reads wrong parameters, and the two symptoms this
  reel produced before the rule existed were a star with a forked spike under the
  push-in and a composite flipping to a neighbouring atlas row at a 0.93 rest.
- **The mark is the shipped editor icon**, wired with `FindAsset(…, editorToo:
  true)`: it exists nowhere but under an `Editor` folder, and a reel is captured
  in the editor.

### Writing the lines on screen

A headline names what the viewer **gets**, in the plainest words available.
Mechanism goes on the second line or in the picture.

| Instead of | Write |
|---|---|
| A baked atlas is a file in your repository. | UniText keeps your repository clean. |
| Every look is a constructor. | Add as many layers as you like. |

Both columns are true; only the right one is a headline. A description of how
something works asks the viewer to derive the benefit themselves, and a viewer
doing arithmetic has stopped watching. Address them, keep it warm, and let the
proof sit underneath in the quieter line — `Stage.Claim(parent, headline, sub)`
builds exactly that pair and fades the second a beat behind the first.

Jargon belongs in the sub-line, never the headline. "No extra shaders. One draw
call." is a fine second line and a terrible first one.

### The pointer

One ordered list of beats, two readings: the path and the spans a slide reacts in. Never author
them separately — that is how a pointer ends up clicking before it arrives.

The arrow's tip is authored at the origin of its own contour, and `VectorShapeProvider` renders a
contour **at its authored position** (`PolygonShape.Resolve(..., fitToRect: false)`) rather than
fitting it to the rect. So the tip sits on the rect's own origin: moving the rect moves the tip,
and scaling it pivots on the tip. **There is no hotspot offset to measure**, which is the only
reason the predecessor's cursor pointed at the wrong place for three iterations.

A drag is a press, a travel and a release — not a click at each end. The button stays held for the
whole leg, and `PressAmount` renders that continuously; otherwise a selection looks like it is
happening by itself.

The ripple is centred on the **recorded press point**, never on the arrow. A ripple that follows
the pointer after the click is the classic tell of a cursor composited in afterwards.

Two rings, the second chasing 90 ms behind, because one expanding circle reads as a dot.

### Audio

There is none, by design: a frame-stepped capture runs faster or slower than a clock, so there is
nothing to record while it runs. Instead `Capture Frames` writes `cues.csv` — `seconds,frame,name`
— and the track is laid against it in ffmpeg or an NLE. `PointerTimeline.Cues()` emits `click` and
`key` from the same compiled beats that move the pointer, so a sound cannot land on a different
frame from the ripple it belongs to.

Pick the music **first** when the time comes. Editors cut picture to music; with no track there is
no timing reference and every pause is guesswork — which is exactly why the predecessor's pacing
was physically correct and rhythmically arbitrary.

`Window` is the most-called member in the package. Staggering is
`Window(local, start + i * step, seconds)` and needs nothing else.

`Bar` returns the **fill** rect, not the track. Drive it with `Stage.Progress`.

### The frame is fixed, and every size derives from it

`Reel.FrameSize` (default 1920×1080) is pinned onto the reel's own rect, and the
capture camera is sized to show exactly that. The canvas is
`ConstantPixelSize` at scale 1 — **never** `ScaleWithScreenSize`.

A canvas under a scaler is whatever shape the Game View happens to be, so a
slide laid out against it is composed for one aspect and captured at another and
every size in the film becomes a guess. With the frame pinned, `Stage.Width`,
`Stage.Height` and `Stage.Half` mean the same thing in every scene, on every
machine, in the editor and in the captured file alike.

**Derive every size from `Stage.Width` / `Stage.Height` / `Stage.Half`.** A
typed 520 is a number that will be wrong the first time the frame changes.
`Stage` throws if its root measures nothing, rather than laying a slide out into
a zero rect and letting the failure surface later as an overflow.

### Text assigned at build time goes through `Text`, not `SetText`

`SetText` replaces the runtime source and leaves the serialized field empty, so
the label does not survive a re-parse or a domain reload — and an empty UniText
is what shows a default. The property writes the serialized field. Per-frame
changes still use `SetText`, which does not dirty the scene.

### Traps inside this package

- **A reserved edge belongs to one frame.** `Stage.For` shares the band a
  `Claim` reserved, which is how content laid out under a headline knows the top
  is spoken for. `Stage.ForFrame` starts a composition with the edges free, and
  it is what the reel hands each slide — sharing them across slides makes every
  slide after the first lay itself out against a band some other slide reserved,
  and `ContentHeight` comes out short by an amount nothing on screen explains.
- **`Stage.Alpha` reaches one graphic and nothing beneath it.** A `Graphic`'s
  colour never propagates to children, so fading a `Panel` that way leaves its
  brand accent bar and every child at full opacity. Build a `CanvasGroup` with
  `Stage.Group` at build time, cache it, and set that.
- **`Stage.Progress` is a real resize, not a free anchor nudge.** The fill's
  width is entirely anchor-derived, so every call re-populates the shape's mesh,
  and under an ancestor layout group it queues a layout pass too. Cheap enough
  per frame; not free.
- **Nothing clips a shape's children.** A full-width bar on a rounded panel
  draws past both corners. `Panel` insets its accent by the panel's own radius.
- **A ramp that fades to transparent *black* darkens as it fades.** The shader
  premultiplies its texels, so the colour curve and the alpha curve multiply.
  Give the outer stop the inner stop's colour at zero alpha.
- **A damped spring has not arrived when its transition ends.** `Spring.Rise`
  over 0.4 s reaches 0.93, so a transition that uses the raw response parks the
  incoming slide short of its settled pose forever. `Lift` normalises by the
  response at its own end.
- **A vertical layout group with `childControlWidth` and no
  `childForceExpandWidth` drives every bare child to width zero.** Pass
  `expandWidth: true` unless the children carry their own preferred width.
- **`CoreLoop.RequestEditorFrame` exists only inside `#if UNITY_EDITOR`.** The
  runtime assembly builds for players; guard the call.
- **`LightSide.Promo.Editor` sees the runtime assembly's internals** through
  `Runtime/AssemblyInfo.cs`. An asmdef reference alone grants nothing — without
  that file every use of the internal `PromoMenu` is `CS0122`.
- **A Screen Space – Camera canvas sizes itself from the camera's current render
  target.** Bind the RenderTexture *before* `Canvas.ForceUpdateCanvases`, or the
  frame is laid out for the Game View and photographed at another size.
- **A stray graphic over the reel is a leftover scene object, never a label this
  package made.** `Stage.Label` assigns `Text` and `color` on every label it
  builds, so nothing it creates can carry content or a colour from anywhere
  else — and the shipped `Text (UniText)` template a new component seeds itself
  from is plain white with an empty `styles` list, so the seeding cannot supply
  one either. Both build commands replace only their own root object and clear
  nothing else, and a Screen Space – Overlay canvas elsewhere in the scene draws
  over the reel's camera regardless of its culling mask. Look outside the rig
  first; nothing inside it can produce a graphic no phase drives.

Available reveal handlers are `Fade`, `Scale`, `Slide`, `Spin`, `Tint`, `Pop`,
`Drop`, `Domino`, `Flip`, `Stretch`, `Swing`, `Spiral` and `Composite`. There is
no burst and no glitch.

A slide is posed by **two** transitions over its life — its own (which brings it
in) and the next slide's (which takes it out) — so the reel restores every slide
with both before composing. A `Restore` override must undo everything `Apply`
touches on *either* argument, and must be idempotent.

`Field` hands back its focus ring as an `out StrokeLayer` so a slide can drive
the ring without walking the layer stack every frame.

**A slide caches what it animates in ordinary fields, and those do not survive a
domain reload — but the built GameObjects do.** `Slide.IsBuilt` reports the
mismatch, `Slide.Render` draws nothing while it is false, and the reel repairs
itself on its next `CoreLoop` tick. The repair deliberately does not happen in
`OnEnable`: destroying a hierarchy while a scene is still loading is not
something to do for a convenience.

Menu paths are constants in `Runtime/PromoMenu.cs`. Never type one inline.

---

## 11. House style

Block-bodied namespaces (LangVersion 9 — no file-scoped namespaces, no records,
no global usings). Four-space indent, Allman braces, one public type per file.
`using System.*` first, then `UnityEngine.*`, aliases last. `camelCase` private
fields with no `s_`, `PascalCase` public. XML `<summary>` on public members and
no prose `//` comments — the summary states the contract, never the story of how
the code came to be.

There is no `.editorconfig` and no ruleset anywhere in the repository. Style is
enforced by imitation only; a file that does not imitate its siblings simply
looks foreign, silently.

Unity 2022.3 is the declared minimum in every `package.json` even though the
editor is 6000.5.2f1. Target .NET Standard 2.1 — `Span<T>` and `HashCode.Combine`
are available.

**Never compile, build, or write tests here.** Edit the source and stop; the
owner compiles in their own open editor and reports errors.
