using UnityEngine;
using UnityEngine.AddressableAssets;

namespace LightSide.Samples
{
    /// <summary>The Basic Usage sample as the CI slideshow captures it.</summary>
    public class BasicUsageExampleWebGL : BasicUsageExampleBase
    {
        [Header("Slideshow")]
        [SerializeField] private AssetReferenceGameObject addressablePrefab;

        internal AssetReferenceGameObject AddressablePrefab => addressablePrefab;
        internal UniText SlideshowText => demoText;
    }
}
