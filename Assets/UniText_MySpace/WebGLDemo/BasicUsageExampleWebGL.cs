using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace LightSide.Samples
{
    /// <summary>
    /// WebGL-specific variant of <see cref="BasicUsageExampleBase"/> that mirrors text changes
    /// to the embedding HTML page (used by the public unity.lightside.media demo).
    /// </summary>
    /// <remarks>
    /// Bridge contract:
    /// <list type="bullet">
    /// <item>JS → Unity: <c>unityInstance.SendMessage(browserBridgeObjectName, "SetDemoText", text)</c> pushes text from the page textarea.</item>
    /// <item>Unity → JS: <c>UniTextDemo_EmitTextChanged(text)</c> (jslib) forwards Unity-originated changes to the page.</item>
    /// </list>
    /// </remarks>
    public class BasicUsageExampleWebGL : BasicUsageExampleBase
    {
        [Header("Browser Bridge")]
        [Tooltip("GameObject name React uses with unityInstance.SendMessage. " +
                 "Must match the value in DemoPage.tsx on the website.")]
        [SerializeField] private string browserBridgeObjectName = "DemoController";

        [Header("Slideshow")]
        [SerializeField] private AssetReferenceGameObject addressablePrefab;
        
        private string lastSyncedText;
        private UniTextFont loadedFont;
        private UniTextFontStack loadedFontStack;

        internal AssetReferenceGameObject AddressablePrefab => addressablePrefab;
        internal UniText SlideshowText => demoText;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void UniTextDemo_EmitTextChanged(string text);
        [DllImport("__Internal")]
        private static extern void UniTextDemo_EmitFontLoaded(string label);
        [DllImport("__Internal")]
        private static extern void UniTextDemo_EmitFontError(string message);
#endif

        protected override void OnInit()
        {
            base.OnInit();
            if (!string.IsNullOrEmpty(browserBridgeObjectName) && gameObject.name != browserBridgeObjectName)
                gameObject.name = browserBridgeObjectName;
        }

        protected override void ApplyText(string text)
        {
            base.ApplyText(text);
            MirrorToBrowser(text);
        }

        protected override void ApplySetText(char[] buffer, int offset, int length, string browserPayload)
        {
            base.ApplySetText(buffer, offset, length, browserPayload);
            MirrorToBrowser(browserPayload);
        }

        protected override void ApplySetTextMemory(ReadOnlyMemory<char> source, string browserPayload)
        {
            base.ApplySetTextMemory(source, browserPayload);
            MirrorToBrowser(browserPayload);
        }

        protected override void ApplySetTextStringBuilder(StringBuilder source, string browserPayload)
        {
            base.ApplySetTextStringBuilder(source, browserPayload);
            MirrorToBrowser(browserPayload);
        }

        private void MirrorToBrowser(string text)
        {
            if (demoText == null || text == null || text == lastSyncedText) return;
            lastSyncedText = text;
            PushTextToBrowser(text);
        }

        /// <summary>
        /// JS → Unity. Invoked by React via <c>unityInstance.SendMessage(browserBridgeObjectName, "SetDemoText", text)</c>.
        /// </summary>
        public void SetDemoText(string text)
        {
            if (demoText == null || text == null) return;
            if (text == lastSyncedText) return;

            lastSyncedText = text;
            demoText.Text = text;
        }

        /// <summary>
        /// JS → Unity. Fetches a font over HTTP and applies it as the primary face while the
        /// bundled stack keeps serving fallback glyphs. Demonstrates streaming a language font
        /// (e.g. CJK) on demand instead of shipping it in the build.
        /// </summary>
        public void LoadPresetFont(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            StartCoroutine(LoadPresetFontRoutine(url));
        }

        /// <summary>
        /// JS → Unity. Applies a Base64-encoded TTF/OTF the visitor picked from their own device,
        /// decoded entirely in the browser — no upload, no rebuild.
        /// </summary>
        public void LoadFontFromBase64(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return;
            try
            {
                ApplyFont(Convert.FromBase64String(base64), "your font");
            }
            catch (Exception e)
            {
                EmitFontError(e.Message);
            }
        }

        /// <summary>JS → Unity. Drops the runtime-loaded font and returns to the bundled stack.</summary>
        public void ClearLoadedFont(string _ = null)
        {
            ReleaseLoadedFont();
        }

        private IEnumerator LoadPresetFontRoutine(string url)
        {
            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                EmitFontError(request.error);
                yield break;
            }

            ApplyFont(request.downloadHandler.data, url);
        }

        private void ApplyFont(byte[] fontBytes, string label)
        {
            var stack = demoText != null ? demoText.FontStack : null;
            if (stack == null)
            {
                EmitFontError("DemoText has no Font Stack.");
                return;
            }

            var font = UniTextFont.CreateFontAsset(fontBytes);
            if (font == null)
            {
                EmitFontError($"Not a valid font: {label}");
                return;
            }

            ReleaseLoadedFont();
            stack.Families.Insert(0, new FontFamily(null, font));
            loadedFont = font;
            loadedFontStack = stack;
            EmitFontLoaded(label);
        }

        private void ReleaseLoadedFont()
        {
            if (loadedFontStack != null && loadedFont != null)
            {
                var families = loadedFontStack.Families;
                for (var i = 0; i < families.Count; i++)
                {
                    if (families[i].primary != loadedFont) continue;
                    families.RemoveAt(i);
                    break;
                }
            }

            loadedFontStack = null;
            if (loadedFont == null) return;
            Destroy(loadedFont);
            loadedFont = null;
        }

        private static void EmitFontLoaded(string label)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UniTextDemo_EmitFontLoaded(label);
#else
            Debug.Log($"[UniText demo] Font loaded: {label}");
#endif
        }

        private static void EmitFontError(string message)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UniTextDemo_EmitFontError(message);
#else
            Debug.LogWarning($"[UniText demo] Font load failed: {message}");
#endif
        }

        private static void PushTextToBrowser(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            UniTextDemo_EmitTextChanged(text);
#endif
        }
    }
}
