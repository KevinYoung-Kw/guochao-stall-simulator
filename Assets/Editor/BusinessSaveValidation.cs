using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    [InitializeOnLoad] public static class BusinessSaveValidation
    {
        [Serializable] public sealed class Report { public bool success;public string failure="";public string[] checks; }
        const string Pending="Guochao.BusinessSave.Pending",Output="Guochao.BusinessSave.Output";
        static IEnumerator running;static string output;static readonly List<string> checks=new List<string>();static BusinessController live;
        static BusinessSaveValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--business-save-output");
            string path=i>=0&&i+1<args.Length?args[i+1]:Path.Combine(Application.dataPath,"../../art-v13-implementation/p1/business-save-validation.json");
            SessionState.SetString(Output,Path.GetFullPath(path));SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");checks.Clear();running=Validate();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish("");}}
            catch(Exception e){Debug.LogException(e);running=null;Finish(e.ToString());}
        }
        static BusinessSaveData Disk(BusinessSaveData data)=>JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(data));
        static BusinessSession NewSession(bool open=true)
        {
            BusinessSetup setup=BusinessSetup.MidgamePractice();setup.seed=12041;setup.profile=new RunProfile{careerId="career4"};
            var session=new BusinessSession(setup);if(open){Need(session.Open(),"Could not open fixture");session.Tick(1);Need(session.Orders[0].recipeIndex==0,"Fixture expected first rice order");session.MarkCustomerArrived(session.Orders[0].id);}return session;
        }
        static void PureModelChecks()
        {
            var profileless=new BusinessSession(BusinessSetup.MidgamePractice());
            Need(profileless.Setup.profile==null&&BusinessSession.Restore(Disk(profileless.CaptureSave())).Setup.profile==null,"Null practice profile became an inline empty profile after JSON restore");
            BusinessSession selection=NewSession(false);BusinessSession selectionRestore=BusinessSession.Restore(Disk(selection.CaptureSave()));
            Need(!selectionRestore.Started&&selectionRestore.Orders.Count==0&&selectionRestore.Elapsed==0,"Selection restore accidentally opened/spawned");
            var original=NewSession();int orderId=original.Orders[0].id;Need(original.ReserveOrder(orderId,0),"Reservation fixture failed");
            BusinessSaveData snapshot=original.CaptureSave();snapshot.setup.profile.selectedTraits=new[]{"速读"};snapshot.inventory[0].count=0;snapshot.orders[0].patience=1;snapshot.equipment[0]="changed";
            Need(original.Setup.profile.selectedTraits.Length==0&&original.Orders[0].patience>1&&original.Count("dough")>0&&!original.Equipment.Contains("changed"),"Capture exposed live arrays or records");
            BusinessSession beforeContact=BusinessSession.Restore(Disk(original.CaptureSave()));
            Need(original.Orders[0].reservedLane==0&&beforeContact.Orders[0].reservedLane<0&&beforeContact.Lanes[0].reservedOrderId<0,"Restore did not cancel only transient reservations");
            Need(beforeContact.Count("rice")==original.Count("rice")&&beforeContact.Gas==original.Gas,"Reservation restoration debited resources");
            original.ReleaseAllReservations();Need(original.BeginOrder(orderId,0),"Could not commit first batch");
            var restored=BusinessSession.Restore(Disk(original.CaptureSave()));
            Need(restored.Count("rice")==58&&restored.Gas==44&&restored.Lanes[0].step==KitchenStep.Prep&&restored.Lanes[0].batchRemaining==2,"Committed batch was not restored exactly once");
            Need(JsonUtility.ToJson(original.CaptureSave())==JsonUtility.ToJson(restored.CaptureSave()),"Business round-trip changed clock/batch/customer/ledger fields");
            // Mark arrivals identically; subsequent customer types, patience and
            // timing must match after the next random calls, not just at load.
            for(int frame=0;frame<7200;frame++)
            {
                foreach(StallOrder order in original.Orders)if(!order.arrived&&!order.departed&&!order.served)original.MarkCustomerArrived(order.id);
                foreach(StallOrder order in restored.Orders)if(!order.arrived&&!order.departed&&!order.served)restored.MarkCustomerArrived(order.id);
                original.Tick(1f/60f);restored.Tick(1f/60f);
            }
            Need(JsonUtility.ToJson(original.CaptureSave())==JsonUtility.ToJson(restored.CaptureSave()),"Post-load random stream, clocks or customer order diverged");
            original.Close();BusinessSession closed=BusinessSession.Restore(Disk(original.CaptureSave()));int lost=closed.Lost;closed.Close();closed.Tick(20);
            Need(closed.Finished&&closed.Lost==lost&&closed.CashCents==original.CashCents,"Closed restore settled or progressed twice");
            var invalid=original.CaptureSave();invalid.schemaVersion++;NeedReject(()=>BusinessSession.Restore(invalid),"Unknown save version accepted");
            invalid=original.CaptureSave();invalid.inventory[0].count=-1;NeedReject(()=>BusinessSession.Restore(invalid),"Negative saved inventory accepted");
            invalid=original.CaptureSave();invalid.randomAlgorithm="different";NeedReject(()=>BusinessSession.Restore(invalid),"Unknown random algorithm accepted");
            checks.Add("JSON deep-copy + null profile preserved + selection + reserved/pre-contact restore + committed batch + 120s identical future RNG + closed/idempotent state + malformed version/inventory/RNG rejection");
        }
        static IEnumerator Validate()
        {
            PureModelChecks();BusinessSession session=NewSession();int first=session.Orders[0].id;
            live=BusinessController.BeginRestored(Disk(session.CaptureSave()),null,null,null);live.enabled=false;yield return null;
            Need(live.SelectOrder(first)&&live.UseIngredient("rice"),"Could not begin physical pickup");
            for(int frame=0;frame<1200&&!live.Stage.WorkAudit.carryingVisualProp;frame++)
            {live.AdvanceSimulation(1f/60f);if(frame%90==0)yield return null;}
            Need(live.Stage.WorkActorBusy&&live.Stage.WorkAudit.carryingVisualProp&&live.Session.Count("rice")==60,"Did not reach carrying-before-pour checkpoint");
            float beforeClock=live.Session.Elapsed;int beforeGas=live.Session.Gas;
            BusinessSaveData pre=Disk(live.CaptureForSave());
            Need(live.Session.Elapsed==beforeClock&&live.Session.Gas==beforeGas&&live.Session.Count("rice")==60,"Capture advanced time or pre-contact resources");
            Need(!live.Stage.WorkActorBusy&&!live.Stage.WorkAudit.carryingVisualProp&&live.PendingWorkCount==0&&live.Session.Orders[0].reservedLane<0,"Capture left a prop/action/reservation active");
            float patience=pre.orders[0].patience;Vector3 vendor=pre.presentation.vendorPosition;live.Cancel();live=null;yield return null;
            live=BusinessController.BeginRestored(pre,null,null,null);live.enabled=false;yield return null;
            Need(live.Session.Orders[0].patience==patience&&live.Session.Orders[0].arrived,"Arrived customer patience was reset on scene restore");
            Need(Vector3.Distance(live.Stage.WorkActorPosition,vendor)<.00001f,"Restored vendor was moved away from saved work route");
            Need(live.Stage.CaptureGuestAudit().Length==1&&live.Stage.CaptureGuestAudit()[0].arrived,"Arrived customer did not restore directly to its queue slot");
            Need(live.Stage.CaptureGuestOverlaps().Length==0,"Restored queue bodies overlap");
            checks.Add("Real pickup-before-contact save cancels prop and reservation; scene restore preserves vendor pose and arrived customer's remaining patience");
            Need(live.SelectOrder(first)&&live.UseIngredient("rice"),"Could not resume cancelled pickup");
            for(int frame=0;frame<1200&&live.Session.Count("rice")==60;frame++)
            {live.AdvanceSimulation(1f/60f);if(frame%90==0)yield return null;}
            Need(live.Session.Count("rice")==58&&live.Session.Gas==44&&live.Stage.WorkActorBusy,"Did not reach committed-contact-before-recovery checkpoint");
            BusinessSaveData post=Disk(live.CaptureForSave());float remaining=post.lanes[0].remaining;
            Need(post.inventory.Length>0&&!live.Stage.WorkActorBusy&&!live.Stage.WorkAudit.carryingVisualProp,"Committed capture left visual work active");
            live.Cancel();live=null;yield return null;
            int publications=0;live=BusinessController.BeginRestored(post,null,cash=>publications++,null);live.enabled=false;yield return null;
            Need(live.Session.Count("rice")==58&&live.Session.Gas==44&&live.Session.Lanes[0].remaining==remaining&&live.Session.Lanes[0].orderId==first,"Post-contact restore repeated debit or reset timer/binding");
            live.AdvanceSimulation(.01f,true);Need(live.Session.Lanes[0].remaining==remaining,"Paused restored cookware progressed");
            checks.Add("Real committed pour saved before action recovery restores raw/energy/batch/timer once, with no resumed callback");
            live.Session.Close();live.AdvanceSimulation(0);Need(publications==1,"Initial settlement did not publish once");
            BusinessSaveData settled=Disk(live.CaptureForSave());Need(settled.settlementPublished&&settled.settlementPublications==1,"Settlement marker was not saved");
            live.Cancel();live=null;yield return null;
            live=BusinessController.BeginRestored(settled,null,cash=>publications++,null);live.enabled=false;yield return null;
            live.CloseDay();live.AdvanceSimulation(1);live.CaptureForSave();
            Need(publications==1&&live.SettlementPublishCount==1,"Restoring completed settlement republished cash");
            checks.Add("Completed settlement restored/displayed without a second cash callback, including repeated close and save");
            live.Cancel();live=null;yield return null;
            var arriving=NewSession(false);arriving.Open();arriving.Tick(1);float unarrivedPatience=arriving.Orders[0].patience;
            live=BusinessController.BeginRestored(Disk(arriving.CaptureSave()),null,null,null);live.enabled=false;yield return null;
            Need(!live.Session.Orders[0].arrived,"Unarrived order was marked arrived during restore");
            for(int i=0;i<60;i++)live.AdvanceSimulation(1f/60f);
            Need(!live.Session.Orders[0].arrived&&live.Session.Orders[0].patience==unarrivedPatience,"Unarrived restored customer lost patience during entry");
            for(int i=0;i<1200&&!live.Session.Orders[0].arrived;i++){live.AdvanceSimulation(1f/60f);if(i%90==0)yield return null;}
            Need(live.Session.Orders[0].arrived&&live.Session.Orders[0].patience==unarrivedPatience,"Restored entry failed to send one arrival event or reset patience");
            live.AdvanceSimulation(1f/60f);Need(live.Session.Orders[0].patience<unarrivedPatience,"Patience did not begin after physical arrival");
            checks.Add("Unarrived saved order physically enters without reroll or patience loss; timer starts only at actual arrival");
            live.Cancel();live=null;yield return null;
        }
        static void Finish(string failure)
        {
            if(live!=null){live.Cancel();live=null;}Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(new Report{success=failure=="",failure=failure,checks=checks.ToArray()},true));
            Debug.Log((failure==""?"BUSINESS_SAVE_PASS ":"BUSINESS_SAVE_FAIL ")+output);
            if(Application.isBatchMode)EditorApplication.Exit(failure==""?0:1);else EditorApplication.ExitPlaymode();
        }
        static void Need(bool condition,string failure){if(!condition)throw new InvalidOperationException(failure);}
        static void NeedReject(Action action,string failure){try{action();}catch(ArgumentException){return;}throw new InvalidOperationException(failure);}
    }
}
