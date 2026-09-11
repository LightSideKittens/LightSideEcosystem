using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide.CI
{
    internal sealed class ShaderCompileCheck
    {
        /// <summary>Packages that must ship shaders — Core owns the surface family every other package renders through.</summary>
        private static readonly string[] requiredShaderRoots =
        {
            "Packages/media.lightside.core"
        };

        /// <summary>Packages scanned for shaders of their own; each may legitimately ship none.</summary>
        private static readonly string[] optionalShaderRoots =
        {
            "Packages/media.lightside.unitext",
            "Packages/media.lightside.unishapes",
            "Packages/media.lightside.unilottie"
        };

        /// <summary>
        /// Compiles every pass of every LightSide shader across the requested color spaces and compiler
        /// platforms, failing on any compiler warning or error. Each pass compiles the variant set
        /// <see cref="VariantSelection"/> derives for it: every keyword combination while the stage's space is
        /// small, a pairwise covering set beyond that, and one base variant per pass for a Shader Graph. The
        /// timeout is GitHub's hard six-hour job ceiling, so the CI job's own timeout always fires first and
        /// stays the single authority. Removing the attribute does not lift the limit: test-framework 1.7.0,
        /// which Unity 6 resolves to whatever the manifest pins, then applies its own 180-second default and
        /// fails a sweep three minutes in.
        /// </summary>
        [Test, Timeout(21600000)]
        public void AllShadersCompileWithoutWarningsOrErrors()
        {
            var expectedPipeline = RequireExpectedPipeline();
            HdrpFixture.EnsureImported(expectedPipeline);

            var platforms = CompilerPlatform.Resolve(SplitCommandLineList("-shaderPlatforms"));
            var colorSpaces = RequestedColorSpaces(expectedPipeline);
            var shaderPaths = FindShaderPaths(expectedPipeline);
            var sweep = new VariantSweep(expectedPipeline, platforms, colorSpaces);

            Debug.Log("Shader compiler matrix: Unity " + Application.unityVersion
                + ", pipeline: " + expectedPipeline
                + ", color spaces: " + string.Join(", ", colorSpaces)
                + ", compiler platforms: " + string.Join(", ", platforms.Select(platform => platform.Name))
                + ", shaders: " + shaderPaths.Length
                + " (every combination while a stage's keyword space stays within "
                + VariantSelection.FullProductLimit + " variants, a pairwise covering set beyond that,"
                + " one base variant per pass for .shadergraph assets; see VariantSelection).");

            foreach (var shaderPath in shaderPaths)
                sweep.Compile(shaderPath);

            var diagnostics = sweep.Diagnostics;
            Debug.Log(sweep.Report);
            foreach (var diagnostic in diagnostics)
                Debug.Log(diagnostic);

            Assert.IsEmpty(diagnostics,
                diagnostics.Count + " shader compiler warning(s) or error(s) were found. The complete list is printed above.");
        }

        /// <summary>
        /// A shader can compile cleanly and still render magenta: when no SubShader matches the active
        /// pipeline, or the matched one only carries legacy LightMode passes an SRP draws with the error
        /// shader, the failure produces zero compiler messages. This asserts, per shader, that the
        /// SubShader the fixture's pipeline would select exists and every one of its passes is drawable there.
        /// </summary>
        [Test, Timeout(600000)]
        public void ActivePipelineSelectsDrawableSubShaders()
        {
            var expectedPipeline = RequireExpectedPipeline();
            HdrpFixture.EnsureImported(expectedPipeline);

            var pipelineTag = ExpectedPipelineTag(expectedPipeline);
            var builtin = pipelineTag.Length == 0;
            var renderPipelineTag = new ShaderTagId("RenderPipeline");
            var lightModeTag = new ShaderTagId("LightMode");
            var failures = new List<string>();

            foreach (var shaderPath in FindShaderPaths(expectedPipeline))
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                {
                    failures.Add(shaderPath + " | failed to load.");
                    continue;
                }

                var selected = -1;
                for (var index = 0; index < shader.subshaderCount; index++)
                {
                    var tag = shader.FindSubshaderTagValue(index, renderPipelineTag).name;
                    if (!string.IsNullOrEmpty(tag) && !string.Equals(tag, pipelineTag, StringComparison.Ordinal))
                        continue;
                    selected = index;
                    break;
                }

                if (selected < 0)
                {
                    failures.Add(shaderPath + " | no SubShader is selectable for '" + expectedPipeline
                        + "' (every SubShader is tagged for another pipeline) — it renders magenta there.");
                    continue;
                }

                if (builtin)
                    continue;

                var passCount = shader.GetPassCountInSubshader(selected);
                for (var pass = 0; pass < passCount; pass++)
                {
                    var lightMode = shader.FindPassTagValue(selected, pass, lightModeTag).name;
                    if (Array.IndexOf(legacyOnlyLightModes, lightMode) < 0)
                        continue;
                    failures.Add(shaderPath + " | SubShader " + selected + " pass " + pass
                        + " has legacy LightMode '" + lightMode + "', which '" + expectedPipeline
                        + "' draws with the error shader — it renders magenta there.");
                }
            }

            foreach (var failure in failures)
                Debug.Log("[SubShader selection] " + failure);

            Assert.IsEmpty(failures,
                failures.Count + " shader(s) are not drawable under the '" + expectedPipeline
                + "' pipeline. The complete list is printed above.");
        }

        /// <summary>LightMode tags only the Built-in pipeline draws; SRPs render such passes with the magenta error shader.</summary>
        private static readonly string[] legacyOnlyLightModes =
        {
            "Always", "ForwardBase", "ForwardAdd", "PrepassBase", "PrepassFinal",
            "Vertex", "VertexLMRGBM", "VertexLM"
        };

        private static string RequireExpectedPipeline()
        {
            var expectedPipeline = GetCommandLineValue("-expectedRenderPipeline");
            Assert.IsNotEmpty(expectedPipeline, "-expectedRenderPipeline is required.");
            return expectedPipeline;
        }

        private static bool IsHdrp(string expectedPipeline)
            => string.Equals(expectedPipeline, "hdrp", StringComparison.OrdinalIgnoreCase);

        private static string ExpectedPipelineTag(string expectedPipeline)
        {
            if (string.Equals(expectedPipeline, "builtin", StringComparison.OrdinalIgnoreCase))
                return "";
            if (string.Equals(expectedPipeline, "urp", StringComparison.OrdinalIgnoreCase))
                return "UniversalPipeline";
            if (IsHdrp(expectedPipeline))
                return "HDRenderPipeline";
            Assert.Fail("Unknown expected render pipeline: " + expectedPipeline);
            return null;
        }

        private static string[] FindShaderPaths(string expectedPipeline)
        {
            var shaderPaths = new List<string>();

            foreach (var packageRoot in requiredShaderRoots)
            {
                var packageShaders = FindShadersUnder(packageRoot);
                Assert.IsNotEmpty(packageShaders, "No shaders were found under " + packageRoot + ".");
                shaderPaths.AddRange(packageShaders);
            }

            foreach (var packageRoot in optionalShaderRoots)
                shaderPaths.AddRange(FindShadersUnder(packageRoot));

            if (IsHdrp(expectedPipeline))
            {
                var hdrpShaders = FindShadersUnder(HdrpFixture.TargetFolder);
                Assert.IsNotEmpty(hdrpShaders, "No HDRP shader assets were found under " + HdrpFixture.TargetFolder + ".");
                shaderPaths.AddRange(hdrpShaders);
            }

            return shaderPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] FindShadersUnder(string packageRoot)
            => AssetDatabase.FindAssets("t:Shader", new[] { packageRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        /// <summary>
        /// Mirrors the package's own HDRP delivery: the graphs live in Core's hidden HdrpAssets~ folder
        /// (a .shadergraph in the always-imported tree breaks Built-in-only imports) and are copied
        /// into Assets before use — here explicitly, because the fixture has no active HDRP asset
        /// to trigger the editor's automatic copy.
        /// </summary>
        private static class HdrpFixture
        {
            internal const string TargetFolder = "Assets/LightSide/HDRP";
            private const string SourceFolder = "Packages/media.lightside.core/HdrpAssets~";

            internal static void EnsureImported(string expectedPipeline)
            {
                if (!IsHdrp(expectedPipeline)) return;

                var source = FileUtil.GetPhysicalPath(SourceFolder);
                Assert.IsTrue(Directory.Exists(source),
                    "HDRP shader sources are missing from the package: " + SourceFolder);

                Directory.CreateDirectory(TargetFolder);
                var copied = false;
                foreach (var file in Directory.GetFiles(source))
                {
                    var target = Path.Combine(TargetFolder, Path.GetFileName(file));
                    if (File.Exists(target)) continue;
                    File.Copy(file, target);
                    copied = true;
                }

                if (copied)
                    AssetDatabase.ImportAsset(TargetFolder, ImportAssetOptions.ImportRecursive);
            }
        }

        private static string GetCommandLineValue(string argument)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], argument, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }

            return null;
        }

        private static string[] SplitCommandLineList(string argument)
        {
            var value = GetCommandLineValue(argument);
            return string.IsNullOrEmpty(value)
                ? Array.Empty<string>()
                : value.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToArray();
        }

        /// <summary>
        /// The color spaces to sweep, from <c>-shaderColorSpaces</c>; Gamma and Linear when it is absent,
        /// Linear alone under HDRP. Gamma and Linear compile different variants, so a run that names only
        /// one leaves the other unchecked — the caller decides which legs carry that cost.
        /// </summary>
        private static ColorSpace[] RequestedColorSpaces(string expectedPipeline)
        {
            // HDRP is Linear-only; a Gamma sweep there would compile a configuration the pipeline forbids.
            var hdrp = IsHdrp(expectedPipeline);
            var requested = SplitCommandLineList("-shaderColorSpaces");
            if (requested.Length == 0)
                return hdrp ? new[] { ColorSpace.Linear } : new[] { ColorSpace.Gamma, ColorSpace.Linear };

            var colorSpaces = new List<ColorSpace>();
            foreach (var name in requested)
            {
                ColorSpace colorSpace;
                Assert.IsTrue(Enum.TryParse(name, true, out colorSpace)
                    && (colorSpace == ColorSpace.Gamma || colorSpace == ColorSpace.Linear),
                    "-shaderColorSpaces names an unknown color space: " + name + ".");
                Assert.IsFalse(hdrp && colorSpace == ColorSpace.Gamma,
                    "HDRP is Linear-only; -shaderColorSpaces must not ask an HDRP fixture for Gamma.");
                colorSpaces.Add(colorSpace);
            }

            return colorSpaces.Distinct().ToArray();
        }

        /// <summary>
        /// A compiler platform paired with the build target its platform defines are taken from, so a sweep
        /// compiles the mobile flavour of the mobile APIs. The color space is not a project setting here but
        /// a define handed to every compile, which is what makes a Gamma leg provably a Gamma leg.
        /// </summary>
        private sealed class CompilerPlatform
        {
            private static readonly KeyValuePair<string, string>[] buildTargets =
            {
                new KeyValuePair<string, string>("D3D", "StandaloneWindows64"),
                new KeyValuePair<string, string>("OpenGLCore", "StandaloneWindows64"),
                new KeyValuePair<string, string>("GLES3x", "Android"),
                new KeyValuePair<string, string>("Vulkan", "Android"),
                new KeyValuePair<string, string>("Metal", "iOS"),
                new KeyValuePair<string, string>("WebGPU", "WebGL")
            };

            /// <summary>Compiler platforms an "all" sweep leaves out: the package's `#pragma target 3.5` excludes them by contract.</summary>
            private static readonly string[] excludedFromAll = { "GLES20" };

            private CompilerPlatform(ShaderCompilerPlatform compiler, BuildTarget target)
            {
                Compiler = compiler;
                Target = target;
            }

            internal ShaderCompilerPlatform Compiler { get; private set; }
            internal BuildTarget Target { get; private set; }
            internal string Name => Compiler.ToString();

            internal BuiltinShaderDefine[] Defines(ColorSpace colorSpace)
            {
                var defines = ShaderUtil.GetShaderPlatformKeywordsForBuildTarget(Compiler, Target)
                    .Where(define => define != BuiltinShaderDefine.UNITY_COLORSPACE_GAMMA)
                    .ToList();
                if (colorSpace == ColorSpace.Gamma)
                    defines.Add(BuiltinShaderDefine.UNITY_COLORSPACE_GAMMA);
                return defines.ToArray();
            }

            /// <summary>
            /// Resolves the requested platform names; an empty list or the single name <c>all</c> takes every
            /// platform this editor can compile for. A name this editor cannot compile fails the run, and so
            /// does an available platform with no build target mapped — quietly compiling fewer platforms
            /// would report a green check for coverage that never ran.
            /// </summary>
            internal static CompilerPlatform[] Resolve(string[] requested)
            {
                var available = AvailableMask();
                Assert.AreNotEqual(0, available, "Unity reported no available shader compiler platforms.");

                var platforms = new List<CompilerPlatform>();
                if (requested.Length == 0
                    || requested.Length == 1 && string.Equals(requested[0], "all", StringComparison.OrdinalIgnoreCase))
                {
                    for (var index = 0; index < 32; index++)
                    {
                        if ((available & (1 << index)) == 0) continue;
                        var platform = (ShaderCompilerPlatform)index;
                        if (Array.IndexOf(excludedFromAll, platform.ToString()) >= 0)
                        {
                            Debug.Log("Shader compiler platform " + platform + " is available but excluded from 'all'.");
                            continue;
                        }
                        platforms.Add(Map(platform));
                    }
                }
                else
                {
                    foreach (var name in requested)
                    {
                        ShaderCompilerPlatform platform;
                        Assert.IsTrue(Enum.TryParse(name, true, out platform)
                            && Enum.IsDefined(typeof(ShaderCompilerPlatform), platform),
                            "-shaderPlatforms names an unknown shader compiler platform: " + name + ".");
                        Assert.AreNotEqual(0, available & (1 << (int)platform),
                            "This editor cannot compile for " + platform + "; it offers "
                            + string.Join(", ", AvailableNames(available)) + ".");
                        platforms.Add(Map(platform));
                    }
                }

                Assert.IsNotEmpty(platforms, "No shader compiler platform was selected.");
                return platforms.ToArray();
            }

            private static CompilerPlatform Map(ShaderCompilerPlatform platform)
            {
                foreach (var entry in buildTargets)
                {
                    if (!string.Equals(entry.Key, platform.ToString(), StringComparison.Ordinal)) continue;
                    BuildTarget target;
                    Assert.IsTrue(Enum.TryParse(entry.Value, out target),
                        "Build target " + entry.Value + " does not exist in this editor.");
                    return new CompilerPlatform(platform, target);
                }

                Assert.Fail("No build target is mapped for shader compiler platform " + platform
                    + "; add it to CompilerPlatform.buildTargets.");
                return null;
            }

            private static int AvailableMask()
            {
                var availablePlatforms = typeof(ShaderUtil).GetMethod("GetAvailableShaderCompilerPlatforms",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                Assert.NotNull(availablePlatforms, "Unity's all-platform shader compiler API is unavailable.");
                return (int)availablePlatforms.Invoke(null, null);
            }

            private static IEnumerable<string> AvailableNames(int available)
            {
                for (var index = 0; index < 32; index++)
                {
                    if ((available & (1 << index)) != 0)
                        yield return ((ShaderCompilerPlatform)index).ToString();
                }
            }
        }

        /// <summary>
        /// Compiles the selected variants of every pass and stage of a shader through
        /// <c>ShaderData.Pass.CompileVariant</c>, which compiles exactly the variant it is handed: no shader
        /// cache, no stripper, no dependence on the render pipeline asset. A message is keyed without its
        /// variant so one warning shared by a hundred variants reads as one line, naming the first variant
        /// it was seen in.
        /// </summary>
        private sealed class VariantSweep
        {
            private static readonly ShaderType[] stages =
            {
                ShaderType.Vertex, ShaderType.Fragment, ShaderType.Geometry, ShaderType.Hull, ShaderType.Domain
            };

            private readonly string pipeline;
            private readonly CompilerPlatform[] platforms;
            private readonly ColorSpace[] colorSpaces;
            private readonly Dictionary<string, string> firstVariantByMessage = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly StringBuilder report = new StringBuilder();

            internal VariantSweep(string pipeline, CompilerPlatform[] platforms, ColorSpace[] colorSpaces)
            {
                this.pipeline = pipeline;
                this.platforms = platforms;
                this.colorSpaces = colorSpaces;
            }

            internal ICollection<string> Diagnostics
                => firstVariantByMessage.Select(entry => entry.Key + " | first seen in variant " + entry.Value).ToArray();

            internal string Report => report.ToString();

            internal void Compile(string shaderPath)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                {
                    Record("[Error] Failed to load shader: " + shaderPath, "<none>");
                    return;
                }

                var graph = shaderPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase);
                var data = ShaderUtil.GetShaderData(shader);
                var variants = 0;
                var largest = 0;
                var largestWhere = "";

                for (var subshaderIndex = 0; subshaderIndex < data.SubshaderCount; subshaderIndex++)
                {
                    var subshader = data.GetSubshader(subshaderIndex);
                    for (var passIndex = 0; passIndex < subshader.PassCount; passIndex++)
                    {
                        var pass = subshader.GetPass(passIndex);
                        if (pass.IsGrabPass) continue;

                        var where = shaderPath + " | SubShader " + subshaderIndex + " pass " + passIndex + " '" + pass.Name + "'";
                        try
                        {
                            var groups = graph ? null : KeywordGroups.Parse(pass.SourceCode, shaderPath, report);
                            var identifier = new PassIdentifier((uint)subshaderIndex, (uint)passIndex);
                            foreach (var stage in stages)
                            {
                                if (!pass.HasShaderStage(stage)) continue;

                                var stageKeywords = ShaderUtil.GetPassKeywords(shader, identifier, stage)
                                    .Select(keyword => keyword.name)
                                    .ToArray();
                                var rows = graph
                                    ? VariantSelection.BaseOnly()
                                    : VariantSelection.Rows(KeywordGroups.ForStage(groups, stageKeywords));

                                foreach (var platform in platforms)
                                {
                                    foreach (var colorSpace in colorSpaces)
                                    {
                                        var defines = platform.Defines(colorSpace);
                                        var configuration = pipeline + ", " + colorSpace;
                                        foreach (var row in rows)
                                        {
                                            var info = pass.CompileVariant(stage, row, platform.Compiler, platform.Target, defines);
                                            variants++;

                                            var size = info.ShaderData == null ? 0 : info.ShaderData.Length;
                                            if (size > largest)
                                            {
                                                largest = size;
                                                largestWhere = "pass '" + pass.Name + "' " + stage + " " + platform.Name + " " + Describe(row);
                                            }

                                            var messages = info.Messages ?? Array.Empty<ShaderMessage>();
                                            foreach (var message in messages)
                                                Record(Format(where, configuration, platform, stage, message), Describe(row));
                                            if (!info.Success && messages.Length == 0)
                                                Record("[Error] [" + configuration + "] [" + platform.Name + "] " + where
                                                    + " | " + stage + " | compilation failed without a message.", Describe(row));
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            Record("[Error] " + where + " | " + exception.GetBaseException(), "<none>");
                        }
                    }
                }

                report.AppendLine(shaderPath + ": " + variants + " variants compiled"
                    + (largest > 0 ? ", largest program " + largest + " bytes (" + largestWhere + ")" : "") + ".");
            }

            private void Record(string message, string variant)
            {
                if (!firstVariantByMessage.ContainsKey(message))
                    firstVariantByMessage.Add(message, variant);
            }

            private static string Describe(string[] keywords)
                => keywords.Length == 0 ? "'<no keywords>'" : "'" + string.Join(" ", keywords) + "'";

            private static string Format(string where, string configuration, CompilerPlatform platform,
                ShaderType stage, ShaderMessage message)
            {
                var builder = new StringBuilder();
                builder.Append('[').Append(message.severity).Append("] [").Append(configuration).Append("] [")
                    .Append(platform.Name).Append("] ").Append(where).Append(" | ").Append(stage);
                if (!string.IsNullOrEmpty(message.file))
                    builder.Append(" | ").Append(message.file);
                if (message.line > 0)
                    builder.Append(':').Append(message.line);
                builder.Append(" | ").Append(message.message);
                if (!string.IsNullOrEmpty(message.messageDetails))
                    builder.AppendLine().Append(message.messageDetails);
                return builder.ToString();
            }
        }

        /// <summary>
        /// The keyword sets a pass declares, each a group of mutually exclusive members where an empty
        /// string is the member with no keyword. Groups are read from the pass's own pragma lines and the
        /// files it pulls in with <c>#include_with_pragmas</c>; a keyword the stage reports that no parsed
        /// group claims becomes a group of its own, so an unparsed pragma form still compiles both ways.
        /// </summary>
        private static class KeywordGroups
        {
            private const int includeDepthLimit = 4;

            private static readonly Regex keywordPragma = new Regex(
                @"^\s*#pragma\s+(?<kind>multi_compile|shader_feature)(?<flags>(?:_local|_vertex|_fragment|_hull|_domain|_geometry|_raytracing)*)\s+(?<list>[^\r\n]+?)\s*$",
                RegexOptions.Multiline | RegexOptions.Compiled);

            private static readonly Regex builtinPragma = new Regex(
                @"^\s*#pragma\s+multi_compile_(?<name>fog|instancing|shadowcaster)\b",
                RegexOptions.Multiline | RegexOptions.Compiled);

            private static readonly Regex includeWithPragmas = new Regex(
                @"^\s*#include_with_pragmas\s+""(?<path>[^""]+)""",
                RegexOptions.Multiline | RegexOptions.Compiled);

            private static readonly Dictionary<string, string[]> builtinGroups = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "fog", new[] { "", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" } },
                { "instancing", new[] { "", "INSTANCING_ON" } },
                { "shadowcaster", new[] { "", "SHADOWS_DEPTH", "SHADOWS_CUBE" } }
            };

            internal static List<string[]> Parse(string source, string shaderPath, StringBuilder report)
            {
                var groups = new List<string[]>();
                Collect(source, Path.GetDirectoryName(shaderPath), groups, 0, report);
                return groups;
            }

            private static void Collect(string source, string directory, List<string[]> groups, int depth, StringBuilder report)
            {
                foreach (Match match in builtinPragma.Matches(source))
                    groups.Add(builtinGroups[match.Groups["name"].Value]);

                foreach (Match match in keywordPragma.Matches(source))
                {
                    var members = match.Groups["list"].Value
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(token => token == "_" || token == "__" ? "" : token)
                        .ToList();
                    if (match.Groups["kind"].Value == "shader_feature" && members.Count == 1)
                        members.Insert(0, "");
                    groups.Add(members.ToArray());
                }

                if (depth >= includeDepthLimit) return;
                foreach (Match match in includeWithPragmas.Matches(source))
                {
                    var path = match.Groups["path"].Value;
                    var resolved = Resolve(IsProjectPath(path) ? path : Path.Combine(directory, path).Replace('\\', '/'));
                    if (!File.Exists(resolved))
                    {
                        report.AppendLine("#include_with_pragmas \"" + path + "\" was not found from " + directory
                            + "; its keywords compile as independent groups.");
                        continue;
                    }
                    Collect(File.ReadAllText(resolved), Path.GetDirectoryName(resolved), groups, depth + 1, report);
                }
            }

            private static bool IsProjectPath(string path)
                => path.StartsWith("Packages/", StringComparison.Ordinal) || path.StartsWith("Assets/", StringComparison.Ordinal);

            /// <summary>A project-relative path becomes the physical one (a registry package lives under Library/PackageCache); every path comes back normalised.</summary>
            private static string Resolve(string path)
                => Path.GetFullPath(IsProjectPath(path) ? FileUtil.GetPhysicalPath(path) : path);

            /// <summary>
            /// Narrows parsed groups to the keywords Unity reports for one stage — a stage-suffixed keyword
            /// never inflates the other stage, and a keyword compiled out by a version conditional never
            /// appears — and gives every reported keyword no group claimed a two-member group of its own.
            /// </summary>
            internal static string[][] ForStage(List<string[]> groups, string[] stageKeywords)
            {
                var known = new HashSet<string>(stageKeywords, StringComparer.Ordinal);
                var claimed = new HashSet<string>(StringComparer.Ordinal);
                var result = new List<string[]>();

                foreach (var group in groups)
                {
                    var members = new List<string>();
                    foreach (var member in group)
                    {
                        if (member.Length == 0)
                        {
                            if (!members.Contains("")) members.Add("");
                            continue;
                        }
                        if (!known.Contains(member) || !claimed.Add(member)) continue;
                        members.Add(member);
                    }
                    if (members.Any(member => member.Length > 0))
                        result.Add(members.ToArray());
                }

                foreach (var keyword in stageKeywords)
                {
                    if (claimed.Add(keyword))
                        result.Add(new[] { "", keyword });
                }

                return result.ToArray();
            }
        }

        /// <summary>
        /// Which variants a stage compiles. Every combination while the product of the group sizes stays
        /// within <see cref="FullProductLimit"/> — a space this package owns is small and a bug can hide in
        /// any one combination. Beyond that a pairwise covering set: every pair of values from two groups
        /// appears together in at least one row, plus the all-off and all-on corners. That is where
        /// interaction bugs live, and it turns a pipeline's cross product of lighting keywords into a few
        /// dozen variants. The greedy construction is deterministic, so a leg compiles the same set every run.
        /// </summary>
        private static class VariantSelection
        {
            internal const int FullProductLimit = 64;

            internal static string[][] BaseOnly() => new[] { Array.Empty<string>() };

            internal static string[][] Rows(string[][] groups)
            {
                if (groups.Length == 0)
                    return BaseOnly();

                long product = 1;
                foreach (var group in groups)
                {
                    product *= group.Length;
                    if (product > FullProductLimit) break;
                }

                var rows = product <= FullProductLimit ? FullProduct(groups) : Pairwise(groups);
                return rows.Select(row => ToKeywords(groups, row)).ToArray();
            }

            private static string[] ToKeywords(string[][] groups, int[] row)
            {
                var keywords = new List<string>();
                for (var index = 0; index < groups.Length; index++)
                {
                    var member = groups[index][row[index]];
                    if (member.Length > 0) keywords.Add(member);
                }
                return keywords.ToArray();
            }

            private static List<int[]> FullProduct(string[][] groups)
            {
                var rows = new List<int[]>();
                var row = new int[groups.Length];
                while (true)
                {
                    rows.Add((int[])row.Clone());
                    var index = groups.Length - 1;
                    while (index >= 0 && ++row[index] == groups[index].Length)
                        row[index--] = 0;
                    if (index < 0) break;
                }
                return rows;
            }

            private static List<int[]> Pairwise(string[][] groups)
            {
                var count = groups.Length;
                var uncovered = new HashSet<Pair>();
                for (var a = 0; a < count; a++)
                for (var b = a + 1; b < count; b++)
                for (var va = 0; va < groups[a].Length; va++)
                for (var vb = 0; vb < groups[b].Length; vb++)
                    uncovered.Add(new Pair(a, va, b, vb));

                var rows = new List<int[]>();
                var allOff = new int[count];
                var allOn = groups.Select(group => group.Length - 1).ToArray();
                AddRow(rows, uncovered, allOff);
                AddRow(rows, uncovered, allOn);

                while (uncovered.Count > 0)
                {
                    var seed = uncovered.OrderBy(pair => pair.A).ThenBy(pair => pair.B).ThenBy(pair => pair.ValueA).ThenBy(pair => pair.ValueB).First();
                    var row = Enumerable.Repeat(-1, count).ToArray();
                    row[seed.A] = seed.ValueA;
                    row[seed.B] = seed.ValueB;

                    for (var group = 0; group < count; group++)
                    {
                        if (row[group] >= 0) continue;
                        var bestValue = 0;
                        var bestGain = -1;
                        for (var value = 0; value < groups[group].Length; value++)
                        {
                            var gain = 0;
                            for (var other = 0; other < count; other++)
                            {
                                if (other == group || row[other] < 0) continue;
                                var pair = other < group
                                    ? new Pair(other, row[other], group, value)
                                    : new Pair(group, value, other, row[other]);
                                if (uncovered.Contains(pair)) gain++;
                            }
                            if (gain > bestGain)
                            {
                                bestGain = gain;
                                bestValue = value;
                            }
                        }
                        row[group] = bestValue;
                    }

                    AddRow(rows, uncovered, row);
                }

                return rows;
            }

            private static void AddRow(List<int[]> rows, HashSet<Pair> uncovered, int[] row)
            {
                rows.Add(row);
                for (var a = 0; a < row.Length; a++)
                for (var b = a + 1; b < row.Length; b++)
                    uncovered.Remove(new Pair(a, row[a], b, row[b]));
            }

            private readonly struct Pair : IEquatable<Pair>
            {
                internal readonly int A;
                internal readonly int ValueA;
                internal readonly int B;
                internal readonly int ValueB;

                internal Pair(int a, int valueA, int b, int valueB)
                {
                    A = a;
                    ValueA = valueA;
                    B = b;
                    ValueB = valueB;
                }

                public bool Equals(Pair other)
                    => A == other.A && ValueA == other.ValueA && B == other.B && ValueB == other.ValueB;

                public override bool Equals(object obj) => obj is Pair other && Equals(other);

                public override int GetHashCode() => ((A * 397 + ValueA) * 397 + B) * 397 + ValueB;
            }
        }
    }
}
