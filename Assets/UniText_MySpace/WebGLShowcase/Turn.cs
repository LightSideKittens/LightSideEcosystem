using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Keeps an object turning.</summary>
    public sealed class Turn : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 8f;

        private void Update() => transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }
}
