using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Per-frame series of one measured window: the <see cref="BenchmarkFrameProbe"/> windows, the frame
    /// interval, main-thread allocation, FrameTimingManager CPU and GPU times, and the render counters.
    /// </summary>
    /// <remarks>
    /// Needs an installed <see cref="BenchmarkFrameProbe"/>. <see cref="Sample"/> runs once per frame right after
    /// <c>yield return null</c> and records the frame that just completed; sampling allocates nothing. Allocation comes
    /// from <see cref="BenchmarkFrameAllocation"/>, so release players report it as unavailable.
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
        readonly BenchmarkFrameAllocation frameAllocation = new();
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
                new Counter("drawCalls", frames, "Draw Calls Count", "Draw Calls"),
                new Counter("setPassCalls", frames, "SetPass Calls Count", "SetPass Calls"),
                new Counter("batches", frames, "Batches Count", "Batches"),
                new Counter("triangles", frames, "Triangles Count", "Triangles"),
                new Counter("vertices", frames, "Vertices Count", "Vertices")
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
            if (frameAllocation.Available) AddAllocation(frameAllocation.LastFrameBytes);
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

        /// <summary>Stops the render and allocation counters.</summary>
        public void Dispose()
        {
            foreach (var counter in counters)
                counter.Dispose();
            frameAllocation.Dispose();
        }

        void AddAllocation(long bytes)
        {
            allocatedBytes.Add(bytes);
            totalAllocatedBytes += bytes;
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
            if (allocatedBytes.Count == 0)
                return Unavailable(BenchmarkFrameAllocation.UnavailableReason);
            var summary = BenchmarkStatistics.Summarize(allocatedBytes, false, 0);
            var allocatingFrames = 0;
            foreach (var bytes in allocatedBytes)
                if (bytes > 0) allocatingFrames++;
            summary["total"] = totalAllocatedBytes;
            summary["allocatingFrames"] = allocatingFrames;
            summary["scope"] = "frame";
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

        /// <summary>
        /// One render counter, read under the first of its names the player exposes in any category; Unity versions
        /// differ in which they keep. A player missing all of them logs its Render counters once.
        /// </summary>
        sealed class Counter : IDisposable
        {
            static bool renderCountersListed;
            readonly string[] names;
            readonly List<float> values;
            ProfilerRecorder recorder;
            string name;

            public Counter(string key, int frames, params string[] names)
            {
                Key = key;
                this.names = names;
                values = new List<float>(frames);
                foreach (var candidate in names)
                {
                    recorder = new ProfilerRecorder(candidate, 1,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
                    if (recorder.Valid)
                    {
                        name = candidate;
                        return;
                    }
                    recorder.Dispose();
                }
                recorder = default;
                ListRenderCounters();
            }

            static void ListRenderCounters()
            {
                if (renderCountersListed) return;
                renderCountersListed = true;
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var found = new List<string>();
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    if (description.Category.Name == ProfilerCategory.Render.Name) found.Add(description.Name);
                }
                found.Sort(StringComparer.Ordinal);
                Debug.Log($"[BenchmarkFrameSampler] Render counters this player publishes: {string.Join(", ", found)}");
            }

            public string Key { get; }

            public void Sample()
            {
                if (recorder.Valid) values.Add(recorder.LastValue);
            }

            public JToken Serialize()
            {
                if (!recorder.Valid || values.Count == 0)
                    return Unavailable($"The player exposes none of the counters '{string.Join("', '", names)}'.");
                var summary = BenchmarkStatistics.Summarize(values, false, 0);
                return new JObject
                {
                    ["median"] = summary["median"],
                    ["min"] = summary["min"],
                    ["max"] = summary["max"],
                    ["counter"] = name
                };
            }

            public void Dispose() => recorder.Dispose();
        }
    }
}
