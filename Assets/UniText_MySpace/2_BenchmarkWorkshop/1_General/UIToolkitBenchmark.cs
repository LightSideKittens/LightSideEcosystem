#if !LIGHTSIDE_NO_UITOOLKIT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LightSide;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;

/// <summary>
/// UI Toolkit half of the text benchmark. A text is one <see cref="Label"/>, or, where the corpus mixes base directions
/// and the phase does not auto-size, a column of one label per paragraph with that paragraph's direction: a label has
/// one direction, so a single label lays every right-to-left paragraph out left to right. Auto-size fits one label to its
/// rect, so auto-size phases keep the single label and its left-to-right layout of those paragraphs.
/// </summary>
public class UIToolkitBenchmark : TextBenchmarkBase<VisualElement>
{
    [Header("UI Toolkit")]
    public PanelSettings panelSettings;

    [Tooltip("UXML template containing the Label every text clones, UI Toolkit's prefab.")]
    public VisualTreeAsset labelTemplate;

    [Tooltip("Font the labels render with: the file the TMP and UniText benchmarks use as their primary font.")]
    public Font font;

    [Tooltip("Fonts the labels fall back to, in order, for scripts the primary font lacks: the files the TMP and UniText benchmarks fall back to.")]
    public Font[] fallbackFonts;

    public override string SystemName => "UIToolkit";

    static readonly string[] Engines = { BenchmarkEngines.UIToolkit };

    public override IReadOnlyList<string> EngineIds => Engines;

    VisualElement container;
    FontAsset fontAsset;
    bool[] rightToLeft;
    bool paragraphLabels;
    readonly Dictionary<string, string[]> paragraphs = new(ReferenceComparer.Instance);

    sealed class ReferenceComparer : IEqualityComparer<string>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(string a, string b) => ReferenceEquals(a, b);
        public int GetHashCode(string s) => RuntimeHelpers.GetHashCode(s);
    }

    /// <summary>Raised when the live panel rebuilds its root mid-run (world-space UI reload on 6000.5+);
    /// consumers re-attach their elements and invalidate any in-flight measurement.</summary>
#pragma warning disable 67
    public event Action RootReloaded;
#pragma warning restore 67

    /// <summary>The panel component is runtime-ensured, never serialized: the newest presentation path the
    /// editor offers (world-space PanelRenderer on 6000.5+, UIDocument before) — a scene asset must stay
    /// loadable on every supported Unity version.</summary>
#if UNITY_6000_5_OR_NEWER
    PanelRenderer panelRenderer;
    VisualElement root;
    int uiVersion;
    bool reloadHooked;

    internal VisualElement RootElement => root;
    internal PanelSettings ActivePanelSettings => panelRenderer != null ? panelRenderer.panelSettings : null;
#else
    UIDocument uiDocument;

    internal VisualElement RootElement => uiDocument != null ? uiDocument.rootVisualElement : null;
    internal PanelSettings ActivePanelSettings => uiDocument != null ? uiDocument.panelSettings : null;
#endif

    private void Start() => EnsurePanel();

    private void EnsurePanel()
    {
#if UNITY_6000_5_OR_NEWER
        if (panelRenderer == null)
        {
            panelRenderer = GetComponent<PanelRenderer>();
            if (panelRenderer == null)
                panelRenderer = gameObject.AddComponent<PanelRenderer>();
        }

        if (panelSettings != null && panelRenderer.panelSettings == null)
            panelRenderer.panelSettings = panelSettings;

        if (!reloadHooked)
        {
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
            reloadHooked = true;
        }
#else
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
                uiDocument = gameObject.AddComponent<UIDocument>();
        }

        if (panelSettings != null && uiDocument.panelSettings == null)
            uiDocument.panelSettings = panelSettings;
#endif
    }

#if UNITY_6000_5_OR_NEWER
    /// <summary>The reload callback is PanelRenderer's only public path to the root element; the root is rebuilt on every UI reload, so a live container re-attaches here.</summary>
    private void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
    {
        if (root == rootElement && uiVersion == version) return;
        root = rootElement;
        uiVersion = version;
        if (container != null)
            rootElement.Add(container);
        RootReloaded?.Invoke();
    }

    private void OnDestroy()
    {
        if (reloadHooked && panelRenderer != null)
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);
    }
#endif

    public override void SelectEngine(string engineId)
    {
        if (engineId != Engines[0]) throw new ArgumentException($"UI Toolkit measures no engine '{engineId}'.", nameof(engineId));
    }

    protected override void OnBeginSession()
    {
        EnsurePanel();
        if (!UIToolkitFontIsolation.Isolate(ActivePanelSettings, null, out var error))
            throw new InvalidOperationException($"UI Toolkit font isolation failed: {error}");
        if (fontAsset == null)
            fontAsset = LookCalibrationProfile.CreateUIToolkitFont(font, fallbackFonts, LookCalibrationProfile.House);
        var lines = TextPhases.PlainText(Corpus).Split('\n');
        rightToLeft = new bool[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            rightToLeft[i] = TextPhases.StartsRightToLeft(lines[i]);
        Debug.Log($"[UIToolkit] Fonts: primary={font.name}, local fallbacks={string.Join(", ", Array.ConvertAll(fallbackFonts, f => f.name))}; global, default, emoji and Dynamic OS fallbacks off.");
    }

    protected override void OnEndSession()
    {
        paragraphs.Clear();
        rightToLeft = null;
    }

    protected override bool ValidateSetup()
    {
        EnsurePanel();
        if (ActivePanelSettings != null && labelTemplate != null) return true;
        Debug.LogError("UI Toolkit panel, PanelSettings or label template not assigned!");
        return false;
    }

    protected override void SetupContainer()
    {
        container = new VisualElement { name = "BenchmarkContainer" };
        container.style.position = Position.Absolute;
        container.style.width = Length.Percent(100);
        container.style.height = Length.Percent(100);
        RootElement?.Add(container);
    }

    protected override void TeardownContainer()
    {
        if (container == null) return;
        container.RemoveFromHierarchy();
        container = null;
    }

    public override bool Visible
    {
        set
        {
            if (container != null) container.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public override void SetStructure(TextPhase phase)
    {
        var mixed = false;
        foreach (var rtl in rightToLeft)
            mixed |= rtl;
        paragraphLabels = mixed && !TextPhases.AutoSize(phase);
    }

    public override void PrepareInputs(string[] inputs)
    {
        if (inputs == null) return;
        foreach (var input in inputs)
            Paragraphs(input);
    }

    string[] Paragraphs(string text)
    {
        if (!paragraphs.TryGetValue(text, out var split))
            paragraphs[text] = split = text.Split('\n');
        return split;
    }

    protected override VisualElement CreateInstance(int index)
    {
        VisualElement text;
        if (paragraphLabels)
        {
            text = new VisualElement { name = "Paragraphs" };
            text.style.flexDirection = FlexDirection.Column;
            for (var i = 0; i < rightToLeft.Length; i++)
            {
                var label = CreateLabel();
                label.style.color = StyleKeyword.Null;
                label.style.fontSize = StyleKeyword.Null;
                label.style.whiteSpace = StyleKeyword.Null;
                label.languageDirection = rightToLeft[i] ? LanguageDirection.RTL : LanguageDirection.LTR;
                text.Add(label);
            }
        }
        else
        {
            text = CreateLabel();
        }
        text.style.unityFontDefinition = FontDefinition.FromSDFFont(fontAsset);
        ApplyLook(text, TextPhases.FontSize);
        text.style.position = Position.Absolute;
        text.style.left = Length.Percent(50);
        text.style.marginLeft = -TextPhases.PageWidth / 2f;
        text.style.top = TextPhases.TopMargin + index * TextPhases.PageStride;
        text.style.width = TextPhases.PageWidth;
        text.style.height = TextPhases.PageHeight;
        container.Add(text);
        return text;
    }

    Label CreateLabel()
    {
        var label = labelTemplate.CloneTree().Q<Label>()
                    ?? throw new InvalidOperationException($"The UXML template {labelTemplate.name} holds no Label.");
#if UNITY_2023_2_OR_NEWER
        label.emojiFallbackSupport = false;
#endif
#if UNITY_6000_0_OR_NEWER
        label.style.unityTextGenerator = TextGeneratorType.Advanced;
#endif
        return label;
    }

    protected override bool LaidOut(VisualElement instance)
    {
        var height = instance.layout.height;
        return !float.IsNaN(height) && height > 0f;
    }

    protected override void SetInspectionName(VisualElement instance, int index)
    {
        if (instance != null)
            instance.name = $"{SystemName}_{index:000}";
    }

    protected override string DescribeInspectionInstance(VisualElement instance, int index)
    {
        if (instance == null) return "null";
        var labels = instance is Label ? 1 : instance.childCount;
        return $"{instance.name}: labels={labels}, whiteSpace={instance.style.whiteSpace}, left={instance.style.left}, top={instance.style.top}, width={instance.style.width}, height={instance.style.height}, fontSize={instance.style.fontSize}, color={instance.style.color}";
    }

    /// <summary>
    /// Detaches the text and, where UI Toolkit has the call (6000.5 and later), releases its layout nodes, text buffers
    /// and callbacks at once instead of at the next collection: the work the other engines' destruction pays.
    /// </summary>
    protected override void DestroyInstance(VisualElement instance)
    {
        if (instance == null) return;
        instance.RemoveFromHierarchy();
#if UNITY_6000_5_OR_NEWER
        if (instance is Label single)
        {
            single.ReleaseResources();
            return;
        }
        for (var i = 0; i < instance.childCount; i++)
            ((Label)instance[i]).ReleaseResources();
#endif
    }

    protected override void SetText(VisualElement instance, string text)
    {
        if (instance is Label label)
        {
            label.text = text;
            return;
        }
        var lines = Paragraphs(text);
        if (lines.Length != instance.childCount)
            throw new InvalidOperationException($"A text of {lines.Length} paragraphs reached a column of {instance.childCount} labels.");
        for (var i = 0; i < lines.Length; i++)
            ((Label)instance[i]).text = lines[i];
    }

    protected override void SetRichText(VisualElement instance, bool enabled)
    {
        if (instance is Label label)
        {
            label.enableRichText = enabled;
            return;
        }
        for (var i = 0; i < instance.childCount; i++)
            ((Label)instance[i]).enableRichText = enabled;
    }

    protected override void SetFontSize(VisualElement instance, float size)
    {
        instance.style.fontSize = new Length(size, LengthUnit.Pixel);
        ApplyLook(instance, size);
    }

    /// <summary>Gives every label of <paramref name="instance"/> the house outline and shadow at <paramref name="fontSize"/>: UI Toolkit sizes them in panel units, so a size change restyles the labels.</summary>
    static void ApplyLook(VisualElement instance, float fontSize)
    {
        if (UniTextBenchmarkEnvironment.Plain) return;
        if (instance is Label label)
        {
            LookCalibrationProfile.ApplyUIToolkit(label, LookCalibrationProfile.House, fontSize);
            return;
        }
        for (var i = 0; i < instance.childCount; i++)
            LookCalibrationProfile.ApplyUIToolkit((Label)instance[i], LookCalibrationProfile.House, fontSize);
    }
    protected override void SetColor(VisualElement instance, Color color) => instance.style.color = new StyleColor(color);
    protected override void SetWordWrap(VisualElement instance, bool enabled) => instance.style.whiteSpace = enabled ? WhiteSpace.Normal : WhiteSpace.NoWrap;

    protected override void SetAutoSize(VisualElement instance, bool enabled)
    {
#if UNITY_6000_0_OR_NEWER
        if (enabled && instance is not Label)
            throw new InvalidOperationException("Auto-size phases build single labels.");
        instance.style.unityTextAutoSize = enabled
            ? new StyleTextAutoSize(new TextAutoSize(TextAutoSizeMode.BestFit, minSize: 10, maxSize: 72))
            : new StyleTextAutoSize(new TextAutoSize(TextAutoSizeMode.None, 28, 28));
#endif
    }

    protected override void SetRectSize(VisualElement instance, float width, float height)
    {
        instance.style.width = new Length(width, LengthUnit.Pixel);
        instance.style.height = new Length(height, LengthUnit.Pixel);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("UniText/Run UIToolkit Memory Benchmark")]
    private static void RunFromMenu()
    {
        var test = ObjectUtils.FindAny<UIToolkitBenchmark>();
        if (test != null)
            test.RunBenchmark();
        else
            Debug.LogError("No UIToolkitBenchmark found in scene. Add UIToolkitBenchmark component to a GameObject with UIDocument.");
    }
#endif
}
#endif
