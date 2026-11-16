using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The sci-fi game: a starfighter's hangar — the ship's status in segmented gauges, its hologram on the pad with
        /// callouts to the four fitted modules, the armory's weapons, and the next mission. The pointer picks a heavier
        /// weapon, equips it — the weapon callout, the firepower gauge and the power rating follow — and launches.
        /// </summary>
        private sealed class Hangar : Product
        {
            public Hangar(Builder k) : base(k)
            {
            }

            private readonly RectTransform[][] lit = new RectTransform[Stats.Length][];
            private readonly RectTransform[] ghost = new RectTransform[Segments_];
            private UniText firepower;
            private UniText power;
            private UniText gain;
            private UniText weaponName;
            private UniText weaponTier;
            private RectTransform weaponBox;
            private RectTransform cannonIcon;
            private RectTransform lanceIcon;
            private RectTransform selection;
            private readonly RectTransform[] equippedTags = new RectTransform[2];
            private Button equip;
            private Button launch;
            private RectTransform launchFill;
            private RectTransform ship;
            private readonly RectTransform[] engines = new RectTransform[2];
            private RectTransform pad;

            public override void Build(RectTransform screen)
            {
                Header(screen);
                Status(screen);
                Hologram(screen);
                Armory(screen);
                Mission(screen);
            }

            private void Header(RectTransform screen)
            {
                K.Heading(screen, "Hangar", 36f, new Vector2(-770f, 404f), 300f);
                K.Caption(screen, "Bay 03 · Cmdr. Vega", 24f, L.TextDim, new Vector2(-560f, 402f), 360f);
                var credits = K.Pill(screen, new Vector2(120f, 404f), 250f, "48,210", false);
                Glyphs.Credit(K, (RectTransform)credits.rectTransform.parent, Builder.PillIcon(250f), 38f, L.Gold);
                var alloy = K.Pill(screen, new Vector2(386f, 404f), 230f, "1,240", false);
                Glyphs.Cube(K, (RectTransform)alloy.rectTransform.parent, Builder.PillIcon(230f), 38f, L.Silver);
                var energy = K.Pill(screen, new Vector2(626f, 404f), 200f, "86%", false);
                Glyphs.Bolt(K, (RectTransform)energy.rectTransform.parent, Builder.PillIcon(200f), 38f, L.Accent.Light);
            }

            /// <summary>The ship's status: its name and class, a segmented gauge for each stat, and its power rating.</summary>
            private void Status(RectTransform screen)
            {
                var panel = K.Panel(screen, "Status", new Vector2(-SideX, PanelY), SideSize);
                var p = panel.Rect;
                var top = SideSize.y * 0.5f;
                K.Heading(p, "Ship status", 26f, new Vector2(-SideSize.x * 0.5f + 24f, top - 34f), 320f);
                K.Figure(p, "VX-7 Arrowhead", 30f, L.Text, new Vector2(-SideSize.x * 0.5f + 24f, top - 92f), 340f);
                K.Caption(p, "Interceptor · Tier 4", 22f, L.TextDim, new Vector2(-SideSize.x * 0.5f + 24f, top - 128f), 340f);
                for (var i = 0; i < Stats.Length; i++)
                {
                    var (name, value, max) = Stats[i];
                    var y = top - 210f - i * 104f;
                    K.Caption(p, name, 22f, L.TextDim, new Vector2(-SideSize.x * 0.5f + 24f, y), 200f);
                    var figure = K.Figure(p, Grouped(value), 28f, L.Text, new Vector2(SideSize.x * 0.5f - 24f, y), 160f, HAlign.Right);
                    if (i == FirepowerIndex) firepower = figure;
                    lit[i] = new RectTransform[Segments_];
                    var count = Lit(value, max);
                    for (var s = 0; s < Segments_; s++)
                    {
                        var at = new Vector2(-SideSize.x * 0.5f + 24f + SegmentWidth * 0.5f + s * (SegmentWidth + SegmentGap), y - 38f);
                        K.Fill(p, "Segment", ShapeKind.RoundedRect, at, new Vector2(SegmentWidth, 14f), Theme.Fade(L.Line, 0.16f), 1.5f);
                        if (i == FirepowerIndex)
                        {
                            ghost[s] = K.Fill(p, "Gain", ShapeKind.RoundedRect, at, new Vector2(SegmentWidth, 14f), Theme.Fade(Hex(0x7CFFB2), 0.55f), 1.5f).Rect;
                            ghost[s].gameObject.SetActive(false);
                        }

                        var on = K.Fill(p, "Lit", ShapeKind.RoundedRect, at, new Vector2(SegmentWidth, 14f), i == FirepowerIndex ? L.Accent.Light : L.Primary.Light, 1.5f);
                        Stage.AddShadow(on.Shape, Theme.Fade(i == FirepowerIndex ? L.Accent.Light : L.Primary.Light, 0.6f), Vector2.zero, 6f);
                        lit[i][s] = on.Rect;
                        on.Rect.gameObject.SetActive(s < count);
                    }
                }

                K.Fill(p, "Rule", ShapeKind.RoundedRect, new Vector2(0f, -top + 116f), new Vector2(SideSize.x - 48f, 1.5f), Theme.Fade(L.Line, 0.5f), 0f);
                K.Caption(p, "Power rating", 22f, L.TextDim, new Vector2(-SideSize.x * 0.5f + 24f, -top + 84f), 300f);
                power = K.Figure(p, Grouped(PowerBefore), 48f, L.Text, new Vector2(-SideSize.x * 0.5f + 24f, -top + 40f), 260f);
                Glow(power, Theme.Fade(L.Line, 0.5f), 0.12f);
                gain = K.Figure(p, "+440", 28f, Hex(0x7CFFB2), new Vector2(SideSize.x * 0.5f - 24f, -top + 40f), 140f, HAlign.Right);
                gain.gameObject.SetActive(false);
            }

            private static int Lit(float value, float max) => Mathf.RoundToInt(value / max * Segments_);

            /// <summary>The ship's hologram over its pad, and a callout from each hardpoint to the module fitted there.</summary>
            private void Hologram(RectTransform screen)
            {
                var plane = K.Node(screen, "Pad", new Vector2(0f, PadY));
                plane.localScale = new Vector3(1f, 0.26f, 1f);
                pad = K.Node(plane, "Spin", Vector2.zero);
                foreach (var (size, thickness, start) in new[] { (560f, 0.04f, 0f), (420f, 0.06f, 90f), (300f, 0.05f, 200f) })
                {
                    var ring = K.Fill(pad, "Ring", ShapeKind.Arc, Vector2.zero, Vector2.one * size, Theme.Fade(L.Line, 0.55f), 0f);
                    ring.Outline.Start = start;
                    ring.Outline.End = start + 250f;
                    ring.Outline.Thickness = thickness;
                    Stage.AddShadow(ring.Shape, Theme.Fade(L.Line, 0.5f), Vector2.zero, 10f);
                }

                var beam = K.Shape(screen, "Beam", ShapeKind.Trapezoid, new Vector2(0f, PadY + 150f), new Vector2(460f, 300f), 0f);
                beam.Outline.Taper = -18f;
                Stage.Linear(beam.Fill, Theme.Fade(L.Line, 0f), Theme.Fade(L.Line, 0.14f), 270f);

                ship = K.Node(screen, "Ship", ShipAt);
                for (var i = 0; i < engines.Length; i++)
                {
                    var flame = Glyphs.Flame(K, ship, new Vector2((i * 2 - 1) * 26f, -ShipSize * 0.47f), 60f, Hex(0x5CC8FF, 0.9f), Hex(0xE6F8FF));
                    flame.localRotation = Quaternion.Euler(0f, 0f, 180f);
                    engines[i] = flame;
                }

                var hull = Glyphs.Ship(K, ship, Vector2.zero, ShipSize, Theme.Fade(L.Line, 0.22f), Theme.Fade(L.Text, 0.85f));
                var shape = hull.GetComponentInChildren<UniShape>();
                Stage.AddStroke(shape, L.Line, 3f, -1f);
                Stage.AddShadow(shape, Theme.Fade(L.Line, 0.7f), Vector2.zero, 22f, 2f);
                foreach (var y in new[] { 60f, -10f, -80f })
                    K.Fill(ship, "Panel Line", ShapeKind.RoundedRect, new Vector2(0f, y), new Vector2(ShipSize * 0.08f, 1.5f), Theme.Fade(L.Line, 0.7f), 0f);

                var callouts = new (Vector2 point, Vector2 box, string kind, string name, string tier)[]
                {
                    (new(-118f, -14f), new(-206f, 262f), "Weapon", "Pulse Cannon", "MK II"),
                    (new(0f, 70f), new(206f, 262f), "Shield", "Aegis Barrier", "MK III"),
                    (new(-40f, -128f), new(-206f, -222f), "Engine", "Ion Thrusters", "MK II"),
                    (new(118f, -14f), new(206f, -222f), "Utility", "Repair Drone", "MK I")
                };
                foreach (var (point, box, kind, name, tier) in callouts)
                {
                    var from = ShipAt + point;
                    var to = box + new Vector2(box.x < 0f ? 60f : -60f, box.y > 0f ? -BoxSize.y * 0.5f : BoxSize.y * 0.5f);
                    Line(screen, from, to);
                    var dot = K.Fill(screen, "Hardpoint", ShapeKind.Circle, from, Vector2.one * 14f, L.Accent.Light, 0f);
                    Stage.AddShadow(dot.Shape, Theme.Fade(L.Accent.Light, 0.8f), Vector2.zero, 8f);
                    var callout = K.Panel(screen, kind, box, BoxSize, 4f);
                    var c = callout.Rect;
                    var slot = K.Slot(c, new Vector2(-BoxSize.x * 0.5f + 44f, 0f), 64f, kind == "Shield" ? 2 : 1);
                    var icon = K.Node(slot.Rect, "Icon", Vector2.zero);
                    Module(icon, kind, 40f);
                    K.Caption(c, kind, 18f, L.TextDim, new Vector2(-BoxSize.x * 0.5f + 88f, 22f), 170f);
                    var label = K.Figure(c, name, 20f, L.Text, new Vector2(-BoxSize.x * 0.5f + 88f, -2f), 170f);
                    var mark = K.Caption(c, tier, 18f, L.Accent.Light, new Vector2(-BoxSize.x * 0.5f + 88f, -26f), 170f);
                    if (kind != "Weapon") continue;
                    cannonIcon = icon;
                    lanceIcon = K.Node(slot.Rect, "Lance", Vector2.zero);
                    Module(lanceIcon, "Lance", 40f);
                    lanceIcon.gameObject.SetActive(false);
                    weaponBox = c;
                    weaponName = label;
                    weaponTier = mark;
                }
            }

            /// <summary>A hairline from <paramref name="from"/> to <paramref name="to"/>.</summary>
            private void Line(RectTransform screen, Vector2 from, Vector2 to)
            {
                var line = K.Fill(screen, "Callout", ShapeKind.RoundedRect, (from + to) * 0.5f, new Vector2(Vector2.Distance(from, to), 2f), Theme.Fade(L.Line, 0.7f), 0f);
                line.Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg);
            }

            /// <summary>The icon of a module of <paramref name="kind"/>, <paramref name="s"/> across.</summary>
            private void Module(RectTransform parent, string kind, float s)
            {
                var color = L.Primary.Light;
                switch (kind)
                {
                    case "Weapon":
                    case "Cannon":
                        K.Fill(parent, "Barrel", ShapeKind.RoundedRect, new Vector2(0f, s * 0.12f), new Vector2(s * 0.2f, s * 0.7f), color, 2f);
                        K.Fill(parent, "Body", ShapeKind.RoundedRect, new Vector2(0f, -s * 0.22f), new Vector2(s * 0.5f, s * 0.34f), color, 3f);
                        break;
                    case "Lance":
                        K.Fill(parent, "Lance", ShapeKind.Rhombus, Vector2.zero, new Vector2(s * 0.3f, s), L.Accent.Light, 0f);
                        K.Fill(parent, "Core", ShapeKind.Rhombus, Vector2.zero, new Vector2(s * 0.12f, s * 0.6f), Color.white, 0f);
                        break;
                    case "Rail":
                        foreach (var x in new[] { -0.16f, 0.16f })
                            K.Fill(parent, "Rail", ShapeKind.RoundedRect, new Vector2(s * x, 0f), new Vector2(s * 0.1f, s * 0.86f), color, 2f);
                        foreach (var y in new[] { -0.2f, 0.12f })
                            K.Fill(parent, "Tie", ShapeKind.RoundedRect, new Vector2(0f, s * y), new Vector2(s * 0.5f, s * 0.08f), color, 1f);
                        break;
                    case "Swarm":
                        foreach (var (x, y) in new[] { (0f, 0.22f), (-0.24f, -0.18f), (0.24f, -0.18f) })
                            K.Fill(parent, "Missile", ShapeKind.Triangle, new Vector2(s * x, s * y), new Vector2(s * 0.26f, s * 0.36f), color, 2f);
                        break;
                    case "Shield":
                        Glyphs.Shield(K, parent, Vector2.zero, s * 0.9f, color);
                        break;
                    case "Engine":
                        Glyphs.Flame(K, parent, Vector2.zero, s * 0.9f, color, Color.white);
                        break;
                    default:
                        Glyphs.Plus(K, parent, Vector2.zero, s * 0.8f, color);
                        break;
                }
            }

            /// <summary>The armory's weapons, each with its tier, damage and the change it would make, and the equip button.</summary>
            private void Armory(RectTransform screen)
            {
                var panel = K.Panel(screen, "Armory", new Vector2(SideX, PanelY), SideSize);
                var p = panel.Rect;
                var top = SideSize.y * 0.5f;
                K.Heading(p, "Armory", 26f, new Vector2(-SideSize.x * 0.5f + 24f, top - 34f), 300f);
                var tabs = K.Segmented(p, new Vector2(0f, top - 92f), new[] { "Weapons", "Shields", "Engines" }, 108f, L.Primary, 48f, 15f);
                tabs.Set(0f, L.TextOnTone, L.TextDim);
                for (var i = 0; i < Modules.Length; i++)
                {
                    var (kind, name, tier, rarity, damage, change, locked) = Modules[i];
                    var card = K.Node(screen, name, CardAt(i));
                    K.Inset(card, "Plate", ShapeKind.RoundedRect, Vector2.zero, CardSize, 4f);
                    var slot = K.Slot(card, new Vector2(-CardSize.x * 0.5f + 48f, 0f), 72f, rarity);
                    Module(slot.Rect, kind, 46f);
                    K.Figure(card, name, 19f, locked ? L.TextDim : L.Text, new Vector2(-CardSize.x * 0.5f + 96f, 18f), 220f);
                    for (var s = 0; s < 4; s++)
                        K.Fill(card, "Tier", ShapeKind.RoundedRect, new Vector2(-CardSize.x * 0.5f + 104f + s * 18f, -20f), new Vector2(12f, 8f),
                            s < tier ? L.Rarity[rarity].Light : Theme.Fade(L.Line, 0.2f), 1f);
                    if (locked)
                    {
                        Glyphs.Lock(K, card, new Vector2(CardSize.x * 0.5f - 124f, -20f), 22f, L.TextDim);
                        K.Caption(card, "Rank 12", 18f, L.TextDim, new Vector2(CardSize.x * 0.5f - 18f, -20f), 100f, HAlign.Right);
                        continue;
                    }

                    K.Caption(card, $"DMG {damage}", 19f, L.Text, new Vector2(-CardSize.x * 0.5f + 176f, -20f), 120f);
                    if (change != 0)
                        K.Caption(card, change > 0 ? $"▲ {change}" : $"▼ {-change}", 19f, change > 0 ? Hex(0x7CFFB2) : L.Danger.Light,
                            new Vector2(CardSize.x * 0.5f - 18f, -20f), 100f, HAlign.Right);
                    if (i < equippedTags.Length)
                    {
                        var tag = K.Tag(card, new Vector2(CardSize.x * 0.5f - 56f, CardSize.y * 0.5f), "Equipped", L.Accent, 15f);
                        equippedTags[i] = (RectTransform)tag.rectTransform.parent;
                    }
                }

                selection = K.Ring(screen, CardAt(0), CardSize + new Vector2(6f, 6f), L.Line, 6f, 2.5f).Rect;
                selection.SetSiblingIndex(screen.Find(Modules[0].name).GetSiblingIndex());
                equip = K.Button(screen, "Equip", EquipAt, new Vector2(SideSize.x - 48f, 74f), L.Primary, 28f);
            }

            private void Mission(RectTransform screen)
            {
                var panel = K.Panel(screen, "Mission", MissionAt, MissionSize, 4f);
                var p = panel.Rect;
                K.Caption(p, "Next mission", 18f, L.TextDim, new Vector2(-MissionSize.x * 0.5f + 24f, 22f), 220f);
                K.Figure(p, "Sector 7 escort", 26f, L.Text, new Vector2(-MissionSize.x * 0.5f + 24f, -12f), 300f);
                for (var i = 0; i < 5; i++)
                    K.Fill(p, "Threat", ShapeKind.Triangle, new Vector2(MissionSize.x * 0.5f - 112f + i * 22f, 22f), new Vector2(17f, 15f),
                        i < 3 ? L.Danger.Light : Theme.Fade(L.Line, 0.25f), 0f);
                Glyphs.Credit(K, p, new Vector2(MissionSize.x * 0.5f - 126f, -12f), 26f, L.Gold);
                K.Figure(p, "+12,000", 22f, L.Gold, new Vector2(MissionSize.x * 0.5f - 108f, -12f), 120f);
                launch = K.Button(screen, "Launch", LaunchAt, LaunchSize, L.Accent, 28f);
                var fill = K.Fill(launch.Node, "Charge", ShapeKind.RoundedRect, new Vector2(-LaunchSize.x * 0.5f, 0f), LaunchSize, Theme.Fade(L.Accent.Light, 0.45f), L.Radius);
                fill.Rect.pivot = new Vector2(0f, 0.5f);
                fill.Rect.anchoredPosition = new Vector2(-LaunchSize.x * 0.5f, 0f);
                fill.Rect.SetSiblingIndex(1);
                launchFill = fill.Rect;
            }

            public override List<Beat> Script()
            {
                var beats = new List<Beat> { Beat.Wait(0.6f) };
                beats.AddRange(Tap(CardAt(1), CardSize.x, "module", 0.4f));
                beats.AddRange(Tap(EquipAt, SideSize.x - 48f, "equip", 0.7f));
                beats.AddRange(Tap(LaunchAt, LaunchSize.x, "launch", 1f, true));
                return beats;
            }

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                pad.localRotation = Quaternion.Euler(0f, 0f, t * 18f);
                var launched = c.Since("launch");
                var lift = launched < 0f ? 0f : Ease.EmphasizedIn.Evaluate(Mathf.Clamp01(launched / 1.2f));
                ship.anchoredPosition = ShipAt + new Vector2(0f, Mathf.Sin(t * 1.6f) * 6f + lift * 60f);
                for (var i = 0; i < engines.Length; i++)
                    engines[i].localScale = new Vector3(1f, 0.7f + 0.2f * Mathf.PerlinNoise(t * 9f, i) + lift * 1.2f, 1f);

                var picked = c.After("module", 0.28f);
                selection.anchoredPosition = Vector2.Lerp(CardAt(0), CardAt(1), picked);
                var equipped = c.Since("equip") - 0.08f;
                var swapped = equipped >= 0f;
                for (var s = 0; s < Segments_; s++)
                    Show(ghost[s], picked > 0.5f && !swapped && s >= Lit(Stats[FirepowerIndex].value, Stats[FirepowerIndex].max) &&
                                   s < Lit(FirepowerAfter, Stats[FirepowerIndex].max));
                equip.Press(c.Held("equip"));
                equip.Say(L.Cased(swapped ? "Equipped" : "Equip"));

                var grow = Ease.Emphasized.Window(t, c.At("equip") + 0.08f, 0.6f);
                var value = Mathf.Lerp(Stats[FirepowerIndex].value, FirepowerAfter, grow);
                Say(firepower, Grouped(Mathf.RoundToInt(value)));
                var count = Lit(value, Stats[FirepowerIndex].max);
                for (var s = 0; s < Segments_; s++) Show(lit[FirepowerIndex][s], s < count);
                Say(power, Grouped(Mathf.RoundToInt(Mathf.Lerp(PowerBefore, PowerBefore + 440, grow))));
                Show(gain, swapped);
                if (swapped) gain.rectTransform.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(equipped / 0.3f));
                Say(weaponName, L.Cased(swapped ? "Plasma Lance" : "Pulse Cannon"));
                Say(weaponTier, L.Cased(swapped ? "MK III" : "MK II"));
                Show(cannonIcon, !swapped);
                Show(lanceIcon, swapped);
                weaponBox.localScale = Vector3.one * (swapped ? 1f + 0.08f * Mathf.Exp(-equipped * 7f) * Mathf.Cos(equipped * 20f) : 1f);
                Show(equippedTags[0], !swapped);
                Show(equippedTags[1], swapped);

                launch.Press(c.Held("launch"));
                var charge = launched < 0f ? 0f : Mathf.Clamp01(launched / 0.9f);
                launchFill.sizeDelta = new Vector2(LaunchSize.x * charge, LaunchSize.y);
                Say(launch.Caption, launched < 0f ? L.Cased("Launch") : charge < 1f ? L.Cased("Launching") : L.Cased("Go"));
            }

            private static Vector2 CardAt(int index) => new(SideX, PanelY + SideSize.y * 0.5f - 196f - index * (CardSize.y + 14f));

            private static readonly (string name, int value, int max)[] Stats =
            {
                ("Hull", 2400, 3200), ("Shields", 1800, 3200), ("Thrust", 320, 400), ("Firepower", 640, 1000)
            };

            private static readonly (string kind, string name, int tier, int rarity, int damage, int change, bool locked)[] Modules =
            {
                ("Cannon", "Pulse Cannon", 2, 1, 140, 0, false), ("Lance", "Plasma Lance", 3, 2, 210, 70, false),
                ("Rail", "Rail Driver", 1, 0, 120, -20, false), ("Swarm", "Ion Swarm", 4, 3, 260, 0, true)
            };

            private const int FirepowerIndex = 3;
            private const int FirepowerAfter = 710;
            private const int PowerBefore = 7420;
            private const int Segments_ = 20;
            private const float SegmentWidth = 13.6f;
            private const float SegmentGap = 3f;
            private const float SideX = 590f;
            private const float PanelY = -24f;
            private static readonly Vector2 SideSize = new(380f, 728f);
            private static readonly Vector2 CardSize = new(340f, 104f);
            private static readonly Vector2 EquipAt = new(SideX, PanelY - 364f + 58f);
            private static readonly Vector2 BoxSize = new(270f, 88f);
            private static readonly Vector2 ShipAt = new(0f, 30f);
            private const float ShipSize = 330f;
            private const float PadY = -110f;
            private static readonly Vector2 MissionAt = new(-110f, -340f);
            private static readonly Vector2 MissionSize = new(500f, 96f);
            private static readonly Vector2 LaunchAt = new(264f, -340f);
            private static readonly Vector2 LaunchSize = new(220f, 96f);
        }
    }
}
