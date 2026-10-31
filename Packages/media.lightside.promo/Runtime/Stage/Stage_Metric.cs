using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>One figure the shot is built around, with the words that make it mean something.</summary>
    public readonly struct Metric
    {
        internal Metric(RectTransform rect, UniText figure, UniText caption)
        {
            Rect = rect;
            Figure = figure;
            Caption = caption;
        }

        public RectTransform Rect { get; }

        /// <summary>The number itself, painted with the brand ramp.</summary>
        public UniText Figure { get; }

        public UniText Caption { get; }

        /// <summary>Counts the figure up and fades the caption in behind it.</summary>
        public void Pose(float t)
        {
            var k = Ease.EmphasizedIn.Window(t, 0f, Fade);
            Stage.Alpha(Figure, k);
            Stage.Scale(Rect, Mathf.LerpUnclamped(0.86f, 1f, k));
            if (Caption) Stage.Alpha(Caption, Ease.EmphasizedIn.Window(t, Gap, Fade));
        }

        private const float Fade = 0.5f;
        private const float Gap = 0.22f;
    }

    /// <summary>Builds the single figure a quantitative shot rests on.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A large figure with a caption beneath it, centered on <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// The figure is painted with the brand ramp and the caption is not, so the eye lands on the number first and
        /// reads the words second — which is the order the claim is written in.
        /// </remarks>
        public Metric Metric(string name, Transform parent, string figure, string caption,
            Vector2 position, float width, float figureSize = 0f)
        {
            var size = figureSize > 0f ? figureSize : Theme.Title;
            var height = size * 1.35f + Theme.Lead * 1.6f;

            var rect = Node(name, parent);
            Box(rect, position, new Vector2(width, height));

            var number = Label(rect, figure, size, Theme.Text, stretch: false, face: Theme.DisplayFace);
            Anchor(number.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, size * 1.35f));
            number.WordWrap = false;
            number.Styles.Add(Style.WholeText(BrandFill(15f)));

            UniText words = null;
            if (!string.IsNullOrEmpty(caption))
            {
                words = Label(rect, caption, Theme.Lead, Theme.TextSoft, stretch: false);
                Anchor(words.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                    Vector2.zero, new Vector2(0f, Theme.Lead * 1.6f));
                words.WordWrap = false;
            }

            return new Metric(rect, number, words);
        }
    }
}
