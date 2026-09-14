using System;
using System.IO;
using LightSide;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

internal sealed class UniTextOnlyBuildProcessor : BuildPlayerProcessor
{
    public override int callbackOrder => -100;

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        var args = Environment.GetCommandLineArgs();
        var enabled = false;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "-ciUniTextOnly") enabled = args[i + 1] == "true";
        if (!enabled) return;

        ValidateUniTextOnly();
    }

    private static void ValidateUniTextOnly()
    {
        LightSideSettingsProvider.Synchronize();
        const LightSideShaderFeature expected = LightSideShaderFeature.Glyphs |
            LightSideShaderFeature.RoundedRectangles;
        var actual = LightSideSettings.ShaderFeatures;
        var settings = LightSideSettings.Instance;
        using var serializedSettings = new SerializedObject(settings);
        var serialized = serializedSettings.FindProperty("shaderFeatures");
        if (actual != expected || serialized.intValue != (int)expected)
            throw new BuildFailedException($"UniText Only requires {expected}; discovered {actual}, saved {serialized.intValue}.");

        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/UniTextOnlyShaderProfile.json", JsonUtility.ToJson(new ShaderProfile
        {
            unityVersion = Application.unityVersion,
            shaderFeatures = actual.ToString(),
            shaderFeatureMask = serialized.intValue
        }, true));
        Debug.Log($"[CI] UniText Only shader profile verified: {actual} ({serialized.intValue}).");
    }

    [Serializable]
    private sealed class ShaderProfile
    {
        public string unityVersion;
        public string shaderFeatures;
        public int shaderFeatureMask;
    }

}
