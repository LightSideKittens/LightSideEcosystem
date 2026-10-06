#if UNITEXT_SLIDESHOW
using System;
using System.Collections.Generic;
using System.Diagnostics;
using LightSide;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// Startup probe of the graphics driver's first-use compilation of the shaders a first display draws. Each
/// shader renders a quad offscreen once cold and then warm, every draw waited for by a one-pixel readback,
/// so the cold draw's excess over the warm one is the compilation.
/// </summary>
public sealed class ShaderCompileProbe
{
    private const string className = "ShaderCompile";
    private const int warmDraws = 3;

    private readonly RenderTexture target = new(4, 4, 0, RenderTextureFormat.ARGB32);
    private readonly Texture2D pixel = new(1, 1, TextureFormat.RGBA32, false);
    private readonly Mesh quad = CreateQuad();
    private readonly CommandBuffer commands = new() { name = nameof(ShaderCompileProbe) };

    /// <summary>
    /// Returns one result per shader variant, its output a JSON record of the device, the LightSide shader
    /// profile and the cold and fastest warm draw times in milliseconds. Unity's default UI shader comes first
    /// for scale, then every surface tier of the LightSide UI shader the build keeps; the world shaders are measured
    /// when the build contains them. Must run before the first frame renders: a variant already drawn is already compiled.
    /// </summary>
    public static List<TestResult> Run()
    {
        var results = new List<TestResult>();
#if UNITY_WEBGL && !UNITY_EDITOR && UNITY_6000_0_OR_NEWER
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.WebGPU)
        {
            Debug.Log("[ShaderCompile] Not measured: WebGPU has no synchronous GPU readback");
            return results;
        }
#endif
        var materials = new List<(string name, Material material)>
        {
            ("UI/Default (Unity)", new Material(Canvas.GetDefaultCanvasMaterial().shader)),
        };
        foreach (LightSideSurfaceTier tier in Enum.GetValues(typeof(LightSideSurfaceTier)))
            if (InBuild(tier))
                materials.Add(($"{LightSideCore.Shaders.Ui} {tier}", new Material(LightSideMaterials.Ui(tier))));
        foreach (var name in new[] { LightSideCore.Shaders.World, LightSideCore.Shaders.WorldLit })
        {
            var shader = LightSideCore.Shaders.Find(name);
            if (shader != null) materials.Add((name, new Material(shader)));
        }

        var probe = new ShaderCompileProbe();
        try
        {
            foreach (var (name, material) in materials)
                results.Add(probe.Measure(name, material));
        }
        finally
        {
            probe.Release();
        }
        return results;
    }

    /// <summary>Whether the build keeps <paramref name="tier"/>: the capability profile includes the surfaces it draws.</summary>
    private static bool InBuild(LightSideSurfaceTier tier)
    {
        var features = LightSideSettings.ShaderFeatures;
        return tier switch
        {
            LightSideSurfaceTier.Full => true,
            LightSideSurfaceTier.Shapes => (features & LightSideShaderFeature.Shapes) != 0,
            LightSideSurfaceTier.AtlasQuads => (features & LightSideShaderFeature.AtlasQuads) != 0,
            _ => (features & LightSideShaderFeature.Glyphs) != 0,
        };
    }

    /// <summary>Allocates the target and runs one readback first, so the first measured draw pays only for its shader.</summary>
    private ShaderCompileProbe()
    {
        target.Create();
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(false, true, Color.clear);
        Submit();
    }

    private TestResult Measure(string name, Material material)
    {
        var record = new Record
        {
            device = SystemInfo.deviceModel,
            gpu = SystemInfo.graphicsDeviceName,
            api = SystemInfo.graphicsDeviceType.ToString(),
            features = LightSideSettings.ShaderFeatures.ToString()
        };
        var result = new TestResult { ClassName = className, MethodName = name, StartTime = DateTime.UtcNow };
        try
        {
            var first = Draw(material);
            var warm = double.MaxValue;
            for (var i = 0; i < warmDraws; i++)
                warm = Math.Min(warm, Draw(material));
            record.firstMs = first;
            record.warmMs = warm;
            result.Passed = true;
            Debug.Log($"[ShaderCompile] {name}: first draw {first:F1} ms, warm draw {warm:F1} ms");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result.ErrorMessage = e.Message;
            result.StackTrace = e.ToString();
        }
        finally
        {
            Object.Destroy(material);
        }
        result.EndTime = DateTime.UtcNow;
        result.Output = JsonUtility.ToJson(record);
        return result;
    }

    private double Draw(Material material)
    {
        commands.Clear();
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(false, true, Color.clear);
        commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
        return Submit();
    }

    private double Submit()
    {
        var stopwatch = Stopwatch.StartNew();
        Graphics.ExecuteCommandBuffer(commands);
        var active = RenderTexture.active;
        RenderTexture.active = target;
        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        RenderTexture.active = active;
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private void Release()
    {
        commands.Release();
        Object.Destroy(quad);
        Object.Destroy(pixel);
        Object.Destroy(target);
    }

    /// <summary>
    /// Covers the target in the canvas vertex layout — position, normal, tangent, color and four float4
    /// texture coordinates. Vulkan and Metal compile the vertex layout into the pipeline, so a leaner mesh
    /// measures a different compilation.
    /// </summary>
    private static Mesh CreateQuad()
    {
        var quad = new Mesh();
        quad.SetVertices(new[]
        {
            new Vector3(-1, -1, 0.5f), new Vector3(-1, 1, 0.5f), new Vector3(1, 1, 0.5f), new Vector3(1, -1, 0.5f)
        });
        quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
        var tangent = new Vector4(1, 0, 0, -1);
        quad.SetTangents(new[] { tangent, tangent, tangent, tangent });
        var white = new Color32(255, 255, 255, 255);
        quad.SetColors(new[] { white, white, white, white });
        for (var channel = 0; channel < 4; channel++)
            quad.SetUVs(channel, new Vector4[4]);
        quad.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
        quad.UploadMeshData(true);
        return quad;
    }

    [Serializable]
    private sealed class Record
    {
        public string device;
        public string gpu;
        public string api;
        public string features;
        public double firstMs;
        public double warmMs;
    }
}
#endif
