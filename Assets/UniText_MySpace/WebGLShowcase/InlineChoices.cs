using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Showcase
{
    /// <summary>
    /// A sentence with dropdowns standing in its words: every choice resizes its dropdown to the caption, and the
    /// sentence wraps around the new width.
    /// </summary>
    public sealed class InlineChoices : MonoBehaviour
    {
        /// <summary>A dropdown placed in the sentence by an <c>obj</c> tag of its name.</summary>
        [Serializable]
        public struct Choice
        {
            public string name;
            public UniTextDropdown dropdown;
            public UniText caption;
        }

        [SerializeField] private UniTextBase sentence;

        [Tooltip("Markup of the sentence; {0}, {1} and so on stand for the choices in order.")]
        [TextArea(3, 8)] [SerializeField] private string markup;

        [SerializeField] private Choice[] choices;

        [Tooltip("Width a dropdown adds around its caption, in pixels.")]
        [SerializeField] private float padding = 70f;

        [Tooltip("Height of a dropdown, in em of the sentence.")]
        [SerializeField] private float height = 1.45f;

        [Tooltip("Shift of a dropdown from the baseline, in em of the sentence.")]
        [SerializeField] private float lift = -0.36f;

        [Tooltip("Space a dropdown keeps from the next word, in em of the sentence.")]
        [SerializeField] private float gap = 0.1f;

        private string[] tags;
        private bool stale;

        private void Awake()
        {
            tags = new string[choices.Length];
            foreach (var choice in choices) choice.dropdown.OnValueChanged.AddListener(_ => stale = true);
        }

        private void OnEnable() => stale = true;

        private void LateUpdate()
        {
            if (!stale) return;
            stale = false;
            var size = sentence.FontSize;
            for (var i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                var width = ((ILayoutElement)choice.caption).preferredWidth + padding;
                ((RectTransform)choice.dropdown.transform).sizeDelta = new Vector2(width, height * size);
                var em = width / size;
                tags[i] = string.Format(CultureInfo.InvariantCulture, "<obj={0},{1:0.###} {2:0.###},0 {3:0.###},{4:0.###}>",
                    choice.name, em, height, lift, em + gap);
            }
            sentence.Text = string.Format(markup, tags);
        }
    }
}
