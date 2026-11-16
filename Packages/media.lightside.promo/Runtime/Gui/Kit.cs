using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>The real UniGUI control prefabs a scene shows: found by name, spawned into a scene palette and placed.</summary>
    /// <remarks>
    /// A spawned control initializes only once its scene is enabled, so its state is set when the scene is posed,
    /// never when it is spawned.
    /// </remarks>
    public static class Kit
    {
        /// <summary>The prefab of <paramref name="kit"/> named <paramref name="control"/>.</summary>
        /// <exception cref="InvalidOperationException">The kit holds no such prefab.</exception>
        public static GameObject Find(GameObject[] kit, string control)
        {
            foreach (var prefab in kit)
                if (prefab && prefab.name == control)
                    return prefab;
            throw new InvalidOperationException($"[Promo] The kit has no control named '{control}'.");
        }

        /// <summary>An instance of the control <paramref name="prefab"/> under <paramref name="parent"/>, painted from <paramref name="palette"/>.</summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, ScenePalette palette)
        {
            var control = UnityEngine.Object.Instantiate(prefab, parent, false);
            control.name = prefab.name;
            control.hideFlags = HideFlags.DontSave;
            palette.Adopt(control);
            return control;
        }

        /// <summary>Rewrites every text under <paramref name="root"/> that reads <paramref name="from"/> to read <paramref name="to"/>.</summary>
        public static void Relabel(GameObject root, string from, string to)
        {
            foreach (var text in root.GetComponentsInChildren<UniText>(true))
                if (text.Text == from)
                    text.Text = to;
        }

        /// <summary>
        /// Hangs <paramref name="control"/> at <paramref name="at"/> by the point <paramref name="edge"/> of its width and
        /// <paramref name="rise"/> of its height, so a control that grows keeps that point where it is.
        /// </summary>
        public static void Place(GameObject control, Vector2 at, float edge = 0.5f, float rise = 0.5f)
        {
            var rect = (RectTransform)control.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(edge, rise);
            rect.anchoredPosition = at;
        }
    }
}
