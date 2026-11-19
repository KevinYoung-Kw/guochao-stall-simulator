using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    public static class CarryCollisionV12Validation
    {
        [Serializable] private sealed class Check
        {public string name,item,detail="";public bool passed;public float forwardTravel,backwardTravel,initialDepth,finalDepth,acceptedYaw;public CarryCollisionAudit hull;}
        [Serializable] private sealed class Report
        {public string schema="guochao.carry.collision.v12",input="Actual production CarryCollisionHull and PlayerMotor.DriveInput with real RiceBox / Aholo B04 model. Physics fixture, not hardware input or pickup-tween validation.";public bool success;public List<Check> checks=new List<Check>();}
        private static readonly List<GameObject> garbage=new List<GameObject>();
        [MenuItem("Guochao/Validate carried-object full sweeps")]
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var report=new Report();
            foreach(string item in new[]{"rice","B04"})
            {
                Execute(report,item,"full-hull-wall-stop-and-reverse",WallAndReverse);
                Execute(report,item,"clear-endpoints-blocked-turn-arc",TurnArc);
                Execute(report,item,"strictly-decreasing-initial-overlap-escape",Escape);
            }
            report.success=report.checks.TrueForAll(x=>x.passed);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v12/carry/carry-collision-validation.json"));
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--carry-output");if(i>=0&&i+1<args.Length)output=Path.GetFullPath(args[i+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log((report.success?"CARRY_COLLISION_PASS ":"CARRY_COLLISION_FAIL ")+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        private static void Execute(Report report,string item,string name,Action<PlayerMotor,Check> work)
        {
            var result=new Check{name=name,item=item};report.checks.Add(result);PlayerMotor player=null;
            try{player=Fixture(item);work(player,result);result.hull=player.CarryHull.CaptureAudit();Need(!result.hull.queryOverflow,"Physics query overflow");result.passed=true;}
            catch(Exception error){result.detail=error.ToString();if(player)result.hull=player.CarryHull.CaptureAudit();}
            finally{foreach(GameObject go in garbage)if(go)UnityEngine.Object.DestroyImmediate(go);garbage.Clear();Physics.SyncTransforms();}
        }
        private static PlayerMotor Fixture(string item)
        {
            Box("Ground",new Vector3(0,-.10f,0),new Vector3(30,.20f,30));
            var root=new GameObject("Actual carried model fixture");garbage.Add(root);root.transform.position=new Vector3(0,.03f,0);
            var visual=new GameObject("Visual wrapper");visual.transform.SetParent(root.transform,false);
            UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Models/Player"),visual.transform,false);
            PlayerMotor player=root.AddComponent<PlayerMotor>();player.Initialize(visual.transform,null);player.enabled=false;player.carrying=true;
            GameObject held;
            if(item=="B04")held=EquipmentVisualFactory.Create("B04",visual.transform);
            else{held=new GameObject("Actual RiceBox");held.transform.SetParent(visual.transform,false);UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Models/RiceBox"),held.transform,false);}
            held.transform.localPosition=item=="B04"?new Vector3(0,.60f,.48f):new Vector3(0,.85f,.50f);
            held.transform.localRotation=Quaternion.identity;player.SetCarriedVisual(held.transform);Physics.SyncTransforms();
            Need(player.CarryHull.Active,"Actual model hull absent");Need(player.CarryHull.CaptureAudit().fullSize.x>.2f,"Real object envelope was reduced");return player;
        }
        private static BoxCollider Box(string name,Vector3 center,Vector3 size)
        {
            var go=new GameObject(name);garbage.Add(go);go.layer=WorldRuntime.EnvironmentLayer;go.transform.position=center;var box=go.AddComponent<BoxCollider>();box.size=size;return box;
        }
        private static void WallAndReverse(PlayerMotor player,Check result)
        {
            Box("Furniture wall",new Vector3(0,1.5f,2),new Vector3(6,3,.12f));Physics.SyncTransforms();
            Vector3 start=player.transform.position;Vector3 size=player.CarryHull.CaptureAudit().fullSize;
            for(int i=0;i<160;i++){player.DriveInput(Vector2.up,false,1f/60);Need(player.CarryHull.CaptureAudit().penetrations.Length==0,"Held model entered wall");}
            result.forwardTravel=player.transform.position.z-start.z;Need(result.forwardTravel>.1f&&result.forwardTravel<1.70f,"Did not stop at carried footprint before body");
            for(int i=0;i<30;i++)player.DriveInput(Vector2.zero,false,1f/60);
            Vector3 beforeReverse=player.transform.position;
            for(int i=0;i<120;i++){player.DriveInput(Vector2.down,false,1f/60);Need(player.CarryHull.CaptureAudit().penetrations.Length==0,"Reverse/turn entered wall");}
            result.backwardTravel=beforeReverse.z-player.transform.position.z;Need(result.backwardTravel>1,"Could not retreat from a resting contact");
            Need(player.CarryHull.CaptureAudit().fullSize==size,"Movement changed held model envelope");
        }
        private static void TurnArc(PlayerMotor player,Check result)
        {
            CarryCollisionAudit hull=player.CarryHull.CaptureAudit();Quaternion end=Quaternion.Euler(0,180,0),middle=Quaternion.Slerp(Quaternion.identity,end,.5f);
            Vector3 local=hull.localCenter;local.z+=hull.fullSize.z*.35f;
            Vector3 post=player.transform.position+middle*local;
            Box("Corner post in swept arc",post,new Vector3(.06f,.40f,.06f));Physics.SyncTransforms();
            Need(player.CarryHull.IsPoseClear(player.transform.position,Quaternion.identity),"Fixture start overlaps post");
            Need(player.CarryHull.IsPoseClear(player.transform.position,end),"Fixture end overlaps post");
            Need(!player.CarryHull.IsPoseClear(player.transform.position,middle),"Blocked middle pose was not detected");
            Need(!player.CarryHull.CanSweep(player.transform.position,Quaternion.identity,player.transform.position,end),"Only end poses checked; swept turn crossed post");
            player.CarryHull.Drive(Vector3.zero,end);result.acceptedYaw=Quaternion.Angle(Quaternion.identity,player.visual.rotation);
            Need(result.acceptedYaw>0&&result.acceptedYaw<179,"Turn did not stop before the post");
            Need(player.CarryHull.CaptureAudit().penetrations.Length==0,"Accepted turn penetrates post");
            player.CarryHull.Drive(Vector3.zero,Quaternion.identity);Need(Quaternion.Angle(player.visual.rotation,Quaternion.identity)<.01f,"Could not turn back away: degrees="+Quaternion.Angle(player.visual.rotation,Quaternion.identity)+" quaternion="+player.visual.rotation);
        }
        private static void Escape(PlayerMotor player,Check result)
        {
            CarryCollisionAudit hull=player.CarryHull.CaptureAudit();float front=player.transform.position.z+hull.localCenter.z+hull.fullSize.z*.5f;
            BoxCollider wall=Box("Shelf initial overlap",new Vector3(0,1.5f,front+.05f-.04f),new Vector3(6,3,.10f));Physics.SyncTransforms();
            float Depth(){return Physics.ComputePenetration(player.CarryCollider,player.CarryCollider.transform.position,player.CarryCollider.transform.rotation,wall,wall.transform.position,wall.transform.rotation,out _,out float depth)?depth:0;}
            result.initialDepth=Depth();Need(result.initialDepth>.03f,"Fixture has no real starting overlap");Vector3 start=player.transform.position;
            player.CarryHull.Drive(new Vector3(0,0,.08f),Quaternion.identity);Need(Vector3.Distance(start,player.transform.position)<.00001f,"Initial overlap allowed further penetration");
            float previous=Depth();
            for(int i=0;i<12;i++)
            {
                player.CarryHull.Drive(new Vector3(0,0,-.01f),Quaternion.identity);float now=Depth();
                Need(now<.00001f||now<previous-.000001f,"Retreat did not strictly reduce penetration");previous=now;
            }
            result.finalDepth=Depth();result.backwardTravel=start.z-player.transform.position.z;Need(result.finalDepth<.00001f&&result.backwardTravel>.10f,"Initial contact trapped player");
            player.CarryHull.Drive(new Vector3(0,0,.30f),Quaternion.identity);Need(Depth()<.00001f,"Escape privilege remained after clear and allowed re-entry");
        }
        private static void Need(bool value,string error){if(!value)throw new InvalidOperationException(error);}
    }
}
