using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide.Promo
{
    /// <summary>The charts and game boards of <see cref="GuiControlsScene"/>.</summary>
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made charts: one player's season over a period switch — matches played per stretch of the period as bars,
        /// the win rate over the same stretches as a line, and the same matches by mode as a ring whose middle counts them,
        /// with each mode's share beside it. Each pick of a period redraws all three from that period's numbers.
        /// </summary>
        private sealed class ChartsPart : KitPart
        {
            /// <summary>A period of the season: its stretches, matches and win rate per stretch, and matches per mode.</summary>
            private readonly struct Period
            {
                public Period(string[] stretches, float[] matches, float[] winRate, float[] modes)
                {
                    Stretches = stretches;
                    Matches = matches;
                    WinRate = winRate;
                    Modes = modes;
                }

                public string[] Stretches { get; }
                public float[] Matches { get; }
                public float[] WinRate { get; }
                public float[] Modes { get; }
            }

            private static readonly Period[] Periods =
            {
                new(new[] { "8a", "11a", "2p", "5p", "8p" }, new[] { 2f, 4f, 3f, 6f, 5f },
                    new[] { 50f, 75f, 67f, 83f, 60f }, new[] { 4f, 6f, 8f, 2f }),
                new(new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }, new[] { 14f, 18f, 11f, 23f, 20f },
                    new[] { 57f, 61f, 45f, 70f, 65f }, new[] { 18f, 24f, 32f, 12f }),
                new(new[] { "W1", "W2", "W3", "W4" }, new[] { 64f, 82f, 58f, 96f },
                    new[] { 52f, 58f, 49f, 63f }, new[] { 66f, 88f, 104f, 42f })
            };

            private static readonly string[] Modes = { "Solo", "Duo", "Squad", "Ranked" };

            /// <summary>The kit pie's slice colors, in the order of its slices.</summary>
            private static readonly string[] ModeSwatches = { "Accent", "Danger", "InkDim", "Line" };

            private readonly UniShapeValue period;
            private readonly UniChart matches;
            private readonly UniChart winRate;
            private readonly UniChart modes;
            private readonly UniText[] shares = new UniText[4];
            private int shown = -1;

            public ChartsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Charts", Board, Scale)
            {
                Label("Season stats", new Vector2(Left, Top - 34f), 24f, "Ink", display: true);
                Label("Mia Chen · Silver Knights", new Vector2(Left, Top - 60f), 13f, "InkDim");

                var switcher = Add("Segmented Control", PeriodAt, 1f);
                Kit.Relabel(switcher, "Option 1", "Day");
                Kit.Relabel(switcher, "Option 2", "Week");
                Kit.Relabel(switcher, "Option 3", "Month");
                period = switcher.GetComponent<UniShapeValue>();

                Label("Matches", new Vector2(Left, CaptionY), 13f, "InkDim");
                matches = Add("Bar Chart", new Vector2(Left, ChartY), 0f).GetComponentInChildren<UniChart>(true);
                Label("Win rate, %", new Vector2(LineLeft, CaptionY), 13f, "InkDim");
                winRate = Add("Line Chart", new Vector2(LineLeft, ChartY), 0f).GetComponentInChildren<UniChart>(true);
                Label("Modes", new Vector2(PieLeft, CaptionY), 13f, "InkDim");
                var pie = Add("Pie Chart", new Vector2(PieLeft, PieY), 0f);
                modes = pie.GetComponentInChildren<UniChart>(true);
                var total = (RectTransform)pie.transform.Find("Label");
                total.offsetMin += Vector2.up * TotalRise;
                total.offsetMax += Vector2.up * TotalRise;
                Label("matches", new Vector2(PieLeft + PieSize * 0.5f, PieY - TotalCaptionDrop), 11f, "InkDim", 0.5f);

                for (var i = 0; i < Modes.Length; i++)
                {
                    var at = new Vector2(PieLeft + PieSize * 0.5f - LegendWidth + LegendColumn * (i % 2),
                        PieY - PieSize * 0.5f - LegendDrop - LegendStep * (i / 2));
                    var dot = stage.Shape("Key", Holder, ShapeKind.Circle);
                    stage.Box(dot.Rect, at, Vector2.one * KeySize);
                    Stage.Solid(dot.Fill, scene.palette[ModeSwatches[i]]);
                    Label(Modes[i], at + new Vector2(KeyGap, 0f), 13f, "Ink");
                    shares[i] = Label(string.Empty, at + new Vector2(ShareRight, 0f), 13f, "InkDim", 1f);
                }

                Plan(Start, new[]
                {
                    Beat.To(At(PeriodAt + Option(1)), null, Wide(77f)),
                    Beat.Click("week", true),
                    Beat.To(At(PeriodAt + Option(2)), null, Wide(77f)),
                    Beat.Click("month", true)
                });
                Aim("week", Captioned(switcher, "Week"));
                Aim("month", Captioned(switcher, "Month"));
            }

            /// <summary>The middle of the switch's option <paramref name="index"/>, from the switch's right end.</summary>
            private static Vector2 Option(int index) => new(-120f - 77.3f + 77.3f * index, 0f);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var pick = played >= ClickAt("month") ? 2 : played >= ClickAt("week") ? 1 : 0;
                period.Value = pick;
                if (pick == shown) return;

                shown = pick;
                var numbers = Periods[pick];
                matches.Series[0].Values.ReplaceAll(numbers.Matches);
                matches.Labels.ReplaceAll(numbers.Stretches);
                winRate.Series[0].Values.ReplaceAll(numbers.WinRate);
                winRate.Labels.ReplaceAll(numbers.Stretches);
                modes.Series[0].Values.ReplaceAll(numbers.Modes);

                var total = 0f;
                foreach (var count in numbers.Modes) total += count;
                for (var i = 0; i < shares.Length; i++) shares[i].Text = $"{Mathf.RoundToInt(numbers.Modes[i] / total * 100f)}%";
            }

            private static readonly Vector2 Board = new(BoardWidth, BoardHeight);
            private const float BoardWidth = 1760f;
            private const float Scale = 1.85f;
            private const float Pad = 28f;
            private const float Left = -BoardWidth / Scale * 0.5f + Pad;
            private const float BoardHeight = 690f;
            private const float Top = BoardHeight / Scale * 0.5f;
            private static readonly Vector2 PeriodAt = new(-Left, Top - 44f);
            private const float CaptionY = Top - 104f;
            private const float ChartY = CaptionY - 14f - 100f;
            private const float ChartGap = 28f;
            private const float LineLeft = Left + 320f + ChartGap;
            private const float PieLeft = LineLeft + 320f + ChartGap;
            private const float PieSize = 160f;
            private const float PieY = ChartY + 18f;
            private const float TotalRise = 8f;
            private const float TotalCaptionDrop = 14f;
            private const float LegendWidth = 92f;
            private const float LegendColumn = 100f;
            private const float LegendDrop = 22f;
            private const float LegendStep = 22f;
            private const float KeySize = 8f;
            private const float KeyGap = 12f;
            private const float ShareRight = 84f;
        }
    }
}
