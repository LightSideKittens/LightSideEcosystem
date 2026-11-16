using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// A hero's loadout: the kit's tutorial spotlights the weapon slot, the click opens the kit's radial menu round it,
        /// the pointer sweeps the wheel and picks the bow, and the kit's radar chart flows from the sword's stats to the
        /// bow's while the changes float up beside it; the spotlight then moves on to the save button.
        /// </summary>
        private sealed class LoadoutPart : KitPart
        {
            public LoadoutPart(GuiControlsScene scene, Stage stage) : base(scene, stage, "Loadout", Board, Scale)
            {
                this.scene = scene;
                this.stage = stage;
                Label("Loadout", new Vector2(Left, Top - 18f), 28f, "Ink", display: true);
                Label("Aldric · Ranger · Level 34", new Vector2(Left, Top - 50f), 16f, "InkDim");

                Card(stage, scene, StatsAt);
                weaponName = Label(Gear[Equipped].name, new Vector2(StatsAt.x - CardSize.x * 0.5f + 24f, CardTop), 20f, "Ink", display: true);
                Label("Stats", new Vector2(StatsAt.x + CardSize.x * 0.5f - 24f, CardTop), 15f, "InkDim", 1f);
                var radar = Add("Radar Chart", StatsAt + new Vector2(0f, -22f));
                ((RectTransform)radar.transform).sizeDelta = Vector2.one * RadarSize;
                stats = radar.GetComponentInChildren<UniPathChart>(true).Series[0].Values;
                for (var i = 0; i < gains.Length; i++) gains[i] = Gain(stage, scene, Changes[i].text, Changes[i].up);

                Card(stage, scene, GearAt);
                Label("Gear", new Vector2(GearAt.x - CardSize.x * 0.5f + 24f, CardTop), 20f, "Ink", display: true);
                weapon = Slot(WeaponAt, WeaponSize, scene.Item(Gear[Equipped].sprite), out weaponIcon);
                Label("Weapon", WeaponAt + new Vector2(0f, -WeaponSize * 0.5f - 18f), 15f, "InkDim", 0.5f);
                Slot(OffhandAt, OffhandSize, scene.Item("Shield"), out _);
                Label("Off-hand", OffhandAt + new Vector2(0f, -OffhandSize * 0.5f - 18f), 15f, "InkDim", 0.5f);
                save = Add("Button", SaveAt);
                Size(save, SaveSize);
                Kit.Relabel(save, "Button", "Save loadout");
                saveLabel = save.GetComponentInChildren<UniText>(true);

                var menu = Add("Radial Menu", WeaponAt);
                ((RectTransform)menu.transform).sizeDelta = Vector2.one * WheelBox;
                wheel = menu.GetComponent<Surface>();
                ring = menu.GetComponentInChildren<UniRadialMenu>(true);
                items = menu.GetComponentsInChildren<UniButton>(true);
                for (var i = 0; i < Gear.Length; i++)
                {
                    items[i].GetComponentInChildren<UniText>(true).gameObject.SetActive(false);
                    Icon(items[i].transform, scene.Item(Gear[i].sprite), WheelIcon);
                }

                var guide = Add("Tutorial", Vector2.zero);
                var guideRect = (RectTransform)guide.transform;
                guideRect.anchorMin = Vector2.zero;
                guideRect.anchorMax = Vector2.one;
                guideRect.offsetMin = Vector2.zero;
                guideRect.offsetMax = Vector2.zero;
                tutorial = guide.GetComponent<UniTutorial>();

                var beats = new System.Collections.Generic.List<Beat>
                {
                    Beat.Wait(GuideRead),
                    Beat.To(At(WeaponAt), null, Wide(WeaponSize)),
                    Beat.Click("slot", true)
                };
                foreach (var index in Sweep) beats.Add(Beat.To(At(WeaponAt + Slice(index) * SweepReach), null, Wide(WheelIcon)));
                beats.Add(Beat.Click("pick", true, true));
                beats.Add(Beat.Wait(MorphFor + GainRead));
                beats.Add(Beat.To(At(SaveAt), null, Wide(SaveSize.x)));
                beats.Add(Beat.Click("save", true));
                beats.Add(Beat.Wait(SavedRead));
                Plan(At(new Vector2(StatsAt.x + 60f, Bottom - 30f)), beats.ToArray(), Pace);
                Aim("slot", weapon);
                Aim("pick", items[Picked]);
                Aim("save", save);
            }

            private readonly GuiControlsScene scene;
            private readonly Stage stage;
            private readonly UniButton[] items;
            private readonly UniText weaponName;
            private readonly StateList<float> stats;
            private readonly (RectTransform rect, CanvasGroup group)[] gains = new (RectTransform, CanvasGroup)[Changes.Length];
            private readonly GameObject weapon;
            private readonly UniShape weaponIcon;
            private readonly GameObject save;
            private readonly UniText saveLabel;
            private readonly Surface wheel;
            private readonly UniRadialMenu ring;
            private readonly UniTutorial tutorial;
            private readonly float[] posed = new float[StatCount];
            private int guided;

            /// <summary>Where on the wheel's ring slice <paramref name="index"/> stands, as a direction: clockwise from the top.</summary>
            private static Vector2 Slice(int index)
            {
                var angle = (90f - index * 360f / Gear.Length) * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            private void Card(Stage stage, GuiControlsScene scene, Vector2 at)
            {
                var card = stage.Shape("Card", Holder, ShapeKind.RoundedRect, 22f);
                stage.Box(card.Rect, at, CardSize);
                Stage.Solid(card.Fill, scene.palette["Raised"]);
                Stage.AddStroke(card.Shape, scene.palette["Line"], 1f, -1f);
            }

            /// <summary>A gear slot: the kit's button, square, holding the item's icon.</summary>
            private GameObject Slot(Vector2 at, float size, Sprite item, out UniShape icon)
            {
                var slot = Add("Button", at);
                Size(slot, Vector2.one * size);
                Kit.Relabel(slot, "Button", string.Empty);
                icon = Icon(slot.transform, item, size * 0.66f);
                return slot;
            }

            /// <summary>An item's icon painted from its sprite inside <paramref name="parent"/>, <paramref name="size"/> across.</summary>
            private UniShape Icon(Transform parent, Sprite item, float size)
            {
                var icon = stage.Shape("Icon", parent, ShapeKind.RoundedRect, 0f);
                stage.Box(icon.Rect, Vector2.zero, Vector2.one * size);
                icon.Rect.gameObject.AddComponent<UniLayout>().IgnoreLayout = true;
                Paint(icon.Shape, item);
                icon.Shape.RaycastTarget = false;
                return icon.Shape;
            }

            private static void Paint(UniShape icon, Sprite item)
            {
                var paint = Stage.FillOf(icon);
                paint.Kind = PaintSourceKind.Sprite;
                paint.Sprite = item;
                paint.Fit = PaintFit.Contain;
            }

            /// <summary>A change to a stat in a pill, green when it rises and red when it falls, floated up as the stats flow.</summary>
            private (RectTransform rect, CanvasGroup group) Gain(Stage stage, GuiControlsScene scene, string text, bool up)
            {
                var pill = stage.Shape("Gain", Holder, ShapeKind.Capsule);
                stage.Box(pill.Rect, Vector2.zero, new Vector2(GainWidth, GainHeight));
                var tone = up ? scene.palette["Success"] : scene.palette["Danger"];
                Stage.Solid(pill.Fill, Theme.Fade(tone, 0.2f));
                Stage.AddStroke(pill.Shape, tone, 1.5f, -1f);
                var label = stage.Label(pill.Rect, text, 17f, tone, stretch: true, face: scene.theme.DisplayFace);
                label.WordWrap = false;
                return (pill.Rect, pill.Rect.gameObject.AddComponent<CanvasGroup>());
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var opened = ClickAt("slot");
                var picked = ClickAt("pick");
                var open = played >= opened && played < picked;
                if (wheel.IsOpen != open)
                {
                    if (open) wheel.Open();
                    else wheel.Close();
                }

                if (open)
                {
                    var tip = Native(Pointer.Timeline.Sample(played).Position) - WeaponAt;
                    if (tip.magnitude > ring.DeadZone) ring.Aim(tip);
                }

                var equipped = played >= picked ? Picked : Equipped;
                var sprite = scene.Item(Gear[equipped].sprite);
                if (Stage.FillOf(weaponIcon).Sprite != sprite) Paint(weaponIcon, sprite);
                if (weaponName.Text != Gear[equipped].name) weaponName.Text = Gear[equipped].name;

                var flow = Ease.Emphasized.Window(played, picked + MorphLead, MorphFor);
                for (var i = 0; i < StatCount; i++) posed[i] = Mathf.Lerp(SwordStats[i], BowStats[i], flow);
                if (!Same(stats, posed)) stats.ReplaceAll(posed);

                for (var i = 0; i < gains.Length; i++)
                {
                    var since = played - picked - MorphLead - i * GainStagger;
                    var (rect, group) = gains[i];
                    Stage.Appear(rect, since, Motion.Back, GainGrow);
                    if (since < 0f) continue;
                    rect.anchoredPosition = StatsAt + Changes[i].at + Vector2.up * (GainRise * Ease.Emphasized.Evaluate(Mathf.Clamp01(since / (GainRead + MorphFor))));
                    group.alpha *= 1f - Mathf.SmoothStep(0f, 1f, (since - GainRead - MorphFor + GainFade) / GainFade);
                }

                var saved = played >= ClickAt("save");
                var caption = saved ? "Saved" : "Save loadout";
                if (saveLabel.Text != caption) saveLabel.Text = caption;

                var step = played < opened ? 1 : played >= picked + MorphLead + MorphFor + GainRead && !saved ? 2 : 0;
                if (step == guided) return;
                guided = step;
                if (step == 0) tutorial.End();
                else if (step == 1) tutorial.Show((RectTransform)weapon.transform, "Tap a slot to change your gear.");
                else tutorial.Show((RectTransform)save.transform, "Save your new loadout.");
            }

            private static bool Same(StateList<float> list, float[] values)
            {
                if (list.Count != values.Length) return false;
                for (var i = 0; i < values.Length; i++)
                    if (!Mathf.Approximately(list[i], values[i])) return false;
                return true;
            }

            /// <summary>The wheel's items in their order round it: what each is called and the sprite of the item sheet it shows.</summary>
            private static readonly (string name, string sprite)[] Gear =
            {
                ("Iron Sword", "Sword"), ("War Axe", "Axe"), ("Hunter's Bow", "Bow"), ("Oak Staff", "Staff"),
                ("Tower Shield", "Shield"), ("Old Ring", "Ring"), ("Health Potion", "HealthPotion"), ("Fire Scroll", "Scroll")
            };

            private const int Equipped = 0;
            private const int Picked = 2;

            /// <summary>The slices the pointer sweeps on its way to the bow, in order.</summary>
            private static readonly int[] Sweep = { 7, 0, 1, 2, 3, 2 };

            /// <summary>The stats the radar shows, Speed, Power, Range, Armor and Agility, with each weapon.</summary>
            private static readonly float[] SwordStats = { 7f, 7f, 2f, 7f, 6f };

            private static readonly float[] BowStats = { 8f, 5f, 9f, 4f, 8f };
            private const int StatCount = 5;

            /// <summary>The changes floated up beside the radar once the bow is picked, where each starts from the radar's middle.</summary>
            private static readonly (string text, bool up, Vector2 at)[] Changes =
            {
                ("+7 Range", true, new Vector2(118f, 36f)), ("+2 Agility", true, new Vector2(128f, -48f)),
                ("−3 Armor", false, new Vector2(-122f, -70f))
            };

            private static readonly Vector2 Board = new(1640f, 800f);
            private const float Scale = 1.7f;
            private const float Pad = 28f;
            private const float Left = -1640f / Scale * 0.5f + Pad;
            private const float Top = 800f / Scale * 0.5f - Pad;
            private const float Bottom = -Top;
            private static readonly Vector2 CardSize = new(430f, 360f);
            private static readonly Vector2 StatsAt = new(-232f, -40f);
            private static readonly Vector2 GearAt = new(232f, -40f);
            private const float CardTop = -40f + 180f - 30f;
            private const float RadarSize = 290f;
            private static readonly Vector2 WeaponAt = new(160f, 2f);
            private const float WeaponSize = 132f;
            private static readonly Vector2 OffhandAt = new(328f, 20f);
            private const float OffhandSize = 96f;
            private static readonly Vector2 SaveAt = new(232f, -166f);
            private static readonly Vector2 SaveSize = new(370f, 52f);
            private const float WheelBox = 360f;
            private const float WheelIcon = 42f;

            /// <summary>How far from the wheel's middle the pointer sweeps: over the slices' ring.</summary>
            private const float SweepReach = 124f;

            private const float GainWidth = 132f;
            private const float GainHeight = 36f;
            private const float GuideRead = 0.6f;
            private const float MorphLead = 0.15f;
            private const float MorphFor = 0.7f;
            private const float GainStagger = 0.12f;
            private const float GainGrow = 0.35f;
            private const float GainRise = 26f;
            private const float GainRead = 0.6f;
            private const float GainFade = 0.3f;
            private const float SavedRead = 0.3f;
            private const float Pace = 1.2f;
        }
    }
}
