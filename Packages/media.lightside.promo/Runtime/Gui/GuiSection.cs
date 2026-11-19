using System;
using System.Collections.Generic;
using UnityEngine;
using HAlign = LightSide.HorizontalAlignment;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    /// <summary>
    /// A section of the UniGUI reel as one continuous scene: a headline, and beats that each show one idea a viewer
    /// can name, each caused by a cursor on a labelled control beside a calm subject.
    /// </summary>
    /// <remarks>
    /// The headline opens the section as a title card in the middle of the frame and is whipped up into its band as
    /// the first beat is whipped up from below, one tilt of the camera. Its first word and the promise under it hold
    /// for the whole section; only the last word changes, thrown out as a beat leaves and slammed in as the next one
    /// lands.
    /// <para>
    /// How one beat gives way to the next says what changed (<see cref="Links"/>). A new subject arrives with a
    /// horizontal whip, the leaving beat and the arriving one a frame apart on one strip. A new property of the same
    /// subject is a match cut: the subject stays where it is and keeps every edit, and only the Inspector card is
    /// swapped, the old one thrown up as the next rises — the corner, outline and shadow beats all edit one panel. A
    /// section that ends in a dive ends as the dive begins: the camera's push into its last subject runs on through the
    /// next slide's entrance, which carries it into the next scene.
    /// </para>
    /// <para>
    /// A beat is either a real Inspector window driven by a recorder (<see cref="Takes"/>) or controls drawn by the
    /// section and operated by its pointer. While a beat's action plays the camera moves in on it (<see cref="Part.Films"/>),
    /// and the headline fades out of its way.
    /// </para>
    /// </remarks>
    public abstract partial class GuiSection : Slide
    {
        /// <summary>The recorded windows the section's beats play, in the order they play.</summary>
        protected readonly List<TakeShot> takes = new();

        protected Theme theme;
        protected Ramp brand;

        /// <summary>
        /// The brand ramp's hot end, coral to magenta: the paint for a surface that carries white text — a button, an
        /// own chat bubble, a lit chip — where the ramp's yellow start would wash the first letters out.
        /// </summary>
        protected Ramp hot;
        protected float frameWidth;
        protected float frameHeight;
        protected float contentCenter;
        protected float contentHeight;

        private RectTransform title;
        private CanvasGroup titleGroup;
        private RectTransform spotlight;
        private Vector2 titleAt;
        private Vector2 spotlightAt;
        private float titleCardLift;
        private GlyphReveal[] wordReveals;
        private GlyphReveal[] titleReveals;
        private RectTransform leadRect;
        private RectTransform[] wordRects;
        private float leadEnd;
        private float slot;

        /// <summary>
        /// How far the headline's line moves for each word so that it stays centered: half the difference between the
        /// word's width and the first word's.
        /// </summary>
        private float[] wordShift;

        private Part[] parts;

        /// <summary>The headline's first word, which holds through the section.</summary>
        protected abstract string Lead { get; }

        /// <summary>The headline's last word for each beat in order.</summary>
        protected abstract IReadOnlyList<string> Words { get; }

        /// <summary>The section's promise, under the headline for the whole section.</summary>
        protected abstract string Promise { get; }

        /// <summary>How each beat is entered, in order, and finally how the section is left: one more than the beats.</summary>
        protected abstract IReadOnlyList<Link> Links { get; }

        /// <summary>The section's beats, in the order they play; as many as <see cref="Words"/>.</summary>
        protected IReadOnlyList<Part> Beats => parts;

        /// <summary>Builds the section's beats, in the order they play, once the headline has reserved its band.</summary>
        protected abstract Part[] BuildParts(Stage stage);

        /// <summary>Poses whatever the section shows outside its beats, at <paramref name="local"/>.</summary>
        protected virtual void PoseScene(float local)
        {
        }

        /// <summary>The ways one composition gives way to the next.</summary>
        protected enum Link
        {
            /// <summary>The first composition rises in from below as the title card rises out of the middle.</summary>
            Rise,

            /// <summary>A new subject: the leaving composition is whipped off to the left as the next arrives from the right.</summary>
            Whip,

            /// <summary>The same subject, a new property: the subject stays, and only its card is thrown up for the next one.</summary>
            Match,

            /// <summary>The section's end: the camera dives into the last subject, on into the next slide's entrance.</summary>
            Dive,

            /// <summary>The next card of a deck: it slides in over the leaving one, which sinks back and fades out under it.</summary>
            Push,

            /// <summary>The next composition drops in from above and lands with a bounce; the leaving one falls away below.</summary>
            Drop,

            /// <summary>The camera moves through the leaving composition, which swells and fades, onto the next, which grows out of the distance.</summary>
            Zoom
        }

        /// <summary>The scene's recorded windows, in the order they play.</summary>
        public IReadOnlyList<TakeShot> Takes => takes;

        protected sealed override void OnBuild(Stage stage)
        {
            if (Links.Count != Words.Count + 1)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' has {Words.Count} words and {Links.Count} links; it needs one link more than words.");

            foreach (var take in takes) take.Dispose();
            takes.Clear();

            theme = stage.Theme;
            brand = theme.Brand;
            hot = new Ramp(new[] { new Stop(0f, theme.Coral), new Stop(1f, theme.Magenta) }, Interp.Perceptual);
            frameWidth = stage.Width;
            frameHeight = stage.Height;
            stage.Backdrop(stage.Root);
            var light = stage.Shape("Spotlight", stage.Root, ShapeKind.Ellipse);
            spotlight = light.Rect;
            spotlightAt = new Vector2(0f, stage.Half.y * 0.92f);
            stage.Box(spotlight, spotlightAt, new Vector2(stage.Width * 0.9f, stage.Height * 1.1f));
            Stage.Radial(light.Fill, Theme.Fade(theme.Orange, SpotlightStrength), Theme.Fade(theme.Orange, 0f));

            BuildTitle(stage);
            contentCenter = stage.ContentCenter;
            contentHeight = stage.ContentHeight;

            parts = BuildParts(stage);
            if (parts.Length != Words.Count)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' built {parts.Length} beats for {Words.Count} words; it needs one beat per word.");
            impacts = new Impacts(stage, stage.Root);
            title.SetAsLastSibling();
            foreach (var part in parts) part.BuildPointer(stage);

            Schedule();
            BuildHits();
        }

        /// <summary>
        /// The headline and the promise under it, as one node centered on the block the title card shows: the lead
        /// right-aligned before the word slot, every beat's word left-aligned in it.
        /// </summary>
        /// <remarks>
        /// <see cref="Stage.Claim"/> lays the section's first line out and reserves the band; its labels then carry the
        /// lead and the promise. Widths come from the labels' own layout, so the title card is centered exactly and the
        /// lead stands at the same point under every word.
        /// </remarks>
        private void BuildTitle(Stage stage)
        {
            var lead = Lead;
            var words = Words;
            var promise = Promise;
            var count = words.Count;
            var claim = stage.Claim(stage.Root, lead + " " + words[0], promise, size: HeadlineSize);
            var head = claim.Headline;
            var note = claim.Sub;
            var full = Measure(head);

            title = stage.Node("Title", stage.Root);
            titleGroup = title.gameObject.AddComponent<CanvasGroup>();
            titleAt = head.rectTransform.anchoredPosition;
            stage.Box(title, titleAt, new Vector2(full, HeadlineSize * 1.5f));
            var noteAt = note.rectTransform.anchoredPosition - titleAt;
            titleCardLift = -noteAt.y * 0.5f;

            head.Text = lead;
            var leadWidth = Measure(head);
            head.HorizontalAlignment = HAlign.Right;

            var labels = new UniText[count];
            wordReveals = new GlyphReveal[count];
            for (var i = 0; i < count; i++)
            {
                var word = stage.Label(title, words[i], HeadlineSize, theme.Text, HAlign.Left, stretch: false,
                    face: theme.DisplayFace);
                word.WordWrap = false;
                word.Styles.Add(Style.Range(stage.BrandFill(), 0, words[i].Length));
                labels[i] = word;
                wordReveals[i] = stage.Reveal(word, 0, words[i].Length, new SlamRevealHandler(SlamHeight), WordSpread);
            }

            var firstWidth = Measure(labels[0]);
            var space = full - leadWidth - firstWidth;
            leadEnd = -full * 0.5f + leadWidth;
            slot = leadEnd + space;
            leadRect = head.rectTransform;
            wordRects = new RectTransform[count];
            wordShift = new float[count];
            for (var i = 0; i < count; i++)
            {
                wordShift[i] = (firstWidth - Measure(labels[i])) * 0.5f;
                wordRects[i] = labels[i].rectTransform;
                Pin(wordRects[i], title, new Vector2(slot, 0f), 0f, HeadlineSize * 1.5f);
            }

            Pin(leadRect, title, new Vector2(leadEnd, 0f), 1f, HeadlineSize * 1.5f);
            Pin(note.rectTransform, title, noteAt, 0.5f, theme.Body * 1.6f);

            titleReveals = new[]
            {
                stage.Reveal(head, 0, lead.Length, new SlamRevealHandler(SlamHeight), WordSpread),
                stage.Reveal(note, 0, promise.Length, new SlideRevealHandler { Offset = new Vector2(0f, -RiseHeight) },
                    WordSpread)
            };
        }

        /// <summary>
        /// Hangs <paramref name="label"/> from <paramref name="parent"/>'s middle by the point <paramref name="edge"/> of
        /// its width, at <paramref name="at"/>: <paramref name="height"/> tall and as wide as the frame, so no word runs
        /// out of it.
        /// </summary>
        private void Pin(RectTransform label, RectTransform parent, Vector2 at, float edge, float height)
        {
            label.SetParent(parent, false);
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
            label.pivot = new Vector2(edge, 0.5f);
            label.sizeDelta = new Vector2(frameWidth, height);
            label.anchoredPosition = at;
            label.localScale = Vector3.one;
        }

        /// <summary>The width <paramref name="label"/>'s text lays out at, measured by its own layout.</summary>
        protected static float Measure(UniText label) => ((UnityEngine.UI.ILayoutElement)label).preferredWidth;

        /// <summary>
        /// Lays the beats end to end on the half-second grid: each is thrown in on a grid line, runs its action once it
        /// has settled and holds the result, and is thrown out on the grid line after, as the next is thrown in — each
        /// way of throwing chosen by <see cref="Links"/>.
        /// </summary>
        private void Schedule()
        {
            var at = TitleHold;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                part.From = Entry(Links[i]);
                part.To = Exit(Links[i + 1]);
                part.Enter = Links[i];
                part.Exit = Links[i + 1];
                part.Dives = Links[i + 1] == Link.Dive;
                part.Leaving = part.Dives ? DiveFor : SwishFor;
                part.In = at;
                part.ActionAt = at + SwishFor + Settle;
                if (part.Take != null) part.Take.Start = part.ActionAt;
                at = part.Navigates ? part.ActionAt + part.ActionSeconds : Grid(part.ActionAt + part.ActionSeconds + Hold);
                part.Out = at;
                Cue(part.Cues(), part.ActionAt);
            }

            Seconds = parts[parts.Length - 1].Dives ? at : at + parts[parts.Length - 1].Leaving;
        }

        /// <summary>Where a composition entered by <paramref name="link"/> arrives from.</summary>
        private Vector2 Entry(Link link) => link switch
        {
            Link.Whip or Link.Push => new Vector2(frameWidth, 0f),
            Link.Drop => new Vector2(0f, frameHeight),
            Link.Zoom => Vector2.zero,
            _ => new Vector2(0f, -frameHeight)
        };

        /// <summary>Where a composition left by <paramref name="link"/> goes.</summary>
        private Vector2 Exit(Link link) => link switch
        {
            Link.Whip => new Vector2(-frameWidth, 0f),
            Link.Match => new Vector2(0f, frameHeight),
            Link.Drop => new Vector2(0f, -frameHeight),
            _ => Vector2.zero
        };

        private static float Grid(float seconds) => GridBeat * Mathf.Ceil(seconds / GridBeat - 0.001f);

        protected override void OnRender(float local)
        {
            foreach (var part in parts)
                if (part is RecordedPart recorded) recorded.Apply(local);
            PoseTitle(local);
            PoseScene(local);
            var deep = false;
            foreach (var part in parts)
            {
                part.Pose(local);
                deep |= part.InDepth && part.Root.gameObject.activeSelf;
            }

            InDepth = deep;
            var look = Vector2.zero;
            var zoom = 1f;
            foreach (var part in parts)
                if (part.Films(local, out var at, out var push))
                {
                    look = at;
                    zoom = push;
                }

            var scale = Content.localScale.x * zoom;
            Content.localScale = Vector3.one * scale;
            Content.anchoredPosition = impacts.Pose(local) - look * scale;
            titleGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, TitleOverflow() / TitleCropFade);
        }

        /// <summary>How far the headline reaches past the top or the sides of the frame, in headline heights.</summary>
        private float TitleOverflow()
        {
            var frame = (RectTransform)title.GetComponentInParent<Canvas>().rootCanvas.transform;
            title.GetWorldCorners(corners);
            var low = frame.InverseTransformPoint(corners[0]);
            var high = frame.InverseTransformPoint(corners[2]);
            var bounds = frame.rect;
            var past = Mathf.Max(high.y - bounds.yMax, Mathf.Max(bounds.xMin - low.x, high.x - bounds.xMax));
            return past / Mathf.Max(1f, high.y - low.y);
        }

        /// <summary>
        /// The title card slammed in word by word and whipped up into its band; then only the last word changing, the
        /// old one thrown up as its beat is whipped away and the new one slammed down as the next settles, the line
        /// sliding to stay centered on the new word; and at the end the whole headline whipped away with the last beat.
        /// </summary>
        /// <remarks>
        /// The throw ends where the slam begins. A word thrown up while the next falls through the same slot crosses it
        /// in mid-air, and the two read as one garbled word.
        /// </remarks>
        private void PoseTitle(float local)
        {
            var rise = Motion.Swish.Window(local, TitleHold, SwishFor);
            var leave = Motion.Swish.Window(local, parts[parts.Length - 1].Out, SwishFor);
            var scale = Mathf.LerpUnclamped(TitleScale, 1f, rise);
            var away = Links[Links.Count - 1] == Link.Dive
                ? new Vector2(0f, frameHeight * 0.6f)
                : Exit(Links[Links.Count - 1]);
            title.anchoredPosition = Vector2.LerpUnclamped(new Vector2(0f, titleCardLift * TitleScale), titleAt, rise) +
                                     away * leave;
            title.localScale = Vector3.one * scale;
            spotlight.anchoredPosition = spotlightAt * rise;

            titleReveals[0].Fill = GlyphReveal.Frontier.Window(local, 0f, WordSlam);
            titleReveals[1].Fill = GlyphReveal.Frontier.Window(local, PromiseAt, PromiseRise);
            var count = parts.Length;
            for (var i = 0; i < count; i++)
            {
                var came = GlyphReveal.Frontier.Window(local, i == 0 ? WordStep : parts[i].In + WordThrow, WordSlam);
                var gone = i + 1 < count ? GlyphReveal.Frontier.Window(local, parts[i + 1].In, WordThrow) : 0f;
                wordReveals[i].Fill = Mathf.Min(came, 1f - gone);
            }

            var shift = wordShift[0];
            for (var i = 1; i < count; i++)
                shift += (wordShift[i] - wordShift[i - 1]) *
                         Motion.Snap.Window(local, parts[i].In, WordThrow + WordSlam * 0.5f);
            leadRect.anchoredPosition = new Vector2(leadEnd + shift, 0f);
            foreach (var word in wordRects) word.anchoredPosition = new Vector2(slot + shift, 0f);
        }

        /// <summary>Releases what the section owns outside its hierarchy; an override calls the base.</summary>
        protected virtual void OnDestroy()
        {
            foreach (var take in takes) take.Dispose();
        }

        private const float HeadlineSize = 92f;

        /// <summary>
        /// How far past the frame's edge, in headline heights, the headline's box reaches when it has faded out: gone before
        /// the edge reaches its letters, so the camera never shows a cut headline.
        /// </summary>
        private const float TitleCropFade = 0.25f;

        private static readonly Vector3[] corners = new Vector3[4];
        private const float SpotlightStrength = 0.065f;
        private const float SlamHeight = 70f;
        private const float WordSpread = 14f;
        private const float RiseHeight = 18f;
        private const float GridBeat = 0.5f;
        private const float TitleScale = 1.35f;
        private const float TitleHold = 1f;
        private const float WordStep = 0.1f;
        private const float WordSlam = 0.24f;

        /// <summary>How fast a replaced word is thrown back out of the headline.</summary>
        private const float WordThrow = 0.16f;

        private const float PromiseAt = 0.3f;
        private const float PromiseRise = 0.35f;

        /// <summary>How long a whip between two compositions lasts.</summary>
        protected const float SwishFor = 0.3f;

        /// <summary>How long a beat stands still after its whip before its action starts.</summary>
        private const float Settle = 0.12f;

        /// <summary>How long a beat holds its result before it is whipped away.</summary>
        private const float Hold = 0.3f;

        /// <summary>How long the closing dive lasts: as long as the next slide's entrance it runs on through.</summary>
        private const float DiveFor = 0.6f;
    }
}
