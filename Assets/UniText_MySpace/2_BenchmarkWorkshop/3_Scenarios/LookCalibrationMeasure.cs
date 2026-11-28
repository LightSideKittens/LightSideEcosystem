using System;
using UnityEngine;

/// <summary>
/// One screen readback of a coded probe: the face drawn pure red, the outline pure green and the shadow blue at a known
/// alpha, over black. With that layering a pixel's red is the face coverage, red plus green the coverage of face and
/// outline, and red plus green plus blue divided by the shadow alpha the coverage of all three, in linear values.
/// </summary>
/// <remarks>Rows run from the bottom of the screen up, as the readback stores them.</remarks>
internal sealed class LookCalibrationFrame
{
    static readonly float[] encodedToLinear = BuildDecode(true);
    static readonly float[] encodedToValue = BuildDecode(false);

    readonly Color32[] pixels;
    readonly float[] decode;

    public LookCalibrationFrame(Color32[] pixels, int width, int height, bool linear)
    {
        this.pixels = pixels;
        Width = width;
        Height = height;
        decode = linear ? encodedToLinear : encodedToValue;
    }

    public int Width { get; }
    public int Height { get; }

    public float Face(int x, int y) => decode[pixels[y * Width + x].r];

    public float FaceAndOutline(int x, int y)
    {
        var p = pixels[y * Width + x];
        return Mathf.Min(1f, decode[p.r] + decode[p.g]);
    }

    public float All(int x, int y, float shadowAlpha)
    {
        var p = pixels[y * Width + x];
        return Mathf.Min(1f, decode[p.r] + decode[p.g] + decode[p.b] / shadowAlpha);
    }

    public float VisibleShadow(int x, int y, float shadowAlpha) => decode[pixels[y * Width + x].b] / shadowAlpha;

    static float[] BuildDecode(bool linear)
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
            table[i] = linear ? Mathf.GammaToLinearSpace(i / 255f) : i / 255f;
        return table;
    }
}

/// <summary>Edges of a coverage region at half coverage, in pixels along the scan lines through the probe's center.</summary>
internal readonly struct LookCalibrationEdges
{
    public readonly float Left;
    public readonly float Right;
    public readonly float Bottom;
    public readonly float Top;

    public LookCalibrationEdges(float left, float right, float bottom, float top)
    {
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
    }

    public float Width => Right - Left;
    public float Height => Top - Bottom;
    public Vector2 Center => new((Left + Right) * 0.5f, (Bottom + Top) * 0.5f);
}

/// <summary>Drawn geometry of one coded probe glyph, in pixels.</summary>
internal readonly struct LookCalibrationProbe
{
    public readonly LookCalibrationEdges Face;
    public readonly LookCalibrationEdges Outline;
    public readonly float ShadowRight;
    public readonly float ShadowBottom;

    /// <summary>Visible shadow coverage summed over the probe's region, in square pixels.</summary>
    public readonly float ShadowInk;

    /// <summary>The most shadow coverage seen inside the outline band right of and below the face; zero when the shadow lies under the outline.</summary>
    public readonly float ShadowOverOutline;

    /// <summary>Distance from the half-coverage to the tenth-coverage crossing of the shadow's lower edge.</summary>
    public readonly float ShadowTail;

    /// <summary>The same distance on the outline's left edge, which only antialiasing softens.</summary>
    public readonly float EdgeTail;

    public LookCalibrationProbe(LookCalibrationEdges face, LookCalibrationEdges outline, float shadowRight,
        float shadowBottom, float shadowInk, float shadowOverOutline, float shadowTail, float edgeTail)
    {
        Face = face;
        Outline = outline;
        ShadowRight = shadowRight;
        ShadowBottom = shadowBottom;
        ShadowInk = shadowInk;
        ShadowOverOutline = shadowOverOutline;
        ShadowTail = shadowTail;
        EdgeTail = edgeTail;
    }
}

/// <summary>
/// Measures a probe glyph with straight vertical and horizontal sides (a Latin capital I) on scan lines through the
/// center of its face; every edge is the half-coverage crossing, interpolated between pixel centers.
/// </summary>
internal static class LookCalibrationMeasure
{
    const float TailCoverage = 0.1f;

    /// <summary>The face edges of an unstyled probe inside <paramref name="region"/>.</summary>
    /// <exception cref="InvalidOperationException">The region holds no face, or the face touches its border.</exception>
    public static LookCalibrationEdges Plain(LookCalibrationFrame frame, RectInt region)
    {
        var center = FaceCenter(frame, region);
        return Edges(frame.Face, center, region);
    }

    /// <summary>The face, outline and shadow geometry of a coded probe inside <paramref name="region"/>.</summary>
    /// <exception cref="InvalidOperationException">The region holds no face, or a layer reaches its border.</exception>
    public static LookCalibrationProbe Styled(LookCalibrationFrame frame, RectInt region, float shadowAlpha)
    {
        var center = FaceCenter(frame, region);
        var face = Edges(frame.Face, center, region);
        var outline = Edges(frame.FaceAndOutline, center, region);
        var right = Walk(x => frame.All(x, center.y, shadowAlpha), center.x, 1, region.xMax);
        var bottom = Walk(y => frame.All(center.x, y, shadowAlpha), center.y, -1, region.yMin - 1);
        var bottomTail = Walk(y => frame.All(center.x, y, shadowAlpha), center.y, -1, region.yMin - 1, TailCoverage);
        var edgeTail = Walk(x => frame.FaceAndOutline(x, center.y), center.x, -1, region.xMin - 1, TailCoverage);
        var ink = 0f;
        for (var y = region.yMin; y < region.yMax; y++)
        for (var x = region.xMin; x < region.xMax; x++)
            ink += frame.VisibleShadow(x, y, shadowAlpha);
        var over = 0f;
        var bandEnd = Mathf.FloorToInt(face.Right + (face.Left - outline.Left)) - 1;
        for (var x = Mathf.CeilToInt(face.Right) + 1; x < bandEnd; x++)
            over = Mathf.Max(over, frame.VisibleShadow(x, center.y, shadowAlpha));
        bandEnd = Mathf.CeilToInt(face.Bottom - (outline.Top - face.Top)) + 1;
        for (var y = Mathf.FloorToInt(face.Bottom) - 1; y > bandEnd; y--)
            over = Mathf.Max(over, frame.VisibleShadow(center.x, y, shadowAlpha));
        return new LookCalibrationProbe(face, outline, right, bottom, ink, over, bottom - bottomTail,
            outline.Left - edgeTail);
    }

    static Vector2Int FaceCenter(LookCalibrationFrame frame, RectInt region)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (var y = region.yMin; y < region.yMax; y++)
        for (var x = region.xMin; x < region.xMax; x++)
        {
            if (frame.Face(x, y) < 0.5f) continue;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        if (minX > maxX)
            throw new InvalidOperationException($"No probe face inside {region}.");
        return new Vector2Int((minX + maxX) / 2, (minY + maxY) / 2);
    }

    static LookCalibrationEdges Edges(Func<int, int, float> coverage, Vector2Int center, RectInt region) => new(
        Walk(x => coverage(x, center.y), center.x, -1, region.xMin - 1),
        Walk(x => coverage(x, center.y), center.x, 1, region.xMax),
        Walk(y => coverage(center.x, y), center.y, -1, region.yMin - 1),
        Walk(y => coverage(center.x, y), center.y, 1, region.yMax));

    static float Walk(Func<int, float> coverage, int start, int step, int end, float threshold = 0.5f)
    {
        var previous = coverage(start);
        if (previous < threshold)
            throw new InvalidOperationException("The probe center is not covered.");
        for (var i = start + step; i != end; i += step)
        {
            var current = coverage(i);
            if (current < threshold)
                return i - step + 0.5f + step * (previous - threshold) / (previous - current);
            previous = current;
        }
        throw new InvalidOperationException("A probe layer reaches the border of its region.");
    }
}
