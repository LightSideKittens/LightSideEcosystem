using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The retro game: an eight-bit role-playing game's menu — the party with their portraits, life and magic, the
        /// bag's items, the chosen item's card and its commands, and a merchant talking in the dialog box. The pointer picks
        /// the steel sword and equips it: the knight's attack rises and the merchant answers.
        /// </summary>
        private sealed class PartyMenu : Product
        {
            public PartyMenu(Builder k) : base(k)
            {
            }

            private RectTransform cursor;
            private RectTransform pointer;
            private readonly CanvasGroup[] cards = new CanvasGroup[2];
            private UniText attack;
            private UniText rise;
            private RectTransform equippedMark;
            private RectTransform portrait;
            private readonly RectTransform[] sparks = new RectTransform[6];
            private UniText speech;
            private RectTransform more;

            public override void Build(RectTransform screen)
            {
                Strip(screen);
                Party(screen);
                Bag(screen);
                K.Window(screen, "Item", InfoAt, InfoSize, "Info");
                cards[0] = Card(screen, Sprites.Potion, "Potion", "Restores 50 HP to one ally.", "HP +50", "40 G");
                cards[1] = Card(screen, Sprites.Sword, "Steel Sword", "A sturdy blade forged in Rivermoor.", "ATK +6", "120 G");
                Commands(screen);
                Dialog(screen);
            }

            /// <summary>The strip across the top: where the party is, how long they have played and their gold.</summary>
            private void Strip(RectTransform screen)
            {
                var strip = K.Panel(screen, "Strip", new Vector2(0f, 412f), new Vector2(1528f, 62f), 8f);
                var s = strip.Rect;
                K.Heading(s, "Rivermoor", 28f, new Vector2(-740f, 0f), 400f);
                K.Caption(s, "Play 12:34", 26f, L.TextDim, new Vector2(0f, 0f), 300f, HAlign.Center);
                Glyphs.Coin(K, s, new Vector2(536f, 0f), 34f);
                K.Figure(s, "1,250 G", 28f, L.Gold, new Vector2(740f, 0f), 260f, HAlign.Right);
            }

            /// <summary>The party: each member's portrait, name, class and level, life and magic, attack and defense.</summary>
            private void Party(RectTransform screen)
            {
                var window = K.Window(screen, "Party", PartyAt, PartySize, "Party");
                var w = window.Node;
                for (var i = 0; i < Members.Length; i++)
                {
                    var (name, job, level, hp, hpMax, mp, mpMax, atk, def) = Members[i];
                    var y = PartySize.y * 0.5f - RowTop - i * RowStep;
                    var frame = K.Inset(w, "Portrait", ShapeKind.RoundedRect, new Vector2(-PartySize.x * 0.5f + 84f, y), Vector2.one * 120f, 6f);
                    Sprites.Draw(K, frame.Rect, Vector2.zero, 8.5f, i == 0 ? Sprites.Arin : i == 1 ? Sprites.Mira : Sprites.Bram);
                    var left = -PartySize.x * 0.5f + 168f;
                    K.Figure(w, name, 30f, L.Text, new Vector2(left, y + 52f), 200f);
                    K.Figure(w, $"Lv{level}", 26f, L.Gold, new Vector2(PartySize.x * 0.5f - 26f, y + 52f), 120f, HAlign.Right);
                    K.Caption(w, job, 22f, L.TextDim, new Vector2(left, y + 20f), 220f);
                    Gauge(w, new Vector2(left, y - 14f), "HP", hp, hpMax, L.Primary);
                    Gauge(w, new Vector2(left, y - 44f), "MP", mp, mpMax, L.Secondary);
                    K.Caption(w, "ATK", 22f, L.TextDim, new Vector2(left, y - 76f), 100f);
                    var strength = K.Figure(w, atk.ToString(), 22f, L.Text, new Vector2(left + 62f, y - 76f), 60f);
                    K.Caption(w, "DEF", 22f, L.TextDim, new Vector2(left + 150f, y - 76f), 100f);
                    K.Figure(w, def.ToString(), 22f, L.Text, new Vector2(left + 212f, y - 76f), 60f);
                    if (i != 0) continue;
                    portrait = frame.Rect;
                    attack = strength;
                    rise = K.Figure(w, "+6", 22f, L.Primary.Light, new Vector2(left + 100f, y - 76f), 80f);
                    rise.gameObject.SetActive(false);
                    for (var s = 0; s < sparks.Length; s++)
                    {
                        sparks[s] = K.Fill(frame.Rect, "Spark", ShapeKind.RoundedRect, Vector2.zero, Vector2.one * 8f, L.Gold, 0f).Rect;
                        sparks[s].gameObject.SetActive(false);
                    }
                }
            }

            private void Gauge(RectTransform parent, Vector2 left, string label, int value, int max, Tone tone)
            {
                K.Caption(parent, label, 20f, L.TextDim, left, 60f);
                K.Progress(parent, left + new Vector2(50f + GaugeWidth * 0.5f, 0f), GaugeWidth, 14f, value / (float)max, tone);
                K.Caption(parent, $"{value}/{max}", 18f, L.Text, left + new Vector2(60f + GaugeWidth, 0f), 140f);
            }

            /// <summary>The bag: its items in a grid with their counts, the cursor on the chosen one.</summary>
            private void Bag(RectTransform screen)
            {
                var window = K.Window(screen, "Items", BagAt, BagSize, "Items");
                var w = window.Node;
                for (var i = 0; i < Items.Length; i++)
                {
                    var at = SlotAt(i) - BagAt;
                    var slot = K.Inset(w, "Slot", ShapeKind.RoundedRect, at, Vector2.one * SlotSize, 6f);
                    var (sprite, count) = Items[i];
                    if (sprite == null) continue;
                    Sprites.Draw(K, slot.Rect, new Vector2(0f, 4f), 5.6f, sprite);
                    K.Figure(slot.Rect, $"x{count}", 18f, L.Text, new Vector2(SlotSize * 0.5f - 6f, -SlotSize * 0.5f + 14f), 80f, HAlign.Right);
                    if (i != SwordSlot) continue;
                    var mark = K.Toned(slot.Rect, "Equipped", ShapeKind.RoundedRect, new Vector2(-SlotSize * 0.5f + 16f, SlotSize * 0.5f - 16f),
                        Vector2.one * 26f, L.Accent, 0f, false);
                    K.Toned(mark.Rect, "E", 16f, new Vector2(1f, 0f), 26f, L.Accent);
                    equippedMark = mark.Rect;
                    equippedMark.gameObject.SetActive(false);
                }

                K.Caption(w, "Sort: Type", 20f, L.TextDim, new Vector2(-BagSize.x * 0.5f + 30f, -BagSize.y * 0.5f + 36f), 300f);
                K.Caption(w, "1/2", 20f, L.TextDim, new Vector2(BagSize.x * 0.5f - 30f, -BagSize.y * 0.5f + 36f), 100f, HAlign.Right);
                cursor = K.Fill(screen, "Cursor", ShapeKind.RoundedRect, SlotAt(0), Vector2.one * (SlotSize + 12f), Color.clear, 8f).Rect;
                Stage.AddStroke(cursor.GetComponent<UniShape>(), L.Line, 5f, 0f);
                Stage.AddStroke(cursor.GetComponent<UniShape>(), L.Outline, 2f, 1f);
            }

            /// <summary>An item's card: its sprite, name, what it does, its effect and its price.</summary>
            private CanvasGroup Card(RectTransform screen, string[] sprite, string name, string text, string effect, string price)
            {
                var (node, group) = K.Group(screen, name, InfoAt);
                Sprites.Draw(K, node, new Vector2(0f, 104f), 8.4f, sprite);
                K.Figure(node, name, 24f, L.Gold, new Vector2(0f, 46f), InfoSize.x - 40f, HAlign.Center);
                K.Paragraph(node, text, 20f, L.Text, new Vector2(-InfoSize.x * 0.5f + 26f, 26f), new Vector2(InfoSize.x - 52f, 60f));
                K.Figure(node, effect, 22f, L.Primary.Light, new Vector2(-InfoSize.x * 0.5f + 26f, -30f), 200f);
                K.Figure(node, price, 22f, L.Gold, new Vector2(InfoSize.x * 0.5f - 26f, -30f), 140f, HAlign.Right);
                return group;
            }

            /// <summary>The card's window and its commands, the pointer's hand beside the one it is on.</summary>
            private void Commands(RectTransform screen)
            {
                var commands = new[] { "Use", "Equip", "Drop" };
                for (var i = 0; i < commands.Length; i++)
                    K.Figure(screen, commands[i], 26f, i == 2 ? L.TextDim : L.Text, CommandAt(i) + new Vector2(-40f, 0f), 200f);
                K.Fill(screen, "Rule", ShapeKind.RoundedRect, InfoAt + new Vector2(0f, -54f), new Vector2(InfoSize.x - 40f, 3f), L.Line, 0f);
                pointer = K.Fill(screen, "Hand", ShapeKind.Triangle, CommandAt(0) + new Vector2(-70f, 0f), new Vector2(24f, 26f), L.Line, 0f).Rect;
                pointer.localRotation = Quaternion.Euler(0f, 0f, -90f);
                Stage.AddStroke(pointer.GetComponent<UniShape>(), L.Outline, 3f, 1f);
            }

            /// <summary>The merchant's dialog box: his portrait, his name and his line typing out, the prompt to go on blinking.</summary>
            private void Dialog(RectTransform screen)
            {
                var window = K.Window(screen, "Dialog", DialogAt, DialogSize, "Merchant");
                var w = window.Node;
                var frame = K.Inset(w, "Portrait", ShapeKind.RoundedRect, new Vector2(-DialogSize.x * 0.5f + 92f, -6f), Vector2.one * 130f, 6f);
                Sprites.Draw(K, frame.Rect, Vector2.zero, 9.5f, Sprites.Merchant);
                speech = K.Paragraph(w, "", 29f, L.Text, new Vector2(-DialogSize.x * 0.5f + 186f, 56f), new Vector2(DialogSize.x - 240f, 140f));
                more = K.Fill(w, "More", ShapeKind.Triangle, new Vector2(DialogSize.x * 0.5f - 40f, -DialogSize.y * 0.5f + 34f), new Vector2(22f, 16f), L.Line, 0f).Rect;
                more.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }

            public override List<Beat> Script()
            {
                var beats = new List<Beat> { Beat.Wait(0.7f) };
                beats.AddRange(Tap(SlotAt(SwordSlot), SlotSize, "item", 0.45f));
                beats.AddRange(Tap(CommandAt(1), 200f, "equip", 1.3f, true));
                return beats;
            }

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                var moved = c.Since("item");
                cursor.anchoredPosition = moved >= 0f ? SlotAt(SwordSlot) : SlotAt(0);
                Show(cursor, Mathf.Repeat(t * 2.2f, 1f) < 0.8f || moved is >= 0f and < 0.3f);
                var picked = moved >= 0f;
                cards[0].alpha = picked ? 0f : 1f;
                cards[1].alpha = picked ? 1f : 0f;
                pointer.anchoredPosition = (c.Since("equip") > -0.35f && picked ? CommandAt(1) : CommandAt(0)) + new Vector2(-70f + Mathf.Repeat(t * 3f, 1f) * 6f, 0f);

                var equipped = c.Since("equip") - 0.1f;
                var done = equipped >= 0f;
                Show(equippedMark, done);
                if (done) equippedMark.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(equipped / 0.25f));
                Say(attack, done ? "18" : "12");
                attack.color = done && equipped < 0.6f && Mathf.Repeat(equipped * 8f, 1f) < 0.5f ? L.Primary.Light : L.Text;
                Show(rise, done && equipped < 1.2f);
                if (done) rise.rectTransform.anchoredPosition = new Vector2(rise.rectTransform.anchoredPosition.x, AttackY + 14f * Mathf.Clamp01(equipped / 0.5f));
                for (var i = 0; i < sparks.Length; i++)
                {
                    var f = equipped / 0.6f;
                    var shown = f >= 0f && f < 1f;
                    Show(sparks[i], shown);
                    if (!shown) continue;
                    var angle = i * Mathf.PI * 2f / sparks.Length;
                    sparks[i].anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (30f + 50f * f);
                }

                portrait.localScale = Vector3.one * (done ? 1f + 0.06f * Mathf.Exp(-equipped * 8f) : 1f);
                var line = done ? Reply : Greeting;
                var typed = done ? equipped - 0.2f : t - 0.3f;
                var letters = Mathf.Clamp(Mathf.FloorToInt(typed * TypeRate), 0, line.Length);
                Say(speech, L.Cased(line.Substring(0, letters)));
                Show(more, letters == line.Length && Mathf.Repeat(t * 2f, 1f) < 0.6f);
            }

            private static Vector2 SlotAt(int index) => BagAt + new Vector2((index % 6 - 2.5f) * (SlotSize + SlotGap), BagSize.y * 0.5f - 112f - index / 6 * (SlotSize + SlotGap));

            private static Vector2 CommandAt(int index) => InfoAt + new Vector2(0f, -86f - index * 38f);

            private static float AttackY => PartySize.y * 0.5f - RowTop - 76f;

            private static readonly (string name, string job, int level, int hp, int hpMax, int mp, int mpMax, int atk, int def)[] Members =
            {
                ("Arin", "Knight", 12, 150, 150, 18, 40, 12, 9), ("Mira", "Mage", 11, 86, 110, 52, 60, 6, 5), ("Bram", "Warrior", 12, 132, 170, 0, 20, 14, 11)
            };

            private static readonly (string[] sprite, int count)[] Items =
            {
                (Sprites.Potion, 5), (Sprites.Ether, 2), (Sprites.Herb, 7), (Sprites.Bomb, 3), (Sprites.Key, 1), (Sprites.Sword, 1),
                (Sprites.Shield, 1), (Sprites.Scroll, 2), (Sprites.Gem, 4), (Sprites.Bread, 6), (null, 0), (null, 0),
                (null, 0), (null, 0), (null, 0), (null, 0), (null, 0), (null, 0)
            };

            private const string Greeting = "Fine steel, traveler! A blade like that wants a steady hand. Try it on?";
            private const string Reply = "A perfect fit, Arin! Mind you don't cut yourself.";
            private const float TypeRate = 42f;
            private const int SwordSlot = 5;
            private const float GaugeWidth = 150f;
            private const float RowTop = 128f;
            private const float RowStep = 212f;
            private static readonly Vector2 PartyAt = new(-510f, -29f);
            private static readonly Vector2 PartySize = new(508f, 718f);
            private static readonly Vector2 BagAt = new(84f, 130f);
            private static readonly Vector2 BagSize = new(620f, 400f);
            private const float SlotSize = 86f;
            private const float SlotGap = 10f;
            private static readonly Vector2 InfoAt = new(594f, 130f);
            private static readonly Vector2 InfoSize = new(340f, 400f);
            private static readonly Vector2 DialogAt = new(269f, -258f);
            private static readonly Vector2 DialogSize = new(990f, 260f);
        }
    }
}
