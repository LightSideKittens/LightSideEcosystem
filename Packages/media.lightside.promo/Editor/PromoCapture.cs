using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Promo
{
    /// <summary>
    /// Renders a reel to disk: every frame as a PNG sequence, or a sample of frames tiled into one contact sheet.
    /// </summary>
    /// <remarks>
    /// Deliberately does not use Unity Recorder. <see cref="Reel.Seek"/> is a pure function of time, so stepping the
    /// playhead and reading pixels is deterministic, needs no package dependency, and works in edit mode where
    /// Recorder's play-mode assumptions do not. Encoding to a video is ffmpeg's job, outside Unity.
    /// <para>
    /// The contact sheet is the point. A reviewer — human or otherwise — who cannot watch a video can still read one
    /// tiled still, and every shot in this repository that shipped unreviewed shipped broken.
    /// </para>
    /// <para>
    /// Row 0 of a <see cref="Texture2D"/> is its bottom row, so the sheet's first tile is written to its last row
    /// and the grid still reads top-left to bottom-right.
    /// </para>
    /// </remarks>
    internal static class PromoCapture
    {
        private const int SheetColumns = 4;
        private const int SheetTiles = 12;
        private const int SheetTileWidth = 480;
        private const string OutputRoot = "Recordings/Promo";

        private static RenderTexture target;

        static PromoCapture()
        {
            EditorLifecycle.UnmanagedCleaning += Release;
        }

        [MenuItem(PromoMenu.Tools.CaptureFrames, false, 30)]
        private static void CaptureFrames()
        {
            var reel = PromoSceneCommands.FindReel();
            if (!Prepare(reel, out var camera, out var size, out var directory)) return;

            var count = reel.FrameCount;
            var written = 0;
            try
            {
                for (var frame = 0; frame < count; frame++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Promo", $"Frame {frame + 1} / {count}",
                            (frame + 1) / (float)count))
                        break;

                    var texture = Grab(reel, camera, frame, size.x, size.y);
                    File.WriteAllBytes(Path.Combine(directory, $"frame_{frame:D5}.png"), texture.EncodeToPNG());
                    ObjectUtils.SafeDestroy(texture);
                    written++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Release();
            }

            WriteCueSheet(reel, directory);
            Debug.Log($"Promo: {written} of {count} frames at {size.x}x{size.y} -> {directory}");
        }

        [MenuItem(PromoMenu.Tools.ContactSheet, false, 31)]
        private static void ContactSheet()
        {
            var reel = PromoSceneCommands.FindReel();
            if (!Prepare(reel, out var camera, out var size, out var directory)) return;

            var last = Mathf.Max(0, reel.FrameCount - 1);
            var rows = Mathf.CeilToInt(SheetTiles / (float)SheetColumns);
            var tileHeight = Mathf.RoundToInt(SheetTileWidth * size.y / (float)size.x);
            var sheet = new Texture2D(SheetTileWidth * SheetColumns, tileHeight * rows, TextureFormat.RGB24, false);

            try
            {
                for (var i = 0; i < SheetTiles; i++)
                {
                    var frame = Mathf.RoundToInt(i * last / (float)(SheetTiles - 1));
                    var shot = Grab(reel, camera, frame, SheetTileWidth, tileHeight);
                    var x = i % SheetColumns * SheetTileWidth;
                    var y = (rows - 1 - i / SheetColumns) * tileHeight;
                    sheet.SetPixels(x, y, SheetTileWidth, tileHeight, shot.GetPixels());
                    ObjectUtils.SafeDestroy(shot);
                }

                sheet.Apply(false);
                var path = Path.Combine(directory, "contact.png");
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                Debug.Log($"Promo: contact sheet -> {path}");
            }
            finally
            {
                ObjectUtils.SafeDestroy(sheet);
                Release();
            }
        }

        /// <summary>
        /// Writes the reel's cues beside the frames, as <c>seconds,frame,name</c>.
        /// </summary>
        /// <remarks>
        /// The audio track is laid against this outside Unity. A frame-stepped capture runs faster or slower than a
        /// clock by definition, so there is no sound to record while it runs.
        /// </remarks>
        private static void WriteCueSheet(Reel reel, string directory)
        {
            var cues = reel.CueSheet();
            if (cues.Count == 0) return;

            var text = new StringBuilder("seconds,frame,name\n");
            for (var i = 0; i < cues.Count; i++)
                text.Append(cues[i].At.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(Mathf.RoundToInt(cues[i].At * reel.Fps))
                    .Append(',')
                    .Append(cues[i].Name)
                    .Append('\n');

            File.WriteAllText(Path.Combine(directory, "cues.csv"), text.ToString());
        }

        private static bool Prepare(Reel reel, out Camera camera, out Vector2Int size, out string directory)
        {
            camera = null;
            size = default;
            directory = null;

            if (!reel)
            {
                Debug.LogWarning("No Reel in the open scene. Run " + PromoMenu.Tools.CreateReel + " first.");
                return false;
            }

            var canvas = reel.GetComponentInParent<Canvas>();
            camera = canvas ? canvas.worldCamera : null;
            if (!camera)
            {
                Debug.LogWarning("The reel's canvas has no camera; there is nothing to render from.");
                return false;
            }

            size = reel.FrameSize;
            camera.orthographic = true;
            camera.orthographicSize = size.y * 0.5f;

            reel.Playing = false;
            reel.Rebuild();

            directory = Path.Combine(Directory.GetCurrentDirectory(), OutputRoot);
            Directory.CreateDirectory(directory);
            return true;
        }

        /// <summary>
        /// Renders one frame into the capture target and reads it back.
        /// </summary>
        /// <remarks>
        /// A Screen Space - Camera canvas takes its rect and its scale factor from the camera's current render
        /// target, so the target is bound before the canvas update, never after it. Updating first lays the frame
        /// out for the Game View and then photographs it at a different size.
        /// </remarks>
        private static Texture2D Grab(Reel reel, Camera camera, int frame, int width, int height)
        {
            if (target == null || target.width != width || target.height != height)
            {
                Release();
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "PromoCapture" };
            }

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            camera.targetTexture = target;
            reel.Frame = frame;
            Canvas.ForceUpdateCanvases();

            camera.Render();
            RenderTexture.active = target;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            texture.Apply(false);

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            return texture;
        }

        private static void Release()
        {
            if (target == null) return;
            target.Release();
            ObjectUtils.SafeDestroy(target);
            target = null;
        }
    }
}
