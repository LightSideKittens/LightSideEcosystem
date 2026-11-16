using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The neon game: a rhythm game's song select — the song list with each song's best rank, the chosen song's cover,
        /// its details, difficulty and best score, the friends' standings on it, the note speed and the play button. The
        /// pointer picks another song, which takes over the cover and every figure, raises the difficulty to expert and
        /// drags the note speed up.
        /// </summary>
        private sealed class SongSelect : Product
        {
            public SongSelect(Builder k) : base(k)
            {
            }

            private RectTransform highlight;
            private readonly CanvasGroup[] covers = new CanvasGroup[2];
            private CanvasGroup details;
            private UniText title;
            private UniText artist;
            private readonly UniText[] chips = new UniText[3];
            private Segments difficulty;
            private UniText best;
            private UniText rank;
            private UniText lesser;
            private Transform combo;
            private UniText accuracy;
            private readonly UniText[] names = new UniText[4];
            private readonly UniText[] scores = new UniText[4];
            private CanvasGroup board;
            private Slider speed;
            private UniText speedValue;
            private RectTransform cell;
            private readonly UniText[] levels = new UniText[4];

            public override void Build(RectTransform screen)
            {
                Header(screen);
                List(screen);
                for (var i = 0; i < covers.Length; i++) covers[i] = Cover(screen, i == 0 ? Songs[0] : Songs[Picked]);
                Standings(screen);
                Details(screen);
                Modifiers(screen);
                var play = K.Button(screen, "Play", PlayAt, new Vector2(InfoWidth, 104f), L.Accent, 52f);
                play.Caption.rectTransform.anchoredPosition += new Vector2(-30f, 0f);
                var cost = K.Node(play.Node, "Cost", new Vector2(InfoWidth * 0.5f - 86f, 0f));
                K.Fill(cost, "Chip", ShapeKind.Capsule, Vector2.zero, new Vector2(110f, 56f), Hex(0x14001F, 0.85f), 0f);
                Glyphs.Bolt(K, cost, new Vector2(-24f, 0f), 34f, L.Accent.Light);
                K.Figure(cost, "5", 30f, L.Accent.Light, new Vector2(12f, 1f), 50f);
            }

            private void Header(RectTransform screen)
            {
                var logo = K.Heading(screen, "Synthline", 46f, new Vector2(-760f, 398f), 400f);
                Ramp(logo, Hex(0xFFFFFF), Hex(0xFF7FD0));
                K.Tag(screen, new Vector2(-400f, 398f), "Season 3", L.Primary, 18f);
                var tabs = new[] { "Songs", "Events", "Store" };
                for (var i = 0; i < tabs.Length; i++)
                {
                    var x = -120f + i * 170f;
                    K.Figure(screen, tabs[i], 28f, i == 0 ? L.Text : L.TextDim, new Vector2(x, 400f), 170f, HAlign.Center);
                    if (i == 0) K.Toned(screen, "Underline", ShapeKind.RoundedRect, new Vector2(x, 372f), new Vector2(96f, 5f), L.Primary, 0f);
                }

                var energy = K.Pill(screen, new Vector2(456f, 398f), 230f, "45/50");
                Glyphs.Bolt(K, (RectTransform)energy.rectTransform.parent, Builder.PillIcon(230f), 40f, L.Accent.Light);
                K.Avatar(screen, new Vector2(616f, 398f), 64f, "M", L.Primary.Dark, L.Primary.Light);
                K.Figure(screen, "LV 27", 26f, L.Text, new Vector2(660f, 410f), 140f);
                K.Progress(screen, new Vector2(710f, 380f), 100f, 8f, 0.62f, L.Secondary);
            }

            /// <summary>The song list: each song's cover, title, artist and tempo, and its best rank, a lock or a new tag.</summary>
            private void List(RectTransform screen)
            {
                var panel = K.Panel(screen, "Songs", ListAt, ListSize);
                K.Heading(panel.Rect, "Songs", 32f, new Vector2(-ListSize.x * 0.5f + 28f, ListSize.y * 0.5f - 40f), 300f);
                K.Caption(panel.Rect, "6 of 48", 22f, L.TextDim, new Vector2(ListSize.x * 0.5f - 28f, ListSize.y * 0.5f - 40f), 200f, HAlign.Right);
                for (var i = 0; i < Songs.Length; i++)
                {
                    var song = Songs[i];
                    var row = K.Node(screen, song.title, RowAt(i));
                    K.Inset(row, "Plate", ShapeKind.RoundedRect, Vector2.zero, RowSize, L.Radius * 0.8f);
                    var art = K.Ramp(row, "Art", ShapeKind.RoundedRect, new Vector2(-RowSize.x * 0.5f + 52f, 0f), Vector2.one * 70f,
                        Color.Lerp(Hex(song.top), Hex(song.glow), 0.35f), Hex(song.bottom), 10f);
                    Stage.AddStroke(art.Shape, Hex(song.line, 0.8f), 2f, -1f);
                    K.Ramp(art.Rect, "Sun", ShapeKind.Circle, new Vector2(0f, 6f), Vector2.one * 36f, Hex(0xFFF07A), Hex(song.glow), 0f);
                    K.Fill(art.Rect, "Horizon", ShapeKind.RoundedRect, new Vector2(0f, -12f), new Vector2(70f, 22f), Hex(song.bottom), 0f);
                    K.Fill(art.Rect, "Line", ShapeKind.RoundedRect, new Vector2(0f, -2f), new Vector2(70f, 2f), Hex(song.line), 0f);
                    K.Figure(row, song.title, 28f, L.Text, new Vector2(-RowSize.x * 0.5f + 104f, 15f), 300f);
                    K.Caption(row, $"{song.artist} · {song.bpm} BPM", 21f, L.TextDim, new Vector2(-RowSize.x * 0.5f + 104f, -19f), 320f);
                    var right = new Vector2(RowSize.x * 0.5f - 46f, 0f);
                    if (song.rank == "LOCK")
                    {
                        Glyphs.Lock(K, row, right + new Vector2(0f, 10f), 30f, L.TextDim);
                        K.Caption(row, "LV 30", 18f, L.TextDim, right + new Vector2(0f, -22f), 80f, HAlign.Center);
                    }
                    else if (song.rank == "NEW") K.Tag(row, right + new Vector2(-6f, 0f), "New", L.Primary, 18f);
                    else Rank(row, right, song.rank, 58f);
                }

                highlight = K.Fill(screen, "Selected", ShapeKind.RoundedRect, RowAt(0), RowSize + new Vector2(8f, 8f), Theme.Fade(L.Primary.Light, 0.08f),
                    L.Radius * 0.8f + 4f).Rect;
                var ring = highlight.GetComponent<UniShape>();
                Stage.AddStroke(ring, Theme.Fade(L.Primary.Light, 0.25f), 9f, 1f);
                Stage.AddStroke(ring, L.Primary.Light, 3f, -1f);
            }

            /// <summary>A rank letter on a hexagon in its rank's color.</summary>
            private UniText Rank(RectTransform parent, Vector2 at, string letter, float size)
            {
                var tone = RankTone(letter);
                var plate = K.Fill(parent, "Rank", ShapeKind.Hexagon, at, Vector2.one * size, Color.Lerp(Hex(0x0C0718), tone, 0.12f), 0f);
                Stage.AddShadow(plate.Shape, Theme.Fade(tone, 0.6f), Vector2.zero, 12f);
                Stage.AddStroke(plate.Shape, tone, 2.5f, -1f);
                var label = K.Figure(plate.Rect, letter, size * 0.5f, tone, new Vector2(0f, 1f), size, HAlign.Center);
                Glow(label, Theme.Fade(tone, 0.7f), 0.2f);
                return label;
            }

            private Color RankTone(string letter) => letter.StartsWith("S") ? L.Accent.Light : letter == "A" ? L.Secondary.Light : L.Primary.Light;

            /// <summary>A song's cover: a striped sun setting behind neon mountains over a grid, in the song's colors.</summary>
            private CanvasGroup Cover(RectTransform screen, Song song)
            {
                var (node, group) = K.Group(screen, "Cover", CoverAt);
                var frame = K.Fill(node, "Frame", ShapeKind.RoundedRect, Vector2.zero, Vector2.one * (CoverSize + 6f), Hex(song.bottom), 20f);
                Stage.AddStroke(frame.Shape, Hex(song.line), 3f, 1f);
                Stage.AddShadow(frame.Shape, Hex(song.line, 0.5f), Vector2.zero, 24f, 2f);
                var art = K.Ramp(node, "Art", ShapeKind.RoundedRect, Vector2.zero, Vector2.one * CoverSize, Hex(song.top), Hex(song.bottom), 18f);
                art.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = true;
                var a = art.Rect;
                K.Halo(a, new Vector2(0f, 20f), 380f, Hex(song.glow, 0.5f));
                var sun = K.Ramp(a, "Sun", ShapeKind.Circle, new Vector2(0f, 40f), Vector2.one * 210f, Hex(0xFFF07A), Hex(song.glow), 0f);
                for (var i = 0; i < 4; i++)
                    K.Fill(a, "Cut", ShapeKind.RoundedRect, new Vector2(0f, 10f - i * 22f), new Vector2(230f, 4f + i * 3f), Hex(song.bottom), 0f);
                foreach (var (x, w, h) in new[] { (-110f, 220f, 130f), (90f, 260f, 160f) })
                {
                    var ridge = K.Fill(a, "Ridge", ShapeKind.Triangle, new Vector2(x, -40f + h * 0.5f - 60f), new Vector2(w, h), Hex(0x0B0418), 0f);
                    Stage.AddStroke(ridge.Shape, Hex(song.line), 3f, -1f);
                    Stage.AddShadow(ridge.Shape, Hex(song.line, 0.6f), Vector2.zero, 12f);
                }

                K.Fill(a, "Floor", ShapeKind.RoundedRect, new Vector2(0f, -130f), new Vector2(CoverSize, 100f), Hex(0x0B0418), 0f);
                for (var i = 0; i < 4; i++)
                    K.Fill(a, "Grid", ShapeKind.RoundedRect, new Vector2(0f, -92f - i * i * 7f - i * 8f), new Vector2(CoverSize, 2f), Hex(song.line, 0.7f), 0f);
                for (var i = -4; i <= 4; i++)
                {
                    var ray = K.Fill(a, "Ray", ShapeKind.RoundedRect, new Vector2(i * 46f, -130f), new Vector2(2f, 100f), Hex(song.line, 0.5f), 0f);
                    ray.Rect.localRotation = Quaternion.Euler(0f, 0f, -i * 11f);
                }

                return group;
            }

            /// <summary>The friends' standings on the chosen song.</summary>
            private void Standings(RectTransform screen)
            {
                var panel = K.Panel(screen, "Friends", BoardAt, BoardSize);
                K.Heading(panel.Rect, "Friends", 28f, new Vector2(-BoardSize.x * 0.5f + 24f, BoardSize.y * 0.5f - 36f), 300f);
                var (node, group) = K.Group(screen, "Standings", BoardAt);
                board = group;
                for (var i = 0; i < 4; i++)
                {
                    var y = BoardSize.y * 0.5f - 96f - i * 60f;
                    K.Figure(node, (i + 1).ToString(), 26f, i == 0 ? L.Accent.Light : L.TextDim, new Vector2(-BoardSize.x * 0.5f + 34f, y), 40f, HAlign.Center);
                    names[i] = K.Figure(node, "", 26f, L.Text, new Vector2(-BoardSize.x * 0.5f + 70f, y), 160f);
                    scores[i] = K.Figure(node, "", 26f, L.Text, new Vector2(BoardSize.x * 0.5f - 24f, y), 180f, HAlign.Right);
                }
            }

            private void Details(RectTransform screen)
            {
                var (node, group) = K.Group(screen, "Details", Vector2.zero);
                details = group;
                title = K.Heading(node, "", 50f, new Vector2(InfoLeft, 296f), InfoWidth);
                artist = K.Caption(node, "", 28f, L.TextDim, new Vector2(InfoLeft, 246f), InfoWidth);
                var left = InfoLeft;
                for (var i = 0; i < chips.Length; i++)
                {
                    var width = ChipWidths[i];
                    var chip = K.Node(node, "Chip", new Vector2(left + width * 0.5f, 192f));
                    left += width + 12f;
                    var face = K.Fill(chip, "Face", ShapeKind.RoundedRect, Vector2.zero, new Vector2(width, 46f), Theme.Fade(L.Secondary.Light, 0.12f), 10f);
                    Stage.AddStroke(face.Shape, Theme.Fade(L.Secondary.Light, 0.7f), 2f, -1f);
                    chips[i] = K.Caption(chip, "", 21f, L.Secondary.Light, new Vector2(0f, 1f), width - 8f, HAlign.Center);
                }

                difficulty = K.Segmented(screen, new Vector2(InfoCenter, DifficultyY), new[] { "Easy", "Normal", "Hard", "Expert" }, CellWidth, L.Primary, 88f, 21f);
                for (var i = 0; i < 4; i++)
                {
                    var x = Segments.Center(i, 4, CellWidth);
                    difficulty.Captions[i].rectTransform.anchoredPosition = new Vector2(x, 16f);
                    levels[i] = K.Figure(difficulty.Captions[i].rectTransform.parent, Levels[i].ToString(), 30f, LevelTone(i),
                        new Vector2(x, -16f), CellWidth, HAlign.Center);
                }

                cell = K.Node(screen, "Expert", new Vector2(InfoCenter + Segments.Center(3, 4, CellWidth), DifficultyY));
                var score = K.Panel(screen, "Best", new Vector2(InfoCenter, BestY), new Vector2(InfoWidth, 128f), L.Radius);
                var s = score.Rect;
                K.Caption(s, "Best score", 21f, L.TextDim, new Vector2(-InfoWidth * 0.5f + 28f, 34f), 300f);
                best = K.Figure(s, "", 50f, L.Text, new Vector2(-InfoWidth * 0.5f + 26f, -12f), 300f);
                accuracy = K.Caption(s, "", 21f, L.Secondary.Light, new Vector2(InfoWidth * 0.5f - 130f, 26f), 220f, HAlign.Right);
                combo = K.Tag(s, new Vector2(InfoWidth * 0.5f - 210f, -22f), "Full combo", L.Secondary, 18f).rectTransform.parent;
                rank = Rank(s, new Vector2(InfoWidth * 0.5f - 64f, 0f), "S", 92f);
                lesser = Rank(s, new Vector2(InfoWidth * 0.5f - 64f, 0f), "A", 92f);
            }

            private Color LevelTone(int index) => index switch
            {
                0 => Hex(0x5CFF9A),
                1 => L.Secondary.Light,
                2 => L.Primary.Light,
                _ => Hex(0xFF4848)
            };

            private void Modifiers(RectTransform screen)
            {
                var panel = K.Panel(screen, "Modifiers", new Vector2(InfoCenter, ModifiersY), new Vector2(InfoWidth, 150f), L.Radius);
                var p = panel.Rect;
                K.Figure(p, "Note speed", 24f, L.Text, new Vector2(-InfoWidth * 0.5f + 28f, 34f), 260f);
                speed = K.Slider(screen, new Vector2(SpeedX, ModifiersY + 34f), SpeedWidth, L.Secondary);
                speedValue = K.Figure(p, "", 30f, L.Secondary.Light, new Vector2(InfoWidth * 0.5f - 28f, 34f), 90f, HAlign.Right);
                K.Figure(p, "Mirror", 24f, L.Text, new Vector2(-InfoWidth * 0.5f + 28f, -34f), 260f);
                K.Switch(p, new Vector2(-100f, -34f)).Set(0f);
                K.Figure(p, "Hit sounds", 24f, L.Text, new Vector2(0f, -34f), 200f);
                K.Switch(p, new Vector2(InfoWidth * 0.5f - 70f, -34f)).Set(1f);
            }

            public override List<Beat> Script()
            {
                var beats = new List<Beat> { Beat.Wait(0.55f) };
                beats.AddRange(Tap(RowAt(Picked), RowSize.x, "song", 0.5f));
                beats.AddRange(Tap(new Vector2(InfoCenter + Segments.Center(3, 4, CellWidth), DifficultyY), CellWidth, "expert", 0.45f));
                beats.Add(To(Knob(SpeedFrom), KnobSize));
                beats.Add(Drag(Knob(SpeedTo), "speed", KnobSize, 0.15f));
                beats.Add(Beat.Wait(0.8f));
                return beats;
            }

            private Vector2 Knob(float value) => new(SpeedX + speed.KnobX(value), ModifiersY + 34f);

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                var moved = c.After("song", 0.32f);
                highlight.anchoredPosition = Vector2.Lerp(RowAt(0), RowAt(Picked), moved);
                var fade = Ease.Emphasized.Window(t, c.At("song") + 0.05f, 0.4f);
                covers[0].alpha = 1f - fade;
                covers[1].alpha = fade;
                var swap = Mathf.Abs(fade - 0.5f) * 2f;
                details.alpha = swap;
                board.alpha = swap;
                var song = Songs[fade >= 0.5f ? Picked : 0];
                Say(title, L.Cased(song.title));
                Say(artist, L.Cased(song.artist));
                Say(chips[0], $"{song.bpm} BPM");
                Say(chips[1], song.length);
                Say(chips[2], L.Cased(song.genre));
                var friends = fade >= 0.5f ? PickedFriends : FirstFriends;
                for (var i = 0; i < 4; i++)
                {
                    Say(names[i], L.Cased(friends[i].name));
                    Say(scores[i], Grouped(friends[i].score));
                    names[i].color = friends[i].name == "You" ? L.Primary.Light : L.Text;
                }

                var raised = c.After("expert", 0.3f);
                difficulty.Set(2f + raised, L.TextOnTone, L.TextDim);
                for (var i = 0; i < levels.Length; i++)
                    levels[i].color = Color.Lerp(LevelTone(i), L.TextOnTone, Mathf.Clamp01(1f - Mathf.Abs(2f + raised - i)));
                cell.localScale = Vector3.one * (1f - 0.06f * c.Held("expert"));
                var expert = c.Past("expert") && c.Since("expert") > 0.1f;
                var figures = expert ? song.expert : song.hard;
                Say(best, Grouped(figures.score));
                Say(accuracy, L.Cased($"Accuracy {figures.accuracy}"));
                var top = figures.rank.StartsWith("S");
                Show(rank.rectTransform.parent, top);
                Show(lesser.rectTransform.parent, !top);
                Show(combo, top);
                if (top) Say(rank, figures.rank);

                var span = c.Span("speed");
                var dragged = t < span.x ? 0f : Mathf.InverseLerp(Knob(SpeedFrom).x, Knob(SpeedTo).x, c.Pointer(Mathf.Min(t, span.y)).x);
                var value = Mathf.Lerp(SpeedFrom, SpeedTo, Mathf.Clamp01(dragged));
                speed.Set(value);
                Say(speedValue, (SpeedMin + value * (SpeedMax - SpeedMin)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }

            private static Vector2 RowAt(int index) => new(ListAt.x, ListAt.y + ListSize.y * 0.5f - 122f - index * (RowSize.y + 12f));

            /// <summary>A song: its list line, its cover's colors, and its figures at hard and at expert.</summary>
            private readonly struct Song
            {
                public Song(string title, string artist, int bpm, string length, string genre, string rank, uint top, uint bottom, uint glow, uint line,
                    (int, string, string) hard, (int, string, string) expert)
                {
                    this.title = title;
                    this.artist = artist;
                    this.bpm = bpm;
                    this.length = length;
                    this.genre = genre;
                    this.rank = rank;
                    this.top = top;
                    this.bottom = bottom;
                    this.glow = glow;
                    this.line = line;
                    this.hard = (hard.Item1, hard.Item2, hard.Item3);
                    this.expert = (expert.Item1, expert.Item2, expert.Item3);
                }

                public readonly string title;
                public readonly string artist;
                public readonly int bpm;
                public readonly string length;
                public readonly string genre;
                public readonly string rank;
                public readonly uint top;
                public readonly uint bottom;
                public readonly uint glow;
                public readonly uint line;
                public readonly (int score, string rank, string accuracy) hard;
                public readonly (int score, string rank, string accuracy) expert;
            }

            private static readonly Song[] Songs =
            {
                new("Neon Skyline", "Ava Lux", 128, "3:42", "Electro", "S", 0x3A0E5A, 0x12021F, 0xFF3D8B, 0x00F0FF, (982450, "S", "98.6%"), (741120, "A", "91.2%")),
                new("Midnight Drive", "Kairo", 104, "4:05", "Synth", "A", 0x0E2A5A, 0x020A1F, 0x3DA8FF, 0xFF2E97, (911300, "A", "95.1%"), (0, "-", "-")),
                new("Chrome Heart", "Nova Rey", 140, "3:18", "Drum & bass", "SS", 0x0A3A4A, 0x02101A, 0x7A5CFF, 0xFCEE09, (994870, "SS", "99.4%"),
                    (803550, "A", "93.0%")),
                new("Afterglow", "Lumen", 96, "2:57", "Chill", "B", 0x4A1A0E, 0x1F0602, 0xFF8A3D, 0x00F0FF, (842010, "B", "88.7%"), (0, "-", "-")),
                new("Hyperspace", "Vega 9", 174, "3:33", "Hardcore", "LOCK", 0x1A1A3A, 0x05050F, 0x8A8AFF, 0x8A8AFF, (0, "-", "-"), (0, "-", "-")),
                new("Static Love", "Orion", 118, "3:05", "Pop", "NEW", 0x4A0E3A, 0x1F0218, 0xFF5CC8, 0x00F0FF, (0, "-", "-"), (0, "-", "-"))
            };

            private static readonly (string name, int score)[] FirstFriends = { ("Zoe", 991200), ("You", 982450), ("Kai", 977010), ("Milo", 951300) };
            private static readonly (string name, int score)[] PickedFriends = { ("You", 994870), ("Rio", 990020), ("Zoe", 987600), ("Kai", 962440) };
            private static readonly int[] Levels = { 3, 6, 9, 12 };
            private static readonly float[] ChipWidths = { 150f, 110f, 210f };

            private const int Picked = 2;
            private static readonly Vector2 ListAt = new(-486f, -40f);
            private static readonly Vector2 ListSize = new(570f, 760f);
            private static readonly Vector2 RowSize = new(528f, 96f);
            private static readonly Vector2 CoverAt = new(0f, 154f);
            private const float CoverSize = 330f;
            private static readonly Vector2 BoardAt = new(0f, -222f);
            private static readonly Vector2 BoardSize = new(340f, 346f);
            private const float InfoLeft = 210f;
            private const float InfoWidth = 560f;
            private const float InfoCenter = InfoLeft + InfoWidth * 0.5f;
            private const float CellWidth = 138f;
            private const float DifficultyY = 98f;
            private const float BestY = -40f;
            private const float ModifiersY = -194f;
            private const float SpeedX = InfoCenter + 40f;
            private const float SpeedWidth = 240f;
            private const float SpeedFrom = 0.61f;
            private const float SpeedTo = 0.83f;
            private const float SpeedMin = 1f;
            private const float SpeedMax = 10f;
            private static readonly Vector2 PlayAt = new(InfoCenter, -342f);
        }
    }
}
