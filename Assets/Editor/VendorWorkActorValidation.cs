using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    public static class VendorWorkActorValidation
    {
        private static readonly StringBuilder log=new StringBuilder();
        private static int failures;
        private static void Need(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
        [MenuItem("Guochao/Validate real vendor bone contact")]
        public static void Run()
        {
            failures=0;log.Clear();log.AppendLine("VENDOR_WORK_VALIDATION real 52-bone asset + authored clips; deterministic visual/action contract fixtures, not a hardware or art review.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("VendorPoseFixture");
            try
            {
                GameObject source=Resources.Load<GameObject>("Models/Player");Need(source!=null,"Player asset missing");
                var player=new GameObject("Vendor");player.transform.SetParent(root.transform,false);UnityEngine.Object.Instantiate(source,player.transform,false);
                player.transform.position=Vector3.up*.03f;
                Transform origin=Find(player.transform,"PlayerOrigin"),forward=Find(player.transform,"PlayerForward");
                Vector3 authored=forward.position-origin.position;authored.y=0;
                player.transform.rotation=Quaternion.FromToRotation(authored.normalized,Vector3.forward)*player.transform.rotation;
                var actor=root.AddComponent<VendorWorkActor>();actor.automaticTick=false;actor.Bind(player.transform);
                float rightA=Length(player.transform,"RightArm","RightForeArm"),rightB=Length(player.transform,"RightForeArm","RightHand");
                log.AppendLine("RIG shoulderR="+Find(player.transform,"RightArm").position+" shoulderL="+Find(player.transform,"LeftArm").position+" rightUpper="+rightA+" rightFore="+rightB+" rightPalm="+actor.RightPalmPosition+" leftPalm="+actor.LeftPalmPosition);
                foreach(float height in new[]{1.05f,1.15f,1.22f,1.30f,1.35f,1.45f,1.50f})foreach(float extension in new[]{.30f,.35f,.40f,.43f})
                {
                    Transform r=new GameObject("ReachR").transform,l=new GameObject("ReachL").transform;r.SetParent(root.transform,false);l.SetParent(root.transform,false);
                    r.position=player.transform.position+new Vector3(.13f,height,extension);l.position=player.transform.position+new Vector3(0,height,extension);
                    r.rotation=l.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
                    var target=new VendorWorkTarget{standPoint=player.transform.position,workPoint=(r.position+l.position)*.5f,rightGrip=r,leftGrip=l,useLeftHand=true};
                    actor.Perform(VendorWorkAction.StirFry,target,.6f);
                    for(int n=0;n<200&&actor.IsBusy&&!actor.CaptureAudit().contactCommitted;n++)actor.Tick(1f/120);
                    var measured=actor.CaptureAudit();log.AppendLine("REACH height="+height+" forward="+extension+" accepted="+measured.contactCommitted+" rightError="+measured.rightError+" leftError="+measured.leftError);
                    actor.Cancel();actor.Tick(.1f);UnityEngine.Object.DestroyImmediate(r.gameObject);UnityEngine.Object.DestroyImmediate(l.gameObject);
                }
                foreach(VendorWorkAction kind in Enum.GetValues(typeof(VendorWorkAction)))
                {
                    try
                    {
                        var right=new GameObject("RightGrip").transform;right.SetParent(root.transform,false);right.position=new Vector3(.17f,1.10f,.30f);right.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
                        var left=new GameObject("LeftGrip").transform;left.SetParent(root.transform,false);left.position=new Vector3(-.17f,1.10f,.30f);left.rotation=right.rotation;
                        var target=new VendorWorkTarget{standPoint=player.transform.position,workPoint=new Vector3(0,1.10f,.30f),rightGrip=right,leftGrip=left,useLeftHand=true};
                        GameObject tool=null;Vector3 toolRest=Vector3.zero;Quaternion toolRotation=Quaternion.identity;
                        if(kind==VendorWorkAction.StirFry||kind==VendorWorkAction.Scoop||kind==VendorWorkAction.Clean)
                        {
                            tool=GameObject.CreatePrimitive(PrimitiveType.Cube);tool.name="ActualToolHandleFixture";tool.transform.SetParent(root.transform,false);
                            tool.transform.localScale=new Vector3(.025f,.025f,.24f);tool.transform.position=right.position;tool.transform.rotation=right.rotation;
                            var grip=new GameObject("ToolGrip").transform;grip.SetParent(tool.transform,false);grip.localPosition=Vector3.zero;
                            target.heldTool=tool.transform;target.toolGrip=grip;target.rightGrip=grip;toolRest=tool.transform.position;toolRotation=tool.transform.rotation;
                        }
                        int contacts=0,finished=0;float contactTime=-1;
                        Need(actor.Perform(kind,target,.8f,()=>{contacts++;contactTime=actor.CaptureAudit().elapsed;},()=>finished++),"Action rejected");
                        Need(!actor.Perform(kind,target,.8f),"Repeated input reentered active action");
                        for(int n=0;n<240&&actor.Phase!=VendorWorkPhase.Anticipation&&actor.IsBusy;n++)actor.Tick(1f/120);
                        actor.Tick(.12f);
                        VendorWorkAudit before=actor.CaptureAudit();Vector3 palm=actor.RightPalmPosition,position=player.transform.position;
                        actor.Tick(.6f,true);VendorWorkAudit frozen=actor.CaptureAudit();
                        Need(frozen.elapsed==before.elapsed&&actor.RightPalmPosition==palm&&player.transform.position==position&&contacts==0,"Pause advanced pose/contact");
                        for(int n=0;n<240&&actor.IsBusy;n++)
                        {
                            actor.Tick(1f/120);var audit=actor.CaptureAudit();
                            if(tool!=null&&audit.contactCommitted&&audit.elapsed<.8f*.91f)
                                Need(Vector3.Distance(target.toolGrip.position,actor.RightPalmPosition)<.002f,"Visible tool detached from palm");
                        }
                        VendorWorkAudit result=actor.CaptureAudit();
                        log.AppendLine("ACTION "+kind+" "+JsonUtility.ToJson(result));
                        Need(actor.LastFailure=="",actor.LastFailure);Need(contacts==1&&finished==1,"Contact/completion callback was not exactly once");
                        Need(Mathf.Abs(contactTime-.8f*VendorWorkActor.CommitFraction(kind))<.0001f,"Contact timestamp drifted");
                        Need(Mathf.Abs(Length(player.transform,"RightArm","RightForeArm")-rightA)<.0001f&&Mathf.Abs(Length(player.transform,"RightForeArm","RightHand")-rightB)<.0001f,"IK changed arm lengths");
                        if(tool!=null){Need(Vector3.Distance(tool.transform.position,toolRest)<.0001f&&Quaternion.Angle(tool.transform.rotation,toolRotation)<.01f,"Tool was not returned after recovery");UnityEngine.Object.DestroyImmediate(tool);}
                        actor.Tick(.5f);Need(contacts==1,"Idle repeated contact");log.AppendLine("PASS "+kind);
                        UnityEngine.Object.DestroyImmediate(right.gameObject);UnityEngine.Object.DestroyImmediate(left.gameObject);
                    }
                    catch(Exception ex){failures++;log.AppendLine("FAIL "+kind+" "+ex.Message);actor.Cancel();}
                }
                try
                {
                    int contacts=0,finished=0;
                    var far=new VendorWorkTarget{standPoint=player.transform.position,workPoint=new Vector3(0,1.1f,2)};
                    Need(actor.Perform(VendorWorkAction.PickIngredient,far,.6f,()=>contacts++,()=>finished++),"Unreachable fixture did not start");
                    for(int n=0;n<400&&actor.IsBusy;n++)actor.Tick(1f/120);
                    Need(actor.Phase==VendorWorkPhase.Failed&&contacts==0&&finished==1,"Out-of-reach action committed or left busy");
                    log.AppendLine("PASS unreachable rejects before transaction "+actor.LastFailure);actor.Cancel();
                    Vector3 start=player.transform.position,goal=start+new Vector3(.8f,0,.8f);float maxStep=0;
                    Need(actor.MoveTo(goal,goal+Vector3.forward,null,new[]{start+Vector3.right*.8f}),"Move rejected");
                    for(int n=0;n<800&&actor.IsBusy;n++)
                    {Vector3 previous=player.transform.position;actor.Tick(1f/120);maxStep=Mathf.Max(maxStep,Vector3.Distance(previous,player.transform.position));}
                    Need(!actor.IsBusy&&Vector3.Distance(player.transform.position,goal)<.04f,"Authored waypoint walk did not arrive");
                    Need(maxStep<=actor.walkSpeed/120+.001f,"Root jumped beyond bounded walking speed");
                    Need(actor.CaptureAudit().maxPlantedFootError<.03f,"Planted foot error exceeds 3 cm: "+actor.CaptureAudit().maxPlantedFootError+" "+actor.CaptureAudit().footErrorDetail);
                    log.AppendLine("PASS waypoint walk maxStep="+maxStep+" "+JsonUtility.ToJson(actor.CaptureAudit()));
                }
                catch(Exception ex){failures++;log.AppendLine("FAIL rejection/walk "+ex.Message);}
                try
                {
                    actor.Cancel();actor.Tick(.1f);
                    var prop=new GameObject("IngredientBowlDisplay").transform;prop.SetParent(root.transform,false);prop.position=actor.RightPalmPosition;prop.rotation=actor.RightPalmRotation;
                    var grip=new GameObject("PalmGrip").transform;grip.SetParent(prop,false);
                    Need(actor.AttachVisualProp(prop,grip),"Visual prop did not attach at the actual palm");
                    Vector3 goal=player.transform.position+Vector3.right*.65f;
                    Need(actor.MoveTo(goal,goal+Vector3.forward),"Carried walking rejected");
                    for(int n=0;n<800&&actor.IsBusy;n++)
                    {actor.Tick(1f/120);Need(Vector3.Distance(grip.position,actor.RightPalmPosition)<.002f,"Carried prop detached while walking");}
                    Need(!actor.IsBusy&&actor.CaptureAudit().carryingVisualProp,"Carry walk did not retain prop");
                    var frozen=actor.CaptureAudit();Vector3 propAt=prop.position;actor.Tick(.5f,true);
                    Need(prop.position==propAt&&actor.CaptureAudit().rootPosition==frozen.rootPosition,"Paused prop or feet moved");
                    log.AppendLine("CARRY_WALK "+JsonUtility.ToJson(actor.CaptureAudit()));
                    actor.Cancel();Need(!prop.gameObject.activeSelf&&!actor.CaptureAudit().carryingVisualProp,"Cancel failed to hide/release visual prop");
                    UnityEngine.Object.DestroyImmediate(prop.gameObject);log.AppendLine("PASS carried prop follows real palm / paused / cancelled");
                }
                catch(Exception ex){failures++;log.AppendLine("FAIL carried prop "+ex.Message);}

            }
            catch(Exception ex){failures++;log.AppendLine("FAIL binding "+ex);}
            finally{UnityEngine.Object.DestroyImmediate(root);}
            log.AppendLine(failures==0?"VENDOR_WORK_PASS":"VENDOR_WORK_FAIL count="+failures);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v11/review/vendor-work-validation.txt"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--vendor-output");if(at>=0&&at+1<args.Length)output=Path.GetFullPath(args[at+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,log.ToString());Debug.Log(log.ToString());
            if(Application.isBatchMode)EditorApplication.Exit(failures==0?0:1);
        }
        private static Transform Find(Transform root,string suffix)
        {foreach(Transform child in root.GetComponentsInChildren<Transform>(true))if(child.name==suffix||child.name.EndsWith(":"+suffix,StringComparison.Ordinal)||child.name=="mixamorig"+suffix)return child;throw new InvalidOperationException("Missing "+suffix);}
        private static float Length(Transform root,string a,string b)=>Vector3.Distance(Find(root,a).position,Find(root,b).position);
    }
}
