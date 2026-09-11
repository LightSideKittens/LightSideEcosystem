using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A plain word becomes something elaborate one layer at a time, each layer carried onto it from the stack
    /// beside it.
    /// </summary>
    /// <remarks>
    /// Every layer exists from the first frame and arrives by growing its own magnitude from zero. Adding styles as
    /// the shot runs would be a structural change per beat — a re-parse, and a slide that cannot be scrubbed
    /// backwards. Growing a width is a mesh update and a pure function of time.
    /// <para>
    /// Styles are declared back to front, which is the reverse of the order they arrive in: a stroke aligned outward
    /// renders behind the fill, so the widest has to be furthest back or it buries the ones inside it.
    /// </para>
    /// <para>
    /// One thing moves at a time. The palette is dark while the stack assembles and the stack is quiet while the
    /// palette repaints, so the frame never offers the eye two subjects at once.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(KitStackSlide))]
    public sealed class KitStackSlide : Slide
    {
        [SerializeField] private string word = "PLAY";
        [SerializeField] private string headline = "Add as many layers as you like.";
        [SerializeField] private string sub = "One set of paints — colour, gradient, photo — on any layer you like.";
        [SerializeField] private string payoff = "No extra shaders. One draw call.";

        /// <summary>
        /// A display face for the specimen. Left empty, the system-font cascade supplies a text face, whose thin
        /// stems leave the outer strokes nothing to sit on.
        /// </summary>
        [SerializeField] private UniTextFont displayFont;

        /// <summary>
        /// One catalog of named paints, shared by every layer on the specimen.
        /// </summary>
        /// <remarks>
        /// The point of the shot is that this is a set, not a property of any one layer: a stroke, a shadow, a glow
        /// and a fill all take their colour from the same names, and a name can be a flat colour, a gradient or a
        /// texture without any of them knowing which.
        /// </remarks>
        [SerializeField] private UniTextPaints paints;

        private readonly Ease enter = Ease.EmphasizedIn;
        private readonly Ease approach = Ease.Emphasized;
        private readonly Ease strike = Ease.Of(EasingType.QuinticIn);

        private UniText specimen;
        private Ledger stack;
        private Chip[] chips;
        private Flyer[] flyers;

        private GlowModifier glow;
        private ShadowModifier shadow;
        private StrokeModifier outer;
        private StrokeModifier middle;
        private StrokeModifier inner;
        private FillModifier fill;

        private Claim claim;
        private Claim note;

        private RectTransform root;
        private Vector2 apex;
        private Vector2 impactPoint;
        private float[] bows;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            root = stage.Root;
            claim = stage.Claim(stage.Root, headline, sub);
            note = stage.Claim(stage.Root, payoff, top: false);

            var column = stage.Width * 0.58f;
            var strip = stage.ContentHeight * 0.26f;

            impactPoint = new Vector2(-stage.Width * 0.19f, stage.ContentCentre + strip * 0.5f);
            apex = new Vector2(stage.Width * 0.02f, stage.ContentCentre + stage.ContentHeight * 0.14f);

            specimen = stage.Label(stage.Root, word, stage.ContentHeight * 0.36f, theme.Text);
            stage.Box(specimen.rectTransform, impactPoint, new Vector2(column, stage.ContentHeight * 0.72f));
            if (displayFont) specimen.Font = displayFont;

            var catalog = new AssetPaintProvider { Asset = paints };
            var layerPaints = new[]
            {
                PaintRef.Named(Swatches[0]),
                PaintRef.Solid(Color.white),
                PaintRef.Named(PaintNames[2]),
                PaintRef.Named(PaintNames[3]),
                PaintRef.Solid(Soot),
                PaintRef.Named(PaintNames[5])
            };

            glow = new GlowModifier
            {
                Provider = catalog,
                Paint = layerPaints[5],
                Radius = UnitValue.Em(0f)
            };
            shadow = new ShadowModifier
            {
                Paint = layerPaints[4],
                Offset = new UnitVector2(Vector2.zero, UnitKind.Em),
                Blur = UnitValue.Em(0f),
                Spread = UnitValue.Em(0f)
            };
            outer = Stroke(catalog, layerPaints[3]);
            middle = Stroke(catalog, layerPaints[2]);
            inner = Stroke(catalog, layerPaints[1]);
            fill = new FillModifier { Provider = catalog, Paint = layerPaints[0] };
            fill.Tint = Clear;

            specimen.Styles.Add(Style.WholeText(glow));
            specimen.Styles.Add(Style.WholeText(shadow));
            specimen.Styles.Add(Style.WholeText(outer));
            specimen.Styles.Add(Style.WholeText(middle));
            specimen.Styles.Add(Style.WholeText(inner));
            specimen.Styles.Add(Style.WholeText(fill));

            BuildPalette(stage, catalog, -stage.Width * 0.46f, stage.ContentBottom + strip * 0.62f, strip);

            var entries = new LedgerEntry[Steps];
            for (var i = 0; i < Steps; i++)
                entries[i] = new LedgerEntry(LayerNames[i], PaintNames[i], (i + 1).ToString(), theme.Violet);

            stack = stage.Ledger("Stack", stage.Root, "One paint set, every layer", entries,
                new Vector2(stage.Width * 0.29f, stage.ContentCentre), stage.Width * 0.34f, onBrand: true);

            BuildFlyers(stage, catalog, layerPaints);

            bows = new float[Steps];
            for (var i = 0; i < Steps; i++) bows[i] = Bows[i] * stage.Height;

            for (var i = 0; i < Steps; i++)
            {
                Cue("whoosh", Launch[i]);
                Cue("snap", Launch[i] + Flight);
            }
            for (var i = 1; i < Swatches.Length; i++) Cue("repaint", Repaint + (i - 1) * Swap);
            Cue("settled", Payoff + Read);
        }

        protected override void OnRender(float local)
        {
            fill.Tint = Color32.Lerp(Clear, Opaque, Land(local, 0));

            inner.Width = UnitValue.Em(0.08f * Land(local, 1));
            middle.Width = UnitValue.Em(0.18f * Land(local, 2));
            outer.Width = UnitValue.Em(0.30f * Land(local, 3));

            var settle = Land(local, 4);
            shadow.Blur = UnitValue.Em(0.12f * settle);
            shadow.Spread = UnitValue.Em(0.27f * settle);
            shadow.Offset = new UnitVector2(new Vector2(0f, -0.09f * settle), UnitKind.Em);

            glow.Radius = UnitValue.Em(0.35f * Land(local, 5));

            var punch = Punch(local);
            Stage.Scale(specimen.rectTransform, 1f + SquashX * punch, 1f - SquashY * punch);

            for (var i = 0; i < Steps; i++)
            {
                stack.Fade(i, Mathf.Lerp(Quiet, 1f, enter.Window(local, Launch[i] - Lead, Lead)));
                PoseFlyer(i, local);
            }

            var active = Mathf.Clamp(Mathf.FloorToInt((local - Repaint) / Swap) + 1, 0, Swatches.Length - 1);
            fill.Paint = PaintRef.Named(Swatches[active]);
            stack.SetTrailing(0, Swatches[active]);

            var shown = enter.Window(local, PaletteIn, 0.5f);
            for (var i = 0; i < chips.Length; i++)
            {
                var chip = chips[i];
                var lift = i == active ? Spring.Pop.Evaluate(local - (Repaint + (i - 1) * Swap)) : 0f;

                Stage.Scale(chip.Rect, Mathf.Lerp(1f, 1.16f, lift) * shown);
                Stage.Alpha(chip.Block, Mathf.Lerp(Resting, 1f, lift) * shown);
                Stage.Alpha(chip.Label, Mathf.Lerp(Resting, 1f, lift) * shown);
            }

            claim.Pose(local);
            note.Pose(local - Payoff);
        }

        /// <summary>
        /// Poses the carrier of layer <paramref name="index"/>: out of its row, up to the apex, and down into the
        /// letters.
        /// </summary>
        /// <remarks>
        /// The hold at the apex is the readable part of the trip and is what stops the flight being a blur; the
        /// strike is a third of its length and accelerates the whole way, so the arrival is an event rather than an
        /// ease.
        /// </remarks>
        private void PoseFlyer(int index, float local)
        {
            var flyer = flyers[index];
            var t = local - Launch[index];

            if (t <= 0f || t >= Flight)
            {
                flyer.Group.alpha = 0f;
                return;
            }

            var from = Launchpad(index);
            Vector2 position;
            float scale;
            var spin = 0f;
            var alpha = 1f;

            if (t < Rise)
            {
                var k = approach.Evaluate(t / Rise);
                var travel = apex - from;

                position = from + travel * k + Perpendicular(travel) * (Mathf.Sin(Mathf.PI * k) * bows[index]);
                scale = Mathf.LerpUnclamped(Held, Large, k);
                spin = Mathf.LerpUnclamped(Spins[index], 0f, k);
                alpha = Mathf.Clamp01(t / (Rise * FadeIn));
            }
            else if (t < Release)
            {
                position = apex;
                scale = Large;
            }
            else
            {
                var k = strike.Evaluate((t - Release) / (Flight - Release));

                position = Vector2.LerpUnclamped(apex, impactPoint, k);
                scale = Mathf.LerpUnclamped(Large, Spent, k);
                alpha = 1f - Mathf.Clamp01((k - FadeOut) / (1f - FadeOut));
            }

            flyer.Group.alpha = alpha;
            flyer.Rect.anchoredPosition = position;
            Stage.Scale(flyer.Rect, scale);
            Stage.Spin(flyer.Rect, spin);
        }

        /// <summary>Centre of the row layer <paramref name="index"/> is carried out of, in frame coordinates.</summary>
        private Vector2 Launchpad(int index)
        {
            var rect = stack[index].Rect;
            return root.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        }

        /// <summary>
        /// The catalog laid out as a row of chips: the paint itself above, its name beneath.
        /// </summary>
        /// <remarks>
        /// Every swatch is on screen at once, and the one the fill is wearing lifts out of the row. That pairing is
        /// the whole argument — a viewer who reads nothing still sees a set of paints, and sees one of them travel
        /// from the row onto the letters.
        /// </remarks>
        private void BuildPalette(Stage stage, IPaintProvider catalog, float left, float middle, float height)
        {
            var theme = stage.Theme;
            var slot = BlockSize * SlotGap;
            var width = Swatches.Length * slot;
            var row = stage.Node("Palette", stage.Root);
            stage.Box(row, new Vector2(left + width * 0.5f, middle), new Vector2(width, height));

            chips = new Chip[Swatches.Length];
            for (var i = 0; i < Swatches.Length; i++)
            {
                var cell = stage.Node("Chip" + i, row);
                stage.Box(cell, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2((i + 0.5f) * slot, 0f), new Vector2(slot, height));

                var swatch = stage.Label(cell, Block, BlockSize, Color.white);
                stage.Anchor(swatch.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(0f, height * 0.58f));
                swatch.WordWrap = false;
                swatch.Styles.Add(Style.WholeText(new FillModifier
                {
                    Provider = catalog,
                    Paint = PaintRef.Named(Swatches[i])
                }));

                var label = stage.Label(cell, Swatches[i], theme.Small * 0.9f, theme.TextSoft);
                stage.Anchor(label.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    Vector2.zero, new Vector2(0f, height * 0.34f));
                label.WordWrap = false;

                chips[i] = new Chip(cell, swatch, label);
            }
        }

        /// <summary>
        /// One carrier per layer, built last so each travels above everything it passes over.
        /// </summary>
        /// <remarks>
        /// A carrier wears the layer's own <see cref="PaintRef"/>, so the paint that lands on the letters is the one
        /// that flew, not a repainted likeness of it. The outline is what keeps the black one visible against the
        /// backdrop it crosses.
        /// </remarks>
        private void BuildFlyers(Stage stage, IPaintProvider catalog, PaintRef[] layerPaints)
        {
            var theme = stage.Theme;
            flyers = new Flyer[Steps];

            for (var i = 0; i < Steps; i++)
            {
                var node = stage.Node("Carrier" + i, stage.Root);
                stage.Box(node, Vector2.zero, new Vector2(BlockSize * 1.7f, BlockSize * 1.8f));

                var swatch = stage.Label(node, Block, BlockSize, Color.white);
                stage.Anchor(swatch.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(0f, BlockSize * 1.2f));
                swatch.WordWrap = false;
                swatch.Styles.Add(Style.WholeText(new StrokeModifier
                {
                    Paint = PaintRef.Solid(theme.Text),
                    Width = UnitValue.Em(0.03f),
                    Align = 1f
                }));
                swatch.Styles.Add(Style.WholeText(new FillModifier
                {
                    Provider = catalog,
                    Paint = layerPaints[i]
                }));

                var label = stage.Label(node, PaintNames[i], theme.Small, theme.Text);
                stage.Anchor(label.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    Vector2.zero, new Vector2(0f, BlockSize * 0.55f));
                label.WordWrap = false;

                flyers[i] = new Flyer(node, Stage.Group(node));
            }
        }

        /// <summary>
        /// How far layer <paramref name="index"/> has arrived, on a bounce that carries it past its settled
        /// magnitude and back.
        /// </summary>
        private static float Land(float local, int index) =>
            Mathf.Max(0f, Spring.Bouncy.Evaluate(local - (Launch[index] + Flight)));

        /// <summary>
        /// The specimen's recoil from the most recent thing done to it: full strength for a layer landing on it,
        /// <see cref="Nudge"/> of it for a change of paint, decaying through zero as it rings out.
        /// </summary>
        /// <remarks>
        /// Only the last event is read. Summing them would let a recoil that has not finished add to the next one,
        /// so the shot would knock harder the longer it ran instead of repeating at one strength.
        /// <para>
        /// A repaint is a smaller event than an arrival and reads wrong at the same amplitude: nothing struck the
        /// letters, they only changed colour, and a full recoil claims an impact the frame never showed.
        /// </para>
        /// </remarks>
        private static float Punch(float local)
        {
            if (local >= Repaint)
            {
                var swap = Mathf.Clamp(Mathf.FloorToInt((local - Repaint) / Swap), 0, Swatches.Length - 2);
                return Nudge * Ring(local - (Repaint + swap * Swap));
            }

            for (var i = Steps - 1; i >= 0; i--)
            {
                var since = local - (Launch[i] + Flight);
                if (since > 0f) return Ring(since);
            }
            return 0f;
        }

        /// <summary>A struck body's decaying oscillation: 1 at the strike, settling to 0.</summary>
        private static float Ring(float since) => since <= 0f ? 0f : 1f - Spring.Bouncy.Evaluate(since);

        private static Vector2 Perpendicular(Vector2 travel)
        {
            var direction = travel.sqrMagnitude > 1e-4f ? travel.normalized : Vector2.right;
            return new Vector2(-direction.y, direction.x);
        }

        private static StrokeModifier Stroke(IPaintProvider catalog, PaintRef paint) => new StrokeModifier
        {
            Provider = catalog,
            Paint = paint,
            Width = UnitValue.Em(0f),
            Align = 1f
        };

        /// <summary>
        /// What the fill wears, in order, once the stack has assembled.
        /// </summary>
        /// <remarks>
        /// A gradient, a photograph and another gradient, all from the same catalog and all reached by name. The
        /// texture in the middle is the load-bearing one: a layer that accepts an image accepts anything, and no
        /// modifier here knows or asks which kind a name resolves to.
        /// </remarks>
        private static readonly string[] Swatches = { "gold", "fire", "ice", "neon", "candy", "photo", "shine" };

        /// <summary>The paint each layer wears while the stack assembles, in arrival order.</summary>
        private static readonly string[] PaintNames = { "gold", "white", "candy", "neon", "black", "shine" };

        /// <summary>The modifier each layer is, in arrival order.</summary>
        private static readonly string[] LayerNames =
        {
            "FillModifier", "StrokeModifier", "StrokeModifier", "StrokeModifier", "ShadowModifier", "GlowModifier"
        };

        /// <summary>
        /// When each layer leaves its row, in seconds.
        /// </summary>
        /// <remarks>
        /// The gaps shorten from 1.05s to 0.55s, so the sequence accelerates and its last two trips overlap. Six
        /// evenly spaced arrivals are a metronome, which is the thing a montage exists to avoid.
        /// </remarks>
        private static readonly float[] Launch = { 0.95f, 2f, 2.95f, 3.8f, 4.42f, 4.97f };

        /// <summary>Sideways bow of each trip, as a fraction of the frame's height and alternating in sign.</summary>
        private static readonly float[] Bows = { 0.13f, -0.17f, 0.11f, -0.15f, 0.14f, -0.1f };

        /// <summary>Tilt each carrier launches at, in degrees, unwound by the time it reaches the apex.</summary>
        private static readonly float[] Spins = { -14f, 11f, -9f, 16f, -12f, 8f };

        private static readonly Color32 Clear = new Color32(255, 255, 255, 0);
        private static readonly Color32 Opaque = new Color32(255, 255, 255, 255);

        /// <summary>The shadow layer's paint, and the swatch its carrier wears.</summary>
        private static readonly Color32 Soot = new Color32(0, 0, 0, 190);

        private const int Steps = 6;

        /// <summary>How long one trip lasts, from leaving the row to landing on the letters.</summary>
        private const float Flight = 0.66f;

        /// <summary>Point in a trip where the climb ends and the carrier holds at the apex.</summary>
        private const float Rise = 0.4f;

        /// <summary>Point in a trip where the hold ends and the strike begins.</summary>
        private const float Release = 0.52f;

        private const float Held = 0.6f;
        private const float Large = 2.6f;
        private const float Spent = 0.25f;

        /// <summary>Fraction of the climb spent fading up, and of the strike spent fading out.</summary>
        private const float FadeIn = 0.3f;

        private const float FadeOut = 0.6f;

        private const float SquashX = 0.13f;
        private const float SquashY = 0.17f;

        /// <summary>How much of a layer's recoil a change of paint is worth.</summary>
        private const float Nudge = 0.34f;

        /// <summary>How visible a stack row is before its layer is called for.</summary>
        private const float Quiet = 0.26f;

        /// <summary>How long a row is lit before its layer leaves it — the warning of where to look.</summary>
        private const float Lead = 0.22f;

        /// <summary>When the palette fades up: after the stack has finished assembling, never beside it.</summary>
        private const float PaletteIn = 5.98f;

        /// <summary>When the fill starts changing paint.</summary>
        private const float Repaint = 6.3f;

        private const float Swap = 0.42f;

        /// <summary>When the closing line arrives, after the last repaint.</summary>
        private const float Payoff = 8.9f;

        /// <summary>
        /// How long the closing line takes to finish arriving, and therefore when the shot is fully composed.
        /// </summary>
        /// <remarks>
        /// Everything after this is the tail: the frame settled, nothing moving, the viewer catching up with six
        /// layers, seven paints and a stack of names. It is the last thing a shot gets and the first thing a
        /// duration takes away — a slide that ends here ends on its own last frame of motion, which reads as a cut
        /// rather than as a finish. The <c>settled</c> cue puts it in the sheet so the tail is visible without
        /// counting frames.
        /// </remarks>
        private const float Read = 0.85f;

        /// <summary>How visible a chip is while some other paint is on the letters.</summary>
        private const float Resting = 0.42f;

        /// <summary>A full block — a swatch of the paint itself, drawn by the text engine.</summary>
        /// <remarks>
        /// A full block advances by about one em, so a swatch is <see cref="BlockSize"/> wide and its width is not
        /// settable independently of its height. The slot has to be sized from it, never the other way round.
        /// </remarks>
        private const string Block = "█";

        /// <summary>Point size of a swatch block, and therefore also its width.</summary>
        private const float BlockSize = 87f;

        /// <summary>A slot in multiples of <see cref="BlockSize"/>; the surplus is the air between two swatches.</summary>
        private const float SlotGap = 1.28f;

        /// <summary>
        /// A chip of one paint, drawn as text so the catalog stays the only place a paint is defined.
        /// </summary>
        /// <remarks>
        /// A shape's paint and a text's paint are different systems: painting the chip as a <see cref="UniShape"/>
        /// would mean re-authoring each gradient and re-pointing each texture, and the shot would then be claiming
        /// one set of paints while showing two. A block glyph under a <see cref="FillModifier"/> is the paint itself.
        /// </remarks>
        private readonly struct Chip
        {
            internal Chip(RectTransform rect, UniText block, UniText label)
            {
                Rect = rect;
                Block = block;
                Label = label;
            }

            public RectTransform Rect { get; }
            public UniText Block { get; }
            public UniText Label { get; }
        }

        /// <summary>One layer in transit: the rect that travels and the group that hides it either side of its trip.</summary>
        private readonly struct Flyer
        {
            internal Flyer(RectTransform rect, CanvasGroup group)
            {
                Rect = rect;
                Group = group;
            }

            public RectTransform Rect { get; }
            public CanvasGroup Group { get; }
        }
    }
}
