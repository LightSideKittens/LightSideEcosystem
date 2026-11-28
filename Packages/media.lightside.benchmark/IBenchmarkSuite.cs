using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>
    /// One measurable suite the runner can schedule. Implementations are components placed in the
    /// benchmark scene; the runner finds them, runs the ones the launch selection asked for, and folds
    /// what they return into the combined result document.
    /// </summary>
    public interface IBenchmarkSuite
    {
        /// <summary>
        /// Token this suite answers to in <c>-benchmarkSuite</c>, the <c>BENCHMARK_SUITE</c> environment
        /// variable, the WebGL <c>?suite=</c> query and the stream file names. Lowercase, no spaces.
        /// </summary>
        string SuiteId { get; }

        /// <summary>Key the suite's results occupy at the root of the combined result document.</summary>
        string Section { get; }

        /// <summary>Browser global the viewer pushes this suite's runs into.</summary>
        string StreamGlobal { get; }

        /// <summary>
        /// Firebase Test Lab game-loop scenario that selects this suite alone. Scenario 1 is reserved for
        /// the full run; zero opts the suite out of device selection.
        /// </summary>
        int Scenario { get; }

        /// <summary>Standing notes on what the suite measures, merged into the run's <c>phaseNotes</c>.</summary>
        IEnumerable<KeyValuePair<string, string>> PhaseNotes { get; }

        /// <summary>Measures. Every fallible step goes through <see cref="BenchmarkContext.Run"/> so one failed engine cannot abort the rest.</summary>
        IEnumerator Run(BenchmarkContext context);

        /// <summary>The suite's results, or <c>null</c> when it produced none.</summary>
        JObject Serialize();

        /// <summary>
        /// Whether the suite reached at least one measured result. A false verdict fails the whole run and
        /// <paramref name="reason"/> carries what the viewer and CI log should say.
        /// </summary>
        bool Measured(out string reason);
    }

    /// <summary>
    /// A suite whose work can be split into launches of their own. A device run selects one by game-loop scenario number
    /// <c>10000 * <see cref="IBenchmarkSuite.Scenario"/> + 1000 * (repeat + 1) + n</c> with repeat 0 to 8: <c>n</c> 1 to
    /// 499 is a timing launch of <see cref="TimingCases"/>[n - 1], every arm of that case in the one process and no
    /// memory sampling thread; <c>n</c> 501 to 999 is a memory launch of <see cref="MemoryParticipants"/>[n - 501], that
    /// participant alone with the process memory sampled (<see cref="BenchmarkProcessMemory.EnableSampling"/>).
    /// </summary>
    public interface IIsolatedLaunchSuite
    {
        /// <summary>The cases measured for time, one per launch with all their arms; at most 499, the same in every build of the suite.</summary>
        IReadOnlyList<string> TimingCases { get; }

        /// <summary>The participants (arm ids) measured for memory, one per launch; at most 499, the same in every build of the suite.</summary>
        IReadOnlyList<string> MemoryParticipants { get; }

        /// <summary>
        /// The memory participants that are arms of the timing case <paramref name="caseId"/>, so a device run can measure
        /// every launch of one case on one unit; empty for a case whose arms have no memory launch.
        /// </summary>
        IReadOnlyList<string> MemoryParticipantsOf(string caseId);
    }

    /// <summary>
    /// The runner's services, handed to a suite for the length of its run: failure isolation, heap
    /// normalization between engines, the watchdog verdict, and the run's error list.
    /// </summary>
    public sealed class BenchmarkContext
    {
        readonly Func<bool> alive;
        readonly Func<string, Func<IEnumerator>, Action, Action, IEnumerator> run;
        readonly Func<IEnumerator> cooldown;
        readonly Func<IEnumerator> thermalSettle;
        readonly Action<string> error;

        internal BenchmarkContext(Func<bool> alive,
            Func<string, Func<IEnumerator>, Action, Action, IEnumerator> run,
            Func<IEnumerator> cooldown, Action<string> error, JObject config,
            string participant, int repeat, bool isolated, bool measuresMemory, Func<IEnumerator> thermalSettle)
        {
            this.alive = alive;
            this.run = run;
            this.cooldown = cooldown;
            this.thermalSettle = thermalSettle;
            this.error = error;
            Config = config;
            Participant = participant;
            Repeat = repeat;
            Isolated = isolated;
            MeasuresMemory = measuresMemory;
        }

        /// <summary>
        /// What this process measures, or null for everything: in a timing launch a case id (or, in a shared launch, a
        /// case-id prefix), whose arms the suite runs in randomized rounds; in a memory launch one participant's id.
        /// </summary>
        public string Participant { get; }

        /// <summary>
        /// Zero-based index of this process among the repeats of the same measurement. Repeats are what
        /// turn a number into a distribution; a single run cannot say whether a difference exceeds noise.
        /// </summary>
        public int Repeat { get; }

        /// <summary>
        /// Whether this process is an isolated launch of an <see cref="IIsolatedLaunchSuite"/>: <see cref="Participant"/>
        /// names exactly one case or participant, never a group a suite would otherwise expand.
        /// </summary>
        public bool Isolated { get; }

        /// <summary>
        /// Whether this process is a memory launch: its memory is sampled, so its timings carry the samplers' cost and
        /// clock effects and are not timing results.
        /// </summary>
        public bool MeasuresMemory { get; }

        /// <summary>Whether this process measures <paramref name="name"/>.</summary>
        public bool Measures(string name) =>
            Participant == null || string.Equals(Participant, name, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a timing launch measures the case <paramref name="caseId"/>: every case without a participant, the named
        /// case, and in a shared launch also the cases under the participant as a prefix (<c>world</c> selects <c>world.*</c>).
        /// </summary>
        public bool Selects(string caseId) =>
            Measures(caseId) || !Isolated && caseId.StartsWith(Participant + ".", StringComparison.OrdinalIgnoreCase);

        /// <summary>Run parameters published beside the results, shared by every suite in the run.</summary>
        public JObject Config { get; }

        /// <summary>False once the watchdog has fired; a suite yielding past that point only delays the partial write.</summary>
        public bool Alive => alive();

        /// <summary>
        /// Runs one measured step under failure isolation. <paramref name="onComplete"/> collects results
        /// after a clean finish, <paramref name="onFailure"/> after a failed one; a throw inside either the
        /// step or the collector is recorded against the run instead of escaping.
        /// </summary>
        public IEnumerator Run(string name, Func<IEnumerator> step, Action onComplete, Action onFailure = null) =>
            run(name, step, onComplete, onFailure);

        /// <summary>
        /// Normalizes the heap between engines. Without it a later engine inherits the previous one's heap
        /// debt and pending finalizers, which contaminates GC counts and allocation deltas in run order.
        /// </summary>
        public IEnumerator Cooldown() => cooldown();

        /// <summary>
        /// Waits until fixed work costs what it did at the start of the run. Every measured window on a
        /// device should open through this: heat, not the participants, is what makes a long comparison
        /// favor whoever ran first.
        /// </summary>
        public IEnumerator ThermalSettle() => thermalSettle();

        /// <summary>Records a run-level error; a run with any error is reported as failed.</summary>
        public void Error(string message) => error(message);
    }
}
