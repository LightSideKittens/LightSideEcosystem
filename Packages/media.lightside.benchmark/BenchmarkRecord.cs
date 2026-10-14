using System;
using System.Collections;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json.Linq;

namespace LightSide.Benchmark
{
    /// <summary>Ties a measuring coroutine to the result record it fills.</summary>
    public static class BenchmarkRecord
    {
        /// <summary>
        /// Runs <paramref name="routine"/> to its end; a failure is written into <paramref name="record"/> as status
        /// <c>failed</c> with its message, then rethrown to the runner, so a partial record never reads as measured.
        /// </summary>
        public static IEnumerator Guard(IEnumerator routine, JObject record)
        {
            var owned = new OwnedEnumerator(routine);
            Exception failure = null;
            var cleanupFailure = false;
            try
            {
                while (owned.MoveNext(out var current, out failure, out cleanupFailure))
                    yield return current;
            }
            finally
            {
                failure = owned.Dispose(failure, ref cleanupFailure);
            }
            if (failure == null) yield break;
            record["status"] = "failed";
            record["reason"] = failure.Message;
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
