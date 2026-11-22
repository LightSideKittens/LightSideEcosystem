using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The layout section of the UniGUI reel: a window of the kit resized by its corner, the cards inside it reflowing
    /// from rows of four to rows of two and their text rewrapping, as a layout that adapts to any size does; then a
    /// leaderboard of a hundred thousand players in the kit's virtual list, flung through and searched for one row; a party
    /// sorted and changed under the layout's motion; a carousel of worlds under the scroll's depth effect.
    /// </summary>
    /// <remarks>
    /// The window resizes through its own handle under the pointer's drag, and the kit's layout places the cards for each
    /// size it passes. The section opens as the controls section's dive lands.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiLayoutScene))]
    public sealed partial class GuiLayoutScene : GuiKitSection
    {
        [SerializeField] private string lead = "Any";

        /// <summary>The headline's last word for each beat: the window's size, the list's length, the party's order, the carousel's scroll.</summary>
        [SerializeField] private string[] words = { "size.", "length.", "order.", "scroll." };

        [SerializeField] private string promise = "Layouts that reflow, scroll and animate.";

        private static readonly Link[] links = { Link.Rise, Link.Whip, Link.Drop, Link.Push, Link.Whip };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildBoards(Stage stage) =>
            new Part[] { new ReflowPart(this, stage), new LengthPart(this, stage), new OrderPart(this, stage), new ScrollPart(this, stage) };

        /// <summary>
        /// A quest board in a window of the kit: a flow of quest cards in the kit's scroll view that wraps as the window is
        /// dragged narrower by its corner, scrolls once it no longer fits, and wraps back as the window widens again.
        /// </summary>
        private sealed class ReflowPart : KitPart
        {
            public ReflowPart(GuiLayoutScene scene, Stage stage) : base(scene, stage, "Reflow", Board, Scale)
            {
                var window = Add("Window", WindowAt, 0f, 1f);
                var rect = (RectTransform)window.transform;
                rect.sizeDelta = WideSize;
                window.GetComponent<UniWindow>().Snapping = false;
                Kit.Relabel(window, "Window", "Quest board");
                var content = window.transform.Find("Content");
                content.Find("Label").gameObject.SetActive(false);

                var scroll = Add("Scroll View", Vector2.zero);
                var scrollRect = (RectTransform)scroll.transform;
                scrollRect.SetParent(content, false);
                scrollRect.anchorMin = Vector2.zero;
                scrollRect.anchorMax = Vector2.one;
                scrollRect.offsetMin = new Vector2(FlowPad, FlowPad);
                scrollRect.offsetMax = new Vector2(-FlowPad, -FlowPad);
                scrollRect.SetAsFirstSibling();
                var flow = (RectTransform)scroll.GetComponentInChildren<UniLayoutScroll>(true).transform;
                for (var i = flow.childCount - 1; i >= 0; i--) Object.DestroyImmediate(flow.GetChild(i).gameObject);
                var layout = flow.GetComponent<UniLayout>();
                layout.Mode = LayoutMode.Row;
                layout.Wrap = true;
                layout.Gap = new Vector2(CardGap, CardGap);
                for (var i = 0; i < Quests.Length; i++) Quest(stage, scene, flow, Quests[i], i);

                var corner = WindowAt + new Vector2(WideSize.x - 8f, -WideSize.y + 8f);
                var narrow = corner + new Vector2(NarrowSize.x - WideSize.x, WideSize.y - NarrowSize.y);
                var browse = WindowAt + new Vector2(NarrowSize.x * 0.5f, -NarrowSize.y * 0.6f);
                Plan(At(corner + new Vector2(80f, -80f)), new[]
                {
                    Beat.To(At(corner), null, Wide(16f)),
                    Beat.Drag(At(narrow), "narrow", Wide(16f), 0.5f),
                    Beat.To(At(browse), null, Wide(CardWidth)),
                    Beat.Drag(At(browse + new Vector2(0f, ScrollTravel)), "scroll", Wide(CardWidth), 0.3f),
                    Beat.Wait(0.3f),
                    Beat.To(At(narrow), null, Wide(16f)),
                    Beat.Drag(At(corner + new Vector2(-60f, 0f)), "widen", Wide(16f), 0.3f)
                }, Pace);
                var handle = window.transform.Find("Content/Resize").gameObject;
                Aim("narrow", handle);
                Aim("widen", handle);
                Aim("scroll", flow);
                grips = new[] { new Grip(handle, "narrow"), new Grip(flow.gameObject, "scroll"), new Grip(handle, "widen") };
                this.window = rect;
            }

            private readonly Grip[] grips;
            private readonly RectTransform window;

            /// <summary>A quest card in the flow: a colored badge, its title and a line that wraps as the card narrows.</summary>
            private static void Quest(Stage stage, GuiLayoutScene scene, RectTransform flow, (string title, string text, Color tone) quest, int index)
            {
                var card = stage.Shape("Quest", flow, ShapeKind.RoundedRect, 12f);
                card.Rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                var size = card.Rect.gameObject.AddComponent<UniLayout>();
                size.Width = LayoutSize.Fixed;
                size.Height = LayoutSize.Fixed;
                var badge = stage.Shape("Badge", card.Rect, index % 2 == 0 ? ShapeKind.Hexagon : ShapeKind.Circle);
                badge.Rect.anchorMin = badge.Rect.anchorMax = new Vector2(0f, 1f);
                badge.Rect.sizeDelta = Vector2.one * 34f;
                badge.Rect.anchoredPosition = new Vector2(34f, -34f);
                Stage.Solid(badge.Fill, quest.tone);
                var title = stage.Label(card.Rect, quest.title, 22f, scene.palette["Ink"], HorizontalAlignment.Left, stretch: false,
                    face: scene.theme.DisplayFace);
                Pin(title.rectTransform, new Vector2(62f, -34f), new Vector2(CardWidth - 76f, 30f));
                var line = stage.Label(card.Rect, quest.text, 18f, scene.palette["InkDim"], HorizontalAlignment.Left, stretch: false);
                line.WordWrap = true;
                Pin(line.rectTransform, new Vector2(18f, -94f), new Vector2(CardWidth - 36f, 76f));
            }

            private static void Pin(RectTransform rect, Vector2 at, Vector2 size)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = size;
                rect.anchoredPosition = at;
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (played < Pointer.Timeline.Span("narrow").x) window.sizeDelta = WideSize;
                foreach (var grip in grips) Pose(grip, played);
            }

            private static readonly Vector2 Board = new(1760f, 760f);
            private const float Scale = 1.35f;
            private static readonly Vector2 WindowAt = new(-620f, 250f);
            private static readonly Vector2 WideSize = new(1240f, 420f);
            private static readonly Vector2 NarrowSize = new(652f, 500f);
            private const float FlowPad = 16f;
            private const float CardGap = 14f;
            private const float CardWidth = 280f;
            private const float CardHeight = 150f;
            private const float Pace = 0.8f;

            /// <summary>How far the pointer drags the narrowed board's list up, scrolling it on.</summary>
            private const float ScrollTravel = 200f;

            private static readonly (string title, string text, Color tone)[] Quests =
            {
                ("Ember Gate", "Clear the gate with a party of four before the season ends.", new Color32(0xFF, 0x8A, 0x1F, 0xFF)),
                ("Night Raid", "Defend the camp until dawn without losing a hero.", new Color32(0xFF, 0x3D, 0x7A, 0xFF)),
                ("Old Friends", "Play three matches with your guild this week.", new Color32(0x16, 0xC9, 0xA4, 0xFF)),
                ("Treasure", "Open five chests found on the northern map.", new Color32(0xFF, 0xC8, 0x4A, 0xFF)),
                ("Duelist", "Win two ranked duels in a row.", new Color32(0xFF, 0x8A, 0x1F, 0xFF)),
                ("Healer", "Restore ten thousand health to allies.", new Color32(0x16, 0xC9, 0xA4, 0xFF)),
                ("Scout", "Reveal every corner of the map in one match.", new Color32(0xFF, 0x3D, 0x7A, 0xFF)),
                ("Collector", "Earn a legendary item from a season chest.", new Color32(0xFF, 0xC8, 0x4A, 0xFF))
            };
        }

        /// <summary>
        /// A season leaderboard of a hundred thousand players in the kit's list, which keeps only the rows on screen alive:
        /// the pointer flings it thousands of rows on, then asks for its own row, and the list glides there, the count of
        /// live rows beside it never moving.
        /// </summary>
        private sealed class LengthPart : KitPart, ILayoutListSource
        {
            public LengthPart(GuiLayoutScene scene, Stage stage) : base(scene, stage, "Length", Board, Scale)
            {
                this.scene = scene;
                var card = stage.Shape("Leaderboard", Holder, ShapeKind.RoundedRect, 22f);
                stage.Box(card.Rect, CardAt, CardSize);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                Label("Season leaderboard", new Vector2(ListLeft, CardTop - 36f), 28f, "Ink", display: true);
                Label("100,000 players", new Vector2(ListLeft, CardTop - 70f), 18f, "InkDim");
                Label("Rank", new Vector2(ListLeft + RowPad, HeaderY), 16f, "InkDim");
                Label("Player", new Vector2(ListLeft + RowPad + RankWidth, HeaderY), 16f, "InkDim");
                Label("Score", new Vector2(ListLeft + ListWidth - RowPad - ScrollbarRoom, HeaderY), 16f, "InkDim", 1f);

                var control = Add("List", new Vector2(ListLeft, ListTop), 0f, 1f);
                ((RectTransform)control.transform).sizeDelta = new Vector2(ListWidth, ListHeight);
                list = control.GetComponentInChildren<UniLayoutList>(true);
                scroll = list.GetComponent<UniLayoutScroll>();
                list.GetComponent<UniTextList>().enabled = false;
                var template = (RectTransform)list.transform.Find("Item");
                var name = template.GetComponentInChildren<UniText>(true);
                name.FontSize = RowText;
                name.GetComponent<UniLayout>().Width = LayoutSize.Fill();
                var rank = Object.Instantiate(name.gameObject, template).GetComponent<UniText>();
                rank.name = "Rank";
                rank.transform.SetAsFirstSibling();
                var rankSize = rank.GetComponent<UniLayout>();
                rankSize.Width = LayoutSize.Fixed;
                ((RectTransform)rank.transform).sizeDelta = new Vector2(RankWidth - RowPad, RowText * 1.4f);
                var score = Object.Instantiate(name.gameObject, template).GetComponent<UniText>();
                score.name = "Score";
                score.HorizontalAlignment = HorizontalAlignment.Right;
                score.GetComponent<UniLayout>().Width = LayoutSize.Hug;
                list.Source = this;

                Label("100,000", new Vector2(StatX, 150f), 64f, "Ink", 0.5f, display: true);
                Label("rows in the list", new Vector2(StatX, 98f), 20f, "InkDim", 0.5f);
                alive = Label("0", new Vector2(StatX, -6f), 64f, "Accent", 0.5f, display: true);
                Label("rows alive on screen", new Vector2(StatX, -58f), 20f, "InkDim", 0.5f);
                var find = Add("Button", new Vector2(StatX, FindY));
                Size(find, new Vector2(260f, 52f));
                Kit.Relabel(find, "Button", "Find me");
                find.GetComponentInChildren<UniText>(true).FontSize = 20f;

                var grab = new Vector2(ListLeft + ListWidth * 0.5f, ListTop - ListHeight * 0.72f);
                Plan(At(grab + new Vector2(140f, -200f)), new[]
                {
                    Beat.To(At(grab), null, Wide(ListWidth)),
                    Beat.Drag(At(grab + new Vector2(0f, FlingReach)), "fling", Wide(ListWidth)),
                    Beat.Wait(CoastFor),
                    Beat.To(At(new Vector2(StatX, FindY)), null, Wide(260f)),
                    Beat.Click("find", true),
                    Beat.Wait(GlideFor + FoundRead)
                }, Pace);
                Aim("fling", list);
                Aim("find", find);
                this.grab = grab;
            }

            private readonly UniLayoutList list;
            private readonly UniLayoutScroll scroll;
            private readonly UniText alive;
            private readonly Vector2 grab;
            private readonly GuiLayoutScene scene;
            private readonly Dictionary<RectTransform, (UniText rank, UniText name, UniText score)> rows = new();
            private float pitch;

            int ILayoutListSource.Count => Players;

            void ILayoutListSource.Bind(RectTransform item, int index)
            {
                if (!rows.TryGetValue(item, out var row))
                {
                    row = (Part(item, "Rank"), Part(item, "Label"), Part(item, "Score"));
                    rows.Add(item, row);
                }

                var you = index == You;
                row.rank.Text = $"#{index + 1:N0}";
                row.name.Text = you ? "Mia (you)" : Names[index % Names.Length] + Suffix(index);
                row.score.Text = Score(index).ToString("N0");
                row.rank.color = scene.palette[you ? "Accent" : "InkDim"];
                row.name.color = scene.palette[you ? "Accent" : "Ink"];
                row.score.color = scene.palette[you ? "Accent" : "Ink"];
            }

            private static UniText Part(RectTransform item, string name) => item.Find(name).GetComponent<UniText>();

            /// <summary>A number after a name that recurs, so neighboring rows read as different players.</summary>
            private static string Suffix(int index) => index < Names.Length ? string.Empty : (index / Names.Length % 97 + 2).ToString();

            /// <summary>The score of the player at <paramref name="index"/>: falling with rank, never tied.</summary>
            private static int Score(int index) => Mathf.RoundToInt(98420f * Mathf.Pow(1f - index / (float)Players, 1.6f)) + (Players - index) % 7;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (pitch <= 0f && list.TryGetItem(0, out var first) && list.TryGetItem(1, out var second))
                    pitch = Mathf.Abs(first.anchoredPosition.y - second.anchoredPosition.y);
                var step = pitch > 0f ? pitch : RowText * 2.2f;

                var held = FlingReach * Dragged("fling", played, At(grab), At(grab + new Vector2(0f, FlingReach)));
                var flung = Flung * step;
                var coast = Mathf.Clamp01((played - Pointer.Timeline.Span("fling").y) / CoastFor);
                var offset = played < Pointer.Timeline.Span("fling").y ? held : Mathf.Lerp(held, flung, 1f - Mathf.Pow(1f - coast, 3f));
                var found = You * step - (ListHeight - step) * 0.5f;
                offset = Mathf.Lerp(offset, found, Ease.Emphasized.Window(played, ClickAt("find"), GlideFor));
                scroll.ScrollTo(new Vector2(0f, Mathf.Max(0f, offset)), false);

                var views = 0;
                foreach (Transform child in list.transform)
                    if (child.gameObject.activeSelf && rows.ContainsKey((RectTransform)child))
                        views++;
                var count = views.ToString();
                if (alive.Text != count) alive.Text = count;
            }

            private static readonly string[] Names =
            {
                "Aurora", "Cinder", "Frost", "Ember", "Dune", "Gale", "Harbor", "Bastion", "Nova", "Wren", "Sable", "Onyx",
                "Juniper", "Rook", "Vale", "Kestrel", "Lumen", "Moss", "Pike", "Quill", "Rune", "Thorn", "Umber", "Vesper"
            };

            private const int Players = 100000;
            private const int You = 48212;
            private const int Flung = 21800;
            private static readonly Vector2 Board = new(1760f, 760f);
            private const float Scale = 1.35f;
            private static readonly Vector2 CardAt = new(-200f, -6f);
            private static readonly Vector2 CardSize = new(860f, 540f);
            private const float CardTop = -6f + 270f;
            private const float ListLeft = -200f - 430f + 28f;
            private const float ListWidth = 860f - 56f;
            private const float HeaderY = CardTop - 110f;
            private const float ListTop = HeaderY - 18f;
            private const float ListHeight = ListTop - (-6f - 270f) - 24f;
            private const float RowPad = 12f;
            private const float RankWidth = 150f;
            private const float ScrollbarRoom = 20f;
            private const float RowText = 21f;
            private const float StatX = 445f;
            private const float FindY = -170f;
            private const float FlingReach = 240f;
            private const float CoastFor = 1.5f;
            private const float GlideFor = 1.3f;
            private const float FoundRead = 0.9f;
            private const float Pace = 1f;
        }
    }
}
