using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Counts a score up in steps, rolling its digits.</summary>
    [RequireComponent(typeof(UniTextBase))]
    public sealed class ScoreTicker : MonoBehaviour
    {
        private const string RollTag = "roll";
        private const int Wrap = 1000000;

        [SerializeField] private int step = 250;
        [SerializeField, Min(0.1f)] private float interval = 2.2f;
        [SerializeField, Min(0.1f)] private float rollSeconds = 1.1f;

        private UniTextBase text;
        private RollingModifier roll;
        private int score;
        private float elapsed;

        private void Awake()
        {
            text = GetComponent<UniTextBase>();
            roll = new RollingModifier();
            text.Styles.Add(Style.Tag(roll, RollTag));
            Next();
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var left = 1f - Mathf.Clamp01(elapsed / rollSeconds);
            roll.Roll = 8f * left * left;
            if (elapsed >= interval) Next();
        }

        private void Next()
        {
            elapsed = 0f;
            score = (score + step) % Wrap;
            text.Text = $"<{RollTag}>{score:D6}</{RollTag}>";
        }
    }
}
