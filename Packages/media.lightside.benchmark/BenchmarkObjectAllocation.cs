using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Managed objects allocated on every thread, read from the object counter the IL2CPP runtime keeps in every
    /// player, release players included.
    /// </summary>
    /// <remarks>
    /// The count covers objects, arrays, strings and boxes whatever thread allocated them, and carries no sizes. Its
    /// first use allocates a known number of objects on the main thread and, where the player has threads, on a
    /// worker thread, and must count every one of them; otherwise the counter reports itself unavailable with what it
    /// saw.
    /// </remarks>
    public static class BenchmarkObjectAllocation
    {
        const int CheckObjects = 256;

        static BenchmarkObjectAllocation()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            var before = Read();
            AllocateCheckObjects();
            var counted = Read() - before;
            if (counted <= CheckObjects)
            {
                UnavailableReason = $"The IL2CPP object counter saw {counted} of the {CheckObjects + 1} objects its self-check allocated.";
                return;
            }
#if !UNITY_WEBGL
            before = Read();
            var worker = new Thread(AllocateCheckObjects);
            worker.Start();
            worker.Join();
            counted = Read() - before;
            if (counted <= CheckObjects)
            {
                UnavailableReason = $"The IL2CPP object counter saw {counted} of the {CheckObjects + 1} objects its self-check allocated on a worker thread.";
                return;
            }
#endif
            Available = true;
#else
            UnavailableReason = "Only IL2CPP players count the managed allocations of every thread.";
#endif
        }

        /// <summary>Whether this player counts allocations: false outside IL2CPP players and after a failed self-check.</summary>
        public static bool Available { get; }

        /// <summary>Why <see cref="Available"/> is false; null when it is true.</summary>
        public static string UnavailableReason { get; }

        /// <summary>Managed objects this process has allocated so far; zero when unavailable.</summary>
        public static long Count => Available ? Read() : 0;

#if ENABLE_IL2CPP && !UNITY_EDITOR
        const int NewObjectCountStat = 0;

        static void AllocateCheckObjects()
        {
            var kept = new object[CheckObjects];
            for (var i = 0; i < kept.Length; i++) kept[i] = new object();
            GC.KeepAlive(kept);
        }

        static long Read() => (long)il2cpp_stats_get_value(NewObjectCountStat);

        [DllImport("__Internal")]
        static extern ulong il2cpp_stats_get_value(int stat);
#else
        static long Read() => 0;
#endif
    }
}
