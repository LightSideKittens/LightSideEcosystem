#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace LightSide.Showcase
{
    /// <summary>Sends showcase events to the page hosting the player; outside a WebGL player nothing receives them.</summary>
    internal static class ShowcaseBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void Showcase_Emit(string name, string payload);
#endif

        public static void Emit(string name, string payload)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Showcase_Emit(name, payload);
#endif
        }
    }
}
