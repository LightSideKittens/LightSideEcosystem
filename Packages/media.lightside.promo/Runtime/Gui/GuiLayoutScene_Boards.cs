using System.Collections.Generic;
using UnityEngine;
using G = LightSide.Promo.GuiStylesScene.Glyphs;

namespace LightSide.Promo
{
    public sealed partial class GuiLayoutScene
    {
        /// <summary>
        /// A party of heroes in a wrapping row under the kit's layout motion: sorted by class, then by level and by name
        /// from the kit's segmented control, every card springing to its new place; a recruit bounces in and the others make
        /// room, and a dismissed hero shrinks away as the rest close up.
        /// </summary>
        private sealed class OrderPart : KitPart
        {
            public OrderPart(GuiLayoutScene scene, Stage stage) : base(scene, stage, "Order", Board, Scale)
            {
                this.scene = scene;
                this.stage = stage;
                Label("Party", new Vector2(Left, Top - 20f), 28f, "Ink", display: true);
                count = Label(string.Empty, new Vector2(Left, Top - 52f), 16f, "InkDim");

                var sort = Add("Segmented Control", SortAt);
                Kit.Relabel(sort, "Option 1", "Level");
                Kit.Relabel(sort, "Option 2", "Class");
                Kit.Relabel(sort, "Option 3", "Name");
                sorting = sort.GetComponent<UniShapeValue>();
                var recruit = Add("Button", RecruitAt);
                Size(recruit, RecruitSize);
                Kit.Relabel(recruit, "Button", "+ Recruit");

                var grid = stage.Node("Party Grid", Holder);
                grid.anchorMin = grid.anchorMax = new Vector2(0.5f, 0.5f);
                grid.pivot = new Vector2(0.5f, 1f);
                grid.sizeDelta = new Vector2(GridWidth, GridHeight);
                grid.anchoredPosition = new Vector2(0f, GridTop);
                layout = grid.gameObject.AddComponent<UniLayout>();
                layout.Mode = LayoutMode.Row;
                layout.Wrap = true;
                layout.Gap = new Vector2(CardGap, CardGap);
                layout.Width = LayoutSize.Fixed;
                layout.Height = LayoutSize.Fixed;
                layout.Motion = LayoutMotion.Bouncy;
                foreach (var hero in Heroes) cards.Add((hero, Card(grid, hero)));
                newcomer = Card(grid, Newcomer);
                newcomer.gameObject.SetActive(false);
                Arrange(1, false);

                var first = new Vector2(SortAt.x - SegmentStep, SortAt.y);
                Plan(At(new Vector2(Left + 300f, Bottom - 40f)), new[]
                {
                    Beat.To(At(first), null, Wide(SegmentStep)),
                    Beat.Click("level", true),
                    Beat.Wait(Travel),
                    Beat.To(At(first + new Vector2(SegmentStep * 2f, 0f)), null, Wide(SegmentStep)),
                    Beat.Click("name", true),
                    Beat.Wait(Travel),
                    Beat.To(At(RecruitAt), null, Wide(RecruitSize.x)),
                    Beat.Click("recruit", true),
                    Beat.Wait(Travel),
                    Beat.To(At(DismissAt), null, Wide(CloseSize)),
                    Beat.Click("dismiss", true, true),
                    Beat.Wait(Travel)
                }, Pace);
                Aim("level", Captioned(sort, "Level"));
                Aim("name", Captioned(sort, "Name"));
                Aim("recruit", recruit);
                Aim("dismiss", () => (RectTransform)cards[Dismissed].card.Find("Close"));
            }

            private readonly GuiLayoutScene scene;
            private readonly Stage stage;
            private readonly UniText count;
            private readonly UniShapeValue sorting;
            private readonly UniLayout layout;
            private readonly List<((string name, string role, int level, float angle) hero, RectTransform card)> cards = new();
            private readonly RectTransform newcomer;
            private readonly List<RectTransform> order = new();
            private bool gone;

            /// <summary>A hero's card: an avatar in the brand ramp, the name, the class, the level and a close button.</summary>
            private RectTransform Card(RectTransform grid, (string name, string role, int level, float angle) hero)
            {
                var card = stage.Shape("Hero " + hero.name, grid, ShapeKind.RoundedRect, 18f);
                card.Rect.sizeDelta = CardSize;
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                var size = card.Rect.gameObject.AddComponent<UniLayout>();
                size.Width = LayoutSize.Fixed;
                size.Height = LayoutSize.Fixed;

                var avatar = stage.Shape("Avatar", card.Rect, ShapeKind.Circle);
                Pin(avatar.Rect, new Vector2(18f + AvatarSize * 0.5f, -18f - AvatarSize * 0.5f), Vector2.one * AvatarSize, 0.5f);
                Stage.Ramped(avatar.Fill, scene.brand, PaintProjectionKind.Linear, hero.angle);
                var letter = stage.Label(avatar.Rect, hero.name.Substring(0, 1), AvatarSize * 0.46f, Color.white, stretch: true,
                    face: scene.theme.DisplayFace);
                letter.WordWrap = false;
                Text(card.Rect, hero.name, 26f, "Ink", new Vector2(32f + AvatarSize, -34f), true);
                Text(card.Rect, hero.role, 18f, "InkDim", new Vector2(32f + AvatarSize, -64f), false);
                Text(card.Rect, "LEVEL", 14f, "InkDim", new Vector2(CardSize.x - 128f, -CardSize.y + 30f), false);
                Text(card.Rect, hero.level.ToString(), 34f, "Accent", new Vector2(CardSize.x - 72f, -CardSize.y + 34f), true);

                var close = stage.Shape("Close", card.Rect, ShapeKind.Circle);
                Pin(close.Rect, new Vector2(CardSize.x - 22f, -22f), Vector2.one * CloseSize, 0.5f);
                Stage.Solid(close.Fill, scene.palette["Inset"]);
                close.Rect.gameObject.AddComponent<Interactable>();
                var cross = stage.Label(close.Rect, "×", 18f, scene.palette["InkDim"], stretch: true);
                cross.WordWrap = false;
                return card.Rect;
            }

            private void Text(RectTransform card, string text, float size, string swatch, Vector2 at, bool display)
            {
                var label = stage.Label(card, text, size, scene.palette[swatch], HorizontalAlignment.Left, stretch: false,
                    face: display ? scene.theme.DisplayFace : null);
                label.WordWrap = false;
                Pin(label.rectTransform, at, new Vector2(CardSize.x - at.x - 12f, size * 1.5f), 0f);
            }

            private static void Pin(RectTransform rect, Vector2 at, Vector2 size, float edge)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(edge, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = at;
            }

            /// <summary>Puts the cards in the order <paramref name="key"/> sorts them by: 0 level, 1 class, 2 name; the newcomer with them once recruited.</summary>
            private void Arrange(int key, bool recruited)
            {
                order.Clear();
                var members = new List<((string name, string role, int level, float angle) hero, RectTransform card)>(cards);
                if (recruited) members.Add((Newcomer, newcomer));
                members.Sort((a, b) => key switch
                {
                    0 => b.hero.level.CompareTo(a.hero.level),
                    1 => string.CompareOrdinal(a.hero.role + a.hero.name, b.hero.role + b.hero.name),
                    _ => string.CompareOrdinal(a.hero.name, b.hero.name)
                });
                foreach (var member in members) order.Add(member.card);
                for (var i = 0; i < order.Count; i++)
                    if (order[i].GetSiblingIndex() != i)
                        order[i].SetSiblingIndex(i);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var key = played >= ClickAt("name") ? 2 : played >= ClickAt("level") ? 0 : 1;
                if (!Mathf.Approximately(sorting.Value, key)) sorting.Value = key;
                var recruited = played >= ClickAt("recruit");
                if (newcomer.gameObject.activeSelf != recruited) newcomer.gameObject.SetActive(recruited);
                Arrange(key, recruited);
                if (played >= ClickAt("dismiss") && !gone)
                {
                    gone = true;
                    layout.Dismiss(cards[Dismissed].card);
                }

                var heroes = Heroes.Length + (recruited ? 1 : 0) - (gone ? 1 : 0);
                var caption = heroes + " heroes";
                if (count.Text != caption) count.Text = caption;
            }

            private static readonly (string name, string role, int level, float angle)[] Heroes =
            {
                ("Ember", "Knight", 45, 20f), ("Aurora", "Mage", 42, 140f), ("Gale", "Ranger", 48, 260f),
                ("Bastion", "Knight", 38, 80f), ("Cinder", "Rogue", 51, 200f), ("Harbor", "Cleric", 36, 320f),
                ("Dune", "Ranger", 27, 40f), ("Frost", "Mage", 33, 170f)
            };

            private static readonly (string name, string role, int level, float angle) Newcomer = ("Iris", "Cleric", 12, 300f);

            /// <summary>The hero the pointer dismisses, by its place in <see cref="Heroes"/>, and where it stands sorted by name with the recruit in: first row, fourth card.</summary>
            private const int Dismissed = 6;

            private const int DismissedColumn = 3;

            private static readonly Vector2 Board = new(1760f, 760f);
            private const float Scale = 1.35f;
            private const float Pad = 28f;
            private const float Left = -1760f / Scale * 0.5f + Pad;
            private const float Top = 760f / Scale * 0.5f - Pad;
            private const float Bottom = -Top;
            private static readonly Vector2 SortAt = new(150f, Top - 36f);
            private const float SegmentStep = 77.3f;
            private static readonly Vector2 RecruitAt = new(-Left - 100f, Top - 36f);
            private static readonly Vector2 RecruitSize = new(180f, 46f);
            private static readonly Vector2 CardSize = new(CardWidth, CardHeight);
            private const float CardWidth = 292f;
            private const float CardHeight = 128f;
            private const float CardGap = 16f;
            private const int Columns = 4;
            private const float GridWidth = Columns * CardWidth + (Columns - 1) * CardGap;
            private const float GridHeight = 3f * CardHeight + 2f * CardGap;
            private const float GridTop = Top - 84f;
            private const float AvatarSize = 58f;
            private const float CloseSize = 28f;

            /// <summary>Where the dismissed hero's close button stands in the holder once the cards are sorted by name.</summary>
            private static readonly Vector2 DismissAt = new(
                -GridWidth * 0.5f + DismissedColumn * (CardWidth + CardGap) + CardWidth - 22f, GridTop - 22f);

            /// <summary>How long each change is left to play out before the pointer moves on.</summary>
            private const float Travel = 0.75f;

            private const float Pace = 1.2f;
        }

        /// <summary>
        /// A world map as the kit's snapping carousel: each world's card turns away, shrinks and fades as it leaves the
        /// middle under the kit's scroll effect; the pointer flings it on twice and plays the world it lands on.
        /// </summary>
        private sealed class ScrollPart : KitPart
        {
            public ScrollPart(GuiLayoutScene scene, Stage stage) : base(scene, stage, "Scroll", Board, Scale)
            {
                var items = G.Plain(stage);
                Label("Choose a world", new Vector2(Left, Top - 20f), 28f, "Ink", display: true);
                Label("Swipe to explore · 8 worlds", new Vector2(Left, Top - 52f), 16f, "InkDim");

                var row = stage.Node("Carousel", Holder);
                row.anchorMin = row.anchorMax = new Vector2(0.5f, 0.5f);
                row.sizeDelta = new Vector2(RowWidth, CardSize.y + 40f);
                row.anchoredPosition = new Vector2(0f, RowY);
                var flow = row.gameObject.AddComponent<UniLayout>();
                flow.Mode = LayoutMode.Row;
                flow.Gap = new Vector2(CardGap, CardGap);
                flow.AlignY = LayoutAlign.Center;
                flow.Width = LayoutSize.Fixed;
                flow.Height = LayoutSize.Fixed;
                var side = (RowWidth - CardSize.x) * 0.5f;
                flow.Padding = new Vector4(side, 0f, side, 0f);
                scroll = row.gameObject.AddComponent<UniLayoutScroll>();
                scroll.Axes = LayoutAxes.Horizontal;
                scroll.Snap = ScrollSnap.Center;
                for (var i = 0; i < Worlds.Length; i++) World(stage, scene, items, row, i);

                var play = Add("Button", PlayAt);
                Size(play, PlaySize);
                Kit.Relabel(play, "Button", "Play");
                playLabel = play.GetComponentInChildren<UniText>(true);

                var grab = new Vector2(CardSize.x * 0.35f, RowY);
                Plan(At(grab + new Vector2(60f, -200f)), new[]
                {
                    Beat.To(At(grab), null, Wide(CardSize.x)),
                    Beat.Drag(At(grab - new Vector2(FlingReach, 0f)), "fling", Wide(CardSize.x)),
                    Beat.Wait(CoastFor),
                    Beat.To(At(grab), null, Wide(CardSize.x)),
                    Beat.Drag(At(grab - new Vector2(FlingReach, 0f)), "again", Wide(CardSize.x)),
                    Beat.Wait(CoastFor),
                    Beat.To(At(PlayAt), null, Wide(PlaySize.x)),
                    Beat.Click("play", true, true),
                    Beat.Wait(PlayRead)
                }, Pace);
                Aim("play", play);
                this.grab = grab;
            }

            private readonly UniLayoutScroll scroll;
            private readonly UniText playLabel;
            private readonly Vector2 grab;

            /// <summary>One world's card: its art in a ramp of its own with its emblem, its number, its name and the stars earned.</summary>
            private static void World(Stage stage, GuiLayoutScene scene, GuiStylesScene.Builder items, RectTransform row, int index)
            {
                var (name, from, to, stars, emblem) = Worlds[index];
                var card = stage.Node("World " + (index + 1), row);
                card.sizeDelta = CardSize;
                var size = card.gameObject.AddComponent<UniLayout>();
                size.Width = LayoutSize.Fixed;
                size.Height = LayoutSize.Fixed;

                var face = stage.Shape("Face", card, ShapeKind.RoundedRect, 26f);
                stage.Stretch(face.Rect);
                Stage.Linear(face.Fill, from, to, 300f);
                Stage.AddStroke(face.Shape, Theme.Fade(Color.white, 0.18f), 1.5f, -1f);
                Stage.AddShadow(face.Shape, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -14f), 34f);
                var effect = face.Rect.gameObject.AddComponent<UniScrollEffect>();
                effect.Scale = 0.8f;
                effect.Opacity = 0.5f;
                effect.Tilt = 42f;
                effect.Reach = 1.4f;
                effect.Ease = Ease.Emphasized;

                var glow = stage.Shape("Glow", face.Rect, ShapeKind.Circle);
                stage.Box(glow.Rect, new Vector2(0f, 70f), Vector2.one * 260f);
                Stage.Radial(glow.Fill, new Color(1f, 0.95f, 0.85f, 0.4f), new Color(1f, 0.95f, 0.85f, 0f));
                emblem(items, face.Rect, new Vector2(0f, 70f), EmblemSize);
                var number = stage.Label(face.Rect, "World " + (index + 1), 18f, Theme.Fade(Color.white, 0.75f), HorizontalAlignment.Center,
                    stretch: false, face: scene.theme.DisplayFace);
                stage.Box(number.rectTransform, new Vector2(0f, -78f), new Vector2(CardSize.x, 26f));
                var title = stage.Label(face.Rect, name, 28f, Color.white, HorizontalAlignment.Center, stretch: false,
                    face: scene.theme.DisplayFace);
                stage.Box(title.rectTransform, new Vector2(0f, -112f), new Vector2(CardSize.x, 40f));
                for (var s = 0; s < 3; s++)
                {
                    var star = stage.Shape("Star", face.Rect, ShapeKind.Star);
                    stage.Box(star.Rect, new Vector2((s - 1) * 36f, -158f), Vector2.one * 28f);
                    Stage.Solid(star.Fill, s < stars ? new Color32(0xFF, 0xD2, 0x4A, 0xFF) : new Color(1f, 1f, 1f, 0.25f));
                }
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var pitch = CardSize.x + CardGap;
                var offset = Flung("fling", played, 0f, FirstStop * pitch);
                if (played >= Pointer.Timeline.Span("again").x) offset = Flung("again", played, FirstStop * pitch, SecondStop * pitch);
                scroll.ScrollTo(new Vector2(offset, 0f), false);
                var caption = played >= ClickAt("play") ? "Loading…" : "Play " + Worlds[SecondStop].name;
                if (playLabel.Text != caption) playLabel.Text = caption;
            }

            /// <summary>
            /// The carousel's offset through the fling named <paramref name="id"/>: from <paramref name="from"/> following
            /// the pointer while it holds, then coasting on to <paramref name="to"/>, the card it snaps to.
            /// </summary>
            private float Flung(string id, float played, float from, float to)
            {
                var span = Pointer.Timeline.Span(id);
                if (played < span.x) return from;
                var held = from + FlingReach * Dragged(id, played, At(grab), At(grab - new Vector2(FlingReach, 0f)));
                if (played < span.y) return held;
                var coast = Mathf.Clamp01((played - span.y) / CoastFor);
                return Mathf.Lerp(held, to, 1f - Mathf.Pow(1f - coast, 3f));
            }

            /// <summary>The worlds in order: name, the art's ramp, the stars earned and the emblem drawn on it.</summary>
            private static readonly (string name, Color from, Color to, int stars, System.Action<GuiStylesScene.Builder, Transform, Vector2, float> emblem)[] Worlds =
            {
                ("Treasure Bay", new Color32(0xFF, 0xB3, 0x47, 0xFF), new Color32(0x8A, 0x3B, 0x12, 0xFF), 3,
                    (k, p, at, s) => G.Chest(k, p, at, s, new Color32(0x9A, 0x5B, 0x2E, 0xFF), new Color32(0xFF, 0xC8, 0x4A, 0xFF))),
                ("Ember Peaks", new Color32(0xFF, 0x7A, 0x2E, 0xFF), new Color32(0x6B, 0x1A, 0x3A, 0xFF), 3,
                    (k, p, at, s) => G.Flame(k, p, at, s, new Color32(0xFF, 0xC8, 0x4A, 0xFF), Color.white)),
                ("Frost Hollow", new Color32(0x5E, 0xE0, 0xF5, 0xFF), new Color32(0x1A, 0x3E, 0x8A, 0xFF), 2,
                    (k, p, at, s) => G.Gem(k, p, at, s)),
                ("Crown Keep", new Color32(0xC9, 0xA0, 0xFF, 0xFF), new Color32(0x3E, 0x1A, 0x6B, 0xFF), 2,
                    (k, p, at, s) => G.Crown(k, p, at, s, new Color32(0xFF, 0xC8, 0x4A, 0xFF))),
                ("Star Port", new Color32(0x4D, 0x8D, 0xFF, 0xFF), new Color32(0x16, 0x1E, 0x5A, 0xFF), 1,
                    (k, p, at, s) => G.Rocket(k, p, at, s, new Color32(0xE8, 0xEE, 0xF8, 0xFF), new Color32(0xFF, 0x3D, 0x7A, 0xFF), new Color32(0xFF, 0xC8, 0x4A, 0xFF))),
                ("Candy Isle", new Color32(0xFF, 0x8F, 0xC8, 0xFF), new Color32(0x8A, 0x1A, 0x5A, 0xFF), 0,
                    (k, p, at, s) => G.Candy(k, p, at, s, new Color32(0xFF, 0x3D, 0x7A, 0xFF))),
                ("Wild Woods", new Color32(0x7C, 0xE5, 0x6A, 0xFF), new Color32(0x14, 0x5A, 0x2E, 0xFF), 0,
                    (k, p, at, s) => G.Slime(k, p, at, s, new Color32(0x5A, 0xD8, 0x4A, 0xFF))),
                ("Deep Reef", new Color32(0x2E, 0xD4, 0xC0, 0xFF), new Color32(0x0E, 0x3A, 0x5A, 0xFF), 0,
                    (k, p, at, s) => G.Ship(k, p, at, s, new Color32(0xE8, 0xEE, 0xF8, 0xFF), new Color32(0x5E, 0xE0, 0xF5, 0xFF)))
            };

            private const int FirstStop = 2;
            private const int SecondStop = 4;
            private static readonly Vector2 Board = new(1760f, 760f);
            private const float Scale = 1.35f;
            private const float Pad = 28f;
            private const float Left = -1760f / Scale * 0.5f + Pad;
            private const float Top = 760f / Scale * 0.5f - Pad;
            private const float RowWidth = 1760f / Scale - 2f * Pad;
            private const float RowY = -6f;
            private static readonly Vector2 CardSize = new(300f, 400f);
            private const float CardGap = 28f;
            private const float EmblemSize = 150f;
            private static readonly Vector2 PlayAt = new(0f, -760f / Scale * 0.5f + 54f);
            private static readonly Vector2 PlaySize = new(300f, 52f);
            private const float FlingReach = 220f;
            private const float CoastFor = 0.7f;
            private const float PlayRead = 0.5f;
            private const float Pace = 1.1f;
        }
    }
}
