using RTLTMPro;

namespace LightSide.Promo
{
    /// <summary>A <see cref="Specimen"/> rendered by TextMeshPro with the community RTL fixer in front of it.</summary>
    /// <remarks>
    /// The typed field is load-bearing. <see cref="RTLTextMeshPro"/> declares <c>text</c> with <c>new</c> rather
    /// than <c>override</c>, so assigning through a <c>TMP_Text</c> reference reaches the base setter, skips the
    /// fixer entirely, and quietly turns this specimen back into a plain one — which would make the comparison a
    /// lie in the fixer's favor and against it at the same time.
    /// </remarks>
    public sealed class RtlTmpSpecimen : Specimen
    {
        private readonly RTLTextMeshPro text;
        private readonly TmpSpecimen inner;

        internal RtlTmpSpecimen(RTLTextMeshPro text)
        {
            this.text = text;
            inner = new TmpSpecimen(text);
        }

        public override UnityEngine.RectTransform Rect => inner.Rect;

        public override string EngineName => "TextMeshPro + RTL plugin";

        public override void SetText(string value)
        {
            text.text = value;
            text.UpdateText();
            text.ForceMeshUpdate();
        }

        public override void Reveal(float fill) => inner.Reveal(fill);

        public override void SetAlpha(float alpha) => inner.SetAlpha(alpha);
    }
}
