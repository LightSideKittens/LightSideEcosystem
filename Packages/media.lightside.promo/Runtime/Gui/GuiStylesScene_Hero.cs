using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The epic game: a fantasy hero's sheet — the attributes with points to spend and what they make of the hero, the
        /// hero's crest in the middle of the six pieces of gear worn, the talent tree and the chosen talent's card. The
        /// pointer spends two points on strength, learns the next talent and confirms.
        /// </summary>
        private sealed class HeroSheet : Product
        {
            public HeroSheet(Builder k) : base(k)
            {
            }

            private UniText strength;
            private UniText points;
            private UniText attack;
            private Button plus;
            private UniText talentPoints;
            private RectTransform learned;
            private RectTransform available;
            private RectTransform link;
            private UniText rank;
            private Button confirm;
            private RectTransform seal;

            public override void Build(RectTransform screen)
            {
                Header(screen);
                Attributes(screen);
                Crest(screen);
                Gear(screen);
                Talents(screen);
                K.Button(screen, "Reset", new Vector2(-116f, ButtonsY), new Vector2(210f, 78f), L.Secondary, 32f);
                confirm = K.Button(screen, "Confirm", ConfirmAt, new Vector2(210f, 78f), L.Primary, 32f);
                seal = K.Node(screen, "Seal", ConfirmAt + new Vector2(96f, 38f));
                var wax = K.Fill(seal, "Wax", ShapeKind.Star, Vector2.zero, Vector2.one * 66f, Hex(0xB02A2A), 0f);
                wax.Outline.StarPoints = 12;
                wax.Outline.StarSharpness = 0.12f;
                Stage.AddShadow(wax.Shape, Theme.Fade(Color.black, 0.4f), new Vector2(0f, -3f), 4f);
                Glyphs.Check(K, seal, Vector2.zero, 34f, L.Gold);
                seal.gameObject.SetActive(false);
            }

            private void Header(RectTransform screen)
            {
                var title = K.Figure(screen, "Character", 42f, Color.white, new Vector2(-770f, 404f), 340f);
                Ramp(title, Hex(0xFFF3C4), Hex(0xE0A93A));
                Drop(title, Theme.Fade(Color.black, 0.6f), new Vector2(0f, -0.06f));
                var tabs = K.Segmented(screen, new Vector2(-60f, 404f), new[] { "Hero", "Talents", "Bag", "Journal" }, 170f, L.Primary, 62f, 24f);
                tabs.Set(0f, L.TextOnTone, L.TextDim);
                var gold = K.Pill(screen, new Vector2(470f, 404f), 230f, "12,840");
                Glyphs.Coin(K, (RectTransform)gold.rectTransform.parent, Builder.PillIcon(230f) + new Vector2(-4f, 0f), 54f);
                var gems = K.Pill(screen, new Vector2(680f, 404f), 180f, "320");
                Glyphs.Gem(K, (RectTransform)gems.rectTransform.parent, Builder.PillIcon(180f), 50f);
            }

            /// <summary>The attributes: each with its icon, its value and a plus to spend a point on it, then what they make of the hero.</summary>
            private void Attributes(RectTransform screen)
            {
                var panel = K.Panel(screen, "Attributes", new Vector2(-SideX, PanelY), SideSize);
                var p = panel.Rect;
                var top = SideSize.y * 0.5f;
                K.Heading(p, "Attributes", 32f, new Vector2(-SideSize.x * 0.5f + 28f, top - 46f), 300f);
                var chip = K.Toned(p, "Points", ShapeKind.Circle, new Vector2(SideSize.x * 0.5f - 46f, top - 46f), Vector2.one * 52f, L.Accent, 0f);
                points = K.Toned(chip.Rect, "3", 28f, new Vector2(0f, 1f), 52f, L.Accent);
                K.Caption(p, "Points", 20f, L.TextDim, new Vector2(SideSize.x * 0.5f - 82f, top - 46f), 120f, HAlign.Right);
                for (var i = 0; i < Attribute.Length; i++)
                {
                    var (name, value) = Attribute[i];
                    var y = top - 130f - i * 78f;
                    K.Inset(p, "Row", ShapeKind.RoundedRect, new Vector2(0f, y), new Vector2(SideSize.x - 40f, 64f), 12f);
                    var icon = new Vector2(-SideSize.x * 0.5f + 52f, y);
                    switch (i)
                    {
                        case 0:
                            Glyphs.Sword(K, p, icon, 44f, Hex(0xE8E8F0), L.Gold);
                            break;
                        case 1:
                            Glyphs.Boots(K, p, icon, 40f, Hex(0x8A5A2E), L.Gold);
                            break;
                        case 2:
                            Glyphs.Flask(K, p, icon, 40f, Hex(0x4A7AE0), Hex(0xE8E0D0));
                            break;
                        default:
                            Glyphs.Heart(K, p, icon, 36f, Hex(0xC0392B));
                            break;
                    }

                    K.Figure(p, name, 26f, L.Text, new Vector2(-SideSize.x * 0.5f + 86f, y), 200f);
                    var figure = K.Figure(p, value.ToString(), 30f, L.Text, new Vector2(SideSize.x * 0.5f - 96f, y), 80f, HAlign.Right);
                    var button = K.Button(screen, "", new Vector2(-SideX, PanelY) + new Vector2(SideSize.x * 0.5f - 56f, y), Vector2.one * 46f, L.Accent, 20f, 23f);
                    Glyphs.Plus(K, button.Node, Vector2.zero, 24f, L.Accent.Ink);
                    if (i != 0) continue;
                    strength = figure;
                    plus = button;
                }

                Ornament(p, new Vector2(0f, top - 450f), SideSize.x - 80f);
                for (var i = 0; i < Derived.Length; i++)
                {
                    var y = top - 494f - i * 44f;
                    K.Caption(p, Derived[i].name, 24f, L.TextDim, new Vector2(-SideSize.x * 0.5f + 32f, y), 220f);
                    var value = K.Figure(p, Derived[i].value, 26f, L.Text, new Vector2(SideSize.x * 0.5f - 32f, y), 180f, HAlign.Right);
                    if (i == 0) attack = value;
                }
            }

            /// <summary>A gold rule with a diamond in its middle, dividing a panel.</summary>
            private void Ornament(RectTransform parent, Vector2 at, float width)
            {
                K.Fill(parent, "Rule", ShapeKind.RoundedRect, at, new Vector2(width, 2.5f), L.Line, 0f);
                var gem = K.Ramp(parent, "Diamond", ShapeKind.Rhombus, at, new Vector2(20f, 26f), Hex(0xFFE9A6), Hex(0xB07A22), 0f);
                Stage.AddStroke(gem.Shape, L.Outline, 2f, 1f);
            }

            /// <summary>The hero's crest: a shield under a crown over crossed swords in a gold ring, the hero's name on a ribbon below.</summary>
            private void Crest(RectTransform screen)
            {
                var c = K.Node(screen, "Crest", CrestAt);
                K.Halo(c, Vector2.zero, 520f, Hex(0xFFE6A0, 0.35f));
                var ring = K.Ramp(c, "Ring", ShapeKind.Circle, Vector2.zero, Vector2.one * 320f, Hex(0xFFE9A6), Hex(0xA8701E), 0f);
                Stage.AddStroke(ring.Shape, L.Outline, 4f, 1f);
                Stage.AddShadow(ring.Shape, Theme.Fade(Color.black, 0.45f), new Vector2(0f, -12f), 24f);
                var velvet = K.Ramp(c, "Velvet", ShapeKind.Circle, Vector2.zero, Vector2.one * 286f, Hex(0x3E6A50), Hex(0x10241A), 0f);
                Stage.AddInnerShadow(velvet.Shape, Theme.Fade(Color.black, 0.6f), new Vector2(0f, -6f), 18f);
                Stage.AddStroke(velvet.Shape, L.Outline, 3f, 1f);
                foreach (var turn in new[] { -40f, 40f })
                    Glyphs.Sword(K, c, new Vector2(0f, 10f), 260f, Hex(0xE8E8F0), L.Gold, turn);
                var shield = Glyphs.Shield(K, c, new Vector2(0f, -10f), 170f, Hex(0xB02A2A));
                Stage.AddStroke(shield.GetComponentInChildren<UniShape>(), L.Gold, 6f, -1f);
                Stage.AddStroke(shield.GetComponentInChildren<UniShape>(), L.Outline, 3f, 1f);
                foreach (var bar in new[] { new Vector2(20f, 112f), new Vector2(92f, 20f) })
                {
                    var arm = K.Ramp(c, "Cross", ShapeKind.RoundedRect, new Vector2(0f, bar.x > 50f ? 6f : -8f), bar, Hex(0xFFE9A6), Hex(0xB07A22), 4f);
                    Stage.AddStroke(arm.Shape, L.Outline, 2f, 1f);
                }
                var crown = Glyphs.Crown(K, c, new Vector2(0f, 104f), 96f, L.Gold);
                Glyphs.Ink(crown, L.Outline, 2.5f);

                K.Ribbon(screen, CrestAt + new Vector2(0f, -196f), 330f, 70f, L.Primary, "Aldric", 40f);
                var job = K.Caption(screen, "Paladin · Level 34", 26f, L.PanelHigh, CrestAt + new Vector2(0f, -252f), 400f, HAlign.Center);
                Drop(job, Theme.Fade(Color.black, 0.6f), new Vector2(0f, -0.06f));
                var power = K.Node(screen, "Power", CrestAt + new Vector2(0f, -296f));
                Glyphs.Sword(K, power, new Vector2(-82f, 0f), 36f, L.PanelHigh, L.Gold);
                var rating = K.Figure(power, "12,840", 36f, Color.white, new Vector2(-58f, 0f), 200f);
                Ramp(rating, Hex(0xFFF3C4), Hex(0xE0A93A));
                Drop(rating, Theme.Fade(Color.black, 0.6f), new Vector2(0f, -0.06f));
            }

            /// <summary>The six pieces of gear the hero wears, each framed by its rarity with its item level.</summary>
            private void Gear(RectTransform screen)
            {
                var pieces = new (int rarity, int level)[] { (3, 318), (2, 305), (3, 322), (2, 300), (1, 290), (2, 296) };
                for (var i = 0; i < 6; i++)
                {
                    var left = i < 3;
                    var at = new Vector2(left ? -GearX : GearX, GearTop - i % 3 * GearStep);
                    var slot = K.Slot(screen, at, GearSize, pieces[i].rarity);
                    var s = slot.Rect;
                    switch (i)
                    {
                        case 0:
                            Glyphs.Helm(K, s, Vector2.zero, 70f, Hex(0xD8D8E4), Hex(0xB02A2A));
                            break;
                        case 1:
                            Glyphs.Amulet(K, s, Vector2.zero, 70f, Hex(0xB07A22), Hex(0x3DBB7A));
                            break;
                        case 2:
                            Glyphs.Sword(K, s, Vector2.zero, 78f, Hex(0xE8E8F0), L.Gold);
                            break;
                        case 3:
                            Glyphs.Chestplate(K, s, Vector2.zero, 72f, Hex(0xC8C8D4), L.Gold);
                            break;
                        case 4:
                            Glyphs.Shield(K, s, Vector2.zero, 64f, Hex(0x2A5CB8));
                            break;
                        default:
                            Glyphs.Boots(K, s, Vector2.zero, 66f, Hex(0x8A5A2E), L.Gold);
                            break;
                    }

                    var tag = K.Fill(s, "Level", ShapeKind.RoundedRect, new Vector2(0f, -GearSize * 0.5f + 4f), new Vector2(56f, 26f), Theme.Fade(L.Outline, 0.85f), 8f);
                    K.Figure(tag.Rect, pieces[i].level.ToString(), 18f, L.Rarity[pieces[i].rarity].Light, new Vector2(0f, 1f), 56f, HAlign.Center);
                }
            }

            /// <summary>The talent tree in three tiers, the learned talents lit, and the card of the one that can be learned next.</summary>
            private void Talents(RectTransform screen)
            {
                var panel = K.Panel(screen, "Talents", new Vector2(SideX, PanelY), SideSize);
                var p = panel.Rect;
                var top = SideSize.y * 0.5f;
                K.Heading(p, "Talents", 32f, new Vector2(-SideSize.x * 0.5f + 28f, top - 46f), 300f);
                var chip = K.Toned(p, "Points", ShapeKind.Circle, new Vector2(SideSize.x * 0.5f - 46f, top - 46f), Vector2.one * 52f, L.Accent, 0f);
                talentPoints = K.Toned(chip.Rect, "1", 28f, new Vector2(0f, 1f), 52f, L.Accent);
                K.Caption(p, "Points", 20f, L.TextDim, new Vector2(SideSize.x * 0.5f - 82f, top - 46f), 120f, HAlign.Right);

                for (var tier = 0; tier < 2; tier++)
                for (var column = 0; column < 3; column++)
                {
                    var from = TalentAt(tier, column);
                    var to = TalentAt(tier + 1, column);
                    var lit = tier == 0 && column == 0;
                    K.Fill(p, "Link", ShapeKind.RoundedRect, (from + to) * 0.5f, new Vector2(6f, TalentStep), lit ? L.Gold : Theme.Fade(L.Outline, 0.25f), 0f);
                }

                link = K.Fill(p, "Lit Link", ShapeKind.RoundedRect, (TalentAt(0, 1) + TalentAt(1, 1)) * 0.5f, new Vector2(6f, TalentStep), L.Gold, 0f).Rect;
                Stage.AddShadow(link.GetComponent<UniShape>(), Theme.Fade(L.Gold, 0.8f), Vector2.zero, 8f);
                for (var tier = 0; tier < 3; tier++)
                for (var column = 0; column < 3; column++)
                {
                    var state = tier == 0 && column < 2 || tier == 1 && column == 0 ? 2 : tier == 1 && column == 1 ? 1 : 0;
                    var node = Talent(p, TalentAt(tier, column), tier * 3 + column, state);
                    if (tier == 1 && column == 1) available = node;
                }

                learned = Talent(p, TalentAt(1, 1), 4, 2);
                var card = K.Inset(p, "Card", ShapeKind.RoundedRect, new Vector2(0f, -top + 112f), new Vector2(SideSize.x - 40f, 180f), 14f);
                var c = card.Rect;
                K.Figure(c, "Holy Bulwark", 28f, L.Text, new Vector2(-SideSize.x * 0.5f + 44f, 56f), 260f);
                rank = K.Figure(c, "0/3", 24f, L.TextDim, new Vector2(SideSize.x * 0.5f - 44f, 56f), 80f, HAlign.Right);
                K.Paragraph(c, "Block 15% more damage while your shield is raised.", 22f, L.Text, new Vector2(-SideSize.x * 0.5f + 44f, 30f),
                    new Vector2(SideSize.x - 88f, 110f));
            }

            /// <summary>A talent's node: lit gold once learned, edged gold while it can be learned, dark while locked.</summary>
            private RectTransform Talent(RectTransform parent, Vector2 at, int icon, int state)
            {
                var node = K.Node(parent, "Talent", at);
                Widget face;
                if (state == 2)
                {
                    face = K.Ramp(node, "Face", ShapeKind.Hexagon, Vector2.zero, Vector2.one * TalentSize, Hex(0xFFE9A6), Hex(0xB07A22), 0f);
                    Stage.AddStroke(face.Shape, L.Outline, 3f, 1f);
                    Stage.AddShadow(face.Shape, Theme.Fade(L.Gold, 0.9f), Vector2.zero, 16f, 2f);
                }
                else
                {
                    face = K.Fill(node, "Face", ShapeKind.Hexagon, Vector2.zero, Vector2.one * TalentSize, state == 1 ? L.PanelHigh : Hex(0x5A4630), 0f);
                    Stage.AddStroke(face.Shape, state == 1 ? L.Gold : Theme.Fade(L.Outline, 0.6f), state == 1 ? 4f : 2f, -1f);
                    Stage.AddStroke(face.Shape, L.Outline, 2f, 1f);
                }

                var ink = state == 2 ? Hex(0x5A3410) : state == 1 ? L.Primary.Light : Hex(0x9A8460);
                switch (icon % 5)
                {
                    case 0:
                        Glyphs.Sword(K, node, Vector2.zero, 40f, ink, ink);
                        break;
                    case 1:
                        Glyphs.Heart(K, node, Vector2.zero, 32f, ink);
                        break;
                    case 2:
                        Glyphs.Flame(K, node, Vector2.zero, 36f, ink, ink);
                        break;
                    case 3:
                        Glyphs.Crown(K, node, Vector2.zero, 38f, ink);
                        break;
                    default:
                        Glyphs.Shield(K, node, Vector2.zero, 36f, ink);
                        break;
                }

                return node;
            }

            private static Vector2 TalentAt(int tier, int column) => new((column - 1) * 104f, SideSize.y * 0.5f - 150f - tier * TalentStep);

            public override List<Beat> Script()
            {
                var plusAt = new Vector2(-SideX, PanelY) + new Vector2(SideSize.x * 0.5f - 56f, SideSize.y * 0.5f - 130f);
                var beats = new List<Beat> { Beat.Wait(0.6f) };
                beats.AddRange(Tap(plusAt, 46f, "strength", 0.2f));
                beats.Add(Beat.Click("again", true));
                beats.Add(Beat.Wait(0.35f));
                beats.AddRange(Tap(new Vector2(SideX, PanelY) + TalentAt(1, 1), TalentSize, "talent", 0.5f));
                beats.AddRange(Tap(ConfirmAt, 210f, "confirm", 1f, true));
                return beats;
            }

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                var spent = (c.Past("strength") ? 1 : 0) + (c.Past("again") ? 1 : 0);
                Say(strength, (Attribute[0].value + spent).ToString());
                Say(points, (3 - spent).ToString());
                plus.Press(Mathf.Max(c.Held("strength"), c.Held("again")));
                var bump = Mathf.Max(Bump(c.Since("strength")), Bump(c.Since("again")));
                strength.rectTransform.localScale = Vector3.one * (1f + 0.25f * bump);
                var gain = Ease.Emphasized.Window(t, c.At("strength"), 0.3f) + Ease.Emphasized.Window(t, c.At("again"), 0.3f);
                Say(attack, Grouped(Mathf.RoundToInt(1240 + 28f * gain)));

                var since = c.Since("talent") - 0.06f;
                var isLearned = since >= 0f;
                Show(learned, isLearned);
                Show(link, isLearned);
                available.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(t * 5f));
                if (isLearned)
                {
                    learned.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(since / 0.35f));
                    link.localScale = new Vector3(1f, Mathf.Clamp01(since / 0.25f), 1f);
                }

                Say(talentPoints, isLearned ? "0" : "1");
                Say(rank, isLearned ? "1/3" : "0/3");
                rank.color = isLearned ? L.Primary.Dark : L.TextDim;

                confirm.Press(c.Held("confirm"));
                var saved = c.Since("confirm") - 0.1f;
                confirm.Say(L.Cased(saved >= 0f ? "Saved" : "Confirm"));
                Show(seal, saved >= 0f);
                if (saved >= 0f)
                {
                    seal.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, Ease.EmphasizedIn.Evaluate(Mathf.Clamp01(saved / 0.22f)));
                    seal.localRotation = Quaternion.Euler(0f, 0f, -14f);
                }
            }

            private static float Bump(float since) => since < 0f ? 0f : Mathf.Exp(-since * 8f);

            private static readonly (string name, int value)[] Attribute = { ("Strength", 42), ("Agility", 28), ("Intellect", 19), ("Vitality", 36) };

            private static readonly (string name, string value)[] Derived =
            {
                ("Attack", "1,240"), ("Armor", "860"), ("Critical", "12.5%"), ("Health", "8,600")
            };

            private const float SideX = 588f;
            private const float PanelY = -30f;
            private static readonly Vector2 SideSize = new(384f, 704f);
            private static readonly Vector2 CrestAt = new(0f, 84f);
            private const float GearX = 256f;
            private const float GearTop = 230f;
            private const float GearStep = 160f;
            private const float GearSize = 112f;
            private const float TalentSize = 84f;
            private const float TalentStep = 124f;
            private const float ButtonsY = -343f;
            private static readonly Vector2 ConfirmAt = new(116f, ButtonsY);
        }
    }
}
