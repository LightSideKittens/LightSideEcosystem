using System;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// The calling thread's managed allocation counter, where the player has a working one: IL2CPP from Unity
    /// 6000.1.4f1. Unity's Mono reports zero from <see cref="GC.GetAllocatedBytesForCurrentThread"/> and IL2CPP players
    /// before 6000.1.4f1 crash on it (Unity issue UUM-100690), so on those players it is never called and allocation
    /// figures are unavailable rather than zero.
    /// </summary>
    public static class BenchmarkAllocation
    {
#if ENABLE_IL2CPP
        const int ProbeBytes = 64 * 1024;
#endif

        static BenchmarkAllocation()
        {
#if ENABLE_IL2CPP
            if (!UnityAtLeast(6000, 1, 4))
            {
                UnavailableReason = $"IL2CPP players before Unity 6000.1.4f1 crash on GC.GetAllocatedBytesForCurrentThread (UUM-100690); this is {Application.unityVersion}.";
                return;
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[ProbeBytes];
            var counted = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(probe);
            if (counted < ProbeBytes)
            {
                UnavailableReason = $"GC.GetAllocatedBytesForCurrentThread counted {counted} bytes for a {ProbeBytes}-byte allocation.";
                return;
            }
            Available = true;
#else
            UnavailableReason = "Unity's Mono runtime reports zero from GC.GetAllocatedBytesForCurrentThread.";
#endif
        }

        /// <summary>Whether <see cref="CurrentThreadBytes"/> counts allocations on this player.</summary>
        public static bool Available { get; }

        /// <summary>Why <see cref="Available"/> is false; null when it is true.</summary>
        public static string UnavailableReason { get; }

        /// <summary>Managed bytes the calling thread has allocated so far; only meaningful while <see cref="Available"/>.</summary>
        public static long CurrentThreadBytes() => GC.GetAllocatedBytesForCurrentThread();

#if ENABLE_IL2CPP
        static bool UnityAtLeast(int major, int minor, int patch)
        {
            var parts = Application.unityVersion.Split('.');
            if (parts.Length < 3 || !int.TryParse(parts[0], out var actualMajor) ||
                !int.TryParse(parts[1], out var actualMinor))
                return false;
            var digits = 0;
            while (digits < parts[2].Length && char.IsDigit(parts[2][digits])) digits++;
            if (!int.TryParse(parts[2].Substring(0, digits), out var actualPatch)) return false;
            if (actualMajor != major) return actualMajor > major;
            if (actualMinor != minor) return actualMinor > minor;
            return actualPatch >= patch;
        }
#endif
    }
}
