using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

static class UIToolkitFontIsolation
{
    static readonly FieldInfo defaultFontField = Field("m_DefaultFontAsset");
    static readonly FieldInfo fallbackFontField = Field("m_FallbackFontAssets");
    static readonly FieldInfo osFallbackField = Field("m_FallbackOSFontAssets");
    static readonly FieldInfo osFallbackInitializedField = Field("m_FallbackOSFontAssetsInitialized");
    static readonly FieldInfo emojiEnabledField = Field("m_EnableEmojiSupport");
    static readonly FieldInfo emojiFallbackField = Field("m_EmojiFallbackTextAssets");
    static readonly FieldInfo defaultSpriteField = Field("m_DefaultSpriteAsset");
    static readonly FieldInfo fallbackSpriteField = Field("m_FallbackSpriteAssets");

    /// <summary>
    /// Makes sure the panel cannot resolve missing glyphs through local, global, sprite, emoji, default-font, or Dynamic OS
    /// fallback sources. Unity versions that build the Dynamic OS fallback list lazily as a runtime cache, instead of
    /// reading it from the Panel Text Settings asset, get that cache settled as empty; every other source is verified.
    /// </summary>
    public static bool Isolate(PanelSettings panelSettings, FontAsset primaryFont, out string error)
    {
        error = null;
        if (panelSettings == null)
            return Fail("PanelSettings is not assigned.", out error);

        var settings = panelSettings.textSettings;
        if (settings == null)
            return Fail("Panel Text Settings is not assigned; Unity would create default settings with Dynamic OS fallback.", out error);
        if (osFallbackInitializedField != null && osFallbackField != null)
        {
            osFallbackField.SetValue(settings, new List<FontAsset>());
            osFallbackInitializedField.SetValue(settings, true);
        }
        if (!TryCollectionCount(settings, osFallbackField, "Dynamic OS fallback", out int osFallbacks, out error))
            return false;
        if (osFallbacks != 0)
            return Fail($"Panel Text Settings contains {osFallbacks} Dynamic OS fallback font(s).", out error);
        if (!TryCollectionCount(settings, fallbackFontField, "global font fallback", out int fontFallbacks, out error))
            return false;
        if (fontFallbacks != 0)
            return Fail($"Panel Text Settings contains {fontFallbacks} global fallback font(s).", out error);
        if (FieldValue(settings, defaultFontField) != null)
            return Fail("Panel Text Settings default font is a fallback source.", out error);
        if (!TryBoolean(settings, emojiEnabledField, "emoji fallback switch", out bool emojiEnabled, out error)
            || !TryCollectionCount(settings, emojiFallbackField, "emoji fallback", out int emojiFallbacks, out error))
            return false;
        if (emojiEnabled || emojiFallbacks != 0)
            return Fail("Panel Text Settings emoji fallback is enabled.", out error);
        if (!TryCollectionCount(settings, fallbackSpriteField, "sprite fallback", out int spriteFallbacks, out error))
            return false;
        if (FieldValue(settings, defaultSpriteField) != null || spriteFallbacks != 0)
            return Fail("Panel Text Settings sprite fallback is enabled.", out error);
        if (primaryFont != null && primaryFont.fallbackFontAssetTable is { Count: > 0 })
            return Fail($"Primary font '{primaryFont.name}' contains {primaryFont.fallbackFontAssetTable.Count} local fallback font(s).", out error);
        return true;
    }

    static FieldInfo Field(string name) => typeof(TextSettings).GetField(
        name, BindingFlags.Instance | BindingFlags.NonPublic);

    static object FieldValue(TextSettings settings, FieldInfo field) => field?.GetValue(settings);

    static bool TryCollectionCount(TextSettings settings, FieldInfo field, string label, out int count,
        out string error)
    {
        count = 0;
        error = null;
        if (field == null)
            return true;
        if (field.GetValue(settings) is not ICollection collection)
            return Fail($"The {label} list is null or has an unexpected type.", out error);
        count = collection.Count;
        return true;
    }

    static bool TryBoolean(TextSettings settings, FieldInfo field, string label, out bool value,
        out string error)
    {
        value = false;
        error = null;
        if (field == null)
            return true;
        if (field.GetValue(settings) is not bool result)
            return Fail($"The serialized {label} field has an unexpected type.", out error);
        value = result;
        return true;
    }

    static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
