using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide.Benchmark
{
    /// <summary>
    /// Firebase Test Lab support for Android game-loop tests.
    /// Based on official Google implementation:
    /// https://github.com/googlecodelabs/unity-firebase-test-lab-game-loop
    /// </summary>
    public static class FirebaseTestLabAndroid
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [DllImport("c")]
        private static extern int dup(int fd);

        [DllImport("c")]
        private static extern int write(int fd, byte[] buf, int count);

        [DllImport("c", EntryPoint = "write")]
        private static extern int write_ptr(int fd, IntPtr buf, int count);

        private static AndroidJavaObject activity;
        private static AndroidJavaObject intent;
        private static int logFileDescriptor = -1;
        private static bool initialized;
#endif

        /// <summary>Game-loop scenario from the launch Intent (0 when not a game-loop run or before <see cref="Initialize"/>). The benchmark suite mapping is 1 = all, 2 = text pipeline, 3 = glyph rasterization.</summary>
        public static int ScenarioNumber { get; private set; }

        /// <summary>
        /// Reads the game-loop launch intent once: the scenario and the descriptor of the results file. The
        /// writes and <see cref="NotifyTestComplete"/> call it themselves; call it early only to read
        /// <see cref="ScenarioNumber"/> before the run.
        /// </summary>
        public static void Initialize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (initialized) return;
            initialized = true;

            try
            {
                Debug.Log("[FirebaseTestLabAndroid] Initializing...");

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    intent = activity.Call<AndroidJavaObject>("getIntent");
                }

                var action = intent.Call<string>("getAction");
                Debug.Log($"[FirebaseTestLabAndroid] Intent action: {action}");

                if (action != "com.google.intent.action.TEST_LOOP")
                {
                    Debug.Log("[FirebaseTestLabAndroid] Not a game-loop test, skipping initialization");
                    return;
                }

                var scenario = intent.Call<int>("getIntExtra", "scenario", 0);
                ScenarioNumber = scenario;
                Debug.Log($"[FirebaseTestLabAndroid] Scenario: {scenario}");

                var logFileUri = intent.Call<AndroidJavaObject>("getData");
                if (logFileUri != null)
                {
                    var encodedPath = logFileUri.Call<string>("getEncodedPath");
                    Debug.Log($"[FirebaseTestLabAndroid] Log file URI: {encodedPath}");

                    var fd = activity
                        .Call<AndroidJavaObject>("getContentResolver")
                        .Call<AndroidJavaObject>("openAssetFileDescriptor", logFileUri, "w")
                        .Call<AndroidJavaObject>("getParcelFileDescriptor")
                        .Call<int>("getFd");

                    Debug.Log($"[FirebaseTestLabAndroid] Original fd: {fd}");

                    logFileDescriptor = dup(fd);
                    Debug.Log($"[FirebaseTestLabAndroid] Duplicated fd: {logFileDescriptor}");
                }
                else
                {
                    Debug.LogWarning("[FirebaseTestLabAndroid] No log file URI in Intent data");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Initialization failed: {e.Message}\n{e.StackTrace}");
            }
#endif
        }

        /// <summary>
        /// Writes test results to Firebase Test Lab.
        /// Uses the file descriptor from Intent if available, otherwise falls back to local file.
        /// </summary>
        public static void WriteResults(string filename, string content)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Initialize();
            try
            {
                var backupDir = Path.Combine(Application.persistentDataPath, "GameLoopResults");
                Directory.CreateDirectory(backupDir);
                var backupPath = Path.Combine(backupDir, filename);
                File.WriteAllText(backupPath, content);
                Debug.Log($"[FirebaseTestLabAndroid] Local backup written: {backupPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Failed to write local backup: {e.Message}");
            }

            if (logFileDescriptor > 0)
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                    var bytesWritten = write(logFileDescriptor, bytes, bytes.Length);
                    Debug.Log($"[FirebaseTestLabAndroid] Written {bytesWritten} bytes to Firebase fd {logFileDescriptor}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FirebaseTestLabAndroid] Failed to write to fd: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[FirebaseTestLabAndroid] No Firebase file descriptor available");
            }
#endif
        }

        /// <summary>
        /// Writes a single binary archive to the game-loop output file. Android game-loop collects only
        /// one output file (the intent fd), so screenshots and results are bundled into one zip here and
        /// unpacked in CI. Loops on write() because the fd may short-write a multi-MB buffer.
        /// </summary>
        public static void WriteResultsArchive(byte[] data, string backupName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Initialize();
            try
            {
                var backupDir = Path.Combine(Application.persistentDataPath, "GameLoopResults");
                Directory.CreateDirectory(backupDir);
                File.WriteAllBytes(Path.Combine(backupDir, backupName), data);
                Debug.Log($"[FirebaseTestLabAndroid] Local backup written: {backupName} ({data.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Failed to write local backup: {e.Message}");
            }

            if (logFileDescriptor <= 0)
            {
                Debug.LogWarning("[FirebaseTestLabAndroid] No Firebase file descriptor; archive only in local backup");
                return;
            }

            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = handle.AddrOfPinnedObject();
                int total = 0;
                while (total < data.Length)
                {
                    int n = write_ptr(logFileDescriptor, IntPtr.Add(basePtr, total), data.Length - total);
                    if (n <= 0) break;
                    total += n;
                }
                Debug.Log($"[FirebaseTestLabAndroid] Wrote {total}/{data.Length} archive bytes to fd {logFileDescriptor}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Failed to write archive to fd: {e.Message}");
            }
            finally { handle.Free(); }
#endif
        }

        /// <summary>
        /// Signals test completion to Firebase Test Lab by finishing the activity, then kills its own
        /// process. All results must be written beforehand. The kill lands before the player's native
        /// deinitialization, so engine shutdown defects cannot reach the device's crash record; a
        /// self-SIGKILL leaves no tombstone. Does not return.
        /// </summary>
        public static void NotifyTestComplete()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Initialize();
            Debug.Log("[FirebaseTestLabAndroid] Test complete, finishing activity and exiting...");
            try
            {
                activity?.Call("finish");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Failed to finish activity: {e.Message}");
            }

            try
            {
                using var process = new AndroidJavaClass("android.os.Process");
                process.CallStatic("killProcess", process.CallStatic<int>("myPid"));
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseTestLabAndroid] Failed to kill process: {e.Message}");
            }
#endif
        }
    }
}
