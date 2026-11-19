using System;
using System.Collections;
using UnityEngine;

namespace Guochao
{
    // Diagnostic-only coroutine host for the FXV3 feedback validation runner.
    // Has no Update and never self-starts: the editor validation creates this
    // component in PlayMode and calls Launch once so the acceptance cases run
    // on real MonoBehaviour.StartCoroutine player frames (yield return null ==
    // one real frame), not on EditorApplication.update editor callbacks.
    // Abort() lets the editor watchdog stop and clean up without DestroyImmediate.
    public sealed class Feedback3CoroutineHost : MonoBehaviour
    {
        Action onCompleted;
        Action<Exception> onError;
        IEnumerator routine;

        public bool IsRunning => routine != null;

        // Dispose error captured by Abort(). The editor watchdog folds it into
        // the failure report (via RequestFinish) instead of letting the host
        // die silently with the cleanup error lost.
        public Exception PendingCleanupError { get; private set; }

        public void Launch(IEnumerator routine, Action onCompleted, Action<Exception> onError)
        {
            this.routine = routine;
            this.onCompleted = onCompleted;
            this.onError = onError;
            StartCoroutine(Run());
        }

        public void Abort()
        {
            StopAllCoroutines();
            var r = routine; routine = null;
            try { (r as IDisposable)?.Dispose(); }
            catch (Exception cleanup) { PendingCleanupError = cleanup; }
        }

        IEnumerator Run()
        {
            var r = routine;
            Exception failure = null;
            // CS1626: no yield inside a try block that has a catch. The
            // MoveNext/Current access and the exception capture happen inside
            // try/catch; the yield itself lives outside, in the loop body.
            try
            {
                while (true)
                {
                    bool hasNext;
                    object current = null;
                    try { hasNext = r.MoveNext(); if (hasNext) current = r.Current; }
                    catch (Exception error) { failure = error; break; }
                    if (!hasNext) break;
                    yield return current; // outside try/catch: legal
                }
            }
            finally
            {
                routine = null;
                // A Dispose that throws must still become the reported
                // failure: record it (never swallow) so onError fires and the
                // editor runner writes a failure report instead of ending with
                // a silently dead host and no result.
                try { (r as IDisposable)?.Dispose(); } // exactly once, success or failure
                catch (Exception cleanup) { failure = failure ?? cleanup; }
            }
            if (failure != null) { onError?.Invoke(failure); yield break; }
            onCompleted?.Invoke();
        }
    }
}
