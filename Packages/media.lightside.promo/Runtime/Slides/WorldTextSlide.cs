using TMPro;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The same storm of damage numbers thrown into a scene by both engines, with the cost of drawing it counted.
    /// </summary>
    /// <remarks>
    /// Both columns are real world-space components — <see cref="TextMeshPro"/> on the left, <see cref="UniTextWorld"/>
    /// on the right — spawned, risen and faded by the same numbers at the same moments. The only difference the shot
    /// puts on screen is what it costs to draw them.
    /// <para>
    /// TextMeshPro gives every object its own <c>MeshRenderer</c> and does not batch across them, so the count is the
    /// number alive. UniText packs every instance sharing a material and a sorting position into one mesh, so the
    /// count is one however many are on screen.
    /// </para>
    /// <para>
    /// Word wrap is off on every number. A world text lays out inside its own rect like any other, and a figure wide
    /// enough to break would lose its tail off the bottom of a rect sized for one line.
    /// </para>
    /// <para>
    /// Only the right column is the product surface. Two dark branded panels read as two of ours, and a viewer who
    /// cannot tell which side is being sold has nothing to take from the two numbers under them. The numbers flying
    /// over the paper column are inked to match it, for the same reason its title is.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(WorldTextSlide))]
    public sealed class WorldTextSlide : Slide
    {
        [SerializeField] private string headline = "A thousand numbers, one draw call.";
        [SerializeField] private string sub = "World-space text, batched. No Canvas anywhere.";

        /// <summary>A face for the left column. Left empty, TextMeshPro falls back to its own default.</summary>
        [SerializeField] private TMP_FontAsset tmpFont;

        private Claim claim;
        private Column left;
        private Column right;

        /// <summary>One side of the split: its panel, its running total, and the numbers flying in it.</summary>
        private readonly struct Column
        {
            internal Column(Widget panel, UniText title, UniText counter, Transform stage)
            {
                Panel = panel;
                Title = title;
                Counter = counter;
                Stage = stage;
            }

            public Widget Panel { get; }
            public UniText Title { get; }

            /// <summary>The draw-call figure, rewritten every frame.</summary>
            public UniText Counter { get; }

            /// <summary>The rect the numbers are parented to.</summary>
            public Transform Stage { get; }
        }

        private UniTextWorld[] uni;
        private TextMeshPro[] tmp;
        private Vector2[] places;
        private float[] sizes;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            var width = stage.Width * 0.46f;
            var offset = (width + stage.Width * 0.02f) * 0.5f;
            var height = stage.ContentHeight * 0.96f;

            left = BuildColumn(stage, "Tmp", "TextMeshPro", theme.Coral, new Vector2(-offset, stage.ContentCentre),
                new Vector2(width, height), product: false);
            right = BuildColumn(stage, "UniText", "UniText", theme.Violet, new Vector2(offset, stage.ContentCentre),
                new Vector2(width, height), product: true);

            places = new Vector2[Count];
            sizes = new float[Count];
            uni = new UniTextWorld[Count];
            tmp = new TextMeshPro[Count];

            var band = new Vector2(width * 0.74f, height * 0.42f);
            for (var i = 0; i < Count; i++)
            {
                var crit = HashNoise.Hash01(i, 11) > 0.82f;
                var value = crit
                    ? (900 + Mathf.FloorToInt(HashNoise.Hash01(i, 13) * 1400f)).ToString()
                    : (60 + Mathf.FloorToInt(HashNoise.Hash01(i, 17) * 380f)).ToString();

                places[i] = new Vector2(
                    HashNoise.HashSigned(i, 19) * band.x * 0.5f,
                    HashNoise.HashSigned(i, 23) * band.y * 0.5f - height * Drop);
                sizes[i] = stage.ContentHeight * (crit ? 0.105f : 0.062f);

                uni[i] = BuildUni(stage, right.Stage, i, value, crit ? theme.Magenta : theme.Text);
                tmp[i] = BuildTmp(stage, left.Stage, i, value, crit ? theme.Magenta : theme.Ink);
            }

            for (var i = 0; i < Count; i++) Cue("hit", First + i * Gap);
        }

        private Column BuildColumn(Stage stage, string name, string title, Color accent,
            Vector2 position, Vector2 size, bool product)
        {
            var panel = product ? stage.Panel(name, stage.Root) : stage.Card(name, stage.Root);
            var soft = product ? stage.Theme.TextSoft : stage.Theme.InkSoft;
            stage.Box(panel.Rect, position, size);

            var head = stage.Label(panel.Rect, title, stage.Theme.Body, soft,
                HorizontalAlignment.Left, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -stage.Theme.PadXl), new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Body * 1.5f));

            var caption = stage.Label(panel.Rect, "draw calls", stage.Theme.Small, soft,
                HorizontalAlignment.Center, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(stage.Theme.PadXl + stage.Theme.Body * 1.5f + stage.Theme.Title * 1.15f)),
                new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Small * 1.5f));

            var counter = stage.Label(panel.Rect, "0", stage.Theme.Title, accent,
                HorizontalAlignment.Center, VerticalAlignment.Middle, stretch: false);
            stage.Anchor(counter.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(stage.Theme.PadXl + stage.Theme.Body * 1.5f)),
                new Vector2(-stage.Theme.PadXl * 2f, stage.Theme.Title * 1.15f));

            return new Column(panel, head, counter, panel.Rect);
        }

        /// <summary>
        /// One <see cref="UniTextWorld"/> number, pushed toward the camera so it draws in front of its panel.
        /// </summary>
        /// <remarks>
        /// Both the panel and this are in the transparent queue, where camera distance decides the order. A number on
        /// the canvas plane would be swallowed by the panel it is supposed to be flying over.
        /// </remarks>
        private UniTextWorld BuildUni(Stage stage, Transform parent, int index, string value, Color color)
        {
            var rect = Place(stage, parent, "Uni" + index, index);
            var text = rect.gameObject.AddComponent<UniTextWorld>();

            text.raycastTarget = false;
            text.WordWrap = false;
            text.FontSize = sizes[index];
            text.color = color;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Middle;
            text.Text = value;
            return text;
        }

        /// <summary>
        /// One <see cref="TextMeshPro"/> number, matched to its UniText twin.
        /// </summary>
        /// <remarks>
        /// <c>isOrthographic</c> is what makes the two columns the same size. World-space TextMeshPro scales its
        /// glyphs by a tenth unless told the camera is orthographic — it assumes a scene measured in metres, while
        /// the reel's camera measures in pixels. Left at its default the left column renders at a tenth of the
        /// right's and the shot compares nothing.
        /// </remarks>
        private TextMeshPro BuildTmp(Stage stage, Transform parent, int index, string value, Color color)
        {
            var rect = Place(stage, parent, "Tmp" + index, index);
            var text = rect.gameObject.AddComponent<TextMeshPro>();

            text.raycastTarget = false;
            text.isOrthographic = true;
            if (tmpFont) text.font = tmpFont;
            text.fontSize = sizes[index];
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = value;
            return text;
        }

        /// <summary>A rect wide enough that a figure never wraps, at the depth its index was given.</summary>
        private RectTransform Place(Stage stage, Transform parent, string name, int index)
        {
            var rect = stage.Node(name, parent);
            stage.Box(rect, Vector2.zero, new Vector2(sizes[index] * 6f, sizes[index] * 2f));
            rect.anchoredPosition3D = new Vector3(places[index].x, places[index].y, Depth - index * 0.2f);
            return rect;
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            var alive = 0;
            for (var i = 0; i < Count; i++)
            {
                var age = local - (First + i * Gap);
                var t = age / Life;
                var shown = t > 0f && t < 1f;
                if (shown) alive++;

                var opacity = shown ? Mathf.Min(Ease.EmphasizedIn.Window(t, 0f, 0.16f), 1f - t * t) : 0f;
                var rise = Rise * Ease.StandardOut.Evaluate(Mathf.Clamp01(t));
                var pop = shown ? Mathf.Lerp(0.55f, 1f, Spring.Pop.Evaluate(age)) : 1f;

                Pose(uni[i].rectTransform, i, rise, pop);
                Pose(tmp[i].rectTransform, i, rise, pop);

                Stage.Alpha(uni[i], opacity);
                tmp[i].alpha = opacity;
            }

            left.Counter.SetText(alive.ToString());
            right.Counter.SetText(alive > 0 ? "1" : "0");
        }

        private void Pose(RectTransform rect, int index, float rise, float scale)
        {
            rect.anchoredPosition3D = new Vector3(
                places[index].x, places[index].y + rise, Depth - index * 0.2f);
            Stage.Scale(rect, scale);
        }

        private const int Count = 28;

        /// <summary>When the first number lands.</summary>
        private const float First = 0.55f;

        /// <summary>Seconds between hits. <see cref="Life"/> divided by this is how many are alive at the peak.</summary>
        private const float Gap = 0.075f;

        private const float Life = 2.1f;

        /// <summary>
        /// How far a number climbs before it dies, in pixels.
        /// </summary>
        /// <remarks>
        /// Bounded together with <see cref="Drop"/> by the header above the field: a number that spawns high and rises
        /// far arrives on top of the very counter it is supposed to be driving.
        /// </remarks>
        private const float Rise = 110f;

        /// <summary>How far below the panel's centre the spawn band sits, as a fraction of the panel's height.</summary>
        private const float Drop = 0.19f;

        /// <summary>How far in front of the canvas plane the numbers fly, in world units.</summary>
        private const float Depth = -40f;
    }
}
