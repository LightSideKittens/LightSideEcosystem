using System;
using System.Collections.Generic;
using System.Text;
using LightSide;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>The text engine a scenario draws with.</summary>
enum ScenarioEngine
{
    UniText,
    Tmp
}

/// <summary>One workload of <see cref="UniTextScenarioSuite"/>: built once, then driven every frame.</summary>
/// <remarks><see cref="Step"/> runs inside measured windows and must not allocate beyond the text engine call it exercises.</remarks>
abstract class UniTextScenario
{
    readonly Action<Object> countTmpCommit;
    UniTextBase committing;
    TMP_Text committingTmp;
    int commits;

    protected UniTextScenario(string id, bool parallel)
    {
        Id = id;
        Parallel = parallel;
        countTmpCommit = CountTmpCommit;
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
        if (committing == null && committingTmp == null) return;
        if (commits * 10 < frames * 9)
            throw new InvalidOperationException($"The changing text reprocessed in {commits} of {frames} frames.");
    }

    /// <summary>Stops counting the reprocessing of the text named by <see cref="ExpectCommitEveryFrame"/>.</summary>
    public void StopCountingCommits()
    {
        if (committing != null) committing.LayoutCommitted -= CountCommit;
        if (committingTmp != null) TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(countTmpCommit);
        committing = null;
        committingTmp = null;
    }

    /// <summary>Declares that <paramref name="text"/> changes every frame, so the window must see it reprocess every frame.</summary>
    protected void ExpectCommitEveryFrame(MaskableGraphic text)
    {
        if (text is UniTextBase uniText)
        {
            committing = uniText;
            uniText.LayoutCommitted += CountCommit;
            return;
        }
        committingTmp = (TMP_Text)text;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(countTmpCommit);
    }

    void CountCommit() => commits++;

    void CountTmpCommit(Object changed)
    {
        if (changed == committingTmp) commits++;
    }
}

/// <summary>The camera, canvas and world root every scenario builds under, and the objects it built.</summary>
/// <remarks>
/// Every 2D text it builds carries a whole-text stroke and shadow at their defaults, and every TMP label the outline
/// and underlay material; 3D text keeps its own layers. UniText texts are instantiated from styled templates built at
/// the first text, never earlier: styling a template initializes UniText, which the first-screen measurement pays.
/// </remarks>
sealed class ScenarioRig : IDisposable
{
    public const float ReferenceWidth = 1080f;
    public const float ReferenceHeight = 1920f;

    readonly UniTextFontStack fontStack;
    readonly TMP_FontAsset tmpFont;
    readonly Material tmpMaterial;
    readonly GameObject textPrefab;
    readonly GameObject worldTextPrefab;
    readonly GameObject inputFieldPrefab;
    readonly GameObject cameraObject;
    readonly GameObject canvasObject;
    readonly GameObject templates;
    readonly List<GameObject> roots = new();
    UniText textTemplate;
    UniTextWorld worldTextTemplate;

    public ScenarioRig(UniTextFontStack fontStack, TMP_FontAsset tmpFont, Material tmpMaterial,
        GameObject textPrefab, GameObject worldTextPrefab, GameObject inputFieldPrefab)
    {
        this.fontStack = fontStack;
        this.tmpFont = tmpFont;
        this.tmpMaterial = tmpMaterial;
        this.textPrefab = textPrefab;
        this.worldTextPrefab = worldTextPrefab;
        this.inputFieldPrefab = inputFieldPrefab;

        templates = new GameObject("ScenarioTemplates");
        templates.SetActive(false);

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

    /// <summary>A text of the default prefab with the rig's stroke and shadow, instantiated as a styled prefab instance is.</summary>
    public UniText CreateText(RectTransform parent, string text, float fontSize)
    {
        if (textTemplate == null)
            textTemplate = Styled(Instantiate<UniText>(textPrefab, templates.transform, "Text"));
        var uniText = Object.Instantiate(textTemplate, parent, false);
        Configure(uniText, text, fontSize);
        return uniText;
    }

    /// <summary>
    /// A label of <paramref name="engine"/>: UniText as <see cref="CreateText"/> makes it; TMP as its menu item makes
    /// one, with the rig's TMP font and outline and underlay material assigned before it wakes, as a prefab instance
    /// carries them.
    /// </summary>
    public MaskableGraphic CreateLabel(ScenarioEngine engine, RectTransform parent, string text, float fontSize)
    {
        if (engine == ScenarioEngine.UniText) return CreateText(parent, text, fontSize);
        var go = new GameObject("Text (TMP)", typeof(RectTransform)) { layer = 5 };
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.font = tmpFont;
        label.fontSharedMaterial = tmpMaterial;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.text = text;
        go.SetActive(true);
        return label;
    }

    /// <summary>A world text of the default prefab with the rig's stroke and shadow, instantiated as a styled prefab instance is.</summary>
    public UniTextWorld CreateWorldText(Transform parent, string text, float fontSize)
    {
        if (worldTextTemplate == null)
            worldTextTemplate = Styled(Instantiate<UniTextWorld>(worldTextPrefab, templates.transform, "World Text"));
        var uniText = Object.Instantiate(worldTextTemplate, parent, false);
        Configure(uniText, text, fontSize);
        return uniText;
    }

#if UNITEXT_3D
    /// <summary>Extruded text made as GameObject > LightSide > UniText > Text 3D makes it: a 400 × 100 rect at a 0.01 scale, centred.</summary>
    public UniText3D CreateText3D(Transform parent, string text, float fontSize)
    {
        var go = new GameObject("Text 3D", typeof(RectTransform));
        go.SetActive(false);
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(400f, 100f);
        rect.localScale = Vector3.one * 0.01f;
        var uniText = go.AddComponent<UniText3D>();
        uniText.HorizontalAlignment = HorizontalAlignment.Center;
        uniText.VerticalAlignment = VerticalAlignment.Middle;
        Configure(uniText, text, fontSize);
        go.SetActive(true);
        return uniText;
    }
#endif

    public UniTextEditable CreateInputField(RectTransform parent, float fontSize)
    {
        var editable = Instantiate<Transform>(inputFieldPrefab, parent, "Input Field")
            .GetComponentInChildren<UniTextEditable>();
        if (editable == null)
            throw new InvalidOperationException("The Input Field prefab has no UniTextEditable.");
        Styled(editable.TextComponent);
        foreach (var text in editable.GetComponentsInChildren<UniTextBase>(true))
            text.FontStack = fontStack;
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
        Object.Destroy(templates);
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

    static T Styled<T>(T text) where T : UniTextBase
    {
        text.SetWholeText<StrokeModifier>();
        text.SetWholeText<ShadowModifier>();
        return text;
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
/// <remarks>A scenario named <c>X.tmp</c> is the TMP twin of <c>X</c>: the same workload drawn by TMP.</remarks>
static class UniTextScenarios
{
    public const string MemoryId = "memory.cycles";
    public const string HiddenMemoryId = "memory.hidden";
    public const string StartupId = "startup";
    public const string IdleId = "idle";

    const int PageColumns = 4;
    const int PageRows = 30;
    const float PageFontSize = 28f;

    static int countersShown;

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

    public static List<UniTextScenario> Create()
    {
        var scenarios = Catalogue();
        scenarios.AddRange(UniTextDocumentScenarios.Create());
#if UNITEXT_3D
        scenarios.AddRange(UniText3DScenarios.Create());
#endif
        return scenarios;
    }

    static List<UniTextScenario> Catalogue() => new()
    {
        new IdleScenario(IdleId),
        new ScreenScenario("screen", ScreenStyle.Base),
        new ScreenScenario("screen.tmp", ScreenStyle.Base, ScenarioEngine.Tmp),
        new ScreenScenario("screen.default", ScreenStyle.Base, defaultMaterial: true),
        new ScreenScenario("screen.outline", ScreenStyle.StrokeOnly),
        new ScreenScenario("screen.shadow", ScreenStyle.ShadowOnly),
        new ScreenScenario("screen.glow", ScreenStyle.Glow),
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
        new RebuildScenario("rebuild.multilingual.st", RebuildContent.Multilingual, false),
        new TypingScenario("typing.end.short", 64, false),
        new TypingScenario("typing.end.2k", 2_000, false),
        new TypingScenario("typing.middle.2k", 2_000, true),
        new TypingScenario("typing.end.20k", 20_000, false),
        new TypingScenario("typing.middle.20k", 20_000, true),
        new ScrollScenario("scroll.static", false),
        new ScrollScenario("scroll.recycle", true),
        new ChurnScenario("churn", ScenarioEngine.UniText),
        new ChurnScenario("churn.tmp", ScenarioEngine.Tmp),
        new HudScenario("hud.toggle", HudKind.Toggle, ScenarioEngine.UniText),
        new HudScenario("hud.toggle.tmp", HudKind.Toggle, ScenarioEngine.Tmp),
        new HudScenario("hud.color", HudKind.Color, ScenarioEngine.UniText),
        new HudScenario("hud.color.tmp", HudKind.Color, ScenarioEngine.Tmp),
        new PagesScenario("pages.swipe", true, ScenarioEngine.UniText),
        new PagesScenario("pages.swipe.tmp", true, ScenarioEngine.Tmp),
        new PagesScenario("pages.offscreen", false, ScenarioEngine.UniText),
        new PagesScenario("pages.offscreen.tmp", false, ScenarioEngine.Tmp),
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

    /// <summary>
    /// Labels of <paramref name="engine"/> filling a <paramref name="columns"/> × <paramref name="rows"/> grid of
    /// <paramref name="root"/>, cycling through <paramref name="content"/>.
    /// </summary>
    public static MaskableGraphic[] LabelGrid(ScenarioRig rig, ScenarioEngine engine, RectTransform root,
        string[] content, int columns, int rows, float fontSize)
    {
        var labels = new MaskableGraphic[columns * rows];
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i] = rig.CreateLabel(engine, root, content[i % content.Length], fontSize);
            PlaceCell(labels[i], i, columns, rows);
        }
        return labels;
    }

    /// <summary>Fills <paramref name="page"/> with the shop-row labels of page <paramref name="index"/>, four to a row.</summary>
    public static MaskableGraphic[] FillPage(ScenarioRig rig, ScenarioEngine engine, RectTransform page, int index) =>
        LabelGrid(rig, engine, page, Labels(PageColumns * PageRows, index * PageColumns * PageRows),
            PageColumns, PageRows, PageFontSize);

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
        var checkedTexts = 0;
        foreach (var text in root.GetComponentsInChildren<UniTextBase>())
        {
            if (!text.isActiveAndEnabled || text.RenderedText.Length == 0) continue;
            checkedTexts++;
            if (text.GlyphCount == 0)
                throw new InvalidOperationException($"'{text.name}' holds text but produced no glyphs.");
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>())
        {
            if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
            checkedTexts++;
            if (text.textInfo.characterCount == 0)
                throw new InvalidOperationException($"'{text.name}' holds text but produced no glyphs.");
        }
        if (checkedTexts == 0)
            throw new InvalidOperationException("The scenario shows no text.");
    }

    /// <summary>
    /// Writes the engine with its effects (UniText's whole-text styles; TMP's font, material and shader), the text
    /// count, the characters and glyphs they currently hold, the font size and how many change per frame.
    /// </summary>
    /// <remarks>The effects are read from the last text, which no scenario switches off.</remarks>
    static void DescribeTexts(JObject workload, Component root, MaskableGraphic[] texts, float fontSize, int changing)
    {
        var characters = 0;
        foreach (var text in texts)
            characters += text is UniTextBase uniText ? uniText.RenderedText.Length : ((TMP_Text)text).text.Length;
        if (texts[texts.Length - 1] is TMP_Text tmp)
        {
            workload["engine"] = "TMP";
            workload["tmpFont"] = tmp.font.name;
            workload["tmpMaterial"] = tmp.fontSharedMaterial.name;
            workload["tmpShader"] = tmp.fontSharedMaterial.shader.name;
        }
        else
        {
            var effects = new JArray();
            foreach (var style in ((UniTextBase)texts[texts.Length - 1]).GetStylesOfType<BaseModifier>())
                if (UniTextBase.IsWholeTextStyle(style))
                    effects.Add(style.Modifier.GetType().Name);
            workload["engine"] = "UniText";
            workload["effects"] = effects;
        }
        workload["texts"] = texts.Length;
        workload["characters"] = characters;
        workload["glyphs"] = CountGlyphs(root);
        workload["fontSize"] = fontSize;
        workload["changingTexts"] = changing;
    }

    /// <summary>Glyphs the active texts under <paramref name="root"/> laid out: UniText's positioned glyphs and TMP's characters.</summary>
    public static int CountGlyphs(Component root)
    {
        var glyphs = 0;
        foreach (var text in root.GetComponentsInChildren<UniTextBase>())
            glyphs += text.GlyphCount;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>())
            glyphs += text.textInfo.characterCount;
        return glyphs;
    }

    /// <summary>Gives <paramref name="label"/> the whole of <paramref name="text"/> through its engine's character-buffer setter.</summary>
    static void SetLabel(MaskableGraphic label, char[] text)
    {
        if (label is UniText uniText) uniText.SetText(text, 0, text.Length);
        else ((TMP_Text)label).SetText(text, 0, text.Length);
    }

    /// <summary>HUD-like labels; label <c>n</c> reads the same in every call, so <paramref name="first"/> continues a sequence.</summary>
    public static string[] Labels(int count, int first = 0)
    {
        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            var n = first + i;
            labels[i] = $"{Words[n % Words.Length]} {Words[(n * 7 + 3) % Words.Length].ToLowerInvariant()} {n * 37 % 1000:000}";
        }
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

    /// <summary>Writes <paramref name="value"/> right-aligned into <paramref name="buffer"/> from <paramref name="digitsStart"/> up to <paramref name="digitsEnd"/>, padding with zeros.</summary>
    public static void WriteDigits(char[] buffer, int digitsStart, int digitsEnd, int value)
    {
        for (var i = digitsEnd - 1; i >= digitsStart; i--)
        {
            buffer[i] = (char)('0' + value % 10);
            value /= 10;
        }
    }

    /// <summary>The next number of the sequence every counter of the process draws from.</summary>
    /// <remarks>
    /// The sequence repeats no number in its first ten million, so no counter shows a number UniText's word cache
    /// saw before, as a real score or timer never does.
    /// </remarks>
    static int NextCounterValue() => (int)(countersShown++ * 7919L % 10_000_000);

    /// <summary>
    /// The same camera and canvas with no text: the frames show what the player and the harness allocate and spend
    /// by themselves, the floor every other scenario's figures sit on.
    /// </summary>
    sealed class IdleScenario : UniTextScenario
    {
        public IdleScenario(string id) : base(id, true) { }

        public override void Setup(ScenarioRig rig) => rig.CreateRoot(Id);

        public override void Validate() { }

        public override void Describe(JObject workload) => workload["texts"] = 0;
    }

    enum ScreenStyle
    {
        Base,
        StrokeOnly,
        ShadowOnly,
        Glow,
        Decorations,
        Msdf,
        Large,
        Multilingual,
        Emoji
    }

    /// <summary>
    /// A static screen full of text with the rig's stroke and shadow; nothing changes, so the frame shows the idle cost
    /// and the GPU cost of the style. A style variant drops one of the two effects or adds one to them. The
    /// default-material variant draws every renderer with the default canvas material once the meshes exist, so its
    /// GPU time is the geometry's and the difference to its source is the text shader's.
    /// </summary>
    sealed class ScreenScenario : UniTextScenario
    {
        const int FirstSwapFrame = 12;
        const int LastSwapFrame = 20;

        readonly ScreenStyle style;
        readonly ScenarioEngine engine;
        readonly bool defaultMaterial;
        RectTransform root;
        MaskableGraphic[] texts;
        float fontSize;
        int swapped;

        public ScreenScenario(string id, ScreenStyle style, ScenarioEngine engine = ScenarioEngine.UniText,
            bool defaultMaterial = false) : base(id, true)
        {
            this.style = style;
            this.engine = engine;
            this.defaultMaterial = defaultMaterial;
        }

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

            texts = LabelGrid(rig, engine, root, content, columns, rows, fontSize);
            if (engine != ScenarioEngine.UniText) return;
            foreach (UniText text in texts)
                ApplyStyle(text);
        }

        public override void Step(int frame)
        {
            if (!defaultMaterial || frame is not (FirstSwapFrame or LastSwapFrame)) return;
            var material = Canvas.GetDefaultCanvasMaterial();
            foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>())
                for (var i = 0; i < renderer.materialCount; i++)
                {
                    if (renderer.GetMaterial(i) == material) continue;
                    renderer.SetMaterial(material, i);
                    swapped++;
                }
        }

        void ApplyStyle(UniText text)
        {
            switch (style)
            {
                case ScreenStyle.StrokeOnly:
                    text.ClearWholeText<ShadowModifier>();
                    break;
                case ScreenStyle.ShadowOnly:
                    text.ClearWholeText<StrokeModifier>();
                    break;
                case ScreenStyle.Glow:
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

        public override void Validate()
        {
            RequireGlyphs(root);
            if (swapped == 0) return;
            var material = Canvas.GetDefaultCanvasMaterial();
            foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>())
                for (var i = 0; i < renderer.materialCount; i++)
                    if (renderer.GetMaterial(i) != material)
                        throw new InvalidOperationException($"'{renderer.name}' draws with its own material again.");
        }

        public override void Describe(JObject workload)
        {
            if (defaultMaterial && swapped == 0)
                throw new InvalidOperationException("No renderer was switched to the default canvas material.");
            DescribeTexts(workload, root, texts, fontSize, 0);
            workload["style"] = style.ToString();
            if (defaultMaterial) workload["material"] = Canvas.GetDefaultCanvasMaterial().shader.name;
        }
    }

    /// <summary>Score-like labels whose number changes every frame, written in place through <see cref="UniTextBase.SetText(char[], int, int)"/>.</summary>
    sealed class CounterScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 16;
        const string Prefix = "Score ";
        const int Digits = 7;

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
                WriteDigits(buffers[i], Prefix.Length, buffers[i].Length, NextCounterValue());
                texts[i].SetText(buffers[i], 0, buffers[i].Length);
            }
        }

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
        readonly ScenarioEngine engine;
        ScenarioRig rig;
        RectTransform root;
        string[] pool;

        public ChurnScenario(string id, ScenarioEngine engine) : base(id, true) => this.engine = engine;

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
                var text = rig.CreateLabel(engine, root, pool[(frame * PerFrame + i) % pool.Length], 30f);
                PlaceCell(text, i, Columns, PerFrame / Columns);
                live.Add(text.gameObject);
            }
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            workload["engine"] = engine == ScenarioEngine.Tmp ? "TMP" : "UniText";
            workload["creation"] = engine == ScenarioEngine.Tmp
                ? "TextMeshProUGUI added to a new GameObject with font and material set before it wakes"
                : "instance of the styled Text (UniText) template";
            workload["createdPerFrame"] = PerFrame;
            workload["destroyedPerFrame"] = PerFrame;
            workload["fontSize"] = 30f;
        }

        public override void Teardown() => live.Clear();
    }

    enum HudKind
    {
        Toggle,
        Color
    }

    /// <summary>
    /// A HUD of twenty labels: the toggle variant switches six of them on and off every frame, the color variant gives
    /// every label a new score inside a <c>color</c> tag every frame, as a migrated TMP project writes it.
    /// </summary>
    sealed class HudScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 5;
        const int Toggled = 6;
        const string Prefix = "<color=#FFD700>";
        const string Suffix = "</color> pts";
        const int Digits = 7;
        const float FontSize = 36f;

        readonly HudKind kind;
        readonly ScenarioEngine engine;
        readonly char[][] buffers = new char[Columns * Rows][];
        MaskableGraphic[] labels;
        RectTransform root;

        public HudScenario(string id, HudKind kind, ScenarioEngine engine) : base(id, true)
        {
            this.kind = kind;
            this.engine = engine;
        }

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            if (kind == HudKind.Toggle)
            {
                labels = LabelGrid(rig, engine, root, Labels(Columns * Rows), Columns, Rows, FontSize);
                return;
            }

            var initial = Prefix + new string('0', Digits) + Suffix;
            for (var i = 0; i < buffers.Length; i++)
                buffers[i] = initial.ToCharArray();
            labels = LabelGrid(rig, engine, root, new[] { initial }, Columns, Rows, FontSize);
            if (engine == ScenarioEngine.UniText)
                foreach (UniText label in labels)
                    label.Styles.Add(Style.Tag(new ColorModifier(), "color"));
            ExpectCommitEveryFrame(labels[0]);
        }

        public override void Step(int frame)
        {
            if (kind == HudKind.Toggle)
            {
                for (var i = 0; i < Toggled; i++)
                    labels[i].gameObject.SetActive((frame & 1) == 0);
                return;
            }
            for (var i = 0; i < labels.Length; i++)
            {
                WriteDigits(buffers[i], Prefix.Length, Prefix.Length + Digits, NextCounterValue());
                SetLabel(labels[i], buffers[i]);
            }
        }

        public override void Validate()
        {
            RequireGlyphs(root);
            if (kind == HudKind.Toggle)
                for (var i = 0; i < Toggled; i++)
                    labels[i].gameObject.SetActive(true);
            if (kind != HudKind.Color) return;
            foreach (var label in labels)
            {
                var shown = label is UniTextBase uniText ? uniText.CleanText.ToString() : ((TMP_Text)label).GetParsedText();
                if (shown.IndexOf('<') >= 0)
                    throw new InvalidOperationException($"'{label.name}' draws its color markup as text.");
            }
        }

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, labels, FontSize, kind == HudKind.Toggle ? Toggled : labels.Length);
            workload["change"] = kind == HudKind.Toggle ? "SetActive" : "color-tagged counter, SetText(char[], int, int)";
        }
    }

    /// <summary>
    /// A horizontal pager of three full-screen pages, each a <see cref="RectMask2D"/> over 120 shop-row labels. The
    /// swipe slides the strip between the first two pages and back; the off-screen variant keeps the first page still
    /// while the third slides back and forth beyond the screen's edge.
    /// </summary>
    sealed class PagesScenario : UniTextScenario
    {
        const int Pages = 3;
        const float Period = 90f;

        readonly bool swipe;
        readonly ScenarioEngine engine;
        readonly RectTransform[] pages = new RectTransform[Pages];
        readonly List<MaskableGraphic> labels = new();
        RectTransform canvas;
        RectTransform root;
        RectTransform strip;

        public PagesScenario(string id, bool swipe, ScenarioEngine engine) : base(id, true)
        {
            this.swipe = swipe;
            this.engine = engine;
        }

        public override void Setup(ScenarioRig rig)
        {
            canvas = rig.CanvasRect;
            root = rig.CreateRoot(Id);
            strip = Panel("Strip", root, 0f);
            for (var i = 0; i < Pages; i++)
            {
                pages[i] = Panel($"Page {i}", strip, i);
                pages[i].gameObject.AddComponent<RectMask2D>();
                labels.AddRange(FillPage(rig, engine, pages[i], i));
            }
        }

        static RectTransform Panel(string name, RectTransform parent, float pageOffset)
        {
            var rect = new GameObject(name, typeof(RectTransform)) { layer = 5 }.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(pageOffset, 0f);
            rect.anchorMax = new Vector2(pageOffset + 1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public override void Step(int frame)
        {
            var phase = frame * (2f * Mathf.PI / Period);
            var width = canvas.rect.width;
            if (swipe)
                strip.anchoredPosition = new Vector2(-width * (0.5f - 0.5f * Mathf.Cos(phase)), 0f);
            else
                pages[2].anchoredPosition = new Vector2(width * (0.45f * Mathf.Sin(phase) - 0.45f), 0f);
        }

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeTexts(workload, root, labels.ToArray(), PageFontSize, 0);
            workload["masks"] = Pages;
            workload["motion"] = swipe
                ? "strip between pages 0 and 1"
                : "page 0 still, page 2 sliding 1.1-2 screens right";
        }
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
                        WriteDigits(buffers[i], Prefix.Length, buffers[i].Length, (frame * 31 + i * 977) % 10_000);
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
