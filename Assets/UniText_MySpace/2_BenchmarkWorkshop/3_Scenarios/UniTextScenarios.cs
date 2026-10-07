using System;
using System.Collections.Generic;
using System.Text;
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One workload of <see cref="UniTextScenarioSuite"/>: built once, then driven every frame.</summary>
/// <remarks><see cref="Step"/> runs inside measured windows and must not allocate beyond the UniText call it exercises.</remarks>
abstract class UniTextScenario
{
    UniTextBase committing;
    int commits;

    protected UniTextScenario(string id, bool parallel)
    {
        Id = id;
        Parallel = parallel;
    }

    public string Id { get; }

    /// <summary>Value of <see cref="UniTextBase.UseParallel"/> while the scenario runs.</summary>
    public bool Parallel { get; }

    public abstract void Setup(ScenarioRig rig);

    public virtual void Step(int frame) { }

    /// <summary>Throws when the workload did not actually run.</summary>
    public abstract void Validate();

    public abstract void Describe(JObject workload);

    public virtual void Teardown() { }

    public void ResetCommits() => commits = 0;

    /// <summary>Throws when the text named by <see cref="ExpectCommitEveryFrame"/> reprocessed in fewer than 90% of <paramref name="frames"/>.</summary>
    public void RequireCommits(int frames)
    {
        if (committing == null) return;
        if (commits * 10 < frames * 9)
            throw new InvalidOperationException($"The changing text reprocessed in {commits} of {frames} frames.");
    }

    /// <summary>Declares that <paramref name="text"/> changes every frame, so the window must see it reprocess every frame.</summary>
    protected void ExpectCommitEveryFrame(UniTextBase text)
    {
        committing = text;
        text.LayoutCommitted += CountCommit;
    }

    void CountCommit() => commits++;
}

/// <summary>The camera, canvas and world root every scenario builds under, and the objects it built.</summary>
sealed class ScenarioRig : IDisposable
{
    public const float ReferenceWidth = 1080f;
    public const float ReferenceHeight = 1920f;

    readonly UniTextFontStack fontStack;
    readonly GameObject textPrefab;
    readonly GameObject worldTextPrefab;
    readonly GameObject inputFieldPrefab;
    readonly GameObject cameraObject;
    readonly GameObject canvasObject;
    readonly List<GameObject> roots = new();

    public ScenarioRig(UniTextFontStack fontStack, GameObject textPrefab, GameObject worldTextPrefab,
        GameObject inputFieldPrefab)
    {
        this.fontStack = fontStack;
        this.textPrefab = textPrefab;
        this.worldTextPrefab = worldTextPrefab;
        this.inputFieldPrefab = inputFieldPrefab;

        cameraObject = new GameObject("ScenarioCamera") { tag = "MainCamera" };
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        Camera = cameraObject.AddComponent<Camera>();
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = new Color(0.28f, 0.32f, 0.38f, 1f);
        Camera.fieldOfView = 60f;
        Camera.nearClipPlane = 0.3f;
        Camera.farClipPlane = 100f;

        canvasObject = new GameObject("ScenarioCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler))
        {
            layer = 5
        };
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera;
        canvas.planeDistance = 5f;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
                                          AdditionalCanvasShaderChannels.TexCoord2 |
                                          AdditionalCanvasShaderChannels.TexCoord3 |
                                          AdditionalCanvasShaderChannels.Normal |
                                          AdditionalCanvasShaderChannels.Tangent;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;
        CanvasRect = (RectTransform)canvasObject.transform;
    }

    public Camera Camera { get; }

    public RectTransform CanvasRect { get; }

    /// <summary>A full-canvas container destroyed by <see cref="Clear"/>.</summary>
    public RectTransform CreateRoot(string name)
    {
        var root = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        roots.Add(root);
        var rect = (RectTransform)root.transform;
        rect.SetParent(CanvasRect, false);
        Stretch(rect);
        return rect;
    }

    /// <summary>A world-space container destroyed by <see cref="Clear"/>.</summary>
    public Transform CreateWorldRoot(string name)
    {
        var root = new GameObject(name);
        roots.Add(root);
        return root.transform;
    }

    public UniText CreateText(RectTransform parent, string text, float fontSize)
    {
        var uniText = Instantiate<UniText>(textPrefab, parent, "Text");
        Configure(uniText, text, fontSize);
        return uniText;
    }

    public UniTextWorld CreateWorldText(Transform parent, string text, float fontSize)
    {
        var uniText = Instantiate<UniTextWorld>(worldTextPrefab, parent, "World Text");
        Configure(uniText, text, fontSize);
        return uniText;
    }

    public UniTextEditable CreateInputField(RectTransform parent, float fontSize)
    {
        var editable = Instantiate<Transform>(inputFieldPrefab, parent, "Input Field")
            .GetComponentInChildren<UniTextEditable>();
        if (editable == null)
            throw new InvalidOperationException("The Input Field prefab has no UniTextEditable.");
        editable.TextComponent.FontStack = fontStack;
        editable.TextComponent.FontSize = fontSize;
        editable.TextComponent.color = Color.white;
        return editable;
    }

    public void Clear()
    {
        foreach (var root in roots)
            if (root != null)
                Object.Destroy(root);
        roots.Clear();
    }

    public void Dispose()
    {
        Clear();
        Object.Destroy(canvasObject);
        Object.Destroy(cameraObject);
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void Configure(UniTextBase text, string value, float fontSize)
    {
        text.FontStack = fontStack;
        text.FontSize = fontSize;
        text.color = Color.white;
        text.Text = value;
    }

    static T Instantiate<T>(GameObject prefab, Transform parent, string role) where T : Component
    {
        var component = Object.Instantiate(prefab, parent, false).GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"The {role} prefab has no {typeof(T).Name}.");
        return component;
    }
}

/// <summary>The scenario catalogue and the content it draws from.</summary>
static class UniTextScenarios
{
    public const string MemoryId = "memory.cycles";

    public static readonly string[] LatinLines = BenchmarkConfig.Latin.Split('\n');
    public static readonly string[] MultilingualLines = BenchmarkConfig.MultilingualPlain.Split('\n');

    static readonly string[] Words =
    {
        "Health", "Mana", "Gold", "Quest", "Level", "Armor", "Speed", "Score", "Combo", "Bonus", "Shield",
        "Energy", "Rank", "Wave", "Crystal", "Stamina", "Ammo", "Credits", "Gems", "Keys", "Lives", "Time",
        "Damage", "Critical", "Defense", "Agility", "Wisdom", "Luck", "Fame", "Honor", "Supply", "Morale"
    };

    static readonly string[] Emoji =
    {
        "\U0001F600", "\U0001F603", "\U0001F604", "\U0001F601", "\U0001F606", "\U0001F605", "\U0001F602",
        "\U0001F923", "\U0001F60A", "\U0001F607", "\U0001F642", "\U0001F609", "\U0001F60D", "\U0001F970",
        "\U0001F618", "\U0001F60B", "\U0001F61C", "\U0001F92A", "\U0001F913", "\U0001F60E", "\U0001F929",
        "\U0001F973", "\U0001F914", "\U0001F44D", "\U0001F44F", "\U0001F64C", "\U0001F525", "\U0001F680",
        "\U0001F389", "\U0001F381", "\U0001F3C6", "\U0001F48E", "\U0001F31F", "\U0001F308", "\U0001F340",
        "\U0001F355", "\U0001F369", "\U0001F431", "\U0001F436", "\U0001F984", "\u2764\uFE0F", "\u2B50",
        "\U0001F44D\U0001F3FD", "\U0001F469\u200D\U0001F4BB", "\U0001F468\u200D\U0001F469\u200D\U0001F467",
        "\U0001F1EF\U0001F1F5", "\U0001F1E7\U0001F1F7", "\U0001F1EC\U0001F1EA"
    };

    public static List<UniTextScenario> Create() => new()
    {
        new ScreenScenario("screen.plain", ScreenStyle.Plain),
        new ScreenScenario("screen.outline", ScreenStyle.Outline),
        new ScreenScenario("screen.shadow", ScreenStyle.Shadow),
        new ScreenScenario("screen.glow", ScreenStyle.Glow),
        new ScreenScenario("screen.effects", ScreenStyle.Effects),
        new ScreenScenario("screen.decorations", ScreenStyle.Decorations),
        new ScreenScenario("screen.msdf", ScreenStyle.Msdf),
        new ScreenScenario("screen.large", ScreenStyle.Large),
        new ScreenScenario("screen.multilingual", ScreenStyle.Multilingual),
        new ScreenScenario("screen.emoji", ScreenStyle.Emoji),
        new CounterScenario("counters", true),
        new CounterScenario("counters.st", false),
        new TweenScenario("tween.color", TweenKind.Color),
        new TweenScenario("tween.size", TweenKind.Size),
        new TweenScenario("tween.alpha", TweenKind.Alpha),
        new AnimatedScenario("animated.wave", text => text.SetWholeText<WaveModifier>()),
        new AnimatedScenario("animated.shake", text => text.SetWholeText<ShakeModifier>()),
        new AnimatedScenario("animated.pulse", text => text.SetWholeText<PulseModifier>()),
        new RebuildScenario("rebuild.labels", RebuildContent.Labels, true),
        new RebuildScenario("rebuild.labels.st", RebuildContent.Labels, false),
        new RebuildScenario("rebuild.paragraphs", RebuildContent.Paragraphs, true),
        new RebuildScenario("rebuild.paragraphs.st", RebuildContent.Paragraphs, false),
        new RebuildScenario("rebuild.multilingual", RebuildContent.Multilingual, true),
        new TypingScenario("typing.end.short", 64, false),
        new TypingScenario("typing.end.2k", 2_000, false),
        new TypingScenario("typing.middle.2k", 2_000, true),
        new TypingScenario("typing.end.20k", 20_000, false),
        new TypingScenario("typing.middle.20k", 20_000, true),
        new ScrollScenario("scroll.static", false),
        new ScrollScenario("scroll.recycle", true),
        new ChurnScenario("churn"),
        new WorldScenario("world.static", WorldMotion.None),
        new WorldScenario("world.moving", WorldMotion.Moving),
        new WorldScenario("world.animated", WorldMotion.Animated),
        new WorldScenario("world.counters", WorldMotion.Counters)
    };

    /// <summary>
    /// Texts filling a <paramref name="columns"/> × <paramref name="rows"/> grid in a horizontal band of
    /// <paramref name="root"/>, cycling through <paramref name="content"/>.
    /// </summary>
    public static UniText[] Grid(ScenarioRig rig, RectTransform root, string[] content, int columns, int rows,
        float fontSize, float bandHeight = 1f, float bandTop = 0f)
    {
        var texts = new UniText[columns * rows];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = rig.CreateText(root, content[i % content.Length], fontSize);
            PlaceCell(texts[i], i, columns, rows, bandHeight, bandTop);
        }
        return texts;
    }

    /// <summary>Places <paramref name="target"/> in cell <paramref name="index"/> of a grid filling a horizontal band of its parent, measured from the top.</summary>
    public static void PlaceCell(Component target, int index, int columns, int rows, float bandHeight = 1f,
        float bandTop = 0f)
    {
        var rect = (RectTransform)target.transform;
        var column = index % columns;
        var row = index / columns;
        var width = 1f / columns;
        var height = bandHeight / rows;
        var top = 1f - bandTop - row * height;
        rect.anchorMin = new Vector2(column * width, top - height);
        rect.anchorMax = new Vector2((column + 1) * width, top);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(8f, 3f);
        rect.offsetMax = new Vector2(-8f, -3f);
    }

    /// <summary>Throws unless every enabled text under <paramref name="root"/> that has content produced glyphs.</summary>
    public static void RequireGlyphs(Component root)
    {
        var texts = root.GetComponentsInChildren<UniTextBase>();
        var checkedTexts = 0;
        foreach (var text in texts)
        {
            if (!text.isActiveAndEnabled || text.RenderedText.Length == 0) continue;
            checkedTexts++;
            if (text.GlyphCount == 0)
                throw new InvalidOperationException($"'{text.name}' holds text but produced no glyphs.");
        }
        if (checkedTexts == 0)
            throw new InvalidOperationException("The scenario shows no text.");
    }

    /// <summary>Writes the text count, the characters and glyphs they currently hold, the font size and how many change per frame.</summary>
    static void DescribeTexts(JObject workload, Component root, UniText[] texts, float fontSize, int changing)
    {
        var characters = 0;
        foreach (var text in texts)
            characters += text.RenderedText.Length;
        workload["texts"] = texts.Length;
        workload["characters"] = characters;
        workload["glyphs"] = CountGlyphs(root);
        workload["fontSize"] = fontSize;
        workload["changingTexts"] = changing;
    }

    public static int CountGlyphs(Component root)
    {
        var glyphs = 0;
        foreach (var text in root.GetComponentsInChildren<UniTextBase>())
            glyphs += text.GlyphCount;
        return glyphs;
    }

    public static string[] Labels(int count)
    {
        var labels = new string[count];
        for (var i = 0; i < count; i++)
            labels[i] = $"{Words[i % Words.Length]} {Words[(i * 7 + 3) % Words.Length].ToLowerInvariant()} {i * 37 % 1000:000}";
        return labels;
    }

    /// <summary><paramref name="count"/> distinct paragraphs of <paramref name="linesPerParagraph"/> consecutive lines, each starting at a different line.</summary>
    public static string[] Paragraphs(string[] lines, int linesPerParagraph, int count)
    {
        var paragraphs = new string[count];
        var builder = new StringBuilder(1024);
        for (var i = 0; i < count; i++)
        {
            builder.Clear();
            builder.Append('#').Append(i + 1).Append(' ');
            for (var j = 0; j < linesPerParagraph; j++)
            {
                if (j > 0) builder.Append(' ');
                builder.Append(lines[(i * 3 + j) % lines.Length]);
            }
            paragraphs[i] = builder.ToString();
        }
        return paragraphs;
    }

    public static string[] EmojiLines(int count, int perLine)
    {
        var lines = new string[count];
        var builder = new StringBuilder(perLine * 6);
        for (var i = 0; i < count; i++)
        {
            builder.Clear();
            for (var j = 0; j < perLine; j++)
            {
                if (j > 0) builder.Append(' ');
                builder.Append(Emoji[(i * 5 + j * 3) % Emoji.Length]);
            }
            lines[i] = builder.ToString();
        }
        return lines;
    }

    /// <summary>Plain Latin text of exactly <paramref name="length"/> characters.</summary>
    public static string LatinText(int length)
    {
        var builder = new StringBuilder(length + 128);
        var line = 0;
        while (builder.Length < length)
        {
            builder.Append(LatinLines[line % LatinLines.Length]).Append(line % 6 == 5 ? '\n' : ' ');
            line++;
        }
        builder.Length = length;
        return builder.ToString();
    }

    /// <summary>Writes <paramref name="value"/> right-aligned into the digits that end <paramref name="buffer"/>, padding with zeros.</summary>
    public static void WriteDigits(char[] buffer, int digitsStart, int value)
    {
        for (var i = buffer.Length - 1; i >= digitsStart; i--)
        {
            buffer[i] = (char)('0' + value % 10);
            value /= 10;
        }
    }

    enum ScreenStyle
    {
        Plain,
        Outline,
        Shadow,
        Glow,
        Effects,
        Decorations,
        Msdf,
        Large,
        Multilingual,
        Emoji
    }

    /// <summary>A static screen full of text; nothing changes, so the frame shows the idle cost and the GPU cost of the style.</summary>
    sealed class ScreenScenario : UniTextScenario
    {
        readonly ScreenStyle style;
        RectTransform root;
        UniText[] texts;
        float fontSize;

        public ScreenScenario(string id, ScreenStyle style) : base(id, true) => this.style = style;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            int columns, rows;
            string[] content;
            switch (style)
            {
                case ScreenStyle.Large:
                    columns = 2;
                    rows = 8;
                    fontSize = 84f;
                    content = Labels(columns * rows);
                    break;
                case ScreenStyle.Multilingual:
                    columns = 2;
                    rows = 12;
                    fontSize = 22f;
                    content = MultilingualLines;
                    break;
                case ScreenStyle.Emoji:
                    columns = 3;
                    rows = 16;
                    fontSize = 30f;
                    content = EmojiLines(columns * rows, 12);
                    break;
                default:
                    columns = 3;
                    rows = 16;
                    fontSize = 20f;
                    content = LatinLines;
                    break;
            }

            texts = Grid(rig, root, content, columns, rows, fontSize);
            foreach (var text in texts)
                ApplyStyle(text);
        }

        void ApplyStyle(UniText text)
        {
            switch (style)
            {
                case ScreenStyle.Outline:
                    text.SetWholeText<StrokeModifier>();
                    break;
                case ScreenStyle.Shadow:
                    text.SetWholeText<ShadowModifier>();
                    break;
                case ScreenStyle.Glow:
                    text.SetWholeText<GlowModifier>();
                    break;
                case ScreenStyle.Effects:
                    text.SetWholeText<StrokeModifier>();
                    text.SetWholeText<ShadowModifier>();
                    text.SetWholeText<GlowModifier>();
                    break;
                case ScreenStyle.Decorations:
                    text.SetWholeText<UnderlineModifier>();
                    text.SetWholeText<HighlightModifier>();
                    break;
                case ScreenStyle.Msdf:
                    text.RenderMode = UniTextRenderMode.MSDF;
                    break;
            }
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, texts, fontSize, 0);
            workload["style"] = style.ToString();
        }
    }

    /// <summary>Score-like labels whose number changes every frame, written in place through <see cref="UniTextBase.SetText(char[], int, int)"/>.</summary>
    /// <remarks>The counter scenarios of a process share one number sequence with no repeats in its first ten million, so no variant shows a number UniText's word cache saw in an earlier one.</remarks>
    sealed class CounterScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 16;
        const string Prefix = "Score ";
        const int Digits = 7;

        static int shown;

        readonly char[][] buffers = new char[Columns * Rows][];
        UniText[] texts;
        RectTransform root;

        public CounterScenario(string id, bool parallel) : base(id, parallel) { }

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            texts = Grid(rig, root, new[] { Prefix }, Columns, Rows, 30f);
            for (var i = 0; i < buffers.Length; i++)
                buffers[i] = (Prefix + new string('0', Digits)).ToCharArray();
            ExpectCommitEveryFrame(texts[0]);
        }

        public override void Step(int frame)
        {
            for (var i = 0; i < texts.Length; i++)
            {
                WriteDigits(buffers[i], Prefix.Length, NextValue());
                texts[i].SetText(buffers[i], 0, buffers[i].Length);
            }
        }

        static int NextValue() => (int)(shown++ * 7919L % 10_000_000);

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, texts, 30f, texts.Length);
            workload["api"] = "SetText(char[], int, int)";
        }
    }

    enum TweenKind
    {
        Color,
        Size,
        Alpha
    }

    /// <summary>Labels whose color, font size or color alpha changes every frame.</summary>
    sealed class TweenScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 16;
        const int Steps = 61;

        readonly TweenKind kind;
        readonly Color[] colors = new Color[Steps];
        readonly float[] sizes = new float[Steps];
        readonly float[] alphas = new float[Steps];
        UniText[] texts;
        RectTransform root;

        public TweenScenario(string id, TweenKind kind) : base(id, true) => this.kind = kind;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            texts = Grid(rig, root, Labels(Columns * Rows), Columns, Rows, 30f);
            for (var i = 0; i < Steps; i++)
            {
                var phase = i / (float)Steps;
                colors[i] = Color.HSVToRGB(phase, 0.6f, 1f);
                sizes[i] = 26f + 8f * Mathf.Sin(phase * Mathf.PI * 2f);
                alphas[i] = 0.55f + 0.45f * Mathf.Cos(phase * Mathf.PI * 2f);
            }
        }

        public override void Step(int frame)
        {
            for (var i = 0; i < texts.Length; i++)
            {
                var index = (frame + i) % Steps;
                switch (kind)
                {
                    case TweenKind.Color:
                        texts[i].color = colors[index];
                        break;
                    case TweenKind.Size:
                        texts[i].FontSize = sizes[index];
                        break;
                    default:
                        var color = texts[i].color;
                        color.a = alphas[index];
                        texts[i].color = color;
                        break;
                }
            }
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, texts, 30f, texts.Length);
            workload["property"] = kind.ToString();
        }
    }

    /// <summary>Labels animated by a whole-text animation modifier advancing on its own clock.</summary>
    sealed class AnimatedScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 16;

        readonly Action<UniText> animate;
        UniText[] texts;
        RectTransform root;

        public AnimatedScenario(string id, Action<UniText> animate) : base(id, true) => this.animate = animate;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            texts = Grid(rig, root, Labels(Columns * Rows), Columns, Rows, 30f);
            foreach (var text in texts)
                animate(text);
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload) => DescribeTexts(workload, root, texts, 30f, texts.Length);
    }

    enum RebuildContent
    {
        Labels,
        Paragraphs,
        Multilingual
    }

    /// <summary>Texts that receive different content every frame, cycling through a pool much larger than one frame's demand.</summary>
    sealed class RebuildScenario : UniTextScenario
    {
        readonly RebuildContent content;
        UniText[] texts;
        string[] pool;
        RectTransform root;
        float fontSize;

        public RebuildScenario(string id, RebuildContent content, bool parallel) : base(id, parallel) =>
            this.content = content;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            int columns, rows;
            switch (content)
            {
                case RebuildContent.Labels:
                    columns = 4;
                    rows = 16;
                    fontSize = 30f;
                    pool = Labels(509);
                    break;
                case RebuildContent.Paragraphs:
                    columns = 2;
                    rows = 3;
                    fontSize = 24f;
                    pool = Paragraphs(LatinLines, 8, 37);
                    break;
                default:
                    columns = 2;
                    rows = 3;
                    fontSize = 22f;
                    pool = Paragraphs(MultilingualLines, 4, 37);
                    break;
            }
            texts = Grid(rig, root, pool, columns, rows, fontSize);
            ExpectCommitEveryFrame(texts[0]);
        }

        public override void Step(int frame)
        {
            for (var i = 0; i < texts.Length; i++)
                texts[i].SetText(pool[(frame * texts.Length + i) % pool.Length]);
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, texts, fontSize, texts.Length);
            workload["content"] = content.ToString();
            workload["poolSize"] = pool.Length;
        }
    }

    /// <summary>
    /// One keystroke per frame into an input field: a character typed on even frames and erased with backspace
    /// on odd ones, so the document length stays constant.
    /// </summary>
    sealed class TypingScenario : UniTextScenario
    {
        const string Typed = "x";

        readonly int length;
        readonly bool middle;
        UniTextEditable editable;
        RectTransform root;
        string initial;
        string typed;
        bool lastWasInsert;

        public TypingScenario(string id, int length, bool middle) : base(id, true)
        {
            this.length = length;
            this.middle = middle;
        }

        int Caret => middle ? length / 2 : length;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            editable = rig.CreateInputField(root, 26f);
            var field = (RectTransform)editable.transform.parent;
            ScenarioRig.Stretch(field);
            field.offsetMin = new Vector2(24f, 24f);
            field.offsetMax = new Vector2(-24f, -24f);
            initial = LatinText(length);
            typed = initial.Insert(Caret, Typed);
            editable.SetText(initial);
            ExpectCommitEveryFrame(editable.TextComponent);
        }

        public override void Step(int frame)
        {
            if ((frame & 1) == 0)
            {
                editable.MoveCaretTo(Caret);
                editable.InsertText(Typed);
                lastWasInsert = true;
            }
            else
            {
                editable.DeletePrevious();
                lastWasInsert = false;
            }
        }

        public override void Validate()
        {
            if (editable.TextComponent.GlyphCount == 0)
                throw new InvalidOperationException("The field produced no glyphs.");
            if (!editable.TextEquals(lastWasInsert ? typed : initial))
                throw new InvalidOperationException("The field does not hold the text the keystrokes should leave.");
        }

        public override void Describe(JObject workload)
        {
            workload["characters"] = length;
            workload["caret"] = middle ? "middle" : "end";
            workload["glyphs"] = editable.TextComponent.GlyphCount;
            workload["fontSize"] = 26f;
            workload["changingTexts"] = 1;
        }
    }

    /// <summary>A masked list taller than the screen scrolled every frame; the recycling variant also re-texts the cells a pooled list would reuse.</summary>
    sealed class ScrollScenario : UniTextScenario
    {
        const int Cells = 120;
        const float CellHeight = 72f;
        const int RecycledPerFrame = 4;

        readonly bool recycle;
        readonly UniText[] texts = new UniText[Cells];
        RectTransform root;
        RectTransform content;
        string[] pool;
        float travel;

        public ScrollScenario(string id, bool recycle) : base(id, true) => this.recycle = recycle;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            root.gameObject.AddComponent<RectMask2D>();
            content = new GameObject("Content", typeof(RectTransform)) { layer = 5 }.GetComponent<RectTransform>();
            content.SetParent(root, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, Cells * CellHeight);
            pool = Labels(Cells * 4);
            for (var i = 0; i < Cells; i++)
            {
                texts[i] = rig.CreateText(content, LatinLines[i % LatinLines.Length], 26f);
                var rect = texts[i].rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(-32f, CellHeight - 6f);
                rect.anchoredPosition = new Vector2(0f, -i * CellHeight);
            }
            travel = Cells * CellHeight - ScenarioRig.ReferenceHeight;
        }

        public override void Step(int frame)
        {
            content.anchoredPosition = new Vector2(0f, Mathf.PingPong(frame * 9f, travel));
            if (!recycle) return;
            for (var i = 0; i < RecycledPerFrame; i++)
            {
                var cell = (frame * RecycledPerFrame + i) % Cells;
                texts[cell].SetText(pool[(frame * RecycledPerFrame + i) % pool.Length]);
            }
        }

        public override void Validate()
        {
            var visible = 0;
            foreach (var text in texts)
                if (!text.canvasRenderer.cull && text.GlyphCount > 0)
                    visible++;
            if (visible == 0)
                throw new InvalidOperationException("No list cell inside the viewport shows glyphs.");
        }

        public override void Describe(JObject workload)
        {
            workload["texts"] = Cells;
            workload["glyphs"] = CountGlyphs(root);
            workload["fontSize"] = 26f;
            workload["changingTexts"] = recycle ? RecycledPerFrame : 0;
            workload["mask"] = "RectMask2D";
        }
    }

    /// <summary>Labels created and destroyed every frame, as a spawning UI or a pool-less list does.</summary>
    sealed class ChurnScenario : UniTextScenario
    {
        const int PerFrame = 16;
        const int Columns = 4;

        readonly List<GameObject> live = new(PerFrame);
        ScenarioRig rig;
        RectTransform root;
        string[] pool;

        public ChurnScenario(string id) : base(id, true) { }

        public override void Setup(ScenarioRig rig)
        {
            this.rig = rig;
            root = rig.CreateRoot(Id);
            pool = Labels(509);
        }

        public override void Step(int frame)
        {
            foreach (var text in live)
                Object.Destroy(text);
            live.Clear();
            for (var i = 0; i < PerFrame; i++)
            {
                var text = rig.CreateText(root, pool[(frame * PerFrame + i) % pool.Length], 30f);
                PlaceCell(text, i, Columns, PerFrame / Columns);
                live.Add(text.gameObject);
            }
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            workload["createdPerFrame"] = PerFrame;
            workload["destroyedPerFrame"] = PerFrame;
            workload["fontSize"] = 30f;
        }

        public override void Teardown() => live.Clear();
    }

    enum WorldMotion
    {
        None,
        Moving,
        Animated,
        Counters
    }

    /// <summary>Nameplate-like world-space labels spread through the camera's view at several depths.</summary>
    sealed class WorldScenario : UniTextScenario
    {
        const int Columns = 8;
        const int Rows = 30;
        const int Count = Columns * Rows;
        const int Counters = 64;
        const string Prefix = "-";
        const int Digits = 4;

        readonly WorldMotion motion;
        readonly UniTextWorld[] texts = new UniTextWorld[Count];
        readonly Vector3[] anchors = new Vector3[Count];
        readonly char[][] buffers = new char[Counters][];
        Transform root;

        public WorldScenario(string id, WorldMotion motion) : base(id, true) => this.motion = motion;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateWorldRoot(Id);
            var labels = Labels(Count);
            for (var i = 0; i < Count; i++)
            {
                var column = i % Columns;
                var row = i / Columns;
                var depth = 8f + (i * 7 % 23);
                anchors[i] = rig.Camera.ViewportToWorldPoint(new Vector3(
                    (column + 0.5f) / Columns, (row + 0.5f) / Rows, depth));
                texts[i] = rig.CreateWorldText(root, labels[i], 36f);
                texts[i].transform.position = anchors[i];
                if (motion == WorldMotion.Animated)
                    texts[i].SetWholeText<WaveModifier>();
            }
            if (motion != WorldMotion.Counters) return;
            for (var i = 0; i < Counters; i++)
                buffers[i] = (Prefix + new string('0', Digits)).ToCharArray();
            ExpectCommitEveryFrame(texts[0]);
        }

        public override void Step(int frame)
        {
            switch (motion)
            {
                case WorldMotion.Moving:
                    for (var i = 0; i < Count; i++)
                    {
                        var phase = frame * 0.05f + i * 0.37f;
                        texts[i].transform.position = anchors[i] + new Vector3(Mathf.Sin(phase) * 0.15f, Mathf.Cos(phase * 1.3f) * 0.1f, 0f);
                    }
                    break;
                case WorldMotion.Counters:
                    for (var i = 0; i < Counters; i++)
                    {
                        WriteDigits(buffers[i], Prefix.Length, (frame * 31 + i * 977) % 10_000);
                        texts[i].SetText(buffers[i], 0, buffers[i].Length);
                    }
                    break;
            }
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            workload["texts"] = Count;
            workload["glyphs"] = CountGlyphs(root);
            workload["fontSize"] = 36f;
            workload["changingTexts"] = motion switch
            {
                WorldMotion.Counters => Counters,
                WorldMotion.None => 0,
                _ => Count
            };
            workload["motion"] = motion.ToString();
        }
    }
}
