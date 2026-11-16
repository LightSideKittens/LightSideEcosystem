using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>The six art directions of <see cref="GuiStylesScene"/>.</summary>
    public sealed partial class GuiStylesScene
    {
        private static Color Hex(uint rgb, float alpha = 1f) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        private static Tone Tones(uint light, uint dark, uint lip) => new(Hex(light), Hex(dark), Hex(lip));

        /// <summary>A light tone, captioned in <paramref name="ink"/>.</summary>
        private static Tone Inked(uint light, uint dark, uint lip, uint ink) => new(Hex(light), Hex(dark), Hex(lip), Hex(ink));

        /// <summary>
        /// Casual: a puzzle game at night — violet squircle panels lit from above, every part on a lip of its own darker
        /// color, candy tones, a rounded heavy face.
        /// </summary>
        private Look Candy() => new()
        {
            Name = "Candy", Display = candyFace, Body = roundFace, DisplayWeight = 800f, BodyWeight = 800f, Depth = Depth.Lip, Radius = 20f,
            ScreenTop = Hex(0x4A2C9A), ScreenBottom = Hex(0x1E1350),
            Panel = Hex(0x2E2168), PanelHigh = Hex(0x3E2E88), Inset = Hex(0x1A1142), Line = Hex(0x6B59C7), Outline = Hex(0x120B33),
            Text = Hex(0xFFFFFF), TextDim = Hex(0xBDB2EC), TextOnTone = Hex(0xFFFFFF),
            Primary = Tones(0x6BF5BE, 0x12A671, 0x0B6B47), Secondary = Tones(0xC9A0FF, 0x7B3FE0, 0x4A2196),
            Accent = Tones(0xFFE27A, 0xF28C00, 0xB35F00), Danger = Tones(0xFF8597, 0xD92F4E, 0x8E1F35),
            Gold = Hex(0xFFC93C), Silver = Hex(0xD5DCF5), Bronze = Hex(0xE79A5C),
            Coin = Hex(0xFFC93C), CoinDark = Hex(0xC77700), Gem = Hex(0x55B8FF), GemDark = Hex(0x1F5FC0),
            Rarity = new[] { Tones(0xC9C2EA, 0x8A82B8, 0x4E4878), Tones(0x7CCBFF, 0x2A86E0, 0x154C8A), Tones(0xD2A8FF, 0x8C4BF0, 0x50239A),
                Tones(0xFFD27A, 0xF28C00, 0xA05800) },
            Heading = text => Drop(text, Hex(0x120B33), new Vector2(0f, -0.07f)),
            OnTone = text => Drop(text, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -0.06f)),
            Ambience = (stage, screen) =>
            {
                Glow(stage, screen, new Vector2(-520f, 260f), 900f, Hex(0xB072FF, 0.35f));
                Glow(stage, screen, new Vector2(560f, -300f), 1000f, Hex(0x55B8FF, 0.26f));
                var sparkles = new RectTransform[Sparkles.Length];
                for (var i = 0; i < Sparkles.Length; i++)
                {
                    var sparkle = stage.Shape("Sparkle", screen, ShapeKind.Star);
                    stage.Box(sparkle.Rect, Sparkles[i], Vector2.one * (i % 3 == 0 ? 22f : 13f));
                    sparkle.Outline.StarPoints = 4;
                    sparkle.Outline.StarSharpness = 0.82f;
                    Stage.Solid(sparkle.Fill, Hex(0xFFFFFF, i % 2 == 0 ? 0.75f : 0.45f));
                    sparkles[i] = sparkle.Rect;
                }

                return t =>
                {
                    for (var i = 0; i < sparkles.Length; i++)
                        sparkles[i].localScale = Vector3.one * Mathf.Max(0f, Mathf.Sin(t * 2.6f + i * 1.9f)) * 1.2f;
                };
            }
        };

        /// <summary>
        /// Glossy: a bright toy shop — cream panels in a thick brown outline with a white rim, toy-like buttons with a shine
        /// across their tops, a sunburst behind it all.
        /// </summary>
        private Look Glossy() => new()
        {
            Name = "Glossy", Display = glossyFace, Body = roundFace, BodyWeight = 900f, Depth = Depth.Toy, Radius = 22f,
            ScreenTop = Hex(0x8FE0FF), ScreenBottom = Hex(0x3B9BEF),
            Panel = Hex(0xFFEDC8), PanelHigh = Hex(0xFFFBEF), Inset = Hex(0xF5DDB0), Line = Hex(0xE2B676), Outline = Hex(0x7A3E12),
            Text = Hex(0x6B3510), TextDim = Hex(0xA8743E), TextOnTone = Hex(0xFFFFFF),
            Primary = Tones(0x9CF26A, 0x3BB01E, 0x2E7A12), Secondary = Tones(0x7FD3FF, 0x2A8BE0, 0x1A5A9E),
            Accent = Tones(0xFFD36E, 0xFF8A1F, 0xB35400), Danger = Tones(0xFF8080, 0xE23838, 0x8E1A1A),
            Gold = Hex(0xFFC020), Silver = Hex(0xC8D2E0), Bronze = Hex(0xD98A4A),
            Coin = Hex(0xFFC930), CoinDark = Hex(0xC27C00), Gem = Hex(0xFF6FC0), GemDark = Hex(0xB52A7E),
            Rarity = new[] { Tones(0xD9DFEA, 0x9AA6BA, 0x5E6A80), Tones(0x7FD3FF, 0x2A8BE0, 0x1A5A9E), Tones(0xD39CFF, 0x9A45F0, 0x5E1FA0),
                Tones(0xFFD860, 0xFF9A10, 0xB35A00) },
            OnTone = text =>
            {
                Stroke(text, Hex(0x5A2A08), 0.12f);
                Drop(text, Hex(0x5A2A08), new Vector2(0f, -0.08f));
            },
            Ambience = (stage, screen) =>
            {
                var burst = stage.Node("Sunburst", screen);
                stage.Box(burst, new Vector2(0f, 60f), Vector2.zero);
                for (var i = 0; i < 18; i++)
                {
                    var ray = stage.Shape("Ray", burst, ShapeKind.Triangle);
                    stage.Box(ray.Rect, Vector2.zero, new Vector2(260f, 1700f));
                    ray.Rect.pivot = new Vector2(0.5f, 1f);
                    ray.Rect.anchoredPosition = Vector2.zero;
                    ray.Rect.localRotation = Quaternion.Euler(0f, 0f, i * 20f);
                    Stage.Solid(ray.Fill, Hex(0xFFFFFF, 0.07f));
                }

                Glow(stage, screen, new Vector2(0f, 120f), 1100f, Hex(0xFFFFFF, 0.32f));
                return t => burst.localRotation = Quaternion.Euler(0f, 0f, t * 8f);
            }
        };

        /// <summary>
        /// Neon: a synthwave rhythm game — black glass with cut corners edged in glowing cyan, hot magenta and acid yellow,
        /// tracked uppercase neon type over a technical body face, a grid floor running at the horizon.
        /// </summary>
        private Look Neon() => new()
        {
            Name = "Neon", Display = neonFace, Body = techFace, BodyWeight = 560f, Caps = true, Tracking = 0.05f, Depth = Depth.Glow, Radius = 16f,
            Smoothing = 0f, Corners = CornerStyle.Bevel, Type = 0.95f,
            ScreenTop = Hex(0x1A0B33), ScreenBottom = Hex(0x07030F),
            Panel = Hex(0x0C0718, 0.9f), PanelHigh = Hex(0x170D30, 0.9f), Inset = Hex(0x1E1234), Line = Hex(0x00F0FF), Outline = Hex(0x000000),
            Text = Hex(0xF2FFFF), TextDim = Hex(0x8CA2B6), TextOnTone = Hex(0xFFFFFF),
            Primary = Tones(0xFF2E97, 0xC4006A, 0x6A0038), Secondary = Tones(0x00D8E8, 0x007E8C, 0x004C55),
            Accent = Inked(0xFCEE09, 0xD4C600, 0x6E6700, 0x14001F), Danger = Tones(0xFF3B5C, 0xB80020, 0x5A0010),
            Gold = Hex(0xFCEE09), Silver = Hex(0xB8F6FF), Bronze = Hex(0xFF8A3D),
            Coin = Hex(0xFCEE09), CoinDark = Hex(0x8A8200), Gem = Hex(0xFF2E97), GemDark = Hex(0x7A0040),
            Rarity = new[] { Tones(0x8CA2B6, 0x4A5A6A, 0x202830), Tones(0x00F0FF, 0x0090A0, 0x004C55), Tones(0xFF2E97, 0xB0005E, 0x5A0030),
                Tones(0xFCEE09, 0xB0A600, 0x5A5500) },
            Heading = text => Glow(text, Hex(0x00F0FF, 0.8f), 0.26f),
            OnTone = text => Glow(text, Hex(0xFFFFFF, 0.45f), 0.14f),
            Ambience = (stage, screen) =>
            {
                Glow(stage, screen, new Vector2(0f, -330f), 1700f, Hex(0xFF2E97, 0.2f));
                Glow(stage, screen, new Vector2(-640f, 330f), 800f, Hex(0x00F0FF, 0.14f));
                var grid = new RectTransform[9];
                for (var i = 0; i < grid.Length; i++)
                {
                    var line = stage.Shape("Grid", screen, ShapeKind.RoundedRect);
                    stage.Box(line.Rect, Vector2.zero, new Vector2(ScreenSize.x, 2f));
                    Stage.Solid(line.Fill, Hex(0xFF2E97, 0.42f));
                    grid[i] = line.Rect;
                }

                for (var i = -8; i <= 8; i++)
                {
                    var line = stage.Shape("Ray", screen, ShapeKind.RoundedRect);
                    stage.Box(line.Rect, new Vector2(i * 130f, -380f), new Vector2(1.5f, 200f));
                    line.Rect.localRotation = Quaternion.Euler(0f, 0f, -i * 8f);
                    Stage.Solid(line.Fill, Hex(0xFF2E97, 0.22f));
                }

                return t =>
                {
                    for (var i = 0; i < grid.Length; i++)
                    {
                        var depth = Mathf.Repeat(i / (float)grid.Length + t * 0.35f, 1f);
                        grid[i].anchoredPosition = new Vector2(0f, -290f - depth * depth * 170f);
                        grid[i].sizeDelta = new Vector2(ScreenSize.x, 1f + depth * 2.5f);
                    }
                };
            },
            Trim = (stage, panel, size) => Ticks(stage, panel, size, 30f, 3f, Hex(0xFF2E97))
        };

        /// <summary>
        /// Sci-fi: a starfighter's hangar — cold glass HUD panels framed by brackets, hairlines and an inner light, wide
        /// tracked uppercase labels over a technical face, a dot grid and range rings behind.
        /// </summary>
        private Look Scifi() => new()
        {
            Name = "Scifi", Display = scifiFace, Body = techFace, DisplayWeight = 700f, BodyWeight = 520f, Caps = true, Tracking = 0.1f,
            Depth = Depth.Hud, Radius = 6f, Smoothing = 0f, Type = 0.82f,
            ScreenTop = Hex(0x0B1C38), ScreenBottom = Hex(0x02060F),
            Panel = Hex(0x0B1A30, 0.82f), PanelHigh = Hex(0x112A4C, 0.82f), Inset = Hex(0x07142A), Line = Hex(0x5CC8FF), Outline = Hex(0x000000),
            Text = Hex(0xE6F8FF), TextDim = Hex(0x7FA6C2), TextOnTone = Hex(0xE6F8FF),
            Primary = Tones(0x5CC8FF, 0x1F7FC2, 0x0E3E66), Secondary = Tones(0x8FA9C7, 0x45617F, 0x22324A),
            Accent = Tones(0xFFB547, 0xC77A0E, 0x6A3E04), Danger = Tones(0xFF5C6C, 0xB81E33, 0x5A0A16),
            Gold = Hex(0xFFD27A), Silver = Hex(0xCFE6F5), Bronze = Hex(0xE0A070),
            Coin = Hex(0xFFD27A), CoinDark = Hex(0x9A6A1E), Gem = Hex(0x9CF0FF), GemDark = Hex(0x2A8BA8),
            Rarity = new[] { Tones(0x8FA9C7, 0x45617F, 0x22324A), Tones(0x5CC8FF, 0x1F7FC2, 0x0E3E66), Tones(0xC08BFF, 0x7A3FD0, 0x3A1A6A),
                Tones(0xFFB547, 0xC77A0E, 0x6A3E04) },
            Heading = text => Glow(text, Hex(0x5CC8FF, 0.5f), 0.14f),
            Ambience = (stage, screen) =>
            {
                for (var x = -780f; x <= 780f; x += 50f)
                for (var y = -430f; y <= 430f; y += 50f)
                {
                    var dot = stage.Shape("Dot", screen, ShapeKind.Circle);
                    stage.Box(dot.Rect, new Vector2(x, y), Vector2.one * 2.5f);
                    Stage.Solid(dot.Fill, Hex(0x5CC8FF, 0.14f));
                }

                Glow(stage, screen, new Vector2(0f, 20f), 1100f, Hex(0x5CC8FF, 0.12f));
                var scan = stage.Shape("Scan", screen, ShapeKind.RoundedRect);
                stage.Box(scan.Rect, Vector2.zero, new Vector2(ScreenSize.x, 110f));
                Stage.Linear(scan.Fill, Hex(0x5CC8FF, 0f), Hex(0x5CC8FF, 0.08f), 270f);
                return t => scan.Rect.anchoredPosition = new Vector2(0f, 520f - Mathf.Repeat(t * 0.4f, 1f) * 1040f);
            },
            Trim = (stage, panel, size) => Brackets(stage, panel, size + new Vector2(16f, 16f), 30f, 2.5f, Hex(0x5CC8FF))
        };

        /// <summary>
        /// Retro: an eight-bit role-playing game's menu — blue gradient windows in a white and black double frame with
        /// notched corners, hard pixel shadows, a pixel face and a console's small palette.
        /// </summary>
        private Look Retro() => new()
        {
            Name = "Retro", Display = retroFace, Body = retroFace, Caps = true, Depth = Depth.Pixel, Radius = 8f, Smoothing = 0f,
            Corners = CornerStyle.Bevel, Type = 0.66f, BodyType = 0.66f,
            ScreenTop = Hex(0x14105A), ScreenBottom = Hex(0x080622),
            Panel = Hex(0x1C2794), PanelHigh = Hex(0x3448D8), Inset = Hex(0x0E145A), Line = Hex(0xFCFCFC), Outline = Hex(0x000000),
            Text = Hex(0xFCFCFC), TextDim = Hex(0xA8B4FF), TextOnTone = Hex(0xFCFCFC),
            Primary = Tones(0x3CD070, 0x00A844, 0x005A24), Secondary = Tones(0x5C94FC, 0x2858D8, 0x0C2A80),
            Accent = Inked(0xFCE020, 0xD8A800, 0x7A5A00, 0x000000), Danger = Tones(0xFC5454, 0xD82800, 0x6A1400),
            Gold = Hex(0xFCE020), Silver = Hex(0xBCBCBC), Bronze = Hex(0xE45C10),
            Coin = Hex(0xFCE020), CoinDark = Hex(0xA87000), Gem = Hex(0x3CBCFC), GemDark = Hex(0x0058F8),
            Rarity = new[] { Tones(0xBCBCBC, 0x7C7C7C, 0x3C3C3C), Tones(0x3CBCFC, 0x0078F8, 0x0040A0), Tones(0xD884FC, 0x9C3CDC, 0x5A1A80),
                Tones(0xFCE020, 0xD8A800, 0x7A5A00) },
            Heading = text => Drop(text, Hex(0x000000), new Vector2(0.12f, -0.12f)),
            OnTone = text => Drop(text, Hex(0x000000), new Vector2(0.1f, -0.1f)),
            Ambience = (stage, screen) =>
            {
                var pixels = new GameObject[Sparkles.Length];
                for (var i = 0; i < Sparkles.Length; i++)
                {
                    var pixel = stage.Shape("Pixel", screen, ShapeKind.RoundedRect);
                    stage.Box(pixel.Rect, Sparkles[i], Vector2.one * (i % 3 == 0 ? 8f : 5f));
                    Stage.Solid(pixel.Fill, Hex(0xFCFCFC, i % 2 == 0 ? 0.8f : 0.45f));
                    pixels[i] = pixel.Rect.gameObject;
                }

                return t =>
                {
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var on = Mathf.Repeat(t * 1.5f + i * 0.37f, 1f) > 0.25f;
                        if (pixels[i].activeSelf != on) pixels[i].SetActive(on);
                    }
                };
            }
        };

        /// <summary>
        /// Epic fantasy: a hero's sheet — parchment panels in bronze and gold trim with gem ornaments, velvet ribbons, a
        /// classical engraved face for names over a plain one for descriptions, light falling through a forest dusk.
        /// </summary>
        private Look Epic() => new()
        {
            Name = "Epic", Display = epicFace, Body = plainFace, DisplayWeight = 800f, Depth = Depth.Ornate, Radius = 14f, Type = 0.92f,
            ScreenTop = Hex(0x26392C), ScreenBottom = Hex(0x0B130F),
            Panel = Hex(0xDDBB82), PanelHigh = Hex(0xF7E9C6), Inset = Hex(0xEAD3A2), Line = Hex(0xB88A3E), Outline = Hex(0x5A3410),
            Text = Hex(0x3A2006), TextDim = Hex(0x7A5A30), TextOnTone = Hex(0xFFF0C0),
            Primary = Tones(0xB02A2A, 0x5A0E0E, 0x2A0505), Secondary = Tones(0x3E7A5A, 0x1F4A33, 0x0E2A1A),
            Accent = Inked(0xF2C75C, 0xB07A22, 0x5A3A0A, 0x3A2006), Danger = Tones(0xC0392B, 0x7A1A12, 0x3A0806),
            Gold = Hex(0xE9C46A), Silver = Hex(0xCFCFD8), Bronze = Hex(0xC47A3E),
            Coin = Hex(0xF2C75C), CoinDark = Hex(0x8A5A12), Gem = Hex(0xE23B5A), GemDark = Hex(0x7A0E24),
            Rarity = new[] { Tones(0xB8AE98, 0x7E7462, 0x4A4234), Tones(0x5A94F0, 0x2A5CB8, 0x142E66), Tones(0xB06CF0, 0x6E2EB8, 0x3A1466),
                Tones(0xF5A23A, 0xC0620E, 0x6A3004) },
            Heading = text => Drop(text, Hex(0xFFF5DC, 0.8f), new Vector2(0f, -0.05f)),
            OnTone = text =>
            {
                Ramp(text, Hex(0xFFF3C4), Hex(0xE0A93A));
                Drop(text, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, -0.06f));
            },
            Ambience = (stage, screen) =>
            {
                for (var i = 0; i < 6; i++)
                {
                    var ray = stage.Shape("Ray", screen, ShapeKind.RoundedRect);
                    stage.Box(ray.Rect, new Vector2(-640f + i * 260f, 260f), new Vector2(130f + i * 24f, 1500f));
                    ray.Rect.localRotation = Quaternion.Euler(0f, 0f, -22f);
                    Stage.Solid(ray.Fill, Hex(0xFFE6A0, 0.05f));
                }

                var motes = new RectTransform[Sparkles.Length];
                for (var i = 0; i < Sparkles.Length; i++)
                {
                    var mote = stage.Shape("Mote", screen, ShapeKind.Circle);
                    stage.Box(mote.Rect, Sparkles[i], Vector2.one * (i % 3 == 0 ? 7f : 4f));
                    Stage.Solid(mote.Fill, Hex(0xFFD27A, 0.6f));
                    motes[i] = mote.Rect;
                }

                Glow(stage, screen, new Vector2(0f, 420f), 1500f, Hex(0xFFE6A0, 0.14f));
                return t =>
                {
                    for (var i = 0; i < motes.Length; i++)
                    {
                        var rise = Mathf.Repeat(t * 0.12f + i * 0.13f, 1f);
                        motes[i].anchoredPosition = new Vector2(Sparkles[i].x + Mathf.Sin(t * 0.9f + i) * 16f, -440f + rise * 880f);
                    }
                };
            },
            Trim = (stage, panel, size) =>
            {
                foreach (var corner in new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) })
                {
                    var gem = stage.Shape("Ornament", panel, ShapeKind.Rhombus);
                    stage.Box(gem.Rect, corner * (size * 0.5f - new Vector2(2f, 2f)), new Vector2(24f, 30f));
                    Stage.Linear(gem.Fill, Hex(0xFFE9A6), Hex(0xB07A22), 270f);
                    Stage.AddStroke(gem.Shape, Hex(0x5A3A10), 2f, 1f);
                }
            }
        };

        /// <summary>A soft round glow of <paramref name="color"/> fading out to its edge.</summary>
        private static void Glow(Stage stage, Transform parent, Vector2 at, float size, Color color)
        {
            var halo = stage.Shape("Glow", parent, ShapeKind.Circle);
            stage.Box(halo.Rect, at, Vector2.one * size);
            Stage.Radial(halo.Fill, color, Theme.Fade(color, 0f));
        }

        /// <summary>Corner brackets framing a box of <paramref name="size"/>: two thin bars meeting at each corner.</summary>
        private static void Brackets(Stage stage, Transform parent, Vector2 size, float arm, float width, Color color)
        {
            foreach (var corner in new[] { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) })
                Corner(stage, parent, corner * size * 0.5f, corner, arm, width, color);
        }

        /// <summary>Brackets on two opposite corners only, the way a cyberpunk panel marks its frame.</summary>
        private static void Ticks(Stage stage, Transform parent, Vector2 size, float arm, float width, Color color)
        {
            foreach (var corner in new[] { new Vector2(-1f, 1f), new Vector2(1f, -1f) })
                Corner(stage, parent, corner * (size * 0.5f + new Vector2(6f, 6f)), corner, arm, width, color);
        }

        private static void Corner(Stage stage, Transform parent, Vector2 at, Vector2 corner, float arm, float width, Color color)
        {
            var across = stage.Shape("Bracket", parent, ShapeKind.RoundedRect);
            stage.Box(across.Rect, at - new Vector2(corner.x * arm * 0.5f, 0f), new Vector2(arm, width));
            Stage.Solid(across.Fill, color);
            var down = stage.Shape("Bracket", parent, ShapeKind.RoundedRect);
            stage.Box(down.Rect, at - new Vector2(0f, corner.y * arm * 0.5f), new Vector2(width, arm));
            Stage.Solid(down.Fill, color);
        }

        private static readonly Vector2[] Sparkles =
        {
            new(-760f, 330f), new(-690f, 160f), new(-560f, 300f), new(-470f, 410f), new(-420f, -400f), new(-740f, -60f),
            new(430f, 410f), new(520f, 220f), new(600f, 380f), new(700f, 290f), new(770f, 130f), new(330f, -420f),
            new(-610f, -350f), new(690f, -400f), new(-250f, 420f), new(250f, 425f), new(760f, -200f), new(-780f, -230f)
        };
    }
}
