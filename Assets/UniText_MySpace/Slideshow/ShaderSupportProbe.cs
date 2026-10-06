#if UNITEXT_SLIDESHOW
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using LightSide;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Diagnostic of the LightSide surface programs a device can run. Each program draws a white colour-glyph quad
/// offscreen and reads it back: a working program reads white, Unity's error shader magenta. The report, with the
/// device's graphics capabilities and every warning and error logged meanwhile, stays on screen until dismissed.
/// </summary>
public sealed class ShaderSupportProbe : MonoBehaviour
{
    private const int maxMessages = 40;
    private const int maxMessageLength = 400;

    private static readonly List<string> lines = new();
    private static readonly List<string> messages = new();
    private static bool dismissed;

    private static readonly string[] copies =
    {
        "Diag_Full_380", "Diag_Full_380_min", "Diag_Text_min", "Diag_Text_novtf", "Diag_Text_nocolor", "Diag_Text_noclip",
        "Diag_Full_noimage", "Diag_Full_nounion", "Diag_Full_noboth",
    };

    private RenderTexture target;
    private Texture2D pixel;
    private Mesh quad;
    private CommandBuffer commands;
    private MaterialPropertyBlock properties;
    private Texture2DArray white;
    private Texture2DArray inside;
    private GUIStyle style;
    private Vector2 scroll;

    /// <summary>Tests every program, logs the report and shows it until dismissed. Runs before the first frame.</summary>
    public static void Run()
    {
        Application.logMessageReceivedThreaded += Capture;
        Report("UniText GPU diagnostic: please photograph this screen, then tap Continue.");
        Report($"{SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceVersion}");
        Report($"{SystemInfo.graphicsDeviceType} | shader level {SystemInfo.graphicsShaderLevel} | instancing {SystemInfo.supportsInstancing} | " +
                  $"2D arrays {SystemInfo.supports2DArrayTextures} ({SystemInfo.maxTextureArraySlices} slices) | Unity {Application.unityVersion}");
        Report($"LightSide profile {LightSideSettings.ShaderFeatures}");

        var probe = new GameObject(nameof(ShaderSupportProbe)).AddComponent<ShaderSupportProbe>();
        DontDestroyOnLoad(probe.gameObject);
        probe.Prepare();
        probe.Test("UI/Default (Unity)", new Material(Canvas.GetDefaultCanvasMaterial().shader));
        foreach (var tier in new[] { LightSideSurfaceTier.Text, LightSideSurfaceTier.StyledText, LightSideSurfaceTier.UnitedText, LightSideSurfaceTier.Full })
            probe.Test($"LightSide/UI {tier}", new Material(LightSideMaterials.Ui(tier)));
        foreach (var name in copies)
        {
            var shader = Resources.Load<Shader>(name);
            if (shader == null)
                Report($"{name}: not in the build");
            else
                probe.Test(name, new Material(shader));
        }
    }

    private static void Report(string line)
    {
        lines.Add(line);
        Debug.Log("[ShaderSupport] " + line);
    }

    /// <summary>Waits until the report is dismissed or <paramref name="timeout"/> seconds pass.</summary>
    public static IEnumerator WaitDismissed(float timeout)
    {
        var end = Time.realtimeSinceStartup + timeout;
        while (!dismissed && Time.realtimeSinceStartup < end)
            yield return null;
        dismissed = true;
    }

    private static void Capture(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Log && message.IndexOf("shader", StringComparison.OrdinalIgnoreCase) < 0 &&
            message.IndexOf("GLSL", StringComparison.OrdinalIgnoreCase) < 0)
            return;
        if (message.StartsWith("[ShaderSupport]", StringComparison.Ordinal) ||
            message.StartsWith("[ShaderCompile]", StringComparison.Ordinal))
            return;
        lock (messages)
        {
            if (messages.Count >= maxMessages) return;
            var text = message.Length > maxMessageLength ? message.Substring(0, maxMessageLength) + "…" : message;
            messages.Add($"{type}: {text}");
        }
    }

    private void Prepare()
    {
        target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        target.Create();
        pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        quad = CreateQuad();
        commands = new CommandBuffer { name = nameof(ShaderSupportProbe) };
        properties = new MaterialPropertyBlock();
        white = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
        white.SetPixels32(new[] { new Color32(255, 255, 255, 255) }, 0);
        white.Apply(false, true);
        inside = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
        inside.SetPixels32(new[] { new Color32(0, 0, 0, 0) }, 0);
        inside.Apply(false, true);
        properties.SetTexture("_LightSideGlyphColor", white);
        properties.SetTexture("_LightSideGlyphSdf", inside);
        properties.SetTexture("_LightSideGlyphMsdf", inside);
    }

    private void Test(string name, Material material)
    {
        Debug.Log("[ShaderSupport] drawing " + name);
        var supported = material.shader.isSupported;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var color = Draw(material);
            var ms = stopwatch.Elapsed.TotalMilliseconds;
            Report($"{name}: {Verdict(color)} | {color.r},{color.g},{color.b},{color.a} | supported {supported}->{material.shader.isSupported} | {ms:F0} ms");
        }
        catch (Exception e)
        {
            Report($"{name}: draw threw {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            Destroy(material);
        }
    }

    private static string Verdict(Color32 c)
    {
        if (c.r > 200 && c.g < 80 && c.b > 200) return "MAGENTA";
        if (c.r > 200 && c.g > 200 && c.b > 200) return "OK";
        if (c.r < 60 && c.g > 200 && c.b < 60) return "NOTHING DRAWN";
        return "OTHER";
    }

    private Color32 Draw(Material material)
    {
        commands.Clear();
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(false, true, Color.green);
        commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);
        Graphics.ExecuteCommandBuffer(commands);
        var active = RenderTexture.active;
        RenderTexture.active = target;
        pixel.ReadPixels(new Rect(1, 1, 1, 1), 0, 0, false);
        RenderTexture.active = active;
        return pixel.GetPixel(0, 0);
    }

    private void OnGUI()
    {
        if (dismissed) return;
        var scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 480f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        var width = Screen.width / scale;
        var height = Screen.height / scale;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        style ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11, normal = { textColor = Color.white } };
        GUILayout.BeginArea(new Rect(6, 6, width - 12, height - 12));
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var line in lines)
            GUILayout.Label(line, style);
        lock (messages)
            foreach (var message in messages)
                GUILayout.Label(message, style);
        GUILayout.EndScrollView();
        if (GUILayout.Button("Continue", GUILayout.Height(40)))
            dismissed = true;
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= Capture;
        commands.Release();
        Destroy(quad);
        Destroy(pixel);
        Destroy(target);
        Destroy(white);
        Destroy(inside);
    }

    /// <summary>
    /// A colour-glyph quad over the whole target in the canvas vertex layout: atlas UV at the centre of layer 0,
    /// UV1.w the packed colour-glyph surface kind, white vertex colour.
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
        var uv0 = new Vector4(0.5f, 0.5f, 0f, 1f);
        quad.SetUVs(0, new[] { uv0, uv0, uv0, uv0 });
        var uv1 = new Vector4(0f, 0f, 0f, LightSideCore.PackSurface(LightSideSurfaceKind.GlyphColor));
        quad.SetUVs(1, new[] { uv1, uv1, uv1, uv1 });
        quad.SetUVs(2, new Vector4[4]);
        quad.SetUVs(3, new Vector4[4]);
        quad.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
        quad.UploadMeshData(true);
        return quad;
    }
}
#endif
