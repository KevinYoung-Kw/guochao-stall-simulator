using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    [InitializeOnLoad] public static class TrolleyNpcContactValidation
    {
        [Serializable] public sealed class Frame
        {
            public int index;public Vector3 before,predicted,actual,cart,npc;public Quaternion acceptedFacing;
            public string flags;public float predictedActualDistance,nominalDepth,controllerDepth,horizontalSeparation;public Quaternion cartRotation;public Vector3 cartWorldCenter;public bool nominalPenetrates,controllerPenetrates;
            public bool nominalOverlap,cartPoseClear;
        }
        [Serializable] public sealed class Case
        {
            public float dt;public Vector3 npcStart;public int frames,overlapFrames,invalidFinalPoses;
            public float maxNominalDepth,maxControllerDepth,maxPredictedActualDifference,travelled;
            public List<Frame> contactFrames=new List<Frame>();
        }
        [Serializable] public sealed class Report
        {
            public string scope="Actual MarketWorld, actual full shopping-cart BoxCollider, actual worker NPC CharacterController and same PlayerMotor.DriveInput. Explicit initial scene fixtures only, not a two-day economy/playthrough result. No pose resets during movement in each case.";
            public bool initialFixturePosesAssigned=true,movementPoseAssignments,success;public int geometryComparisons,geometryMismatches;
            public string failure="";public float npcRadius,npcHeight;public Vector3 cartSize,cartCenter,cartScale,npcScale,npcCenter;
            public List<Case> cases=new List<Case>();
        }
        const string Pending="Guochao.TrolleyNpcContact.Pending",Output="Guochao.TrolleyNpcContact.Output";
        static IEnumerator running;static string output;static Report report;static GameObject host;
        static TrolleyNpcContactValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--trolley-npc-output");
            string path=at>=0&&at+1<args.Length?args[at+1]:Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v13-implementation/p1/playthrough/trolley-npc-contact.json"));
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();running=Execute();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}
            catch(Exception error){report.failure=error.ToString();running=null;Finish();}
        }
        static IEnumerator Execute()
        {
            host=new GameObject("ActualTrolleyNpcContactFixture");var flow=host.AddComponent<GameFlow>();flow.enabled=false;
            flow.Run.ReturnToMarket();flow.Initialize();var w=flow.World;w.Player.enabled=false;w.Crowd.enabled=false;
            foreach(string id in w.Crowd.InstanceIds)if(id!="market_customer_5")w.transform.Find(id).gameObject.SetActive(false);
            Transform actor=w.transform.Find("market_customer_5");var npc=actor.GetComponent<CharacterController>();
            var trolley=w.ShoppingMotor;var box=trolley.Trolley.GetComponent<BoxCollider>();
            report.npcRadius=npc.radius;report.npcHeight=npc.height;report.cartSize=box.size;report.cartCenter=box.center;report.cartScale=box.transform.lossyScale;report.npcScale=npc.transform.lossyScale;report.npcCenter=npc.center;
            var query=new GameObject("Nominal capsule exact penetration probe");query.transform.SetParent(host.transform);query.layer=2;
            var capsule=query.AddComponent<CapsuleCollider>();capsule.direction=1;capsule.radius=npc.radius;capsule.height=npc.height;capsule.center=npc.center;capsule.isTrigger=true;capsule.enabled=true;
            yield return null;Physics.SyncTransforms();
            var random=new System.Random(13051);
            for(int i=0;i<600;i++)
            {
                Vector3 p=actor.position;Quaternion q=Quaternion.Euler((float)random.NextDouble()*70-35,(float)random.NextDouble()*360,(float)random.NextDouble()*70-35);
                Vector3 pose=p+new Vector3((float)random.NextDouble()*4-2,(float)random.NextDouble()*3-1,(float)random.NextDouble()*4-2);
                bool unity=Physics.ComputePenetration(box,pose,q,capsule,p,actor.rotation,out _,out float depth);
                Vector3 lower=p+Vector3.up*(.03f+npc.radius),upper=p+Vector3.up*(.03f+Mathf.Max(npc.radius,npc.height-npc.radius));
                bool analytic=NpcCapsuleGeometry.IntersectsBox(lower,upper,npc.radius,pose+q*box.center,box.size*.5f,q);
                report.geometryComparisons++;if(unity!=analytic)report.geometryMismatches++;
            }
            foreach(float dt in new[]{1f/120f,1f/60f,1f/30f})
            foreach(float dx in new[]{-.25f,0,.25f})foreach(float dz in new[]{-.25f,0,.25f})
            {
                var c=new Case{dt=dt,npcStart=new Vector3(-9.4f+dx,.005f,-4.9f+dz)};report.cases.Add(c);
                // Staged fixture before the first drive command, never a recovery.
                npc.enabled=false;actor.position=c.npcStart;actor.rotation=Quaternion.identity;npc.enabled=true;
                trolley.ResetPoseForValidation(new Vector3(-6.0229f,.03f,-7.7578f),Quaternion.Euler(0,8.13f,0));
                w.Player.StopMotion();flow.Run.Paused=false;w.Sync(flow.Run);Physics.SyncTransforms();
                for(int i=0;i<Mathf.RoundToInt(3/dt);i++)
                {
                    Vector3 before=w.Player.transform.position;w.Player.DriveInput(Vector2.down,false,dt);Physics.SyncTransforms();
                    Vector3 actual=w.Player.transform.position;float move=Vector3.Distance(actual,before);c.travelled+=move;c.frames++;
                    Vector3 p=actor.position;float radius=npc.radius;
                    Vector3 lo=p+Vector3.up*(.03f+radius),hi=p+Vector3.up*(.03f+Mathf.Max(radius,npc.height-radius));
                    bool overlap=Array.Exists(Physics.OverlapCapsule(lo,hi,radius,1<<25,QueryTriggerInteraction.Collide),v=>v==box);
                    float nominalDepth=0,controllerDepth=0;
                    bool nominalPenetrates=Physics.ComputePenetration(box,box.transform.position,box.transform.rotation,capsule,p,actor.rotation,out _,out nominalDepth);
                    bool controllerPenetrates=Physics.ComputePenetration(box,box.transform.position,box.transform.rotation,npc,p,actor.rotation,out _,out controllerDepth);
                    if(!nominalPenetrates)nominalDepth=0;if(!controllerPenetrates)controllerDepth=0;
                    Vector3 center=box.transform.TransformPoint(box.center),local=Quaternion.Inverse(box.transform.rotation)*(p-center);
                    Vector3 half=Vector3.Scale(box.size*.5f,box.transform.lossyScale);
                    float qx=Mathf.Max(0,Mathf.Abs(local.x)-half.x),qz=Mathf.Max(0,Mathf.Abs(local.z)-half.z);
                    // Both vertical capsule-cylinder span and cart span overlap in
                    // this fixture: exact horizontal oriented-rectangle/disc gap.
                    float horizontalSeparation=Mathf.Sqrt(qx*qx+qz*qz)-radius;
                    bool clear=trolley.IsPoseClear(actual,trolley.Facing);
                    float difference=Vector3.Distance(trolley.LastPredictedPosition,trolley.LastActualPosition);
                    c.maxNominalDepth=Mathf.Max(c.maxNominalDepth,nominalDepth);c.maxControllerDepth=Mathf.Max(c.maxControllerDepth,controllerDepth);
                    c.maxPredictedActualDifference=Mathf.Max(c.maxPredictedActualDifference,difference);
                    if(overlap)c.overlapFrames++;if(!clear)c.invalidFinalPoses++;
                    if(overlap||!clear||difference>.0001f)
                    {
                        if(c.contactFrames.Count<12)c.contactFrames.Add(new Frame{index=i,before=before,predicted=trolley.LastPredictedPosition,actual=actual,
                            cart=box.transform.position,npc=actor.position,acceptedFacing=trolley.LastAcceptedFacing,flags=trolley.LastMoveCollisionFlags.ToString(),
                            predictedActualDistance=difference,nominalDepth=nominalDepth,controllerDepth=controllerDepth,nominalOverlap=overlap,cartPoseClear=clear,nominalPenetrates=nominalPenetrates,controllerPenetrates=controllerPenetrates,horizontalSeparation=horizontalSeparation,cartRotation=box.transform.rotation,cartWorldCenter=center});
                    }
                    // Retain the FIRST exact failing contact; continuing would
                    // only pile up the same invalid initial pose on later inputs.
                    if(overlap||!clear)break;
                    if(i%60==0)yield return null;
                }
            }
            report.success=report.geometryMismatches==0;
            foreach(var c in report.cases)if(c.overlapFrames>0||c.invalidFinalPoses>0||c.maxNominalDepth>0||c.travelled<1||c.travelled>8.35f)report.success=false;
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("TROLLEY_NPC_CONTACT_"+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
