using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Showcase
{
    /// <summary>A word in painted looks; every variant lights the stage in its own colors.</summary>
    public sealed class StyleShowcaseScene : TextShowcaseScene
    {
        /// <summary>The stage light of a variant.</summary>
        [Serializable]
        public struct Stage
        {
            public string variant;
            public Color upperLight;
            public Color lowerLight;
            public Color rays;
        }

        [SerializeField] private Stage[] stages;
        [SerializeField] private Graphic rays;
        [SerializeField] private Graphic[] sparkles;

        private Stage stage;

        public override Color UpperLight => stage.upperLight;
        public override Color LowerLight => stage.lowerLight;

        public override void SetVariant(string id)
        {
            stage = stages.First(candidate => candidate.variant == id);
            rays.color = stage.rays;
            foreach (var sparkle in sparkles) sparkle.color = Color.Lerp(stage.rays, Color.white, 0.75f);
            base.SetVariant(id);
        }
    }
}
