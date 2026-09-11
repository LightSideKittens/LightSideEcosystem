using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Two dozen writing systems at once, each greeting carrying an emoji, arriving one after another.
    /// </summary>
    /// <remarks>
    /// The strings are joined, bidirectional and conjunct-forming on purpose. A row of isolated codepoints proves
    /// nothing about shaping — it is what a broken engine also produces.
    /// <para>
    /// The lines carry no face. They are the shot's evidence rather than its chrome, and evidence for the system
    /// cascade has to be resolved by the system cascade.
    /// </para>
    /// <para>
    /// Every line ends in an emoji, and that is the second claim: one component, one string, a script and a colour
    /// glyph shaped together. Anything that needs a separate component for the picture has not made it.
    /// </para>
    /// <para>
    /// Each line auto-sizes inside its own cell. The strings are authored data in two dozen scripts whose widths at
    /// a common size are nothing alike, and a column laid out for the shortest of them loses the longest off its
    /// edge.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(ScriptsSlide))]
    public sealed class ScriptsSlide : Slide
    {
        [SerializeField] private string headline = "Every writing system on Earth";

        /// <summary>
        /// The greetings, filled into the columns in order: the first <c>count / columns</c> go in the first column.
        /// </summary>
        [SerializeField] private string[] lines =
        {
            "Hello 👋",
            "Привет 🌍",
            "Γειά σου ✨",
            "Merhaba ☕",
            "Olá 🎉",
            "Xin chào 🌸",

            "مرحبا 🕊️",
            "שלום 🌙",
            "سلام 📚",
            "ሰላም ☀️",
            "Բարեւ 🎈",
            "გამარჯობა 🍇",

            "नमस्ते 🙏",
            "নমস্কার 🌺",
            "வணக்கம் 🪔",
            "નમસ્તે 🌼",
            "ਸਤ ਸ੍ਰੀ ਅਕਾਲ 🌟",
            "ನಮಸ್ಕಾರ 🌻",

            "こんにちは 🍡",
            "안녕하세요 🎏",
            "你好 🧧",
            "สวัสดี 🐘",
            "ສະບາຍດີ 🌾",
            "မင်္ဂလာပါ 🛕"
        };

        [SerializeField, Min(1)] private int columns = 4;
        [SerializeField] private float step = 0.11f;

        private readonly Ease reveal = GlyphReveal.Frontier;

        private GlyphReveal[] reveals;
        private Claim claim;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline);

            var count = Mathf.Max(1, columns);
            var rows = Mathf.CeilToInt(lines.Length / (float)count);
            var gutter = stage.Height * 0.02f;
            var margin = stage.Height * 0.055f;
            var span = (stage.Width - margin * 2f - gutter * (count - 1)) / count;
            var size = stage.ContentHeight * 0.058f;

            reveals = new GlyphReveal[lines.Length];

            for (var c = 0; c < count; c++)
            {
                var panel = stage.Panel("Column" + c, stage.Root);
                stage.Box(panel.Rect,
                    new Vector2(-stage.Half.x + margin + span * 0.5f + c * (span + gutter), stage.ContentCentre),
                    new Vector2(span, stage.ContentHeight * 0.92f));

                var list = stage.Node("Rows", panel.Rect);
                stage.Stretch(list, theme.PadXl, theme.PadXl, theme.PadXl, theme.PadXl);
                stage.Column(list.gameObject, 4f, Stage.Pad(0), TextAnchor.MiddleCenter,
                    expandWidth: true, expandHeight: true);

                for (var r = 0; r < rows; r++)
                {
                    var index = c * rows + r;
                    if (index >= lines.Length) break;

                    var cell = stage.Node("Row" + r, list);
                    stage.Size(cell.gameObject, preferredHeight: size * 1.3f, flexibleHeight: 1f);

                    var line = stage.Label(cell, lines[index], size, theme.Text);
                    line.Font = null;
                    line.WordWrap = false;
                    line.MaxFontSize = size;
                    line.MinFontSize = size * Fit;
                    line.AutoSize = true;

                    reveals[index] = stage.Reveal(line, new FadeRevealHandler());
                }
            }

            Cue("settled", First + (lines.Length - 1) * step + Fade);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);

            for (var i = 0; i < reveals.Length; i++)
                reveals[i].Fill = reveal.Window(local, First + i * step, Fade);
        }

        /// <summary>When the first line starts arriving: after the line naming what they are has landed.</summary>
        private const float First = 0.85f;

        private const float Fade = 0.55f;

        /// <summary>How far below its cell's size a greeting may shrink to fit the column.</summary>
        private const float Fit = 0.55f;
    }
}
