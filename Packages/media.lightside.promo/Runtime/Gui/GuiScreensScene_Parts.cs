using System;
using UnityEngine;
using G = LightSide.Promo.GuiStylesScene.Glyphs;
using Items = LightSide.Promo.GuiStylesScene.Builder;

namespace LightSide.Promo
{
    /// <summary>The screens of <see cref="GuiScreensScene"/>.</summary>
    public sealed partial class GuiScreensScene
    {
        /// <summary>
        /// One screen of the game: a board the size of a window, with the game's header — the player and their currencies
        /// — along its top, and the shapes the screen's artwork is drawn with.
        /// </summary>
        private abstract class ScreenPart : KitPart
        {
            protected ScreenPart(GuiScreensScene scene, Stage stage, string name, string title)
                : base(scene, stage, name, Board, Scale)
            {
                this.scene = scene;
                this.stage = stage;
                items = G.Plain(stage);
                Label(title, new Vector2(Left, Top - 22f), 26f, "Ink", display: true);
                Avatar(new Vector2(-Left - AvatarSize * 0.5f, Top - 22f), AvatarSize, "M", 35f);
                Label("Mia", new Vector2(-Left - AvatarSize - 12f, Top - 12f), 17f, "Ink", 1f, display: true);
                Label("Level 12", new Vector2(-Left - AvatarSize - 12f, Top - 33f), 13f, "InkDim", 1f);
                Purse(new Vector2(-Left - ProfileWidth, Top - 22f), "1,240", G.Coin);
                Purse(new Vector2(-Left - ProfileWidth - PurseWidth - PurseGap, Top - 22f), "86", G.Gem);
            }

            protected readonly GuiScreensScene scene;
            protected readonly Stage stage;

            /// <summary>What the screen's items and currencies are drawn with: the item glyphs of the styles section, lit from above.</summary>
            protected readonly Items items;

            /// <summary>
            /// A currency's purse, its right end on <paramref name="native"/>: a pill holding the currency's
            /// <paramref name="token"/>, its amount and the button that buys more.
            /// </summary>
            private void Purse(Vector2 native, string amount, Func<Items, Transform, Vector2, float, RectTransform> token)
            {
                var pill = stage.Shape("Purse", Holder, ShapeKind.Capsule);
                stage.Box(pill.Rect, native - new Vector2(PurseWidth * 0.5f, 0f), new Vector2(PurseWidth, PurseHeight));
                Stage.Solid(pill.Fill, scene.palette["Inset"]);
                Stage.AddStroke(pill.Shape, scene.palette["Line"], 1f, -1f);
                token(items, Holder, native - new Vector2(PurseWidth - PurseHeight * 0.5f, 0f), TokenSize);
                Label(amount, native - new Vector2(PurseWidth - PurseHeight - 4f, 0f), 17f, "Ink", display: true);
                var more = stage.Shape("More", Holder, ShapeKind.Circle);
                stage.Box(more.Rect, native - new Vector2(PurseHeight * 0.5f, 0f), Vector2.one * (PurseHeight - 10f));
                Stage.Solid(more.Fill, scene.palette["Accent"]);
                var plus = stage.Label(more.Rect, "+", 20f, Color.white, stretch: true, face: scene.theme.DisplayFace);
                plus.WordWrap = false;
            }

            /// <summary>
            /// A selection ring around a kit control centered on <paramref name="native"/> and <paramref name="size"/>
            /// across: <see cref="RingGap"/> outside the control's own outline, its corners concentric with the control's.
            /// </summary>
            protected Widget Ring(string name, GameObject control, Vector2 native, Vector2 size)
            {
                var outline = (PrimitiveShapeProvider)control.GetComponent<UniShape>().Shape;
                var ring = stage.Shape(name, Holder, ShapeKind.RoundedRect, outline.Radius + RingGap);
                ((PrimitiveShapeProvider)ring.Shape.Shape).Smoothing = outline.Smoothing;
                stage.Box(ring.Rect, native, size + Vector2.one * RingGap * 2f);
                Stage.Solid(ring.Fill, Color.clear);
                Stage.AddStroke(ring.Shape, scene.palette["Accent"], RingWidth, 1f);
                return ring;
            }

            /// <summary>A card of the kit's raised surface, <paramref name="size"/> across, centered on <paramref name="native"/>.</summary>
            protected Widget Card(string name, Vector2 native, Vector2 size, float radius = 14f)
            {
                var card = stage.Shape(name, Holder, ShapeKind.RoundedRect, radius);
                stage.Box(card.Rect, native, size);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
                return card;
            }

            /// <summary>
            /// Artwork: a rounded panel in a warm gradient with a soft light behind its middle and the item
            /// <paramref name="icon"/> draws on it, <paramref name="share"/> of the panel's height across.
            /// </summary>
            protected Widget Art(string name, Vector2 native, Vector2 size, Color from, Color to, Func<Transform, float, RectTransform> icon,
                float share = 0.78f, float radius = 12f)
            {
                var art = stage.Shape(name, Holder, ShapeKind.RoundedRect, radius);
                stage.Box(art.Rect, native, size);
                Stage.Linear(art.Fill, from, to, 315f);
                art.Rect.gameObject.AddComponent<UniShapeMask>().ShowShape = true;
                var glow = stage.Shape("Glow", art.Rect, ShapeKind.Circle);
                stage.Box(glow.Rect, Vector2.zero, Vector2.one * size.y * 1.2f);
                Stage.Radial(glow.Fill, new Color(1f, 0.93f, 0.8f, 0.45f), new Color(1f, 0.93f, 0.8f, 0f));
                icon(art.Rect, size.y * share);
                return art;
            }


            protected static readonly Vector2 Board = new(1760f, 820f);
            protected const float Scale = 1.6f;
            protected const float Pad = 30f;
            protected const float Left = -1760f / Scale * 0.5f + Pad;
            protected const float Top = 820f / Scale * 0.5f - Pad;
            protected const float Bottom = -Top;
            private const float AvatarSize = 44f;

            /// <summary>How much of the header's right end the profile takes: the avatar, the name and the level.</summary>
            private const float ProfileWidth = 150f;

            private const float PurseWidth = 140f;
            private const float PurseHeight = 38f;
            private const float PurseGap = 14f;
            private const float TokenSize = 26f;

            /// <summary>How far outside a control's outline its selection ring stands, and how thick the ring is drawn.</summary>
            protected const float RingGap = 4f;

            private const float RingWidth = 2.5f;

            protected static readonly Color Ember = new Color32(0xFF, 0x8A, 0x1F, 0xFF);
            protected static readonly Color Rose = new Color32(0xFF, 0x3D, 0x7A, 0xFF);
            protected static readonly Color Plum = new Color32(0x6B, 0x2A, 0x4A, 0xFF);
            protected static readonly Color Gold = new Color32(0xFF, 0xC8, 0x4A, 0xFF);
            protected static readonly Color Teal = new Color32(0x16, 0xC9, 0xA4, 0xFF);
            protected static readonly Color Night = new Color32(0x2A, 0x1E, 0x2C, 0xFF);
            protected static readonly Color Steel = new Color32(0xD9, 0xE2, 0xEE, 0xFF);
            protected static readonly Color Bronze = new Color32(0xC9, 0x8A, 0x3E, 0xFF);
            protected static readonly Color Wood = new Color32(0x9A, 0x5B, 0x2E, 0xFF);
            protected static readonly Color Glass = new Color32(0xF2, 0xEC, 0xF6, 0xFF);
        }

        /// <summary>The main menu: the game's title over its key art, the menu, and the news of the latest patch; the pointer opens the settings.</summary>
        private sealed class MenuPart : ScreenPart
        {
            public MenuPart(GuiScreensScene scene, Stage stage) : base(scene, stage, "Menu", string.Empty)
            {
                var hero = Art("Key Art", new Vector2(Left + 275f, -20f), new Vector2(550f, 400f), Ember, Plum,
                    (node, size) => G.Helm(items, node, new Vector2(110f, -30f), size, Steel, Rose), 0.62f, 20f);
                Label("SILVER KNIGHTS", new Vector2(Left + 30f, 140f), 42f, "Ink", display: true);
                Label("Season 4 · The Ember Gate", new Vector2(Left + 32f, 104f), 17f, "Ink");
                hero.Rect.SetAsFirstSibling();

                var at = new Vector2(Left + 30f + 100f, 30f);
                for (var i = 0; i < Items.Length; i++)
                {
                    var button = Add("Button", at - new Vector2(0f, ItemStep * i));
                    Size(button, new Vector2(200f, 44f));
                    Kit.Relabel(button, "Button", Items[i]);
                    if (i == SettingsItem) settings = button;
                }

                Card("News", new Vector2(-Left - 230f, -20f), new Vector2(460f, 400f), 18f);
                Art("Patch Art", new Vector2(-Left - 230f, 80f), new Vector2(420f, 170f), Rose, Ember,
                    (node, size) => G.Flame(items, node, Vector2.zero, size, Gold, Color.white));
                Label("Patch 4.2 is live", new Vector2(-Left - 440f, -32f), 22f, "Ink", display: true);
                Label("Ranked returns with the Ember Gate raid,", new Vector2(-Left - 440f, -64f), 16f, "InkDim");
                Label("two new heroes and a new map.", new Vector2(-Left - 440f, -86f), 16f, "InkDim");
                var notes = Add("Button", new Vector2(-Left - 370f, -160f));
                Kit.Relabel(notes, "Button", "Read notes");

                var settingsAt = at - new Vector2(0f, ItemStep * SettingsItem);
                Plan(At(new Vector2(Left + 160f, Bottom - 30f)), new[]
                {
                    Beat.To(At(at), null, Wide(200f)),
                    Beat.Wait(BrowseRead),
                    Beat.To(At(settingsAt), null, Wide(200f)),
                    Beat.Click("settings")
                }, Pace);
                Aim("settings", settings);
            }

            public override bool Navigates => true;

            private readonly GameObject settings;

            private static readonly string[] Items = { "Play", "Continue", "Collection", "Settings" };
            private const int SettingsItem = 3;
            private const float ItemStep = 56f;
            private const float BrowseRead = 0.15f;
            private const float Pace = 1.2f;
        }

        /// <summary>
        /// The settings: tabs over a video page and a controls page of key bindings; the pointer opens the controls,
        /// picks the jump binding and a key press rebinds it, the big key beside the bindings showing the key bound.
        /// </summary>
        private sealed class SettingsPart : ScreenPart
        {
            public SettingsPart(GuiScreensScene scene, Stage stage) : base(scene, stage, "Settings", "Settings")
            {
                Closeup = true;
                var tabs = Add("Segmented Control", TabsAt, 0.5f);
                Kit.Relabel(tabs, "Option 1", "Video");
                Kit.Relabel(tabs, "Option 2", "Audio");
                Kit.Relabel(tabs, "Option 3", "Controls");
                tab = tabs.GetComponent<UniShapeValue>();

                Card("Page", PageAt, PageSize, 18f);
                video = Page("Video Page");
                controls = Page("Controls Page");
                for (var i = 0; i < Video.Length; i++)
                {
                    Into(video, Label(Video[i].name, new Vector2(RowLeft, RowY(i)), 18f, "Ink"));
                    Into(video, Label(Video[i].value, new Vector2(ValueLeft, RowY(i)), 18f, "Accent"));
                }

                for (var i = 0; i < Bindings.Length; i++)
                {
                    var (action, keys) = Bindings[i];
                    Into(controls, Label(action, new Vector2(RowLeft, RowY(i)), 18f, "Ink"));
                    var x = ValueLeft;
                    foreach (var key in keys)
                    {
                        var width = i == Rebound ? ReboundWidth : Mathf.Max(CapHeight, key.Length * CapLetter + 24f);
                        var cap = Add("Button", new Vector2(x + width * 0.5f, RowY(i)));
                        Size(cap, new Vector2(width, CapHeight));
                        Kit.Relabel(cap, "Button", key);
                        Into(controls, cap);
                        if (i == Rebound)
                        {
                            jump = cap;
                            jumpLabel = cap.GetComponentInChildren<UniText>(true);
                        }

                        x += width + CapGap;
                    }
                }

                listening = Ring("Listening", jump, new Vector2(ValueLeft + ReboundWidth * 0.5f, RowY(Rebound)),
                    new Vector2(ReboundWidth, CapHeight));
                Into(controls, listening.Rect);

                Card("Key Card", KeyCardAt, KeyCardSize, 18f);
                bigKey = stage.Shape("Big Key", Holder, ShapeKind.RoundedRect, 28f);
                stage.Box(bigKey.Rect, KeyCardAt + new Vector2(0f, 30f), Vector2.one * BigKey);
                Stage.Linear(bigKey.Fill, Ember, Rose, 300f);
                Stage.AddBevel(bigKey.Shape, 110f, 10f, 1.4f);
                bigKeyDepth = new ExtrudeLayer
                {
                    Offset = new Vector2(0f, -KeyDepth), NearColor = Plum, FarColor = Night, Steps = 12
                };
                bigKey.Shape.Layers.Insert(0, bigKeyDepth);
                Stage.AddShadow(bigKey.Shape, new Color(0f, 0f, 0f, 0.5f), new Vector2(0f, -KeyDepth - 14f), 30f);
                bigKeyAt = bigKey.Rect.anchoredPosition;
                bigLabel = stage.Label(bigKey.Rect, "Space", 44f, Color.white, face: scene.theme.DisplayFace);
                bigLabel.WordWrap = false;
                Label("Jump", KeyCardAt + new Vector2(0f, -BigKey * 0.5f - 34f), 18f, "Ink", 0.5f, display: true);

                Plan(At(new Vector2(Left + 300f, Bottom - 30f)), new[]
                {
                    Beat.To(At(TabsAt + new Vector2(TabStep, 0f)), null, Wide(77f)),
                    Beat.Click("controls", true),
                    Beat.To(At(new Vector2(ValueLeft + ReboundWidth * 0.5f, RowY(Rebound))), null, Wide(ReboundWidth)),
                    Beat.Click("rebind", true),
                    Beat.Wait(0.35f),
                    Beat.Key("F", "key"),
                    Beat.Wait(0.2f),
                    Beat.To(At(KeyCardAt + new Vector2(-BigKey * 0.5f - 40f, -BigKey * 0.5f)), null, Wide(BigKey)),
                    Beat.Wait(0.5f)
                }, Pace);
                Aim("controls", Captioned(tabs, "Controls"));
                Aim("rebind", jump);
            }

            private readonly UniShapeValue tab;
            private readonly CanvasGroup video;
            private readonly CanvasGroup controls;
            private readonly GameObject jump;
            private readonly UniText jumpLabel;
            private readonly Widget listening;
            private readonly Widget bigKey;
            private readonly ExtrudeLayer bigKeyDepth;
            private readonly Vector2 bigKeyAt;
            private readonly UniText bigLabel;

            /// <summary>A page of the tabs: a group over the whole board that the controls of one tab are moved into.</summary>
            private CanvasGroup Page(string name) => stage.Stretch(stage.Node(name, Holder)).gameObject.AddComponent<CanvasGroup>();

            private static void Into(CanvasGroup page, Component part) => part.transform.SetParent(page.transform, true);

            private static void Into(CanvasGroup page, GameObject part) => part.transform.SetParent(page.transform, true);

            private static float RowY(int index) => PageAt.y + PageSize.y * 0.5f - 52f - index * RowStep;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var opened = played >= ClickAt("controls");
                tab.Value = opened ? 2 : 0;
                var swap = Ease.Emphasized.Window(played, ClickAt("controls"), PageSwap);
                video.alpha = 1f - swap;
                controls.alpha = swap;
                video.gameObject.SetActive(swap < 1f);
                controls.gameObject.SetActive(swap > 0f);
                video.transform.localPosition = new Vector3(-PageSlide * swap, 0f, 0f);
                controls.transform.localPosition = new Vector3(PageSlide * (1f - swap), 0f, 0f);

                var pressed = Pointer.Timeline.Span("key").x;
                var waiting = played >= ClickAt("rebind") && played < pressed;
                var bound = played >= pressed;
                var caption = bound ? "F" : waiting ? "Press a key" : "Space";
                if (jumpLabel.Text != caption) jumpLabel.Text = caption;
                Stage.Appear(listening.Rect, waiting ? played - ClickAt("rebind") : -1f, Motion.Back, RingGrow);
                if (waiting) Stage.Alpha(listening.Shape, 0.55f + 0.45f * Mathf.Cos((played - ClickAt("rebind")) * Mathf.PI * 3f));

                var key = bound ? "F" : waiting ? "?" : "Space";
                if (bigLabel.Text != key) bigLabel.Text = key;
                var push = Mathf.Sin(Mathf.PI * Mathf.Clamp01((played - pressed) / KeyPress));
                bigKeyDepth.Offset = new Vector2(0f, -KeyDepth * (1f - 0.7f * push));
                bigKey.Rect.anchoredPosition = bigKeyAt - new Vector2(0f, KeyDepth * 0.7f * push);
            }

            private static readonly (string name, string value)[] Video =
            {
                ("Resolution", "2560 × 1440"), ("Frame rate", "120 fps"), ("HDR", "On"), ("Upscaling", "Quality")
            };

            private static readonly (string action, string[] keys)[] Bindings =
            {
                ("Move", new[] { "W", "A", "S", "D" }), ("Jump", new[] { "Space" }), ("Dash", new[] { "Shift" }),
                ("Ability", new[] { "Q" }), ("Map", new[] { "M" }), ("Inventory", new[] { "I" })
            };

            private const int Rebound = 1;
            private static readonly Vector2 TabsAt = new(Left + 120f, Top - 74f);
            private const float TabStep = 77.3f;
            private static readonly Vector2 PageAt = new(Left + 300f, -40f);
            private static readonly Vector2 PageSize = new(600f, 340f);
            private static readonly Vector2 KeyCardAt = new(-Left - 210f, -40f);
            private static readonly Vector2 KeyCardSize = new(400f, 340f);
            private const float RowLeft = Left + 34f;
            private const float ValueLeft = Left + 300f;
            private const float RowStep = 50f;
            private const float CapHeight = 42f;
            private const float CapLetter = 11f;
            private const float CapGap = 8f;
            private const float ReboundWidth = 150f;
            private const float BigKey = 190f;
            private const float KeyDepth = 18f;
            private const float KeyPress = 0.22f;
            private const float PageSwap = 0.3f;
            private const float RingGrow = 0.3f;
            private const float PageSlide = 40f;
            private const float Pace = 1.2f;
        }

        /// <summary>
        /// The inventory: a grid of the hero's items filtered by kind, and the chosen one's card — its rarity, its stats
        /// and the button that equips it.
        /// </summary>
        private sealed class InventoryPart : ScreenPart
        {
            public InventoryPart(GuiScreensScene scene, Stage stage) : base(scene, stage, "Inventory", "Inventory")
            {
                var filter = Add("Segmented Control", new Vector2(Left + 120f, Top - 74f), 0.5f);
                Kit.Relabel(filter, "Option 1", "All");
                Kit.Relabel(filter, "Option 2", "Weapons");
                Kit.Relabel(filter, "Option 3", "Potions");
                kind = filter.GetComponent<UniShapeValue>();

                for (var i = 0; i < Slots.Length; i++)
                {
                    var (icon, weapon, count) = Slots[i];
                    var at = Slot(i);
                    var slot = Add("Button", at);
                    var rect = (RectTransform)slot.transform;
                    Size(slot, Vector2.one * SlotSize);
                    Kit.Relabel(slot, "Button", string.Empty);
                    icon(items, rect, SlotSize * ItemShare);
                    if (count > 1)
                    {
                        var badge = Add("Badge", at + new Vector2(SlotSize * 0.5f - 8f, SlotSize * 0.5f - 8f));
                        Kit.Relabel(badge, "3", count.ToString());
                    }

                    var group = slot.TryGetComponent<CanvasGroup>(out var own) ? own : slot.AddComponent<CanvasGroup>();
                    slots[i] = (group, weapon);
                }

                highlight = Ring("Chosen", slots[0].group.gameObject, Slot(0), Vector2.one * SlotSize);

                var details = new Vector2(-Left - 220f, -25f);
                Card("Details", details, new Vector2(440f, 390f), 18f);
                Art("Blade Art", details + new Vector2(0f, 108f), new Vector2(400f, 140f), Plum, Night,
                    (node, size) => G.Sword(items, node, Vector2.zero, size, Ember, Gold, -90f), 1.6f);
                Label("Ember Blade", details + new Vector2(-200f, 12f), 24f, "Ink", display: true);
                rating = Add("Rating", details + new Vector2(-200f, -20f), 0f).GetComponent<UniRating>();
                attack = Stat("Attack", details + new Vector2(-200f, -60f), "Progress Bar");
                speed = Stat("Speed", details + new Vector2(-200f, -94f), "Segmented Progress");
                equip = Add("Button", details + new Vector2(0f, -158f));
                Size(equip, new Vector2(400f, 44f));
                equipLabel = equip.GetComponentInChildren<UniText>(true);

                Plan(At(new Vector2(Left + 200f, Bottom - 30f)), new[]
                {
                    Beat.To(At(new Vector2(Left + 120f, Top - 74f) + new Vector2(0f, 0f)), null, Wide(77f)),
                    Beat.Click("weapons", true),
                    Beat.To(At(Slot(ChosenSlot)), null, Wide(SlotSize)),
                    Beat.Click("choose", true),
                    Beat.To(At(details + new Vector2(0f, -158f)), null, Wide(400f)),
                    Beat.Click("equip", true, true),
                    Beat.Wait(0.4f)
                }, Pace);
                Aim("weapons", Captioned(filter, "Weapons"));
                Aim("choose", slots[ChosenSlot].group);
                Aim("equip", equip);
            }

            private readonly UniShapeValue kind;
            private readonly (CanvasGroup group, bool weapon)[] slots = new (CanvasGroup, bool)[Slots.Length];
            private readonly UniRating rating;
            private readonly UniShapeValue attack;
            private readonly UniShapeValue speed;
            private readonly Widget highlight;
            private readonly GameObject equip;
            private readonly UniText equipLabel;

            /// <summary>A stat's name and the bar of the kit's <paramref name="control"/> its value shows on.</summary>
            private UniShapeValue Stat(string name, Vector2 native, string control)
            {
                Label(name, native, 15f, "InkDim");
                return Add(control, native + new Vector2(110f, 0f), 0f).GetComponentInChildren<UniShapeValue>(true);
            }

            private static Vector2 Slot(int index) =>
                new(Left + SlotSize * 0.5f + (SlotSize + SlotGap) * (index % Columns), Top - 156f - (SlotSize + SlotGap) * (index / Columns));

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                rating.Value = 4;
                attack.Value = 0.82f;
                speed.Value = 0.6f;
                var weapons = played >= ClickAt("weapons");
                kind.Value = weapons ? 1 : 0;
                var dim = Ease.Emphasized.Window(played, ClickAt("weapons"), FilterFor);
                foreach (var (group, weapon) in slots) group.alpha = weapon ? 1f : Mathf.Lerp(1f, Dimmed, dim);

                var chosen = played >= ClickAt("choose") ? ChosenSlot : 0;
                var move = Ease.Emphasized.Window(played, ClickAt("choose"), MoveFor);
                highlight.Rect.anchoredPosition = Vector2.LerpUnclamped(Slot(0), Slot(ChosenSlot), chosen == 0 ? 0f : move);
                var caption = played >= ClickAt("equip") ? "Equipped" : "Equip";
                if (equipLabel.Text != caption) equipLabel.Text = caption;
            }

            /// <summary>The hero's items, each drawn into its slot <c>s</c> units across, whether it is a weapon, and how many there are.</summary>
            private static readonly (Action<Items, Transform, float> icon, bool weapon, int count)[] Slots =
            {
                ((k, p, s) => G.Sword(k, p, Vector2.zero, s, Steel, Bronze), true, 1),
                ((k, p, s) => G.Flask(k, p, Vector2.zero, s * 0.9f, Rose, Glass), false, 3),
                ((k, p, s) => G.Hammer(k, p, Vector2.zero, s, Steel, Wood), true, 1),
                ((k, p, s) => G.Helm(k, p, Vector2.zero, s, Steel, Rose), false, 1),
                ((k, p, s) => G.Sword(k, p, Vector2.zero, s, Ember, Gold), true, 1),
                ((k, p, s) => G.Flask(k, p, Vector2.zero, s * 0.9f, Teal, Glass), false, 5),
                ((k, p, s) => G.Wand(k, p, Vector2.zero, s, Wood, Gold), true, 1),
                ((k, p, s) => G.Amulet(k, p, Vector2.zero, s, Gold, Teal), false, 1),
                ((k, p, s) => G.Shield(k, p, Vector2.zero, s * 0.9f, Teal), false, 1),
                ((k, p, s) => G.Boots(k, p, Vector2.zero, s, Wood, Bronze), false, 1),
                ((k, p, s) => G.Bomb(k, p, Vector2.zero, s * 0.9f, Night, Gold), false, 2),
                ((k, p, s) => G.Chestplate(k, p, Vector2.zero, s, Steel, Gold), false, 1)
            };

            /// <summary>How much of a slot its item fills.</summary>
            private const float ItemShare = 0.62f;

            private const int Columns = 4;
            private const float SlotSize = 108f;
            private const float SlotGap = 14f;
            private const int ChosenSlot = 4;
            private const float Dimmed = 0.22f;
            private const float FilterFor = 0.35f;
            private const float MoveFor = 0.3f;
            private const float Pace = 1.2f;
        }

        /// <summary>
        /// The shop: three offers, the best value of them marked and ending soon; buying it asks the kit's dialog to
        /// confirm the price, and a toast confirms the purchase. The section dives into the pack bought.
        /// </summary>
        private sealed class ShopPart : ScreenPart
        {
            public ShopPart(GuiScreensScene scene, Stage stage) : base(scene, stage, "Shop", "Shop")
            {
                for (var i = 0; i < Offers.Length; i++)
                {
                    var (title, price, icon, from, to) = Offers[i];
                    var at = OfferAt(i);
                    Card("Offer", at, new Vector2(OfferWidth, 390f), 18f);
                    var art = Art("Offer Art", at + new Vector2(0f, 82f), new Vector2(OfferWidth - 36f, 200f), from, to,
                        (node, size) => icon(items, node, size));
                    if (i == Best) pack = art.Rect;
                    Label(title, at + new Vector2(-OfferWidth * 0.5f + 20f, -40f), 22f, "Ink", display: true);
                    Label(price, at + new Vector2(-OfferWidth * 0.5f + 20f, -68f), 16f, "InkDim");
                    var buy = Add("Button", at + new Vector2(0f, -160f));
                    Size(buy, new Vector2(OfferWidth - 40f, 44f));
                    Kit.Relabel(buy, "Button", "Buy");
                    if (i == Best) this.buy = buy;
                }

                var chip = Add("Chip", OfferAt(Best) + new Vector2(0f, 196f));
                Kit.Relabel(chip, "Chip", "Best value");
                best = chip.GetComponent<UniToggle>();
                timer = Label(string.Empty, OfferAt(Best) + new Vector2(-OfferWidth * 0.5f + 20f, -98f), 15f, "Accent");

                var asking = Add("Dialogs", Vector2.zero);
                var rect = (RectTransform)asking.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                dialogs = asking.GetComponent<UniMessages>();
                toasts = Add("Toasts", new Vector2(0f, Bottom + 40f)).GetComponentInChildren<UniMessages>(true);
                veil = Veil();
                veil.transform.SetSiblingIndex(rect.GetSiblingIndex());

                Plan(At(new Vector2(OfferAt(0).x, Bottom - 30f)), new[]
                {
                    Beat.To(At(OfferAt(0) + new Vector2(0f, 82f)), null, Wide(OfferWidth)),
                    Beat.Wait(0.25f),
                    Beat.To(At(OfferAt(Best) + new Vector2(0f, -160f)), null, Wide(OfferWidth)),
                    Beat.Click("buy", true),
                    Beat.To(At(new Vector2(ConfirmX, ConfirmY)), null, Wide(100f)),
                    Beat.Click("confirm", true, true),
                    Beat.Wait(ToastRead)
                }, Pace);
                Aim("buy", this.buy);
                Aim("confirm", Captioned(asking, "Buy"));
            }

            private readonly RectTransform pack;
            private readonly GameObject buy;
            private readonly UniToggle best;
            private readonly UniText timer;
            private readonly UniMessages dialogs;
            private readonly UniMessages toasts;
            private readonly UniShape veil;
            private bool asked;
            private bool answered;
            private bool toasted;

            private static Vector2 OfferAt(int index) => new((index - 1) * (OfferWidth + OfferGap), -30f);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (!best.On) best.On = true;
                var left = Mathf.Max(0f, TimerStart - Mathf.Max(0f, played));
                var seconds = Mathf.FloorToInt(left);
                timer.Text = $"Ends in 02:14:{seconds % 60:00}";

                if (played >= ClickAt("buy") && !asked)
                {
                    asked = true;
                    _ = dialogs.Confirm("Buy the Ember Pack?", "860 gems will be spent.", "Buy");
                }

                if (played >= ClickAt("confirm") && !answered)
                {
                    answered = true;
                    Click(Action(dialogs, "Buy").gameObject);
                }

                var bought = played >= ClickAt("confirm");
                if (bought && !toasted)
                    toasts.Show(new MessageRequest { Template = "Success", Title = "Purchased", Text = "Ember Pack is in your inventory", Timeout = ToastRead });
                if (!bought && toasted) toasts.Clear();
                toasted = bought;
                var open = 0f;
                foreach (var surface in dialogs.GetComponentsInChildren<Surface>(true)) open = Mathf.Max(open, surface.Openness);
                Stage.Alpha(veil, open);

                Dive(played, At(pack.anchoredPosition));
            }

            private static UniButton Action(UniMessages presenter, string caption)
            {
                foreach (var button in presenter.GetComponentsInChildren<UniButton>())
                    if (button.GetComponentInChildren<UniText>(true).Text == caption)
                        return button;
                throw new InvalidOperationException($"[Promo] '{presenter.name}' shows no '{caption}' action.");
            }

            private static readonly (string title, string price, Func<Items, Transform, float, RectTransform> icon, Color from, Color to)[] Offers =
            {
                ("Starter Kit", "120 gems", (k, p, s) => G.Gift(k, p, Vector2.zero, s * 0.8f, Teal, Gold), Teal, Night),
                ("Ember Pack", "860 gems", (k, p, s) => G.Flame(k, p, Vector2.zero, s * 0.86f, Gold, Color.white), Ember, Rose),
                ("Royal Chest", "2,400 gems", (k, p, s) => G.Chest(k, p, Vector2.zero, s * 0.8f, Wood, Gold), Gold, Plum)
            };

            private const int Best = 1;
            private const float OfferWidth = 310f;
            private const float OfferGap = 26f;
            private const float ConfirmX = 105f;
            private const float ConfirmY = -33f;
            private const float TimerStart = 47f;
            private const float ToastRead = 1.3f;
            private const float Pace = 1.2f;
        }
    }
}
