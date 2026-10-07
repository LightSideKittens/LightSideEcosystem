#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LightSide.DeviceTests
{
    /// <summary>Builds the context menu device test scene alone, for batch-mode builds on a build machine.</summary>
    public static class DeviceTestBuild
    {
        private const string Scene = "Assets/UniText_MySpace/DeviceTests/ContextMenuDeviceTest.unity";

        /// <summary>
        /// Exports the iOS Xcode project of the test scene to the path after <c>-deviceTestOutput</c>, signed for the
        /// team after <c>-deviceTestTeam</c>, and quits with the build's result.
        /// </summary>
        public static void BuildIOS()
        {
            PlayerSettings.iOS.appleDeveloperTeamID = Argument("-deviceTestTeam");
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Argument("-deviceTestOutput"),
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.Development,
            });
            Debug.Log($"[DeviceTestBuild] result={report.summary.result} errors={report.summary.totalErrors}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException($"DeviceTestBuild needs {name} <value> on the command line.");
            return args[index + 1];
        }
    }
}
#endif
