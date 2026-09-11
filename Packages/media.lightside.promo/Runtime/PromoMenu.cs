namespace LightSide.Promo
{
    internal static class PromoMenu
    {
        private const string Root = LightSideCore.Menu.Root + "/Promo/";
        internal const string ReelObjectName = "Promo Reel";
        internal const string ShowreelObjectName = "Promo Showreel";
        internal const string ShapesReelObjectName = "Promo Shapes Reel";

        internal static class Tools
        {
            private const string P = "Tools/" + Root;
            public const string CreateReel = P + "Create " + nameof(Reel);
            public const string CreateShowreel = P + "Create " + nameof(ShowreelScene);
            public const string CreateShapesReel = P + "Create " + nameof(ShapesReelScene);
            public const string Rebuild = P + "Rebuild Scene";
            public const string CaptureFrames = P + "Capture Frames";
            public const string ContactSheet = P + "Capture Contact Sheet";
        }

        internal static class AddComponent
        {
            public const string Reel = Root + nameof(global::LightSide.Promo.Reel);
            public const string Scenes = Root;
            public const string Slides = Root + "Slides/";
        }
    }
}
