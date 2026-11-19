using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // STAFFV15 entry: creates a real empty PlayMode scene, enters playmode and lets
    // the runtime host (MarketStaffV15Host, scheduled via MonoBehaviour.StartCoroutine)
    // perform the acceptance on real frames. This class only starts the run and
    // enforces a global 180 s watchdog; it never drives test logic from
    // EditorApplication.update and never treats a yielded null as a frame.
    public static class MarketStaffV15Validation
    {
        const string PendingKey="Guochao.MarketStaffV15.Pending",OutputKey="Guochao.MarketStaffV15.Output";
        const double WatchdogSeconds=180;
        static double startedAt=-1,failRequestedAt=-1;
        static MarketStaffV15Validation(){EnsureSubscribed();}
        [InitializeOnLoadMethod] // survives EnterPlaymode domain reloads; plain static ctor does not
        static void EnsureSubscribed(){EditorApplication.update-=Watchdog;EditorApplication.update+=Watchdog;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();
            int at=Array.IndexOf(args,"--marketstaff-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --marketstaff-output <new report.json>");
            SessionState.SetString(OutputKey,Path.GetFullPath(args[at+1]));SessionState.SetBool(PendingKey,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        static void Watchdog()
        {
            if(SessionState.GetBool(PendingKey,false)&&EditorApplication.isPlaying&&!EditorApplication.isPaused)
            {
                SessionState.SetBool(PendingKey,false);startedAt=EditorApplication.timeSinceStartup;
                MarketStaffV15Host.Finished=false;MarketStaffV15Host.ExitCode=1;
            }
            if(startedAt<0||!EditorApplication.isPlaying)return;
            if(MarketStaffV15Host.Finished)
            {
                int code=MarketStaffV15Host.ExitCode;startedAt=-1;failRequestedAt=-1;
                Debug.Log("MARKETSTAFF_V15_EXIT "+code);
                if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode();
                return;
            }
            if(failRequestedAt>=0)
            {
                // Grace period already given to the host for its own cleanup/report.
                if(EditorApplication.timeSinceStartup-failRequestedAt>10)
                {
                    Debug.LogError("MARKETSTAFF_V15_WATCHDOG_CLEANUP_STALLED");
                    startedAt=-1;failRequestedAt=-1;
                    if(Application.isBatchMode)EditorApplication.Exit(1);else EditorApplication.ExitPlaymode();
                }
                return;
            }
            if(EditorApplication.timeSinceStartup-startedAt>WatchdogSeconds)
            {
                // Only request host-side failure cleanup; the host keeps its error
                // listener, disposes its step chain, destroys its own flowRoot and
                // writes the full real report once. No minimal JSON is written here,
                // so genuine errors captured so far are never overwritten.
                MarketStaffV15Host.FailureRequested="Global 180s watchdog exceeded in editor runner.";
                failRequestedAt=EditorApplication.timeSinceStartup;
                Debug.LogError("MARKETSTAFF_V15_WATCHDOG_TIMEOUT cleanup requested");
            }
        }
    }
}
