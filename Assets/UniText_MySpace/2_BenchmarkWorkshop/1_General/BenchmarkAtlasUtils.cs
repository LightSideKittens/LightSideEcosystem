using UnityEngine;
using UnityEngine.Rendering;

static class BenchmarkAtlasUtils
{
    const long ReadbackBudgetBytes = 64L * 1024 * 1024;
    const int CpuSamplesPerSlice = 4096;

    public static long Checksum(Texture tex)
        => Checksum(tex, int.MaxValue);

    public static long Checksum(Texture tex, int maxSlices)
    {
        if (tex is Texture2DArray array && TryCpuChecksum(array, maxSlices, out var cpuChecksum))
            return cpuChecksum;

        if (tex == null || !SystemInfo.supportsAsyncGPUReadback || !CanWaitForReadback) return 0;
        if (EstimatedStorageBytes(tex) > ReadbackBudgetBytes) return 0;

        int w = Mathf.Min(tex.width, 256);
        int h = Mathf.Min(tex.height, 256);
        var req = AsyncGPUReadback.Request(tex, 0, 0, w, 0, h, 0, 1);
        req.WaitForCompletion();
        if (req.hasError) return 0;

        var data = req.GetData<byte>();
        long sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return sum;
    }

    /// <summary>WebGPU cannot block on a GPU readback, so the waiting checksum is unavailable there.</summary>
    static bool CanWaitForReadback =>
#if UNITY_6000_0_OR_NEWER
        SystemInfo.graphicsDeviceType != GraphicsDeviceType.WebGPU;
#else
        true;
#endif

    public static int ContentSliceCount(Texture tex, int maxSlices)
    {
        if (tex is not Texture2DArray array) return -1;

        try
        {
            int count = 0;
            int limit = Mathf.Min(array.depth, Mathf.Max(0, maxSlices));
            for (int slice = 0; slice < limit; slice++)
            {
                var data = array.GetPixelData<byte>(0, slice);
                if (SliceChecksum(data) != 0)
                    count++;
            }
            return count;
        }
        catch
        {
            return -1;
        }
    }

    public static long EstimatedStorageBytes(Texture tex)
    {
        int depth = tex switch
        {
            Texture2DArray ta => ta.depth,
            RenderTexture { dimension: TextureDimension.Tex2DArray } rt => rt.volumeDepth,
            _ => 1
        };
        return (long)tex.width * tex.height * depth * BytesPerPixel(tex);
    }

    static bool TryCpuChecksum(Texture2DArray array, int maxSlices, out long checksum)
    {
        checksum = 0;
        try
        {
            int limit = Mathf.Min(array.depth, Mathf.Max(0, maxSlices));
            for (int slice = 0; slice < limit; slice++)
            {
                var data = array.GetPixelData<byte>(0, slice);
                checksum = unchecked(checksum * 16777619L + SliceChecksum(data) + slice);
            }
            return true;
        }
        catch
        {
            checksum = 0;
            return false;
        }
    }

    static long SliceChecksum(Unity.Collections.NativeArray<byte> data)
    {
        int length = data.Length;
        if (length == 0) return 0;

        int step = Mathf.Max(1, length / CpuSamplesPerSlice);
        if ((step & 1) == 0) step++;

        long sum = 0;
        for (int i = 0; i < length; i += step)
        {
            sum += data[i];
            if (i + 1 < length) sum += data[i + 1] * 257L;
        }
        return sum + data[length - 1];
    }

    static int BytesPerPixel(Texture tex)
    {
        return tex switch
        {
            Texture2DArray { format: TextureFormat.RHalf } => 2,
            Texture2DArray { format: TextureFormat.RGBAHalf } => 8,
            Texture2DArray { format: TextureFormat.RGBA32 } => 4,
            RenderTexture { format: RenderTextureFormat.RHalf } => 2,
            RenderTexture { format: RenderTextureFormat.ARGBHalf } => 8,
            _ => 4
        };
    }
}
