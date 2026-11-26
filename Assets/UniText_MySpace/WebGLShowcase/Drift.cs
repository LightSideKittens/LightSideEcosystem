using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Sways an object slowly around the place it starts at.</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class Drift : MonoBehaviour
    {
        [SerializeField] private Vector2 extent = new(14f, 10f);
        [SerializeField] private float speed = 0.5f;
        [SerializeField] private float phase;

        private RectTransform rect;
        private Vector2 origin;

        private void Awake()
        {
            rect = (RectTransform)transform;
            origin = rect.anchoredPosition;
        }

        private void Update()
        {
            var angle = Time.time * speed + phase;
            rect.anchoredPosition = origin + Vector2.Scale(extent, new Vector2(Mathf.Sin(angle), Mathf.Sin(angle * 1.7f)));
        }
    }
}
