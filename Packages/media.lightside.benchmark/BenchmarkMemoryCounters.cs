using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Unity's Memory profiler counters read as one snapshot: GC used and reserved, total used and reserved, and the
    /// memory the operating system reports for the process; where the player reads it, the process's own dirty, clean
    /// and proportional memory (<see cref="BenchmarkProcessMemory"/>).
    /// </summary>
    public sealed class BenchmarkMemoryCounters : IDisposable
    {
        static readonly string[] Names =
        {
            "GC Used Memory", "GC Reserved Memory", "Total Used Memory", "Total Reserved Memory", "System Used Memory"
        };

        static readonly string[] Keys = { "gcUsed", "gcReserved", "totalUsed", "totalReserved", "systemUsed" };

        readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Names.Length];

        /// <summary>Starts every counter.</summary>
        public BenchmarkMemoryCounters()
        {
            for (var i = 0; i < Names.Length; i++)
                recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Memory, Names[i], 1);
        }

        /// <summary>
        /// Current value of every counter in bytes, keyed gcUsed, gcReserved, totalUsed, totalReserved, systemUsed, and
        /// dirty, clean and pss for each <see cref="ProcessMemory"/> value the player reads. The keys are the same on
        /// every read of one player.
        /// </summary>
        /// <exception cref="NotSupportedException">The player publishes one of the counters under no name.</exception>
        public JObject Read()
        {
            var snapshot = new JObject();
            for (var i = 0; i < recorders.Length; i++)
            {
                if (!recorders[i].Valid)
                    throw new NotSupportedException($"The player exposes no '{Names[i]}' counter.");
                snapshot[Keys[i]] = recorders[i].CurrentValue;
            }
            if (!BenchmarkProcessMemory.Available) return snapshot;
            var process = BenchmarkProcessMemory.Read();
            snapshot["dirty"] = process.dirty;
            if (process.clean >= 0) snapshot["clean"] = process.clean;
            if (process.pss >= 0) snapshot["pss"] = process.pss;
            return snapshot;
        }

        /// <summary>
        /// Lets two frames finish, collects garbage twice around pending finalizers, and lets two more frames
        /// finish, so a following <see cref="Read"/> sees memory the live objects hold rather than garbage.
        /// </summary>
        public static IEnumerator Settle()
        {
            for (var i = 0; i < 2; i++)
                yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            for (var i = 0; i < 2; i++)
                yield return null;
        }

        /// <summary>Stops every counter.</summary>
        public void Dispose()
        {
            foreach (var recorder in recorders)
                recorder.Dispose();
        }
    }
}
