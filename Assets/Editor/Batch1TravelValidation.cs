using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Regression for the real SceneTransition.PlayTravel skip/cancel path.
    // SkipTravel is the button/Space handler on SceneTransition; it is invoked
    // here via Reflection (explicitly NOT synthetic key/mouse events), so the
    // tested code path is the production one. Mid-travel is reached by calling
    // CinematicTravelStage.Advance directly (no real 60s waits); full-duration
    // behaviour belongs to CinematicTravelAutoplayValidation.
    [InitializeOnLoad] public static class Batch1TravelValidation
    {
        [Serializable] public sealed class Case
        {
            public string name; public float duration;
            public bool pass, expectFinish = true; public int skipCalls, arrival, finished;
            public string[] errors = new string[0];
        }
        [Serializable] public sealed class Report
        {
            public string scope = "Production SceneTransition.PlayTravel with private SkipTravel invoked via Reflection (button handler path, not key/mouse injection, not CinematicTravelStage.SkipToEnd alone). Durations 11.25/15/21; mid-travel advanced via CinematicTravelStage.Advance.";
            public bool success; public string failure = "";
            public List<Case> cases = new List<Case>();
            public string[] pending = {
                "Pause guard (Esc blocks skip while GameFlow paused) needs a real GameFlow instance — pending root on-machine verification.",
                "Full-duration timing/driving has CinematicTravelAutoplayValidation; this file only covers skip/cancel semantics."
            };
        }
        const string PendingKey = "Guochao.Batch1Travel.Pending", OutputKey = "Guochao.Batch1Travel.Output";
        static Runner runner; static Report report; static double deadline;
        static Batch1TravelValidation() { EditorApplication.update += Advance; }
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--batch1-travel-output");
            if (at < 0) throw new ArgumentException("Specify --batch1-travel-output");
            SessionState.SetString(OutputKey, args[at + 1]);
            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
            if (!SessionState.GetBool(PendingKey, false))
            {
                if(runner!=null&&!runner.done&&EditorApplication.timeSinceStartup>deadline){report.failure="Travel validation exceeded 90s wall deadline";runner.done=true;}
                if (runner != null && runner.done) Finish();
                return;
            }
            SessionState.SetBool(PendingKey, false);
            report = new Report();
            deadline=EditorApplication.timeSinceStartup+90;
            Application.logMessageReceived+=CaptureError;
            runner = new GameObject("Batch1TravelValidation").AddComponent<Runner>();
            runner.StartCoroutine(runner.RunAll(report));
        }
        static void Finish()
        {
            string path = SessionState.GetString(OutputKey, "");
            Application.logMessageReceived-=CaptureError;
            report.success = report.failure.Length == 0&&report.cases.Count==3;
            foreach (var c in report.cases) report.success &= c.pass && c.errors.Length == 0 && c.arrival==(c.expectFinish?1:0) && c.finished==(c.expectFinish?1:0);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception e) { report.success = false; report.failure += e; }
            Debug.Log("BATCH1_TRAVEL_" + (report.success ? "PASS" : "FAIL") + " " + path + " " + report.failure);
            EditorApplication.Exit(report.success ? 0 : 1);
        }
        static void CaptureError(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){report.failure+=message+"\n"+stack;if(runner!=null)runner.done=true;}}

        public sealed class Runner : MonoBehaviour
        {
            public bool done;
            static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            const double PhaseTimeoutSeconds = 5.0, QuietObservationSeconds = 0.6;
            public IEnumerator RunAll(Report report)
            {
                var c1 = new Case { name = "skip-mid-overlay-active", duration = 15f };
                yield return RunCase(c1, SkipMid);
                report.cases.Add(c1);
                var c2 = new Case { name = "double-skip-once", duration = 11.25f };
                yield return RunCase(c2, DoubleSkip);
                report.cases.Add(c2);
                var c3 = new Case { name = "cancel-no-arrival", duration = 21f };
                yield return RunCase(c3, CancelMid);
                report.cases.Add(c3);
                done = true;
            }
            delegate void MidAction(SceneTransition st, CinematicTravelStage t, Case c);
            Camera expectedCamera;
            IEnumerator RunCase(Case c, MidAction mid)
            {
                var go = new GameObject("TransitionUnderTest");
                var st = go.AddComponent<SceneTransition>();
                var cameraObject=new GameObject("Travel original camera fixture");cameraObject.tag="MainCamera";cameraObject.AddComponent<Camera>();
                int arrival = 0, finished = 0; expectedCamera = null;
                try
                {
                    st.PlayTravel(true, () => arrival++, () => finished++, null, c.duration);
                    var t = (CinematicTravelStage)typeof(SceneTransition).GetField("travel", F).GetValue(st);
                    // -nographics can run hundreds of frames before the real 0.30s fade
                    // has elapsed. Bound each wait by wall time, not frame throughput.
                    double phaseDeadline = EditorApplication.timeSinceStartup + PhaseTimeoutSeconds;
                    while (t == null && EditorApplication.timeSinceStartup < phaseDeadline)
                    {
                        yield return null;
                        t = (CinematicTravelStage)typeof(SceneTransition).GetField("travel", F).GetValue(st);
                    }
                    if (t == null) Add(c, "timeout waiting for travel stage to start (5s wall time)");
                    else
                    {
                        t.Advance(c.duration * 0.5f, false); // jump to mid-travel, no real waiting
                        yield return null; // let the production transition update its fade alpha
                        expectedCamera = (Camera)typeof(CinematicTravelStage).GetField("originalCamera", F).GetValue(t);
                        mid(st, t, c);
                    }
                    phaseDeadline = EditorApplication.timeSinceStartup + PhaseTimeoutSeconds;
                    while (c.expectFinish && finished == 0 && EditorApplication.timeSinceStartup < phaseDeadline)
                        yield return null;
                    if (c.expectFinish && finished == 0) Add(c, "timeout waiting for finished callback (5s wall time)");
                    // Observe longer than the real 0.45s arrival fade so a cancelled
                    // continuation or repeated completion cannot pass after two fast frames.
                    double quietUntil = EditorApplication.timeSinceStartup + QuietObservationSeconds;
                    while (EditorApplication.timeSinceStartup < quietUntil) yield return null;
                    c.arrival = arrival; c.finished = finished;
                    if(arrival!=(c.expectFinish?1:0)||finished!=(c.expectFinish?1:0))Add(c,"callback count mismatch: arrival="+arrival+", finished="+finished);
                    var travelNow = (CinematicTravelStage)typeof(SceneTransition).GetField("travel", F).GetValue(st);
                    if (arrival > 0 && travelNow != null) Add(c, "travel stage not released after finish");
                    if (expectedCamera != null && !expectedCamera.enabled) Add(c, "original camera not restored after travel released");
                    if (c.errors.Length == 0) c.pass = true;
                }
                finally { DestroyImmediate(go);DestroyImmediate(cameraObject); }
            }
            void SkipMid(SceneTransition st, CinematicTravelStage t, Case c)
            {
                var group = (CanvasGroup)typeof(SceneTransition).GetField("group", F).GetValue(st);
                if (!Mathf.Approximately(group.alpha, 0f)) Add(c, "fade group alpha=" + group.alpha + ", expected 0 mid-travel");
                var button = (UnityEngine.UI.Button)typeof(SceneTransition).GetField("skipButton", F).GetValue(st);
                if (button == null) { Add(c, "skip overlay not created"); return; }
                var overlay = button.transform.parent;
                if (!overlay.gameObject.activeSelf) Add(c, "skip overlay canvas inactive mid-travel");
                if (!Mathf.Approximately(overlay.GetComponent<CanvasGroup>().alpha, 1f)) Add(c, "skip overlay not opaque mid-travel");
                if (!button.interactable) Add(c, "skip button not interactable mid-travel");
                Skip(st); c.skipCalls = 1;
            }
            void DoubleSkip(SceneTransition st, CinematicTravelStage t, Case c)
            {
                Skip(st); Skip(st); c.skipCalls = 2;
            }
            void CancelMid(SceneTransition st, CinematicTravelStage t, Case c)
            {
                c.expectFinish = false;
                st.Cancel();
                var travelNow = (CinematicTravelStage)typeof(SceneTransition).GetField("travel", F).GetValue(st);
                if (travelNow != null) Add(c, "travel stage not released after Cancel");
            }
            static void Skip(SceneTransition st)
            {
                typeof(SceneTransition).GetMethod("SkipTravel", F).Invoke(st, null); // production button/Space handler, no synthetic input
            }
            static void Add(Case c, string error)
            {
                var list = new List<string>(c.errors) { error };
                c.errors = list.ToArray();
            }
        }
    }
}
