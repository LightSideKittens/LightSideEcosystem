using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>Builds the argument reel into the open scene, entirely from code.</summary>
    internal static class PromoSceneCommands
    {
        /// <summary>
        /// Builds the capture rig and the demo slides into the open scene, replacing any rig already there.
        /// </summary>
        /// <remarks>
        /// Replaces rather than adopts. A rig left by an earlier version of this command has whatever slides that
        /// version knew about, and silently handing it back looks identical to having created a current one.
        /// <para>
        /// Only a rig this command made is replaced. A reel the author added to their own hierarchy is reported and
        /// left alone; deleting the scene root that happens to be above it would take everything else with it.
        /// </para>
        /// </remarks>
        [MenuItem(PromoMenu.Tools.CreateReel, false, 10)]
        private static void CreateReel()
        {
            var existing = FindReel();
            if (existing)
            {
                var host = existing.transform;
                while (host && host.name != PromoMenu.ReelObjectName) host = host.parent;

                if (!host)
                {
                    Debug.LogWarning(
                        $"A Reel already exists on '{existing.name}', outside any '{PromoMenu.ReelObjectName}' " +
                        "rig. Remove it before running " + PromoMenu.Tools.CreateReel + ".");
                    return;
                }
            }

            var reel = PromoRig.Create(PromoMenu.ReelObjectName, "Create Promo Reel", out var reelRect);
            if (!reel) return;

            PromoRig.AddSlide<OpeningSlide>(reelRect, "00 Opening", 5.75f, new Cut());
            PromoRig.AddSlide<ScriptsSlide>(reelRect, "01 Script Wall", 6.5f, new Push());
            var versusArabic = PromoRig.AddSlide<VersusSlide>(reelRect, "02a Versus Arabic", 8f, new Push());
            var versusHindi = PromoRig.AddSlide<VersusSlide>(reelRect, "02b Versus Hindi", 8f, new Push());
            var versusAll = PromoRig.AddSlide<VersusSlide>(reelRect, "02c Versus Everything", 9f, new Push());
            PromoRig.AddSlide<SystemFontsSlide>(reelRect, "03 System Fonts", 5f, new Lift());
            PromoRig.AddSlide<CjkSlide>(reelRect, "03b CJK Wall", 7f, new Push());
            PromoRig.AddSlide<ConformanceSlide>(reelRect, "04 Conformance", 4.5f, new Push());
            PromoRig.AddSlide<EmojiSlide>(reelRect, "05 Emoji", 4.5f, new Push());
            PromoRig.AddSlide<AssetChurnSlide>(reelRect, "06 Asset Churn", 5f, new Push());
            PromoRig.AddSlide<BuildSizeSlide>(reelRect, "07 Build Size", 5.5f, new Push());
            var richText = PromoRig.AddSlide<RichTextSlide>(reelRect, "08 Rich Text", 4.5f, new Lift());
            PromoRig.AddSlide<HighlightPaintsSlide>(reelRect, "09 Highlights", 5f, new Push());
            var kit = PromoRig.AddSlide<KitStackSlide>(reelRect, "10 Kit Stack", 12f, new Lift());
            var extrude = PromoRig.AddSlide<ExtrudeSlide>(reelRect, "11 Extrude", 5f, new Push());
            PromoRig.AddSlide<LivingTextSlide>(reelRect, "12 Living Text", 5.5f, new Push());
            var variable = PromoRig.AddSlide<VariableFontSlide>(reelRect, "12b Variable Font", 6f, new Push());
            var worldText = PromoRig.AddSlide<WorldTextSlide>(reelRect, "13 World Text", 5.5f, new Push());
            PromoRig.AddSlide<MentionsSlide>(reelRect, "14 Mentions", 5f, new Push());
            PromoRig.AddSlide<SpoilersSlide>(reelRect, "15 Spoilers", 6f, new Push());
            PromoRig.AddSlide<FindSlide>(reelRect, "16 Find", 8f, new Push());
            var copyPaste = PromoRig.AddSlide<FieldCopySlide>(reelRect, "17 Copy Paste", 5f, new Push());
            var inlineMedia = PromoRig.AddSlide<InlineMediaSlide>(reelRect, "18 Inline Media", 5f, new Push());
            PromoRig.AddSlide<InputKitSlide>(reelRect, "19 Input Kit", 7.75f, new Lift());
            var math = PromoRig.AddSlide<MathSlide>(reelRect, "20 Math", 5f, new Push());
            PromoRig.AddSlide<TypographySlide>(reelRect, "21 Typography", 5f, new Push());
            PromoRig.AddSlide<SpeedSlide>(reelRect, "22 Speed", 4.5f, new Push());
            var endCard = PromoRig.AddSlide<TitleSlide>(reelRect, "23 End Card", 3.5f, new CrossFade());

            PromoRig.Wire(reel, "theme.displayFace", PromoRig.FindAsset<UniTextFont>("FiraSans-ExtraBold"));
            PromoRig.Wire(reel, "theme.bodyFace", PromoRig.FindAsset<UniTextFont>("FiraSans-Medium"));

            var tmp = PromoRig.FindAsset<TMP_FontAsset>("NotoSans-Regular SDF");
            WireCase(versusArabic, tmp, DemoText.Trip, true, VersusFlow.Rows, 0.055f, 1.4f,
                "Your Arabic will look like Arabic.",
                "The number, the brackets and the full stop all belong at the other end.");
            WireMarks(versusArabic, new[]
            {
                Ring(2, 0.982f, 4.3f, MarkTone.Fault),
                Ring(0, 0.982f, 4.3f, MarkTone.Pass),
                Ring(2, 0.508f, 5.1f, MarkTone.Fault),
                Ring(0, 0.508f, 5.1f, MarkTone.Pass)
            });

            WireCase(versusHindi, tmp, DemoText.Hindi, false, VersusFlow.Rows, 0.058f, 1.4f,
                "Your Hindi will look like Hindi.",
                "Letters that must combine, combined — by the font's own rules.");

            WireMarks(versusHindi, new[]
            {
                Underline(1, 0.155f, 0.31f, 4.4f, MarkTone.Fault),
                Underline(2, 0.155f, 0.31f, 4.4f, MarkTone.Fault),
                Underline(0, 0.155f, 0.31f, 4.4f, MarkTone.Pass),
                Note(1, Unreadable, 5f),
                Note(2, Unreadable, 5f)
            });

            WireCase(versusAll, tmp, DemoText.Postcard, false, VersusFlow.Columns, 0.026f, 2.6f,
                "And all of it at once.",
                "Ordinary holiday chatter. Nothing in it is a special case.");

            WireVerdicts(versusHindi, Failing);
            WireVerdicts(versusAll, Failing);

            var links = PromoRig.FindAsset<ModifierGraphPreset>("LinkPreset");
            var display = PromoRig.FindAsset<UniTextFont>("Modak-Regular");
            PromoRig.Wire(richText, "linkPreset", links);
            PromoRig.Wire(copyPaste, "linkPreset", links);
            PromoRig.Wire(kit, "displayFont", display);
            PromoRig.Wire(kit, "paints", PromoRig.FindAsset<UniTextPaints>("DemoPaints"));
            PromoRig.Wire(extrude, "displayFont", display);
            PromoRig.Wire(math, "mathFont", PromoRig.FindAsset<UniTextFont>("STIXTwoMath-Regular"));
            PromoRig.Wire(inlineMedia, "image", PromoRig.FindAsset<Sprite>("cat-sulejman"));
            PromoRig.Wire(worldText, "tmpFont", PromoRig.FindAsset<TMP_FontAsset>("NotoSans-Regular SDF"));
            PromoRig.Wire(variable, "variableFont", PromoRig.FindAsset<UniTextFont>("NotoSans-VariableFont_wdth,wght"));
            PromoRig.Wire(endCard, "logo", PromoRig.FindAsset<Texture2D>("unitext-icon"));
            WireArray(copyPaste, "toolbarIcons", ToolbarIcons);

            PromoRig.Finish(reel);
        }

        [MenuItem(PromoMenu.Tools.Rebuild, false, 11)]
        private static void Rebuild()
        {
            var reel = FindReel();
            if (!reel)
            {
                Debug.LogWarning("No Reel in the open scene. Run " + PromoMenu.Tools.CreateReel + " first.");
                return;
            }
            reel.Rebuild();
        }

        /// <summary>The formatting glyphs a document editor's toolbar carries, in reading order.</summary>
        private static readonly string[] ToolbarIcons =
        {
            "bold", "italic", "underline", "strikethrough", "color", "link"
        };

        /// <summary>Fills a serialized array with the assets named in <paramref name="names"/>, skipping any missing.</summary>
        private static void WireArray(Object target, string field, string[] names)
        {
            var found = new List<Object>(names.Length);
            for (var i = 0; i < names.Length; i++)
            {
                var asset = PromoRig.FindAsset<Texture2D>(names[i]);
                if (asset) found.Add(asset);
                else Debug.LogWarning($"[Promo] No icon named '{names[i]}' for '{target.name}.{field}'.");
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) return;

            property.arraySize = found.Count;
            for (var i = 0; i < found.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Fills one comparison slide's case, so the three of them are declared together and read as a sequence
        /// rather than as three inspectors that happen to differ.
        /// </summary>
        /// <remarks>
        /// A missing TextMeshPro font is reported rather than tolerated. Without one that engine draws nothing at
        /// all, which on this frame reads as a failure far worse than the one being demonstrated, and is not a
        /// failure it actually has.
        /// </remarks>
        private static void WireCase(VersusSlide slide, TMP_FontAsset font, string message, bool rightToLeft,
            VersusFlow flow, float textScale, float hold, string headline, string sub)
        {
            if (!font)
                Debug.LogWarning($"[Promo] '{slide.name}' has no TextMeshPro font; assign one before capturing.",
                    slide);

            var serialized = new SerializedObject(slide);
            serialized.FindProperty("tmpFont").objectReferenceValue = font;
            serialized.FindProperty("message").stringValue = message;
            serialized.FindProperty("rightToLeft").boolValue = rightToLeft;
            serialized.FindProperty("flow").enumValueIndex = (int)flow;
            serialized.FindProperty("textScale").floatValue = textScale;
            serialized.FindProperty("hold").floatValue = hold;
            serialized.FindProperty("headline").stringValue = headline;
            serialized.FindProperty("sub").stringValue = sub;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A ring over one panel, centered on <paramref name="x"/> across it and filling its height.
        /// </summary>
        /// <remarks>
        /// The same <paramref name="x"/> on two panels is the shot: one place, two engines, two answers. Give the
        /// pair the same <paramref name="at"/> as well — they read as one comparison rather than as two events.
        /// </remarks>
        private static MarkSpec Ring(int panel, float x, float at, MarkTone tone) => new MarkSpec
        {
            kind = MarkKind.Ring,
            tone = tone,
            panel = panel,
            center = new Vector2(x, 0.5f),
            size = new Vector2(0.055f, 0.9f),
            at = at
        };

        /// <summary>One pill, its word and its color authored together.</summary>
        private static VerdictSpec Verdict(string label, VerdictTone tone) =>
            new VerdictSpec { label = label, tone = tone };

        /// <summary>
        /// The pills for a case the RTL plugin does not help with at all.
        /// </summary>
        /// <remarks>
        /// The plugin repairs right-to-left runs and nothing else, so on a Devanagari line it has no work to do and
        /// its panel renders what plain TextMeshPro renders. A "partial" pill over a line the same shot has just
        /// underlined in red and captioned as unreadable contradicts its own frame.
        /// </remarks>
        private static readonly VerdictSpec[] Failing =
        {
            Verdict("PERFECT", VerdictTone.Pass),
            Verdict("BROKEN", VerdictTone.Broken),
            Verdict("BROKEN", VerdictTone.Broken)
        };

        /// <summary>
        /// Replaces a comparison slide's pills, for a case where an engine fares differently than it does by
        /// default.
        /// </summary>
        private static void WireVerdicts(VersusSlide slide, VerdictSpec[] verdicts)
        {
            var serialized = new SerializedObject(slide);
            var array = serialized.FindProperty("verdicts");
            array.arraySize = verdicts.Length;

            for (var i = 0; i < verdicts.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("label").stringValue = verdicts[i].label;
                element.FindPropertyRelative("tone").enumValueIndex = (int)verdicts[i].tone;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A stroke under a run of text, sized to the run rather than to the panel.</summary>
        private static MarkSpec Underline(int panel, float centerX, float width, float at, MarkTone tone) =>
            new MarkSpec
            {
                kind = MarkKind.Underline,
                tone = tone,
                panel = panel,
                center = new Vector2(centerX, 0.16f),
                size = new Vector2(width, 0.055f),
                at = at
            };

        /// <summary>Words in the empty half of a panel, saying what the mark under the text means.</summary>
        private static MarkSpec Note(int panel, string text, float at) => new MarkSpec
        {
            kind = MarkKind.Note,
            tone = MarkTone.Fault,
            panel = panel,
            center = new Vector2(0.675f, 0.5f),
            size = new Vector2(0.63f, 0.86f),
            note = text,
            at = at
        };

        /// <summary>
        /// The verdict on a Devanagari line neither TextMeshPro nor the RTL plugin shapes.
        /// </summary>
        /// <remarks>
        /// It claims unreadability, which is a claim about a competitor on a published frame and therefore one the
        /// owner confirms against a rendered shot before it ships, not one derived from the code.
        /// </remarks>
        private const string Unreadable = "Wrong glyphs. Unreadable to anyone who reads Hindi.";

        /// <summary>
        /// Fills a comparison slide's annotations, which are placed by eye and belong in its inspector afterwards.
        /// </summary>
        private static void WireMarks(VersusSlide slide, MarkSpec[] specs)
        {
            var serialized = new SerializedObject(slide);
            var array = serialized.FindProperty("marks");
            array.arraySize = specs.Length;

            for (var i = 0; i < specs.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("kind").enumValueIndex = (int)specs[i].kind;
                element.FindPropertyRelative("tone").enumValueIndex = (int)specs[i].tone;
                element.FindPropertyRelative("panel").intValue = specs[i].panel;
                element.FindPropertyRelative("center").vector2Value = specs[i].center;
                element.FindPropertyRelative("size").vector2Value = specs[i].size;
                element.FindPropertyRelative("note").stringValue = specs[i].note ?? string.Empty;
                element.FindPropertyRelative("at").floatValue = specs[i].at;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The reel in the open scene, found by component rather than by adopting whatever canvas happens to exist.
        /// </summary>
        internal static Reel FindReel() => ObjectUtils.FindAny<Reel>(true);
    }
}
