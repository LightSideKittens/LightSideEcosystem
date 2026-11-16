using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>The beats of <see cref="GuiMotionScene"/>.</summary>
    public sealed partial class GuiMotionScene
    {
        /// <summary>The curves the section's animations arrive on: an elastic spring and a back overshoot.</summary>
        private static readonly Ease ElasticOut = Ease.Of(EasingType.ElasticOut);

        private static readonly Ease BackOut = Ease.Of(EasingType.BackOut);

        /// <summary>A Lottie animation on the board, posed from the time it started: looping, or played through once.</summary>
        private sealed class Clip
        {
            public Clip(LottieAsset asset, RectTransform parent, Vector2 at, float size)
            {
                view = UniLottie.Create(asset, parent, 0f, false);
                view.gameObject.layer = parent.gameObject.layer;
                Rect = (RectTransform)view.transform;
                Rect.anchorMin = Rect.anchorMax = new Vector2(0.5f, 0.5f);
                Rect.sizeDelta = Vector2.one * size;
                Rect.anchoredPosition = at;
                duration = Mathf.Max(0.01f, asset.Duration);
            }

            private readonly UniLottie view;
            private readonly float duration;

            public RectTransform Rect { get; }

            /// <summary>Whether the animation is drawn mirrored left to right.</summary>
            public bool Mirrored { get; set; }

            /// <summary>Shows the animation <paramref name="since"/> seconds after it is called for, as <see cref="Stage.Appear"/> does.</summary>
            public void Appear(float since, Ease ease, float growFor) => Stage.Appear(Rect, since, ease, growFor, Mirrored);

            /// <summary>Fades the animation out over <paramref name="seconds"/> from <paramref name="from"/> seconds into its showing.</summary>
            public void Fade(float since, float from, float seconds)
            {
                var group = Rect.GetComponent<CanvasGroup>();
                group.alpha = Mathf.Min(group.alpha, 1f - Mathf.SmoothStep(0f, 1f, (since - from) / seconds));
            }

            /// <summary>Poses the animation <paramref name="seconds"/> into its playing: round and round, or held on its last frame.</summary>
            public void Pose(float seconds, bool loop)
            {
                var t = Mathf.Max(0f, seconds) / duration;
                view.manager.SeekProgress(loop ? t - Mathf.Floor(t) : Mathf.Min(t, 0.999f));
            }
        }

        /// <summary>
        /// A guild chat with a reaction picker open over its last message: the pointer runs along the living emoji, each
        /// swelling as it passes, and picks one, which bursts where it was picked as the picker closes, then flies into
        /// the message's reaction and counts it.
        /// </summary>
        private sealed class ReactionsPart : KitPart
        {
            public ReactionsPart(GuiMotionScene scene, Stage stage) : base(scene, stage, "Reactions", Board, Scale)
            {
                Label("Guild chat", new Vector2(Left, Top - 22f), 24f, "Ink", display: true);
                Label("4 online", new Vector2(-Left, Top - 22f), 16f, "InkDim", 1f);
                Divider(new Vector2(0f, Top - 50f), Width);
                Message(stage, scene, new Vector2(Left, FirstY), "A", "Aurora", "Raid at nine? We need a healer!", 35f);
                Message(stage, scene, new Vector2(Left, LastY), "M", "Mia", "Count me in. Bringing potions.", 210f);

                chip = stage.Shape("Reaction", Holder, ShapeKind.Capsule);
                stage.Box(chip.Rect, ChipAt, ChipSize);
                Stage.Solid(chip.Fill, scene.palette["Raised"]);
                Stage.AddStroke(chip.Shape, scene.palette["Line"], 1f, -1f);
                mine = stage.Shape("Mine", Holder, ShapeKind.Capsule);
                stage.Box(mine.Rect, ChipAt, ChipSize);
                Stage.Solid(mine.Fill, Theme.Fade(scene.palette["Accent"], 0.24f));
                Stage.AddStroke(mine.Shape, scene.palette["Accent"], 1.5f, -1f);
                chipIcon = new Clip(scene.reactions[Picked], Holder, ChipAt + IconOffset, ChipIcon);
                chipCount = Label("2", ChipAt + CountOffset, 20f, "Ink", 0.5f, display: true);

                var field = Add("Text Field", new Vector2(0f, FieldY));
                Size(field, new Vector2(Width, FieldHeight));
                Kit.Relabel(field, "Enter text", "Message the guild");

                bar = stage.Shape("Reaction Bar", Holder, ShapeKind.Capsule);
                stage.Box(bar.Rect, BarAt, new Vector2(ReactionStep * GuiMotionScene.ReactionCount + 24f, 66f));
                Stage.Solid(bar.Fill, scene.palette["Raised"]);
                Stage.AddStroke(bar.Shape, scene.palette["Line"], 1f, -1f);
                Stage.AddShadow(bar.Shape, new Color(0f, 0f, 0f, 0.35f), new Vector2(0f, -6f), 18f);
                for (var i = 0; i < clips.Length; i++)
                {
                    clips[i] = new Clip(scene.reactions[i], Holder, ReactionAt(i), ReactionSize);
                    clips[i].Rect.gameObject.AddComponent<Interactable>();
                }
                pop = new Clip(scene.reactions[Picked], Holder, ReactionAt(Picked), PopSize);

                var beats = new System.Collections.Generic.List<Beat> { Beat.To(At(ReactionAt(0) + new Vector2(0f, -10f)), null, Wide(ReactionSize)) };
                for (var i = 1; i <= Picked + 1; i++) beats.Add(Beat.To(At(ReactionAt(i)), null, Wide(ReactionSize)));
                beats.Add(Beat.To(At(ReactionAt(Picked)), null, Wide(ReactionSize)));
                beats.Add(Beat.Click("react", true, true));
                beats.Add(Beat.Wait(Landed + 0.6f));
                Plan(At(ReactionAt(0) + new Vector2(-120f, -150f)), beats.ToArray(), Pace);
                Aim("react", clips[Picked].Rect);
            }

            private readonly Clip[] clips = new Clip[GuiMotionScene.ReactionCount];
            private readonly Clip pop;
            private readonly Clip chipIcon;
            private readonly Widget bar;
            private readonly Widget chip;
            private readonly Widget mine;
            private readonly UniText chipCount;

            /// <summary>A message: the sender's avatar, name and words in a bubble.</summary>
            private void Message(Stage stage, GuiMotionScene scene, Vector2 at, string initial, string sender, string text, float angle)
            {
                Avatar(at + new Vector2(26f, 0f), 48f, initial, angle);
                var bubble = stage.Shape("Bubble", Holder, ShapeKind.RoundedRect, 18f);
                stage.Box(bubble.Rect, at + new Vector2(70f + BubbleWidth * 0.5f, 0f), new Vector2(BubbleWidth, BubbleHeight));
                Stage.Solid(bubble.Fill, scene.palette["Raised"]);
                Label(sender, at + new Vector2(90f, 15f), 16f, "Accent", display: true);
                Label(text, at + new Vector2(90f, -12f), 19f, "Ink");
            }

            private static Vector2 ReactionAt(int index) =>
                BarAt + new Vector2((index - (GuiMotionScene.ReactionCount - 1) * 0.5f) * ReactionStep, 0f);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var at = Mathf.Max(0f, played);
                var since = played - ClickAt("react");
                var reacted = since >= 0f;
                var tip = Native(Pointer.Timeline.Sample(at).Position);
                var open = reacted ? 1f - Ease.EmphasizedIn.Evaluate(Mathf.Clamp01(since / Close)) : 1f;
                bar.Rect.localScale = Vector3.one * open;
                bar.Rect.gameObject.SetActive(open > 0f);
                for (var i = 0; i < clips.Length; i++)
                {
                    clips[i].Pose(at + i * 0.37f, true);
                    var near = 1f - Mathf.Clamp01((tip - ReactionAt(i)).magnitude / (ReactionStep * 0.9f));
                    clips[i].Rect.localScale = Vector3.one * (1f + Swell * Mathf.SmoothStep(0f, 1f, near)) * open;
                    clips[i].Rect.anchoredPosition = BarAt + (ReactionAt(i) - BarAt) * open;
                    clips[i].Rect.gameObject.SetActive(open > 0f && (i != Picked || !reacted));
                }

                chipIcon.Pose(at, true);
                var landed = since - Landed;
                var counted = landed >= 0f;
                var count = counted ? "3" : "2";
                if (chipCount.Text != count) chipCount.Text = count;
                var bump = counted ? 1f + Bump * Mathf.Sin(Mathf.PI * Mathf.Clamp01(landed / BumpFor)) : 1f;
                chip.Rect.localScale = Vector3.one * bump;
                chipCount.rectTransform.localScale = Vector3.one * bump;
                chipIcon.Rect.localScale = Vector3.one * bump;
                Stage.Appear(mine.Rect, counted ? landed : -1f, Motion.Back, BumpFor);
                if (counted) mine.Rect.localScale *= bump;

                pop.Appear(reacted && !counted ? since : -1f, BackOut, PopGrow);
                if (!reacted || counted) return;
                pop.Pose(since, false);
                var flight = Ease.EmphasizedIn.Evaluate(Mathf.Clamp01((since - Burst) / (Landed - Burst)));
                pop.Rect.anchoredPosition = Vector2.Lerp(ReactionAt(Picked), ChipAt + IconOffset, flight) + Vector2.up * (Arc * Mathf.Sin(Mathf.PI * flight));
                pop.Rect.localScale *= Mathf.Lerp(1f, ChipIcon / PopSize, flight);
            }

            private static readonly Vector2 Board = new(1190f, 740f);
            private const float Scale = 1.6f;
            private const float Pad = 28f;
            private const float Left = -1190f / Scale * 0.5f + Pad;
            private const float Top = 740f / Scale * 0.5f - Pad;
            private const float Width = -2f * Left;
            private const float FirstY = Top - 104f;
            private const float LastY = Top - 190f;
            private const float BubbleWidth = 400f;
            private const float BubbleHeight = 70f;
            private const float FieldY = -Top + FieldHeight * 0.5f;
            private const float FieldHeight = 46f;
            private static readonly Vector2 BarAt = new(Left + 330f, -98f);
            private static readonly Vector2 ChipAt = new(Left + 116f, LastY - BubbleHeight * 0.5f - 30f);
            private static readonly Vector2 ChipSize = new(86f, 42f);

            /// <summary>Where the reaction's icon and count stand from the chip's middle: the icon on the left half, the count on the right.</summary>
            private static readonly Vector2 IconOffset = new(-16f, 0f);

            private static readonly Vector2 CountOffset = new(17f, 1f);
            private const float ChipIcon = 32f;
            private const float ReactionStep = 66f;
            private const float ReactionSize = 52f;
            private const float Swell = 0.35f;
            private const int Picked = 2;

            /// <summary>
            /// How the pick plays out from the click: the picker closes, the picked emoji bursts where it was for a moment,
            /// then flies on an arc into the reaction, which counts it with a bump.
            /// </summary>
            private const float Close = 0.22f;

            private const float PopSize = 130f;
            private const float PopGrow = 0.3f;
            private const float Burst = 0.55f;
            private const float Landed = 0.95f;
            private const float Arc = -40f;
            private const float Bump = 0.18f;
            private const float BumpFor = 0.3f;
            private const float Pace = 0.9f;
        }

        /// <summary>
        /// A season's reward card: claiming it fires a party popper from each lower corner of the board and bursts its
        /// three stars one after another, and the claim button turns to the reward taken.
        /// </summary>
        private sealed class RewardPart : KitPart
        {
            public RewardPart(GuiMotionScene scene, Stage stage) : base(scene, stage, "Reward", Board, Scale)
            {
                for (var i = 0; i < poppers.Length; i++)
                {
                    var side = i == 0 ? -1f : 1f;
                    poppers[i] = new Clip(scene.rewards[0], Holder, new Vector2(side * PopperX, PopperY), PopperSize);
                    poppers[i].Mirrored = side > 0f;
                }
                Label("Season 4 complete", new Vector2(0f, 156f), 32f, "Ink", 0.5f, display: true);
                Label("You reached rank Ember in 42 matches.", new Vector2(0f, 116f), 18f, "InkDim", 0.5f);
                for (var i = 0; i < stars.Length; i++)
                {
                    var at = new Vector2((i - 1) * StarStep, i == 1 ? 26f : 6f);
                    var slot = stage.Shape("Star Slot", Holder, ShapeKind.Star);
                    stage.Box(slot.Rect, at, Vector2.one * 64f);
                    Stage.Solid(slot.Fill, scene.palette["Line"]);
                    stars[i] = new Clip(scene.rewards[1], Holder, at, i == 1 ? 130f : 110f);
                }

                claim = Add("Button", new Vector2(0f, ClaimY));
                Size(claim, new Vector2(280f, 50f));
                label = claim.GetComponentInChildren<UniText>(true);

                Plan(At(new Vector2(200f, -230f)), new[]
                {
                    Beat.To(At(new Vector2(0f, ClaimY)), null, Wide(280f)),
                    Beat.Click("claim", true, true),
                    Beat.To(At(new Vector2(150f, ClaimY - 70f)), null, Wide(280f)),
                    Beat.Wait(CelebrateFor)
                }, Pace);
                Aim("claim", claim);
            }

            private readonly Clip[] stars = new Clip[3];
            private readonly Clip[] poppers = new Clip[2];
            private readonly GameObject claim;
            private readonly UniText label;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var since = played - ClickAt("claim");
                var claimed = since >= 0f;
                var caption = claimed ? "Claimed" : "Claim reward";
                if (label.Text != caption) label.Text = caption;
                foreach (var popper in poppers)
                {
                    popper.Appear(since, BackOut, PopperGrow);
                    if (claimed) popper.Pose(since, false);
                }
                for (var i = 0; i < stars.Length; i++)
                {
                    var own = since - StarDelay * (i + 1);
                    stars[i].Appear(own, ElasticOut, StarGrow);
                    if (own >= 0f) stars[i].Pose(own, false);
                }
            }

            private static readonly Vector2 Board = new(1500f, 740f);
            private const float Scale = 1.6f;
            private const float ClaimY = -118f;
            private const float StarStep = 140f;
            private const float StarDelay = 0.28f;
            private const float StarGrow = 0.7f;
            private const float PopperGrow = 0.35f;
            private const float PopperX = 1500f / Scale * 0.5f - PopperSize * 0.5f - 10f;
            private const float PopperY = -740f / Scale * 0.5f + PopperSize * 0.5f + 10f;
            private const float PopperSize = 230f;
            private const float CelebrateFor = 2.2f;
            private const float Pace = 1f;
        }
    }
}
