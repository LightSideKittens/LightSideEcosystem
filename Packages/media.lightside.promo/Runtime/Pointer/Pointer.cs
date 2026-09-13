using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A pointer driven by a <see cref="PointerTimeline"/>: the arrow, its press deformation, the click ripple and
    /// the keystroke chip.
    /// </summary>
    /// <remarks>
    /// The arrow's tip is authored at the origin of its own contour, and <see cref="VectorShapeProvider"/> renders a
    /// contour at its authored position rather than fitting it to the rect. The tip therefore sits exactly on the
    /// rect's own origin: positioning the rect positions the tip, and scaling it pivots on the tip. There is no
    /// hotspot offset to measure and none to get wrong.
    /// <para>
    /// The ripple is centred on the recorded press point, never on the arrow. A ripple that follows the pointer
    /// after the click is the classic tell that a cursor was composited in afterwards.
    /// </para>
    /// </remarks>
    public sealed class Pointer
    {
        private const float Scale = 1.8f;

        /// <summary>
        /// The classic arrow, tip first, wound clockwise. Authored so the tip is the contour's origin.
        /// </summary>
        private static readonly Vector2[] Outline =
        {
            new Vector2(0f, 0f),
            new Vector2(0f, -26f),
            new Vector2(6.5f, -19.5f),
            new Vector2(11f, -29f),
            new Vector2(16f, -27f),
            new Vector2(11.5f, -17.5f),
            new Vector2(19f, -17.5f)
        };

        /// <summary>
        /// A pointing hand, wound clockwise from the fingertip, which is the contour's origin.
        /// </summary>
        /// <remarks>
        /// The index finger sits to the LEFT of the fist, three folded knuckles step down to the right of it, and the
        /// thumb bulges out on the left. All three are load-bearing: a single finger centred on a round fist is the
        /// obscene gesture, not a pointer, and at cursor size the silhouette is the whole of what a viewer resolves.
        /// <para>
        /// The valleys between the knuckles are cut deep on purpose. The shape carries a white rim, and a notch
        /// shallower than the rim is swallowed by it — leaving a mitten, which is where the reading goes wrong again.
        /// </para>
        /// <para>
        /// Only the pads are round. Each — the index finger's and all three folded ones — is a true semicircle,
        /// sampled at 0°, 45°, 90°, 135° and 180° of a circle whose diameter is the finger's own width, and bounded
        /// at both ends by a corner. Everything else is a stem or the fist, and rounding those is what turns a hand
        /// into a blob.
        /// </para>
        /// <para>
        /// Five anchors per pad rather than three: an arc carried by three is decided almost entirely by its two
        /// handle lengths, so any difference between them lands as a visible skew on the side that lost.
        /// </para>
        /// </remarks>
        private static readonly Vector2[] HandOutline =
        {
            new Vector2(0f, 0f),
            new Vector2(2.475f, -1.025f),
            new Vector2(3.5f, -3.5f),
            new Vector2(3.5f, -18f),
            new Vector2(4.7f, -20.2f),
            new Vector2(5.462f, -18.362f),
            new Vector2(7.3f, -17.6f),
            new Vector2(9.138f, -18.362f),
            new Vector2(9.9f, -20.2f),
            new Vector2(9.9f, -21.4f),
            new Vector2(10.662f, -19.562f),
            new Vector2(12.5f, -18.8f),
            new Vector2(14.338f, -19.562f),
            new Vector2(15.1f, -21.4f),
            new Vector2(15.1f, -22.8f),
            new Vector2(15.803f, -21.103f),
            new Vector2(17.5f, -20.4f),
            new Vector2(19.197f, -21.103f),
            new Vector2(19.9f, -22.8f),
            new Vector2(20.8f, -28f),
            new Vector2(20.8f, -35f),
            new Vector2(19.2f, -40.5f),
            new Vector2(15.5f, -44f),
            new Vector2(10f, -45.6f),
            new Vector2(3.5f, -45.4f),
            new Vector2(-2f, -43.8f),
            new Vector2(-6f, -41f),
            new Vector2(-8.6f, -36.5f),
            new Vector2(-9.4f, -31f),
            new Vector2(-8.6f, -25.6f),
            new Vector2(-6.2f, -21.6f),
            new Vector2(-3.5f, -18.6f),
            new Vector2(-3.5f, -3.5f),
            new Vector2(-2.475f, -1.025f)
        };

        /// <summary>
        /// Where each pad begins and ends, kept as corners so the rounding stops there.
        /// </summary>
        /// <remarks>
        /// A pad is round; nothing else on a hand is. Smoothing the base of a pad bleeds its curve into the stem
        /// below it and the finger swells into the fist — which is the difference between a hand and a mitten.
        /// </remarks>
        private static readonly int[] HandCorners = { 2, 3, 4, 8, 9, 13, 14, 18, 31, 32 };

        private readonly PointerTimeline timeline;
        private readonly CursorRegions cursors;
        private readonly RectTransform space;

        /// <summary>The shapes the pointer can wear, indexed by <see cref="IndexFor"/>.</summary>
        private readonly Widget[] shapes;

        private readonly Widget[] rings;
        private readonly Widget chip;
        private readonly UniText chipLabel;
        private readonly RectTransform root;

        /// <summary>
        /// Optional arrow artwork with a normalized hotspot and display height; an unset texture selects the built-in vector arrow.
        /// </summary>
        /// <remarks>
        /// <see cref="Hotspot"/> locates the tip as fractions of the image's width and height from its top-left corner.
        /// </remarks>
        public readonly struct Art
        {
            public Art(Texture2D texture, Vector2 hotspot = default, float height = 52f)
            {
                Texture = texture;
                Hotspot = hotspot;
                Height = height;
            }

            public Texture2D Texture { get; }
            public Vector2 Hotspot { get; }
            public float Height { get; }

            public bool IsSet => Texture;

            public Vector2 Size => new Vector2(
                Height * Texture.width / Mathf.Max(1, Texture.height), Height);

            /// <summary>Pivot that puts the hotspot on the rect's origin.</summary>
            public Vector2 Pivot => new Vector2(Mathf.Clamp01(Hotspot.x), 1f - Mathf.Clamp01(Hotspot.y));
        }

        internal Pointer(Stage stage, PointerTimeline timeline, Color tint, Color rippleTint, Art art)
        {
            this.timeline = timeline;
            cursors = stage.Cursors;
            space = stage.Root;
            var theme = stage.Theme;

            root = stage.Node("Pointer", stage.Root);
            stage.Stretch(root);

            rings = new[]
            {
                Ring(stage, "Ring0", rippleTint),
                Ring(stage, "Ring1", rippleTint)
            };

            chip = stage.Shape("Key", root, ShapeKind.Capsule);
            stage.Box(chip.Rect, Vector2.zero, new Vector2(160f, 68f));
            Stage.Solid(chip.Fill, theme.Ink);
            Stage.AddShadow(chip.Shape, theme.Shadow, new Vector2(0f, -6f), 18f);
            chipLabel = stage.Label(chip.Rect, string.Empty, theme.Small, Color.white);

            var arrow = stage.Shape("Arrow", root, ShapeKind.RoundedRect);
            if (art.IsSet)
            {
                Stage.Textured(arrow.Fill, art.Texture);
                stage.Anchor(arrow.Rect, Half, Half, art.Pivot, Vector2.zero, art.Size);
                Stage.AddShadow(arrow.Shape, Drop, new Vector2(0f, -6f), 14f);
            }
            else
            {
                Carve(stage, arrow, Outline, tint, TipPivot, ArrowSize);
            }

            var beam = stage.Shape("Beam", root, ShapeKind.RoundedRect);
            Carve(stage, beam, BeamOutline, tint, Half, BeamSize);

            var hand = stage.Shape("Hand", root, ShapeKind.RoundedRect);
            Carve(stage, hand, HandOutline, tint, HandPivot, HandSize, smooth: true, corners: HandCorners);

            shapes = new[] { arrow, beam, hand };
        }

        /// <summary>
        /// Fills <paramref name="widget"/> with a contour, rimmed and dropped so it survives any backdrop.
        /// </summary>
        /// <remarks>
        /// <paramref name="pivot"/> is where the contour's own origin sits inside its bounding box, which is what puts
        /// the hotspot on the rect's origin: <c>(origin - min) / size</c> on each axis.
        /// </remarks>
        private static void Carve(Stage stage, Widget widget, Vector2[] outline, Color tint,
            Vector2 pivot, Vector2 size, bool smooth = false, int[] corners = null)
        {
            widget.Shape.Shape = Stage.Contour(outline, Scale, true, smooth, corners);
            Stage.Solid(widget.Fill, tint);
            Stage.AddStroke(widget.Shape, Color.white, 5f, 1f);
            Stage.AddShadow(widget.Shape, Drop, new Vector2(0f, -6f), 14f);
            stage.Anchor(widget.Rect, Half, Half, pivot, Vector2.zero, size);
        }

        private static readonly Color Drop = new Color(0.06f, 0.06f, 0.11f, 0.35f);

        private static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        /// <summary>Top-left, so the rect's own origin coincides with the contour's authored tip.</summary>
        private static readonly Vector2 TipPivot = new Vector2(0f, 1f);

        private static readonly Vector2 ArrowSize = new Vector2(19f * Scale, 29f * Scale);

        /// <summary>
        /// The I-beam, authored symmetrically about its own origin so its hotspot is its centre — which is why it
        /// carries a centred pivot where the arrow carries a corner one.
        /// </summary>
        private static readonly Vector2[] BeamOutline =
        {
            new Vector2(-4.5f, 13f), new Vector2(4.5f, 13f), new Vector2(4.5f, 10.6f),
            new Vector2(1.6f, 10.6f), new Vector2(1.6f, -10.6f), new Vector2(4.5f, -10.6f),
            new Vector2(4.5f, -13f), new Vector2(-4.5f, -13f), new Vector2(-4.5f, -10.6f),
            new Vector2(-1.6f, -10.6f), new Vector2(-1.6f, 10.6f), new Vector2(-4.5f, 10.6f)
        };

        private static readonly Vector2 BeamSize = new Vector2(9f * Scale, 26f * Scale);

        private static readonly Vector2 HandSize = new Vector2(30.2f * Scale, 45.6f * Scale);

        /// <summary>
        /// The fingertip's place in the hand's bounding box: 9.4 of its 30.2 units from the left, at the top.
        /// </summary>
        /// <remarks>
        /// Not the centre, because the finger is not centred. Every pivot here is <c>(origin - min) / size</c> of the
        /// contour it belongs to; typing a half here would aim the hand a third of its width off the target.
        /// </remarks>
        private static readonly Vector2 HandPivot = new Vector2(9.4f / 30.2f, 1f);

        /// <summary>The compiled beats, for the spans a slide reacts in.</summary>
        public PointerTimeline Timeline => timeline;

        /// <summary>Whether the pointer is drawn at all.</summary>
        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set => root.gameObject.SetActive(value);
        }

        /// <summary>Poses the pointer, its ripple and its chip for <paramref name="seconds"/>.</summary>
        /// <remarks>
        /// The shape is decided by what the pointer is actually over, asked of the scene at its sampled position —
        /// never declared by the slide, which cannot know where a laid-out region ended up.
        /// </remarks>
        public void Pose(float seconds)
        {
            var pose = timeline.Sample(seconds);
            var press = 1f - pose.Press * (1f - PointerTiming.PressScale);
            var index = IndexFor(cursors.At(pose.Position, space));

            for (var i = 0; i < shapes.Length; i++) shapes[i].Rect.gameObject.SetActive(i == index);

            var shown = shapes[index];
            shown.Rect.anchoredPosition = pose.Position;
            Stage.Scale(shown.Rect, press);

            PoseRipple(seconds);
            PoseChip(seconds);
        }

        /// <summary>
        /// Which silhouette a cursor type wears.
        /// </summary>
        /// <remarks>
        /// Text and Link have their own; everything else falls back to the arrow, the same way a platform cursor
        /// manager falls back when a system ships no native cursor for a type.
        /// </remarks>
        private static int IndexFor(CursorType type) => type switch
        {
            CursorType.Text => 1,
            CursorType.Link => 2,
            _ => 0
        };

        private void PoseRipple(float seconds)
        {
            var press = Newest(seconds);
            for (var i = 0; i < rings.Length; i++)
            {
                var delay = i * PointerTiming.RingDelay;
                var t = press.HasValue
                    ? (seconds - press.Value.Down - delay) / PointerTiming.RingSeconds
                    : 1f;

                if (t <= 0f || t >= 1f)
                {
                    rings[i].Rect.gameObject.SetActive(false);
                    continue;
                }

                var eased = Ease.EmphasizedIn.Evaluate(t);
                var radius = eased * PointerTiming.RingRadius * (i == 0 ? 1f : PointerTiming.RingSecondScale);
                var ring = rings[i];

                ring.Rect.gameObject.SetActive(true);
                ring.Rect.anchoredPosition = press.Value.Point;
                ring.Rect.sizeDelta = new Vector2(radius * 2f, radius * 2f);
                ring.Outline.Thickness = Mathf.Clamp01((1f - eased) * 0.5f + 0.06f);
                Stage.Alpha(ring.Shape, (1f - eased) * PointerTiming.RingOpacity * (i == 0 ? 1f : 0.6f));
            }
        }

        private void PoseChip(float seconds)
        {
            var newest = -1;
            for (var i = 0; i < timeline.Keys.Count; i++)
                if (seconds >= timeline.Keys[i].At)
                    newest = i;

            if (newest < 0)
            {
                chip.Rect.gameObject.SetActive(false);
                return;
            }

            var key = timeline.Keys[newest];
            var life = seconds - key.At;
            var fade = 1f - Mathf.Clamp01((life - PointerTiming.KeyHold) / PointerTiming.PressUp);
            if (fade <= 0f)
            {
                chip.Rect.gameObject.SetActive(false);
                return;
            }

            var rise = Spring.Rise.Evaluate(life);
            chip.Rect.gameObject.SetActive(true);
            chipLabel.SetText(key.Label);
            chip.Rect.sizeDelta = new Vector2(48f + key.Label.Length * 20f, 68f);
            chip.Rect.anchoredPosition = key.Point + new Vector2(0f, 96f + rise * 22f);
            Stage.Alpha(chip.Shape, fade);
            Stage.Alpha(chipLabel, fade);
        }

        private PointerTimeline.Press? Newest(float seconds)
        {
            PointerTimeline.Press? found = null;
            for (var i = 0; i < timeline.Presses.Count; i++)
            {
                var press = timeline.Presses[i];
                if (press.Down > seconds) break;
                found = press;
            }
            return found;
        }

        private Widget Ring(Stage stage, string name, Color tint)
        {
            var ring = stage.Shape(name, root, ShapeKind.Ring);
            stage.Box(ring.Rect, Vector2.zero, Vector2.zero);
            Stage.Solid(ring.Fill, tint);
            return ring;
        }
    }
}
