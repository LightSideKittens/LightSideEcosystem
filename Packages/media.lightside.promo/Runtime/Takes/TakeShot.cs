using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>One value of a take's subject: recorded from it every frame, written back into it on replay.</summary>
    public readonly struct TakeChannel
    {
        public TakeChannel(string name, Func<float> read, Action<float> write, float unrecorded)
        {
            Name = name;
            Read = read;
            Write = write;
            Unrecorded = unrecorded;
        }

        public string Name { get; }

        public Func<float> Read { get; }

        public Action<float> Write { get; }

        /// <summary>The value the subject holds while no take has been recorded.</summary>
        public float Unrecorded { get; }
    }

    /// <summary>Which part of the recorded window a take's card shows, and which of its sections must be open.</summary>
    public readonly struct TakeCrop
    {
        public TakeCrop(string top, float above, string bottom, float below, string[] unfold = null, float left = 0f,
            float right = 0f)
        {
            Top = top;
            Above = above;
            Bottom = bottom;
            Below = below;
            Unfold = unfold ?? Array.Empty<string>();
            Left = left;
            Right = right;
        }

        /// <summary>Label of the row the card's top edge sits above.</summary>
        public string Top { get; }

        /// <summary>Points kept above <see cref="Top"/>.</summary>
        public float Above { get; }

        /// <summary>Label of the row the card's bottom edge sits below.</summary>
        public string Bottom { get; }

        /// <summary>Points kept below <see cref="Bottom"/>.</summary>
        public float Below { get; }

        /// <summary>Labels of the foldouts the recorder opens, in order, before it measures any row.</summary>
        public IReadOnlyList<string> Unfold { get; }

        /// <summary>Points trimmed off the left of the window's viewport.</summary>
        public float Left { get; }

        /// <summary>Points trimmed off the right of the window's viewport.</summary>
        public float Right { get; }
    }

    /// <summary>
    /// A recorded editor window inside a scene: the take on disk, the card that shows it beside its subject, the
    /// pointer that replays it, and everything the recorder needs to drive the live window that makes it.
    /// </summary>
    /// <remarks>
    /// Recording drives a live Inspector inspecting <see cref="Subject"/> and writes a <see cref="WindowTake"/>;
    /// rendering reads only that take, so the scene stays a pure function of time while everything the window shows,
    /// every value the subject takes and every point the pointer reaches comes from the one real edit. The card and
    /// the subject are laid out as one pair; the recorder maps window pixels through the same layout the replay draws.
    /// <para>
    /// Posing only chooses the frame; the frame is decoded and uploaded once, as canvases are about to render. A reel
    /// is posed more often than it is drawn — scrubbing, settling controls between frames — and each upload holds GPU
    /// memory until the editor's frame ends.
    /// </para>
    /// </remarks>
    public sealed class TakeShot : IDisposable
    {
        private readonly TakeBeat[] script;
        private readonly TakeChannel[] channels;
        private readonly string folder;
        private WindowTake take;
        private Texture2D shown;
        private int shownFrame = -1;
        private int wantedFrame;
        private Vector2 subjectSize;
        private float gap;
        private float centerY;
        private float maxHeight;
        private float pairWidth;

        /// <summary>A take recorded into <paramref name="takeFolder"/>, relative to the project, of an Inspector on <paramref name="subject"/>.</summary>
        public TakeShot(string takeFolder, UniShape subject, TakeBeat[] script, TakeChannel[] channels, TakeCrop crop,
            Rect window)
        {
            folder = Path.GetFullPath(takeFolder);
            Subject = subject;
            this.script = script;
            this.channels = channels;
            Crop = crop;
            Window = window;
            take = WindowTake.Load(folder);
        }

        /// <summary>The take's folder, absolute.</summary>
        public string Folder => folder;

        /// <summary>The shape the recorded Inspector edits.</summary>
        public UniShape Subject { get; }

        /// <summary>The beats the recorder drives the window with, aimed at its rows.</summary>
        public IReadOnlyList<TakeBeat> Script => script;

        public TakeCrop Crop { get; }

        /// <summary>Where the recorded Inspector floats while the take records, in screen points.</summary>
        public Rect Window { get; }

        /// <summary>How long the take plays, or <paramref name="unrecorded"/> before one exists.</summary>
        public float Seconds(float unrecorded) => take == null ? unrecorded : take.FrameCount / take.Fps;

        /// <summary>Names of the subject values recorded per frame.</summary>
        public string[] ChannelNames
        {
            get
            {
                var names = new string[channels.Length];
                for (var i = 0; i < names.Length; i++) names[i] = channels[i].Name;
                return names;
            }
        }

        /// <summary>The scene time the take starts playing at, where the subject holds the values the edit starts from.</summary>
        public float Start { get; set; }

        /// <summary>Where the subject rests beside the card, in stage coordinates.</summary>
        public Vector2 SubjectAt
        {
            get
            {
                var at = Arrange(FrameSize, out var extent, out _);
                return new Vector2(at.x + (extent.x + subjectSize.x) * 0.5f + gap, centerY);
            }
        }

        /// <summary>The pixel size the card is laid out for: the take's, or a placeholder before one exists.</summary>
        private Vector2Int FrameSize => take?.FrameSize ?? new Vector2Int(1900, 1000);

        /// <summary>The pointer replaying the take, or null before one exists.</summary>
        public Pointer Pointer { get; private set; }

        /// <summary>
        /// Lays the card out beside a subject of <paramref name="subject"/> size, <paramref name="space"/> apart, the
        /// two centered as a pair on <paramref name="middle"/>, the card as large as <paramref name="height"/> and the
        /// pair's <paramref name="width"/> allow.
        /// </summary>
        /// <remarks>
        /// The window is recorded at a higher editor scale than the card ever shows, so the card is resampled down,
        /// never up, at any size this picks for a 4K capture.
        /// </remarks>
        public void Layout(Vector2 subject, float space, float middle, float height, float width)
        {
            subjectSize = subject;
            gap = space;
            centerY = middle;
            maxHeight = height;
            pairWidth = width;
        }

        /// <summary>
        /// Where the pointer starts for a capture of <paramref name="size"/>, in stage coordinates: in the gap between
        /// the card and the subject, so it travels in with them in plain sight and hovers nothing on the card.
        /// </summary>
        public Vector2 PointerStartFor(Vector2Int size)
        {
            var at = Arrange(size, out var extent, out _);
            return new Vector2(at.x + extent.x * 0.5f + gap * 0.5f, centerY - extent.y * PointerLift);
        }

        /// <summary>The stage point under <paramref name="pixel"/> of a capture <paramref name="size"/> pixels large.</summary>
        /// <remarks><paramref name="pixel"/> is measured from the capture's top-left corner, downwards.</remarks>
        public Vector2 CardPoint(Vector2 pixel, Vector2Int size)
        {
            var at = Arrange(size, out var extent, out var scale);
            var topLeft = at + new Vector2(-extent.x, extent.y) * 0.5f;
            return topLeft + new Vector2(pixel.x, -pixel.y) * scale;
        }

        /// <summary>The capture pixel under <paramref name="point"/>, the inverse of <see cref="CardPoint"/>.</summary>
        public Vector2 CapturePixel(Vector2 point, Vector2Int size)
        {
            var at = Arrange(size, out var extent, out var scale);
            var topLeft = at + new Vector2(-extent.x, extent.y) * 0.5f;
            var offset = (point - topLeft) / scale;
            return new Vector2(offset.x, -offset.y);
        }

        /// <summary>Where a card for a capture of <paramref name="size"/> rests, its extent and its points per pixel.</summary>
        private Vector2 Arrange(Vector2Int size, out Vector2 extent, out float scale)
        {
            scale = Mathf.Min(maxHeight / size.y, (pairWidth - gap - subjectSize.x) / size.x);
            extent = (Vector2)size * scale;
            return new Vector2(-(extent.x + gap + subjectSize.x) * 0.5f + extent.x * 0.5f, centerY);
        }

        /// <summary>Builds the card under <paramref name="parent"/> at its laid-out place, and the pointer if a take exists.</summary>
        /// <remarks>Build it after everything it must draw above, and before anything that must draw above it.</remarks>
        public void Build(Stage stage, Transform parent, float radius, float rimWidth)
        {
            var theme = stage.Theme;
            var cardAt = Arrange(FrameSize, out var extent, out _);
            var card = stage.Node("Take Card", parent);
            stage.Box(card, cardAt, extent);

            var rim = stage.Shape("Rim", card, ShapeKind.RoundedRect, radius + rimWidth);
            stage.Box(rim.Rect, Vector2.zero, extent + Vector2.one * (rimWidth * 2f));
            Stage.Solid(rim.Fill, Theme.Lift(theme.Line, 0.12f));
            Stage.AddShadow(rim.Shape, new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, -14f), 44f);

            var face = stage.Shape("Window", card, ShapeKind.RoundedRect, radius);
            stage.Box(face.Rect, Vector2.zero, extent);

            if (take == null)
            {
                Stage.Solid(face.Fill, theme.Surface);
                return;
            }

            foreach (var region in take.Cursors)
            {
                var area = stage.Node("Cursor Area", card);
                stage.Box(area, region.Rect.center - cardAt, region.Rect.size);
                stage.Cursors.Add(area, region.Cursor);
            }

            shown = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Take Frame", filterMode = FilterMode.Bilinear };
            wantedFrame = 0;
            Upload();
            Canvas.willRenderCanvases += Upload;
            Stage.Textured(face.Fill, shown, PaintFit.Stretch);
        }

        /// <summary>Builds the pointer that replays the take, above everything built so far; null before a take exists.</summary>
        public Pointer BuildPointer(Stage stage) => Pointer = take == null ? null : stage.Pointer(take.Start, take.Beats());

        /// <summary>
        /// Writes into the subject the values the take had <paramref name="played"/> seconds in: its first frame's before
        /// it starts and its last frame's once it has played; the unrecorded values before a take exists.
        /// </summary>
        /// <remarks>
        /// Call it every frame whether the take is on screen or not. Takes that share a subject each own their own
        /// channels, so the subject carries every earlier take's result into the later ones.
        /// </remarks>
        public void Apply(float played)
        {
            var frame = FrameAt(played);
            for (var i = 0; i < channels.Length; i++)
                channels[i].Write(take == null ? channels[i].Unrecorded : take.Value(frame, i));
        }

        /// <summary>Shows the window and the pointer as they were <paramref name="played"/> seconds into the take.</summary>
        public void Pose(float played)
        {
            if (take == null) return;
            wantedFrame = FrameAt(played);
            Pointer.Pose(Mathf.Max(0f, played));
        }

        private int FrameAt(float played) => take == null
            ? 0
            : Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, played) * take.Fps), 0, take.FrameCount - 1);

        /// <summary>Writes the subject's current value of every channel into <paramref name="values"/>.</summary>
        public void ReadChannels(float[] values)
        {
            for (var i = 0; i < channels.Length; i++) values[i] = channels[i].Read();
        }

        /// <summary>How far below the card's middle the pointer starts, as a fraction of the card's height.</summary>
        private const float PointerLift = 0.15f;

        /// <summary>Loads the frame the last pose chose into the card's texture, unless it already shows it.</summary>
        private void Upload()
        {
            if (wantedFrame == shownFrame) return;
            shown.LoadImage(File.ReadAllBytes(WindowTake.FramePath(folder, wantedFrame)));
            shownFrame = wantedFrame;
        }

        public void Dispose()
        {
            Canvas.willRenderCanvases -= Upload;
            if (shown) ObjectUtils.SafeDestroy(shown);
            shown = null;
        }
    }
}
