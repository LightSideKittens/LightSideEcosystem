using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Makes a sparkle flare up and go out.</summary>
    public sealed class Twinkle : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float period = 2.4f;

        [Tooltip("Where in its period the sparkle starts, from 0 to 1.")]
        [SerializeField] private float phase;

        private void Update()
        {
            var flare = Mathf.Max(0f, Mathf.Sin((Time.time / period + phase) * Mathf.PI * 2f));
            transform.localScale = Vector3.one * flare;
            transform.localRotation = Quaternion.Euler(0f, 0f, 24f * (1f - flare));
        }
    }
}
