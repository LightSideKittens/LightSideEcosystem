using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>A scene around one text: each variant wraps it in markup and may bring its own text.</summary>
    public class TextShowcaseScene : ShowcaseScene
    {
        private const string EntranceTag = "enter";

        /// <summary>One look the page offers for the text.</summary>
        [Serializable]
        public struct Variant
        {
            public string id;

            [Tooltip("Text the variant brings; empty keeps the current one.")]
            public string text;

            [Tooltip("Markup around the text, which stands for {0}.")]
            public string markup;

            [Tooltip("Font the variant draws in; empty keeps the font of the text.")]
            public UniTextFont font;
        }

        [SerializeField] private UniTextBase target;
        [SerializeField] private Variant[] variants;
        [SerializeField, Min(0.01f)] private float entranceSeconds = 0.9f;

        [Tooltip("Keeps the words a visitor typed when another variant opens.")]
        [SerializeField] private bool keepTyped;

        [Tooltip("How the letters appear when the scene or a variant opens.")]
        [SerializeReference] private RevealHandler entrance;

        private RevealModifier reveal;
        private UniTextFont ownFont;
        private UniTextFont loadedFont;
        private Variant variant;
        private string text;
        private bool typed;
        private float elapsed;

        public override string Text => text;
        public override string Markup => string.Format(variant.markup, text);
        public override IEnumerable<string> Variants => variants.Select(candidate => candidate.id);

        private void Awake()
        {
            ownFont = target.Font;
            var handlers = new InlineRevealHandlerProvider();
            handlers.Entries.Add(new RevealHandlerEntry { Name = string.Empty, Handler = entrance });
            reveal = new RevealModifier { Provider = handlers };
            target.Styles.Add(Style.Tag(reveal, EntranceTag));
        }

        public override void Enter()
        {
            typed = false;
            SetVariant(variants[0].id);
        }

        public override void SetVariant(string id)
        {
            var index = Array.FindIndex(variants, candidate => candidate.id == id);
            if (index < 0) throw new ArgumentException($"Showcase scene '{Id}' has no variant '{id}'.", nameof(id));
            variant = variants[index];
            if (!string.IsNullOrEmpty(variant.text) && !(keepTyped && typed)) text = variant.text;
            ShowFont();
            elapsed = 0f;
            reveal.Front = UnitValue.Percent(0f);
            Apply();
        }

        public override void SetText(string value)
        {
            text = value;
            typed = true;
            elapsed = entranceSeconds;
            reveal.Front = UnitValue.Percent(100f);
            Apply();
        }

        public override void SetFont(UniTextFont font)
        {
            loadedFont = font;
            ShowFont();
        }

        protected virtual void Update()
        {
            if (elapsed >= entranceSeconds) return;
            elapsed = Mathf.Min(entranceSeconds, elapsed + Time.deltaTime);
            reveal.Front = UnitValue.Percent(elapsed / entranceSeconds * 100f);
        }

        private void ShowFont() => target.Font = loadedFont != null ? loadedFont : variant.font != null ? variant.font : ownFont;

        private void Apply() => target.Text = $"<{EntranceTag}>{Markup}</{EntranceTag}>";
    }
}
