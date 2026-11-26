using System.Globalization;
using System.Text;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Runs a smooth wave of weight and width along a word set in a variable font.</summary>
    public sealed class WeightWave : MonoBehaviour
    {
        [SerializeField] private UniTextBase target;
        [SerializeField] private string word = "Flexible";

        [Tooltip("Tag of the variation modifier: weight, then width.")]
        [SerializeField] private string variationTag = "var";

        [SerializeField] private Vector2 weights = new(100f, 1000f);
        [SerializeField] private Vector2 widths = new(60f, 140f);

        [Tooltip("Seconds of one full swing of a letter, from thinnest to heaviest and back.")]
        [SerializeField, Min(0.1f)] private float period = 2.6f;

        [Tooltip("Phase between neighboring letters, in radians.")]
        [SerializeField] private float spread = 0.6f;

        private readonly StringBuilder markup = new();
        private string[] letters;
        private float elapsed;

        private void Awake()
        {
            var info = new StringInfo(word);
            letters = new string[info.LengthInTextElements];
            for (var i = 0; i < letters.Length; i++) letters[i] = info.SubstringByTextElements(i, 1);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var angle = elapsed / period * Mathf.PI * 2f;
            markup.Clear();
            for (var i = 0; i < letters.Length; i++)
            {
                var level = 0.5f - 0.5f * Mathf.Cos(angle - i * spread);
                var weight = Mathf.RoundToInt(Mathf.Lerp(weights.x, weights.y, level));
                var width = Mathf.RoundToInt(Mathf.Lerp(widths.x, widths.y, level));
                markup.Append('<').Append(variationTag).Append('=').Append(weight).Append(',').Append(width).Append('>')
                    .Append(letters[i]).Append("</").Append(variationTag).Append('>');
            }
            target.Text = markup.ToString();
        }
    }
}
