using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class CampaignCounterArrivalValidation
    {
        [Serializable] public sealed class Report
        {
            public string scope="Exact r7 player/cart/NPC spatial fixture, same production DriveInput, same diagnostic Visit and production PrimaryAction/ChangeSupply. Initial default Market inventory fixture; not a resumed legal campaign or two-day acceptance.";
            public string source,failure="",crowdBefore,crowdAfter;public bool success,initiallyBlocked,openedSupplyShop,p05InUnpaidBasket,cashUnchanged;
            public int arrivalTrafficWaits;public float normalGameSeconds,playerDistance;public Vector3 before,after;
        }
        const string Pending="Guochao.CounterArrival.Pending",Source="Guochao.CounterArrival.Source",Output="Guochao.CounterArrival.Output";
        static IEnumerator running;static Report report;static string output;
        static CampaignCounterArrivalValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int si=Array.IndexOf(args,"--counter-arrival-source"),oi=Array.IndexOf(args,"--counter-arrival-output");
            if(si<0||oi<0)throw new ArgumentException("Specify counter arrival source/output");
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
            var host=new GameObject("CapturedCounterArrivalFixture");var flow=host.AddComponent<GameFlow>();flow.Run.ReturnToMarket();flow.Initialize();flow.SetPaused(true);
            var w=flow.World;w.Player.enabled=false;yield return null;
            // Spatial fixture initialization only, before any normal input/waiting.
            w.Player.Teleport(captured.world.playerPosition);w.Player.visual.rotation=captured.world.playerRotation;
            w.ShoppingMotor.RestorePose(captured.world);w.Crowd.RestoreSaveJson(captured.world.crowdJson);Physics.SyncTransforms();
            report.before=w.Player.transform.position;report.crowdBefore=w.Crowd.CaptureSaveJson();report.initiallyBlocked=!flow.IsActionInRange("SupplyCounter");
            if(!report.initiallyBlocked)throw new InvalidOperationException("Captured NPC occlusion not reproduced");
            float initialTime=flow.Run.MarketUsed;int cash=flow.Run.CashCents;flow.SetPaused(false);
            bool completed=false;string error="";
            CampaignPlaythroughValidation.BeginReachProbe(flow,"SupplyCounter",(e,waits)=>{completed=true;error=e;report.arrivalTrafficWaits=waits;});
            float deadline=Time.realtimeSinceStartup+45;
            while(!completed){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Captured arrival probe stalled");yield return null;}
            if(error!="")throw new InvalidOperationException(error);
            if(!flow.IsActionInRange("SupplyCounter"))throw new InvalidOperationException("Actual interaction gate not clear");
            flow.PrimaryAction();report.openedSupplyShop=SupplyShop.IsOpen;
            if(!report.openedSupplyShop)throw new InvalidOperationException("Production PrimaryAction did not open SupplyShop");
            if(!flow.ChangeSupply("P05",false))throw new InvalidOperationException("Actual counter purchase rejected: "+flow.Run.Message);
            report.p05InUnpaidBasket=flow.Run.Cart.Contains("P05")&&!flow.Run.Paid;report.cashUnchanged=flow.Run.CashCents==cash;
            report.after=w.Player.transform.position;report.playerDistance=Vector3.Distance(report.before,report.after);
            report.crowdAfter=w.Crowd.CaptureSaveJson();report.normalGameSeconds=flow.Run.MarketUsed-initialTime;
            if(w.Crowd.CaptureOverlaps().Length>0)throw new InvalidOperationException("Overlap during captured counter arrival");
            report.success=report.p05InUnpaidBasket&&report.cashUnchanged&&report.arrivalTrafficWaits>0&&report.normalGameSeconds>=2;
            SupplyShop.Close();flow.SetPaused(true);
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("COUNTER_ARRIVAL_"+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
    }
}
