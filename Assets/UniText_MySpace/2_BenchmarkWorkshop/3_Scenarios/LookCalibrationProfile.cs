using System.Collections.Generic;
using LightSide;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using Style = LightSide.Style;

/// <summary>
/// A text effect look as it is drawn on screen: distances in em of the font size, measured from the edge of the
/// unstyled face, independent of how any engine parameterizes its effects.
/// </summary>
public readonly struct LookCalibrationLook
{
    /// <summary>Distance from the unstyled face edge to the outer edge of the outline; the face itself stays unfattened.</summary>
    public readonly float OutlineEm;

    /// <summary>Shadow displacement, x to the right and y up.</summary>
    public readonly Vector2 ShadowOffsetEm;

    /// <summary>Outward growth of the shadow beyond the unstyled face before the offset applies.</summary>
    public readonly float ShadowSpreadEm;

    /// <summary>Blur radius of the shadow edge.</summary>
    public readonly float ShadowBlurEm;

    /// <summary>Color of the outline band.</summary>
    public readonly Color OutlineColor;

    /// <summary>Color of the shadow; its alpha is the shadow's strength.</summary>
    public readonly Color ShadowColor;

    /// <summary>Creates a look from drawn distances in em.</summary>
    public LookCalibrationLook(float outlineEm, Vector2 shadowOffsetEm, float shadowSpreadEm, float shadowBlurEm,
        Color outlineColor, Color shadowColor)
    {
        OutlineEm = outlineEm;
        ShadowOffsetEm = shadowOffsetEm;
        ShadowSpreadEm = shadowSpreadEm;
        ShadowBlurEm = shadowBlurEm;
        OutlineColor = outlineColor;
        ShadowColor = shadowColor;
    }

    /// <summary>How far the shadow reaches past the unstyled face edge on the right (x) and at the bottom (y), at half coverage.</summary>
    public Vector2 ShadowReachEm => new(ShadowOffsetEm.x + ShadowSpreadEm, ShadowSpreadEm - ShadowOffsetEm.y);

    /// <summary>The same geometry drawn in other colors.</summary>
    public LookCalibrationLook WithColors(Color outlineColor, Color shadowColor) =>
        new(OutlineEm, ShadowOffsetEm, ShadowSpreadEm, ShadowBlurEm, outlineColor, shadowColor);
}

/// <summary>
/// The bench's text effect looks and the per-engine parameters that draw them. Every styled bench text of every engine
/// takes its effects from here, so the engines draw one look; the coefficients are the drawn em per parameter unit that
/// <see cref="LookCalibrationSuite"/> measures on a device, and the parameters follow from them.
/// </summary>
/// <remarks>
/// UniText em effect widths (stroke width, shadow spread and blur) draw at half their value, so its parameters are about
/// twice the drawn look; its effects behind the face stack in style-list order, so the shadow is listed before the
/// stroke to lie under it as in the other engines. TextMesh Pro parameters depend on the material's gradient scale and the font asset's sampling
/// size; its fallback font assets draw the same look only at the primary asset's spread. UI Toolkit labels must be styled
/// again whenever their font size changes, because its outline and shadow sizes are in panel units, not em.
/// </remarks>
public static class LookCalibrationProfile
{
    /// <summary>The look of every styled bench text: outline 0.12 em, shadow offset (0.05, −0.14) em with 0.05 em spread and blur, black at 60 %.</summary>
    public static readonly LookCalibrationLook House = new(0.12f, new Vector2(0.05f, -0.14f), 0.05f, 0.05f,
        Color.black, new Color(0f, 0f, 0f, 0.6f));

    /// <summary>The demonstration look of the effect close-up: the house shadow under a 0.16 em pink outline.</summary>
    public static readonly LookCalibrationLook CloseUp = new(0.16f, House.ShadowOffsetEm, House.ShadowSpreadEm,
        House.ShadowBlurEm, new Color32(255, 61, 127, 255), House.ShadowColor);

    /// <summary>Background of every bench screen and still.</summary>
    public static readonly Color Background = new Color32(0x5A, 0x5F, 0x8A, 0xFF);

    /// <summary>Atlas sampling size of UI Toolkit font assets made by <see cref="CreateUIToolkitFont"/>.</summary>
    public const int UIToolkitSamplingPointSize = 90;

    /// <summary>Atlas padding of UI Toolkit font assets: 0.33 em, which holds the outline and the shadow of both looks unclipped.</summary>
    public const int UIToolkitAtlasPadding = 30;

    internal const float UniTextStrokePerWidth = 0.5f;
    internal const float UniTextShadowPerSpread = 0.5f;
    internal const float UniTextShadowPerBlur = 0.5f;

    internal const float TmpDilatePerUnit = 1.02f;
    internal const float TmpOutlinePerWidth = 1f;
    internal const float TmpUnderlayPerOffset = 1f;
    internal const float TmpSoftnessPerUnit = 3.44f;

    internal const float UIToolkitFacePerBold = 0.0224f;
    internal const float UIToolkitOutlinePerWidth = 0.98f;
    internal const float UIToolkitShadowPerOffset = 1f;
    internal const float UIToolkitShadowPerBlur = 1.26f;

    /// <summary>Replaces the whole-text stroke and shadow of <paramref name="text"/> with ones that draw <paramref name="look"/>.</summary>
    public static void ApplyUniText(UniTextBase text, in LookCalibrationLook look)
    {
        text.ClearWholeText<StrokeModifier>();
        text.ClearWholeText<ShadowModifier>();
        text.Styles.Add(Style.WholeText(new ShadowModifier
        {
            Paint = PaintRef.Solid(look.ShadowColor),
            Offset = new UnitVector2(look.ShadowOffsetEm, UnitKind.Em),
            Blur = UnitValue.Em(UniTextShadowBlurEm(look)),
            Spread = UnitValue.Em(UniTextShadowSpreadEm(look))
        }));
        text.Styles.Add(Style.WholeText(new StrokeModifier
        {
            Paint = PaintRef.Solid(look.OutlineColor),
            Width = UnitValue.Em(UniTextStrokeWidthEm(look)),
            Align = 1f
        }));
    }

    /// <summary>
    /// A new material, owned by the caller, that draws <paramref name="look"/> with outline and underlay for text in
    /// <paramref name="font"/>; it copies <paramref name="preset"/>, an outline-and-underlay material of that font.
    /// </summary>
    public static Material CreateTmpMaterial(Material preset, TMP_FontAsset font, in LookCalibrationLook look)
    {
        var material = new Material(preset) { name = $"{preset.name} (look)" };
        material.EnableKeyword(ShaderUtilities.Keyword_Outline);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_OutlineColor, look.OutlineColor);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, look.ShadowColor);
        var emPerUnit = material.GetFloat(ShaderUtilities.ID_GradientScale) / font.faceInfo.pointSize;
        for (var pass = 0; pass < 3; pass++)
        {
            ShaderUtilities.UpdateShaderRatios(material);
            var ratioA = material.GetFloat(ShaderUtilities.ID_ScaleRatio_A) * emPerUnit;
            var ratioC = material.GetFloat(ShaderUtilities.ID_ScaleRatio_C) * emPerUnit;
            var halfOutline = look.OutlineEm * 0.5f;
            material.SetFloat(ShaderUtilities.ID_FaceDilate, halfOutline / (TmpDilatePerUnit * ratioA));
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, halfOutline / (TmpOutlinePerWidth * ratioA));
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate,
                (look.ShadowSpreadEm - halfOutline) / (TmpDilatePerUnit * ratioC));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, look.ShadowOffsetEm.x / (TmpUnderlayPerOffset * ratioC));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, look.ShadowOffsetEm.y / (TmpUnderlayPerOffset * ratioC));
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, look.ShadowBlurEm / (TmpSoftnessPerUnit * ratioC));
        }
        ShaderUtilities.UpdateShaderRatios(material);
        return material;
    }

    /// <summary>
    /// A dynamic SDF font asset of <paramref name="source"/> whose synthetic bold keeps the face unfattened under the
    /// outline of <paramref name="look"/>; <paramref name="fallbacks"/> become its local fallbacks, made the same way.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">A font is unassigned or its import settings exclude the font data.</exception>
    public static FontAsset CreateUIToolkitFont(Font source, IReadOnlyList<Font> fallbacks, in LookCalibrationLook look)
    {
        var primary = CreateUIToolkitFontAsset(source, look);
        var table = new List<FontAsset>(fallbacks.Count);
        foreach (var fallback in fallbacks)
            table.Add(CreateUIToolkitFontAsset(fallback, look));
        primary.fallbackFontAssetTable = table;
        return primary;
    }

    /// <summary>
    /// Gives <paramref name="label"/> the outline and shadow of <paramref name="look"/> at <paramref name="fontSize"/>,
    /// its font size in panel units; its font must come from <see cref="CreateUIToolkitFont"/> with the same look.
    /// </summary>
    public static void ApplyUIToolkit(Label label, in LookCalibrationLook look, float fontSize)
    {
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.unityTextOutlineWidth = UIToolkitOutlineWidthEm(look) * fontSize;
        label.style.unityTextOutlineColor = look.OutlineColor;
        label.style.textShadow = new TextShadow
        {
            offset = UIToolkitShadowOffsetEm(look) * fontSize,
            blurRadius = UIToolkitShadowBlurEm(look) * fontSize,
            color = look.ShadowColor
        };
    }

    internal static float UniTextStrokeWidthEm(in LookCalibrationLook look) => look.OutlineEm / UniTextStrokePerWidth;
    internal static float UniTextShadowSpreadEm(in LookCalibrationLook look) => look.ShadowSpreadEm / UniTextShadowPerSpread;
    internal static float UniTextShadowBlurEm(in LookCalibrationLook look) => look.ShadowBlurEm / UniTextShadowPerBlur;

    internal static float UIToolkitBoldWeight(in LookCalibrationLook look) => look.OutlineEm * 0.5f / UIToolkitFacePerBold;
    internal static float UIToolkitOutlineWidthEm(in LookCalibrationLook look) => look.OutlineEm / UIToolkitOutlinePerWidth;
    internal static float UIToolkitShadowBlurEm(in LookCalibrationLook look) => look.ShadowBlurEm / UIToolkitShadowPerBlur;

    internal static Vector2 UIToolkitShadowOffsetEm(in LookCalibrationLook look)
    {
        var halfOutline = look.OutlineEm * 0.5f;
        var reach = look.ShadowReachEm;
        return new Vector2(reach.x - halfOutline, reach.y - halfOutline) / UIToolkitShadowPerOffset;
    }

    static FontAsset CreateUIToolkitFontAsset(Font source, in LookCalibrationLook look)
    {
        if (source == null)
            throw new System.InvalidOperationException("A UI Toolkit font slot of the bench is unassigned.");
        var asset = FontAsset.CreateFontAsset(source, UIToolkitSamplingPointSize, UIToolkitAtlasPadding,
            GlyphRenderMode.SDFAA, 1024, 1024);
        if (asset == null)
            throw new System.InvalidOperationException(
                $"UI Toolkit cannot load '{source.name}'; its import settings must include the font data.");
        asset.boldStyleSpacing = 0f;
        asset.boldStyleWeight = UIToolkitBoldWeight(look);
        return asset;
    }
}
