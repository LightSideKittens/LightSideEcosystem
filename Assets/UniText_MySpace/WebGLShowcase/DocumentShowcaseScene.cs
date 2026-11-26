using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>A reading page; variants open different documents, each in its own view.</summary>
    public sealed class DocumentShowcaseScene : ShowcaseScene
    {
        /// <summary>A document of the page and the view that shows it.</summary>
        [Serializable]
        public struct Document
        {
            public string variant;
            public GameObject view;

            [Tooltip("Text the view shows; empty for a view that loads its own.")]
            public UniText text;
        }

        [SerializeField] private Document[] documents;

        private Document shown;

        public override string Text => string.Empty;
        public override string Markup => shown.text != null ? shown.text.Text : string.Empty;
        public override IEnumerable<string> Variants => documents.Select(document => document.variant);

        public override void Enter() => SetVariant(documents[0].variant);

        public override void SetVariant(string id)
        {
            var index = Array.FindIndex(documents, candidate => candidate.variant == id);
            if (index < 0) throw new ArgumentException($"Showcase scene '{Id}' has no document '{id}'.", nameof(id));
            foreach (var document in documents) document.view.SetActive(false);
            shown = documents[index];
            shown.view.SetActive(true);
        }
    }
}
