using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The casual game: a puzzle game's level map at night — the road of levels with their stars, the player's pin on
        /// the next one, the lives and coins above, events down either side. The pointer opens the next level, picks a
        /// booster and plays, which spends a life: the heart flies up to the lives and their refill starts counting down.
        /// </summary>
        private sealed class SagaMap : Product
        {
            public SagaMap(Builder k) : base(k)
            {
            }

            private RectTransform pulse;
            private RectTransform pin;
            private RectTransform current;
            private UniText spinTimer;
            private UniText lives;
            private UniText livesState;
            private CanvasGroup veil;
            private Window popup;
            private RectTransform chosen;
            private RectTransform chosenTick;
            private UniText chosenCount;
            private Button play;
            private RectTransform heart;

            public override void Build(RectTransform screen)
            {
                Land(screen);
                Road(screen);
                for (var i = 0; i < Levels.Length; i++) Level(screen, i);
                Events(screen);
                veil = K.Veil(screen, 0.62f);
                Popup(screen);
                TopBar(screen);
                heart = Glyphs.Heart(K, screen, PlayAt, 58f, L.Danger.Light);
                heart.gameObject.SetActive(false);
            }

            /// <summary>Soft islands the road runs over, so the map reads as land rather than a void.</summary>
            private void Land(RectTransform screen)
            {
                foreach (var (at, size, turn) in Islands)
                {
                    var island = K.Fill(screen, "Island", ShapeKind.Ellipse, at, size, Hex(0x5B3DB0, 0.4f), 0f);
                    island.Rect.localRotation = Quaternion.Euler(0f, 0f, turn);
                    Stage.AddInnerShadow(island.Shape, Hex(0xB89BFF, 0.35f), new Vector2(0f, -10f), 26f);
                }
            }

            /// <summary>The road of levels: a dark band through every level and a light line down its middle.</summary>
            private void Road(RectTransform screen)
            {
                foreach (var (width, color) in new[] { (50f, Hex(0x241760)), (10f, Hex(0x9C88F2, 0.55f)) })
                {
                    var road = K.Shape(screen, "Road", ShapeKind.RoundedRect, Vector2.zero, ScreenSize, 0f);
                    var path = Stage.Contour(RoadPoints(), 1f, false, true);
                    path.Width = width;
                    path.Cap = EndCap.Round;
                    road.Shape.Shape = path;
                    Stage.Solid(road.Fill, color);
                }
            }

            private static Vector2[] RoadPoints()
            {
                var points = new List<Vector2> { new(-800f, -420f) };
                foreach (var level in Levels) points.Add(level.at);
                points.Add(new Vector2(600f, 250f));
                return points.ToArray();
            }

            private void Level(RectTransform screen, int index)
            {
                var (at, number, stars) = Levels[index];
                var node = K.Node(screen, "Level " + number, at);
                if (number < CurrentLevel)
                {
                    K.Toned(node, "Disc", ShapeKind.Circle, Vector2.zero, Vector2.one * 80f, Done, 0f);
                    K.Toned(node, number.ToString(), 34f, new Vector2(0f, 2f), 80f, Done);
                    K.Stars(node, new Vector2(0f, 56f), 28f, 54f, stars, 8f);
                }
                else if (number == CurrentLevel)
                {
                    current = node;
                    pulse = K.Fill(node, "Pulse", ShapeKind.Circle, Vector2.zero, Vector2.one * 100f, Color.clear, 0f).Rect;
                    Stage.AddStroke(pulse.GetComponent<UniShape>(), L.Accent.Light, 6f, 0f);
                    K.Toned(node, "Disc", ShapeKind.Circle, Vector2.zero, Vector2.one * 100f, L.Accent, 0f);
                    K.Toned(node, number.ToString(), 40f, new Vector2(0f, 2f), 100f, L.Accent);
                    var (marker, head) = Glyphs.Pin(K, node, new Vector2(0f, PinRise), 78f, Color.white);
                    K.Avatar(head, Vector2.zero, 64f, "M", L.Secondary.Dark, L.Secondary.Light);
                    pin = marker;
                }
                else if (index == Levels.Length - 1)
                {
                    K.Fill(node, "Plate", ShapeKind.Circle, Vector2.zero, Vector2.one * 110f, Hex(0x2A1C66), 0f);
                    Glyphs.Chest(K, node, new Vector2(0f, 6f), 92f, Hex(0xD9873E), L.Gold);
                }
                else
                {
                    var disc = K.Fill(node, "Disc", ShapeKind.Circle, Vector2.zero, Vector2.one * 72f, L.Panel, 0f);
                    Stage.AddStroke(disc.Shape, L.Line, 4f, -1f);
                    Stage.AddShadow(disc.Shape, Theme.Fade(L.Outline, 0.7f), new Vector2(0f, -6f), 0f);
                    Glyphs.Lock(K, node, new Vector2(0f, 2f), 32f, L.TextDim);
                }
            }

            /// <summary>Events down either side: a daily spin and its countdown, the team, a free gift; the star chest and the pass.</summary>
            private void Events(RectTransform screen)
            {
                var spin = Event(screen, new Vector2(-EventX, 236f), L.Secondary, "");
                Glyphs.Wheel(K, spin, new Vector2(0f, -2f), 80f, new[] { Hex(0xFF7FB0), Hex(0xFFE27A), Hex(0x6BF5BE), Hex(0x7CCBFF) }, Color.white);
                spinTimer = Caption(spin, Clockface(SpinLeft));
                var team = Event(screen, new Vector2(-EventX, 66f), L.Secondary, "Team");
                Glyphs.Team(K, team, new Vector2(0f, -2f), 74f, Color.white, Hex(0xD2C4FF));
                K.Badge(team, new Vector2(40f, 40f), "3");
                var gift = Event(screen, new Vector2(-EventX, -104f), L.Danger, "Free!");
                Glyphs.Gift(K, gift, new Vector2(0f, -2f), 74f, Hex(0xFF8FB6), L.Gold);
                K.Badge(gift, new Vector2(40f, 40f), "1");

                var chest = Event(screen, new Vector2(EventX, 236f), L.Accent, "");
                Glyphs.Chest(K, chest, new Vector2(0f, 0f), 72f, Hex(0xD9873E), Hex(0xFFF1B0));
                var bar = K.Progress(chest, new Vector2(0f, -70f), 128f, 26f, 0.8f, L.Accent);
                K.Figure(bar.parent, "32/40", 20f, Color.white, new Vector2(8f, 1f), 128f, HAlign.Center);
                Glyphs.Star(K, bar.parent, new Vector2(-62f, 0f), 34f, L.Gold);
                var pass = Event(screen, new Vector2(EventX, 66f), L.Primary, "Pass Lv 7");
                Glyphs.Crown(K, pass, new Vector2(0f, 0f), 66f, L.Gold);
            }

            /// <summary>An event's button with a caption on a dark chip below it; returns the button's node.</summary>
            private RectTransform Event(RectTransform screen, Vector2 at, Tone tone, string caption)
            {
                var button = K.IconButton(screen, at, EventSize, tone);
                if (caption.Length > 0) Caption(button.Node, caption);
                return button.Node;
            }

            private UniText Caption(RectTransform owner, string text)
            {
                K.Fill(owner, "Chip", ShapeKind.Capsule, new Vector2(0f, -EventSize * 0.5f - 14f), new Vector2(136f, 38f), Theme.Fade(L.Outline, 0.8f), 0f);
                return K.Figure(owner, text, 22f, Color.white, new Vector2(0f, -EventSize * 0.5f - 13f), 136f, HAlign.Center);
            }

            /// <summary>The level's window: its target stars, what to collect, the boosters to take in and the play button.</summary>
            private void Popup(RectTransform screen)
            {
                popup = K.Window(screen, "Level Window", PopupAt, PopupSize, "Level " + CurrentLevel, true);
                var w = popup.Node;
                K.Stars(w, new Vector2(0f, 200f), 70f, 170f, 0, 20f);

                K.Inset(w, "Goals", ShapeKind.RoundedRect, new Vector2(0f, 66f), new Vector2(548f, 132f), 22f);
                K.Caption(w, "Collect in 25 moves", 24f, L.TextDim, new Vector2(0f, 108f), 500f, HAlign.Center);
                Glyphs.Candy(K, w, new Vector2(-150f, 46f), 80f, Hex(0xFF6FA8));
                K.Figure(w, "30", 40f, Color.white, new Vector2(-96f, 43f), 100f);
                Glyphs.Bean(K, w, new Vector2(80f, 46f), 76f, Hex(0x58C4FF));
                K.Figure(w, "24", 40f, Color.white, new Vector2(130f, 43f), 100f);

                K.Caption(w, "Boosters", 24f, L.TextDim, new Vector2(-264f, -24f), 300f);
                for (var i = 0; i < 3; i++)
                {
                    var x = (i - 1) * BoosterStep;
                    var slot = K.Slot(w, new Vector2(x, BoosterY), BoosterSize, i == 2 ? 1 : 2);
                    if (i == 0) Glyphs.Bomb(K, slot.Rect, new Vector2(-4f, -2f), 82f, Hex(0x4A3A8C), L.Accent.Light);
                    else if (i == 1) Glyphs.Hammer(K, slot.Rect, Vector2.zero, 84f, Hex(0xFF8FB6), Hex(0xFFD27A));
                    else
                    {
                        K.Toned(slot.Rect, "Moves", ShapeKind.RoundedRect, Vector2.zero, new Vector2(80f, 62f), L.Primary, 16f);
                        K.Toned(slot.Rect, "+5", 36f, new Vector2(0f, 2f), 80f, L.Primary);
                    }

                    var chip = K.Toned(slot.Rect, "Count", ShapeKind.Circle, new Vector2(48f, -48f), Vector2.one * 42f, L.Secondary, 0f, false);
                    var count = K.Figure(chip.Rect, BoosterCounts[i].ToString(), 24f, Color.white, new Vector2(0f, 1f), 42f, HAlign.Center);
                    if (i != 0) continue;
                    chosen = K.Ring(slot.Rect, Vector2.zero, Vector2.one * (BoosterSize + 10f), L.Primary.Light, L.Radius * 0.7f + 5f, 6f).Rect;
                    var tick = K.Toned(slot.Rect, "Tick", ShapeKind.Circle, new Vector2(-48f, 48f), Vector2.one * 44f, L.Primary, 0f, false);
                    Glyphs.Check(K, tick.Rect, Vector2.zero, 26f, Color.white);
                    chosenTick = tick.Rect;
                    chosenCount = count;
                }

                play = K.Button(w, "Play", PlayAt - PopupAt, new Vector2(400f, 104f), L.Primary, 50f);
            }

            private void TopBar(RectTransform screen)
            {
                lives = null;
                var livesPurse = K.Pill(screen, LivesAt, 260f, "", false);
                var livesNode = (RectTransform)livesPurse.rectTransform.parent;
                var livesHeart = Glyphs.Heart(K, livesNode, Builder.PillIcon(260f) + new Vector2(-8f, 0f), 70f, L.Danger.Light);
                lives = K.Figure(livesHeart, "5", 30f, Color.white, new Vector2(0f, -2f), 60f, HAlign.Center);
                livesState = livesPurse;
                livesState.rectTransform.anchoredPosition += new Vector2(6f, 0f);
                Say(livesState, "Full");

                var coins = K.Pill(screen, CoinsAt, 260f, Grouped(12480));
                Glyphs.Coin(K, (RectTransform)coins.rectTransform.parent, Builder.PillIcon(260f) + new Vector2(-6f, 0f), 58f);

                K.Ribbon(screen, new Vector2(0f, 398f), 380f, 72f, L.Danger, "Candy Coast", 36f);
                var settings = K.IconButton(screen, new Vector2(722f, 396f), 78f, L.Secondary);
                Glyphs.Gear(K, settings.Node, Vector2.zero, 48f, Color.white, L.Secondary.Dark);
                var shop = K.IconButton(screen, new Vector2(628f, 396f), 78f, L.Accent);
                Glyphs.Coins(K, shop.Node, Vector2.zero, 56f);
                Glyphs.Plus(K, shop.Node, new Vector2(26f, -24f), 26f, Color.white);
            }

            public override List<Beat> Script()
            {
                var beats = new List<Beat> { Beat.Wait(0.55f) };
                beats.AddRange(Tap(Levels[CurrentIndex].at, 100f, "level", 0.45f));
                beats.AddRange(Tap(PopupAt + new Vector2(-BoosterStep, BoosterY), BoosterSize, "booster", 0.35f));
                beats.AddRange(Tap(PlayAt, 400f, "play", 1.1f, true));
                return beats;
            }

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                var beat = Mathf.Repeat(t * 0.9f, 1f);
                pulse.localScale = Vector3.one * (1f + 0.45f * beat);
                Stage.Alpha(pulse.GetComponent<UniShape>(), 1f - beat);
                pin.anchoredPosition = new Vector2(0f, PinRise + Mathf.Sin(t * 3.2f) * 6f);
                current.localScale = Vector3.one * (1f - 0.08f * c.Held("level"));
                Say(spinTimer, Clockface(SpinLeft - Mathf.FloorToInt(t)));

                var open = c.Since("level") - 0.08f;
                var closed = c.Since("play") - 0.12f;
                popup.Pose(open, closed);
                veil.alpha = Mathf.Clamp01(open / 0.25f) * (closed < 0f ? 1f : 1f - Mathf.Clamp01(closed / Window.CloseFor));

                var picked = c.Since("booster");
                Show(chosen, picked >= 0f);
                Show(chosenTick, picked >= 0f);
                if (picked >= 0f)
                {
                    chosen.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(picked / 0.3f));
                    chosenTick.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01((picked - 0.06f) / 0.3f));
                }

                Say(chosenCount, (BoosterCounts[0] - (picked >= 0f ? 1 : 0)).ToString());
                play.Press(c.Held("play"));

                var flight = (c.Since("play") - 0.12f) / HeartFlight;
                Show(heart, flight >= 0f && flight < 1f);
                var to = LivesAt + Builder.PillIcon(260f) + new Vector2(-8f, 0f);
                if (flight >= 0f && flight < 1f)
                {
                    var e = Ease.EmphasizedIn.Evaluate(flight);
                    heart.anchoredPosition = Vector2.Lerp(PlayAt, to, e) + new Vector2(Mathf.Sin(flight * Mathf.PI) * -120f, Mathf.Sin(flight * Mathf.PI) * 80f);
                    heart.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.9f, e);
                }

                var landed = flight >= 1f;
                Say(lives, landed ? "4" : "5");
                var since = c.Since("play") - 0.12f - HeartFlight;
                Say(livesState, landed ? Clockface(RefillSeconds - Mathf.FloorToInt(since)).Substring(3) : "Full");
                lives.rectTransform.parent.localScale = Vector3.one * (landed ? 1f + 0.25f * Mathf.Exp(-since * 6f) * Mathf.Cos(since * 20f) : 1f);
            }

            private Tone Done => new(Hex(0xFF8FC0), Hex(0xE0367A), Hex(0x93184D));

            private static readonly (Vector2 at, int number, int stars)[] Levels =
            {
                (new(-560f, -330f), 38, 3), (new(-390f, -250f), 39, 3), (new(-210f, -318f), 40, 2), (new(-30f, -246f), 41, 3),
                (new(150f, -150f), 42, 0), (new(30f, -20f), 43, 0), (new(-150f, 60f), 44, 0), (new(-40f, 188f), 45, 0),
                (new(170f, 250f), 46, 0), (new(400f, 180f), 47, 0)
            };

            private static readonly (Vector2 at, Vector2 size, float turn)[] Islands =
            {
                (new(-380f, -300f), new(620f, 260f), -6f), (new(60f, -130f), new(520f, 300f), 18f), (new(-60f, 150f), new(560f, 300f), -10f),
                (new(380f, 210f), new(420f, 240f), 8f)
            };

            private const int CurrentLevel = 42;
            private const int CurrentIndex = 4;
            private const float PinRise = 104f;
            private const float EventX = 706f;
            private const float EventSize = 108f;
            private const int SpinLeft = 2 * 3600 + 14 * 60 + 33;
            private const int RefillSeconds = 29 * 60 + 59;
            private static readonly Vector2 PopupAt = new(0f, -60f);
            private static readonly Vector2 PopupSize = new(640f, 660f);
            private const float BoosterStep = 172f;
            private const float BoosterY = -134f;
            private const float BoosterSize = 136f;
            private static readonly int[] BoosterCounts = { 3, 1, 2 };
            private static readonly Vector2 PlayAt = new(0f, -328f);
            private static readonly Vector2 LivesAt = new(-610f, 396f);
            private static readonly Vector2 CoinsAt = new(-330f, 396f);
            private const float HeartFlight = 0.6f;
        }
    }
}
