using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Shows a hand dragging along a rail until a visitor has dragged.</summary>
    public sealed class DragHint : MonoBehaviour
    {
        private const float FadeRate = 5f;

        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform hand;

        [Tooltip("Half the width of the sweep and how far it bows down in its middle.")]
        [SerializeField] private Vector2 reach = new(190f, 60f);

        private bool dismissed;

        /// <summary>Hides the hint for good.</summary>
        public void Dismiss() => dismissed = true;

        /// <summary>Puts the hand at <paramref name="position"/> of its sweep, from -1 to 1.</summary>
        public void Sweep(float position)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, dismissed ? 0f : 1f, FadeRate * Time.deltaTime);
            hand.anchoredPosition = new Vector2(position * reach.x, -Mathf.Sqrt(1f - position * position) * reach.y);
        }
    }
}
