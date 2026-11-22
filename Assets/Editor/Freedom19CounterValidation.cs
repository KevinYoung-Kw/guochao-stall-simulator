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
    // FREEDOM19 counter acceptance + MidgamePractice rapid ingredient queuing
    // regression: real free-batch cooking, real counter packaging (PackToCounter debits
    // exactly one package + one pan portion at actor contact), explicit SelectCounterMeal
    // -> real actor delivery -> original payment path, scripted Controller/Stage
    // UseIngredient("egg") -> UseIngredient("rice") (0-frame interval) on fresh and
    // post-burn-cleaned lane 0, plus post-burn-cleaned 3-tap UseIngredient("egg") ->
    // UseIngredient("rice") -> UseIngredient("fruit") (3-frame ~0.050s intervals) under
    // BusinessSetup.MidgamePractice().
    // No automatic delivery, no Session.Serve shortcut, no fake products/orders/Dirty/Idle.
    // Explicitly scripted Controller/Stage calls against real Stage/actor; NOT human mouse
    // input, OS hardware input, full visual verification, or a full human-played day.
    // All engine results pending verification.
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
            public string scope="Real Editor PlayMode Stage/actor acceptance with scripted Controller/Stage commands: stir-fry sale, independent counter focus, pending-save cancellation (including post-save non-replay and no double debit), cold-mix and reheat finishing/packing, MidgamePractice zero-frame UseIngredient(egg)->UseIngredient(rice) on fresh and post-burn-cleaned lane 0, post-burn-cleaned 3-tap UseIngredient(egg)->UseIngredient(rice)->UseIngredient(fruit) preflight rejection, CancelPendingWork queue/stock/active-contact isolation, single-stock dual-lane reservation guard, active lane-switch predicted-input isolation, busy/dirty lane rejection, and two fact-recording MidgamePractice dynamic contrast observations (committed egg on lane 0 then UseIngredient(fruit); dispatched start-cook on lane 0 then same-tick UseIngredientAtLane(0,rice)). Finite normal patience and unchanged collision/heat; not human mouse operation, no OS hardware input, not full visual verification, and not a full human-played day.";
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
        const int ExpectedCaseCount=14;
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
            string[] names={"free-batch-counter-sale","counter-meal-independent-focus","save-cancels-uncommitted-prep","cold-mix-controller-finishing","reheat-controller-finishing","midgame-practice-zero-frame-egg-rice-fresh","midgame-practice-zero-frame-egg-rice-after-burn-clean","midgame-practice-three-tap-egg-rice-fruit-after-burn-clean","cancel-pending-work-releases-uncommitted-queue","single-stock-dual-lane-reservation-guard","switch-lane-isolates-predicted-inputs","busy-and-dirty-lane-rejects-illegal-ingredient","midgame-practice-egg-lane0-use-fruit-observation","midgame-practice-dispatched-start-then-rice-observation"};
            Func<Case,IEnumerator>[] tests={CounterSale,IndependentFocus,SaveCancels,ColdMixFinishing,ReheatFinishing,MidgameZeroFrameEggRiceFresh,MidgameZeroFrameEggRiceAfterBurnClean,MidgameThreeTapEggRiceFruitAfterBurnClean,CancelPendingWorkReleasesQueue,SingleStockDualLaneReservationGuard,SwitchLaneIsolatesPredictedInputs,BusyAndDirtyLaneRejectsIllegalIngredient,MidgameEggLane0UseFruitObservation,MidgameDispatchedStartThenRiceObservation};
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
            item.fixture="Queue an uncommitted first pickup plus an explicit future heat step, then CaptureForSave before any clock/contact advance; stock/fuel/cash unchanged, restore keeps the pan empty, live ticks do not resume cancelled intents, and a subsequent real pickup debits exactly once without replaying heat.";
            Fixture f=Create();yield return null;int lane=0;string before=Stock(f.Session);
            int eggBefore=f.Session.Count("egg"),gasBefore=f.Session.Gas;
            Need(f.controller.UseIngredientAtLane(lane,"egg"),"First pickup rejected");
            Need(f.controller.UseCookware(lane),"Pre-contact heat appointment rejected");
            Need(f.controller.PendingWorkCount>0&&Stock(f.Session)==before,"No queued uncommitted work or premature debit");
            BusinessSaveData saved=f.controller.CaptureForSave();
            Need(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel),"Save left transient actor/queue work");
            Need(Stock(f.Session)==before,"Save committed uncontacted work or mutated stock/fuel/cash");
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(saved)));
            Need(restored.Lanes[lane].step==KitchenStep.Idle&&(restored.Lanes[lane].freeBatch==null||restored.Lanes[lane].freeBatch.inputs==null||restored.Lanes[lane].freeBatch.inputs.Length==0),"Restore kept uncommitted ingredients in the pan");
            int replay=0;restored.SaleCompleted+=(o,r,c,s)=>replay++;restored.Tick(.5f);
            Need(replay==0&&Stock(restored)==before&&restored.Lanes[lane].step==KitchenStep.Idle&&UnitsOf(restored.Lanes[lane],"egg")==0&&restored.Count("egg")==eggBefore&&restored.Gas==gasBefore,"Restore replayed pending work or sale");
            Invariants(restored);
            // Live controller after save: advancing simulation must not restore or commit cancelled intents.
            for(int i=0;i<60;i++)Tick(f);
            Need(Stock(f.Session)==before&&f.Session.Lanes[lane].step==KitchenStep.Idle&&UnitsOf(f.Session.Lanes[lane],"egg")==0&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy,
                "Post-save ticks unexpectedly resumed or debited cancelled intents (stock="+Stock(f.Session)+",step="+f.Session.Lanes[lane].step+",eggInPan="+UnitsOf(f.Session.Lanes[lane],"egg")+")");
            // Re-adding egg after save must succeed cleanly and debit exactly once (no double debit, no resumed heat).
            Need(f.controller.UseIngredientAtLane(lane,"egg"),"Post-save UseIngredientAtLane(0, egg) rejected after cancelled intents");
            bool landed=false;
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(UnitsOf(f.Session.Lanes[lane],"egg")==1&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy){landed=true;break;}
                if(frame%60==0)yield return null;
            }
            Need(landed&&UnitsOf(f.Session.Lanes[lane],"egg")==1,"Post-save egg pickup did not reach lane 0");
            Need(f.Session.Count("egg")==eggBefore-1,"Post-save egg pickup must debit exactly once (expected "+(eggBefore-1)+", got "+f.Session.Count("egg")+")");
            Need(f.Session.Gas==gasBefore&&f.Session.Lanes[lane].step==KitchenStep.Prep,"Cancelled pre-save heat appointment unexpectedly resumed (gas="+f.Session.Gas+",step="+f.Session.Lanes[lane].step+")");
            item.detail="Save cancels uncommitted pickup/appointment without ledger mutation; restore and post-save ticks keep the pan empty; subsequent egg pickup debits once ("+eggBefore+"->"+f.Session.Count("egg")+") with gas unchanged ("+gasBefore+").";
        }
        // Scripted Controller/Stage regression (not human mouse input):
        // Case A: MidgamePractice (default district 城中村老街口), fresh lane 0,
        // 0-frame UseIngredient("egg") -> UseIngredient("rice").
        static IEnumerator MidgameZeroFrameEggRiceFresh(Case item)
        {
            item.fixture="Scripted Controller/Stage (not human mouse input) on BusinessSetup.MidgamePractice() (default district 城中村老街口): fresh lane 0, real UI UseIngredient(egg) then UseIngredient(rice) with 0-frame interval; excludes tea ingredients.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            Need(f.Session.Setup.practice&&f.Session.DistrictIndex==0&&f.Session.District.name=="城中村老街口"&&f.Session.Lanes.Length>=2,
                "Expected MidgamePractice with default district 城中村老街口 and >=2 lanes");
            IEnumerator seq=RunZeroFrameEggRiceSequence(f,item,0,"[CaseA:fresh]");
            while(seq.MoveNext())yield return seq.Current;
        }
        // Scripted Controller/Stage regression (not human mouse input):
        // Case B: MidgamePractice (default district 城中村老街口), burn lane 0 via real
        // cooking + Utensil timeout, clean via UseCookware(0) and wait to Idle, then
        // 0-frame UseIngredient("egg") -> UseIngredient("rice").
        static IEnumerator MidgameZeroFrameEggRiceAfterBurnClean(Case item)
        {
            item.fixture="Scripted Controller/Stage (not human mouse input) on BusinessSetup.MidgamePractice() (default district 城中村老街口): burn lane 0 via real cooking/Utensil timeout, clean via UseCookware(0) until Idle, then 0-frame UseIngredient(egg) -> UseIngredient(rice); excludes tea ingredients.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            Need(f.Session.Setup.practice&&f.Session.DistrictIndex==0&&f.Session.District.name=="城中村老街口"&&f.Session.Lanes.Length>=2,
                "Expected MidgamePractice with default district 城中村老街口 and >=2 lanes");
            var burnTrace=new List<string>();
            IEnumerator burnClean=BurnAndCleanLane0(f,0,burnTrace);
            while(burnClean.MoveNext())yield return burnClean.Current;
            IEnumerator seq=RunZeroFrameEggRiceSequence(f,item,0,"[CaseB:afterBurnClean "+string.Join(" ",burnTrace)+"]");
            while(seq.MoveNext())yield return seq.Current;
        }
        static IEnumerator BurnAndCleanLane0(Fixture f,int lane,List<string> trace)
        {
            int wastedBefore=f.Session.WastedPortions,eggBefore=f.Session.Count("egg"),gasBefore=f.Session.Gas;
            Need(f.controller.UseIngredientAtLane(lane,"egg"),"Burn setup: UseIngredientAtLane(0, egg) rejected");
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(UnitsOf(f.Session.Lanes[lane],"egg")==1&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            Need(UnitsOf(f.Session.Lanes[lane],"egg")==1&&f.Session.Count("egg")==eggBefore-1,
                "Burn setup: egg did not reach lane 0 via real actor contact");
            Need(f.controller.SetFreeCookingMethod(lane,FreeKitchenRules.MethodStirFry),
                "Burn setup: SetFreeCookingMethod(stir_fry) rejected");
            Need(f.controller.UseCookware(lane),"Burn setup: UseCookware(0) to start cooking rejected");
            bool sawHeating=false,sawUtensil=false;
            for(int frame=0;frame<60*60;frame++)
            {
                Tick(f);
                KitchenStep step=f.Session.Lanes[lane].step;
                if(step==KitchenStep.Heating)sawHeating=true;
                if(step==KitchenStep.Utensil)sawUtensil=true;
                if(step==KitchenStep.Dirty)break;
                if(frame%60==0)yield return null;
            }
            Need(sawHeating&&sawUtensil&&f.Session.Lanes[lane].step==KitchenStep.Dirty,
                "Burn setup: lane 0 did not burn to Dirty via real Heating->Utensil timeout (heating="+sawHeating+",utensil="+sawUtensil+",step="+f.Session.Lanes[lane].step+")");
            Need(f.Session.Gas==gasBefore-1&&f.Session.WastedPortions==wastedBefore+1&&f.Session.Lanes[lane].freeBatch==null,
                "Burn setup: expected 1 gas debit, +1 WastedPortions, and cleared freeBatch on burn");
            float burnedAt=f.Session.Elapsed;
            Need(f.controller.UseCookware(lane),"Burn setup: UseCookware(0) to clean Dirty lane rejected");
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(f.Session.Lanes[lane].step==KitchenStep.Idle&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.Lanes[lane].step==KitchenStep.Idle&&f.Session.Lanes[lane].freeBatch==null&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy,
                "Burn setup: lane 0 did not finish cleaning to Idle");
            trace.Add("burnedAt="+burnedAt.ToString("F2")+" cleanedAt="+f.Session.Elapsed.ToString("F2")
                +" wasted="+f.Session.WastedPortions+" selectedLane="+f.controller.SelectedLaneIndex);
        }
        static IEnumerator RunZeroFrameEggRiceSequence(Fixture f,Case item,int targetLane,string prefix)
        {
            int eggBefore=f.Session.Count("egg"),riceBefore=f.Session.Count("rice"),fruitBefore=f.Session.Count("fruit");
            string stockBefore=Stock(f.Session);
            int historyStart=f.controller.CaptureWorkHistory().Length;
            bool canEggBefore=f.Session.CanAddFreeIngredient(targetLane,"egg",out string eggBeforeReason);
            bool canRiceBefore=f.Session.CanAddFreeIngredient(targetLane,"rice",out string riceBeforeReason);

            // Click 1: real UI UseIngredient("egg")
            float eggClickElapsed=f.Session.Elapsed;
            bool eggAccepted=f.controller.UseIngredient("egg");
            int selAfterEgg=f.controller.SelectedLaneIndex;
            string activeAfterEgg=f.controller.ActiveWorkLabel,pendingAfterEgg=f.controller.PendingWorkLabel;
            int pendingCountAfterEgg=f.controller.PendingWorkCount;
            int eggStockAfterEgg=f.Session.Count("egg"),riceStockAfterEgg=f.Session.Count("rice");
            string stockAfterEgg=Stock(f.Session),hintAfterEgg=f.controller.InteractionHint;

            // Click 2: real UI UseIngredient("rice") with 0-frame interval (no Tick between calls)
            float riceClickElapsed=f.Session.Elapsed;
            bool riceAccepted=f.controller.UseIngredient("rice");
            int selAfterRice=f.controller.SelectedLaneIndex;
            string activeAfterRice=f.controller.ActiveWorkLabel,pendingAfterRice=f.controller.PendingWorkLabel;
            int pendingCountAfterRice=f.controller.PendingWorkCount;
            int eggStockAfterRice=f.Session.Count("egg"),riceStockAfterRice=f.Session.Count("rice");
            string stockAfterRice=Stock(f.Session),hintAfterRice=f.controller.InteractionHint;

            string clickEvidence=prefix
                +" pre(canEgg="+canEggBefore+"["+eggBeforeReason+"],canRice="+canRiceBefore+"["+riceBeforeReason+"],egg="+eggBefore+",rice="+riceBefore+")"
                +" click1_egg(t="+eggClickElapsed.ToString("F3")+",ret="+eggAccepted+",sel="+selAfterEgg+",active='"+activeAfterEgg+"',pending='"+pendingAfterEgg+"',pCount="+pendingCountAfterEgg+",egg="+eggStockAfterEgg+",rice="+riceStockAfterEgg+",hint='"+hintAfterEgg+"')"
                +" click2_rice(t="+riceClickElapsed.ToString("F3")+",ret="+riceAccepted+",sel="+selAfterRice+",active='"+activeAfterRice+"',pending='"+pendingAfterRice+"',pCount="+pendingCountAfterRice+",egg="+eggStockAfterRice+",rice="+riceStockAfterRice+",hint='"+hintAfterRice+"')";
            item.detail=clickEvidence;

            bool eggContactObserved=false,canRiceAfterEggContact=false,completed=false;
            float eggContactElapsed=-1f;
            string riceAfterEggReason="";
            int maxEggDebit=0,maxRiceDebit=0;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                int eggDebit=eggBefore-f.Session.Count("egg"),riceDebit=riceBefore-f.Session.Count("rice");
                if(eggDebit>maxEggDebit)maxEggDebit=eggDebit;
                if(riceDebit>maxRiceDebit)maxRiceDebit=riceDebit;
                if(!eggContactObserved&&UnitsOf(f.Session.Lanes[targetLane],"egg")>0)
                {
                    eggContactObserved=true;eggContactElapsed=f.Session.Elapsed;
                    canRiceAfterEggContact=f.Session.CanAddFreeIngredient(targetLane,"rice",out riceAfterEggReason);
                }
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                if(frame%60==0)yield return null;
            }

            int finalEggUnits=UnitsOf(f.Session.Lanes[targetLane],"egg"),finalRiceUnits=UnitsOf(f.Session.Lanes[targetLane],"rice");
            int otherEggUnits=0,otherRiceUnits=0;
            for(int i=0;i<f.Session.Lanes.Length;i++)
                if(i!=targetLane){otherEggUnits+=UnitsOf(f.Session.Lanes[i],"egg");otherRiceUnits+=UnitsOf(f.Session.Lanes[i],"rice");}
            int eggDelta=eggBefore-f.Session.Count("egg"),riceDelta=riceBefore-f.Session.Count("rice");
            bool canRiceFinal=false;string riceFinalReason="";
            if(finalRiceUnits==0)canRiceFinal=f.Session.CanAddFreeIngredient(targetLane,"rice",out riceFinalReason);
            BusinessWorkAudit[] history=f.controller.CaptureWorkHistory();
            string workSlice=FormatWorkSlice(history,historyStart);

            item.detail=clickEvidence
                +" afterEggContact(observed="+eggContactObserved+",t="+eggContactElapsed.ToString("F3")+",canRice="+canRiceAfterEggContact+"["+riceAfterEggReason+"])"
                +" final(completed="+completed+",t="+f.Session.Elapsed.ToString("F3")+",lane0Step="+f.Session.Lanes[targetLane].step
                +",lane0Egg="+finalEggUnits+",lane0Rice="+finalRiceUnits+",otherEgg="+otherEggUnits+",otherRice="+otherRiceUnits
                +",eggDelta="+eggDelta+",maxEggDebit="+maxEggDebit+",riceDelta="+riceDelta+",maxRiceDebit="+maxRiceDebit
                +(finalRiceUnits==0?",finalCanRice="+canRiceFinal+"["+riceFinalReason+"]":"")
                +",work="+workSlice+")";

            Need(Mathf.Approximately(eggClickElapsed,riceClickElapsed),"Zero-frame interval violated between egg and rice clicks: "+item.detail);
            Need(stockAfterEgg==stockBefore&&eggStockAfterEgg==eggBefore&&riceStockAfterEgg==riceBefore,
                "UseIngredient(egg) debited inventory prematurely on click before actor contact: "+item.detail);
            Need(stockAfterRice==stockBefore&&eggStockAfterRice==eggBefore&&riceStockAfterRice==riceBefore,
                "UseIngredient(rice) debited inventory prematurely on click before actor contact: "+item.detail);
            Need(completed,"Queued intents did not complete within 40s timeout: "+item.detail);
            Need(canEggBefore,"CanAddFreeIngredient(0, egg) unexpectedly false before clicks ("+eggBeforeReason+"): "+item.detail);
            Need(eggAccepted,"UseIngredient(egg) returned false: "+item.detail);
            Need(eggContactObserved&&finalEggUnits==1,"Egg did not reach lane 0 via real actor contact: "+item.detail);
            Need(eggDelta==1&&maxEggDebit==1,"Egg inventory must debit exactly once with no duplicate debit: "+item.detail);
            Need(canRiceAfterEggContact,"CanAddFreeIngredient(0, rice) false after egg contact ("+riceAfterEggReason+"): "+item.detail);
            Need(riceAccepted,"Zero-frame UseIngredient(rice) returned false despite egg+rice being legal: "+item.detail);
            Need(selAfterEgg==targetLane&&selAfterRice==targetLane,"SelectedLaneIndex drifted from lane 0 during zero-frame clicks: "+item.detail);
            Need(finalRiceUnits==1,"Rice did not reach lane 0 after zero-frame egg->rice clicks: "+item.detail);
            Need(otherEggUnits==0&&otherRiceUnits==0,"Ingredients spilled onto non-target lane: "+item.detail);
            Need(riceDelta==1&&maxRiceDebit==1,"Rice inventory must debit exactly once with no duplicate debit: "+item.detail);
            Need(f.Session.Count("fruit")==fruitBefore,"Non-target tea/fruit stock mutated unexpectedly: "+item.detail);

            int committedInputs=0,nonCommitted=0;
            for(int i=historyStart;i<history.Length;i++)
            {
                var w=history[i];
                if(w.lane==targetLane&&w.action=="投料"&&w.status=="committed")committedInputs++;
                else if(w.status!="committed")nonCommitted++;
            }
            Need(committedInputs==2&&nonCommitted==0,
                "Expected 2 committed ingredient deliveries on lane 0 with zero cancelled/failed intents (committed="+committedInputs+",nonCommitted="+nonCommitted+"): "+item.detail);
        }
        // Scripted Controller/Stage diagnostic case (not human mouse input, no full visual verification):
        // MidgamePractice (default district 城中村老街口), burn lane 0 and clean via UseCookware(0)
        // until Idle, then real UI UseIngredient("egg") -> 3 frames (~0.050s) -> UseIngredient("rice")
        // -> 3 frames (~0.050s) -> UseIngredient("fruit"), recording CanAddFreeIngredient for fruit
        // (empty / after egg contact / final), per-frame InteractionHint transitions, per-lane counts,
        // exact inventory deltas, and call-ordered work serial associations.
        static IEnumerator MidgameThreeTapEggRiceFruitAfterBurnClean(Case item)
        {
            item.fixture="Scripted Controller/Stage (not human mouse input, no full visual verification) on BusinessSetup.MidgamePractice() (default district 城中村老街口): burn lane 0 via real cooking/Utensil timeout, clean via UseCookware(0) until Idle, then real UI UseIngredient(egg) -> 3 frames (~0.050s) -> UseIngredient(rice) -> 3 frames (~0.050s) -> UseIngredient(fruit), tracking InteractionHint frame-by-frame until idle or 60s timeout.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            Need(f.Session.Setup.practice&&f.Session.DistrictIndex==0&&f.Session.District.name=="城中村老街口"&&f.Session.Lanes.Length>=2,
                "Expected MidgamePractice with default district 城中村老街口 and >=2 lanes");
            var burnTrace=new List<string>();
            IEnumerator burnClean=BurnAndCleanLane0(f,0,burnTrace);
            while(burnClean.MoveNext())yield return burnClean.Current;

            const int targetLane=0;
            const int gapFrames=3;
            int eggBefore=f.Session.Count("egg"),riceBefore=f.Session.Count("rice"),fruitBefore=f.Session.Count("fruit");
            int historyStart=f.controller.CaptureWorkHistory().Length;

            bool canEggEmpty=f.Session.CanAddFreeIngredient(targetLane,"egg",out string eggEmptyReason);
            bool canRiceEmpty=f.Session.CanAddFreeIngredient(targetLane,"rice",out string riceEmptyReason);
            bool canFruitEmpty=f.Session.CanAddFreeIngredient(targetLane,"fruit",out string fruitEmptyReason);

            var hintLog=new List<string>();
            string lastHint=f.controller.InteractionHint??"";
            hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"[pre]:'"+lastHint+"'");
            Action<string> recordHint=tag=>
            {
                string cur=f.controller.InteractionHint??"";
                if(cur!=lastHint){lastHint=cur;hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"["+tag+"]:'"+cur+"'");}
            };

            int maxEggDebit=0,maxRiceDebit=0,maxFruitDebit=0;
            int minEggDebit=0,minRiceDebit=0,minFruitDebit=0;
            int minEggStock=eggBefore,minRiceStock=riceBefore,minFruitStock=fruitBefore;
            Action trackInventory=()=>
            {
                int e=f.Session.Count("egg"),r=f.Session.Count("rice"),fr=f.Session.Count("fruit");
                if(e<minEggStock)minEggStock=e;
                if(r<minRiceStock)minRiceStock=r;
                if(fr<minFruitStock)minFruitStock=fr;
                int de=eggBefore-e,dr=riceBefore-r,df=fruitBefore-fr;
                if(de>maxEggDebit)maxEggDebit=de;if(de<minEggDebit)minEggDebit=de;
                if(dr>maxRiceDebit)maxRiceDebit=dr;if(dr<minRiceDebit)minRiceDebit=dr;
                if(df>maxFruitDebit)maxFruitDebit=df;if(df<minFruitDebit)minFruitDebit=df;
            };

            bool eggContactObserved=false,canRiceAfterEgg=false,canFruitAfterEgg=false;
            float eggContactElapsed=-1f;
            string riceAfterEggReason="",fruitAfterEggReason="";
            Action checkEggContact=()=>
            {
                if(!eggContactObserved&&UnitsOf(f.Session.Lanes[targetLane],"egg")>0)
                {
                    eggContactObserved=true;eggContactElapsed=f.Session.Elapsed;
                    canRiceAfterEgg=f.Session.CanAddFreeIngredient(targetLane,"rice",out riceAfterEggReason);
                    canFruitAfterEgg=f.Session.CanAddFreeIngredient(targetLane,"fruit",out fruitAfterEggReason);
                }
            };

            // Click 1: UseIngredient("egg")
            float click1Elapsed=f.Session.Elapsed;
            bool eggAccepted=f.controller.UseIngredient("egg");
            trackInventory();recordHint("click1_egg");
            string click1Log="click1_egg(t="+click1Elapsed.ToString("F3")+",ret="+eggAccepted
                +",sel="+f.controller.SelectedLaneIndex+",active='"+f.controller.ActiveWorkLabel
                +"',pending='"+f.controller.PendingWorkLabel+"',pCount="+f.controller.PendingWorkCount
                +",egg="+f.Session.Count("egg")+",rice="+f.Session.Count("rice")+",fruit="+f.Session.Count("fruit")
                +",hint='"+(f.controller.InteractionHint??"")+"')";

            for(int k=0;k<gapFrames;k++)
            {
                Tick(f);trackInventory();checkEggContact();recordHint("gap1_f"+(k+1));
            }

            // Click 2: UseIngredient("rice")
            float click2Elapsed=f.Session.Elapsed;
            bool riceAccepted=f.controller.UseIngredient("rice");
            trackInventory();recordHint("click2_rice");
            string click2Log="click2_rice(t="+click2Elapsed.ToString("F3")+",dt="+(click2Elapsed-click1Elapsed).ToString("F3")
                +",ret="+riceAccepted+",sel="+f.controller.SelectedLaneIndex+",active='"+f.controller.ActiveWorkLabel
                +"',pending='"+f.controller.PendingWorkLabel+"',pCount="+f.controller.PendingWorkCount
                +",egg="+f.Session.Count("egg")+",rice="+f.Session.Count("rice")+",fruit="+f.Session.Count("fruit")
                +",hint='"+(f.controller.InteractionHint??"")+"')";

            for(int k=0;k<gapFrames;k++)
            {
                Tick(f);trackInventory();checkEggContact();recordHint("gap2_f"+(k+1));
            }

            // Click 3: UseIngredient("fruit")
            float click3Elapsed=f.Session.Elapsed;
            int pendingBeforeFruit=f.controller.PendingWorkCount;
            bool fruitAccepted=f.controller.UseIngredient("fruit");
            string fruitClickHint=f.controller.InteractionHint??"";
            int pendingAfterFruit=f.controller.PendingWorkCount;
            int selAfterFruit=f.controller.SelectedLaneIndex;
            trackInventory();recordHint("click3_fruit");
            string click3Log="click3_fruit(t="+click3Elapsed.ToString("F3")+",dt="+(click3Elapsed-click2Elapsed).ToString("F3")
                +",ret="+fruitAccepted+",sel="+selAfterFruit+",active='"+f.controller.ActiveWorkLabel
                +"',pending='"+f.controller.PendingWorkLabel+"',pCount="+pendingBeforeFruit+"->"+pendingAfterFruit
                +",egg="+f.Session.Count("egg")+",rice="+f.Session.Count("rice")+",fruit="+f.Session.Count("fruit")
                +",hint='"+fruitClickHint+"')";

            // Advance frame-by-frame until queue and actor are both idle, or 60s timeout.
            bool completed=false;
            for(int frame=0;frame<60*60;frame++)
            {
                Tick(f);trackInventory();checkEggContact();recordHint("f"+(frame+1));
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                if(frame%60==0)yield return null;
            }

            bool canFruitFinal=f.Session.CanAddFreeIngredient(targetLane,"fruit",out string fruitFinalReason);
            bool canRiceFinal=f.Session.CanAddFreeIngredient(targetLane,"rice",out string riceFinalReason);

            var laneParts=new List<string>();
            int totalEggInPans=0,totalRiceInPans=0,totalFruitInPans=0;
            for(int i=0;i<f.Session.Lanes.Length;i++)
            {
                KitchenLane l=f.Session.Lanes[i];
                int le=UnitsOf(l,"egg"),lr=UnitsOf(l,"rice"),lf=UnitsOf(l,"fruit");
                totalEggInPans+=le;totalRiceInPans+=lr;totalFruitInPans+=lf;
                laneParts.Add("lane"+i+"(step="+l.step+",egg="+le+",rice="+lr+",fruit="+lf+")");
            }
            int lane0Egg=UnitsOf(f.Session.Lanes[targetLane],"egg");
            int lane0Rice=UnitsOf(f.Session.Lanes[targetLane],"rice");

            int eggAfter=f.Session.Count("egg"),riceAfter=f.Session.Count("rice"),fruitAfter=f.Session.Count("fruit");
            int eggDelta=eggBefore-eggAfter,riceDelta=riceBefore-riceAfter,fruitDelta=fruitBefore-fruitAfter;

            BusinessWorkAudit[] history=f.controller.CaptureWorkHistory();
            var newWork=new List<BusinessWorkAudit>();
            for(int i=historyStart;i<history.Length;i++)newWork.Add(history[i]);
            var bySerial=new List<BusinessWorkAudit>(newWork);
            bySerial.Sort((a,b)=>a.serial.CompareTo(b.serial));

            int idx=0;
            BusinessWorkAudit eggWork=eggAccepted&&idx<bySerial.Count?bySerial[idx++]:null;
            BusinessWorkAudit riceWork=riceAccepted&&idx<bySerial.Count?bySerial[idx++]:null;
            BusinessWorkAudit fruitWork=fruitAccepted&&idx<bySerial.Count?bySerial[idx++]:null;

            var cancelledParts=new List<string>();
            if(eggWork!=null&&eggWork.status!="committed")cancelledParts.Add("call1:egg("+FormatWorkEntry(eggWork)+")");
            if(riceWork!=null&&riceWork.status!="committed")cancelledParts.Add("call2:rice("+FormatWorkEntry(riceWork)+")");
            if(fruitWork!=null&&fruitWork.status!="committed")cancelledParts.Add("call3:fruit("+FormatWorkEntry(fruitWork)+")");
            for(int i=idx;i<bySerial.Count;i++)
                if(bySerial[i].status!="committed")cancelledParts.Add("extra("+FormatWorkEntry(bySerial[i])+")");

            string fruitOutcome=!fruitAccepted?"rejected_on_click"
                :fruitWork==null?"queued_unfinished"
                :fruitWork.status=="committed"?"queued_then_committed"
                :"queued_then_cancelled("+fruitWork.status+")";

            string callSerialMap="call1:egg->"+(eggAccepted?FormatWorkEntry(eggWork):"rejected_on_click")
                +"; call2:rice->"+(riceAccepted?FormatWorkEntry(riceWork):"rejected_on_click")
                +"; call3:fruit->"+(fruitAccepted?FormatWorkEntry(fruitWork):"rejected_on_click");

            item.detail="[CaseC:threeTapAfterBurnClean "+string.Join(" ",burnTrace)+"]"
                +" pre(egg="+eggBefore+",rice="+riceBefore+",fruit="+fruitBefore
                +",canEggEmpty="+canEggEmpty+"["+eggEmptyReason+"]"
                +",canRiceEmpty="+canRiceEmpty+"["+riceEmptyReason+"]"
                +",canFruitEmpty="+canFruitEmpty+"["+fruitEmptyReason+"])"
                +" "+click1Log+" "+click2Log+" "+click3Log
                +" afterEggContact(observed="+eggContactObserved+",t="+eggContactElapsed.ToString("F3")
                +",canRice="+canRiceAfterEgg+"["+riceAfterEggReason+"]"
                +",canFruit="+canFruitAfterEgg+"["+fruitAfterEggReason+"])"
                +" final(completed="+completed+",t="+f.Session.Elapsed.ToString("F3")
                +",canFruitFinal="+canFruitFinal+"["+fruitFinalReason+"]"
                +",canRiceFinal="+canRiceFinal+"["+riceFinalReason+"]"
                +",lanes=["+string.Join("; ",laneParts)+"]"
                +",stockDelta(egg="+eggBefore+"->"+eggAfter+"[d="+eggDelta+",min="+minEggStock+",debitRange="+minEggDebit+".."+maxEggDebit+"]"
                +",rice="+riceBefore+"->"+riceAfter+"[d="+riceDelta+",min="+minRiceStock+",debitRange="+minRiceDebit+".."+maxRiceDebit+"]"
                +",fruit="+fruitBefore+"->"+fruitAfter+"[d="+fruitDelta+",min="+minFruitStock+",debitRange="+minFruitDebit+".."+maxFruitDebit+"])"
                +",fruitOutcome="+fruitOutcome
                +",cancelled=["+(cancelledParts.Count>0?string.Join("; ",cancelledParts):"none")+"]"
                +",callSerials=["+callSerialMap+"]"
                +",workHistoryOrder="+FormatWorkSlice(history,historyStart)
                +",hintTimeline=["+string.Join(" | ",hintLog)+"])";

            Need(completed,"Three-tap sequence did not become idle within 60s timeout: "+item.detail);
            Need(minEggStock>=0&&minRiceStock>=0&&minFruitStock>=0,
                "Negative inventory observed during three-tap sequence: "+item.detail);
            Need(maxEggDebit<=1&&minEggDebit>=0&&maxRiceDebit<=1&&minRiceDebit>=0&&maxFruitDebit==0&&minFruitDebit==0,
                "Inventory changed unexpectedly for egg/rice/fruit (expected egg<=1, rice<=1, fruit==0): "+item.detail);
            Need(eggAccepted,"UseIngredient(egg) returned false: "+item.detail);
            Need(riceAccepted,"UseIngredient(rice) returned false: "+item.detail);
            Need(!fruitAccepted,"UseIngredient(fruit) must return false immediately on click when incompatible egg+rice are already active/queued on lane 0: "+item.detail);
            Need(pendingAfterFruit==pendingBeforeFruit,"Rejected fruit click unexpectedly changed PendingWorkCount ("+pendingBeforeFruit+"->"+pendingAfterFruit+"): "+item.detail);
            Need(selAfterFruit==targetLane,"Rejected fruit click unexpectedly drifted SelectedLaneIndex from lane 0 to "+selAfterFruit+": "+item.detail);
            bool hintExplainsIncompat=fruitClickHint.Contains("不适合")||fruitClickHint.Contains("搭配")||fruitClickHint.Contains("做不了")||fruitClickHint.Contains("装不下")||fruitClickHint.Contains("不能");
            bool hintExplainsSwitchPan=fruitClickHint.Contains("换")||fruitClickHint.Contains("空锅")||fruitClickHint.Contains("另一")||fruitClickHint.Contains("另选")||fruitClickHint.Contains("别的锅");
            Need(!string.IsNullOrEmpty(fruitClickHint)&&hintExplainsIncompat&&hintExplainsSwitchPan,
                "Rejected fruit click hint must explain same-pan incompatibility and suggest switching pans (hint='"+fruitClickHint+"'): "+item.detail);
            Need(eggContactObserved&&lane0Egg==1&&totalEggInPans==1&&eggDelta==1,
                "Egg did not reach lane 0 with exact single debit: "+item.detail);
            Need(lane0Rice==1&&totalRiceInPans==1&&riceDelta==1,
                "Rice did not reach lane 0 within timeout with exact single debit: "+item.detail);
            Need(totalFruitInPans==0&&fruitDelta==0&&fruitAfter==fruitBefore,
                "Fruit stock or pan state mutated despite rejected fruit click: "+item.detail);
            Need(eggWork!=null&&eggWork.status=="committed"&&riceWork!=null&&riceWork.status=="committed",
                "Expected committed work records for egg and rice: "+item.detail);
            Need(fruitWork==null&&bySerial.Count==2&&cancelledParts.Count==0&&fruitOutcome=="rejected_on_click",
                "Fruit must be rejected on click with zero queued-then-cancelled fruit work (expected 2 committed works, 0 cancelled): "+item.detail);
        }
        // Dual-lane real equipment fixture: B04 (2 gas burners), two woks (V01+V02) with
        // two spatulas (U01+U02) for simultaneous stir-fry lanes, plus cold-mix (V16+U12).
        static BusinessSetup DualWokAndColdSetup(int egg,int rice,int fruit)
        {
            return new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=15000,seed=12017,dayDuration=300,gasPoints=45,electricityPoints=45,
                equipment=new HashSet<string>{"B04","V01","V02","U01","U02","V16","U12"},
                inventory=new Dictionary<string,int>{{"egg",egg},{"rice",rice},{"fruit",fruit},{"seasoning",30},{"P05",30},{"P08",30}}};
        }
        // Regression 2: CancelPendingWork clears queued uncommitted inputs so they neither
        // occupy stock nor poison compatibility, while an in-flight uncommitted activeWork
        // on the actor still counts for both compatibility and stock reservation.
        static IEnumerator CancelPendingWorkReleasesQueue(Case item)
        {
            item.fixture="Dual-lane real B04/V01/V02/U01/U02/V16/U12 with stock egg=1, rice=1, fruit=2: queue egg+rice on lane 0, CancelPendingWork before contact, verify active uncommitted egg still blocks fruit on lane 0 and last egg on lane 1 while cancelled rice releases stock=1; queue rice on lane 1, CancelPendingWork again, verify cancelled rice on lane 1 neither blocks fruit on lane 1 nor occupies rice for lane 0; tick to real contact.";
            Fixture f=Create(DualWokAndColdSetup(1,1,2));yield return null;
            Need(f.Session.Lanes.Length>=2,"Expected >=2 lanes on B04 dual-wok fixture");
            Need(f.Session.CanAddFreeIngredient(0,"egg",out string r0e),"Pre-check CanAdd(0,egg) failed: "+r0e);
            Need(f.Session.CanAddFreeIngredient(0,"rice",out string r0r),"Pre-check CanAdd(0,rice) failed: "+r0r);
            Need(f.Session.CanAddFreeIngredient(0,"fruit",out string r0f),"Pre-check CanAdd(0,fruit) failed: "+r0f);
            Need(f.Session.CanAddFreeIngredient(1,"egg",out string r1e),"Pre-check CanAdd(1,egg) failed: "+r1e);
            Need(f.Session.CanAddFreeIngredient(1,"rice",out string r1r),"Pre-check CanAdd(1,rice) failed: "+r1r);
            Need(f.Session.CanAddFreeIngredient(1,"fruit",out string r1f),"Pre-check CanAdd(1,fruit) failed: "+r1f);

            int historyStart=f.controller.CaptureWorkHistory().Length;
            string stockBefore=Stock(f.Session);

            bool qEgg0=f.controller.UseIngredientAtLane(0,"egg");
            bool qRice0=f.controller.UseIngredientAtLane(0,"rice");
            int pCountBeforeCancel1=f.controller.PendingWorkCount;
            bool busyBeforeCancel1=f.stage.WorkActorBusy;
            f.controller.CancelPendingWork();
            int pCountAfterCancel1=f.controller.PendingWorkCount;
            bool busyAfterCancel1=f.stage.WorkActorBusy;
            int lane0EggAtCancel1=UnitsOf(f.Session.Lanes[0],"egg");

            // Active uncommitted egg@lane0 must still count for compatibility (lane 0) and stock=1 (lane 1).
            bool fruit0WhileActive=f.controller.UseIngredientAtLane(0,"fruit");
            string fruit0Hint=f.controller.InteractionHint??"";
            bool egg1WhileActive=f.controller.UseIngredientAtLane(1,"egg");
            string egg1Hint=f.controller.InteractionHint??"";
            // Cancelled rice@lane0 must NOT occupy stock=1.
            bool rice1AfterCancel1=f.controller.UseIngredientAtLane(1,"rice");

            // Cancel pending rice@lane1 while egg@lane0 is still uncommitted in activeWork.
            f.controller.CancelPendingWork();
            int pCountAfterCancel2=f.controller.PendingWorkCount;
            int lane0EggAtCancel2=UnitsOf(f.Session.Lanes[0],"egg");
            // Cancelled rice@lane1 must NOT poison lane 1 compatibility with fruit or occupy rice for lane 0.
            bool fruit1AfterCancel2=f.controller.UseIngredientAtLane(1,"fruit");
            bool rice0AfterCancel2=f.controller.UseIngredientAtLane(0,"rice");
            string stockBeforeTicks=Stock(f.Session);

            int minEgg=f.Session.Count("egg"),minRice=f.Session.Count("rice"),minFruit=f.Session.Count("fruit");
            bool completed=false;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                minEgg=Math.Min(minEgg,f.Session.Count("egg"));
                minRice=Math.Min(minRice,f.Session.Count("rice"));
                minFruit=Math.Min(minFruit,f.Session.Count("fruit"));
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                if(frame%60==0)yield return null;
            }

            BusinessWorkAudit[] history=f.controller.CaptureWorkHistory();
            int committed=0,cancelledPending=0,otherNonCommitted=0;
            for(int i=historyStart;i<history.Length;i++)
            {
                if(history[i].status=="committed")committed++;
                else if(history[i].status=="cancelled_pending")cancelledPending++;
                else otherNonCommitted++;
            }
            item.detail="qEgg0="+qEgg0+",qRice0="+qRice0+",pBeforeC1="+pCountBeforeCancel1+",busyC1="+busyBeforeCancel1+"->"+busyAfterCancel1
                +",pAfterC1="+pCountAfterCancel1+",lane0EggAtC1="+lane0EggAtCancel1
                +",fruit0WhileActive="+fruit0WhileActive+"['"+fruit0Hint+"'],egg1WhileActive="+egg1WhileActive+"['"+egg1Hint+"']"
                +",rice1AfterC1="+rice1AfterCancel1+",pAfterC2="+pCountAfterCancel2+",lane0EggAtC2="+lane0EggAtCancel2
                +",fruit1AfterC2="+fruit1AfterCancel2+",rice0AfterC2="+rice0AfterCancel2
                +",completed="+completed+",lane0=(egg="+UnitsOf(f.Session.Lanes[0],"egg")+",rice="+UnitsOf(f.Session.Lanes[0],"rice")+",fruit="+UnitsOf(f.Session.Lanes[0],"fruit")+")"
                +",lane1=(egg="+UnitsOf(f.Session.Lanes[1],"egg")+",rice="+UnitsOf(f.Session.Lanes[1],"rice")+",fruit="+UnitsOf(f.Session.Lanes[1],"fruit")+")"
                +",stock=(egg="+f.Session.Count("egg")+",rice="+f.Session.Count("rice")+",fruit="+f.Session.Count("fruit")+",min="+minEgg+"/"+minRice+"/"+minFruit+")"
                +",work(committed="+committed+",cancelledPending="+cancelledPending+",other="+otherNonCommitted+")="+FormatWorkSlice(history,historyStart);

            Need(qEgg0&&qRice0&&pCountBeforeCancel1==1&&busyBeforeCancel1,"Initial queue of egg+rice on lane 0 failed: "+item.detail);
            Need(pCountAfterCancel1==0&&busyAfterCancel1&&lane0EggAtCancel1==0,"First CancelPendingWork must clear pending rice while keeping uncommitted egg active: "+item.detail);
            Need(!fruit0WhileActive,"Uncommitted activeWork egg on lane 0 must still reject incompatible fruit on lane 0: "+item.detail);
            Need(!egg1WhileActive,"Uncommitted activeWork egg must still reserve the last 1 egg against lane 1: "+item.detail);
            Need(rice1AfterCancel1,"Cancelled pending rice must release stock=1 so lane 1 can queue rice: "+item.detail);
            Need(pCountAfterCancel2==0&&lane0EggAtCancel2==0,"Second CancelPendingWork did not clear pending rice while egg remained uncommitted: "+item.detail);
            Need(fruit1AfterCancel2,"Cancelled pending rice on lane 1 must not cause false incompatibility when queuing fruit on lane 1: "+item.detail);
            Need(rice0AfterCancel2,"Cancelled pending rice must not block re-queuing rice on lane 0 or occupy stock=1: "+item.detail);
            Need(stockBeforeTicks==stockBefore,"Inventory mutated before real actor contact: "+item.detail);
            Need(completed,"Remaining queued intents did not complete within 40s: "+item.detail);
            Need(minEgg>=0&&minRice>=0&&minFruit>=0,"Negative inventory observed: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[0],"egg")==1&&UnitsOf(f.Session.Lanes[0],"rice")==1&&UnitsOf(f.Session.Lanes[0],"fruit")==0,"Lane 0 final contents mismatch: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[1],"fruit")==1&&UnitsOf(f.Session.Lanes[1],"egg")==0&&UnitsOf(f.Session.Lanes[1],"rice")==0,"Lane 1 final contents mismatch: "+item.detail);
            Need(f.Session.Count("egg")==0&&f.Session.Count("rice")==0&&f.Session.Count("fruit")==1,"Final stock mismatch: "+item.detail);
            Need(committed==3&&cancelledPending==2&&otherNonCommitted==0,"Unexpected work history counts: "+item.detail);
        }
        // Regression 3: When a SKU has only 1 unit left in stock, reserving it on a second pan
        // must be rejected without premature debit or negative stock, and releasing via save or
        // CancelPendingWork must restore clean reservation on the other pan.
        static IEnumerator SingleStockDualLaneReservationGuard(Case item)
        {
            item.fixture="Dual-lane real B04/V01/V02/U01/U02/V16/U12 with egg=1, rice=1: duplicate egg reservation across lanes 0->1 rejected, CaptureForSave releases active egg so lane 1 succeeds; duplicate rice reservation across lanes 1->0 rejected, CancelPendingWork releases pending rice so lane 0 succeeds; tick to contact.";
            Fixture f=Create(DualWokAndColdSetup(1,1,2));yield return null;
            Need(f.Session.Lanes.Length>=2,"Expected >=2 lanes on B04 dual-wok fixture");
            Need(f.Session.CanAddFreeIngredient(0,"egg",out string r0e),"Pre-check CanAdd(0,egg) failed: "+r0e);
            Need(f.Session.CanAddFreeIngredient(1,"egg",out string r1e),"Pre-check CanAdd(1,egg) failed: "+r1e);
            Need(f.Session.CanAddFreeIngredient(0,"rice",out string r0r),"Pre-check CanAdd(0,rice) failed: "+r0r);
            Need(f.Session.CanAddFreeIngredient(1,"rice",out string r1r),"Pre-check CanAdd(1,rice) failed: "+r1r);

            string before=Stock(f.Session);
            // Part A: active reservation on lane 0 blocks lane 1; CaptureForSave releases it.
            bool egg0=f.controller.UseIngredientAtLane(0,"egg");
            bool egg1Dup=f.controller.UseIngredientAtLane(1,"egg");
            string egg1DupHint=f.controller.InteractionHint??"";
            string stockAfterEggDup=Stock(f.Session);
            BusinessSaveData saved=f.controller.CaptureForSave();
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(saved)));
            bool busyAfterSave=f.stage.WorkActorBusy;
            int pAfterSave=f.controller.PendingWorkCount;
            string stockAfterSave=Stock(f.Session);
            bool egg1AfterSave=f.controller.UseIngredientAtLane(1,"egg");

            // Part B: pending reservation on lane 1 blocks lane 0; CancelPendingWork releases it.
            bool rice1=f.controller.UseIngredientAtLane(1,"rice");
            bool rice0Dup=f.controller.UseIngredientAtLane(0,"rice");
            string rice0DupHint=f.controller.InteractionHint??"";
            string stockAfterRiceDup=Stock(f.Session);
            f.controller.CancelPendingWork();
            int pAfterCancel=f.controller.PendingWorkCount;
            bool rice0AfterCancel=f.controller.UseIngredientAtLane(0,"rice");
            bool rice1DupAfter=f.controller.UseIngredientAtLane(1,"rice");
            string stockBeforeTicks=Stock(f.Session);

            int minEgg=f.Session.Count("egg"),minRice=f.Session.Count("rice");
            bool completed=false;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                minEgg=Math.Min(minEgg,f.Session.Count("egg"));
                minRice=Math.Min(minRice,f.Session.Count("rice"));
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                if(frame%60==0)yield return null;
            }

            item.detail="egg0="+egg0+",egg1Dup="+egg1Dup+"['"+egg1DupHint+"'],busyAfterSave="+busyAfterSave+",pAfterSave="+pAfterSave
                +",egg1AfterSave="+egg1AfterSave+",rice1="+rice1+",rice0Dup="+rice0Dup+"['"+rice0DupHint+"']"
                +",pAfterCancel="+pAfterCancel+",rice0AfterCancel="+rice0AfterCancel+",rice1DupAfter="+rice1DupAfter
                +",completed="+completed+",lane0=(egg="+UnitsOf(f.Session.Lanes[0],"egg")+",rice="+UnitsOf(f.Session.Lanes[0],"rice")+")"
                +",lane1=(egg="+UnitsOf(f.Session.Lanes[1],"egg")+",rice="+UnitsOf(f.Session.Lanes[1],"rice")+")"
                +",stock=(egg="+f.Session.Count("egg")+",rice="+f.Session.Count("rice")+",min="+minEgg+"/"+minRice+")";

            Need(egg0,"First egg reservation on lane 0 rejected: "+item.detail);
            Need(!egg1Dup,"Second egg reservation on lane 1 must be rejected when stock=1: "+item.detail);
            Need(stockAfterEggDup==before&&stockAfterSave==before,"Stock mutated before contact or during save: "+item.detail);
            Need(!busyAfterSave&&pAfterSave==0&&restored.Count("egg")==1&&restored.Lanes[0].step==KitchenStep.Idle&&restored.Lanes[1].step==KitchenStep.Idle,
                "CaptureForSave did not cleanly release active egg reservation: "+item.detail);
            Need(egg1AfterSave,"After CaptureForSave released egg=1, lane 1 must accept egg: "+item.detail);
            Need(rice1,"First rice reservation on lane 1 rejected: "+item.detail);
            Need(!rice0Dup,"Second rice reservation on lane 0 must be rejected when stock=1: "+item.detail);
            Need(stockAfterRiceDup==before&&pAfterCancel==0,"Pending rice reservation mutated stock or did not clear on CancelPendingWork: "+item.detail);
            Need(rice0AfterCancel,"After CancelPendingWork released rice=1, lane 0 must accept rice: "+item.detail);
            Need(!rice1DupAfter,"Duplicate rice reservation on lane 1 must be rejected after lane 0 claimed rice=1: "+item.detail);
            Need(stockBeforeTicks==before,"Inventory mutated before real actor contact: "+item.detail);
            Need(completed,"Queued intents did not complete within 40s: "+item.detail);
            Need(minEgg>=0&&minRice>=0,"Negative inventory observed: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[0],"rice")==1&&UnitsOf(f.Session.Lanes[0],"egg")==0,"Lane 0 final contents mismatch: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[1],"egg")==1&&UnitsOf(f.Session.Lanes[1],"rice")==0,"Lane 1 final contents mismatch: "+item.detail);
            Need(f.Session.Count("egg")==0&&f.Session.Count("rice")==0,"Final stock mismatch: "+item.detail);
        }
        // Regression 4: Actively switching to lane 1 to add a different ingredient (fruit) must
        // not mix lane 0's predicted inputs (egg+rice) into lane 1, and lane 1's own predicted
        // input (fruit) must remain isolated to lane 1.
        static IEnumerator SwitchLaneIsolatesPredictedInputs(Case item)
        {
            item.fixture="BusinessSetup.MidgamePractice(): queue egg+rice on lane 0, then explicitly switch to lane 1 via UseIngredientAtLane(1, fruit) before contact; verify lane 1 accepts fruit without mixing lane 0 predictions, rejects incompatible egg on lane 1, and all three real inputs commit cleanly to their own lanes.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            Need(f.Session.Lanes.Length>=2,"Expected >=2 lanes in MidgamePractice");
            Need(f.Session.CanAddFreeIngredient(0,"egg",out string r0e),"Pre-check CanAdd(0,egg) failed: "+r0e);
            Need(f.Session.CanAddFreeIngredient(0,"rice",out string r0r),"Pre-check CanAdd(0,rice) failed: "+r0r);
            Need(f.Session.CanAddFreeIngredient(1,"fruit",out string r1f),"Pre-check CanAdd(1,fruit) failed: "+r1f);
            Need(f.Session.CanAddFreeIngredient(1,"egg",out string r1e),"Pre-check CanAdd(1,egg) failed: "+r1e);

            int eggBefore=f.Session.Count("egg"),riceBefore=f.Session.Count("rice"),fruitBefore=f.Session.Count("fruit");
            string stockBefore=Stock(f.Session);
            int historyStart=f.controller.CaptureWorkHistory().Length;

            bool egg0=f.controller.UseIngredientAtLane(0,"egg");
            bool rice0=f.controller.UseIngredientAtLane(0,"rice");
            bool fruit1=f.controller.UseIngredientAtLane(1,"fruit");
            int selAfterSwitch=f.controller.SelectedLaneIndex;
            bool egg1Rejected=f.controller.UseIngredientAtLane(1,"egg");
            string egg1Hint=f.controller.InteractionHint??"";
            string stockBeforeTicks=Stock(f.Session);

            bool completed=false;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                if(frame%60==0)yield return null;
            }

            BusinessWorkAudit[] history=f.controller.CaptureWorkHistory();
            int committedLane0=0,committedLane1=0,nonCommitted=0;
            for(int i=historyStart;i<history.Length;i++)
            {
                if(history[i].status=="committed"&&history[i].action=="投料")
                {if(history[i].lane==0)committedLane0++;else if(history[i].lane==1)committedLane1++;}
                else if(history[i].status!="committed")nonCommitted++;
            }

            item.detail="egg0="+egg0+",rice0="+rice0+",fruit1="+fruit1+",selAfterSwitch="+selAfterSwitch
                +",egg1Rejected="+egg1Rejected+"['"+egg1Hint+"'],completed="+completed
                +",lane0(step="+f.Session.Lanes[0].step+",egg="+UnitsOf(f.Session.Lanes[0],"egg")+",rice="+UnitsOf(f.Session.Lanes[0],"rice")+",fruit="+UnitsOf(f.Session.Lanes[0],"fruit")+")"
                +",lane1(step="+f.Session.Lanes[1].step+",egg="+UnitsOf(f.Session.Lanes[1],"egg")+",rice="+UnitsOf(f.Session.Lanes[1],"rice")+",fruit="+UnitsOf(f.Session.Lanes[1],"fruit")+")"
                +",deltas=(egg="+(eggBefore-f.Session.Count("egg"))+",rice="+(riceBefore-f.Session.Count("rice"))+",fruit="+(fruitBefore-f.Session.Count("fruit"))+")"
                +",work(l0="+committedLane0+",l1="+committedLane1+",nonCommitted="+nonCommitted+")="+FormatWorkSlice(history,historyStart);

            Need(egg0&&rice0,"Queueing egg+rice on lane 0 rejected: "+item.detail);
            Need(fruit1,"Switching to lane 1 for fruit was wrongly rejected (lane 0 predicted inputs leaked into lane 1): "+item.detail);
            Need(selAfterSwitch==1,"SelectedLaneIndex did not move to lane 1: "+item.detail);
            Need(!egg1Rejected,"Lane 1 with queued fruit must reject incompatible egg on lane 1: "+item.detail);
            Need(stockBeforeTicks==stockBefore,"Inventory debited before real actor contact: "+item.detail);
            Need(completed,"Queued intents across lanes 0 and 1 did not complete within 40s: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[0],"egg")==1&&UnitsOf(f.Session.Lanes[0],"rice")==1&&UnitsOf(f.Session.Lanes[0],"fruit")==0,"Lane 0 contents mismatch: "+item.detail);
            Need(UnitsOf(f.Session.Lanes[1],"fruit")==1&&UnitsOf(f.Session.Lanes[1],"egg")==0&&UnitsOf(f.Session.Lanes[1],"rice")==0,"Lane 1 contents mismatch: "+item.detail);
            Need(eggBefore-f.Session.Count("egg")==1&&riceBefore-f.Session.Count("rice")==1&&fruitBefore-f.Session.Count("fruit")==1,"Inventory debits mismatch: "+item.detail);
            Need(committedLane0==2&&committedLane1==1&&nonCommitted==0,"Expected 2 committed on lane 0, 1 on lane 1, and 0 cancelled: "+item.detail);
        }
        // Regression 5: Real busy pan (Heating / Utensil) and real burned pan (Dirty) must
        // reject illegal ingredient additions via both UseIngredientAtLane and UseIngredient,
        // without mutating stock or queuing work; cleaning back to Idle restores normal input.
        static IEnumerator BusyAndDirtyLaneRejectsIllegalIngredient(Case item)
        {
            item.fixture="Single-lane real B03/V01/U01: cook egg into real Heating -> Utensil -> natural burn Dirty, verify illegal ingredient calls are rejected in all three states without stock/queue mutation, then clean via UseCookware(0) and verify recovery.";
            Fixture f=Create();yield return null;
            Need(f.Session.Lanes.Length==1,"Expected single-lane B03 fixture");
            Need(f.Session.CanAddFreeIngredient(0,"egg",out string rEgg),"Pre-check CanAdd(0,egg) failed: "+rEgg);
            Need(f.Session.CanAddFreeIngredient(0,"meat",out string rMeat),"Pre-check CanAdd(0,meat) failed: "+rMeat);

            Need(f.controller.UseIngredientAtLane(0,"egg"),"Initial egg pickup rejected");
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(UnitsOf(f.Session.Lanes[0],"egg")==1&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            Need(UnitsOf(f.Session.Lanes[0],"egg")==1,"Initial egg never reached lane 0");
            Need(f.controller.SetFreeCookingMethod(0,FreeKitchenRules.MethodStirFry),"SetFreeCookingMethod(stir_fry) rejected");
            Need(f.controller.UseCookware(0),"Start cooking UseCookware(0) rejected");

            // Reach real Heating state.
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(f.Session.Lanes[0].step==KitchenStep.Heating&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.Lanes[0].step==KitchenStep.Heating,"Lane 0 never entered real Heating state");
            string stockHeating=Stock(f.Session);
            bool canAddHeating=f.Session.CanAddFreeIngredient(0,"meat",out string heatingCanReason);
            bool atLaneHeating=f.controller.UseIngredientAtLane(0,"meat");
            bool uiHeating=f.controller.UseIngredient("meat");
            string heatingHint=f.controller.InteractionHint??"";
            int pHeating=f.controller.PendingWorkCount;
            string stockHeatingAfter=Stock(f.Session);

            // Reach real Utensil state.
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                if(f.Session.Lanes[0].step==KitchenStep.Utensil)break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.Lanes[0].step==KitchenStep.Utensil,"Lane 0 never entered real Utensil state");
            string stockUtensil=Stock(f.Session);
            bool canAddUtensil=f.Session.CanAddFreeIngredient(0,"meat",out string utensilCanReason);
            bool atLaneUtensil=f.controller.UseIngredientAtLane(0,"meat");
            bool uiUtensil=f.controller.UseIngredient("meat");
            string utensilHint=f.controller.InteractionHint??"";
            int pUtensil=f.controller.PendingWorkCount;
            string stockUtensilAfter=Stock(f.Session);

            // Let Utensil naturally time out into real Dirty (burned) state.
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);
                if(f.Session.Lanes[0].step==KitchenStep.Dirty)break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.Lanes[0].step==KitchenStep.Dirty&&f.Session.Lanes[0].freeBatch==null,"Lane 0 never burned into real Dirty state");
            string stockDirty=Stock(f.Session);
            bool canAddDirty=f.Session.CanAddFreeIngredient(0,"egg",out string dirtyCanReason);
            bool atLaneDirtyEgg=f.controller.UseIngredientAtLane(0,"egg");
            bool uiDirtyEgg=f.controller.UseIngredient("egg");
            bool atLaneDirtyMeat=f.controller.UseIngredientAtLane(0,"meat");
            string dirtyHint=f.controller.InteractionHint??"";
            int pDirty=f.controller.PendingWorkCount;
            string stockDirtyAfter=Stock(f.Session);

            // Clean Dirty lane via real UseCookware(0) and verify recovery.
            int meatBeforeRecovery=f.Session.Count("meat");
            Need(f.controller.UseCookware(0),"Cleaning Dirty lane via UseCookware(0) rejected");
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(f.Session.Lanes[0].step==KitchenStep.Idle&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy)break;
                if(frame%60==0)yield return null;
            }
            Need(f.Session.Lanes[0].step==KitchenStep.Idle,"Dirty lane did not clean back to Idle");
            bool recoverAccepted=f.controller.UseIngredientAtLane(0,"meat");
            bool recovered=false;
            for(int frame=0;frame<30*60;frame++)
            {
                Tick(f);
                if(UnitsOf(f.Session.Lanes[0],"meat")==1&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy){recovered=true;break;}
                if(frame%60==0)yield return null;
            }

            item.detail="heating(canAdd="+canAddHeating+"["+heatingCanReason+"],atLane="+atLaneHeating+",ui="+uiHeating+",p="+pHeating+",hint='"+heatingHint+"')"
                +" utensil(canAdd="+canAddUtensil+"["+utensilCanReason+"],atLane="+atLaneUtensil+",ui="+uiUtensil+",p="+pUtensil+",hint='"+utensilHint+"')"
                +" dirty(canAdd="+canAddDirty+"["+dirtyCanReason+"],atLaneEgg="+atLaneDirtyEgg+",uiEgg="+uiDirtyEgg+",atLaneMeat="+atLaneDirtyMeat+",p="+pDirty+",hint='"+dirtyHint+"')"
                +" recovery(accepted="+recoverAccepted+",landed="+recovered+",meat="+meatBeforeRecovery+"->"+f.Session.Count("meat")+")";

            Need(!canAddHeating&&!atLaneHeating&&!uiHeating&&pHeating==0&&stockHeatingAfter==stockHeating,"Illegal ingredient accepted or mutated state during Heating: "+item.detail);
            Need(!canAddUtensil&&!atLaneUtensil&&!uiUtensil&&pUtensil==0&&stockUtensilAfter==stockUtensil,"Illegal ingredient accepted or mutated state during Utensil: "+item.detail);
            Need(!canAddDirty&&!atLaneDirtyEgg&&!uiDirtyEgg&&!atLaneDirtyMeat&&pDirty==0&&stockDirtyAfter==stockDirty,"Illegal ingredient accepted or mutated state during Dirty: "+item.detail);
            Need(recoverAccepted&&recovered&&UnitsOf(f.Session.Lanes[0],"meat")==1&&f.Session.Count("meat")==meatBeforeRecovery-1,"Lane 0 did not recover cleanly after cleaning Dirty state: "+item.detail);
        }
        // Fact-recording dynamic contrast A (scripted Controller/Stage simulation; not human mouse
        // input or full visual verification): on BusinessSetup.MidgamePractice() with 2 physical
        // lanes, deliver egg to lane 0 via real actor contact until idle, verify focus on lane 0,
        // inspect CanAddFreeIngredient(0,fruit) and CanAddFreeIngredient(1,fruit), then invoke
        // player entry UseIngredient("fruit") and tick until idle, faithfully recording whether it
        // rejects immediately or auto-selects and delivers to lane 1 without prescribing a branch.
        static IEnumerator MidgameEggLane0UseFruitObservation(Case item)
        {
            item.fixture="Fact-recording scripted Controller/Stage observation (not human mouse input, not full visual verification) on BusinessSetup.MidgamePractice() with 2 physical lanes: real egg delivery to lane 0 until actor idle, check focus on lane 0 and CanAddFreeIngredient(0/1,fruit), then call UseIngredient(fruit) and tick until idle; records observed behavior neutrally without prescribing a branch.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            int laneCount=f.Session.Lanes.Length;
            int eggInit=f.Session.Count("egg"),fruitInit=f.Session.Count("fruit"),riceInit=f.Session.Count("rice"),gasInit=f.Session.Gas;
            int historyStart=f.controller.CaptureWorkHistory().Length;
            item.detail="[ContrastA:init] practice="+f.Session.Setup.practice+",lanes="+laneCount
                +",stock(egg="+eggInit+",fruit="+fruitInit+",rice="+riceInit+",gas="+gasInit+")";
            Need(f.Session.Setup.practice&&laneCount==2,"Setup precondition failed: expected MidgamePractice with 2 physical lanes: "+item.detail);

            var hintLog=new List<string>();
            string lastHint=f.controller.InteractionHint??"";
            hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"[init]:'"+lastHint+"'");
            Action<string> recordHint=tag=>
            {
                string cur=f.controller.InteractionHint??"";
                if(cur!=lastHint){lastHint=cur;hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"["+tag+"]:'"+cur+"'");}
            };

            float setupEggAt=f.Session.Elapsed;
            bool setupEggRet=f.controller.UseIngredientAtLane(0,"egg");
            int selAfterSetupEgg=f.controller.SelectedLaneIndex;
            string hintAfterSetupEgg=f.controller.InteractionHint??"";
            recordHint("setup_egg");
            item.detail+=" setupEgg(t="+setupEggAt.ToString("F3")+",ret="+setupEggRet+",sel="+selAfterSetupEgg+",hint='"+hintAfterSetupEgg+"')";
            Need(setupEggRet,"Setup precondition failed: UseIngredientAtLane(0,egg) returned false: "+item.detail);

            bool setupEggContact=false,setupIdle=false;
            float setupEggContactAt=-1f;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);recordHint("setup_f"+(frame+1));
                if(!setupEggContact&&UnitsOf(f.Session.Lanes[0],"egg")==1){setupEggContact=true;setupEggContactAt=f.Session.Elapsed;}
                if(setupEggContact&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {setupIdle=true;break;}
                if(frame%60==0)yield return null;
            }
            float setupIdleAt=f.Session.Elapsed;
            int selPreFruit=f.controller.SelectedLaneIndex;
            int eggPreFruit=f.Session.Count("egg"),fruitPreFruit=f.Session.Count("fruit"),gasPreFruit=f.Session.Gas;
            string hintPreFruit=f.controller.InteractionHint??"";
            BusinessWorkAudit[] setupHistory=f.controller.CaptureWorkHistory();
            item.detail+=" setupSettled(contact="+setupEggContact+",contactAt="+setupEggContactAt.ToString("F3")+",idle="+setupIdle+",idleAt="+setupIdleAt.ToString("F3")
                +",sel="+selPreFruit+",hint='"+hintPreFruit+"',stock(egg="+eggPreFruit+",fruit="+fruitPreFruit+",gas="+gasPreFruit+")"
                +","+DescribeLaneState(f.Session,0)+","+DescribeLaneState(f.Session,1)
                +",work="+FormatWorkSliceExact(setupHistory,historyStart)+")";
            Need(setupEggContact&&setupIdle&&UnitsOf(f.Session.Lanes[0],"egg")==1&&f.Session.Lanes[0].step==KitchenStep.Prep&&eggPreFruit==eggInit-1,
                "Setup precondition failed: egg did not commit to lane 0 and reach idle Prep state: "+item.detail);
            Need(f.Session.Lanes[1].step==KitchenStep.Idle&&UnitsOf(f.Session.Lanes[1],"egg")==0&&UnitsOf(f.Session.Lanes[1],"fruit")==0,
                "Setup precondition failed: lane 1 is not Idle/empty before fruit input: "+item.detail);
            Need(selPreFruit==0,"Setup precondition failed: SelectedLaneIndex is "+selPreFruit+" instead of 0 before fruit input: "+item.detail);

            bool canFruit0=f.Session.CanAddFreeIngredient(0,"fruit",out string canFruit0Reason);
            bool canFruit1=f.Session.CanAddFreeIngredient(1,"fruit",out string canFruit1Reason);
            int historyBeforeFruit=setupHistory.Length;
            int pendingBeforeFruit=f.controller.PendingWorkCount;

            float fruitCallAt=f.Session.Elapsed;
            bool fruitRet=f.controller.UseIngredient("fruit");
            int selAfterFruitCall=f.controller.SelectedLaneIndex;
            string hintAfterFruitCall=f.controller.InteractionHint??"";
            string activeAfterFruitCall=f.controller.ActiveWorkLabel??"";
            string pendingAfterFruitCall=f.controller.PendingWorkLabel??"";
            int pendingAfterFruitCount=f.controller.PendingWorkCount;
            bool busyAfterFruitCall=f.stage.WorkActorBusy;
            int eggAfterFruitCall=f.Session.Count("egg"),fruitAfterFruitCall=f.Session.Count("fruit");
            recordHint("call_fruit");

            bool fruitRunCompleted=false;
            for(int frame=0;frame<40*60;frame++)
            {
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {fruitRunCompleted=true;break;}
                Tick(f);recordHint("fruit_f"+(frame+1));
                if(frame%60==0)yield return null;
            }
            if(!fruitRunCompleted&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                fruitRunCompleted=true;

            float finalAt=f.Session.Elapsed;
            int selFinal=f.controller.SelectedLaneIndex;
            string hintFinal=f.controller.InteractionHint??"";
            int eggFinal=f.Session.Count("egg"),fruitFinal=f.Session.Count("fruit"),riceFinal=f.Session.Count("rice"),gasFinal=f.Session.Gas;
            int fruitDelta=fruitPreFruit-fruitFinal;
            int lane0Fruit=UnitsOf(f.Session.Lanes[0],"fruit"),lane1Fruit=UnitsOf(f.Session.Lanes[1],"fruit");
            BusinessWorkAudit[] finalHistory=f.controller.CaptureWorkHistory();
            int fruitWorkCount=finalHistory.Length-historyBeforeFruit;
            string observedOutcome=!fruitRet&&fruitWorkCount==0&&lane0Fruit==0&&lane1Fruit==0&&fruitDelta==0?"rejected_immediately"
                :fruitRet&&lane1Fruit>0&&lane0Fruit==0?"auto_selected_and_delivered_lane1"
                :fruitRet&&lane0Fruit>0?"delivered_lane0"
                :fruitRet?"accepted_without_delivery"
                :"other";

            item.detail+=" preFruit(canAdd0="+canFruit0+"['"+canFruit0Reason+"'],canAdd1="+canFruit1+"['"+canFruit1Reason+"'],pCount="+pendingBeforeFruit+")"
                +" callFruit(t="+fruitCallAt.ToString("F3")+",ret="+fruitRet+",sel="+selAfterFruitCall+",busy="+busyAfterFruitCall
                +",active='"+activeAfterFruitCall+"',pending='"+pendingAfterFruitCall+"',pCount="+pendingAfterFruitCount
                +",stock(egg="+eggAfterFruitCall+",fruit="+fruitAfterFruitCall+"),hint='"+hintAfterFruitCall+"')"
                +" final(completed="+fruitRunCompleted+",t="+finalAt.ToString("F3")+",observedOutcome="+observedOutcome
                +",sel="+selFinal+",hint='"+hintFinal+"'"
                +",stock(egg="+eggInit+"->"+eggFinal+",fruit="+fruitInit+"->"+fruitFinal+"[d="+fruitDelta+"],rice="+riceInit+"->"+riceFinal+",gas="+gasInit+"->"+gasFinal+")"
                +",lanes=["+DescribeLaneState(f.Session,0)+"; "+DescribeLaneState(f.Session,1)+"]"
                +",fruitWork="+FormatWorkSliceExact(finalHistory,historyBeforeFruit)
                +",allWork="+FormatWorkSliceExact(finalHistory,historyStart)
                +",hintTimeline=["+string.Join(" | ",hintLog)+"])";

            Need(fruitRunCompleted,"Fruit action did not reach idle within 40s timeout: "+item.detail);
            Need(fruitRet&&selAfterFruitCall==1,"Expected UseIngredient(fruit) to succeed and select lane 1 (2号锅): "+item.detail);
            Need(fruitAfterFruitCall==fruitPreFruit,"Fruit stock must not debit at click time before actor contact: "+item.detail);
            Need(fruitDelta==1&&lane1Fruit==1&&lane0Fruit==0,"Expected fruit to debit exactly 1 and land on lane 1 (2号锅) only: "+item.detail);
            bool hintHasCompatReason=hintAfterFruitCall.Contains("不合")||hintAfterFruitCall.Contains("不能")||hintAfterFruitCall.Contains("不适合")||hintAfterFruitCall.Contains("不兼容");
            Need(hintAfterFruitCall.Contains("1号锅")&&hintAfterFruitCall.Contains("2号锅")&&hintHasCompatReason,
                "Expected click hint to mention 1号锅, 2号锅, and compatibility reason: "+item.detail);
        }
        // Fact-recording dynamic contrast B (scripted Controller/Stage simulation; not human mouse
        // input or full visual verification): on BusinessSetup.MidgamePractice() with 2 physical
        // lanes, deliver egg to lane 0 until idle, select stir_fry via real API, call UseCookware(0)
        // to dispatch start-cooking, verify it is dispatched and not yet contacted, then at the
        // exact same simulation timestamp call UseIngredientAtLane(0,rice) and tick until the work
        // queue empties; faithfully records start-cook status, rice delivery, final lane state,
        // energy delta, and hints without prescribing a branch.
        static IEnumerator MidgameDispatchedStartThenRiceObservation(Case item)
        {
            item.fixture="Fact-recording scripted Controller/Stage observation (not human mouse input, not full visual verification) on BusinessSetup.MidgamePractice() with 2 physical lanes: real egg delivery to lane 0 until idle, SetFreeCookingMethod(0,stir_fry), UseCookware(0) dispatched before contact, same-timestamp UseIngredientAtLane(0,rice), tick until queue empty; records observed behavior neutrally without prescribing a branch.";
            Fixture f=Create(BusinessSetup.MidgamePractice());yield return null;
            int laneCount=f.Session.Lanes.Length;
            int eggInit=f.Session.Count("egg"),riceInit=f.Session.Count("rice"),fruitInit=f.Session.Count("fruit");
            int gasInit=f.Session.Gas,elecInit=f.Session.Electricity,coalInit=f.Session.Coal,cartInit=f.Session.Cartridge;
            int historyStart=f.controller.CaptureWorkHistory().Length;
            item.detail="[ContrastB:init] practice="+f.Session.Setup.practice+",lanes="+laneCount
                +",stock(egg="+eggInit+",rice="+riceInit+",fruit="+fruitInit+")"
                +",energy(gas="+gasInit+",elec="+elecInit+",coal="+coalInit+",cart="+cartInit+")";
            Need(f.Session.Setup.practice&&laneCount==2,"Setup precondition failed: expected MidgamePractice with 2 physical lanes: "+item.detail);

            var hintLog=new List<string>();
            string lastHint=f.controller.InteractionHint??"";
            hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"[init]:'"+lastHint+"'");
            Action<string> recordHint=tag=>
            {
                string cur=f.controller.InteractionHint??"";
                if(cur!=lastHint){lastHint=cur;hintLog.Add("t="+f.Session.Elapsed.ToString("F3")+"["+tag+"]:'"+cur+"'");}
            };

            float setupEggAt=f.Session.Elapsed;
            bool setupEggRet=f.controller.UseIngredientAtLane(0,"egg");
            int selAfterSetupEgg=f.controller.SelectedLaneIndex;
            string hintAfterSetupEgg=f.controller.InteractionHint??"";
            recordHint("setup_egg");
            item.detail+=" setupEgg(t="+setupEggAt.ToString("F3")+",ret="+setupEggRet+",sel="+selAfterSetupEgg+",hint='"+hintAfterSetupEgg+"')";
            Need(setupEggRet,"Setup precondition failed: UseIngredientAtLane(0,egg) returned false: "+item.detail);

            bool setupEggContact=false,setupIdle=false;
            float setupEggContactAt=-1f;
            for(int frame=0;frame<40*60;frame++)
            {
                Tick(f);recordHint("setup_f"+(frame+1));
                if(!setupEggContact&&UnitsOf(f.Session.Lanes[0],"egg")==1){setupEggContact=true;setupEggContactAt=f.Session.Elapsed;}
                if(setupEggContact&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {setupIdle=true;break;}
                if(frame%60==0)yield return null;
            }
            float setupIdleAt=f.Session.Elapsed;
            string[] methodsAvailable=f.Session.FreeMethods(0);
            bool stirFryAvailable=Array.IndexOf(methodsAvailable,FreeKitchenRules.MethodStirFry)>=0;
            item.detail+=" setupSettled(contact="+setupEggContact+",contactAt="+setupEggContactAt.ToString("F3")+",idle="+setupIdle+",idleAt="+setupIdleAt.ToString("F3")
                +",sel="+f.controller.SelectedLaneIndex+",methods=["+string.Join(",",methodsAvailable)+"],"+DescribeLaneState(f.Session,0)+")";
            Need(setupEggContact&&setupIdle&&UnitsOf(f.Session.Lanes[0],"egg")==1&&f.Session.Lanes[0].step==KitchenStep.Prep&&f.Session.Count("egg")==eggInit-1,
                "Setup precondition failed: egg did not commit to lane 0 and reach idle Prep state: "+item.detail);
            Need(stirFryAvailable,"Setup precondition failed: stir_fry not in FreeMethods(0): "+item.detail);

            float setMethodAt=f.Session.Elapsed;
            bool setMethodRet=f.controller.SetFreeCookingMethod(0,FreeKitchenRules.MethodStirFry);
            string hintAfterSetMethod=f.controller.InteractionHint??"";
            recordHint("set_method");
            string methodAfterSet=f.Session.Lanes[0].freeBatch!=null?(f.Session.Lanes[0].freeBatch.method??""):"";
            bool canRicePreStart=f.Session.CanAddFreeIngredient(0,"rice",out string canRicePreStartReason);
            item.detail+=" setMethod(t="+setMethodAt.ToString("F3")+",ret="+setMethodRet+",method='"+methodAfterSet+"',canRice="+canRicePreStart+"['"+canRicePreStartReason+"'],hint='"+hintAfterSetMethod+"')";
            Need(setMethodRet&&methodAfterSet==FreeKitchenRules.MethodStirFry&&f.Session.Lanes[0].step==KitchenStep.Prep,
                "Setup precondition failed: SetFreeCookingMethod(0,stir_fry) did not lock stir_fry in Prep: "+item.detail);
            Need(canRicePreStart,"Setup precondition failed: CanAddFreeIngredient(0,rice) was false before UseCookware(0): "+item.detail);

            int gasPreStart=f.Session.Gas,elecPreStart=f.Session.Electricity,coalPreStart=f.Session.Coal,cartPreStart=f.Session.Cartridge;
            int ricePreStart=f.Session.Count("rice"),eggPreStart=f.Session.Count("egg");
            int historyBeforeStart=f.controller.CaptureWorkHistory().Length;

            float startCallAt=f.Session.Elapsed;
            bool startRet=f.controller.UseCookware(0);
            int selAfterStart=f.controller.SelectedLaneIndex;
            bool busyAfterStart=f.stage.WorkActorBusy;
            string activeAfterStart=f.controller.ActiveWorkLabel??"";
            string pendingAfterStart=f.controller.PendingWorkLabel??"";
            int pendingCountAfterStart=f.controller.PendingWorkCount;
            KitchenStep stepAfterStart=f.Session.Lanes[0].step;
            int gasAfterStart=f.Session.Gas;
            int historyAfterStart=f.controller.CaptureWorkHistory().Length;
            string hintAfterStart=f.controller.InteractionHint??"";
            bool hasCookwareIntentAfterStart=f.controller.HasPendingIntent(StallInteractionKind.Cookware,0);
            recordHint("call_start");

            item.detail+=" callStart(t="+startCallAt.ToString("F3")+",ret="+startRet+",sel="+selAfterStart+",busy="+busyAfterStart
                +",active='"+activeAfterStart+"',pending='"+pendingAfterStart+"',pCount="+pendingCountAfterStart
                +",hasCookwareIntent="+hasCookwareIntentAfterStart+",lane0Step="+stepAfterStart
                +",gas="+gasAfterStart+",histLen="+historyBeforeStart+"->"+historyAfterStart+",hint='"+hintAfterStart+"')";
            Need(startRet,"Setup precondition failed: UseCookware(0) returned false: "+item.detail);
            Need(busyAfterStart&&activeAfterStart.Contains("开火烹制")&&pendingCountAfterStart==0&&hasCookwareIntentAfterStart
                &&stepAfterStart==KitchenStep.Prep&&gasAfterStart==gasPreStart&&historyAfterStart==historyBeforeStart,
                "Setup precondition failed: start-cook intent was not in dispatched-before-contact state: "+item.detail);

            // Same simulation timestamp (no Tick between UseCookware(0) and UseIngredientAtLane(0,rice))
            float riceCallAt=f.Session.Elapsed;
            bool canRiceAtDispatch=f.Session.CanAddFreeIngredient(0,"rice",out string canRiceAtDispatchReason);
            bool riceRet=f.controller.UseIngredientAtLane(0,"rice");
            int selAfterRice=f.controller.SelectedLaneIndex;
            bool busyAfterRice=f.stage.WorkActorBusy;
            string activeAfterRice=f.controller.ActiveWorkLabel??"";
            string pendingAfterRice=f.controller.PendingWorkLabel??"";
            int pendingCountAfterRice=f.controller.PendingWorkCount;
            int riceAfterRiceCall=f.Session.Count("rice"),gasAfterRiceCall=f.Session.Gas;
            KitchenStep stepAfterRiceCall=f.Session.Lanes[0].step;
            string hintAfterRice=f.controller.InteractionHint??"";
            recordHint("call_rice");

            item.detail+=" callRice(t="+riceCallAt.ToString("F3")+",dt="+(riceCallAt-startCallAt).ToString("F3")
                +",canRice="+canRiceAtDispatch+"['"+canRiceAtDispatchReason+"'],ret="+riceRet+",sel="+selAfterRice
                +",busy="+busyAfterRice+",active='"+activeAfterRice+"',pending='"+pendingAfterRice+"',pCount="+pendingCountAfterRice
                +",lane0Step="+stepAfterRiceCall+",rice="+riceAfterRiceCall+",gas="+gasAfterRiceCall+",hint='"+hintAfterRice+"')";
            Need(Mathf.Approximately(startCallAt,riceCallAt),"Setup precondition failed: UseIngredientAtLane(0,rice) did not occur at same simulation time as UseCookware(0): "+item.detail);

            bool completed=false;
            for(int frame=0;frame<40*60;frame++)
            {
                if(f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                {completed=true;break;}
                Tick(f);recordHint("run_f"+(frame+1));
                if(frame%60==0)yield return null;
            }
            if(!completed&&f.controller.PendingWorkCount==0&&!f.stage.WorkActorBusy&&string.IsNullOrEmpty(f.controller.ActiveWorkLabel))
                completed=true;

            float finalAt=f.Session.Elapsed;
            int selFinal=f.controller.SelectedLaneIndex;
            string hintFinal=f.controller.InteractionHint??"";
            int eggFinal=f.Session.Count("egg"),riceFinal=f.Session.Count("rice"),fruitFinal=f.Session.Count("fruit");
            int gasFinal=f.Session.Gas,elecFinal=f.Session.Electricity,coalFinal=f.Session.Coal,cartFinal=f.Session.Cartridge;
            int gasDelta=gasPreStart-gasFinal,elecDelta=elecPreStart-elecFinal,coalDelta=coalPreStart-coalFinal,cartDelta=cartPreStart-cartFinal;
            bool energyDeducted=gasDelta>0||elecDelta>0||coalDelta>0||cartDelta>0;
            int riceDelta=ricePreStart-riceFinal;
            int lane0Rice=UnitsOf(f.Session.Lanes[0],"rice"),lane1Rice=UnitsOf(f.Session.Lanes[1],"rice");

            BusinessWorkAudit[] finalHistory=f.controller.CaptureWorkHistory();
            BusinessWorkAudit startWork=null,riceWork=null;
            for(int i=historyBeforeStart;i<finalHistory.Length;i++)
            {
                var w=finalHistory[i];
                if(startWork==null&&w.action=="开火烹制")startWork=w;
                else if(riceWork==null&&w.action=="投料")riceWork=w;
            }

            item.detail+=" final(completed="+completed+",t="+finalAt.ToString("F3")+",sel="+selFinal+",hint='"+hintFinal+"'"
                +",observed(startStatus="+(startWork!=null?startWork.status:"none")+",startSerial="+(startWork!=null?startWork.serial:-1)
                +",startContactAt="+(startWork!=null?startWork.contactAt.ToString("F3"):"-1.000")
                +",riceWorkStatus="+(riceWork!=null?riceWork.status:"none")+",riceWorkSerial="+(riceWork!=null?riceWork.serial:-1)
                +",riceContactAt="+(riceWork!=null?riceWork.contactAt.ToString("F3"):"-1.000")
                +",riceLandedLane0="+(lane0Rice>0)+",lane0Rice="+lane0Rice+",lane1Rice="+lane1Rice
                +",finalLane0Step="+f.Session.Lanes[0].step+",energyDeducted="+energyDeducted+")"
                +",stock(egg="+eggInit+"->"+eggFinal+",rice="+riceInit+"->"+riceFinal+"[d="+riceDelta+"],fruit="+fruitInit+"->"+fruitFinal+")"
                +",energy(gas="+gasPreStart+"->"+gasFinal+"[d="+gasDelta+"],elec="+elecPreStart+"->"+elecFinal+"[d="+elecDelta
                +"],coal="+coalPreStart+"->"+coalFinal+"[d="+coalDelta+"],cart="+cartPreStart+"->"+cartFinal+"[d="+cartDelta+"])"
                +",lanes=["+DescribeLaneState(f.Session,0)+"; "+DescribeLaneState(f.Session,1)+"]"
                +",postStartWork="+FormatWorkSliceExact(finalHistory,historyBeforeStart)
                +",allWork="+FormatWorkSliceExact(finalHistory,historyStart)
                +",hintTimeline=["+string.Join(" | ",hintLog)+"])";

            Need(completed,"Work queue did not empty within 40s timeout: "+item.detail);
            Need(riceWork!=null&&riceWork.status=="committed"&&startWork!=null&&startWork.status=="committed",
                "Expected both riceWork and startWork to be committed: "+item.detail);
            Need(riceWork.contactAt<startWork.contactAt,"Expected riceWork.contactAt to precede startWork.contactAt: "+item.detail);
            Need(riceDelta==1&&gasDelta==1&&elecDelta==0&&coalDelta==0&&cartDelta==0,
                "Expected exactly 1 rice and 1 gas debited with no other energy deducted: "+item.detail);
            int startCookCount=0,startCookCommitted=0;
            for(int i=historyBeforeStart;i<finalHistory.Length;i++)
            {
                var w=finalHistory[i];
                if(w.action=="开火烹制"){startCookCount++;if(w.status=="committed")startCookCommitted++;}
            }
            Need(startCookCount==1&&startCookCommitted==1,
                "Expected exactly one committed 开火烹制 record from historyBeforeStart (count="+startCookCount+",committed="+startCookCommitted+"): "+item.detail);
        }
        static string DescribeLaneState(BusinessSession s,int laneIndex)
        {
            KitchenLane l=s.Lanes[laneIndex];
            string method=l.freeBatch!=null?(l.freeBatch.method??""):"";
            string contents=s.LaneContents(laneIndex);
            return "lane"+laneIndex+"(step="+l.step+",method='"+method+"',contents='"+contents+"',egg="+UnitsOf(l,"egg")+",rice="+UnitsOf(l,"rice")+",fruit="+UnitsOf(l,"fruit")+",batchRem="+l.batchRemaining+",energy='"+(l.energy??"")+"')";
        }
        static string FormatWorkSliceExact(BusinessWorkAudit[] history,int startIndex)
        {
            var parts=new List<string>();
            for(int i=startIndex;i<history.Length;i++)parts.Add(FormatWorkEntry(history[i]));
            return "["+string.Join("; ",parts)+"]";
        }
        static string FormatWorkEntry(BusinessWorkAudit w)
        {
            if(w==null)return "none";
            return "#"+w.serial+"(lane="+w.lane+",act="+w.action+",st="+w.status
                +",q="+w.queuedAt.ToString("F3")+",s="+w.startedAt.ToString("F3")
                +",c="+w.contactAt.ToString("F3")+",f="+w.finishedAt.ToString("F3")+")";
        }
        static string FormatWorkSlice(BusinessWorkAudit[] history,int startIndex)
        {
            var parts=new List<string>();
            for(int i=startIndex;i<history.Length;i++)
            {
                var w=history[i];
                parts.Add("#"+w.serial+"(lane="+w.lane+",act="+w.action+",st="+w.status
                    +",q="+w.queuedAt.ToString("F2")+",s="+w.startedAt.ToString("F2")
                    +",c="+w.contactAt.ToString("F2")+",f="+w.finishedAt.ToString("F2")+")");
            }
            return "["+string.Join("; ",parts)+"]";
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
            report.engineErrors=errors.ToArray();report.success=failure==""&&report.failed==0&&report.passed==ExpectedCaseCount&&errors.Count==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
