using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Opt-in graphical validation. Commands use the same Controller entry points
    // as UI, but are scripted calls, explicitly not hardware pointer/keyboard input.
    public sealed class BusinessRuntimeValidation : MonoBehaviour
    {
        [Serializable] public sealed class Checkpoint { public string name;public BusinessAuditState state;public VendorWorkAudit vendor; }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.business.runtime-validation.v2";
            public string inputSource="Scripted new object-interaction Controller commands with real vendor contact callbacks and Unity Update; not hardware input";
            public bool success;
            public string failure="";
            public float realPauseSeconds,expiredIntentCancelSeconds;
            public int completedRecipeTypes,settlementCallbacks,restarts;
            public bool duplicateCommandsBlocked,expiredContactPreservedStock,actionPausePassed,selectionContextPassed;
            public string[] completed,screenshots,engineErrors;
            public Checkpoint[] checkpoints;
            public string[] excluded={"Hardware pointer/keyboard input feel","Normal procurement traversal","Save/import/restore","All 24 recipes and night simulation"};
        }
        private readonly Report report=new Report();
        private readonly List<string> completed=new List<string>(),screenshots=new List<string>(),errors=new List<string>();
        private readonly List<Checkpoint> checkpoints=new List<Checkpoint>();
        private string output,directory;
        private GameFlow flow;
        private int lastFailedRequest=-1,consecutiveActorFailures;
        private string previousActorFailure="";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--business-test")>=0)
                new GameObject("BusinessRuntimeValidation").AddComponent<BusinessRuntimeValidation>();
        }
        private void Awake(){Application.logMessageReceived+=Log;}
        private void Log(string condition,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception)errors.Add(condition+"\n"+trace);}
        private IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--business-output");
            output=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"business-runtime-validation.json");
            if(!output.EndsWith(".json",StringComparison.OrdinalIgnoreCase))output=Path.Combine(output,"business-runtime-validation.json");
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                object current=null;bool moved=false;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception exception){report.failure=exception.ToString();break;}
                if(!moved){stack.Pop();continue;}
                if(current is IEnumerator nested){stack.Push(nested);continue;}
                yield return current;
            }
            if(report.failure!=""||errors.Count>0)
            {
                foreach(BusinessController current in FindObjectsByType<BusinessController>()){Add("failure-final-state",current);break;}
                string failurePath=NextScreenshotPath("business-failure.png");yield return new WaitForEndOfFrame();
                try{ScreenCapture.CaptureScreenshot(failurePath);}catch(Exception exception){errors.Add("Failure capture: "+exception.Message);}
                float captureDeadline=Time.realtimeSinceStartup+3;while(!File.Exists(failurePath)&&Time.realtimeSinceStartup<captureDeadline)yield return null;
                if(File.Exists(failurePath))screenshots.Add(failurePath);
            }
            report.success=report.failure==""&&errors.Count==0;
            report.completed=completed.ToArray();report.screenshots=screenshots.ToArray();report.engineErrors=errors.ToArray();report.checkpoints=checkpoints.ToArray();
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.success)Debug.Log("BUSINESS_RUNTIME_VALIDATION_PASS "+output);else Debug.LogError("BUSINESS_RUNTIME_VALIDATION_FAIL "+output+" "+report.failure);
            yield return null;Application.Quit(report.success?0:1);
        }
        private IEnumerator Execute()
        {
            Need(!Application.isBatchMode,"Business runtime validation requires a graphical Player, not batch/headless mode.");
            foreach(string flag in new[]{"--interaction-test","--smoke-test","--traversal-test","--capture-dir"})
                Need(Array.IndexOf(Environment.GetCommandLineArgs(),flag)<0,"Run graphical business validation separately from "+flag+".");
            float deadline=Time.realtimeSinceStartup+30;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            flow=GameFlow.Instance;Need(flow!=null&&flow.World!=null,"GameFlow did not create its initial world.");
            yield return null;Screen.SetResolution(1600,900,false);yield return null;
            if(CharacterCreationUI.IsOpen)
            {
                Button practice=null;foreach(Button b in FindObjectsByType<Button>())if(b.gameObject.name=="Practice"){practice=b;break;}
                Need(practice!=null,"Visible creator had no Practice button.");practice.onClick.Invoke();
            }
            else flow.StartPractice();
            yield return null;yield return null;
            Need(!CharacterCreationUI.IsOpen,"Practice entry left character creation active.");flow.SetPaused(false);
            BusinessController business=OnlyBusiness();
            Need(business.Session.Setup.practice&&business.Session.Setup.seed==12017,"StartPractice did not use the declared fixed fixture.");
            business.SelectDistrict(4);
            Need(business.Session.DistrictIndex==4,"Controller district command did not change the session.");
            Need(business.Session.SelectedRecipes.Count==2&&business.Session.SelectedRecipes.Contains(0)&&business.Session.SelectedRecipes.Contains(2),"Practice should configure fried rice and flatbread on two slots.");
            Add("practice-selected-two-real-menus",business);Mark("StartPractice → district selection → two valid menus");
            yield return Capture("business-01-site-selection.png");
            business.Open();business.Open();yield return WaitUntil(()=>business.Session.Started&&!business.CaptureAudit().opening,5,"Opening did not complete.");
            Need(flow.Run.Phase==GamePhase.Business&&business.Session.Lanes.Length==2,"Opening did not establish two active workstations.");
            yield return new WaitForSecondsRealtime(.8f);yield return Capture("business-00-opened-scene.png");
            CheckVisibleWorkplace(business);
            CheckObjectTargets(business);
            bool pausedChecked=false,operatingCaptured=false;
            var servedRecipes=new HashSet<int>();deadline=Time.realtimeSinceStartup+150;
            while((servedRecipes.Count<2||business.Session.Served<2||!pausedChecked||!report.selectionContextPassed)&&Time.realtimeSinceStartup<deadline)
            {
                CheckActorFailures(business);
                foreach(StallOrder order in business.Session.Orders.ToArray())if(order.served)servedRecipes.Add(order.recipeIndex);
                if(business.CanInteract)DriveObjectCommand(business);
                bool heat=false;foreach(var lane in business.Session.Lanes)heat|=lane.step==KitchenStep.Heating||lane.step==KitchenStep.Utensil;
                if(!pausedChecked&&heat&&business.Stage.WorkActorBusy)
                {
                    flow.SetPaused(true);yield return null;
                    BusinessAuditState before=business.CaptureAudit();Vector3 actorBefore=business.Stage.WorkActorPosition;string actorAudit=JsonUtility.ToJson(business.Stage.WorkAudit);Add("paused-during-vendor-contact-before",business);
                    Need(before.paused&&before.walkingCustomerActors==0&&business.Stage.WorkActorBusy,"Pause did not retain the active vendor action and freeze walkers.");
                    business.CloseDay();Need(!business.UseCookware(0)&&!business.UseSeasoning(0)&&!business.UsePackage(0,"P05")&&!business.UseIngredient("rice"),"Paused object commands were accepted.");
                    foreach(var order in business.Session.Orders)if(!order.departed&&!order.served){Need(!business.SelectOrder(order.id),"Paused order selection was accepted.");break;}
                    float started=Time.realtimeSinceStartup;yield return new WaitForSecondsRealtime(2.05f);
                    BusinessAuditState after=business.CaptureAudit();report.realPauseSeconds=Time.realtimeSinceStartup-started;
                    Need(before.elapsed==after.elapsed&&before.remaining==after.remaining,"Pause advanced the business clock.");
                    Need(JsonUtility.ToJson(new OrderArray{values=before.orders})==JsonUtility.ToJson(new OrderArray{values=after.orders}),"Pause changed order patience or status.");
                    Need(JsonUtility.ToJson(new LaneArray{values=before.lanes})==JsonUtility.ToJson(new LaneArray{values=after.lanes}),"Pause advanced the cookware state/timers.");
                    Need(JsonUtility.ToJson(new StockArray{values=before.inventory})==JsonUtility.ToJson(new StockArray{values=after.inventory})&&before.cashCents==after.cashCents,"Paused commands changed stock/cash.");
                    Need(!after.finished&&after.walkingCustomerActors==0&&business.Stage.WorkActorBusy&&business.Stage.WorkActorPosition==actorBefore,"Paused vendor/contact action or close escaped the Controller guard.");
                    Need(actorAudit==JsonUtility.ToJson(business.Stage.WorkAudit),"Pause advanced the vendor action timer, IK contact or tool pose.");
                    Add("paused-during-vendor-contact-after-two-seconds",business);flow.SetPaused(false);yield return new WaitForSecondsRealtime(.2f);
                    Need(business.Session.Elapsed>before.elapsed,"Business clock failed to resume.");pausedChecked=true;report.actionPausePassed=true;Mark("Real pause freezes active vendor action, clock, patience, cooking, actor motion and object commands");
                    yield return Capture("business-02-live-cooking.png");operatingCaptured=true;
                }
                BusinessAuditState check=business.CaptureAudit();Need(check.cashConserved&&check.packagesConserved&&check.nonNegativeInventory&&check.nonNegativeEnergy,"Live simulation violated money/stock/energy invariants.");
                yield return null;
            }
            Need(pausedChecked&&business.Session.Served>=2&&servedRecipes.Count==2,"Timed out before both configured dishes were actually delivered.");
            Need(report.selectionContextPassed,"Did not execute select-next-order → package-other-pan → pickup-for-selected-order sequence.");
            report.completedRecipeTypes=servedRecipes.Count;Add("two-dishes-delivered",business);Mark("Two configured dishes delivered through selected order, physical ingredient/cookware/seasoning/package commands and real actor contact");
            if(!operatingCaptured)yield return Capture("business-02-live-cooking.png");
            yield return CheckExpiredContact(business);
            business.CloseDay();business.CloseDay();yield return null;yield return null;
            int finalCash=business.Session.CashCents;
            Need(business.SettlementPublishCount==1&&flow.Run.Phase==GamePhase.DaySummary&&flow.Run.CashCents==finalCash,"Settlement callback was duplicated or cash was not committed.");
            yield return new WaitForSecondsRealtime(.25f);business.CloseDay();
            Need(business.SettlementPublishCount==1&&business.Session.CashCents==finalCash,"Repeated close republished/changed settlement.");
            report.settlementCallbacks=business.SettlementPublishCount;Add("settled-once",business);Mark("Repeated close → one committed DaySummary callback");
            yield return Capture("business-03-day-summary.png");
            for(int restart=1;restart<=2;restart++)
            {
                business.Continue();business.Continue();yield return null;yield return null;
                business=OnlyBusiness();Need(CountStallCameras()==1,"Continue left an extra business camera.");
                Need(business.Session.Setup.practice&&!business.Session.Started&&business.Session.CashCents==120000&&business.Session.Count("rice")==60,"Practice restart retained stale operating state.");
                report.restarts=restart;Add("clean-practice-restart-"+restart,business);
                if(restart==1)
                {
                    business.Open();yield return WaitUntil(()=>business.Session.Started&&!business.CaptureAudit().opening,5,"Reopened practice did not finish its curtain.");
                    business.CloseDay();yield return null;yield return null;Need(business.SettlementPublishCount==1,"Second controller did not settle once.");
                }
            }
            Mark("Two Continue restarts leave one controller and one camera, with fresh explicit fixture data");
            Need(errors.Count==0,"Unity emitted errors/exceptions during graphical business validation.");
        }
        [Serializable] private sealed class OrderArray { public BusinessOrderAudit[] values; }
        [Serializable] private sealed class LaneArray { public BusinessLaneAudit[] values; }
        [Serializable] private sealed class StockArray { public BusinessInventoryAudit[] values; }
        private void CheckActorFailures(BusinessController business)
        {
            VendorWorkAudit actor=business.Stage.WorkAudit;if(actor==null)return;
            if(actor.lastFailure==""&&actor.contactCommitted&&!actor.busy){consecutiveActorFailures=0;previousActorFailure="";return;}
            if(actor.lastFailure==""||actor.requestId==lastFailedRequest)return;
            lastFailedRequest=actor.requestId;int colon=actor.lastFailure.IndexOf(':');string kind=colon>=0?actor.lastFailure.Substring(0,colon):actor.lastFailure;
            consecutiveActorFailures=kind==previousActorFailure?consecutiveActorFailures+1:1;previousActorFailure=kind;
            Add("vendor-failure-"+actor.requestId,business);
            Need(consecutiveActorFailures<2,"Stopped after the same vendor action failure twice; no blind retry: "+actor.lastFailure);
        }
        private void DriveObjectCommand(BusinessController business)
        {
            // Busy actors still accept bounded intentions. A reserved pan cannot
            // be claimed for another order before its first pickup contacts it.
            if(business.PendingWorkCount>=3)return;
            foreach(KitchenLane lane in business.Session.Lanes)if(lane.step==KitchenStep.Utensil&&!business.HasPendingIntent(StallInteractionKind.Cookware,lane.index))
            {if(business.UseCookware(lane.index)){Need(!business.UseCookware(lane.index),"Duplicate cookware command entered the actor queue.");report.duplicateCommandsBlocked=true;}return;}
            foreach(KitchenLane lane in business.Session.Lanes)if(lane.step==KitchenStep.Pack&&!business.HasPendingIntent(StallInteractionKind.Packaging,lane.index))
            {
                StallOrder bound=business.Session.FindOrder(lane.orderId);if(bound==null||bound.departed||bound.patience<6)continue;
                foreach(string id in BusinessSession.Recipes[lane.recipeIndex].packages)if(business.Session.Count(id)>0)
                {
                    if(!report.selectionContextPassed&&business.PendingWorkCount<=1&&TrySelectionContext(business,lane.index,id))return;
                    if(business.UsePackage(lane.index,id)){Need(!business.UsePackage(lane.index,id),"Duplicate packaging command entered the actor queue.");report.duplicateCommandsBlocked=true;}return;
                }
            }
            foreach(KitchenLane lane in business.Session.Lanes)if(lane.step==KitchenStep.Season&&lane.remaining<=0&&!business.HasPendingIntent(StallInteractionKind.Seasoning,lane.index))
            {business.UseSeasoning(lane.index);return;}
            foreach(KitchenLane lane in business.Session.Lanes)if(lane.step==KitchenStep.Dirty&&!business.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){business.UseCookware(lane.index);return;}
            foreach(KitchenLane lane in business.Session.Lanes)if((lane.step==KitchenStep.Heating||lane.step==KitchenStep.Prep)&&!business.HasPendingIntent(StallInteractionKind.Cookware,lane.index))
            {business.UseCookware(lane.index);return;}
            StallOrder chosen=null;KitchenLane chosenLane=null;float best=float.NegativeInfinity;
            foreach(StallOrder order in business.Session.Orders)
            {
                if(!order.arrived||order.served||order.departed||order.lane>=0||order.reservedLane>=0)continue;
                foreach(KitchenLane lane in business.Session.Lanes)
                {
                    if(lane.recipeIndex!=order.recipeIndex||lane.reservedOrderId>=0||(lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.ReadyStock))continue;
                    // This is a player-like diagnostic strategy, not a change to
                    // customer patience: don't start a full new batch for someone
                    // who will leave before the real walk/cook/pack route can finish.
                    float budget=lane.step==KitchenStep.ReadyStock?10:24;if(order.patience<budget)continue;
                    bool alreadyDelivered=business.Session.Orders.Exists(other=>other.served&&other.recipeIndex==order.recipeIndex);
                    float score=order.patience+(alreadyDelivered?0:1000)+(lane.step==KitchenStep.ReadyStock?20:0);
                    if(score>best){best=score;chosen=order;chosenLane=lane;}
                }
            }
            if(chosen==null)return;
            BusinessAuditState before=business.CaptureAudit();Need(business.SelectOrder(chosen.id),"New order could not be selected.");
            Need(JsonUtility.ToJson(new StockArray{values=before.inventory})==JsonUtility.ToJson(new StockArray{values=business.CaptureAudit().inventory}),"Selecting an order debited stock.");
            if(chosenLane.step==KitchenStep.ReadyStock)
            {
                Need(business.UseCookware(chosenLane.index),"Cooked batch could not be served from its physical cookware.");
                Need(!business.UseCookware(chosenLane.index),"Duplicate cooked-portion command entered the actor queue.");report.duplicateCommandsBlocked=true;return;
            }
            foreach(string raw in BusinessSession.Recipes[chosen.recipeIndex].ingredients.Keys)
            {
                Need(business.UseIngredient(raw),"Valid physical ingredient command was rejected.");Need(business.HasPendingIntent(StallInteractionKind.Ingredient,chosenLane.index),"Accepted pickup has neither active action nor queued intent.");
                Need(!business.UseIngredient(raw),"Duplicate ingredient command entered the actor queue.");report.duplicateCommandsBlocked=true;return;
            }
        }
        private bool TrySelectionContext(BusinessController business,int packageLane,string package)
        {
            foreach(StallOrder order in business.Session.Orders)
            {
                if(!order.arrived||order.departed||order.served||order.lane>=0||order.reservedLane>=0||order.patience<24)continue;
                foreach(KitchenLane lane in business.Session.Lanes)
                {
                    if(lane.index==packageLane||lane.recipeIndex!=order.recipeIndex||lane.step!=KitchenStep.Idle||lane.reservedOrderId>=0)continue;
                    string raw="";foreach(string item in BusinessSession.Recipes[order.recipeIndex].ingredients.Keys)if(item!="seasoning"){raw=item;break;}
                    Need(raw!="","Context fixture recipe has no pickable raw ingredient");
                    BusinessAuditState before=business.CaptureAudit();Need(business.SelectOrder(order.id),"Could not select next order while previous pan waits for packaging");
                    Need(business.UsePackage(packageLane,package),"Could not queue packaging on other physical pan");
                    Need(business.SelectedOrderId==order.id&&business.SelectedLaneIndex==packageLane&&business.SelectedOrderLaneIndex==lane.index,"Tool focus replaced independent selected-order context");
                    Need(business.UseIngredient(raw),"Clicking selected order's ingredient followed the other pan's tool focus");
                    Need(order.reservedLane==lane.index&&business.HasPendingIntent(StallInteractionKind.Ingredient,lane.index),"Selected-order pickup reserved the wrong pan");
                    Need(JsonUtility.ToJson(new StockArray{values=before.inventory})==JsonUtility.ToJson(new StockArray{values=business.CaptureAudit().inventory}),"Context change consumed goods before real contact");
                    report.selectionContextPassed=true;Add("selected-order-independent-of-other-pan-packaging",business);Mark("Select next order → package other pan → selected order ingredient queues on its own cookware without early debit");return true;
                }
            }
            return false;
        }
        private IEnumerator CheckExpiredContact(BusinessController business)
        {
            business.CancelPendingWork();
            yield return WaitUntil(()=>!business.Stage.WorkActorBusy,12,"Vendor did not finish its prior delivery.");
            StallOrder candidate=null;int candidateLane=-1;float deadline=Time.realtimeSinceStartup+35;
            while(candidate==null&&Time.realtimeSinceStartup<deadline)
            {
                foreach(StallOrder order in business.Session.Orders)
                {
                    if(!order.arrived||order.departed||order.served||order.lane>=0||order.reservedLane>=0||order.patience<.3f)continue;
                    foreach(KitchenLane lane in business.Session.Lanes)
                        if(lane.recipeIndex==order.recipeIndex&&(lane.step==KitchenStep.Idle||lane.step==KitchenStep.ReadyStock)){candidate=order;candidateLane=lane.index;break;}
                    if(candidate!=null)break;
                }
                if(candidate==null)yield return null;
            }
            Need(candidate!=null,"No pending order available for the delayed-contact expiry check.");
            while(!candidate.departed&&candidate.patience>.14f)yield return null;
            Need(!candidate.departed&&business.CanInteract,"Expiry test missed the final patience window.");
            BusinessAuditState before=business.CaptureAudit();int previousWorkCount=business.CaptureWorkHistory().Length;Need(business.SelectOrder(candidate.id),"Near-expiry order could not be selected.");
            string ingredient="";foreach(string id in BusinessSession.Recipes[candidate.recipeIndex].ingredients.Keys){ingredient=id;break;}
            bool cooked=business.Session.Lanes[candidateLane].step==KitchenStep.ReadyStock;
            Need(cooked?business.UseCookware(candidateLane):business.UseIngredient(ingredient),"Near-expiry physical serving command was not accepted.");
            Need(JsonUtility.ToJson(new StockArray{values=before.inventory})==JsonUtility.ToJson(new StockArray{values=business.CaptureAudit().inventory}),"Stock was debited before vendor contact.");
            yield return WaitUntil(()=>candidate.departed&&!business.Stage.WorkActorBusy,2,"Expired uncommitted intent was not cancelled promptly.");
            BusinessAuditState after=business.CaptureAudit();
            report.expiredIntentCancelSeconds=after.elapsed-before.elapsed;
            bool cancelled=false;BusinessWorkAudit[] history=business.CaptureWorkHistory();for(int i=previousWorkCount;i<history.Length;i++)
            {var work=history[i];if(work.orderId==candidate.id&&work.contactAt<0&&(work.status=="cancelled_stale_before_contact"||work.status=="expired_or_state_changed"))cancelled=true;}
            Need(cancelled&&business.Stage.WorkAudit!=null&&business.Stage.WorkAudit.lastFailure==""&&!business.Stage.WorkActorBusy,"Expired uncommitted intent was not explicitly cancelled and cleaned up.");
            Need(candidate.departed&&candidate.lane<0&&candidate.reservedLane<0&&business.Session.Lanes[candidateLane].reservedOrderId<0,"Expired uncommitted order retained a physical or reserved pan binding.");
            Need(before.cashCents==after.cashCents&&before.gas==after.gas&&before.coal==after.coal&&before.electricity==after.electricity&&JsonUtility.ToJson(new StockArray{values=before.inventory})==JsonUtility.ToJson(new StockArray{values=after.inventory}),"Expired contact consumed raw stock, energy, packaging or money.");
            report.expiredContactPreservedStock=true;Add("expired-before-vendor-contact-no-transaction",business);Mark("Customer leaves before actor contact: pending intent cancelled, actor released, no raw/energy/package debit or money change");
        }
        private static void CheckObjectTargets(BusinessController business)
        {
            var kinds=new HashSet<StallInteractionKind>();foreach(var target in business.Stage.GetComponentsInChildren<StallInteractionTarget>())
            {kinds.Add(target.kind);Need(target.GetComponentInChildren<Collider>()!=null,"Physical interaction target has no hit collider: "+target.kind+" / "+target.itemId);}
            foreach(var kind in new[]{StallInteractionKind.Ingredient,StallInteractionKind.Cookware,StallInteractionKind.Seasoning,StallInteractionKind.Packaging})Need(kinds.Contains(kind),"Missing real object interaction category: "+kind);
        }
        private static BusinessController OnlyBusiness()
        {
            BusinessController[] all=FindObjectsByType<BusinessController>(FindObjectsInactive.Include);
            Need(all.Length==1&&all[0].gameObject.activeInHierarchy,"Expected exactly one surviving active BusinessController, found "+all.Length);return all[0];
        }
        private static int CountStallCameras()
        {int count=0;foreach(Camera view in FindObjectsByType<Camera>(FindObjectsInactive.Include))if(view.gameObject.name=="Street counter camera"||view.gameObject.name=="Operating scene camera")count++;return count;}
        private void CheckVisibleWorkplace(BusinessController business)
        {
            BusinessModelAudit[] models=business.CaptureAudit().sceneModels;
            Need(models!=null&&models.Length>=4,"Missing actual cart, equipment or cookware geometry audit.");
            foreach(BusinessModelAudit model in models)
            {
                Need(model.activeRenderers>0&&model.inFrustumRenderers>0&&model.renderedLastFrame>0,model.model+" is inactive, outside the camera, or was not rendered.");
                if(model.model=="cart-ai")Need(model.worldSize[1]>1.5f&&model.worldSize[1]<2.3f,"CartAI is not upright at its authored metre scale.");
                else
                {
                    float midY=(model.viewportMinMax[1]+model.viewportMinMax[4])*.5f;
                    Need(midY>.14f&&midY<.85f,model.model+" lies outside the clear central operation area.");
                }
            }
            Add("actual-cart-stove-and-two-cookware-in-view",business);Mark("Actual CartAI, loaded equipment and both workstations render in the clear operation area (pixel review still required)");
        }
        private void Add(string name,BusinessController business){checkpoints.Add(new Checkpoint{name=name,state=business.CaptureAudit(),vendor=business.Stage.WorkAudit});}
        private void Mark(string text){completed.Add(text);Debug.Log("BUSINESS_RUNTIME_CHECK "+text);}
        private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        private static IEnumerator WaitUntil(Func<bool> condition,float seconds,string error)
        {float deadline=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;Need(condition(),error);}
        private IEnumerator Capture(string name)
        {
            string path=NextScreenshotPath(name);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(path);
            float deadline=Time.realtimeSinceStartup+4;while(!File.Exists(path)&&Time.realtimeSinceStartup<deadline)yield return null;
            Need(File.Exists(path),"Graphical screenshot was not written: "+path);screenshots.Add(path);
        }
        private string NextScreenshotPath(string name)
        {string path=Path.Combine(directory,name);int version=2;while(File.Exists(path))path=Path.Combine(directory,Path.GetFileNameWithoutExtension(name)+"-r"+(version++)+Path.GetExtension(name));return path;}
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
