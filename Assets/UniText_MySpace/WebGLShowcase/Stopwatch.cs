using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Counts time on several texts at once, each wrapping the reading in its own markup.</summary>
    public sealed class Stopwatch : MonoBehaviour
    {
        [SerializeField] private UniTextBase[] targets;

        [Tooltip("Markup of each target around the reading, which stands for {0}.")]
        [SerializeField] private string[] markups;

        private float elapsed;
        private int shown = -1;

        private void Update()
        {
            elapsed += Time.deltaTime;
            var hundredths = (int)(elapsed * 100f);
            if (hundredths == shown) return;
            shown = hundredths;
            var reading = $"{hundredths / 6000 % 60:00}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
            for (var i = 0; i < targets.Length; i++) targets[i].Text = string.Format(markups[i], reading);
        }
    }
}
