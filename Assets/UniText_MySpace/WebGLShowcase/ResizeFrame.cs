using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide.Showcase
{
    /// <summary>
    /// A handle that resizes its frame; until a visitor takes it, the frame breathes between its smallest size and
    /// the room its parent gives.
    /// </summary>
    /// <remarks>The frame is centered horizontally and hangs from its top edge, so a drag widens it on both sides.</remarks>
    public sealed class ResizeFrame : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [SerializeField] private RectTransform frame;
        [SerializeField] private Vector2 smallest = new(220f, 120f);

        [Tooltip("Space kept free between the frame and the edges of its parent.")]
        [SerializeField] private Vector2 margin = new(24f, 24f);

        [Tooltip("Seconds of one breath while nobody holds the handle.")]
        [SerializeField, Min(0.1f)] private float breath = 9f;

        [Tooltip("How much of its free height the frame gives up while breathing, from 0 to 1.")]
        [SerializeField, Range(0f, 1f)] private float heightShare = 0.35f;

        private Canvas canvas;
        private float elapsed;
        private bool held;

        private void Awake() => canvas = GetComponentInParent<Canvas>().rootCanvas;

        private void OnEnable()
        {
            held = false;
            elapsed = 0f;
        }

        private void Update()
        {
            if (held) return;
            elapsed += Time.deltaTime;
            var squeeze = (1f - Mathf.Cos(elapsed / breath * Mathf.PI * 2f)) * 0.5f;
            var largest = Largest();
            frame.sizeDelta = new Vector2(Mathf.Lerp(largest.x, smallest.x, squeeze), Mathf.Lerp(largest.y, smallest.y, squeeze * heightShare));
        }

        public void OnBeginDrag(PointerEventData eventData) => held = true;

        public void OnDrag(PointerEventData eventData)
        {
            var delta = eventData.delta / canvas.scaleFactor;
            var largest = Largest();
            frame.sizeDelta = new Vector2(Mathf.Clamp(frame.sizeDelta.x + delta.x * 2f, smallest.x, largest.x),
                Mathf.Clamp(frame.sizeDelta.y - delta.y, smallest.y, largest.y));
        }

        private Vector2 Largest()
        {
            var room = ((RectTransform)frame.parent).rect.size - margin * 2f;
            return Vector2.Max(room, smallest);
        }
    }
}
