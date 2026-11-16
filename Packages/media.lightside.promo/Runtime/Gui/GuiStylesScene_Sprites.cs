using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiStylesScene
    {
        /// <summary>
        /// The retro game's pixel art: each sprite a grid of palette letters, a dot for a clear pixel, drawn as one shape per
        /// run of a color along a row.
        /// </summary>
        private static class Sprites
        {
            /// <summary>Draws <paramref name="rows"/> centered on <paramref name="at"/>, each pixel <paramref name="pixel"/> units across.</summary>
            public static RectTransform Draw(Builder k, Transform parent, Vector2 at, float pixel, string[] rows)
            {
                var node = k.Node(parent, "Sprite", at);
                var height = rows.Length;
                for (var y = 0; y < height; y++)
                {
                    var row = rows[y];
                    var width = row.Length;
                    for (var x = 0; x < width;)
                    {
                        var c = row[x];
                        var end = x + 1;
                        while (end < width && row[end] == c) end++;
                        if (c != '.')
                        {
                            var center = new Vector2(((x + end) * 0.5f - width * 0.5f) * pixel, (height * 0.5f - y - 0.5f) * pixel);
                            k.Fill(node, "Pixels", ShapeKind.RoundedRect, center, new Vector2((end - x) * pixel + Seam, pixel + Seam), Palette[c], 0f);
                        }

                        x = end;
                    }
                }

                return node;
            }

            /// <summary>How far each run overlaps its neighbors, so no hairline of the background shows between abutting pixels.</summary>
            private const float Seam = 0.6f;

            private static readonly Dictionary<char, Color> Palette = new()
            {
                ['k'] = Hex(0x000000), ['w'] = Hex(0xFCFCFC), ['s'] = Hex(0xFCBCA0), ['S'] = Hex(0xD87C60), ['e'] = Hex(0x201010),
                ['h'] = Hex(0xFCD848), ['m'] = Hex(0xBCBCBC), ['M'] = Hex(0x6C6C6C), ['y'] = Hex(0xFCE020), ['r'] = Hex(0xD82800),
                ['R'] = Hex(0xFC7460), ['b'] = Hex(0x0058F8), ['B'] = Hex(0x3CBCFC), ['g'] = Hex(0x00A844), ['G'] = Hex(0x58D854),
                ['p'] = Hex(0x8C2CDC), ['P'] = Hex(0xC884FC), ['o'] = Hex(0xE45C10), ['n'] = Hex(0xAC7C00), ['N'] = Hex(0x503000)
            };

            public static readonly string[] Arin =
            {
                "...kkkkkk...",
                "..kmmmmwwk..",
                ".kmmmmmmmwk.",
                ".kmmmmmmmmk.",
                ".kkkkkkkkkk.",
                ".kMssssssMk.",
                ".kMseSSesMk.",
                ".kMssssssMk.",
                "..kMsrrsMk..",
                "..kkMMMMkk..",
                ".kbbkyykbbk.",
                "kbbbbbbbbbbk"
            };

            public static readonly string[] Mira =
            {
                ".....kk.....",
                "....kPpk....",
                "...kppppk...",
                "..kpppyppk..",
                "kppppppppppk",
                ".khsssssshk.",
                ".khseSSeshk.",
                ".khsssssshk.",
                ".khhsrrshhk.",
                "..khhsshhk..",
                ".kppkwwkppk.",
                "kppppppppppk"
            };

            public static readonly string[] Bram =
            {
                "..k......k..",
                ".kwk.kk.kwk.",
                ".kwkkmmkkwk.",
                "..kmmmmmmk..",
                ".kmmmmmmmmk.",
                ".kkkkkkkkkk.",
                ".ksseSSessk.",
                ".kooossoook.",
                "kooooooooook",
                "..koooooook.",
                "...koooook..",
                "kggggggggggk"
            };

            public static readonly string[] Merchant =
            {
                "...kkkkkk...",
                "..knnnnnnk..",
                ".knnNNNNnnk.",
                ".knNssssNnk.",
                ".knseSSesnk.",
                ".knssssssnk.",
                ".knsNNNNsnk.",
                ".knssrrssnk.",
                "..knssssnk..",
                ".knnkyykknk.",
                "knnnnnnnnnnk"
            };

            public static readonly string[] Potion =
            {
                "...kkkk...",
                "...knnk...",
                "....kk....",
                "...kBBk...",
                "..kRRRRk..",
                ".kRwRRRRk.",
                ".kRRRRRRk.",
                ".krrrrrrk.",
                "..krrrrk..",
                "...kkkk..."
            };

            public static readonly string[] Ether =
            {
                "...kkkk...",
                "...knnk...",
                "....kk....",
                "...kwwk...",
                "..kBBBBk..",
                ".kBwBBBBk.",
                ".kBBBBBBk.",
                ".kbbbbbbk.",
                "..kbbbbk..",
                "...kkkk..."
            };

            public static readonly string[] Herb =
            {
                "......kkk.",
                ".....kGGGk",
                "....kGGGgk",
                "...kGGGgk.",
                "..kGGGgk..",
                ".kGGggk...",
                ".kGgkk....",
                "kngk......",
                "knk.......",
                ".k........"
            };

            public static readonly string[] Sword =
            {
                "....kk....",
                "...kwmk...",
                "...kwmk...",
                "...kwmk...",
                "...kwmk...",
                "...kwmk...",
                ".kkyyyykk.",
                "...knnk...",
                "...knnk...",
                "...kyyk...",
                "....kk...."
            };

            public static readonly string[] Shield =
            {
                ".kkkkkkkk.",
                "kbbbbbbbbk",
                "kbBbyybBbk",
                "kbbbyybbbk",
                "kbyyyyyybk",
                "kbbbyybbbk",
                ".kbbyybbk.",
                "..kbbbbk..",
                "...kbbk...",
                "....kk...."
            };

            public static readonly string[] Key =
            {
                "..kkk.....",
                ".kyyyk....",
                "kyk.kyk...",
                "kyk.kyk...",
                ".kyyyk....",
                "..kyk.....",
                "..kyyyk...",
                "..kyk.....",
                "..kyyk....",
                "...kk....."
            };

            public static readonly string[] Bomb =
            {
                "......yy..",
                ".....kn...",
                "...kkkk...",
                "..kMMMMk..",
                ".kMwMMMMk.",
                ".kMMMMMMk.",
                ".kMMMMMMk.",
                ".kMMMMMMk.",
                "..kMMMMk..",
                "...kkkk..."
            };

            public static readonly string[] Scroll =
            {
                ".kkkkkkkk.",
                "knwwwwwwnk",
                ".kwMwMwwk.",
                ".kwwwwwwk.",
                ".kwMMwMwk.",
                ".kwwwwwwk.",
                "knwwwwwwnk",
                ".kkkkkkkk."
            };

            public static readonly string[] Gem =
            {
                "...kkkk...",
                "..kBwBBk..",
                ".kBwBBBBk.",
                "kbbbbbbbbk",
                ".kbBbbbbk.",
                "..kbbbbk..",
                "...kbbk...",
                "....kk...."
            };

            public static readonly string[] Bread =
            {
                "..kkkkk...",
                ".knnnnnk..",
                "knyynyynk.",
                "knnnnnnnnk",
                ".kNNNNNNk.",
                "..kkkkkk.."
            };
        }
    }
}
