using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>One scene of the demo page: what it shows, the variants the page offers and the text a visitor may type.</summary>
    public abstract class ShowcaseScene : MonoBehaviour
    {
        [Tooltip("Name the page opens the scene by.")]
        [SerializeField] private string id;

        [Tooltip("Light in the upper part of the backdrop while the scene is shown.")]
        [SerializeField] private Color upperLight;

        [Tooltip("Light in the lower part of the backdrop while the scene is shown.")]
        [SerializeField] private Color lowerLight;

        [Tooltip("Fades the scene in; empty for a scene outside the canvas.")]
        [SerializeField] private CanvasGroup group;

        public string Id => id;
        public virtual Color UpperLight => upperLight;
        public virtual Color LowerLight => lowerLight;

        /// <summary>Text the page's field edits; empty when the scene has no editable text.</summary>
        public abstract string Text { get; }

        /// <summary>Markup the scene renders, as a developer would write it.</summary>
        public abstract string Markup { get; }

        /// <summary>Names of the variants, in the order the page offers them.</summary>
        public abstract IEnumerable<string> Variants { get; }

        /// <summary>Opens the scene in its first state.</summary>
        public abstract void Enter();

        /// <summary>Shows the variant named <paramref name="variant"/>.</summary>
        /// <exception cref="ArgumentException">The scene has no such variant.</exception>
        public abstract void SetVariant(string variant);

        /// <summary>Replaces the editable text.</summary>
        /// <exception cref="NotSupportedException">The scene has no editable text.</exception>
        public virtual void SetText(string text)
            => throw new NotSupportedException($"Showcase scene '{id}' has no editable text.");

        /// <summary>Draws the editable text in <paramref name="font"/>; null returns to the scene's own font.</summary>
        /// <exception cref="NotSupportedException">The scene has no editable text.</exception>
        public virtual void SetFont(UniTextFont font)
            => throw new NotSupportedException($"Showcase scene '{id}' has no editable text.");

        /// <summary>Renders the entrance at <paramref name="progress"/>, from 0 to 1.</summary>
        public void Present(float progress)
        {
            if (group == null) return;
            group.alpha = Mathf.Clamp01(progress * 1.6f);
            var rise = 1f - Mathf.Pow(1f - progress, 3f);
            var overshoot = Mathf.Sin(progress * Mathf.PI) * 0.012f;
            transform.localScale = Vector3.one * (Mathf.Lerp(0.97f, 1f, rise) + overshoot);
        }
    }
}
