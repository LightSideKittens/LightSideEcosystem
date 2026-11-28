using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using LightSide.Benchmark;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Measures on the device how every engine draws <see cref="LookCalibrationProfile.House"/> and captures the calibration
/// strip and the effect close-up stills. Lives in the text benchmark scene and draws with that scene's engine setups.
/// </summary>
/// <remarks>
/// Each engine draws a capital I unstyled beside a coded one (face red, outline green, shadow blue at the look's alpha)
/// at the smallest and the largest case size. The result holds, per engine and size, the drawn face growth, outline
/// reach, shadow reach and shadow ink, their residuals against the look, whether they pass, the parameters drawn with,
/// and the coefficients (drawn em per parameter unit) measured at the largest size, which belong in
/// <see cref="LookCalibrationProfile"/> when they differ from its own.
/// </remarks>
public sealed class LookCalibrationSuite : MonoBehaviour, IBenchmarkSuite
{
    const string ProbeText = "I";
    const float TolerancePx = 0.5f;
    const float InkTolerance = 0.15f;
    static readonly float[] Sizes = { 28f, 72f };

    JObject result;

    public string SuiteId => "look";

    public string Section => "lookCalibration";

    public string StreamGlobal => "__unitextLookRuns";

    public int Scenario => 7;

    public IEnumerable<KeyValuePair<string, string>> PhaseNotes => new[]
    {
        new KeyValuePair<string, string>("look",
            "Every engine draws a capital I unstyled and with the house look coded in color (face red, outline green, shadow blue) over black, at 28 and 72 scenario units. Edges are half-coverage crossings on scan lines through the face center; face growth, outline reach and shadow reach are measured from the unstyled face edge and divided by the engine's em, which is its unstyled cap height over UniText's cap-height ratio. A size passes when every residual is within 0.5 px and the visible shadow ink within 15 % of UniText's.")
    };

    public IEnumerator Run(BenchmarkContext context)
    {
        result = null;
        yield return context.Run("look", Calibrate, () => { });
    }

    public JObject Serialize() => result;

    public bool Measured(out string reason)
    {
        reason = result == null ? "The look calibration produced no result." : null;
        return result != null;
    }

    IEnumerator Calibrate()
    {
        var stage = new LookCalibrationStage();
        try
        {
            yield return stage.Prepare();
            var look = LookCalibrationProfile.House;
            var coded = look.WithColors(Color.green, new Color(0f, 0f, 1f, look.ShadowColor.a));
            var engines = new JObject();
            var largest = new Dictionary<LookCalibrationEngine, Geometry>();
            var reference = default(Geometry);
            var pass = true;
            foreach (var engine in stage.Engines)
                engines[Id(engine)] = new JObject { ["sizes"] = new JObject(), ["geometry"] = new JObject() };

            foreach (var units in Sizes)
            {
                var capRatio = 0f;
                foreach (var engine in stage.Engines)
                {
                    int width = stage.Width, height = stage.Height;
                    stage.Clear();
                    stage.Background(Color.black);
                    stage.Add(engine, ProbeText, units, new Rect(0f, height * 0.25f, width * 0.5f, height * 0.5f),
                        Color.red, null);
                    stage.Add(engine, ProbeText, units,
                        new Rect(width * 0.5f, height * 0.25f, width * 0.5f, height * 0.5f), Color.red, coded);
                    yield return LookCalibrationStage.Settle();
                    yield return new WaitForEndOfFrame();
                    var frame = stage.Read();
                    var plain = LookCalibrationMeasure.Plain(frame, new RectInt(0, 0, width / 2, height));
                    var styled = LookCalibrationMeasure.Styled(frame, new RectInt(width / 2, 0, width - width / 2, height),
                        coded.ShadowColor.a);
                    if (engine == LookCalibrationEngine.UniText)
                        capRatio = plain.Height / (units * stage.CanvasScale);
                    var geometry = new Geometry(plain, styled, capRatio);
                    if (engine == LookCalibrationEngine.UniText)
                        reference = geometry;
                    var size = units.ToString("0", CultureInfo.InvariantCulture);
                    var entry = (JObject)engines[Id(engine)];
                    entry["sizes"][size] = geometry.Measures(look, reference, out var within);
                    entry["geometry"][size] = geometry.Describe();
                    pass &= within;
                    largest[engine] = geometry;
                    yield return BenchmarkScreenshot.Capture($"look-probe-{Id(engine)}-{units:0}");
                }
            }

            foreach (var engine in stage.Engines)
            {
                var entry = (JObject)engines[Id(engine)];
                entry["parameters"] = Parameters(stage, engine, look);
                entry["coefficients"] = Coefficients(stage, engine, look, coded, largest[engine],
                    largest[LookCalibrationEngine.UniText]);
            }

            yield return Strip(stage);
            yield return CloseUp(stage);

            var reach = look.ShadowReachEm;
            result = new JObject
            {
                ["target"] = new JObject
                {
                    ["outlineEm"] = look.OutlineEm,
                    ["shadowOffsetEm"] = Pair(look.ShadowOffsetEm),
                    ["shadowSpreadEm"] = look.ShadowSpreadEm,
                    ["shadowBlurEm"] = look.ShadowBlurEm,
                    ["shadowReachEm"] = Pair(reach),
                    ["shadowAlpha"] = look.ShadowColor.a,
                    ["tolerancePx"] = TolerancePx,
                    ["inkTolerance"] = InkTolerance
                },
                ["screen"] = new JArray(stage.Width, stage.Height),
                ["canvasScale"] = stage.CanvasScale,
                ["panelScale"] = stage.PanelScale,
                ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["engines"] = engines,
                ["stills"] = new JArray("look-strip", "look-closeup"),
                ["pass"] = pass
            };
        }
        finally
        {
            stage.Dispose();
        }
    }

    static IEnumerator Strip(LookCalibrationStage stage)
    {
        stage.Clear();
        stage.Background(LookCalibrationProfile.Background);
        var scale = stage.CanvasScale;
        var margin = 48f * scale;
        var lineWidth = stage.Width - 2f * margin;
        var row = RowHeight(stage, 160f, 120f);
        for (var i = 0; i < stage.Engines.Count; i++)
        {
            var engine = stage.Engines[i];
            var y = (160f + i * row) * scale;
            stage.Add(LookCalibrationEngine.UniText, Name(engine), 32f, new Rect(margin, y, lineWidth, 48f * scale),
                Color.white, null);
            stage.Add(engine, "Health 1234", 72f, new Rect(margin, y + 70f * scale, lineWidth, 130f * scale),
                Color.white, LookCalibrationProfile.House);
            stage.Add(engine, "Health quest 1234 pts", 28f, new Rect(margin, y + 230f * scale, lineWidth, 60f * scale),
                Color.white, LookCalibrationProfile.House);
            stage.Add(engine, "العربية", 44f, new Rect(margin, y + 330f * scale, lineWidth, 140f * scale), Color.white,
                LookCalibrationProfile.House, rightToLeft: true);
        }
        yield return LookCalibrationStage.Settle();
        yield return BenchmarkScreenshot.Capture("look-strip");
    }

    static IEnumerator CloseUp(LookCalibrationStage stage)
    {
        stage.Clear();
        stage.Background(LookCalibrationProfile.Background);
        var scale = stage.CanvasScale;
        var margin = 48f * scale;
        var lineWidth = stage.Width - 2f * margin;
        var look = LookCalibrationProfile.CloseUp;
        stage.Add(LookCalibrationEngine.UniText, $"Demonstration: outline {look.OutlineEm:0.##} em", 32f,
            new Rect(margin, 100f * scale, lineWidth, 48f * scale), Color.white, null);
        var row = RowHeight(stage, 200f, 80f);
        for (var i = 0; i < stage.Engines.Count; i++)
        {
            var engine = stage.Engines[i];
            var y = (200f + i * row) * scale;
            stage.Add(LookCalibrationEngine.UniText, Name(engine), 32f, new Rect(margin, y, lineWidth, 48f * scale),
                Color.white, null);
            stage.Add(engine, "العربية", 120f, new Rect(margin, y + 90f * scale, lineWidth, 220f * scale), Color.white,
                look, rightToLeft: true);
            stage.Add(engine, "AVATAR Wolf", 96f, new Rect(margin, y + 340f * scale, lineWidth, 170f * scale),
                Color.white, look, -0.08f);
        }
        yield return LookCalibrationStage.Settle();
        yield return BenchmarkScreenshot.Capture("look-closeup");
    }

    static float RowHeight(LookCalibrationStage stage, float topUnits, float bottomUnits) =>
        (stage.Height / stage.CanvasScale - topUnits - bottomUnits) / stage.Engines.Count;

    static JObject Parameters(LookCalibrationStage stage, LookCalibrationEngine engine, in LookCalibrationLook look)
    {
        switch (engine)
        {
            case LookCalibrationEngine.UniText:
                return new JObject
                {
                    ["strokeWidthEm"] = LookCalibrationProfile.UniTextStrokeWidthEm(look),
                    ["shadowOffsetEm"] = Pair(look.ShadowOffsetEm),
                    ["shadowSpreadEm"] = LookCalibrationProfile.UniTextShadowSpreadEm(look),
                    ["shadowBlurEm"] = LookCalibrationProfile.UniTextShadowBlurEm(look)
                };
            case LookCalibrationEngine.Tmp:
            {
                var material = stage.TmpMaterial(look);
                return new JObject
                {
                    ["faceDilate"] = material.GetFloat(ShaderUtilities.ID_FaceDilate),
                    ["outlineWidth"] = material.GetFloat(ShaderUtilities.ID_OutlineWidth),
                    ["underlayOffset"] = new JArray(material.GetFloat(ShaderUtilities.ID_UnderlayOffsetX),
                        material.GetFloat(ShaderUtilities.ID_UnderlayOffsetY)),
                    ["underlayDilate"] = material.GetFloat(ShaderUtilities.ID_UnderlayDilate),
                    ["underlaySoftness"] = material.GetFloat(ShaderUtilities.ID_UnderlaySoftness),
                    ["scaleRatioA"] = material.GetFloat(ShaderUtilities.ID_ScaleRatio_A),
                    ["scaleRatioC"] = material.GetFloat(ShaderUtilities.ID_ScaleRatio_C),
                    ["gradientScale"] = material.GetFloat(ShaderUtilities.ID_GradientScale),
                    ["samplingPointSize"] = stage.TmpFont.faceInfo.pointSize
                };
            }
            default:
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
                return new JObject
                {
                    ["boldWeight"] = stage.UIToolkitFont(look).boldStyleWeight,
                    ["outlineWidthEm"] = LookCalibrationProfile.UIToolkitOutlineWidthEm(look),
                    ["shadowOffsetEm"] = Pair(LookCalibrationProfile.UIToolkitShadowOffsetEm(look)),
                    ["shadowBlurEm"] = LookCalibrationProfile.UIToolkitShadowBlurEm(look),
                    ["samplingPointSize"] = LookCalibrationProfile.UIToolkitSamplingPointSize,
                    ["atlasPadding"] = LookCalibrationProfile.UIToolkitAtlasPadding
                };
#else
                return null;
#endif
        }
    }

    /// <summary>
    /// The coefficients the largest probe implies under the profile's linear model, beside the profile's own: the face
    /// edge moves by the dilation alone, the outline reaches the dilation plus half the outline band, the shadow reaches
    /// the dilation plus its own growth plus its offset. The shadow terms use the downward reach only: the house shadow
    /// ends inside the outline on the right, where the measured edge is the outline's. A rival's blur coefficient is
    /// the one that brings its shadow tail to UniText's, whose shadow defines the look, taking the part of the tail its
    /// own antialiasing adds (its outline edge's tail) as fixed.
    /// </summary>
    static JObject Coefficients(LookCalibrationStage stage, LookCalibrationEngine engine, in LookCalibrationLook look,
        in LookCalibrationLook coded, Geometry measured, Geometry uniText)
    {
        var blurRatio = (measured.ShadowTailEm - measured.EdgeTailEm) / (uniText.ShadowTailEm - measured.EdgeTailEm);
        var dilation = (measured.OutlineEm + measured.FaceGrowthEm) * 0.5f;
        var halfBand = (measured.OutlineEm - measured.FaceGrowthEm) * 0.5f;
        switch (engine)
        {
            case LookCalibrationEngine.UniText:
            {
                var spread = measured.UnionDownEm + look.ShadowOffsetEm.y;
                return new JObject
                {
                    ["strokePerWidth"] = Coefficient(measured.OutlineEm / LookCalibrationProfile.UniTextStrokeWidthEm(look),
                        LookCalibrationProfile.UniTextStrokePerWidth),
                    ["shadowPerSpread"] = Coefficient(spread / LookCalibrationProfile.UniTextShadowSpreadEm(look),
                        LookCalibrationProfile.UniTextShadowPerSpread)
                };
            }
            case LookCalibrationEngine.Tmp:
            {
                var material = stage.TmpMaterial(coded);
                var emPerUnit = material.GetFloat(ShaderUtilities.ID_GradientScale) / stage.TmpFont.faceInfo.pointSize;
                var ratioA = material.GetFloat(ShaderUtilities.ID_ScaleRatio_A) * emPerUnit;
                var ratioC = material.GetFloat(ShaderUtilities.ID_ScaleRatio_C) * emPerUnit;
                var dilatePerUnit = dilation / (material.GetFloat(ShaderUtilities.ID_FaceDilate) * ratioA);
                var underlayGrowth = dilatePerUnit * material.GetFloat(ShaderUtilities.ID_UnderlayDilate) * ratioC;
                var offsetY = Mathf.Abs(material.GetFloat(ShaderUtilities.ID_UnderlayOffsetY)) * ratioC;
                var offsetPerUnit = (measured.UnionDownEm - dilation - underlayGrowth) / offsetY;
                return new JObject
                {
                    ["dilatePerUnit"] = Coefficient(dilatePerUnit, LookCalibrationProfile.TmpDilatePerUnit),
                    ["outlinePerWidth"] = Coefficient(
                        halfBand / (material.GetFloat(ShaderUtilities.ID_OutlineWidth) * ratioA),
                        LookCalibrationProfile.TmpOutlinePerWidth),
                    ["underlayPerOffset"] = Coefficient(offsetPerUnit, LookCalibrationProfile.TmpUnderlayPerOffset),
                    ["softnessPerUnit"] = Coefficient(LookCalibrationProfile.TmpSoftnessPerUnit * blurRatio,
                        LookCalibrationProfile.TmpSoftnessPerUnit)
                };
            }
            default:
            {
#if !LIGHTSIDE_NO_UITOOLKIT && !(UNITY_WEBGL && !UNITY_EDITOR)
                var bold = stage.UIToolkitFont(look).boldStyleWeight;
                var shadowDown = LookCalibrationProfile.UIToolkitShadowOffsetEm(look).y;
                return new JObject
                {
                    ["facePerBold"] = Coefficient(dilation / bold, LookCalibrationProfile.UIToolkitFacePerBold),
                    ["outlinePerWidth"] = Coefficient(2f * halfBand / LookCalibrationProfile.UIToolkitOutlineWidthEm(look),
                        LookCalibrationProfile.UIToolkitOutlinePerWidth),
                    ["shadowPerOffset"] = Coefficient((measured.UnionDownEm - dilation) / shadowDown,
                        LookCalibrationProfile.UIToolkitShadowPerOffset),
                    ["shadowPerBlur"] = Coefficient(LookCalibrationProfile.UIToolkitShadowPerBlur * blurRatio,
                        LookCalibrationProfile.UIToolkitShadowPerBlur)
                };
#else
                return null;
#endif
            }
        }
    }

    static JObject Coefficient(float measured, float profile) => new() { ["measured"] = measured, ["profile"] = profile };

    static JArray Pair(Vector2 value) => new(value.x, value.y);

    static string Id(LookCalibrationEngine engine) => engine switch
    {
        LookCalibrationEngine.UniText => "unitext",
        LookCalibrationEngine.Tmp => "tmp",
        _ => "uiToolkit"
    };

    static string Name(LookCalibrationEngine engine) => engine switch
    {
        LookCalibrationEngine.UniText => "UniText",
        LookCalibrationEngine.Tmp => "TextMesh Pro",
        _ => "UI Toolkit"
    };

    /// <summary>
    /// A coded probe's drawn geometry in em, relative to the edges of the same engine's unstyled probe. The outline reach
    /// is read on the left and top, which the down-right shadow never reaches; the union reaches are where face, outline
    /// and shadow together end on the right and at the bottom.
    /// </summary>
    readonly struct Geometry
    {
        const float ShadowOverOutlineLimit = 0.2f;

        public readonly float EmPx;
        public readonly float FaceGrowthEm;
        public readonly float OutlineEm;
        public readonly Vector4 OutlineSidesEm;
        public readonly float UnionRightEm;
        public readonly float UnionDownEm;
        public readonly float InkEm2;
        public readonly float ShadowOverOutline;
        public readonly float ShadowTailEm;
        public readonly float EdgeTailEm;

        public Geometry(LookCalibrationEdges plain, LookCalibrationProbe styled, float capRatio)
        {
            EmPx = plain.Height / capRatio;
            var center = styled.Face.Center;
            var left = center.x - plain.Width * 0.5f;
            var right = center.x + plain.Width * 0.5f;
            var bottom = center.y - plain.Height * 0.5f;
            var top = center.y + plain.Height * 0.5f;
            FaceGrowthEm = (styled.Face.Width - plain.Width + styled.Face.Height - plain.Height) * 0.25f / EmPx;
            OutlineSidesEm = new Vector4(left - styled.Outline.Left, styled.Outline.Right - right,
                styled.Outline.Top - top, bottom - styled.Outline.Bottom) / EmPx;
            OutlineEm = (OutlineSidesEm.x + OutlineSidesEm.z) * 0.5f;
            UnionRightEm = (styled.ShadowRight - right) / EmPx;
            UnionDownEm = (bottom - styled.ShadowBottom) / EmPx;
            InkEm2 = styled.ShadowInk / (EmPx * EmPx);
            ShadowOverOutline = styled.ShadowOverOutline;
            ShadowTailEm = styled.ShadowTail / EmPx;
            EdgeTailEm = styled.EdgeTail / EmPx;
        }

        /// <summary>
        /// Every checked measure as <c>{value, target, residual, tolerance, pass}</c>, residuals and tolerances in screen
        /// pixels for distances, as a fraction of UniText's for ink, and as coverage for the shadow over the outline; the
        /// shadow tail and the ink are held to <paramref name="uniText"/>, whose shadow defines the look.
        /// </summary>
        public JObject Measures(in LookCalibrationLook look, in Geometry uniText, out bool within)
        {
            var reach = look.ShadowReachEm;
            var measures = new JObject
            {
                ["face"] = Distance(FaceGrowthEm, 0f),
                ["outline"] = Distance(OutlineEm, look.OutlineEm),
                ["unionRight"] = Distance(UnionRightEm, Mathf.Max(look.OutlineEm, reach.x)),
                ["unionDown"] = Distance(UnionDownEm, Mathf.Max(look.OutlineEm, reach.y)),
                ["shadowTail"] = Distance(ShadowTailEm, uniText.ShadowTailEm),
                ["ink"] = Measure(InkEm2 / uniText.InkEm2, 1f, InkEm2 / uniText.InkEm2 - 1f, InkTolerance),
                ["shadowOverOutline"] = Measure(ShadowOverOutline, 0f, ShadowOverOutline, ShadowOverOutlineLimit)
            };
            within = true;
            foreach (var measure in measures)
                within &= (bool)measure.Value["pass"];
            return measures;
        }

        public JObject Describe() => new()
        {
            ["emPx"] = EmPx,
            ["faceGrowthEm"] = FaceGrowthEm,
            ["outlineEm"] = OutlineEm,
            ["outlineSidesEm"] = new JArray(OutlineSidesEm.x, OutlineSidesEm.y, OutlineSidesEm.z, OutlineSidesEm.w),
            ["unionReachEm"] = new JArray(UnionRightEm, UnionDownEm),
            ["shadowInkEm2"] = InkEm2,
            ["shadowOverOutline"] = ShadowOverOutline,
            ["shadowTailEm"] = ShadowTailEm,
            ["edgeTailEm"] = EdgeTailEm
        };

        JObject Distance(float valueEm, float targetEm) =>
            Measure(valueEm, targetEm, (valueEm - targetEm) * EmPx, TolerancePx);

        static JObject Measure(float value, float target, float residual, float tolerance) => new()
        {
            ["value"] = value,
            ["target"] = target,
            ["residual"] = residual,
            ["tolerance"] = tolerance,
            ["pass"] = Mathf.Abs(residual) <= tolerance
        };
    }
}
