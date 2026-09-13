using LightSide;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public class UIToolkitBenchmark : TextBenchmarkBase<Label>
{
    [Header("UI Toolkit")]
    public PanelSettings panelSettings;

    [Tooltip("UXML template containing a Label to clone")]
    public VisualTreeAsset labelTemplate;

    public override string SystemName => "UIToolkit";

    /// <summary>uGUI benchmark rect width — UITK labels must wrap at the same width or they lay out a different line count than the other engines.</summary>
    const float UGuiRectWidth = 1068.9f;

    VisualElement container;

    /// <summary>Raised when the live panel rebuilds its root mid-run (world-space UI reload on 6000.5+);
    /// consumers re-attach their elements and invalidate any in-flight measurement.</summary>
#pragma warning disable 67
    public event System.Action RootReloaded;
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

    protected override void OnBeforeAllTests()
    {
        EnsurePanel();
        if (!UIToolkitFontIsolation.Validate(ActivePanelSettings, null, out var error))
            throw new System.InvalidOperationException($"UI Toolkit font isolation failed: {error}");
        Debug.Log("[UIToolkit] Font fallback isolation: local=none, global=none, default=none, emoji=off, Dynamic OS=off.");
    }
    protected override void OnAfterAllTests() { }

    protected override bool ValidateSetup()
    {
        EnsurePanel();
        if (ActivePanelSettings != null) return true;
        Debug.LogError("UI Toolkit panel or PanelSettings not assigned!");
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
        if (container != null)
        {
            container.RemoveFromHierarchy();
            container = null;
        }
    }

    protected override Label CreateInstance(int index)
    {
        Label label = null;
        if (labelTemplate != null)
        {
            label = labelTemplate.CloneTree().Q<Label>();
            if (label == null)
                Debug.LogWarning("No Label found in UXML template, using fallback");
        }

        label ??= new Label();
#if UNITY_2023_2_OR_NEWER
        label.emojiFallbackSupport = false;
#endif
#if UNITY_6000_0_OR_NEWER
        label.style.unityTextGenerator = TextGeneratorType.Advanced;
#endif
        label.style.position = Position.Absolute;
        label.style.left = 10;
        label.style.top = 10 + index * 50;
        label.style.width = UGuiRectWidth;
        container.Add(label);
        return label;
    }

    protected override void SetInspectionName(Label instance, int index, BenchmarkInspectionPhase phase)
    {
        if (instance != null)
            instance.name = $"{SystemName}_{phase}_{index:000}";
    }

    protected override int CountInspectionObjects() => container != null ? container.childCount : 0;

    protected override string DescribeInspectionInstance(Label instance, int index)
    {
        if (instance == null) return "null";
        return $"{instance.name}: textLen={instance.text?.Length ?? 0}, richText={instance.enableRichText}, whiteSpace={instance.style.whiteSpace}, left={instance.style.left}, top={instance.style.top}, width={instance.style.width}, height={instance.style.height}, fontSize={instance.style.fontSize}, color={instance.style.color}";
    }

    protected override void DestroyInstance(Label instance) => instance?.RemoveFromHierarchy();

    protected override void SetText(Label instance, string text) => instance.text = text;
    protected override void SetFontSize(Label instance, float size) => instance.style.fontSize = new Length(size, LengthUnit.Pixel);
    protected override void SetColor(Label instance, Color color) => instance.style.color = new StyleColor(color);
    protected override void SetWordWrap(Label instance, bool enabled) => instance.style.whiteSpace = enabled ? WhiteSpace.Normal : WhiteSpace.NoWrap;
    protected override void SetRichText(Label instance, bool enabled) => instance.enableRichText = enabled;

    protected override void SetAutoSize(Label instance, bool enabled)
    {
#if UNITY_6000_0_OR_NEWER
        instance.style.unityTextAutoSize = enabled
            ? new StyleTextAutoSize(new TextAutoSize(TextAutoSizeMode.BestFit, minSize: 10, maxSize: 72))
            : new StyleTextAutoSize(new TextAutoSize(TextAutoSizeMode.None, 28, 28));
#endif
    }

    protected override void SetRectSize(Label instance, float width, float height)
    {
        instance.style.width = new Length(width, LengthUnit.Pixel);
        instance.style.height = new Length(height, LengthUnit.Pixel);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("UniText/Run UIToolkit Benchmark")]
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
