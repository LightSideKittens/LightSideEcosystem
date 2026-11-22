using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LightSide;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Holds the house inspectors to the fields their targets inherit: once per inspected type shown in an inspector, the
/// visible serialized fields declared by a LightSide base type above the type a <see cref="FullWidthEditor"/> is
/// declared for, which no shown row of that inspector draws and <see cref="omitted"/> does not name, are reported by name.
/// </summary>
/// <remarks>
/// Shown rows are read from Core's registry of serialized rows on a panel (<c>AnimatedPropertyVisual.attached</c>); the
/// check runs on the first tick after their count changed and then held.
/// </remarks>
[InitializeOnLoad]
internal static class InspectorCoverage
{
    private static readonly Dictionary<string, string[]> omitted = new()
    {
        ["UniParticlesLayer"] = new[] { "material", "raycastTarget", "raycastPadding" },
        ["UniLottie"] = new[] { "material" },
        ["UniShapeWorld"] = new[] { "material" },
    };

    private static readonly IEnumerable shownRows;
    private static readonly Func<int> shownCount;
    private static readonly FieldInfo rowBinding;
    private static readonly HashSet<Type> checkedTypes = new();
    private static Dictionary<Type, bool> houseEditors;
    private static int lastCount;
    private static bool changed;

    static InspectorCoverage()
    {
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var registry = typeof(FullWidthEditor).Assembly.GetType("LightSide.AnimatedPropertyVisual", true);
        var rows = registry.GetField("attached", hidden)?.GetValue(null) ??
                   throw new InvalidOperationException("AnimatedPropertyVisual no longer keeps its shown rows in 'attached'.");
        shownRows = (IEnumerable)rows;
        shownCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), rows, rows.GetType().GetProperty("Count")!.GetGetMethod());
        rowBinding = registry.GetNestedType("Tracker", hidden)?.GetField("binding", hidden) ??
                     throw new InvalidOperationException("AnimatedPropertyVisual's row tracker no longer holds its 'binding'.");
        EditorApplication.update += Update;
    }

    private static void Update()
    {
        var count = shownCount();
        if (count != lastCount)
        {
            lastCount = count;
            changed = true;
        }
        else if (changed)
        {
            changed = false;
            Check();
        }
    }

    private static void Check()
    {
        var drawn = new Dictionary<Object, HashSet<string>>();
        foreach (var row in shownRows)
        {
            var binding = (SerializedPropertyBinding)rowBinding.GetValue(row);
            var target = binding.SerializedObject.targetObject;
            if (target == null || checkedTypes.Contains(target.GetType())) continue;
            if (!drawn.TryGetValue(target, out var names)) drawn[target] = names = new HashSet<string>();
            var path = binding.PropertyPath;
            var dot = path.IndexOf('.');
            names.Add(dot < 0 ? path : path.Substring(0, dot));
        }

        foreach (var (target, names) in drawn)
        {
            var type = target.GetType();
            if (!checkedTypes.Add(type) || EditorTypeOf(type) is not { } declared) continue;
            var left = omitted.TryGetValue(type.Name, out var fields) ? fields : Array.Empty<string>();
            var missing = new List<string>();
            using var serializedObject = new SerializedObject(target);
            var property = serializedObject.GetIterator();
            for (var enter = true; property.NextVisible(enter); enter = false)
            {
                if (names.Contains(property.name) || Array.IndexOf(left, property.name) >= 0) continue;
                var owner = SerializedReflection.FindField(type, property.name)?.DeclaringType;
                if (owner != null && owner != declared && declared.IsSubclassOf(owner) &&
                    owner.Assembly.GetName().Name.StartsWith("LightSide.", StringComparison.Ordinal))
                    missing.Add($"{property.name} ({owner.Name})");
            }
            if (missing.Count > 0)
                Debug.LogWarning($"The {type.Name} inspector draws no row for the inherited field(s) " +
                                 $"{string.Join(", ", missing)}. Draw them in it, mark them [HideInInspector] when they " +
                                 "are not for designers, or name them in InspectorCoverage.omitted when it leaves them " +
                                 "out by design.", target);
        }
    }

    /// <summary>Finds the type the house editor of <paramref name="type"/> is declared for; null when no house editor inspects it.</summary>
    private static Type EditorTypeOf(Type type)
    {
        houseEditors ??= HouseEditors();
        for (var candidate = type; candidate != null && candidate != typeof(Object); candidate = candidate.BaseType)
            if (houseEditors.TryGetValue(candidate, out var forChildren) && (candidate == type || forChildren))
                return candidate;
        return null;
    }

    private static Dictionary<Type, bool> HouseEditors()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var inspected = typeof(CustomEditor).GetField("m_InspectedType", instance) ??
                        throw new InvalidOperationException("CustomEditor no longer names its inspected type.");
        var children = typeof(CustomEditor).GetField("m_EditorForChildClasses", instance) ??
                       throw new InvalidOperationException("CustomEditor no longer names its child-class flag.");
        var map = new Dictionary<Type, bool>();
        foreach (var editor in TypeCache.GetTypesWithAttribute<CustomEditor>())
        {
            if (editor.IsAbstract || !typeof(FullWidthEditor).IsAssignableFrom(editor)) continue;
            foreach (CustomEditor attribute in editor.GetCustomAttributes(typeof(CustomEditor), false))
                if (inspected.GetValue(attribute) is Type type)
                    map[type] = (bool)children.GetValue(attribute) || (map.TryGetValue(type, out var known) && known);
        }
        return map;
    }
}
