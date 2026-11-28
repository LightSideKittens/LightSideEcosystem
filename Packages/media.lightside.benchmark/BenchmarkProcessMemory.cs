using System;
using System.Runtime.InteropServices;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>The process's own memory in bytes as the operating system accounts it; -1 where the platform reports no such value.</summary>
    public struct ProcessMemory
    {
        /// <summary>
        /// Private memory the system can reclaim only by writing it out: Private_Dirty plus SwapPss on Android and Linux,
        /// PrivateUsage on Windows, phys_footprint on iOS.
        /// </summary>
        public long dirty;

        /// <summary>Private file-backed memory the system can drop and read back (Private_Clean); Android and Linux only.</summary>
        public long clean;

        /// <summary>Proportional set size (Pss); Android and Linux only.</summary>
        public long pss;
    }

    /// <summary>Reads the <see cref="ProcessMemory"/> of the running player and tracks its peak without managed allocation.</summary>
    /// <remarks>
    /// On Android and Linux a read walks every mapping of the process (<c>/proc/self/smaps_rollup</c>, or <c>/proc/self/smaps</c>
    /// on kernels without it) and takes milliseconds, so it belongs to settled checkpoints rather than to every frame. GPU
    /// driver memory the system tracks apart from the process's mappings (Android memtrack) is part of none of these
    /// values, iOS phys_footprint aside; <see cref="SystemFootprint"/> reports it. The editor reports nothing: its own
    /// memory would hide the player's.
    /// The peak comes from a background thread that reads the private memory of the process every millisecond, so it
    /// includes memory held only inside a frame, which checkpoints between frames never see. A rise shorter than one
    /// interval can escape it; the platform's sleep granularity and a starved thread lengthen the interval. Peak windows
    /// do not nest: a <see cref="StartPeak"/> restarts the peak of every window still open.
    /// Sampling threads exist only in a process that called <see cref="EnableSampling"/> (a memory launch): they compete
    /// with the measured work for cores and change the clock the governor picks, so a timing launch has none and its peak
    /// windows are empty.
    /// </remarks>
    public static class BenchmarkProcessMemory
    {
        /// <summary>Whether this player reads its process memory.</summary>
        public static bool Available { get; }

        /// <summary>Why <see cref="Available"/> is false; null when it is true.</summary>
        public static string UnavailableReason { get; }

        /// <summary>Whether this player can track the peak of its process memory; peaks are sampled only after <see cref="EnableSampling"/>.</summary>
        public static bool PeakAvailable { get; }

        /// <summary>
        /// What the peak measures on this player: anonymous resident memory (RssAnon, swapped pages excluded) on Android
        /// and Linux, phys_footprint on iOS, private usage on Windows; null when <see cref="PeakAvailable"/> is false.
        /// </summary>
        public static string PeakKind { get; }

        /// <summary>Why <see cref="PeakAvailable"/> is false; null when it is true.</summary>
        public static string PeakUnavailableReason { get; }

        static readonly object PeakGate = new object();
        static Thread sampler;
        static int generation;
        static long peak;
        static string samplerFailure;

        /// <summary>Whether this player reads its footprint (<see cref="EnableSampling"/>, <see cref="SystemFootprint"/>).</summary>
        public static bool FootprintAvailable => FootprintSource != null;

        /// <summary>What the sampled footprint measures on this player; null where <see cref="FootprintAvailable"/> is false.</summary>
        public static string FootprintKind => FootprintSource == null ? null : FootprintSource + $", read back to back {FootprintIntervalMs} ms apart";

        static readonly object FootprintGate = new object();
        static Thread footprintSampler;
        static int footprintGeneration;
        static long footprintPeak;
        static long footprintSum;
        static int footprintSamples;
        static long footprintOpenedAt;
        static string footprintFailure;
        static bool systemFootprintRead;

        /// <summary>
        /// Makes this process a memory launch: peak windows (<see cref="StartPeak"/>, <see cref="PeakRecord"/>) sample from
        /// now on, and where the player reads its footprint every window also carries the footprint's start, peak, mean over
        /// the samples, end, sample count and duration (<c>footprint</c>); on iOS that peak is the one the kernel keeps for
        /// the window, which no sampling interval can miss. The footprint thread reads back to back beside the frames, and on
        /// Android each read walks every mapping of the process.
        /// </summary>
        public static void EnableSampling()
        {
            sampling = true;
            if (!FootprintAvailable) return;
            lock (FootprintGate)
            {
                if (footprintSampler != null) return;
                footprintPeak = ReadFootprintChecked();
                footprintSampler = new Thread(SampleFootprint) { IsBackground = true, Name = "Benchmark footprint sampler" };
                footprintSampler.Start();
            }
        }

        static bool sampling;

        /// <summary>Whether <see cref="EnableSampling"/> ran in this process.</summary>
        public static bool SamplingEnabled => sampling;

        /// <summary>
        /// The process's memory with graphics driver memory, as the system's own accounting reports it, in bytes:
        /// <c>{total, graphics, dirty}</c> on Android, where the system service reads the graphics part a process cannot read
        /// for itself (<c>total</c> total PSS, <c>graphics</c> its graphics part, <c>dirty</c> private dirty plus swapped-out
        /// dirty memory, graphics driver memory included); <c>{total, dirty}</c> on iOS, both phys_footprint. Null after the
        /// first call in a process and in processes that did not <see cref="EnableSampling"/> or read no footprint.
        /// </summary>
        /// <remarks>Android answers one fresh read per process every five minutes and repeats it otherwise, so one call
        /// per process at its steady point is all a launch gets.</remarks>
        /// <exception cref="InvalidOperationException">The system refused the read.</exception>
        public static JObject SystemFootprint()
        {
            if (footprintSampler == null || systemFootprintRead) return null;
            systemFootprintRead = true;
            if (!TryReadSystemFootprint(out var total, out var graphics, out var dirty))
                throw new InvalidOperationException("The system refused the read of the process's memory.");
            var record = new JObject { ["total"] = total, ["dirty"] = dirty };
            if (graphics >= 0) record["graphics"] = graphics;
            return record;
        }

        static long ReadFootprintChecked()
        {
            var footprint = ReadFootprint();
            if (footprint < 0)
                throw new InvalidOperationException($"The system refused the read of the {FootprintSource}.");
            return footprint;
        }

        static void SampleFootprint()
        {
            while (true)
            {
                var sampled = Volatile.Read(ref footprintGeneration);
                var current = ReadFootprint();
                lock (FootprintGate)
                {
                    if (current < 0)
                    {
                        footprintFailure = $"The sampling thread's read of the {FootprintSource} failed.";
                        return;
                    }
                    if (sampled == footprintGeneration) Note(current);
                }
                Thread.Sleep(FootprintIntervalMs);
            }
        }

        static long OpenFootprintWindow()
        {
            var current = ReadFootprintChecked();
            lock (FootprintGate)
            {
                if (footprintFailure != null) throw new InvalidOperationException(footprintFailure);
                footprintGeneration++;
                footprintPeak = current;
                footprintSum = current;
                footprintSamples = 1;
                footprintOpenedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            ResetKernelFootprintPeak();
            return current;
        }

        static void Note(long footprint)
        {
            if (footprint > footprintPeak) footprintPeak = footprint;
            footprintSum += footprint;
            footprintSamples++;
        }

        static JObject FootprintRecord(long start)
        {
            var current = ReadFootprintChecked();
            var kernelPeak = KernelFootprintPeak();
            lock (FootprintGate)
            {
                if (footprintFailure != null) throw new InvalidOperationException(footprintFailure);
                Note(current);
                var record = new JObject
                {
                    ["start"] = start, ["peak"] = Math.Max(footprintPeak, kernelPeak), ["mean"] = footprintSum / footprintSamples,
                    ["end"] = current, ["samples"] = footprintSamples,
                    ["durationMs"] = (System.Diagnostics.Stopwatch.GetTimestamp() - footprintOpenedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency
                };
                if (kernelPeak >= 0)
                {
                    record["sampledPeak"] = footprintPeak;
                    record["kernelPeak"] = kernelPeak;
                }
                return record;
            }
        }

        static BenchmarkProcessMemory()
        {
            try
            {
                if (TryReadPlatform(out _, out var failure))
                    Available = true;
                else
                    UnavailableReason = failure;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                UnavailableReason = $"The player cannot call the system's memory query: {e.Message}";
            }
            try
            {
                if (ReadCurrent() >= 0)
                {
                    PeakAvailable = true;
                    PeakKind = PeakSource + " sampled every millisecond";
                }
                else
                    PeakUnavailableReason = Available ? $"The system refused the read of the {PeakSource}." : UnavailableReason;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                PeakUnavailableReason = $"The player cannot call the system's memory query: {e.Message}";
            }
        }

        /// <summary>Current memory of the process.</summary>
        /// <exception cref="InvalidOperationException">The player reads no process memory (<see cref="Available"/> is false), or the system refused the read.</exception>
        public static ProcessMemory Read()
        {
            if (!Available) throw new InvalidOperationException(UnavailableReason);
            if (!TryReadPlatform(out var memory, out var failure)) throw new InvalidOperationException(failure);
            return memory;
        }

        static long ResetPeak()
        {
            var current = ReadCurrentChecked();
            lock (PeakGate)
            {
                ThrowOnSamplerFailure();
                generation++;
                peak = current;
                if (sampler == null)
                {
                    sampler = new Thread(Sample) { IsBackground = true, Name = "Benchmark memory sampler", Priority = ThreadPriority.AboveNormal };
                    sampler.Start();
                }
            }
            return current;
        }

        /// <summary>
        /// Opens a measured window: restarts the peak and returns the memory it starts from, -1 where the player tracks no
        /// peak or the process is not a memory launch (<see cref="EnableSampling"/>); <see cref="PeakRecord"/> closes the
        /// window. The first window of a memory launch starts the sampling thread, which then runs until the process exits.
        /// </summary>
        public static long StartPeak()
        {
            if (!PeakAvailable || !sampling) return -1;
            if (footprintSampler != null) footprintWindowStart = OpenFootprintWindow();
            return ResetPeak();
        }

        static long footprintWindowStart;

        /// <summary>
        /// Closes the window <see cref="StartPeak"/> opened with <paramref name="start"/>: its start, peak and growth in
        /// bytes as every suite records them, and where the player reads its footprint the footprint's start, peak, mean,
        /// end, sample count and duration (<c>footprint</c>); null for a window <see cref="StartPeak"/> did not open.
        /// </summary>
        public static JObject PeakRecord(long start)
        {
            if (start < 0) return null;
            var peak = ReadPeak();
            var record = new JObject { ["start"] = start, ["peak"] = peak, ["growth"] = peak - start };
            if (footprintSampler != null) record["footprint"] = FootprintRecord(footprintWindowStart);
            return record;
        }

        static long ReadPeak()
        {
            var current = ReadCurrentChecked();
            lock (PeakGate)
            {
                if (sampler == null) throw new InvalidOperationException($"{nameof(ReadPeak)} needs a {nameof(ResetPeak)} first.");
                ThrowOnSamplerFailure();
                return Math.Max(peak, current);
            }
        }

        static long ReadCurrentChecked()
        {
            if (!PeakAvailable) throw new InvalidOperationException(PeakUnavailableReason);
            var current = ReadCurrent();
            if (current < 0) throw new InvalidOperationException($"The system refused the read of the {PeakSource}.");
            return current;
        }

        static void ThrowOnSamplerFailure()
        {
            if (samplerFailure != null) throw new InvalidOperationException(samplerFailure);
        }

        static void Sample()
        {
            while (true)
            {
                var sampled = Volatile.Read(ref generation);
                var current = ReadCurrent();
                lock (PeakGate)
                {
                    if (current < 0)
                    {
                        samplerFailure = $"The sampling thread's read of the {PeakSource} failed.";
                        return;
                    }
                    if (sampled == generation && current > peak) peak = current;
                }
                Thread.Sleep(1);
            }
        }

#if (UNITY_ANDROID || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        static readonly byte[] MapsPath = MemoryMapsPath();
        static readonly byte[] StatmPath = System.Text.Encoding.ASCII.GetBytes("/proc/self/statm\0");
        static readonly object MapsGate = new object();
        static readonly byte[] Buffer = new byte[8192];
        static readonly byte[] Chunk = new byte[2048];
        static readonly byte[] PssKey = System.Text.Encoding.ASCII.GetBytes("Pss:");
        static readonly byte[] PrivateCleanKey = System.Text.Encoding.ASCII.GetBytes("Private_Clean:");
        static readonly byte[] PrivateDirtyKey = System.Text.Encoding.ASCII.GetBytes("Private_Dirty:");
        static readonly byte[] SwapPssKey = System.Text.Encoding.ASCII.GetBytes("SwapPss:");
        static readonly int StatmFd = open(StatmPath, 0);
        static readonly long PageSize = getpagesize();

        const string PeakSource = "anonymous resident memory (RssAnon)";

#if UNITY_ANDROID
        const string FootprintSource =
            "dirty memory (smaps Private_Dirty + SwapPss) without the GPU driver memory the system tracks for the process (memtrack), which a process cannot read for itself";
        const int FootprintIntervalMs = 1;

        static long ReadFootprint() => TryReadPlatform(out var memory, out _) ? memory.dirty : -1;

        static void ResetKernelFootprintPeak() { }

        static long KernelFootprintPeak() => -1;

        static bool TryReadSystemFootprint(out long total, out long graphics, out long dirty)
        {
            total = graphics = dirty = -1;
            if (!TryReadMaps(out _, out var swapPss, out _, out _, out _)) return false;
            try
            {
                using var player = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
                using var manager = activity.Call<UnityEngine.AndroidJavaObject>("getSystemService", "activity");
                using var process = new UnityEngine.AndroidJavaClass("android.os.Process");
                using var infos = manager.Call<UnityEngine.AndroidJavaObject>("getProcessMemoryInfo",
                    new[] { process.CallStatic<int>("myPid") });
                using var arrays = new UnityEngine.AndroidJavaClass("java.lang.reflect.Array");
                using var info = arrays.CallStatic<UnityEngine.AndroidJavaObject>("get", infos, 0);
                if (!TryMemoryStat(info, "summary.total-pss", out total) || !TryMemoryStat(info, "summary.graphics", out graphics))
                    return false;
                dirty = info.Call<int>("getTotalPrivateDirty") * 1024L + swapPss;
                return true;
            }
            catch (UnityEngine.AndroidJavaException)
            {
                return false;
            }
        }

        static bool TryMemoryStat(UnityEngine.AndroidJavaObject info, string name, out long bytes)
        {
            var ok = long.TryParse(info.Call<string>("getMemoryStat", name), out var kilobytes);
            bytes = kilobytes * 1024;
            return ok;
        }
#else
        const string FootprintSource = null;
        const int FootprintIntervalMs = 0;

        static long ReadFootprint() => -1;

        static void ResetKernelFootprintPeak() { }

        static long KernelFootprintPeak() => -1;

        static bool TryReadSystemFootprint(out long total, out long graphics, out long dirty)
        {
            total = graphics = dirty = -1;
            return false;
        }
#endif

        [DllImport("c")]
        static extern int open(byte[] path, int flags);

        [DllImport("c")]
        static extern IntPtr read(int fd, byte[] buffer, UIntPtr count);

        [DllImport("c")]
        static extern IntPtr pread64(int fd, ref byte buffer, UIntPtr count, long offset);

        [DllImport("c")]
        static extern int close(int fd);

        [DllImport("c")]
        static extern int getpagesize();

        static long ReadCurrent()
        {
            Span<byte> text = stackalloc byte[128];
            var length = (int)(long)pread64(StatmFd, ref MemoryMarshal.GetReference(text), (UIntPtr)(uint)text.Length, 0);
            long value = 0, resident = -1;
            var field = 0;
            var digits = 0;
            for (var i = 0; i < length; i++)
            {
                var c = text[i];
                if (c >= (byte)'0' && c <= (byte)'9')
                {
                    value = value * 10 + (c - (byte)'0');
                    digits++;
                    continue;
                }
                if (digits == 0) continue;
                if (field == 1) resident = value;
                else if (field == 2) return (resident - value) * PageSize;
                field++;
                value = 0;
                digits = 0;
            }
            return -1;
        }

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            var ok = TryReadMaps(out var privateDirty, out var swapPss, out var privateClean, out var pss, out failure);
            memory = ok
                ? new ProcessMemory { dirty = privateDirty + swapPss, clean = privateClean, pss = pss }
                : new ProcessMemory { dirty = -1, clean = -1, pss = -1 };
            return ok;
        }

        static bool TryReadMaps(out long privateDirty, out long swapPss, out long privateClean, out long pss, out string failure)
        {
            lock (MapsGate) return TrySumMaps(out privateDirty, out swapPss, out privateClean, out pss, out failure);
        }

        /// <summary>The kernel's rollup of every mapping where it has one (Linux 4.14 and backports), else the per-mapping file, which sums to the same figures.</summary>
        static byte[] MemoryMapsPath()
        {
            var rollup = System.Text.Encoding.ASCII.GetBytes("/proc/self/smaps_rollup\0");
            var fd = open(rollup, 0);
            if (fd < 0) return System.Text.Encoding.ASCII.GetBytes("/proc/self/smaps\0");
            close(fd);
            return rollup;
        }

        static bool TrySumMaps(out long privateDirty, out long swapPss, out long privateClean, out long pss, out string failure)
        {
            privateDirty = swapPss = privateClean = pss = 0;
            var fd = open(MapsPath, 0);
            if (fd < 0)
            {
                privateDirty = privateClean = pss = -1;
                failure = "The kernel refused /proc/self/smaps.";
                return false;
            }
            bool dirtySeen = false, cleanSeen = false, pssSeen = false;
            var filled = 0;
            while (true)
            {
                if (filled + Chunk.Length > Buffer.Length) filled = 0;
                var count = (int)(long)read(fd, Chunk, (UIntPtr)(uint)Chunk.Length);
                if (count <= 0) break;
                Array.Copy(Chunk, 0, Buffer, filled, count);
                filled += count;
                var line = 0;
                for (var end = Array.IndexOf(Buffer, (byte)'\n', 0, filled); end >= 0; end = Array.IndexOf(Buffer, (byte)'\n', line, filled - line))
                {
                    long value;
                    if (TryField(line, end, PrivateDirtyKey, out value)) { privateDirty += value; dirtySeen = true; }
                    else if (TryField(line, end, SwapPssKey, out value)) swapPss += value;
                    else if (TryField(line, end, PrivateCleanKey, out value)) { privateClean += value; cleanSeen = true; }
                    else if (TryField(line, end, PssKey, out value)) { pss += value; pssSeen = true; }
                    line = end + 1;
                }
                Array.Copy(Buffer, line, Buffer, 0, filled - line);
                filled -= line;
            }
            close(fd);
            if (!dirtySeen || !cleanSeen || !pssSeen)
            {
                privateDirty = privateClean = pss = -1;
                failure = "/proc/self/smaps lacks Pss, Private_Clean or Private_Dirty.";
                return false;
            }
            failure = null;
            return true;
        }

        static bool TryField(int start, int length, byte[] key, out long bytes)
        {
            bytes = 0;
            if (start + key.Length > length) return false;
            for (var i = 0; i < key.Length; i++)
                if (Buffer[start + i] != key[i]) return false;
            var p = start + key.Length;
            while (p < length && Buffer[p] == (byte)' ') p++;
            var kilobytes = 0L;
            var digits = 0;
            for (; p < length && Buffer[p] >= (byte)'0' && Buffer[p] <= (byte)'9'; p++, digits++)
                kilobytes = kilobytes * 10 + (Buffer[p] - (byte)'0');
            bytes = kilobytes * 1024;
            return digits > 0;
        }
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryCounters
        {
            public uint cb;
            public uint pageFaultCount;
            public UIntPtr peakWorkingSetSize;
            public UIntPtr workingSetSize;
            public UIntPtr quotaPeakPagedPoolUsage;
            public UIntPtr quotaPagedPoolUsage;
            public UIntPtr quotaPeakNonPagedPoolUsage;
            public UIntPtr quotaNonPagedPoolUsage;
            public UIntPtr pagefileUsage;
            public UIntPtr peakPagefileUsage;
            public UIntPtr privateUsage;
        }

        const string PeakSource = "private usage";
        const string FootprintSource = null;
        const int FootprintIntervalMs = 0;

        static long ReadFootprint() => -1;

        static void ResetKernelFootprintPeak() { }

        static long KernelFootprintPeak() => -1;

        static bool TryReadSystemFootprint(out long total, out long graphics, out long dirty)
        {
            total = graphics = dirty = -1;
            return false;
        }

        static readonly uint CountersSize = (uint)Marshal.SizeOf<MemoryCounters>();

        [DllImport("kernel32")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32")]
        static extern bool K32GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

        static long ReadCurrent() =>
            K32GetProcessMemoryInfo(GetCurrentProcess(), out var counters, CountersSize)
                ? (long)counters.privateUsage.ToUInt64()
                : -1;

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = ReadCurrent(), clean = -1, pss = -1 };
            failure = memory.dirty < 0 ? "K32GetProcessMemoryInfo failed." : null;
            return memory.dirty >= 0;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        const string PeakSource = "phys_footprint";
        const string FootprintSource = "phys_footprint, graphics driver memory included, with the peak the kernel keeps for the window (ri_interval_max_phys_footprint)";
        const int FootprintIntervalMs = 1;

        [DllImport("__Internal")]
        static extern long LightSideBenchmarkPhysFootprint();

        static long ReadCurrent() => LightSideBenchmarkPhysFootprint();

        static long ReadFootprint() => LightSideBenchmarkPhysFootprint();

        [DllImport("__Internal")]
        static extern long LightSideBenchmarkPhysFootprintIntervalMax();

        [DllImport("__Internal")]
        static extern int LightSideBenchmarkResetPhysFootprintInterval();

        static void ResetKernelFootprintPeak()
        {
            if (LightSideBenchmarkResetPhysFootprintInterval() != 0)
                throw new InvalidOperationException("proc_reset_footprint_interval failed.");
        }

        static long KernelFootprintPeak()
        {
            var peak = LightSideBenchmarkPhysFootprintIntervalMax();
            if (peak < 0) throw new InvalidOperationException("proc_pid_rusage(RUSAGE_INFO_V4) failed.");
            return peak;
        }

        static bool TryReadSystemFootprint(out long total, out long graphics, out long dirty)
        {
            total = dirty = LightSideBenchmarkPhysFootprint();
            graphics = -1;
            return total >= 0;
        }

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = ReadCurrent(), clean = -1, pss = -1 };
            failure = memory.dirty < 0 ? "task_info(TASK_VM_INFO) failed." : null;
            return memory.dirty >= 0;
        }
#else
        const string PeakSource = "process memory";
        const string FootprintSource = null;
        const int FootprintIntervalMs = 0;

        static long ReadCurrent() => -1;

        static long ReadFootprint() => -1;

        static void ResetKernelFootprintPeak() { }

        static long KernelFootprintPeak() => -1;

        static bool TryReadSystemFootprint(out long total, out long graphics, out long dirty)
        {
            total = graphics = dirty = -1;
            return false;
        }

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = -1, clean = -1, pss = -1 };
            failure = UnityEngine.Application.isEditor
                ? "The editor reports its own memory, not the player's."
                : "This platform reads no process memory.";
            return false;
        }
#endif
    }
}
