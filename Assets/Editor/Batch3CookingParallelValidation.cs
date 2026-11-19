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
    // Explicit restored-state fixtures; production Stage/actor/contact/collision,
    // finite normal patience. These are not hardware-input or campaign evidence.
    [InitializeOnLoad] public static class Batch3CookingParallelValidation
    {
        [Serializable] public sealed class Case
        {
            public string id,status="running",fixture,detail="",failure="";
            public int sales;public float elapsed;
            public BusinessAuditState final;
            public BusinessWorkAudit[] work;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.batch3.parallel.v1";
            public string scope="Production model and isolated real-stage restored fixtures; explicitly requested commands, finite normal patience, unchanged collision/actor duration. Not human input or complete-day acceptance.";
            public bool success,hardwareInput=false,collisionDisabled=false,infinitePatience=false;
            public int passed,failed,expectedSubscriberErrors;public string failure="";
            public List<Case> cases=new List<Case>();public string[] engineErrors;
        }
        sealed class Fixture
        {
            public GameObject root;public BusinessController controller;public StreetStallStage stage;
            public BusinessSession Session=>controller.Session;
            public readonly List<string> failures=new List<string>();
        }
        const float Step=1f/60;
        const string Pending="Guochao.Cook3.Pending",Output="Guochao.Cook3.Output";
        static readonly List<string> errors=new List<string>();
        static IEnumerator running;static Fixture live;static Report report;static string output;static double began;static bool expectObserverFailure;
        static Batch3CookingParallelValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--batch3-cooking-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --batch3-cooking-output <new report.json>");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Refusing to replace prior evidence");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();began=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;running=Cases();}
            if(running==null)return;
            try
            {
                if(EditorApplication.timeSinceStartup-began>240)throw new TimeoutException("Parallel validation exceeded 240 seconds");
                if(!running.MoveNext()){running=null;Finish("");}
            }
            catch(Exception ex){running=null;Finish(ex.ToString());}
        }
        static IEnumerator Cases()
        {
            Need(GameFlow.Instance==null,"Isolated empty PlayMode scene required");
            string[] names={"automatic-heat-releases-vendor","cross-pan-dependent-queue","pure-approach-reconsiders-ready-work","save-clears-uncommitted-dependencies","departure-expires-order-bound-chain","reserved-ready-stock-package","sale-event-exact-once"};
            Func<Case,IEnumerator>[] tests={AutomaticHeat,DependentQueue,RedirectReady,SaveDependencies,Departure,ReadyStockPackage,SaleEvent};
            for(int i=0;i<tests.Length;i++)
            {
                var item=new Case{id=names[i]};report.cases.Add(item);IEnumerator test=tests[i](item);bool done=false;
                while(!done)
                {
                    object yielded=null;
                    try{if(test.MoveNext())yielded=test.Current;else{item.status="passed";done=true;}}
                    catch(Exception ex){item.status="failed";item.failure=ex.ToString();done=true;}
                    if(!done)yield return yielded;
                }
                if(live!=null){item.elapsed=live.Session.Elapsed;item.final=live.Session.CaptureAudit();item.work=live.controller.CaptureWorkHistory();}
                (test as IDisposable)?.Dispose();DisposeFixture();yield return null;
            }
        }
        static BusinessSaveData Copy(BusinessSaveData data)=>JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(data));
        static BusinessSaveData Checkpoint(bool automatic=true,bool soupCold=false,bool shortage=false)
        {
            for(int seed=12017;seed<12065;seed++)
            {
                var setup=new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=15000,seed=seed,dayDuration=300,gasPoints=45,electricityPoints=45,
                    equipment=soupCold?new HashSet<string>{"B04","V09","V16","U12"}:new HashSet<string>{"B04",automatic?"V04":"V01","V05","U01","U05"},
                    inventory=soupCold?new Dictionary<string,int>{{"egg",60},{"skewer",60},{"fruit",60},{"seasoning",60},{"P07",50},{"P08",50}}:
                        new Dictionary<string,int>{{"rice",60},{"egg",60},{"dough",60},{"skewer",60},{"seasoning",60},{"P05",50},{"P04",50}}};
                if(shortage)setup.inventory["seasoning"]=0;
                var session=new BusinessSession(setup);session.SelectedRecipes.Clear();session.SelectedRecipes.Add(soupCold?3:0);session.SelectedRecipes.Add(soupCold?5:2);
                Need(session.Open(),"Fixture hardware/menu must be valid: "+session.Message);
                StallOrder first=null,second=null;
                for(int tick=0;tick<360;tick++)
                {
                    session.Tick(.25f);first=session.Orders.Find(x=>x.recipeIndex==session.Lanes[0].recipeIndex);second=session.Orders.Find(x=>x.recipeIndex==session.Lanes[1].recipeIndex);
                    if(first!=null&&second!=null)break;if(session.Orders.Count>=setup.queueCapacity)break;
                }
                if(first==null||second==null)continue;
                foreach(StallOrder order in session.Orders)session.MarkCustomerArrived(order.id);
                return Copy(session.CaptureSave());
            }
            throw new InvalidOperationException("No finite four-slot natural order seed supplied the fixture recipes");
        }
        static Fixture Create(BusinessSaveData data)
        {
            Need(live==null,"Fixtures must not overlap");var f=new Fixture{root=new GameObject("Cook3_RealStage")};live=f;
            var session=BusinessSession.Restore(Copy(data));f.controller=f.root.AddComponent<BusinessController>();f.controller.enabled=false;
            f.stage=f.root.AddComponent<StreetStallStage>();f.stage.Initialize(session.Setup,null);f.stage.RestoreSession(session,data.presentation);
            typeof(BusinessController).GetProperty("Session").SetValue(f.controller,session);typeof(BusinessController).GetProperty("Stage").SetValue(f.controller,f.stage);
            f.stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);f.stage.WorkFailed+=message=>f.failures.Add(message);
            VendorWorkActor actor=f.root.GetComponent<VendorWorkActor>();Need(actor!=null&&actor.IsBound&&!actor.automaticTick,"Real manually-ticked actor required");
            int solids=0;foreach(Collider collider in f.root.GetComponentsInChildren<Collider>())if(collider.enabled&&!collider.isTrigger&&collider.gameObject.layer==StreetStallStage.SolidLayer)solids++;
            Need(solids>0,"Real collision must remain enabled");return f;
        }
        static void Tick(Fixture f,float dt=Step)
        {
            Physics.SyncTransforms();f.controller.AdvanceSimulation(dt);
            Need(f.failures.Count==0,"Actor failure: "+string.Join(";",f.failures));Need(string.IsNullOrEmpty(f.stage.WorkAudit.lastFailure),"Actor rejected action");
            Invariants(f.Session);Need(f.stage.WorkAudit.maxContactError<=.0251f,"Real contact missed tolerance");
        }
        static void Invariants(BusinessSession session)
        {var a=session.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"Ledger invariant");foreach(var p in a.recipeProduction)Need(p.portionsConserved,"Portion invariant "+p.recipe);}
        static StallOrder Order(BusinessSession s,int lane)=>s.Orders.Find(o=>o.recipeIndex==s.Lanes[lane].recipeIndex&&!o.departed&&!o.served);
        static string Stock(BusinessSession s)
        {var v=new List<string>();foreach(var x in s.Inventory)v.Add(x.Key+"="+x.Value);v.Sort(StringComparer.Ordinal);return string.Join(";",v)+"|"+s.Gas+"|"+s.Electricity+"|"+s.CashCents+"|"+s.Served+"|"+s.IntegrityChange;}
        static IEnumerator AutomaticHeat(Case item)
        {
            item.fixture="V04 automatic rice + normal flatbread; production-debited heat restored with two normal finite-patience arrived orders.";
            BusinessSession model=BusinessSession.Restore(Checkpoint());Need(model.BeginOrder(Order(model,0).id,0),"Begin automatic batch");
            while(model.Lanes[0].step==KitchenStep.Prep)model.Tick(Step);
            Fixture f=Create(model.CaptureSave());yield return null;
            f.controller.UseCookware(0);Need(!f.controller.HasPendingIntent(StallInteractionKind.Cookware,0),"Automatic heat created an impossible utensil appointment");
            Need(float.IsPositiveInfinity(f.controller.SecondsUntilHeatDeadline(0)),"Automatic pan exposes a fictitious manual deadline");
            Need(f.controller.SelectOrder(Order(f.Session,1).id)&&f.controller.UseIngredient("dough"),"Other pan pickup should be accepted during automatic heat");
            Need(f.stage.WorkActorBusy&&f.controller.ActiveWorkLabel.Contains("2号锅"),"Other pan did not start actual work promptly");
            float heat=f.Session.Lanes[0].remaining;Vector3 position=f.stage.WorkActorPosition;
            for(int frame=0;frame<60;frame++)Tick(f);
            Need(f.Session.Lanes[0].step==KitchenStep.Heating&&f.Session.Lanes[0].remaining<heat-.9f,"Heat stopped while another action ran");
            Need(Vector3.Distance(position,f.stage.WorkActorPosition)>.01f,"Other action has no real movement");
            item.detail="Automatic pan has no manual appointment; second pan starts real movement while first heat continues.";
        }
        static IEnumerator DependentQueue(Case item)
        {
            item.fixture="Two normal arrived orders, V04 rice + manual flatbread. Explicit future seasoning/package requests, real pickup and contact; no changes to patience or clock.";
            Fixture f=Create(Checkpoint());yield return null;int first=Order(f.Session,0).id,second=Order(f.Session,1).id;
            string before=Stock(f.Session);Need(f.controller.SelectOrder(first)&&f.controller.UseIngredient("rice"),"First pickup rejected");
            Need(f.controller.UseSeasoning(0)&&f.controller.UsePackage(0,"P05"),"Feasible dependent seasoning/package rejected before pickup contact");
            int queued=f.controller.PendingWorkCount;Need(!f.controller.UsePackage(0,"P05")&&f.controller.PendingWorkCount==queued,"Duplicate dependency admitted");
            Need(f.controller.SelectOrder(second)&&f.controller.UseIngredient("dough"),"Second-pan pickup cannot queue behind first pickup");
            Need(Stock(f.Session)==before,"Queueing committed resources before a real contact");
            bool overlap=false;float nextDecision=f.Session.Elapsed;int sales=0;f.Session.SaleCompleted+=(o,r,c,shortage)=>sales++;
            for(int frame=0;frame<32*60;frame++)
            {
                if(f.Session.Elapsed>=nextDecision)
                {
                    KitchenLane other=f.Session.Lanes[1];
                    if((other.step==KitchenStep.Prep||other.step==KitchenStep.Heating||other.step==KitchenStep.Utensil)&&!f.controller.HasPendingIntent(StallInteractionKind.Cookware,1))f.controller.UseCookware(1);
                    if((other.step==KitchenStep.Prep||other.step==KitchenStep.Heating||other.step==KitchenStep.Utensil||other.step==KitchenStep.Season)&&!f.controller.HasPendingIntent(StallInteractionKind.Seasoning,1))f.controller.UseSeasoning(1);
                    if(other.orderId>=0&&other.step!=KitchenStep.Idle&&other.step!=KitchenStep.ReadyStock&&!f.controller.HasPendingIntent(StallInteractionKind.Packaging,1))f.controller.UsePackage(1,"P04");
                    nextDecision=f.Session.Elapsed+.3f;
                }
                Tick(f);overlap|=f.Session.Lanes[0].step==KitchenStep.Heating&&f.controller.ActiveWorkLabel.Contains("2号锅");
                if(f.Session.FindOrder(first).served&&f.Session.FindOrder(second).served&&!f.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            Need(overlap,"No automatic heat overlapped other-pan work");Need(f.Session.FindOrder(first).served&&f.Session.FindOrder(second).served,"Both explicitly scheduled orders did not complete in finite patience");
            Need(sales==2&&f.Session.Served==2&&f.Session.WastedPortions==0,"Sale count or portions changed");item.sales=sales;
            foreach(var work in f.controller.CaptureWorkHistory())if(work.status=="committed")Need(work.contactAt>=work.startedAt&&work.finishedAt>work.startedAt,"Instant or repeated contact audit");
            item.detail="One real vendor interleaves two kitchens; future steps wait for prerequisites and both original orders settle once.";
        }
        static IEnumerator RedirectReady(Case item)
        {
            item.fixture="Soup heating (natural production timer) and an idle cold-drink station; real initial vendor pose, no position assignments during measurement.";
            var model=BusinessSession.Restore(Checkpoint(false,true));Need(model.BeginOrder(Order(model,0).id,0),"Soup debit");while(model.Lanes[0].step==KitchenStep.Prep)model.Tick(Step);
            Fixture f=Create(model.CaptureSave());yield return null;Need(f.controller.UseCookware(0),"Soup appointment rejected");
            for(int i=0;i<3;i++)Tick(f);Need(f.stage.WorkActorBusy&&!f.stage.WorkAudit.carryingVisualProp,"Expected pure approach before ready work arrives");
            int request=f.stage.WorkAudit.requestId;Vector3 position=f.stage.WorkActorPosition;
            Need(f.controller.SelectOrder(Order(f.Session,1).id)&&f.controller.UseIngredient("fruit"),"Ready cold-station request rejected");
            Need(f.controller.ActiveWorkLabel.Contains("2号锅")&&f.stage.WorkAudit.requestId>request,"Pure approach did not reconsider feasible ready work");
            Need(Vector3.Distance(position,f.stage.WorkActorPosition)<.000001f,"Replanning teleported vendor");
            for(int frame=0;frame<18*60;frame++)
            {Tick(f);Need(f.Session.Lanes[0].step!=KitchenStep.Dirty,"Replanning burned a previously timely soup appointment");if(f.Session.Lanes[0].step==KitchenStep.Season&&f.Session.Lanes[1].step==KitchenStep.Season&&!f.stage.WorkActorBusy)break;if(frame%60==0)yield return null;}
            Need(f.Session.Lanes[0].step==KitchenStep.Season&&f.Session.Lanes[1].step==KitchenStep.Season,"Both explicitly requested tasks did not commit");
            Need(f.Session.Served==0&&f.Session.WastedPortions==0,"No sale was requested; no portions may vanish");item.detail="A newly ready cold preparation redirects an empty approach, without teleport or interruption of held work; soup is still contacted in time.";
        }
        static IEnumerator SaveDependencies(Case item)
        {
            item.fixture="Uncommitted real first pickup plus three explicit future steps; synchronous production save before any clock/contact advance.";
            Fixture f=Create(Checkpoint(false));yield return null;string before=Stock(f.Session);int id=Order(f.Session,0).id;
            Need(f.controller.SelectOrder(id)&&f.controller.UseIngredient("rice"),"Pickup");
            Need(f.controller.UseCookware(0)&&f.controller.UseSeasoning(0)&&f.controller.UsePackage(0,"P05"),"Pre-contact dependent chain rejected");
            Need(f.controller.PendingWorkCount==3&&Stock(f.Session)==before,"Queue limit or premature debit");
            BusinessSaveData saved=f.controller.CaptureForSave();Need(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy,"Save left transient actor/queue work");
            Need(Stock(f.Session)==before,"Save committed uncontacted work");foreach(StallOrder o in f.Session.Orders)Need(o.reservedLane<0,"Save left a reservation");
            var restored=BusinessSession.Restore(Copy(saved));int notifications=0;restored.SaleCompleted+=(o,r,c,s)=>notifications++;restored.Tick(.5f);
            Need(notifications==0&&Stock(restored)==before,"Restore replayed pending work or sale");Invariants(restored);
            item.detail="Save cancels all uncommitted dependencies and reservations without inventory/fuel/cash mutation or replay.";
        }
        static IEnumerator Departure(Case item)
        {
            item.fixture="Natural automatic heat checkpoint; only remaining patience shortened to .2s to force a real expiry, never extended or frozen.";
            var model=BusinessSession.Restore(Checkpoint());int id=Order(model,0).id;Need(model.BeginOrder(id,0),"Automatic debit");while(model.Lanes[0].step==KitchenStep.Prep)model.Tick(Step);
            BusinessSaveData data=model.CaptureSave();Array.Find(data.orders,o=>o.id==id).patience=.2f;
            Fixture f=Create(data);yield return null;string before=Stock(f.Session);int portion=f.Session.Lanes[0].batchRemaining;
            Need(f.controller.UseSeasoning(0)&&f.controller.UsePackage(0,"P05"),"Future chain rejected");int sales=0;f.Session.SaleCompleted+=(o,r,c,s)=>sales++;
            for(int frame=0;frame<8*60;frame++){Tick(f);if(f.Session.Lanes[0].step==KitchenStep.ReadyStock)break;if(frame%60==0)yield return null;}
            Need(f.Session.FindOrder(id).departed&&f.controller.PendingWorkCount==0,"Departed order retained future dependencies");
            Need(f.Session.Lanes[0].step==KitchenStep.ReadyStock&&f.Session.Lanes[0].batchRemaining==portion,"Automatic food was lost");
            Need(Stock(f.Session)==before&&sales==0,"Expired chain mutated stock/cash or emitted a sale");item.detail="Order-bound future steps expire; automatic food survives for the next customer without a fictitious sale.";
        }
        static IEnumerator ReadyStockPackage(Case item)
        {
            item.fixture="Real bulk soup batch: first portion cooked/seasoned/sold through model, then a naturally generated same-recipe customer reserves its already-seasoned remainder. Real scoop/package actor.";
            var model=BusinessSession.Restore(Checkpoint(false,true));int first=Order(model,0).id;
            Need(model.BeginOrder(first,0),"Soup debit");while(model.Lanes[0].step!=KitchenStep.Utensil)model.Tick(Step);
            Need(model.Action(0)&&model.Action(0)&&model.Serve(0,"P07"),"Initial soup portion");
            StallOrder next=Order(model,0);
            for(int i=0;next==null&&i<240;i++){model.Tick(.25f);next=Order(model,0);}
            Need(next!=null,"No naturally generated replacement soup customer");model.MarkCustomerArrived(next.id);
            Need(model.Lanes[0].step==KitchenStep.ReadyStock&&model.Lanes[0].seasoned,"Expected seasoned real remaining batch");
            Fixture f=Create(model.CaptureSave());yield return null;string before=Stock(f.Session);int portions=f.Session.Lanes[0].batchRemaining,sales=0;
            f.Session.SaleCompleted+=(o,r,c,b)=>sales++;
            Need(f.controller.SelectOrder(next.id)&&f.controller.UseCookware(0),"Real stock scoop reservation");
            Need(!f.controller.UseSeasoning(0),"Seasoned stock admitted unnecessary seasoning");
            Need(f.controller.UsePackage(0,"P07"),"Reserved stock cannot queue its package before scoop contact");
            Need(Stock(f.Session)==before,"Stock reservation prematurely settled");
            for(int frame=0;frame<15*60;frame++){Tick(f);if(f.Session.FindOrder(next.id).served&&!f.stage.WorkActorBusy)break;if(frame%60==0)yield return null;}
            Need(f.Session.FindOrder(next.id).served&&sales==1&&f.Session.Lanes[0].batchRemaining==portions-1,"Reserved stock chain did not settle one real portion");
            item.sales=sales;item.detail="A reserved seasoned portion can explicitly queue packaging before scoop contact, without re-seasoning, batch re-debit, or duplicate sale.";
        }
        static IEnumerator SaleEvent(Case item)
        {
            item.fixture="Pure production BusinessSession, real BeginOrder/Action/Serve and JSON save/restore; no 3D claim. Normal and missing-seasoning variants.";
            foreach(bool shortage in new[]{false,true})
            {
                var s=BusinessSession.Restore(Checkpoint(false,false,shortage));int id=Order(s,0).id;Need(s.BeginOrder(id,0),"Model debit");
                while(s.Lanes[0].step!=KitchenStep.Utensil)s.Tick(Step);Need(s.Action(0)&&s.Action(0),"Model complete heat/season");
                int count=0,observedId=-1,observedRecipe=-1,observedCents=-1;bool observedShort=false,ledgerPublished=false;
                int cash=s.CashCents,revenue=s.RevenueCents,served=s.Served,revision=s.Revision;
                s.SaleCompleted+=(o,r,c,b)=>throw new InvalidOperationException("Intentional validation subscriber failure");
                s.SaleCompleted+=(o,r,c,b)=>{count++;observedId=o;observedRecipe=r;observedCents=c;observedShort=b;ledgerPublished=s.FindOrder(o).served&&s.CashCents==cash+c&&s.Served==served+1&&s.Revision>revision;};
                string before=Stock(s);Need(!s.Serve(0,"P02")&&count==0&&Stock(s)==before,"Empty compatible package emitted/debited a sale");
                bool success;expectObserverFailure=true;try{success=s.Serve(0,"P05");}finally{expectObserverFailure=false;}
                Need(success,"Sale observer exception escaped committed transaction");
                Need(count==1&&observedId==id&&observedRecipe==0&&observedShort==shortage&&observedCents==s.CashCents-cash&&observedCents==s.RevenueCents-revenue&&ledgerPublished,"Sale payload/ordering/observer isolation mismatch");
                string sold=Stock(s);Need(!s.Serve(0,"P05")&&count==1&&Stock(s)==sold,"Duplicate Serve replayed a sale");
                var restored=BusinessSession.Restore(Copy(s.CaptureSave()));int replay=0;restored.SaleCompleted+=(o,r,c,b)=>replay++;
                Need(!restored.Serve(0,"P05"),"Restore admitted already served order");restored.Tick(.1f);Need(replay==0&&Stock(restored)==sold,"Restore replayed settlement");Invariants(s);Invariants(restored);item.sales+=count;
            }
            item.detail="Successful settlement emits once after ledger mutation, failed/duplicate/restored transactions emit zero, and a throwing subscriber does not block the next observer.";yield break;
        }
        static void DisposeFixture(){if(live==null)return;var f=live;live=null;f.stage?.Release();if(f.root)UnityEngine.Object.DestroyImmediate(f.root);}
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Log(string condition,string trace,LogType type)
        {
            if(expectObserverFailure&&condition.Contains("Intentional validation subscriber failure")){report.expectedSubscriberErrors++;return;}
            if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(condition+"\n"+trace);
        }
        static void Finish(string failure)
        {
            DisposeFixture();Application.logMessageReceived-=Log;report.failure=failure;
            foreach(Case item in report.cases){if(item.status=="passed")report.passed++;else report.failed++;}
            report.engineErrors=errors.ToArray();report.success=failure==""&&report.failed==0&&report.passed==7&&errors.Count==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
