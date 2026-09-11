using System;
using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    /// <summary>
    /// The UniShapes showreel as one continuous scene: one big shape in the middle of the frame is born, morphed,
    /// dialled, layered, painted, redrawn and set in motion — one control at a time beneath it — and then a whole
    /// screen is built from nothing but shapes.
    /// </summary>
    /// <remarks>
    /// One slide, no cuts, no cursor and no component card. The frame holds the shape, a headline and exactly the
    /// one control the current beat is about. A morph is a <see cref="CompositeShapeProvider"/> the hero wears only
    /// while the morph runs: a composite evaluated per pixel is a transition, not a resting state, so every steady
    /// phase puts the plain provider it is about on the shape.
    /// <para>
    /// Build and schedule live here, the choreography in its own file, and the payoff screen and end card in a
    /// third.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(ShapesReelScene))]
    public sealed partial class ShapesReelScene : Slide
    {
        /// <summary>A headline and the quieter line under it, for one phase of the film.</summary>
        [Serializable]
        public struct Line
        {
            public string headline;
            public string sub;

            public Line(string headline, string sub)
            {
                this.headline = headline;
                this.sub = sub;
            }
        }

        /// <summary>The mark the film ends on. Left empty, the plate wears the brand ramp instead.</summary>
        [SerializeField] private Texture2D logo;

        [SerializeField] private string drawCalls = "1 draw call";
        [SerializeField] private string sprites = "0 sprites";
        [SerializeField] private string codeLine = "star.StarPointsTo(12, 0.4f)";
        [SerializeField] private string wordmark = "UniShapes";
        [SerializeField] private string site = "unity.lightside.media";

        /// <summary>One line per phase, in phase order; the reel refuses to build with any other count.</summary>
        [SerializeField] private Line[] lines =
        {
            new Line("Vector UI shapes.", "Drawn per pixel. Not one sprite."),
            new Line("Tune it live.", "Radius, smoothing. Nothing to re-import."),
            new Line("Stack layers. Any order.", "Shadow, stroke, inner shadow. One draw call."),
            new Line("Paint it anything.", "Unlimited stops. Even by edge distance."),
            new Line("Combine shapes live.", "Union, subtract, intersect, exclude."),
            new Line("Or draw your own.", "Bézier or SVG. Edit it right in the scene."),
            new Line("Sharp at any size.", "Nothing re-tessellates. Ever."),
            new Line("Animate the geometry.", "A parameter, not a new mesh. 43 MoveIt tweens."),
            new Line("Every panel. Every button.", "This whole screen is one draw call.")
        };

        /// <summary>The outlines the cold open runs through, in order, as the pill under the shape names them.</summary>
        private static readonly string[] ChainNames = { "Circle", "Star", "Heart", "Hexagon", RectName };

        /// <summary>The layers that land on the hero, one strike each, as their pills name them.</summary>
        /// <remarks>
        /// Not authoring surface. Each name is a layer this scene constructs and drives by hand, so a list that could
        /// be edited apart from the code would be a caption promising a layer nobody added.
        /// </remarks>
        private static readonly string[] StrikeNames = { "+ Shadow", "+ Stroke", "+ Inner Shadow" };

        /// <summary>The fill's projections as the picker names them; the first is the state the film starts in.</summary>
        private static readonly string[] PaintNames = { "Linear", "Radial", "Angular", "Distance" };

        /// <summary>The boolean ops as the picker names them, indexed by <see cref="CompositeOp"/>.</summary>
        private static readonly string[] OpNames = { "Union", "Subtract", "Intersect", "Exclude" };

        /// <summary>The ops the combine beat runs through, one pulse of the circle each.</summary>
        private static readonly CompositeOp[] OpSequence =
        {
            CompositeOp.Union, CompositeOp.Subtract, CompositeOp.Intersect, CompositeOp.Exclude
        };

        /// <summary>A lightning bolt in a ±50 box, wound as a simple polygon.</summary>
        private static readonly Vector2[] Bolt =
        {
            new Vector2(14f, 50f), new Vector2(-34f, -2f), new Vector2(-6f, -2f),
            new Vector2(-16f, -50f), new Vector2(34f, 4f), new Vector2(6f, 4f)
        };

        /// <summary>Where the bolt's right point is dragged to, in the bolt's own box.</summary>
        private static readonly Vector2 DragTo = new Vector2(56f, 20f);

        /// <summary>One control under the shape: its plate, and the group that hides it outside its beat.</summary>
        private readonly struct Control
        {
            public Control(RectTransform rect, UniShape shape, CanvasGroup group)
            {
                Rect = rect;
                Shape = shape;
                Group = group;
            }

            public RectTransform Rect { get; }
            public UniShape Shape { get; }
            public CanvasGroup Group { get; }
        }

        /// <summary>A control and the beat it is on screen for.</summary>
        private readonly struct Slot
        {
            public Slot(Control control, float at, float until)
            {
                Control = control;
                At = at;
                Until = until;
            }

            public Control Control { get; }
            public float At { get; }
            public float Until { get; }
        }

        private Theme theme;
        private RectTransform root;
        private float frameWidth;
        private float frameHeight;
        private RectTransform world;

        private Widget glow;
        private Widget hero;
        private CanvasGroup heroGroup;
        private CompositeShapeProvider chain;
        private CompositeShapeProvider boolean;
        private CompositeShapeProvider toBolt;
        private CompositeShapeProvider toStar;
        private CompositeElement[] chainMorphs;
        private CompositeElement bite;
        private CompositeElement boltMorph;
        private CompositeElement starMorph;
        private PrimitiveShapeProvider chainBase;
        private PrimitiveShapeProvider dial;
        private PrimitiveShapeProvider star;
        private VectorShapeProvider bolt;
        private Vector2[] dragged;
        private float zoomNow = 1f;
        private ShadowLayer shadow;
        private StrokeLayer stroke;
        private InnerShadowLayer inner;
        private Ramp brand;
        private Ramp seamless;
        private Ramp neon;
        private float heroSize;
        private float boltScale;
        private float controlY;

        private CanvasGroup knotsGroup;
        private Widget[] knots;
        private Vector2 knotFrom;
        private Vector2 knotTo;
        private Vector2 zoomFocus;

        private readonly List<Slot> slots = new();
        private Control kindPill;
        private UniText kindLabel;
        private Control radiusPlate;
        private Control smoothPlate;
        private Slider radius;
        private Slider smooth;
        private Control[] strikePills;
        private Control fillPlate;
        private UniText fillPick;
        private Control opPlate;
        private UniText opPick;
        private Control shapePlate;
        private Control codePill;

        private Claim[] claims;
        private float[] claimAt;
        private float[] claimUntil;

        private float chainEnd;
        private float dialAt;
        private float smoothAt;
        private float layersAt;
        private float paintAt;
        private float booleanAt;
        private float boolEnd;
        private float[] opAt;
        private float vectorAt;
        private float boltEnd;
        private float knotAt;
        private float zoomAt;
        private float motionAt;
        private float starEnd;
        private float pointsAt;
        private float finale;
        private float appStart;
        private float appDone;
        private float end;
        private Vector2 radiusRun;
        private Vector2 smoothRun;
        private Vector2 knotRun;
        private float[] strikes;
        private float[] paints;

        protected override void OnBuild(Stage stage)
        {
            theme = stage.Theme;
            root = stage.Root;
            frameWidth = stage.Width;
            frameHeight = stage.Height;
            brand = theme.Brand;
            seamless = Seamless(brand);
            neon = Neon(theme);

            stage.Backdrop(stage.Root);
            BuildClaims(stage);

            world = stage.Node("World");
            stage.Stretch(world);

            heroSize = frameHeight * HeroFraction;
            controlY = -heroSize * 0.5f - ControlGap - frameHeight * ControlHeight * 0.5f;

            BuildHero(stage);
            BuildKnots(stage);
            BuildControls(stage);
            BuildApp(stage);
            BuildEnd(stage);
            Schedule();
        }

        private void BuildClaims(Stage stage)
        {
            if (lines.Length != LineCount)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' needs exactly {LineCount} lines, one per phase; it has {lines.Length}.");

            claims = new Claim[LineCount];
            for (var i = 0; i < LineCount; i++)
                claims[i] = stage.Claim(stage.Root, lines[i].headline, lines[i].sub, size: theme.Head * 1.15f);
        }

        /// <summary>
        /// The hero, centred in the frame, with the three providers it rests on, the three composites it morphs
        /// through, and every layer the film will add to it, each at a magnitude of zero.
        /// </summary>
        /// <remarks>
        /// Adding a layer mid-film would be a structural change per beat and a scene that cannot be scrubbed
        /// backwards; growing a width is a pure function of time. A morph composite hands over to the plain
        /// provider at exactly the outline it ends on, so the swap draws the same pixels on both sides of it.
        /// <para>
        /// The hero is never scaled through its transform — it is resized. uGUI transforms the tangent stream by
        /// the element's local-to-canvas matrix when it batches, and that stream carries the shape's parameters:
        /// under any scale a star's point count stops being an integer and a polygon's atlas row lands on a
        /// neighbour's.
        /// </para>
        /// </remarks>
        private void BuildHero(Stage stage)
        {
            glow = stage.Shape("Glow", world, ShapeKind.Circle);
            stage.Box(glow.Rect, Vector2.zero, Vector2.one * (heroSize * GlowSpread));
            Stage.Radial(glow.Fill, Theme.Fade(theme.Orange, GlowAlpha), Theme.Fade(theme.Orange, 0f));

            hero = stage.Shape("Hero", world, ShapeKind.RoundedRect);
            stage.Box(hero.Rect, Vector2.zero, Vector2.one * heroSize);
            heroGroup = Stage.Group(hero.Rect);

            var r0 = heroSize * RadiusFrom;
            var rMax = heroSize * 0.5f;
            boltScale = heroSize * BoltFit / UnitBox;

            dial = Rounded(r0, 0f);
            bolt = Stage.Contour(Bolt, boltScale);
            star = Star(PointsMin, 0.5f);
            chainBase = Rounded(r0, theme.Smoothing);

            chain = Composite(new IShapeProvider[]
            {
                chainBase,
                new PrimitiveShapeProvider { Kind = ShapeKind.Circle },
                new PrimitiveShapeProvider
                {
                    Kind = ShapeKind.Star, StarPoints = 5, StarSharpness = 0.55f, Rounding = heroSize * 0.03f
                },
                new PrimitiveShapeProvider { Kind = ShapeKind.Heart },
                new PrimitiveShapeProvider { Kind = ShapeKind.Hexagon, Rounding = heroSize * 0.06f },
                Rounded(r0, 0f)
            }, out chainMorphs);

            bite = new CompositeElement
            {
                Shape = new PrimitiveShapeProvider { Kind = ShapeKind.Circle },
                Operation = CompositeOp.Union,
                Rect = new RectPlacement
                {
                    anchorMax = Vector2.one,
                    pivot = new Vector2(0.5f, 0.5f),
                    sizeDelta = Vector2.one * -heroSize,
                    anchoredPosition = BiteRest * heroSize,
                },
            };
            boolean = new CompositeShapeProvider();
            boolean.Elements.Clear();
            boolean.Elements.Add(new CompositeElement { Shape = Rounded(rMax, 1f) });
            boolean.Elements.Add(bite);

            toBolt = Composite(new IShapeProvider[] { Rounded(rMax, 1f), Stage.Contour(Bolt, boltScale) },
                out var boltMorphs);
            boltMorph = boltMorphs[0];

            dragged = (Vector2[])Bolt.Clone();
            dragged[DragKnot] = DragTo;
            toStar = Composite(new IShapeProvider[] { Stage.Contour(dragged, boltScale), Star(PointsMin, 0.5f) },
                out var starMorphs);
            starMorph = starMorphs[0];

            hero.Shape.Shape = chain;

            Stage.Ramped(hero.Fill, brand, PaintProjectionKind.Linear, LinearAngle);
            shadow = Stage.AddShadow(hero.Shape, Theme.Fade(theme.Magenta, 0f), new Vector2(0f, -ShadowDrop), 0f);
            stroke = Stage.AddStroke(hero.Shape, Color.white, 0f, 1f);
            inner = Stage.AddInnerShadow(hero.Shape, new Color(1f, 1f, 1f, 0f), Vector2.zero, 0f);
        }

        private PrimitiveShapeProvider Rounded(float radius, float smoothing) =>
            new PrimitiveShapeProvider { Kind = ShapeKind.RoundedRect, Radius = radius, Smoothing = smoothing };

        private PrimitiveShapeProvider Star(int points, float sharpness) => new PrimitiveShapeProvider
        {
            Kind = ShapeKind.Star, StarPoints = points, StarSharpness = sharpness, Rounding = heroSize * 0.02f
        };

        /// <summary>
        /// A composite laying <paramref name="outlines"/>[0] as its base and folding every later outline in as a
        /// morph at progress zero; <paramref name="morphs"/> are those elements, in order, for the choreography to
        /// drive.
        /// </summary>
        private static CompositeShapeProvider Composite(IShapeProvider[] outlines, out CompositeElement[] morphs)
        {
            var composite = new CompositeShapeProvider();
            composite.Elements.Clear();
            composite.Elements.Add(new CompositeElement { Shape = outlines[0] });

            morphs = new CompositeElement[outlines.Length - 1];
            for (var i = 0; i < morphs.Length; i++)
            {
                morphs[i] = new CompositeElement { Shape = outlines[i + 1], Operation = CompositeOp.Morph };
                composite.Elements.Add(morphs[i]);
            }

            return composite;
        }

        /// <summary>The bolt's anchors, drawn as the scene editor draws them, at the path's own coordinates.</summary>
        private void BuildKnots(Stage stage)
        {
            var holder = stage.Node("Knots", hero.Rect);
            stage.Stretch(holder);
            knotsGroup = Stage.Group(holder);

            knots = new Widget[Bolt.Length];
            for (var i = 0; i < Bolt.Length; i++)
            {
                var knot = stage.Shape("Knot" + i, holder, ShapeKind.Circle);
                stage.Box(knot.Rect, Bolt[i] * boltScale, Vector2.one * KnotSize);
                Stage.Solid(knot.Fill, Color.white);
                Stage.AddStroke(knot.Shape, theme.Accent, 3f, 1f);
                Stage.AddShadow(knot.Shape, theme.Shadow, new Vector2(0f, -3f), 8f);
                knots[i] = knot;
            }

            knotFrom = Bolt[DragKnot] * boltScale;
            knotTo = DragTo * boltScale;
            zoomFocus = Bolt[0] * boltScale + ZoomInset;
        }

        /// <summary>
        /// Every control the film shows, all built on the same spot under the shape: one is on screen at a time.
        /// </summary>
        /// <remarks>
        /// On the stage root rather than in the world, with the headlines, so the zoom leaves them where they are.
        /// </remarks>
        private void BuildControls(Stage stage)
        {
            kindPill = Pill(stage, "Kind", RectName, theme.Surface, theme.Text, out kindLabel);

            radiusPlate = SliderPlate(stage, "Radius", "Radius", out radius, heroSize * RadiusFrom, heroSize * 0.5f,
                "{0:0}");
            smoothPlate = SliderPlate(stage, "Smoothing", "Smoothing", out smooth, 0f, 1f, "{0:0.00}");

            strikePills = new Control[StrikeNames.Length];
            for (var i = 0; i < StrikeNames.Length; i++)
            {
                strikePills[i] = Pill(stage, "Strike" + i, StrikeNames[i], theme.Surface, Color.white, out _);
                Stage.Ramped(Stage.FillOf(strikePills[i].Shape), brand, PaintProjectionKind.Linear, 100f);
            }

            fillPlate = PickerPlate(stage, "Fill", "Fill", PaintNames[0], out fillPick);
            opPlate = PickerPlate(stage, "Operation", "Operation", OpNames[0], out opPick);
            shapePlate = PickerPlate(stage, "Shape", "Shape", VectorName, out _);
            codePill = Pill(stage, "Code", codeLine, Theme.Sink(theme.Surface, 0.45f), theme.Text, out _);
        }

        /// <summary>A capsule with a word in it, sized to the word, on the control spot.</summary>
        private Control Pill(Stage stage, string name, string text, Color fill, Color ink, out UniText label)
        {
            var size = frameHeight * ControlText;
            var pill = stage.Shape(name, root, ShapeKind.Capsule);
            stage.Box(pill.Rect, new Vector2(0f, controlY),
                new Vector2(theme.PadXl * 2f + Stage.Estimate(text, size), size * 2.1f));
            Stage.Solid(pill.Fill, fill);
            Stage.AddStroke(pill.Shape, theme.Line, 2f, -1f);
            Stage.AddShadow(pill.Shape, theme.Shadow, new Vector2(0f, -8f), 30f);

            label = stage.Label(pill.Rect, text, size, ink);
            label.WordWrap = false;
            label.Styles.Add(Style.WholeText(new BoldModifier()));
            return new Control(pill.Rect, pill.Shape, Stage.Group(pill.Rect));
        }

        /// <summary>A plate on the control spot with a caption on its left, the rest of it left to the caller.</summary>
        private Widget Plate(Stage stage, string name, string caption)
        {
            var plate = stage.Shape(name, root, ShapeKind.RoundedRect, theme.RadiusLg);
            stage.Box(plate.Rect, new Vector2(0f, controlY),
                new Vector2(frameWidth * PlateWidth, frameHeight * ControlHeight));
            Stage.Solid(plate.Fill, theme.Surface);
            Stage.AddStroke(plate.Shape, theme.Line, 2f, -1f);
            Stage.AddShadow(plate.Shape, theme.Shadow, new Vector2(0f, -8f), 30f);

            var label = stage.Label(plate.Rect, caption, frameHeight * ControlText, theme.TextSoft,
                HAlign.Left, VAlign.Middle, false);
            stage.Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(CaptionShare, 1f),
                new Vector2(0f, 0.5f), new Vector2(theme.PadXl, 0f), Vector2.zero);
            label.WordWrap = false;
            return plate;
        }

        private Control SliderPlate(Stage stage, string name, string caption, out Slider slider, float min,
            float max, string format)
        {
            var plate = Plate(stage, name, caption);
            var lane = stage.Node("Lane", plate.Rect);
            stage.Anchor(lane, new Vector2(CaptionShare, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            slider = stage.Slider(lane, frameHeight * ControlText, min, max, format);
            return new Control(plate.Rect, plate.Shape, Stage.Group(plate.Rect));
        }

        private Control PickerPlate(Stage stage, string name, string caption, string pick, out UniText label)
        {
            var plate = Plate(stage, name, caption);
            var size = frameHeight * ControlText;
            var chip = stage.Picker(plate.Rect, pick, size, out label);
            stage.Anchor(chip.Rect, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-theme.PadMd, 0f), new Vector2(size * PickerWidth, -theme.PadSm * 2f));
            chip.Outline.Radius = theme.Inner(theme.RadiusLg, theme.PadSm);
            return new Control(plate.Rect, plate.Shape, Stage.Group(plate.Rect));
        }

        /// <summary>Every phase time, derived from the one before it; the slide's length from the last.</summary>
        private void Schedule()
        {
            chainEnd = MorphAt + (ChainCount - 1) * MorphStep + MorphFor;
            dialAt = chainEnd + DialLead;
            radiusRun = new Vector2(dialAt + RunLead, dialAt + RunLead + RunFor);
            smoothAt = radiusRun.y + DialHold;
            smoothRun = new Vector2(smoothAt + RunLead, smoothAt + RunLead + RunFor);
            layersAt = smoothRun.y + DialHold;

            strikes = new float[StrikeNames.Length];
            for (var i = 0; i < strikes.Length; i++) strikes[i] = layersAt + StrikeLead + i * StrikeStep;
            paintAt = strikes[strikes.Length - 1] + StrikeStep + LayersHold;

            paints = new float[PaintClicks];
            for (var i = 0; i < paints.Length; i++) paints[i] = paintAt + PaintLead + i * PaintStep;
            booleanAt = paints[paints.Length - 1] + PaintStep + PaintHold;

            opAt = new float[OpSequence.Length];
            for (var i = 0; i < opAt.Length; i++) opAt[i] = booleanAt + BiteLead + i * PulseFor;
            boolEnd = opAt[opAt.Length - 1] + PulseFor;
            vectorAt = boolEnd + BoolHold;

            boltEnd = vectorAt + MorphLag + BoltFor;
            knotAt = boltEnd + KnotsLead;
            knotRun = new Vector2(knotAt + KnotLead, knotAt + KnotLead + KnotFor);
            zoomAt = knotRun.y + VectorHold;
            motionAt = zoomAt + ZoomFor + ZoomHold;
            starEnd = motionAt + MorphLag + StarFor;
            pointsAt = starEnd + PointsLead;
            finale = pointsAt + (PointsMax - PointsMin) * PointStep + MotionHold;
            appStart = finale + AppLead;
            appDone = appStart + (arrivals.Count - 1) * AppStagger + AppSettle;
            end = appDone + AppHold;

            Seconds = Mathf.Max(Seconds, end + EndFor);

            claimAt = new float[LineCount];
            claimUntil = new float[LineCount];
            Window(OpenLine, 0.6f, chainEnd + 0.1f);
            Window(DialLine, dialAt, layersAt - 0.2f);
            Window(LayersLine, layersAt, paintAt - 0.2f);
            Window(PaintLine, paintAt, booleanAt - 0.2f);
            Window(BoolLine, booleanAt, vectorAt - 0.2f);
            Window(VectorLine, vectorAt, zoomAt);
            Window(ZoomLine, zoomAt + 0.1f, motionAt - 0.1f);
            Window(MotionLine, motionAt, finale);
            Window(AppLine, appStart + 0.5f, end - 0.1f);

            slots.Clear();
            slots.Add(new Slot(kindPill, MorphAt - 0.2f, chainEnd + 0.15f));
            slots.Add(new Slot(radiusPlate, dialAt, smoothAt));
            slots.Add(new Slot(smoothPlate, smoothAt, layersAt));
            for (var i = 0; i < strikePills.Length; i++)
                slots.Add(new Slot(strikePills[i], strikes[i], i + 1 < strikes.Length ? strikes[i + 1] : paintAt));
            slots.Add(new Slot(fillPlate, paintAt, booleanAt));
            slots.Add(new Slot(opPlate, booleanAt, vectorAt));
            slots.Add(new Slot(shapePlate, vectorAt, zoomAt - 0.1f));
            slots.Add(new Slot(codePill, motionAt, finale));

            for (var i = 0; i < ChainCount; i++) Cue("morph", MorphAt + i * MorphStep);
            Cue("dial", radiusRun.x);
            Cue("dial", smoothRun.x);
            for (var i = 0; i < strikes.Length; i++) Cue("strike", strikes[i]);
            for (var i = 0; i < paints.Length; i++) Cue("paint", paints[i]);
            for (var i = 0; i < opAt.Length; i++) Cue("combine", opAt[i]);
            Cue("vector", vectorAt);
            Cue("drag", knotRun.x);
            Cue("zoom", zoomAt);
            Cue("play", motionAt);
            Cue("assemble", appStart);
            Cue("settled", appDone);
            Cue("end", end);
        }

        private void Window(int line, float at, float until)
        {
            claimAt[line] = at;
            claimUntil[line] = until;
        }

        /// <summary>
        /// <paramref name="ramp"/> folded onto itself, so a conic sweep meets its own start without a seam.
        /// </summary>
        private static Ramp Seamless(Ramp ramp)
        {
            const int count = 9;
            var stops = new Stop[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (count - 1f);
                var u = t <= 0.5f ? t * 2f : (1f - t) * 2f;
                stops[i] = new Stop(t, ramp.Evaluate(u));
            }

            return new Ramp(stops, Interp.Perceptual);
        }

        /// <summary>
        /// A neon band for the distance projection: dark at both ends so it repeats into concentric rings.
        /// </summary>
        private static Ramp Neon(Theme theme)
        {
            var dark = Theme.Sink(theme.Surface, 0.4f);
            return new Ramp(new[]
            {
                new Stop(0f, dark),
                new Stop(0.16f, theme.Magenta),
                new Stop(0.34f, theme.Orange),
                new Stop(0.5f, Color.white),
                new Stop(0.66f, theme.Orange),
                new Stop(0.84f, theme.Magenta),
                new Stop(1f, dark)
            }, Interp.Perceptual);
        }

        private const int OpenLine = 0;
        private const int DialLine = 1;
        private const int LayersLine = 2;
        private const int PaintLine = 3;
        private const int BoolLine = 4;
        private const int VectorLine = 5;
        private const int ZoomLine = 6;
        private const int MotionLine = 7;
        private const int AppLine = 8;
        private const int LineCount = 9;

        private const string RectName = "Rounded Rect";
        private const string VectorName = "Bézier";

        /// <summary>How many outlines the cold open runs through before the film settles on a rectangle.</summary>
        private static int ChainCount => ChainNames.Length;

        private static int PaintClicks => PaintNames.Length - 1;

        private const int DistancePaint = 3;

        /// <summary>Which bolt anchor is dragged.</summary>
        private const int DragKnot = 4;

        /// <summary>The bolt is authored in a box this many units tall.</summary>
        private const float UnitBox = 100f;

        private const float HeroFraction = 0.48f;
        private const float RadiusFrom = 0.12f;
        private const float BoltFit = 0.84f;
        private const float GlowSpread = 2.2f;
        private const float GlowAlpha = 0.16f;
        private const float KnotSize = 24f;
        private static readonly Vector2 ZoomInset = new Vector2(-10f, -22f);

        /// <summary>Air between the shape's bottom edge and the control under it.</summary>
        private const float ControlGap = 40f;

        private const float ControlHeight = 0.09f;
        private const float ControlText = 0.04f;
        private const float PlateWidth = 0.46f;

        /// <summary>How much of a plate its caption takes; the control has the rest. Wide enough for "Smoothing" at the control's type size.</summary>
        private const float CaptionShare = 0.34f;

        /// <summary>The circle that grows out of the hero, as a fraction of its size.</summary>
        private const float BiteSize = 0.62f;

        /// <summary>
        /// The circle at the ends of the intersect pulse, as a fraction of the hero's size: wide enough to cover the
        /// whole rectangle from the circle's off-centre seat and its drift, so the intersection is the rectangle
        /// itself and the picture is the same on both sides of the switch.
        /// </summary>
        private const float BiteHuge = 2.6f;

        /// <summary>Where that circle sits, as a fraction of the hero's size from its centre.</summary>
        private static readonly Vector2 BiteRest = new Vector2(0.42f, 0.18f);

        /// <summary>The seam fillet under a union, and under the cutting ops, as fractions of the hero's size.</summary>
        private const float FilletUnion = 0.11f;

        private const float FilletCut = 0.05f;
        private const float DriftX = 0.05f;
        private const float DriftY = 0.03f;

        /// <summary>Turns per second of the circle's drift while the ops are shown.</summary>
        private const float DriftRate = 0.3f;

        private const float PickerWidth = 7f;

        private const float Land = 0.12f;
        private const float MorphAt = 0.7f;

        /// <summary>The gap between two morphs of the cold open, wider than one morph so each lands before the next.</summary>
        private const float MorphStep = 0.55f;

        private const float MorphFor = 0.5f;
        private const float MorphLag = 0.1f;
        private const float BoltFor = 0.5f;
        private const float StarFor = 0.45f;
        private const float LinearAngle = 100f;
        private const float ShadowDrop = 22f;

        private const float DialLead = 0.2f;
        private const float RunLead = 0.35f;
        private const float RunFor = 1.0f;
        private const float DialHold = 0.3f;
        private const float StrikeLead = 0.1f;
        private const float StrikeStep = 0.6f;
        private const float LayersHold = 0.4f;
        private const float PaintLead = 0.4f;
        private const float PaintStep = 1.0f;
        private const float PaintHold = 0.2f;

        /// <summary>How long after the combine beat opens the first pulse begins.</summary>
        private const float BiteLead = 0.25f;

        /// <summary>One pulse of the circle: grown, held drifting, shrunk away — one op per pulse, switched while it is gone.</summary>
        private const float GrowFor = 0.5f;

        private const float PulseHold = 0.55f;
        private const float ShrinkFor = 0.4f;
        private static float PulseFor => GrowFor + PulseHold + ShrinkFor;
        private const float BoolHold = 0.2f;
        private const float KnotsLead = 0.15f;
        private const float KnotLead = 0.5f;
        private const float KnotFor = 0.8f;
        private const float VectorHold = 0.5f;
        private const float ZoomFor = 2.1f;
        private const float ZoomHold = 0.2f;
        private const float ZoomScale = 5f;
        private const float PointsLead = 0.3f;
        private const float PointStep = 0.14f;
        private const int PointsMin = 3;
        private const int PointsMax = 12;
        private const float MotionHold = 0.8f;

        private const float AppLead = 0.35f;
        private const float AppStagger = 0.05f;
        private const float AppSettle = 1.3f;
        private const float AppHold = 1.4f;
        private const float EndFor = 3.4f;
    }
}
