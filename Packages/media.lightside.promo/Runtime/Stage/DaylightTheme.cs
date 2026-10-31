using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A livery on warm paper: every product surface is a tint of one ground, every word is one violet.
    /// </summary>
    /// <remarks>
    /// <see cref="Ground"/> and <see cref="Letter"/> are the only authored colors. Every other product-surface
    /// value is a tint or shade of one of them, so retuning the livery is retuning those two.
    /// <para>
    /// The neutral set stays cool and gray while the product surface stays warm. On a dark livery the product is
    /// told apart by being the lit thing in the frame, which a light livery cannot offer; temperature carries the
    /// distinction instead, and it carries it at any brightness.
    /// </para>
    /// <para>
    /// The brand ramp is deepened. Its light end is a pale orange authored to sit on near-black, and left alone it
    /// is all but invisible against this ground.
    /// </para>
    /// <para>
    /// Secondary ink moves a tenth of the way to the ground, not the four tenths a dark livery affords. A light
    /// ground is already near the top of the luminance range, so every step toward it costs contrast steeply: at
    /// four tenths this pair reaches 2:1 and stops being text. Set that distance from a measured ratio, never by eye.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeDescription("Warm paper and one violet, with the neutral set kept cool to stay apart from it.")]
    public sealed class DaylightTheme : Theme
    {
        /// <summary>The warm ground every product surface in this livery is derived from.</summary>
        public const string Ground = "#FDE3B7";

        /// <summary>The violet every word on a product surface is written in.</summary>
        public const string Letter = "#480D8B";

        public DaylightTheme()
        {
            var ground = Hex(Ground);
            var letter = Hex(Letter);

            Background = ground;
            Bar = Lift(ground, 0.30f);
            Surface = Lift(ground, 0.55f);
            Line = Mix(ground, letter, 0.20f);
            Text = letter;
            TextSoft = Mix(letter, ground, 0.10f);

            Paper = Hex("#FFFFFF");
            PaperDim = Hex("#F1F1F4");
            PaperLine = Hex("#DCDCE2");
            Canvas = Hex("#EAEAEF");
            Ink = Hex("#1B1B22");
            InkSoft = Hex("#5C5C68");

            Green = Sink(Green, 0.15f);

            Orange = Sink(Orange, 0.30f);
            Coral = Sink(Coral, 0.26f);
            Magenta = Sink(Magenta, 0.14f);
            Violet = Sink(Violet, 0.18f);
            Glow = Sink(Glow, 0.16f);

            Accent = Violet;
            Shadow = Fade(letter, 0.16f);
        }
    }
}
