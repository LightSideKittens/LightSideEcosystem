using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Two live fields: a formatted phrase is swept out of one with the pointer, copied, and pasted into the other
    /// with its formatting intact.
    /// </summary>
    /// <remarks>
    /// The content is markup, not text with styles applied over it. Ranged component styles render identically and
    /// copy as nothing: the clipboard serializes the attributed document's spans, and a ranged style is not one.
    /// Markup imported through the editable becomes those spans, and the tag bindings on both fields turn them back
    /// into modifiers on the way in.
    /// <para>
    /// Every moment comes from the pointer's own compiled timeline. The selection follows the arrow's sampled
    /// position rather than a time of its own, and the copy and the paste fire on the beats that show their
    /// keystroke chips — so the pointer cannot arrive after the thing it is supposed to have done.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(FieldCopySlide))]
    public sealed class FieldCopySlide : Slide
    {
        [SerializeField] private ModifierGraphPreset linkPreset;

        [SerializeField, TextArea(2, 5)]
        private string source = "Ship it with <b>bold</b>, <color=#F5726B>colour</color> and <link>links</link>.";

        [SerializeField] private string sweepPhrase = "bold, colour and links";
        [SerializeField] private string documentTitle = "Release notes.doc";
        [SerializeField] private string headline = "Copy from anywhere.";
        [SerializeField] private string sub = "Bold, colour and links arrive intact.";
        [SerializeField] private Texture2D[] toolbarIcons;

        private EditableField from;
        private EditableField into;
        private Pointer pointer;
        private Claim claim;

        private int sweepStart;
        private int sweepEnd;
        private Vector2 sweepFrom;
        private Vector2 sweepTo;
        private Vector2 sweepSpan;

        protected override void OnBuild(Stage stage)
        {
            if (!linkPreset)
            {
                throw new InvalidOperationException(
                    $"[Promo] '{name}' has no Link Preset. Assign Defaults/ModifierGraphPresets/LinkPreset — the " +
                    "slide claims a link survives a copy, and without the recipe there is no link to survive.");
            }

            stage.Backdrop(stage.Root);
            claim = stage.Claim(stage.Root, headline, sub);

            var column = stage.Width * 0.42f;
            var offset = (column + stage.Width * 0.03f) * 0.5f;
            var height = stage.ContentHeight * 0.72f;

            var doc = stage.DocWindow("Doc", stage.Root, documentTitle,
                new Vector2(-offset, stage.ContentCentre), new Vector2(column, height), toolbarIcons);

            from = stage.EditableField("From", doc.Page, source, stage.Theme.Body, linkPreset, onPaper: true);
            stage.Fit(from.Rect);

            into = Build(stage, "Into", offset, column, height, string.Empty);

            var visible = Visible(source);
            sweepStart = visible.IndexOf(sweepPhrase, StringComparison.Ordinal);

            if (sweepStart < 0)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' cannot find \"{sweepPhrase}\" in \"{visible}\". The Sweep Phrase is the " +
                    "run the pointer selects and copies; it has to occur in the field's own text.");

            sweepEnd = sweepStart + sweepPhrase.Length;
            sweepFrom = from.PointAfter(visible.Substring(0, sweepStart), stage.Root);
            sweepTo = from.PointAfter(visible.Substring(0, sweepEnd), stage.Root);

            pointer = stage.Pointer(new Vector2(-stage.Half.x * 0.8f, -stage.Half.y * 0.7f), new[]
            {
                Beat.To(sweepFrom, targetWidth: from.TextSize),
                Beat.Click("focusFrom"),
                Beat.Drag(sweepTo, "sweep", from.TextSize),
                Beat.Key("Ctrl + C", "copy"),
                Beat.To(into.PointAfter(string.Empty, stage.Root) + new Vector2(into.TextSize * 0.3f, 0f),
                    targetWidth: column * 0.6f),
                Beat.Click("focusInto", settles: true),
                Beat.Key("Ctrl + V", "paste"),
                Beat.To(stage.At(0.5f, 0.12f))
            });

            var timeline = pointer.Timeline;
            sweepSpan = timeline.Span("sweep");

            Script
                .Rewind(() =>
                {
                    from.Editable.Text = source;
                    into.Editable.Text = string.Empty;
                    from.Editable.Select(0, 0);
                })
                .At(timeline.Mark("focusFrom"), null, () => from.Editable.Activate(showKeyboard: false))
                .At(timeline.Mark("copy"), null, () => from.Editable.Copy())
                .At(timeline.Mark("focusInto"), null, () => into.Editable.Activate(showKeyboard: false))
                .At(timeline.Mark("paste"), null, () => into.Editable.Paste());

            Cue(timeline.Cues());
            Seconds = Mathf.Max(Seconds, timeline.Total + Tail);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            pointer.Pose(local);

            var fraction = local < sweepSpan.x
                ? 0f
                : local >= sweepSpan.y
                    ? 1f
                    : Mathf.InverseLerp(sweepFrom.x, sweepTo.x, pointer.Timeline.Sample(local).Position.x);

            from.Sweep(sweepStart, sweepEnd, fraction);
            from.PoseFocus(local - pointer.Timeline.Mark("focusFrom"));
            into.PoseFocus(local - pointer.Timeline.Mark("focusInto"));
        }

        /// <summary>
        /// <paramref name="markup"/> with its tags removed — what a reader sees, and what an index into the content
        /// means.
        /// </summary>
        /// <remarks>
        /// Derived from the authored string rather than read back from the component. The editable parses on its own
        /// schedule, and on the frame the slide is built it has not; asking it then returns an empty document, which
        /// silently resolves every offset to zero and aims the whole shot at the first character.
        /// </remarks>
        private static string Visible(string markup)
        {
            var text = new System.Text.StringBuilder(markup.Length);
            var depth = 0;

            foreach (var c in markup)
            {
                if (c == '<') depth++;
                else if (c == '>') { if (depth > 0) depth--; }
                else if (depth == 0) text.Append(c);
            }

            return text.ToString();
        }

        private EditableField Build(Stage stage, string name, float x, float width, float height, string markup)
        {
            var theme = stage.Theme;
            var panel = stage.Panel(name + " Panel", stage.Root);
            stage.Box(panel.Rect, new Vector2(x, stage.ContentCentre), new Vector2(width, height));

            var field = stage.EditableField(name, panel.Rect, markup, theme.Body, linkPreset);
            stage.Stretch(field.Rect, theme.PadXl, theme.PadXl, theme.PadXl, theme.PadXl);
            return field;
        }

        private const float Tail = 1.2f;
    }
}
