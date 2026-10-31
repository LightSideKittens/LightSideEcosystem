using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The build context handed to a <see cref="Slide"/>: the rect it fills, the <see cref="Promo.Theme"/> it is
    /// drawn in, and every factory that creates content.
    /// </summary>
    /// <remarks>
    /// A stage is the only place a promo GameObject is born. Slides never call <c>new GameObject</c>.
    /// <para>
    /// Coordinates are the root's own local space with the origin at its center, matching a stretched child's
    /// <c>anchoredPosition</c>, so a position computed from <see cref="Center"/> or <see cref="At"/> can be assigned
    /// straight to one.
    /// </para>
    /// </remarks>
    public sealed partial class Stage
    {
        private readonly Bands bands;

        public Stage(RectTransform root) : this(root, new Theme(), new CursorRegions(), new Bands())
        {
        }

        /// <summary>A stage that builds into <paramref name="root"/>, drawn in <paramref name="theme"/>.</summary>
        public Stage(RectTransform root, Theme theme)
            : this(root, theme ?? new Theme(), new CursorRegions(), new Bands())
        {
        }

        private Stage(RectTransform root, Theme theme, CursorRegions cursors, Bands bands)
        {
            Root = root;
            Theme = theme;
            Cursors = cursors;
            this.bands = bands;
        }

        /// <summary>How much of the frame's top and bottom edges are spoken for.</summary>
        internal sealed class Bands
        {
            public float Top;
            public float Bottom;
        }

        /// <summary>The rect new content is parented to and measured against.</summary>
        public RectTransform Root { get; }

        /// <summary>The palette and scales every factory reads. Shared with every stage derived by <see cref="For"/>.</summary>
        public Theme Theme { get; set; }

        /// <summary>The root's rect in its own local space.</summary>
        public Rect Frame => Root.rect;

        /// <summary>
        /// Width of the frame being composed for.
        /// </summary>
        /// <remarks>
        /// Every size in a slide should be derived from this and <see cref="Height"/> rather than typed, so a change
        /// of frame moves the whole composition instead of leaving half of it behind.
        /// </remarks>
        public float Width => Measured.x;

        public float Height => Measured.y;

        /// <summary>Half the frame, which is where every edge is.</summary>
        public Vector2 Half => Measured * 0.5f;

        /// <summary>
        /// The frame, refusing to be zero.
        /// </summary>
        /// <remarks>
        /// A stage whose root has no size lays every child out into nothing, and the failure surfaces much later as
        /// a composition that silently overflows or collapses. The reel pins its own rect to a fixed frame, so a
        /// zero here means the stage was built against something that is not a reel.
        /// </remarks>
        private Vector2 Measured
        {
            get
            {
                var rect = Root.rect;
                if (rect.width > 1f && rect.height > 1f) return rect.size;

                throw new InvalidOperationException(
                    $"[Promo] Stage root '{Root.name}' measures {rect.width}x{rect.height}. A slide cannot be " +
                    "composed against a frame with no size; the reel pins its own rect, so this stage was built " +
                    "against something else.");
            }
        }

        /// <summary>
        /// What each rect claims the pointer should look like over it. Shared with every stage derived by
        /// <see cref="For"/>, because a slide is one scene however many stages build it.
        /// </summary>
        public CursorRegions Cursors { get; }

        /// <summary>A stage that builds into <paramref name="root"/>, carrying this stage's theme and regions.</summary>
        public Stage For(RectTransform root) => new Stage(root, Theme, Cursors, bands);

        /// <summary>
        /// A stage for a whole frame's composition: this stage's theme and regions, and edges nothing has spoken
        /// for yet.
        /// </summary>
        /// <remarks>
        /// A reserved edge belongs to one composition. <see cref="For"/> shares it, which is what lets a headline
        /// and the content laid out beneath it agree about where the band is; carrying it into the next composition
        /// instead lays that one out against a band some other frame reserved, and every size in it comes out short
        /// by an amount nothing on screen explains.
        /// </remarks>
        public Stage ForFrame(RectTransform root) => new Stage(root, Theme, Cursors, new Bands());

        /// <summary>
        /// The band left for content once headlines have taken their edges: its center and its height.
        /// </summary>
        /// <remarks>
        /// Position content against this rather than the frame. A shot that lays itself out edge to edge and then
        /// gains a headline has no way to know the top is spoken for, and the two overlap — the same mistake as a
        /// caller guessing at a panel's height instead of asking it.
        /// </remarks>
        public float ContentCenter => (bands.Bottom - bands.Top) * 0.5f;

        public float ContentHeight => Mathf.Max(1f, Height - bands.Top - bands.Bottom);

        /// <summary>Top edge of the content band, in frame coordinates.</summary>
        public float ContentTop => Half.y - bands.Top;

        /// <summary>Bottom edge of the content band, in frame coordinates.</summary>
        public float ContentBottom => -Half.y + bands.Bottom;

        /// <summary>Reserves an edge of the frame, so content laid out afterwards keeps clear of it.</summary>
        internal void Reserve(bool top, float extent)
        {
            if (top) bands.Top = Mathf.Max(bands.Top, extent);
            else bands.Bottom = Mathf.Max(bands.Bottom, extent);
        }

        /// <summary>Anchors <paramref name="rect"/> to fill its parent exactly, with no offsets.</summary>
        public void Fit(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        /// <summary>Center of the frame.</summary>
        public Vector2 Center => Vector2.zero;

        /// <summary>
        /// A point at fractions <paramref name="fx"/> and <paramref name="fy"/> of the frame, each 0 at the left or
        /// bottom edge and 1 at the right or top.
        /// </summary>
        public Vector2 At(float fx, float fy) =>
            new Vector2((fx - 0.5f) * Width, (fy - 0.5f) * Height);

        public Vector2 Left(float inset = 0f) => new Vector2(-Width * 0.5f + inset, 0f);

        public Vector2 Right(float inset = 0f) => new Vector2(Width * 0.5f - inset, 0f);

        public Vector2 Top(float inset = 0f) => new Vector2(0f, Height * 0.5f - inset);

        public Vector2 Bottom(float inset = 0f) => new Vector2(0f, -Height * 0.5f + inset);

        public Vector2 TopLeft(float inset = 0f) =>
            new Vector2(-Width * 0.5f + inset, Height * 0.5f - inset);

        public Vector2 TopRight(float inset = 0f) =>
            new Vector2(Width * 0.5f - inset, Height * 0.5f - inset);

        public Vector2 BottomLeft(float inset = 0f) =>
            new Vector2(-Width * 0.5f + inset, -Height * 0.5f + inset);

        public Vector2 BottomRight(float inset = 0f) =>
            new Vector2(Width * 0.5f - inset, -Height * 0.5f + inset);
    }
}
