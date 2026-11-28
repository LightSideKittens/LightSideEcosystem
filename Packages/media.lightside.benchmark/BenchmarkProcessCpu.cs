using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>
    /// CPU time the process has run on all its threads — main, render, Unity's job workers, engine worker pools, GC —
    /// from <c>clock_gettime(CLOCK_PROCESS_CPUTIME_ID)</c> in Android and iOS players.
    /// </summary>
    /// <remarks>
    /// The clock counts every nanosecond a thread of the process runs, spin-waits of worker pools included, and keeps
    /// the time of threads that have exited. Its difference over a window is the window's total CPU work only while every
    /// thread runs at one clock (<see cref="BenchmarkClock"/>'s hint session); under a free governor, threads on slower
    /// cores take more time for the same work.
    /// </remarks>
    public static class BenchmarkProcessCpu
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const int ClockProcessCpuTimeId = 2;

        struct TimeSpec
        {
            public IntPtr seconds;
            public IntPtr nanoseconds;
        }

        [DllImport("c", SetLastError = true)]
        static extern int clock_gettime(int clockId, out TimeSpec time);

        /// <summary>Why this player reads no process CPU time; null where it does.</summary>
        public static string UnavailableReason => null;

        /// <summary>Nanoseconds of CPU time the process has run on all its threads since it started.</summary>
        /// <exception cref="InvalidOperationException">The clock could not be read.</exception>
        public static long Nanoseconds()
        {
            if (clock_gettime(ClockProcessCpuTimeId, out var time) != 0)
                throw new InvalidOperationException($"clock_gettime(CLOCK_PROCESS_CPUTIME_ID) failed with errno {Marshal.GetLastWin32Error()}.");
            return (long)time.seconds * 1_000_000_000L + (long)time.nanoseconds;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern long LightSideBenchmarkProcessCpuNanoseconds();

        /// <summary>Why this player reads no process CPU time; null where it does.</summary>
        public static string UnavailableReason => null;

        /// <summary>Nanoseconds of CPU time the process has run on all its threads since it started.</summary>
        /// <exception cref="InvalidOperationException">The clock could not be read.</exception>
        public static long Nanoseconds()
        {
            var nanoseconds = LightSideBenchmarkProcessCpuNanoseconds();
            if (nanoseconds < 0)
                throw new InvalidOperationException("clock_gettime(CLOCK_PROCESS_CPUTIME_ID) failed.");
            return nanoseconds;
        }
#else
        /// <summary>Why this player reads no process CPU time; null where it does.</summary>
        public static string UnavailableReason => "Process CPU time is read in Android and iOS players only.";

        /// <summary>Nanoseconds of CPU time the process has run on all its threads since it started.</summary>
        /// <exception cref="NotSupportedException">The player reads no process CPU time (<see cref="UnavailableReason"/>).</exception>
        public static long Nanoseconds() => throw new NotSupportedException(UnavailableReason);
#endif

        /// <summary>Whether this player reads process CPU time.</summary>
        public static bool Available => UnavailableReason == null;

        /// <summary>The clock a result's <c>processCpuMs</c> comes from, or why there is none.</summary>
        public static JObject Describe() => Available
            ? new JObject { ["status"] = "measured", ["source"] = "clock_gettime(CLOCK_PROCESS_CPUTIME_ID)" }
            : new JObject { ["status"] = "unavailable", ["reason"] = UnavailableReason };
    }
}
