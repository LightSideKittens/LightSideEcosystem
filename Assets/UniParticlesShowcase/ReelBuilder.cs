using System;
using System.IO;
using LightSide.Promo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LightSide.ParticleShowcase
{
    /// <summary>
    /// Builds the UniParticles reel — its baked words, its scene asset — and captures it frame by frame in a preview scene
    /// of its own, so no open scene is touched.
    /// </summary>
    public static class ReelBuilder
    {
        private const int UiLayer = 5;

        /// <summary>The reel's slides in order: type, seconds, and how each arrives.</summary>
        private static (Type type, float seconds, Func<SlideTransition> enter)[] Plan() => new (Type, float, Func<SlideTransition>)[]
        {
            (typeof(IntroSlide), 5f, () => new Cut()),
            (typeof(RewardSlide), 6f, () => new Push()),
            (typeof(LevelSlide), 6.5f, () => Pushing(Vector2.down)),
            (typeof(BoardSlide), 5f, () => new Cut()),
            (typeof(ShopSlide), 5.5f, () => Pushing(Vector2.right)),
            (typeof(PassSlide), 6f, () => Pushing(Vector2.up)),
            (typeof(ChestSlide), 6.5f, () => Pushing(Vector2.left)),
            (typeof(RecordSlide), 4.5f, () => new CrossFade()),
            (typeof(RibbonSlide), 4.5f, () => new Push()),
            (typeof(CampfireSlide), 6f, () => new Cut()),
            (typeof(LootSlide), 5f, () => Pushing(Vector2.down)),
            (typeof(FireworksSlide), 6f, () => new CrossFade()),
            (typeof(RewindSlide), 6.5f, () => Pushing(Vector2.up)),
            (typeof(ZoomSlide), 4.5f, () => new Cut()),
            (typeof(OutroSlide), 5f, () => new CrossFade()),
        };

        private static SlideTransition Pushing(Vector2 direction)
        {
            var push = new Push();
            var serialized = typeof(Push).GetField("direction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            serialized!.SetValue(push, direction);
            return push;
        }

        /// <summary>Renders the reel's words into sprites under <see cref="ReelArt.Folder"/>.</summary>
        public static string BakeWords()
        {
            if (!AssetDatabase.IsValidFolder(ReelArt.Folder)) AssetDatabase.CreateFolder("Assets/UniParticlesShowcase", "Art");
            var a = Bake("UniParticles", ReelArt.Wordmark, 2600, 620, 360f, 0.07f, new Color(0.1f, 0.05f, 0.2f),
                (u, v) => Color.Lerp(Ramp4(u, new Color(0.35f, 0.88f, 1f), new Color(0.68f, 0.48f, 1f), new Color(1f, 0.42f, 0.72f), new Color(1f, 0.72f, 0.3f)), Color.white, 0.35f * v));
            var b = Bake("NEW RECORD!", ReelArt.Record, 2600, 560, 330f, 0.08f, new Color(0.35f, 0.05f, 0.1f),
                (u, v) => Color.Lerp(new Color(1f, 0.5f, 0.14f), new Color(1f, 0.96f, 0.6f), v));
            return a + "; " + b;
        }

        private static Color Ramp4(float u, Color a, Color b, Color c, Color d)
            => u < 1f / 3f ? Color.Lerp(a, b, u * 3f) : u < 2f / 3f ? Color.Lerp(b, c, u * 3f - 1f) : Color.Lerp(c, d, u * 3f - 2f);

        /// <summary>
        /// Renders <paramref name="text"/> twice in white on black — its fill, then fill and outline — and writes a sprite
        /// whose fill takes <paramref name="paint"/> at each pixel's place (u right, v up, both 0..1 over the text's bounds)
        /// and whose outline takes <paramref name="ink"/>, with the coverage of both as alpha.
        /// </summary>
        private static string Bake(string text, string path, int width, int height, float fontSize, float strokeEm, Color ink, Func<float, float, Color> paint)
        {
            var fill = Render(text, width, height, fontSize, 0f);
            var all = Render(text, width, height, fontSize, strokeEm);
            int minX = width, maxX = 0, minY = height, maxY = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (all[y * width + x].r > 8)
                {
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }

            const int pad = 8;
            minX = Mathf.Max(0, minX - pad);
            minY = Mathf.Max(0, minY - pad);
            maxX = Mathf.Min(width - 1, maxX + pad);
            maxY = Mathf.Min(height - 1, maxY + pad);
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            var output = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y + minY) * width + x + minX;
                var coverage = all[i].r / 255f;
                var inner = fill[i].r / 255f;
                var color = Color.Lerp(ink, paint(x / (float)(w - 1), y / (float)(h - 1)), coverage > 0f ? Mathf.Clamp01(inner / coverage) : 0f);
                color.a = coverage;
                pixels[y * w + x] = color;
            }

            output.SetPixels32(pixels);
            output.Apply();
            File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
            return $"{path} {w}x{h}";
        }

        private static Color32[] Render(string text, int width, int height, float fontSize, float strokeEm)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create();
                var cameraObject = new GameObject("Bake Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                var canvasObject = new GameObject("Bake Canvas", typeof(RectTransform), typeof(Canvas)) { layer = UiLayer };
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                var label = ReelKit.Text(canvasObject.transform, text, Vector2.zero, new Vector2(width, height), fontSize, Color.white);
                if (strokeEm > 0f) ReelKit.Outline(label, Color.white, strokeEm);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Builds the rig — camera, canvas, reel and every slide of the plan — into <paramref name="scene"/>; returns the reel, built.</summary>
        public static Reel Rig(Scene scene, string only = null)
        {
            var root = new GameObject("UniParticles Reel", typeof(RectTransform)) { layer = UiLayer };
            SceneManager.MoveGameObjectToScene(root, scene);
            var camera = new GameObject("Promo Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(0f, 0f, -100f);
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << UiLayer;
            if (EditorSceneManager.IsPreviewScene(scene)) camera.scene = scene;

            var canvasObject = new GameObject("Canvas", typeof(Canvas)) { layer = UiLayer };
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100f;

            var reelObject = new GameObject("Reel", typeof(RectTransform), typeof(Reel)) { layer = UiLayer };
            var reelRect = (RectTransform)reelObject.transform;
            reelRect.SetParent(canvasObject.transform, false);
            var reel = reelObject.GetComponent<Reel>();
            camera.orthographicSize = reel.FrameSize.y * 0.5f;

            var plan = Plan();
            for (var i = 0; i < plan.Length; i++)
            {
                var (type, seconds, enter) = plan[i];
                if (only != null && type.Name != only) continue;
                var go = new GameObject($"{i + 1:00} {type.Name.Replace("Slide", "")}", typeof(RectTransform)) { layer = UiLayer };
                var rect = (RectTransform)go.transform;
                rect.SetParent(reelRect, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var slide = (Slide)go.AddComponent(type);
                slide.Seconds = seconds;
                slide.Enter = only != null ? new Cut() : enter();
            }

            reel.Rebuild();
            return reel;
        }

        /// <summary>Each slide's name and the reel frames it starts and ends on (the next slide's start), one per line.</summary>
        public static string Timeline()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var reel = Rig(scene);
                var slides = reel.GetComponentsInChildren<Slide>(true);
                var lines = new System.Text.StringBuilder();
                for (var i = 0; i < slides.Length; i++)
                {
                    var start = Mathf.RoundToInt(reel.StartOf(slides[i]) * reel.Fps);
                    var end = i + 1 < slides.Length ? Mathf.RoundToInt(reel.StartOf(slides[i + 1]) * reel.Fps) : reel.FrameCount;
                    lines.Append(slides[i].GetType().Name).Append(' ').Append(start).Append(' ').Append(end).Append('\n');
                }

                return lines.ToString();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Writes the reel into its own scene asset at <see cref="ReelArt.Scene"/>, for scrubbing in the editor; the open scenes stay as they are.</summary>
        public static string SaveScene()
        {
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var reel = Rig(scene);
                EditorSceneManager.SaveScene(scene, ReelArt.Scene);
                return $"{ReelArt.Scene}: {reel.FrameCount} frames, {reel.Duration:0.0} s";
            }
            finally
            {
                if (active.IsValid()) SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Builds the reel in a preview scene and writes frames <paramref name="from"/>..<paramref name="to"/> (inclusive,
        /// every <paramref name="every"/>th; to below 0 for the last) as <c>f_00000.png</c>… into <paramref name="folder"/>, numbered
        /// from 0 in the order written, at <paramref name="width"/> pixels wide; with <paramref name="only"/>, of a reel holding only
        /// the slide of that type name. Frames already on disk keep their numbers: a capture in pieces writes them at
        /// <paramref name="from"/> / <paramref name="every"/> on.
        /// </summary>
        public static string Capture(string folder, int from, int to, int every, int width, string only = null)
        {
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.NewPreviewScene();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            RenderTexture target = null;
            Texture2D pixels = null;
            Camera eye = null;
            var previous = RenderTexture.active;
            try
            {
                var reel = Rig(scene, only);
                var canvas = reel.GetComponentInParent<Canvas>();
                var camera = eye = canvas.worldCamera;
                var height = Mathf.RoundToInt(width * reel.FrameSize.y / (float)reel.FrameSize.x);
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                camera.targetTexture = target;
                canvas.scaleFactor = width / (float)reel.FrameSize.x;
                reel.Playing = false;
                var last = to < 0 ? reel.FrameCount - 1 : Mathf.Min(to, reel.FrameCount - 1);
                var written = 0;
                var number = from / Mathf.Max(1, every);
                for (var frame = from; frame <= last; frame += Mathf.Max(1, every))
                {
                    reel.Frame = frame;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    pixels.Apply(false);
                    File.WriteAllBytes(Path.Combine(folder, $"f_{number + written:00000}.png"), pixels.EncodeToPNG());
                    written++;
                }

                return $"{written} frames of {reel.FrameCount} ({reel.Duration:0.00} s) at {width}x{height} in {watch.Elapsed.TotalSeconds:0.0} s";
            }
            finally
            {
                RenderTexture.active = previous;
                if (eye != null) eye.targetTexture = null;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
