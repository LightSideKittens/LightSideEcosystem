#if !LIGHTSIDE_NO_UITOOLKIT
using System;
using System.Collections;
using System.Collections.Generic;
using LightSide;
using Newtonsoft.Json.Linq;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit's glyph operations on one label of the text suite's panel: a dynamic font asset made per case, every fallback
/// source off; the label is drawn once the panel repainted it and the case font gained glyphs.
/// </summary>
/// <remarks>
/// UI Toolkit rasterizes on Unity's job workers. Its single-threaded arm sets <see cref="JobsUtility.JobWorkerCount"/> to 0
/// for one operation, from its preparation to its reset, so every job Unity schedules in those frames runs on the main
/// thread; UniText's worker pool is its own threads and unaffected. Every other arm runs at Unity's own count.
/// </remarks>
public class UIToolkit_GlyphRasterizationBenchmark : GlyphRasterBenchmarkBase
{
    const float LabelFontSize = 36f;

    static readonly string[] Engines = { BenchmarkEngines.UIToolkit, BenchmarkEngines.UIToolkitSingleThreaded };

    UIToolkitBenchmark panel;
    VisualElement container;
    Label label;
    FontAsset caseFont;
    int panelVersion, panelAtShow;
    int renderSequence, renderAtShow;
    int glyphsAtShow;
    int defaultJobWorkers;

    public override IReadOnlyList<string> EngineIds => Engines;

    public override string UnsupportedReason =>
#if UNITY_WEBGL && !UNITY_EDITOR
        "UI Toolkit's dynamic font asset clear-and-repopulate path traps the WebGL runtime (RuntimeError: unreachable) on Unity 6000.x.";
#elif !UNITY_6000_0_OR_NEWER
        "Unity before 6000 has only the legacy UI Toolkit text generator, which does not rasterize again after an atlas clear.";
#else
        null;
#endif

    public override void BeginCase(GlyphBenchmarkFonts.GlyphFont pair, GlyphTier tier)
    {
        if (UnsupportedReason != null) throw new NotSupportedException(UnsupportedReason);
        if (panel == null)
        {
            panel = ObjectUtils.FindAny<UIToolkitBenchmark>()
                    ?? throw new InvalidOperationException("The UI Toolkit glyph benchmark needs the UIToolkitBenchmark panel in the scene.");
            panel.RootReloaded += OnRootReloaded;
        }
        if (panel.ActivePanelSettings == null || panel.RootElement == null)
            throw new InvalidOperationException("The UI Toolkit panel has no live root.");

        var atlas = RivalAtlas(pair, tier, true);
        caseFont = FontAsset.CreateFontAsset(atlas.source, atlas.pointSize, atlas.padding, GlyphRenderMode.SDFAA,
                       atlas.width, atlas.height)
                   ?? throw new InvalidOperationException($"UI Toolkit cannot load '{atlas.source.name}'; its import settings must include the font data.");
        caseFont.name = $"{atlas.source.name} ({tier})";
        if (!UIToolkitFontIsolation.Isolate(panel.ActivePanelSettings, caseFont, out var isolationError))
            throw new InvalidOperationException($"UI Toolkit font isolation failed: {isolationError}");

        container = new VisualElement { name = "UIToolkitGlyphRaster", pickingMode = PickingMode.Ignore };
        container.style.position = Position.Absolute;
        container.style.left = 10;
        container.style.top = 10;
        container.style.width = 1068.9f;
        container.style.height = Length.Percent(100);
        container.style.display = DisplayStyle.None;
        label = new Label { name = "UIToolkitGlyphRasterLabel", pickingMode = PickingMode.Ignore, enableRichText = false };
#if UNITY_2023_2_OR_NEWER
        label.emojiFallbackSupport = false;
#endif
        label.style.fontSize = LabelFontSize;
        label.style.color = Color.white;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.unityFontDefinition = new StyleFontDefinition(caseFont);
#if UNITY_6000_0_OR_NEWER
        label.style.unityTextGenerator = TextGeneratorType.Advanced;
#endif
        if (tier == GlyphTier.Styled)
        {
            caseFont.boldStyleSpacing = 0f;
            caseFont.boldStyleWeight = LookCalibrationProfile.UIToolkitBoldWeight(LookCalibrationProfile.House);
            LookCalibrationProfile.ApplyUIToolkit(label, LookCalibrationProfile.House, LabelFontSize);
        }
        label.generateVisualContent += OnGenerateVisualContent;
        container.Add(label);
        panel.RootElement.Add(container);
    }

    void OnRootReloaded()
    {
        panelVersion++;
        if (container != null && container.parent == null)
            panel.RootElement.Add(container);
    }

    void OnGenerateVisualContent(MeshGenerationContext context) => renderSequence++;

    public override void EndCase()
    {
        if (container == null) return;
        label.generateVisualContent -= OnGenerateVisualContent;
        container.RemoveFromHierarchy();
        container = null;
        label = null;
        caseFont.ClearFontAssetData(true);
        foreach (var texture in caseFont.atlasTextures)
            if (texture != null) Destroy(texture);
        if (caseFont.material != null) Destroy(caseFont.material);
        Destroy(caseFont);
        caseFont = null;
        Rival = null;
        JobsUtility.ResetJobWorkerCount();
    }

    void OnDestroy()
    {
        if (panel != null) panel.RootReloaded -= OnRootReloaded;
    }

    public override void SelectEngine(string engineId)
    {
        JobsUtility.ResetJobWorkerCount();
        defaultJobWorkers = JobsUtility.JobWorkerCount;
        switch (engineId)
        {
            case BenchmarkEngines.UIToolkit:
                break;
            case BenchmarkEngines.UIToolkitSingleThreaded:
                JobsUtility.JobWorkerCount = 0;
                break;
            default:
                throw new ArgumentException($"UI Toolkit measures no engine '{engineId}'.", nameof(engineId));
        }
    }

    public override void ReleaseEngine() => JobsUtility.ResetJobWorkerCount();

    /// <summary>Writes <c>jobWorkers</c>, the Unity job workers the arm ran with: 0 single-threaded, Unity's own count otherwise.</summary>
    public override void DescribeEngine(string engineId, JObject arm) =>
        arm["jobWorkers"] = engineId == BenchmarkEngines.UIToolkitSingleThreaded ? 0 : defaultJobWorkers;

    public override IEnumerator Clear(string text)
    {
        Hide();
        yield return null;
        caseFont.ClearFontAssetData(false);
        label.text = text;
    }

    public override void Show()
    {
        panelAtShow = panelVersion;
        renderAtShow = renderSequence;
        glyphsAtShow = GlyphCount;
        container.style.display = DisplayStyle.Flex;
        label.MarkDirtyRepaint();
    }

    /// <exception cref="InvalidOperationException">The panel root reloaded since <see cref="Show"/>.</exception>
    public override bool Drawn
    {
        get
        {
            if (panelVersion != panelAtShow)
                throw new InvalidOperationException("The UI Toolkit panel root reloaded during the measured rasterization.");
            return renderSequence != renderAtShow && GlyphCount > glyphsAtShow;
        }
    }

    public override int GlyphCount => FontAssetUtils.GlyphCount(caseFont);

    public override void Hide() => container.style.display = DisplayStyle.None;
}
#endif
