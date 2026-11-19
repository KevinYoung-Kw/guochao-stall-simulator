using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class MarketContactRecoveryValidation
    {
        [Serializable] public sealed class Report
        {
            public string scope="Exact r8 player/cart/all-six-NPC spatial fixture. Initial pose restoration only; subsequent normal production NPC Update and PlayerMotor.DriveInput. Not a resumed campaign or two-day pass.";
            public string failure="",source;public bool success,initialPhysicalOverlap,initialSkinOverlap,recovered,finalPoseClear;
            public float originalGap,skinGap,clearanceSkin,secondsUntilClear,normalGameSeconds,playerTravelled,npcTravelled,npcReportedTravelled,minPhysicalGap;
            public int overlapFrames,frames;public List<string> snapshots=new List<string>();
        }
        const string Pending="Guochao.MarketContact.Pending",Source="Guochao.MarketContact.Source",Output="Guochao.MarketContact.Output";
        static IEnumerator running;static Report report;static string output;
        static MarketContactRecoveryValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int si=Array.IndexOf(args,"--market-contact-source"),oi=Array.IndexOf(args,"--market-contact-output");
            if(si<0||oi<0)throw new ArgumentException("Specify source/output");
            SessionState.SetString(Source,args[si+1]);SessionState.SetString(Output,args[oi+1]);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report{source=SessionState.GetString(Source,"")};running=Execute();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}catch(Exception e){report.failure=e.ToString();running=null;Finish();}
        }
        static float Gap(Transform npc,BoxCollider box,float skin)
        {
            var cc=npc.GetComponent<CharacterController>();Vector3 p=npc.position;
            Vector3 lo=p+Vector3.up*(.03f+cc.radius),hi=p+Vector3.up*(.03f+Mathf.Max(cc.radius,cc.height-cc.radius));
            Vector3 half=Vector3.Scale(box.size*.5f,box.transform.lossyScale)+new Vector3(skin,0,skin);
            return Mathf.Sqrt(NpcCapsuleGeometry.SegmentBoxDistanceSquared(lo,hi,box.transform.TransformPoint(box.center),half,box.transform.rotation))-cc.radius;
        }
        static IEnumerator Execute()
        {
            Application.targetFrameRate=60;
            var captured=JsonUtility.FromJson<TraversalValidation.RouteFailureAudit>(File.ReadAllText(report.source));
            var host=new GameObject("CapturedMarketContactFixture");var flow=host.AddComponent<GameFlow>();flow.Run.ReturnToMarket();flow.Initialize();flow.SetPaused(true);
            var w=flow.World;w.Player.enabled=false;yield return null;
            // Explicit scene setup only. Never repeat relocation during recovery.
            w.Player.Teleport(captured.world.playerPosition);w.Player.visual.rotation=captured.world.playerRotation;
            w.ShoppingMotor.RestorePose(captured.world);w.Crowd.RestoreSaveJson(captured.world.crowdJson);Physics.SyncTransforms();
            Transform npc=w.transform.Find("market_customer_5");var box=w.ShoppingMotor.Trolley.GetComponent<BoxCollider>();
            report.clearanceSkin=LinkedTrolleyMotor.ClearanceSkin;report.originalGap=Gap(npc,box,0);report.skinGap=Gap(npc,box,report.clearanceSkin);
            report.initialPhysicalOverlap=report.originalGap<0;report.initialSkinOverlap=report.skinGap<0;report.minPhysicalGap=report.originalGap;
            if(report.initialPhysicalOverlap||!report.initialSkinOverlap)throw new InvalidOperationException("Exact r8 clearance-only contact not reproduced");
            report.snapshots.Add(w.Crowd.CaptureSaveJson());float initialTime=flow.Run.MarketUsed,initialTravel=w.Crowd.TravelledMetres;Vector3[] npcPrevious=w.Crowd.Positions;flow.SetPaused(false);
            float waited=0,deadline=Time.realtimeSinceStartup+35;
            while(waited<8)
            {
                if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Recovery wall deadline");
                float dt=Time.deltaTime;w.Player.DriveInput(Vector2.zero,false,dt);Physics.SyncTransforms();waited+=dt;report.frames++;
                AccumulatePositions(w.Crowd,ref npcPrevious);
                if(w.Crowd.CaptureOverlaps().Length>0)report.overlapFrames++;
                report.minPhysicalGap=Mathf.Min(report.minPhysicalGap,Gap(npc,box,0));
                if(!report.recovered&&w.ShoppingMotor.IsPoseClear(w.Player.transform.position,w.ShoppingMotor.Facing))
                {report.recovered=true;report.secondsUntilClear=waited;report.snapshots.Add(w.Crowd.CaptureSaveJson());break;}
                yield return null;
            }
            if(!report.recovered)throw new InvalidOperationException("NPC did not release the shared cart clearance in 8 normal seconds");
            float driven=0;Vector3 previous=w.Player.transform.position;
            while(driven<3)
            {
                float dt=Time.deltaTime;w.Player.DriveInput(new Vector2(-.70710678f,-.70710678f),false,dt);Physics.SyncTransforms();driven+=dt;report.frames++;
                report.playerTravelled+=Vector3.Distance(previous,w.Player.transform.position);previous=w.Player.transform.position;
                AccumulatePositions(w.Crowd,ref npcPrevious);
                if(w.Crowd.CaptureOverlaps().Length>0)report.overlapFrames++;
                report.minPhysicalGap=Mathf.Min(report.minPhysicalGap,Gap(npc,box,0));yield return null;
            }
            report.npcReportedTravelled=w.Crowd.TravelledMetres-initialTravel;report.normalGameSeconds=flow.Run.MarketUsed-initialTime;
            report.finalPoseClear=w.ShoppingMotor.IsPoseClear(w.Player.transform.position,w.ShoppingMotor.Facing);report.snapshots.Add(w.Crowd.CaptureSaveJson());
            report.success=report.recovered&&report.finalPoseClear&&report.overlapFrames==0&&report.playerTravelled>1&&report.npcTravelled>1;
            flow.SetPaused(true);
        }
        static void AccumulatePositions(CustomerCrowd crowd,ref Vector3[] previous)
        {
            Vector3[] actual=crowd.Positions;
            for(int i=0;i<actual.Length;i++)report.npcTravelled+=Vector3.Distance(previous[i],actual[i]);
            previous=actual;
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("MARKET_CONTACT_RECOVERY_"+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
    }
}
