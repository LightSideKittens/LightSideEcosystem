using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// One sentence in which every claimed effect is the real modifier that makes it: a weight, a colour, a paint
    /// and the shipped link recipe.
    /// </summary>
    /// <remarks>
    /// Build and pose are split across two files because they change for different reasons and at different times —
    /// the layout settles early and the timing never stops being tuned.
    /// <para>
    /// Assign <see cref="linkPreset"/> to <c>Defaults/ModifierGraphPresets/LinkPreset</c>. A link drawn as a
    /// coloured underline would be a picture of a link; the preset is the thing itself, hover cursor and all.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(RichTextSlide))]
    public sealed partial class RichTextSlide : Slide
    {
        [SerializeField] private ModifierGraphPreset linkPreset;
        [SerializeField, TextArea(2, 4)] private string sentence = "Ship it with bold, colour, gradients and links.";
        [SerializeField] private string boldWord = "bold";
        [SerializeField] private string colourWord = "colour";
        [SerializeField] private string gradientWord = "gradients";
        [SerializeField] private string linkWord = "links";
        [SerializeField] private string label = "Rich text input";

        private readonly Ease reveal = GlyphReveal.Frontier;
        private readonly Spring focus = Spring.Pop;

        private Showcase show;
        private RectTransform bar;
        private GlyphReveal typewriter;

        protected override void OnRender(float local)
        {
            var focusIn = Mathf.Clamp01(focus.Evaluate(local - 0.5f));
            var typing = reveal.Window(local, 0.75f, 1.6f);

            show.Pose(local);
            show.Ring.Width = Mathf.LerpUnclamped(3f, 6f, focusIn);
            typewriter.Fill = typing;
            Stage.Progress(bar, typing);
        }
    }
}
