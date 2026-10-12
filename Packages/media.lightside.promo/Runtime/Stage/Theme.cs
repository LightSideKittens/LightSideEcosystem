using System;
using UnityEngine;
using Ramp = LightSide.Gradient;
using Stop = LightSide.GradientStop;
using Interp = LightSide.GradientInterpolation;

namespace LightSide.Promo
{
    /// <summary>
    /// The look a reel is drawn in: palette, radius and padding scales, elevation and type sizes.
    /// </summary>
    /// <remarks>
    /// Carried on <see cref="Stage"/> and propagated to every nested stage, so a whole reel changes appearance by
    /// assigning one instance. It is deliberately neither a static class nor an asset: a palette is policy, and a
    /// reel that cannot swap it cannot show the same shot in two liveries.
    /// <para>
    /// The product surface carries <see cref="Brand"/>; every surface that is not the product uses the neutral paper
    /// set. A viewer who has never heard of the product must be able to tell in one frame which window is the one
    /// being sold, so a livery that changes one set changes the other to stay apart from it.
    /// </para>
    /// <para>
    /// Colours are read at build time and baked into the shapes a slide creates. A reel already showing content
    /// keeps the palette it was built with until it is rebuilt.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeDescription("Dark product surface carrying the brand ramp, against neutral paper.")]
    public class Theme
    {
        /// <summary>Smallest radius a corner may be reduced to before it stops reading as a corner at all.</summary>
        public const float MinRadius = 4f;

        [SerializeField] private Color coral = Hex("#F5726B");
        [SerializeField] private Color orange = Hex("#F58E4A");
        [SerializeField] private Color magenta = Hex("#F03CD0");
        [SerializeField] private Color violet = Hex("#7B3FF2");
        [SerializeField] private Color glow = Hex("#C13BE0");
        [SerializeField] private Color pass = Hex("#34D399");

        [SerializeField] private Color background = Hex("#14101F");
        [SerializeField] private Color bar = Hex("#1D1730");
        [SerializeField] private Color surface = Hex("#221B38");
        [SerializeField] private Color line = Hex("#332A50");
        [SerializeField] private Color text = Hex("#F2EEFF");
        [SerializeField] private Color textSoft = Hex("#A79BC8");

        [SerializeField] private Color paper = Hex("#FFFFFF");
        [SerializeField] private Color paperDim = Hex("#F3F4FB");
        [SerializeField] private Color paperLine = Hex("#E3E5F2");
        [SerializeField] private Color canvas = Hex("#F6F6FC");
        [SerializeField] private Color ink = Hex("#141420");
        [SerializeField] private Color inkSoft = Hex("#54546A");
        [SerializeField] private Color accent = Hex("#7A5BFF");

        [SerializeField] private Color green = Hex("#1FB663");

        [SerializeField] private Color shadow = new Color(0.03f, 0.02f, 0.08f, 0.55f);

        [SerializeField] private float radiusXs = 10f;
        [SerializeField] private float radiusSm = 14f;
        [SerializeField] private float radiusMd = 20f;
        [SerializeField] private float radiusLg = 28f;
        [SerializeField] private float radiusXl = 36f;
        [SerializeField] private float radiusXxl = 48f;

        [SerializeField, Range(0f, 1f)] private float smoothing = 0.35f;
        [SerializeField, Min(1f)] private float revealSpread = 4.5f;

        [SerializeField] private float padXs = 6f;
        [SerializeField] private float padSm = 10f;
        [SerializeField] private float padMd = 16f;
        [SerializeField] private float padLg = 24f;
        [SerializeField] private float padXl = 34f;

        [SerializeField] private UniTextFont displayFace;
        [SerializeField] private UniTextFont bodyFace;

        [SerializeField] private float hero = 168f;
        [SerializeField] private float title = 104f;
        [SerializeField] private float head = 64f;
        [SerializeField] private float lead = 54f;
        [SerializeField] private float body = 42f;
        [SerializeField] private float small = 32f;

        public Color Coral { get => coral; set => coral = value; }
        public Color Orange { get => orange; set => orange = value; }
        public Color Magenta { get => magenta; set => magenta = value; }
        public Color Violet { get => violet; set => violet = value; }
        public Color Glow { get => glow; set => glow = value; }

        /// <summary>
        /// The colour of a passed check.
        /// </summary>
        /// <remarks>
        /// Deliberately outside the brand ramp. A tick in a brand colour reads as decoration on a surface that is
        /// already brand-coloured; green is the one hue a viewer takes as a verdict without being told, which is what
        /// lets a column of ticks be scanned instead of read.
        /// </remarks>
        public Color Pass { get => pass; set => pass = value; }

        public Color Background { get => background; set => background = value; }
        public Color Bar { get => bar; set => bar = value; }
        public Color Surface { get => surface; set => surface = value; }
        public Color Line { get => line; set => line = value; }
        public Color Text { get => text; set => text = value; }
        public Color TextSoft { get => textSoft; set => textSoft = value; }

        public Color Paper { get => paper; set => paper = value; }
        public Color PaperDim { get => paperDim; set => paperDim = value; }
        public Color PaperLine { get => paperLine; set => paperLine = value; }
        public Color Canvas { get => canvas; set => canvas = value; }
        public Color Ink { get => ink; set => ink = value; }
        public Color InkSoft { get => inkSoft; set => inkSoft = value; }
        public Color Accent { get => accent; set => accent = value; }

        /// <summary>
        /// The one hue outside <see cref="Brand"/>, and not a member of it.
        /// </summary>
        /// <remarks>
        /// An annotation saying "this one is correct" has to read as correct before it reads as ours, and no ramp
        /// running orange to violet can say that. It belongs to marks and verdicts, never to a surface.
        /// </remarks>
        public Color Green { get => green; set => green = value; }

        public Color Shadow { get => shadow; set => shadow = value; }

        /// <summary>Corner radii, smallest to largest.</summary>
        public float RadiusXs { get => radiusXs; set => radiusXs = value; }
        public float RadiusSm { get => radiusSm; set => radiusSm = value; }
        public float RadiusMd { get => radiusMd; set => radiusMd = value; }
        public float RadiusLg { get => radiusLg; set => radiusLg = value; }
        public float RadiusXl { get => radiusXl; set => radiusXl = value; }
        public float RadiusXxl { get => radiusXxl; set => radiusXxl = value; }

        /// <summary>Figma corner smoothing every rounded corner is drawn with: 0 is a circular arc, 0.6 the iOS curve.</summary>
        public float Smoothing { get => smoothing; set => smoothing = value; }

        /// <summary>
        /// How many glyphs are mid-arrival at once during a reveal. One is a pop; several overlap into a wave.
        /// </summary>
        public float RevealSpread { get => revealSpread; set => revealSpread = value; }

        public float PadXs { get => padXs; set => padXs = value; }
        public float PadSm { get => padSm; set => padSm = value; }
        public float PadMd { get => padMd; set => padMd = value; }
        public float PadLg { get => padLg; set => padLg = value; }
        public float PadXl { get => padXl; set => padXl = value; }

        /// <summary>
        /// The face a headline, a hero line and a figure are set in; unassigned leaves them on the OS cascade.
        /// </summary>
        /// <remarks>
        /// A face covering only one script is safe here. The provider resolves an assigned font first and falls
        /// through to <see cref="SystemFont.Default"/> for anything it has no glyph for, so a Latin display face on
        /// a frame carrying eight writing systems shapes the other seven through the OS exactly as an unassigned
        /// one would.
        /// </remarks>
        public UniTextFont DisplayFace { get => displayFace; set => displayFace = value; }

        /// <summary>The face everything else is set in; unassigned leaves it on the OS cascade.</summary>
        public UniTextFont BodyFace { get => bodyFace; set => bodyFace = value; }

        /// <summary>
        /// Type sizes for a 1920x1080 frame. These are a floor and not a target: a hero frame wants roughly twice
        /// <see cref="Title"/>, and every frame wants one element that dominates it.
        /// </summary>
        public float Hero { get => hero; set => hero = value; }
        public float Title { get => title; set => title = value; }
        public float Head { get => head; set => head = value; }
        /// <summary>The line under a large figure: bigger than body copy, quieter than a heading.</summary>
        public float Lead { get => lead; set => lead = value; }

        public float Body { get => body; set => body = value; }
        public float Small { get => small; set => small = value; }

        /// <summary>The brand ramp, in Oklab so the sweep through magenta stays saturated instead of greying out.</summary>
        public virtual Ramp Brand => new Ramp(
            new[]
            {
                new Stop(0f, Orange),
                new Stop(0.22f, Coral),
                new Stop(0.6f, Magenta),
                new Stop(1f, Violet)
            },
            Interp.Perceptual);

        /// <summary>
        /// Radius of a child inset by <paramref name="gap"/> inside a parent of radius <paramref name="outer"/>.
        /// </summary>
        /// <remarks>
        /// Only equal centres keep two arcs parallel. Giving parent and child the same radius swells the visible gap
        /// from <c>gap</c> along the straight edges to <c>gap * sqrt(2)</c> at the corner — a 41% bulge that reads as
        /// wrong without being nameable. Never type the second radius by hand.
        /// </remarks>
        public float Inner(float outer, float gap) => Mathf.Max(MinRadius, outer - gap);

        /// <summary>
        /// Blends <paramref name="a"/> into <paramref name="b"/> in linear light.
        /// </summary>
        /// <remarks>
        /// Linear rather than perceptual, which matters only across a wide hue sweep; a livery derives tints and
        /// shades of one hue, where the two agree and this allocates nothing.
        /// </remarks>
        public static Color Mix(Color a, Color b, float t) =>
            Color.Lerp(a.linear, b.linear, Mathf.Clamp01(t)).gamma;

        /// <summary>Lightens <paramref name="color"/> toward white.</summary>
        public static Color Lift(Color color, float amount) => Mix(color, Color.white, amount);

        /// <summary>Darkens <paramref name="color"/> toward black, keeping its hue.</summary>
        public static Color Sink(Color color, float amount) => Mix(color, Color.black, amount);

        /// <summary>
        /// <paramref name="color"/> at <paramref name="alpha"/>, its own colour intact.
        /// </summary>
        /// <remarks>
        /// The outer stop of a ramp that fades out must be this and not a transparent black: the shader
        /// premultiplies its texels, so a colour curve running to black multiplies into the alpha curve and the ramp
        /// darkens as it disappears.
        /// </remarks>
        public static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        /// <summary>Parses a hex string, or magenta when it is not one.</summary>
        protected static Color Hex(string value) =>
            ColorParsing.TryParse(value, out var parsed) ? (Color)parsed : Color.magenta;
    }
}
