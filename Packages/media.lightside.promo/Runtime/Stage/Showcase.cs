using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>A product panel with one titled well of live text in it: the shape most feature shots take.</summary>
    public readonly struct Showcase
    {
        internal Showcase(Widget panel, CanvasGroup group, UniText title, Widget well, UniText body, StrokeLayer ring)
        {
            Panel = panel;
            Group = group;
            Title = title;
            Well = well;
            Body = body;
            Ring = ring;
        }

        public Widget Panel { get; }

        /// <summary>Opacity of the whole panel; a graphic's own colour never reaches its children.</summary>
        public CanvasGroup Group { get; }

        /// <summary>The panel's caption, or null when it was built without one and the well took that space.</summary>
        public UniText Title { get; }

        /// <summary>The inset well, for a shot that puts more than the body text inside the panel.</summary>
        public Widget Well { get; }

        /// <summary>The live text the shot is about.</summary>
        public UniText Body { get; }

        /// <summary>The well's focus ring, for a shot that wants the panel to look focused.</summary>
        public StrokeLayer Ring { get; }

        public RectTransform Rect => Panel.Rect;

        /// <summary>Fades the panel in over its first beat, <paramref name="t"/> seconds after the shot starts.</summary>
        public void Pose(float t) => Group.alpha = Ease.EmphasizedIn.Window(t, 0f, Fade);

        private const float Fade = 0.55f;
    }

    /// <summary>Builds a <see cref="Promo.Showcase"/>.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A titled panel whose well carries <paramref name="markup"/>, centred on <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// The body carries markup, so the caller binds the tags it uses before the shot renders — a tag resolves to a
        /// modifier only where a <see cref="Style"/> binds it, and an unbound tag is shown as the literal text it is
        /// written as.
        /// <para>
        /// <paramref name="bodySize"/> is a <em>ceiling</em>, not a size. The body auto-sizes, so a line count nobody
        /// counted still lands inside the well instead of over its edge — the well's height is known when the shot is
        /// built and the text's is not, because it depends on where the words happen to wrap.
        /// </para>
        /// <para>
        /// The body carries no face. <see cref="Promo.Theme.BodyFace"/> dresses the frame's chrome, and the body is
        /// the text the shot is arguing about: a panel claiming a project needs no font assets, set in one, argues
        /// against itself. Assign <c>Body.Font</c> afterwards on a shot whose point is a particular typeface.
        /// </para>
        /// </remarks>
        public Showcase Showcase(string name, Transform parent, string title, string markup,
            Vector2 position, Vector2 size, float bodySize = 0f,
            HorizontalAlignment horizontal = HorizontalAlignment.Left,
            VerticalAlignment vertical = VerticalAlignment.Middle)
        {
            var ceiling = bodySize > 0f ? bodySize : Theme.Body;
            var panel = Panel(name, parent);
            Box(panel.Rect, position, new Vector2(size.x, Mathf.Max(size.y, ShowcaseMinHeight(ceiling))));
            var group = Group(panel.Rect);

            var titled = !string.IsNullOrEmpty(title);
            var titleBar = titled ? Theme.Body * 1.6f : 0f;
            var gap = titled ? Theme.PadXl : 0f;

            UniText head = null;
            if (titled)
            {
                head = Label(panel.Rect, title, Theme.Body, Theme.TextSoft,
                    HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
                Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -Theme.PadXl), new Vector2(-Theme.PadXl * 2f, titleBar));
            }

            var well = Field(name + " Well", panel.Rect, out var ring, Theme.Inner(Theme.RadiusXl, Theme.PadXl));
            Anchor(well.Rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -(titleBar + gap) * 0.5f),
                new Vector2(-Theme.PadXl * 2f, -(Theme.PadXl * 2f + titleBar + gap)));

            var body = Label(well.Rect, markup, ceiling, Theme.Text, horizontal, vertical);
            body.Font = null;
            Stretch(body.rectTransform, Theme.PadLg, Theme.PadLg, Theme.PadLg, Theme.PadLg);

            body.MaxFontSize = ceiling;
            body.MinFontSize = ceiling * MinFit;
            body.AutoSize = true;

            return new Showcase(panel, group, head, well, body, ring);
        }

        /// <summary>A showcase filling the content band, which is what a shot with one panel and nothing else wants.</summary>
        public Showcase Showcase(string name, Transform parent, string title, string markup, float bodySize = 0f,
            float widthFraction = 0.84f, float heightFraction = 0.92f,
            HorizontalAlignment horizontal = HorizontalAlignment.Left,
            VerticalAlignment vertical = VerticalAlignment.Middle) =>
            Showcase(name, parent, title, markup, new Vector2(0f, ContentCentre),
                new Vector2(Width * widthFraction, ContentHeight * heightFraction), bodySize, horizontal, vertical);

        /// <summary>
        /// The shortest a panel may be and still hold its title bar and one line of body at
        /// <paramref name="bodySize"/>.
        /// </summary>
        /// <remarks>
        /// A showcase spends a fixed amount of its height on chrome — two paddings, the title bar, the gap under it,
        /// the well's own inset — before a single word is placed. Ask for less than that and the well comes out
        /// shorter than one line, which no auto-size can rescue: the body bottoms out at its floor and spills anyway.
        /// <para>
        /// <see cref="Showcase(string, Transform, string, string, Vector2, Vector2, float, HorizontalAlignment, VerticalAlignment)"/> grows a panel to this rather than obeying, for the same reason
        /// <see cref="Ledger"/> sizes itself: the arithmetic belongs where the pieces are, not in every caller.
        /// Stack panels against it — a caller that wants two in a band asks for this height for the short one and
        /// gives the rest to the tall one.
        /// </para>
        /// </remarks>
        public float ShowcaseMinHeight(float bodySize) =>
            Theme.PadXl * 3f + Theme.Body * 1.6f + Theme.PadLg * 2f +
            (bodySize > 0f ? bodySize : Theme.Body) * LineRatio;

        /// <summary>
        /// Height one line occupies, as a multiple of its size.
        /// </summary>
        /// <remarks>
        /// An estimate, and unavoidably one: a panel reserves its height before it has any text to measure. Where a
        /// component already exists, <see cref="LineBox"/> asks it instead of guessing.
        /// </remarks>
        private const float LineRatio = 1.45f;

        /// <summary>
        /// How far below its ceiling a body may shrink to fit.
        /// </summary>
        /// <remarks>
        /// A floor rather than none, because past it the shot has stopped being readable from a couch and the honest
        /// answer is less text, not smaller text.
        /// </remarks>
        private const float MinFit = 0.42f;
    }
}
