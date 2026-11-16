using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// The styles section of the UniGUI reel: six games of six genres, each drawn in its own art direction from the same
    /// shapes and text — a puzzle game's level map, a toy shop, a rhythm game's song select, a starfighter hangar, a pixel
    /// role-playing game's menu and a fantasy hero's sheet — each a whole screen the pointer works as a player would.
    /// </summary>
    /// <remarks>
    /// A look is a set of design tokens — colors, faces, a shape language and a way of giving depth — that one set of
    /// builders reads; each game composes its screen from those builders and its own parts. The faces are assigned on the
    /// component; a look whose face is missing fails the build.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Scenes + nameof(GuiStylesScene))]
    public sealed partial class GuiStylesScene : GuiSection
    {
        [SerializeField] private string lead = "Feels";

        /// <summary>The headline's last word for each game in order: casual, glossy, neon, sci-fi, retro, epic.</summary>
        [SerializeField] private string[] words = { "casual.", "glossy.", "neon.", "sci-fi.", "retro.", "epic." };

        [SerializeField] private string promise = "One kit. Any art direction.";

        [SerializeField] private UniTextFont candyFace;
        [SerializeField] private UniTextFont glossyFace;
        [SerializeField] private UniTextFont neonFace;
        [SerializeField] private UniTextFont scifiFace;
        [SerializeField] private UniTextFont retroFace;
        [SerializeField] private UniTextFont epicFace;

        /// <summary>The rounded body face of the casual and glossy games.</summary>
        [SerializeField] private UniTextFont roundFace;

        /// <summary>The technical body face of the neon and sci-fi games.</summary>
        [SerializeField] private UniTextFont techFace;

        /// <summary>The plain body face of the epic game's descriptions.</summary>
        [SerializeField] private UniTextFont plainFace;

        private static readonly Link[] links = { Link.Rise, Link.Whip, Link.Push, Link.Whip, Link.Zoom, Link.Whip, Link.Whip };

        protected override string Lead => lead;

        protected override IReadOnlyList<string> Words => words;

        protected override string Promise => promise;

        protected override IReadOnlyList<Link> Links => links;

        protected override Part[] BuildParts(Stage stage)
        {
            var games = new (Look look, Func<Builder, Product> make)[]
            {
                (Candy(), k => new SagaMap(k)),
                (Glossy(), k => new ToyShop(k)),
                (Neon(), k => new SongSelect(k)),
                (Scifi(), k => new Hangar(k)),
                (Retro(), k => new PartyMenu(k)),
                (Epic(), k => new HeroSheet(k))
            };
            var parts = new Part[games.Length];
            var center = new Vector2(0f, contentCenter);
            for (var i = 0; i < games.Length; i++)
            {
                var (look, make) = games[i];
                if (!look.Display || !look.Body) throw new InvalidOperationException($"[Promo] '{name}' needs its {look.Name} faces assigned.");
                parts[i] = new LookPart(stage, look, make, center);
            }

            return parts;
        }

        /// <summary>
        /// One game's beat: its screen, a device's display scaled to sit under the headline, lands whole; then the camera
        /// fills the frame with it while the pointer plays the game's moves, each answered by the screen.
        /// </summary>
        private sealed class LookPart : DrawnPart
        {
            public LookPart(Stage stage, Look look, Func<Builder, Product> make, Vector2 center) : base(stage, look.Name)
            {
                var device = stage.Node("Device", Root);
                stage.Box(device, center, ScreenSize);
                device.localScale = Vector3.one * ScreenScale;
                var k = new Builder(stage, look);
                product = make(k);
                product.Origin = center;
                product.Scale = ScreenScale;
                product.Build(k.Screen(device));
                Plan(center + new Vector2(300f, -ScreenSize.y * ScreenScale * 0.5f - PointerDrop), product.Script().ToArray(), Pace);
                composition = new Rect(center - ScreenSize * ScreenScale * 0.5f, ScreenSize * ScreenScale);
            }

            private readonly Product product;
            private readonly Rect composition;

            protected override float CameraZoom => Closeup;

            protected override Rect? Composition => composition;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                product.Pose(new Clock(Pointer.Timeline, Mathf.Max(0f, played), product));
            }

            private const float Pace = 1.35f;
            private const float Closeup = 1.45f;
        }

        /// <summary>
        /// One game: its screen, built once in screen units from the screen's middle, the pointer's moves on it, and how
        /// the screen answers them at a time in the beat.
        /// </summary>
        private abstract class Product
        {
            protected Product(Builder k)
            {
                K = k;
                L = k.L;
            }

            protected Builder K { get; }
            protected Look L { get; }

            /// <summary>Where the screen's middle stands on the stage, and how large its units stand there.</summary>
            public Vector2 Origin { get; set; }

            public float Scale { get; set; }

            /// <summary>Builds the screen's parts under <paramref name="screen"/>, a rect of <see cref="ScreenSize"/>.</summary>
            public abstract void Build(RectTransform screen);

            /// <summary>The pointer's moves, aimed in screen units.</summary>
            public abstract List<Beat> Script();

            public abstract void Pose(in Clock clock);

            /// <summary>A move to the screen point <paramref name="point"/>, at a target <paramref name="width"/> wide.</summary>
            protected Beat To(Vector2 point, float width) => Beat.To(Origin + point * Scale, null, width * Scale);

            protected Beat Drag(Vector2 point, string id, float width, float hold = 0f) => Beat.Drag(Origin + point * Scale, id, width * Scale, hold);

            /// <summary>Moves to <paramref name="point"/> and presses there: a press that changes the screen, the beat's payoff when <paramref name="accent"/>.</summary>
            protected IEnumerable<Beat> Tap(Vector2 point, float width, string id, float read, bool accent = false)
            {
                yield return To(point, width);
                yield return Beat.Click(id, true, accent);
                if (read > 0f) yield return Beat.Wait(read);
            }
        }

        /// <summary>A moment in a game's beat: its time and what the pointer does at and around it.</summary>
        private readonly struct Clock
        {
            public Clock(PointerTimeline timeline, float t, Product product)
            {
                this.timeline = timeline;
                T = t;
                origin = product.Origin;
                scale = product.Scale;
            }

            private readonly PointerTimeline timeline;
            private readonly Vector2 origin;
            private readonly float scale;

            /// <summary>Seconds since the beat's action started.</summary>
            public float T { get; }

            /// <summary>When the press named <paramref name="id"/> goes down.</summary>
            public float At(string id) => timeline.Span(id).x;

            /// <summary>Seconds since the press named <paramref name="id"/>; negative before it.</summary>
            public float Since(string id) => T - At(id);

            public bool Past(string id) => T >= At(id);

            public Vector2 Span(string id) => timeline.Span(id);

            /// <summary>How far down the pointer holds a press named <paramref name="id"/>: 0 away from it.</summary>
            public float Held(string id) => Mathf.Abs(T - At(id)) < PressWindow ? timeline.Sample(T).Press : 0f;

            /// <summary>Where the pointer stands at <paramref name="seconds"/>, in screen units.</summary>
            public Vector2 Pointer(float seconds) => (timeline.Sample(seconds).Position - origin) / scale;

            /// <summary>How far through <paramref name="length"/> seconds after the press named <paramref name="id"/> the beat is, eased.</summary>
            public float After(string id, float length, float delay = 0f) => Ease.Emphasized.Window(T, At(id) + delay, length);

            private const float PressWindow = 0.3f;
        }

        private static readonly Ease Pop = Ease.Of(EasingType.BackOut);

        private static void Stroke(UniText text, Color color, float em) =>
            text.Styles.Add(Style.WholeText(new StrokeModifier { Paint = PaintRef.Solid(color), Width = UnitValue.Em(em), Align = 1f }));

        private static void Drop(UniText text, Color color, Vector2 em, float blur = 0f) =>
            text.Styles.Add(Style.WholeText(new ShadowModifier
            {
                Paint = PaintRef.Solid(color), Offset = new UnitVector2(em, UnitKind.Em), Blur = UnitValue.Em(blur), Spread = UnitValue.Em(0f)
            }));

        private static void Glow(UniText text, Color color, float em) =>
            text.Styles.Add(Style.WholeText(new GlowModifier { Paint = PaintRef.Solid(color), Radius = UnitValue.Em(em) }));

        /// <summary>Fills <paramref name="text"/> with a ramp from <paramref name="top"/> to <paramref name="bottom"/> down each line.</summary>
        private static void Ramp(UniText text, Color top, Color bottom)
        {
            var paint = Paint.Default;
            paint.source.kind = PaintSourceKind.Gradient;
            paint.source.color = Color.white;
            paint.source.gradient = new Gradient(new[] { new GradientStop(0f, top), new GradientStop(1f, bottom) },
                GradientInterpolation.Perceptual);
            paint.projection.kind = PaintProjectionKind.Linear;
            paint.projection.angle = 270f;
            paint.projection.scale = 1f;
            var catalog = new InlinePaintProvider();
            catalog.Entries.Add(new PaintSwatch { name = RampSwatch, paint = paint, mapping = PaintMapping.Line });
            text.Styles.Add(Style.WholeText(new FillModifier { Provider = catalog, Paint = PaintRef.Named(RampSwatch) }));
        }

        private const string RampSwatch = "look.ramp";

        /// <summary>A count written the way the games write amounts: digits grouped by commas.</summary>
        private static string Grouped(int amount) => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A countdown of <paramref name="seconds"/> as hours, minutes and seconds.</summary>
        private static string Clockface(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }

        /// <summary>A game's screen: a landscape tablet's display, in the units every game is built in.</summary>
        private static readonly Vector2 ScreenSize = new(1600f, 900f);

        /// <summary>How large the screen stands on the stage at rest, under the headline; the close-up fills the frame with it.</summary>
        private const float ScreenScale = 0.78f;
    }
}
