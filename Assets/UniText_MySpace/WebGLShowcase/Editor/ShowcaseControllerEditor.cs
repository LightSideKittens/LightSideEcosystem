using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide.Showcase
{
    /// <summary>Opens the scenes of the demo, and in Play Mode their variants, the way the page does.</summary>
    [CustomEditor(typeof(ShowcaseController))]
    internal sealed class ShowcaseControllerEditor : Editor
    {
        private const int ButtonsPerRow = 4;

        public override void OnInspectorGUI()
        {
            var controller = (ShowcaseController)target;
            var scenes = Scenes();
            var open = Array.FindIndex(scenes, scene => scene.gameObject.activeSelf);

            EditorGUILayout.LabelField("Scene", EditorStyles.boldLabel);
            var chosen = GUILayout.SelectionGrid(open, scenes.Select(scene => scene.Id).ToArray(), ButtonsPerRow);
            if (chosen != open)
            {
                if (Application.isPlaying) controller.SetScene(scenes[chosen].Id);
                else Show(scenes, chosen);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Variant", EditorStyles.boldLabel);
                var variants = scenes[chosen].Variants.ToArray();
                var variant = GUILayout.SelectionGrid(-1, variants, ButtonsPerRow);
                if (variant >= 0) controller.SetVariant(variants[variant]);
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        private ShowcaseScene[] Scenes()
        {
            var list = serializedObject.FindProperty("scenes");
            var scenes = new ShowcaseScene[list.arraySize];
            for (var i = 0; i < scenes.Length; i++)
                scenes[i] = (ShowcaseScene)list.GetArrayElementAtIndex(i).objectReferenceValue;
            return scenes;
        }

        private static void Show(ShowcaseScene[] scenes, int shown)
        {
            Undo.RecordObjects(scenes.Select(scene => (Object)scene.gameObject).ToArray(), "Open Showcase Scene");
            for (var i = 0; i < scenes.Length; i++) scenes[i].gameObject.SetActive(i == shown);
        }
    }
}
