using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>A scene of panels; each variant opens a panel of its own.</summary>
    public sealed class PanelShowcaseScene : ShowcaseScene
    {
        /// <summary>A panel of the scene and what the page shows as its markup.</summary>
        [Serializable]
        public struct Panel
        {
            public string variant;
            public GameObject view;

            [Tooltip("Text whose markup the page shows; empty shows Setup instead.")]
            public UniTextBase text;

            [Tooltip("What the page shows for a panel without a text: the setup behind it, as a developer would write it.")]
            [TextArea] public string setup;
        }

        [SerializeField] private Panel[] panels;

        private Panel shown;

        public override string Text => string.Empty;
        public override string Markup => shown.text != null ? shown.text.Text : shown.setup;
        public override IEnumerable<string> Variants => panels.Select(panel => panel.variant);

        public override void Enter() => SetVariant(panels[0].variant);

        public override void SetVariant(string id)
        {
            var index = Array.FindIndex(panels, candidate => candidate.variant == id);
            if (index < 0) throw new ArgumentException($"Showcase scene '{Id}' has no panel '{id}'.", nameof(id));
            foreach (var panel in panels) panel.view.SetActive(false);
            shown = panels[index];
            shown.view.SetActive(true);
        }
    }
}
