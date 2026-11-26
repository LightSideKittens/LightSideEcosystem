using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Showcase
{
    /// <summary>
    /// Runs the demo on the UniText page: the page opens scenes and sends text through <c>SendMessage</c> to the object
    /// named <c>DemoController</c>, and hears back through <see cref="ShowcaseBridge"/>.
    /// </summary>
    public sealed class ShowcaseController : MonoBehaviour
    {
        private const float EntranceSeconds = 0.5f;
        private const float LightResponse = 6f;

        [Tooltip("Scenes in the order of the page. The one enabled in the hierarchy opens first.")]
        [SerializeField] private ShowcaseScene[] scenes;
        [SerializeField] private Graphic upperLight;
        [SerializeField] private Graphic lowerLight;

        private ShowcaseScene active;
        private UniTextFont loadedFont;
        private float entrance;

        private void Start()
        {
            var first = scenes.First(scene => scene.gameObject.activeSelf);
            foreach (var scene in scenes) scene.gameObject.SetActive(false);
            SetScene(first.Id);
            upperLight.color = active.UpperLight;
            lowerLight.color = active.LowerLight;
            ShowcaseBridge.Emit("ready", active.Id);
        }

        private void Update()
        {
            var light = 1f - Mathf.Exp(-Time.unscaledDeltaTime * LightResponse);
            upperLight.color = Color.Lerp(upperLight.color, active.UpperLight, light);
            lowerLight.color = Color.Lerp(lowerLight.color, active.LowerLight, light);
            if (entrance >= 1f) return;
            entrance = Mathf.Min(1f, entrance + Time.unscaledDeltaTime / EntranceSeconds);
            active.Present(entrance);
        }

        /// <summary>Opens the scene named <paramref name="id"/>.</summary>
        public void SetScene(string id)
        {
            var next = Array.Find(scenes, scene => scene.Id == id)
                       ?? throw new ArgumentException($"The showcase has no scene '{id}'.", nameof(id));
            if (active != null)
            {
                ClearLoadedFont();
                active.gameObject.SetActive(false);
            }
            active = next;
            entrance = 0f;
            active.Present(0f);
            active.gameObject.SetActive(true);
            active.Enter();
            ReportText();
        }

        /// <summary>Shows the variant <paramref name="variant"/> of the open scene.</summary>
        public void SetVariant(string variant)
        {
            active.SetVariant(variant);
            ReportText();
        }

        /// <summary>Replaces the editable text of the open scene.</summary>
        public void SetDemoText(string text)
        {
            active.SetText(text);
            ReportMarkup();
        }

        /// <summary>Draws the editable text of the open scene in the font file encoded in <paramref name="base64"/>.</summary>
        public void LoadFontFromBase64(string base64)
        {
            var font = UniTextFont.CreateFontAsset(Convert.FromBase64String(base64));
            if (font == null)
            {
                ShowcaseBridge.Emit("fontError", string.Empty);
                return;
            }
            active.SetFont(font);
            if (loadedFont != null) Destroy(loadedFont);
            loadedFont = font;
            ShowcaseBridge.Emit("fontLoaded", string.Empty);
        }

        /// <summary>Returns the open scene to its own font.</summary>
        public void ClearLoadedFont()
        {
            if (loadedFont == null) return;
            active.SetFont(null);
            Destroy(loadedFont);
            loadedFont = null;
        }

        private void ReportText()
        {
            ShowcaseBridge.Emit("textChanged", active.Text);
            ReportMarkup();
        }

        private void ReportMarkup() => ShowcaseBridge.Emit("markupChanged", active.Markup);
    }
}
