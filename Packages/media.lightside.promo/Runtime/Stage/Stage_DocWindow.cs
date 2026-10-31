using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>A document editor's window: title, formatting toolbar, and a white page to write on.</summary>
    public readonly struct DocWindow
    {
        internal DocWindow(Widget surface, UniText title, RectTransform page)
        {
            Surface = surface;
            Title = title;
            Page = page;
        }

        public Widget Surface { get; }
        public UniText Title { get; }

        /// <summary>The white page. Content goes here.</summary>
        public RectTransform Page { get; }

        public RectTransform Rect => Surface.Rect;
    }

    /// <summary>Builds the window formatted text is copied out of.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A generic word-processor window: paper surface, a document title, a row of formatting controls, and a
        /// page.
        /// </summary>
        /// <remarks>
        /// Deliberately generic. It has to read instantly as "somebody's document editor", because the point of the
        /// shot is that formatted text arrives from outside — but it carries no real product's name, mark or
        /// palette. Reproducing another company's interface inside marketing material is a different kind of
        /// problem from a design one, and the shot does not need it to work.
        /// </remarks>
        public DocWindow DocWindow(string name, Transform parent, string title, Vector2 position, Vector2 size,
            Texture2D[] icons = null)
        {
            var surface = Card(name, parent, Theme.RadiusLg);
            Box(surface.Rect, position, size);

            var bar = Theme.Small * 2.2f;
            var tools = Theme.Small * 2.4f;

            var caption = Label(surface.Rect, title, Theme.Small, Theme.InkSoft, HAlign.Left, VAlign.Middle, false);
            Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -Theme.PadLg), new Vector2(-Theme.PadLg * 2f, bar));

            var strip = Node("Toolbar", surface.Rect);
            Anchor(strip, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(Theme.PadLg + bar)), new Vector2(-Theme.PadLg * 2f, tools));
            Row(strip.gameObject, Theme.PadSm, Pad(0), TextAnchor.MiddleLeft);

            if (icons != null && icons.Length > 0)
                for (var i = 0; i < icons.Length; i++) Tool(strip, icons[i], tools);
            else
                foreach (var glyph in FallbackGlyphs) Tool(strip, glyph, tools);

            var page = Node("Page", surface.Rect);
            Anchor(page, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -(Theme.PadLg + bar + tools) * 0.5f),
                new Vector2(-Theme.PadLg * 2f, -(Theme.PadLg * 2f + bar + tools)));

            var paper = Shape("Paper", page, ShapeKind.RoundedRect, Theme.RadiusSm);
            Fit(paper.Rect);
            Solid(paper.Fill, Color.white);
            AddInnerShadow(paper.Shape, new Color(0f, 0f, 0f, 0.08f), new Vector2(0f, 2f), 6f);

            return new DocWindow(surface, caption, page);
        }

        /// <summary>
        /// One toolbar button carrying <paramref name="icon"/>.
        /// </summary>
        /// <remarks>
        /// The glyph is tinted rather than drawn as authored: these are interface icons, drawn light for a dark
        /// editor skin, and they would vanish on paper untouched.
        /// </remarks>
        private void Tool(RectTransform strip, Texture2D icon, float size)
        {
            var button = ToolButton(strip, size);
            var glyph = Shape("Icon", button.Rect, ShapeKind.RoundedRect);
            Box(glyph.Rect, CenterAnchor, CenterAnchor, Vector2.zero, Vector2.one * (size * 0.56f));
            Textured(glyph.Fill, icon);
            glyph.Fill.Color = Theme.InkSoft;
        }

        private void Tool(RectTransform strip, string glyph, float size)
        {
            var button = ToolButton(strip, size);
            var label = Label(button.Rect, glyph, Theme.Small * 0.85f, Theme.InkSoft);
            label.HorizontalAlignment = HAlign.Center;
            label.VerticalAlignment = VAlign.Middle;
        }

        private Widget ToolButton(RectTransform strip, float size)
        {
            var button = Shape("Tool", strip, ShapeKind.RoundedRect, Theme.RadiusXs);
            Size(button.GameObject, size, size);
            Solid(button.Fill, Theme.PaperDim);
            return button;
        }

        private static readonly string[] FallbackGlyphs = { "B", "I", "U", "S", "A" };
    }
}
