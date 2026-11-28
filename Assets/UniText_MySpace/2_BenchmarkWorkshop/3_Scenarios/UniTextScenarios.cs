using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LightSide;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>The engine and look an arm draws its labels with.</summary>
enum ScenarioEngine
{
    UniText,
    UniTextPlain,

    /// <summary>UniText with the house effects merged into one quad per glyph where glyphs do not touch (glyph-major paint order).</summary>
    UniTextMerged,

    /// <summary>TMP with the outline and underlay material preset, every glyph drawing its own effects in one quad.</summary>
    Tmp,

    TmpPlain,

    /// <summary>
    /// TMP drawing every shadow and outline under every face: on a Canvas three texts per label in three sibling
    /// containers (every shadow, then every outline, then every face), in world space one text with the two-pass material.
    /// </summary>
    TmpLayered
}

/// <summary>Type steps of the bench's visual spec, in units of the 1080 × 1920 reference canvas.</summary>
static class TypeStep
{
    public const float Dense = 28f;
    public const float Body = 32f;
    public const float Label = 36f;
    public const float Number = 44f;
    public const float Title = 72f;

    /// <summary>Height of a one-line text box of <paramref name="fontSize"/>: the scenario fonts' line height with a margin.</summary>
    public static float Line(float fontSize) => fontSize * 1.4f;
}

/// <summary>One arm of a <see cref="ScenarioCase"/>: a workload built once, then shown, driven every frame and hidden by the suite.</summary>
/// <remarks><see cref="Step"/> runs inside measured windows and must not allocate beyond the text engine call it exercises.</remarks>
abstract class UniTextScenario
{
    readonly Action<Object> countTmpCommit;
    readonly List<GameObject> roots = new();
    ScenarioRig rig;
    UniTextBase committing;
    TMP_Text committingTmp;
    bool listeningToTmp;
    int commits;

    protected UniTextScenario(string id, bool parallel, ScenarioEngine engine = ScenarioEngine.UniText)
    {
        Id = id;
        Parallel = parallel;
        TextEngine = engine;
        countTmpCommit = CountTmpCommit;
    }

    public string Id { get; }

    /// <summary>Value of <see cref="UniTextBase.UseParallel"/> while the arm is active.</summary>
    public bool Parallel { get; }

    protected ScenarioEngine TextEngine { get; }

    /// <summary>Size of the workload in the case's own unit: the labels that change per frame, else the labels or objects on screen.</summary>
    protected abstract int Load { get; }

    /// <summary>Whether the arm draws its texts with the default canvas material: the control of the GPU counter.</summary>
    protected virtual bool Baseline => false;

    /// <summary>The lowercase name of the implementation the arm measures: <c>unitext</c> or <c>tmp</c> for text arms, the package's own name otherwise.</summary>
    protected virtual string EngineFamily =>
        TextEngine is ScenarioEngine.Tmp or ScenarioEngine.TmpPlain or ScenarioEngine.TmpLayered ? "tmp" : "unitext";

    /// <summary>
    /// What tells the arm apart from arms of its case that the other descriptor keys describe alike, as dot-joined
    /// camelCase words (<c>tmpLayered</c>, <c>merged</c>, an effect, motion or mask); null when nothing needs telling apart.
    /// </summary>
    protected virtual string Variant => TextEngine switch
    {
        ScenarioEngine.TmpLayered => "tmpLayered",
        ScenarioEngine.UniTextMerged => "merged",
        _ => null
    };

    /// <summary>False where the arm draws something other than the case's first arm does, as TMP does on scripts it cannot shape.</summary>
    protected virtual bool Comparable => true;

    /// <summary>
    /// Writes the arm's descriptor for the reducers: <c>engine</c>, <c>threads</c> (<c>parallel</c> or <c>single</c>
    /// for UniText, null for TMP), <c>style</c> (<c>styled</c> or <c>plain</c>), <c>load</c>, <c>baseline</c>, and
    /// <c>variant</c> and <c>comparable</c> = false where they apply.
    /// </summary>
    public void DescribeArm(JObject arm)
    {
        var family = EngineFamily;
        arm["engine"] = family;
        if (family == "unitext") arm["threads"] = Parallel ? "parallel" : "single";
        else arm["threads"] = JValue.CreateNull();
        arm["style"] = TextEngine is ScenarioEngine.UniTextPlain or ScenarioEngine.TmpPlain ? "plain" : "styled";
        arm["load"] = Load;
        arm["baseline"] = Baseline;
        var variant = Variant;
        if (variant != null) arm["variant"] = variant;
        if (!Comparable) arm["comparable"] = false;
    }

    /// <summary>The camelCase name of <paramref name="value"/>, a variant word.</summary>
    protected static string VariantWord(Enum value)
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>The variant words that apply, dot-joined; null when none does.</summary>
    protected static string JoinVariant(string first, string second) =>
        first == null ? second : second == null ? first : first + "." + second;

    /// <summary>Builds the arm under containers of its own; other arms of the same case may already stand in <paramref name="rig"/>.</summary>
    public void Setup(ScenarioRig rig)
    {
        this.rig = rig;
        rig.BeginBuild(roots);
        try
        {
            Build(rig);
        }
        finally
        {
            rig.EndBuild();
        }
    }

    protected abstract void Build(ScenarioRig rig);

    /// <summary>
    /// Shows or hides everything the arm built. An inactive arm neither updates, rebuilds nor draws; an activated arm's
    /// canvas streams only the shader channels its own texts ask for.
    /// </summary>
    public virtual void SetActive(bool active)
    {
        if (active) rig.ResetCanvasChannels();
        foreach (var root in roots)
            if (root != null)
                root.SetActive(active);
        ListenToTmp(active);
    }

    /// <summary>Whether the suite records the memory <see cref="Setup"/> leaves held, settling garbage before and after it.</summary>
    public virtual bool MeasuresFootprint => false;

    /// <summary>Profiler markers whose time in each measured frame the suite records; markers exist only in the editor and development players.</summary>
    public virtual string[] Markers => Array.Empty<string>();

    /// <summary>Advances the workload one frame; <see cref="UniTextScenarioSuite.StillFrame"/> shows the fixed content every arm of the case shares.</summary>
    public virtual void Step(int frame) { }

    /// <summary>Throws when the workload did not actually run or its layout broke.</summary>
    public abstract void Validate();

    /// <summary>Writes what the arm draws; reads the laid-out texts, so it runs while the arm is active.</summary>
    public abstract void Describe(JObject workload);

    public virtual void Teardown() { }

    public void ResetCommits() => commits = 0;

    /// <summary>Called before the first measured frame of a slot: counters of the scenario's own start here.</summary>
    public virtual void MeasuringStarts() { }

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
        ListenToTmp(false);
        committing = null;
        committingTmp = null;
    }

    /// <summary>Declares that <paramref name="text"/> changes every frame, so a slot must see it reprocess every frame.</summary>
    protected void ExpectCommitEveryFrame(MaskableGraphic text)
    {
        if (text is UniTextBase uniText)
        {
            committing = uniText;
            uniText.LayoutCommitted += CountCommit;
            return;
        }
        committingTmp = (TMP_Text)text;
        ListenToTmp(true);
    }

    /// <summary>
    /// Subscribes the commit count to TMP's process-wide text-changed event only while the arm is shown: TMP raises it for
    /// every regenerated text in players too, so a listener of a hidden arm would charge its work to the active TMP arm.
    /// </summary>
    void ListenToTmp(bool listen)
    {
        if (committingTmp == null || listeningToTmp == listen) return;
        listeningToTmp = listen;
        if (listen) TMPro_EventManager.TEXT_CHANGED_EVENT.Add(countTmpCommit);
        else TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(countTmpCommit);
    }

    void CountCommit() => commits++;

    void CountTmpCommit(Object changed)
    {
        if (changed == committingTmp) commits++;
    }
}

/// <summary>One label of an arm: a UniText text, a TMP text, or the TMP texts that draw one effect layer each.</summary>
sealed class ScenarioLabel
{
    readonly UniTextBase uniText;
    readonly TMP_Text[] layers;
    readonly RectTransform[] rects;

    public ScenarioLabel(UniTextBase uniText)
    {
        this.uniText = uniText;
        rects = new[] { uniText.rectTransform };
    }

    /// <summary>A TMP label of <paramref name="layers"/>, bottom to top, all showing the same text in the same rect.</summary>
    public ScenarioLabel(TMP_Text[] layers)
    {
        this.layers = layers;
        rects = new RectTransform[layers.Length];
        for (var i = 0; i < layers.Length; i++)
            rects[i] = layers[i].rectTransform;
    }

    /// <summary>The rect of the text drawn last; the other layers of a layered label follow only the placement and activation methods.</summary>
    public RectTransform Rect => rects[rects.Length - 1];

    /// <summary>The rects of every layer, bottom to top.</summary>
    public IReadOnlyList<RectTransform> Rects => rects;

    public UniTextBase UniText => uniText;

    /// <summary>Places every layer as <see cref="ScenarioRig.Place"/> does.</summary>
    public void Place(float x, float top, float width, float height)
    {
        foreach (var rect in rects)
            ScenarioRig.Place(rect, x, top, width, height);
    }

    /// <summary>Places every layer as <see cref="ScenarioRig.PlaceFromBottom"/> does.</summary>
    public void PlaceFromBottom(float x, float bottom, float width, float height)
    {
        foreach (var rect in rects)
            ScenarioRig.PlaceFromBottom(rect, x, bottom, width, height);
    }

    public void SetActive(bool active)
    {
        foreach (var rect in rects)
            rect.gameObject.SetActive(active);
    }

    /// <summary>The TMP texts bottom to top; empty for a UniText label.</summary>
    public IReadOnlyList<TMP_Text> TmpLayers => layers ?? Array.Empty<TMP_Text>();

    /// <summary>The text that stands for the label in commits, glyph and line counts: the UniText, or the TMP text drawn last.</summary>
    public MaskableGraphic Graphic => uniText != null ? uniText : layers[layers.Length - 1];

    public void SetText(char[] text)
    {
        if (uniText != null)
        {
            uniText.SetText(text, 0, text.Length);
            return;
        }
        for (var i = 0; i < layers.Length; i++)
            layers[i].SetText(text, 0, text.Length);
    }

    public void SetText(string text)
    {
        if (uniText != null)
        {
            uniText.SetText(text);
            return;
        }
        for (var i = 0; i < layers.Length; i++)
            layers[i].text = text;
    }

    /// <summary>Aligns the label in its rect horizontally and centres it vertically; TMP has no paragraph direction, so start is left there.</summary>
    public void Align(HorizontalAlignment alignment)
    {
        if (uniText != null)
        {
            uniText.HorizontalAlignment = alignment;
            uniText.VerticalAlignment = VerticalAlignment.Middle;
            return;
        }
        var options = alignment switch
        {
            HorizontalAlignment.Center => TextAlignmentOptions.Midline,
            HorizontalAlignment.End => TextAlignmentOptions.MidlineRight,
            _ => TextAlignmentOptions.MidlineLeft
        };
        for (var i = 0; i < layers.Length; i++)
            layers[i].alignment = options;
    }

    public int Characters => uniText != null ? uniText.RenderedText.Length : layers[layers.Length - 1].text.Length;

    /// <summary>Positioned glyphs of UniText; characters TMP laid out.</summary>
    public int Glyphs => uniText != null ? uniText.GlyphCount : layers[layers.Length - 1].textInfo.characterCount;

    /// <remarks>UniText's lines are read after <see cref="UniTextBase.GlyphCount"/>, which restores the layout an idle text returned.</remarks>
    public int Lines
    {
        get
        {
            if (uniText == null) return layers[layers.Length - 1].textInfo.lineCount;
            return uniText.GlyphCount > 0 ? uniText.Buffers.lines.count : 0;
        }
    }
}

/// <summary>The camera, canvas, world root and text templates every arm builds under, and the containers it built.</summary>
/// <remarks>
/// Every label is a copy of a template, as a prefab instance is. UniText templates carry the house effects (or none for
/// the plain look) and are built at the first label, never earlier: styling a template initializes UniText, which the
/// first-screen measurement pays. TMP templates never wake, so each copy wakes with TMP Settings' defaults before its
/// size and text are set. 3D text keeps its own layers.
/// </remarks>
sealed class ScenarioRig : IDisposable
{
    public const float ReferenceWidth = 1080f;
    public const float ReferenceHeight = 1920f;

    /// <summary>Width of the content area: the 960-unit safe frame minus 48-unit side margins; every test phone's canvas is at least 961 units wide.</summary>
    public const float ContentWidth = 864f;

    /// <summary>Height of the content area on the shortest test canvas (16:9).</summary>
    public const float ContentHeight = ReferenceHeight - TopMargin - BottomMargin;

    /// <summary>Size of a world label in the world text prefab's units: one line of a long label at 36 points, no wrapping.</summary>
    public static readonly Vector2 NameplateSize = new(400f, 60f);

    const float TopMargin = 120f;
    const float BottomMargin = 80f;
    const float TmpWorldUnitsPerPoint = 0.1f;
    const string TwoPassShader = "BenchmarkTmpTwoPass";

    static readonly string[] LayerContainerNames = { "TMP shadows", "TMP outlines", "TMP faces" };

    readonly UniTextFontStack fontStack;
    readonly TMP_FontAsset tmpFont;
    readonly Material tmpMaterial;
    readonly GameObject textPrefab;
    readonly GameObject worldTextPrefab;
    readonly GameObject inputFieldPrefab;
    readonly GameObject cameraObject;
    readonly GameObject canvasObject;
    readonly Canvas canvas;
    readonly GameObject templates;
    readonly List<GameObject> roots = new();
    readonly List<Material> materials = new();
    readonly Dictionary<ScenarioEngine, GameObject> canvasTemplates = new();
    readonly Dictionary<ScenarioEngine, GameObject> worldTemplates = new();
    readonly Dictionary<Transform, RectTransform[]> layerContainers = new();
    List<GameObject> building;
    Material houseTmpMaterial;
    GameObject[] layerTemplates;

    public ScenarioRig(UniTextFontStack fontStack, TMP_FontAsset tmpFont, Material tmpMaterial,
        GameObject textPrefab, GameObject worldTextPrefab, GameObject inputFieldPrefab, Material particleMaterial)
    {
        ParticleMaterial = particleMaterial;
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
        Camera.backgroundColor = LookCalibrationProfile.Background;
        Camera.fieldOfView = 60f;
        Camera.nearClipPlane = 0.3f;
        Camera.farClipPlane = 100f;

        canvasObject = new GameObject("ScenarioCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler))
        {
            layer = 5
        };
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Camera;
        canvas.planeDistance = 5f;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.matchWidthOrHeight = 0.5f;
        CanvasRect = (RectTransform)canvasObject.transform;
    }

    public Camera Camera { get; }

    public RectTransform CanvasRect { get; }

    /// <summary>The render pipeline's default particle material, the one a new <c>ParticleSystem</c> gets.</summary>
    public Material ParticleMaterial { get; }

    /// <summary>A full-canvas container destroyed by <see cref="Clear"/>.</summary>
    public RectTransform CreateRoot(string name)
    {
        var root = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        Register(root);
        var rect = (RectTransform)root.transform;
        rect.SetParent(CanvasRect, false);
        Stretch(rect);
        return rect;
    }

    /// <summary>The content area of a new full-canvas container destroyed by <see cref="Clear"/>.</summary>
    public RectTransform CreateContent(string name) => Content(CreateRoot(name));

    /// <summary>
    /// The content area inside <paramref name="screen"/>, a rect spanning the canvas: <see cref="ContentWidth"/> wide and
    /// centred, below a 120-unit top and above an 80-unit bottom margin. Its children are placed from its top-left corner.
    /// </summary>
    public static RectTransform Content(RectTransform screen)
    {
        var rect = new GameObject("Content", typeof(RectTransform)) { layer = 5 }.GetComponent<RectTransform>();
        rect.SetParent(screen, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(-ContentWidth * 0.5f, BottomMargin);
        rect.offsetMax = new Vector2(ContentWidth * 0.5f, -TopMargin);
        return rect;
    }

    /// <summary>A world-space container destroyed by <see cref="Clear"/>.</summary>
    public Transform CreateWorldRoot(string name)
    {
        var root = new GameObject(name);
        Register(root);
        return root.transform;
    }

    /// <summary>A UniText of the default prefab with the house effects, aligned as the prefab is.</summary>
    public UniText CreateText(RectTransform parent, string text, float fontSize) =>
        (UniText)Configure(Object.Instantiate(CanvasTemplate(ScenarioEngine.UniText), parent, false), text, fontSize).UniText;

    /// <summary>
    /// A label of <paramref name="engine"/>, a copy of its template, start-aligned and vertically centred. A layered TMP
    /// label puts one copy of each layer's template into the matching container of <paramref name="parent"/>, so every
    /// shadow under the parent draws before every outline and every outline before every face, in three batches.
    /// </summary>
    public ScenarioLabel CreateLabel(ScenarioEngine engine, RectTransform parent, string text, float fontSize)
    {
        ScenarioLabel label;
        if (engine == ScenarioEngine.TmpLayered)
        {
            var containers = LayerContainers(parent);
            var sources = LayerTemplates();
            var layers = new TMP_Text[sources.Length];
            for (var i = 0; i < layers.Length; i++)
                layers[i] = Object.Instantiate(sources[i], containers[i], false).GetComponent<TMP_Text>();
            label = ConfigureTmp(layers, text, fontSize);
        }
        else label = Configure(Object.Instantiate(CanvasTemplate(engine), parent, false), text, fontSize);
        label.Align(HorizontalAlignment.Start);
        return label;
    }

    /// <summary>
    /// A world label of <paramref name="engine"/>, a copy of its template, centred in a <see cref="NameplateSize"/> rect at
    /// the same world em for every engine: UniText's world prefab draws a point as one local unit at a 0.01 scale, TMP's
    /// world text a point as 0.1 local units, so TMP's rect is scaled to match.
    /// </summary>
    public ScenarioLabel CreateWorldLabel(ScenarioEngine engine, Transform parent, string text, float fontSize)
    {
        var label = Configure(Object.Instantiate(WorldTemplate(engine), parent, false), text, fontSize);
        label.Align(HorizontalAlignment.Center);
        return label;
    }

#if UNITEXT_3D
    /// <summary>Extruded text made as GameObject > LightSide > UniText > Text 3D makes it: a 400 × 100 rect at a 0.01 scale, centered.</summary>
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
        uniText.FontStack = fontStack;
        uniText.FontSize = fontSize;
        uniText.color = Color.white;
        uniText.Text = text;
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
        Styled(editable.TextComponent, ScenarioEngine.UniText);
        foreach (var text in editable.GetComponentsInChildren<UniTextBase>(true))
            text.FontStack = fontStack;
        editable.TextComponent.FontSize = fontSize;
        editable.TextComponent.color = Color.white;
        return editable;
    }

    /// <summary>Takes every additional shader channel off the canvas; the texts that become active add back the ones they ask for.</summary>
    public void ResetCanvasChannels() => canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

    /// <summary>Starts collecting the containers created from now on into <paramref name="scenarioRoots"/>.</summary>
    public void BeginBuild(List<GameObject> scenarioRoots) => building = scenarioRoots;

    public void EndBuild() => building = null;

    /// <summary>Destroys every container built so far and takes the canvas's additional shader channels off.</summary>
    public void Clear()
    {
        foreach (var root in roots)
            if (root != null)
                Object.Destroy(root);
        roots.Clear();
        layerContainers.Clear();
        ResetCanvasChannels();
    }

    public void Dispose()
    {
        Clear();
        Object.Destroy(canvasObject);
        Object.Destroy(cameraObject);
        Object.Destroy(templates);
        foreach (var material in materials)
            Object.Destroy(material);
        materials.Clear();
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Places <paramref name="rect"/> at <paramref name="x"/>, <paramref name="top"/> from its parent's top-left corner.</summary>
    public static void Place(RectTransform rect, float x, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>Places <paramref name="rect"/> at <paramref name="x"/>, <paramref name="bottom"/> from its parent's bottom-left corner.</summary>
    public static void PlaceFromBottom(RectTransform rect, float x, float bottom, float width, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, bottom);
        rect.sizeDelta = new Vector2(width, height);
    }

    void Register(GameObject root)
    {
        roots.Add(root);
        building?.Add(root);
    }

    ScenarioLabel Configure(GameObject copy, string text, float fontSize)
    {
        var uniText = copy.GetComponent<UniTextBase>();
        if (uniText != null)
        {
            uniText.FontStack = fontStack;
            uniText.FontSize = fontSize;
            uniText.color = Color.white;
            uniText.Text = text;
            return new ScenarioLabel(uniText);
        }
        return ConfigureTmp(new[] { copy.GetComponent<TMP_Text>() }, text, fontSize);
    }

    static ScenarioLabel ConfigureTmp(TMP_Text[] layers, string text, float fontSize)
    {
        foreach (var layer in layers)
        {
            layer.fontSize = fontSize;
            layer.color = Color.white;
            layer.text = text;
        }
        return new ScenarioLabel(layers);
    }

    GameObject CanvasTemplate(ScenarioEngine engine)
    {
        if (canvasTemplates.TryGetValue(engine, out var template)) return template;
        template = engine switch
        {
            ScenarioEngine.Tmp => TmpTemplate<TextMeshProUGUI>("Text (TMP)", HouseTmpMaterial(), templates.transform),
            ScenarioEngine.TmpPlain => TmpTemplate<TextMeshProUGUI>("Text (TMP)", tmpFont.material, templates.transform),
            ScenarioEngine.UniText or ScenarioEngine.UniTextPlain or ScenarioEngine.UniTextMerged =>
                Styled(Instantiate<UniText>(textPrefab, templates.transform, "Text"), engine).gameObject,
            _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Layered TMP labels have one template per layer.")
        };
        canvasTemplates[engine] = template;
        return template;
    }

    GameObject WorldTemplate(ScenarioEngine engine)
    {
        if (worldTemplates.TryGetValue(engine, out var template)) return template;
        template = engine switch
        {
            ScenarioEngine.Tmp => TmpWorldTemplate(HouseTmpMaterial()),
            ScenarioEngine.TmpPlain => TmpWorldTemplate(tmpFont.material),
            ScenarioEngine.TmpLayered => TmpWorldTemplate(TwoPassMaterial()),
            _ => UniTextWorldTemplate(engine)
        };
        worldTemplates[engine] = template;
        return template;
    }

    GameObject UniTextWorldTemplate(ScenarioEngine engine)
    {
        var text = Styled(Instantiate<UniTextWorld>(worldTextPrefab, templates.transform, "World Text"), engine);
        text.rectTransform.sizeDelta = NameplateSize;
        return text.gameObject;
    }

    GameObject TmpWorldTemplate(Material material)
    {
        var template = TmpTemplate<TextMeshPro>("Text (TMP)", material, templates.transform);
        var rect = (RectTransform)template.transform;
        rect.sizeDelta = NameplateSize * TmpWorldUnitsPerPoint;
        rect.localScale = worldTextPrefab.transform.localScale / TmpWorldUnitsPerPoint;
        return template;
    }

    /// <summary>A TMP text that never wakes on its own: font and material are set as a prefab carries them, the size stays unset so each copy takes TMP Settings' defaults.</summary>
    GameObject TmpTemplate<T>(string name, Material material, Transform parent) where T : TMP_Text
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<T>();
        text.font = tmpFont;
        text.fontSharedMaterial = material;
        return go;
    }

    /// <summary>
    /// Templates of a layered label's texts, bottom to top: the shadow (the house material with transparent face and
    /// outline), the outline band (transparent face, no underlay) and the face (the font's default material).
    /// </summary>
    GameObject[] LayerTemplates()
    {
        if (layerTemplates != null) return layerTemplates;
        var house = HouseTmpMaterial();
        var shadow = new Material(house) { name = house.name + " Shadow" };
        shadow.SetColor(ShaderUtilities.ID_FaceColor, Color.clear);
        shadow.SetColor(ShaderUtilities.ID_OutlineColor, Color.clear);
        materials.Add(shadow);
        var outline = new Material(house) { name = house.name + " Outline" };
        outline.SetColor(ShaderUtilities.ID_FaceColor, Color.clear);
        outline.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        materials.Add(outline);
        layerTemplates = new[]
        {
            TmpTemplate<TextMeshProUGUI>("Text (TMP shadow)", shadow, templates.transform),
            TmpTemplate<TextMeshProUGUI>("Text (TMP outline)", outline, templates.transform),
            TmpTemplate<TextMeshProUGUI>("Text (TMP face)", tmpFont.material, templates.transform)
        };
        return layerTemplates;
    }

    /// <summary>The shadow, outline and face containers of layered labels under <paramref name="parent"/>, each spanning it, created at its first layered label.</summary>
    RectTransform[] LayerContainers(RectTransform parent)
    {
        if (layerContainers.TryGetValue(parent, out var containers)) return containers;
        containers = new RectTransform[LayerContainerNames.Length];
        for (var i = 0; i < containers.Length; i++)
        {
            var container = new GameObject(LayerContainerNames[i], typeof(RectTransform)) { layer = 5 }
                .GetComponent<RectTransform>();
            container.SetParent(parent, false);
            Stretch(container);
            containers[i] = container;
        }
        layerContainers.Add(parent, containers);
        return containers;
    }

    /// <summary>
    /// The TMP material that draws the house look, made from the outline and underlay preset at the first TMP label, so
    /// that TMP's first use in the process stays inside the first-screen measurement.
    /// </summary>
    Material HouseTmpMaterial()
    {
        if (houseTmpMaterial != null) return houseTmpMaterial;
        houseTmpMaterial = LookCalibrationProfile.CreateTmpMaterial(tmpMaterial, tmpFont, LookCalibrationProfile.House);
        materials.Add(houseTmpMaterial);
        return houseTmpMaterial;
    }

    /// <summary>The house TMP material on the bench's two-pass shader: every outline and underlay of a mesh first, then every face.</summary>
    Material TwoPassMaterial()
    {
        var shader = Resources.Load<Shader>(TwoPassShader);
        if (shader == null)
            throw new InvalidOperationException($"The two-pass TMP shader '{TwoPassShader}' is missing from Resources.");
        var source = HouseTmpMaterial();
        var material = new Material(shader) { name = source.name + " Two Pass" };
        material.CopyPropertiesFromMaterial(source);
        material.EnableKeyword(ShaderUtilities.Keyword_Outline);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        materials.Add(material);
        return material;
    }

    static T Styled<T>(T text, ScenarioEngine engine) where T : UniTextBase
    {
        if (engine == ScenarioEngine.UniTextPlain) return text;
        LookCalibrationProfile.ApplyUniText(text, LookCalibrationProfile.House);
        if (engine == ScenarioEngine.UniTextMerged)
            text.SetWholeText<PaintOrderModifier>("glyph,false,true");
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

/// <summary>The scenario catalog and the content it draws from.</summary>
/// <remarks>
/// Every case's first arm is UniText's default configuration; <c>.st</c> is single-threaded, <c>.tmp</c> the TMP twin,
/// <c>.tmp.layered</c> TMP drawing UniText's layer order, <c>.plain</c> the same arm without effects, <c>.merged</c>
/// UniText with merged layers. Changing content never repeats across the arms of a case: counters draw from one
/// process-wide sequence, text pools of UniText arms are offset from each other.
/// </remarks>
static class UniTextScenarios
{
    public const string MemoryId = "memory.cycles";
    public const string HiddenMemoryId = "memory.hidden";
    public const string StartupId = "startup";

    const int PageColumns = 2;
    const int PageRows = 30;
    const float PageNameWidth = 296f;
    const float Gutter = 24f;
    const int CounterDigits = 7;

    /// <summary>Frames after which continuous motion restarts; keeps float phases exact for the large frame numbers the suite passes.</summary>
    const int MotionPeriodFrames = 100_000;

    static int countersShown;

    public static readonly string[] LatinLines = BenchmarkConfig.Latin.Split('\n');
    public static readonly string[] MultilingualLines = BenchmarkConfig.MultilingualPlain.Split('\n');

    static readonly int[] MultilingualScreenLines = { 0, 6, 7, 8, 10, 11, 12, 13, 14, 22 };

    static readonly string[] Words =
    {
        "Health", "Mana", "Gold", "Quest", "Level", "Armor", "Speed", "Score", "Combo", "Bonus", "Shield",
        "Energy", "Rank", "Wave", "Crystal", "Stamina", "Ammo", "Credits", "Gems", "Keys", "Lives", "Time",
        "Damage", "Critical", "Defense", "Agility", "Wisdom", "Luck", "Fame", "Honor", "Supply", "Morale"
    };

    static readonly string[] SettingsSections = { "Audio", "Graphics", "Account" };

    static readonly string[] SettingsRows =
    {
        "Music volume", "80 %", "Sound effects", "65 %", "Voice chat", "On", "Microphone", "Off",
        "Subtitles", "Large", "Speaker mode", "Stereo", "Dialogue language", "English", "Vibration", "Strong",
        "Quality preset", "High", "Frame rate", "60 fps", "Resolution scale", "90 %", "Shadows", "Soft",
        "Anti-aliasing", "4x MSAA", "Bloom", "On", "Motion blur", "Off", "Field of view", "75\u00B0",
        "Player name", "Starfall_77", "Region", "Europe", "Friends online", "12", "Achievements", "48 / 120",
        "Cloud save", "Synced", "Notifications", "Daily", "Parental lock", "Off", "Version", "1.4.0"
    };

    static readonly string[] Titles =
    {
        "Victory!", "Level 12 complete", "Stars 3 / 3", "Score 1,234,567", "Best 1,298,004", "Coins +2,450",
        "Gems +35", "Time 03:27", "Combo x48", "Rank: Gold II", "Next: Level 13", "Tap to continue"
    };

    static readonly string[] HudCaptions = { "Score", "Time", "Coins", "Gems", "Ammo", "Combo" };

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

    /// <summary>Every case of the scenario suite with its arms, in a fixed order.</summary>
    public static List<ScenarioCase> Cases()
    {
        var cases = new List<ScenarioCase>
        {
            new ScenarioCase("screen",
                new ScreenScenario("screen", ScreenContent.Settings),
                new ScreenScenario("screen.tmp", ScreenContent.Settings, ScenarioEngine.Tmp),
                new ScreenScenario("screen.tmp.layered", ScreenContent.Settings, ScenarioEngine.TmpLayered),
                new ScreenScenario("screen.plain", ScreenContent.Settings, ScenarioEngine.UniTextPlain),
                new ScreenScenario("screen.tmp.plain", ScreenContent.Settings, ScenarioEngine.TmpPlain),
                new ScreenScenario("screen.default", ScreenContent.Settings, defaultMaterial: true)),
            new ScenarioCase("screen.outline",
                new ScreenScenario("screen.outline", ScreenContent.Settings, style: ScreenStyle.StrokeOnly),
                new ScreenScenario("screen.shadow", ScreenContent.Settings, style: ScreenStyle.ShadowOnly),
                new ScreenScenario("screen.glow", ScreenContent.Settings, style: ScreenStyle.Glow),
                new ScreenScenario("screen.decorations", ScreenContent.Settings, style: ScreenStyle.Decorations),
                new ScreenScenario("screen.msdf", ScreenContent.Settings, style: ScreenStyle.Msdf)),
            new ScenarioCase("screen.large",
                new ScreenScenario("screen.large", ScreenContent.Titles),
                new ScreenScenario("screen.large.tmp", ScreenContent.Titles, ScenarioEngine.Tmp)),
            new ScenarioCase("screen.multilingual",
                new ScreenScenario("screen.multilingual", ScreenContent.Multilingual),
                new ScreenScenario("screen.multilingual.tmp", ScreenContent.Multilingual, ScenarioEngine.Tmp)),
            new ScenarioCase("screen.emoji", new ScreenScenario("screen.emoji", ScreenContent.Emoji)),
            new ScenarioCase("hud.counters",
                new HudCountersScenario("hud.counters", ScenarioEngine.UniText),
                new HudCountersScenario("hud.counters.st", ScenarioEngine.UniText, false),
                new HudCountersScenario("hud.counters.tmp", ScenarioEngine.Tmp),
                new HudCountersScenario("hud.counters.plain", ScenarioEngine.UniTextPlain),
                new HudCountersScenario("hud.counters.tmp.plain", ScenarioEngine.TmpPlain)),
            new ScenarioCase("counters.240",
                new CounterGridScenario("counters.240", ScenarioEngine.UniText),
                new CounterGridScenario("counters.240.st", ScenarioEngine.UniText, false),
                new CounterGridScenario("counters.240.tmp", ScenarioEngine.Tmp),
                new CounterGridScenario("counters.240.tmp.layered", ScenarioEngine.TmpLayered),
                new CounterGridScenario("counters.240.plain", ScenarioEngine.UniTextPlain),
                new CounterGridScenario("counters.240.tmp.plain", ScenarioEngine.TmpPlain),
                new CounterGridScenario("counters.240.merged", ScenarioEngine.UniTextMerged)),
            new ScenarioCase("hud.toggle",
                new HudScenario("hud.toggle", HudKind.Toggle, ScenarioEngine.UniText),
                new HudScenario("hud.toggle.tmp", HudKind.Toggle, ScenarioEngine.Tmp)),
            new ScenarioCase("hud.markup",
                new HudScenario("hud.markup", HudKind.Markup, ScenarioEngine.UniText),
                new HudScenario("hud.markup.tmp", HudKind.Markup, ScenarioEngine.Tmp)),
            new ScenarioCase("tween.color",
                new TweenScenario("tween.color", TweenKind.Color, 0),
                new TweenScenario("tween.size", TweenKind.Size, 1000),
                new TweenScenario("tween.alpha", TweenKind.Alpha, 2000)),
            new ScenarioCase("animated.wave",
                new AnimatedScenario("animated.wave", text => text.SetWholeText<WaveModifier>(), "wave", 0),
                new AnimatedScenario("animated.shake", text => text.SetWholeText<ShakeModifier>(), "shake", 1000),
                new AnimatedScenario("animated.pulse", text => text.SetWholeText<PulseModifier>(), "pulse", 2000)),
            new ScenarioCase("rebuild.labels",
                new RebuildScenario("rebuild.labels", RebuildContent.Labels, true, 0),
                new RebuildScenario("rebuild.labels.st", RebuildContent.Labels, false, 2000)),
            new ScenarioCase("rebuild.paragraphs",
                new RebuildScenario("rebuild.paragraphs", RebuildContent.Paragraphs, true, 0),
                new RebuildScenario("rebuild.paragraphs.st", RebuildContent.Paragraphs, false, 100),
                new RebuildScenario("rebuild.justified", RebuildContent.Paragraphs, true, 200,
                    text => text.HorizontalAlignment = HorizontalAlignment.Justify, "justify")),
            new ScenarioCase("rebuild.multilingual",
                new RebuildScenario("rebuild.multilingual", RebuildContent.Multilingual, true, 0),
                new RebuildScenario("rebuild.multilingual.st", RebuildContent.Multilingual, false, 100)),
            new ScenarioCase("rebuild.headings",
                new RebuildScenario("rebuild.headings", RebuildContent.Headings, true, 0,
                    text => text.HorizontalAlignment = HorizontalAlignment.Center),
                new RebuildScenario("rebuild.headings.balance", RebuildContent.Headings, true, 100, text =>
                {
                    text.HorizontalAlignment = HorizontalAlignment.Center;
                    text.SetWholeText<BalanceModifier>();
                }, "balance")),
            new ScenarioCase("typing.end.short", new TypingScenario("typing.end.short", 64, false, 0)),
            new ScenarioCase("typing.end.2k",
                new TypingScenario("typing.end.2k", 2_000, false, 0),
                new TypingScenario("typing.middle.2k", 2_000, true, 7)),
            new ScenarioCase("typing.end.20k",
                new TypingScenario("typing.end.20k", 20_000, false, 0),
                new TypingScenario("typing.middle.20k", 20_000, true, 7)),
            new ScenarioCase("scroll.static",
                new ScrollScenario("scroll.static", false, ScenarioMask.RectMask2D, 0),
                new ScrollScenario("scroll.static.lsmask", false, ScenarioMask.LightSide, 0),
                new ScrollScenario("scroll.recycle", true, ScenarioMask.RectMask2D, 1000),
                new ScrollScenario("scroll.recycle.lsmask", true, ScenarioMask.LightSide, 2000)),
            new ScenarioCase("churn",
                new ChurnScenario("churn", ScenarioEngine.UniText, 0),
                new ChurnScenario("churn.tmp", ScenarioEngine.Tmp, 0)),
            new ScenarioCase("pages.swipe",
                new PagesScenario("pages.swipe", PagesMotion.Swipe, ScenarioEngine.UniText, ScenarioMask.RectMask2D),
                new PagesScenario("pages.swipe.lsmask", PagesMotion.Swipe, ScenarioEngine.UniText, ScenarioMask.LightSide),
                new PagesScenario("pages.swipe.tmp", PagesMotion.Swipe, ScenarioEngine.Tmp, ScenarioMask.RectMask2D),
                new PagesScenario("pages.offscreen", PagesMotion.Offscreen, ScenarioEngine.UniText, ScenarioMask.RectMask2D),
                new PagesScenario("pages.offscreen.lsmask", PagesMotion.Offscreen, ScenarioEngine.UniText, ScenarioMask.LightSide),
                new PagesScenario("pages.offscreen.tmp", PagesMotion.Offscreen, ScenarioEngine.Tmp, ScenarioMask.RectMask2D),
                new PagesScenario("pages.rest", PagesMotion.Rest, ScenarioEngine.UniText, ScenarioMask.RectMask2D),
                new PagesScenario("pages.rest.lsmask", PagesMotion.Rest, ScenarioEngine.UniText, ScenarioMask.LightSide)),
            new ScenarioCase("world.nameplates",
                new WorldScenario("world.nameplates", WorldMotion.Moving, ScenarioEngine.UniText, 30, 5),
                new WorldScenario("world.nameplates.st", WorldMotion.Moving, ScenarioEngine.UniText, 30, 5, false),
                new WorldScenario("world.nameplates.tmp", WorldMotion.Moving, ScenarioEngine.Tmp, 30, 5),
                new WorldScenario("world.nameplates.tmp.layered", WorldMotion.Moving, ScenarioEngine.TmpLayered, 30, 5)),
            new ScenarioCase("world.counters",
                new WorldScenario("world.counters", WorldMotion.None, ScenarioEngine.UniText, 240, 240),
                new WorldScenario("world.counters.st", WorldMotion.None, ScenarioEngine.UniText, 240, 240, false),
                new WorldScenario("world.counters.tmp", WorldMotion.None, ScenarioEngine.Tmp, 240, 240),
                new WorldScenario("world.counters.tmp.layered", WorldMotion.None, ScenarioEngine.TmpLayered, 240, 240),
                new WorldScenario("world.counters.plain", WorldMotion.None, ScenarioEngine.UniTextPlain, 240, 240),
                new WorldScenario("world.counters.tmp.plain", WorldMotion.None, ScenarioEngine.TmpPlain, 240, 240),
                new WorldScenario("world.counters.merged", WorldMotion.None, ScenarioEngine.UniTextMerged, 240, 240)),
            new ScenarioCase("world.static",
                new WorldScenario("world.static", WorldMotion.None, ScenarioEngine.UniText, 240, 0),
                new WorldScenario("world.static.tmp", WorldMotion.None, ScenarioEngine.Tmp, 240, 0),
                new WorldScenario("world.moving", WorldMotion.Moving, ScenarioEngine.UniText, 240, 0),
                new WorldScenario("world.moving.tmp", WorldMotion.Moving, ScenarioEngine.Tmp, 240, 0),
                new WorldScenario("world.animated", WorldMotion.Animated, ScenarioEngine.UniText, 240, 0))
        };
        cases.AddRange(UniTextDocumentScenarios.Cases());
#if UNITEXT_3D
        cases.AddRange(UniText3DScenarios.Cases());
#endif
#if LIGHTSIDE_HAS_UNIPARTICLES
        cases.AddRange(UniParticlesScenarios.Cases());
#endif
#if LIGHTSIDE_HAS_UNIGUI
        cases.AddRange(UniShapesScenarios.Cases());
#endif
        return cases;
    }

    /// <summary>
    /// UniText texts with the house effects filling a <paramref name="columns"/> × <paramref name="rows"/> grid in a
    /// horizontal band of <paramref name="root"/>, cycling through <paramref name="content"/>.
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
            var label = rig.CreateLabel(engine, root, content[i % content.Length], fontSize);
            foreach (var rect in label.Rects)
                PlaceCell(rect, i, columns, rows);
            labels[i] = label.Graphic;
        }
        return labels;
    }

    /// <summary>
    /// Fills <paramref name="page"/>, a rect spanning the canvas, with page <paramref name="index"/> of a shop: two
    /// columns of 30 rows, each row an item name and its price, 120 labels at the dense type step.
    /// </summary>
    public static MaskableGraphic[] FillPage(ScenarioRig rig, ScenarioEngine engine, RectTransform page, int index)
    {
        var labels = new List<ScenarioLabel>(PageColumns * PageRows * 2);
        BuildPage(rig, engine, page, index, labels);
        var graphics = new MaskableGraphic[labels.Count];
        for (var i = 0; i < graphics.Length; i++)
            graphics[i] = labels[i].Graphic;
        return graphics;
    }

    static void BuildPage(ScenarioRig rig, ScenarioEngine engine, RectTransform page, int index, List<ScenarioLabel> labels)
    {
        var content = ScenarioRig.Content(page);
        var names = Labels(PageColumns * PageRows, index * PageColumns * PageRows);
        var columnWidth = (ScenarioRig.ContentWidth - Gutter) / PageColumns;
        var height = TypeStep.Line(TypeStep.Dense);
        for (var i = 0; i < names.Length; i++)
        {
            var x = i % PageColumns * (columnWidth + Gutter);
            var top = i / PageColumns * (height + 5f);
            var name = rig.CreateLabel(engine, content, names[i], TypeStep.Dense);
            name.Place(x, top, PageNameWidth, height);
            labels.Add(name);
            var price = rig.CreateLabel(engine, content, Price(index * names.Length + i), TypeStep.Dense);
            price.Place(x + PageNameWidth, top, columnWidth - PageNameWidth, height);
            price.Align(HorizontalAlignment.End);
            labels.Add(price);
        }
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

    /// <summary>Throws when an active label of <paramref name="labels"/> laid out on more than one line: one-line labels must fit their cells on every test phone.</summary>
    static void RequireSingleLines(IReadOnlyList<ScenarioLabel> labels)
    {
        foreach (var label in labels)
        {
            if (!label.Graphic.isActiveAndEnabled) continue;
            var lines = label.Lines;
            if (lines > 1)
                throw new InvalidOperationException($"'{label.Rect.name}' wraps to {lines} lines; the layout must keep it on one.");
        }
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

    /// <summary>
    /// Writes the engine with its effects (UniText's whole-text styles; under <c>rival</c>, TMP's font with its sampling,
    /// padding, atlas and fallbacks, the materials of its layers and their shader), the label count, the characters,
    /// glyphs and lines they hold, the font size and how many change per frame.
    /// </summary>
    /// <remarks>Each label counts once, a layered TMP label by its face text.</remarks>
    static void DescribeLabels(JObject workload, IReadOnlyList<ScenarioLabel> labels, float fontSize, int changing)
    {
        var characters = 0;
        var glyphs = 0;
        var lines = 0;
        foreach (var label in labels)
        {
            characters += label.Characters;
            glyphs += label.Glyphs;
            lines += label.Lines;
        }
        var first = labels[0];
        if (first.UniText == null)
        {
            var font = first.TmpLayers[0].font;
            var materials = new JArray();
            foreach (var layer in first.TmpLayers)
                materials.Add(layer.fontSharedMaterial.name);
            var fallbacks = new JArray();
            if (font.fallbackFontAssetTable != null)
                foreach (var fallback in font.fallbackFontAssetTable)
                    fallbacks.Add(fallback.name);
            workload["engine"] = "TMP";
            workload["rival"] = new JObject
            {
                ["font"] = font.name,
                ["samplingPointSize"] = font.faceInfo.pointSize,
                ["atlasPadding"] = font.atlasPadding,
                ["atlasWidth"] = font.atlasWidth,
                ["atlasHeight"] = font.atlasHeight,
                ["fallbacks"] = fallbacks,
                ["materials"] = materials,
                ["shader"] = first.TmpLayers[0].fontSharedMaterial.shader.name
            };
        }
        else
        {
            var effects = new JArray();
            foreach (var style in first.UniText.GetStylesOfType<BaseModifier>())
                if (UniTextBase.IsWholeTextStyle(style))
                    effects.Add(style.Modifier.GetType().Name);
            workload["engine"] = "UniText";
            workload["effects"] = effects;
        }
        workload["texts"] = labels.Count;
        workload["characters"] = characters;
        workload["glyphs"] = glyphs;
        workload["lines"] = lines;
        workload["fontSize"] = fontSize;
        workload["changingTexts"] = changing;
    }

    static ScenarioLabel Add(List<ScenarioLabel> labels, ScenarioRig rig, ScenarioEngine engine, RectTransform root,
        string text, float fontSize, float x, float top, float width, HorizontalAlignment alignment)
    {
        var label = rig.CreateLabel(engine, root, text, fontSize);
        label.Place(x, top, width, TypeStep.Line(fontSize));
        label.Align(alignment);
        labels.Add(label);
        return label;
    }

    /// <summary>
    /// UniText texts with the house effects in <paramref name="columns"/> columns of the content area <paramref name="root"/>,
    /// one line each, rows a fixed pitch apart from the top.
    /// </summary>
    static UniText[] TextColumns(ScenarioRig rig, RectTransform root, string[] content, int columns, float fontSize,
        float rowGap)
    {
        var texts = new UniText[content.Length];
        var width = (ScenarioRig.ContentWidth - Gutter * (columns - 1)) / columns;
        var height = TypeStep.Line(fontSize);
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = rig.CreateText(root, content[i], fontSize);
            ScenarioRig.Place(texts[i].rectTransform, i % columns * (width + Gutter), i / columns * (height + rowGap), width,
                height);
            texts[i].VerticalAlignment = VerticalAlignment.Middle;
        }
        return texts;
    }

    static List<ScenarioLabel> Wrap(UniText[] texts)
    {
        var labels = new List<ScenarioLabel>(texts.Length);
        foreach (var text in texts)
            labels.Add(new ScenarioLabel(text));
        return labels;
    }

    static bool IsUniText(ScenarioEngine engine) =>
        engine is ScenarioEngine.UniText or ScenarioEngine.UniTextPlain or ScenarioEngine.UniTextMerged;

    /// <summary>HUD-like labels; label <c>n</c> reads the same in every call, and labels repeat every 4,000.</summary>
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

    /// <summary>
    /// Numbered title-like phrases of seven to ten words, each two lines at the number type step across the content
    /// width; heading <c>n</c> reads the same in every call, and pools of different firsts share no heading.
    /// </summary>
    public static string[] Headings(int count, int first = 0)
    {
        var headings = new string[count];
        var builder = new StringBuilder(128);
        for (var i = 0; i < count; i++)
        {
            var n = first + i;
            builder.Clear();
            builder.Append(n + 1).Append(". ");
            var words = 7 + n % 4;
            for (var j = 0; j < words; j++)
            {
                if (j > 0) builder.Append(' ');
                var word = Words[(n * 11 + j * 5) % Words.Length];
                builder.Append(j == 0 ? word : word.ToLowerInvariant());
            }
            headings[i] = builder.ToString();
        }
        return headings;
    }

    /// <summary>
    /// <paramref name="count"/> distinct paragraphs of <paramref name="linesPerParagraph"/> consecutive lines, each
    /// starting at a different line; <paramref name="first"/> numbers and selects them, so pools of different firsts
    /// share no paragraph.
    /// </summary>
    public static string[] Paragraphs(string[] lines, int linesPerParagraph, int count, int first = 0)
    {
        var paragraphs = new string[count];
        var builder = new StringBuilder(1024);
        for (var i = 0; i < count; i++)
        {
            var n = first + i;
            builder.Clear();
            builder.Append('#').Append(n + 1).Append(' ');
            for (var j = 0; j < linesPerParagraph; j++)
            {
                if (j > 0) builder.Append(' ');
                builder.Append(lines[(n * 3 + j) % lines.Length]);
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

    /// <summary>Plain Latin text of exactly <paramref name="length"/> characters starting at Latin line <paramref name="firstLine"/>.</summary>
    public static string LatinText(int length, int firstLine = 0)
    {
        var builder = new StringBuilder(length + 128);
        var line = 0;
        while (builder.Length < length)
        {
            builder.Append(LatinLines[(firstLine + line) % LatinLines.Length]).Append(line % 6 == 5 ? '\n' : ' ');
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
    /// The sequence repeats no number in its first ten million, so no counter shows a number UniText's word cache saw
    /// before, as a real score or timer never does, and no two arms of a case ever show the same number.
    /// </remarks>
    static int NextCounterValue() => (int)(countersShown++ * 7919L % 10_000_000);

    /// <summary>The number counter <paramref name="index"/> shows in the stills, the same in every arm.</summary>
    static int StillValue(int index) => (int)((index * 104_729L + 1_234_567) % 10_000_000);

    static string Digits(int value)
    {
        var buffer = new char[CounterDigits];
        WriteDigits(buffer, 0, CounterDigits, value);
        return new string(buffer);
    }

    static string Price(int index) =>
        (index * 7919 % 9_800 + 100).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Adds the viewport mask a scenario clips with: uGUI's <see cref="RectMask2D"/> or <see cref="LightSideRectMask"/>.</summary>
    static void AddMask(GameObject host, ScenarioMask mask)
    {
        if (mask == ScenarioMask.LightSide) host.AddComponent<LightSideRectMask>();
        else host.AddComponent<RectMask2D>();
    }

    enum ScreenContent
    {
        Settings,
        Titles,
        Multilingual,
        Emoji
    }

    enum ScreenStyle
    {
        Base,
        StrokeOnly,
        ShadowOnly,
        Glow,
        Decorations,
        Msdf
    }

    /// <summary>
    /// A static screen of text; nothing changes, so the frame shows the idle cost and the GPU cost of the look. Settings is a
    /// settings screen (title, three sections of eight name/value rows), Titles a results screen of twelve title lines,
    /// Multilingual one paragraph per script, Emoji forty lines of eight emoji. A style variant drops one of the two effects
    /// or adds one to them. The default-material variant draws every renderer with the default canvas material a few frames
    /// after each activation, so its GPU time is the geometry's and the difference to the base arm is the text shader's.
    /// </summary>
    sealed class ScreenScenario : UniTextScenario
    {
        const int SwapDelayFrames = 2;
        const float NameWidth = 560f;
        const int RowsPerSection = 8;

        readonly ScreenContent content;
        readonly ScreenStyle style;
        readonly bool defaultMaterial;
        readonly List<ScenarioLabel> labels = new();
        RectTransform root;
        float fontSize;
        int swapCountdown;
        int swapped;

        public ScreenScenario(string id, ScreenContent content, ScenarioEngine engine = ScenarioEngine.UniText,
            ScreenStyle style = ScreenStyle.Base, bool defaultMaterial = false) : base(id, true, engine)
        {
            this.content = content;
            this.style = style;
            this.defaultMaterial = defaultMaterial;
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            switch (content)
            {
                case ScreenContent.Settings:
                    BuildSettings(rig);
                    break;
                case ScreenContent.Titles:
                    fontSize = TypeStep.Title;
                    for (var i = 0; i < Titles.Length; i++)
                        Add(labels, rig, TextEngine, root, Titles[i], fontSize, 0f, i * (TypeStep.Line(fontSize) + 9f),
                            ScenarioRig.ContentWidth, HorizontalAlignment.Start);
                    break;
                case ScreenContent.Multilingual:
                    BuildMultilingual(rig);
                    break;
                default:
                    fontSize = TypeStep.Body;
                    var emoji = EmojiLines(40, 8);
                    var width = (ScenarioRig.ContentWidth - Gutter) / 2f;
                    for (var i = 0; i < emoji.Length; i++)
                        Add(labels, rig, TextEngine, root, emoji[i], fontSize, i % 2 * (width + Gutter),
                            i / 2 * (TypeStep.Line(fontSize) + 6f), width, HorizontalAlignment.Start);
                    break;
            }
            if (!IsUniText(TextEngine)) return;
            foreach (var label in labels)
                ApplyStyle(label.UniText);
        }

        void BuildSettings(ScenarioRig rig)
        {
            fontSize = TypeStep.Body;
            var width = ScenarioRig.ContentWidth;
            Add(labels, rig, TextEngine, root, "Settings", TypeStep.Title, 0f, 0f, width, HorizontalAlignment.Start);
            var top = TypeStep.Line(TypeStep.Title) + 24f;
            for (var section = 0; section < SettingsSections.Length; section++)
            {
                Add(labels, rig, TextEngine, root, SettingsSections[section], TypeStep.Number, 0f, top, width,
                    HorizontalAlignment.Start);
                top += TypeStep.Line(TypeStep.Number) + 8f;
                for (var row = 0; row < RowsPerSection; row++)
                {
                    var pair = (section * RowsPerSection + row) * 2;
                    Add(labels, rig, TextEngine, root, SettingsRows[pair], fontSize, 0f, top, NameWidth,
                        HorizontalAlignment.Start);
                    Add(labels, rig, TextEngine, root, SettingsRows[pair + 1], fontSize, NameWidth, top, width - NameWidth,
                        HorizontalAlignment.End);
                    top += TypeStep.Line(fontSize) + 6f;
                }
                top += 26f;
            }
        }

        void BuildMultilingual(ScenarioRig rig)
        {
            fontSize = TypeStep.Body;
            var height = 3f * TypeStep.Line(fontSize);
            for (var i = 0; i < MultilingualScreenLines.Length; i++)
            {
                var label = rig.CreateLabel(TextEngine, root, MultilingualLines[MultilingualScreenLines[i]], fontSize);
                label.Place(0f, i * (height + 10f), ScenarioRig.ContentWidth, height);
                label.Align(HorizontalAlignment.Start);
                labels.Add(label);
            }
        }

        public override void SetActive(bool active)
        {
            base.SetActive(active);
            if (active && defaultMaterial) swapCountdown = SwapDelayFrames;
        }

        public override void Step(int frame)
        {
            if (swapCountdown <= 0 || --swapCountdown > 0) return;
            var material = Canvas.GetDefaultCanvasMaterial();
            foreach (var renderer in root.GetComponentsInChildren<CanvasRenderer>())
                for (var i = 0; i < renderer.materialCount; i++)
                {
                    if (renderer.GetMaterial(i) == material) continue;
                    renderer.SetMaterial(material, i);
                    swapped++;
                }
        }

        void ApplyStyle(UniTextBase text)
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

        protected override int Load => labels.Count;

        protected override bool Baseline => defaultMaterial;

        protected override string Variant =>
            JoinVariant(base.Variant, style == ScreenStyle.Base ? null : VariantWord(style));

        protected override bool Comparable => content != ScreenContent.Multilingual || IsUniText(TextEngine);

        public override void Validate()
        {
            RequireGlyphs(root);
            if (content != ScreenContent.Multilingual) RequireSingleLines(labels);
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
            DescribeLabels(workload, labels, fontSize, 0);
            workload["content"] = content.ToString();
            workload["style"] = style.ToString();
            if (defaultMaterial) workload["material"] = Canvas.GetDefaultCanvasMaterial().shader.name;
        }
    }

    /// <summary>
    /// A game HUD of 30 labels: a top bar of six captioned counters (score, time, coins, gems, ammo, combo) whose numbers
    /// change every frame, and two stacks of nine status labels along the bottom corners.
    /// </summary>
    sealed class HudCountersScenario : UniTextScenario
    {
        const int StackRows = 9;

        readonly List<ScenarioLabel> labels = new();
        readonly ScenarioLabel[] numbers = new ScenarioLabel[HudCaptions.Length];
        readonly char[][] buffers = new char[HudCaptions.Length][];
        RectTransform root;

        public HudCountersScenario(string id, ScenarioEngine engine, bool parallel = true) : base(id, parallel, engine) { }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            var cellWidth = (ScenarioRig.ContentWidth - 2f * Gutter) / 3f;
            for (var i = 0; i < HudCaptions.Length; i++)
            {
                var x = i % 3 * (cellWidth + Gutter);
                var top = i / 3 * (TypeStep.Line(TypeStep.Label) + TypeStep.Line(TypeStep.Number) + Gutter);
                Add(labels, rig, TextEngine, root, HudCaptions[i], TypeStep.Label, x, top, cellWidth, HorizontalAlignment.Start);
                buffers[i] = Digits(StillValue(i)).ToCharArray();
                numbers[i] = Add(labels, rig, TextEngine, root, new string(buffers[i]), TypeStep.Number, x,
                    top + TypeStep.Line(TypeStep.Label), cellWidth, HorizontalAlignment.End);
            }

            var stack = Labels(2 * StackRows, 3000);
            var columnWidth = (ScenarioRig.ContentWidth - Gutter) / 2f;
            var height = TypeStep.Line(TypeStep.Label);
            for (var i = 0; i < stack.Length; i++)
            {
                var column = i % 2;
                var label = rig.CreateLabel(TextEngine, root, stack[i], TypeStep.Label);
                label.PlaceFromBottom(column * (columnWidth + Gutter),
                    (StackRows - 1 - i / 2) * (height + 8f), columnWidth, height);
                label.Align(column == 0 ? HorizontalAlignment.Start : HorizontalAlignment.End);
                labels.Add(label);
            }
            ExpectCommitEveryFrame(numbers[0].Graphic);
        }

        public override void Step(int frame)
        {
            var still = frame == UniTextScenarioSuite.StillFrame;
            for (var i = 0; i < numbers.Length; i++)
            {
                WriteDigits(buffers[i], 0, CounterDigits, still ? StillValue(i) : NextCounterValue());
                numbers[i].SetText(buffers[i]);
            }
        }

        protected override int Load => numbers.Length;

        public override void Validate()
        {
            RequireGlyphs(root);
            RequireSingleLines(labels);
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, labels, TypeStep.Label, numbers.Length);
            workload["numberSize"] = TypeStep.Number;
            workload["api"] = "SetText(char[], int, int)";
        }
    }

    /// <summary>240 seven-digit counters in a 6 × 40 grid at the dense type step, end-aligned, each showing a new number every frame.</summary>
    sealed class CounterGridScenario : UniTextScenario
    {
        const int Columns = 6;
        const int Count = 240;
        const float ColumnGutter = 16f;

        readonly ScenarioLabel[] labels = new ScenarioLabel[Count];
        readonly char[][] buffers = new char[Count][];
        RectTransform root;

        public CounterGridScenario(string id, ScenarioEngine engine, bool parallel = true) : base(id, parallel, engine) { }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            var width = (ScenarioRig.ContentWidth - ColumnGutter * (Columns - 1)) / Columns;
            var height = TypeStep.Line(TypeStep.Dense);
            for (var i = 0; i < Count; i++)
            {
                buffers[i] = Digits(StillValue(i)).ToCharArray();
                labels[i] = rig.CreateLabel(TextEngine, root, new string(buffers[i]), TypeStep.Dense);
                labels[i].Place(i % Columns * (width + ColumnGutter), i / Columns * (height + 3f), width,
                    height);
                labels[i].Align(HorizontalAlignment.End);
            }
            ExpectCommitEveryFrame(labels[0].Graphic);
        }

        public override void Step(int frame)
        {
            var still = frame == UniTextScenarioSuite.StillFrame;
            for (var i = 0; i < Count; i++)
            {
                WriteDigits(buffers[i], 0, CounterDigits, still ? StillValue(i) : NextCounterValue());
                labels[i].SetText(buffers[i]);
            }
        }

        protected override int Load => Count;

        public override void Validate()
        {
            RequireGlyphs(root);
            RequireSingleLines(labels);
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, labels, TypeStep.Dense, Count);
            workload["api"] = "SetText(char[], int, int)";
        }
    }

    enum TweenKind
    {
        Color,
        Size,
        Alpha
    }

    /// <summary>64 labels in two columns at the dense type step whose color, font size or color alpha changes every frame.</summary>
    sealed class TweenScenario : UniTextScenario
    {
        const int Count = 64;
        const int Steps = 61;

        readonly TweenKind kind;
        readonly int contentOffset;
        readonly Color[] colors = new Color[Steps];
        readonly float[] sizes = new float[Steps];
        readonly float[] alphas = new float[Steps];
        UniText[] texts;
        RectTransform root;

        public TweenScenario(string id, TweenKind kind, int contentOffset) : base(id, true)
        {
            this.kind = kind;
            this.contentOffset = contentOffset;
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            texts = TextColumns(rig, root, Labels(Count, contentOffset), 2, TypeStep.Dense, 5f);
            for (var i = 0; i < Steps; i++)
            {
                var phase = i / (float)Steps;
                colors[i] = Color.HSVToRGB(phase, 0.6f, 1f);
                sizes[i] = TypeStep.Dense + 4f * Mathf.Sin(phase * Mathf.PI * 2f);
                alphas[i] = 0.55f + 0.45f * Mathf.Cos(phase * Mathf.PI * 2f);
            }
        }

        public override void Step(int frame)
        {
            var still = frame == UniTextScenarioSuite.StillFrame;
            for (var i = 0; i < texts.Length; i++)
            {
                var index = still ? 0 : (frame + i) % Steps;
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

        protected override int Load => Count;

        protected override string Variant => VariantWord(kind);

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, Wrap(texts), TypeStep.Dense, texts.Length);
            workload["property"] = kind.ToString();
        }
    }

    /// <summary>64 labels in two columns at the dense type step animated by a whole-text animation modifier advancing on its own clock.</summary>
    sealed class AnimatedScenario : UniTextScenario
    {
        const int Count = 64;

        readonly Action<UniText> animate;
        readonly string variant;
        readonly int contentOffset;
        UniText[] texts;
        RectTransform root;

        public AnimatedScenario(string id, Action<UniText> animate, string variant, int contentOffset) : base(id, true)
        {
            this.animate = animate;
            this.variant = variant;
            this.contentOffset = contentOffset;
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            texts = TextColumns(rig, root, Labels(Count, contentOffset), 2, TypeStep.Dense, 5f);
            foreach (var text in texts)
                animate(text);
        }

        protected override int Load => Count;

        protected override string Variant => variant;

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload) =>
            DescribeLabels(workload, Wrap(texts), TypeStep.Dense, texts.Length);
    }

    enum RebuildContent
    {
        Labels,
        Paragraphs,
        Multilingual,
        Headings
    }

    /// <summary>
    /// Texts that receive different content every frame, cycling through a pool much larger than one frame's demand:
    /// 64 one-line labels, four paragraphs of four Latin lines, four paragraphs of two multilingual lines, or twelve
    /// centered headings.
    /// </summary>
    sealed class RebuildScenario : UniTextScenario
    {
        readonly RebuildContent content;
        readonly int contentOffset;
        readonly Action<UniText> style;
        readonly string variant;
        UniText[] texts;
        string[] pool;
        string[] still;
        RectTransform root;
        float fontSize;

        public RebuildScenario(string id, RebuildContent content, bool parallel, int contentOffset,
            Action<UniText> style = null, string variant = null) : base(id, parallel)
        {
            this.content = content;
            this.contentOffset = contentOffset;
            this.style = style;
            this.variant = variant;
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            switch (content)
            {
                case RebuildContent.Labels:
                    fontSize = TypeStep.Dense;
                    pool = Labels(509, contentOffset);
                    still = Labels(64);
                    texts = TextColumns(rig, root, still, 2, fontSize, 5f);
                    break;
                case RebuildContent.Paragraphs:
                    fontSize = TypeStep.Body;
                    pool = Paragraphs(LatinLines, 4, 37, contentOffset);
                    still = Paragraphs(LatinLines, 4, 4);
                    texts = Blocks(rig, still, 8, 16f);
                    break;
                case RebuildContent.Headings:
                    fontSize = TypeStep.Number;
                    pool = Headings(97, contentOffset);
                    still = Headings(12);
                    texts = Blocks(rig, still, 2, 12f);
                    break;
                default:
                    fontSize = TypeStep.Body;
                    pool = Paragraphs(MultilingualLines, 2, 37, contentOffset);
                    still = Paragraphs(MultilingualLines, 2, 4);
                    texts = Blocks(rig, still, 6, 16f);
                    break;
            }
            if (style != null)
                foreach (var text in texts)
                    style(text);
            ExpectCommitEveryFrame(texts[0]);
        }

        /// <summary>One text per entry of <paramref name="content"/> in one column, each a block of <paramref name="lines"/> lines.</summary>
        UniText[] Blocks(ScenarioRig rig, string[] content, int lines, float gap)
        {
            var blocks = new UniText[content.Length];
            var height = lines * TypeStep.Line(fontSize);
            for (var i = 0; i < blocks.Length; i++)
            {
                blocks[i] = rig.CreateText(root, content[i], fontSize);
                ScenarioRig.Place(blocks[i].rectTransform, 0f, i * (height + gap), ScenarioRig.ContentWidth, height);
            }
            return blocks;
        }

        public override void Step(int frame)
        {
            if (frame == UniTextScenarioSuite.StillFrame)
            {
                for (var i = 0; i < texts.Length; i++)
                    texts[i].SetText(still[i]);
                return;
            }
            for (var i = 0; i < texts.Length; i++)
                texts[i].SetText(pool[(int)(((long)frame * texts.Length + i) % pool.Length)]);
        }

        protected override int Load => texts.Length;

        protected override string Variant => variant;

        public override void Validate()
        {
            RequireGlyphs(root);
            if (content == RebuildContent.Labels) RequireSingleLines(Wrap(texts));
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, Wrap(texts), fontSize, texts.Length);
            workload["content"] = content.ToString();
            workload["poolSize"] = pool.Length;
        }
    }

    /// <summary>
    /// One keystroke per step into an input field filling the content area: a character typed and erased with backspace on
    /// alternate steps, so the document length stays constant; the still erases a pending character.
    /// </summary>
    /// <remarks>The field's own state picks the keystroke, never the frame number: the suite's frame numbers restart and jump between setup, warm-up, slots and stills.</remarks>
    sealed class TypingScenario : UniTextScenario
    {
        const string Typed = "x";

        readonly int length;
        readonly bool middle;
        readonly int firstLine;
        UniTextEditable editable;
        string initial;
        string typed;
        bool inserted;

        public TypingScenario(string id, int length, bool middle, int firstLine) : base(id, true)
        {
            this.length = length;
            this.middle = middle;
            this.firstLine = firstLine;
        }

        int Caret => middle ? length / 2 : length;

        protected override void Build(ScenarioRig rig)
        {
            var root = rig.CreateContent(Id);
            editable = rig.CreateInputField(root, TypeStep.Body);
            ScenarioRig.Stretch((RectTransform)editable.transform.parent);
            initial = LatinText(length, firstLine);
            typed = initial.Insert(Caret, Typed);
            editable.SetText(initial);
            ExpectCommitEveryFrame(editable.TextComponent);
        }

        public override void Step(int frame)
        {
            if (inserted)
            {
                editable.DeletePrevious();
                inserted = false;
            }
            else if (frame != UniTextScenarioSuite.StillFrame)
            {
                editable.MoveCaretTo(Caret);
                editable.InsertText(Typed);
                inserted = true;
            }
        }

        protected override int Load => length;

        protected override string Variant => middle ? "middle" : null;

        public override void Validate()
        {
            if (editable.TextComponent.GlyphCount == 0)
                throw new InvalidOperationException("The field produced no glyphs.");
            if (!editable.TextEquals(inserted ? typed : initial))
                throw new InvalidOperationException("The field does not hold the text the keystrokes should leave.");
        }

        public override void Describe(JObject workload)
        {
            workload["characters"] = length;
            workload["caret"] = middle ? "middle" : "end";
            workload["glyphs"] = editable.TextComponent.GlyphCount;
            workload["fontSize"] = TypeStep.Body;
            workload["changingTexts"] = 1;
        }
    }

    /// <summary>
    /// A masked list of 120 one-line rows, taller than the content area, scrolled every frame; the recycling variant also
    /// re-texts the four cells a pooled list would reuse.
    /// </summary>
    sealed class ScrollScenario : UniTextScenario
    {
        const int Cells = 120;
        const float CellHeight = 64f;
        const int RecycledPerFrame = 4;

        readonly bool recycle;
        readonly ScenarioMask mask;
        readonly int poolOffset;
        readonly UniText[] texts = new UniText[Cells];
        readonly string[] initial = Labels(Cells);
        RectTransform viewport;
        RectTransform content;
        string[] pool;
        float travel;

        public ScrollScenario(string id, bool recycle, ScenarioMask mask, int poolOffset) : base(id, true)
        {
            this.recycle = recycle;
            this.mask = mask;
            this.poolOffset = poolOffset;
        }

        protected override void Build(ScenarioRig rig)
        {
            viewport = rig.CreateContent(Id);
            AddMask(viewport.gameObject, mask);
            content = new GameObject("Rows", typeof(RectTransform)) { layer = 5 }.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, Cells * CellHeight);
            pool = Labels(Cells * 4, Cells + poolOffset);
            for (var i = 0; i < Cells; i++)
            {
                texts[i] = rig.CreateText(content, initial[i], TypeStep.Body);
                ScenarioRig.Place(texts[i].rectTransform, 0f, i * CellHeight, ScenarioRig.ContentWidth, CellHeight - 6f);
                texts[i].VerticalAlignment = VerticalAlignment.Middle;
            }
            travel = Cells * CellHeight - ScenarioRig.ContentHeight;
        }

        public override void Step(int frame)
        {
            if (frame == UniTextScenarioSuite.StillFrame)
            {
                content.anchoredPosition = new Vector2(0f, travel * 0.25f);
                if (!recycle) return;
                for (var i = 0; i < Cells; i++)
                    texts[i].SetText(initial[i]);
                return;
            }
            content.anchoredPosition = new Vector2(0f, Mathf.PingPong(frame % MotionPeriodFrames * 9f, travel));
            if (!recycle) return;
            for (var i = 0; i < RecycledPerFrame; i++)
            {
                var n = (long)frame * RecycledPerFrame + i;
                texts[(int)(n % Cells)].SetText(pool[(int)(n % pool.Length)]);
            }
        }

        protected override int Load => recycle ? RecycledPerFrame : Cells;

        protected override string Variant => mask == ScenarioMask.LightSide ? "lsmask" : null;

        public override void Validate()
        {
            var visible = 0;
            foreach (var text in texts)
                if (!text.canvasRenderer.cull && text.GlyphCount > 0)
                    visible++;
            if (visible == 0)
                throw new InvalidOperationException("No list cell inside the viewport shows glyphs.");
            RequireSingleLines(Wrap(texts));
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, Wrap(texts), TypeStep.Body, recycle ? RecycledPerFrame : 0);
            workload["mask"] = mask.ToString();
        }
    }

    /// <summary>Sixteen labels created and destroyed every frame in a two-column block mid-screen, as a spawning loot feed or a pool-less list does.</summary>
    sealed class ChurnScenario : UniTextScenario
    {
        const int PerFrame = 16;
        const float BlockTop = 560f;

        readonly List<GameObject> live = new(PerFrame);
        readonly int poolOffset;
        ScenarioRig rig;
        RectTransform root;
        string[] pool;
        string[] still;

        public ChurnScenario(string id, ScenarioEngine engine, int poolOffset) : base(id, true, engine)
        {
            this.poolOffset = poolOffset;
        }

        protected override void Build(ScenarioRig rig)
        {
            this.rig = rig;
            root = rig.CreateContent(Id);
            pool = Labels(509, poolOffset);
            still = Labels(PerFrame);
        }

        public override void Step(int frame)
        {
            foreach (var text in live)
                Object.Destroy(text);
            live.Clear();
            var stillFrame = frame == UniTextScenarioSuite.StillFrame;
            var width = (ScenarioRig.ContentWidth - Gutter) / 2f;
            var height = TypeStep.Line(TypeStep.Label);
            for (var i = 0; i < PerFrame; i++)
            {
                var text = stillFrame ? still[i] : pool[(int)(((long)frame * PerFrame + i) % pool.Length)];
                var label = rig.CreateLabel(TextEngine, root, text, TypeStep.Label);
                label.Place(i % 2 * (width + Gutter), BlockTop + i / 2 * (height + 8f), width, height);
                foreach (var rect in label.Rects)
                    live.Add(rect.gameObject);
            }
        }

        protected override int Load => PerFrame;

        public override void Validate() => RequireGlyphs(root);

        public override void Describe(JObject workload)
        {
            workload["engine"] = TextEngine == ScenarioEngine.Tmp ? "TMP" : "UniText";
            workload["creation"] = "instance of the engine's styled label template";
            workload["createdPerFrame"] = PerFrame;
            workload["destroyedPerFrame"] = PerFrame;
            workload["fontSize"] = TypeStep.Label;
        }

        public override void Teardown() => live.Clear();
    }

    enum HudKind
    {
        Toggle,
        Markup
    }

    /// <summary>
    /// A HUD of twenty labels in two stacks, start-aligned on the left and end-aligned on the right. The toggle variant
    /// switches six of them on and off every frame; the markup variant gives every label a new score inside a
    /// <c>color</c> tag every frame, as a migrated TMP project writes it.
    /// </summary>
    sealed class HudScenario : UniTextScenario
    {
        const int Count = 20;
        const int Toggled = 6;
        const float StackTop = 200f;
        const string Prefix = "<color=#FFD700>";
        const string Suffix = "</color> pts";

        readonly HudKind kind;
        readonly List<ScenarioLabel> labels = new(Count);
        readonly char[][] buffers = new char[Count][];
        RectTransform root;
        float fontSize;

        public HudScenario(string id, HudKind kind, ScenarioEngine engine) : base(id, true, engine)
        {
            this.kind = kind;
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateContent(Id);
            fontSize = kind == HudKind.Toggle ? TypeStep.Label : TypeStep.Number;
            var content = kind == HudKind.Toggle ? Labels(Count, 3100) : null;
            var width = (ScenarioRig.ContentWidth - Gutter) / 2f;
            for (var i = 0; i < Count; i++)
            {
                string text;
                if (content != null) text = content[i];
                else
                {
                    buffers[i] = (Prefix + Digits(StillValue(i)) + Suffix).ToCharArray();
                    text = new string(buffers[i]);
                }
                var column = i % 2;
                Add(labels, rig, TextEngine, root, text, fontSize, column * (width + Gutter),
                    StackTop + i / 2 * (TypeStep.Line(fontSize) + 8f), width,
                    column == 0 ? HorizontalAlignment.Start : HorizontalAlignment.End);
            }
            if (kind != HudKind.Markup) return;
            if (TextEngine == ScenarioEngine.UniText)
                foreach (var label in labels)
                    label.UniText.Styles.Add(Style.Tag(new ColorModifier(), "color"));
            ExpectCommitEveryFrame(labels[0].Graphic);
        }

        public override void Step(int frame)
        {
            var still = frame == UniTextScenarioSuite.StillFrame;
            if (kind == HudKind.Toggle)
            {
                for (var i = 0; i < Toggled; i++)
                    labels[i].SetActive(still || (frame & 1) == 0);
                return;
            }
            for (var i = 0; i < labels.Count; i++)
            {
                WriteDigits(buffers[i], Prefix.Length, Prefix.Length + CounterDigits, still ? StillValue(i) : NextCounterValue());
                labels[i].SetText(buffers[i]);
            }
        }

        protected override int Load => kind == HudKind.Toggle ? Toggled : Count;

        public override void Validate()
        {
            if (kind == HudKind.Toggle)
                for (var i = 0; i < Toggled; i++)
                    labels[i].SetActive(true);
            RequireGlyphs(root);
            RequireSingleLines(labels);
            if (kind != HudKind.Markup) return;
            foreach (var label in labels)
            {
                var shown = label.UniText != null ? label.UniText.CleanText.ToString() : ((TMP_Text)label.Graphic).GetParsedText();
                if (shown.IndexOf('<') >= 0)
                    throw new InvalidOperationException($"'{label.Rect.name}' draws its color markup as text.");
            }
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, labels, fontSize, kind == HudKind.Toggle ? Toggled : labels.Count);
            workload["change"] = kind == HudKind.Toggle ? "SetActive" : "color-tagged counter, SetText(char[], int, int)";
        }
    }

    enum PagesMotion
    {
        Swipe,
        Offscreen,
        Rest
    }

    /// <summary>
    /// A horizontal pager of three full-screen shop pages, each a mask over 120 name and price labels. The swipe slides the
    /// strip between the first two pages and back; the off-screen variant keeps the first page still while the third
    /// slides back and forth beyond the screen's edge; at rest nothing moves.
    /// </summary>
    sealed class PagesScenario : UniTextScenario
    {
        const int Pages = 3;
        const float Period = 90f;

        readonly PagesMotion motion;
        readonly ScenarioMask mask;
        readonly RectTransform[] pages = new RectTransform[Pages];
        readonly List<ScenarioLabel> labels = new();
        RectTransform canvas;
        RectTransform root;
        RectTransform strip;

        public PagesScenario(string id, PagesMotion motion, ScenarioEngine engine, ScenarioMask mask) : base(id, true, engine)
        {
            this.motion = motion;
            this.mask = mask;
        }

        protected override void Build(ScenarioRig rig)
        {
            canvas = rig.CanvasRect;
            root = rig.CreateRoot(Id);
            strip = Panel("Strip", root, 0f);
            for (var i = 0; i < Pages; i++)
            {
                pages[i] = Panel($"Page {i}", strip, i);
                AddMask(pages[i].gameObject, mask);
                BuildPage(rig, TextEngine, pages[i], i, labels);
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
            var phase = frame == UniTextScenarioSuite.StillFrame
                ? 0.5f * Mathf.PI
                : frame % (int)Period * (2f * Mathf.PI / Period);
            var width = canvas.rect.width;
            if (motion == PagesMotion.Swipe)
                strip.anchoredPosition = new Vector2(-width * (0.5f - 0.5f * Mathf.Cos(phase)), 0f);
            else if (motion == PagesMotion.Offscreen)
                pages[2].anchoredPosition = new Vector2(width * (0.45f * Mathf.Sin(phase) - 0.45f), 0f);
        }

        protected override int Load => labels.Count;

        protected override string Variant =>
            JoinVariant(VariantWord(motion), mask == ScenarioMask.LightSide ? "lsmask" : null);

        public override void Validate()
        {
            RequireGlyphs(root);
            RequireSingleLines(labels);
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, labels, TypeStep.Dense, 0);
            workload["masks"] = Pages;
            workload["mask"] = mask.ToString();
            workload["motion"] = motion switch
            {
                PagesMotion.Swipe => "strip between pages 0 and 1",
                PagesMotion.Offscreen => "page 0 still, page 2 sliding 1.1-2 screens right",
                _ => "nothing moves"
            };
        }
    }

    enum ScenarioMask
    {
        RectMask2D,
        LightSide
    }

    enum WorldMotion
    {
        None,
        Moving,
        Animated
    }

    /// <summary>
    /// Nameplate-like world-space labels at 36 points spread over four depth bands of the camera's view, no two labels of
    /// one band overlapping; <c>changing</c> of them, spread evenly, are seven-digit counters that change every frame.
    /// </summary>
    /// <remarks>Animated labels need UniText's <see cref="WaveModifier"/>; TMP has no counterpart.</remarks>
    sealed class WorldScenario : UniTextScenario
    {
        const float FontSize = 36f;

        /// <summary>A nameplate's cell in world units: the longest label with its effects and its sway, so neighbors never touch.</summary>
        static readonly Vector2 Cell = new(3.9f, 0.66f);

        static readonly Vector2 Sway = new(0.15f, 0.05f);
        static readonly float[] BandDepths = { 30f, 24f, 18f, 12f };

        readonly WorldMotion motion;
        readonly int count;
        readonly ScenarioLabel[] labels;
        readonly Vector3[] anchors;
        readonly int[] changed;
        readonly char[][] buffers;
        Transform root;

        public WorldScenario(string id, WorldMotion motion, ScenarioEngine engine, int count, int changing,
            bool parallel = true) : base(id, parallel, engine)
        {
            this.motion = motion;
            this.count = count;
            labels = new ScenarioLabel[count];
            anchors = new Vector3[count];
            changed = new int[changing];
            buffers = new char[changing][];
        }

        protected override void Build(ScenarioRig rig)
        {
            root = rig.CreateWorldRoot(Id);
            Spread(rig.Camera, anchors);
            var names = Labels(count, 3200);
            var stride = changed.Length > 0 ? count / changed.Length : 0;
            for (var k = 0; k < changed.Length; k++)
            {
                changed[k] = k * stride;
                buffers[k] = Digits(StillValue(changed[k])).ToCharArray();
                names[changed[k]] = new string(buffers[k]);
            }
            for (var i = 0; i < count; i++)
            {
                labels[i] = rig.CreateWorldLabel(TextEngine, root, names[i], FontSize);
                labels[i].Rect.position = anchors[i];
                if (motion == WorldMotion.Animated)
                    labels[i].UniText.SetWholeText<WaveModifier>();
            }
            if (changed.Length > 0) ExpectCommitEveryFrame(labels[changed[0]].Graphic);
        }

        /// <summary>Fills <paramref name="positions"/> with label centres spread over the depth bands in proportion to the cells each band holds.</summary>
        static void Spread(Camera camera, Vector3[] positions)
        {
            var tan = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var columns = new int[BandDepths.Length];
            var rows = new int[BandDepths.Length];
            var total = 0;
            for (var b = 0; b < BandDepths.Length; b++)
            {
                var height = 2f * BandDepths[b] * tan * 0.9f;
                columns[b] = Mathf.Max(1, Mathf.FloorToInt(height * camera.aspect / Cell.x));
                rows[b] = Mathf.Max(1, Mathf.FloorToInt(height / Cell.y));
                total += columns[b] * rows[b];
            }
            if (total < positions.Length)
                throw new InvalidOperationException(
                    $"The camera's view holds {total} nameplates without overlap, fewer than {positions.Length}.");

            var inBands = new int[BandDepths.Length];
            var assigned = 0;
            for (var b = 0; b < BandDepths.Length; b++)
            {
                inBands[b] = (int)((long)positions.Length * columns[b] * rows[b] / total);
                assigned += inBands[b];
            }
            for (var b = 0; assigned < positions.Length; b++)
                if (inBands[b] < columns[b] * rows[b])
                {
                    inBands[b]++;
                    assigned++;
                }

            var placed = 0;
            for (var b = 0; b < BandDepths.Length; b++)
            {
                var cells = columns[b] * rows[b];
                for (var j = 0; j < inBands[b]; j++)
                {
                    var cell = (int)((long)j * cells / inBands[b]);
                    var x = 0.05f + 0.9f * (cell % columns[b] + 0.5f) / columns[b];
                    var y = 0.05f + 0.9f * (cell / columns[b] + 0.5f) / rows[b];
                    positions[placed + j] = camera.ViewportToWorldPoint(new Vector3(x, y, BandDepths[b]));
                }
                placed += inBands[b];
            }
        }

        public override void Step(int frame)
        {
            var still = frame == UniTextScenarioSuite.StillFrame;
            if (motion == WorldMotion.Moving)
                for (var i = 0; i < count; i++)
                {
                    var phase = (still ? 0 : frame % MotionPeriodFrames) * 0.05f + i * 0.37f;
                    labels[i].Rect.position = anchors[i] +
                                              new Vector3(Mathf.Sin(phase) * Sway.x, Mathf.Cos(phase * 1.3f) * Sway.y, 0f);
                }
            for (var k = 0; k < changed.Length; k++)
            {
                WriteDigits(buffers[k], 0, CounterDigits, still ? StillValue(changed[k]) : NextCounterValue());
                labels[changed[k]].SetText(buffers[k]);
            }
        }

        protected override int Load => motion != WorldMotion.None || changed.Length == 0 ? count : changed.Length;

        protected override string Variant => JoinVariant(motion == WorldMotion.None ? null : VariantWord(motion),
            TextEngine == ScenarioEngine.TmpLayered ? "tmpTwoPass" : base.Variant);

        public override void Validate()
        {
            RequireGlyphs(root);
            RequireSingleLines(labels);
        }

        public override void Describe(JObject workload)
        {
            DescribeLabels(workload, labels, FontSize, motion == WorldMotion.Animated ? count : changed.Length);
            workload["motion"] = motion.ToString();
            workload["counters"] = changed.Length;
            if (changed.Length > 0) workload["api"] = "SetText(char[], int, int)";
        }
    }
}
