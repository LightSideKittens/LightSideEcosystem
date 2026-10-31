using System;
using System.Collections.Generic;
using LightSide;
using UnityEngine;

/// <summary>
/// The world one scenario plays in: it hands out objects, collects them again when the scenario ends, and
/// records both narration and invariant violations for the heads-up display. Everything a scenario spawns
/// through the stage is destroyed on teardown, so a scenario that leaks a motion is visible as a live count
/// that never returns to zero.
/// </summary>
public sealed class MoveItStage
{
    private const int logCapacity = 14;
    private const int failureCapacity = 12;

    private readonly List<GameObject> spawned = new();
    private readonly Queue<string> log = new();
    private readonly List<string> failures = new();
    private readonly MaterialPropertyBlock block = new();
    private readonly Transform root;

    internal MoveItStage(Transform root) => this.root = root;

    /// <summary>How many invariant checks this scenario has failed since it started.</summary>
    public int FailureCount { get; private set; }

    /// <summary>Objects currently alive on the stage.</summary>
    public int SpawnedCount => spawned.Count;

    internal IReadOnlyList<string> Failures => failures;

    internal IEnumerable<string> Log => log;

    /// <summary>Seconds the current scenario has been running.</summary>
    public float Elapsed { get; internal set; }

    /// <summary>A primitive parented to the stage, colored through a property block so no material leaks.</summary>
    public Transform Spawn(PrimitiveType shape, Vector3 position, Color color, string name = null)
    {
        var instance = GameObject.CreatePrimitive(shape);
        instance.name = name ?? shape.ToString();
        ObjectUtils.SafeDestroy(instance.GetComponent<Collider>());

        var transform = instance.transform;
        transform.SetParent(root, false);
        transform.position = position;
        Tint(instance.GetComponent<Renderer>(), color);
        spawned.Add(instance);
        return transform;
    }

    /// <summary>
    /// A primitive colored by hue alone, which is what a row or grid of demonstrator objects wants: the
    /// saturation and value are fixed so neighboring items differ only in the way the eye reads fastest.
    /// </summary>
    public Transform Spawn(PrimitiveType shape, Vector3 position, float hue, string name = null) =>
        Spawn(shape, position, Color.HSVToRGB(Mathf.Repeat(hue, 1f), 0.7f, 1f), name);

    /// <summary>A floating caption parented to the stage, for naming what an object or a row shows.</summary>
    public Transform Label(Vector3 position, string text, float height = 0.24f,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var instance = new GameObject($"Label {text}");
        var mesh = instance.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.fontSize = 64;
        mesh.characterSize = height * 10f / 64f;
        mesh.anchor = anchor;
        mesh.alignment = anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Center;
        mesh.color = new Color(1f, 1f, 1f, 0.8f);
        instance.GetComponent<MeshRenderer>().sharedMaterial = mesh.font.material;

        var transform = instance.transform;
        transform.SetParent(root, false);
        transform.position = position;
        spawned.Add(instance);
        return transform;
    }

    /// <summary>Removes one object early, which is how a scenario pulls a target out from under a live motion.</summary>
    public void Despawn(Transform target, bool immediate = false)
    {
        if (target == null) return;
        var instance = target.gameObject;
        spawned.Remove(instance);
        if (immediate) UnityEngine.Object.DestroyImmediate(instance);
        else ObjectUtils.SafeDestroy(instance);
    }

    public void Tint(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        renderer.GetPropertyBlock(block);
        block.SetColor(renderer.ColorPropertyId(), color);
        renderer.SetPropertyBlock(block);
    }

    /// <summary>Adds one line to the narration strip.</summary>
    public void Say(string message)
    {
        log.Enqueue($"{Elapsed,6:0.00}  {message}");
        while (log.Count > logCapacity) log.Dequeue();
    }

    /// <summary>
    /// Asserts an invariant the engine is expected to hold. A failure is counted and shown rather than thrown,
    /// so one broken guarantee does not hide the rest of the run.
    /// </summary>
    public void Check(bool condition, string invariant)
    {
        if (condition) return;
        FailureCount++;
        if (failures.Count < failureCapacity) failures.Add($"{Elapsed,6:0.00}  {invariant}");
        Debug.LogError($"[MoveIt playground] invariant failed: {invariant}");
    }

    /// <summary>Runs work that is expected to throw, and checks that it threw the expected type.</summary>
    public void CheckThrows<TException>(Action work, string what) where TException : Exception
    {
        try
        {
            work();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception unexpected)
        {
            Check(false, $"{what} threw {unexpected.GetType().Name}, expected {typeof(TException).Name}");
            return;
        }
        Check(false, $"{what} was accepted, expected {typeof(TException).Name}");
    }

    /// <summary>
    /// Runs work whose outcome is observed rather than required. Used where the engine legitimately refuses —
    /// a refusal is reported to the narration, never counted as a broken invariant.
    /// </summary>
    public void Probe(Action work, string what)
    {
        try
        {
            work();
        }
        catch (Exception exception)
        {
            Say($"{what} refused with {exception.GetType().Name}");
        }
    }

    /// <summary>Runs work that must not throw whatever state it is handed.</summary>
    public void CheckSurvives(Action work, string what)
    {
        try
        {
            work();
        }
        catch (Exception exception)
        {
            Check(false, $"{what} threw {exception.GetType().Name}: {exception.Message}");
        }
    }

    internal void Clear()
    {
        MoveIt.StopAll();
        for (var i = 0; i < spawned.Count; i++) ObjectUtils.SafeDestroy(spawned[i]);
        spawned.Clear();
        log.Clear();
        failures.Clear();
        FailureCount = 0;
        Elapsed = 0f;
    }
}
