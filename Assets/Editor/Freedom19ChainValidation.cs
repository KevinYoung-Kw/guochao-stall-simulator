using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Codex reviewer integration QA. Actual career funds, purchases, measured
    // CartAI/factory packing and production save files; never injects stock or
    // rewards. Supplies local interaction intents directly to model APIs.
    // MarkCustomerArrived represents arrival ONLY, not keyboard navigation,
    // customer animation or proof of physical reach in the rendered scene.
    public static class Freedom19ChainValidation
    {
        [Serializable] public sealed class Check { public string name,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool success;
            public string scope="Ordinary career procurement -> measured packing -> empty-plan free cooking -> independent counter meals -> natural orders -> exact receipts -> checksummed files/day ledger. Model contact intents and arrival markers; no real keyboard/animation certification.";
            public string saveDirectory,productName,productId,mealId;
            public int initialCash,purchases,revenue,closingCash,naturalArrivalAttempts,arrivalMarkers;
            public Check[] checks;
            public string[] runtimeErrors;
        }
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<Check> checks=new List<Check>();
        static readonly List<string> errors=new List<string>();
        static GameObject fixture;static VehicleLoadout packing;static GameFlow mapper;static Report report;
        static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static void Passed(string name,string detail){checks.Add(new Check{name=name,detail=detail,passed=true});}
        static void ErrorLog(string message,string stack,LogType kind)
        {if(kind==LogType.Exception||kind==LogType.Error||kind==LogType.Assert)errors.Add(message+"\n"+stack);}

        [MenuItem("Guochao/Validate freedom19 procurement to sales chain")]
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--freedom19-chain-report");
            string path=Path.GetFullPath(at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.dataPath,"../../art-v19/freedom19-chain-report.json"));
            string previous=CampaignSaveStore.TestDirectory;
            report=new Report{saveDirectory=Path.Combine(Path.GetDirectoryName(path),"isolated-freedom19-chain-"+Guid.NewGuid().ToString("N"))};
            checks.Clear();errors.Clear();Application.logMessageReceived+=ErrorLog;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                fixture=new GameObject("Freedom19RealProcurementChain");
                var vehicle=new GameObject("ActualCartAI");vehicle.transform.SetParent(fixture.transform,false);
                var prefab=Resources.Load<GameObject>("Models/CartAI");Need(prefab!=null,"Models/CartAI is missing");
                UnityEngine.Object.Instantiate(prefab,vehicle.transform,false);
                packing=vehicle.AddComponent<VehicleLoadout>();packing.Initialize(vehicle.transform,Make);
                var mapping=new GameObject("ProductionSetupMapper");mapping.transform.SetParent(fixture.transform,false);mapping.SetActive(false);mapper=mapping.AddComponent<GameFlow>();
                CampaignSaveStore.TestDirectory=report.saveDirectory;
                Chain();
            }
            catch(Exception error){checks.Add(new Check{name="chain failure",detail=error.ToString(),passed=false});}
            finally
            {
                if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);
                fixture=null;packing=null;mapper=null;CampaignSaveStore.TestDirectory=previous;
                Application.logMessageReceived-=ErrorLog;
            }
            report.checks=checks.ToArray();report.runtimeErrors=errors.ToArray();report.success=checks.Count>0&&checks.All(x=>x.passed)&&errors.Count==0;
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log((report.success?"FREEDOM19_CHAIN_PASS ":"FREEDOM19_CHAIN_FAIL ")+path);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static GameObject Make(string id,Transform parent)
        {
            if(SupplyCatalog.IsEquipment(id)||id.StartsWith("P")||id.StartsWith("S"))return EquipmentVisualFactory.Create(id,parent);
            var factory=typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic);
            Need(factory!=null,"production food factory missing");
            return (GameObject)factory.Invoke(null,new object[]{id,parent,.36f});
        }
        static void Bind(RunState run){run.LoadValidator=(gear,cargo)=>packing.Validate(gear,cargo,run.PackingSupplies);}
        static BusinessSetup Setup(RunState run)
        {typeof(GameFlow).GetField("run",Hidden).SetValue(mapper,run);return mapper.BuildBusinessSetup();}
        static CampaignSaveData Roundtrip(CampaignState campaign,RunState run,BusinessSession service=null)
        {
            var data=new CampaignSaveData{campaign=campaign,run=run.CaptureSave(),business=service?.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
            Need(CampaignSaveStore.Save(data,out string status),"save: "+status);
            Need(CampaignSaveStore.Load(out CampaignSaveData loaded,out status),"load: "+status);
            Need(File.Exists(Path.Combine(report.saveDirectory,"current.json")),"physical save file missing");
            return loaded;
        }
        static void Buy(RunState run,params string[] ids)
        {foreach(string id in ids)Need(run.AddSupply(id),"buy "+id+": "+run.Message);}
        static void PaySelection(RunState run,string[] selected)
        {
            int cash=run.CashCents,spent=run.SpentCents,total=selected.Sum(run.PurchaseItemPriceCents);
            Need(run.PreviewCheckoutSelection(selected,out int preview,out string reason)&&preview==total,"preview: "+reason);
            Need(run.ConfirmCheckoutSelection(selected),"confirm: "+run.Message);
            Need(run.CashCents==cash&&run.SpentCents==spent,"selection charged before service");
            run.Tick(run.CheckoutDuration,true);
            Need(run.Paid&&run.CashCents==cash-total&&run.SpentCents==spent+total,"selection debit mismatch");
            Need(run.Cart.Count==selected.Length,"subset retained wrong count");
        }
        static void Load(RunState run)
        {
            int count=run.Cart.Count,cargo=run.Cargo.Count,cash=run.CashCents;
            Need(run.Act("MarketCart",true),"actual packing/load: "+run.Message);
            Need(run.Cart.Count==0&&run.Cargo.Count==cargo+count&&!run.Paid&&run.CashCents==cash,"load lost goods or money");
            Need(packing.Plan!=null&&packing.Plan.FullyValidated,"measured packing not validated");
            Need(packing.Plan.placements.Select(x=>x.instanceId).Distinct().Count()==packing.Plan.placements.Count,"packing contains duplicate instance ids");
        }
        static void Cook(BusinessSession session,int lane,string flavor)
        {
            Need(session.SetFreeMethod(lane,FreeKitchenRules.MethodStirFry),"method: "+session.Message);
            int gas=session.Gas;
            Need(session.StartFreeCooking(lane),"start: "+session.Message);
            Need(session.Gas==gas-1,"actual stove did not debit exactly one gas");
            Need(!session.StartFreeCooking(lane)&&session.Gas==gas-1,"start charged twice");
            for(int step=0;step<600&&session.Lanes[lane].step==KitchenStep.Heating;step++)session.Tick(.05f);
            Need(session.Lanes[lane].step==KitchenStep.Utensil,"did not reach utensil window");
            Need(session.Action(lane),"utensil: "+session.Message);
            Need(flavor==""?session.Action(lane):session.Action(lane,flavor),"season: "+session.Message);
            Need(session.Lanes[lane].step==KitchenStep.Pack&&session.ProductAtLane(lane)!=null,"cooking did not create product");
        }
        static int ServeNaturalCustomer(BusinessSession session,string mealId)
        {
            int target=-1;
            for(int step=0;step<1200&&target<0&&!session.Finished;step++)
            {
                foreach(var order in session.Orders)
                    if(!order.arrived&&!order.departed&&!order.served)
                    {Need(session.MarkCustomerArrived(order.id),"arrival marker refused");report.arrivalMarkers++;}
                target=session.FindServingCounterOrder(mealId);
                if(target<0)session.Tick(.05f);
            }
            Need(target>=0,"no natural compatible walk-up for real counter meal");
            var meal=session.ReadyMeals.Find(x=>x.id==mealId);Need(meal!=null,"counter meal vanished while waiting");
            var orderToServe=session.FindOrder(target);
            Need(orderToServe!=null&&orderToServe.productId!=""&&orderToServe.productName==meal.product.name,"order was not spawned from actual product");
            int quote=meal.priceCents,cash=session.CashCents,revenue=session.RevenueCents,pack=session.Count(meal.packageId),egg=session.Count("egg"),meat=session.Count("meat"),flavor=session.Count("S02");
            Need(session.ServeCounterMeal(mealId,target),"selected real target serve: "+session.Message);
            Need(session.CashCents==cash+quote&&session.RevenueCents==revenue+quote&&orderToServe.paidCents==quote,"sale did not collect actual quote exactly once");
            Need(session.Count(meal.packageId)==pack&&session.Count("egg")==egg&&session.Count("meat")==meat&&session.Count("S02")==flavor,"delivery charged ingredients/package/flavor twice");
            Need(!session.ServeCounterMeal(mealId,target)&&session.CashCents==cash+quote,"duplicate delivery paid again");
            return target;
        }
        static void Chain()
        {
            var profile=new RunProfile{careerId="career4",selectedTraits=new[]{"迟钝学者"}};
            Need(profile.Validate(out string reason),"ordinary career invalid: "+reason);
            var run=new RunState(4,profile);var campaign=CampaignState.New(profile,run);
            report.initialCash=run.CashCents;Need(run.CashCents==profile.InitialCashCents&&run.MealPlan.Count==0,"ordinary opening funds/empty plan changed");
            int openingPackages=run.Supplies.TryGetValue("P05",out int p)?p:0,openingFlavor=run.Supplies.TryGetValue("S02",out int f)?f:0;
            Bind(run);
            for(int guard=0;run.HasUnloadedEquipment&&guard<20;guard++)
            {Need(run.Act("StovePickup",true),run.Message);Need(run.Act("HomeCart",true),"load starter gear: "+run.Message);}
            Need(!run.HasUnloadedEquipment&&run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"ordinary market transition failed");
            Buy(run,"egg_small","egg_small","meat_small","P05","E03","S02","seasoning_small","fruit_small");
            string[] selected={"egg_small","meat_small","P05","E03","S02","seasoning_small"};
            PaySelection(run,selected);
            Need(run.Cart.Count(x=>x=="egg_small")==1&&!run.Cart.Contains("fruit_small"),"unselected duplicate/fruit were charged or kept");
            int paid=run.SpentCents;
            var paidSave=Roundtrip(campaign,run);campaign=paidSave.campaign;run=RunState.Restore(paidSave.run);run.Paused=false;Bind(run);
            Need(run.Paid&&run.SpentCents==paid&&!run.Act("Cashier",true),"paid pending load restore allowed repeat charge");
            Load(run);Buy(run,"egg_small");PaySelection(run,new[]{"egg_small"});Load(run);
            Need(run.Cargo.Count(x=>x=="egg_small")==2,"second paid batch lost same-SKU unit");
            Need(packing.Plan.Find("cargo_egg_small")!=null&&packing.Plan.Find("cargo_egg_small_1")!=null,"actual measured cargo lacks both egg units");
            report.purchases=run.SpentCents;
            Need(run.CashCents==report.initialCash-report.purchases&&run.MealPlan.Count==0,"procurement cash or optional plan mutated");
            Passed("ordinary subset + repeated paid batch + actual CartAI","career4/迟钝学者; actual wallet, 6-unit selected basket, second egg unit, paid-pending save, measured distinct cargo placements");

            Need(run.Act("MarketExit",true),run.Message);
            var setup=Setup(run);Need(!setup.practice&&setup.profile.careerId=="career4","practice setup replaced real supplies");
            var service=new BusinessSession(setup);service.SelectedRecipes.Clear();
            Need(service.Open()&&service.SelectedRecipes.Count==0,"empty menu cannot open");run.SetBusinessPhase(GamePhase.Business);
            Need(service.Count("egg")==8&&service.Count("meat")==3,"purchased portion projection drifted");
            Need(service.Count("P05")==openingPackages+GameCatalog.Find("P05").portions&&service.Count("S02")==openingFlavor+GameCatalog.Find("S02").portions,"bought package/flavor quantities lost");
            int lane=service.FindFreeLane("egg"),eggBefore=service.Count("egg"),meatBefore=service.Count("meat");Need(lane>=0,"no actual compatible cookware");
            Need(service.AddFreeIngredient(lane,"egg")&&service.AddFreeIngredient(lane,"meat"),"free composition rejected: "+service.Message);
            Need(service.Count("egg")==eggBefore-1&&service.Count("meat")==meatBefore-1,"free input debit not exact");
            Cook(service,lane,"S02");var product=service.ProductAtLane(lane);
            Need(product.matchedRecipeIndex==-1&&product.name!=BusinessSession.Recipes[product.visualRecipeIndex].name,"generic product masquerades as a fixed recipe");
            report.productName=product.name;report.productId=product.id;
            int cashBefore=service.CashCents,packagesBefore=service.Count("P05"),flavorBefore=service.Count("S02"),saltBefore=service.Count("seasoning");
            Need(service.Orders.Count==0,"first pack unexpectedly depends on a customer");
            Need(service.PackToCounter(lane,"P05"),"pack before customer failed: "+service.Message);
            var first=service.ReadyMeals.Single();report.mealId=first.id;int firstQuote=first.priceCents;
            Need(service.CashCents==cashBefore&&service.RevenueCents==0,"pack produced premature revenue");
            Need(service.Count("P05")==packagesBefore-1&&service.Count("S02")==flavorBefore-1&&service.Count("seasoning")==saltBefore-1,"pack transaction did not debit one actual package/flavor/salt");
            Need(service.Lanes[lane].step==KitchenStep.Idle&&service.ProductAtLane(lane)==null,"empty cookware remained occupied");
            Need(service.FindFreeLane("egg")==lane&&service.AddFreeIngredient(lane,"egg"),"same cookware cannot begin next batch");
            Cook(service,lane,"");Need(service.PackToCounter(lane,"P05"),"second actual dish did not pack");
            Need(first.product.name==report.productName&&first.product.id==report.productId&&first.priceCents==firstQuote,"new batch changed independent first meal");
            string secondId=service.ReadyMeals.Find(x=>x.id!=first.id).id;
            Passed("free composition + pack first + cookware reuse",report.productName+"; true input/gas/packaging/flavor debit; two independently packaged products; no plan and no selected recipes");

            var saved=Roundtrip(campaign,run,service);campaign=saved.campaign;run=RunState.Restore(saved.run);run.Paused=false;Bind(run);service=BusinessSession.Restore(saved.business);
            var restoredMeal=service.ReadyMeals.Find(x=>x.id==report.mealId);
            Need(restoredMeal!=null&&restoredMeal.product.name==report.productName&&restoredMeal.product.id==report.productId&&restoredMeal.priceCents==firstQuote&&restoredMeal.flavorId=="S02"&&restoredMeal.packageId=="P05","counter save lost actual product/package/flavor/quote");
            Need(service.ReadyMeals.Count==2&&service.SelectedRecipes.Count==0&&service.Lanes[lane].step==KitchenStep.Idle,"save restored menu gate or occupied cookware");
            int firstOrder=ServeNaturalCustomer(service,report.mealId);ServeNaturalCustomer(service,secondId);
            Need(service.OrderDishName(firstOrder)==report.productName,"served order lost actual product identity");
            Need(service.ReadyMeals.Count==0&&service.Served==2,"counter products did not each sell once");
            var receipt=CommerceReceiptData.Sales(service);
            Need(receipt.totalsMatch&&receipt.totalCents==service.RevenueCents&&receipt.lines.Any(x=>x.name==report.productName&&x.unitCents==firstQuote&&x.subtotalCents==firstQuote),"actual product missing/mispriced in sales receipt");
            var audit=service.CaptureAudit();Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeInventory&&audit.nonNegativeEnergy&&audit.recipeProduction.All(x=>x.portionsConserved),"production conservation failed");
            report.naturalArrivalAttempts=service.ArrivalAttempts;report.revenue=service.RevenueCents;
            Passed("checksummed file reload + natural walk-up + selected delivery","No Orders.Add/injected customer or money. Natural SpawnOrder + marked model arrival, actual quote paid once, no duplicate package/input debit; receipt preserves generic name.");

            service.Close();run.SetBusinessCash(service.CashCents);run.SetBusinessPhase(GamePhase.DaySummary);
            Need(campaign.CommitDay(run,service,service.SessionId),"real day settlement failed");
            var ledger=campaign.lastDay;
            Need(ledger.purchasesCents==report.purchases&&ledger.revenueCents==report.revenue&&ledger.sales.Sum(x=>x.totalCents)==report.revenue,"day ledger does not conserve real procurement/revenue");
            Need(ledger.sales.Any(x=>x.name==report.productName&&x.unitPriceCents==firstQuote&&x.quantity==1&&x.totalCents==firstQuote),"night ledger replaced actual product with a recipe");
            Need(campaign.cashCents==report.initialCash-report.purchases+report.revenue-ledger.nightFuelCents,"ordinary career cash equation failed");
            string before=JsonUtility.ToJson(campaign);Need(!campaign.CommitDay(run,service,service.SessionId)&&JsonUtility.ToJson(campaign)==before,"duplicate day settlement changed money/stock");
            var nightSave=Roundtrip(campaign,run,service);
            Need(nightSave.campaign.lastDay.sales.Any(x=>x.name==report.productName&&x.unitPriceCents==firstQuote)&&nightSave.campaign.cashCents==campaign.cashCents,"night file lost actual product/price/cash");
            report.closingCash=campaign.cashCents;
            Passed("actual income + persistent day ledger","opening "+report.initialCash+" - purchases "+report.purchases+" + sales "+report.revenue+" - night fuel "+ledger.nightFuelCents+" = closing "+report.closingCash+" cents; duplicate settlement refused");
        }
    }
}
