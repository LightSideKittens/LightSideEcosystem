using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using LightSide;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using Object = UnityEngine.Object;

internal enum LookCalibrationEngine
{
    UniText,
    Tmp,
    UIToolkit
}

/// <summary>
/// The screen the look calibration draws on: labels of every engine made from the text suite's own engine setups,
/// placed in screen pixels from the top left, over the main camera's clear color with every other canvas hidden.
/// </summary>
/// <remarks>Sizes are in the scenario rig's units (1080 × 1920 reference, match 0.5); UI Toolkit labels are converted to the same pixels.</remarks>
internal sealed class LookCalibrationStage : IDisposable
{
    const float RigMatch = 0.5f;
    const int SortingOrder = 30000;

    readonly GameObject uniTextSource;
    readonly GameObject tmpSource;
    readonly TMP_FontAsset tmpFont;
    readonly Material tmpPreset;
    readonly Camera camera;
    readonly CameraClearFlags clearFlags;
    readonly Color clearColor;
    readonly List<Canvas> hiddenCanvases = new();
    readonly GameObject canvasObject;
    readonly Canvas canvas;
    readonly RectTransform canvasRect;
    readonly List<GameObject> spawned = new();
    readonly Dictionary<LookCalibrationLook, Material> tmpMaterials = new();
    readonly Texture2D readback;
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
    readonly UIToolkitBenchmark uiToolkit;
    readonly Dictionary<float, FontAsset> uiToolkitFonts = new();
    VisualElement container;
#endif

    public LookCalibrationStage()
    {
        uniTextSource = Require(ObjectUtils.FindAny<UniTextBenchmark>(), nameof(UniTextBenchmark)).prefab;
        tmpSource = Require(ObjectUtils.FindAny<TMPBenchmark>(), nameof(TMPBenchmark)).prefab;
        var tmpTemplate = Require(tmpSource.GetComponent<TMP_Text>(), "TMP benchmark prefab text");
        tmpFont = tmpTemplate.font;
        tmpPreset = tmpTemplate.fontSharedMaterial;
        camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException("The look calibration clears the screen through the scene's main camera, and there is none.");
        clearFlags = camera.clearFlags;
        clearColor = camera.backgroundColor;

        var engines = new List<LookCalibrationEngine> { LookCalibrationEngine.UniText, LookCalibrationEngine.Tmp };
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        uiToolkit = Require(ObjectUtils.FindAny<UIToolkitBenchmark>(), nameof(UIToolkitBenchmark));
        engines.Add(LookCalibrationEngine.UIToolkit);
#endif
        Engines = engines;

        foreach (var other in ObjectUtils.FindAll<Canvas>())
        {
            if (!other.isRootCanvas || !other.enabled) continue;
            other.enabled = false;
            hiddenCanvases.Add(other);
        }

        canvasObject = new GameObject("LookCalibrationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler))
        {
            layer = 5
        };
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ScenarioRig.ReferenceWidth, ScenarioRig.ReferenceHeight);
        scaler.matchWidthOrHeight = RigMatch;
        canvasRect = (RectTransform)canvasObject.transform;
        readback = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
    }

    /// <summary>The engines this player can draw, UniText first.</summary>
    public IReadOnlyList<LookCalibrationEngine> Engines { get; }

    /// <summary>Screen pixels per rig unit.</summary>
    public float CanvasScale => canvas.scaleFactor;

    /// <summary>Screen pixels per UI Toolkit panel unit; 1 where the player has no UI Toolkit.</summary>
    public float PanelScale { get; private set; } = 1f;

    public int Width => readback.width;
    public int Height => readback.height;

    /// <summary>Lets the canvas scaler and the UI Toolkit panel resolve their scales; required before any label is added.</summary>
    /// <exception cref="InvalidOperationException">The UI Toolkit panel never laid out or its fonts cannot be isolated.</exception>
    public IEnumerator Prepare()
    {
        for (var frame = 0; frame < 3; frame++)
            yield return null;
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        var root = uiToolkit.RootElement;
        if (root?.panel == null)
            throw new InvalidOperationException("The UI Toolkit bench panel has no root element.");
        if (!UIToolkitFontIsolation.Isolate(uiToolkit.ActivePanelSettings, null, out var error))
            throw new InvalidOperationException($"UI Toolkit font isolation failed: {error}");
        var panelWidth = root.panel.visualTree.layout.width;
        if (!(panelWidth > 0f))
            throw new InvalidOperationException("The UI Toolkit bench panel has no layout width.");
        PanelScale = Screen.width / panelWidth;
        container = new VisualElement { name = "LookCalibration" };
        container.style.position = Position.Absolute;
        container.style.width = Length.Percent(100);
        container.style.height = Length.Percent(100);
        root.Add(container);
#endif
    }

    /// <summary>Clears the screen to <paramref name="color"/> behind every label.</summary>
    public void Background(Color color)
    {
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = color;
    }

    /// <summary>
    /// Adds a centered single-line label of <paramref name="engine"/> inside <paramref name="area"/> (screen pixels from the
    /// top left), drawn plain when <paramref name="look"/> is null.
    /// </summary>
    public void Add(LookCalibrationEngine engine, string value, float units, Rect area, Color face,
        LookCalibrationLook? look, float trackingEm = 0f, bool rightToLeft = false)
    {
        switch (engine)
        {
            case LookCalibrationEngine.UniText:
                AddUniText(value, units, area, face, look, trackingEm);
                break;
            case LookCalibrationEngine.Tmp:
                AddTmp(value, units, area, face, look, trackingEm);
                break;
            default:
                AddUIToolkit(value, units, area, face, look, trackingEm, rightToLeft);
                break;
        }
    }

    /// <summary>Removes every label.</summary>
    public void Clear()
    {
        foreach (var go in spawned)
            Object.Destroy(go);
        spawned.Clear();
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        container?.Clear();
#endif
    }

    /// <summary>Frames every engine needs before a new label is on screen.</summary>
    public static IEnumerator Settle()
    {
        for (var frame = 0; frame < 10; frame++)
            yield return null;
    }

    /// <summary>The screen as drawn this frame; call only after <c>WaitForEndOfFrame</c>.</summary>
    public LookCalibrationFrame Read()
    {
        var active = RenderTexture.active;
        RenderTexture.active = null;
        readback.ReadPixels(new Rect(0, 0, readback.width, readback.height), 0, 0, false);
        RenderTexture.active = active;
        return new LookCalibrationFrame(readback.GetPixels32(), readback.width, readback.height,
            QualitySettings.activeColorSpace == ColorSpace.Linear);
    }

    /// <summary>The outline and underlay material that draws <paramref name="look"/> in TextMesh Pro.</summary>
    public Material TmpMaterial(in LookCalibrationLook look)
    {
        if (tmpMaterials.TryGetValue(look, out var material)) return material;
        material = LookCalibrationProfile.CreateTmpMaterial(tmpPreset, tmpFont, look);
        tmpMaterials.Add(look, material);
        return material;
    }

    /// <summary>The TextMesh Pro font asset of the bench; the material parameters depend on its sampling size.</summary>
    public TMP_FontAsset TmpFont => tmpFont;

#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
    /// <summary>The UI Toolkit font whose synthetic bold matches the outline of <paramref name="look"/>.</summary>
    public FontAsset UIToolkitFont(in LookCalibrationLook look)
    {
        if (uiToolkitFonts.TryGetValue(look.OutlineEm, out var font)) return font;
        font = LookCalibrationProfile.CreateUIToolkitFont(uiToolkit.font, uiToolkit.fallbackFonts, look);
        uiToolkitFonts.Add(look.OutlineEm, font);
        return font;
    }
#endif

    public void Dispose()
    {
        Clear();
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        container?.RemoveFromHierarchy();
        foreach (var font in uiToolkitFonts.Values)
        {
            foreach (var fallback in font.fallbackFontAssetTable)
                Object.Destroy(fallback);
            Object.Destroy(font);
        }
        uiToolkitFonts.Clear();
#endif
        foreach (var material in tmpMaterials.Values)
            Object.Destroy(material);
        tmpMaterials.Clear();
        Object.Destroy(canvasObject);
        Object.Destroy(readback);
        foreach (var hidden in hiddenCanvases)
            if (hidden != null)
                hidden.enabled = true;
        hiddenCanvases.Clear();
        if (camera == null) return;
        camera.clearFlags = clearFlags;
        camera.backgroundColor = clearColor;
    }

    void AddUniText(string value, float units, Rect area, Color face, LookCalibrationLook? look, float trackingEm)
    {
        var text = Spawn<UniText>(uniTextSource);
        text.ClearWholeText<StrokeModifier>();
        text.ClearWholeText<ShadowModifier>();
        text.ClearWholeText<LetterSpacingModifier>();
        text.WordWrap = false;
        text.AutoSize = false;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Middle;
        text.FontSize = units;
        text.color = face;
        if (look.HasValue)
            LookCalibrationProfile.ApplyUniText(text, look.Value);
        if (trackingEm != 0f)
            text.SetWholeText<LetterSpacingModifier>(trackingEm.ToString("0.###", CultureInfo.InvariantCulture) + "em");
        text.Text = value;
        Place(text.rectTransform, area);
    }

    void AddTmp(string value, float units, Rect area, Color face, LookCalibrationLook? look, float trackingEm)
    {
        var text = Spawn<TMP_Text>(tmpSource);
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSharedMaterial = look.HasValue ? TmpMaterial(look.Value) : tmpFont.material;
        text.fontSize = units;
        text.color = face;
        text.characterSpacing = trackingEm * 100f;
        text.text = value;
        Place((RectTransform)text.transform, area);
    }

    void AddUIToolkit(string value, float units, Rect area, Color face, LookCalibrationLook? look, float trackingEm,
        bool rightToLeft)
    {
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
        if (container == null)
            throw new InvalidOperationException("Prepare the stage before adding UI Toolkit labels.");
        var size = units * CanvasScale / PanelScale;
        var label = new Label(value);
#if UNITY_2023_2_OR_NEWER
        label.emojiFallbackSupport = false;
#endif
#if UNITY_6000_0_OR_NEWER
        label.style.unityTextGenerator = TextGeneratorType.Advanced;
#endif
        label.style.unityFontDefinition = FontDefinition.FromSDFFont(UIToolkitFont(look ?? LookCalibrationProfile.House));
        label.style.fontSize = size;
        label.style.color = face;
        label.style.position = Position.Absolute;
        label.style.left = area.x / PanelScale;
        label.style.top = area.y / PanelScale;
        label.style.width = area.width / PanelScale;
        label.style.height = area.height / PanelScale;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        if (trackingEm != 0f)
            label.style.letterSpacing = trackingEm * size;
        if (rightToLeft)
            label.languageDirection = LanguageDirection.RTL;
        if (look.HasValue)
            LookCalibrationProfile.ApplyUIToolkit(label, look.Value, size);
        container.Add(label);
#else
        throw new InvalidOperationException("This player has no UI Toolkit text.");
#endif
    }

    T Spawn<T>(GameObject source) where T : Component
    {
        var go = Object.Instantiate(source, canvasRect, false);
        spawned.Add(go);
        go.SetActive(true);
        var component = go.GetComponent<T>();
        if (component == null)
            throw new InvalidOperationException($"'{source.name}' has no {typeof(T).Name}.");
        return component;
    }

    void Place(RectTransform rect, Rect area)
    {
        var scale = CanvasScale;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(area.x / scale, -area.y / scale);
        rect.sizeDelta = new Vector2(area.width / scale, area.height / scale);
    }

    static T Require<T>(T component, string role) where T : Object
    {
        if (component == null)
            throw new InvalidOperationException($"The look calibration needs the {role} of the text benchmark scene.");
        return component;
    }
}
