using System;
using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.LowLevel;

namespace LightSide.Benchmark
{
    /// <summary>
    /// The CPU clock state a timing run measures under, set from <c>-benchmarkClock</c> before the run starts: <c>none</c>
    /// leaves the governor alone; <c>hold:&lt;fraction&gt;</c> runs a fixed amount of arithmetic at the start of every
    /// frame's Update, sized at calibration to that fraction of the refresh interval; <c>hint:&lt;target ms&gt;</c> opens an
    /// Android Performance Hint session on the main thread with that target work duration and reports every frame's
    /// update and canvas work to it.
    /// </summary>
    /// <remarks>
    /// The hold is work inside the update window: every frame records its time as
    /// <see cref="BenchmarkFrameProbe.LastHoldMilliseconds"/>, and it heats the device as work does. The calibration prices
    /// <see cref="Busy"/> at the clock settled when <see cref="Start"/> runs, so <see cref="Iterations"/> converts a
    /// duration at that clock into a fixed amount of work.
    /// </remarks>
    public static class BenchmarkClock
    {
        struct ClockMarker { }

        const double CalibrationWarmupMilliseconds = 100d;
        const int CalibrationProbes = 8;
        const long CalibrationIterations = 200_000;

        static double holdFraction;
        static double hintTargetMilliseconds;
        static long holdIterations;
        static double nanosecondsPerIteration;
        static double refreshIntervalMilliseconds;
        static bool started;
        static bool installed;
        static double sink;

        /// <summary>The configured mode: <c>none</c>, <c>hold</c> or <c>hint</c>.</summary>
        public static string Mode { get; private set; } = "none";

        /// <summary>Whether <see cref="Start"/> priced the work of <see cref="Busy"/>.</summary>
        public static bool Calibrated => nanosecondsPerIteration > 0;

        /// <summary>Sets the mode from a <c>-benchmarkClock</c> value; null, empty or <c>none</c> leaves the clock free.</summary>
        /// <exception cref="InvalidOperationException">The clock is running (<see cref="Start"/> without <see cref="Stop"/>).</exception>
        /// <exception cref="ArgumentException">The value names no mode, or its argument is not a positive number (a hold fraction must stay below 1).</exception>
        public static void Configure(string value)
        {
            if (started) throw new InvalidOperationException("The clock mode cannot change while the clock runs.");
            if (string.IsNullOrEmpty(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                Mode = "none";
                return;
            }
            var separator = value.IndexOf(':');
            var name = separator < 0 ? value : value.Substring(0, separator);
            var argument = separator < 0 ? null : value.Substring(separator + 1);
            if (!double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new ArgumentException($"Clock mode '{value}' needs a positive number after the colon: hold:<fraction> or hint:<target ms>.");
            switch (name.ToLowerInvariant())
            {
                case "hold":
                    if (number >= 1)
                        throw new ArgumentException($"A hold fraction must stay below one refresh interval; '{value}' does not.");
                    Mode = "hold";
                    holdFraction = number;
                    break;
                case "hint":
                    Mode = "hint";
                    hintTargetMilliseconds = number;
                    break;
                default:
                    throw new ArgumentException($"Unknown clock mode '{value}': use none, hold:<fraction> or hint:<target ms>.");
            }
        }

        /// <summary>Prices <see cref="Busy"/> at the current clock and starts the configured mode; call once the clock has settled.</summary>
        /// <exception cref="InvalidOperationException">The clock is already running.</exception>
        /// <exception cref="NotSupportedException">The mode is <c>hint</c> and the device opens no Performance Hint session.</exception>
        public static void Start()
        {
            if (started) throw new InvalidOperationException("The clock is already running.");
            started = true;
            refreshIntervalMilliseconds = RefreshIntervalMilliseconds();
            Calibrate();
            BenchmarkFrameProbe.Install();
            if (Mode == "none") return;
            if (Mode == "hold") holdIterations = Iterations(holdFraction * refreshIntervalMilliseconds);
            else OpenHintSession();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            BenchmarkFrameProbe.Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update),
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), false, typeof(ClockMarker), Tick);
            PlayerLoop.SetPlayerLoop(loop);
            installed = true;
        }

        /// <summary>Removes the per-frame step and closes the hint session; the calibration stays until the next <see cref="Start"/>.</summary>
        public static void Stop()
        {
            started = false;
            failure = null;
            if (installed)
            {
                var loop = PlayerLoop.GetCurrentPlayerLoop();
                BenchmarkFrameProbe.Remove(ref loop, typeof(UnityEngine.PlayerLoop.Update), typeof(ClockMarker));
                PlayerLoop.SetPlayerLoop(loop);
                installed = false;
            }
            CloseHintSession();
        }

        /// <summary>Throws when the per-frame step failed since <see cref="Start"/>; measured windows must not outlive such a failure.</summary>
        /// <exception cref="InvalidOperationException">The step failed.</exception>
        public static void ThrowOnFailure()
        {
            if (failure != null) throw new InvalidOperationException(failure);
        }

        static string failure;

        /// <summary>Iterations of <see cref="Busy"/> that take <paramref name="milliseconds"/> at the calibrated clock.</summary>
        /// <exception cref="InvalidOperationException"><see cref="Start"/> has not run.</exception>
        public static long Iterations(double milliseconds)
        {
            if (!Calibrated) throw new InvalidOperationException("The clock is not calibrated: BenchmarkClock.Start has not run in this process.");
            return Math.Max(1L, (long)Math.Round(milliseconds * 1e6 / nanosecondsPerIteration));
        }

        /// <summary>Milliseconds the hold takes per frame at the calibrated clock; zero unless the mode is <c>hold</c>.</summary>
        public static double HoldMilliseconds => Mode == "hold" && Calibrated ? Milliseconds(holdIterations) : 0d;

        /// <summary>Milliseconds <paramref name="iterations"/> of <see cref="Busy"/> took at the calibrated clock.</summary>
        /// <exception cref="InvalidOperationException"><see cref="Start"/> has not run.</exception>
        public static double Milliseconds(long iterations)
        {
            if (!Calibrated) throw new InvalidOperationException("The clock is not calibrated: BenchmarkClock.Start has not run in this process.");
            return iterations * nanosecondsPerIteration / 1e6;
        }

        /// <summary>Fixed arithmetic work whose cost changes only with the clock and the core it runs on.</summary>
        public static void Busy(long iterations)
        {
            var accumulator = 0d;
            for (long i = 1; i <= iterations; i++)
                accumulator += 1d / i;
            sink = accumulator;
        }

        /// <summary>The mode with its parameters and the calibration, as results publish it.</summary>
        public static JObject Describe()
        {
            var description = new JObject
            {
                ["mode"] = Mode,
                ["refreshIntervalMs"] = Math.Round(refreshIntervalMilliseconds, 3),
                ["nanosecondsPerIteration"] = Math.Round(nanosecondsPerIteration, 4)
            };
            if (Mode == "hold")
            {
                description["holdFraction"] = holdFraction;
                description["holdIterations"] = holdIterations;
                description["holdMsAtCalibration"] = Math.Round(holdIterations * nanosecondsPerIteration / 1e6, 3);
            }
            if (Mode == "hint") description["hintTargetMs"] = hintTargetMilliseconds;
            return description;
        }

        static void Calibrate()
        {
            var warmupEnd = Stopwatch.GetTimestamp() + (long)(CalibrationWarmupMilliseconds * Stopwatch.Frequency / 1000d);
            while (Stopwatch.GetTimestamp() < warmupEnd)
                Busy(CalibrationIterations);
            var fastest = double.MaxValue;
            for (var i = 0; i < CalibrationProbes; i++)
            {
                var begin = Stopwatch.GetTimestamp();
                Busy(CalibrationIterations);
                fastest = Math.Min(fastest, (Stopwatch.GetTimestamp() - begin) * 1e9 / Stopwatch.Frequency);
            }
            nanosecondsPerIteration = fastest / CalibrationIterations;
        }

        static double RefreshIntervalMilliseconds()
        {
            var hertz = Screen.currentResolution.refreshRateRatio.value;
            if (!(hertz > 0)) throw new InvalidOperationException("The display reports no refresh rate, so a hold cannot be sized against its interval.");
            return 1000d / hertz;
        }

        static void Tick()
        {
            if (Mode == "hold")
            {
                var begin = Stopwatch.GetTimestamp();
                Busy(holdIterations);
                BenchmarkFrameProbe.AddHold((Stopwatch.GetTimestamp() - begin) * 1000d / Stopwatch.Frequency);
                return;
            }
            ReportWork(BenchmarkFrameProbe.LastMilliseconds + BenchmarkFrameProbe.LastCanvasMilliseconds);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject hintSession;
        static IntPtr reportMethod;
        static readonly jvalue[] reportArguments = new jvalue[1];

        static void OpenHintSession()
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            var apiLevel = version.GetStatic<int>("SDK_INT");
            if (apiLevel < 31)
                throw new NotSupportedException($"A Performance Hint session needs Android 12 (API 31); this device runs API {apiLevel}.");
            using var context = new AndroidJavaClass("android.content.Context");
            var serviceName = context.GetStatic<string>("PERFORMANCE_HINT_SERVICE");
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var manager = activity.Call<AndroidJavaObject>("getSystemService", serviceName);
            if (manager == null)
                throw new NotSupportedException("The device offers no PerformanceHintManager.");
            using var process = new AndroidJavaClass("android.os.Process");
            var mainThread = process.CallStatic<int>("myTid");
            var targetNanoseconds = (long)Math.Round(hintTargetMilliseconds * 1e6);
            hintSession = manager.Call<AndroidJavaObject>("createHintSession", new[] { mainThread }, targetNanoseconds);
            if (hintSession == null)
                throw new NotSupportedException("The device refused a Performance Hint session for the main thread.");
            var sessionClass = AndroidJNI.GetObjectClass(hintSession.GetRawObject());
            reportMethod = AndroidJNI.GetMethodID(sessionClass, "reportActualWorkDuration", "(J)V");
            AndroidJNI.DeleteLocalRef(sessionClass);
        }

        static void ReportWork(double milliseconds)
        {
            if (failure != null) return;
            reportArguments[0].j = Math.Max(1L, (long)(milliseconds * 1e6));
            AndroidJNI.CallVoidMethod(hintSession.GetRawObject(), reportMethod, reportArguments);
            var exception = AndroidJNI.ExceptionOccurred();
            if (exception == IntPtr.Zero) return;
            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(exception);
            failure = "The Performance Hint session rejected a work report; the clock state of the frames after it is unknown.";
        }

        static void CloseHintSession()
        {
            if (hintSession == null) return;
            hintSession.Call("close");
            hintSession.Dispose();
            hintSession = null;
            reportMethod = IntPtr.Zero;
        }
#else
        static void OpenHintSession() =>
            throw new NotSupportedException("A Performance Hint session exists only in Android players.");

        static void ReportWork(double milliseconds) { }

        static void CloseHintSession() { }
#endif
    }
}
