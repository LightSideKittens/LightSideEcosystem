using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Opt-in, allocation-free hot-path counters for the GpuUpload delivery pipeline. Disabled by
    /// default: each probe is a single <see cref="Enabled"/> bool check when off, so the probe calls
    /// are safe to leave permanently in the engine. A diagnostic harness sets <see cref="Enabled"/>,
    /// calls <see cref="Reset"/> before the window of interest, drives the workload, then reads
    /// <see cref="Report"/> once at the end — there is no per-event logging.
    /// </summary>
    public static class GpuUploadDiagnostics
    {
        /// <summary>Named hot-path probes, in flush execution order. Each is timed (Start/Stop) and carries a call count.</summary>
        public enum Probe
        {
            /// <summary>FlushPending before the tile loop: MaterializeAtlasCapacity + EnsureGpuUploadTarget.</summary>
            FlushPrologue,
            /// <summary>The delivery round's ticket reservation and whole-tile admission into the batch against the region, staging and slot bounds.</summary>
            PlanChunk,
            /// <summary>GpuUpload.TryAcquireSlot: acquiring a writable upload slot from the native slot ring.</summary>
            AcquireSlot,
            /// <summary>WriteTilePixels + DownsampleTileMips into the cached tile scratch plus the memcpy into the slot (once over all tiles in the chunk).</summary>
            StagingFill,
            /// <summary>GpuUpload.TryBeginBatch: claiming the allocation-free batch builder.</summary>
            BeginBatch,
            /// <summary>The per-tile/per-mip TryAddRegion loop: region layout + ABI struct writes.</summary>
            AddRegion,
            /// <summary>GpuUploadBatch.Submit: serialize + submit against the acquired slot (CreateSubmission and ExecuteCommandBuffer below are sub-parts of this).</summary>
            Submit,
            /// <summary>Native ls_gpu_v6_create_submission P/Invoke: batch-blob admission against the acquired slot.</summary>
            CreateSubmission,
            /// <summary>Graphics.ExecuteCommandBuffer that dispatches the upload+retire plugin events to the render thread.</summary>
            ExecuteCommandBuffer,
            /// <summary>uploadBatch.Dispose + the idempotent slot release at the end of each chunk.</summary>
            Dispose,
            /// <summary>The per-flush logZone.Meow(...) in the FlushTilePixels tail: string interpolation + Debug.Log (with editor stack-trace capture) when LIGHTSIDE_DEBUG is on and the zone is enabled.</summary>
            LogLine,
            /// <summary>GpuUploadDelivery.TryDrain: the AsyncGPUReadback.WaitForCompletion synchronous GPU stall taken on delivery backpressure.</summary>
            BackpressureDrain,
            Count
        }

        /// <summary>Master switch. When false every probe is a no-op; the harness owns turning it on around the measured window.</summary>
        public static bool Enabled;

        static readonly long[] counts = new long[(int)Probe.Count];
        static readonly long[] ticks = new long[(int)Probe.Count];
        static readonly long[] maxTicks = new long[(int)Probe.Count];

        /// <summary>Timestamp to hand back to <see cref="Stop"/>, or 0 when disabled.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>Records one timed occurrence of <paramref name="probe"/> since <paramref name="start"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Stop(Probe probe, long start)
        {
            if (!Enabled) return;
            long dt = Stopwatch.GetTimestamp() - start;
            int i = (int)probe;
            counts[i]++;
            ticks[i] += dt;
            if (dt > maxTicks[i]) maxTicks[i] = dt;
        }

        public static void Reset()
        {
            Array.Clear(counts, 0, counts.Length);
            Array.Clear(ticks, 0, ticks.Length);
            Array.Clear(maxTicks, 0, maxTicks.Length);
        }

        public static long CountOf(Probe p) => counts[(int)p];
        public static double TotalMs(Probe p) => ticks[(int)p] * 1000.0 / Stopwatch.Frequency;
        public static double MaxMs(Probe p) => maxTicks[(int)p] * 1000.0 / Stopwatch.Frequency;

        /// <summary>One formatted table (count, total ms, avg µs/call, max µs, and ms/frame when <paramref name="frames"/> &gt; 0). No side effects.</summary>
        public static string Report(int frames = 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  {"Probe",-22}{"calls",8}{"total ms",11}{"avg µs",10}{"max µs",10}" + (frames > 0 ? $"{"ms/frame",11}" : ""));
            for (int i = 0; i < (int)Probe.Count; i++)
            {
                var p = (Probe)i;
                long c = counts[i];
                double total = TotalMs(p);
                double avgUs = c > 0 ? total * 1000.0 / c : 0.0;
                double maxUs = MaxMs(p) * 1000.0;
                string perFrame = frames > 0 ? $"{total / frames,11:0.000}" : "";
                sb.AppendLine($"  {p,-22}{c,8}{total,11:0.000}{avgUs,10:0.0}{maxUs,10:0.0}{perFrame}");
            }
            return sb.ToString();
        }
    }
}
