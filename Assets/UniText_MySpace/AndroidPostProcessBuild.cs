#if UNITY_ANDROID && UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Adds Firebase Test Lab game-loop intent-filter to AndroidManifest.xml
/// </summary>
public class AndroidPostProcessBuild : IPostGenerateGradleAndroidProject
{
    static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        Debug.Log("[AndroidPostProcessBuild] Processing Android build...");
        AddFirebaseGameLoopIntent(path);
    }

    void AddFirebaseGameLoopIntent(string gradlePath)
    {
        string manifestPath = Path.Combine(gradlePath, "src", "main", "AndroidManifest.xml");

        if (!File.Exists(manifestPath))
        {
            Debug.LogError($"[AndroidPostProcessBuild] AndroidManifest.xml not found at: {manifestPath}");
            return;
        }

        var doc = XDocument.Load(manifestPath);
        var application = doc.Root?.Element("application");
        if (application == null)
        {
            Debug.LogError("[AndroidPostProcessBuild] <application> not found in manifest");
            return;
        }

        var mainActivity = application.Elements("activity")
            .FirstOrDefault(a => a.Elements("intent-filter")
                .Any(f => f.Elements("action")
                    .Any(action => (string)action.Attribute(AndroidNs + "name") == "android.intent.action.MAIN")));

        if (mainActivity == null)
        {
            Debug.LogError("[AndroidPostProcessBuild] Main activity not found");
            return;
        }

        bool alreadyExists = mainActivity.Elements("intent-filter")
            .Any(f => f.Elements("action")
                .Any(a => (string)a.Attribute(AndroidNs + "name") == "com.google.intent.action.TEST_LOOP"));

        if (alreadyExists)
        {
            Debug.Log("[AndroidPostProcessBuild] Firebase game-loop intent already exists");
            return;
        }

        var gameLoopFilter = new XElement("intent-filter",
            new XElement("action", new XAttribute(AndroidNs + "name", "com.google.intent.action.TEST_LOOP")),
            new XElement("category", new XAttribute(AndroidNs + "name", "android.intent.category.DEFAULT")),
            new XElement("data", new XAttribute(AndroidNs + "mimeType", "application/javascript"))
        );

        mainActivity.Add(gameLoopFilter);

        AddScenarioLabel(application, "suite_all", "1");
        AddScenarioLabel(application, "suite_text", "2");
        AddScenarioLabel(application, "suite_glyph", "3");
        AddScenarioLabel(application, "suite_motion", "4");
        AddScenarioLabel(application, "suite_scenarios", "5");

        doc.Save(manifestPath);
        Debug.Log("[AndroidPostProcessBuild] Added Firebase game-loop intent-filter and scenario labels");
    }

    /// <summary>Optional Firebase scenario labels (benchmark suite mapping: 1 = all, 2 = text pipeline, 3 = glyph rasterization, 4 = motion, 5 = UniText scenarios) — CI drives suites via --scenario-numbers; the labels document the mapping in the Firebase console.</summary>
    static void AddScenarioLabel(XElement application, string label, string value)
    {
        var name = $"com.google.test.loops.{label}";
        bool exists = application.Elements("meta-data")
            .Any(m => (string)m.Attribute(AndroidNs + "name") == name);
        if (exists) return;
        application.Add(new XElement("meta-data",
            new XAttribute(AndroidNs + "name", name),
            new XAttribute(AndroidNs + "value", value)));
    }
}
#endif
