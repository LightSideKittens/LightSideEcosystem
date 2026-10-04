using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Per-frame series of one measured window: the <see cref="BenchmarkFrameProbe"/> windows, the frame
    /// interval, main-thread allocation, FrameTimingManager CPU and GPU times, and the render counters.
    /// </summary>
    /// <remarks>
    /// Needs an installed <see cref="BenchmarkFrameProbe"/>. <see cref="Sample"/> runs once per frame right after
    /// <c>yield return null</c> and records the frame that just completed; sampling allocates nothing.
    /// FrameTimingManager delivers a frame only once its GPU time is known, a few frames late, so its series has
    /// the same length shifted by that latency and represents the window only on a steady workload. It reports
    /// nothing unless the player was built with Frame Timing Stats or as a development build, and some drivers
    /// report no GPU time; such series are marked unavailable rather than filled with zeros.
    /// </remarks>
    public sealed class BenchmarkFrameSampler : IDisposable
    {
        readonly List<float> frameMs;
        readonly List<float> updateMs;
        readonly List<float> canvasMs;
        readonly List<float> intervalMs;
        readonly List<float> allocatedBytes;
        readonly List<float> cpuFrameMs;
        readonly List<float> cpuMainMs;
        readonly List<float> cpuPresentWaitMs;
        readonly List<float> cpuRenderMs;
        readonly List<float> gpuMs;
        readonly Counter[] counters;
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly bool frameTimingEnabled;
        ulong lastTimingFrame;
        long totalAllocatedBytes;
        int collections;
        int timedFrames;

        /// <summary>Starts the render counters and reserves <paramref name="frames"/> samples per series.</summary>
        public BenchmarkFrameSampler(int frames)
        {
            frameMs = new List<float>(frames);
            updateMs = new List<float>(frames);
            canvasMs = new List<float>(frames);
            intervalMs = new List<float>(frames);
            allocatedBytes = new List<float>(frames);
            cpuFrameMs = new List<float>(frames);
            cpuMainMs = new List<float>(frames);
            cpuPresentWaitMs = new List<float>(frames);
            cpuRenderMs = new List<float>(frames);
            gpuMs = new List<float>(frames);
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            counters = new[]
            {
                new Counter("drawCalls", "Draw Calls Count", frames),
                new Counter("setPassCalls", "SetPass Calls Count", frames),
                new Counter("batches", "Batches Count", frames),
                new Counter("triangles", "Triangles Count", frames),
                new Counter("vertices", "Vertices Count", frames)
            };
        }

        /// <summary>Frames recorded so far.</summary>
        public int Frames => frameMs.Count;

        /// <summary>Records the frame that just completed.</summary>
        public void Sample()
        {
            frameMs.Add((float)BenchmarkFrameProbe.LastFrameMilliseconds);
            updateMs.Add((float)BenchmarkFrameProbe.LastMilliseconds);
            canvasMs.Add((float)BenchmarkFrameProbe.LastCanvasMilliseconds);
            intervalMs.Add(Time.unscaledDeltaTime * 1000f);
            var allocated = BenchmarkFrameProbe.LastFrameAllocatedBytes;
            allocatedBytes.Add(allocated);
            totalAllocatedBytes += allocated;
            collections += BenchmarkFrameProbe.LastFrameCollections;
            foreach (var counter in counters)
                counter.Sample();

            if (!frameTimingEnabled) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) == 0) return;
            var latest = timing[0];
            if (latest.frameStartTimestamp == lastTimingFrame) return;
            lastTimingFrame = latest.frameStartTimestamp;
            timedFrames++;
            AddPositive(cpuFrameMs, (float)latest.cpuFrameTime);
            AddPositive(cpuMainMs, (float)latest.cpuMainThreadFrameTime);
            cpuPresentWaitMs.Add((float)latest.cpuMainThreadPresentWaitTime);
            AddPositive(cpuRenderMs, (float)latest.cpuRenderThreadFrameTime);
            AddPositive(gpuMs, (float)latest.gpuFrameTime);
        }

        /// <summary>Summaries of every series; raw samples of the timing series when <paramref name="includeSamples"/> is set.</summary>
        public JObject Serialize(bool includeSamples)
        {
            var result = new JObject
            {
                ["frames"] = frameMs.Count,
                ["frameMs"] = BenchmarkStatistics.Summarize(frameMs, includeSamples),
                ["updateMs"] = BenchmarkStatistics.Summarize(updateMs, includeSamples),
                ["canvasMs"] = BenchmarkStatistics.Summarize(canvasMs, includeSamples),
                ["intervalMs"] = BenchmarkStatistics.Summarize(intervalMs, includeSamples),
                ["allocatedBytes"] = SerializeAllocation(),
                ["gcCollections"] = collections
            };

            if (!frameTimingEnabled)
            {
                result["frameTiming"] = Unavailable(
                    "FrameTimingManager is disabled: the player was built without Frame Timing Stats.");
            }
            else
            {
                result["frameTiming"] = new JObject
                {
                    ["status"] = timedFrames > 0 ? "measured" : "unavailable",
                    ["frames"] = timedFrames
                };
                result["cpuFrameMs"] = Timed(cpuFrameMs, includeSamples, "CPU frame time");
                result["cpuMainMs"] = Timed(cpuMainMs, includeSamples, "main thread time");
                result["cpuPresentWaitMs"] = timedFrames > 0
                    ? BenchmarkStatistics.Summarize(cpuPresentWaitMs, includeSamples)
                    : Unavailable("FrameTimingManager delivered no frame.");
                result["cpuRenderMs"] = Timed(cpuRenderMs, includeSamples, "render thread time");
                result["gpuMs"] = Timed(gpuMs, includeSamples, "GPU time");
            }

            var render = new JObject();
            foreach (var counter in counters)
                render[counter.Key] = counter.Serialize();
            result["render"] = render;
            return result;
        }

        /// <summary>Stops the render counters.</summary>
        public void Dispose()
        {
            foreach (var counter in counters)
                counter.Dispose();
        }

        JToken Timed(List<float> series, bool includeSamples, string label)
        {
            if (timedFrames == 0) return Unavailable("FrameTimingManager delivered no frame.");
            if (series.Count * 2 < timedFrames)
                return Unavailable($"The platform reported {label} for {series.Count} of {timedFrames} frames.");
            return BenchmarkStatistics.Summarize(series, includeSamples);
        }

        JObject SerializeAllocation()
        {
            var summary = BenchmarkStatistics.Summarize(allocatedBytes, false, 0);
            var allocatingFrames = 0;
            foreach (var bytes in allocatedBytes)
                if (bytes > 0) allocatingFrames++;
            summary["total"] = totalAllocatedBytes;
            summary["allocatingFrames"] = allocatingFrames;
            summary["scope"] = "mainThread";
            return summary;
        }

        static void AddPositive(List<float> series, float value)
        {
            if (value > 0f) series.Add(value);
        }

        static JObject Unavailable(string reason) => new()
        {
            ["status"] = "unavailable",
            ["reason"] = reason
        };

        sealed class Counter : IDisposable
        {
            readonly string name;
            readonly List<float> values;
            ProfilerRecorder recorder;

            public Counter(string key, string name, int frames)
            {
                Key = key;
                this.name = name;
                values = new List<float>(frames);
                recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1);
            }

            public string Key { get; }

            public void Sample()
            {
                if (recorder.Valid) values.Add(recorder.LastValue);
            }

            public JToken Serialize()
            {
                if (!recorder.Valid || values.Count == 0)
                    return Unavailable($"The player exposes no '{name}' counter.");
                var summary = BenchmarkStatistics.Summarize(values, false, 0);
                return new JObject
                {
                    ["median"] = summary["median"],
                    ["min"] = summary["min"],
                    ["max"] = summary["max"]
                };
            }

            public void Dispose() => recorder.Dispose();
        }
    }
}
