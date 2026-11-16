using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made navigation: a game hub's settings tab switched and a question of its help unfolded.
        /// </summary>
        private sealed class NavigationPart : KitPart
        {
            private readonly UniToggle[] tabs;
            private readonly UniToggle question;

            public NavigationPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Navigation", NavigationBoard, NavigationScale)
            {
                Label("Game hub", new Vector2(NavigationLeft, NavigationTop - 34f), 24f, "Ink", display: true);

                Label("Settings", new Vector2(NavigationLeft, ColumnTop), 14f, "InkDim");
                var strip = Add("Tabs", new Vector2(NavigationLeft, TabsY), 0f);
                Kit.Relabel(strip, "Tab 1", "General");
                Kit.Relabel(strip, "Tab 2", "Audio");
                Kit.Relabel(strip, "Tab 3", "Video");
                Kit.Relabel(strip, "Page 1", "Language · English\nRegion · Europe\nAccount · mia@guild.gg");
                Kit.Relabel(strip, "Page 2", "Music · 80 %\nEffects · 65 %\nVoice · On");
                Kit.Relabel(strip, "Page 3", "Resolution · 2560 × 1440\nFrame rate · 120 fps\nHDR · On");
                tabs = strip.GetComponentsInChildren<UniToggle>(true);

                Label("Help", new Vector2(NavigationRight, ColumnTop), 14f, "InkDim");
                var faq = Add("Accordion", new Vector2(NavigationRight, ColumnTop - LabelTop), 0f, 1f);
                Kit.Relabel(faq, "Section", "How do I join a raid?");
                Kit.Relabel(faq, "Content of the section.", "Open the hub, pick Raids and press Join.");
                question = faq.GetComponentInChildren<UniToggle>(true);

                Plan(At(new Vector2(NavigationLeft + 200f, TabsY + TabsStrip - 70f)), new[]
                {
                    Beat.To(At(new Vector2(NavigationLeft + TabCenter(2), TabsY + TabsStrip)), null, Wide(100f)),
                    Beat.Click("tab", true),
                    Beat.To(At(new Vector2(NavigationRight + 140f, ColumnTop - LabelTop - FaqHeader)), null, Wide(280f)),
                    Beat.Click("faq", true)
                }, NavigationPace);
                Aim("tab", Captioned(strip, "Video"));
                Aim("faq", Named(faq, "Header"));
            }

            private static float TabCenter(int index) => 320f / 6f * (index * 2 + 1);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var tab = played >= ClickAt("tab") ? 2 : 0;
                if (!tabs[tab].On) tabs[tab].On = true;

                var open = played >= ClickAt("faq");
                if (question.On != open) question.On = open;
            }

            private const float TabsY = ColumnTop - LabelTop - 100f;
            private const float TabsStrip = 84f;
            private const float FaqHeader = 20f;
        }

        /// <summary>
        /// Ready-made navigation, the second card: the hub's news swiped to its next page and its store pushed to a
        /// bundle's details and back.
        /// </summary>
        private sealed class NewsPart : KitPart
        {
            private readonly UniLayoutScroll news;
            private readonly UniShapeValue dot;
            private readonly System.Collections.Generic.List<UniText> headlines = new();
            private readonly UniScreens store;
            private readonly Surface details;

            public NewsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "News", NavigationBoard, NavigationScale)
            {
                Label("News & store", new Vector2(NavigationLeft, NavigationTop - 34f), 24f, "Ink", display: true);

                Label("News", new Vector2(NavigationLeft, ColumnTop), 14f, "InkDim");
                var pager = Add("Pager", new Vector2(NavigationLeft, PagerY), 0f);
                Kit.Relabel(pager, "Page 1", "Patch 4.2 is live");
                Kit.Relabel(pager, "Page 2", "Double XP weekend");
                Kit.Relabel(pager, "Page 3", "New raid: Ember Keep");
                news = pager.GetComponentInChildren<UniPager>(true).GetComponent<UniLayoutScroll>();
                foreach (var value in pager.GetComponentsInChildren<UniShapeValue>(true))
                    if (value.name == "Dot")
                        dot = value;
                if (dot == null) throw new InvalidOperationException("[Promo] The pager no longer has a page dot.");
                var angle = 20f;
                foreach (var text in pager.GetComponentsInChildren<UniText>(true))
                {
                    if (text.transform.parent == null || !text.transform.parent.name.StartsWith("Page")) continue;
                    Banner(text.transform.parent, BannerInset, angle);
                    text.FontSize = BannerText;
                    text.Font = scene.theme.DisplayFace;
                    headlines.Add(text);
                    angle += 60f;
                }

                Label("Store", new Vector2(NavigationRight, ColumnTop), 14f, "InkDim");
                var screens = Add("Screens", new Vector2(NavigationRight, StoreY), 0f);
                Kit.Relabel(screens, "Home", "Bundles");
                Kit.Relabel(screens, "Details", "Starter bundle · 4.99");
                store = screens.GetComponentInChildren<UniScreens>(true);
                details = Find(screens, "Details");

                Plan(At(new Vector2(NavigationLeft + 200f, PagerY - 140f)), new[]
                {
                    Beat.To(At(new Vector2(NavigationLeft + 230f, PagerY)), null, Wide(160f)),
                    Beat.Drag(At(new Vector2(NavigationLeft + 80f, PagerY)), "swipe", Wide(160f)),
                    Beat.To(At(new Vector2(NavigationRight + ActionX, StoreY + ActionY)), null, Wide(ActionWidth)),
                    Beat.Click("push", true),
                    Beat.Wait(DetailsRead),
                    Beat.Click("back", true)
                }, NavigationPace);
                Aim("swipe", Named(pager, "Pages"));
                Aim("push", Captioned(screens, "Next"));
                Aim("back", Captioned(screens, "Back"));
            }

            private static Surface Find(GameObject root, string screen)
            {
                foreach (var surface in root.GetComponentsInChildren<Surface>(true))
                    if (surface.name == screen)
                        return surface;
                throw new InvalidOperationException($"[Promo] The screens control has no '{screen}' screen.");
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var swipe = Pointer.Timeline.Span("swipe");
                var held = Dragged("swipe", played, At(new Vector2(NavigationLeft + 230f, PagerY)), At(new Vector2(NavigationLeft + 80f, PagerY)));
                var settle = Motion.Snap.Window(played, swipe.y, PageSettle);
                var page = Mathf.Lerp(held * SwipeShare, 1f, settle);
                news.ScrollTo(new Vector2(PageWidth * page, 0f), false);
                dot.TrackTo(page / (PageCount - 1f));
                foreach (var headline in headlines) headline.color = Color.white;

                var pushed = played >= ClickAt("push") && played < ClickAt("back");
                var showing = store.Current == details;
                if (pushed && !showing) store.Push(details);
                if (!pushed && showing) store.Back();
            }

            private const float PagerY = ColumnTop - LabelTop - 100f;
            private const float StoreY = ColumnTop - LabelTop - 120f;
            private const float BannerInset = 6f;
            private const float BannerText = 24f;

            /// <summary>
            /// How wide a news page is, how much of it the finger's swipe carries before release, and how long the page
            /// takes to settle on the next one afterwards.
            /// </summary>
            private const float PageWidth = 320f;

            private const int PageCount = 3;
            private const float SwipeShare = 0.45f;
            private const float PageSettle = 0.35f;

            /// <summary>
            /// Where the store's action button stands from the store's left edge and middle — Next on the home screen and
            /// Back on the details, both at the bottom right of the page — and how wide it is.
            /// </summary>
            private const float ActionX = 270f;

            private const float ActionY = -80f;
            private const float ActionWidth = 140f;

            /// <summary>How long the pushed details stay on screen, settled, before the pointer goes back.</summary>
            private const float DetailsRead = 0.7f;
        }

        private static readonly Vector2 NavigationBoard = new(1560f, 720f);
        private const float NavigationScale = 2.1f;
        private const float NavigationPace = 1.4f;
        private const float NavigationTop = 720f / NavigationScale * 0.5f;
        private const float NavigationLeft = -1560f / NavigationScale * 0.5f + Pad;
        private const float NavigationRight = NavigationLeft + 320f + 48f;
        private const float ColumnTop = NavigationTop - 76f;
    }
}
