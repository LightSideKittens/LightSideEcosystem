using System;
using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A search running over mixed scripts: every hit lit at once, the current one lit differently.
    /// </summary>
    /// <remarks>
    /// <see cref="UniTextBase.FindAll"/> writes codepoint-correct ranges into a caller-owned span, and the two
    /// <see cref="MutableRangeSource"/>s here turn them into ordinary styles — so the highlight is the same modifier
    /// the rest of the film uses, with the same paints and the same geometry, rather than a search-specific widget.
    /// <para>
    /// The queries are deliberately not Latin-only. A search that matches by UTF-16 index finds the wrong thing the
    /// moment a surrogate pair or a combining mark is ahead of the match.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(FindSlide))]
    public sealed class FindSlide : Slide
    {
        [SerializeField] private string headline = "Find anything, in any script.";
        [SerializeField] private string sub = "Codepoint-correct, allocation-free, styled like everything else.";

        [SerializeField, TextArea(3, 8)]
        private string body =
            "The fox runs and the dog naps.\n\n" +
            "الثعلب يركض — fox and dog again.\n\n" +
            "きつねは走る。The fox never stops.";

        [SerializeField] private string[] queries = { "fox", "dog", "the" };

        private readonly TextRange[] hits = new TextRange[64];
        private readonly MutableRangeSource matches = new();
        private readonly MutableRangeSource current = new();

        private Claim claim;
        private Claim readout;
        private Showcase panel;
        private RevealModifier typewriter;
        private int found;

        protected override void OnBuild(Stage stage)
        {
            var theme = stage.Theme;
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, sub);
            readout = stage.Claim(stage.Root, Readout(queries[0], 0), top: false);

            panel = stage.Showcase("Find", stage.Root, "UniTextBase.FindAll → two range sources",
                body, stage.ContentHeight * 0.082f, heightFraction: 0.84f);

            matches.SetIdentity("promo.find.matches");
            current.SetIdentity("promo.find.current");

            panel.Body.Styles.Add(Style.FromSource(matches, new HighlightModifier
            {
                Paint = PaintRef.Solid(Theme.Fade(theme.Orange, 0.35f)),
                GeometryMapping = GeometryMapping.Range,
                Height = RangeHeight.Content,
                Padding = new UnitVector2(new Vector2(0.14f, 0.06f), UnitKind.Em),
                CornerRadius = UnitValue.Em(0.22f)
            }));

            panel.Body.Styles.Add(Style.FromSource(current, new HighlightModifier
            {
                Paint = PaintRef.Solid(theme.Magenta),
                GeometryMapping = GeometryMapping.Range,
                Height = RangeHeight.Content,
                Padding = new UnitVector2(new Vector2(0.14f, 0.06f), UnitKind.Em),
                CornerRadius = UnitValue.Em(0.22f),
                Priority = RangeDecorationPriorities.FindCurrent
            }));

            typewriter = stage.Typewriter(panel.Body, new FadeRevealHandler());

            Script.Rewind(Clear);
            for (var i = 0; i < queries.Length; i++)
            {
                var query = queries[i];
                Script.At(FirstSearch + i * Beat, "search", () => Search(query));

                for (var step = 1; step <= Cycles; step++)
                {
                    var index = step;
                    Script.At(FirstSearch + i * Beat + index * (Beat / (Cycles + 1)), "cycle",
                        () => Cycle(index));
                }
            }

            Cue("writeon", First);
            for (var i = 0; i < queries.Length; i++) Cue("search", FirstSearch + i * Beat);
            Cue("settled", FirstSearch + (queries.Length - 1) * Beat + Cycles * (Beat / (Cycles + 1)));
        }

        private void Search(string query)
        {
            found = panel.Body.FindAll(query, StringComparison.OrdinalIgnoreCase, hits);

            using (var update = matches.BeginUpdate(matches.Snapshot.Revision))
            {
                update.Clear();
                for (var i = 0; i < found; i++) update.Add(hits[i]);
                update.Commit();
            }

            SetCurrent(0);
            readout.Headline.SetText(Readout(query, found));
        }

        private static string Readout(string query, int count) =>
            count == 0 ? $"“{query}” — searching" : $"“{query}” — {count} matches";

        private void Cycle(int index) => SetCurrent(found == 0 ? 0 : index % found);

        private void SetCurrent(int index)
        {
            using var update = current.BeginUpdate(current.Snapshot.Revision);
            update.Clear();
            if (found > 0) update.Add(hits[index]);
            update.Commit();
        }

        private void Clear()
        {
            found = 0;
            Empty(matches);
            Empty(current);
        }

        private static void Empty(MutableRangeSource source)
        {
            var snapshot = source.Snapshot;
            if (!snapshot.IsValid || source.Count == 0) return;

            using var update = source.BeginUpdate(snapshot.Revision);
            update.Clear();
            update.Commit();
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local);
            typewriter.Front = UnitValue.Percent(GlyphReveal.Frontier.Window(local, First, WriteOn) * 100f);
            readout.Pose(local - (FirstSearch - 0.3f));
        }

        private const int Cycles = 2;
        private const float First = 0.3f;
        private const float WriteOn = 1.8f;
        private const float FirstSearch = 2.1f;
        private const float Beat = 1.5f;
    }
}
