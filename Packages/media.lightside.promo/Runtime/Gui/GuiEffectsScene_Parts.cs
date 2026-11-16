using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>The beats of <see cref="GuiEffectsScene"/>.</summary>
    public sealed partial class GuiEffectsScene
    {
        /// <summary>
        /// A reward card through the kit's effect stack beside tabs of its effects: each tab pages the kit's pager to a page
        /// that says what the effect does and holds a slider per parameter, and the card follows the sliders as the pointer
        /// drags them — a shine placed, turned and swept off, a burn eating the card with its glowing edge widened and drawn back, a
        /// pixelation coarsening it and clearing, its hues turned and saturated.
        /// </summary>
        /// <remarks>
        /// Every slider stands where the pointer's drags have taken it by the moment posed, and the pager on the page of the
        /// last tab pressed, gliding there only just after the press: the board looks the same however the film is sought,
        /// a rebuild part-way through included.
        /// </remarks>
        private sealed class StackPart : KitPart
        {
            public StackPart(GuiEffectsScene scene, Stage stage) : base(scene, stage, "Stack", Board, Scale)
            {
                var card = stage.Node("Card", Holder);
                stage.Box(card, CardAt, CardSize);
                var face = stage.Shape("Face", card, ShapeKind.RoundedRect, 22f);
                stage.Box(face.Rect, Vector2.zero, CardSize);
                Stage.Solid(face.Fill, scene.palette["Raised"]);
                Stage.AddStroke(face.Shape, scene.palette["Line"], 1f, -1f);
                var art = stage.Shape("Art", card, ShapeKind.RoundedRect, 16f);
                stage.Box(art.Rect, new Vector2(0f, 70f), new Vector2(CardSize.x - 32f, 200f));
                Stage.Linear(art.Fill, Ember, Plum, 315f);
                GuiStylesScene.Glyphs.Sword(GuiStylesScene.Glyphs.Plain(stage), art.Rect, Vector2.zero, BladeSize, Ember, Gold);
                var title = stage.Label(card, "Ember Blade", 26f, scene.palette["Ink"], HorizontalAlignment.Left, stretch: false,
                    face: scene.theme.DisplayFace);
                stage.Box(title.rectTransform, new Vector2(0f, -78f), new Vector2(CardSize.x - 36f, 34f));
                var kind = stage.Label(card, "Legendary · Sword", 16f, scene.palette["InkDim"], HorizontalAlignment.Left, stretch: false);
                stage.Box(kind.rectTransform, new Vector2(0f, -110f), new Vector2(CardSize.x - 36f, 24f));
                var power = stage.Label(card, "+42 Attack", 18f, scene.palette["Accent"], HorizontalAlignment.Left, stretch: false);
                stage.Box(power.rectTransform, new Vector2(0f, -146f), new Vector2(CardSize.x - 36f, 26f));

                var effects = card.gameObject.AddComponent<UniEffects>();
                hue = new HueRotateFilter();
                saturation = new SaturationFilter();
                shine = new ShinyEffect { Width = 0.16f, Color = new Color(1f, 0.95f, 0.85f, 0.85f) };
                dissolve = new DissolveEffect { EdgeColor = new Color(1f, 0.55f, 0.12f, 1f) };
                pixelate = new PixelateEffect();
                effects.Effects.Add(new ColorAdjustmentEffect { Filter = hue });
                effects.Effects.Add(new ColorAdjustmentEffect { Filter = saturation });
                effects.Effects.Add(dissolve);
                effects.Effects.Add(pixelate);
                effects.Effects.Add(shine);

                var tabsControl = Add("Tabs", TabsAt);
                tabsControl.transform.Find("Pages").gameObject.SetActive(false);
                tabsControl.GetComponent<UniLayout>().Height = LayoutSize.Fixed;
                ((RectTransform)tabsControl.transform).sizeDelta = new Vector2(TabStep * Pages.Length + 20f, StripHeight);
                var strip = tabsControl.transform.Find("Strip");
                var last = strip.GetComponentsInChildren<UniToggle>(true)[^1].gameObject;
                while (strip.GetComponentsInChildren<UniToggle>(true).Length < Pages.Length)
                    Object.Instantiate(last, last.transform.parent, false);
                tabs = strip.GetComponentsInChildren<UniToggle>(true);
                for (var i = 0; i < Pages.Length; i++)
                {
                    var caption = tabs[i].GetComponentInChildren<UniText>(true);
                    caption.Text = Pages[i].name;
                    caption.FontSize = TabText;
                    tabs[i].Shows = null;
                }

                var pagerControl = Add("Pager", PagerAt);
                ((RectTransform)pagerControl.transform).sizeDelta = PagerSize;
                pager = pagerControl.GetComponentInChildren<UniPager>(true);
                var pagesRect = (RectTransform)pager.transform;
                pagesRect.sizeDelta = new Vector2(pagesRect.sizeDelta.x, PageSize.y);
                var model = pagesRect.GetChild(2);
                for (var i = 3; i < Pages.Length; i++)
                    Object.Instantiate(model, pagesRect, false).SetSiblingIndex(i);
                var rows = new System.Collections.Generic.List<GameObject>();
                for (var i = 0; i < Pages.Length; i++)
                {
                    var page = (RectTransform)pagesRect.GetChild(i);
                    page.sizeDelta = PageSize;
                    page.Find("Label").gameObject.SetActive(false);
                    var line = stage.Label(page, Pages[i].note, 20f, scene.palette["Ink"], HorizontalAlignment.Left, stretch: false);
                    line.WordWrap = true;
                    stage.Box(line.rectTransform, new Vector2(0f, NoteY), new Vector2(PageSize.x - 40f, 56f));
                    foreach (var row in Rows)
                    {
                        if (row.page != i) continue;
                        var y = RowY(rows.Count);
                        var caption = stage.Label(page, row.caption, 16f, scene.palette["InkDim"], HorizontalAlignment.Left, stretch: false);
                        stage.Box(caption.rectTransform, new Vector2(CaptionX + CaptionWidth * 0.5f, y), new Vector2(CaptionWidth, 26f));
                        var slider = Kit.Spawn(Kit.Find(scene.kit, "Slider"), page, scene.palette);
                        Kit.Place(slider, new Vector2(SliderX, y));
                        ((RectTransform)slider.transform).sizeDelta = new Vector2(SliderWidth, 28f);
                        rows.Add(slider);
                    }
                }

                sliders = new UniShapeValue[rows.Count];
                for (var i = 0; i < rows.Count; i++) sliders[i] = rows[i].GetComponentInChildren<UniShapeValue>(true);
                var beats = new System.Collections.Generic.List<Beat>();
                var tab = 0;
                foreach (var drag in Drags)
                {
                    if (Rows[drag.row].page != tab)
                    {
                        tab = Rows[drag.row].page;
                        beats.Add(Beat.Wait(0.15f));
                        beats.Add(Beat.To(At(Tab(tab)), null, Wide(TabStep)));
                        beats.Add(Beat.Click(TabId(tab), true));
                        beats.Add(Beat.Wait(PageRead));
                    }

                    var from = drag.from ?? Rows[drag.row].start;
                    beats.Add(Beat.To(At(Thumb(drag.row, from)), null, Wide(28f)));
                    beats.Add(Beat.Drag(At(Thumb(drag.row, drag.to)), drag.id, Wide(28f), drag.hold));
                }

                beats.Add(Beat.Wait(0.4f));
                Plan(At(PagerAt + new Vector2(0f, -170f)), beats.ToArray(), Pace);
                for (var i = 1; i < Pages.Length; i++) Aim(TabId(i), Captioned(strip.gameObject, Pages[i].name));
            }

            private readonly HueRotateFilter hue;
            private readonly SaturationFilter saturation;
            private readonly ShinyEffect shine;
            private readonly DissolveEffect dissolve;
            private readonly PixelateEffect pixelate;
            private readonly UniToggle[] tabs;
            private readonly UniPager pager;
            private readonly UniShapeValue[] sliders;

            private static string TabId(int page) => Pages[page].name + " tab";

            /// <summary>Where row <paramref name="row"/> stands on its page: two rows share a page, a lone one sits between them.</summary>
            private static float RowY(int row)
            {
                var page = Rows[row].page;
                var first = row > 0 && Rows[row - 1].page == page;
                var shared = first || row + 1 < Rows.Length && Rows[row + 1].page == page;
                return shared ? first ? LowerRowY : UpperRowY : (UpperRowY + LowerRowY) * 0.5f;
            }

            /// <summary>Where row <paramref name="row"/>'s slider thumb stands at <paramref name="value"/>, with its page in view.</summary>
            private static Vector2 Thumb(int row, float value) =>
                PagerAt + PageOffset + new Vector2(SliderX - SliderWidth * 0.5f + ThumbInset + (SliderWidth - ThumbInset * 2f) * value, RowY(row));

            private static Vector2 Tab(int index) => TabsAt + new Vector2((index - (Pages.Length - 1) * 0.5f) * TabStep, 0f);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var page = 0;
                var pressed = float.NegativeInfinity;
                for (var i = 1; i < Pages.Length; i++)
                {
                    if (played < ClickAt(TabId(i))) continue;
                    page = i;
                    pressed = ClickAt(TabId(i));
                }

                for (var i = 0; i < tabs.Length; i++)
                    if (tabs[i].On != (i == page)) tabs[i].On = i == page;
                pager.GoTo(page, played - pressed < PageGlide);

                for (var i = 0; i < sliders.Length; i++) sliders[i].TrackTo(Slid(i, played));
                shine.Position = Mathf.Lerp(-0.3f, 1.3f, sliders[0].Value);
                shine.Angle = Mathf.Lerp(-36f, 84f, sliders[1].Value);
                dissolve.Progress = sliders[2].Value * DissolveDepth;
                dissolve.EdgeWidth = Mathf.Lerp(0.02f, 0.3f, sliders[3].Value);
                pixelate.Size = Mathf.Round(sliders[4].Value * PixelMax);
                hue.Degrees = Mathf.Lerp(-180f, 180f, sliders[5].Value);
                saturation.Amount = Mathf.Lerp(0f, 2f, sliders[6].Value);
            }

            /// <summary>Where row <paramref name="row"/>'s slider stands at <paramref name="played"/>: its start, then each of its drags in turn as far as the pointer has taken it.</summary>
            private float Slid(int row, float played)
            {
                var value = Rows[row].start;
                foreach (var drag in Drags)
                {
                    if (drag.row != row) continue;
                    if (played < Pointer.Timeline.Span(drag.id).x) break;
                    var from = drag.from ?? value;
                    value = Mathf.Lerp(from, drag.to, Dragged(drag.id, played, At(Thumb(row, from)), At(Thumb(row, drag.to))));
                }

                return value;
            }

            private static readonly (string name, string note)[] Pages =
            {
                ("Shine", "A band of light, placed and turned anywhere."),
                ("Dissolve", "Burns the card away behind a glowing edge."),
                ("Pixelate", "Coarsens it into pixels, text and all."),
                ("Color", "Turns its hues and saturation, live.")
            };

            private static readonly (int page, string caption, float start)[] Rows =
            {
                (0, "Position", 0f), (0, "Angle", 0.5f),
                (1, "Burn", 0f), (1, "Edge", 0.3f),
                (2, "Pixel size", 0f),
                (3, "Hue", 0.5f), (3, "Saturation", 0.5f)
            };

            private static readonly (int row, string id, float? from, float to, float hold)[] Drags =
            {
                (0, "shine position", null, 0.5f, 0f),
                (1, "shine angle", null, 0.9f, 0.2f),
                (0, "shine away", 0.5f, 1f, 0f),
                (2, "burn", null, 0.7f, 0f),
                (3, "edge", null, 1f, 0.3f),
                (2, "burn back", 0.7f, 0f, 0f),
                (4, "pixel", null, 1f, 0.4f),
                (4, "pixel back", 1f, 0f, 0f),
                (5, "hue", null, 0.9f, 0f),
                (6, "saturation", null, 1f, 0.3f)
            };

            private static readonly Vector2 Board = new(1600f, 756f);
            private const float Scale = 1.85f;
            private static readonly Vector2 CardAt = new(-245f, 0f);
            private static readonly Vector2 CardSize = new(300f, 380f);
            private const float BladeSize = 170f;
            private static readonly Vector2 TabsAt = new(195f, 120f);
            private const float TabStep = 100f;
            private const float TabText = 17f;
            private const float StripHeight = 36f;
            private static readonly Vector2 PagerAt = new(195f, -28f);
            private static readonly Vector2 PagerSize = new(420f, 220f);
            private static readonly Vector2 PageSize = new(420f, 200f);

            /// <summary>Where a page's middle stands from the pager's while the page is in view: the pages hang from its top.</summary>
            private static readonly Vector2 PageOffset = new(0f, PagerSize.y * 0.5f - PageSize.y * 0.5f);

            private const float NoteY = 52f;
            private const float UpperRowY = -12f;
            private const float LowerRowY = -60f;
            private const float CaptionX = -190f;
            private const float CaptionWidth = 110f;
            private const float SliderX = 50f;
            private const float SliderWidth = 260f;
            private const float ThumbInset = 6f;
            private const float PageRead = 0.4f;

            /// <summary>How long after its tab is pressed a page glides into view; past it the pager stands on the page.</summary>
            private const float PageGlide = 0.6f;
            private const float DissolveDepth = 0.9f;
            private const float PixelMax = 24f;
            private const float Pace = 1.3f;
        }

        /// <summary>
        /// A pause menu of the kit's glass panel over the game, its blur of everything behind it turned up live by a slider
        /// in the menu; the section dives into the glass.
        /// </summary>
        private sealed class GlassPart : KitPart
        {
            public GlassPart(GuiEffectsScene scene, Stage stage) : base(scene, stage, "Glass", Board, Scale)
            {
                Scenery(stage, Board / Scale);
                for (var i = 0; i < Sparks.Length; i++)
                {
                    var spark = stage.Shape("Spark", Holder, i % 2 == 0 ? ShapeKind.Star : ShapeKind.Circle);
                    stage.Box(spark.Rect, Sparks[i], Vector2.one * (i % 3 == 0 ? 46f : 28f));
                    Stage.Solid(spark.Fill, i % 2 == 0 ? new Color(1f, 0.85f, 0.4f, 0.95f) : new Color(1f, 0.45f, 0.55f, 0.9f));
                }

                glass = Add("Glass Panel", Vector2.zero);
                ((RectTransform)glass.transform).sizeDelta = GlassSize;
                blur = (BlurEffect)glass.GetComponent<UniBackdrop>().Effects[0];
                var title = stage.Label(glass.transform, "Paused", 30f, scene.palette["Ink"], HorizontalAlignment.Center, stretch: false,
                    face: scene.theme.DisplayFace);
                stage.Box(title.rectTransform, new Vector2(0f, GlassSize.y * 0.5f - 44f), new Vector2(GlassSize.x, 40f));
                var note = stage.Label(glass.transform, "Background blur", 18f, scene.palette["Ink"], HorizontalAlignment.Left, stretch: false);
                stage.Box(note.rectTransform, new Vector2(0f, 20f), new Vector2(GlassSize.x - 60f, 22f));
                slider = Add("Slider", SliderAt);
                ((RectTransform)slider.transform).sizeDelta = new Vector2(GlassSize.x - 60f, 28f);
                level = slider.GetComponentInChildren<UniShapeValue>(true);
                var resume = Add("Button", new Vector2(-80f, -GlassSize.y * 0.5f + 46f));
                Kit.Relabel(resume, "Button", "Resume");
                var quit = Add("Button", new Vector2(80f, -GlassSize.y * 0.5f + 46f));
                Kit.Relabel(quit, "Button", "Quit");

                var from = SliderAt + new Vector2(-(GlassSize.x - 60f) * 0.5f + 6f, 0f);
                Plan(At(new Vector2(260f, -260f)), new[]
                {
                    Beat.To(At(from), null, Wide(28f)),
                    Beat.Drag(At(from + new Vector2(GlassSize.x - 72f, 0f)), "blur", Wide(28f)),
                    Beat.Wait(0.6f)
                }, Pace);
                Aim("blur", slider);
                grip = new Grip(slider, "blur");
            }

            private readonly GameObject glass;
            private readonly GameObject slider;
            private readonly BlurEffect blur;
            private readonly UniShapeValue level;
            private readonly Grip grip;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (played < Pointer.Timeline.Span("blur").x) level.Value = 0f;
                Pose(grip, played);
                blur.Radius = Mathf.Lerp(BlurFrom, BlurTo, Mathf.Clamp01(level.Value));
                Dive(played, At(((RectTransform)glass.transform).anchoredPosition));
            }

            private static readonly Vector2 Board = new(1760f, 756f);
            private const float Scale = 1.7f;
            private static readonly Vector2 GlassSize = new(460f, 300f);
            private static readonly Vector2 SliderAt = new(0f, -10f);
            private const float BlurFrom = 1f;
            private const float BlurTo = 22f;
            private const float Pace = 1.1f;

            private static readonly Vector2[] Sparks =
            {
                new(-420f, 150f), new(-300f, 40f), new(-180f, 170f), new(-60f, 60f), new(70f, 160f), new(180f, 30f),
                new(300f, 150f), new(420f, 60f), new(-360f, -120f), new(380f, -110f)
            };
        }

        private static readonly Color Ember = new Color32(0xFF, 0x8A, 0x1F, 0xFF);
        private static readonly Color Plum = new Color32(0x6B, 0x2A, 0x4A, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xC8, 0x4A, 0xFF);
    }
}
