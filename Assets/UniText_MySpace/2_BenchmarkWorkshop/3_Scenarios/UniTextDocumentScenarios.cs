using System;
using System.Text;
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The large-document workloads of <see cref="UniTextScenarioSuite"/>: a log in a Document View that grows every frame.</summary>
static class UniTextDocumentScenarios
{
    public static UniTextScenario[] Create() => new UniTextScenario[]
    {
        new LogScenario("document.follow", true, false),
        new LogScenario("document.reading", false, false),
        new LogScenario("document.capped", false, true)
    };

    /// <summary>
    /// A log of <see cref="Lines"/> Latin lines set through <see cref="UniTextBase.SetDocument"/> into a Document View
    /// without word wrap, one line appended per frame. The view follows the end, or stays in the middle while the log
    /// grows, or stays in the middle while the oldest line is dropped every frame, as a console with a line limit does.
    /// </summary>
    sealed class LogScenario : UniTextScenario
    {
        const int Lines = 2000;
        const float FontSize = 26f;

        readonly bool follow;
        readonly bool capped;
        readonly int[] lineLengths = new int[Lines + 1];
        RectTransform root;
        UniText text;
        UniTextDocumentView view;
        Rope log;
        string[] appended;
        int oldestLine;
        int lineCount;
        int nextAppended;
        bool placed;

        public LogScenario(string id, bool follow, bool capped) : base(id, true)
        {
            this.follow = follow;
            this.capped = capped;
        }

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateRoot(Id);
            var viewObject = new GameObject("Document View", typeof(RectTransform), typeof(RectMask2D)) { layer = 5 };
            var viewRect = (RectTransform)viewObject.transform;
            viewRect.SetParent(root, false);
            ScenarioRig.Stretch(viewRect);
            viewRect.offsetMin = new Vector2(24f, 24f);
            viewRect.offsetMax = new Vector2(-24f, -24f);
            text = rig.CreateText(viewRect, string.Empty, FontSize);
            text.WordWrap = false;
            view = viewObject.AddComponent<UniTextDocumentView>();

            var lines = UniTextScenarios.LatinLines;
            appended = new string[lines.Length];
            for (var i = 0; i < lines.Length; i++)
                appended[i] = "\n" + lines[i];
            var builder = new StringBuilder(Lines * 80);
            for (var i = 0; i < Lines; i++)
            {
                var line = lines[i % lines.Length];
                if (i > 0) builder.Append('\n');
                builder.Append(line);
                if (capped) PushLine(line.Length + 1);
            }
            nextAppended = Lines % lines.Length;
            log = Rope.FromChars(builder.ToString().AsSpan(), text.ScanProfile);
            text.SetDocument(log, true);
            if (follow) ExpectCommitEveryFrame(text);
        }

        public override void Step(int frame)
        {
            var line = appended[nextAppended];
            nextAppended = (nextAppended + 1) % appended.Length;
            log.Insert(log.CharCount, line.AsSpan());
            if (capped)
            {
                PushLine(line.Length);
                log.Delete(0, PopLine());
            }

            if (follow) view.Position = text.MaxDocumentTop;
            else if (!placed && text.MaxDocumentTop > 0)
            {
                view.Position = text.MaxDocumentTop * 0.5;
                placed = true;
            }
        }

        public override void Validate()
        {
            UniTextScenarios.RequireGlyphs(root);
            if (follow && text.DocumentTop < text.MaxDocumentTop - 1)
                throw new InvalidOperationException("The view does not show the end of the log.");
            if (!follow && (!placed || text.DocumentTop >= text.MaxDocumentTop - text.ViewportRect.height))
                throw new InvalidOperationException("The view does not stay inside the log.");
            if (capped && log.ParagraphCount != Lines)
                throw new InvalidOperationException($"The capped log holds {log.ParagraphCount} lines instead of {Lines}.");
        }

        public override void Describe(JObject workload)
        {
            workload["lines"] = log.ParagraphCount;
            workload["characters"] = log.CharCount;
            workload["blocks"] = log.BlockCount;
            workload["windowCodepoints"] = text.RenderedCodepointExtent;
            workload["glyphs"] = text.GlyphCount;
            workload["fontSize"] = FontSize;
            workload["view"] = follow ? "end" : "middle";
            workload["appendedPerFrame"] = 1;
            workload["droppedPerFrame"] = capped ? 1 : 0;
        }

        void PushLine(int length)
        {
            lineLengths[(oldestLine + lineCount) % lineLengths.Length] = length;
            lineCount++;
        }

        int PopLine()
        {
            var length = lineLengths[oldestLine];
            oldestLine = (oldestLine + 1) % lineLengths.Length;
            lineCount--;
            return length;
        }
    }
}
