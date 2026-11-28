using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// uGUI half: instantiates the assigned prefab into a canvas of the engine's own, which copies the scene canvas's render
/// mode and scaler and starts with no additional shader channels, so each engine draws with only the channels it adds
/// itself and no other graphic is rebuilt or batched with its texts. Concretes only bind the text component and its
/// property setters.
/// </summary>
public abstract class UGuiTextBenchmarkBase : TextBenchmarkBase<Component>
{
    [Header("Prefab")]
    public GameObject prefab;

    protected Transform container;

    private Canvas measuredCanvas;

    protected abstract Component GetInstanceComponent(GameObject go);

    protected override bool ValidateSetup()
    {
        if (prefab != null && GetComponentInParent<Canvas>() != null) return true;
        Debug.LogError($"{SystemName} benchmark needs its prefab and a scene canvas to copy.");
        return false;
    }

    protected override void SetupContainer()
    {
        var source = GetComponentInParent<Canvas>().rootCanvas;
        var root = new GameObject($"{SystemName} measured canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.layer = source.gameObject.layer;
        measuredCanvas = root.GetComponent<Canvas>();
        measuredCanvas.renderMode = source.renderMode;
        measuredCanvas.worldCamera = source.worldCamera;
        measuredCanvas.planeDistance = source.planeDistance;
        measuredCanvas.sortingOrder = source.sortingOrder;
        measuredCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;
        var scaler = root.GetComponent<CanvasScaler>();
        var sourceScaler = source.GetComponent<CanvasScaler>();
        if (sourceScaler != null)
        {
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.referenceResolution = sourceScaler.referenceResolution;
            scaler.screenMatchMode = sourceScaler.screenMatchMode;
            scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            scaler.scaleFactor = sourceScaler.scaleFactor;
        }

        var go = new GameObject($"{SystemName}BenchmarkContainer", typeof(RectTransform));
        container = go.transform;
        container.SetParent(root.transform, false);
        var rt = (RectTransform)container;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    protected override void TeardownContainer()
    {
        if (measuredCanvas != null)
            Destroy(measuredCanvas.gameObject);
        measuredCanvas = null;
        container = null;
    }

    public override bool Visible
    {
        set
        {
            if (measuredCanvas != null) measuredCanvas.enabled = value;
        }
    }

    protected override Component CreateInstance(int index)
    {
        var go = Instantiate(prefab, container);
        go.SetActive(true);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(TextPhases.TopMargin + index * TextPhases.PageStride));
        rt.sizeDelta = new Vector2(TextPhases.PageWidth, TextPhases.PageHeight);
        return GetInstanceComponent(go);
    }

    protected override void SetInspectionName(Component instance, int index)
    {
        if (instance != null)
            instance.gameObject.name = $"{SystemName}_{index:000}";
    }

    protected override string DescribeInspectionInstance(Component instance, int index)
    {
        if (instance == null) return "null";
        var rt = instance.transform as RectTransform;
        var rect = rt != null ? $"{rt.rect.width:F1}x{rt.rect.height:F1}" : "no RectTransform";
        var position = rt != null ? rt.anchoredPosition.ToString("F1") : "n/a";
        return $"{instance.gameObject.name}: component={instance.GetType().Name}, active={instance.gameObject.activeInHierarchy}, rect={rect}, anchored={position}";
    }

    protected override void DestroyInstance(Component instance)
    {
        if (instance != null)
            Destroy(instance.gameObject);
    }

    protected override void SetRectSize(Component instance, float width, float height)
    {
        var rt = (RectTransform)instance.transform;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }
}
