using System;
using TMPro;
using UnityEngine;
using VAlign = LightSide.VerticalAlignment;

namespace LightSide.Promo
{
    /// <summary>
    /// One string rendered live by three engines at once: TextMeshPro, TextMeshPro with the community RTL fixer,
    /// and UniText — with the failures circled.
    /// </summary>
    /// <remarks>
    /// Assign <see cref="tmpFont"/> to the font asset a developer would actually reach for. Whatever it cannot cover
    /// renders as tofu or as nothing at all, and that is not a flaw in the demo — it is the demonstration.
    /// <para>
    /// One case per slide, so a viewer meets one rule at a time. A paragraph exercising eight of them at once shows
    /// everything and teaches nothing: the difference is on screen, and nobody who has not already been told what to
    /// look for finds it. Give a case a short string, set it large, and point at the exact glyph that moved.
    /// </para>
    /// <para>
    /// The specimens do not write themselves on. Three engines spelling out the same string at once is three things
    /// moving, and what the shot is about is not on any one frame of that — it is the difference between the three
    /// once they are still. They arrive with the surfaces that carry them, and the frame then holds.
    /// </para>
    /// <para>
    /// <see cref="marks"/> is authored by eye against a rendered frame, which is the only place an engine's mistake
    /// is visible. Build the shot first, look at the contact sheet, then place them.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(VersusSlide))]
    public sealed class VersusSlide : Slide
    {
        [SerializeField] private TMP_FontAsset tmpFont;
        [SerializeField, TextArea(2, 12)] private string message = DemoText.Postcard;

        [SerializeField] private string headline = "Every script, in one paragraph.";
        [SerializeField] private string sub;

        /// <summary>How the engines are laid out. Stacked for one line, side by side for a paragraph.</summary>
        [SerializeField] private VersusFlow flow = VersusFlow.Rows;

        /// <summary>
        /// Whether the sample's own base direction runs right to left.
        /// </summary>
        /// <remarks>
        /// It configures the competitor, never UniText. The RTL plugin reverses what it is given and has to be
        /// aligned to match, so on a left-to-right script it must be told so or it parks a Devanagari line against
        /// the wrong edge — a fault of the shot rather than of the engine. UniText resolves direction from the text
        /// and is told nothing.
        /// </remarks>
        [SerializeField] private bool rightToLeft = true;

        /// <summary>Body size as a fraction of the frame's height, so the surfaces fill it at any frame.</summary>
        [SerializeField, Range(0.012f, 0.1f)] private float textScale = 0.055f;

        /// <summary>
        /// How long the frame holds once the surfaces have arrived, before the first verdict lands.
        /// </summary>
        /// <remarks>
        /// The comparison is made in this beat and nowhere else: three renderings of one string are in front of the
        /// viewer and they have to find the difference before being told what it is. A paragraph needs longer than a
        /// line.
        /// </remarks>
        [SerializeField, Min(0.2f)] private float hold = 1.4f;

        /// <summary>The pills, in the order the engines are listed. Adjust once the frame has been looked at.</summary>
        [SerializeField] private VerdictSpec[] verdicts =
        {
            new VerdictSpec { label = "PERFECT", tone = VerdictTone.Pass },
            new VerdictSpec { label = "BROKEN", tone = VerdictTone.Broken },
            new VerdictSpec { label = "PARTIAL", tone = VerdictTone.Partial }
        };

        /// <summary>What is wrong and where, authored against a rendered frame.</summary>
        [SerializeField] private MarkSpec[] marks;

        private readonly Ease enter = Ease.EmphasizedIn;

        private VersusRig rig;
        private Specimen[] specimens;
        private Mark[] built;
        private Claim claim;
        private float verdictAt;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            if (!string.IsNullOrEmpty(headline)) claim = stage.Claim(stage.Root, headline, sub);

            if (verdicts == null || verdicts.Length != Engines.Length)
                throw new InvalidOperationException(
                    $"[Promo] '{name}' lists {verdicts?.Length ?? 0} verdicts and {Engines.Length} engines. " +
                    "There is one pill per engine, in the order the engines are listed.");

            var entries = new VersusEntry[Engines.Length];
            for (var i = 0; i < entries.Length; i++)
                entries[i] = new VersusEntry(Engines[i], verdicts[i].label, Fill(theme, verdicts[i].tone),
                    isProduct: i == 0);

            rig = stage.Versus(stage.Root, entries, flow);

            var size = stage.Height * textScale;
            var stacked = flow == VersusFlow.Rows;
            specimens = new Specimen[]
            {
                stage.UniTextSpecimen(rig[0].Body, size, theme.Text,
                    vertical: stacked ? VAlign.Middle : VAlign.Top),
                stage.TmpSpecimen(rig[1].Body, tmpFont, size, theme.Ink,
                    stacked ? TextAlignmentOptions.Left : TextAlignmentOptions.TopLeft),
                stage.RtlTmpSpecimen(rig[2].Body, tmpFont, size, theme.Ink, rightToLeft
                    ? (stacked ? TextAlignmentOptions.Right : TextAlignmentOptions.TopRight)
                    : (stacked ? TextAlignmentOptions.Left : TextAlignmentOptions.TopLeft))
            };

            for (var i = 0; i < specimens.Length; i++) specimens[i].SetText(message);

            verdictAt = RowsIn + (specimens.Length - 1) * RowStep + RowFade + hold;
            built = new Mark[marks?.Length ?? 0];
            for (var i = 0; i < built.Length; i++)
            {
                var spec = marks[i];
                if (spec.panel < 0 || spec.panel >= rig.Count)
                    throw new InvalidOperationException(
                        $"[Promo] '{name}' places mark {i} on panel {spec.panel}, and the rig has {rig.Count}. " +
                        "Panels are indexed in the order the engines are listed, which is the order they are laid " +
                        "out in.");

                built[i] = stage.Mark(rig[spec.panel].Body, spec);
                Cue("mark", spec.at);
            }

            for (var i = 0; i < specimens.Length; i++) Cue("verdict", verdictAt + i * Stagger);
        }

        protected override void OnRender(float local)
        {
            if (claim.Headline) claim.Pose(local);

            for (var i = 0; i < specimens.Length; i++)
            {
                rig.PoseColumn(i, enter.Window(local, RowsIn + i * RowStep, RowFade));
                specimens[i].Reveal(1f);
                rig.PoseVerdict(i, local - verdictAt - i * Stagger);
            }

            for (var i = 0; i < built.Length; i++) built[i].Pose(local - marks[i].at);
        }

        /// <remarks>
        /// A pass is green rather than brand violet, and shares its green with a passing <see cref="Mark"/>. The
        /// frame already says which engine is ours through the surface it is drawn on; spending the pill on that
        /// too would leave the viewer nothing that plainly means "correct".
        /// </remarks>
        private static Color Fill(Theme theme, VerdictTone tone) => tone switch
        {
            VerdictTone.Broken => theme.Coral,
            VerdictTone.Partial => theme.Orange,
            _ => theme.Green
        };

        /// <summary>
        /// The engines, in the order a viewer meets them: ours first, then the common one and its patch.
        /// </summary>
        /// <remarks>
        /// The answer goes first. A viewer who reads two broken lines before the correct one spends both of them not
        /// knowing what correct looks like, and arrives at the third with nothing to compare against.
        /// <para>
        /// Index 0 is the product. Verdict pills, specimens and every <c>MarkSpec</c> panel index count from this
        /// array, so a change here is a change to all four.
        /// </para>
        /// </remarks>
        private static readonly string[] Engines =
        {
            "UniText", "TextMeshPro", "TextMeshPro + RTL plugin"
        };

        /// <summary>When the surfaces arrive: after the headline has had the frame to itself.</summary>
        private const float RowsIn = 0.95f;

        private const float RowStep = 0.14f;
        private const float RowFade = 0.45f;

        private const float Stagger = 0.22f;
    }
}
