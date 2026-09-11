using System.Collections.Generic;
using System.Text;
using LightSide;
using UnityEngine;

/// <summary>
/// A self-building scene that puts MoveIt through every sink, timing mode and abuse case it is expected to
/// survive. Drop it on an empty GameObject and press Play: it creates its own camera, light and objects, and
/// reports invariant violations on screen instead of relying on the viewer to spot them.
/// </summary>
/// <remarks>
/// Scenarios marked as stress deliberately provoke failures — the Throwing callbacks scenario logs handled
/// exceptions to the console on purpose, and a clean run is one where the on-screen failure count stays zero.
/// </remarks>
[AddComponentMenu(LightSideCore.Menu.Root + "/MoveIt/Samples/" + nameof(MoveItPlayground))]
[DisallowMultipleComponent]
public sealed class MoveItPlayground : MonoBehaviour
{
    [SerializeReference] private MoveItPlaygroundScenario[] scenarios = Defaults();

    private static MoveItPlaygroundScenario[] Defaults() => new MoveItPlaygroundScenario[]
    {
        new ChannelChoirScenario(),
        new EaseGalleryScenario(),
        new CycleModeScenario(),
        new PhysicalDriverScenario(),
        new SequenceTheatreScenario(),
        new NestedSequenceScenario(),
        new EndlessChildrenScenario(),
        new ScrubbingScenario(),
        new ClockAndPhaseScenario(),
        new GroupAndLabelScenario(),
        new PriorityDuelScenario(),
        new CompositionScenario(),
        new SinkGamutScenario(),
        new ComponentGamutScenario(),
        new DestroyUnderFootScenario(),
        new ChannelRefusalScenario(),
        new ReentrancyStormScenario(),
        new SequenceSelfSabotageScenario(),
        new ThrowingCallbackScenario(),
        new ChurnBurstScenario(),
        new DeadHandleScenario(),
        new IllegalOperationScenario(),
        new ReducedMotionScenario(),
    };

    /// <summary>A scene serialized before a scenario existed gains it at the end of its authored list.</summary>
    private static MoveItPlaygroundScenario[] WithMissingDefaults(MoveItPlaygroundScenario[] configured)
    {
        var defaults = Defaults();
        if (configured == null || configured.Length == 0) return defaults;

        var merged = new List<MoveItPlaygroundScenario>(configured.Length + defaults.Length);
        for (var i = 0; i < configured.Length; i++)
            if (configured[i] != null)
                merged.Add(configured[i]);
        foreach (var candidate in defaults)
        {
            var present = false;
            for (var i = 0; i < merged.Count && !present; i++)
                present = merged[i].GetType() == candidate.GetType();
            if (!present) merged.Add(candidate);
        }
        return merged.ToArray();
    }

    [SerializeField] private bool buildEnvironment = true;

    /// <summary>Seconds before the next scenario starts on its own; zero leaves switching to the keyboard.</summary>
    [SerializeField, Min(0f)] private float autoAdvanceSeconds;

    /// <summary>Runs every stress scenario at once, which is the harshest state the engine can be put in.</summary>
    [SerializeField] private bool gauntlet;

    private readonly List<MoveItPlaygroundScenario> running = new();
    private readonly StringBuilder line = new();
    private MoveItStage stage;
    private Transform stageRoot;
    private int index;
    private bool gauntletActive;
    private float frameMilliseconds;
    private int totalFailures;

    private void Awake()
    {
        scenarios = WithMissingDefaults(scenarios);
        stageRoot = new GameObject("MoveIt Playground Stage").transform;
        stageRoot.SetParent(transform, false);
        stage = new MoveItStage(stageRoot);
        if (buildEnvironment) BuildEnvironment();
    }

    private void Start() => Activate(index, gauntlet);

    private void OnDisable() => Deactivate();

    private void Update()
    {
        ReadKeyboard();

        var smoothing = 0.1f;
        frameMilliseconds = Mathf.Lerp(frameMilliseconds, Time.unscaledDeltaTime * 1000f, smoothing);
        stage.Elapsed += Time.unscaledDeltaTime;

        for (var i = 0; i < running.Count; i++)
        {
            var scenario = running[i];
            stage.CheckSurvives(() => scenario.Tick(stage), $"{scenario.Title} ticked without throwing");
        }

        if (autoAdvanceSeconds > 0f && !gauntletActive && stage.Elapsed >= autoAdvanceSeconds)
            Activate((index + 1) % scenarios.Length, false);
    }

    private void ReadKeyboard()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown))
            Activate((index + 1) % scenarios.Length, false);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
            Activate((index - 1 + scenarios.Length) % scenarios.Length, false);
        else if (Input.GetKeyDown(KeyCode.R)) Activate(index, gauntletActive);
        else if (Input.GetKeyDown(KeyCode.G)) Activate(index, !gauntletActive);

        for (var key = 0; key < 9 && key < scenarios.Length; key++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + key))
                Activate(key, false);
        if (scenarios.Length > 9 && Input.GetKeyDown(KeyCode.Alpha0)) Activate(9, false);
    }

    private void Activate(int next, bool asGauntlet)
    {
        Deactivate();

        index = Mathf.Clamp(next, 0, Mathf.Max(0, scenarios.Length - 1));
        gauntletActive = asGauntlet;
        stage.Clear();

        if (gauntletActive)
        {
            for (var i = 0; i < scenarios.Length; i++)
                if (scenarios[i] != null && scenarios[i].IsStress)
                    running.Add(scenarios[i]);
        }
        else if (scenarios.Length > 0 && scenarios[index] != null)
        {
            running.Add(scenarios[index]);
        }

        for (var i = 0; i < running.Count; i++) running[i].Enter(stage);
    }

    private void Deactivate()
    {
        totalFailures += stage?.FailureCount ?? 0;
        for (var i = 0; i < running.Count; i++) running[i].Exit();
        running.Clear();
    }

    private void BuildEnvironment()
    {
        if (Camera.main == null)
        {
            var camera = new GameObject("Playground Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.SetParent(transform, false);
            camera.transform.position = new Vector3(0f, 0.5f, -12f);
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        if (ObjectUtils.FindAny<Light>() != null) return;
        var light = new GameObject("Playground Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.SetParent(transform, false);
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.intensity = 1.1f;
    }

    private void OnGUI()
    {
        const float width = 430f;
        GUILayout.BeginArea(new Rect(10f, 10f, width, Screen.height - 20f), GUI.skin.box);

        GUILayout.Label(gauntletActive
            ? "GAUNTLET — every stress scenario at once"
            : $"[{index + 1}/{scenarios.Length}] {Current?.Title}");

        var watch = gauntletActive ? "Every abuse case running together." : Current?.Watch;
        if (!string.IsNullOrEmpty(watch)) GUILayout.Label(watch);

        GUILayout.Space(6f);
        line.Clear();
        line.Append("live motions ").Append(MoveIt.Count())
            .Append("   objects ").Append(stage.SpawnedCount)
            .Append("   frame ").Append(frameMilliseconds.ToString("0.00")).Append(" ms");
        GUILayout.Label(line.ToString());
        GUILayout.Label($"timeScale {Time.timeScale:0.00}   reduced motion {Accessibility.PrefersReducedMotion}");

        GUILayout.Space(6f);
        var failed = stage.FailureCount > 0;
        var previous = GUI.color;
        GUI.color = failed ? Color.red : Color.green;
        GUILayout.Label(failed
            ? $"INVARIANTS FAILED: {stage.FailureCount} here, {totalFailures + stage.FailureCount} total"
            : $"invariants holding — {totalFailures} failures in earlier scenarios");
        GUI.color = previous;

        if (failed)
        {
            GUI.color = new Color(1f, 0.6f, 0.6f);
            foreach (var failure in stage.Failures) GUILayout.Label(failure);
            GUI.color = previous;
        }

        GUILayout.Space(6f);
        foreach (var entry in stage.Log) GUILayout.Label(entry);

        GUILayout.FlexibleSpace();
        GUILayout.Label("← → switch    1-0 jump    R restart    G gauntlet");
        GUILayout.EndArea();
    }

    private MoveItPlaygroundScenario Current =>
        scenarios != null && index >= 0 && index < scenarios.Length ? scenarios[index] : null;
}
