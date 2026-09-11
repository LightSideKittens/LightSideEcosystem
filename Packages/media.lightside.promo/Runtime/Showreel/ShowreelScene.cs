using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Promo
{
    /// <summary>
    /// The whole showreel as one continuous scene: a button becomes a component, and that component then drives
    /// everything the reel shows — the text it renders, the paint it takes, and the way that text arrives.
    /// </summary>
    /// <remarks>
    /// One slide and no cuts. The card is the causal spine: every effect a specimen takes has a row that dropped into
    /// the card's Styles list on the same beat, so nothing on screen reads as a video effect — it is a modifier
    /// somebody added, in front of the viewer. The card therefore never leaves once it lands.
    /// <para>
    /// Build and pose are split across two files: the layout settles once, the choreography never stops being tuned.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(ShowreelScene))]
    public sealed partial class ShowreelScene : Slide
    {
        [SerializeField] private string buttonCaption = "Add Component";
        [SerializeField] private string componentTitle = "UniText";
        [SerializeField] private string[] fieldLabels = { "Text", "Font", "Styles" };
        [SerializeField] private string shoutLine = "0 fonts. Not one.";
        [SerializeField] private string barsTitle = "Memory for this text";
        [SerializeField] private string arabic = "مرحبا";

        [SerializeField, TextArea(4, 8)]
        private string wall =
            "日月火水木金土山川田人口手足目耳鼻舌歯首心身体力気血肉骨皮毛" +
            "雨雪雲風雷霜露虹星空海洋湖池河谷岩石砂原野林森竹松梅桜菊蘭" +
            "犬猫馬牛羊豚鶏鳥魚亀蛇虫蝶蜂蟻鹿熊狼狐猿象虎豹鯨鮫鷲鷹燕鳩" +
            "行走飛泳読書話聞見食飲寝起座立歩考思知覚感動働遊学教習練修" +
            "国家族村町市県都府省庁院校館店屋堂塔橋道路駅港軍警官民法権" +
            "简体中文和繁體中文，一個字體都不用。\n" +
            "😀😃😄😁😆😅🤣😂🙂🙃😉😊😇🥰😍🤩😘😗😚😙🥲😋😛😜🤪😝\n" +
            "🤗🤭🤫🤔🤐🤨😐😑😶😏😒🙄😬🤥😌😔😪🤤😴😷🤒🤕🤢🤮🤧🥵\n" +
            "🐶🐱🐭🐹🐰🦊🐻🐼🐨🐯🦁🐮🐷🐸🐵🙈🙉🙊🐒🐔🐧🐦🐤🦆🦅🦉\n" +
            "🍏🍎🍐🍊🍋🍌🍉🍇🍓🫐🍈🍒🍑🥭🍍🥥🥝🍅🥑🍆🥔🥕🌽🌶🫑🥒\n" +
            "🚀🛸🛰🌍🌎🌏🌕🌖🌗🌘🌑🌒🌓🌔⭐️🌟✨⚡️🔥💥❄️🌈☀️🌤⛅️";

        [SerializeField] private string typed = "日月火水木金土山川田人口手足";

        [SerializeField] private float uniMiB = 5f;
        [SerializeField] private float tmpMiB = 80f;

        /// <summary>
        /// The Styles list as the card shows it: the paint layers, then the reveal that closes the film.
        /// </summary>
        /// <remarks>
        /// Not authoring surface. Each name is the type of a modifier this scene constructs and drives by hand, so a
        /// list that could be edited apart from the code would be a caption promising a layer nobody added.
        /// </remarks>
        private static readonly string[] StyleNames =
        {
            "FillModifier", "StrokeModifier", "StrokeModifier", "StrokeModifier",
            "ShadowModifier", "GlowModifier", "RevealModifier"
        };

        /// <summary>The reveal handlers the finale cycles, named as the picker shows them.</summary>
        private static readonly string[] RevealNames = { "Slide", "Drop", "Pop", "Swing", "Spiral" };

        /// <summary>A display face for the Arabic specimen; a text face is too thin to carry the outer strokes.</summary>
        [SerializeField] private UniTextFont displayFont;

        private RectTransform world;
        private Pointer pointer;

        private Widget button;
        private CanvasGroup buttonGroup;
        private Widget burst;

        private Inspector card;
        private GlyphReveal fieldReveal;
        private StyleStack styles;

        private Widget halo;
        private Widget haloPlate;
        private UniText shout;

        private RectTransform payload;
        private Meters bars;
        private RectTransform scroll;
        private UniText wallText;
        private GlyphReveal wallReveal;
        private float scrollBy;

        private UniText word;
        private GlyphReveal wordReveal;
        private GlowModifier glow;
        private ShadowModifier shadow;
        private StrokeModifier outer;
        private StrokeModifier middle;
        private StrokeModifier inner;
        private FillModifier fill;

        private RectTransform finaleHolder;
        private UniText[] lines;
        private GlyphReveal[] arrivals;

        private float press;
        private float morph;
        private float focus;
        private float aside;
        private float pour;
        private float wipe;
        private float slam;
        private float strikes;
        private float exitWord;
        private float finale;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            world = stage.Node("World");
            stage.Stretch(world);

            BuildButton(stage);
            BuildCard(stage, theme);
            BuildPayload(stage, theme);
            BuildWord(stage, theme);
            BuildFinale(stage, theme);
            BuildPointer(stage);
            Schedule();
        }

        private void BuildButton(Stage stage)
        {
            var size = new Vector2(stage.Width * 0.4f, stage.Height * 0.15f);

            burst = stage.Shape("Burst", world, ShapeKind.Capsule);
            stage.Box(burst.Rect, Vector2.zero, size);
            Stage.Ramped(burst.Fill, stage.Theme.Brand, PaintProjectionKind.Linear, 100f);

            button = stage.Button("Button", world);
            stage.Box(button.Rect, Vector2.zero, size);
            buttonGroup = Stage.Group(button.Rect);

            var label = stage.Label(button.Rect, buttonCaption, stage.Height * 0.05f, Color.white,
                face: stage.Theme.DisplayFace);
            label.WordWrap = false;
        }

        private void BuildCard(Stage stage, Theme theme)
        {
            var body = stage.Height * 0.046f;
            card = stage.Inspector("Card", world, componentTitle, fieldLabels,
                Vector2.zero, CardSize(stage), body,
                new[] { typed, null, null }, new[] { 1f, 1f, 4.2f });

            fieldReveal = stage.Reveal(card[TextRow].Value, new PopRevealHandler(), 2.4f);

            styles = stage.StyleStack(card[StylesRow].Well.Rect, StyleNames, body * 0.5f,
                pickerAt: RevealRow, pick: RevealNames[0]);

            BuildHalo(stage, theme);

            shout = stage.Label(world, shoutLine, stage.Height * 0.1f, theme.Text,
                stretch: false, face: theme.DisplayFace);
            stage.Box(shout.rectTransform,
                new Vector2(0f, -CardSize(stage).y * 0.5f - stage.Height * 0.11f),
                new Vector2(stage.Width * 0.9f, stage.Height * 0.14f));
            shout.WordWrap = false;
            shout.Styles.Add(Style.WholeText(stage.BrandFill(12f)));
        }

        /// <summary>
        /// The plate and ring that pick the Font row out of the card.
        /// </summary>
        /// <remarks>
        /// A solid plate under the ring, not a ring alone. Scaled up over a panel of the same colour, an outline with
        /// nothing behind it reads as a stray rectangle rather than as the row being lifted out of the card.
        /// </remarks>
        private void BuildHalo(Stage stage, Theme theme)
        {
            var field = card[FontRow];

            haloPlate = stage.Shape("HaloPlate", field.Rect, ShapeKind.RoundedRect, theme.RadiusXxl);
            stage.Stretch(haloPlate.Rect, -HaloPad, -HaloPad, -HaloPad, -HaloPad);
            Stage.Solid(haloPlate.Fill, Theme.Lift(theme.Surface, 0.16f));
            Stage.AddShadow(haloPlate.Shape, theme.Shadow, new Vector2(0f, -12f), 44f, 6f);
            haloPlate.Rect.SetAsFirstSibling();

            halo = stage.Shape("Halo", field.Rect, ShapeKind.RoundedRect, theme.RadiusXxl);
            stage.Stretch(halo.Rect, -HaloPad, -HaloPad, -HaloPad, -HaloPad);
            Stage.Solid(halo.Fill, Color.clear);
            Stage.AddStroke(halo.Shape, theme.Magenta, 5f, 0f);
        }

        /// <summary>
        /// The left column of the typing beat: two memory bars, and under them the wall of text that fills them.
        /// </summary>
        /// <remarks>
        /// The wall is a masked viewport whose content is taller than it, scrolled by the reveal. Text that overruns
        /// its box and stops is a layout failure on screen; the same overrun scrolling reads as "there is more of
        /// this", which is the claim the shot is making.
        /// </remarks>
        private void BuildPayload(Stage stage, Theme theme)
        {
            payload = stage.Node("Payload", world);
            stage.Stretch(payload);

            var column = stage.Width * 0.42f;
            var x = -stage.Width * 0.25f;
            var top = stage.Height * 0.45f;

            bars = stage.Meters("Bars", payload, barsTitle, new[]
            {
                new MeterEntry("UniText", Read(0f), uniMiB / tmpMiB * BarLie, true, theme.Pass),
                new MeterEntry("TextMeshPro", Read(0f), 1f, true, theme.Coral)
            }, Vector2.zero, column, textSize: stage.Height * 0.03f);

            var barsHeight = bars.Height;
            bars.Surface.Rect.anchoredPosition = new Vector2(x, top - barsHeight * 0.5f);

            var wallTop = top - barsHeight - PanelGap;
            var wallHeight = wallTop + stage.Height * 0.45f;
            var panel = stage.Panel("Wall", payload);
            stage.Box(panel.Rect, new Vector2(x, wallTop - wallHeight * 0.5f), new Vector2(column, wallHeight));

            var viewport = stage.Node("Viewport", panel.Rect);
            stage.Stretch(viewport, theme.PadLg, theme.PadLg, theme.PadLg, theme.PadLg);
            viewport.gameObject.AddComponent<RectMask2D>();

            var visible = wallHeight - theme.PadLg * 2f;
            var content = visible * ContentRuns;
            scrollBy = content - visible;

            scroll = stage.Node("Content", viewport);
            stage.Anchor(scroll, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, content));

            wallText = stage.Label(scroll, wall, stage.Height * 0.034f, theme.Text,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            stage.Stretch(wallText.rectTransform);
            wallReveal = stage.Reveal(wallText, new FadeRevealHandler(), 24f);
        }

        private void BuildWord(Stage stage, Theme theme)
        {
            word = stage.Label(world, arabic, stage.Height * 0.34f, Color.white, stretch: false);
            stage.Box(word.rectTransform, new Vector2(-stage.Width * 0.24f, 0f),
                new Vector2(stage.Width * 0.46f, stage.Height * 0.6f));
            word.WordWrap = false;
            if (displayFont) word.Font = displayFont;
            wordReveal = stage.Reveal(word, new PopRevealHandler(), 2.2f);

            glow = new GlowModifier { Paint = PaintRef.Solid(theme.Magenta), Radius = UnitValue.Em(0f) };
            shadow = new ShadowModifier
            {
                Paint = PaintRef.Solid(new Color32(0, 0, 0, 200)),
                Offset = new UnitVector2(Vector2.zero, UnitKind.Em),
                Blur = UnitValue.Em(0f),
                Spread = UnitValue.Em(0f)
            };
            outer = Stroke(theme.Violet);
            middle = Stroke(theme.Coral);
            inner = Stroke(Color.white);
            fill = stage.BrandFill(120f);
            fill.Tint = ClearTint;

            word.Styles.Add(Style.WholeText(glow));
            word.Styles.Add(Style.WholeText(shadow));
            word.Styles.Add(Style.WholeText(outer));
            word.Styles.Add(Style.WholeText(middle));
            word.Styles.Add(Style.WholeText(inner));
            word.Styles.Add(Style.WholeText(fill));
        }

        private void BuildFinale(Stage stage, Theme theme)
        {
            finaleHolder = stage.Node("Finale", world);
            stage.Stretch(finaleHolder);

            var handlers = new RevealHandler[]
            {
                new SlideRevealHandler { Offset = new Vector2(-90f, 0f) },
                new DropRevealHandler(),
                new PopRevealHandler(),
                new SwingRevealHandler(),
                new SpiralRevealHandler()
            };

            var size = stage.Height * 0.092f;
            var step = size * 1.3f;
            lines = new UniText[handlers.Length];
            arrivals = new GlyphReveal[handlers.Length];

            for (var i = 0; i < handlers.Length; i++)
            {
                var y = -step * (i - (handlers.Length - 1) * 0.5f);
                lines[i] = stage.Label(finaleHolder, RevealNames[i], size, theme.Text,
                    stretch: false, face: theme.DisplayFace);
                stage.Box(lines[i].rectTransform, new Vector2(-stage.Width * 0.24f, y),
                    new Vector2(stage.Width * 0.46f, step));
                lines[i].WordWrap = false;
                arrivals[i] = stage.Reveal(lines[i], handlers[i], 3f);
            }
        }

        private void BuildPointer(Stage stage)
        {
            pointer = stage.Pointer(stage.At(0.16f, 0.1f), new[]
            {
                Beat.To(new Vector2(0f, -stage.Height * 0.02f), targetWidth: stage.Width * 0.4f),
                Beat.Click("press", settles: true),
                Beat.Wait(0.1f),
                Beat.To(stage.At(1.18f, -0.12f))
            });
        }

        /// <summary>Derives every phase from the pointer's own press, and the slide's length from the last of them.</summary>
        private void Schedule()
        {
            press = Aim + pointer.Timeline.Mark("press");
            morph = press + 0.1f;
            focus = morph + 1.9f;
            aside = focus + 2.9f;
            pour = aside + 0.55f;
            wipe = pour + PourFor + 1f;
            slam = wipe + 0.4f;
            strikes = slam + 0.75f;
            exitWord = strikes + PaintCount * StrikeStep + 0.8f;
            finale = exitWord + 0.35f;

            Seconds = Mathf.Max(Seconds,
                finale + FinaleLead + (lines.Length - 1) * FinaleStep + FinaleArrive + Hold);

            Cue(pointer.Timeline.Cues(), Aim);
            Cue("morph", morph);
            Cue("shout", focus + 0.5f);
            Cue("aside", aside);
            Cue("pour", pour);
            Cue("wipe", wipe);
            Cue("slam", slam);
            for (var i = 0; i < PaintCount; i++) Cue("strike", strikes + i * StrikeStep);
            for (var i = 0; i < lines.Length; i++) Cue("reveal", finale + FinaleLead + i * FinaleStep);
        }

        private StrokeModifier Stroke(Color color) => new StrokeModifier
        {
            Paint = PaintRef.Solid(color),
            Width = UnitValue.Em(0f),
            Align = 1f
        };

        /// <summary>A memory figure as the bars write it.</summary>
        private static string Read(float mib) => mib >= 10f ? $"{mib:0} MiB" : $"{mib:0.0} MiB";

        private static Vector2 CardSize(Stage stage) =>
            new Vector2(stage.Width * 0.6f, stage.Height * 0.62f);

        private const int TextRow = 0;

        /// <summary>Which field the no-font beat is about.</summary>
        private const int FontRow = 1;

        private const int StylesRow = 2;

        /// <summary>How many style rows are paint layers; the last row is the reveal.</summary>
        private static readonly int PaintCount = StyleNames.Length - 1;

        private static readonly int RevealRow = PaintCount;

        /// <summary>How many viewport heights of text the wall holds, and therefore how far it scrolls.</summary>
        private const float ContentRuns = 2.4f;

        /// <summary>
        /// How full the green bar settles relative to the red one.
        /// </summary>
        /// <remarks>
        /// Drawn above true scale: at 5 against 80 the honest bar is a sliver the eye reads as empty, and "empty" is
        /// a different claim than "small". The figures beside the bars stay exact.
        /// </remarks>
        private const float BarLie = 1.9f;

        private static readonly Color32 ClearTint = new Color32(255, 255, 255, 0);
        private static readonly Color32 FullTint = new Color32(255, 255, 255, 255);

        private const float Aim = 0.5f;
        private const float PourFor = 4.2f;
        private const float StrikeStep = 0.36f;
        private const float FinaleLead = 0.4f;

        /// <summary>
        /// The gap between one finale line and the next.
        /// </summary>
        /// <remarks>
        /// Wide enough that the picker visibly changes before the line it names arrives: the beat's point is that the
        /// dropdown chose the animation, and lines arriving faster than the eye can follow the picker read as five
        /// effects rather than one selectable one.
        /// </remarks>
        private const float FinaleStep = 0.62f;

        private const float FinaleArrive = 0.95f;
        private const float Hold = 1.4f;
        private const float PanelGap = 24f;
        private const float HaloPad = 14f;
    }
}
