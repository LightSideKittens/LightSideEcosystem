using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    public static class BenchmarkStatistics
    {
        public static float MedianSorted(IReadOnlyList<float> sorted) => PercentileSorted(sorted, 0.5f);

        public static float PercentileSorted(IReadOnlyList<float> sorted, float percentile)
        {
            int count = sorted?.Count ?? 0;
            if (count == 0) return 0;
            if (float.IsNaN(percentile) || percentile < 0f || percentile > 1f)
                throw new ArgumentOutOfRangeException(nameof(percentile), percentile,
                    "A percentile must be between zero and one.");

            float position = (count - 1) * percentile;
            int lower = (int)position;
            int upper = Math.Min(lower + 1, count - 1);
            float fraction = position - lower;
            return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
        }

        /// <summary>
        /// Distribution of <paramref name="samples"/>: count, median, p95, p99, mean, median absolute deviation,
        /// minimum and maximum, plus the samples in recorded order when <paramref name="includeSamples"/> is set.
        /// Values are rounded to <paramref name="decimals"/> places.
        /// </summary>
        public static JObject Summarize(IReadOnlyList<float> samples, bool includeSamples, int decimals = 3)
        {
            var count = samples.Count;
            var sorted = new List<float>(count);
            double sum = 0;
            for (var i = 0; i < count; i++)
            {
                sorted.Add(samples[i]);
                sum += samples[i];
            }
            sorted.Sort();

            var median = MedianSorted(sorted);
            var deviations = new List<float>(count);
            for (var i = 0; i < count; i++)
                deviations.Add(Math.Abs(sorted[i] - median));
            deviations.Sort();

            var summary = new JObject
            {
                ["n"] = count,
                ["median"] = Round(median, decimals),
                ["p95"] = Round(PercentileSorted(sorted, 0.95f), decimals),
                ["p99"] = Round(PercentileSorted(sorted, 0.99f), decimals),
                ["mean"] = Round(count == 0 ? 0 : sum / count, decimals),
                ["mad"] = Round(MedianSorted(deviations), decimals),
                ["min"] = Round(count == 0 ? 0 : sorted[0], decimals),
                ["max"] = Round(count == 0 ? 0 : sorted[count - 1], decimals)
            };
            if (includeSamples)
            {
                var raw = new JArray();
                for (var i = 0; i < count; i++)
                    raw.Add(Round(samples[i], decimals));
                summary["samples"] = raw;
            }
            return summary;
        }

        static double Round(double value, int decimals) => Math.Round(value, decimals);
    }
}
