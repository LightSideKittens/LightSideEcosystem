using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

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
        /// Verifies shader variants across the requested compiler platforms and color spaces;
        /// any compiler diagnostic fails the run. The explicit timeout overrides the runner's default.
        /// </summary>
        [Test, Timeout(21600000)]
        public void AllShadersCompileWithoutWarningsOrErrors()
        {
            var expectedPipeline = RequireExpectedPipeline();
            HdrpFixture.EnsureImported(expectedPipeline);

            var platforms = CompilerPlatform.Resolve(SplitCommandLineList("-shaderPlatforms"));
            var colorSpaces = RequestedColorSpaces(expectedPipeline);
            var shaderPaths = FindShaderPaths(expectedPipeline);

            Debug.Log("Shader compiler matrix: Unity " + Application.unityVersion
                + ", pipeline: " + expectedPipeline
                + ", color spaces: " + string.Join(", ", colorSpaces)
                + ", compiler platforms: " + string.Join(", ", platforms.Select(platform => platform.Name))
                + ", shaders: " + shaderPaths.Length
                + " (every combination for a single keyword set or a product of up to "
                + VariantSelection.FullProductLimit + " variants; pairwise coverage beyond that, including Shader Graph passes).");

            using (var sweep = new VariantSweep(expectedPipeline, platforms, colorSpaces))
            {
                foreach (var shaderPath in shaderPaths)
                    sweep.Compile(shaderPath);
                Debug.Log(sweep.Report);
            }
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
                    if (File.Exists(target) && File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(target))) continue;
                    File.Copy(file, target, true);
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

            /// <summary>Indicates whether the compiler emits all stages through the Vertex entry point.</summary>
            internal bool CombinesStages => Compiler == ShaderCompilerPlatform.Vulkan
                || Compiler == ShaderCompilerPlatform.OpenGLCore || Compiler == ShaderCompilerPlatform.GLES3x
                || Name == "WebGPU";

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

        private sealed class VariantSweep : IDisposable
        {
            private readonly CompilerPlatform[] platforms;
            private readonly ColorSpace[] colorSpaces;
            private readonly VerificationCache cache;
            private readonly StringBuilder report = new StringBuilder();
            private readonly Stopwatch elapsed = Stopwatch.StartNew();
            private string nativeError;
            private int compiled;
            private int reused;

            internal VariantSweep(string pipeline, CompilerPlatform[] platforms, ColorSpace[] colorSpaces)
            {
                this.platforms = platforms;
                this.colorSpaces = colorSpaces;
                cache = new VerificationCache(pipeline);
                Application.logMessageReceivedThreaded += OnLog;
            }

            internal string Report => report + "\nShader sweep: " + compiled + " variants compiled, "
                + reused + " verified variants reused, " + elapsed.Elapsed.TotalSeconds.ToString("F1") + "s.";

            public void Dispose() => Application.logMessageReceivedThreaded -= OnLog;

            private void OnLog(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    System.Threading.Interlocked.CompareExchange(ref nativeError, message, null);
            }

            private void CheckLog(string operation)
            {
                var error = System.Threading.Interlocked.CompareExchange(ref nativeError, null, null);
                Assert.IsNull(error, operation + " | " + error);
            }

            internal void Compile(string shaderPath)
            {
                var timer = Stopwatch.StartNew();
                Debug.Log("[Shader] " + shaderPath + " | preparing passes");
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                Assert.NotNull(shader, "Failed to load shader: " + shaderPath);
                CheckLog(shaderPath);

                var importedMessages = ShaderUtil.GetShaderMessages(shader);
                Assert.IsEmpty(importedMessages, shaderPath + " | import diagnostics:\n"
                    + string.Join("\n", importedMessages.Select(message => message.message)));

                var data = ShaderUtil.GetShaderData(shader);
                var keywords = new HashSet<string>(shader.keywordSpace.keywords.Select(keyword => keyword.name),
                    StringComparer.Ordinal);
                var programs = new List<PassProgram>();
                for (var subshaderIndex = 0; subshaderIndex < data.SubshaderCount; subshaderIndex++)
                {
                    var subshader = data.GetSubshader(subshaderIndex);
                    for (var passIndex = 0; passIndex < subshader.PassCount; passIndex++)
                    {
                        var pass = subshader.GetPass(passIndex);
                        if (pass.IsGrabPass) continue;
                        var where = shaderPath + " | SubShader " + subshaderIndex + " pass " + passIndex
                            + " '" + pass.Name + "'";
                        Debug.Log("[Pass] " + where);
                        programs.Add(new PassProgram(pass, where, shaderPath, keywords));
                        CheckLog(where);
                    }
                }
                Assert.IsNotEmpty(programs, shaderPath + " has no programmable passes.");
                var dependencyKey = cache.Dependencies(shaderPath, programs);
                var beforeCompiled = compiled;
                var beforeReused = reused;

                foreach (var platform in platforms)
                foreach (var colorSpace in colorSpaces)
                {
                    var defines = platform.Defines(colorSpace);
                    var configuration = platform.Name + "/" + platform.Target + "/" + colorSpace;
                    CheckLog(shaderPath + " | " + configuration);
                    var key = cache.Key(dependencyKey, configuration, defines);
                    if (cache.TryRead(key, out var cachedVariants))
                    {
                        reused += cachedVariants;
                        Debug.Log("[Shader] " + shaderPath + " | " + configuration + " | cache hit: "
                            + cachedVariants + " verified variants");
                        continue;
                    }

                    var configurationTimer = Stopwatch.StartNew();
                    var count = 0;
                    foreach (var program in programs)
                    {
                        if (!program.Groups.Supports(platform))
                        {
                            Debug.Log("[Excluded] " + program.Where + " | " + configuration + " | renderer directive");
                            continue;
                        }
                        var stages = platform.CombinesStages
                            ? new[] { ShaderType.Vertex }
                            : program.Stages;
                        foreach (var stage in stages)
                        {
                            var groups = program.Groups.ForStage(stage, platform);
                            var rows = VariantSelection.Rows(groups);
                            var coverage = VariantSelection.IsExhaustive(groups) ? "exhaustive" : "pairwise";
                            var where = program.Where + " | " + configuration + " | "
                                + (platform.CombinesStages ? "combined stages" : stage.ToString());
                            Debug.Log("[Compile] " + where + " | " + rows.Length + " variants | " + coverage);
                            var stageTimer = Stopwatch.StartNew();
                            var progressAt = stageTimer.Elapsed.TotalSeconds + 30;
                            for (var index = 0; index < rows.Length; index++)
                            {
                                var row = rows[index];
                                var info = program.Pass.CompileVariant(stage, row, platform.Compiler, platform.Target, defines);
                                var variant = row.Length == 0 ? "<no keywords>" : string.Join(" ", row);
                                CheckLog(where + " | " + variant);
                                var messages = info.Messages ?? Array.Empty<ShaderMessage>();
                                Assert.IsTrue(info.Success && messages.Length == 0,
                                    where + " | " + variant + "\n"
                                    + string.Join("\n", messages.Select(message => "[" + message.severity + "] "
                                        + message.file + ":" + message.line + " | " + message.message
                                        + "\n" + message.messageDetails)));
                                Assert.IsNotNull(info.ShaderData, where + " returned no bytecode.");
                                Assert.Greater(info.ShaderData.Length, 0, where + " returned empty bytecode for a declared stage.");
                                count++;
                                compiled++;
                                if (stageTimer.Elapsed.TotalSeconds >= progressAt)
                                {
                                    Debug.Log("[Progress] " + where + " | " + (index + 1) + "/" + rows.Length
                                        + " | " + stageTimer.Elapsed.TotalSeconds.ToString("F1") + "s");
                                    progressAt = stageTimer.Elapsed.TotalSeconds + 30;
                                }
                            }
                        }
                    }
                    CheckLog(shaderPath);
                    if (count > 0) cache.Write(key, count);
                    Debug.Log((count > 0 ? "[Verified] " : "[Excluded] ") + shaderPath + " | " + configuration + " | " + count
                        + " variants | " + configurationTimer.Elapsed.TotalSeconds.ToString("F1") + "s");
                }

                var summary = shaderPath + ": " + (compiled - beforeCompiled) + " variants compiled, "
                    + (reused - beforeReused) + " verified variants reused, " + programs.Count + " passes, "
                    + timer.Elapsed.TotalSeconds.ToString("F1") + "s.";
                report.AppendLine(summary);
                Debug.Log("[Shader complete] " + summary);
            }
        }

        private sealed class PassProgram
        {
            internal readonly ShaderData.Pass Pass;
            internal readonly string Where;
            internal readonly string Source;
            internal readonly ShaderType[] Stages;
            internal readonly KeywordGroups Groups;

            internal PassProgram(ShaderData.Pass pass, string where, string shaderPath, HashSet<string> keywords)
            {
                Pass = pass;
                Where = where;
                Source = pass.SourceCode;
                Groups = new KeywordGroups(Source, shaderPath, keywords);
                Stages = Groups.Stages.ToArray();
                Assert.IsTrue(Stages.Contains(ShaderType.Vertex),
                    where + " | pass source does not declare a vertex entry point.");
            }
        }

        private sealed class VerificationCache
        {
            private const string directory = "Library/LightSideShaderChecks";
            private readonly string environmentKey;

            internal VerificationCache(string pipeline)
            {
                var context = new StringBuilder()
                    .AppendLine(Application.unityVersion)
                    .AppendLine(typeof(ShaderUtil).Assembly.ManifestModule.ModuleVersionId.ToString())
                    .AppendLine(SystemInfo.operatingSystem)
                    .AppendLine(SystemInfo.processorType)
                    .AppendLine(pipeline)
                    .AppendLine(EditorUserBuildSettings.activeBuildTarget.ToString())
                    .AppendLine(ShaderUtil.disableShaderOptimization.ToString());
                foreach (var folder in new[] { "ProjectSettings", FileUtil.GetPhysicalPath("Packages/media.lightside.shader-compile-ci") })
                    foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(path => !path.EndsWith(".meta", StringComparison.Ordinal))
                        .OrderBy(path => path, StringComparer.Ordinal))
                        context.AppendLine(file.Replace('\\', '/')).AppendLine(Hash(File.ReadAllBytes(file)));
                foreach (var file in new[] { "Packages/manifest.json", "Packages/packages-lock.json" })
                    context.AppendLine(file).AppendLine(Hash(File.ReadAllBytes(file)));
                environmentKey = Hash(Encoding.UTF8.GetBytes(context.ToString()));
                Directory.CreateDirectory(directory);
            }

            internal string Dependencies(string shaderPath, List<PassProgram> programs)
            {
                var input = new StringBuilder().AppendLine(environmentKey).AppendLine(shaderPath)
                    .AppendLine(AssetDatabase.GetAssetDependencyHash(shaderPath).ToString());
                foreach (var dependency in AssetDatabase.GetDependencies(shaderPath, true).OrderBy(path => path, StringComparer.Ordinal))
                    input.AppendLine(dependency).AppendLine(AssetDatabase.GetAssetDependencyHash(dependency).ToString());
                foreach (var program in programs)
                {
                    input.AppendLine(program.Where).AppendLine(program.Source);
                    foreach (var file in program.Groups.Includes.OrderBy(path => path, StringComparer.Ordinal))
                        input.AppendLine(file).AppendLine(Hash(File.ReadAllBytes(file)));
                }
                return Hash(Encoding.UTF8.GetBytes(input.ToString()));
            }

            internal string Key(string dependencies, string configuration, BuiltinShaderDefine[] defines)
                => Hash(Encoding.UTF8.GetBytes(dependencies + "\n" + configuration + "\n"
                    + string.Join("\n", defines.Select(define => define.ToString()))));

            internal bool TryRead(string key, out int variants)
            {
                var file = Path.Combine(directory, key + ".txt");
                variants = 0;
                if (!File.Exists(file)) return false;
                variants = int.Parse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture);
                Assert.Greater(variants, 0, "Invalid shader verification receipt: " + file);
                return true;
            }

            internal void Write(string key, int variants)
            {
                Assert.Greater(variants, 0, "Cannot cache an empty shader verification.");
                File.WriteAllText(Path.Combine(directory, key + ".txt"),
                    variants.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            private static string Hash(byte[] bytes)
            {
                using (var algorithm = SHA256.Create())
                    return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Declared keyword sets and stages; unresolved conditions on coverage directives fail the check.</summary>
        private sealed class KeywordGroups
        {
            private static readonly Regex comments = new Regex(@"""(?:\\.|[^""\\])*""|/\*[\s\S]*?\*/|//[^\r\n]*", RegexOptions.Compiled);
            private static readonly Regex directives = new Regex(
                @"^[ \t]*#[ \t]*(?<kind>pragma|include_with_pragmas|if|ifdef|ifndef|elif|else|endif|define|undef)\b[ \t]*(?<body>[^\r\n]*)",
                RegexOptions.Multiline | RegexOptions.Compiled);
            private static readonly Regex includeGuard = new Regex(
                @"\A\s*#\s*ifndef\s+(?<name>\w+)\s*\r?\n\s*#\s*define\s+\k<name>\b", RegexOptions.Compiled);
            private static readonly Regex versionComparison = new Regex(
                @"^UNITY_VERSION\s*(?<operator>>=|<=|==|!=|>|<)\s*(?<version>\d+)$", RegexOptions.Compiled);
            private static readonly Dictionary<string, string[]> shortcuts = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "fog", new[] { "", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" } },
                { "instancing", new[] { "", "INSTANCING_ON" } },
                { "shadowcaster", new[] { "SHADOWS_DEPTH", "SHADOWS_CUBE" } }
            };
            private readonly List<Group> groups = new List<Group>();
            private readonly HashSet<string> keywords;
            private readonly Dictionary<string, bool?> defined = new Dictionary<string, bool?>(StringComparer.Ordinal);
            private readonly HashSet<string> skipped = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> onlyRenderers = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> excludedRenderers = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> Includes = new HashSet<string>(StringComparer.Ordinal);
            internal readonly List<ShaderType> Stages = new List<ShaderType>();

            internal KeywordGroups(string source, string shaderPath, HashSet<string> keywords)
            {
                this.keywords = keywords;
                Collect(source, shaderPath, new HashSet<string>(StringComparer.Ordinal));
                Add(new[] { "", "UNITY_SINGLE_PASS_STEREO", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON" }, 0, true);
                Add(new[] { "", "STEREO_CUBEMAP_RENDER_ON" }, 0, true);
                Stages.Sort();
                Assert.IsNotEmpty(Stages, shaderPath + " | pass source contains no shader-stage declarations.");
            }

            private void Collect(string source, string file, HashSet<string> visiting)
            {
                source = source.Replace("\\\r\n", "").Replace("\\\n", "");
                source = comments.Replace(source, match => match.Value[0] == '"' ? match.Value
                    : new string(match.Value.Select(character => character == '\r' || character == '\n' ? character : ' ').ToArray()));
                var guard = includeGuard.Match(source);
                if (guard.Success)
                {
                    var name = guard.Groups["name"].Value;
                    if (!defined.TryGetValue(name, out var present)) defined.Add(name, false);
                    else if (present == true) return;
                }
                Assert.IsTrue(visiting.Add(file), "Recursive pragma include: " + file);
                var directory = Path.GetDirectoryName(file);
                var branches = new Stack<(bool? Parent, bool? Taken)>();
                bool? enabled = true;
                foreach (Match match in directives.Matches(source))
                {
                    var kind = match.Groups["kind"].Value;
                    var body = match.Groups["body"].Value.Trim();
                    if (kind == "if" || kind == "ifdef" || kind == "ifndef")
                    {
                        var condition = kind == "if" ? VersionCondition(body)
                            : defined.TryGetValue(body, out var value) ? value : (bool?)null;
                        if (kind == "ifndef") condition = !condition;
                        branches.Push((enabled, condition));
                        enabled &= condition;
                        continue;
                    }
                    if (kind == "elif" || kind == "else" || kind == "endif")
                    {
                        Assert.IsNotEmpty(branches, "Unmatched preprocessor directive: " + kind);
                        var branch = branches.Pop();
                        var condition = kind == "elif" ? VersionCondition(body) : true;
                        enabled = kind == "endif" ? branch.Parent : branch.Parent & !branch.Taken & condition;
                        if (kind != "endif") branches.Push((branch.Parent, branch.Taken | condition));
                        continue;
                    }
                    if (enabled == false) continue;
                    if (kind == "define" || kind == "undef")
                    {
                        var name = Regex.Match(body, @"^\w+").Value;
                        if (name.Length > 0) defined[name] = enabled.HasValue ? kind == "define" : (bool?)null;
                        continue;
                    }
                    if (kind == "include_with_pragmas")
                    {
                        Assert.IsTrue(enabled.HasValue, "Unresolved condition on pragma include: " + body);
                        Assert.IsTrue(body.Length >= 2 && body[0] == '"' && body[body.Length - 1] == '"',
                            "Unsupported pragma include: " + body);
                        var path = body.Substring(1, body.Length - 2);
                        var resolved = Resolve(path.StartsWith("Packages/", StringComparison.Ordinal)
                            || path.StartsWith("Assets/", StringComparison.Ordinal) ? path : Path.Combine(directory, path));
                        Assert.IsTrue(File.Exists(resolved), "Missing pragma include: " + resolved);
                        Includes.Add(resolved);
                        Collect(File.ReadAllText(resolved), resolved, visiting);
                        continue;
                    }

                    var parts = body.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    var directive = parts[0];
                    if (Enum.TryParse(directive, true, out ShaderType stage)
                        && (int)stage >= (int)ShaderType.Vertex && (int)stage <= (int)ShaderType.Domain)
                    {
                        Assert.IsTrue(enabled.HasValue, "Unresolved condition on shader stage: " + body);
                        Assert.IsTrue(parts.Length == 2, "Invalid stage declaration: " + body);
                        if (!Stages.Contains(stage)) Stages.Add(stage);
                        continue;
                    }

                    if (directive == "skip_variants" || directive == "only_renderers" || directive == "exclude_renderers")
                    {
                        Assert.IsTrue(enabled.HasValue, "Unresolved condition on variant restriction: " + body);
                        var target = directive == "skip_variants" ? skipped
                            : directive == "only_renderers" ? onlyRenderers : excludedRenderers;
                        target.UnionWith(parts.Skip(1));
                        continue;
                    }

                    var feature = directive.StartsWith("shader_feature", StringComparison.Ordinal);
                    if (!feature && !directive.StartsWith("multi_compile", StringComparison.Ordinal)) continue;
                    Assert.IsTrue(enabled.HasValue, "Unresolved condition on keyword set: " + body);
                    var suffix = directive.Substring(feature ? "shader_feature".Length : "multi_compile".Length);
                    if (suffix.Length > 0 && shortcuts.TryGetValue(suffix.Substring(1), out var members))
                    {
                        Assert.AreEqual(1, parts.Length, "Invalid built-in keyword directive: " + body);
                        Add(members, 0, true);
                        continue;
                    }

                    var stageMask = 0;
                    foreach (var flag in suffix.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (flag == "local") continue;
                        Assert.IsTrue(Enum.TryParse(flag, true, out stage)
                            && (int)stage >= (int)ShaderType.Vertex && (int)stage <= (int)ShaderType.Domain,
                            "Unsupported keyword directive: " + body);
                        stageMask |= 1 << (int)stage;
                    }
                    Assert.Greater(parts.Length, 1, "Empty keyword directive: " + body);
                    var values = parts.Skip(1).Select(value => value == "_" || value == "__" ? "" : value).ToList();
                    if (feature && !values.Contains("")) values.Insert(0, "");
                    Add(values, stageMask);
                }
                Assert.IsEmpty(branches, "Unclosed preprocessor condition in pass source.");
                visiting.Remove(file);
            }

            private static bool? VersionCondition(string expression)
            {
                expression = expression.Trim().Trim('(', ')').Trim();
                if (expression == "0") return false;
                if (expression == "1") return true;
                var match = versionComparison.Match(expression);
                if (!match.Success) return null;
                var parts = Regex.Match(Application.unityVersion, @"^(\d+)\.(\d+)\.(\d+)");
                var major = int.Parse(parts.Groups[1].Value);
                var minor = int.Parse(parts.Groups[2].Value);
                var patch = int.Parse(parts.Groups[3].Value);
                var version = major >= 6000 ? major * 10000 + minor * 10000 + patch
                    : major * 100 + minor * 10 + Math.Min(patch, 9);
                var expected = int.Parse(match.Groups["version"].Value);
                switch (match.Groups["operator"].Value)
                {
                    case ">=": return version >= expected;
                    case "<=": return version <= expected;
                    case "==": return version == expected;
                    case "!=": return version != expected;
                    case ">": return version > expected;
                    default: return version < expected;
                }
            }

            private void Add(IEnumerable<string> values, int stageMask, bool builtIn = false)
            {
                if (!builtIn)
                    Assert.IsTrue(values.All(value => value.Length == 0 || keywords.Contains(value)),
                        "Declared keyword is missing from the imported shader: "
                        + string.Join(" ", values.Where(value => value.Length > 0 && !keywords.Contains(value))));
                var members = values.Where(value => value.Length == 0 || keywords.Contains(value))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (!members.Any(value => value.Length > 0)) return;
                if (!groups.Any(group => group.StageMask == stageMask && group.Members.SequenceEqual(members)))
                    groups.Add(new Group(members, stageMask));
            }

            internal bool Supports(CompilerPlatform platform)
            {
                var renderer = platform.Compiler == ShaderCompilerPlatform.D3D ? "d3d11"
                    : platform.Compiler == ShaderCompilerPlatform.OpenGLCore ? "glcore"
                    : platform.Compiler == ShaderCompilerPlatform.GLES3x ? "gles3"
                    : platform.Name.ToLowerInvariant();
                return (onlyRenderers.Count == 0 || onlyRenderers.Contains(renderer))
                    && !excludedRenderers.Contains(renderer);
            }

            internal string[][] ForStage(ShaderType stage, CompilerPlatform platform)
            {
                var mask = 1 << (int)stage;
                if (platform.Compiler == ShaderCompilerPlatform.Metal
                    && (stage == ShaderType.Vertex || stage == ShaderType.Hull || stage == ShaderType.Domain))
                    mask = (1 << (int)ShaderType.Vertex) | (1 << (int)ShaderType.Hull) | (1 << (int)ShaderType.Domain);
                var selected = groups.Where(group => platform.CombinesStages || group.StageMask == 0 || (group.StageMask & mask) != 0)
                    .Select(group => group.Members.Where(member => !skipped.Contains(member)).ToArray())
                    .ToArray();
                return selected;
            }

            private static string Resolve(string path)
            {
                path = path.Replace('\\', '/');
                return Path.GetFullPath(path.StartsWith("Packages/", StringComparison.Ordinal)
                    || path.StartsWith("Assets/", StringComparison.Ordinal) ? FileUtil.GetPhysicalPath(path) : path);
            }

            private sealed class Group
            {
                internal readonly string[] Members;
                internal readonly int StageMask;

                internal Group(string[] members, int stageMask)
                {
                    Members = members;
                    StageMask = stageMask;
                }
            }
        }

        /// <summary>
        /// Deterministic exhaustive coverage for small products and single sets, and pairwise coverage
        /// for larger products; pairwise coverage does not guarantee interactions of three or more sets.
        /// </summary>
        private static class VariantSelection
        {
            internal const int FullProductLimit = 64;

            internal static string[][] Rows(string[][] groups)
            {
                if (groups.Length == 0)
                    return new[] { Array.Empty<string>() };
                if (groups.Any(group => group.Length == 0))
                    return Array.Empty<string[]>();

                var rows = IsExhaustive(groups) ? FullProduct(groups) : Pairwise(groups);
                return rows.Select(row => ToKeywords(groups, row))
                    .GroupBy(row => string.Join(" ", row), StringComparer.Ordinal)
                    .Select(group => group.First()).ToArray();
            }

            internal static bool IsExhaustive(string[][] groups)
            {
                if (groups.Length <= 1) return true;
                long product = 1;
                foreach (var group in groups)
                {
                    product *= group.Length;
                    if (product > FullProductLimit) return false;
                }
                return true;
            }

            private static string[] ToKeywords(string[][] groups, int[] row)
            {
                var keywords = new List<string>();
                for (var index = 0; index < groups.Length; index++)
                {
                    var member = groups[index][row[index]];
                    if (member.Length > 0) keywords.Add(member);
                }
                return keywords.Distinct(StringComparer.Ordinal).OrderBy(keyword => keyword, StringComparer.Ordinal).ToArray();
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
                var firstValues = new int[count];
                var lastValues = groups.Select(group => group.Length - 1).ToArray();
                AddRow(rows, uncovered, firstValues);
                AddRow(rows, uncovered, lastValues);

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
