using System;
using UnityEngine;

namespace LightSide.Promo
{
    public sealed partial class GuiControlsScene
    {
        /// <summary>
        /// Ready-made pickers: a party member set up from lists and a wheel — a class picked from a dropdown, two skills
        /// ticked in a multi-select and a level spun on a wheel picker.
        /// </summary>
        private sealed class PickersPart : KitPart
        {
            private static readonly string[] Classes = { "Warrior", "Ranger", "Mage", "Rogue" };
            private static readonly string[] Skills = { "Archery", "Stealth", "Tracking", "Healing" };

            private readonly UniDropdown role;
            private readonly UniDropdown skills;
            private readonly Flick levelFlick;

            public PickersPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Pickers", Board, Scale)
            {
                Label("Party member", new Vector2(Left, Top - 34f), 24f, "Ink", display: true);

                Label("Class", new Vector2(Left, LabelY), 14f, "InkDim");
                role = Add("Dropdown", new Vector2(Left, FieldY), 0f).GetComponent<UniDropdown>();
                role.Options.ReplaceAll(Array.ConvertAll(Classes, text => new DropdownOption(text)));

                Label("Skills", new Vector2(Middle, LabelY), 14f, "InkDim");
                skills = Add("Multi-Select Dropdown", new Vector2(Middle, FieldY), 0f).GetComponent<UniDropdown>();
                skills.Options.ReplaceAll(Array.ConvertAll(Skills, text => new DropdownOption(text)));

                Label("Level", new Vector2(WheelLeft, LabelY), 14f, "InkDim");
                var level = Add("Wheel Picker", new Vector2(WheelLeft, WheelY), 0f).GetComponent<UniShapeValue>();
                level.Value = LevelFrom;
                levelFlick = new Flick(level, "level", LevelTo - LevelFrom);

                Plan(At(new Vector2(Left + 100f, FieldY - 90f)), new[]
                {
                    Beat.To(At(new Vector2(Left + 100f, FieldY)), null, Wide(200f)),
                    Beat.Click("class"),
                    Beat.To(At(new Vector2(Left + 80f, FieldY - ListFirst - ListStep)), null, Wide(160f)),
                    Beat.Click("ranger"),
                    Beat.To(At(new Vector2(Middle + 100f, FieldY)), null, Wide(200f)),
                    Beat.Click("skills"),
                    Beat.To(At(new Vector2(Middle + 80f, FieldY - ListFirst)), null, Wide(160f)),
                    Beat.Click("archery"),
                    Beat.To(At(new Vector2(Middle + 80f, FieldY - ListFirst - ListStep * 2f)), null, Wide(160f)),
                    Beat.Click("tracking"),
                    Beat.To(At(new Vector2(WheelLeft + WheelHalf, WheelY - WheelReach * 0.5f)), null, Wide(120f)),
                    Beat.Drag(At(new Vector2(WheelLeft + WheelHalf, WheelY + WheelReach * 0.5f)), "level", Wide(120f)),
                    Beat.Wait(0.3f)
                }, Pace);
                Aim("class", role);
                Aim("ranger", Captioned(role.gameObject, "Ranger"));
                Aim("skills", skills);
                Aim("archery", Captioned(skills.gameObject, "Archery"));
                Aim("tracking", Captioned(skills.gameObject, "Tracking"));
                Aim("level", level);
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                Show(role, played >= ClickAt("class") && played < ClickAt("ranger") + ListLinger);
                role.Value = played >= ClickAt("ranger") ? 1 : -1;
                Show(skills, played >= ClickAt("skills") && played < Pointer.Timeline.Mark("level") - ListLead);
                skills.SetChosen(0, played >= ClickAt("archery"));
                skills.SetChosen(2, played >= ClickAt("tracking"));
                Pose(levelFlick, played);
            }

            private static readonly Vector2 Board = new(1620f, 740f);
            private const float Scale = 2.4f;
            private const float Pace = 1.45f;
            private const float Top = 740f / Scale * 0.5f;
            private const float Left = -300f;
            private const float Middle = Left + 240f;
            private const float WheelLeft = Middle + 240f;
            private const float LabelY = Top - HeaderHeight;
            private const float FieldY = LabelY - LabelGap;
            private const float WheelY = LabelY - LabelTop - WheelHeight * 0.5f;
            private const float WheelHeight = 180f;
            private const float WheelHalf = 60f;
            private const float WheelReach = 110f;
            private const float ListStep = 34f;
            private const float ListLead = 0.1f;
            private const int LevelFrom = 5;
            private const int LevelTo = 12;
        }

        /// <summary>
        /// Ready-made pickers, the second card: a team color dragged out on the color picker — the hue strip swept from
        /// blue to ember and a tone picked in the shade square — while the team's banner beside it takes the color.
        /// </summary>
        private sealed class ColorPart : KitPart
        {
            private readonly UniColorPicker color;
            private readonly ShapePaint banner;

            public ColorPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Color", Board, Scale)
            {
                color = Add("Color Picker", new Vector2(PickerLeft, 0f), 0f).GetComponent<UniColorPicker>();

                Label("Team color", new Vector2(BannerLeft, Top - 44f), 24f, "Ink", display: true);
                var flag = stage.Shape("Banner", Holder, ShapeKind.RoundedRect, 26f);
                stage.Box(flag.Rect, new Vector2(BannerLeft + BannerSize.x * 0.5f, BannerY), BannerSize);
                banner = flag.Fill;
                Stage.Solid(banner, FirstColor);
                Stage.AddShadow(flag.Shape, new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, -8f), 24f);
                var emblem = stage.Shape("Emblem", flag.Rect, ShapeKind.Star);
                stage.Box(emblem.Rect, new Vector2(0f, 26f), Vector2.one * 110f);
                Stage.Solid(emblem.Fill, new Color(1f, 0.98f, 0.94f, 0.95f));
                Stage.AddShadow(emblem.Shape, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -4f), 10f);
                var team = stage.Label(flag.Rect, "SILVER KNIGHTS", 22f, Color.white, HorizontalAlignment.Center, stretch: false,
                    face: scene.theme.DisplayFace);
                stage.Box(team.rectTransform, new Vector2(0f, -74f), new Vector2(BannerSize.x - 20f, 34f));

                Plan(At(new Vector2(PickerLeft + 120f, -170f)), new[]
                {
                    Beat.To(At(new Vector2(PickerLeft + Hue(HueFrom), HueRise)), null, Wide(18f)),
                    Beat.Drag(At(new Vector2(PickerLeft + Hue(HueTo), HueRise)), "hue", Wide(18f)),
                    Beat.To(At(new Vector2(PickerLeft + Shade(ToneSaturation), Tone(ToneBrightness))), null, Wide(24f)),
                    Beat.Click("tone", true)
                }, Pace);
                Aim("hue", color);
                Aim("tone", color);
            }

            /// <summary>Where the hue strip puts <paramref name="hue"/>, from the picker's left end.</summary>
            private static float Hue(float hue) => 21f + 198f * hue;

            /// <summary>Where the shade square puts <paramref name="saturation"/>, from the picker's left end.</summary>
            private static float Shade(float saturation) => 12f + 216f * saturation;

            /// <summary>How far above the picker's middle the shade square puts <paramref name="brightness"/>.</summary>
            private static float Tone(float brightness) => 9.5f + 160f * brightness;

            private static Color FirstColor => Color.HSVToRGB(HueFrom, StartSaturation, StartBrightness);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                var hue = Mathf.Lerp(HueFrom, HueTo, Dragged("hue", played,
                    At(new Vector2(PickerLeft + Hue(HueFrom), HueRise)), At(new Vector2(PickerLeft + Hue(HueTo), HueRise))));
                var toned = played >= ClickAt("tone");
                var picked = Color.HSVToRGB(hue, toned ? ToneSaturation : StartSaturation, toned ? ToneBrightness : StartBrightness);
                if (color.Color != picked) color.Color = picked;
                if (banner.Color != picked) Stage.Solid(banner, picked);
            }

            private static readonly Vector2 Board = new(1400f, 740f);
            private const float Scale = 1.85f;
            private const float Pace = 1.3f;
            private const float Top = 740f / Scale * 0.5f;
            private const float PickerLeft = -300f;
            private const float BannerLeft = 40f;
            private static readonly Vector2 BannerSize = new(250f, 250f);
            private const float BannerY = -24f;
            private const float HueRise = -10f;
            private const float HueFrom = 0.6f;
            private const float HueTo = 0.07f;
            private const float StartSaturation = 0.7f;
            private const float StartBrightness = 0.9f;
            private const float ToneSaturation = 0.85f;
            private const float ToneBrightness = 0.95f;
        }

        /// <summary>
        /// Ready-made dates: a session booked — its days dragged out on the calendar while the range field spells them,
        /// and its length stepped on the time picker.
        /// </summary>
        private sealed class DatesPart : KitPart
        {
            private readonly UniCalendar calendar;
            private readonly UniDateField range;
            private readonly UniTimePicker length;

            public DatesPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Dates", Board, Scale)
            {
                calendar = Add("Calendar", new Vector2(Left, 0f), 0f).GetComponent<UniCalendar>();
                calendar.Selection = DateSelection.Range;

                Label("Book a session", new Vector2(Right, TitleY), 24f, "Ink", display: true);
                Label("Dates", new Vector2(Right, DatesLabel), 14f, "InkDim");
                range = Add("Date Range Picker", new Vector2(Right, DatesLabel - LabelTop - FieldHalf), 0f).GetComponent<UniDateField>();
                range.Calendar.Selection = DateSelection.Range;

                Label("Length", new Vector2(Right, LengthLabel), 14f, "InkDim");
                length = Add("Time Picker", new Vector2(Right, LengthY), 0f).GetComponent<UniTimePicker>();
                length.Cycle = HourCycle.TwentyFourHour;

                Plan(At(new Vector2(Left + Day(FirstDay).x, -190f)), new[]
                {
                    Beat.To(At(new Vector2(Left, 0f) + Day(FirstDay)), null, Wide(36f)),
                    Beat.Drag(At(new Vector2(Left, 0f) + Day(LastDay)), "range", Wide(36f)),
                    Beat.To(At(new Vector2(Right + LengthHours, LengthY + StepRise)), null, Wide(30f)),
                    Beat.Click("longer"),
                    Beat.Click("longer again", true)
                }, Pace);
                Aim("range", calendar);
                Aim("longer", Named(length.gameObject, "Increase"));
                Aim("longer again", Named(length.gameObject, "Increase"));
            }

            private static int DayAt(Vector2 fromCalendar)
            {
                var column = Mathf.Clamp(Mathf.RoundToInt((fromCalendar.x - 138f + 108f) / 36f), 0, 6);
                var row = Mathf.Clamp(Mathf.RoundToInt((60f - fromCalendar.y) / 36f), 0, 5);
                return row * 7 + column - (int)Month.DayOfWeek + 1;
            }

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                if (calendar.Month != Month) calendar.Month = Month;
                if (range.Calendar.Month != Month) range.Calendar.Month = Month;

                var span = Pointer.Timeline.Span("range");
                DateTime? first = null;
                DateTime? last = null;
                if (played >= span.x)
                {
                    first = Month.AddDays(FirstDay - 1);
                    var under = Native(Pointer.Timeline.Sample(Mathf.Min(played, span.y)).Position) - new Vector2(Left, 0f);
                    var day = Mathf.Clamp(DayAt(under), FirstDay, LastDay);
                    if (day > FirstDay) last = Month.AddDays(day - 1);
                }

                if (calendar.Date != first || calendar.RangeEnd != last) calendar.SetRange(first, last);
                if (range.Calendar.Date != first || range.Calendar.RangeEnd != last) range.Calendar.SetRange(first, last);

                var hours = 1 + (played >= ClickAt("longer") ? 1 : 0) + (played >= ClickAt("longer again") ? 1 : 0);
                if (length.Hour != hours) length.Hour = hours;
            }

            private static readonly Vector2 Board = new(1340f, 756f);
            private const float Scale = 2.1f;
            private const float Pace = 1.45f;
            private const float Left = -1340f / Scale * 0.5f + Pad;
            private const float Right = Left + 276f + 48f;
            private const float TitleY = 110f;
            private const float DatesLabel = 62f;
            private const float LengthLabel = -20f;
            private const float LengthY = LengthLabel - LabelTop - 32f;
            private const float LengthHours = 40f;
            private const float StepRise = 24f;
            private const int FirstDay = 14;
            private const int LastDay = 24;
        }

        /// <summary>
        /// Ready-made dates, the second card: the session's start spun on the time wheel while a summary restates it, and
        /// a reminder picked from the date picker's popup calendar.
        /// </summary>
        private sealed class ReminderPart : KitPart
        {
            private readonly UniTimePicker start;
            private readonly Flick hourFlick;
            private readonly UniDateField reminder;
            private readonly UniText when;
            private bool reminding;
            private bool picked;

            public ReminderPart(GuiControlsScene scene, Stage stage)
                : base(scene, stage, "Reminder", Board, Scale)
            {
                Label("Raid night", new Vector2(Left, TitleY), 24f, "Ink", display: true);
                Label("Starts at", new Vector2(Left, WheelLabel), 14f, "InkDim");
                start = Add("Time Wheel", new Vector2(Left, WheelY), 0f).GetComponent<UniTimePicker>();
                start.Hour = HourFrom;
                start.Minute = StartMinute;
                hourFlick = new Flick(start.GetComponentInChildren<UniWheelPicker>(true).GetComponent<UniShapeValue>(), "hour",
                    HourTo - HourFrom);

                Label("Dec 14 – Dec 24", new Vector2(Right, TitleY), 24f, "Ink", display: true);
                when = Label(When(HourFrom), new Vector2(Right, TitleY - 36f), 16f, "InkDim");

                Label("Remind me", new Vector2(Right, ReminderLabel), 14f, "InkDim");
                reminder = Add("Date Picker", new Vector2(Right, ReminderY), 0f).GetComponent<UniDateField>();
                reminder.Calendar.Date = Month.AddDays(FirstDay - 1);

                Plan(At(new Vector2(Left + HourColumn, WheelY - 160f)), new[]
                {
                    Beat.To(At(new Vector2(Left + HourColumn, WheelY - WheelReach * 0.5f)), null, Wide(70f)),
                    Beat.Drag(At(new Vector2(Left + HourColumn, WheelY + WheelReach * 0.5f)), "hour", Wide(70f)),
                    Beat.To(At(new Vector2(Right + 100f, ReminderY)), null, Wide(200f)),
                    Beat.Click("remind"),
                    Beat.To(At(PopupDay(RemindDay)), null, Wide(36f)),
                    Beat.Click("pick", true)
                }, Pace);
                Aim("hour", start);
                Aim("remind", reminder);
                Aim("pick", () => (RectTransform)reminder.Calendar.CellOf(Month.AddDays(RemindDay - 1)).transform);
            }

            private static string When(int hour) => $"Every evening at {hour}:{StartMinute:00}";

            /// <summary>
            /// Where <paramref name="day"/> sits in the reminder's popup calendar. The field is at the card's foot, so the
            /// popup flips to open above it.
            /// </summary>
            private static Vector2 PopupDay(int day) =>
                new Vector2(Right, ReminderY + FieldHalf + PopupGap + CalendarHalfHeight) + Day(day);

            protected override void PoseAction(float played)
            {
                base.PoseAction(played);
                Pose(hourFlick, played);
                var line = When(start.Hour);
                if (when.Text != line) when.Text = line;

                if (played >= ClickAt("remind") && !reminding)
                {
                    reminding = true;
                    Click(reminder.gameObject);
                }

                if (played >= ClickAt("pick") && !picked)
                {
                    picked = true;
                    Click(reminder.Calendar.CellOf(Month.AddDays(RemindDay - 1)).gameObject);
                }
            }

            private static readonly Vector2 Board = new(1300f, 756f);
            private const float Scale = 2f;
            private const float Pace = 1.45f;
            private const float Top = 756f / Scale * 0.5f;
            private const float Left = -1300f / Scale * 0.5f + Pad;
            private const float Right = Left + 276f + 48f;
            private const float TitleY = Top - 34f;
            private const float WheelLabel = TitleY - 50f;
            private const float WheelY = WheelLabel - LabelTop - 90f;
            private const float ReminderY = -Top + Pad + FieldHalf;
            private const float ReminderLabel = ReminderY + FieldHalf + LabelTop;
            private const float PopupGap = 6f;
            private const float CalendarHalfHeight = 150f;
            private const float HourColumn = 40f;
            private const float WheelReach = 100f;
            private const int RemindDay = 13;
            private const int HourFrom = 17;
            private const int HourTo = 20;
            private const int StartMinute = 30;
        }

        private static readonly DateTime Month = new(2026, 12, 1);

        /// <summary>The middle of <paramref name="day"/>'s cell in the shown month, from the calendar's middle.</summary>
        private static Vector2 Day(int day)
        {
            var cell = (int)Month.DayOfWeek + day - 1;
            return new Vector2(-108f + 36f * (cell % 7) + 138f, 60f - 36f * (cell / 7));
        }

        private static void Show(UniDropdown dropdown, bool open)
        {
            if (dropdown.IsOpen == open) return;
            if (open) dropdown.Open();
            else dropdown.Close();
        }

        private const int FirstDay = 14;
        private const float LabelTop = 14f;
        private const float FieldHalf = 20f;
        private const float ListFirst = 44f;
        private const float ListLinger = 0.15f;
    }
}
