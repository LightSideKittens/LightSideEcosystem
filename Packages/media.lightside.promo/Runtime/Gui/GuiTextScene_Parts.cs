using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    /// <summary>The three beats of <see cref="GuiTextScene"/>, each built and posed on its own.</summary>
    public sealed partial class GuiTextScene
    {
        /// <summary>Every language: a reward card whose every text switches script from a tab, mirroring for Arabic.</summary>
        private Part BuildLanguages(Stage stage)
        {
            var size = new Vector2(700f, 320f);
            var at = new Vector2(0f, contentCenter + contentHeight * 0.1f);
            var cell = new Vector2(150f, 70f);
            var switchAt = new Vector2(0f, at.y - size.y * 0.5f - contentHeight * 0.15f);
            var part = new LanguagePart(stage, new Vector2(cell.x * 2.4f, switchAt.y - PointerDrop), cell, switchAt);
            part.Build(this, stage, at, size);
            return part;
        }

        /// <summary>
        /// A reward card in four languages. Each tab click throws the card's texts out and brings the next language's
        /// in; for Arabic the card mirrors — the badge, the toggle and the button cross to the other side and the texts
        /// align right — and mirrors back when the next tab is clicked.
        /// </summary>
        private sealed class LanguagePart : DrawnPart
        {
            private static readonly string[] Tabs = { "EN", "عربي", "हिन्दी", "日本語" };

            /// <summary>The card's texts in each tab's language: title, body, button and toggle.</summary>
            private static readonly string[][] Texts =
            {
                new[] { "Daily Reward", "Come back tomorrow for a bigger chest.", "Claim  →", "Notify me" },
                new[] { "المكافأة اليومية", "عد غدًا للحصول على صندوق أكبر.", "استلم  ←", "نبّهني" },
                new[] { "दैनिक इनाम", "बड़े संदूक के लिए कल फिर आएं।", "लें  →", "याद दिलाएं" },
                new[] { "デイリー報酬", "明日はもっと大きな宝箱が待っています。", "受け取る  →", "通知する" }
            };

            /// <summary>The tab whose language is written right to left.</summary>
            private const int RightToLeft = 1;

            private readonly float[] clicks = new float[Tabs.Length - 1];
            private readonly float[] weights = new float[Tabs.Length];
            private readonly Vector2 cell;
            private readonly Vector2 switchAt;
            private Switch options;
            private UniText[] tabs;
            private GlyphReveal[][] reveals;
            private RectTransform badge;
            private RectTransform button;
            private RectTransform toggle;
            private RectTransform knob;
            private Vector2 badgeAt;
            private Vector2 buttonAt;
            private Vector2 toggleAt;
            private float axis;

            public LanguagePart(Stage stage, Vector2 start, Vector2 cell, Vector2 switchAt)
                : base(stage, "Languages", start, Script(cell, switchAt), SwitchPace)
            {
                this.cell = cell;
                this.switchAt = switchAt;
            }

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat>();
                for (var i = 1; i < Tabs.Length; i++)
                {
                    beats.Add(Beat.To(at + new Vector2((i - (Tabs.Length - 1) * 0.5f) * cell.x, 0f), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("tab" + i, true));
                    beats.Add(Beat.Wait(LanguageRead));
                }

                return beats.ToArray();
            }

            public void Build(GuiTextScene scene, Stage stage, Vector2 at, Vector2 size)
            {
                var theme = scene.theme;
                axis = at.x;
                scene.Panel(stage, Root, "Card", at, size, 30f);

                const float inset = 40f;
                badgeAt = new Vector2(at.x - size.x * 0.5f + inset + BadgeSize * 0.5f, at.y + 46f);
                var badgeShape = stage.Shape("Badge", Root, ShapeKind.Circle);
                badge = badgeShape.Rect;
                stage.Box(badge, badgeAt, Vector2.one * BadgeSize);
                Stage.Ramped(badgeShape.Fill, scene.brand, PaintProjectionKind.Linear, 35f);
                var star = stage.Shape("Star", badge, ShapeKind.Star);
                ((PrimitiveShapeProvider)star.Shape.Shape).StarSharpness = 0.45f;
                stage.Box(star.Rect, Vector2.zero, Vector2.one * BadgeSize * 0.5f);
                Stage.Solid(star.Fill, Color.white);

                buttonAt = new Vector2(at.x + size.x * 0.5f - inset - ButtonSize.x * 0.5f, at.y - size.y * 0.5f + inset + ButtonSize.y * 0.5f);
                var buttonShape = stage.Shape("Button", Root, ShapeKind.Capsule);
                button = buttonShape.Rect;
                stage.Box(button, buttonAt, ButtonSize);
                Stage.Ramped(buttonShape.Fill, scene.hot, PaintProjectionKind.Linear, 20f);

                toggleAt = new Vector2(at.x - size.x * 0.5f + inset + ToggleSize.x * 0.5f, buttonAt.y);
                var toggleShape = stage.Shape("Toggle", Root, ShapeKind.Capsule);
                toggle = toggleShape.Rect;
                stage.Box(toggle, toggleAt, ToggleSize);
                Stage.Ramped(toggleShape.Fill, scene.brand, PaintProjectionKind.Linear, 0f);
                var knobShape = stage.Shape("Knob", toggle, ShapeKind.Circle);
                knob = knobShape.Rect;
                stage.Box(knob, new Vector2(KnobTravel, 0f), Vector2.one * (ToggleSize.y - 8f));
                Stage.Solid(knobShape.Fill, Color.white);

                var textLeft = badgeAt.x + BadgeSize * 0.5f + 28f;
                var textWidth = at.x + size.x * 0.5f - inset - textLeft;
                var toggleLeft = toggleAt.x + ToggleSize.x * 0.5f + 18f;
                reveals = new GlyphReveal[Texts.Length][];
                for (var language = 0; language < Texts.Length; language++)
                {
                    var rtl = language == RightToLeft;
                    var align = rtl ? HAlign.Right : HAlign.Left;
                    var edge = rtl ? 1f : 0f;
                    var texts = Texts[language];
                    var title = scene.Text(stage, Root, texts[0], 40f, theme.Text, align,
                        new Vector2(Side(textLeft, rtl), at.y + 80f), new Vector2(textWidth, 56f), edge, theme.DisplayFace);
                    var body = scene.Text(stage, Root, texts[1], 25f, theme.TextSoft, align,
                        new Vector2(Side(textLeft, rtl), at.y + 14f), new Vector2(textWidth, 76f), edge);
                    body.WordWrap = true;
                    var claim = scene.Text(stage, button, texts[2], 26f, Color.white, HAlign.Center, Vector2.zero, ButtonSize, 0.5f,
                        theme.DisplayFace);
                    var notify = scene.Text(stage, Root, texts[3], 24f, theme.Text, align,
                        new Vector2(Side(toggleLeft, rtl), toggleAt.y), new Vector2(240f, 40f), edge);
                    var row = new[] { title, body, claim, notify };
                    reveals[language] = new GlyphReveal[row.Length];
                    for (var i = 0; i < row.Length; i++)
                        reveals[language][i] = stage.Reveal(row[i], 0, texts[i].Length,
                            new SlideRevealHandler { Offset = new Vector2(0f, -TextRise) }, TextSpread);
                }

                options = new Switch(stage, Root, theme, switchAt, cell, Tabs.Length);
                tabs = new UniText[Tabs.Length];
                for (var i = 0; i < Tabs.Length; i++)
                {
                    tabs[i] = stage.Label(options.Rect, Tabs[i], 28f, theme.TextSoft, stretch: false, face: theme.DisplayFace);
                    stage.Box(tabs[i].rectTransform, options[i], cell);
                }
            }

            /// <summary><paramref name="x"/>, or its mirror across the card's middle for a right-to-left language.</summary>
            private float Side(float x, bool rtl) => rtl ? 2f * axis - x : x;

            private Vector2 Mirror(Vector2 point, float amount) =>
                new Vector2(Mathf.LerpUnclamped(point.x, 2f * axis - point.x, amount), point.y);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("tab" + (i + 1));
                options.Pose(played, clicks, weights);
                for (var i = 0; i < tabs.Length; i++)
                {
                    tabs[i].color = Color.LerpUnclamped(SoftText, Color.white, weights[i]);
                    tabs[i].rectTransform.localScale = Vector3.one * (i == 0 ? 1f : Kick(played, clicks[i - 1]));
                }

                for (var language = 0; language < reveals.Length; language++)
                for (var i = 0; i < reveals[language].Length; i++)
                {
                    var came = language == 0
                        ? 1f
                        : GlyphReveal.Frontier.Window(played, clicks[language - 1] + TextLag + i * RowStep, TextIn);
                    var gone = language + 1 < reveals.Length
                        ? GlyphReveal.Frontier.Window(played, clicks[language], TextOut)
                        : 0f;
                    reveals[language][i].Fill = Mathf.Min(came, 1f - gone);
                }

                var mirror = Motion.Snap.Window(played, clicks[RightToLeft - 1], MirrorFor) -
                             Motion.Snap.Window(played, clicks[RightToLeft], MirrorFor);
                badge.anchoredPosition = Mirror(badgeAt, mirror);
                button.anchoredPosition = Mirror(buttonAt, mirror);
                toggle.anchoredPosition = Mirror(toggleAt, mirror);
                knob.anchoredPosition = new Vector2(Mathf.LerpUnclamped(KnobTravel, -KnobTravel, mirror), 0f);
            }

            private static readonly Color SoftText = new Color(0.78f, 0.7f, 0.63f, 1f);
        }

        /// <summary>Every emoji: a chat whose message is finished from an emoji bar and sent.</summary>
        private Part BuildChat(Stage stage)
        {
            var size = new Vector2(640f, 380f);
            var at = new Vector2(0f, contentCenter + contentHeight * 0.12f);
            var cell = new Vector2(96f, 84f);
            var barAt = new Vector2(0f, at.y - size.y * 0.5f - contentHeight * 0.13f);
            var part = new ChatPart(stage, cell, barAt, at, size);
            part.Build(this, stage);
            return part;
        }

        /// <summary>
        /// A guild chat with a message half typed. Each click on the emoji bar appends that emoji to the message; the
        /// send button then flies the message up into the conversation.
        /// </summary>
        private sealed class ChatPart : DrawnPart
        {
            private static readonly string[] Emoji = { "🎉", "🔥", "😂", "❤️", "👍" };
            private const string Typed = "GG everyone ";

            /// <summary>How many emoji, from the bar's start, the message gets before it is sent.</summary>
            private const int Picked = 3;

            private readonly float[] clicks = new float[Picked];
            private readonly Vector2 cell;
            private readonly Vector2 barAt;
            private readonly Vector2 at;
            private readonly Vector2 size;
            private UniText[] emoji;
            private UniText typed;
            private UniText placeholder;
            private RectTransform caret;
            private RectTransform sent;
            private CanvasGroup sentGroup;
            private Vector2 sentAt;
            private Vector2 inputAt;
            private float textLeft;
            private int shown = -1;

            public ChatPart(Stage stage, Vector2 cell, Vector2 barAt, Vector2 at, Vector2 size)
                : base(stage, "Chat", new Vector2(cell.x * 2.6f, barAt.y - PointerDrop), Script(cell, barAt, at, size))
            {
                this.cell = cell;
                this.barAt = barAt;
                this.at = at;
                this.size = size;
            }

            private static Vector2 Slot(Vector2 barAt, Vector2 cell, int index) =>
                barAt + new Vector2((index - (Emoji.Length - 1) * 0.5f) * cell.x, 0f);

            private static Vector2 SendAt(Vector2 at, Vector2 size) =>
                new Vector2(at.x + size.x * 0.5f - Inset - SendSize * 0.5f, at.y - size.y * 0.5f + Inset + SendSize * 0.5f);

            private static Beat[] Script(Vector2 cell, Vector2 barAt, Vector2 at, Vector2 size)
            {
                var beats = new List<Beat>();
                for (var i = 0; i < Picked; i++)
                {
                    beats.Add(Beat.To(Slot(barAt, cell, i), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("emoji" + i));
                }

                beats.Add(Beat.To(SendAt(at, size), null, SendSize));
                beats.Add(Beat.Click("send", true, true));
                beats.Add(Beat.Wait(0.5f));
                return beats.ToArray();
            }

            public void Build(GuiTextScene scene, Stage stage)
            {
                var theme = scene.theme;
                scene.Panel(stage, Root, "Chat", at, size, 30f);
                var left = at.x - size.x * 0.5f + Inset;
                var right = at.x + size.x * 0.5f - Inset;
                var top = at.y + size.y * 0.5f;

                scene.Text(stage, Root, "Guild chat", 24f, theme.TextSoft, HAlign.Left, new Vector2(left, top - 36f),
                    new Vector2(300f, 36f), 0f, theme.DisplayFace);

                var avatar = stage.Shape("Avatar", Root, ShapeKind.Circle);
                stage.Box(avatar.Rect, new Vector2(left + 23f, top - 100f), Vector2.one * 46f);
                Stage.Solid(avatar.Fill, theme.Coral);
                scene.Text(stage, avatar.Rect, "K", 24f, Color.white, HAlign.Center, Vector2.zero, Vector2.one * 46f, 0.5f,
                    theme.DisplayFace);
                Bubble(scene, stage, Root, "Raid at nine? ⚔️", new Vector2(left + 58f, top - 100f), 0f, false);
                Bubble(scene, stage, Root, "Count me in 🔥", new Vector2(right, top - 166f), 1f, true);

                sentAt = new Vector2(right, top - 232f);
                sent = stage.Stretch(stage.Node("Sent", Root));
                sentGroup = Stage.Group(sent);
                var message = Typed;
                for (var i = 0; i < Picked; i++) message += Emoji[i];
                Bubble(scene, stage, sent, message, sentAt, 1f, true);

                var sendAt = SendAt(at, size);
                inputAt = new Vector2((left + sendAt.x - SendSize * 0.5f - 12f) * 0.5f, sendAt.y);
                var input = stage.Shape("Input", Root, ShapeKind.Capsule);
                stage.Box(input.Rect, inputAt, new Vector2(sendAt.x - SendSize * 0.5f - 12f - left, SendSize));
                Stage.Solid(input.Fill, Theme.Sink(theme.Surface, 0.35f));
                Stage.AddStroke(input.Shape, theme.Line, 1.5f, -1f);
                textLeft = left + 22f;
                typed = scene.Text(stage, Root, Typed, 25f, theme.Text, HAlign.Left, new Vector2(textLeft, inputAt.y),
                    new Vector2(420f, 40f), 0f);
                placeholder = scene.Text(stage, Root, "Message", 25f, theme.TextSoft, HAlign.Left,
                    new Vector2(textLeft, inputAt.y), new Vector2(420f, 40f), 0f);
                var caretShape = stage.Shape("Caret", Root, ShapeKind.RoundedRect, 1.5f);
                caret = caretShape.Rect;
                stage.Box(caret, new Vector2(textLeft, inputAt.y), new Vector2(3f, 30f));
                Stage.Solid(caretShape.Fill, theme.Orange);

                var send = stage.Shape("Send", Root, ShapeKind.Circle);
                stage.Box(send.Rect, sendAt, Vector2.one * SendSize);
                Stage.Ramped(send.Fill, scene.hot, PaintProjectionKind.Linear, 35f);
                scene.Text(stage, send.Rect, "➤", 26f, Color.white, HAlign.Center, new Vector2(2f, 0f), Vector2.one * SendSize, 0.5f);

                var bar = stage.Shape("Emoji Bar", Root, ShapeKind.Capsule);
                stage.Box(bar.Rect, barAt, new Vector2(cell.x * Emoji.Length + 12f, cell.y + 12f));
                Stage.Solid(bar.Fill, Theme.Sink(theme.Surface, 0.3f));
                Stage.AddStroke(bar.Shape, Theme.Fade(theme.Line, 0.6f), 1.5f, -1f);
                emoji = new UniText[Emoji.Length];
                for (var i = 0; i < Emoji.Length; i++)
                    emoji[i] = scene.Text(stage, Root, Emoji[i], 44f, Color.white, HAlign.Center, Slot(barAt, cell, i), cell, 0.5f);
            }

            /// <summary>A message bubble hung by its <paramref name="edge"/> at <paramref name="anchor"/>, sized to its text.</summary>
            private static void Bubble(GuiTextScene scene, Stage stage, Transform parent, string text, Vector2 anchor, float edge,
                bool own)
            {
                var theme = scene.theme;
                var label = scene.Text(stage, parent, text, 25f, own ? Color.white : theme.Text, HAlign.Center, anchor,
                    new Vector2(600f, 40f), edge);
                var width = Measure(label) + BubblePad * 2f;
                var center = new Vector2(anchor.x + (0.5f - edge) * width, anchor.y);
                var shape = stage.Shape(own ? "Own" : "Theirs", parent, ShapeKind.RoundedRect, 22f);
                stage.Box(shape.Rect, center, new Vector2(width, 50f));
                if (own) Stage.Ramped(shape.Fill, scene.hot, PaintProjectionKind.Linear, 20f);
                else Stage.Solid(shape.Fill, Theme.Mix(theme.Surface, theme.Line, 0.7f));
                shape.Rect.SetSiblingIndex(label.rectTransform.GetSiblingIndex());
                var rect = label.rectTransform;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = center;
                rect.sizeDelta = new Vector2(width, 50f);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < Picked; i++) clicks[i] = ClickAt("emoji" + i);
                var sendClick = ClickAt("send");

                var count = 0;
                for (var i = 0; i < Picked; i++)
                    if (played >= clicks[i]) count++;
                var isSent = played >= sendClick;
                var state = isSent ? -2 : count;
                if (state != shown)
                {
                    var text = new StringBuilder(Typed);
                    for (var i = 0; i < count; i++) text.Append(Emoji[i]);
                    typed.Text = text.ToString();
                    shown = state;
                }

                typed.gameObject.SetActive(!isSent);
                placeholder.gameObject.SetActive(isSent);
                var blink = Mathf.Repeat(played, CaretBlink * 2f) < CaretBlink;
                caret.gameObject.SetActive(!isSent && blink);
                caret.anchoredPosition = new Vector2(textLeft + Measure(typed) + 3f, inputAt.y);

                for (var i = 0; i < emoji.Length; i++)
                    emoji[i].rectTransform.localScale = Vector3.one * (i < Picked ? Kick(played, clicks[i]) : 1f);

                var flight = Motion.Snap.Window(played, sendClick, SendFlight);
                sent.gameObject.SetActive(isSent);
                sentGroup.alpha = Mathf.Clamp01(flight * 3f);
                sent.anchoredPosition = new Vector2(0f, (inputAt.y - sentAt.y) * (1f - flight));
            }
        }

        /// <summary>Every style: one word dressed by style chips, each landing on the word with a blow.</summary>
        private Part BuildStyles(Stage stage)
        {
            var at = new Vector2(0f, contentCenter + contentHeight * 0.14f);
            var cell = new Vector2(210f, 66f);
            var chipsAt = new Vector2(0f, at.y - contentHeight * 0.38f);
            var part = new StylePart(stage, cell, chipsAt);
            part.Build(this, stage, at);
            return part;
        }

        /// <summary>
        /// A headline word and a row of style chips. Each chip clicked turns on and lands its style on the word — the
        /// brand gradient, an outline, a drop shadow, a glow — each kept as the next arrives.
        /// </summary>
        /// <remarks>
        /// The gradient runs through the word a letter at a time, one style per letter switched on in turn, each fill
        /// mapped across the whole line so the letters join into one gradient. A letter has either the text's own default fill or the gradient, never
        /// both: two fills stacked on one glyph composite their antialiased edges twice, and the lower one shows
        /// through as a fringe around every letter. The shadow and the glow fade in with their own strike, so before
        /// they land they leave nothing under the word.
        /// <para>
        /// The shadow is a hard, deep-violet copy dropped down and right, not a soft black one: black on the reel's
        /// near-black backdrop does not read. The word lifts as it lands, so the drop reads as height.
        /// </para>
        /// </remarks>
        private sealed class StylePart : DrawnPart
        {
            private static readonly string[] Chips = { "Gradient", "Outline", "Shadow", "Glow" };

            private readonly float[] clicks = new float[Chips.Length];
            private readonly Vector2 cell;
            private readonly Vector2 chipsAt;
            private Style[] letters;
            private Color shadowColor;
            private Color glowColor;
            private RectTransform wordRect;
            private Vector2 wordAt;
            private StrokeModifier stroke;
            private ShadowModifier shadow;
            private GlowModifier glow;
            private UniShape[] lit;
            private RectTransform[] chips;

            public StylePart(Stage stage, Vector2 cell, Vector2 chipsAt)
                : base(stage, "Styles", new Vector2(cell.x * 2.4f, chipsAt.y - PointerDrop), Script(cell, chipsAt), SwitchPace)
            {
                this.cell = cell;
                this.chipsAt = chipsAt;
            }

            private static Vector2 Chip(Vector2 cell, Vector2 at, int index) =>
                at + new Vector2((index - (Chips.Length - 1) * 0.5f) * (cell.x + ChipGap), 0f);

            private static Beat[] Script(Vector2 cell, Vector2 at)
            {
                var beats = new List<Beat>();
                for (var i = 0; i < Chips.Length; i++)
                {
                    beats.Add(Beat.To(Chip(cell, at, i), null, cell.x * 0.6f));
                    beats.Add(Beat.Click("chip" + i, true));
                    beats.Add(Beat.Wait(StyleRead));
                }

                return beats.ToArray();
            }

            public void Build(GuiTextScene scene, Stage stage, Vector2 at)
            {
                var theme = scene.theme;
                var word = scene.Text(stage, Root, Word, 190f, Color.white, HAlign.Center, at, new Vector2(1500f, 260f), 0.5f,
                    theme.DisplayFace);
                word.WordWrap = false;
                wordRect = word.rectTransform;
                wordAt = wordRect.anchoredPosition;
                glowColor = theme.Magenta;
                shadowColor = Theme.Sink(theme.Violet, 0.35f);
                glow = new GlowModifier { Paint = PaintRef.Solid(Theme.Fade(glowColor, 0f)), Radius = UnitValue.Em(0f) };
                shadow = new ShadowModifier
                {
                    Paint = PaintRef.Solid(Theme.Fade(shadowColor, 0f)),
                    Offset = new UnitVector2(Vector2.zero, UnitKind.Em),
                    Blur = UnitValue.Em(0f),
                    Spread = UnitValue.Em(0f)
                };
                stroke = new StrokeModifier { Paint = PaintRef.Solid(Color.white), Width = UnitValue.Em(0f), Align = 1f };
                const string swatch = "victory.brand";
                word.Styles.Add(Style.WholeText(glow));
                word.Styles.Add(Style.WholeText(shadow));
                word.Styles.Add(Style.WholeText(stroke));
                letters = new Style[Word.Length];
                for (var i = 0; i < letters.Length; i++)
                {
                    var fill = new FillModifier
                    {
                        Provider = stage.BrandCatalog(swatch, 100f, PaintMapping.Line),
                        Paint = PaintRef.Named(swatch)
                    };
                    letters[i] = Style.Range(fill, i, i + 1);
                    letters[i].Enabled = false;
                    word.Styles.Add(letters[i]);
                }

                lit = new UniShape[Chips.Length];
                chips = new RectTransform[Chips.Length];
                for (var i = 0; i < Chips.Length; i++)
                {
                    var node = stage.Node("Chip", Root);
                    stage.Box(node, Chip(cell, chipsAt, i), cell);
                    chips[i] = node;
                    var plate = stage.Shape("Plate", node, ShapeKind.Capsule);
                    stage.Stretch(plate.Rect);
                    Stage.Solid(plate.Fill, Theme.Sink(theme.Surface, 0.3f));
                    Stage.AddStroke(plate.Shape, theme.Line, 1.5f, -1f);
                    var on = stage.Shape("Lit", node, ShapeKind.Capsule);
                    stage.Stretch(on.Rect);
                    Stage.Ramped(on.Fill, scene.hot, PaintProjectionKind.Linear, 20f);
                    lit[i] = on.Shape;
                    var label = stage.Label(node, Chips[i], 26f, Color.white, face: theme.DisplayFace);
                    label.WordWrap = false;
                }
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                for (var i = 0; i < clicks.Length; i++) clicks[i] = ClickAt("chip" + i);

                for (var i = 0; i < letters.Length; i++)
                {
                    var on = played >= clicks[0] + i * LetterStep;
                    if (letters[i].Enabled != on) letters[i].Enabled = on;
                }
                stroke.Width = UnitValue.Em(OutlineWidth * Strike(played, 1));
                var dropped = Strike(played, 2);
                shadow.Offset = new UnitVector2(ShadowDrop * dropped, UnitKind.Em);
                wordRect.anchoredPosition = wordAt + new Vector2(0f, WordLift * dropped);
                var punch = 0f;
                for (var i = 0; i < clicks.Length; i++) punch = Mathf.Max(punch, Kick(played, clicks[i]) - 1f);
                wordRect.localScale = Vector3.one * (1f + punch * WordPunch);
                shadow.Blur = UnitValue.Em(ShadowBlur * dropped);
                shadow.Paint = PaintRef.Solid(Theme.Fade(shadowColor, shadowColor.a * Mathf.Clamp01(dropped)));
                var glowing = Strike(played, 3);
                glow.Radius = UnitValue.Em(GlowRadius * glowing);
                glow.Paint = PaintRef.Solid(Theme.Fade(glowColor, GlowAlpha * Mathf.Clamp01(glowing)));

                for (var i = 0; i < Chips.Length; i++)
                {
                    Stage.Alpha(lit[i], Motion.Snap.Window(played, clicks[i], ChipLight));
                    chips[i].sizeDelta = cell * Kick(played, clicks[i]);
                }
            }

            /// <summary>How far style <paramref name="index"/> has landed, on a spring so it lands with a blow.</summary>
            private float Strike(float played, int index) => Mathf.Max(0f, Spring.Bouncy.Evaluate(played - clicks[index]));

            private const string Word = "VICTORY";
            private static readonly Vector2 ShadowDrop = new Vector2(0.06f, -0.11f);
        }

        /// <summary>How long each language stays on the card before the next tab is clicked.</summary>
        private const float LanguageRead = 0.7f;

        /// <summary>How long after a tab click the new texts start in, one row after another, and how fast they come and go.</summary>
        private const float TextLag = 0.08f;

        private const float RowStep = 0.05f;
        private const float TextIn = 0.3f;
        private const float TextOut = 0.12f;
        private const float TextRise = 16f;
        private const float TextSpread = 10f;
        private const float MirrorFor = 0.45f;
        private const float BadgeSize = 120f;
        private const float KnobTravel = 18f;
        private static readonly Vector2 ButtonSize = new Vector2(220f, 64f);
        private static readonly Vector2 ToggleSize = new Vector2(76f, 40f);

        private const float Inset = 28f;
        private const float SendSize = 56f;
        private const float BubblePad = 20f;
        private const float CaretBlink = 0.45f;
        private const float SendFlight = 0.4f;

        /// <summary>How long each style holds on the word before the next chip is clicked.</summary>
        private const float StyleRead = 0.35f;

        private const float ChipGap = 18f;
        private const float ChipLight = 0.18f;
        private const float OutlineWidth = 0.05f;

        /// <summary>How long the gradient takes to reach each next letter, and how hard the word punches on each style.</summary>
        private const float LetterStep = 0.035f;

        private const float WordPunch = 0.35f;
        private const float ShadowBlur = 0.012f;

        /// <summary>How far the word rises as its shadow lands.</summary>
        private const float WordLift = 12f;
        private const float GlowRadius = 0.2f;
        private const float GlowAlpha = 0.8f;
    }
}
