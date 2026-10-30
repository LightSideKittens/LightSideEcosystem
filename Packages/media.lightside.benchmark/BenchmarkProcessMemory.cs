using System;
using System.Runtime.InteropServices;

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

    /// <summary>Reads the <see cref="ProcessMemory"/> of the running player without managed allocation.</summary>
    /// <remarks>
    /// On Android and Linux a read walks every mapping of the process (<c>/proc/self/smaps_rollup</c>) and can take
    /// milliseconds, so it belongs to settled checkpoints rather than to every frame. Graphics driver memory is part of
    /// none of the values. The editor reports nothing: its own memory would hide the player's.
    /// </remarks>
    public static class BenchmarkProcessMemory
    {
        /// <summary>Whether this player reads its process memory.</summary>
        public static bool Available { get; }

        /// <summary>Why <see cref="Available"/> is false; null when it is true.</summary>
        public static string UnavailableReason { get; }

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
        }

        /// <summary>Current memory of the process.</summary>
        /// <exception cref="InvalidOperationException">The player reads no process memory (<see cref="Available"/> is false), or the system refused the read.</exception>
        public static ProcessMemory Read()
        {
            if (!Available) throw new InvalidOperationException(UnavailableReason);
            if (!TryReadPlatform(out var memory, out var failure)) throw new InvalidOperationException(failure);
            return memory;
        }

#if (UNITY_ANDROID || UNITY_STANDALONE_LINUX) && !UNITY_EDITOR
        static readonly byte[] RollupPath = System.Text.Encoding.ASCII.GetBytes("/proc/self/smaps_rollup\0");
        static readonly byte[] Buffer = new byte[8192];
        static readonly byte[] Chunk = new byte[2048];
        static readonly byte[] PssKey = System.Text.Encoding.ASCII.GetBytes("Pss:");
        static readonly byte[] PrivateCleanKey = System.Text.Encoding.ASCII.GetBytes("Private_Clean:");
        static readonly byte[] PrivateDirtyKey = System.Text.Encoding.ASCII.GetBytes("Private_Dirty:");
        static readonly byte[] SwapPssKey = System.Text.Encoding.ASCII.GetBytes("SwapPss:");

        [DllImport("c")]
        static extern int open(byte[] path, int flags);

        [DllImport("c")]
        static extern IntPtr read(int fd, byte[] buffer, UIntPtr count);

        [DllImport("c")]
        static extern int close(int fd);

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = -1, clean = -1, pss = -1 };
            var fd = open(RollupPath, 0);
            if (fd < 0)
            {
                failure = "The kernel has no /proc/self/smaps_rollup (Linux 4.14 or a backport is required).";
                return false;
            }
            var length = 0;
            while (length < Buffer.Length)
            {
                var count = (int)(long)read(fd, Chunk, (UIntPtr)(uint)Math.Min(Chunk.Length, Buffer.Length - length));
                if (count <= 0) break;
                Array.Copy(Chunk, 0, Buffer, length, count);
                length += count;
            }
            close(fd);

            long privateDirty = -1, swapPss = 0, privateClean = -1, pss = -1;
            for (var line = 0; line < length;)
            {
                long value;
                if (TryField(line, length, PrivateDirtyKey, out value)) privateDirty = value;
                else if (TryField(line, length, SwapPssKey, out value)) swapPss = value;
                else if (TryField(line, length, PrivateCleanKey, out value)) privateClean = value;
                else if (TryField(line, length, PssKey, out value)) pss = value;
                while (line < length && Buffer[line] != (byte)'\n') line++;
                line++;
            }
            if (privateDirty < 0 || privateClean < 0 || pss < 0)
            {
                failure = "/proc/self/smaps_rollup lacks Pss, Private_Clean or Private_Dirty.";
                return false;
            }
            memory = new ProcessMemory { dirty = privateDirty + swapPss, clean = privateClean, pss = pss };
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

        [DllImport("kernel32")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32")]
        static extern bool K32GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = -1, clean = -1, pss = -1 };
            var size = (uint)Marshal.SizeOf<MemoryCounters>();
            if (!K32GetProcessMemoryInfo(GetCurrentProcess(), out var counters, size))
            {
                failure = "K32GetProcessMemoryInfo failed.";
                return false;
            }
            memory.dirty = (long)counters.privateUsage.ToUInt64();
            failure = null;
            return true;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern long LightSideBenchmarkPhysFootprint();

        static bool TryReadPlatform(out ProcessMemory memory, out string failure)
        {
            memory = new ProcessMemory { dirty = LightSideBenchmarkPhysFootprint(), clean = -1, pss = -1 };
            failure = memory.dirty < 0 ? "task_info(TASK_VM_INFO) failed." : null;
            return memory.dirty >= 0;
        }
#else
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
