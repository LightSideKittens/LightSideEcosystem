using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide.Benchmark
{
    public static class FirebaseTestLabiOS
    {
        private const string GameLoopScheme = "firebase-game-loop";
        private const string GameLoopCompleteURL = "firebase-game-loop-complete://";

        private static bool _initialized;
        private static bool _isGameLoopTest;
        private static int _scenarioNumber = -1;

        public static int ScenarioNumber => _scenarioNumber;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern IntPtr FirebaseGameLoop_GetLaunchURL();

        [DllImport("__Internal")]
        private static extern bool FirebaseGameLoop_IsGameLoopLaunch();

        [DllImport("__Internal")]
        private static extern void FirebaseGameLoop_ClearLaunchURL();
#endif

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

#if UNITY_IOS && !UNITY_EDITOR
            DetectGameLoop();
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private static void DetectGameLoop()
        {
            try
            {
                if (FirebaseGameLoop_IsGameLoopLaunch())
                {
                    _isGameLoopTest = true;
                    var urlPtr = FirebaseGameLoop_GetLaunchURL();
                    if (urlPtr != IntPtr.Zero)
                    {
                        var url = Marshal.PtrToStringAnsi(urlPtr);
                        ParseScenarioFromURL(url);
                        Debug.Log($"[FirebaseTestLab] Detected via native plugin: {url}");
                    }
                    else
                    {
                        Debug.Log("[FirebaseTestLab] Detected via native plugin (no URL details)");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FirebaseTestLab] Native plugin check failed: {e.Message}");
            }

            if (!_isGameLoopTest && !string.IsNullOrEmpty(Application.absoluteURL))
            {
                if (Application.absoluteURL.Contains(GameLoopScheme))
                {
                    _isGameLoopTest = true;
                    ParseScenarioFromURL(Application.absoluteURL);
                    Debug.Log($"[FirebaseTestLab] Detected via absoluteURL: {Application.absoluteURL}");
                }
            }

            if (!_isGameLoopTest)
            {
                var args = Environment.GetCommandLineArgs();
                foreach (var arg in args)
                {
                    if (arg.Contains(GameLoopScheme))
                    {
                        _isGameLoopTest = true;
                        ParseScenarioFromURL(arg);
                        Debug.Log($"[FirebaseTestLab] Detected via command line: {arg}");
                        break;
                    }
                }
            }

            if (!_isGameLoopTest)
            {
                var envScenario = Environment.GetEnvironmentVariable("FIREBASE_GAME_LOOP_SCENARIO");
                if (!string.IsNullOrEmpty(envScenario))
                {
                    _isGameLoopTest = true;
                    int.TryParse(envScenario, out _scenarioNumber);
                    Debug.Log($"[FirebaseTestLab] Detected via environment: scenario={_scenarioNumber}");
                }
            }

            Debug.Log($"[FirebaseTestLab] Init complete: IsGameLoop={_isGameLoopTest}, Scenario={_scenarioNumber}");
        }

        private static void ParseScenarioFromURL(string url)
        {
            try
            {
                var queryStart = url.IndexOf('?');
                if (queryStart < 0) return;

                var query = url.Substring(queryStart + 1);
                var parts = query.Split('&');

                foreach (var part in parts)
                {
                    if (part.StartsWith("scenario=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = part.Substring(9);
                        int.TryParse(value, out _scenarioNumber);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FirebaseTestLab] Failed to parse scenario from URL: {e.Message}");
            }
        }
#endif

            public static void NotifyTestComplete()
        {
#if UNITY_IOS && !UNITY_EDITOR
            Debug.Log("[FirebaseTestLab] Notifying test completion...");

            try
            {
                Application.OpenURL(GameLoopCompleteURL);
                Debug.Log("[FirebaseTestLab] Completion URL opened");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLab] Failed to open completion URL: {e.Message}");
            }
#else
            Debug.Log("[FirebaseTestLab] NotifyTestComplete called (no-op outside iOS device)");
#endif
        }

            public static void WriteResults(string filename, string content)
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                var resultsDir = Path.Combine(Application.persistentDataPath, "GameLoopResults");
                Directory.CreateDirectory(resultsDir);

                var filePath = Path.Combine(resultsDir, filename);
                File.WriteAllText(filePath, content);

                Debug.Log($"[FirebaseTestLab] Results written: {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLab] Failed to write results: {e.Message}");
            }
#endif
        }

            public static void WriteResults(string filename, byte[] data)
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                var resultsDir = Path.Combine(Application.persistentDataPath, "GameLoopResults");
                Directory.CreateDirectory(resultsDir);

                var filePath = Path.Combine(resultsDir, filename);
                File.WriteAllBytes(filePath, data);

                Debug.Log($"[FirebaseTestLab] Binary results written: {filePath} ({data.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLab] Failed to write binary results: {e.Message}");
            }
#endif
        }
    }
}
