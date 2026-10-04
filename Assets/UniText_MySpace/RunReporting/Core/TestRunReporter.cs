using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using LightSide.Benchmark;

/// <summary>
/// Delivers a finished in-player test run to CI: writes the JUnit XML to persistentDataPath,
/// routes it through the platform channel (Firebase game-loop on Android/iOS, JS bridge on WebGL)
/// and exits batch-mode players with the pass/fail code. Shared by every runner that produces a
/// <see cref="TestResultCollection"/>.
/// </summary>
public static class TestRunReporter
{
    public static void Report(TestResultCollection results, string logTag)
    {
        var xml = results.ToJUnitXml();

        var xmlPath = Path.Combine(Application.persistentDataPath, "testResults.xml");
        File.WriteAllText(xmlPath, xml);
        Debug.Log($"{logTag} Results saved to: {xmlPath}");

        Console.WriteLine($"TEST_RESULTS_PATH={xmlPath}");

#if UNITY_IOS && !UNITY_EDITOR
        FirebaseTestLabiOS.WriteResults("testResults.xml", xml);

        FirebaseTestLabiOS.NotifyTestComplete();

        System.Threading.Thread.Sleep(500);
#elif UNITY_ANDROID && !UNITY_EDITOR
        FirebaseTestLabAndroid.WriteResultsArchive(BuildResultsArchive(xml), "results.zip");

        FirebaseTestLabAndroid.NotifyTestComplete();
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        ReportTestResults(JsonUtility.ToJson(new WebGLTestResults
        {
            xml = xml,
            allPassed = results.AllPassed,
            total = results.Total,
            passed = results.Passed,
            failed = results.Failed
        }));
#else
        if (Application.isBatchMode)
        {
            Application.Quit(results.AllPassed ? 0 : 1);
        }
#endif
    }

    /// <summary>Android game-loop collects a single output file, so the XML and every screenshot ship in one zip.</summary>
    static byte[] BuildResultsArchive(string xml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("testResults.xml").Open()))
                w.Write(xml);

            var dir = Path.Combine(Application.persistentDataPath, "Screenshots");
            if (Directory.Exists(dir))
                foreach (var png in Directory.GetFiles(dir, "*.png"))
                {
                    using var es = zip.CreateEntry("screenshots/" + Path.GetFileName(png)).Open();
                    using var fs = File.OpenRead(png);
                    fs.CopyTo(es);
                }
        }
        return ms.ToArray();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void ReportTestResults(string json);

    [Serializable]
    private class WebGLTestResults
    {
        public string xml;
        public bool allPassed;
        public int total;
        public int passed;
        public int failed;
    }
#endif
}
