using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Assembles a run's result document: provenance, the machine and driver state the numbers were taken
    /// on, and one section per suite. Everything here is engine-agnostic; a suite's own shape is its own.
    /// </summary>
    public static class BenchmarkEnvironment
    {
        /// <summary>Result-document schema. Raise it whenever a key changes meaning, never for an addition.</summary>
        public const string SchemaVersion = "1.8";

        /// <summary>
        /// Extra <c>systemInfo</c> entries the hosting project contributes — its own scripting defines,
        /// device notes, anything the harness cannot know. Register before a run starts; entries override
        /// same-named built-ins.
        /// </summary>
        public static readonly Dictionary<string, JToken> ExtraSystemInfo = new();

        /// <summary>
        /// Serializes the run. <paramref name="postRunSummary"/> receives the GPU-upload and power readings
        /// taken at this moment, which the runner logs so a tainted run is visible in the CI log alone.
        /// </summary>
        public static string Serialize(BenchmarkRunData data, IReadOnlyList<IBenchmarkSuite> suites,
            out string postRunSummary)
        {
            var gpuUpload = SerializeGpuUpload();
            var power = SerializePowerState("postRun");
            postRunSummary = "[Benchmark GpuUpload Post-Run] " + gpuUpload.ToString(Formatting.None)
                             + "\n[Benchmark Power Post-Run] " + power.ToString(Formatting.None);

            var phaseNotes = new JObject();
            var root = new JObject
            {
                ["version"] = SchemaVersion,
                ["timestamp"] = data.timestamp,
                ["meta"] = new JObject
                {
                    ["utc"] = data.utc,
                    ["commit"] = data.commit,
                    ["branch"] = data.branch,
                    ["dirty"] = data.dirty,
                    ["submoduleCommit"] = data.submoduleCommit,
                    ["submoduleBranch"] = data.submoduleBranch,
                    ["submoduleDirty"] = data.submoduleDirty,
                    ["source"] = data.source,
                    ["participant"] = data.participant,
                    ["repeat"] = data.repeat
                },
                ["systemInfo"] = SerializeSystemInfo(gpuUpload, power),
                ["config"] = data.config
            };

            foreach (var suite in suites)
            {
                root[suite.Section] = suite.Serialize() ?? new JObject();
                foreach (var note in suite.PhaseNotes)
                    phaseNotes[note.Key] = note.Value;
            }

            root["phaseNotes"] = phaseNotes;
            root["errors"] = new JArray(data.errors.ToArray());
            return root.ToString(Formatting.Indented);
        }

        static JObject SerializeSystemInfo(JObject gpuUpload, JObject power)
        {
            string backend;
#if ENABLE_IL2CPP
            backend = "IL2CPP";
#else
            backend = "Mono";
#endif

            var info = new JObject
            {
                ["deviceModel"] = SystemInfo.deviceModel,
                ["deviceName"] = SystemInfo.deviceName,
                ["operatingSystem"] = SystemInfo.operatingSystem,
                ["processorType"] = SystemInfo.processorType,
                ["processorCount"] = SystemInfo.processorCount,
                ["processorFrequency"] = SystemInfo.processorFrequency,
                ["systemMemorySize"] = SystemInfo.systemMemorySize,
                ["graphicsDeviceName"] = SystemInfo.graphicsDeviceName,
                ["graphicsDeviceVendor"] = SystemInfo.graphicsDeviceVendor,
                ["graphicsDeviceType"] = SystemInfo.graphicsDeviceType.ToString(),
                ["graphicsMemorySize"] = SystemInfo.graphicsMemorySize,
                ["graphicsDeviceVersion"] = SystemInfo.graphicsDeviceVersion,
                ["graphicsShaderLevel"] = SystemInfo.graphicsShaderLevel,
                ["graphicsMultiThreaded"] = SystemInfo.graphicsMultiThreaded,
                ["renderingThreadingMode"] = SystemInfo.renderingThreadingMode.ToString(),
                ["screenWidth"] = Screen.width,
                ["screenHeight"] = Screen.height,
                ["screenDpi"] = Screen.dpi,
                ["screenRefreshRateHz"] = RefreshRateHz(),
                ["targetFrameRate"] = Application.targetFrameRate,
                ["vSyncCount"] = QualitySettings.vSyncCount,
                ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                ["qualityLevel"] = QualityLevelName(),
                ["renderPipeline"] = RenderPipelineName(),
                ["unityVersion"] = Application.unityVersion,
                ["applicationVersion"] = Application.version,
                ["buildGuid"] = Application.buildGUID,
                ["scriptingBackend"] = backend,
                ["platform"] = Application.platform.ToString(),
                ["isEditor"] = Application.isEditor,
                ["isBatchMode"] = Application.isBatchMode,
                ["isDebugBuild"] = UnityEngine.Debug.isDebugBuild,
                ["jobWorkerCount"] = JobsUtility.JobWorkerCount,
                ["jobWorkerMaximumCount"] = JobsUtility.JobWorkerMaximumCount,
                ["jobCompilerEnabled"] = JobsUtility.JobCompilerEnabled,
                ["jobDebuggerEnabled"] = JobsUtility.JobDebuggerEnabled,
                ["burst"] = new JObject
                {
                    ["enabled"] = BurstCompiler.IsEnabled,
                    ["compilationEnabled"] = BurstCompiler.Options.EnableBurstCompilation,
                    ["safetyChecks"] = BurstCompiler.Options.EnableBurstSafetyChecks,
                    ["synchronousCompilation"] = BurstCompiler.Options.EnableBurstCompileSynchronously,
                    ["debug"] = BurstCompiler.Options.EnableBurstDebug
                },
                ["frameTimingStats"] = FrameTimingManager.IsFeatureEnabled(),
                ["softwareRenderer"] = SoftwareRenderer(),
                ["graphicsCapabilities"] = new JObject
                {
                    ["computeShaders"] = SystemInfo.supportsComputeShaders,
                    ["texture2DArray"] = SystemInfo.supports2DArrayTextures,
                    ["maxComputeBufferInputs"] = SystemInfo.maxComputeBufferInputsCompute,
                    ["randomWriteRHalf"] = SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf),
                    ["randomWriteArgbHalf"] = SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf),
                    ["copyTextureSupport"] = SystemInfo.copyTextureSupport.ToString(),
                    ["graphicsFence"] = SystemInfo.supportsGraphicsFence,
                    ["asyncGpuReadback"] = SystemInfo.supportsAsyncGPUReadback
                },
                ["gpuUpload"] = gpuUpload,
                ["power"] = power,
                ["lightsideDebugDefine"] =
#if LIGHTSIDE_DEBUG
                    true
#else
                    false
#endif
            };

            foreach (var extra in ExtraSystemInfo)
                info[extra.Key] = extra.Value;
            return info;
        }

        /// <summary>
        /// Whether the graphics device is a CPU rasterizer or a paravirtual adapter — the hosted CI runners' GPUs —
        /// on which GPU timings and frame intervals measure the emulation, not a GPU.
        /// </summary>
        public static bool SoftwareRenderer()
        {
            var name = SystemInfo.graphicsDeviceName ?? "";
            foreach (var marker in SoftwareRendererMarkers)
                if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static readonly string[] SoftwareRendererMarkers =
        {
            "Basic Render Driver", "SwiftShader", "llvmpipe", "softpipe", "Paravirtual", "WARP", "Software"
        };

        static double RefreshRateHz()
        {
#if UNITY_2022_2_OR_NEWER
            return Screen.currentResolution.refreshRateRatio.value;
#else
            return Screen.currentResolution.refreshRate;
#endif
        }

        static string QualityLevelName()
        {
            int level = QualitySettings.GetQualityLevel();
            return level >= 0 && level < QualitySettings.names.Length ? QualitySettings.names[level] : level.ToString();
        }

        static string RenderPipelineName() => GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.GetType().FullName
            : "BuiltIn";

        static JObject SerializeGpuUpload()
        {
            bool rHalfTexture2DArray = GpuUpload.Supports(GraphicsFormat.R16_SFloat,
                TextureDimension.Tex2DArray);
            bool rgbaHalfTexture2DArray = GpuUpload.Supports(GraphicsFormat.R16G16B16A16_SFloat,
                TextureDimension.Tex2DArray);
            bool supported = GpuUpload.IsSupported;
            var obj = new JObject
            {
                ["observation"] = "postRunProbe",
                ["probeMayInitializeBackend"] = true,
                ["supported"] = supported
            };
            if (!supported) return obj;

            var info = GpuUpload.Info;
            obj["physicalPoolInstalled"] = info.MaxConcurrentSubmissions > 0;
            obj["renderer"] = info.Renderer.ToString();
            obj["abi"] = $"{info.AbiMajor}.{info.AbiMinor}";
            obj["capabilities"] = info.Capabilities.ToString();
            obj["graphicsDeviceEpoch"] = info.GraphicsDeviceEpoch.ToString();
            obj["maxStagingBytes"] = info.MaxStagingBytes.ToString();
            obj["maxConcurrentSubmissions"] = info.MaxConcurrentSubmissions;
            obj["rHalfTexture2DArray"] = rHalfTexture2DArray;
            obj["rgbaHalfTexture2DArray"] = rgbaHalfTexture2DArray;

            if (GpuUpload.TryGetStats(out var stats, out var error))
                obj["stats"] = new JObject
                {
                    ["submissionsAccepted"] = stats.SubmissionsAccepted.ToString(),
                    ["submissionsRejected"] = stats.SubmissionsRejected.ToString(),
                    ["submissionsEncoded"] = stats.SubmissionsEncoded.ToString(),
                    ["duplicateCallbacks"] = stats.DuplicateCallbacks.ToString(),
                    ["staleCallbacks"] = stats.StaleCallbacks.ToString(),
                    ["backpressureCount"] = stats.BackpressureCount.ToString(),
                    ["encodedPayloadBytes"] = stats.EncodedPayloadBytes.ToString(),
                    ["poolNodes"] = stats.PoolNodes.ToString(),
                    ["poolNodesFree"] = stats.PoolNodesFree.ToString(),
                    ["poolNodesInFlight"] = stats.PoolNodesInFlight.ToString(),
                    ["poolStagingCapacityBytes"] = stats.PoolStagingCapacityBytes.ToString(),
                    ["poolStagingFreeBytes"] = stats.PoolStagingFreeBytes.ToString(),
                    ["poolStagingInFlightBytes"] = stats.PoolStagingInFlightBytes.ToString()
                };
            else
                obj["statsError"] = error.ToString();
            return obj;
        }

        static JObject SerializePowerState(string observation)
        {
            var obj = new JObject
            {
                ["observation"] = observation,
                ["batteryLevel"] = SystemInfo.batteryLevel,
                ["batteryStatus"] = SystemInfo.batteryStatus.ToString()
            };
#if UNITY_ANDROID
            if (!Application.isEditor)
            {
                try
                {
                    using var version = new AndroidJavaClass("android.os.Build$VERSION");
                    int apiLevel = version.GetStatic<int>("SDK_INT");
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using var power = activity.Call<AndroidJavaObject>("getSystemService", "power");
                    obj["androidApiLevel"] = apiLevel;
                    obj["powerSaveMode"] = power.Call<bool>("isPowerSaveMode");
                    if (apiLevel >= 29)
                    {
                        int status = power.Call<int>("getCurrentThermalStatus");
                        obj["thermalStatusCode"] = status;
                        obj["thermalStatus"] = AndroidThermalStatus(status);
                    }
                    else obj["thermalStatus"] = "notAvailableBeforeApi29";
                }
                catch (Exception exception)
                {
                    obj["androidProbeError"] = exception.GetType().Name + ": " + exception.Message;
                }
            }
#endif
            return obj;
        }

        static string AndroidThermalStatus(int status) => status switch
        {
            0 => "none",
            1 => "light",
            2 => "moderate",
            3 => "severe",
            4 => "critical",
            5 => "emergency",
            6 => "shutdown",
            _ => "unknown"
        };

        public static string EnvironmentSummary()
        {
            var power = SerializePowerState("start");
            var sb = new StringBuilder(512);
            sb.AppendLine("[Benchmark Environment]");
            sb.Append("Unity ").Append(Application.unityVersion).Append(" | ").Append(Application.platform)
              .Append(" | ").Append(Application.isEditor ? "Editor" : "Player")
              .Append(" | ").Append(UnityEngine.Debug.isDebugBuild ? "Debug" : "Release").AppendLine();
            sb.Append("Build: app=").Append(Application.version).Append(" | guid=").Append(Application.buildGUID)
              .Append(" | quality=").Append(QualityLevelName())
              .Append(" | pipeline=").Append(RenderPipelineName())
              .Append(" | colorSpace=").Append(QualitySettings.activeColorSpace).AppendLine();
            sb.Append("CPU: ").Append(SystemInfo.processorType).Append(" | logical=").Append(SystemInfo.processorCount)
              .Append(" | MHz=").Append(SystemInfo.processorFrequency)
              .Append(" | jobs=").Append(JobsUtility.JobWorkerCount).Append('/').Append(JobsUtility.JobWorkerMaximumCount)
              .Append(" | jobCompiler=").Append(JobsUtility.JobCompilerEnabled)
              .Append(" | jobDebugger=").Append(JobsUtility.JobDebuggerEnabled)
              .Append(" | Burst=").Append(BurstCompiler.IsEnabled)
              .Append(" | safety=").Append(BurstCompiler.Options.EnableBurstSafetyChecks).AppendLine();
            sb.Append("GPU: ").Append(SystemInfo.graphicsDeviceName).Append(" | ").Append(SystemInfo.graphicsDeviceType)
              .Append(" | ").Append(SystemInfo.renderingThreadingMode)
              .Append(" | graphicsMT=").Append(SystemInfo.graphicsMultiThreaded).AppendLine();
            sb.Append("Frame timing stats: ").Append(FrameTimingManager.IsFeatureEnabled())
              .Append(" | software renderer: ").Append(SoftwareRenderer()).AppendLine();
            sb.Append("Frame pacing: target=").Append(Application.targetFrameRate).Append(" | vSync=").Append(QualitySettings.vSyncCount)
              .Append(" | refresh=").Append(RefreshRateHz().ToString("F3"))
              .Append(" | resolution=").Append(Screen.width).Append('x').Append(Screen.height).AppendLine();
            sb.Append("Power at start: battery=").Append(power["batteryLevel"])
              .Append(" | status=").Append(power["batteryStatus"])
              .Append(" | saver=").Append(power["powerSaveMode"] ?? "notRecorded")
              .Append(" | thermal=").Append(power["thermalStatus"] ?? "notRecorded")
              .Append(" | probeError=").Append(power["androidProbeError"] ?? "none").AppendLine();
            sb.Append("Graphics gates: compute=").Append(SystemInfo.supportsComputeShaders)
              .Append(" | texture2DArray=").Append(SystemInfo.supports2DArrayTextures)
              .Append(" | computeBuffers=").Append(SystemInfo.maxComputeBufferInputsCompute)
              .Append(" | copyTexture=").Append(SystemInfo.copyTextureSupport)
              .Append(" | fence=").Append(SystemInfo.supportsGraphicsFence)
              .Append(" | asyncReadback=").Append(SystemInfo.supportsAsyncGPUReadback);
            return sb.ToString();
        }

    }
}
