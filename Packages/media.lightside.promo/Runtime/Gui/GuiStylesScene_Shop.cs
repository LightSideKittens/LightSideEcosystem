using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The glossy game: a toy shop — the shop's tabs, a featured bundle and the day's deals, each item framed by its
        /// rarity with its price. The pointer picks the wizard hat, confirms it in the purchase window, and the hat flies
        /// into its card, now owned, as the coins it cost count away from the purse.
        /// </summary>
        private sealed class ToyShop : Product
        {
            public ToyShop(Builder k) : base(k)
            {
            }

            private UniText coins;
            private UniText spent;
            private UniText refresh;
            private Button price;
            private RectTransform owned;
            private RectTransform card;
            private CanvasGroup veil;
            private Window confirm;
            private Button buy;
            private RectTransform flyer;
            private readonly RectTransform[] sparks = new RectTransform[8];

            public override void Build(RectTransform screen)
            {
                Bundle(screen);
                for (var i = 0; i < Deals.Length; i++) Deal(screen, i);
                Header(screen);
                veil = K.Veil(screen, 0.55f);
                Confirm(screen);
                flyer = Item(screen, 0, ConfirmAt + new Vector2(0f, ItemRise), 160f);
                flyer.gameObject.SetActive(false);
                for (var i = 0; i < sparks.Length; i++)
                {
                    var spark = Glyphs.Star(K, screen, Vector2.zero, 26f, Hex(0xFFF2A8));
                    sparks[i] = spark;
                    spark.gameObject.SetActive(false);
                }
            }

            private void Header(RectTransform screen)
            {
                var back = K.IconButton(screen, new Vector2(-722f, 396f), 78f, L.Secondary);
                Glyphs.Chevron(K, back.Node, new Vector2(-3f, 0f), 40f, Color.white);
                K.Ribbon(screen, new Vector2(0f, 398f), 360f, 84f, L.Accent, "Shop", 46f);
                coins = K.Pill(screen, CoinsAt, 270f, Grouped(CoinsBefore));
                Glyphs.Coin(K, (RectTransform)coins.rectTransform.parent, Builder.PillIcon(270f) + new Vector2(-6f, 0f), 60f);
                spent = K.Figure(screen, "-450", 30f, L.Danger.Dark, CoinsAt + new Vector2(-20f, -54f), 160f, HAlign.Center);
                Stroke(spent, Color.white, 0.12f);
                spent.gameObject.SetActive(false);
                var gems = K.Pill(screen, new Vector2(676f, 396f), 220f, "320");
                Glyphs.Gem(K, (RectTransform)gems.rectTransform.parent, Builder.PillIcon(220f) + new Vector2(-4f, 0f), 56f);

                var tabs = K.Segmented(screen, new Vector2(BundleAt.x - BundleSize.x * 0.5f + 476f, TabsY), new[] { "Featured", "Daily", "Chests", "Gems" }, 236f, L.Accent, 72f, 30f);
                tabs.Set(1f, L.TextOnTone, L.TextDim);
                L.OnTone?.Invoke(tabs.Captions[1], L.Accent);
                var timer = K.Node(screen, "Refresh", new Vector2(GridLeft + 4f * CardSize.x + 3f * CardGap - 125f, TabsY));
                K.Inset(timer, "Well", ShapeKind.Capsule, Vector2.zero, new Vector2(250f, 58f), 0f);
                Glyphs.Clock(K, timer, new Vector2(-92f, 0f), 40f, Color.white, L.Text);
                refresh = K.Figure(timer, Clockface(RefreshLeft), 28f, L.Text, new Vector2(-62f, 1f), 190f);
            }

            /// <summary>The featured bundle: a big chest, what it holds, its discount, when it ends and its price.</summary>
            private void Bundle(RectTransform screen)
            {
                var panel = K.Panel(screen, "Bundle", BundleAt, BundleSize);
                var p = panel.Rect;
                var face = p.sizeDelta;
                var top = face.y * 0.5f;
                K.Halo(p, new Vector2(0f, top - 180f), 330f, Hex(0xFFE27A, 0.85f));
                var band = K.Toned(p, "Band", ShapeKind.RoundedRect, new Vector2(0f, top - 46f), new Vector2(face.x - 36f, 66f), L.Danger, L.Radius * 0.8f);
                K.Toned(band.Rect, "Starter Bundle", 34f, new Vector2(0f, 2f), face.x - 60f, L.Danger);
                var chest = Glyphs.Chest(K, p, new Vector2(0f, top - 190f), 200f, Hex(0xE08A3C), L.Gold);
                Glyphs.Ink(chest, 4f);
                var tag = K.Tag(p, new Vector2(130f, top - 100f), "-60%", L.Danger, 28f);
                tag.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, -12f);

                var rows = new (string text, int icon)[] { ("2,000", 0), ("100", 1), ("Rocket Pack", 2) };
                for (var i = 0; i < rows.Length; i++)
                {
                    var y = 4f - i * 56f;
                    K.Inset(p, "Row", ShapeKind.RoundedRect, new Vector2(0f, y), new Vector2(face.x - 60f, 50f), 14f);
                    var at = new Vector2(-face.x * 0.5f + 64f, y);
                    if (rows[i].icon == 0) Glyphs.Coin(K, p, at, 40f);
                    else if (rows[i].icon == 1) Glyphs.Gem(K, p, at, 40f);
                    else Glyphs.Rocket(K, p, at, 44f, Hex(0xF2F4FF), Hex(0xFF5A5A), Hex(0xFFB020));
                    K.Figure(p, rows[i].text, 28f, L.Text, at + new Vector2(36f, 1f), 260f);
                }

                K.Caption(p, "Ends in 1d 04:12", 24f, L.TextDim, new Vector2(0f, -top + 132f), 300f, HAlign.Center);
                K.Button(p, "$4.99", new Vector2(0f, -top + 63f), new Vector2(300f, 78f), L.Primary, 40f);
            }

            private void Deal(RectTransform screen, int index)
            {
                var (_, name, rarity, gems, amount) = Deals[index];
                var at = CardAt(index);
                var panel = K.Panel(screen, name, at, CardSize, L.Radius);
                var c = panel.Rect;
                var face = c.sizeDelta;
                var tone = L.Rarity[rarity];
                var band = K.Toned(c, "Rarity", ShapeKind.RoundedRect, new Vector2(0f, face.y * 0.5f - 34f), new Vector2(face.x - 28f, 46f), tone, 14f);
                K.Toned(band.Rect, RarityNames[rarity], 24f, new Vector2(0f, 1f), face.x - 40f, tone);
                K.Halo(c, new Vector2(0f, 34f), 190f, Theme.Fade(tone.Light, 0.7f));
                Item(c, index, new Vector2(0f, 34f), 118f);
                K.Figure(c, name, 26f, L.Text, new Vector2(0f, -40f), face.x - 20f, HAlign.Center);

                var free = amount == 0;
                var priceY = -face.y * 0.5f + PriceRise;
                var button = K.Button(c, free ? "Free" : amount.ToString(), new Vector2(0f, priceY), new Vector2(212f, 66f), free ? L.Secondary : L.Primary, 32f);
                button.Caption.rectTransform.anchoredPosition += new Vector2(free ? 18f : 20f, 0f);
                var icon = new Vector2(free ? -56f : -58f, 2f);
                if (free)
                {
                    var screenIcon = K.Fill(button.Node, "Ad", ShapeKind.RoundedRect, icon, new Vector2(40f, 30f), Color.white, 6f);
                    Glyphs.Play(K, screenIcon.Rect, new Vector2(1f, 0f), 18f, L.Secondary.Dark);
                }
                else if (gems) Glyphs.Gem(K, button.Node, icon, 40f);
                else Glyphs.Coin(K, button.Node, icon, 40f);

                if (index != 0) return;
                card = c;
                price = button;
                owned = K.Node(c, "Owned", new Vector2(0f, priceY));
                K.Inset(owned, "Well", ShapeKind.RoundedRect, Vector2.zero, new Vector2(212f, 66f), L.Radius);
                var tick = K.Toned(owned, "Tick", ShapeKind.Circle, new Vector2(-62f, 0f), Vector2.one * 42f, L.Primary, 0f, false);
                Glyphs.Check(K, tick.Rect, Vector2.zero, 24f, Color.white);
                K.Figure(owned, "Owned", 30f, L.Text, new Vector2(-30f, 1f), 140f);
                owned.gameObject.SetActive(false);
            }

            /// <summary>Deal <paramref name="index"/>'s item, <paramref name="s"/> across, each piece edged in a darker shade of its own color.</summary>
            private RectTransform Item(RectTransform parent, int index, Vector2 at, float s)
            {
                RectTransform item = index switch
                {
                    0 => Glyphs.Hat(K, parent, at, s, Hex(0x8A4BF0), Hex(0xFFC93C), Hex(0xFFF2A8)),
                    1 => Glyphs.Shades(K, parent, at, s, Hex(0x2A2F4A), Hex(0xFF5A8A)),
                    2 => Glyphs.Rocket(K, parent, at, s, Hex(0xF2F4FF), Hex(0xFF5A5A), Hex(0xFFB020)),
                    3 => Glyphs.Slime(K, parent, at, s, Hex(0x7BE36A)),
                    4 => Glyphs.Crown(K, parent, at, s * 0.9f, L.Gold),
                    5 => Glyphs.Wand(K, parent, at, s, Hex(0x8A5A2E), Hex(0xFFD84A)),
                    6 => Glyphs.Gift(K, parent, at, s * 0.9f, Hex(0x5BC8FF), Hex(0xFF5A8A)),
                    _ => Glyphs.Coins(K, parent, at, s)
                };
                Glyphs.Ink(item, 3f);
                return item;
            }

            /// <summary>The purchase window: the item on its glow, its name and rarity, and the choice to cancel or buy.</summary>
            private void Confirm(RectTransform screen)
            {
                confirm = K.Window(screen, "Confirm", ConfirmAt, ConfirmSize, "Buy item?", true);
                var w = confirm.Node;
                K.Halo(w, new Vector2(0f, ItemRise), 250f, Theme.Fade(L.Rarity[2].Light, 0.75f));
                K.Inset(w, "Stage", ShapeKind.Ellipse, new Vector2(0f, ItemRise - 78f), new Vector2(200f, 40f), 0f);
                Item(w, 0, new Vector2(0f, ItemRise), 160f);
                K.Figure(w, "Wizard Hat", 44f, L.Text, new Vector2(0f, -54f), 500f, HAlign.Center);
                K.Tag(w, new Vector2(0f, -102f), "Epic", L.Rarity[2], 24f);
                K.Button(w, "Cancel", new Vector2(-136f, ButtonsY), new Vector2(240f, 92f), L.Danger, 36f);
                buy = K.Button(w, "450", new Vector2(136f, ButtonsY), new Vector2(240f, 92f), L.Primary, 40f);
                buy.Caption.rectTransform.anchoredPosition += new Vector2(22f, 0f);
                Glyphs.Coin(K, buy.Node, new Vector2(-50f, 2f), 48f);
            }

            public override List<Beat> Script()
            {
                var beats = new List<Beat> { Beat.Wait(0.55f) };
                beats.AddRange(Tap(card.anchoredPosition + price.Home, 212f, "item", 0.5f));
                beats.AddRange(Tap(ConfirmAt + buy.Home, 240f, "buy", 1.15f, true));
                return beats;
            }

            public override void Pose(in Clock c)
            {
                var t = c.T;
                K.Live?.Invoke(t);
                Say(refresh, Clockface(RefreshLeft - Mathf.FloorToInt(t)));
                price.Press(c.Held("item"));
                var open = c.Since("item") - 0.08f;
                var closed = c.Since("buy") - 0.12f;
                confirm.Pose(open, closed);
                veil.alpha = Mathf.Clamp01(open / 0.25f) * (closed < 0f ? 1f : 1f - Mathf.Clamp01(closed / Window.CloseFor));
                buy.Press(c.Held("buy"));

                var flight = (c.Since("buy") - 0.1f) / ItemFlight;
                Show(flyer, flight >= 0f && flight < 1f);
                var home = card.anchoredPosition + new Vector2(0f, 34f);
                if (flight >= 0f && flight < 1f)
                {
                    var e = Ease.Emphasized.Evaluate(flight);
                    flyer.anchoredPosition = Vector2.Lerp(ConfirmAt + new Vector2(0f, ItemRise), home, e) + new Vector2(0f, Mathf.Sin(flight * Mathf.PI) * 90f);
                    flyer.localScale = Vector3.one * Mathf.Lerp(1f, 118f / 160f, e);
                }

                var landed = c.Since("buy") - 0.1f - ItemFlight;
                var isOwned = landed >= 0f;
                Show(owned, isOwned);
                Show(price.Node, !isOwned);
                if (isOwned) owned.localScale = Vector3.one * Pop.Evaluate(Mathf.Clamp01(landed / 0.3f));
                card.localScale = Vector3.one * (isOwned ? 1f + 0.06f * Mathf.Exp(-landed * 6f) * Mathf.Cos(landed * 18f) : 1f);
                for (var i = 0; i < sparks.Length; i++)
                {
                    var f = landed / 0.6f;
                    var shown = f >= 0f && f < 1f;
                    Show(sparks[i], shown);
                    if (!shown) continue;
                    var angle = i * Mathf.PI * 2f / sparks.Length + 0.3f;
                    sparks[i].anchoredPosition = home + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (60f + 90f * Ease.EmphasizedOut.Evaluate(f));
                    sparks[i].localScale = Vector3.one * (1f - f);
                }

                var paid = Ease.Emphasized.Window(t, c.At("buy") + 0.15f, 0.6f);
                Say(coins, Grouped(Mathf.RoundToInt(Mathf.Lerp(CoinsBefore, CoinsBefore - 450, paid))));
                var shownSpent = c.Since("buy") - 0.15f;
                Show(spent, shownSpent >= 0f && shownSpent < 1.1f);
                if (shownSpent >= 0f)
                {
                    spent.rectTransform.anchoredPosition = CoinsAt + new Vector2(-20f, -54f - 30f * Ease.EmphasizedOut.Evaluate(Mathf.Clamp01(shownSpent / 1.1f)));
                    spent.color = Theme.Fade(L.Danger.Dark, 1f - Mathf.InverseLerp(0.7f, 1.1f, shownSpent));
                }
            }

            private static Vector2 CardAt(int index) => new(GridLeft + CardSize.x * 0.5f + index % 4 * (CardSize.x + CardGap),
                GridTop - CardSize.y * 0.5f - index / 4 * (CardSize.y + CardGap));

            private static readonly (int item, string name, int rarity, bool gems, int amount)[] Deals =
            {
                (0, "Wizard Hat", 2, false, 450), (1, "Cool Shades", 1, false, 180), (2, "Rocket Pack", 3, true, 60), (3, "Pet Slime", 1, false, 220),
                (4, "Royal Crown", 3, true, 90), (5, "Magic Wand", 2, false, 380), (6, "Daily Gift", 0, false, 0), (7, "500 Coins", 0, true, 20)
            };

            private static readonly string[] RarityNames = { "Common", "Rare", "Epic", "Legendary" };

            private const int CoinsBefore = 2450;
            private const int RefreshLeft = 5 * 3600 + 12 * 60 + 44;
            private static readonly Vector2 CoinsAt = new(406f, 396f);
            private const float TabsY = 298f;
            private static readonly Vector2 BundleAt = new(-572f, -93f);
            private static readonly Vector2 BundleSize = new(390f, 634f);
            private static readonly Vector2 CardSize = new(256f, 309f);
            private const float CardGap = 16f;
            private const float GridLeft = -344f;
            private const float GridTop = 224f;
            /// <summary>How far above a card's face bottom its price button's middle stands.</summary>
            private const float PriceRise = 51f;
            private static readonly Vector2 ConfirmAt = new(0f, -40f);
            private static readonly Vector2 ConfirmSize = new(600f, 560f);
            private const float ItemRise = 86f;
            private const float ButtonsY = -196f;
            private const float ItemFlight = 0.55f;
        }
    }
}
