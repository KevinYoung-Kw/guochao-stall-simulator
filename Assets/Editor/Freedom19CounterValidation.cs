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
    // FREEDOM19 counter acceptance: real free-batch cooking, real counter packaging
    // (PackToCounter debits exactly one package + one pan portion at actor contact),
    // explicit SelectCounterMeal -> real actor delivery -> original payment path.
    // No automatic delivery, no Session.Serve shortcut, no fake products/orders.
    // Explicitly scripted instructions against real Stage/actor; not hardware input
    // or a full human-played day. All engine results pending verification.
    [InitializeOnLoad] public static class Freedom19CounterValidation
    {
        [Serializable] public sealed class MealSnapshot
        {
            public string id,packageId,flavorId,name,method;
            public int portions,remaining,priceCents;public float quality;
            public bool shortSeasoning;public string inputs="";
        }
        [Serializable] public sealed class Case
        {
            public string id,status="running",fixture,detail="",failure="";
            public float elapsed;public int sales;public MealSnapshot meal;
            public BusinessAuditState final;public BusinessWorkAudit[] work;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.freedom19.counter.v1";
            public string scope="Real Editor PlayMode Stage/actor acceptance with scripted Controller commands: stir-fry sale, independent counter focus, pending-save cancellation, cold-mix and reheat finishing/packing. Finite normal patience and unchanged collision/heat; no OS hardware input or full human-played day.";
            public bool success,hardwareInput=false,collisionDisabled=false,infinitePatience=false;
            public int passed,failed;public string failure="";
            public List<Case> cases=new List<Case>();public string[] engineErrors;
            public List<VisualFrame> screenshots=new List<VisualFrame>();
        }
        [Serializable] public sealed class VisualFrame
        {public string name,path;public int width,height,meals,visibleMealTargets,slotCount;public bool allVisibleMealCentersPicked,packageBoundsDisjoint;}
        sealed class Fixture
        {
            public GameObject root;public BusinessController controller;public StreetStallStage stage;
            public BusinessSession Session=>controller.Session;
            public readonly List<string> failures=new List<string>();
        }
        const float Step=1f/60;
        const string Pending="Guochao.F19.Counter.Pending",Output="Guochao.F19.Counter.Output";
        static readonly List<string> errors=new List<string>();
        static Feedback3CoroutineHost frameHost;static Fixture live;static Report report;static string output;static double began;static bool withUI;
        static Freedom19CounterValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--freedom19-counter-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --freedom19-counter-output <new report.json>");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Refusing to replace prior evidence");
            SessionState.SetString(Output,path);SessionState.SetBool("Guochao.F19.Counter.UI",Array.IndexOf(args,"--freedom19-counter-ui")>=0);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();withUI=SessionState.GetBool("Guochao.F19.Counter.UI",false);errors.Clear();began=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;
                frameHost=new GameObject("Freedom19_PlayerFrameHost").AddComponent<Feedback3CoroutineHost>();
                frameHost.Launch(Cases(),()=>Finish(""),ex=>Finish(ex.ToString()));
            }
            if(frameHost!=null&&frameHost.IsRunning&&EditorApplication.timeSinceStartup-began>420)
            {frameHost.Abort();Finish("Counter validation exceeded 420 seconds");}
        }
        static IEnumerator Cases()
        {
            Need(GameFlow.Instance==null,"Isolated empty PlayMode scene required");
            string[] names={"free-batch-counter-sale","counter-meal-independent-focus","save-cancels-uncommitted-prep","cold-mix-controller-finishing","reheat-controller-finishing"};
            Func<Case,IEnumerator>[] tests={CounterSale,IndependentFocus,SaveCancels,ColdMixFinishing,ReheatFinishing};
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
        static BusinessSetup Setup()
        {
            // B03 is the real gas burner compatible with stir_fry (baseIds B04/B03/B02/B01),
            // V01 wok + U01 spatula per FreeKitchenRules.Methods; gas is the real energy.
            return new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=15000,seed=12017,dayDuration=300,gasPoints=45,electricityPoints=45,
                equipment=new HashSet<string>{"B03","V01","U01"},
                inventory=new Dictionary<string,int>{{"egg",4},{"meat",2},{"seasoning",30},{"P05",30}}};
        }
        static Fixture Create(BusinessSetup customSetup=null)
        {
            Need(live==null,"Fixtures must not overlap");var f=new Fixture{root=new GameObject("F19_Counter_RealStage")};live=f;
            var session=new BusinessSession(customSetup??Setup());session.SelectedRecipes.Clear();
            Need(session.Open(),"Free-batch session must open with no SelectedRecipes gate: "+session.Message);
            f.controller=f.root.AddComponent<BusinessController>();f.controller.enabled=false;
            f.stage=f.root.AddComponent<StreetStallStage>();
            f.stage.Initialize(session.Setup,null);
            f.stage.Configure(session);
            typeof(BusinessController).GetProperty("Session").SetValue(f.controller,session);
            typeof(BusinessController).GetProperty("Stage").SetValue(f.controller,f.stage);
            var input=f.root.AddComponent<StallInteractionInput>();input.Initialize(f.controller);input.enabled=false;
            typeof(BusinessController).GetProperty("InputHandler").SetValue(f.controller,input);
            if(withUI)
            {
                if(UnityEngine.EventSystems.EventSystem.current==null)
                {var events=new GameObject("CounterTest_EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.EventSystems.StandaloneInputModule));events.transform.SetParent(f.root.transform);}
                var hud=f.root.AddComponent<BusinessHUD>();typeof(BusinessController).GetProperty("HUD").SetValue(f.controller,hud);
                hud.Initialize(f.controller,session);hud.ShowBusiness();
            }
            f.stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);
            f.stage.WorkFailed+=message=>f.failures.Add(message);
            VendorWorkActor actor=f.root.GetComponent<VendorWorkActor>();Need(actor!=null&&actor.IsBound&&!actor.automaticTick,"Real manually-ticked actor required");
            int solids=0;foreach(Collider collider in f.root.GetComponentsInChildren<Collider>())if(collider.enabled&&!collider.isTrigger&&collider.gameObject.layer==StreetStallStage.SolidLayer)solids++;
            Need(solids>0,"Real collision must remain enabled");return f;
        }
        static void Tick(Fixture f,float dt=Step)
        {
            Physics.SyncTransforms();f.controller.AdvanceSimulation(dt);
            Need(f.failures.Count==0,"Actor failure: "+string.Join(";",f.failures));
            Need(string.IsNullOrEmpty(f.stage.WorkAudit.lastFailure),"Actor rejected action");
            Invariants(f.Session);Need(f.stage.WorkAudit.maxContactError<=.0251f,"Real contact missed tolerance");
        }
        static void Invariants(BusinessSession session)
        {var a=session.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"Ledger invariant");}
        static string Stock(BusinessSession s)
        {var v=new List<string>();foreach(var x in s.Inventory)v.Add(x.Key+"="+x.Value);v.Sort(StringComparer.Ordinal);return string.Join(";",v)+"|"+s.Gas+"|"+s.Electricity+"|"+s.CashCents+"|"+s.Served;}
        static int UnitsOf(KitchenLane lane,string itemId)
        {if(lane.freeBatch==null||lane.freeBatch.inputs==null)return 0;foreach(var input in lane.freeBatch.inputs)if(input.itemId==itemId)return input.units;return 0;}
        // Cook one real free batch (egg+meat, stir_fry) to a packable state.
        // Returns after product exists at the lane; pan may still hold remaining portions.
        static IEnumerator CookFreeBatchToReady(Fixture f,Case item,int lane)
        {
            int eggBefore=f.Session.Inventory["egg"],meatBefore=f.Session.Inventory["meat"];
            string before=Stock(f.Session);int gasBefore=f.Session.Gas;
            Need(f.controller.UseIngredientAtLane(lane,"egg"),"First free ingredient rejected");
            Need(f.controller.UseIngredientAtLane(lane,"meat"),"Second compatible ingredient rejected");
            Need(Stock(f.Session)==before,"Ingredients debited before real actor contact");
            Need(f.controller.SelectedOrderId==-1,"Free batch must never select an order");
            // Early heat appointment while the vendor carries inputs.
            Need(f.controller.UseCookware(lane),"Early cookware appointment rejected while collecting");
            // Wait until BOTH inputs really contacted: egg=1 AND meat=1 in the batch;
            // meanwhile each raw stock may drop by at most one unit.
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                var l=f.Session.Lanes[lane];
                if(UnitsOf(l,"egg")==1&&UnitsOf(l,"meat")==1)break;
                Need(eggBefore-f.Session.Inventory["egg"]<=1&&meatBefore-f.Session.Inventory["meat"]<=1,"More than one unit debited per ingredient before batch assembled");
                if(frame%60==0)yield return null;
            }
            var assembled=f.Session.Lanes[lane];
            Need(UnitsOf(assembled,"egg")==1&&UnitsOf(assembled,"meat")==1,"Batch never assembled both inputs at real contact");
            Need(f.controller.SetFreeCookingMethod(lane,FreeKitchenRules.MethodStirFry),"Choose actual stir-fry while the two real inputs are still in Prep");
            Need(assembled.freeBatch.method==FreeKitchenRules.MethodStirFry,"Lane not on the real stir_fry method: "+assembled.freeBatch.method);
            Need(f.controller.SelectedOrderId==-1,"SelectedOrderId drifted during prep");
            // Cook: stage-aware cookware taps (heating appointment / utensil action) inside the
            // real windows; never extend patience or freeze the clock.
            bool energySettled=false;float deadline=f.Session.Elapsed+90f;
            while(true)
            {
                var l=f.Session.Lanes[lane];
                if(l.freeBatch!=null&&l.freeBatch.product!=null)break;
                Need(f.Session.Elapsed<deadline,"Cooking never produced a product within the real windows");
                if((l.step==KitchenStep.Heating||l.step==KitchenStep.Utensil)&&!f.controller.HasPendingIntent(StallInteractionKind.Cookware,lane))
                    f.controller.UseCookware(lane);
                if(!energySettled&&f.Session.Gas<gasBefore)energySettled=true;
                Tick(f);yield return null;
            }
            Need(energySettled&&f.Session.Gas==gasBefore-1,"StartFreeCooking must consume exactly one gas unit on B03");
            // Finish remaining real steps: utensil/season only in their real windows.
            deadline=f.Session.Elapsed+90f;
            while(true)
            {
                var l=f.Session.Lanes[lane];
                if(l.step==KitchenStep.Pack||l.step==KitchenStep.ReadyStock)break;
                Need(f.Session.Elapsed<deadline,"Cooking never reached a packable state");
                if((l.step==KitchenStep.Heating||l.step==KitchenStep.Utensil)&&!f.controller.HasPendingIntent(StallInteractionKind.Cookware,lane))
                    f.controller.UseCookware(lane);
                if(l.step==KitchenStep.Season&&!f.controller.HasPendingIntent(StallInteractionKind.Seasoning,lane))
                    f.controller.UseSeasoning(lane);
                Tick(f);yield return null;
            }
            FoodProduct product=f.Session.ProductAtLane(lane);
            Need(product!=null,"No real product created from actual inputs");
            Need(product.method==FreeKitchenRules.MethodStirFry,"Product method mismatch: "+product.method);
            item.meal=new MealSnapshot{id="pending",name=f.Session.LaneDishName(lane),method=product.method,portions=product.portions,remaining=product.remaining,priceCents=product.priceCents,quality=product.quality};
            foreach(var input in product.inputs)item.meal.inputs+=(item.meal.inputs==""?"":";")+input.itemId+"x"+input.units;
            item.detail="egg×1+meat×1 stir_fry cooked to '"+item.meal.name+"' portions="+product.portions+" remaining="+product.remaining+" price="+product.priceCents+"c via real actor contact.";
        }
        // Pack one lane portion to the counter via the real actor; asserts exactly one
        // package and exactly one portion debit, cash unchanged. Returns the meal.
        static IEnumerator PackOneToCounter(Fixture f,Case item,int lane,CounterMeal[] sink,string packageId="P05")
        {
            string before=Stock(f.Session);int cash=f.Session.CashCents;
            int packBefore=f.Session.Inventory.ContainsKey(packageId)?f.Session.Inventory[packageId]:-1;
            int mealsBefore=f.Session.ReadyMeals.Count;
            FoodProduct beforeProduct=f.Session.ProductAtLane(lane);
            int beforeRemaining=beforeProduct!=null?beforeProduct.remaining:f.Session.Lanes[lane].batchRemaining;
            Need(f.controller.UseServingCounter(lane),"Counter packaging command rejected on cooked lane");
            bool settled=false;
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(f.Session.ReadyMeals.Count==mealsBefore+1){settled=true;break;}
                if(frame%60==0)yield return null;
            }
            Need(settled,"PackToCounter never placed a real meal on the counter");
            Need(f.stage.LastCounterPlacementError<=.025f,"Packed container did not reach its supported resting pose: "+f.stage.LastCounterPlacementError.ToString("F4")+"m");
            Need(f.Session.ReadyMeals.Count==mealsBefore+1,"ReadyMeals count mismatch after real contact");
            Need(f.Session.Inventory.ContainsKey(packageId)&&f.Session.Inventory[packageId]==packBefore-1,"Exactly one package must debit at counter contact");
            FoodProduct afterProduct=f.Session.ProductAtLane(lane);
            int afterRemaining=afterProduct!=null?afterProduct.remaining:f.Session.Lanes[lane].batchRemaining;
            Need(afterRemaining==beforeRemaining-1,"Exactly one pan portion must debit at counter contact ("+beforeRemaining+"->"+afterRemaining+")");
            Need(f.Session.CashCents==cash,"Packaging to counter must not change cash");
            CounterMeal meal=f.Session.ReadyMeals[f.Session.ReadyMeals.Count-1];
            Need(meal!=null&&meal.product!=null,"Counter meal lacks its independent product snapshot");
            Need(meal.priceCents>=0,"Counter meal missing a real quote");
            // Real visible meal spot: active GameObject, live renderer, hit collider, id bound.
            int mealTargets=0;foreach(var target in f.root.GetComponentsInChildren<StallInteractionTarget>(true))
                if(target.kind==StallInteractionKind.CounterMeal&&target.itemId==meal.id&&target.gameObject.activeInHierarchy
                    &&target.GetComponentInChildren<Renderer>()!=null&&target.GetComponent<Collider>()!=null)mealTargets++;
            Need(mealTargets>0,"No active rendered CounterMeal target bound to the real meal id");
            for(int frame=0;f.stage.WorkActorBusy&&frame<12*60;frame++){Tick(f);if(frame%60==0)yield return null;}
            Need(!f.stage.WorkActorBusy,"Packaging contact completed but actor never finished placing the meal");
            sink[0]=meal;yield break;
        }
        static IEnumerator CounterSale(Case item)
        {
            item.fixture="egg+meat free batch on real B03(gas)/V01/U01; explicit stir_fry cook, real counter packaging to ReadyMeals, then explicit SelectCounterMeal + real actor delivery to the first naturally arrived compatible customer. No auto delivery.";
            Fixture f=Create();yield return null;int lane=0;
            IEnumerator cook=CookFreeBatchToReady(f,item,lane);while(cook.MoveNext())yield return cook.Current;
            var first=new CounterMeal[1];IEnumerator pack=PackOneToCounter(f,item,lane,first);while(pack.MoveNext())yield return pack.Current;
            CounterMeal meal=first[0];
            item.meal.id=meal.id;item.meal.packageId=meal.packageId;item.meal.flavorId=meal.flavorId;item.meal.shortSeasoning=meal.shortSeasoning;
            // Guarded mismatch: delivery before selection must not deliver or move money.
            int cashMid=f.Session.CashCents;int saleCents=0;int sales=0;f.Session.SaleCompleted+=(o,r,c,s)=>{sales++;saleCents=c;};
            Need(!f.controller.DeliverCounterMealTo(-1),"Delivery without a selection must be rejected");
            Need(f.Session.CashCents==cashMid&&f.Session.ReadyMeals.Count==1,"Rejected delivery mutated cash or dropped the meal");
            Need(f.controller.SelectCounterMeal(meal.id),"Selecting the real counter meal rejected");
            Need(f.controller.SelectedCounterMealId==meal.id,"SelectedCounterMealId does not echo the meal");
            if(withUI){IEnumerator capture=CaptureBoth(f,"selected-meal");while(capture.MoveNext())yield return capture.Current;}
            // Wait for a natural compatible customer (recommendation is read-only). Earlier
            // customers may finish and leave; later natural arrivals still count.
            int orderId=-1;float deadline=f.Session.Elapsed+120f;
            while(orderId<0)
            {
                Tick(f);yield return null;
                orderId=f.Session.FindServingCounterOrder(meal.id);
                Need(f.Session.Elapsed<deadline,"No naturally arrived compatible customer within 120s; meal must be retained, not auto-sold");
            }
            Need(f.Session.ReadyMeals.Count==1,"Meal vanished before real delivery");
            int revenue=f.Session.RevenueCents,servedCount=f.Session.Served,packagesBeforeDelivery=f.Session.Inventory["P05"];
            Need(f.controller.DeliverCounterMealTo(orderId),"Real actor delivery to the recommended order rejected");
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(f.Session.ReadyMeals.Count==0)break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.ReadyMeals.Count==0,"Meal not removed by the single real delivery");
            var order=f.Session.FindOrder(orderId);
            Need(order.served,"Original order not marked served through the real payment path");
            Need(sales==1&&f.Session.Served==servedCount+1,"Exactly one sale event expected");
            // Use the existing risk ledger, rather than guessing fields on an order.
            bool riskPending=f.Session.HasPendingRiskOrder(orderId);
            Need(saleCents==f.Session.CashCents-cashMid,"Sale event cents must equal the real cash delta");
            Need(f.Session.RevenueCents==revenue+saleCents,"Revenue must match the sale event amount");
            if(!riskPending)Need(saleCents==meal.priceCents,"A normally settled meal must pay its real quote "+meal.priceCents);
            else item.detail+=" [risk] first compatible customer was a risk order; settlement follows the existing risk path (event="+saleCents+"c).";
            Need(f.Session.Inventory["P05"]==packagesBeforeDelivery,"Delivery debited a second package");
            Need(f.stage.WorkAudit.pathMetres>0,"No physical movement recorded during cooking and delivery");
            Need(f.Session.CaptureAudit().packagesConserved,"Package conservation broken");
            if(withUI){IEnumerator capture=CaptureBoth(f,"after-delivery");while(capture.MoveNext())yield return capture.Current;}
            item.sales=sales;
            item.detail+=" Counter packaging consumed one package + one portion; explicit selection; real actor delivery paid "+saleCents+"c once; meal removed.";
        }
        static IEnumerator IndependentFocus(Case item)
        {
            item.fixture="Pack every remaining portion of the first batch to the counter (<=6 meals), start a second one-egg batch on the freed lane; waiting meals, selection, and cash must stay independent; mismatch/departed delivery calls are no-ops.";
            Fixture f=Create();yield return null;int lane=0;
            IEnumerator cook=CookFreeBatchToReady(f,item,lane);while(cook.MoveNext())yield return cook.Current;
            FoodProduct product=f.Session.ProductAtLane(lane);Need(product!=null,"No product for first batch");
            int originalPortions=product.remaining;
            Need(originalPortions>=1&&originalPortions<=BusinessSession.CounterCapacity,"First batch exceeds physical counter capacity");
            for(int p=0;p<originalPortions;p++){var sink=new CounterMeal[1];IEnumerator pack=PackOneToCounter(f,item,lane,sink);while(pack.MoveNext())yield return pack.Current;}
            Need(f.Session.ReadyMeals.Count==originalPortions,"Not all real portions reached the counter");
            Need(f.Session.ProductAtLane(lane)==null||f.Session.Lanes[lane].batchRemaining==0||f.Session.Lanes[lane].step==KitchenStep.Idle,"Pan still holds portions after packing all of them");
            CounterMeal meal=f.Session.ReadyMeals[0];string before=Stock(f.Session);int eggBefore=f.Session.Inventory["egg"];
            Need(f.controller.SelectCounterMeal(meal.id),"First meal selection rejected before second production");
            IEnumerator secondCook=CookFreeBatchToReady(f,item,lane);while(secondCook.MoveNext())yield return secondCook.Current;
            Need(f.controller.SelectedCounterMealId==meal.id,"Cooking changed the selected counter meal");
            var secondMeal=new CounterMeal[1];IEnumerator secondPack=PackOneToCounter(f,item,lane,secondMeal);while(secondPack.MoveNext())yield return secondPack.Current;
            Need(f.Session.Inventory["egg"]==eggBefore-1,"Second batch did not debit exactly one egg at contact");
            Need(f.Session.ReadyMeals.Count==originalPortions+1&&f.Session.ReadyMeals[0].id==meal.id,"Lane work disturbed the waiting meals");
            if(withUI){IEnumerator capture=CaptureBoth(f,"two-meals");while(capture.MoveNext())yield return capture.Current;}
            // Mismatched/invalid delivery calls must be no-ops for money and meals.
            int cash=f.Session.CashCents;int sales=0;f.Session.SaleCompleted+=(o,r,c,s)=>sales++;
            Need(!f.controller.DeliverCounterMealTo(-1),"Delivery to invalid order accepted");
            int gone=-1;foreach(var o in f.Session.Orders)if(o.departed){gone=o.id;break;}
            if(gone>=0)
            {
                Need(f.controller.SelectCounterMeal(meal.id),"Re-selecting the meal rejected");
                Need(!f.controller.DeliverCounterMealTo(gone),"Delivery to a departed customer accepted");
            }
            Need(f.Session.CashCents==cash&&sales==0,"Mismatched calls mutated cash or emitted a sale");
            Need(f.Session.ReadyMeals.Count==originalPortions+1&&f.Session.ReadyMeals[0].id==meal.id,"Mismatched calls dropped a meal");
            item.detail="All "+originalPortions+" portions packed (one package + one portion each); second batch contact debited one egg; meals/selection/cash unaffected by lane work and rejected calls.";
        }
        // Release regression: model-only Action tests cannot detect a controller that
        // refuses the finishing action based on a legacy visual recipe adapter.
        static IEnumerator ColdMixFinishing(Case item)=>MethodFinishing(item,FreeKitchenRules.MethodColdMix);
        static IEnumerator ReheatFinishing(Case item)=>MethodFinishing(item,FreeKitchenRules.MethodReheat);
        static IEnumerator MethodFinishing(Case item,string method)
        {
            bool cold=method==FreeKitchenRules.MethodColdMix;
            var setup=Setup();setup.gasPoints=0;setup.stoveLoaded=false;
            setup.equipment=cold?new HashSet<string>{"V16","U12"}:new HashSet<string>{"B08","RiceBucket","U14"};
            setup.inventory=cold?new Dictionary<string,int>{{"fruit",2},{"P08",4}}:new Dictionary<string,int>{{"rice",2},{"prepared",2},{"P05",4}};
            item.fixture=method+" with actual equipped hardware; real ingredient carrying, Controller cookware finishing contact, and physical counter packing. No direct Session.Action/PackToCounter shortcuts.";
            // Retain old-save automatic behavior; only the free branch changes.
            var legacy=new BusinessSession(setup);legacy.SelectedRecipes.Clear();legacy.SelectedRecipes.Add(cold?5:1);legacy.Open();
            Need(!legacy.RequiresManualHeatAction(0),"Legacy cold/microwave automatic semantics changed");
            if(!cold)
            {
                var automaticSetup=Setup();automaticSetup.equipment.Remove("V01");automaticSetup.equipment.Add("V04");
                var automatic=new BusinessSession(automaticSetup);automatic.SelectedRecipes.Clear();automatic.Open();
                Need(automatic.AddFreeIngredient(0,"egg")&&automatic.SetFreeMethod(0,"stir_fry"),"Actual V04 fixture failed");
                Need(!automatic.RequiresManualHeatAction(0),"Actual V04 must remain automatic");
            }
            Fixture f=Create(setup);yield return null;int lane=0;
            string[] inputs=cold?new[]{"fruit"}:new[]{"rice","prepared"};string before=Stock(f.Session);
            foreach(string input in inputs)Need(f.controller.UseIngredientAtLane(lane,input),method+" ingredient command rejected: "+input);
            Need(Stock(f.Session)==before,"Raw material debited before actual pickup contact");
            float deadline=f.Session.Elapsed+45f;
            while(true)
            {
                bool all=true;foreach(string input in inputs)all&=UnitsOf(f.Session.Lanes[lane],input)==1;
                if(all&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy)break;
                Need(f.Session.Elapsed<deadline,method+" ingredients never reached cookware");Tick(f);yield return null;
            }
            Need(f.controller.SetFreeCookingMethod(lane,method),method+" physical method selection rejected");
            Need(f.Session.RequiresManualHeatAction(lane),method+" must allow real finishing contact");
            int gas=f.Session.Gas,electricity=f.Session.Electricity;
            Need(f.controller.UseCookware(lane),method+" start command rejected");deadline=f.Session.Elapsed+45f;
            while(f.Session.ProductAtLane(lane)==null)
            {
                var state=f.Session.Lanes[lane].step;
                if((state==KitchenStep.Heating||state==KitchenStep.Utensil)&&!f.controller.HasPendingIntent(StallInteractionKind.Cookware,lane))
                    Need(f.controller.UseCookware(lane),method+" finishing command rejected by legacy automatic heuristic");
                Need(f.Session.Elapsed<deadline,method+" never completed its real utensil contact");Tick(f);yield return null;
            }
            FoodProduct product=f.Session.ProductAtLane(lane);
            Need(product.method==method&&product.portions==1,"Actual product/method/yield mismatch");
            Need(f.Session.Lanes[lane].step==KitchenStep.Pack,"Cold/prepared food unexpectedly needs extra seasoning");
            Need(f.Session.Gas==gas&&f.Session.Electricity==electricity-(cold?0:1),"Actual fuel must charge once for reheat, zero for cold mixing");
            Need(f.Session.WastedPortions==0,"Finishing appointment allowed a batch to burn");
            item.meal=new MealSnapshot{id="pending",name=product.name,method=product.method,portions=product.portions,remaining=product.remaining,priceCents=product.priceCents,quality=product.quality};
            foreach(var input in product.inputs)item.meal.inputs+=(item.meal.inputs==""?"":";")+input.itemId+"x"+input.units;
            var packed=new CounterMeal[1];IEnumerator pack=PackOneToCounter(f,item,lane,packed,cold?"P08":"P05");while(pack.MoveNext())yield return pack.Current;
            item.meal.id=packed[0].id;item.meal.packageId=packed[0].packageId;item.meal.shortSeasoning=packed[0].shortSeasoning;
            Need(!packed[0].shortSeasoning&&f.Session.IntegrityChange==0,"False missing-seasoning penalty");
            Need(f.controller.SelectedOrderId==-1&&f.Session.Served==0,"Preparing/packing unexpectedly selected or served a customer");
            bool finishingCommitted=false;foreach(var work in f.controller.CaptureWorkHistory())if(work.action=="掌握火候"&&work.status=="committed")finishingCommitted=true;
            Need(finishingCommitted,"No real controller finishing contact in work history");
            item.detail=method+" completed via actual actor finishing contact and placed one independent meal on the counter; stock, energy and package charged exactly once. Legacy automatic behavior and actual V04 preserved.";
        }

        static IEnumerator SaveCancels(Case item)
        {
            item.fixture="Queue an uncommitted first pickup plus an explicit future heat step, then CaptureForSave before any clock/contact advance; stock/fuel/cash unchanged and restore keeps the pan empty.";
            Fixture f=Create();yield return null;int lane=0;string before=Stock(f.Session);
            Need(f.controller.UseIngredientAtLane(lane,"egg"),"First pickup rejected");
            Need(f.controller.UseCookware(lane),"Pre-contact heat appointment rejected");
            Need(f.controller.PendingWorkCount>0&&Stock(f.Session)==before,"No queued uncommitted work or premature debit");
            BusinessSaveData saved=f.controller.CaptureForSave();
            Need(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy,"Save left transient actor/queue work");
            Need(Stock(f.Session)==before,"Save committed uncontacted work or mutated stock/fuel/cash");
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(saved)));
            Need(restored.Lanes[lane].freeBatch==null||restored.Lanes[lane].freeBatch.inputs==null||restored.Lanes[lane].freeBatch.inputs.Length==0,"Restore kept uncommitted ingredients in the pan");
            int replay=0;restored.SaleCompleted+=(o,r,c,s)=>replay++;restored.Tick(.5f);
            Need(replay==0&&Stock(restored)==before,"Restore replayed pending work or sale");
            Invariants(restored);
            item.detail="Save cancels uncommitted pickup/appointment without ledger mutation; restore keeps the pan empty.";
        }
        // Captures stop only this explicit fixture clock; no production patience/physics is changed.
        // Use the existing graphical Editor sizing helper, not an artificial camera-only render.
        static IEnumerator CaptureBoth(Fixture f,string name)
        {
            Need(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"UI snapshots need graphical Editor, without -nographics");
            MethodInfo resize=typeof(Map19VisualPreview).GetMethod("Resize",BindingFlags.Static|BindingFlags.NonPublic);
            Need(resize!=null,"Existing GameView sizing helper missing");
            var args=Environment.GetCommandLineArgs();int widthArg=Array.IndexOf(args,"--freedom19-counter-width");
            int width=widthArg>=0?int.Parse(args[widthArg+1]):1280;
            foreach(var size in new[]{new Vector2Int(width,width==1920?1080:720)})
            {
                IEnumerator sizing=(IEnumerator)resize.Invoke(null,new object[]{size.x,size.y});while(sizing.MoveNext())yield return sizing.Current;
                for(int i=0;i<5;i++){f.stage.Present(f.Session,0,false);f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();yield return null;}
                var frame=new VisualFrame{name=name,width=Screen.width,height=Screen.height,meals=f.Session.ReadyMeals.Count,allVisibleMealCentersPicked=true,packageBoundsDisjoint=true};
                var targets=f.root.GetComponentsInChildren<StallInteractionTarget>(false);
                foreach(Transform node in f.root.GetComponentsInChildren<Transform>(false))if(node.name.StartsWith("Counter tray ",StringComparison.Ordinal))frame.slotCount++;
                Physics.SyncTransforms();
                foreach(var target in targets)if(target.kind==StallInteractionKind.CounterMeal&&target.enabled)
                {
                    frame.visibleMealTargets++;Vector3 screen=f.stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                    frame.allVisibleMealCentersPicked&=screen.z>0&&screen.x>0&&screen.x<Screen.width&&screen.y>0&&screen.y<Screen.height&&f.controller.InputHandler.PickScreenPoint(screen)==target;
                    foreach(var package in targets)if(package.kind==StallInteractionKind.Packaging&&package.HitCollider!=null)
                        frame.packageBoundsDisjoint&=!target.HitCollider.bounds.Intersects(package.HitCollider.bounds);
                }
                Need(frame.slotCount==BusinessSession.CounterCapacity,"Six physical serving slots required");
                Need(frame.visibleMealTargets==frame.meals&&frame.allVisibleMealCentersPicked,"Visible meal center does not select its own real target");
                Need(frame.packageBoundsDisjoint,"Meal and package interaction volumes overlap");
                string dir=Path.GetDirectoryName(output);Directory.CreateDirectory(dir);frame.path=Path.Combine(dir,name+"-"+size.x+"x"+size.y+".png");
                Need(!File.Exists(frame.path),"Preserve earlier screenshot");ScreenCapture.CaptureScreenshot(frame.path);
                double deadline=EditorApplication.timeSinceStartup+15;
                while(!File.Exists(frame.path)||new FileInfo(frame.path).Length==0){Need(EditorApplication.timeSinceStartup<deadline,"GameView screenshot was not written");yield return null;}
                report.screenshots.Add(frame);
            }
        }
        static void DisposeFixture(){if(live==null)return;var f=live;live=null;f.stage?.Release();if(f.root)UnityEngine.Object.DestroyImmediate(f.root);}
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Log(string condition,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(condition+"\n"+trace);}
        static void Finish(string failure)
        {
            DisposeFixture();Application.logMessageReceived-=Log;report.failure=failure;
            foreach(Case item in report.cases){if(item.status=="passed")report.passed++;else report.failed++;}
            report.engineErrors=errors.ToArray();report.success=failure==""&&report.failed==0&&report.passed==5&&errors.Count==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
