using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A section whose beats are boards of the kit's real control prefabs, painted from the section's own copy of the
    /// kit's palette in the film's warm livery.
    /// </summary>
    /// <remarks>
    /// The controls are the shipped prefabs, unchanged but for their labels and data. Their states are written from the
    /// pointer's timeline each time the section is posed, so every change happens on the press that causes it and a
    /// frame depends on its time alone; the controls then ease between states with their own transitions.
    /// </remarks>
    public abstract partial class GuiKitSection : GuiSection
    {
        /// <summary>The kit's palette the section's own copy starts from.</summary>
        [SerializeField] private UniShapePaints basePalette;

        /// <summary>The kit's control prefabs, found by name.</summary>
        [SerializeField] protected GameObject[] kit = Array.Empty<GameObject>();

        protected ScenePalette palette;

        /// <summary>Builds the section's boards, in the order they play, once the section's palette exists.</summary>
        protected abstract Part[] BuildBoards(Stage stage);

        protected sealed override Part[] BuildParts(Stage stage)
        {
            if (!basePalette) throw new InvalidOperationException($"[Promo] '{name}' needs its palette assigned.");

            palette?.Dispose();
            palette = new ScenePalette(basePalette);
            palette.Warm(theme);
            var parts = BuildBoards(stage);
            palette.Present();
            return parts;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            palette?.Dispose();
        }
    }
}
