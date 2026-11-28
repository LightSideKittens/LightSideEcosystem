using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// The player's launch arguments: those baked in at build time (<see cref="BenchmarkBuildInfo.launchArguments"/>),
    /// followed by the real command line, so a later real value overrides a baked one.
    /// </summary>
    /// <remarks>Read on the main thread first: the baked part comes from a Resources asset.</remarks>
    public static class BenchmarkLaunch
    {
        static string[] arguments;

        /// <summary>Baked arguments, then the command line.</summary>
        public static IReadOnlyList<string> Arguments => arguments ??= Collect();

        /// <summary>The arguments baked into this player at build time; empty when none were.</summary>
        public static IReadOnlyList<string> Baked => baked ??= ReadBaked();

        static string[] baked;

        /// <summary>Whether <paramref name="flag"/> is among the <see cref="Arguments"/>, compared without case.</summary>
        public static bool Has(string flag)
        {
            foreach (var argument in Arguments)
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The last value of <paramref name="flag"/>, written as <c>flag value</c> or <c>flag=value</c>, else the
        /// environment variable <paramref name="environmentVariable"/>; null when none is set or the value is blank.
        /// </summary>
        public static string Value(string flag, string environmentVariable)
        {
            var args = Arguments;
            var inline = flag + "=";
            string value = null;
            for (var i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                    value = args[i + 1];
                else if (args[i].StartsWith(inline, StringComparison.OrdinalIgnoreCase))
                    value = args[i].Substring(inline.Length);
            }
            value ??= Environment.GetEnvironmentVariable(environmentVariable);
            value = value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        static string[] Collect()
        {
            var bakedArguments = Baked;
            var real = Environment.GetCommandLineArgs();
            var all = new string[bakedArguments.Count + real.Length];
            for (var i = 0; i < bakedArguments.Count; i++) all[i] = bakedArguments[i];
            Array.Copy(real, 0, all, bakedArguments.Count, real.Length);
            return all;
        }

        static string[] ReadBaked()
        {
            var asset = Resources.Load<TextAsset>(BenchmarkBuildInfo.ResourceName);
            if (asset == null) return Array.Empty<string>();
            return JsonUtility.FromJson<BenchmarkBuildInfo>(asset.text).launchArguments ?? Array.Empty<string>();
        }
    }
}
