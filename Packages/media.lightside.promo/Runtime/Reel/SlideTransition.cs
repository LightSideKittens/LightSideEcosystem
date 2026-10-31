using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// How one slide gives way to the next. Owns nothing but the hand-off: it poses the outgoing and incoming
    /// slides' own rects and opacity, and never reaches into their content.
    /// </summary>
    /// <remarks>
    /// Implementations are held in <c>[SerializeReference]</c> fields, so a subclass may be added without touching
    /// the reel. <see cref="Restore"/> must undo everything <see cref="Apply"/> does, or successive transitions
    /// compound on the same slide.
    /// </remarks>
    [Serializable]
    public abstract class SlideTransition
    {
        [SerializeField] private float seconds = 0.4f;

        /// <summary>How long the hand-off lasts. It overlaps the tail of the outgoing slide and never lengthens the reel.</summary>
        public float Seconds => Mathf.Max(0f, seconds);

        /// <summary>
        /// Poses both slides at <paramref name="t"/> in [0, 1] through the hand-off. <paramref name="from"/> is null
        /// for the first slide of the reel.
        /// </summary>
        public abstract void Apply(Slide from, Slide to, float t);

        /// <summary>Returns a slide to its neutral pose.</summary>
        /// <remarks>
        /// A slide is posed by two transitions over its life — its own, which brings it in, and the next slide's,
        /// which takes it out — so the reel restores every slide with both before composing a frame. An override
        /// must therefore undo everything <see cref="Apply"/> touches on <em>either</em> argument, and must be
        /// idempotent.
        /// </remarks>
        public virtual void Restore(Slide slide)
        {
            slide.Group.alpha = 1f;
            slide.Rect.anchoredPosition = Vector2.zero;
            slide.Rect.localScale = Vector3.one;
        }
    }

    /// <summary>Hard cut. The default, and the one that costs nothing.</summary>
    [Serializable]
    public sealed class Cut : SlideTransition
    {
        public override void Apply(Slide from, Slide to, float t)
        {
            if (from) from.Group.alpha = 0f;
            to.Group.alpha = 1f;
        }
    }

    /// <summary>Dissolve. Both slides are visible throughout, which is why it reads as soft and slightly cheap.</summary>
    [Serializable]
    public sealed class CrossFade : SlideTransition
    {
        [SerializeField] private Ease ease = Ease.Linear;

        public override void Apply(Slide from, Slide to, float t)
        {
            var e = ease.Evaluate(t);
            if (from) from.Group.alpha = 1f - e;
            to.Group.alpha = e;
        }
    }

    /// <summary>
    /// The incoming slide travels in and shoves the outgoing one out along the same axis, so the cut reads as one
    /// continuous camera move rather than two pictures blending.
    /// </summary>
    [Serializable]
    public sealed class Push : SlideTransition
    {
        [SerializeField] private Vector2 direction = Vector2.left;
        [SerializeField] private float travel = 1f;
        [SerializeField] private Ease ease = default;

        public Push() => ease = Ease.Emphasized;

        public override void Apply(Slide from, Slide to, float t)
        {
            var e = ease.Evaluate(t);
            var span = to.Rect.rect.size * travel;
            var step = new Vector2(direction.x * span.x, direction.y * span.y);

            to.Group.alpha = 1f;
            to.Rect.anchoredPosition = -step * (1f - e);
            if (!from) return;

            from.Group.alpha = 1f;
            from.Rect.anchoredPosition = step * e;
        }
    }

    /// <summary>
    /// The incoming slide rises into place while the outgoing one recedes, which keeps the eye on one plane and
    /// suits a reveal that must feel like an arrival rather than a change of subject.
    /// </summary>
    [Serializable]
    public sealed class Lift : SlideTransition
    {
        [SerializeField] private float rise = 90f;
        [SerializeField] private float recede = 0.94f;
        [SerializeField] private Spring spring = default;

        public Lift() => spring = Spring.Rise;

        /// <remarks>
        /// The spring response is normalized by its own value at the end of the hand-off. A damped spring has not
        /// reached its target after a mere 0.4s, so using the raw response would leave the incoming slide parked
        /// short of its settled pose for the rest of the shot.
        /// </remarks>
        public override void Apply(Slide from, Slide to, float t)
        {
            var settled = Seconds > 0f ? spring.Evaluate(Seconds) : 0f;
            var s = settled > 0f ? Mathf.Clamp01(spring.Evaluate(t * Seconds) / settled) : 1f;

            to.Group.alpha = s;
            to.Rect.anchoredPosition = new Vector2(0f, rise * (1f - s));

            if (!from) return;
            from.Group.alpha = 1f - t;
            var k = Mathf.LerpUnclamped(1f, recede, s);
            from.Rect.localScale = new Vector3(k, k, 1f);
        }
    }
}
