using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Types a conversation line by line and starts over.</summary>
    [RequireComponent(typeof(UniTextBase))]
    public sealed class DialogueLoop : MonoBehaviour
    {
        private const string LineTag = "line";

        [SerializeField, TextArea] private string[] lines;
        [SerializeField, Min(0.1f)] private float typingSeconds = 1.8f;
        [SerializeField, Min(0f)] private float restSeconds = 1.6f;

        private UniTextBase text;
        private RevealModifier reveal;
        private float elapsed;
        private int current;

        private void Awake()
        {
            text = GetComponent<UniTextBase>();
            var handlers = new InlineRevealHandlerProvider();
            handlers.Entries.Add(new RevealHandlerEntry { Name = string.Empty, Handler = new FadeRevealHandler { Duration = 0.12f } });
            reveal = new RevealModifier { Provider = handlers };
            text.Styles.Add(Style.Tag(reveal, LineTag));
        }

        private void OnEnable() => Show(0);

        private void Update()
        {
            elapsed += Time.deltaTime;
            reveal.Front = UnitValue.Percent(Mathf.Clamp01(elapsed / typingSeconds) * 100f);
            if (elapsed >= typingSeconds + restSeconds) Show((current + 1) % lines.Length);
        }

        private void Show(int line)
        {
            current = line;
            elapsed = 0f;
            reveal.Front = UnitValue.Percent(0f);
            text.Text = $"<{LineTag}>{lines[line]}</{LineTag}>";
        }
    }
}
