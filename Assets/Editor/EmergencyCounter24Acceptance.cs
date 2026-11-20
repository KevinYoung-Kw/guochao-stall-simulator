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
    // Controller-entry regression for the simplified v21 fulfilment contract.
    // Preparation still uses the actual stage. Serving is NOT an IK accuracy test.
    [InitializeOnLoad] public static class EmergencyCounter24Acceptance
    {
        [Serializable] public sealed class Delivery
        {public string mealId,customer,route;public int orderId,priceCents;public float seconds,vendorMetres;}
        [Serializable] public sealed class Case
        {public string id,status="running",detail="",failure="";public float elapsed;public int saleEvents,maximumWaiting;public List<Delivery> deliveries=new List<Delivery>();public BusinessAuditState final;public BusinessWorkAudit[] work;}
        [Serializable] public sealed class Report
        {public string schema="guochao.service21.controller-scene.v1",failure="",scope="Real PlayMode Stage + Controller commands, real frames and finite initial stock. Counter rush fixture uses 4-second customer interval with unchanged 35..54-second patience to exercise full queue; no injected orders, cooked food, revenue, or disabled collisions. VSync is disabled and every rendered frame advances a fixed 1/60 simulation step; bounded service seconds are simulation time, not human wall-clock input speed. Scripted inputs, not hardware UI clicks or whole-campaign approval. Direct delivery <=3 seconds and vendor travel <=0.5m; no hand-contact accuracy gate.";public bool success;public int passed,failed;public List<Case> cases=new List<Case>();public List<string> screenshots=new List<string>();public string[] engineErrors;}
        sealed class Fixture
        {
            public GameObject root;public GameFlow flow;public BusinessController controller;public StreetStallStage stage;public Transform vendor;
            public BusinessSession Session=>controller.Session;public readonly List<string> failures=new List<string>();public readonly Dictionary<int,int> sales=new Dictionary<int,int>();
        }
        const float Step=1f/60;const string Pending="Guochao.EmergencyCounter24.Pending",Output="Guochao.EmergencyCounter24.Output";
        static Fixture live;static Report report;static string output;static double began;static Feedback3CoroutineHost host;static readonly List<string> errors=new List<string>();
        static EmergencyCounter24Acceptance(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--counter24-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("--counter24-output <new report.json> required");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Preserve existing evidence");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;Application.runInBackground=true;began=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;
                host=new GameObject("Service21_RealFrames").AddComponent<Feedback3CoroutineHost>();host.Launch(Cases(),()=>Finish(""),e=>Finish(e.ToString()));
            }
            if(host!=null&&host.IsRunning&&EditorApplication.timeSinceStartup-began>600){host.Abort();Finish("Service21 exceeded 600 seconds");}
        }
        static IEnumerator Cases()
        {
            string[] names={"empty-disabled","emergency-counter","normal-counter","held-counter"};
            Func<Case,IEnumerator>[] tests={CounterEmpty,CounterEmergency,CounterNormal,CounterHeld};
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--counter24-case");string filter=at>=0&&at+1<args.Length?args[at+1]:"all";
            for(int i=0;i<tests.Length;i++)
            {
                if(filter!="all"&&names[i].IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0)continue;
                var item=new Case{id=names[i]};report.cases.Add(item);var test=tests[i](item);bool done=false;
                while(!done)
                {
                    object yielded=null;
                    try{if(test.MoveNext())yielded=test.Current;else{item.status="passed";done=true;}}
                    catch(Exception e){item.status="failed";item.failure=e.ToString();done=true;}
                    if(!done)yield return yielded;
                }
                if(live!=null){item.elapsed=live.Session.Elapsed;item.final=live.Session.CaptureAudit();item.work=live.controller.CaptureWorkHistory();foreach(var pair in live.sales)item.saleEvents+=pair.Value;}
                (test as IDisposable)?.Dispose();Dispose();yield return null;
            }
        }
        static Fixture Create(bool rush=false,int packages=0)
        {
            Need(live==null&&GameFlow.Instance==null,"Isolated fixture required");
            var f=new Fixture{root=new GameObject("Service21_ActualStage")};live=f;
            // Disabled before Start: establishes the real pause authority without
            // creating another world, UI, save I/O, or hardware-input loop.
            f.flow=f.root.AddComponent<GameFlow>();f.flow.enabled=false;f.flow.Run.SetBusinessPhase(GamePhase.Business);
            var setup=new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=15000,seed=12017,dayDuration=300,gasPoints=45,electricityPoints=45,
                customerIntervalSeconds=rush?4:16,patienceMinSeconds=35,patienceMaxSeconds=54,queueCapacity=4,
                equipment=new HashSet<string>{"B03",rush?"V03":"V01","U01"},inventory=new Dictionary<string,int>{{"egg",12},{"seasoning",30},{"P05",packages}}};
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();Need(session.Open(),"Open free kitchen: "+session.Message);
            f.controller=f.root.AddComponent<BusinessController>();f.controller.enabled=false;f.stage=f.root.AddComponent<StreetStallStage>();f.stage.Initialize(session.Setup,null);f.stage.Configure(session);
            typeof(BusinessController).GetProperty("Session").SetValue(f.controller,session);typeof(BusinessController).GetProperty("Stage").SetValue(f.controller,f.stage);
            var input=f.root.AddComponent<StallInteractionInput>();input.Initialize(f.controller);input.enabled=false;typeof(BusinessController).GetProperty("InputHandler").SetValue(f.controller,input);
            f.vendor=(Transform)typeof(StreetStallStage).GetField("player",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f.stage);Need(f.vendor!=null,"Actual vendor missing");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--counter24-ui")>=0)
            {
                if(UnityEngine.EventSystems.EventSystem.current==null){var events=new GameObject("Service21_UI",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.EventSystems.StandaloneInputModule));events.transform.SetParent(f.root.transform);}
                var hud=f.root.AddComponent<BusinessHUD>();typeof(BusinessController).GetProperty("HUD").SetValue(f.controller,hud);hud.Initialize(f.controller,session);hud.ShowBusiness();
            }
            f.stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);f.stage.WorkFailed+=message=>f.failures.Add(message);
            session.SaleCompleted+=(id,r,c,s)=>{if(!f.sales.ContainsKey(id))f.sales[id]=0;f.sales[id]++;};
            Need(f.root.GetComponent<VendorWorkActor>()?.IsBound==true,"Actual preparation actor missing");
            int solids=0;foreach(Collider col in f.root.GetComponentsInChildren<Collider>())if(col.enabled&&!col.isTrigger&&col.gameObject.layer==StreetStallStage.SolidLayer)solids++;
            Need(solids>0,"Production collisions missing");return f;
        }
        static void Tick(Fixture f)
        {
            Physics.SyncTransforms();f.controller.AdvanceSimulation(Step);
            Need(f.failures.Count==0,"Stage rejected work: "+string.Join(";",f.failures));
            var a=f.Session.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"Ledger invariant");
        }
        static int InputUnits(Fixture f)
        {var inputs=f.Session.Lanes[0].freeBatch?.inputs;if(inputs!=null)foreach(var x in inputs)if(x.itemId=="egg")return x.units;return 0;}
        static CounterMeal Meal(Fixture f,string id){foreach(var x in f.Session.ReadyMeals)if(x.id==id)return x;return null;}
        static bool Waiting(StallOrder o)=>o!=null&&o.arrived&&!o.served&&!o.departed;
        static Transform Customer(Fixture f,int id)
        {foreach(var t in f.root.GetComponentsInChildren<StallInteractionTarget>())if(t.kind==StallInteractionKind.Order&&t.orderId==id)return t.transform.parent;throw new InvalidOperationException("Customer actor missing");}
        static IEnumerator Settle(Fixture f)
        {float limit=f.Session.Elapsed+20;while(f.stage.WorkActorBusy||f.controller.IsPackingMeal){Need(f.Session.Elapsed<limit,"Preparation actor stuck");Tick(f);yield return new WaitForEndOfFrame();}}
        static IEnumerator Cook(Fixture f,int portions)
        {
            Need(f.Session.Lanes[0].step==KitchenStep.Idle,"Cook requires empty pot");int stock=f.Session.Count("egg"),gas=f.Session.Gas;
            for(int n=1;n<=portions;n++)
            {
                Need(f.controller.UseIngredientAtLane(0,"egg"),"Real ingredient command rejected: "+f.controller.InteractionHint);float limit=f.Session.Elapsed+20;
                while(InputUnits(f)<n){Need(f.Session.Elapsed<limit,"Ingredient never arrived");Tick(f);yield return new WaitForEndOfFrame();}
            }
            Need(f.Session.Count("egg")==stock-portions,"Ingredient count mismatch");Need(f.controller.SelectedOrderId==-1,"Preparation selected a customer");
            Need(f.controller.SetFreeCookingMethod(0,FreeKitchenRules.MethodStirFry),"Actual cookware rejects stir_fry");Need(f.controller.UseCookware(0),"Start cooking command rejected");
            float deadline=f.Session.Elapsed+90;
            while(f.Session.Lanes[0].step!=KitchenStep.Pack&&f.Session.Lanes[0].step!=KitchenStep.ReadyStock)
            {
                Need(f.Session.Elapsed<deadline,"Free cooking did not finish");KitchenStep step=f.Session.Lanes[0].step;
                if((step==KitchenStep.Heating||step==KitchenStep.Utensil)&&!f.controller.HasPendingIntent(StallInteractionKind.Cookware,0))f.controller.UseCookware(0);
                if(step==KitchenStep.Season&&!f.controller.HasPendingIntent(StallInteractionKind.Seasoning,0))f.controller.UseSeasoning(0);
                Tick(f);yield return new WaitForEndOfFrame();
            }
            Need(f.Session.ProductAtLane(0)?.remaining==portions&&f.Session.Gas==gas-1,"Actual portions/energy mismatch");
            IEnumerator settle=Settle(f);while(settle.MoveNext())yield return settle.Current;
        }
        static IEnumerator Pack(Fixture f)
        {
            int packages=f.Session.Count("P05"),cash=f.Session.CashCents;
            var button=Array.Find(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Button>(false),b=>b.GetComponentInChildren<UnityEngine.UI.Text>()?.text=="应急袋装餐");
            Need(button!=null&&button.interactable,"Emergency pack button absent or disabled");
            var rt=(RectTransform)button.transform;var corners=new Vector3[4];rt.GetWorldCorners(corners);
            var pos=RectTransformUtility.WorldToScreenPoint(null,(corners[0]+corners[2])*.5f);
            var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=pos};
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(data,hits);
            Need(hits.Exists(x=>x.gameObject==button.gameObject||x.gameObject.transform.IsChildOf(button.transform)),"Emergency pack button is occluded");
            button.onClick.Invoke();Need(f.controller.IsPackingMeal||f.Session.HeldMeal!=null,"Emergency button did not queue pack");float limit=f.Session.Elapsed+25;
            while(f.Session.HeldMeal==null){Need(f.Session.Elapsed<limit,"Real pack did not finish");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.Count("P05")==packages&&f.Session.CashCents==cash&&f.Session.HeldMeal.packageId==BusinessSession.EmergencyBagId,"Reserve pack ledger mismatch");IEnumerator settle=Settle(f);while(settle.MoveNext())yield return settle.Current;
        }
        static IEnumerator Place(Fixture f)
        {
            string id=f.Session.HeldMeal.id;int cash=f.Session.CashCents,packages=f.Session.Count("P05");Need(f.controller.PlaceHeldMeal(),"Place command rejected");float limit=f.Session.Elapsed+25;
            while(f.Session.HeldMeal!=null){Need(f.Session.Elapsed<limit,"Place did not finish");Tick(f);yield return new WaitForEndOfFrame();}
            Need(Meal(f,id)!=null&&f.Session.CashCents==cash&&f.Session.Count("P05")==packages,"Place altered identity/ledger");IEnumerator settle=Settle(f);while(settle.MoveNext())yield return settle.Current;
        }
        static IEnumerator FindHeldCustomer(Fixture f,int[] target)
        {float limit=f.Session.Elapsed+90;while((target[0]=f.Session.FindServingHeldOrder())<0){Need(f.Session.Elapsed<limit,"No natural held customer");Tick(f);yield return new WaitForEndOfFrame();}}
        static UnityEngine.UI.Button ServingButton(Fixture f,string expected,bool interactable)
        {
            f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();
            var buttons=f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            var button=Array.Find(buttons,b=>b.transform.parent!=null&&b.transform.parent.name=="Object readout Serving");
            Need(button!=null,"Serving HUD button missing");
            var label=button.GetComponentInChildren<UnityEngine.UI.Text>();
            Need(label!=null&&label.text==expected,"Serving label expected "+expected+", got "+(label==null?"<missing>":label.text));
            Need(button.interactable==interactable,"Serving enabled state incorrect for "+expected);
            if(interactable)
            {
                var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
                var pos=RectTransformUtility.WorldToScreenPoint(null,(corners[0]+corners[2])*.5f);
                var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=pos};
                var hits=new List<UnityEngine.EventSystems.RaycastResult>();UnityEngine.EventSystems.EventSystem.current.RaycastAll(data,hits);
                Need(hits.Exists(x=>x.gameObject==button.gameObject||x.gameObject.transform.IsChildOf(button.transform)),"Serving HUD button is occluded");
            }
            return button;
        }
        static IEnumerator CounterEmpty(Case item)
        {
            var f=Create();yield return null;ServingButton(f,"待出餐",false);
            IEnumerator capture=Capture(f,"counter-empty");while(capture.MoveNext())yield return capture.Current;
            item.detail="No cooked or held food: the real serving HUD offers no packing action.";
        }
        static IEnumerator CounterEmergency(Case item)
        {
            var f=Create();yield return null;IEnumerator step=Cook(f,1);while(step.MoveNext())yield return step.Current;
            var button=ServingButton(f,"装餐放下",true);
            var tag=Array.Find(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),t=>t.text=="可以装餐");
            Need(tag!=null,"Cookware must say 可以装餐");
            step=Capture(f,"counter-emergency-ready");while(step.MoveNext())yield return step.Current;
            int cash=f.Session.CashCents;button.onClick.Invoke();float limit=f.Session.Elapsed+45;
            while(f.Session.ReadyMeals.Count==0){Need(f.Session.Elapsed<limit,"Emergency counter never received real meal");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.HeldMeal==null&&f.Session.ReadyMeals[0].packageId==BusinessSession.EmergencyBagId,"Emergency meal not actually placed");
            Need(f.Session.Count("P05")==0&&!f.Session.Inventory.ContainsKey(BusinessSession.EmergencyBagId)&&f.Session.CashCents==cash,"Emergency counter duplicated stock or money");
            step=Capture(f,"counter-emergency-placed");while(step.MoveNext())yield return step.Current;item.detail="Actual enabled serving button packages with the emergency bag and puts the same meal down without free stock or payment.";
        }
        static IEnumerator CounterNormal(Case item)
        {
            var f=Create(packages:1);yield return null;IEnumerator step=Cook(f,1);while(step.MoveNext())yield return step.Current;
            var button=ServingButton(f,"装餐放下",true);int cash=f.Session.CashCents;button.onClick.Invoke();float limit=f.Session.Elapsed+45;
            while(f.Session.ReadyMeals.Count==0){Need(f.Session.Elapsed<limit,"Normal pack counter never received meal");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.HeldMeal==null&&f.Session.ReadyMeals[0].packageId=="P05"&&f.Session.Count("P05")==0,"Physical package must be consumed once");
            Need(f.Session.CashCents==cash&&f.Session.EmergencyPackagingScore==0,"Normal pack caused cash/score mutation");
            step=Capture(f,"counter-normal-placed");while(step.MoveNext())yield return step.Current;item.detail="Actual serving button consumes existing compatible P05 once and places the meal.";
        }
        static IEnumerator CounterHeld(Case item)
        {
            var f=Create();yield return null;IEnumerator step=Cook(f,1);while(step.MoveNext())yield return step.Current;step=Pack(f);while(step.MoveNext())yield return step.Current;
            Need(f.Session.HeldMeal!=null,"Expected held meal");string id=f.Session.HeldMeal.id;int cash=f.Session.CashCents;
            var button=ServingButton(f,"放下成品",true);step=Capture(f,"counter-held-ready");while(step.MoveNext())yield return step.Current;
            button.onClick.Invoke();float limit=f.Session.Elapsed+25;
            while(f.Session.HeldMeal!=null){Need(f.Session.Elapsed<limit,"Held meal not placed");Tick(f);yield return new WaitForEndOfFrame();}
            Need(Meal(f,id)!=null&&f.Session.CashCents==cash&&f.Session.Count("P05")==0,"Held placement changed meal or ledger");
            step=Capture(f,"counter-held-placed");while(step.MoveNext())yield return step.Current;item.detail="With a meal in hand, the same real HUD button places the existing meal rather than packaging again.";
        }
        static IEnumerator Direct(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator step=Cook(f,1);while(step.MoveNext())yield return step.Current;step=Capture(f,"emergency-prepack");while(step.MoveNext())yield return step.Current;step=Pack(f);while(step.MoveNext())yield return step.Current;step=Capture(f,"emergency-held");while(step.MoveNext())yield return step.Current;
            var target=new[]{-1};step=FindHeldCustomer(f,target);while(step.MoveNext())yield return step.Current;
            string id=f.Session.HeldMeal.id;int price=f.Session.HeldMeal.priceCents,cash=f.Session.CashCents,packages=f.Session.Count("P05"),table=f.Session.ReadyMeals.Count;
            Vector3 previous=f.vendor.position;float distance=0,start=f.Session.Elapsed;
            Need(f.controller.DeliverHeldMealTo(target[0]),"Direct entry rejected");Need(!f.controller.DeliverHeldMealTo(target[0]),"Double direct command accepted");
            while(f.Session.HeldMeal!=null)
            {
                Need(f.Session.Elapsed-start<=3f,"Direct handoff exceeded 3 seconds");Tick(f);yield return new WaitForEndOfFrame();
                distance+=Vector3.Distance(previous,f.vendor.position);previous=f.vendor.position;Need(distance<=.5f,"Vendor detoured to deliver held meal");
            }
            Need(f.Session.FindOrder(target[0]).served&&f.sales[target[0]]==1,"Direct fixed customer not settled once");
            Need(f.Session.CashCents==cash+price&&f.Session.Count("P05")==packages&&f.Session.ReadyMeals.Count==table,"Direct duplicated or inserted table food");
            item.deliveries.Add(new Delivery{mealId=id,orderId=target[0],route="held-direct",customer=Customer(f,target[0]).name,priceCents=price,seconds=f.Session.Elapsed-start,vendorMetres=distance});
            IEnumerator capture=Capture(f,"direct-paid");while(capture.MoveNext())yield return capture.Current;
            Need(!f.controller.DeliverHeldMealTo(target[0]),"Consumed held meal delivered again");for(int i=0;i<90;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.CashCents==cash+price&&f.sales[target[0]]==1,"Late callback repeated direct sale");Need(f.Session.EmergencyPackagesSold==1&&f.Session.EmergencyPackagingScore==-5&&!f.Session.Inventory.ContainsKey(BusinessSession.EmergencyBagId),"Emergency provenance or score wrong");
            f.Session.Close();f.controller.HUD.ShowSummary();Canvas.ForceUpdateCanvases();
            var summary=Array.Find(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),t=>t.text.Contains("应急薄袋 1 份")&&t.text.Contains("国潮 -5"));
            Need(summary!=null&&summary.preferredHeight<=summary.rectTransform.rect.height+1,"Emergency packaging score missing or clipped on settlement");
            capture=Capture(f,"emergency-day-summary");while(capture.MoveNext())yield return capture.Current;
            item.detail="Real actor contacts, no stock fallback and fixed-customer direct handoff; one payment, no free regular inventory, visible saved score -5.";
        }
        static IEnumerator CounterRush(Case item)
        {
            Fixture f=Create(true);yield return null;IEnumerator step=Cook(f,4);while(step.MoveNext())yield return step.Current;
            for(int n=0;n<4;n++){step=Pack(f);while(step.MoveNext())yield return step.Current;step=Place(f);while(step.MoveNext())yield return step.Current;}
            Need(f.Session.ReadyMeals.Count==4,"Four real counter meals missing");float until=f.Session.Elapsed+45;
            var customers=new List<int>();
            while(true)
            {
                customers.Clear();foreach(var o in f.Session.Orders)if(Waiting(o))customers.Add(o.id);item.maximumWaiting=Math.Max(item.maximumWaiting,customers.Count);
                if(customers.Count==4)break;Need(f.Session.Elapsed<until,"Rush fixture never reached full natural queue");Tick(f);yield return new WaitForEndOfFrame();
            }
            var meals=new List<CounterMeal>(f.Session.ReadyMeals);int cash=f.Session.CashCents,packages=f.Session.Count("P05"),expected=0,eggs=f.Session.Count("egg");float start=f.Session.Elapsed;Vector3 previous=f.vendor.position;
            for(int i=0;i<4;i++)
            {
                Need(f.Session.CanServeCounterMeal(meals[i].id,customers[i],out string reason),"Full queue meal mismatch: "+reason);
                Need(f.controller.SelectCounterMeal(meals[i].id),"Actual select-meal entry rejected");
                Need(f.controller.DeliverCounterMealTo(customers[i]),"Select meal then customer rejected: "+f.controller.InteractionHint);
                Need(!f.controller.RequestCounterPickup(meals[i].id,customers[i]),"Duplicate table request accepted");expected+=meals[i].priceCents;
                item.deliveries.Add(new Delivery{mealId=meals[i].id,orderId=customers[i],route="selected-counter",customer=Customer(f,customers[i]).name,priceCents=meals[i].priceCents});
            }
            Need(f.controller.UseIngredientAtLane(0,"egg"),"Cannot prepare while customers collect");
            until=f.Session.Elapsed+6;
            while(f.Session.ReadyMeals.Count>0)
            {Need(f.Session.Elapsed<until,"Four counter meals did not sell within bounded short service");Tick(f);yield return new WaitForEndOfFrame();}
            foreach(var d in item.deliveries){Need(f.Session.FindOrder(d.orderId).served&&f.sales[d.orderId]==1,"Full queue sale mismatch");d.seconds=f.Session.Elapsed-start;Need(!f.controller.RequestCounterPickup(d.mealId,d.orderId),"Used counter meal was sold twice");}
            Need(f.Session.CashCents==cash+expected&&f.Session.Count("P05")==packages,"Counter double package/payment");
            IEnumerator capture=Capture(f,"counter-four-paid");while(capture.MoveNext())yield return capture.Current;
            until=f.Session.Elapsed+15;while(f.Session.Count("egg")==eggs){Need(f.Session.Elapsed<until,"Parallel ingredient work disappeared");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.Count("egg")==eggs-1&&InputUnits(f)==1,"Parallel preparation duplicated ingredient");
            item.detail="Four distinct real packaged portions sold to full normal-patience mixed-body queue through selected-meal/customer API while next preparation remains active.";
        }
        static IEnumerator PauseCancel(Case item)
        {
            Fixture f=Create();yield return null;IEnumerator step=Cook(f,1);while(step.MoveNext())yield return step.Current;step=Pack(f);while(step.MoveNext())yield return step.Current;
            var target=new[]{-1};step=FindHeldCustomer(f,target);while(step.MoveNext())yield return step.Current;
            string id=f.Session.HeldMeal.id;int cash=f.Session.CashCents,packages=f.Session.Count("P05");
            Need(f.controller.DeliverHeldMealTo(target[0]),"Direct request rejected before pause");f.flow.SetPaused(true);float clock=f.Session.Elapsed;
            for(int i=0;i<90;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.Elapsed==clock&&f.Session.HeldMeal?.id==id&&f.Session.CashCents==cash&&f.sales.Count==0,"Paused handoff settled");
            Need(!f.controller.DeliverHeldMealTo(target[0]),"Pause permits another delivery command");f.flow.SetPaused(false);
            var save=f.controller.CaptureForSave();var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(save)));
            Need(restored.HeldMeal?.id==id&&restored.CashCents==cash,"Precommit save/cancel lost held meal");
            for(int i=0;i<60;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.HeldMeal?.id==id&&f.Session.CashCents==cash,"Cancelled direct callback later settled");
            step=Place(f);while(step.MoveNext())yield return step.Current;
            Need(f.controller.SelectCounterMeal(id)&&f.controller.DeliverCounterMealTo(target[0]),"Table request rejected after direct cancellation");
            Need(f.controller.CancelCounterPickup(id),"Precommit table cancellation rejected");
            for(int i=0;i<60;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(Meal(f,id)!=null&&f.Session.CashCents==cash&&f.Session.Count("P05")==packages,"Counter cancel lost meal/debited again");
            Need(f.controller.SelectCounterMeal(id)&&f.controller.DeliverCounterMealTo(target[0]),"Cancelled table meal cannot be retried");f.flow.SetPaused(true);clock=f.Session.Elapsed;
            for(int i=0;i<90;i++){Tick(f);yield return new WaitForEndOfFrame();}
            Need(Meal(f,id)!=null&&f.Session.CashCents==cash&&f.Session.Elapsed==clock,"Paused counter pickup settled");f.flow.SetPaused(false);
            float until=f.Session.Elapsed+3;while(Meal(f,id)!=null){Need(f.Session.Elapsed<until,"Resumed pickup never settled");Tick(f);yield return new WaitForEndOfFrame();}
            Need(f.Session.FindOrder(target[0]).served&&f.sales[target[0]]==1&&f.Session.Count("P05")==packages,"Retry did not settle exactly once");
            item.detail="Real pause authority stops both handoff paths; precommit checkpoint and explicit counter cancellation preserve exact meal; resume/retry sells once.";
        }
        static IEnumerator Capture(Fixture f,string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)yield break;
            for(int i=0;i<3;i++){f.stage.Present(f.Session,0,false);f.controller.HUD?.Refresh(f.controller.IsPaused);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();}
            string directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,name+"-"+Screen.width+"x"+Screen.height+".png");Need(!File.Exists(path),"Preserve prior screenshot");
            ScreenCapture.CaptureScreenshot(path);double deadline=EditorApplication.timeSinceStartup+10;
            while(!File.Exists(path)||new FileInfo(path).Length==0){Need(EditorApplication.timeSinceStartup<deadline,"Screenshot was not written");yield return null;}
            report.screenshots.Add(path);
        }
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text+"\n"+trace);}
        static void Dispose(){if(live==null)return;var f=live;live=null;f.stage?.Release();if(f.root)UnityEngine.Object.DestroyImmediate(f.root);}
        static void Finish(string failure)
        {Dispose();Application.logMessageReceived-=Log;report.failure=failure;foreach(var c in report.cases)if(c.status=="passed")report.passed++;else report.failed++;report.engineErrors=errors.ToArray();report.success=failure==""&&report.cases.Count>0&&report.failed==0&&errors.Count==0;Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);}
    }
}
