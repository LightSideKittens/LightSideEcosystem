using System;
using LightSide.Promo;
using UnityEngine;

namespace LightSide.ParticleShowcase
{
    /// <summary>A slide whose particle hosts are posed by time: seeked exactly, or replayed from their start when they move.</summary>
    public abstract class ShotSlide : Slide
    {
        /// <summary>The fixed step a replay advances by.</summary>
        protected const float Step = 1f / 60f;

        /// <summary>
        /// Shows <paramref name="host"/> at <paramref name="seconds"/> after its start; before its start, at its start.
        /// Plays it first: the reel disables the slides off screen, which releases their players.
        /// </summary>
        protected static void Show(UniParticles host, float seconds)
        {
            host.Play();
            host.Seek(Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// Replays <paramref name="host"/> from its start to <paramref name="seconds"/> in fixed steps, running
        /// <paramref name="pose"/> with each step's time before it: an effect whose emitter moves shows the path it took.
        /// </summary>
        protected static void Replay(UniParticles host, float seconds, Action<float> pose)
        {
            pose(0f);
            host.Play();
            host.Seek(0f);
            var steps = Mathf.FloorToInt(Mathf.Max(0f, seconds) / Step + 1e-4f);
            for (var i = 1; i <= steps; i++)
            {
                pose(i * Step);
                host.Simulate(Step);
            }
        }

        /// <summary>Activates and plays every host built under <paramref name="root"/>, configured while inactive; their manual clocks keep them still until posed.</summary>
        protected static void Wake(Transform root)
        {
            foreach (var host in root.GetComponentsInChildren<UniParticles>(true))
            {
                host.gameObject.SetActive(true);
                host.Play();
            }
        }
    }
}
