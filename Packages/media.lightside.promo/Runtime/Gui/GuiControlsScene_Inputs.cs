using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made inputs: a game's match settings card — a mode picked on the segmented control, a region stepped on
        /// the selector, players added on the stepper and a map picked from the radio group — sent off with its button
        /// while a summary line restates the choices.
        /// </summary>
        private sealed class InputsPart : KitPart
        {
            private static readonly string[] Regions = { "Europe", "Asia", "Americas" };
            private static readonly string[] Modes = { "Solo", "Duo", "Squad" };
            private static readonly string[] Maps = { "Desert", "Forest", "Snow" };

            private readonly UniShapeValue mode;
            private readonly UniSelector region;
            private readonly UniShapeValue players;
            private readonly UniToggle[] maps;
            private readonly UniText findLabel;
            private readonly GameObject spinner;
            private readonly UniText summary;

            public InputsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Inputs", Board, Scale)
            {
                Label("Match settings", new Vector2(Left, Top - 34f), 24f, "Ink", display: true);

                Row("Mode", LeftLabel, 0);
                var segmented = Add("Segmented Control", new Vector2(LeftControl, RowY(0)), 0f);
                Kit.Relabel(segmented, "Option 1", "Solo");
                Kit.Relabel(segmented, "Option 2", "Duo");
                Kit.Relabel(segmented, "Option 3", "Squad");
                mode = segmented.GetComponent<UniShapeValue>();

                Row("Region", RightLabel, 0);
                region = Add("Selector", new Vector2(RightControl, RowY(0)), 0f).GetComponent<UniSelector>();
                region.Options.ReplaceAll(Regions);

                Row("Players", LeftLabel, 1);
                players = Add("Stepper", new Vector2(LeftControl, RowY(1)), 0f).GetComponent<UniShapeValue>();

                Row("Map", RightLabel, 1);
                var radio = Add("Radio Group", new Vector2(RightControl, RadioY), 0f);
                Kit.Relabel(radio, "First", "Desert");
                Kit.Relabel(radio, "Second", "Forest");
                Kit.Relabel(radio, "Third", "Snow");
                maps = radio.GetComponentsInChildren<UniToggle>(true);

                Divider(new Vector2(0f, Bottom + ButtonHalf * 2f + FooterGap), Board.x / Scale - Pad * 2f);
                summary = Label(Summary(0, 0, 1, 0), new Vector2(Left, Bottom + ButtonHalf), 15f, "Ink");
                var find = Add("Button", new Vector2(-Left, Bottom + ButtonHalf), 1f);
                Kit.Relabel(find, "Button", "Find match");
                findLabel = find.GetComponentInChildren<UniText>(true);
                spinner = Add("Spinner", new Vector2(-Left - ButtonWidth - SpinnerGap - SpinnerHalf, Bottom + ButtonHalf));

                Plan(At(new Vector2(LeftControl + 200f, RowY(0) - 60f)), new[]
                {
                    Beat.To(At(new Vector2(LeftControl + SegmentEnd, RowY(0))), null, Wide(70f)),
                    Beat.Click("mode"),
                    Beat.To(At(new Vector2(RightControl + SelectorEnd, RowY(0))), null, Wide(36f)),
                    Beat.Click("region"),
                    Beat.To(At(new Vector2(LeftControl + StepperPlus, RowY(1))), null, Wide(36f)),
                    Beat.Click("players"),
                    Beat.Click("players again"),
                    Beat.To(At(new Vector2(RightControl + RadioRing, RadioY)), null, Wide(26f)),
                    Beat.Click("map"),
                    Beat.To(At(new Vector2(-Left - ButtonWidth * 0.5f, Bottom + ButtonHalf)), null, Wide(ButtonWidth)),
                    Beat.Click("find", true, true)
                }, Pace);
                Aim("mode", Captioned(segmented, "Squad"));
                Aim("region", Named(region.gameObject, "Next"));
                Aim("players", Named(players.gameObject, "Increase"));
                Aim("players again", Named(players.gameObject, "Increase"));
                Aim("map", Captioned(radio, "Forest"));
                Aim("find", find);
            }

            private static string Summary(int mode, int region, int players, int map) =>
                $"{Modes[mode]} · {Regions[region]} · {players} {(players == 1 ? "player" : "players")} · {Maps[map]}";

            /// <summary>A row's label in the column whose labels start at <paramref name="x"/>.</summary>
            private void Row(string text, float x, int row) => Label(text, new Vector2(x, RowY(row)), 15f, "InkDim");

            private static float RowY(int row) => Top - HeaderHeight - row * RowStep;

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var picked = played >= ClickAt("mode") ? 2 : 0;
                var place = played >= ClickAt("region") ? 1 : 0;
                var count = 1 + (played >= ClickAt("players") ? 1 : 0) + (played >= ClickAt("players again") ? 1 : 0);
                var map = played >= ClickAt("map") ? 1 : 0;
                mode.Value = picked;
                region.Value = place;
                players.Value = count;
                if (!maps[map].On) maps[map].On = true;
                var line = Summary(picked, place, count, map);
                if (summary.Text != line) summary.Text = line;

                var searching = played >= ClickAt("find");
                var caption = searching ? "Searching…" : "Find match";
                if (findLabel.Text != caption) findLabel.Text = caption;
                Stage.Appear((RectTransform)spinner.transform, searching ? played - ClickAt("find") : -1f, Motion.Back, SpinnerGrow);
            }

            private static readonly Vector2 Board = new(1700f, 680f);
            private const float Scale = 2.15f;
            private const float Pace = 1.45f;
            private const float Pad = 28f;
            private const float HeaderHeight = 80f;
            private const float RowStep = 70f;
            private const float Top = 680f / Scale * 0.5f;
            private const float Bottom = -Top + Pad;
            private const float Left = -1700f / Scale * 0.5f + Pad;

            private const float LeftLabel = Left;
            private const float LeftControl = LeftLabel + LabelColumn;
            private const float RightLabel = 30f;
            private const float RightControl = RightLabel + LabelColumn;
            private const float LabelColumn = 96f;

            /// <summary>The radio group's middle, where its second option sits: its first option level with the map row.</summary>
            private static float RadioY => RowY(1) - RadioDrop;

            private const float RadioDrop = 26f;
            private const float RadioRing = 10f;
            private const float SegmentEnd = 240f - 4f - 77.3f * 0.5f;
            private const float SelectorEnd = 200f - 18f;

            /// <summary>Where the stepper's increase button stands from its left end: the stepper hugs its digit.</summary>
            private const float StepperPlus = 63f;

            private const float ButtonWidth = 140f;
            private const float ButtonHalf = 20f;
            private const float SpinnerGap = 14f;
            private const float SpinnerHalf = 16f;
            private const float FooterGap = 14f;
        }

        /// <summary>
        /// Ready-made inputs, the second card: game options set by the pointer — a skill range and the volume dragged out,
        /// a difficulty rated, voice chat switched on, crossplay ticked and a tag chip picked.
        /// </summary>
        /// <remarks>
        /// Controls whose width follows their caption — the switch, the checkbox, the chips — have their captions cleared
        /// or hang by a fixed end, so every point the pointer aims at is known before the controls lay themselves out.
        /// </remarks>
        private sealed class OptionsPart : KitPart
        {
            private readonly UniShapeValue skillThumb;
            private readonly UniShapeValue skillFill;
            private readonly UniShapeValue volume;
            private readonly UniRating difficulty;
            private readonly UniToggle voice;
            private readonly UniToggle crossplay;
            private readonly UniToggle ranked;

            public OptionsPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Options", Board, Scale)
            {
                Label("Game options", new Vector2(Left, Top - 34f), 24f, "Ink", display: true);

                Row("Skill", LeftLabel, 0);
                var range = Add("Range Slider", new Vector2(LeftControl, RowY(0)), 0f);
                foreach (var value in range.GetComponentsInChildren<UniShapeValue>(true))
                {
                    if (value.name == "Thumb" && Mathf.Approximately(value.Value, SkillFrom)) skillThumb = value;
                    if (value.name == "Fill" && value.Floor != null) skillFill = value;
                }

                if (skillThumb == null || skillFill == null)
                    throw new InvalidOperationException("[Promo] The range slider no longer has an upper thumb and fill.");

                Row("Volume", LeftLabel, 1);
                volume = FillOf(Add("Slider", new Vector2(LeftControl, RowY(1)), 0f));

                Row("Difficulty", LeftLabel, 2);
                difficulty = Add("Rating", new Vector2(LeftControl, RowY(2)), 0f).GetComponent<UniRating>();

                Row("Voice chat", RightLabel, 0);
                var toggle = Add("Switch", new Vector2(RightControl + Track, RowY(0)), 1f);
                Kit.Relabel(toggle, "Switch", "");
                voice = toggle.GetComponent<UniToggle>();

                Row("Crossplay", RightLabel, 1);
                var box = Add("Checkbox", new Vector2(RightControl, RowY(1)), 0f);
                Kit.Relabel(box, "Checkbox", "");
                crossplay = box.GetComponent<UniToggle>();

                Row("Tags", RightLabel, 2);
                var chip = Add("Chip", new Vector2(RightControl, RowY(2)), 0f);
                Kit.Relabel(chip, "Chip", "Ranked");
                ranked = chip.GetComponent<UniToggle>();
                var coop = Add("Chip", new Vector2(RightControl, RowY(2)), 0f);
                Kit.Relabel(coop, "Chip", "Coop");
                Strip(new Vector2(RightControl, RowY(2)), ChipGap, chip, coop);

                Plan(At(new Vector2(LeftControl + 160f, RowY(2) - 50f)), new[]
                {
                    Beat.To(At(new Vector2(LeftControl + Along(SkillFrom), RowY(0))), null, Wide(28f)),
                    Beat.Drag(At(new Vector2(LeftControl + Along(SkillTo), RowY(0))), "skill", Wide(28f)),
                    Beat.To(At(new Vector2(LeftControl + Along(VolumeFrom), RowY(1))), null, Wide(28f)),
                    Beat.Drag(At(new Vector2(LeftControl + Along(VolumeTo), RowY(1))), "volume", Wide(28f)),
                    Beat.To(At(new Vector2(LeftControl + Star(3), RowY(2))), null, Wide(26f)),
                    Beat.Click("difficulty"),
                    Beat.To(At(new Vector2(RightControl + Track * 0.5f, RowY(0))), null, Wide(46f)),
                    Beat.Click("voice"),
                    Beat.To(At(new Vector2(RightControl + BoxMiddle, RowY(1))), null, Wide(20f)),
                    Beat.Click("crossplay"),
                    Beat.To(At(new Vector2(RightControl + ChipAim, RowY(2))), null, Wide(60f)),
                    Beat.Click("ranked", true)
                }, Pace);
                Aim("skill", range);
                Aim("difficulty", Named(difficulty.gameObject, "Star 4"));
                Aim("voice", toggle);
                Aim("crossplay", box);
                Aim("ranked", chip);
            }

            /// <summary>A row's label in the column whose labels start at <paramref name="x"/>.</summary>
            private void Row(string text, float x, int row) => Label(text, new Vector2(x, RowY(row)), 15f, "InkDim");

            private static float RowY(int row) => Top - HeaderHeight - row * RowStep;

            /// <summary>How far from a value control's left end its thumb stands at <paramref name="value"/>.</summary>
            private static float Along(float value) => 14f + 212f * value;

            /// <summary>How far from the rating's left end the middle of star <paramref name="index"/> stands.</summary>
            private static float Star(int index) => 13f + 31f * index;

            private static UniShapeValue FillOf(GameObject slider)
            {
                foreach (var value in slider.GetComponentsInChildren<UniShapeValue>(true))
                    if (value.name == "Fill")
                        return value;
                throw new InvalidOperationException("[Promo] The slider no longer has a fill value.");
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var skill = Mathf.Lerp(SkillFrom, SkillTo, Dragged("skill", played,
                    At(new Vector2(LeftControl + Along(SkillFrom), RowY(0))), At(new Vector2(LeftControl + Along(SkillTo), RowY(0)))));
                skillThumb.TrackTo(skill);
                skillFill.TrackTo(skill);
                volume.TrackTo(Mathf.Lerp(VolumeFrom, VolumeTo, Dragged("volume", played,
                    At(new Vector2(LeftControl + Along(VolumeFrom), RowY(1))), At(new Vector2(LeftControl + Along(VolumeTo), RowY(1))))));

                difficulty.Value = played >= ClickAt("difficulty") ? 4 : 2;
                Set(voice, played >= ClickAt("voice"));
                Set(crossplay, played >= ClickAt("crossplay"));
                Set(ranked, played >= ClickAt("ranked"));
            }

            private static void Set(UniToggle toggle, bool on)
            {
                if (toggle.On != on) toggle.On = on;
            }

            private static readonly Vector2 Board = new(1700f, 500f);
            private const float Scale = 2.15f;
            private const float Pace = 1.45f;
            private const float Pad = 28f;
            private const float HeaderHeight = 76f;
            private const float RowStep = 50f;
            private const float Top = 500f / Scale * 0.5f;
            private const float Left = -1700f / Scale * 0.5f + Pad;

            private const float LeftLabel = Left;
            private const float LeftControl = LeftLabel + LabelColumn;
            private const float RightLabel = 30f;
            private const float RightControl = RightLabel + LabelColumn;
            private const float LabelColumn = 110f;
            private const float Track = 46f;
            private const float BoxMiddle = 10f;
            private const float ChipGap = 8f;
            private const float ChipAim = 30f;
            private const float SkillFrom = 0.6f;
            private const float SkillTo = 0.88f;
            private const float VolumeFrom = 0.35f;
            private const float VolumeTo = 0.8f;
        }
    }
}
