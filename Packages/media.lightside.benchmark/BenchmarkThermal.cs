using System;
using Newtonsoft.Json.Linq;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace LightSide.Benchmark
{
    /// <summary>The device's thermal state as its operating system reports it to an app.</summary>
    /// <remarks>
    /// Android: <c>status</c> and <c>statusCode</c> from <c>PowerManager.getCurrentThermalStatus</c> (API 29+) and
    /// <c>headroom</c> from <c>getThermalHeadroom(0)</c> (API 30+; null when the device has no forecast or the call came
    /// sooner than about a second after the previous one). iOS: <c>state</c> from <c>NSProcessInfo.thermalState</c>.
    /// Some Android devices report <c>none</c> whatever their temperature.
    /// </remarks>
    public static class BenchmarkThermal
    {
        /// <summary>The current reading; an empty object on platforms that report none.</summary>
        public static JObject Read()
        {
            var reading = new JObject();
#if UNITY_ANDROID && !UNITY_EDITOR
            var power = Power();
            if (apiLevel >= 29)
            {
                var code = power.Call<int>("getCurrentThermalStatus");
                reading["statusCode"] = code;
                reading["status"] = StatusName(code);
            }
            if (apiLevel >= 30)
            {
                var headroom = power.Call<float>("getThermalHeadroom", 0);
                reading["headroom"] = float.IsNaN(headroom) ? JValue.CreateNull() : new JValue(Math.Round(headroom, 3));
            }
#elif UNITY_IOS && !UNITY_EDITOR
            reading["state"] = StateName(LightSideBenchmarkThermalState());
#endif
            return reading;
        }

        /// <summary>Whether Android reports Sustained Performance Mode as implemented (API 24+); null on other platforms.</summary>
        public static bool? SustainedPerformanceSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var manager = Power();
                return apiLevel >= 24 && manager.Call<bool>("isSustainedPerformanceModeSupported");
#else
                return null;
#endif
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject power;
        static int apiLevel;

        static AndroidJavaObject Power()
        {
            if (power != null) return power;
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            apiLevel = version.GetStatic<int>("SDK_INT");
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            power = activity.Call<AndroidJavaObject>("getSystemService", "power")
                    ?? throw new InvalidOperationException("The activity returned no PowerManager.");
            return power;
        }

        static string StatusName(int status) => status switch
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
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern long LightSideBenchmarkThermalState();

        static string StateName(long state) => state switch
        {
            0 => "nominal",
            1 => "fair",
            2 => "serious",
            3 => "critical",
            _ => "unknown"
        };
#endif
    }
}
