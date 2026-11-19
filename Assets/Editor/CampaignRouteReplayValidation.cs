using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class CampaignRouteReplayValidation
    {
        [Serializable] public sealed class Report
        {
            public bool success,exactPoseReconstructed;public string source,failure="",hint;
            public string scope="One read-only search on the exact captured failure scene. Load-time fixture reconstruction only; no gameplay movement, no two-day acceptance claim.";
            public float computeWallSeconds;public Vector3 player;public string target;
        }
        const string Pending="Guochao.RouteReplay.Pending",Source="Guochao.RouteReplay.Source",Output="Guochao.RouteReplay.Output";
        static IEnumerator running;static Report report;static string output;
        static CampaignRouteReplayValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int si=Array.IndexOf(args,"--route-replay-source"),oi=Array.IndexOf(args,"--route-replay-output");
            if(si<0||oi<0)throw new ArgumentException("Specify --route-replay-source and --route-replay-output");
            SessionState.SetString(Source,args[si+1]);SessionState.SetString(Output,args[oi+1]);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report{source=SessionState.GetString(Source,"")};running=Execute();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}catch(Exception error){report.failure=error.ToString();running=null;Finish();}
        }
        static IEnumerator Execute()
        {
            var captured=JsonUtility.FromJson<TraversalValidation.RouteFailureAudit>(File.ReadAllText(report.source));
            var host=new GameObject("CapturedRouteReadOnlyReplay");var flow=host.AddComponent<GameFlow>();flow.enabled=false;
            flow.Run.ReturnToMarket();flow.Initialize();var w=flow.World;w.Player.enabled=false;w.Crowd.enabled=false;yield return null;
            // Exact saved scene hydration, not a path-following shortcut.
            w.Player.Teleport(captured.world.playerPosition);w.Player.visual.rotation=captured.world.playerRotation;
            w.ShoppingMotor.RestorePose(captured.world);w.Crowd.RestoreSaveJson(captured.world.crowdJson);Physics.SyncTransforms();flow.SetPaused(true);
            var compare=new WorldSaveData();w.ShoppingMotor.CapturePose(compare);
            report.exactPoseReconstructed=Vector3.Distance(w.Player.transform.position,captured.world.playerPosition)<.000001f&&
                Vector3.Distance(compare.cartPosition,captured.world.cartPosition)<.000001f&&Quaternion.Angle(compare.cartRotation,captured.world.cartRotation)<.001f;
            if(!report.exactPoseReconstructed)throw new InvalidOperationException("Scene pose was not reconstructed exactly");
            report.player=w.Player.transform.position;report.target=captured.target;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            report.hint=TraversalValidation.ReadOnlyKeyboardHint(flow,captured.target);timer.Stop();report.computeWallSeconds=(float)timer.Elapsed.TotalSeconds;
            report.success=true;
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("CAPTURED_ROUTE_REPLAY_"+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
    }
}
