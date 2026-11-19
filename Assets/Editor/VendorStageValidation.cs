using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Runs in Play mode so the real Stage's deferred destruction/creation contract
    // is unchanged. Calls its actual BuildCart and Configure; skips only unrelated
    // environment/customer camera/icon rendering. No actor pose/rig is substituted.
    [InitializeOnLoad] public static class VendorStageValidation
    {
        const string Pending="Guochao.VendorStageValidation.Pending";
        const string Output="Guochao.VendorStageValidation.Output";
        static VendorStageValidation(){EditorApplication.update+=CheckPending;}
        public static void Run()
        {
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v12/review/vendor-stage-validation.txt"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--vendor-stage-output");if(at>=0&&at+1<args.Length)output=Path.GetFullPath(args[at+1]);
            SessionState.SetString(Output,output);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void CheckPending()
        {
            if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            SessionState.SetBool(Pending,false);var log=new StringBuilder();int failures=0;
            log.AppendLine("VENDOR_STAGE_VALIDATION actual StreetStallStage.BuildCart / Configure / PerformInteraction + 52-bone Actor + real attached prep bowl, condiment, package and tools. Manual stage ticks; not formal input or art review.");
            foreach(float standZ in new[]{-.97f})
            {
                var root=new GameObject("RealStagePoseFixture_"+standZ);int caseFailures=0;
                try
                {
                    var stage=root.AddComponent<StreetStallStage>();
                    // The same geometry builder only needs its real materials; no
                    // headless camera.Render/texture readback is needed for bone tests.
                    var paint=typeof(StreetStallStage).GetMethod("Paint",BindingFlags.Instance|BindingFlags.NonPublic);
                    foreach(string field in new[]{"cream","forest","rust","ink","steel","wood","rice","egg","green","sausage","dough","white","flame"})
                    {
                        var material=(Material)paint.Invoke(stage,new object[]{"Fixture_"+field,Color.white,.12f});
                        typeof(StreetStallStage).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(stage,material);
                    }
                    BusinessSetup setup=BusinessSetup.MidgamePractice();
                    typeof(StreetStallStage).GetMethod("BuildCart",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(stage,new object[]{setup});
                    var session=new BusinessSession(setup);Need(session.Open(),"Practice menu did not open");stage.Configure(session);
                    var stations=(IList)typeof(StreetStallStage).GetField("panStations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stage);
                    foreach(object station in stations)
                    {
                        var field=station.GetType().GetField("stand");Vector3 point=(Vector3)field.GetValue(station);point.z=standZ;field.SetValue(station,point);
                    }
                    log.AppendLine("CANDIDATE standZ="+standZ+" lanes="+session.Lanes.Length);
                    for(int lane=0;lane<session.Lanes.Length;lane++)
                    {
                        int recipe=session.Lanes[lane].recipeIndex;
                        string raw=recipe==2?"dough":"rice",pack=recipe==2?"P04":"P05";
                        Test(stage,session,lane,StallInteractionKind.Ingredient,raw,KitchenStep.Idle,log,ref caseFailures);
                        Test(stage,session,lane,StallInteractionKind.Cookware,"",KitchenStep.Utensil,log,ref caseFailures);
                        Test(stage,session,lane,StallInteractionKind.Seasoning,"seasoning",KitchenStep.Season,log,ref caseFailures);
                        Test(stage,session,lane,StallInteractionKind.Packaging,pack,KitchenStep.Pack,log,ref caseFailures);
                        Test(stage,session,lane,StallInteractionKind.Cookware,"",KitchenStep.ReadyStock,log,ref caseFailures);
                        Test(stage,session,lane,StallInteractionKind.Cookware,"",KitchenStep.Dirty,log,ref caseFailures);
                    }
                    try
                    {
                        int cancelledContacts=0,cancelledFinished=0;
                        session.Lanes[0].step=KitchenStep.Idle;
                        stage.PerformInteraction(StallInteractionKind.Ingredient,0,"rice",()=>cancelledContacts++,()=>cancelledFinished++);
                        for(int frame=0;frame<3000&&stage.WorkActorBusy;frame++)
                        {
                            stage.Present(session,1f/120,false);var a=stage.WorkAudit;
                            if(a.action=="PourIngredient"&&a.phase=="Anticipation"&&a.elapsed>.08f)break;
                        }
                        var beforePause=stage.WorkAudit;stage.Present(session,.5f,true);var afterPause=stage.WorkAudit;
                        Need(beforePause.rootPosition==afterPause.rootPosition&&beforePause.rightPalm==afterPause.rightPalm&&beforePause.elapsed==afterPause.elapsed,"Paused carried action changed pose/time");
                        stage.CancelWork();var cancelled=stage.WorkAudit;
                        Need(!stage.WorkActorBusy&&!cancelled.carryingVisualProp&&cancelledContacts==0&&cancelledFinished==0,"Cancel retained work or dispatched business callback");
                        Need(cancelled.rightPalm.y-cancelled.rootPosition.y<.95f,"Cancel must restore resting arm without waiting for another Tick");
                        log.AppendLine("PASS pause/cancel restores standing immediately "+JsonUtility.ToJson(cancelled));
                    }
                    catch(Exception ex){caseFailures++;log.AppendLine("FAIL pause/cancel "+ex.Message);}
                    log.AppendLine("CANDIDATE_RESULT standZ="+standZ+" failures="+caseFailures);
                    if(standZ==-.97f)failures+=caseFailures;
                }
                catch(Exception ex){failures++;log.AppendLine("FAIL fixture standZ="+standZ+" "+ex);}
                finally{UnityEngine.Object.DestroyImmediate(root);}
            }
            log.AppendLine(failures==0?"VENDOR_STAGE_CANDIDATE_PASS":"VENDOR_STAGE_CANDIDATE_FAIL count="+failures);
            string outputPath=SessionState.GetString(Output,"");Directory.CreateDirectory(Path.GetDirectoryName(outputPath));File.WriteAllText(outputPath,log.ToString());Debug.Log(log.ToString());
            if(Application.isBatchMode)EditorApplication.Exit(failures==0?0:1);else EditorApplication.ExitPlaymode();
        }
        private static void Test(StreetStallStage stage,BusinessSession session,int lane,StallInteractionKind kind,string item,KitchenStep step,StringBuilder log,ref int failures)
        {
            try
            {
                session.Lanes[lane].step=step;int contacts=0,finished=0;
                stage.PerformInteraction(kind,lane,item,()=>contacts++,()=>finished++);
                float peak=0;VendorWorkAudit peakAudit=null;
                for(int frame=0;frame<3000&&stage.WorkActorBusy;frame++)
                {
                    stage.Present(session,1f/120,false);VendorWorkAudit sample=stage.WorkAudit;
                    Vector3 position=stage.WorkActorPosition;
                    foreach(Collider solid in Physics.OverlapCapsule(position+Vector3.up*.29f,position+Vector3.up*1.40f,.25f,StreetStallStage.SolidMask,QueryTriggerInteraction.Ignore))
                        Need(false,"Chef body penetrates "+solid.name+" at "+position);
                    float progress=sample.duration>0?sample.elapsed/sample.duration:0;
                    if(progress>=.32f&&progress<=.91f&&Mathf.Max(sample.rightError,sample.leftError)>peak)
                    {peak=Mathf.Max(sample.rightError,sample.leftError);peakAudit=sample;}
                }
                log.AppendLine("WORK_PEAK "+JsonUtility.ToJson(peakAudit));
                VendorWorkAudit audit=stage.WorkAudit;
                log.AppendLine("CASE lane="+lane+" kind="+kind+" step="+step+" contacts="+contacts+" finished="+finished+" "+JsonUtility.ToJson(audit));
                Need(!stage.WorkActorBusy,"Action did not finish in 25 simulated seconds");
                Need(string.IsNullOrEmpty(audit.lastFailure),audit.lastFailure);
                Need(contacts==1&&finished==1,"Stage must dispatch exactly one business contact and one completion");
                Need(audit.maxWorkError<=.025f,"Working trajectory exceeds 2.5cm: "+audit.maxWorkError);
                Need(!audit.carryingVisualProp,"Completed action retained attached visual inventory");
            }
            catch(Exception ex){failures++;log.AppendLine("FAIL lane="+lane+" "+kind+"/"+step+" "+ex.Message);}
        }
        private static void Need(bool value,string error){if(!value)throw new InvalidOperationException(error);}
    }
}
