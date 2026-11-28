using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// TextMesh Pro's glyph operations on the child texts: a dynamic font asset made per case, font features on, every
/// fallback source off; a text is drawn once TMP reports its mesh regenerated, which rasterizes its missing glyphs first.
/// </summary>
public class TMP_GlyphRasterizationBenchmark : GlyphRasterBenchmarkBase
{
    static readonly string[] Engines = { BenchmarkEngines.Tmp };

    TMP_Text[] targets;
    TMP_FontAsset caseFont;
    Material caseMaterial;
    readonly HashSet<Object> watched = new();
    Action<Object> countCommit;
    long commits, commitsAtShow;

    List<TMP_FontAsset> savedGlobalFallback;
    TMP_FontAsset savedDefaultFont;
    List<TMP_Asset> savedEmojiFallback;
    TMP_SpriteAsset savedDefaultSprite;
    bool savedEmojiEnabled;
    (TMP_FontAsset font, Material material, bool emoji, TMP_SpriteAsset sprite)[] savedTexts;

    public override IReadOnlyList<string> EngineIds => Engines;

    public override void BeginCase(GlyphBenchmarkFonts.GlyphFont pair, GlyphTier tier)
    {
        var list = new List<TMP_Text>();
        foreach (var text in GetComponentsInChildren<TMP_Text>(true))
            if (text.gameObject != gameObject)
                list.Add(text);
        if (list.Count == 0)
            throw new InvalidOperationException("The TMP glyph benchmark has no child texts.");
        var atlas = RivalAtlas(pair, tier, TMP_Settings.getFontFeaturesAtRuntime);
        caseFont = TMP_FontAsset.CreateFontAsset(atlas.source, atlas.pointSize, atlas.padding, GlyphRenderMode.SDFAA,
                       atlas.width, atlas.height, AtlasPopulationMode.Dynamic, true)
                   ?? throw new InvalidOperationException($"TMP cannot load '{atlas.source.name}'; its import settings must include the font data.");
        caseFont.name = $"{atlas.source.name} ({tier})";
        caseFont.getFontFeatures = true;
        caseMaterial = tier == GlyphTier.Styled
            ? LookCalibrationProfile.CreateTmpMaterial(caseFont.material, caseFont, LookCalibrationProfile.House)
            : null;

        savedGlobalFallback = TMP_Settings.fallbackFontAssets;
        savedDefaultFont = TMP_Settings.defaultFontAsset;
        savedEmojiFallback = TMP_Settings.emojiFallbackTextAssets;
        savedDefaultSprite = TMP_Settings.defaultSpriteAsset;
        savedEmojiEnabled = TMP_Settings.enableEmojiSupport;
        TMP_Settings.fallbackFontAssets = new List<TMP_FontAsset>();
        TMP_Settings.defaultFontAsset = null;
        TMP_Settings.emojiFallbackTextAssets = new List<TMP_Asset>();
        TMP_Settings.defaultSpriteAsset = null;
        TMP_Settings.enableEmojiSupport = false;

        targets = list.ToArray();
        savedTexts = new (TMP_FontAsset, Material, bool, TMP_SpriteAsset)[targets.Length];
        countCommit ??= changed =>
        {
            if (watched.Contains(changed)) commits++;
        };
        for (var i = 0; i < targets.Length; i++)
        {
            var text = targets[i];
            savedTexts[i] = (text.font, text.fontSharedMaterial, text.emojiFallbackSupport, text.spriteAsset);
            text.gameObject.SetActive(false);
            text.font = caseFont;
            text.fontSharedMaterial = caseMaterial != null ? caseMaterial : caseFont.material;
            text.emojiFallbackSupport = false;
            text.spriteAsset = null;
            watched.Add(text);
        }
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(countCommit);
    }

    public override void EndCase()
    {
        if (targets == null) return;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(countCommit);
        watched.Clear();
        for (var i = 0; i < targets.Length; i++)
        {
            var text = targets[i];
            text.gameObject.SetActive(false);
            (text.font, text.fontSharedMaterial, text.emojiFallbackSupport, text.spriteAsset) = savedTexts[i];
        }
        targets = null;
        TMP_Settings.fallbackFontAssets = savedGlobalFallback;
        TMP_Settings.defaultFontAsset = savedDefaultFont;
        TMP_Settings.emojiFallbackTextAssets = savedEmojiFallback;
        TMP_Settings.defaultSpriteAsset = savedDefaultSprite;
        TMP_Settings.enableEmojiSupport = savedEmojiEnabled;

        if (caseMaterial != null) Destroy(caseMaterial);
        caseMaterial = null;
        foreach (var texture in caseFont.atlasTextures)
            if (texture != null) Destroy(texture);
        Destroy(caseFont.material);
        Destroy(caseFont);
        caseFont = null;
        Rival = null;
    }

    public override void SelectEngine(string engineId)
    {
        if (engineId != BenchmarkEngines.Tmp)
            throw new ArgumentException($"TMP measures no engine '{engineId}'.", nameof(engineId));
    }

    public override IEnumerator Clear(string text)
    {
        Hide();
        yield return null;
        caseFont.ClearFontAssetData(false);
        foreach (var target in targets)
            target.text = text;
    }

    public override void Show()
    {
        commitsAtShow = commits;
        foreach (var text in targets)
            text.gameObject.SetActive(true);
    }

    public override bool Drawn => commits - commitsAtShow >= targets.Length;

    public override int GlyphCount => FontAssetUtils.GlyphCount(caseFont);

    public override void Hide()
    {
        foreach (var text in targets)
            text.gameObject.SetActive(false);
    }
}
