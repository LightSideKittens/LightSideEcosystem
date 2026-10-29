#if UNITEXT_3D
using LightSide;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>The extruded-text workloads of <see cref="UniTextScenarioSuite"/>.</summary>
static class UniText3DScenarios
{
    public static UniTextScenario[] Create() => new UniTextScenario[]
    {
        new Text3DScenario("text3d.static", Text3DWorkload.Static, true),
        new Text3DScenario("text3d.animated", Text3DWorkload.Wave, true),
        new Text3DScenario("text3d.animated.cpu", Text3DWorkload.WaveCpu, true),
        new Text3DScenario("text3d.animated.cpu.st", Text3DWorkload.WaveCpu, false),
        new Text3DScenario("text3d.spin", Text3DWorkload.Spin, true),
        new Text3DScenario("text3d.outline", Text3DWorkload.Outline, true),
        new Text3DScenario("text3d.deform", Text3DWorkload.Deform, true),
        new Text3DScenario("text3d.counters", Text3DWorkload.Counters, true),
        new Text3DScenario("text3d.counters.st", Text3DWorkload.Counters, false)
    };

    enum Text3DWorkload
    {
        Static,
        Wave,
        WaveCpu,
        Spin,
        Outline,
        Deform,
        Counters
    }

    /// <summary>
    /// Extruded labels in a grid in front of the camera: static, animated by a whole-text modifier (placed by the
    /// shader, or on the CPU with Animate In Shader off), spun in depth, carrying a per-glyph outline layer, rippled by
    /// a wave deformer on its own clock, or counting.
    /// </summary>
    sealed class Text3DScenario : UniTextScenario
    {
        const int Columns = 4;
        const int Rows = 6;
        const int Count = Columns * Rows;
        const float FontSize = 72f;
        const string Prefix = "Score ";
        const int Digits = 5;

        static int shown;

        readonly Text3DWorkload workload;
        readonly UniText3D[] texts = new UniText3D[Count];
        readonly char[][] buffers = new char[Count][];
        Transform root;

        public Text3DScenario(string id, Text3DWorkload workload, bool parallel) : base(id, parallel) =>
            this.workload = workload;

        public override void Setup(ScenarioRig rig)
        {
            root = rig.CreateWorldRoot(Id);
            var labels = UniTextScenarios.Labels(Count);
            for (var i = 0; i < Count; i++)
            {
                var column = i % Columns;
                var row = i / Columns;
                var text = rig.CreateText3D(root, workload == Text3DWorkload.Counters ? Prefix : labels[i], FontSize);
                text.transform.position = new Vector3((column - (Columns - 1) * 0.5f) * 4.4f,
                    ((Rows - 1) * 0.5f - row) * 1.6f, 4f);
                texts[i] = text;
                switch (workload)
                {
                    case Text3DWorkload.Wave:
                        text.SetWholeText<WaveModifier>();
                        break;
                    case Text3DWorkload.WaveCpu:
                        text.AnimateInShader = false;
                        text.SetWholeText<WaveModifier>();
                        break;
                    case Text3DWorkload.Spin:
                        text.SetWholeText<Spin3DModifier>();
                        break;
                    case Text3DWorkload.Outline:
                        var outline = Text3DLayer.Default;
                        outline.unit = Text3DLayerUnit.Glyph;
                        text.Styles.Add(Style.WholeText(new Layer3DModifier { Layer = outline }));
                        text.SetWholeText<WaveModifier>();
                        break;
                    case Text3DWorkload.Deform:
                        var wave = Text3DDeformer.Default;
                        wave.kind = Text3DDeformerKind.Wave;
                        wave.amplitude = 0.25f;
                        wave.wavelength = 2.5f;
                        wave.speed = 1f;
                        text.Styles.Add(Style.WholeText(new Deform3DModifier { Deformer = wave }));
                        break;
                    case Text3DWorkload.Counters:
                        buffers[i] = (Prefix + new string('0', Digits)).ToCharArray();
                        break;
                }
            }
            if (workload == Text3DWorkload.Counters) ExpectCommitEveryFrame(texts[0]);
        }

        public override void Step(int frame)
        {
            if (workload != Text3DWorkload.Counters) return;
            for (var i = 0; i < Count; i++)
            {
                UniTextScenarios.WriteDigits(buffers[i], Prefix.Length, buffers[i].Length, NextValue());
                texts[i].SetText(buffers[i], 0, buffers[i].Length);
            }
        }

        static int NextValue() => (int)(shown++ * 7919L % 100_000);

        public override void Validate() => UniTextScenarios.RequireGlyphs(root);

        public override void Describe(JObject description)
        {
            var vertices = 0;
            foreach (var text in texts)
                vertices += text.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            description["texts"] = Count;
            description["glyphs"] = UniTextScenarios.CountGlyphs(root);
            description["vertices"] = vertices;
            description["fontSize"] = FontSize;
            description["changingTexts"] = workload == Text3DWorkload.Static ? 0 : Count;
            description["motion"] = workload.ToString();
        }
    }
}
#endif
