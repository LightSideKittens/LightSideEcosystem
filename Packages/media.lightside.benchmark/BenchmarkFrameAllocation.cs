using System;
using Unity.Profiling;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Managed bytes allocated per frame, from Unity's "GC Allocated In Frame" counter, which development players and
    /// the editor publish and release players do not. A frame's value is published after its end-of-frame coroutines,
    /// so <see cref="LastFrameBytes"/> read during a frame describes the frame before it.
    /// </summary>
    /// <remarks>
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is no substitute: it reports zero on Unity's Mono and IL2CPP
    /// players and crashes IL2CPP players before 6000.1.4f1 (Unity issue UUM-100690).
    /// </remarks>
    public sealed class BenchmarkFrameAllocation : IDisposable
    {
        /// <summary>The Unity counter this reads.</summary>
        public const string CounterName = "GC Allocated In Frame";

        /// <summary>What a player without the counter reports instead of allocation figures.</summary>
        public const string UnavailableReason =
            "Unity publishes 'GC Allocated In Frame' only in development players and the editor, and " +
            "GC.GetAllocatedBytesForCurrentThread reports zero in Unity players.";

        ProfilerRecorder recorder;

        /// <summary>Starts reading the counter.</summary>
        public BenchmarkFrameAllocation() =>
            recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, CounterName, 1);

        /// <summary>Whether this player publishes the counter.</summary>
        public bool Available => recorder.Valid;

        /// <summary>Managed bytes allocated during the most recently completed frame.</summary>
        public long LastFrameBytes => recorder.LastValue;

        /// <summary>Stops reading the counter.</summary>
        public void Dispose() => recorder.Dispose();
    }
}
