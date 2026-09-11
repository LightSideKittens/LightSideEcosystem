using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// Eleven kinetic effects running at once, each written as a tag rather than a script.
    /// </summary>
    /// <remarks>
    /// Every modifier here renders a pure function of its phase, and the slide writes its own local time into all of them.
    /// A free-running driver would advance the phase from its own <c>Update</c> off <c>deltaTime</c>, which
    /// makes the frame a function of when it was composed rather than of where it sits in the reel — the shot could
    /// not be scrubbed, and an offline capture stepping faster than the wall clock would freeze it.
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(LivingTextSlide))]
    public sealed class LivingTextSlide : Slide
    {
        [SerializeField] private string headline = "Text that moves, without a line of code.";
        [SerializeField] private string sub = "Eleven kinetic effects and nineteen ways to arrive.";

        [SerializeField, TextArea(3, 8)]
        private string body =
            "<wave>wave</wave>  <wobble>wobble</wobble>  <pulse>pulse</pulse>  <bounce>bounce</bounce>\n" +
            "<shake>shake</shake>  <float>float</float>  <pendulum>swing</pendulum>  <spin>spin</spin>\n" +
            "<glitch>glitch</glitch>  roll <roll>1240</roll>  <scramble>scramble</scramble>";

        /// <summary>
        /// The arrival line, carried by the bottom claim rather than a fourth line of <see cref="body"/>.
        /// </summary>
        /// <remarks>
        /// A ranged style addresses codepoints of the <em>parsed</em> text, while a word is found by searching the
        /// markup — so every tag above the word shifts its range by the tag's own length, and the handler lands on
        /// whatever happens to sit there. On a string with no markup the two coordinate spaces are the same one.
        /// <para>
        /// Seven names, because the line does not wrap and the claim's own box is 86% of the frame: at
        /// <see cref="Promo.Theme.Head"/> an eighth would put it past that edge rather than onto a second line.
        /// </para>
        /// </remarks>
        [SerializeField] private string arrivalLine = "drop rain burst domino spiral flip chaos";

        /// <summary>Each word of <see cref="arrivalLine"/>, arriving under the handler it is named for.</summary>
        private static readonly (string word, RevealHandler handler)[] Arrivals =
        {
            ("drop", new DropRevealHandler()),
            ("rain", new RainRevealHandler()),
            ("burst", new BurstRevealHandler()),
            ("domino", new DominoRevealHandler()),
            ("spiral", new SpiralRevealHandler()),
            ("flip", new FlipRevealHandler()),
            ("chaos", new ChaosRevealHandler())
        };

        private readonly Action<float>[] driven = new Action<float>[10];

        private readonly GlyphReveal[] arrivals = new GlyphReveal[Arrivals.Length];

        private Claim claim;
        private Claim arrival;
        private Showcase panel;
        private RollingModifier roll;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, sub);
            arrival = stage.Claim(stage.Root, arrivalLine, top: false);

            panel = stage.Showcase("Kinetic", stage.Root, "One tag each — no scripts, no Animator",
                body, stage.ContentHeight * 0.2f, widthFraction: 0.92f, heightFraction: 0.98f,
                horizontal: HorizontalAlignment.Center);

            Bind(0, new WaveModifier(), "wave");
            Bind(1, new WobbleModifier(), "wobble");
            Bind(2, new PulseModifier(), "pulse");
            Bind(3, new BounceModifier(), "bounce");
            Bind(4, new ShakeModifier(), "shake");
            Bind(5, new FloatModifier(), "float");
            Bind(6, new PendulumModifier(), "pendulum");
            Bind(7, new SpinModifier(), "spin");
            Bind(8, new GlitchModifier(), "glitch");
            Bind(9, new ScrambleModifier { Progress = 0f }, "scramble");

            roll = new RollingModifier();
            panel.Body.Styles.Add(Style.Tag(roll, "roll"));

            for (var i = 0; i < Arrivals.Length; i++)
                arrivals[i] = stage.Reveal(arrival.Headline, Arrivals[i].word, Arrivals[i].handler);

            Cue("kinetic", 0.3f);
            for (var i = 0; i < arrivals.Length; i++) Cue("arrive", WriteOn + i * Stagger);
        }

        private void Bind<TParams>(int index, GlyphParamModifier<TParams> modifier, string tag)
            where TParams : unmanaged
        {
            driven[index] = value => modifier.Phase = value;
            panel.Body.Styles.Add(Style.Tag(modifier, tag));
        }

        private void Bind(int index, GlitchModifier modifier, string tag)
        {
            driven[index] = value => modifier.Phase = value;
            panel.Body.Styles.Add(Style.Tag(modifier, tag));
        }

        private void Bind(int index, ScrambleModifier modifier, string tag)
        {
            driven[index] = value => modifier.Phase = value;
            panel.Body.Styles.Add(Style.Tag(modifier, tag));
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);

            for (var i = 0; i < driven.Length; i++) driven[i](local);
            roll.Roll = RollFrom * (1f - Ease.EmphasizedIn.Window(local, RollAt, RollFor));

            for (var i = 0; i < arrivals.Length; i++)
                arrivals[i].Fill = GlyphReveal.Frontier.Window(local, WriteOn + i * Stagger, Arrive);
        }

        private const float WriteOn = 1.6f;

        /// <summary>Gap between one arrival and the next, so the handlers read one at a time.</summary>
        private const float Stagger = 0.34f;

        /// <summary>How long a single word takes to arrive.</summary>
        private const float Arrive = 0.75f;

        /// <summary>
        /// Wheel positions the odometer is spun back before it settles.
        /// </summary>
        /// <remarks>
        /// <c>Roll</c> is a distance from the settled reading, not a phase: zero shows the real characters, and the
        /// shot drives it toward zero. Feeding it a value that only ever grows spins the wheel forever and it never
        /// lands — which is the whole of what an odometer has to do.
        /// </remarks>
        private const float RollFrom = 14f;

        private const float RollAt = 0.4f;
        private const float RollFor = 2.2f;
    }
}
