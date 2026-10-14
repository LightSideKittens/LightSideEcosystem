using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Adaptive warmup verdict: settled once the mean of consecutive 12-frame windows agrees within 5%, or within
    /// 0.05 ms for frames too cheap for a relative bound to mean anything.
    /// </summary>
    public sealed class BenchmarkSteadyState
    {
        const int Window = 12;
        const float Tolerance = 0.05f;
        const float AbsoluteToleranceMs = 0.05f;

        readonly float[] recent = new float[Window];
        float previousMean;

        /// <summary>Frames observed so far.</summary>
        public int Frames { get; private set; }

        /// <summary>Whether the last two complete windows agreed.</summary>
        public bool Settled { get; private set; }

        /// <summary>Records the cost of one more frame.</summary>
        public void Observe(float milliseconds)
        {
            recent[Frames % Window] = milliseconds;
            Frames++;
            if (Frames < Window * 2 || Frames % Window != 0) return;
            var mean = 0f;
            foreach (var value in recent)
                mean += value;
            mean /= Window;
            Settled = Mathf.Abs(mean - previousMean) <= Mathf.Max(previousMean * Tolerance, AbsoluteToleranceMs);
            previousMean = mean;
        }
    }
}
