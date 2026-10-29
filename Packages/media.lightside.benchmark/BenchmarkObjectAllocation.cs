using System;
using System.Runtime.InteropServices;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Managed objects allocated on every thread, read from the object counter the IL2CPP runtime keeps in every
    /// player, release players included.
    /// </summary>
    /// <remarks>
    /// The count covers objects, arrays, strings and boxes whatever thread allocated them, and carries no sizes. Its
    /// first use allocates a known number of objects and must count every one of them; otherwise the counter reports
    /// itself unavailable with what it saw.
    /// </remarks>
    public static class BenchmarkObjectAllocation
    {
        const int CheckObjects = 256;

        static BenchmarkObjectAllocation()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            var before = Read();
            var kept = new object[CheckObjects];
            for (var i = 0; i < kept.Length; i++) kept[i] = new object();
            var counted = Read() - before;
            GC.KeepAlive(kept);
            if (counted > CheckObjects)
            {
                Available = true;
                return;
            }
            UnavailableReason = $"The IL2CPP object counter saw {counted} of the {CheckObjects + 1} objects its self-check allocated.";
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

        static long Read() => (long)il2cpp_stats_get_value(NewObjectCountStat);

        [DllImport("__Internal")]
        static extern ulong il2cpp_stats_get_value(int stat);
#else
        static long Read() => 0;
#endif
    }
}
