using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>Builds real editable fields.</summary>
    public sealed partial class Stage
    {
        /// <summary>
        /// A live editable field: the inset well, the text, the shipped selection and editing components, and the
        /// tag bindings that let pasted formatting resolve.
        /// </summary>
        /// <remarks>
        /// <see cref="UniTextEditable"/> requires <see cref="UniTextSelectable"/> and creates its own caret on first
        /// activation, so neither is assembled here. Both are <c>[ExecuteAlways]</c>, which is why a field works in
        /// the editor without entering play mode — and therefore why it can be captured by frame-stepping at all.
        /// <para>
        /// The tag styles are not optional decoration. A paste writes markup, and a tag resolves to a modifier only
        /// where a <see cref="Style"/> binds it — styles are never created on the fly. A field without these
        /// bindings accepts formatted content and renders it as flat text, which is the one outcome a film about
        /// formatting must not produce. No global preset ships assigned, so every field carries its own.
        /// </para>
        /// <para>
        /// <paramref name="markup"/> is assigned through the editable so it is <em>imported</em> as attributed
        /// document spans. Formatting applied instead as ranged component styles renders identically and copies as
        /// nothing at all: the clipboard serializes the document's spans, and ranged styles are not among them.
        /// </para>
        /// </remarks>
        public EditableField EditableField(string name, Transform parent, string markup, float textSize = 0f,
            ModifierGraphPreset linkPreset = null, bool onPaper = false, float radius = -1f)
        {
            var size = textSize > 0f ? textSize : Theme.Head;
            var well = Field(name, parent, out var ring, radius);

            if (onPaper)
            {
                Solid(FillOf(well.Shape), Color.clear);
                Solid(ring.Paint, Theme.Accent);
            }

            var text = Label(well.Rect, string.Empty, size, onPaper ? Theme.Ink : Theme.Text, HAlign.Left, VAlign.Top);
            Stretch(text.rectTransform, Theme.PadLg, Theme.PadLg, Theme.PadLg, Theme.PadLg);

            BindTags(text, linkPreset);

            var editable = text.gameObject.AddComponent<UniTextEditable>();
            editable.Text = markup;

            Cursors.Add(text.rectTransform, CursorType.Text);
            return new EditableField(well, text, editable, ring, size, Theme.PadLg);
        }

        /// <summary>
        /// Binds the markup a paste can carry to the modifiers that realize it.
        /// </summary>
        /// <remarks>
        /// Tag names are the conventional ones the clipboard adapters emit. The per-modifier parse rules that once
        /// carried them are obsolete and internal; <see cref="Style.Tag"/> is the current binding.
        /// </remarks>
        public static void BindTags(UniText text, ModifierGraphPreset linkPreset = null)
        {
            text.Styles.Add(Style.Tag(new BoldModifier(), "b"));
            text.Styles.Add(Style.Tag(new ItalicModifier(), "i"));
            text.Styles.Add(Style.Tag(new UnderlineModifier(), "u"));
            text.Styles.Add(Style.Tag(new StrikethroughModifier(), "s"));
            text.Styles.Add(Style.Tag(new ColorModifier(), "color"));

            if (linkPreset) text.Styles.Add(Style.Tag(new ModifierGraphModifier { Preset = linkPreset }, "link"));
        }
    }
}
