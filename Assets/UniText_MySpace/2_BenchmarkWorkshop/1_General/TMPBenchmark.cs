using System;
using System.Collections.Generic;
using LightSide;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public class TMPBenchmark : UGuiTextBenchmarkBase
{
    public override string SystemName => "TMP";

    static readonly string[] Engines = { BenchmarkEngines.Tmp };

    public override IReadOnlyList<string> EngineIds => Engines;

    Material presetMaterial, lookMaterial;
    readonly HashSet<Object> watched = new();
    Action<Object> countCommit;

    public override void SelectEngine(string engineId)
    {
        if (engineId != Engines[0]) throw new ArgumentException($"TMP measures no engine '{engineId}'.", nameof(engineId));
    }

    protected override void OnBeginSession()
    {
        countCommit ??= CountCommit;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(countCommit);
        var template = prefab.GetComponent<TMP_Text>();
        presetMaterial = template.fontSharedMaterial;
        if (UniTextBenchmarkEnvironment.Plain)
        {
            template.fontSharedMaterial = template.font.material;
            return;
        }
        lookMaterial = LookCalibrationProfile.CreateTmpMaterial(presetMaterial, template.font, LookCalibrationProfile.House);
        template.fontSharedMaterial = lookMaterial;
    }

    protected override void OnEndSession()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(countCommit);
        watched.Clear();
        if (presetMaterial != null)
            prefab.GetComponent<TMP_Text>().fontSharedMaterial = presetMaterial;
        presetMaterial = null;
        if (lookMaterial != null)
            Destroy(lookMaterial);
        lookMaterial = null;
    }

    void CountCommit(Object changed)
    {
        if (watched.Contains(changed)) commitCount++;
    }

    protected override bool CountsCommits => true;
    protected override void WatchCommits(Component instance) => watched.Add(instance);
    protected override void UnwatchCommits(Component instance) => watched.Remove(instance);

    protected override Component GetInstanceComponent(GameObject go) => go.GetComponent<TMP_Text>();

    protected override void SetText(Component instance, string text) => ((TMP_Text)instance).text = text;
    protected override void SetFontSize(Component instance, float size) => ((TMP_Text)instance).fontSize = size;
    protected override void SetColor(Component instance, Color color) => ((TMP_Text)instance).color = color;
    protected override void SetAutoSize(Component instance, bool enabled) => ((TMP_Text)instance).enableAutoSizing = enabled;
    protected override void SetRichText(Component instance, bool enabled) => ((TMP_Text)instance).richText = enabled;

    protected override string DescribeInspectionInstance(Component instance, int index)
    {
        var text = (TMP_Text)instance;
        return $"{base.DescribeInspectionInstance(instance, index)}, textLen={text.text?.Length ?? 0}, fontSize={text.fontSize:F1}, color={text.color}, wrap={text.textWrappingMode}, autoSize={text.enableAutoSizing}, richText={text.richText}, font={text.font?.name ?? "null"}";
    }

    protected override void SetWordWrap(Component instance, bool enabled) =>
        ((TMP_Text)instance).textWrappingMode = enabled ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("UniText/Run TMP Memory Benchmark")]
    private static void RunFromMenu()
    {
        var test = ObjectUtils.FindAny<TMPBenchmark>();
        if (test != null)
            test.RunBenchmark();
        else
            Debug.LogError("No TMPBenchmark found in scene.");
    }
#endif
}
