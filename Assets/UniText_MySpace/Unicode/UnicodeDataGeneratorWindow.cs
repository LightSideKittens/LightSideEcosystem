#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using LightSide;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

[Serializable]
public class TestData
{
    [SerializeField] private int maxFailuresToLog = 10;
    [SerializeField] private TextAsset unicodeDataAsset;
    [SerializeField] private TextAsset bidiCharacterTestAsset;
    [SerializeField] private TextAsset bidiTestAsset;
    [SerializeField] private TextAsset lineBreakTestAsset;
    [SerializeField] private TextAsset scriptsAsset;
    [SerializeField] private TextAsset scriptAnalyzerTestAsset;
    [SerializeField] private TextAsset graphemeBreakTestAsset;
    [SerializeField] private TextAsset wordBreakTestAsset;

    public void RunBidiCharacterTests()
    {
        if (unicodeDataAsset == null || bidiCharacterTestAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset and bidiCharacterTestAsset.");
            return;
        }

        try
        {
            var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var engine = new BidiEngine(provider);
            var runner = new BidiConformanceRunner(engine);
            var summary = runner.RunBidiCharacterTests(bidiCharacterTestAsset.text, maxFailuresToLog);

            LogSummary("BidiCharacterTest", summary.passedTests, summary.failedTests,
                summary.skippedTests, summary.sampleFailures);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running BidiCharacterTest: {ex}");
        }
    }

    public void RunBidiTests()
    {
        if (unicodeDataAsset == null || bidiTestAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset and bidiTestAsset.");
            return;
        }

        try
        {
            var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var engine = new BidiEngine(provider);
            var runner = new BidiTestRunner(engine);
            var summary = runner.RunTests(bidiTestAsset.text, maxFailuresToLog);

            LogSummary("BidiTest", summary.passedTests, summary.failedTests,
                summary.skippedTests, summary.sampleFailures);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running BidiTest: {ex}");
        }
    }

    public void RunLineBreakTests()
    {
        if (unicodeDataAsset == null || lineBreakTestAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset and lineBreakTestAsset.");
            return;
        }

        try
        {
            var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var runner = new LineBreakConformanceRunner(new LineBreakAlgorithm(provider));
            var summary = runner.RunTests(lineBreakTestAsset.text, maxFailuresToLog);

            LogSummary("LineBreakTest", summary.passedTests, summary.failedTests,
                summary.skippedTests, summary.sampleFailures);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running LineBreakTest: {ex}");
        }
    }

    public void RunScriptTests()
    {
        if (unicodeDataAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset.");
            return;
        }

        if (scriptsAsset == null && scriptAnalyzerTestAsset == null)
        {
            Debug.LogError("Assign scriptsAsset and/or scriptAnalyzerTestAsset.");
            return;
        }

        try
        {
            var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var runner = new ScriptConformanceRunner(provider);

            if (scriptsAsset != null)
            {
                var dataSummary = runner.RunDataTests(scriptsAsset.text, maxFailuresToLog);
                LogSummary("ScriptDataTest", dataSummary.passedTests, dataSummary.failedTests,
                    dataSummary.skippedTests, dataSummary.sampleFailures);
            }

            if (scriptAnalyzerTestAsset != null)
            {
                var analyzer = new ScriptAnalyzer(provider);
                var analyzerSummary = runner.RunAnalyzerTests(analyzer, scriptAnalyzerTestAsset.text, maxFailuresToLog);
                LogSummary("ScriptAnalyzerTest", analyzerSummary.passedTests, analyzerSummary.failedTests,
                    0, analyzerSummary.sampleFailures);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running ScriptTests: {ex}");
        }
    }

    public void RunGraphemeTests()
    {
        if (unicodeDataAsset == null || graphemeBreakTestAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset and graphemeBreakTestAsset.");
            return;
        }

        try
        {
            var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var breaker = new GraphemeBreaker(provider);
            var runner = new GraphemeConformanceRunner(breaker);
            var summary = runner.RunTests(graphemeBreakTestAsset.text, maxFailuresToLog);

            LogSummary("GraphemeBreakTest", summary.passedTests, summary.failedTests,
                summary.skippedTests, summary.sampleFailures);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running GraphemeTests: {ex}");
        }
    }

    public void RunWordBreakTests()
    {
        if (unicodeDataAsset == null || wordBreakTestAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset and wordBreakTestAsset.");
            return;
        }

        try
        {
            using var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
            var runner = new WordBreakConformanceRunner(provider);
            var summary = runner.RunTests(wordBreakTestAsset.text, maxFailuresToLog);

            LogSummary("WordBreakTest", summary.passedTests, summary.failedTests,
                summary.skippedTests, summary.sampleFailures);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Exception while running WordBreakTest: {ex}");
        }
    }

    public void RunBidiProfiling()
    {
        if (unicodeDataAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset first.");
            return;
        }

        var provider = new UnicodeDataProvider(unicodeDataAsset.bytes);
        var engine = new BidiEngine(provider);
        var sb = new StringBuilder();
        sb.AppendLine("=== BidiEngine Performance Profiling ===\n");

        var warmupText = new int[] { 0x0041, 0x05D0, 0x0041 };
        engine.Process(warmupText);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (bidiTestAsset != null)
        {
            sb.AppendLine("--- Test 1: BidiTest.txt (770K calls) - Allocation Check ---");

            var runner = new BidiTestRunner(engine);

            var memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();

            var summary = runner.RunTests(bidiTestAsset.text, 0);

            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);
            var allocated = memAfter - memBefore;

            sb.AppendLine($"  Tests run: {summary.totalTests:N0}");
            sb.AppendLine($"  Time: {sw.ElapsedMilliseconds:N0} ms ({sw.ElapsedMilliseconds * 1000.0 / summary.totalTests:F3} μs/test)");
            sb.AppendLine($"  Memory delta: {allocated:N0} bytes ({allocated / (double)summary.totalTests:F2} bytes/test)");
            sb.AppendLine($"  GC Gen0: {GC.CollectionCount(0)}, Gen1: {GC.CollectionCount(1)}, Gen2: {GC.CollectionCount(2)}");
            sb.AppendLine();
        }

        sb.AppendLine("--- Test 2: Long Synthetic Texts - O(n²) Check ---");
        var lengths = new[] { 100, 500, 1000, 2000, 5000 };

        foreach (var len in lengths)
        {
            var mixedText = new int[len];
            for (var i = 0; i < len; i++)
                mixedText[i] = (i % 2 == 0) ? 0x0041 : 0x05D0;

            engine.Process(mixedText);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var memBefore = GC.GetTotalMemory(true);
            var gcBefore = GC.CollectionCount(0);
            var sw = Stopwatch.StartNew();

            const int iterations = 100;
            for (var iter = 0; iter < iterations; iter++)
                engine.Process(mixedText);

            sw.Stop();
            var memAfter = GC.GetTotalMemory(false);
            var gcAfter = GC.CollectionCount(0);

            var avgMs = sw.ElapsedMilliseconds / (double)iterations;
            var avgPerChar = avgMs * 1000.0 / len;

            sb.AppendLine($"  Length {len,5}: {avgMs,8:F3} ms/call, {avgPerChar,6:F3} μs/char, GC: {gcAfter - gcBefore}, Mem: {(memAfter - memBefore) / iterations:N0} bytes/call");
        }

        sb.AppendLine();
        sb.AppendLine("  (If μs/char grows with length → O(n²) problem)");
        sb.AppendLine();

        sb.AppendLine("--- Test 3: Text with Brackets - Paired Brackets Check ---");
        var bracketLengths = new[] { 100, 500, 1000 };

        foreach (var len in bracketLengths)
        {
            var bracketText = new int[len];
            var half = len / 2;
            for (var i = 0; i < half; i++)
                bracketText[i] = 0x0028;
            for (var i = half; i < len; i++)
                bracketText[i] = 0x0029;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();
            const int iterations = 100;
            for (var iter = 0; iter < iterations; iter++)
                engine.Process(bracketText);
            sw.Stop();

            var avgMs = sw.ElapsedMilliseconds / (double)iterations;
            var avgPerChar = avgMs * 1000.0 / len;

            sb.AppendLine($"  Brackets {len,5}: {avgMs,8:F3} ms/call, {avgPerChar,6:F3} μs/char");
        }

        sb.AppendLine();

        sb.AppendLine("--- Test 4: Deep Isolates - Stack Check ---");
        var isolateDepths = new[] { 10, 50, 100 };

        foreach (var depth in isolateDepths)
        {
            var isolateText = new int[depth * 2 + 1];
            for (var i = 0; i < depth; i++)
                isolateText[i] = 0x2066;
            isolateText[depth] = 0x0041;
            for (var i = 0; i < depth; i++)
                isolateText[depth + 1 + i] = 0x2069;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();
            const int iterations = 1000;
            for (var iter = 0; iter < iterations; iter++)
                engine.Process(isolateText);
            sw.Stop();

            var avgMs = sw.ElapsedMilliseconds / (double)iterations;

            sb.AppendLine($"  Depth {depth,3}: {avgMs,8:F4} ms/call");
        }

        sb.AppendLine();
        sb.AppendLine("=== Profiling Complete ===");

        var result = sb.ToString();
        Debug.Log(result);

        var filePath = Path.Combine(Application.persistentDataPath, "BidiProfilingResults.txt");
        File.WriteAllText(filePath, result);
        Debug.Log($"Results saved to: {filePath}");
    }

    public void RunAllTests()
    {
        if (unicodeDataAsset == null)
        {
            Debug.LogError("Assign unicodeDataAsset first.");
            return;
        }

        Debug.Log("=== Running All Unicode Conformance Tests ===");

        if (bidiCharacterTestAsset != null)
            RunBidiCharacterTests();
        else
            Debug.LogWarning("Skipping BidiCharacter tests: bidiCharacterTestAsset not assigned");

        if (bidiTestAsset != null)
            RunBidiTests();
        else
            Debug.LogWarning("Skipping BidiTest tests: bidiTestAsset not assigned");

        if (lineBreakTestAsset != null)
            RunLineBreakTests();
        else
            Debug.LogWarning("Skipping LineBreak tests: lineBreakTestAsset not assigned");

        if (scriptsAsset != null || scriptAnalyzerTestAsset != null)
            RunScriptTests();
        else
            Debug.LogWarning("Skipping Script tests: scriptsAsset and scriptAnalyzerTestAsset not assigned");

        if (graphemeBreakTestAsset != null)
            RunGraphemeTests();
        else
            Debug.LogWarning("Skipping Grapheme tests: graphemeBreakTestAsset not assigned");

        if (wordBreakTestAsset != null)
            RunWordBreakTests();
        else
            Debug.LogWarning("Skipping WordBreak tests: wordBreakTestAsset not assigned");

        Debug.Log("=== All Tests Complete ===");
    }

    private void LogSummary(string testName, int passed, int failed, int skipped, string sampleFailures)
    {
        var log = $"{testName}: Passed={passed}, Failed={failed}, Skipped={skipped}";

        if (failed > 0 && !string.IsNullOrEmpty(sampleFailures))
        {
            log += $"\nSample Failures:\n{sampleFailures}";
            Debug.LogWarning(log);
        }
        else
        {
            Debug.Log(log);
        }

        var filePath = Path.Combine(Application.persistentDataPath, $"{testName}Results.txt");
        File.WriteAllText(filePath, log);
    }

    public void OnGui()
    {
        maxFailuresToLog = EditorGUILayout.IntField("Max Failures To Log", maxFailuresToLog);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test Data Assets", EditorStyles.miniBoldLabel);

        unicodeDataAsset = (TextAsset)EditorGUILayout.ObjectField(
            "UnicodeData.bytes",
            unicodeDataAsset,
            typeof(TextAsset),
            false);

        bidiCharacterTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "BidiCharacterTest.txt",
            bidiCharacterTestAsset,
            typeof(TextAsset),
            false);

        bidiTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "BidiTest.txt",
            bidiTestAsset,
            typeof(TextAsset),
            false);

        lineBreakTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "LineBreakTest.txt",
            lineBreakTestAsset,
            typeof(TextAsset),
            false);

        scriptsAsset = (TextAsset)EditorGUILayout.ObjectField(
            "Scripts.txt",
            scriptsAsset,
            typeof(TextAsset),
            false);

        scriptAnalyzerTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "ScriptAnalyzerTest.txt",
            scriptAnalyzerTestAsset,
            typeof(TextAsset),
            false);

        graphemeBreakTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "GraphemeBreakTest.txt",
            graphemeBreakTestAsset,
            typeof(TextAsset),
            false);

        wordBreakTestAsset = (TextAsset)EditorGUILayout.ObjectField(
            "WordBreakTest.txt",
            wordBreakTestAsset,
            typeof(TextAsset),
            false);

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Run All Tests"))
        {
            var sw = new Stopwatch();
            sw.Start();
            RunAllTests();
            Debug.Log(sw.ElapsedTicks);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null || bidiCharacterTestAsset == null);
        if (GUILayout.Button("BiDiChar"))
            RunBidiCharacterTests();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null || bidiTestAsset == null);
        if (GUILayout.Button("BiDiTest"))
            RunBidiTests();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null || lineBreakTestAsset == null);
        if (GUILayout.Button("LineBreak"))
            RunLineBreakTests();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null ||
                                     (scriptsAsset == null && scriptAnalyzerTestAsset == null));
        if (GUILayout.Button("Script"))
            RunScriptTests();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null || graphemeBreakTestAsset == null);
        if (GUILayout.Button("Grapheme"))
            RunGraphemeTests();
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null || wordBreakTestAsset == null);
        if (GUILayout.Button("WordBreak"))
            RunWordBreakTests();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(unicodeDataAsset == null);
        if (GUILayout.Button("Profile BiDi"))
            RunBidiProfiling();
        EditorGUI.EndDisabledGroup();
    }
}

public class UnicodeDataGeneratorWindow : EditorWindow
{
    public UnicodeDataGeneratorConfig data;
    
    [MenuItem("UniText/Unicode Data Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<UnicodeDataGeneratorWindow>();
        window.titleContent = new GUIContent("Unicode Data Generator");
        window.minSize = new Vector2(500, 400);
    }

    private void OnGUI()
    {
        data =
            EditorGUILayout.ObjectField("Data Generator", data, typeof(UnicodeDataGeneratorConfig), false) as
                UnicodeDataGeneratorConfig;

        if (data != null) data.OnGUI();
    }
}
#endif
