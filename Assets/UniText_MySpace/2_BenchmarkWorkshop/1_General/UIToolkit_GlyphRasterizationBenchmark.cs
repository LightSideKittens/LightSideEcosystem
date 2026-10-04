using System.Collections;
using System.Collections.Generic;
using LightSide;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

/// <summary>
/// UI Toolkit glyph rasterization. Clears a dynamic TextCore <see cref="FontAsset"/>, enables a
/// live <see cref="Label"/>, and measures until that panel render has populated the atlas.
/// </summary>
public class UIToolkit_GlyphRasterizationBenchmark : GlyphRasterBenchmarkBase
{
    const double RenderTimeoutSeconds = 30.0;

    [Tooltip("Dynamic TextCore FontAsset under test. Assigned per font by BenchmarkFontSelector.")]
    public FontAsset fontAsset;

    string glyphText;
    UIToolkitBenchmark visualBenchmark;
    VisualElement previewContainer;
    Label previewLabel;
    bool abortRun;
    int panelVersion;
    int rasterPanelVersion;
    int renderSequence;
    int rasterRenderSequence;
    int rasterGlyphCount;
    readonly WaitForEndOfFrame waitForEndOfFrame = new();

    VisualElement Root => visualBenchmark != null ? visualBenchmark.RootElement : null;

    protected override string EngineName => "UIToolkit";
    protected override bool HasE2E => true;

    public IEnumerator RunBenchmarkCoroutine() => RunPass(null);

    void Start() => EnsurePreviewPanel();

    void OnDestroy()
    {
        if (visualBenchmark != null)
            visualBenchmark.RootReloaded -= OnRootReloaded;
        if (previewLabel != null)
            previewLabel.generateVisualContent -= OnGenerateVisualContent;
    }

    [ContextMenu("Run Benchmark")]
    public void RunBenchmark()
    {
        if (!isRunning) StartCoroutine(RunPass(null));
    }

    protected override bool CollectTargets()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // UI Toolkit's dynamic FontAsset clear-and-repopulate path traps the WebGL runtime
        // (RuntimeError: unreachable) on Unity 6000.x; skip so the UniText/TMP WebGL numbers,
        // already measured before this engine runs, survive to the results file.
        SetRunStatus("unsupported", "UI Toolkit glyph rasterization is not supported on WebGL");
        Debug.LogWarning("[UIToolkit GlyphRaster] Skipped on WebGL (dynamic FontAsset rasterization traps the WASM runtime).");
        return false;
#elif !UNITY_6000_0_OR_NEWER
        SetRunStatus("unsupported", "UI Toolkit glyph rasterization requires Unity 6000+ (Advanced Text Generator: HarfBuzz/ICU/FreeType); Unity 2022 ships only the legacy generator, which does not re-rasterize a cleared dynamic atlas and is not comparable to the modern path measured on Unity 6");
        Debug.LogWarning("[UIToolkit GlyphRaster] Skipped on Unity < 6000: no Advanced Text Generator; the legacy generator does not re-tessellate after an atlas clear, so no measurable rasterization occurs.");
        return false;
#else
        if (fontAsset == null)
        {
            SetRunStatus("skipped", "No FontAsset is assigned");
            Debug.LogError("[UIToolkit GlyphRaster] No FontAsset - assign one on the BenchmarkFontSelector's font list (uiToolkitFont).");
            return false;
        }
        if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
        {
            SetRunStatus("unsupported", $"'{fontAsset.name}' is not a Dynamic FontAsset");
            Debug.LogWarning($"[UIToolkit GlyphRaster] '{fontAsset.name}' is not a Dynamic FontAsset - it cannot re-rasterize after a clear; skipping.");
            return false;
        }

        glyphText = BenchmarkConfig.Instance != null ? BenchmarkConfig.Instance.GlyphRasterText : null;
        if (string.IsNullOrEmpty(glyphText))
        {
            SetRunStatus("skipped", "No glyph corpus is configured");
            Debug.LogError("[UIToolkit GlyphRaster] No glyph corpus - set the Glyph Rasterization text on the BenchmarkConfig in the scene.");
            return false;
        }

        EnsurePreviewPanel();
        if (visualBenchmark == null || visualBenchmark.ActivePanelSettings == null || Root == null)
        {
            SetRunStatus("skipped", "No live UI Toolkit panel root is available");
            Debug.LogError("[UIToolkit GlyphRaster] No live UI Toolkit panel root is available.");
            return false;
        }
        if (!UIToolkitFontIsolation.Isolate(visualBenchmark.ActivePanelSettings, fontAsset, out var fallbackError))
        {
            SetRunStatus("failed", fallbackError);
            Debug.LogError($"[UIToolkit GlyphRaster] Font isolation failed: {fallbackError}");
            return false;
        }

        EnsurePreviewElements();
        abortRun = false;
        Debug.Log("[UIToolkit GlyphRaster] Trigger=Label display enable; fallback local/global/default/sprite/emoji/Dynamic OS=off; completion=panel render + selected FontAsset population + async GPU readback.");
        return true;
#endif
    }

    void EnsurePreviewPanel()
    {
        if (visualBenchmark != null) return;
        visualBenchmark = ObjectUtils.FindAny<UIToolkitBenchmark>();
        if (visualBenchmark != null)
            visualBenchmark.RootReloaded += OnRootReloaded;
    }

    void OnRootReloaded()
    {
        panelVersion++;
        if (previewContainer != null && previewContainer.parent == null)
            Root.Add(previewContainer);
    }

    void EnsurePreviewElements()
    {
        previewContainer ??= new VisualElement { name = "UIToolkitGlyphRasterPreview" };
        previewContainer.style.position = Position.Absolute;
        previewContainer.style.left = 10;
        previewContainer.style.top = 10;
        previewContainer.style.width = 1068.9f;
        previewContainer.style.height = Length.Percent(100);
        previewContainer.style.display = DisplayStyle.None;
        previewContainer.pickingMode = PickingMode.Ignore;

        if (previewLabel == null)
        {
            previewLabel = new Label { name = "UIToolkitGlyphRasterPreviewLabel", pickingMode = PickingMode.Ignore };
            previewLabel.generateVisualContent += OnGenerateVisualContent;
        }
        previewLabel.text = glyphText;
        previewLabel.enableRichText = false;
#if UNITY_2023_2_OR_NEWER
        previewLabel.emojiFallbackSupport = false;
#endif
        previewLabel.style.fontSize = 28;
        previewLabel.style.color = Color.white;
        previewLabel.style.whiteSpace = WhiteSpace.Normal;
        previewLabel.style.unityFontDefinition = new StyleFontDefinition(fontAsset);
#if UNITY_6000_0_OR_NEWER
        previewLabel.style.unityTextGenerator = TextGeneratorType.Advanced;
#endif

        if (previewLabel.parent == null)
            previewContainer.Add(previewLabel);
        if (previewContainer.parent == null)
            Root.Add(previewContainer);
    }

    void OnGenerateVisualContent(MeshGenerationContext _)
    {
        renderSequence++;
    }

    /// <summary>Empties the dynamic atlas the run rasterized so its grown texture stops occupying memory between runs; in the editor also persists the shrunk font (the atlas is a sub-asset that otherwise survives play mode and bloats the saved .asset).</summary>
    protected override void OnAfterRun()
    {
        if (fontAsset == null) return;
        fontAsset.ClearFontAssetData(true);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(fontAsset);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(fontAsset);
#endif
    }

    protected override void ClearCaches() => fontAsset.ClearFontAssetData(false);

    protected override void Rasterize()
    {
        rasterPanelVersion = panelVersion;
        rasterRenderSequence = renderSequence;
        rasterGlyphCount = FontAssetUtils.GlyphCount(fontAsset);
        previewContainer.style.display = DisplayStyle.Flex;
        previewLabel.MarkDirtyRepaint();
    }

    protected override IEnumerator AwaitAsyncCompletion(float cpuMs)
    {
        double dispatchStart = Time.realtimeSinceStartupAsDouble - cpuMs / 1000.0;
        double timeoutAt = Time.realtimeSinceStartupAsDouble + RenderTimeoutSeconds;
        while ((renderSequence == rasterRenderSequence || FontAssetUtils.GlyphCount(fontAsset) <= rasterGlyphCount)
               && Time.realtimeSinceStartupAsDouble < timeoutAt)
        {
            if (panelVersion != rasterPanelVersion)
            {
                AbortPanelMeasurement("the UI Toolkit panel root reloaded during the measured pass");
                yield break;
            }
            yield return waitForEndOfFrame;
        }

        if (panelVersion != rasterPanelVersion)
        {
            AbortPanelMeasurement("the UI Toolkit panel root reloaded during the measured pass");
            yield break;
        }

        if (renderSequence == rasterRenderSequence)
        {
            AbortPanelMeasurement($"no Label render occurred within {RenderTimeoutSeconds:F0} seconds");
            yield break;
        }
        if (FontAssetUtils.GlyphCount(fontAsset) <= rasterGlyphCount)
        {
            AbortPanelMeasurement($"the Label rendered without populating the selected FontAsset within {RenderTimeoutSeconds:F0} seconds");
            yield break;
        }

        var textures = new List<Texture>();
        var atlases = fontAsset.atlasTextures;
        if (atlases != null)
            for (int i = 0; i < atlases.Length; i++)
                if (atlases[i] != null)
                    textures.Add(atlases[i]);
        yield return AwaitGpuTextureCompletion(dispatchStart, textures,
            AbortPanelMeasurement);
    }

    void AbortPanelMeasurement(string reason)
    {
        abortRun = true;
        lastE2eMs = float.NaN;
        SetRunStatus("failed", reason);
        Debug.LogWarning($"[UIToolkit GlyphRaster] Aborted: {reason}.");
    }

    protected override void ShowPreview()
    {
        if (previewContainer != null)
            previewContainer.style.display = DisplayStyle.Flex;
    }

    protected override void Deactivate()
    {
        if (previewContainer != null)
            previewContainer.style.display = DisplayStyle.None;
    }

    protected override int CountGlyphs() => FontAssetUtils.GlyphCount(fontAsset);

    protected override bool ShouldAbortRun() => abortRun;

    protected override GlyphExecutionSample CaptureExecutionDiagnostics()
    {
        var sample = new GlyphExecutionSample
        {
            trigger = "VisualElement.display=Flex",
            rasterBackend = "CpuTextCore",
            threading = "mainThread",
            completion = "panelRenderAndAsyncGpuReadback",
            fallback = "localGlobalDefaultSpriteEmojiAndDynamicOsDisabled"
        };
        var textures = fontAsset.atlasTextures;
        if (textures != null)
            for (int i = 0; i < textures.Length; i++)
            {
                var texture = textures[i];
                if (texture == null) continue;
                sample.atlases.Add(new GlyphAtlasExecutionData
                {
                    mode = $"atlas{i}",
                    requestedPath = "engineNative",
                    backend = "CpuTextCore",
                    storage = texture.GetType().Name,
                    cpuMirror = texture.isReadable,
                    gpuUploadTarget = false,
                    preparation = "mainThread",
                    writePaths = new System.Collections.Generic.List<string> { "textCoreInternal" }
                });
            }
        return sample;
    }

    protected override string Diagnostics(string label)
    {
        int glyphs = FontAssetUtils.GlyphCount(fontAsset);
        if (label == "AFTER raster" && glyphs == 0)
        {
            abortRun = true;
            SetRunStatus("failed", "The Label rendered without populating the selected FontAsset");
            Debug.LogWarning("[UIToolkit GlyphRaster] The Label rendered without populating the selected FontAsset; the pass is invalid.");
        }
        return $"[Atlas {label}] '{fontAsset.name}': glyphs={glyphs} chars={FontAssetUtils.CharacterCount(fontAsset)} atlasCount={fontAsset.atlasTextureCount} trigger=Label.display raster=CPU-TextCore completion=panel-render+asyncGpuReadback cpuMirror={fontAsset.atlasTexture != null && fontAsset.atlasTexture.isReadable} fallback=local/global/default/sprite/emoji/DynamicOS-off";
    }
}
