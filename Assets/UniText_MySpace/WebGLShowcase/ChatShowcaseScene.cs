using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>A live chat; variants put a ready message into its composer.</summary>
    public sealed class ChatShowcaseScene : ShowcaseScene
    {
        /// <summary>A message the page offers to send.</summary>
        [Serializable]
        public struct Prompt
        {
            public string variant;
            [TextArea] public string message;
        }

        [SerializeField] private UniTextEditable composer;
        [SerializeField] private MentionCard card;
        [SerializeField] private Prompt[] prompts;

        /// <summary>The card mentions open.</summary>
        public MentionCard Card => card;

        public override string Text => string.Empty;
        public override string Markup => composer.Text;
        public override IEnumerable<string> Variants => prompts.Select(prompt => prompt.variant);

        public override void Enter() => card.gameObject.SetActive(false);

        public override void SetVariant(string id)
        {
            var index = Array.FindIndex(prompts, candidate => candidate.variant == id);
            if (index < 0) throw new ArgumentException($"Showcase scene '{Id}' has no prompt '{id}'.", nameof(id));
            composer.SetText(prompts[index].message);
        }
    }
}
