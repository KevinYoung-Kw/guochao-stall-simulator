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
    [InitializeOnLoad] public static class Naming24SceneAcceptance
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
        const float Step=1f/60;const string Pending="Guochao.Naming24.Pending",Output="Guochao.Naming24.Output";
        static Fixture live;static Report report;static string output;static double began;static Feedback3CoroutineHost host;static readonly List<string> errors=new List<string>();
        static Naming24SceneAcceptance(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--naming24-scene-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("--naming24-scene-output <new report.json> required");
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
            IEnumerator resize=Resize(1280,720);while(resize.MoveNext())yield return resize.Current;
            string[] names={"griddle-egg-ui-sale","stir-egg-ui-sale","long-combination-emergency-720p"};
            Func<Case,IEnumerator>[] tests={GriddleEgg,StirEgg,LongCombination};
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--naming24-scene-case");string filter=at>=0&&at+1<args.Length?args[at+1]:"all";
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
        static Fixture Create(bool rush=false,bool mixed=false)
        {
            Need(live==null&&GameFlow.Instance==null,"Isolated fixture required");
            var f=new Fixture{root=new GameObject("Service21_ActualStage")};live=f;
            // Disabled before Start: establishes the real pause authority without
            // creating another world, UI, save I/O, or hardware-input loop.
            f.flow=f.root.AddComponent<GameFlow>();f.flow.enabled=false;f.flow.Run.SetBusinessPhase(GamePhase.Business);
            var setup=new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=15000,seed=12017,dayDuration=300,gasPoints=45,electricityPoints=45,
                customerIntervalSeconds=rush?4:16,patienceMinSeconds=35,patienceMaxSeconds=54,queueCapacity=4,
                equipment=new HashSet<string>{"B04","V02","V05","V09","U02","U04","U12"},inventory=new Dictionary<string,int>{{"egg",12},{"seasoning",30},{"P05",5},{"P04",5}}};
            if(mixed){setup.inventory["rice"]=1;setup.inventory["meat"]=1;setup.inventory["skewer"]=1;setup.inventory["prepared"]=1;}
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();Need(session.Open(),"Open free kitchen: "+session.Message);
            f.controller=f.root.AddComponent<BusinessController>();f.controller.enabled=false;f.stage=f.root.AddComponent<StreetStallStage>();f.stage.Initialize(session.Setup,null);f.stage.Configure(session);
            typeof(BusinessController).GetProperty("Session").SetValue(f.controller,session);typeof(BusinessController).GetProperty("Stage").SetValue(f.controller,f.stage);
            var input=f.root.AddComponent<StallInteractionInput>();input.Initialize(f.controller);input.enabled=false;typeof(BusinessController).GetProperty("InputHandler").SetValue(f.controller,input);
            f.vendor=(Transform)typeof(StreetStallStage).GetField("player",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f.stage);Need(f.vendor!=null,"Actual vendor missing");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--naming24-scene-ui")>=0)
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
        static IEnumerator Cook(Fixture f,int portions,string method=FreeKitchenRules.MethodStirFry)
        {
            Need(f.Session.Lanes[0].step==KitchenStep.Idle,"Cook requires empty pot");int stock=f.Session.Count("egg"),gas=f.Session.Gas;
            for(int n=1;n<=portions;n++)
            {
                Need(f.controller.UseIngredientAtLane(0,"egg"),"Real ingredient command rejected: "+f.controller.InteractionHint);float limit=f.Session.Elapsed+20;
                while(InputUnits(f)<n){Need(f.Session.Elapsed<limit,"Ingredient never arrived");Tick(f);yield return new WaitForEndOfFrame();}
            }
            Need(f.Session.Count("egg")==stock-portions,"Ingredient count mismatch");Need(f.controller.SelectedOrderId==-1,"Preparation selected a customer");
            Need(f.controller.SetFreeCookingMethod(0,method),"Actual cookware rejects stir_fry");Need(f.controller.UseCookware(0),"Start cooking command rejected");
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
        static IEnumerator GriddleEgg(Case item)
        {var c=SingleEgg(item,FreeKitchenRules.MethodGriddle,"煎鸡蛋","蛋菜煎饼");while(c.MoveNext())yield return c.Current;}
        static IEnumerator StirEgg(Case item)
        {var c=SingleEgg(item,FreeKitchenRules.MethodStirFry,"炒鸡蛋","家常青菜滑蛋");while(c.MoveNext())yield return c.Current;}
        static IEnumerator SingleEgg(Case item,string method,string expected,string storedName)
        {
            var f=Create();yield return null;IEnumerator step=Cook(f,1,method);while(step.MoveNext())yield return step.Current;
            var product=f.Session.ProductAtLane(0);Need(product!=null&&product.name==storedName&&product.DisplayName==expected&&f.Session.LaneDishName(0)==expected,"Persistent identity or displayed product name wrong");
            f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();
            Need(Array.Exists(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text.Contains(expected)),"Actual HUD does not display "+expected);
            step=Capture(f,method+"-pot");while(step.MoveNext())yield return step.Current;
            int cash=f.Session.CashCents;Need(f.controller.PackMeal(0),"Real pack command rejected");float limit=f.Session.Elapsed+25;
            while(f.Session.HeldMeal==null){Need(f.Session.Elapsed<limit,"Meal was not packed");Tick(f);yield return new WaitForEndOfFrame();}
            f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();
            Need(Array.Exists(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text=="手上 · "+expected),"Held meal card does not show actual display name");
            step=Capture(f,method+"-held");while(step.MoveNext())yield return step.Current;
            int price=f.Session.HeldMeal.priceCents;var target=new[]{-1};step=FindHeldCustomer(f,target);while(step.MoveNext())yield return step.Current;
            Need(f.Session.OrderDishName(target[0])==expected,"Natural customer name wrong");Need(f.controller.DeliverHeldMealTo(target[0]),"Delivery rejected");limit=f.Session.Elapsed+5;
            while(f.Session.HeldMeal!=null){Need(f.Session.Elapsed<limit,"Delivery timed out");Tick(f);yield return new WaitForEndOfFrame();}
            var order=f.Session.FindOrder(target[0]);Need(order.served&&order.productName==storedName&&f.Session.OrderDishName(order.id)==expected&&f.Session.CashCents==cash+price,"Actual sale identity/display/cash wrong");
            step=Capture(f,method+"-paid");while(step.MoveNext())yield return step.Current;
            bool matched=method==FreeKitchenRules.MethodStirFry;f.Session.Close();f.controller.HUD.ShowSummary();Canvas.ForceUpdateCanvases();
            var receipt=CommerceReceiptData.Sales(f.Session);Need(receipt.lines.Length==1&&receipt.lines[0].name==expected&&receipt.lines[0].recipeIndex==(matched?8:-1)&&receipt.totalCents==price,"Actual receipt data wrong");
            Need(Array.Exists(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text==expected),"Actual receipt name missing");
            var sales=(RectTransform)typeof(BusinessHUD).GetField("summarySales",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f.controller.HUD);
            bool portrait=Array.Exists(sales.GetComponentsInChildren<UnityEngine.UI.Image>(false),x=>x.gameObject.name=="Actual item portrait"&&x.sprite!=null);
            Need(portrait==matched,"Actual receipt incorrect matched recipe image");
            step=Capture(f,method+"-summary");while(step.MoveNext())yield return step.Current;
            var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};var run=new RunState(.25f,profile);var campaign=CampaignState.New(profile,run);
            Need(campaign.CommitDay(run,f.Session,f.Session.SessionId),"Actual sale day failed to commit");
            Need(campaign.lastDay.sales.Count==1&&campaign.lastDay.sales[0].name==expected&&campaign.lastDay.sales[0].totalCents==price,"Night ledger lost truthful name or sale");
            typeof(GameFlow).GetProperty("Campaign").SetValue(f.flow,campaign);NightHubUI.Open(f.flow);Canvas.ForceUpdateCanvases();
            var night=GameObject.Find("Night at home — campaign ledger");Need(night!=null&&NightHubUI.IsOpen,"Night ledger UI not open");
            Need(Array.Exists(night.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text==expected),"Night ledger actual name missing");
            bool nightIcon=Array.Exists(night.GetComponentsInChildren<StallUIIcon>(false),x=>x.gameObject.name=="Dish egg_greens");Need(nightIcon==matched,"Night ledger matched recipe icon wrong");
            step=Capture(f,method+"-night-ledger");while(step.MoveNext())yield return step.Current;NightHubUI.Close();
            item.detail="Actual actor cooks egg, pot and held card use actual name; natural customer pays once; both receipt views preserve the correct image identity.";
        }
        // Long-combination layout is seeded through public session transactions;
        // actor navigation is separately covered in the two single-egg cases.
        static IEnumerator LongCombination(Case item)
        {
            var f=Create(mixed:true);yield return null;
            foreach(string id in new[]{"rice","egg","meat","skewer","prepared"})Need(f.Session.AddFreeIngredient(0,id),"could not add "+id);
            Need(f.Session.SetFreeMethod(0,FreeKitchenRules.MethodBoil)&&f.Session.StartFreeCooking(0),"could not start long mixture");
            for(int i=0;i<120&&f.Session.Lanes[0].step==KitchenStep.Heating;i++)f.Session.Tick(.1f);
            Need(f.Session.Lanes[0].step==KitchenStep.Utensil&&f.Session.Action(0),"long mixture heat incomplete");
            if(f.Session.Lanes[0].step==KitchenStep.Season)Need(f.Session.Action(0),"long mixture season failed");
            var product=f.Session.ProductAtLane(0);string expected="煮米面、鸡蛋、鲜肉、串料、料理包";
            Need(product!=null&&product.DisplayName==expected,"long ingredient name inaccurate");
            typeof(BusinessController).GetProperty("SelectedLaneIndex").SetValue(f.controller,0);
            f.controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();
            var label=Array.Find(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text.Contains(expected));
            Need(Screen.width==1280&&Screen.height==720,"expected 720p GameView");
            Need(label!=null&&label.text.Contains("应急薄袋")&&label.text.Contains("选适配包材"),"long name/next step/emergency warning missing");
            Need(label.preferredHeight<=label.rectTransform.rect.height+1f,"long product name or state is clipped");
            for(int i=0;i<5;i++)Need(Array.Exists(f.controller.HUD.GetComponentsInChildren<UnityEngine.UI.Text>(false),x=>x.text==new[]{"米面","鸡蛋","鲜肉","串料","料理包"}[i]),"missing input icon label "+i);
            IEnumerator capture=Capture(f,"long-mixture-emergency-720p");while(capture.MoveNext())yield return capture.Current;
            item.detail="720p actual HUD shows all five real inputs, full product name, next step and emergency warning without text overflow.";
        }
        static Type EditorType(string name){var t=Type.GetType(name+", UnityEditor");if(t!=null)return t;foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){t=a.GetType(name,false);if(t!=null)return t;}return null;}
        static IEnumerator Resize(int width,int height)
        {
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            Type viewType=EditorType("UnityEditor.GameView"),sizesType=EditorType("UnityEditor.GameViewSizes"),sizeType=EditorType("UnityEditor.GameViewSize"),sizeKind=EditorType("UnityEditor.GameViewSizeType");
            Need(viewType!=null&&sizesType!=null&&sizeType!=null&&sizeKind!=null,"GameView API unavailable");var view=EditorWindow.GetWindow(viewType);view.Show();view.Focus();
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);object sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            object groupType=sizesType.GetProperty("currentGroupType",flags).GetValue(sizes),group=sizesType.GetMethod("GetGroup",flags).Invoke(sizes,new[]{groupType});
            Type groupClass=group.GetType();int builtin=(int)groupClass.GetMethod("GetBuiltinCount",flags).Invoke(group,null),custom=(int)groupClass.GetMethod("GetCustomCount",flags).Invoke(group,null),selected=-1;
            for(int i=0;i<builtin+custom;i++){object candidate=groupClass.GetMethod("GetGameViewSize",flags).Invoke(group,new object[]{i});int w=(int)sizeType.GetProperty("width",flags).GetValue(candidate),h=(int)sizeType.GetProperty("height",flags).GetValue(candidate);if(w==width&&h==height&&sizeType.GetProperty("sizeType",flags).GetValue(candidate).ToString()=="FixedResolution"){selected=i;break;}}
            if(selected<0){object size=Activator.CreateInstance(sizeType,flags,null,new object[]{Enum.Parse(sizeKind,"FixedResolution"),width,height,"Naming24 720p"},null);groupClass.GetMethod("AddCustomSize",flags).Invoke(group,new[]{size});selected=builtin+custom;}
            viewType.GetProperty("selectedSizeIndex",flags).SetValue(view,selected);var low=viewType.GetProperty("lowResolutionForAspectRatios",flags);if(low!=null&&low.CanWrite)low.SetValue(view,false);
            double deadline=Time.realtimeSinceStartupAsDouble+10;while(Screen.width!=width||Screen.height!=height){Need(Time.realtimeSinceStartupAsDouble<deadline,"Resolution mismatch");view.Repaint();yield return null;}yield return null;yield return null;
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
        static void Dispose(){NightHubUI.Close();if(live==null)return;var f=live;live=null;f.stage?.Release();if(f.root)UnityEngine.Object.DestroyImmediate(f.root);}
        static void Finish(string failure)
        {Dispose();Application.logMessageReceived-=Log;report.failure=failure;foreach(var c in report.cases)if(c.status=="passed")report.passed++;else report.failed++;report.engineErrors=errors.ToArray();report.success=failure==""&&report.cases.Count>0&&report.failed==0&&errors.Count==0;Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);}
    }
}
