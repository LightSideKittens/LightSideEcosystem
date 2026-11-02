using UnityEditor;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The capture rig every promo command starts from — a camera, a canvas and an empty reel — and the wiring
    /// helpers a command fills it with, all from code.
    /// </summary>
    /// <remarks>
    /// The canvas deliberately carries no scaler and stays at scale 1. A scaler that follows the Game View would
    /// compose the film for whatever shape the window happens to be and capture it at another; the reel pins its
    /// own rect to its frame instead, and the camera is sized to show exactly that.
    /// </remarks>
    internal static class PromoRig
    {
        internal const int UiLayer = 5;

        /// <summary>
        /// A fresh rig named <paramref name="name"/>, replacing one of that name once the user agrees. Null when
        /// they decline, with nothing touched.
        /// </summary>
        public static Reel Create(string name, string undo, out RectTransform reelRect)
        {
            reelRect = null;

            var existing = GameObject.Find(name);
            if (existing)
            {
                if (!EditorUtility.DisplayDialog("Promo",
                        $"'{existing.name}' already exists. Replace it?", "Replace", "Cancel"))
                    return null;

                Undo.DestroyObjectImmediate(existing);
            }

            var root = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            Undo.RegisterCreatedObjectUndo(root, undo);

            var camera = new GameObject("Promo Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(0f, 0f, -100f);
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << UiLayer;

            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(LightSideRaycaster))
            {
                layer = UiLayer
            };
            canvasObject.transform.SetParent(root.transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100f;

            var reelObject = new GameObject("Reel", typeof(RectTransform), typeof(Reel)) { layer = UiLayer };
            reelRect = (RectTransform)reelObject.transform;
            reelRect.SetParent(canvasObject.transform, false);

            var reel = reelObject.GetComponent<Reel>();
            camera.orthographicSize = reel.FrameSize.y * 0.5f;
            return reel;
        }

        /// <summary>
        /// A slide child, sized to the reel. Named with a leading ordinal because a reel plays its children in
        /// sibling order and the hierarchy is the only place that order is visible.
        /// </summary>
        public static T AddSlide<T>(RectTransform parent, string name, float seconds, SlideTransition enter)
            where T : Slide
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var slide = go.AddComponent<T>();
            slide.Seconds = seconds;
            slide.Enter = enter;
            return slide;
        }

        /// <summary>
        /// The first asset of type <typeparamref name="T"/> named <paramref name="name"/>, or null. Assets under an
        /// <c>Editor</c> folder are skipped unless <paramref name="editorToo"/> allows them.
        /// </summary>
        /// <remarks>
        /// Found by search rather than by path: the shipped assets live inside another package, whose folder layout
        /// is not this package's to depend on.
        /// <para>
        /// Editor copies are skipped by default. Art the text reels use ships in two copies — one for the inspector,
        /// one for consumers — and a scene that referenced the editor copy would resolve to nothing outside the
        /// editor, a failure that surfaces only in a build. A mark that exists nowhere else is a different case: a
        /// reel is captured in the editor, and the editor copy is the only copy there is.
        /// </para>
        /// </remarks>
        public static T FindAsset<T>(string name, bool editorToo = false) where T : Object
        {
            var guids = AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!editorToo && path.Contains("/Editor/")) continue;

                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset && asset.name == name) return asset;
            }

            return null;
        }

        /// <summary>
        /// Assigns <paramref name="value"/> to a serialized field, so a slide's shipped dependencies are wired when
        /// the rig is created rather than hunted for by hand.
        /// </summary>
        public static void Wire(Object target, string field, Object value)
        {
            if (!value)
            {
                Debug.LogWarning($"[Promo] Could not find the asset for '{target.name}.{field}'; assign it by hand.");
                return;
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[Promo] '{target.GetType().Name}' has no serialized field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Builds the reel's content and puts the reel in front of the user.</summary>
        public static void Finish(Reel reel)
        {
            reel.Rebuild();
            Selection.activeGameObject = reel.gameObject;
            EditorGUIUtility.PingObject(reel.gameObject);
        }
    }
}
