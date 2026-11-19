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
    // Real Stage + physical NPC arrival + Controller intent/contact path. Commands
    // have bounded human-scale cadence, but this remains a scripted benchmark.
    [InitializeOnLoad] public static class BusinessThroughputValidation
    {
        [Serializable] public sealed class GuestSnapshot { public float elapsed; public StreetStallStage.GuestNavigationAudit[] guests; public string[] overlaps; }
        [Serializable] public sealed class Case
        {
            public string skill,failure="";
            public int seed,decisions,timedOutWaiting,timedOutInService,closingOrders,actorFailures,totalArrived,notArrivedOver20AtEnd;
            public float duration,maxArrivalDelay,customerIntervalSeconds,maxObservedUnarrivedSeconds; public int[] delayedArrivalIds; public GuestSnapshot[] guestSnapshots;
            public float reactionSeconds,customerLossFraction,servedPerMinute,pathMetres,maxWorkError,maxPlantedFootError,maxMovingFootError,maxWorkingFootError;
            public string worstFootDetail="";
            public bool success,pauseClockAndPosePassed,invariantsPassed,closingStatePassed,throughputTargetApplies=true;
            public BusinessAuditState final;
            public BusinessWorkAudit[] work;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.business.throughput.v12",input="Scripted explicit object commands with decision cadence; real 3D Stage movement, NPC arrival, collisions, IK contact and Controller clock. Not hardware input or user success-rate measurement.";
            public bool success;public Case[] cases;
        }
        const string Pending="Guochao.Throughput.Pending",Output="Guochao.Throughput.Output",Stress="Guochao.Throughput.Stress";
        static IEnumerator running;static readonly List<Case> completed=new List<Case>();static string output;static bool stressOnly;
        static BusinessThroughputValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v12/throughput/throughput.json"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--throughput-output");if(at>=0&&at+1<args.Length)path=Path.GetFullPath(args[at+1]);
            SessionState.SetString(Output,path);SessionState.SetBool(Stress,Array.IndexOf(args,"--throughput-stress-only")>=0);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");stressOnly=SessionState.GetBool(Stress,false);completed.Clear();running=RunCases();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}
            catch(Exception error){Debug.LogException(error);completed.Add(new Case{skill="fixture",failure=error.ToString()});running=null;Finish();}
        }
        static IEnumerator RunCases()
        {
            CheckStateContracts();
            CheckSelectionContext();yield return null;
            // Exercise the formerly deadlocked long run before accepting short rounds.
            var specs=new List<Vector3>{new Vector3(12017,.8f,600)};if(!stressOnly)foreach(float cadence in new[]{.3f,.8f})foreach(int seed in new[]{12017,12029,12041})specs.Add(new Vector3(seed,cadence,300));
            foreach(Vector3 spec in specs)
            {
                int seed=(int)spec.x;float cadence=spec.y;var snapshots=new List<GuestSnapshot>();
                Case result=new Case{seed=seed,duration=spec.z,skill=cadence<.5f?"熟练指令节奏":"普通指令节奏",reactionSeconds=cadence,throughputTargetApplies=!stressOnly};
                var host=new GameObject("RealThroughput_"+seed+"_"+cadence);
                BusinessController controller=null;StreetStallStage stage=null;BusinessSession session=null;
                bool initialized=false;
                try
                {
                    var setup=BusinessSetup.MidgamePractice();setup.seed=seed;setup.dayDuration=spec.z;
                    if(stressOnly)setup.customerIntervalSeconds=10;result.customerIntervalSeconds=setup.customerIntervalSeconds;
                    if(spec.z>300){setup.gasPoints*=2;foreach(string key in new List<string>(setup.inventory.Keys))setup.inventory[key]*=2;} // Explicit long-run fixture stock, never granted during gameplay.
                    controller=host.AddComponent<BusinessController>();controller.enabled=false;
                    stage=host.AddComponent<StreetStallStage>();stage.Initialize(setup,null);
                    session=new BusinessSession(setup);Need(session.Open(),"Could not open two-pan midgame fixture");
                    stage.Configure(session);
                    typeof(BusinessController).GetProperty("Session").SetValue(controller,session);
                    typeof(BusinessController).GetProperty("Stage").SetValue(controller,stage);
                    stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);
                    stage.WorkFailed+=reason=>{result.actorFailures++;Debug.LogWarning("THROUGHPUT_ACTOR_FAILURE "+reason);};
                    initialized=true;
                }
                catch(Exception ex){result.failure="initialize: "+ex;}
                // Let deferred primitive trigger destruction finish before the
                // first physical query; never omit the real Stage solid layer.
                yield return null;
                if(initialized)
                {
                    float nextDecision=0,nextSnapshot=0;int frame=0,arrivalCountAtClearing=-1;bool sawLastCall=false,sawClearing=false;
                    while(!session.Finished&&frame<(int)(spec.z*60)+100&&result.failure=="")
                    {
                        try
                        {
                            Physics.SyncTransforms();
                            if(session.Elapsed>=nextDecision)
                            {Drive(controller);result.decisions++;nextDecision=session.Elapsed+cadence;}
                            controller.AdvanceSimulation(1f/60f);
                            VendorWorkAudit actor=stage.WorkAudit;
                            result.maxWorkError=Mathf.Max(result.maxWorkError,actor.maxWorkError);
                            if(actor.maxPlantedFootError>result.maxPlantedFootError){result.maxPlantedFootError=actor.maxPlantedFootError;result.worstFootDetail=actor.footErrorDetail;}
                            result.maxMovingFootError=Mathf.Max(result.maxMovingFootError,actor.maxMovingFootError);result.maxWorkingFootError=Mathf.Max(result.maxWorkingFootError,actor.maxWorkingFootError);
                            Need(actor.maxWorkError<=.0251f,"Working hand trajectory exceeds 2.5 cm: "+actor.maxWorkError);
                            Need(actor.maxWorkingFootError<=.0301f,"Stationary work foot exceeds 3 cm: "+actor.maxWorkingFootError+" "+actor.footErrorDetail);
                            if(session.Elapsed>=nextSnapshot)
                            {
                                var guests=stage.CaptureGuestAudit();string[] overlaps=stage.CaptureGuestOverlaps();
                                snapshots.Add(new GuestSnapshot{elapsed=session.Elapsed,guests=guests,overlaps=overlaps});nextSnapshot=session.Elapsed+10;
                                Need(overlaps.Length==0,"Customer bodies overlap: "+string.Join(";",overlaps));
                            }
                            BusinessAuditState audit=session.CaptureAudit();
                            Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeInventory&&audit.nonNegativeEnergy,"Money/package/stock/energy conservation failed");
                            foreach(var recipe in audit.recipeProduction)Need(recipe.portionsConserved,"Batch portions not conserved: "+recipe.recipe);
                            sawLastCall|=session.ServicePhase==BusinessServicePhase.LastCall;
                            if(session.ServicePhase==BusinessServicePhase.Clearing)
                            {
                                sawClearing=true;if(arrivalCountAtClearing<0)arrivalCountAtClearing=session.ArrivalAttempts;
                                Need(!session.AcceptingOrders&&session.ArrivalAttempts==arrivalCountAtClearing,"New arrivals after last-order cutoff");
                            }
                            Need(result.actorFailures<2,"Stopped after two actor failures, without blind retries");
                            if(!result.pauseClockAndPosePassed&&session.Elapsed>=30&&stage.WorkActorBusy)
                            {
                                float time=session.Elapsed;Vector3 position=stage.WorkActorPosition,palm=actor.rightPalm;float actionTime=actor.elapsed;
                                controller.AdvanceSimulation(2,true);
                                Need(session.Elapsed==time&&stage.WorkActorPosition==position&&stage.WorkAudit.rightPalm==palm&&stage.WorkAudit.elapsed==actionTime,"Paused full-stage clock/pose changed");
                                result.pauseClockAndPosePassed=true;
                            }
                        }
                        catch(Exception ex){result.failure=ex.ToString();}
                        frame++;if(frame%180==0)yield return null;
                    }
                    result.invariantsPassed=result.failure=="";result.closingStatePassed=sawLastCall&&sawClearing&&session.ServicePhase==BusinessServicePhase.Closed;
                    result.final=controller.CaptureAudit();result.work=controller.CaptureWorkHistory();result.pathMetres=stage.WorkAudit.pathMetres;
                    snapshots.Add(new GuestSnapshot{elapsed=session.Elapsed,guests=stage.CaptureGuestAudit(),overlaps=stage.CaptureGuestOverlaps()});result.guestSnapshots=snapshots.ToArray();
                    var delayed=new HashSet<int>();foreach(var snapshot in snapshots)foreach(var guest in snapshot.guests)if(!guest.arrived)
                    {result.maxObservedUnarrivedSeconds=Mathf.Max(result.maxObservedUnarrivedSeconds,guest.waitingToArriveSeconds);if(guest.waitingToArriveSeconds>20)delayed.Add(guest.id);}
                    result.delayedArrivalIds=new List<int>(delayed).ToArray();
                    foreach(var order in session.Orders)
                    {if(order.arrived){result.totalArrived++;result.maxArrivalDelay=Mathf.Max(result.maxArrivalDelay,order.arrivedAt-order.spawnedAt);}else if(session.Elapsed-order.spawnedAt>20)result.notArrivedOver20AtEnd++;}
                    foreach(var order in session.Orders)
                    {if(order.departureReason=="patience_waiting")result.timedOutWaiting++;else if(order.departureReason=="patience_during_service")result.timedOutInService++;else if(order.departureReason=="closing")result.closingOrders++;}
                    int demand=session.Served+session.Lost+session.QueueTurnaways;
                    result.customerLossFraction=demand>0?(session.Lost+session.QueueTurnaways)/(float)demand:0;
                    result.servedPerMinute=session.Elapsed>0?session.Served*60/session.Elapsed:0;
                    float limit=cadence<.5f?.10f:.20f;
                    foreach(var order in session.Orders)if(order.spawnedAt>=session.LastOrderAt)result.closingStatePassed=false;
                    result.success=result.failure==""&&session.Finished&&result.pauseClockAndPosePassed&&result.closingStatePassed&&result.notArrivedOver20AtEnd==0&&(!result.throughputTargetApplies||result.customerLossFraction<=limit);
                    if(result.failure==""&&!result.success)result.failure="Throughput target not met: loss="+result.customerLossFraction.ToString("P1")+" limit="+(result.throughputTargetApplies?limit.ToString("P0"):"not applied to crowd stress")+"; served="+session.Served+" timedOut="+(result.timedOutWaiting+result.timedOutInService)+" closing="+result.closingOrders+" queueTurnaways="+session.QueueTurnaways+" unarrivedOver20="+result.notArrivedOver20AtEnd;
                    Debug.Log("THROUGHPUT_CASE "+JsonUtility.ToJson(result));
                }
                completed.Add(result);if(stage!=null)stage.Release();UnityEngine.Object.DestroyImmediate(host);yield return null;
                // Geometry failures repeated within a case already invalidate the
                // same fixture; do not burn five further seeds proving it again.
                if(result.actorFailures>=2||!initialized||result.failure!=""&&!result.failure.StartsWith("Throughput target",StringComparison.Ordinal))yield break;
            }
        }
        static void CheckStateContracts()
        {
            var setup=BusinessSetup.MidgamePractice();setup.dayDuration=60;setup.customerIntervalSeconds=1;
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();session.SelectedRecipes.Add(0);Need(session.Open(),"Contract fixture could not open");
            while(session.Elapsed<10)session.Tick(.25f);
            Need(session.ServicePhase==BusinessServicePhase.LastCall&&session.AcceptingOrders,"Last-call notice must precede cutoff");
            while(session.Elapsed<25)session.Tick(.25f);
            int demand=session.ArrivalAttempts;Need(session.ServicePhase==BusinessServicePhase.Clearing&&!session.AcceptingOrders,"Clear-down must stop new demand");
            StallOrder order=session.Orders[0];Need(session.MarkCustomerArrived(order.id)&&session.BeginOrder(order.id,0),"Already admitted order cannot start during clear-down");
            while(!session.Finished)session.Tick(.25f);
            Need(session.ArrivalAttempts==demand&&session.Elapsed==60&&session.ServicePhase==BusinessServicePhase.Closed,"Clear-down changed closing time or spawned demand");
            int lost=session.Lost;Need(lost==session.Orders.Count&&lost>0,"Unfinished admitted orders were hidden at closing");session.Close();Need(session.Lost==lost,"Closing counted loss twice");

            setup=BusinessSetup.MidgamePractice();setup.customerIntervalSeconds=1;
            session=new BusinessSession(setup);session.SelectedRecipes.Clear();session.SelectedRecipes.Add(0);Need(session.Open(),"Reservation fixture could not open");
            session.Tick(1);order=session.Orders[0];session.MarkCustomerArrived(order.id);
            int rice=session.Count("rice"),gas=session.Gas,cash=session.CashCents;
            Need(session.ReserveOrder(order.id,0),"Initial reservation failed");
            session.ReleaseOrderReservation(order.id);
            Need(order.reservedLane==-1&&session.Lanes[0].reservedOrderId==-1&&session.Count("rice")==rice&&session.Gas==gas&&session.CashCents==cash,"Cancelled pickup altered resources or left reservation");
            Need(session.BeginOrder(order.id,0),"Could not begin reserved fixture order");
            while(session.Lanes[0].step!=KitchenStep.Utensil)session.Tick(1f/60f);
            Need(session.Action(0)&&session.Action(0)&&session.Serve(0,"P05"),"Could not create genuine cooked batch remainder");
            Need(session.Lanes[0].step==KitchenStep.ReadyStock&&session.Lanes[0].batchRemaining==1,"Expected one cooked remainder");
            StallOrder second=session.Orders.Find(x=>!x.served&&!x.departed);Need(second!=null,"No second order in reservation fixture");session.MarkCustomerArrived(second.id);
            rice=session.Count("rice");gas=session.Gas;cash=session.CashCents;
            Need(session.ReserveOrder(second.id,0),"Cooked remainder reservation failed");session.ReleaseOrderReservation(second.id);
            Need(second.reservedLane==-1&&session.Lanes[0].reservedOrderId==-1&&session.Lanes[0].batchRemaining==1&&session.Count("rice")==rice&&session.Gas==gas&&session.CashCents==cash,"Cancelled cooked reservation changed batch or resource state");
            Need(session.ReserveOrder(second.id,0),"Could not re-reserve cooked remainder");second.patience=.01f;session.Tick(.02f);
            Need(second.departed&&second.reservedLane==-1&&session.Lanes[0].reservedOrderId==-1&&session.Lanes[0].batchRemaining==1,"Expired reserved order retained claim or consumed food");
            Debug.Log("BUSINESS_STATE_CONTRACTS_PASS last-call, clear-down, fixed closing, cancelled/expired batch reservations");
        }
        static void CheckSelectionContext()
        {
            var setup=BusinessSetup.MidgamePractice();setup.seed=12041;
            var session=new BusinessSession(setup);Need(session.Open(),"Context fixture could not open");session.Tick(1);
            StallOrder first=session.Orders[0];Need(first.recipeIndex==0,"Expected explicit seeded first rice order");session.MarkCustomerArrived(first.id);Need(session.BeginOrder(first.id,0),"Could not start first fixture pan");
            while(session.Lanes[0].step!=KitchenStep.Utensil)session.Tick(1f/60f);
            Need(session.Action(0)&&session.Action(0),"Could not bring first pan to packaging");
            while(session.Orders.Count<2)session.Tick(1f/60f);
            StallOrder next=session.Orders[1];Need(next.recipeIndex==2,"Expected explicit seeded second flatbread order");session.MarkCustomerArrived(next.id);
            var host=new GameObject("CrossPanSelectionContract");StreetStallStage stage=null;
            try
            {
                BusinessController controller=host.AddComponent<BusinessController>();controller.enabled=false;
                stage=host.AddComponent<StreetStallStage>();stage.Initialize(setup,null);stage.Configure(session);
                typeof(BusinessController).GetProperty("Session").SetValue(controller,session);typeof(BusinessController).GetProperty("Stage").SetValue(controller,stage);
                int dough=session.Count("dough"),package=session.Count("P05"),gas=session.Gas;
                Need(controller.SelectOrder(next.id)&&controller.UsePackage(0,"P05"),"Could not select second order then package first pan");
                Need(controller.SelectedOrderId==next.id&&controller.SelectedLaneIndex==0&&controller.SelectedOrderLaneIndex==1,"Tool focus and selected-order recipe were not independent");
                Need(controller.UseIngredient("dough")&&controller.HasPendingIntent(StallInteractionKind.Ingredient,1)&&next.reservedLane==1,"Selected-order ingredient was sent to previously operated pan");
                Need(session.Count("dough")==dough&&session.Count("P05")==package&&session.Gas==gas,"Queued cross-pan selection debited before contact");
                controller.CancelPendingWork();Need(next.reservedLane<0&&session.Lanes[1].reservedOrderId<0,"Cancelling cross-pan pickup left order reservation");
                Debug.Log("BUSINESS_SELECTION_CONTEXT_PASS select-next-order, package-other-pan, pickup-selected-recipe, cancel-reservation");
            }
            finally{if(stage!=null)stage.Release();UnityEngine.Object.DestroyImmediate(host);}
        }
        static void Drive(BusinessController c)
        {
            if(!c.CanInteract||c.PendingWorkCount>=3)return;BusinessSession s=c.Session;
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Utensil&&!c.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){c.UseCookware(lane.index);return;}
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Season&&!c.HasPendingIntent(StallInteractionKind.Seasoning,lane.index)){c.UseSeasoning(lane.index);return;}
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Pack&&!c.HasPendingIntent(StallInteractionKind.Packaging,lane.index))
            {foreach(string package in BusinessSession.Recipes[lane.recipeIndex].packages)if(s.Count(package)>0){c.UsePackage(lane.index,package);return;}}
            foreach(var lane in s.Lanes)if((lane.step==KitchenStep.Heating||lane.step==KitchenStep.Prep)&&!c.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){c.UseCookware(lane.index);return;}
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Dirty&&!c.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){c.UseCookware(lane.index);return;}
            StallOrder best=null;KitchenLane pan=null;
            foreach(var order in s.Orders)
            {
                if(!order.arrived||order.served||order.departed||order.lane>=0||order.reservedLane>=0)continue;
                foreach(var lane in s.Lanes)
                {
                    if(lane.recipeIndex!=order.recipeIndex||lane.reservedOrderId>=0||(lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.ReadyStock))continue;
                    if(best==null||order.patience<best.patience){best=order;pan=lane;}
                }
            }
            if(best==null)return;
            // Selecting and acting are separate explicit decisions/clicks.
            if(c.SelectedOrderId!=best.id){c.SelectOrder(best.id);return;}
            if(pan.step==KitchenStep.ReadyStock){c.UseCookware(pan.index);return;}
            foreach(string raw in BusinessSession.Recipes[best.recipeIndex].ingredients.Keys)if(raw!="seasoning"){c.UseIngredient(raw);return;}
        }
        static void Finish()
        {
            bool passed=completed.Count==(stressOnly?1:7);foreach(Case result in completed)passed&=result.success;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(new Report{success=passed,cases=completed.ToArray()},true));
            Debug.Log((passed?"BUSINESS_THROUGHPUT_PASS ":"BUSINESS_THROUGHPUT_FAIL ")+output);
            if(Application.isBatchMode)EditorApplication.Exit(passed?0:1);else EditorApplication.ExitPlaymode();
        }
        static void Need(bool value,string error){if(!value)throw new InvalidOperationException(error);}
    }
}
