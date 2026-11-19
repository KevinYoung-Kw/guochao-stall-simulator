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
    // Isolated restored-state fixtures, real production Stage/actor/colliders.
    // This is not hardware-input, procurement, or complete-campaign evidence.
    [InitializeOnLoad]
    public static class Batch1CookingValidation
    {
        [Serializable] public sealed class Observation
        {
            public string label;
            public float elapsed,deadline;
            public int positioningLane;
            public Vector3 actorPosition;
            public VendorWorkAudit actor;
            public BusinessAuditState model;
            public BusinessWorkAudit[] work;
        }
        [Serializable] public sealed class CaseResult
        {
            public string id,status="running",detail="",failure="",fixture="";
            public List<Observation> observations=new List<Observation>();
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.batch1.cooking.v1";
            public string scope="Four isolated production cooking regressions. Real StreetStallStage, VendorWorkActor, fixed clock, IK contact and solid collision; no Unity Input injection and no complete-campaign claim. Fixtures use finite normal patience and explicit practice stock; restored arrived guests and saved vendor poses are fixture setup, not a traversal claim.";
            public bool success,usesFixtureInventory=true,usesRestoredPoses=true,hardwareInput=false;
            public bool collisionDisabled=false,infinitePatience=false,diagnosticPoseAssignmentsDuringMeasuredActions=false;
            public int passed,failed,pending;
            public string failure="";
            public List<CaseResult> cases=new List<CaseResult>();
            public string[] engineErrors;
        }
        sealed class PendingCaseException:Exception { public PendingCaseException(string message):base(message){} }
        sealed class Fixture
        {
            public GameObject root;
            public BusinessController controller;
            public StreetStallStage stage;
            public readonly List<string> actorFailures=new List<string>();
            public BusinessSession Session=>controller.Session;
        }
        const string PendingKey="Guochao.Batch1Cooking.Pending",OutputKey="Guochao.Batch1Cooking.Output";
        const float Step=1f/60f;
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> errors=new List<string>();
        static Fixture live;
        static IEnumerator running;
        static Report report;
        static string output;
        static double startedAt;
        static BusinessSaveData heatingCheckpoint,greenCheckpoint;

        static Batch1CookingValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--batch1-cooking-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --batch1-cooking-output <new report.json>");
            SessionState.SetString(OutputKey,Path.GetFullPath(args[at+1]));SessionState.SetBool(PendingKey,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(PendingKey,false))
            {
                SessionState.SetBool(PendingKey,false);output=SessionState.GetString(OutputKey,"");
                errors.Clear();report=new Report();heatingCheckpoint=greenCheckpoint=null;startedAt=EditorApplication.timeSinceStartup;
                Application.logMessageReceived+=CaptureError;running=RunCases();
            }
            if(running==null)return;
            try
            {
                if(EditorApplication.timeSinceStartup-startedAt>240)throw new TimeoutException("Cooking fixture watchdog exceeded 240 wall-clock seconds.");
                if(!running.MoveNext()){running=null;Finish("");}
            }
            catch(Exception error){running=null;Finish(error.ToString());}
        }
        static IEnumerator RunCases()
        {
            if(GameFlow.Instance!=null)throw new PendingCaseException("An active GameFlow is present; this entry requires an isolated empty PlayMode scene.");
            string[] ids={"fixed-step-large-delta-contact","remaining-green-deadline","departure-keeps-pan-appointment","urgent-pan-redirects-empty-approach"};
            Func<CaseResult,IEnumerator>[] tests={LargeDelta,RemainingDeadline,Departure,Redirect};
            for(int i=0;i<tests.Length;i++)
            {
                var result=new CaseResult{id=ids[i]};report.cases.Add(result);
                IEnumerator test=tests[i](result);bool finished=false;
                while(!finished)
                {
                    object yielded=null;
                    try
                    {
                        if(test.MoveNext())yielded=test.Current;
                        else{result.status="passed";finished=true;}
                    }
                    catch(PendingCaseException error){result.status="pending";result.failure=error.Message;finished=true;}
                    catch(Exception error){result.status="failed";result.failure=error.ToString();finished=true;}
                    if(!finished)yield return yielded;
                }
                (test as IDisposable)?.Dispose();DisposeFixture();
                Debug.Log("BATCH1_COOKING_CASE "+result.id+" "+result.status+" "+result.failure);
                yield return null;
            }
        }
        static BusinessSaveData Disk(BusinessSaveData data)=>JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(data));
        static BusinessSaveData ModelCheckpoint(bool dual)
        {
            // Create orders and debit batches through production methods. The
            // only selected seeds are those with both recipes in the finite queue.
            for(int seed=12017;seed<12065;seed++)
            {
                var setup=new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=120000,seed=seed,dayDuration=300,gasPoints=45,
                    equipment=new HashSet<string>{"B04","V01","V09","U01","U12"},
                    inventory=new Dictionary<string,int>{{"rice",60},{"egg",60},{"skewer",60},{"seasoning",60},{"P05",50},{"P07",50}}};
                var session=new BusinessSession(setup);session.SelectedRecipes.Clear();session.SelectedRecipes.Add(0);if(dual)session.SelectedRecipes.Add(3);
                Need(session.Open(),"Fixture menu could not open: "+session.Message);
                StallOrder rice=null,soup=null;
                for(int tick=0;tick<360;tick++)
                {
                    session.Tick(.25f);
                    rice=session.Orders.Find(x=>x.recipeIndex==0&&!x.departed&&!x.served);
                    soup=session.Orders.Find(x=>x.recipeIndex==3&&!x.departed&&!x.served);
                    if(rice!=null&&(!dual||soup!=null))break;
                    if(session.Orders.Count>=setup.queueCapacity)break;
                }
                if(rice==null||dual&&soup==null)continue;
                session.MarkCustomerArrived(rice.id);Need(session.BeginOrder(rice.id,0),"Rice fixture debit failed.");
                if(dual){session.MarkCustomerArrived(soup.id);Need(session.BeginOrder(soup.id,1),"Soup fixture debit failed.");}
                return Disk(session.CaptureSave());
            }
            throw new PendingCaseException("No finite four-slot seed produced the required fixture recipes; no order or patience was fabricated to force success.");
        }
        static Fixture CreateFixture(BusinessSaveData data)
        {
            Need(live==null,"Fixture overlap would contaminate collision queries.");
            var fixture=new Fixture{root=new GameObject("Batch1Cooking_RealStage")};live=fixture;
            fixture.controller=fixture.root.AddComponent<BusinessController>();fixture.controller.enabled=false;
            var session=BusinessSession.Restore(Disk(data));
            fixture.stage=fixture.root.AddComponent<StreetStallStage>();fixture.stage.Initialize(session.Setup,null);
            fixture.stage.WorkFailed+=reason=>fixture.actorFailures.Add(reason);
            fixture.stage.RestoreSession(session,data.presentation);
            typeof(BusinessController).GetProperty("Session").SetValue(fixture.controller,session);
            typeof(BusinessController).GetProperty("Stage").SetValue(fixture.controller,fixture.stage);
            fixture.stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);
            var actor=fixture.root.GetComponent<VendorWorkActor>();
            Need(actor!=null&&actor.IsBound,"Production actor did not bind to the actual character asset.");
            Need(!actor.automaticTick,"Fixture actor must advance through the production controller only.");
            int solids=0;foreach(var collider in fixture.root.GetComponentsInChildren<Collider>())
                if(collider.enabled&&!collider.isTrigger&&collider.gameObject.layer==StreetStallStage.SolidLayer)solids++;
            Need(solids>0,"Production stage has no enabled solid colliders.");
            return fixture;
        }
        static void DisposeFixture()
        {
            if(live==null)return;
            Fixture prior=live;live=null;
            if(prior.stage!=null)prior.stage.Release();
            if(prior.root!=null)UnityEngine.Object.DestroyImmediate(prior.root);
        }
        static void Tick(Fixture fixture,float dt)
        {
            Physics.SyncTransforms();fixture.controller.AdvanceSimulation(dt);CheckActor(fixture);
        }
        static void CheckActor(Fixture fixture)
        {
            Need(fixture.actorFailures.Count==0,"Real actor failed: "+string.Join(" | ",fixture.actorFailures));
            VendorWorkAudit audit=fixture.stage.WorkAudit;
            Need(audit!=null&&audit.bound,"Real actor audit unavailable.");
            Need(string.IsNullOrEmpty(audit.lastFailure),"Real actor failure: "+audit.lastFailure);
        }
        static void CheckModel(Fixture fixture)
        {
            BusinessAuditState audit=fixture.Session.CaptureAudit();
            Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeInventory&&audit.nonNegativeEnergy,"Production ledger invariant failed.");
            foreach(var recipe in audit.recipeProduction)Need(recipe.portionsConserved,"Production portion invariant failed: "+recipe.recipe);
        }
        static string ResourcesOf(BusinessSession session)
        {
            var values=new List<string>();foreach(var item in session.Inventory)values.Add(item.Key+"="+item.Value);values.Sort(StringComparer.Ordinal);
            return string.Join(";",values)+"|cash="+session.CashCents+"|revenue="+session.RevenueCents+"|served="+session.Served+
                "|gas="+session.Gas+"|coal="+session.Coal+"|cartridge="+session.Cartridge+"|electricity="+session.Electricity+
                "|short="+session.UnseasonedPortionsServed+"|integrity="+session.IntegrityChange;
        }
        static int PositioningLane(Fixture fixture)
        {
            FieldInfo field=typeof(BusinessController).GetField("positioningLane",Hidden);
            if(field==null)throw new PendingCaseException("positioningLane observation field is unavailable; cannot prove that an in-flight approach was redirected.");
            return (int)field.GetValue(fixture.controller);
        }
        static Observation Observe(Fixture fixture,string label,int lane=0)
        {
            CheckActor(fixture);CheckModel(fixture);
            float deadline=fixture.controller.SecondsUntilHeatDeadline(lane);
            // No finite heat deadline after a successful contact. Keep the
            // exported JSON portable instead of serializing Infinity/NaN.
            if(float.IsInfinity(deadline)||float.IsNaN(deadline))deadline=-1;
            return new Observation{label=label,elapsed=fixture.Session.Elapsed,deadline=deadline,
                positioningLane=PositioningLane(fixture),actorPosition=fixture.stage.WorkActorPosition,actor=fixture.stage.WorkAudit,
                model=fixture.Session.CaptureAudit(),work=fixture.controller.CaptureWorkHistory()};
        }
        static IEnumerator EnsureCheckpoints()
        {
            if(greenCheckpoint!=null&&heatingCheckpoint!=null)yield break;
            Fixture fixture=CreateFixture(ModelCheckpoint(false));yield return null;
            bool arrived=false;fixture.stage.ApproachCookware(0,()=>arrived=true);
            for(int frame=0;!arrived&&frame<600;frame++){Tick(fixture,Step);if(frame%60==0)yield return null;}
            Need(arrived&&fixture.stage.IsAtCookware(0)&&!fixture.stage.WorkActorBusy,"Could not reach the pan through the real collision-aware route.");
            for(int frame=0;frame<900;frame++)
            {
                KitchenLane lane=fixture.Session.Lanes[0];
                Need(lane.step!=KitchenStep.Dirty,"Checkpoint preparation burned before the requested boundary.");
                if(heatingCheckpoint==null&&lane.step==KitchenStep.Heating&&lane.remaining<=.5f+.0001f)
                    heatingCheckpoint=Disk(fixture.controller.CaptureForSave());
                if(lane.step==KitchenStep.Utensil&&lane.remaining<=.5f+.0001f)
                {
                    Need(lane.remaining>.45f,"Missed the half-second green boundary.");
                    greenCheckpoint=Disk(fixture.controller.CaptureForSave());break;
                }
                Tick(fixture,Step);if(frame%60==0)yield return null;
            }
            Need(heatingCheckpoint!=null&&greenCheckpoint!=null,"Natural heating/green checkpoints were not observed.");
            DisposeFixture();yield return null;
        }
        static IEnumerator LargeDelta(CaseResult result)
        {
            result.fixture="One-pan V01 rice, normal 2.5s window. Production batch debit, real approach to pan, natural progression to approximately 0.5s green remaining, then JSON save/restore. No active-action state is copied or stubbed.";
            IEnumerator preparation=EnsureCheckpoints();while(preparation.MoveNext())yield return preparation.Current;
            Observation[] atBoundary=new Observation[2];float[] contactTimes=new float[2];string[] resourceStates=new string[2];
            for(int pass=0;pass<2;pass++)
            {
                Fixture fixture=CreateFixture(Disk(greenCheckpoint));yield return null;
                string before=ResourcesOf(fixture.Session);float began=fixture.Session.Elapsed;
                Need(fixture.stage.IsAtCookware(0),"Restored real approach pose no longer reaches the pan.");
                Need(fixture.controller.UseCookware(0),"Timely utensil intent was rejected.");
                result.observations.Add(Observe(fixture,pass==0?"large-before":"small-before"));
                if(pass==0)Tick(fixture,.6f);else for(int step=0;step<36;step++)Tick(fixture,Step);
                Need(fixture.Session.Lanes[0].step==KitchenStep.Season,"Timely contact was lost or pan burned during "+(pass==0?"one 0.6s delta":"36 fixed deltas")+".");
                Need(fixture.stage.WorkAudit.contactCommitted&&fixture.stage.WorkAudit.contacts==1,"Expected one real hand contact in the first 0.6s.");
                Need(ResourcesOf(fixture.Session)==before,"Utensil contact repeated a stock, fuel or cash transaction.");
                Near(fixture.Session.Elapsed-began,.6f,.0001f,"Controller omitted or added a fixed step.");
                atBoundary[pass]=Observe(fixture,pass==0?"large-after-0.6":"small-after-0.6");result.observations.Add(atBoundary[pass]);
                resourceStates[pass]=ResourcesOf(fixture.Session);
                for(int frame=0;fixture.stage.WorkActorBusy&&frame<120;frame++)Tick(fixture,Step);
                BusinessWorkAudit[] history=fixture.controller.CaptureWorkHistory();
                Need(history.Length==1&&history[0].status=="committed"&&history[0].contactAt>=began,"Utensil did not finish with one committed audit event.");
                contactTimes[pass]=history[0].contactAt;
                Need(fixture.stage.WorkAudit.maxContactError<=.0251f,"Hand missed the production contact tolerance.");
                result.observations.Add(Observe(fixture,pass==0?"large-recovered":"small-recovered"));
                DisposeFixture();yield return null;
            }
            Near(atBoundary[0].elapsed,atBoundary[1].elapsed,.0001f,"Large/small delta clocks diverged.");
            Near(contactTimes[0],contactTimes[1],.0001f,"Large/small delta contact times diverged.");
            Near(atBoundary[0].actor.elapsed,atBoundary[1].actor.elapsed,.0001f,"Large/small actor timeline diverged.");
            Need(Vector3.Distance(atBoundary[0].actorPosition,atBoundary[1].actorPosition)<.0001f,"Large/small actor poses diverged.");
            Need(resourceStates[0]==resourceStates[1],"Large/small resource states differ.");
            result.detail="One .6s delta and 36 x 1/60s use identical restored state; both make one timely physical contact, preserve stock/fuel/cash, and match clock/contact/actor progress.";
        }
        static IEnumerator RemainingDeadline(CaseResult result)
        {
            result.fixture="Same real-stage checkpoint; only the saved remaining-green value is set to exactly 0.1s to isolate the query boundary. No contact-success claim is made by this query case.";
            IEnumerator preparation=EnsureCheckpoints();while(preparation.MoveNext())yield return preparation.Current;
            BusinessSaveData data=Disk(greenCheckpoint);data.lanes[0].remaining=.1f;
            Fixture fixture=CreateFixture(data);yield return null;
            float before=fixture.Session.Elapsed;string resources=ResourcesOf(fixture.Session);
            Near(fixture.controller.SecondsUntilHeatDeadline(0),.1f,.000001f,"Deadline query returned the full green window.");
            Need(fixture.Session.Elapsed==before&&ResourcesOf(fixture.Session)==resources,"Read-only deadline query changed business state.");
            result.observations.Add(Observe(fixture,"remaining-0.1"));
            result.detail="A real Utensil lane with 0.1s remaining reports 0.1s, with no clock/resource mutation.";
        }
        static IEnumerator Departure(CaseResult result)
        {
            result.fixture="Natural heating checkpoint with real vendor already at pan. Existing customer's finite remaining patience is shortened to 0.2s before restore so departure precedes the green window; no patience extension or frozen clock.";
            IEnumerator preparation=EnsureCheckpoints();while(preparation.MoveNext())yield return preparation.Current;
            BusinessSaveData data=Disk(heatingCheckpoint);int orderId=data.lanes[0].orderId;
            StallOrder saved=Array.Find(data.orders,x=>x.id==orderId);Need(saved!=null,"Heating fixture has no bound order.");saved.patience=.2f;
            Fixture fixture=CreateFixture(data);yield return null;
            string before=ResourcesOf(fixture.Session);int portions=fixture.Session.Lanes[0].batchRemaining;
            Need(fixture.controller.UseCookware(0),"Pre-departure future utensil appointment rejected.");
            result.observations.Add(Observe(fixture,"booked-before-departure"));
            bool sawDeparture=false;
            for(int frame=0;frame<360;frame++)
            {
                Tick(fixture,Step);StallOrder order=fixture.Session.FindOrder(orderId);
                if(order.departed&&!sawDeparture)
                {
                    sawDeparture=true;
                    Need(fixture.controller.HasPendingIntent(StallInteractionKind.Cookware,0),"Customer departure cancelled the still-valid pan appointment.");
                    result.observations.Add(Observe(fixture,"departed-appointment-kept"));
                }
                if(fixture.Session.Lanes[0].step==KitchenStep.ReadyStock&&!fixture.stage.WorkActorBusy)break;
                Need(fixture.Session.Lanes[0].step!=KitchenStep.Dirty,"Saved-food appointment burned after the customer left.");
                if(frame%60==0)yield return null;
            }
            Need(sawDeparture&&fixture.Session.FindOrder(orderId).departed,"Finite patience did not expire.");
            Need(fixture.Session.Lanes[0].step==KitchenStep.ReadyStock&&fixture.Session.Lanes[0].batchRemaining==portions,"The departed customer's food was not rescued as cooked stock.");
            Need(fixture.Session.WastedPortions==0&&fixture.Session.Served==0&&ResourcesOf(fixture.Session)==before,"Rescue discarded stock, minted a sale, or repeated debit.");
            BusinessWorkAudit[] history=fixture.controller.CaptureWorkHistory();
            Need(history.Length==1&&history[0].status=="committed"&&history[0].contactAt>=0,"Departure rescue lacks a physical committed contact.");
            result.observations.Add(Observe(fixture,"rescued-cooked-stock"));
            result.detail="An appointment queued before the customer leaves survives departure and physically saves the paid batch as ReadyStock, with no extra debit or fictitious sale.";
        }
        static IEnumerator Redirect(CaseResult result)
        {
            result.fixture="Two valid production-debited batches (V01 rice lane0, V09/U12 soup lane1). Restored edge fixture places both in Heating, with 1s and 5s remaining; normal 2.5s/5s green windows and standard finite patience. Default authored vendor start; no position assignments after fixture creation.";
            BusinessSaveData data=ModelCheckpoint(true);
            data.lanes[0].step=KitchenStep.Heating;data.lanes[0].remaining=1;data.lanes[0].duration=BusinessSession.Recipes[0].heatSeconds/1.25f;
            data.lanes[1].step=KitchenStep.Heating;data.lanes[1].remaining=5;data.lanes[1].duration=BusinessSession.Recipes[3].heatSeconds/1.25f;
            Fixture fixture=CreateFixture(data);yield return null;
            string before=ResourcesOf(fixture.Session);
            Need(fixture.controller.UseCookware(1),"First future pan appointment rejected.");
            for(int frame=0;frame<3;frame++)Tick(fixture,Step);
            Need(PositioningLane(fixture)==1&&fixture.stage.WorkActorBusy,"First appointment did not create a live pure approach.");
            Need(!fixture.stage.WorkAudit.carryingVisualProp&&!fixture.stage.WorkAudit.contactCommitted,"Approach already holds a prop or committed contact; redirection would not be a pure movement test.");
            int priorRequest=fixture.stage.WorkAudit.requestId;Vector3 priorPosition=fixture.stage.WorkActorPosition;
            result.observations.Add(Observe(fixture,"approaching-less-urgent-soup",1));
            Need(fixture.controller.SecondsUntilHeatDeadline(0)+.05f<fixture.controller.SecondsUntilHeatDeadline(1),"Fixture does not have an earlier rice deadline.");
            Need(fixture.controller.UseCookware(0),"More urgent pan appointment rejected.");
            Need(PositioningLane(fixture)==0&&fixture.stage.WorkActorBusy,"Pure approach did not redirect to the newly queued urgent pan.");
            Need(fixture.stage.WorkAudit.requestId>priorRequest,"Redirection did not issue a new actual actor route.");
            Need(Vector3.Distance(priorPosition,fixture.stage.WorkActorPosition)<.000001f,"Enqueue/redirection moved the actor without simulation.");
            Need(!fixture.stage.WorkAudit.carryingVisualProp&&ResourcesOf(fixture.Session)==before,"Redirect changed a prop or business resources.");
            result.observations.Add(Observe(fixture,"redirected-to-urgent-rice"));
            for(int frame=0;frame<900;frame++)
            {
                Tick(fixture,Step);
                Need(fixture.Session.Lanes[0].step!=KitchenStep.Dirty&&fixture.Session.Lanes[1].step!=KitchenStep.Dirty,"A timely queued pan burned after pure-approach redirection.");
                if(fixture.controller.CaptureWorkHistory().Length>=2&&!fixture.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            BusinessWorkAudit[] history=fixture.controller.CaptureWorkHistory();
            Need(history.Length==2&&history[0].lane==0&&history[1].lane==1,"Actual contacts did not complete urgent rice then soup exactly once.");
            foreach(var work in history)Need(work.status=="committed"&&work.contactAt>=work.startedAt,"Redirected appointment lacked a committed contact.");
            Need(fixture.Session.Lanes[0].step==KitchenStep.Season&&fixture.Session.Lanes[1].step==KitchenStep.Season,"Both rescued pans did not reach the normal next step.");
            Need(ResourcesOf(fixture.Session)==before&&fixture.Session.WastedPortions==0,"Redirection repeated a debit or discarded portions.");
            result.observations.Add(Observe(fixture,"both-appointments-committed"));
            result.detail="A real collision-aware move to soup is reissued toward the earlier rice deadline without a prop/transaction/instant move; actual contacts complete rice first, then soup, preserving both batches.";
        }
        static void CaptureError(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Near(float a,float b,float tolerance,string message)
        {Need(!float.IsNaN(a)&&!float.IsNaN(b)&&Mathf.Abs(a-b)<=tolerance,message+" ("+a.ToString("R")+" vs "+b.ToString("R")+")");}
        static void Finish(string failure)
        {
            DisposeFixture();Application.logMessageReceived-=CaptureError;
            report.failure=failure;report.engineErrors=errors.ToArray();
            foreach(var item in report.cases)
            {if(item.status=="passed")report.passed++;else if(item.status=="pending")report.pending++;else report.failed++;}
            report.success=failure==""&&report.engineErrors.Length==0&&report.passed==4&&report.failed==0&&report.pending==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("BATCH1_COOKING_"+(report.success?"PASS":"FAIL")+" "+output+" "+failure);
            int code=report.success?0:report.failed==0&&failure==""&&errors.Count==0&&report.pending>0?2:1;
            if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode();
        }
    }
}
