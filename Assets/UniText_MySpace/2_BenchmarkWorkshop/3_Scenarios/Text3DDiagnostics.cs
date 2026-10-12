#if UNITY_EDITOR && UNITEXT_3D
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEditor;

namespace LightSide
{
    /// <summary>Editor checks of the UniText 3D geometry kernel over whole fonts.</summary>
    internal static unsafe class Text3DDiagnostics
    {
        private readonly struct FontScan
        {
            public readonly UniTextFont.Core font;
            public readonly int[] coords;
            public readonly int glyphCount;

            public FontScan(string fontAssetPath)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniTextFont>(fontAssetPath);
                if (asset == null)
                    throw new ArgumentException($"No UniText font asset at '{fontAssetPath}'.", nameof(fontAssetPath));
                font = asset.Runtime;
                if (!font.HasFontData)
                    throw new ArgumentException($"'{fontAssetPath}' holds no font file data.", nameof(fontAssetPath));
                coords = font.DefaultFtCoords;
                var cache = font.CurveCache;
                var face = cache.RentFace(coords);
                try
                {
                    glyphCount = FT.GetFaceInfo(face).numGlyphs;
                }
                finally
                {
                    cache.ReturnFace(face);
                }
            }
        }

        /// <summary>
        /// Flattens every glyph of a font asset with a font file and reports point, contour and corner counts with the
        /// time spent.
        /// </summary>
        internal static string FlattenReport(string fontAssetPath, float toleranceEm)
        {
            var scan = new FontScan(fontAssetPath);
            var outline = new FlatOutline();
            long points = 0, contours = 0, corners = 0;
            int empty = 0, maxPoints = 0;
            uint maxGlyph = 0;
            var watch = Stopwatch.StartNew();
            try
            {
                for (uint glyph = 0; glyph < (uint)scan.glyphCount; glyph++)
                {
                    if (!GlyphOutlineReader.TryFlatten(scan.font, scan.coords, glyph, toleranceEm, &outline))
                    {
                        empty++;
                        continue;
                    }

                    var count = outline.points.count;
                    points += count;
                    contours += outline.contourStarts.count;
                    for (var i = 0; i < count; i++)
                        if ((outline.points[i].flags & OutlinePointFlags.Corner) != 0)
                            corners++;
                    if (count <= maxPoints) continue;
                    maxPoints = count;
                    maxGlyph = glyph;
                }
            }
            finally
            {
                watch.Stop();
                outline.Dispose();
            }

            var drawn = scan.glyphCount - empty;
            var perGlyph = drawn > 0 ? points / (double)drawn : 0;
            var microseconds = drawn > 0 ? watch.Elapsed.TotalMilliseconds * 1000.0 / drawn : 0;
            return $"{fontAssetPath}: {scan.glyphCount} glyphs, {drawn} with outlines, {empty} empty; {points} points " +
                   $"({perGlyph:F1} per glyph, max {maxPoints} in glyph {maxGlyph}), {contours} contours, " +
                   $"{corners} corners; {watch.Elapsed.TotalMilliseconds:F1} ms ({microseconds:F1} us per glyph) " +
                   $"at tolerance {toleranceEm} em";
        }

        /// <summary>
        /// Unites every glyph of a font asset and checks the result: output edges must meet only at shared vertices,
        /// and random points away from the edges must have the same nonzero inside state before and after the union.
        /// </summary>
        internal static string UnionReport(string fontAssetPath, float toleranceEm, int samplesPerGlyph)
        {
            var scan = new FontScan(fontAssetPath);
            var flat = new FlatOutline();
            var union = new UnionOutline();
            var scratch = new PolygonUnionScratch();
            var random = new Unity.Mathematics.Random(0x5EED1234u);
            long crossings = 0, mismatches = 0, inputPoints = 0, outputPoints = 0;
            int glyphs = 0, changed = 0, failing = 0, worstPoints = 0;
            uint worstGlyph = 0;
            double worstMs = 0;
            string firstIssue = null;
            var watch = new Stopwatch();
            var single = new Stopwatch();
            try
            {
                for (uint glyph = 0; glyph < (uint)scan.glyphCount; glyph++)
                {
                    if (!GlyphOutlineReader.TryFlatten(scan.font, scan.coords, glyph, toleranceEm, &flat)) continue;
                    glyphs++;
                    single.Restart();
                    watch.Start();
                    PolygonUnion.Resolve(&flat, &scratch, &union, UnionFill.NonZero);
                    watch.Stop();
                    single.Stop();
                    if (single.Elapsed.TotalMilliseconds > worstMs)
                    {
                        worstMs = single.Elapsed.TotalMilliseconds;
                        worstGlyph = glyph;
                        worstPoints = flat.points.count;
                    }

                    inputPoints += flat.points.count;
                    outputPoints += union.points.count;
                    if (union.points.count != flat.points.count || union.loopStarts.count != flat.contourStarts.count)
                        changed++;

                    var glyphCrossings = CountCrossings(&union);
                    var glyphMismatches = CountMismatches(&flat, &union, samplesPerGlyph, ref random);
                    crossings += glyphCrossings;
                    mismatches += glyphMismatches;
                    if (glyphCrossings + glyphMismatches == 0) continue;
                    failing++;
                    firstIssue ??= $"glyph {glyph}: {glyphCrossings} crossings, {glyphMismatches} mismatches";
                }
            }
            finally
            {
                flat.Dispose();
                union.Dispose();
                scratch.Dispose();
            }

            var microseconds = glyphs > 0 ? watch.Elapsed.TotalMilliseconds * 1000.0 / glyphs : 0;
            return $"{fontAssetPath}: {glyphs} glyphs united, {changed} changed, {failing} failing " +
                   $"({crossings} crossings, {mismatches} winding mismatches){(firstIssue != null ? ", first " + firstIssue : "")}; " +
                   $"points {inputPoints} -> {outputPoints}; {watch.Elapsed.TotalMilliseconds:F1} ms " +
                   $"({microseconds:F1} us per glyph), slowest glyph {worstGlyph} ({worstPoints} points) {worstMs:F2} ms";
        }

        /// <summary>Round edge profile of a quarter circle from the wall to the cap.</summary>
        internal static ProfileSample[] RoundProfile(float width, float height, int segments)
        {
            if (width <= 0f || segments < 1)
                return new[] { new ProfileSample { inset = 0, height = 0, tangent = new float2(0, -1) } };
            var samples = new ProfileSample[segments + 1];
            var length = 0f;
            var previous = float2.zero;
            for (var k = 0; k <= segments; k++)
            {
                var angle = k / (float)segments * math.PI * 0.5f;
                var inset = width * (1f - math.cos(angle));
                var h = height * (1f - math.sin(angle));
                var point = new float2(inset, h);
                if (k > 0) length += math.distance(point, previous);
                previous = point;
                samples[k] = new ProfileSample
                {
                    inset = inset,
                    height = h,
                    tangent = math.normalizesafe(new float2(width * math.sin(angle), -height * math.cos(angle)),
                        new float2(1, 0)),
                    length = length
                };
            }
            return samples;
        }

        /// <summary>
        /// Builds the solid of every glyph of a font asset with a round bevel on both sides, split to edges no longer
        /// than <paramref name="refineEm"/> when it is positive or sliced along a grid of its magnitude when it is
        /// negative, and checks it: every edge of the position-welded
        /// surface must be shared by exactly two triangles running it in opposite directions, triangle winding must
        /// agree with the vertex normals, and the enclosed volume must be positive.
        /// </summary>
        internal static string SolidReport(string fontAssetPath, float toleranceEm, float bevelWidthEm,
            float bevelHeightEm, int segments, float depthEm, float refineEm = 0f)
        {
            var scan = new FontScan(fontAssetPath);
            var profile = RoundProfile(bevelWidthEm, bevelHeightEm, segments);
            var settings = new SolidSettings
            {
                unitsPerEm = scan.font.UnitsPerEm,
                smoothCos = math.cos(math.radians(30f)),
                frontCap = 1,
                backCap = 1,
                sides = 1
            };
            var flat = new FlatOutline();
            var union = new UnionOutline();
            var unionScratch = new PolygonUnionScratch();
            var solidScratch = new SolidScratch();
            var refineScratch = new RefineScratch();
            var mesh = new SolidMesh();
            int glyphs = 0, budget = 0, open = 0, touching = 0, flipped = 0, negative = 0, maxVertices = 0;
            long vertices = 0, triangles = 0, degenerate = 0, folds = 0;
            uint worstGlyph = 0, maxGlyph = 0, foldGlyph = 0;
            double worstMs = 0, maxFold = 0;
            string firstIssue = null;
            var watch = new Stopwatch();
            var single = new Stopwatch();
            try
            {
                fixed (ProfileSample* samples = profile)
                {
                    for (uint glyph = 0; glyph < (uint)scan.glyphCount; glyph++)
                    {
                        if (!GlyphOutlineReader.TryFlatten(scan.font, scan.coords, glyph, toleranceEm, &flat)) continue;
                        glyphs++;
                        single.Restart();
                        watch.Start();
                        PolygonUnion.Resolve(&flat, &unionScratch, &union, UnionFill.NonZero);
                        var completed = GlyphSolidBuilder.Build(&union, samples, profile.Length, samples,
                            profile.Length, &settings, &solidScratch, &mesh);
                        if (refineEm > 0f) MeshRefine.SplitSolid(&mesh, refineEm, &refineScratch);
                        else if (refineEm < 0f)
                        {
                            var cell = new float2(-refineEm);
                            MeshRefine.SliceSolid(&mesh, &cell, &refineScratch);
                        }
                        watch.Stop();
                        single.Stop();
                        if (single.Elapsed.TotalMilliseconds > worstMs)
                        {
                            worstMs = single.Elapsed.TotalMilliseconds;
                            worstGlyph = glyph;
                        }

                        if (!completed) budget++;
                        vertices += mesh.vertices.count;
                        var glyphTriangles = (mesh.front.count + mesh.frontEdge.count + mesh.side.count +
                                              mesh.backEdge.count + mesh.back.count) / 3;
                        triangles += glyphTriangles;
                        if (mesh.vertices.count > maxVertices)
                        {
                            maxVertices = mesh.vertices.count;
                            maxGlyph = glyph;
                        }

                        Inspect(&mesh, depthEm, out var badEdges, out var touchingEdges, out var wrongWinding,
                            out var zeroArea, out var volume, out var foldedArea);
                        if (touchingEdges > 0) touching++;
                        degenerate += zeroArea;
                        folds += wrongWinding;
                        if (foldedArea > maxFold)
                        {
                            maxFold = foldedArea;
                            foldGlyph = glyph;
                        }
                        if (badEdges > 0) open++;
                        if (wrongWinding > 0) flipped++;
                        if (volume <= 0) negative++;
                        if (badEdges == 0 && volume > 0 && completed) continue;
                        firstIssue ??= $"glyph {glyph}: {badEdges} open edges, {wrongWinding} flipped, " +
                                       $"volume {volume:G3}, completed {completed}";
                    }
                }
            }
            finally
            {
                flat.Dispose();
                union.Dispose();
                unionScratch.Dispose();
                solidScratch.Dispose();
                refineScratch.Dispose();
                mesh.Dispose();
            }

            var microseconds = glyphs > 0 ? watch.Elapsed.TotalMilliseconds * 1000.0 / glyphs : 0;
            var perGlyphVertices = glyphs > 0 ? vertices / glyphs : 0;
            var perGlyphTriangles = glyphs > 0 ? triangles / glyphs : 0;
            return $"{fontAssetPath}: {glyphs} solids, {open} open, {touching} touching, {negative} non-positive volume, {budget} out of " +
                   $"budget; {flipped} with {folds} folded triangles (largest {maxFold:G3} em2 in glyph {foldGlyph}), " +
                   $"{degenerate} degenerate triangles" +
                   $"{(firstIssue != null ? ", first " + firstIssue : "")}; {perGlyphVertices} vertices and " +
                   $"{perGlyphTriangles} triangles per glyph (max {maxVertices} in glyph {maxGlyph}); " +
                   $"{watch.Elapsed.TotalMilliseconds:F1} ms ({microseconds:F1} us per glyph), " +
                   $"slowest glyph {worstGlyph} {worstMs:F2} ms";
        }

        /// <summary>
        /// Builds one glyph solid with a round bevel and writes its union outline, front bevel triangles, cap loops,
        /// open edges and event counts as JSON for inspection.
        /// </summary>
        internal static string DumpGlyph(string fontAssetPath, int glyph, float toleranceEm, float bevelWidthEm,
            float bevelHeightEm, int segments, string outputPath)
        {
            var scan = new FontScan(fontAssetPath);
            var profile = RoundProfile(bevelWidthEm, bevelHeightEm, segments);
            var settings = new SolidSettings
            {
                unitsPerEm = scan.font.UnitsPerEm,
                smoothCos = math.cos(math.radians(30f)),
                frontCap = 1,
                backCap = 1,
                sides = 1
            };
            var flat = new FlatOutline();
            var union = new UnionOutline();
            var unionScratch = new PolygonUnionScratch();
            var solidScratch = new SolidScratch();
            var mesh = new SolidMesh();
            var json = new System.Text.StringBuilder();
            try
            {
                if (!GlyphOutlineReader.TryFlatten(scan.font, scan.coords, (uint)glyph, toleranceEm, &flat))
                    return "empty glyph";
                PolygonUnion.Resolve(&flat, &unionScratch, &union, UnionFill.NonZero);
                bool completed;
                fixed (ProfileSample* samples = profile)
                {
                    var side = new WaveSide
                    {
                        samples = samples,
                        sampleCount = profile.Length,
                        zSign = 1f,
                        anchor = 0,
                        surface = (byte)Text3DSurface.FrontEdge
                    };
                    mesh.Clear();
                    solidScratch.triangulation.Clear();
                    CapTriangulator.Triangulate(union.grid.data, union.loopStarts.data, union.loopStarts.count,
                        union.points.count, &solidScratch.cap, &solidScratch.triangulation);
                    completed = WaveInset.Run(&union, solidScratch.triangulation.data,
                        solidScratch.triangulation.count, side, scan.font.UnitsPerEm, settings.smoothCos,
                        &solidScratch.wave, &mesh);
                }

                var units = (double)scan.font.UnitsPerEm;
                json.Append("{\"completed\":").Append(completed ? "true" : "false");
                json.Append(",\"edgeEvents\":").Append(solidScratch.wave.edgeEvents);
                json.Append(",\"splitEvents\":").Append(solidScratch.wave.splitEvents);
                json.Append(",\"meetEvents\":").Append(solidScratch.wave.meetEvents);
                json.Append(",\"flipEvents\":").Append(solidScratch.wave.flipEvents);
                json.Append(",\"staleEvents\":").Append(solidScratch.wave.staleEvents);
                json.Append(",\"loops\":[");
                for (var loop = 0; loop < union.loopStarts.count; loop++)
                {
                    if (loop > 0) json.Append(',');
                    json.Append('[');
                    for (var i = union.loopStarts[loop]; i < union.LoopEnd(loop); i++)
                    {
                        if (i > union.loopStarts[loop]) json.Append(',');
                        var p = new double2(union.grid[i]) / PolygonUnion.GridScale / units;
                        json.Append('[').Append(p.x.ToString("R")).Append(',').Append(p.y.ToString("R")).Append(']');
                    }
                    json.Append(']');
                }
                json.Append("],\"triangles\":[");
                ref var list = ref mesh.frontEdge;
                for (var t = 0; t + 2 < list.count; t += 3)
                {
                    if (t > 0) json.Append(',');
                    json.Append('[');
                    for (var k = 0; k < 3; k++)
                    {
                        if (k > 0) json.Append(',');
                        var p = mesh.vertices[list[t + k]].position;
                        json.Append('[').Append(p.x.ToString("R")).Append(',').Append(p.y.ToString("R")).Append(',')
                            .Append(p.z.ToString("R")).Append(']');
                    }
                    json.Append(']');
                }
                json.Append("],\"cap\":[");
                ref var cap = ref solidScratch.wave.capTriangles;
                for (var t = 0; t + 2 < cap.count; t += 3)
                {
                    if (t > 0) json.Append(',');
                    json.Append('[');
                    for (var k = 0; k < 3; k++)
                    {
                        if (k > 0) json.Append(',');
                        var p = solidScratch.wave.capPositions[cap[t + k]];
                        json.Append('[').Append(p.x.ToString("R")).Append(',').Append(p.y.ToString("R")).Append(']');
                    }
                    json.Append(']');
                }
                json.Append("]}");
                System.IO.File.WriteAllText(outputPath, json.ToString());

                var full = new SolidMesh();
                try
                {
                    fixed (ProfileSample* samples = profile)
                        GlyphSolidBuilder.Build(&union, samples, profile.Length, samples, profile.Length, &settings,
                            &solidScratch, &full);
                    Inspect(&full, 0.3f, out var badEdges, out var touchingEdges, out var wrongWinding,
                        out var zeroArea, out var volume, out var foldedArea);
                    var open = OpenEdges(&full, 0.3f);
                    var edgesJson = new System.Text.StringBuilder("[");
                    for (var k = 0; k < open.Count; k++)
                    {
                        if (k > 0) edgesJson.Append(',');
                        var (a, b) = open[k];
                        edgesJson.Append('[').Append(a.x.ToString("R")).Append(',').Append(a.y.ToString("R"))
                            .Append(',').Append(a.z.ToString("R")).Append(',').Append(b.x.ToString("R")).Append(',')
                            .Append(b.y.ToString("R")).Append(',').Append(b.z.ToString("R")).Append(']');
                    }
                    edgesJson.Append(']');
                    System.IO.File.WriteAllText(outputPath.Replace(".json", ".open.json"), edgesJson.ToString());
                    return $"glyph {glyph}: completed {completed}, events edge {solidScratch.wave.edgeEvents} " +
                           $"split {solidScratch.wave.splitEvents} meet {solidScratch.wave.meetEvents} " +
                           $"flip {solidScratch.wave.flipEvents} " +
                           $"stale {solidScratch.wave.staleEvents}; " +
                           $"{badEdges} open edges, {touchingEdges} touching, {wrongWinding} folded (largest {foldedArea:G3} em2), " +
                           $"{zeroArea} degenerate, volume {volume:G4}; " +
                           $"{union.loopStarts.count} loops, {union.points.count} points";
                }
                finally
                {
                    full.Dispose();
                }
            }
            finally
            {
                flat.Dispose();
                union.Dispose();
                unionScratch.Dispose();
                solidScratch.Dispose();
                mesh.Dispose();
            }
        }

        private static List<(double3, double3)> OpenEdges(SolidMesh* mesh, float depth)
        {
            var weld = new Dictionary<(int, int, int), int>();
            var ids = new int[mesh->vertices.count];
            var welded = new List<double3>();
            for (var i = 0; i < mesh->vertices.count; i++)
            {
                var v = mesh->vertices[i];
                var p = new double3(v.position.x, v.position.y, v.position.z + v.anchor * depth);
                var key = Key(v, depth);
                if (!weld.TryGetValue(key, out var id))
                {
                    id = weld.Count;
                    weld.Add(key, id);
                    welded.Add(p);
                }
                ids[i] = id;
            }

            var edges = new Dictionary<(int, int), int>();
            for (var surface = 0; surface < SolidMesh.SurfaceCount; surface++)
            {
                ref var list = ref SolidMesh.Triangles(mesh, (Text3DSurface)surface);
                for (var t = 0; t + 2 < list.count; t += 3)
                {
                    int wa = ids[list[t]], wb = ids[list[t + 1]], wc = ids[list[t + 2]];
                    if (wa == wb || wb == wc || wa == wc) continue;
                    Count(edges, wa, wb);
                    Count(edges, wb, wc);
                    Count(edges, wc, wa);
                }
            }

            var open = new List<(double3, double3)>();
            foreach (var pair in edges)
            {
                edges.TryGetValue((pair.Key.Item2, pair.Key.Item1), out var reverse);
                if (pair.Value != reverse) open.Add((welded[pair.Key.Item1], welded[pair.Key.Item2]));
            }
            return open;
        }

        private static void Inspect(SolidMesh* mesh, float depth, out int badEdges, out int touchingEdges,
            out int wrongWinding, out int zeroArea, out double volume, out double foldedArea)
        {
            var weld = new Dictionary<(int, int, int), int>();
            var ids = new int[mesh->vertices.count];
            var positions = new double3[mesh->vertices.count];
            for (var i = 0; i < mesh->vertices.count; i++)
            {
                var v = mesh->vertices[i];
                var p = new double3(v.position.x, v.position.y, v.position.z + v.anchor * depth);
                positions[i] = p;
                var key = Key(v, depth);
                if (!weld.TryGetValue(key, out var id))
                {
                    id = weld.Count;
                    weld.Add(key, id);
                }
                ids[i] = id;
            }

            var edges = new Dictionary<(int, int), int>();
            wrongWinding = 0;
            zeroArea = 0;
            volume = 0;
            foldedArea = 0;
            for (var surface = 0; surface < SolidMesh.SurfaceCount; surface++)
            {
                ref var list = ref SolidMesh.Triangles(mesh, (Text3DSurface)surface);
                for (var t = 0; t + 2 < list.count; t += 3)
                {
                    int a = list[t], b = list[t + 1], c = list[t + 2];
                    int wa = ids[a], wb = ids[b], wc = ids[c];
                    if (wa == wb || wb == wc || wa == wc)
                    {
                        zeroArea++;
                        continue;
                    }

                    var cross = math.cross(positions[b] - positions[a], positions[c] - positions[a]);
                    if (math.lengthsq(cross) <= 0) zeroArea++;
                    var expected = mesh->vertices[a].normal + mesh->vertices[b].normal + mesh->vertices[c].normal;
                    if (math.dot(cross, (double3)expected) < 0)
                    {
                        wrongWinding++;
                        foldedArea = math.max(foldedArea, math.length(cross) * 0.5);
                    }
                    volume += math.dot(positions[a], math.cross(positions[b], positions[c])) / 6.0;
                    Count(edges, wa, wb);
                    Count(edges, wb, wc);
                    Count(edges, wc, wa);
                }
            }

            badEdges = 0;
            touchingEdges = 0;
            foreach (var pair in edges)
            {
                edges.TryGetValue((pair.Key.Item2, pair.Key.Item1), out var reverse);
                if (pair.Value != reverse) badEdges++;
                else if (pair.Value > 1) touchingEdges++;
            }
        }

        private static (int, int, int) Key(in SolidVertex v, float depth)
            => (math.asint(v.position.x + 0f), math.asint(v.position.y + 0f), math.asint(v.position.z + v.anchor * depth + 0f));

        private static void Count(Dictionary<(int, int), int> edges, int a, int b)
        {
            edges.TryGetValue((a, b), out var count);
            edges[(a, b)] = count + 1;
        }

        private static int CountCrossings(UnionOutline* union)
        {
            var issues = 0;
            for (var loopA = 0; loopA < union->loopStarts.count; loopA++)
            {
                var startA = union->loopStarts[loopA];
                var endA = union->LoopEnd(loopA);
                for (var i = startA; i < endA; i++)
                {
                    var a0 = union->grid[i];
                    var a1 = union->grid[i + 1 < endA ? i + 1 : startA];
                    for (var loopB = loopA; loopB < union->loopStarts.count; loopB++)
                    {
                        var startB = union->loopStarts[loopB];
                        var endB = union->LoopEnd(loopB);
                        for (var j = loopB == loopA ? i + 1 : startB; j < endB; j++)
                        {
                            var b0 = union->grid[j];
                            var b1 = union->grid[j + 1 < endB ? j + 1 : startB];
                            if (Conflict(a0, a1, b0, b1)) issues++;
                        }
                    }
                }
            }
            return issues;
        }

        private static bool Conflict(int2 a0, int2 a1, int2 b0, int2 b1)
        {
            if (math.max(a0.x, a1.x) < math.min(b0.x, b1.x) || math.max(b0.x, b1.x) < math.min(a0.x, a1.x) ||
                math.max(a0.y, a1.y) < math.min(b0.y, b1.y) || math.max(b0.y, b1.y) < math.min(a0.y, a1.y))
                return false;
            var o1 = Orient(a0, a1, b0);
            var o2 = Orient(a0, a1, b1);
            var o3 = Orient(b0, b1, a0);
            var o4 = Orient(b0, b1, a1);
            if (o1 != 0 && o2 != 0 && o3 != 0 && o4 != 0)
                return (o1 > 0) != (o2 > 0) && (o3 > 0) != (o4 > 0);
            if (o1 == 0 && o2 == 0)
            {
                var axis = a1 - a0;
                var t0 = Dot(b0 - a0, axis);
                var t1 = Dot(b1 - a0, axis);
                var length = Dot(axis, axis);
                var lo = math.min(t0, t1);
                var hi = math.max(t0, t1);
                return math.min(hi, length) - math.max(lo, 0) > 0;
            }

            if (o1 == 0 && Interior(a0, a1, b0)) return true;
            if (o2 == 0 && Interior(a0, a1, b1)) return true;
            if (o3 == 0 && Interior(b0, b1, a0)) return true;
            return o4 == 0 && Interior(b0, b1, a1);
        }

        private static bool Interior(int2 a, int2 b, int2 p)
        {
            if (p.x == a.x && p.y == a.y || p.x == b.x && p.y == b.y) return false;
            return math.min(a.x, b.x) <= p.x && p.x <= math.max(a.x, b.x) &&
                   math.min(a.y, b.y) <= p.y && p.y <= math.max(a.y, b.y);
        }

        private static long Orient(int2 a, int2 b, int2 c)
            => (long)(b.x - a.x) * (c.y - a.y) - (long)(b.y - a.y) * (c.x - a.x);

        private static long Dot(int2 a, int2 b) => (long)a.x * b.x + (long)a.y * b.y;

        private static int CountMismatches(FlatOutline* flat, UnionOutline* union, int samples, ref Unity.Mathematics.Random random)
        {
            if (flat->points.count == 0) return 0;
            var min = flat->points[0].position;
            var max = min;
            for (var i = 1; i < flat->points.count; i++)
            {
                min = math.min(min, flat->points[i].position);
                max = math.max(max, flat->points[i].position);
            }

            var margin = 2f / PolygonUnion.GridScale + 0.05f;
            var mismatches = 0;
            for (var k = 0; k < samples; k++)
            {
                var p = random.NextFloat2(min, max);
                var before = WindingOf(flat->points.data, flat->contourStarts.data, flat->contourStarts.count,
                    flat->points.count, p) != 0;
                var after = WindingOf(union->points.data, union->loopStarts.data, union->loopStarts.count,
                    union->points.count, p) != 0;
                if (before == after) continue;
                if (NearEdge(flat->points.data, flat->contourStarts.data, flat->contourStarts.count, flat->points.count,
                        p, margin) ||
                    NearEdge(union->points.data, union->loopStarts.data, union->loopStarts.count, union->points.count,
                        p, margin))
                    continue;
                mismatches++;
            }
            return mismatches;
        }

        private static int WindingOf(OutlinePoint* points, int* starts, int loops, int total, float2 p)
        {
            var winding = 0;
            for (var loop = 0; loop < loops; loop++)
            {
                var start = starts[loop];
                var end = loop + 1 < loops ? starts[loop + 1] : total;
                for (var i = start; i < end; i++)
                {
                    var a = (double2)points[i].position;
                    var b = (double2)points[i + 1 < end ? i + 1 : start].position;
                    var side = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                    if (a.y <= p.y)
                    {
                        if (b.y > p.y && side > 0) winding++;
                    }
                    else if (b.y <= p.y && side < 0)
                        winding--;
                }
            }
            return winding;
        }

        private static bool NearEdge(OutlinePoint* points, int* starts, int loops, int total, float2 p, float margin)
        {
            for (var loop = 0; loop < loops; loop++)
            {
                var start = starts[loop];
                var end = loop + 1 < loops ? starts[loop + 1] : total;
                for (var i = start; i < end; i++)
                {
                    var a = points[i].position;
                    var b = points[i + 1 < end ? i + 1 : start].position;
                    var axis = b - a;
                    var t = math.saturate(math.dot(p - a, axis) / math.max(math.lengthsq(axis), 1e-12f));
                    if (math.distancesq(p, a + t * axis) <= margin * margin) return true;
                }
            }
            return false;
        }
    }
}
#endif
