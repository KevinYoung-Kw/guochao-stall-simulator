using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Declared inventory/equipment fixtures; real public Session transactions,
    // natural order spawning and JSON disk persistence. Physical actors belong
    // to Service20Acceptance; no claim of keyboard/animation validation here.
    public static class Fulfilment20CoreValidation
    {
        [Serializable] public sealed class Report
        {
            public string scope="Declared model fixtures: real cooking, hand/table transfers, natural customer demand, actual payment/risk and JSON disk roundtrip. No actor-contact or keyboard claim.";
            public bool success;public int checks;public List<string> failures=new List<string>();
        }
        static Report report;static string output;
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--fulfilment-core-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --fulfilment-core-output <json>");
            output=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(Path.GetDirectoryName(output));report=new Report();
            Case("pack",PackDebitsAndSnapshot);Case("place",PlaceWithoutSecondDebit);
            Case("direct",LastPackageDirectSale);Case("wrong-departed",WrongAndDepartedKeepFood);
            Case("full-counter",FullCounterStillAllowsHeldDirect);Case("json",SaveAndMalformedData);
            Case("risk",RiskDirectRoundtrip);Case("plan",ReferencePlanFreedom);
            Case("close",ClosingPreservesHeld);Case("flavor-supply",DistinctPackagedFlavors);Case("legacy",LegacyCookedBatchRemainsDeliverable);
            report.success=report.failures.Count==0;File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("FULFILMENT20_CORE_"+(report.success?"PASS":"FAIL")+" "+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static void Case(string name,Action action){try{action();}catch(Exception e){report.failures.Add(name+": "+e);}}
        static void Need(bool condition,string detail){report.checks++;if(!condition)throw new InvalidOperationException(detail);}
        static BusinessSetup Setup(int portions=3,int packages=3)=>new BusinessSetup
        {
            cashCents=3200,seed=12017,dayDuration=300,stoveLoaded=true,gasPoints=20,
            customerIntervalSeconds=1,queueCapacity=12,patienceMinSeconds=30,patienceMaxSeconds=45,
            equipment=new HashSet<string>{"B04","V02","U02","V15","U13","V16","U12"},
            inventory=new Dictionary<string,int>{{"egg",portions},{"meat",portions},{"seasoning",portions},{"S01",portions},{"P05",packages}}
        };
        static BusinessSession Open(BusinessSetup setup=null)
        {
            var s=new BusinessSession(setup??Setup());s.SelectedRecipes.Clear();Need(s.Open(),"Opening with no menu failed");return s;
        }
        static FoodProduct Cook(BusinessSession s,int portions=1)
        {
            for(int i=0;i<portions;i++){Need(s.AddFreeIngredient(0,"egg"),"Egg input failed");Need(s.AddFreeIngredient(0,"meat"),"Meat input failed");}
            Need(s.SetFreeMethod(0,"stir_fry")&&s.StartFreeCooking(0),"Actual stir-fry start failed");
            for(int i=0;i<160&&s.Lanes[0].step==KitchenStep.Heating;i++)s.Tick(.1f);
            if(s.Lanes[0].step==KitchenStep.Utensil)Need(s.Action(0),"Actual utensil finish failed");
            if(s.Lanes[0].step==KitchenStep.Season)Need(s.Action(0,"seasoning"),"Actual seasoning finish failed");
            FoodProduct product=s.ProductAtLane(0);
            Need(product!=null&&product.matchedRecipeIndex==-1&&s.Lanes[0].step==KitchenStep.Pack,"Expected real generic cooked product");return product;
        }
        static CounterMeal Pack(BusinessSession s,bool flavored=false)
        {
            Cook(s);if(flavored)Need(s.Action(0,"S01"),"Flavor selection failed");
            Need(s.PackToHand(0,"P05"),"Pack to actual independent hand failed");return s.HeldMeal;
        }
        static StallOrder Customer(BusinessSession s,CounterMeal meal)
        {
            for(int step=0;step<50;step++)
            {
                var found=s.Orders.FirstOrDefault(o=>!o.served&&!o.departed&&o.productId==meal.product.id&&(o.requestedFlavorId??"")==meal.flavorId);
                if(found!=null){Need(s.MarkCustomerArrived(found.id)||found.arrived,"Arrival acknowledgement failed");return found;}
                s.Tick(.2f);
            }
            throw new InvalidOperationException("No natural customer for actual packed supply");
        }
        static void Audit(BusinessSession s)
        {
            var a=s.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"Cash/package/inventory/energy conservation failed");
            foreach(var p in a.recipeProduction)Need(p.portionsConserved,"Portion conservation failed for "+p.recipe);
            Need((a.heldMeal==null)==(s.HeldMeal==null),"Held audit missing real holding state");
        }
        static BusinessSession Disk(BusinessSession s,string name)
        {
            string path=Path.Combine(Path.GetDirectoryName(output),"held-"+name+".json");
            File.WriteAllText(path,JsonUtility.ToJson(s.CaptureSave()));
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(File.ReadAllText(path)));Audit(restored);return restored;
        }
        static void PackDebitsAndSnapshot()
        {
            var s=Open();FoodProduct pan=Cook(s,2);Need(s.Action(0,"S01"),"Flavor selection");
            int wrappers=s.Count("P05"),flavor=s.Count("S01"),oil=s.Count("seasoning"),cash=s.CashCents;
            Need(s.PackToHand(0,"P05"),"First pack");var held=s.HeldMeal;
            Need(held!=null&&s.ReadyMeals.Count==0&&held.product!=pan,"Held must be independent, not hidden table insertion");
            Need(s.Count("P05")==wrappers-1&&s.Count("S01")==flavor-1&&s.Count("seasoning")==oil-1,"Pack must debit one actual wrapper/flavor/oil portion");
            Need(pan.remaining==1&&held.product.remaining==1&&held.product.portions==1&&held.product.sourcePortions==2,"Batch-to-single product provenance drift");
            Need(held.flavorId=="S01"&&held.priceCents==held.product.priceCents+StallFlavorMenu.MarkupFor("S01"),"Frozen flavored quote missing");
            Need(!s.PackToHand(0,"P05")&&s.HeldMeal.id==held.id&&pan.remaining==1&&s.Count("P05")==wrappers-1,"Full hand consumed or replaced food");
            Need(s.CashCents==cash&&s.RevenueCents==0,"Packing booked an early sale");
            var copy=s.CaptureSave();copy.heldMeal.product.inputs[0].units=99;
            Need(held.product.inputs[0].units==2,"Save held product aliases live object");Audit(s);
        }
        static void PlaceWithoutSecondDebit()
        {
            var s=Open();var meal=Pack(s,true);string inventory=Stock(s);int cash=s.CashCents;
            Need(s.PlaceHeldMealOnCounter(),"Place held meal failed");
            Need(s.HeldMeal==null&&s.ReadyMeals.Count==1&&s.ReadyMeals[0].id==meal.id,"Placement changed identity or duplicated food");
            Need(Stock(s)==inventory&&s.CashCents==cash&&s.RevenueCents==0,"Placement debited inventory or charged customer");
            Need(!s.PlaceHeldMealOnCounter()&&s.ReadyMeals.Count==1,"Repeated place duplicated meal");
            var restored=Disk(s,"placed");Need(restored.ReadyMeals.Count==1&&restored.HeldMeal==null,"Placed persistence drift");
        }
        static void LastPackageDirectSale()
        {
            var s=Open(Setup(1,1));var meal=Pack(s);int cash=s.CashCents;
            Need(s.Count("P05")==0&&s.ReadyMeals.Count==0&&s.ProductAtLane(0)==null,"Fixture must contain only the last held packed meal");
            var order=Customer(s,meal);Need(s.CanServeHeldMeal(order.id,out _),"Held customer mismatch");
            Need(s.ServeHeldMeal(order.id),"Actual direct serve failed");
            Need(s.HeldMeal==null&&s.ReadyMeals.Count==0&&s.CashCents==cash+meal.priceCents&&order.paidCents==meal.priceCents,"Direct serve must settle once without any table insertion");
            Need(!s.ServeHeldMeal(order.id)&&s.CashCents==cash+meal.priceCents,"Repeated direct serve duplicated payment");
            var restored=Disk(s,"direct-sold");Need(restored.RevenueCents==meal.priceCents&&restored.HeldMeal==null,"Sold persistence changed cash or restored consumed meal");
        }
        static void WrongAndDepartedKeepFood()
        {
            var s=Open(Setup(1,1));var meal=Pack(s);var order=Customer(s,meal);int cash=s.CashCents;
            order.requestedFlavorId="S01"; // declared changed customer-request fixture, no stock/payment mutation
            Need(!s.CanServeHeldMeal(order.id,out _)&&!s.ServeHeldMeal(order.id),"Wrong flavor accepted");
            Need(s.HeldMeal.id==meal.id&&s.CashCents==cash,"Wrong target consumed food/payment");order.requestedFlavorId="";
            s.Tick(order.patience+.1f);Need(order.departed,"Patience did not expire");
            Need(!s.ServeHeldMeal(order.id)&&s.HeldMeal.id==meal.id,"Departed customer consumed held meal");Audit(s);
        }
        static void FullCounterStillAllowsHeldDirect()
        {
            var s=Open(Setup(8,8));
            for(int i=0;i<BusinessSession.CounterCapacity;i++){Cook(s);Need(s.PackToCounter(0,"P05"),"Counter fill failed");}
            var meal=Pack(s);Need(s.ReadyMeals.Count==6&&s.HeldMeal!=null,"Full table prevented independent hand packing");
            Need(!s.CanPlaceHeldMeal(out _)&&!s.PlaceHeldMealOnCounter()&&s.HeldMeal.id==meal.id,"Full table lost/inserted held meal");
            var restored=Disk(s,"six-table-plus-hand");Need(restored.ReadyMeals.Count==6&&restored.HeldMeal.id==meal.id,"Seven independent holding slots did not persist");
            var order=Customer(restored,restored.HeldMeal);Need(restored.ServeHeldMeal(order.id),"Full table blocked direct hand sale");
            Need(restored.ReadyMeals.Count==6&&restored.HeldMeal==null,"Direct full-table sale removed an unrelated table meal");Audit(restored);
        }
        static string Stock(BusinessSession s)=>string.Join("|",s.Inventory.OrderBy(p=>p.Key).Select(p=>p.Key+":"+p.Value));
        static void LegacyCookedBatchRemainsDeliverable()
        {
            var setup=Setup();setup.inventory["S01"]=0;var s=new BusinessSession(setup);s.SelectedRecipes.Clear();s.SelectedRecipes.Add(8);
            Need(s.Open()&&s.BeginBatch(0),"Legacy batch could not start");
            for(int i=0;i<200&&(s.Lanes[0].step==KitchenStep.Prep||s.Lanes[0].step==KitchenStep.Heating);i++)s.Tick(.1f);
            if(s.Lanes[0].step==KitchenStep.Utensil)Need(s.Action(0),"Legacy utensil failed");
            Need(s.Lanes[0].freeBatch==null&&s.Lanes[0].step==KitchenStep.ReadyStock&&s.Lanes[0].batchRemaining>0,"Legacy cooked fixture invalid");
            int originalPortions=s.Lanes[0].batchRemaining;
            string oldJson=RemoveField(RemoveField(JsonUtility.ToJson(s.CaptureSave()),"heldMeal"),"heldMealPresent");
            string path=Path.Combine(Path.GetDirectoryName(output),"legacy-cooked-before-held.json");File.WriteAllText(path,oldJson);
            var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(File.ReadAllText(path)));
            Need(restored.HeldMeal==null&&restored.Lanes[0].freeBatch==null&&restored.Lanes[0].batchRemaining==originalPortions,"Upgrade erased or fabricated legacy cooked food");
            StallOrder order=null;
            for(int i=0;i<50&&order==null;i++)
            {order=restored.Orders.FirstOrDefault(o=>!o.served&&!o.departed&&o.recipeIndex==8);if(order==null)restored.Tick(.2f);}
            Need(order!=null,"No customer for retained legacy cooked batch");restored.MarkCustomerArrived(order.id);
            Need(restored.ScoopBatch(0)&&restored.Action(0),"Retained legacy scoop/season route failed");
            int cash=restored.CashCents,packages=restored.Count("P05");
            Need(restored.ServeToOrder(0,"P05",order.id),"Retained legacy ServeToOrder failed");
            Need(restored.CashCents==cash+BusinessSession.Recipes[8].priceCents&&restored.Count("P05")==packages-1&&restored.Lanes[0].batchRemaining==originalPortions-1,"Legacy delivery not settled exactly once");
            Need(!restored.ServeToOrder(0,"P05",order.id)&&restored.CashCents==cash+BusinessSession.Recipes[8].priceCents,"Legacy duplicate delivery paid again");Audit(Disk(restored,"legacy-after-sale"));
        }
        static void RejectSave(BusinessSaveData d,string expected)
        {
            bool rejected=false;try{BusinessSession.Restore(d);}catch(ArgumentException e){rejected=expected==""||e.Message.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0;}
            Need(rejected,"Malformed save was not rejected: "+expected);
        }
        static void SaveAndMalformedData()
        {
            var empty=Open();Need(Disk(empty,"empty").HeldMeal==null,"Unity inline null became a phantom held meal");
            string legacy=RemoveField(RemoveField(JsonUtility.ToJson(empty.CaptureSave()),"heldMeal"),"heldMealPresent");
            var old=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(legacy));Need(old.HeldMeal==null&&old.CashCents==empty.CashCents,"Old JSON without held fields changed state");
            var s=Open();var meal=Pack(s,true);var restored=Disk(s,"held");
            Need(restored.HeldMeal.id==meal.id&&restored.HeldMeal.flavorId==meal.flavorId&&restored.HeldMeal.priceCents==meal.priceCents&&restored.ReadyMeals.Count==0,"Held identity/flavor/quote not persisted");
            Need(!ReferenceEquals(restored.HeldMeal,s.HeldMeal)&&!ReferenceEquals(restored.HeldMeal.product.inputs,s.HeldMeal.product.inputs),"Restore aliases held state");
            var malformed=s.CaptureSave();malformed.heldMeal.priceCents++;RejectSave(malformed,"quote");
            malformed=s.CaptureSave();malformed.heldMeal.product.remaining=2;RejectSave(malformed,"product");
            malformed=s.CaptureSave();malformed.readyMeals=new[]{malformed.heldMeal.Copy()};RejectSave(malformed,"duplicate");
            malformed=s.CaptureSave();malformed.heldMeal=null;malformed.heldMealPresent=true;RejectSave(malformed,"Missing marked held");
        }
        static void RiskDirectRoundtrip()
        {
            var s=Open(Setup(1,1));var meal=Pack(s);var order=Customer(s,meal);int cash=s.CashCents;
            s.ServeRiskGate=(id,recipe,price)=>new StallRiskGateDecision(true,price*2,4.5f);
            Need(s.ServeHeldMeal(order.id)&&s.HeldMeal==null&&order.paidCents==0&&s.CashCents==cash,"Risk adoption changed immediate cash or retained sold food");
            var restored=Disk(s,"risk-pending");var risk=restored.RiskState.ActiveEvent;
            Need(risk!=null&&risk.RecipeId==meal.product.id&&risk.PriceCents==meal.priceCents,"Pending risk lost actual held product identity/face value");
            Need(restored.TryCompleteRiskSale(risk.Serial,risk.CollectCents),"Risk collection failed");
            Need(restored.CashCents==cash+risk.CollectCents&&!restored.TryCompleteRiskSale(risk.Serial,risk.CollectCents),"Risk payment duplicated or missing");Audit(Disk(restored,"risk-paid"));
        }
        static void ReferencePlanFreedom()
        {
            var setup=Setup(0,0);var s=new BusinessSession(setup);s.SelectedRecipes.Clear();int cash=s.CashCents;
            Need(s.Missing(0)!=""&&s.ToggleRecipe(0)&&s.SelectedRecipes.Contains(0),"Missing ingredients must not block choosing a reference dish");
            Need(s.Open()&&s.ToggleRecipe(0)&&s.SelectedRecipes.Count==0,"Running plan cannot be cleared");
            Need(!s.CanAddFreeIngredient(0,"egg",out _)&&s.Count("egg")==0&&s.CashCents==cash,"Reference selection bypassed actual inventory");Audit(Disk(s,"empty-plan"));
        }
        static void ClosingPreservesHeld()
        {
            var s=Open();var meal=Pack(s);s.Close();
            Need(s.HeldMeal.id==meal.id&&!s.PlaceHeldMealOnCounter()&&!s.CanServeHeldMeal(1,out _),"Closing deleted the held food or permitted transactions");
            var restored=Disk(s,"closed-held");Need(restored.Finished&&restored.HeldMeal.id==meal.id&&restored.RevenueCents==0,"Closed held meal ledger drift");
        }
        static void DistinctPackagedFlavors()
        {
            var s=Open(Setup(2,2));Cook(s,2);Need(s.Action(0,"S01")&&s.PackToCounter(0,"P05"),"Flavored table supply failed");
            Need(s.Action(0,"seasoning")&&s.PackToHand(0,"P05"),"Plain held supply failed");
            for(int i=0;i<60;i++)s.Tick(.2f);
            var orders=s.Orders.Where(o=>!o.served&&!o.departed).ToArray();
            Need(orders.Length==2&&orders.Count(o=>o.requestedFlavorId=="S01")==1&&orders.Count(o=>o.requestedFlavorId=="")==1,"Natural orders oversold one packaged flavor or ignored held supply");Audit(s);
        }
        static string RemoveField(string json,string field)
        {
            int start=json.IndexOf("\""+field+"\":",StringComparison.Ordinal);if(start<0)return json;
            int p=json.IndexOf(':',start)+1,depth=0;bool quoted=false,escaped=false;
            for(;p<json.Length;p++)
            {
                char c=json[p];if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='\"')quoted=false;continue;}
                if(c=='\"'){quoted=true;continue;}if(c=='{'||c=='[')depth++;else if(c=='}'||c==']'){if(depth==0)break;depth--;}
                if(depth==0&&c==',')return json.Remove(start,p-start+1);
            }
            if(start>0&&json[start-1]==',')start--;return json.Remove(start,p-start);
        }
    }
}
