using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Opens the card of a person when their mention in the text is activated.</summary>
    [RequireComponent(typeof(UniTextBase))]
    public sealed class MentionLinks : MonoBehaviour
    {
        private UniTextBase text;
        private MentionCard card;

        private void Awake()
        {
            text = GetComponent<UniTextBase>();
            card = GetComponentInParent<ChatShowcaseScene>().Card;
        }

        private void OnEnable() => UniTextInteractions.For(text).Bubbling += OnInteraction;

        private void OnDisable() => UniTextInteractions.For(text).Bubbling -= OnInteraction;

        private void OnInteraction(RangeInteraction interaction)
        {
            if (interaction.Kind != RangeInteractionKind.Activated || interaction.Source is not TriggerWordParseRule) return;
            card.Open(interaction.Range.PrimaryValue, text.rectTransform, interaction.AnchorRect);
        }
    }
}
