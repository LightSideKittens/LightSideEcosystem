using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A recorded editor window: one capture of it per frame on disk, the pointer beats that drove it in the slide's
    /// own coordinates, and per frame the values its edits wrote into the subject.
    /// </summary>
    /// <remarks>
    /// The take is the slide's only source for anything the window did. The card shows the captured frame, the
    /// subject is posed from the recorded values and the pointer replays the recorded beats, so a scrubbed or
    /// re-captured frame shows exactly what the real window showed at that moment without the window being open.
    /// </remarks>
    [Serializable]
    public sealed class WindowTake
    {
        /// <summary>Name of the take's description inside its folder.</summary>
        public const string FileName = "take.json";

        [SerializeField] private float fps = 60f;
        [SerializeField] private int frameCount;
        [SerializeField] private Vector2Int frameSize;
        [SerializeField] private Vector2 start;
        [SerializeField] private RecordedBeat[] beats = Array.Empty<RecordedBeat>();
        [SerializeField] private CursorRegion[] cursors = Array.Empty<CursorRegion>();
        [SerializeField] private string[] channels = Array.Empty<string>();
        [SerializeField] private List<float> values = new();

        private WindowTake() { }

        /// <summary>Starts an empty take for frames of <paramref name="size"/> pixels captured at <paramref name="rate"/>.</summary>
        public WindowTake(float rate, Vector2Int size, Vector2 pointerStart, IReadOnlyList<Beat> pointerBeats,
            CursorRegion[] cursorRegions, string[] channelNames)
        {
            fps = rate;
            frameSize = size;
            start = pointerStart;
            cursors = cursorRegions;
            channels = channelNames;
            beats = new RecordedBeat[pointerBeats.Count];
            for (var i = 0; i < beats.Length; i++) beats[i] = new RecordedBeat(pointerBeats[i]);
        }

        /// <summary>Frames per second the window was stepped and captured at.</summary>
        public float Fps => fps;

        /// <summary>Number of captured frames.</summary>
        public int FrameCount => frameCount;

        /// <summary>Pixel size of every captured frame.</summary>
        public Vector2Int FrameSize => frameSize;

        /// <summary>Where the pointer starts, in the slide's stage coordinates.</summary>
        public Vector2 Start => start;

        /// <summary>The window's areas that wear a cursor other than the arrow, in the slide's stage coordinates.</summary>
        public IReadOnlyList<CursorRegion> Cursors => cursors;

        /// <summary>Names of the subject values recorded per frame, in <see cref="Value"/>'s channel order.</summary>
        public IReadOnlyList<string> Channels => channels;

        /// <summary>The beats that drove the window, in the slide's stage coordinates.</summary>
        public Beat[] Beats()
        {
            var result = new Beat[beats.Length];
            for (var i = 0; i < beats.Length; i++) result[i] = beats[i].ToBeat();
            return result;
        }

        /// <summary>The value of <paramref name="channel"/> after the input of <paramref name="frame"/>.</summary>
        public float Value(int frame, int channel) =>
            values[Mathf.Clamp(frame, 0, frameCount - 1) * channels.Length + channel];

        /// <summary>Appends the next frame's subject values, one per channel.</summary>
        public void Append(float[] frameValues)
        {
            if (frameValues.Length != channels.Length)
                throw new ArgumentException($"A take of {channels.Length} channels was handed {frameValues.Length} values.");
            values.AddRange(frameValues);
            frameCount++;
        }

        /// <summary>Path of the capture of <paramref name="frame"/> inside <paramref name="folder"/>.</summary>
        public static string FramePath(string folder, int frame) => Path.Combine(folder, $"frame_{frame:D5}.png");

        /// <summary>Reads the take in <paramref name="folder"/>, or null when nothing has been recorded there.</summary>
        public static WindowTake Load(string folder)
        {
            var path = Path.Combine(folder, FileName);
            return File.Exists(path) ? JsonUtility.FromJson<WindowTake>(File.ReadAllText(path)) : null;
        }

        /// <summary>Writes the take's description into <paramref name="folder"/>.</summary>
        public void Save(string folder) =>
            File.WriteAllText(Path.Combine(folder, FileName), JsonUtility.ToJson(this, true));

        /// <summary>An area of the recorded window and the cursor the window shows over it.</summary>
        [Serializable]
        public struct CursorRegion
        {
            /// <summary>The area, in the slide's stage coordinates.</summary>
            public Rect Rect;

            /// <summary>The cursor over it.</summary>
            public CursorType Cursor;
        }

        [Serializable]
        private struct RecordedBeat
        {
            [SerializeField] private BeatKind kind;
            [SerializeField] private Vector2 point;
            [SerializeField] private float seconds;
            [SerializeField] private string id;
            [SerializeField] private string label;
            [SerializeField] private float targetWidth;
            [SerializeField] private bool settles;

            public RecordedBeat(Beat beat)
            {
                kind = beat.Kind;
                point = beat.Point;
                seconds = beat.Seconds;
                id = beat.Id;
                label = beat.Label;
                targetWidth = beat.TargetWidth;
                settles = beat.Settles;
            }

            public Beat ToBeat()
            {
                var named = string.IsNullOrEmpty(id) ? null : id;
                return kind switch
                {
                    BeatKind.To => Beat.To(point, named, targetWidth),
                    BeatKind.Wait => Beat.Wait(seconds, named),
                    BeatKind.Click => Beat.Click(named, settles),
                    BeatKind.Drag => Beat.Drag(point, named, targetWidth),
                    _ => Beat.Key(label, named)
                };
            }
        }
    }
}
