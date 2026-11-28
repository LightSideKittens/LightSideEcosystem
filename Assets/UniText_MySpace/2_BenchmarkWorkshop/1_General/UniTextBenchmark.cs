using System;
using System.Collections.Generic;
using LightSide;
using UnityEngine;

public class UniTextBenchmark : UGuiTextBenchmarkBase
{
    public override string SystemName => "UniText";

    /// <summary>The default parallel configuration first: arms of a case list UniText's default before its variants.</summary>
    static readonly string[] Engines = { BenchmarkEngines.UniTextParallel, BenchmarkEngines.UniTextSingleThreaded };

    public override IReadOnlyList<string> EngineIds => Engines;

    StylePreset richPreset;
    Action countCommit;

    public override void SelectEngine(string engineId)
    {
        var parallel = engineId switch
        {
            BenchmarkEngines.UniTextParallel => true,
            BenchmarkEngines.UniTextSingleThreaded => false,
            _ => throw new ArgumentException($"UniText measures no engine '{engineId}'.", nameof(engineId))
        };
        UniText.UseParallel = parallel;
        GlyphAtlas.forceSingleThreaded = !parallel;
    }

    protected override void OnBeginSession()
    {
        CatZones.MuteAll = true;
        countCommit ??= () => commitCount++;
        if (UniTextBenchmarkEnvironment.Plain) return;
        var template = (UniText)GetInstanceComponent(prefab);
        LookCalibrationProfile.ApplyUniText(template, LookCalibrationProfile.House);
        if (UniTextBenchmarkEnvironment.MergedLayers)
            template.SetWholeText<PaintOrderModifier>("glyph,false,true");
    }

    protected override void OnEndSession()
    {
        UniText.UseParallel = true;
        GlyphAtlas.forceSingleThreaded = false;
        CatZones.MuteAll = false;
        if (richPreset != null)
        {
            Destroy(richPreset);
            richPreset = null;
        }
    }

    protected override bool CountsCommits => true;
    protected override void WatchCommits(Component instance) => ((UniText)instance).LayoutCommitted += countCommit;
    protected override void UnwatchCommits(Component instance) => ((UniText)instance).LayoutCommitted -= countCommit;

    protected override Component GetInstanceComponent(GameObject go) => go.GetComponent<UniText>();

    protected override void SetText(Component instance, string text) => ((UniText)instance).Text = text;
    protected override void SetFontSize(Component instance, float size) => ((UniText)instance).FontSize = size;
    protected override void SetColor(Component instance, Color color) => ((UniText)instance).color = color;
    protected override void SetWordWrap(Component instance, bool enabled) => ((UniText)instance).WordWrap = enabled;
    protected override void SetAutoSize(Component instance, bool enabled) => ((UniText)instance).AutoSize = enabled;

    protected override string DescribeInspectionInstance(Component instance, int index)
    {
        var text = (UniText)instance;
        return $"{base.DescribeInspectionInstance(instance, index)}, textLen={text.Text?.Length ?? 0}, fontSize={text.FontSize:F1}, color={text.color}, wrap={text.WordWrap}, autoSize={text.AutoSize}, globalPreset={text.UseGlobalStylePreset}, stylePresets={text.StylePresets.Count}, dirty={text.CurrentDirtyFlags}";
    }

    /// <summary>
    /// Rich on = register a runtime preset whose tag rules parse the corpus's markup (the same
    /// &lt;b&gt;/&lt;i&gt;/&lt;u&gt;/&lt;s&gt;/&lt;color&gt;/&lt;size&gt;/&lt;sub&gt;/&lt;sup&gt;/&lt;uppercase&gt;/&lt;lowercase&gt; set TMP and UI Toolkit
    /// parse natively); rich off = remove it. The project's global preset is force-disabled either way,
    /// so markup parses only in the rich phase — fair against TMP/UITK, which likewise start rich off.
    /// </summary>
    protected override void SetRichText(Component instance, bool enabled)
    {
        var text = (UniText)instance;
        text.UseGlobalStylePreset = false;
        if (enabled)
        {
            richPreset ??= BuildRichPreset();
            if (!text.StylePresets.Contains(richPreset)) text.StylePresets.Add(richPreset);
        }
        else if (richPreset != null)
        {
            text.StylePresets.Remove(richPreset);
        }
    }

    static StylePreset BuildRichPreset()
    {
        var preset = ScriptableObject.CreateInstance<StylePreset>();
        preset.Styles.Add(Style.Tag(new BoldModifier(), "b"));
        preset.Styles.Add(Style.Tag(new ItalicModifier(), "i"));
        preset.Styles.Add(Style.Tag(new UnderlineModifier(), "u"));
        preset.Styles.Add(Style.Tag(new StrikethroughModifier(), "s"));
        preset.Styles.Add(Style.Tag(new ColorModifier(), "color"));
        preset.Styles.Add(Style.Tag(new SizeModifier(), "size"));
        preset.Styles.Add(Style.Tag(new ScriptPositionModifier(), "sup", "super"));
        preset.Styles.Add(Style.Tag(new ScriptPositionModifier(), "sub", "sub"));
        preset.Styles.Add(Style.Tag(new UppercaseModifier(), "uppercase"));
        preset.Styles.Add(Style.Tag(new LowercaseModifier(), "lowercase"));
        return preset;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("UniText/Run UniText Memory Benchmark")]
    private static void RunFromMenu()
    {
        var test = ObjectUtils.FindAny<UniTextBenchmark>();
        if (test != null)
            test.RunBenchmark();
        else
            Debug.LogError("No UniTextBenchmark found in scene.");
    }
#endif
}
