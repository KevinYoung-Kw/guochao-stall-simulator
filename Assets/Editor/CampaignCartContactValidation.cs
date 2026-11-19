using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class CampaignCartContactValidation
    {
        [Serializable] public sealed class Report
        {
            public string scope="Exact r9 spatial fixture plus explicitly reconstructed paid basket/profile from its report. Initial fixture state only. Same diagnostic Visit and production PrimaryAction; not a resumed legal campaign or two-day pass.";
            public string source,failure="";public bool success,exactPose,wrapperMatchesCurrentPosition,actualGate,loaded,cashUnchanged;
            public int oldAcceptedGoals,newAcceptedGoals,trafficWaits,basketBefore,basketAfter,cargoAfter,cashBefore,cashAfter;
            public Vector3 before,after,liveContact,candidateContact;public float oldContactDistanceAtAnchor,candidateContactDistanceAtAnchor,normalGameSeconds;
        }
        const string Pending="Guochao.CartContact.Pending",Source="Guochao.CartContact.Source",RunReport="Guochao.CartContact.RunReport",Output="Guochao.CartContact.Output";
        static IEnumerator running;static Report report;static string output,runReport;
        static CampaignCartContactValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int si=Array.IndexOf(args,"--cart-contact-source"),ri=Array.IndexOf(args,"--cart-contact-run-report"),oi=Array.IndexOf(args,"--cart-contact-output");
            if(si<0||ri<0||oi<0)throw new ArgumentException("Specify cart contact source, run report and output");
            SessionState.SetString(Source,args[si+1]);SessionState.SetString(RunReport,args[ri+1]);SessionState.SetString(Output,args[oi+1]);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");runReport=SessionState.GetString(RunReport,"");report=new Report{source=SessionState.GetString(Source,"")};running=Execute();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}catch(Exception e){report.failure=e.ToString();running=null;Finish();}
        }
        static IEnumerator Execute()
        {
            var captured=JsonUtility.FromJson<TraversalValidation.RouteFailureAudit>(File.ReadAllText(report.source));
            var native=JsonUtility.FromJson<CampaignPlaythroughValidation.Report>(File.ReadAllText(runReport));var last=native.checkpoints[native.checkpoints.Count-1];
            var profile=new RunProfile{careerId="career2",selectedTraits=new[]{"耳背"},debtDifficulty="标准"};
            RunSaveData data=new RunState(4,profile).CaptureSave();data.phase=GamePhase.Market;data.cashCents=last.cash;data.spentCents=profile.InitialCashCents-last.cash;
            data.remaining=last.remaining;data.marketUsed=last.marketUsed;data.paid=true;data.stoveLoaded=true;data.cart=last.cart;data.cargo=last.cargo;
            data.homeEquipment=new[]{"B03","E03"};data.loadedEquipment=last.loaded;
            var host=new GameObject("CapturedCartContactFixture");var flow=host.AddComponent<GameFlow>();
            // Fixture state construction precedes world creation. These values
            // are reported, never presented as earned in this short test.
            typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,RunState.Restore(data));
            flow.Initialize();flow.SetPaused(true);var w=flow.World;w.Player.enabled=false;yield return null;
            w.Player.Teleport(captured.world.playerPosition);w.Player.visual.rotation=captured.world.playerRotation;
            w.ShoppingMotor.RestorePose(captured.world);w.Crowd.RestoreSaveJson(captured.world.crowdJson);Physics.SyncTransforms();
            report.before=w.Player.transform.position;report.exactPose=Vector3.Distance(report.before,captured.world.playerPosition)<1e-6f;
            foreach(var g in captured.goals)if(g.focusAccepted&&g.bodyClear&&g.clearCartHeadings>0)report.oldAcceptedGoals++;
            var audit=JsonUtility.FromJson<TraversalValidation.RouteFailureAudit>(TraversalValidation.CaptureRouteFailure(flow,"MarketCart"));
            File.WriteAllText(Path.ChangeExtension(output,"new-goals.json"),JsonUtility.ToJson(audit,true));
            foreach(var g in audit.goals)if(g.focusAccepted&&g.bodyClear&&g.clearCartHeadings>0)report.newAcceptedGoals++;
            w.TryInteractionContact("MarketCart",out report.liveContact);w.TryInteractionContactAt("MarketCart",report.before,out Vector3 actual);
            report.wrapperMatchesCurrentPosition=Vector3.Distance(actual,report.liveContact)<1e-6f;
            Vector3 anchor=w.Anchor("MarketCart");w.TryInteractionContactAt("MarketCart",anchor,out report.candidateContact);
            Vector3 oldDelta=report.liveContact-anchor,newDelta=report.candidateContact-anchor;oldDelta.y=0;newDelta.y=0;
            report.oldContactDistanceAtAnchor=oldDelta.magnitude;report.candidateContactDistanceAtAnchor=newDelta.magnitude;
            if(!report.exactPose||!report.wrapperMatchesCurrentPosition||report.newAcceptedGoals==0)throw new InvalidOperationException("Candidate contact goals still unavailable");
            report.basketBefore=flow.Run.Cart.Count;report.cashBefore=flow.Run.CashCents;float time=flow.Run.MarketUsed;
            bool done=false;string error="";flow.SetPaused(false);
            CampaignPlaythroughValidation.BeginReachProbe(flow,"MarketCart",(e,waits)=>{done=true;error=e;report.trafficWaits=waits;});
            float deadline=Time.realtimeSinceStartup+100;
            while(!done){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Real loading approach exceeded wall limit");yield return null;}
            if(error!="")throw new InvalidOperationException(error);
            report.actualGate=flow.IsActionInRange("MarketCart")&&flow.State.targetId=="MarketCart"&&flow.State.primaryEnabled;
            if(!report.actualGate)throw new InvalidOperationException("Actual loading gate not satisfied");
            flow.PrimaryAction();yield return null;float finish=Time.realtimeSinceStartup+8;
            while(flow.IsBusy){if(Time.realtimeSinceStartup>finish)throw new InvalidOperationException("Loading contact did not complete");yield return null;}
            report.after=w.Player.transform.position;report.basketAfter=flow.Run.Cart.Count;report.cargoAfter=flow.Run.Cargo.Count;report.loaded=flow.Run.Loaded;
            report.cashAfter=flow.Run.CashCents;report.cashUnchanged=report.cashAfter==report.cashBefore;report.normalGameSeconds=flow.Run.MarketUsed-time;
            report.success=report.actualGate&&report.loaded&&report.basketAfter==0&&report.cargoAfter==report.basketBefore&&report.cashUnchanged&&w.Crowd.CaptureOverlaps().Length==0;
            flow.SetPaused(true);
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("CART_CONTACT_"+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
    }
}
