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
    // Independent supervisor acceptance. This is a finite fixture with explicit
    // starting supplies, real Stage/actor/collision, and scripted Controller inputs.
    // It is not an OS-input or full-day human playthrough.
    [InitializeOnLoad] public static class Service20Acceptance
    {
        [Serializable] public sealed class MealSnapshot
        {public string id,packageId,flavorId,name,method,inputs="";public int portions,remaining,priceCents;public float quality;}
        [Serializable] public sealed class ContactView
        {public string subject;public float[] viewportMinMax;public int width,height,renderers;}
        [Serializable] public sealed class Case
        {public string id,status="running",detail="",failure="",contactVisibilityFailure="";public float elapsed,customerTravel,maxCustomerStep,pickupContactError,directContactError;public int sales;public MealSnapshot meal;public BusinessAuditState final;public BusinessWorkAudit[] work;public List<ContactView> contactViews=new List<ContactView>();}
        [Serializable] public sealed class Frame
        {public string name,path;public int width,height,tableMeals;public string heldMeal;}
        [Serializable] public sealed class Report
        {public string schema="guochao.service20.scene.v1",scope="Real PlayMode, fixed fixture supplies, natural arriving customers, normal patience, unchanged collision and 2.5cm contact tolerance; scripted Controller commands on real player frames. No full human-played day or hardware input claim.",failure="";public bool success;public int passed,failed;public List<Case> cases=new List<Case>();public List<Frame> screenshots=new List<Frame>();public string[] engineErrors;}
        sealed class Fixture
        {public GameObject root;public BusinessController controller;public StreetStallStage stage;public BusinessSession Session=>controller.Session;public readonly List<string> failures=new List<string>();}
        const float Step=1f/60;
        const string Pending="Guochao.Service20.Pending",Output="Guochao.Service20.Output",UI="Guochao.Service20.UI";
        static Fixture live;static Report report;static string output;static bool withUI;static double began;
        static readonly List<string> errors=new List<string>();static Feedback3CoroutineHost frameHost;
        static Service20Acceptance(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--service20-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("--service20-output <new report.json> required");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Preserve previous evidence");
            SessionState.SetString(Output,path);SessionState.SetBool(UI,Array.IndexOf(args,"--service20-ui")>=0);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");withUI=SessionState.GetBool(UI,false);report=new Report();errors.Clear();began=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;
                frameHost=new GameObject("Service20_FrameHost").AddComponent<Feedback3CoroutineHost>();frameHost.Launch(Cases(),()=>Finish(""),ex=>Finish(ex.ToString()));
            }
            if(frameHost!=null&&frameHost.IsRunning&&EditorApplication.timeSinceStartup-began>600){frameHost.Abort();Finish("Service20 exceeded 600 seconds");}
        }
        static IEnumerator Cases()
        {
            Need(GameFlow.Instance==null,"An isolated empty PlayMode scene is required");
            string[] names={"pack-held-direct-fixed-customer","counter-customer-pickup-with-vendor-prep","cancel-and-departure-retain-counter-meal","held-json-save-restore","auntie-real-counter-pickup"};
            Func<Case,IEnumerator>[] tests={Direct,PickupWhilePreparing,CancelAndDepart,HeldSave,AuntiePickup};
            string[] arguments=Environment.GetCommandLineArgs();int filterAt=Array.IndexOf(arguments,"--service20-case");
            string filter=filterAt>=0&&filterAt+1<arguments.Length?arguments[filterAt+1]:"";
            for(int i=0;i<tests.Length;i++)
            {
                if(filter!=""&&names[i].IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0)continue;
                var item=new Case{id=names[i]};report.cases.Add(item);IEnumerator test=tests[i](item);bool done=false;
                while(!done)
                {
                    object yielded=null;
                    try{if(test.MoveNext())yielded=test.Current;else{item.status="passed";done=true;}}
                    catch(Exception e){item.status="failed";item.failure=e.ToString();done=true;}
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
        static Fixture Create(BusinessSetup customSetup=null,string fixtureCustomerArchetype=null)
        {
            Need(live==null,"Fixtures must not overlap");var f=new Fixture{root=new GameObject("Service20_RealStage")};live=f;
            var session=new BusinessSession(customSetup??Setup());session.SelectedRecipes.Clear();
            Need(session.Open(),"Free-batch session must open with no SelectedRecipes gate: "+session.Message);
            f.controller=f.root.AddComponent<BusinessController>();f.controller.enabled=false;
            f.stage=f.root.AddComponent<StreetStallStage>();
            f.stage.Initialize(session.Setup,null);
            if(fixtureCustomerArchetype!=null)
            {
                // Editor-only appearance fixture; orders, stock, arrival clocks
                // and patience still use the ordinary economic simulation.
                var assets=(List<LoadedCharacterAsset>)typeof(StreetStallStage).GetField("customerAssets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.stage);
                LoadedCharacterAsset chosen=assets.Find(a=>a.Definition.Id==fixtureCustomerArchetype);Need(chosen!=null,"Missing specified real customer asset");assets.Clear();assets.Add(chosen);
            }
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
                yield return new WaitForEndOfFrame();
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
                Tick(f);yield return new WaitForEndOfFrame();
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
                Tick(f);yield return new WaitForEndOfFrame();
            }
            FoodProduct product=f.Session.ProductAtLane(lane);
            Need(product!=null,"No real product created from actual inputs");
            Need(product.method==FreeKitchenRules.MethodStirFry,"Product method mismatch: "+product.method);
            item.meal=new MealSnapshot{id="pending",name=f.Session.LaneDishName(lane),method=product.method,portions=product.portions,remaining=product.remaining,priceCents=product.priceCents,quality=product.quality};
            foreach(var input in product.inputs)item.meal.inputs+=(item.meal.inputs==""?"":";")+input.itemId+"x"+input.units;
            item.detail="egg×1+meat×1 stir_fry cooked to '"+item.meal.name+"' portions="+product.portions+" remaining="+product.remaining+" price="+product.priceCents+"c via real actor contact.";
        }
        static IEnumerator SettleActor(Fixture f)
        {
            float until=f.Session.Elapsed+20;
            while(f.stage.WorkActorBusy){Need(f.Session.Elapsed<until,"Vendor did not finish action");Tick(f);yield return new WaitForEndOfFrame();}
        }
        static IEnumerator PackHeld(Fixture f,Case item)
        {
            IEnumerator cook=CookFreeBatchToReady(f,item,0);while(cook.MoveNext())yield return cook.Current;
            IEnumerator settle=SettleActor(f);while(settle.MoveNext())yield return settle.Current;
            int packages=f.Session.Count("P05"),cash=f.Session.CashCents;
            Need(f.controller.PackMeal(0,"P05"),"PackMeal rejected: "+f.controller.InteractionHint);
            float until=f.Session.Elapsed+30;
            while(f.Session.HeldMeal==null){Need(f.Session.Elapsed<until,"PackMeal never reached real HeldMeal");Need(f.Session.ReadyMeals.Count==0,"Direct packing must not enter counter");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.Count("P05")==packages-1&&f.Session.CashCents==cash,"Pack must consume one package without payment");
            Need(f.Session.ReadyMeals.Count==0,"Held meal incorrectly entered counter");
            item.meal.id=f.Session.HeldMeal.id;item.meal.packageId=f.Session.HeldMeal.packageId;
            settle=SettleActor(f);while(settle.MoveNext())yield return settle.Current;
        }
        static IEnumerator FindCustomer(Fixture f,Func<int> match,int[] sink)
        {
            float until=f.Session.Elapsed+120;
            while(true)
            {
                int id=match();if(id>=0){sink[0]=id;yield break;}
                Need(f.Session.Elapsed<until,"No natural compatible customer within normal 120-second window");
                Tick(f);yield return new WaitForEndOfFrame();
            }
        }
        static IEnumerator Place(Fixture f,string mealId)
        {
            int packages=f.Session.Count("P05"),cash=f.Session.CashCents;
            Need(f.controller.PlaceHeldMeal(),"PlaceHeldMeal rejected: "+f.controller.InteractionHint);
            float until=f.Session.Elapsed+30;
            while(f.Session.HeldMeal!=null){Need(f.Session.Elapsed<until,"Held meal never placed");Tick(f);yield return new WaitForEndOfFrame();}
            Need(FindMeal(f,mealId)!=null,"Placing did not preserve exact meal id");
            Need(f.Session.Count("P05")==packages&&f.Session.CashCents==cash,"Placing debited again or took payment");
            Need(f.stage.LastCounterPlacementError<=.025f,"Container failed physical table support pose");
            IEnumerator settle=SettleActor(f);while(settle.MoveNext())yield return settle.Current;
        }
        static CounterMeal FindMeal(Fixture f,string id){foreach(var m in f.Session.ReadyMeals)if(m.id==id)return m;return null;}
        static Transform CustomerBody(Fixture f,int orderId)
        {foreach(var t in f.root.GetComponentsInChildren<StallInteractionTarget>(true))if(t.kind==StallInteractionKind.Order&&t.orderId==orderId)return t.transform.parent;throw new InvalidOperationException("Missing real customer model "+orderId);}
        static void ContactVisible(Fixture f,Case item,int orderId,string mealId)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            Transform vendor=(Transform)typeof(StreetStallStage).GetField("player",flags).GetValue(f.stage);
            Transform meal=null;
            if(f.stage.HeldMealVisualId==mealId)meal=(Transform)typeof(StreetStallStage).GetField("heldProp",flags).GetValue(f.stage);
            else foreach(var target in f.root.GetComponentsInChildren<StallInteractionTarget>(true))
                if(target.kind==StallInteractionKind.CounterMeal&&target.itemId==mealId){meal=target.transform;break;}
            VisibleBounds(f,item,vendor,"vendor-at-contact");
            VisibleBounds(f,item,CustomerBody(f,orderId),"customer-at-contact");
            VisibleBounds(f,item,meal,"actual-meal-at-contact");
        }
        static void VisibleBounds(Fixture f,Case item,Transform subject,string label)
        {
            Need(subject!=null,"Missing physical subject "+label);int count=0,vertices=0;
            Vector3 min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity;
            foreach(var renderer in subject.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer.forceRenderingOff)continue;
                Need((f.stage.View.cullingMask&(1<<renderer.gameObject.layer))!=0,label+" excluded by camera layer");
                Mesh mesh=null;bool dispose=false;
                if(renderer is SkinnedMeshRenderer skin)
                {
                    // Imported bounds cover ALL animation extremes and are not
                    // the current body. true is the scale-compensated bake path
                    // independently verified against bind-pose weights in AssetsV15Validation.
                    mesh=new Mesh();skin.BakeMesh(mesh,true);dispose=true;
                }
                else {var filter=renderer.GetComponent<MeshFilter>();if(filter)mesh=filter.sharedMesh;}
                if(mesh==null)continue;count++;
                try
                {
                    foreach(Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point=f.stage.View.WorldToViewportPoint(renderer.transform.TransformPoint(vertex));
                        Need(!float.IsNaN(point.x)&&!float.IsInfinity(point.x),"Nonfinite actual mesh vertex");
                        min=Vector3.Min(min,point);max=Vector3.Max(max,point);vertices++;
                    }
                }
                finally{if(dispose)UnityEngine.Object.DestroyImmediate(mesh);}
            }
            Need(count>0&&vertices>0,"Physical subject has no active mesh "+label);
            item.contactViews.Add(new ContactView{subject=label,width=Screen.width,height=Screen.height,renderers=count,viewportMinMax=new[]{min.x,min.y,min.z,max.x,max.y,max.z}});
            Need(min.z>f.stage.View.nearClipPlane&&min.x>=0&&min.y>=0&&max.x<=1&&max.y<=1,label+" actual posed mesh is clipped at contact: "+min+" / "+max);
        }
        static void RecordContactVisibility(Fixture f,Case item,int orderId,string mealId)
        {
            // A diagnostic cannot undo or hide an already settled sale. Preserve
            // its failure separately, then fail the case outside the event callback.
            try{ContactVisible(f,item,orderId,mealId);}catch(Exception e){item.contactVisibilityFailure=e.ToString();}
        }
        static float ContactMetric(Fixture f,string property)
        {var p=typeof(StreetStallStage).GetProperty(property);Need(p!=null,"Missing actual contact audit "+property);return (float)p.GetValue(f.stage);}
        static IEnumerator Direct(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string mealId=f.Session.HeldMeal.id;int price=f.Session.HeldMeal.priceCents,cash=f.Session.CashCents,packages=f.Session.Count("P05");
            var target=new[]{-1};IEnumerator find=FindCustomer(f,()=>f.Session.FindServingHeldOrder(),target);while(find.MoveNext())yield return find.Current;
            if(withUI){IEnumerator pic=Capture(f,"held-ready",target[0]);while(pic.MoveNext())yield return pic.Current;}
            int events=0;f.Session.SaleCompleted+=(o,r,c,s)=>{Need(o==target[0],"Direct meal switched customer");events++;RecordContactVisibility(f,item,o,mealId);};
            Need(f.controller.DeliverHeldMealTo(target[0]),"Direct request rejected: "+f.controller.InteractionHint);
            float until=f.Session.Elapsed+35;
            while(f.Session.HeldMeal!=null)
            {Need(f.Session.HeldMeal.id==mealId,"Direct changed meal identity");Need(f.Session.ReadyMeals.Count==0,"Direct secretly inserted meal into counter");Need(f.Session.Elapsed<until,"Direct handoff did not finish");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.FindOrder(target[0]).served&&events==1,"Fixed customer did not receive exactly one sale");
            Need(item.contactVisibilityFailure=="",item.contactVisibilityFailure);
            Need(f.Session.CashCents==cash+price&&f.Session.Count("P05")==packages,"Direct ledger double debit/payment");
            item.directContactError=ContactMetric(f,"LastDirectHandoffContactError");Need(item.directContactError<=.025f,"Direct handoff missed measured palms");
            Need(f.stage.WorkActorBusy,"Checkpoint must exercise contact-before-vendor-retract window");
            BusinessSaveData checkpoint=f.controller.CaptureForSave();
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(checkpoint)));
            Need(restored.HeldMeal==null&&restored.ReadyMeals.Count==0&&restored.CashCents==cash+price,"Post-contact checkpoint replayed or lost handoff");
            for(int i=0;i<90;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(events==1&&!f.stage.WorkActorBusy,"Checkpoint left direct work stuck or repeated payment");
            if(withUI){IEnumerator pic=Capture(f,"direct-paid");while(pic.MoveNext())yield return pic.Current;}
            // A second real production/collection proves the transient visit was
            // released; no test mutates the visit, customer, or core ledger.
            make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string second=f.Session.HeldMeal.id;int secondPrice=f.Session.HeldMeal.priceCents;
            IEnumerator place=Place(f,second);while(place.MoveNext())yield return place.Current;
            find=FindCustomer(f,()=>f.Session.FindServingCounterOrder(second),target);while(find.MoveNext())yield return find.Current;
            mealId=second;
            Need(f.controller.RequestCounterPickup(second,target[0]),"Checkpoint left service visit permanently occupied");
            until=f.Session.Elapsed+40;
            while(FindMeal(f,second)!=null){Need(f.Session.Elapsed<until,"Collection after direct checkpoint never completed");Tick(f);yield return new WaitForEndOfFrame();}
            Need(events==2&&f.Session.CashCents==cash+price+secondPrice,"Post-checkpoint sale not conserved");
            Need(item.contactVisibilityFailure=="",item.contactVisibilityFailure);
            item.sales=events;item.detail="Real direct handoff saved after contact before retract; exact JSON ledger, then fresh cooking and customer pickup prove continued service without repeat payment.";
        }
        static IEnumerator PickupWhilePreparing(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string mealId=f.Session.HeldMeal.id;int price=f.Session.HeldMeal.priceCents;
            IEnumerator place=Place(f,mealId);while(place.MoveNext())yield return place.Current;
            var target=new[]{-1};IEnumerator find=FindCustomer(f,()=>f.Session.FindServingCounterOrder(mealId),target);while(find.MoveNext())yield return find.Current;
            Need(f.controller.SelectCounterMeal(mealId),"Select real table meal failed");
            if(withUI){IEnumerator pic=Capture(f,"table-selected",target[0]);while(pic.MoveNext())yield return pic.Current;}
            Transform customer=CustomerBody(f,target[0]);Vector3 previous=customer.position;int cash=f.Session.CashCents,packages=f.Session.Count("P05"),eggs=f.Session.Count("egg");int events=0;
            f.Session.SaleCompleted+=(o,r,c,s)=>{Need(o==target[0],"Pickup switched customer");events++;RecordContactVisibility(f,item,o,mealId);};
            Need(f.controller.RequestCounterPickup(mealId,target[0]),"Customer pickup request rejected: "+f.controller.InteractionHint+" / "+f.stage.LastPickupRejection);
            Need(f.controller.PickupOrderForMeal(mealId)==target[0],"Reservation did not bind exact customer");
            f.stage.CancelWork(); // Same vendor-only cancellation used by kitchen rescheduling.
            Need(f.controller.PickupOrderForMeal(mealId)==target[0]&&f.stage.IsCounterMealReserved(mealId),"Vendor cancellation cancelled independent customer pickup");
            Need(f.controller.UseIngredientAtLane(0,"egg"),"Vendor cannot prep while customer comes to collect");
            float until=f.Session.Elapsed+40;
            while(FindMeal(f,mealId)!=null)
            {
                Need(f.Session.Elapsed<until,"Customer did not collect table meal");Tick(f);yield return new WaitForEndOfFrame();
                float distance=Vector3.Distance(previous,customer.position);item.customerTravel+=distance;item.maxCustomerStep=Mathf.Max(item.maxCustomerStep,distance);previous=customer.position;
                Need(distance<=.12f,"Customer root jumped rather than walking");
            }
            Need(item.customerTravel>.20f,"Customer did not physically come to meal");
            Need(f.Session.Count("egg")==eggs-1||f.controller.HasPendingIntent(StallInteractionKind.Ingredient,0),"Independent preparation disappeared");
            foreach(var work in f.controller.CaptureWorkHistory())Need(!(work.orderId==target[0]&&work.action.Contains("送餐")),"Vendor delivered a customer-pickup meal");
            Need(events==1&&f.Session.FindOrder(target[0]).served,"Pickup did not create exactly one fixed-customer sale");
            Need(item.contactVisibilityFailure=="",item.contactVisibilityFailure);
            Need(f.Session.CashCents==cash+price&&f.Session.Count("P05")==packages,"Pickup charged/debited incorrectly");
            item.pickupContactError=ContactMetric(f,"LastCustomerPickupContactError");Need(item.pickupContactError<=.025f,"Customer pickup missed actual hand contact");
            until=f.Session.Elapsed+3f;
            while(f.controller.PickupOrderForMeal(mealId)>=0)
            {Need(f.Session.Elapsed<until,"Completed receiving gesture kept stale reservation");Tick(f);yield return new WaitForEndOfFrame();}

            item.sales=events;item.detail="Customer walked to reserved food and took it at measured contact while vendor preparation remained independent.";
            if(withUI){IEnumerator pic=Capture(f,"customer-collected");while(pic.MoveNext())yield return pic.Current;}
        }
        static IEnumerator CancelAndDepart(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string mealId=f.Session.HeldMeal.id;IEnumerator place=Place(f,mealId);while(place.MoveNext())yield return place.Current;
            var target=new[]{-1};IEnumerator find=FindCustomer(f,()=>f.Session.FindServingCounterOrder(mealId),target);while(find.MoveNext())yield return find.Current;
            int cash=f.Session.CashCents,packages=f.Session.Count("P05");Need(f.controller.RequestCounterPickup(mealId,target[0]),"Pickup reservation rejected: "+f.stage.LastPickupRejection);
            Need(f.controller.CancelCounterPickup(mealId),"Cancel pending pickup rejected");Need(f.controller.PickupOrderForMeal(mealId)<0,"Cancel kept reservation");
            Need(FindMeal(f,mealId)!=null&&f.Session.CashCents==cash,"Cancel lost meal or charged money");
            float until=f.Session.Elapsed+90;
            while(!f.Session.FindOrder(target[0]).departed){Need(f.Session.Elapsed<until,"Normal patience never expired");Tick(f);yield return new WaitForEndOfFrame();}
            Need(!f.controller.RequestCounterPickup(mealId,target[0]),"Departed customer accepted");
            Need(FindMeal(f,mealId)!=null&&f.Session.CashCents==cash&&f.Session.Count("P05")==packages,"Cancelled/departed request mutated meal ledger");
            item.detail="Cancelled reservation clears; real patience later expires; food remains and cannot be assigned to the departed customer.";
        }
        static IEnumerator HeldSave(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string mealId=f.Session.HeldMeal.id,before=Stock(f.Session);int ready=f.Session.ReadyMeals.Count;
            BusinessSaveData saved=f.controller.CaptureForSave();string json=JsonUtility.ToJson(saved);var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(json));
            Need(restored.HeldMeal!=null&&restored.HeldMeal.id==mealId,"JSON lost held meal identity");Need(restored.ReadyMeals.Count==ready,"JSON moved held meal to counter");Need(Stock(restored)==before,"JSON changed ledger");
            int replays=0;restored.SaleCompleted+=(o,r,c,s)=>replays++;restored.Tick(.5f);Need(replays==0&&Stock(restored)==before,"Restore replayed payment");
            for(int i=0;i<3;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.HeldMeal!=null&&f.Session.HeldMeal.id==mealId,"Save cancellation discarded live held meal");
            item.detail="Controller checkpoint and real JsonUtility roundtrip retain independent held meal without counter insertion or repeated payment.";
            if(withUI){IEnumerator pic=Capture(f,"held-after-save");while(pic.MoveNext())yield return pic.Current;}
        }
        static IEnumerator AuntiePickup(Case item)
        {
            Fixture f=Create(null,"customer_auntie_v11");yield return null;IEnumerator make=PackHeld(f,item);while(make.MoveNext())yield return make.Current;
            string mealId=f.Session.HeldMeal.id;int price=f.Session.HeldMeal.priceCents;
            IEnumerator place=Place(f,mealId);while(place.MoveNext())yield return place.Current;
            var target=new[]{-1};
            // Explicit appearance fixture assigns the genuine auntie rig to
            // the first natural matching order; it does not fabricate a fourth
            // demand for the single available meal or extend anybody's patience.
            IEnumerator find=FindCustomer(f,()=>f.Session.FindServingCounterOrder(mealId),target);
            while(find.MoveNext())yield return find.Current;
            int orderId=target[0];Transform customer=CustomerBody(f,orderId);Need(customer.name.Contains("阿姨"),"Specified actual asset is not auntie: "+customer.name);
            int cash=f.Session.CashCents,packages=f.Session.Count("P05"),events=0;Vector3 previous=customer.position;
            f.Session.SaleCompleted+=(o,r,c,s)=>{Need(o==orderId,"Auntie meal changed customer");events++;RecordContactVisibility(f,item,o,mealId);};
            Need(f.controller.RequestCounterPickup(mealId,orderId),"Auntie cannot collect: "+f.stage.LastPickupRejection);
            float until=f.Session.Elapsed+40;
            while(FindMeal(f,mealId)!=null)
            {
                Need(f.Session.Elapsed<until,"Auntie did not complete real pickup");
                Need(f.controller.PickupOrderForMeal(mealId)>=0,"Auntie pickup cancelled: "+customer.GetComponent<CustomerPickupReach>()?.ReachDiagnostic+" at "+customer.position.ToString("F4"));
                Tick(f);yield return new WaitForEndOfFrame();
                float distance=Vector3.Distance(previous,customer.position);item.customerTravel+=distance;item.maxCustomerStep=Mathf.Max(item.maxCustomerStep,distance);previous=customer.position;
                Need(distance<=.12f,"Auntie root jumped");
            }
            var reach=customer.GetComponent<CustomerPickupReach>();Need(reach!=null&&reach.LastFootError<=.025f,"Auntie feet lost real support");
            item.pickupContactError=ContactMetric(f,"LastCustomerPickupContactError");Need(item.pickupContactError<=.025f,"Auntie actual hand missed meal");
            Need(events==1&&f.Session.CashCents==cash+price&&f.Session.Count("P05")==packages,"Auntie pickup ledger mismatch");
            Need(item.contactVisibilityFailure=="",item.contactVisibilityFailure);
            item.sales=events;item.detail="Editor appearance fixture assigns genuine auntie asset to first normal natural matching order; measured .78m auntie collision; body walks, feet remain planted during receive, actual palm contact and one payment. "+reach.ReachDiagnostic;
            if(withUI){IEnumerator pic=Capture(f,"auntie-collected");while(pic.MoveNext())yield return pic.Current;}
        }
        static IEnumerator Capture(Fixture f,string name,int guideOrder=-1)
        {
            Need(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Graphical Editor required");
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--service20-width");int width=at>=0?int.Parse(args[at+1]):1280,height=width==1920?1080:720;
            int[] widths=Array.IndexOf(args,"--service20-both-sizes")>=0?new[]{1920,1280}:new[]{width};
            foreach(int captureWidth in widths)
            {
                IEnumerator capture=CaptureSize(f,name,guideOrder,captureWidth,captureWidth==1920?1080:720);
                while(capture.MoveNext())yield return capture.Current;
            }
        }
        static IEnumerator CaptureSize(Fixture f,string name,int guideOrder,int width,int height)
        {
            BusinessWorldHUD world=f.root.GetComponentInChildren<BusinessWorldHUD>(true);Need(world!=null,"Real order guide HUD missing");
            world.ShowOrderGuide(-1); // Close deterministically before each size; ShowOrderGuide is a toggle.
            MethodInfo resize=typeof(Map19VisualPreview).GetMethod("Resize",BindingFlags.Static|BindingFlags.NonPublic);Need(resize!=null,"GameView resize helper missing");
            IEnumerator sizing=(IEnumerator)resize.Invoke(null,new object[]{width,height});while(sizing.MoveNext())yield return sizing.Current;
            for(int i=0;i<4;i++){f.stage.Present(f.Session,0,false);f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();}
            string dir=Path.GetDirectoryName(output);Directory.CreateDirectory(dir);string path=Path.Combine(dir,name+"-"+width+"x"+height+".png");Need(!File.Exists(path),"Preserve screenshot");
            ScreenCapture.CaptureScreenshot(path);double until=EditorApplication.timeSinceStartup+15;while(!File.Exists(path)||new FileInfo(path).Length==0){Need(EditorApplication.timeSinceStartup<until,"Screenshot missing");yield return null;}
            report.screenshots.Add(new Frame{name=name,path=path,width=Screen.width,height=Screen.height,heldMeal=f.Session.HeldMeal?.id??"",tableMeals=f.Session.ReadyMeals.Count});
            Need(Screen.width==width&&Screen.height==height,"Capture dimensions wrong");
            // The guide is shown separately, retaining the prior held/table frame.
            if(guideOrder>=0)
            {
                world.ShowOrderGuide(-1);world.ShowOrderGuide(guideOrder);
                for(int i=0;i<3;i++){f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();}
                path=Path.Combine(dir,name+"-order-guide-"+width+"x"+height+".png");Need(!File.Exists(path),"Preserve guide screenshot");ScreenCapture.CaptureScreenshot(path);
                until=EditorApplication.timeSinceStartup+15;while(!File.Exists(path)||new FileInfo(path).Length==0){Need(EditorApplication.timeSinceStartup<until,"Guide screenshot missing");yield return null;}
                report.screenshots.Add(new Frame{name=name+"-order-guide",path=path,width=Screen.width,height=Screen.height,heldMeal=f.Session.HeldMeal?.id??"",tableMeals=f.Session.ReadyMeals.Count});
                Need(Screen.width==width&&Screen.height==height,"Guide capture dimensions wrong");
            }
            world.ShowOrderGuide(-1);
        }
        static void DisposeFixture(){if(live==null)return;var f=live;live=null;f.stage?.Release();if(f.root)UnityEngine.Object.DestroyImmediate(f.root);}
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Log(string condition,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(condition+"\n"+trace);}
        static void Finish(string failure)
        {DisposeFixture();Application.logMessageReceived-=Log;report.failure=failure;foreach(var item in report.cases)if(item.status=="passed")report.passed++;else report.failed++;report.engineErrors=errors.ToArray();report.success=failure==""&&report.failed==0&&report.cases.Count>0&&report.passed==report.cases.Count&&errors.Count==0;Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);}
    }
}
