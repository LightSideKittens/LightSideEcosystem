using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The close of the UniGUI reel: the whole kit as a wall of real controls rising as the camera lands on it, the count
    /// slammed over it, the proof under it, and the wordmark.
    /// </summary>
    /// <remarks>
    /// The wall is every prefab of the kit but those that show nothing until opened, all at one scale, packed edge to
    /// edge across the frame: shelves of columns, each column a tall control with smaller ones stacked under it or a
    /// stack of small ones, narrow ones side by side, every shelf spread to the frame's width. The fields hold text and
    /// the toggles stand on, as a kit in use does. It starts close, its controls rising in as a wave from the middle
    /// outward, and pulls back until the whole kit is in the frame. The wall then sinks under a scrim for the lines, and
    /// the wall and lines are whipped up and away as the wordmark lands.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiFinaleScene))]
    public sealed class GuiFinaleScene : Slide
    {
        [SerializeField] private string count = "63 controls.";
        [SerializeField] private string kitLine = "One kit.";
        [SerializeField] private string proof = "Every pixel in this film is UniGUI.";
        [SerializeField] private string wordmark = "UniGUI";
        [SerializeField] private string site = "lightside.media";

        /// <summary>The kit's palette the scene's own copy starts from.</summary>
        [SerializeField] private UniShapePaints basePalette;

        /// <summary>The kit's control prefabs, the wall's cells in the order given.</summary>
        [SerializeField] private GameObject[] kit = Array.Empty<GameObject>();

        /// <summary>Prefabs the wall leaves out: those that show nothing until opened or fed.</summary>
        [SerializeField] private string[] hidden =
            { "Dialog", "Dialogs", "Toasts", "Radial Menu", "Image", "Glass Panel", "Sheet", "Tutorial", "World Marker" };

        /// <summary>The prefab that sits on the corner of another control rather than in a place of its own.</summary>
        [SerializeField] private string badge = "Badge";

        /// <summary>The control <see cref="badge"/> sits on.</summary>
        [SerializeField] private string badgeHost = "Menu";

        private readonly List<Cell> cells = new();
        private ScenePalette palette;
        private Impacts impacts;
        private RectTransform wall;
        private CanvasGroup wallGroup;
        private RectTransform lines;
        private UniShape scrim;
        private GlyphReveal countReveal;
        private GlyphReveal kitReveal;
        private GlyphReveal proofReveal;
        private GlyphReveal markReveal;
        private UniText siteLabel;
        private float frameHeight;
        private Vector2 wallFrame;
        private bool arranged;

        private sealed class Cell
        {
            public RectTransform Rect;
            public RectTransform Control;
            public CanvasGroup Group;
            public Vector2 At;
            public float Delay;
        }

        protected override void OnBuild(Stage stage)
        {
            if (!basePalette) throw new InvalidOperationException($"[Promo] '{name}' needs its palette assigned.");

            var theme = stage.Theme;
            frameHeight = stage.Height;
            stage.Backdrop(stage.Root);
            palette?.Dispose();
            palette = new ScenePalette(basePalette);
            palette.Warm(theme);

            BuildWall(stage);
            palette.Present();

            var shade = stage.Shape("Scrim", stage.Root, ShapeKind.Ellipse);
            stage.Box(shade.Rect, Vector2.zero, new Vector2(stage.Width * 1.25f, stage.Height * 1.1f));
            Stage.Radial(shade.Fill, Theme.Fade(theme.Background, ScrimStrength), Theme.Fade(theme.Background, 0f));
            scrim = shade.Shape;

            lines = stage.Stretch(stage.Node("Lines", stage.Root));
            countReveal = Slam.Line(stage, lines, count, CountSize, new Vector2(0f, CountSize * 0.62f), false, theme.Text);
            kitReveal = Slam.Line(stage, lines, kitLine, CountSize, new Vector2(0f, -CountSize * 0.62f), true, theme.Text);
            var note = stage.Label(lines, proof, ProofSize, theme.TextSoft, stretch: false);
            note.WordWrap = false;
            stage.Box(note.rectTransform, new Vector2(0f, -CountSize * 1.75f), new Vector2(stage.Width, ProofSize * 1.6f));
            proofReveal = stage.Reveal(note, 0, proof.Length, new SlideRevealHandler { Offset = new Vector2(0f, -ProofRise) },
                ProofSpread);

            var mark = stage.Stretch(stage.Node("Wordmark", stage.Root));
            markReveal = Slam.Line(stage, mark, wordmark, MarkSize, new Vector2(0f, MarkSize * 0.12f), true, theme.Text);
            siteLabel = stage.Label(mark, site, SiteSize, theme.TextSoft, stretch: false);
            stage.Box(siteLabel.rectTransform, new Vector2(0f, -MarkSize * 0.62f), new Vector2(stage.Width, SiteSize * 1.6f));

            impacts = new Impacts(stage, stage.Root);
            impacts.Hit(MarkAt + LandAfter, Vector2.zero, MarkBurst);
            impacts.Shake(MarkAt + LandAfter, Vector2.down, MarkJolt);


            Cue("count", CountAt);
            Cue("kit", KitAt);
            Cue("mark", MarkAt);
            Seconds = End;
        }

        /// <summary>Spawns every shown prefab of the kit onto the wall; the wall is packed once the controls lay out.</summary>
        private void BuildWall(Stage stage)
        {
            cells.Clear();
            arranged = false;
            wall = stage.Stretch(stage.Node("Wall", stage.Root));
            wallGroup = wall.gameObject.AddComponent<CanvasGroup>();
            wallFrame = new Vector2(stage.Width, stage.Height) - Vector2.one * (2f * WallMargin);

            foreach (var prefab in kit)
            {
                if (!prefab || Array.IndexOf(hidden, prefab.name) >= 0) continue;
                var holder = stage.Node(prefab.name, wall);
                var group = holder.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                var control = Kit.Spawn(prefab, holder, palette);
                Kit.Place(control, Vector2.zero);
                cells.Add(new Cell { Rect = holder, Control = (RectTransform)control.transform, Group = group });
            }
        }

        /// <summary>
        /// Puts the wall's controls into use — text in the fields, the toggles on — and packs them across the frame at one
        /// scale by the size each lays out at, staggering their rise by distance from the middle.
        /// </summary>
        private void Arrange()
        {
            arranged = true;
            foreach (var cell in cells) Dress(cell.Control.gameObject);
            UniLayout.LayoutImmediate();

            Cell rider = null;
            Cell host = null;
            var packed = new List<Cell>(cells.Count);
            foreach (var cell in cells)
            {
                if (cell.Control.name == badge) rider = cell;
                else packed.Add(cell);
                if (cell.Control.name == badgeHost) host = cell;
            }
            if (rider != null && host == null)
                throw new InvalidOperationException($"[Promo] The wall shows no '{badgeHost}' for the '{badge}' to sit on.");

            var sizes = new List<Vector2>(packed.Count);
            foreach (var cell in packed) sizes.Add(cell.Control.rect.size);
            var centers = new Vector2[packed.Count];
            var scale = Masonry.Pack(sizes, wallFrame, centers);
            var farthest = (wallFrame * 0.5f).magnitude;
            for (var i = 0; i < packed.Count; i++)
                Seat(packed[i], sizes[i], centers[i], scale, WaveFor * centers[i].magnitude / farthest);

            if (rider == null) return;
            var corner = host.At + (host.Control.rect.size * 0.5f - Vector2.one * BadgeInset) * scale;
            Seat(rider, rider.Control.rect.size, corner, scale, host.Delay);
            rider.Rect.SetAsLastSibling();
        }

        private static void Seat(Cell cell, Vector2 size, Vector2 at, float scale, float delay)
        {
            cell.Rect.sizeDelta = size;
            cell.Rect.localScale = Vector3.one * scale;
            cell.At = at;
            cell.Delay = delay;
        }

        private static void Dress(GameObject control)
        {
            switch (control.name)
            {
                case "Text Field":
                    Type(control, "Mia Chen");
                    break;
                case "Password Field":
                    Type(control, "dragon42");
                    break;
                case "Search Field":
                    Type(control, "raid night");
                    break;
                case "Text Area":
                    Type(control, "Healers wanted for Friday.");
                    break;
                case "Checkbox":
                case "Switch":
                case "Chip":
                    control.GetComponent<UniToggle>().On = true;
                    break;
                case "Calendar":
                    var calendar = control.GetComponent<UniCalendar>();
                    calendar.Selection = DateSelection.Range;
                    calendar.Month = Month;
                    calendar.SetRange(Month.AddDays(RangeFrom - 1), Month.AddDays(RangeTo - 1));
                    break;
                case "Date Range Picker":
                    var range = control.GetComponent<UniDateField>().Calendar;
                    range.Selection = DateSelection.Range;
                    range.SetRange(Month.AddDays(RangeFrom - 1), Month.AddDays(RangeTo - 1));
                    break;
                case "Date Picker":
                    control.GetComponent<UniDateField>().Calendar.Date = Month.AddDays(RangeFrom - 2);
                    break;
            }
        }

        /// <summary>The month the wall's dates fall in, so the wall reads the same on any day it is rendered.</summary>
        private static readonly DateTime Month = new(2026, 12, 1);

        private const int RangeFrom = 14;
        private const int RangeTo = 24;

        private static void Type(GameObject control, string text) =>
            (control.GetComponentInChildren<UniTextEditable>(true)
             ?? throw new InvalidOperationException($"[Promo] '{control.name}' has no editable text.")).SetText(text);

        /// <summary>
        /// Packs controls at one scale into shelves that fill a frame: each shelf a row of columns spread to the frame's
        /// width, each column its tallest control with the smaller ones that fit stacked under it, narrow ones side by side
        /// on a line, and its lines spread down the shelf.
        /// </summary>
        private static class Masonry
        {
            private sealed class Column
            {
                public readonly List<List<int>> Lines = new();
                private readonly List<Vector2> sizes;
                private readonly float height;

                public Column(List<Vector2> sizes, float height, int first)
                {
                    this.sizes = sizes;
                    this.height = height;
                    Lines.Add(new List<int> { first });
                }

                public float Width
                {
                    get
                    {
                        var width = 0f;
                        foreach (var line in Lines) width = Mathf.Max(width, LineWidth(line));
                        return width;
                    }
                }

                public float Used
                {
                    get
                    {
                        var used = -Gap;
                        foreach (var line in Lines) used += LineHeight(line) + Gap;
                        return used;
                    }
                }

                public float LineWidth(List<int> line)
                {
                    var width = -Gap;
                    foreach (var item in line) width += sizes[item].x + Gap;
                    return width;
                }

                public float LineHeight(List<int> line)
                {
                    var tallest = 0f;
                    foreach (var item in line) tallest = Mathf.Max(tallest, sizes[item].y);
                    return tallest;
                }

                /// <summary>Whether <paramref name="item"/> fits, <paramref name="beside"/> the last line's controls or on a line of its own.</summary>
                public bool Fits(int item, out bool beside)
                {
                    var size = sizes[item];
                    var last = Lines[Lines.Count - 1];
                    var lineHeight = LineHeight(last);
                    beside = LineWidth(last) + Gap + size.x <= Width && size.y <= lineHeight * LineSlack &&
                             Used - lineHeight + Mathf.Max(lineHeight, size.y) <= height;
                    return beside || (Used + Gap + size.y <= height && size.x <= Mathf.Max(Width * Widen, Width + WidenBy));
                }

                public void Add(int item, bool beside)
                {
                    if (beside) Lines[Lines.Count - 1].Add(item);
                    else Lines.Add(new List<int> { item });
                }

                public void Remove(bool beside)
                {
                    var last = Lines[Lines.Count - 1];
                    if (beside) last.RemoveAt(last.Count - 1);
                    else Lines.RemoveAt(Lines.Count - 1);
                }
            }

            /// <summary>
            /// Writes each control's center, in frame units from the frame's middle, into <paramref name="centers"/> and
            /// answers the scale every control takes: the largest at which the controls fill some count of shelves.
            /// </summary>
            /// <exception cref="InvalidOperationException">No scale fits the controls into the frame.</exception>
            public static float Pack(List<Vector2> sizes, Vector2 frame, Vector2[] centers)
            {
                var best = 0f;
                var trial = new Vector2[centers.Length];
                for (var shelves = MinShelves; shelves <= MaxShelves; shelves++)
                    for (var scale = MaxScale; scale > best; scale -= ScaleStep)
                    {
                        if (!TryPack(sizes, frame, shelves, scale, trial)) continue;
                        best = scale;
                        Array.Copy(trial, centers, centers.Length);
                        break;
                    }

                if (best <= 0f) throw new InvalidOperationException("[Promo] The kit fits the finale's wall at no scale.");
                return best;
            }

            private static bool TryPack(List<Vector2> sizes, Vector2 frame, int shelves, float scale, Vector2[] centers)
            {
                var native = frame / scale;
                var height = (native.y - Gap * (shelves - 1)) / shelves;
                foreach (var size in sizes)
                    if (size.y > height) return false;

                var columns = Columns(sizes, height);
                var rows = new List<Column>[shelves];
                var widths = new float[shelves];
                for (var r = 0; r < shelves; r++) rows[r] = new List<Column>();
                columns.Sort((a, b) => b.Width.CompareTo(a.Width));
                foreach (var column in columns)
                {
                    var narrowest = 0;
                    for (var r = 1; r < shelves; r++)
                        if (widths[r] < widths[narrowest]) narrowest = r;
                    rows[narrowest].Add(column);
                    widths[narrowest] += column.Width + Gap;
                }

                for (var r = 0; r < shelves; r++)
                    if (widths[r] - Gap > native.x || rows[r].Count == 0) return false;

                for (var r = 0; r < shelves; r++)
                {
                    var row = Interleave(rows[r]);
                    var used = 0f;
                    foreach (var column in row) used += column.Width;
                    var spacing = row.Count > 1 ? (native.x - used) / (row.Count - 1) : 0f;
                    var x = row.Count > 1 ? 0f : (native.x - used) * 0.5f;
                    var top = r * (height + Gap);
                    foreach (var column in row)
                    {
                        Place(column, sizes, x, top, height, native, scale, centers);
                        x += column.Width + spacing;
                    }
                }

                return true;
            }

            private static List<Column> Columns(List<Vector2> sizes, float height)
            {
                var pool = new List<int>();
                for (var i = 0; i < sizes.Count; i++) pool.Add(i);
                pool.Sort((a, b) =>
                {
                    var taller = sizes[b].y.CompareTo(sizes[a].y);
                    if (taller != 0) return taller;
                    var wider = sizes[b].x.CompareTo(sizes[a].x);
                    return wider != 0 ? wider : a.CompareTo(b);
                });

                var columns = new List<Column>();
                while (pool.Count > 0)
                {
                    var column = new Column(sizes, height, pool[0]);
                    pool.RemoveAt(0);
                    while (true)
                    {
                        var best = -1;
                        var bestBeside = false;
                        for (var k = 0; k < pool.Count; k++)
                        {
                            if (!column.Fits(pool[k], out var beside)) continue;
                            var y = sizes[pool[k]].y;
                            if (best >= 0 && (y < sizes[pool[best]].y || (Mathf.Approximately(y, sizes[pool[best]].y) && (!beside || bestBeside))))
                                continue;
                            best = k;
                            bestBeside = beside;
                        }

                        if (best < 0) break;
                        column.Add(pool[best], bestBeside);
                        pool.RemoveAt(best);
                    }

                    columns.Add(column);
                }

                Fold(columns, height);
                return columns;
            }

            /// <summary>Moves the controls of a sparse column into the room the others left, when every one of them finds some.</summary>
            private static void Fold(List<Column> columns, float height)
            {
                for (var c = columns.Count - 1; c >= 0 && columns.Count > 1; c--)
                {
                    var sparse = columns[c];
                    if (sparse.Used > height * SparseFill) continue;
                    var moved = new List<(Column, bool)>();
                    var all = true;
                    foreach (var line in sparse.Lines)
                        foreach (var item in line)
                        {
                            Column into = null;
                            var beside = false;
                            foreach (var other in columns)
                            {
                                if (other == sparse || !other.Fits(item, out var fits)) continue;
                                if (into != null && height - other.Used <= height - into.Used) continue;
                                into = other;
                                beside = fits;
                            }

                            if (into == null)
                            {
                                all = false;
                                break;
                            }

                            into.Add(item, beside);
                            moved.Add((into, beside));
                        }

                    if (all)
                    {
                        columns.RemoveAt(c);
                        continue;
                    }

                    for (var m = moved.Count - 1; m >= 0; m--) moved[m].Item1.Remove(moved[m].Item2);
                }
            }

            /// <summary>The shelf's columns ordered tallest, shortest, next tallest and so on, so tall and stacked columns alternate.</summary>
            private static List<Column> Interleave(List<Column> row)
            {
                var byHead = new List<Column>(row);
                byHead.Sort((a, b) => b.LineHeight(b.Lines[0]).CompareTo(a.LineHeight(a.Lines[0])));
                var ordered = new List<Column>(row.Count);
                for (int head = 0, tail = byHead.Count - 1; head <= tail; head++, tail--)
                {
                    ordered.Add(byHead[head]);
                    if (head != tail) ordered.Add(byHead[tail]);
                }

                return ordered;
            }

            private static void Place(Column column, List<Vector2> sizes, float x, float top, float height, Vector2 native,
                float scale, Vector2[] centers)
            {
                var stacked = 0f;
                foreach (var line in column.Lines) stacked += column.LineHeight(line);
                var lines = column.Lines.Count;
                var spread = lines > 1 ? Mathf.Min((height - stacked) / (lines - 1), Gap * MaxSpread) : 0f;
                var y = top + (height - stacked - spread * (lines - 1)) * 0.5f;
                foreach (var line in column.Lines)
                {
                    var lineHeight = column.LineHeight(line);
                    var left = x + (column.Width - column.LineWidth(line)) * 0.5f;
                    foreach (var item in line)
                    {
                        var size = sizes[item];
                        var middle = new Vector2(left + size.x * 0.5f, y + lineHeight * 0.5f);
                        centers[item] = new Vector2(middle.x - native.x * 0.5f, native.y * 0.5f - middle.y) * scale;
                        left += size.x + Gap;
                    }

                    y += lineHeight + spread;
                }
            }

            /// <summary>The space between controls, in the controls' own units.</summary>
            private const float Gap = 24f;

            /// <summary>How much taller than its line a control may be and still join it.</summary>
            private const float LineSlack = 1.25f;

            /// <summary>How much a control may widen the column it joins: by this share, or by <see cref="WidenBy"/> units.</summary>
            private const float Widen = 1.1f;

            private const float WidenBy = 30f;

            /// <summary>How full a column may be and still give its controls up to the others.</summary>
            private const float SparseFill = 0.55f;

            /// <summary>The most a column's lines are spread apart, in gaps.</summary>
            private const float MaxSpread = 3f;

            private const int MinShelves = 2;
            private const int MaxShelves = 4;
            private const float MaxScale = 1.3f;
            private const float ScaleStep = 0.01f;
        }

        protected override void OnRender(float local)
        {
            if (!arranged) Arrange();
            foreach (var cell in cells)
            {
                var start = WaveAt + cell.Delay;
                var rise = Motion.Swish.Window(local, start, CellRise);
                cell.Group.alpha = Mathf.Clamp01((local - start) / CellFade);
                cell.Rect.anchoredPosition = cell.At + new Vector2(0f, -CellDrop * (1f - rise));
            }

            var settle = Ease.Emphasized.Window(local, 0f, ZoomFor);
            wall.localScale = Vector3.one * Mathf.LerpUnclamped(WallZoom, 1f, settle);
            var sink = Ease.Emphasized.Window(local, ScrimAt, ScrimFor);
            var away = Motion.Swish.Window(local, AwayAt, AwayFor);
            var level = 1f - Mathf.SmoothStep(0f, 1f, local / LevelFor);
            wall.localRotation = Quaternion.Euler(0f, 0f, WallRoll * level);
            wall.anchoredPosition = new Vector2(0f, WallDrift * Mathf.Clamp01(local / AwayAt) + frameHeight * away);
            wallGroup.alpha = Mathf.Lerp(1f, WallDim, sink);
            Stage.Alpha(scrim, sink * (1f - away));

            countReveal.Fill = GlyphReveal.Frontier.Window(local, CountAt, LineSlam);
            kitReveal.Fill = GlyphReveal.Frontier.Window(local, KitAt, LineSlam);
            proofReveal.Fill = GlyphReveal.Frontier.Window(local, ProofAt, ProofFor);
            lines.anchoredPosition = new Vector2(0f, frameHeight * away);

            markReveal.Fill = GlyphReveal.Frontier.Window(local, MarkAt, LineSlam);
            Stage.Alpha(siteLabel, Ease.Emphasized.Window(local, SiteAt, SiteFor));

            Content.anchoredPosition = impacts.Pose(local);
        }

        private void OnDestroy() => palette?.Dispose();

        /// <summary>The space the wall leaves at the frame's edges.</summary>
        private const float WallMargin = 30f;

        /// <summary>How far in from its host's corner the badge's middle sits, in the host's units.</summary>
        private const float BadgeInset = 4f;

        /// <summary>When the wave starts: as the slide arrives, so the camera lands on controls already rising.</summary>
        private const float WaveAt = -0.2f;
        private const float WaveFor = 0.8f;
        private const float CellRise = 0.4f;
        private const float CellFade = 0.18f;
        private const float CellDrop = 70f;
        private const float WallDrift = 40f;

        /// <summary>How much larger the wall starts, and how long it takes to pull back to show the whole kit.</summary>
        private const float WallZoom = 1.8f;

        /// <summary>How far, in degrees, the wall starts rolled, and how long it takes to level as it pulls back: done as the count lands.</summary>
        private const float WallRoll = 9f;

        private const float LevelFor = 1.5f;

        private const float ZoomFor = 1.7f;
        private const float WallDim = 0.4f;
        private const float ScrimAt = 1.4f;
        private const float ScrimFor = 0.45f;
        private const float ScrimStrength = 0.85f;

        private const float CountSize = 120f;
        private const float CountAt = 1.55f;
        private const float KitAt = 1.95f;
        private const float LineSlam = 0.26f;
        private const float LandAfter = LineSlam * 0.7f;

        private const float ProofSize = 40f;
        private const float ProofAt = 2.7f;
        private const float ProofFor = 0.6f;
        private const float ProofRise = 18f;
        private const float ProofSpread = 14f;

        private const float AwayAt = 4.6f;
        private const float AwayFor = 0.35f;

        private const float MarkSize = 230f;
        private const float MarkAt = 4.75f;
        private const float MarkBurst = 560f;
        private const float MarkJolt = 20f;
        private const float SiteSize = 36f;
        private const float SiteAt = 5.3f;
        private const float SiteFor = 0.5f;
        private const float End = 7.1f;
    }
}
