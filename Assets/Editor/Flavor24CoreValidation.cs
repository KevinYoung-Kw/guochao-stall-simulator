using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Isolated model-level checks against production purchase and cooking APIs.
    // Fixtures supply ingredients but never alter a real campaign save.
    public static class Flavor24CoreValidation
    {
        [Serializable] private sealed class Result { public string name, error; public bool passed; }
        [Serializable] private sealed class Report
        {
            public bool success;
            public string scope="Isolated Editor model fixtures using actual RunState and BusinessSession transactions; not hardware input or a full campaign.";
            public List<Result> checks=new List<Result>();
        }
        private static readonly Report report=new Report();
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static void Case(string name,Action test)
        {
            var result=new Result{name=name};report.checks.Add(result);
            try{test();result.passed=true;}catch(Exception e){result.error=e.ToString();}
        }
        private static RunState MarketRun()
        {
            var run=new RunState(.05f);
            Need(run.Act("StovePickup",true),"home pickup");
            Need(run.Act("HomeCart",true),"home load");
            Need(run.Act("HomeExit",true),"market entry");
            Need(run.Phase==GamePhase.Market,"market phase");
            return run;
        }
        private static BusinessSetup Setup()
        {
            return new BusinessSetup{cashCents=5000,seed=12017,dayDuration=300,stoveLoaded=true,customerIntervalSeconds=16,
                equipment=new HashSet<string>{"V15","V16","U12","U13"},
                inventory=new Dictionary<string,int>{{"fruit",3},{"P08",3},{"S13",2},{"S14",2}}};
        }
        private static BusinessSession Open()
        {
            var s=new BusinessSession(Setup());s.SelectedRecipes.Clear();Need(s.Open(),"open");return s;
        }
        private static CounterMeal MakeDrink(BusinessSession s,string flavor)
        {
            Need(s.AddFreeIngredient(0,"fruit"),"add fruit: "+s.Message);
            Need(s.SetFreeMethod(0,FreeKitchenRules.MethodMuddle),"choose muddle: "+s.Message);
            Need(s.StartFreeCooking(0),"start: "+s.Message);
            Need(s.Action(0),"finish: "+s.Message);
            Need(s.ProductAtLane(0)!=null,"no actual product");
            int before=s.Count(flavor);Need(s.Action(0,flavor),"choose topping: "+s.Message);
            Need(s.Count(flavor)==before,"selecting should not debit topping");
            Need(s.PackToCounter(0,"P08"),"pack: "+s.Message);
            Need(s.Count(flavor)==before-1,"packing debits exactly one topping");
            return s.ReadyMeals[s.ReadyMeals.Count-1];
        }
        public static void Run()
        {
            Case("catalog, family and original prices",()=>{
                foreach(var x in new[]{(id:"S13",price:1200),(id:"S14",price:900)})
                {
                    var f=StallFlavorMenu.Find(x.id);var sku=SupplyCatalog.Find(x.id);
                    Need(f!=null&&sku!=null&&f.portionsPerBottle==30&&f.bottlePriceCents==x.price&&f.markupCentsPerPortion==200,"wrong flavor metadata "+x.id);
                    Need(sku.priceCents==x.price&&sku.portions==30&&SupplyCatalog.IsCounterItem(x.id),"not purchasable "+x.id);
                    Need(StallFlavorMenu.Compatible(BusinessSession.Recipes[10],x.id),"cold recipe incompatible "+x.id);
                    Need(!StallFlavorMenu.Compatible(BusinessSession.Recipes[0],x.id),"hot recipe accepts tea topping "+x.id);
                }
            });
            Case("new milk-tea career owns both real stocks",()=>{
                var run=new RunState(.05f,new RunProfile{careerId="career6"});
                Need(run.Supplies.TryGetValue("S13",out int a)&&a==30,"missing S13 starter stock");
                Need(run.Supplies.TryGetValue("S14",out int b)&&b==30,"missing S14 starter stock");
                Need(!run.LoadedEquipment.Contains("S13")&&!run.LoadedEquipment.Contains("S14"),"toppings classified as equipment");
            });
            Case("old career save migrates via campaign load",()=>{
                var profile=new RunProfile{careerId="career6"};var run=new RunState(.05f,profile);
                var campaign=CampaignState.New(profile,run);var oldRun=run.CaptureSave();
                oldRun.loadedEquipment=oldRun.loadedEquipment.Concat(new[]{"S13","S14"}).ToArray();
                oldRun.supplies=oldRun.supplies.Where(x=>x.id!="S13"&&x.id!="S14").ToArray();
                campaign.stock.RemoveAll(x=>x.id=="S13"||x.id=="S14");
                campaign.equipment.Add(new CampaignGearRow{instanceId="old-s13",id="S13",name="珍珠",location="vehicle"});
                campaign.equipment.Add(new CampaignGearRow{instanceId="old-s14",id="S14",name="椰果",location="vehicle"});
                string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--flavor24-report");
                Need(at>=0&&at+1<args.Length,"report directory required");
                string dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[at+1])),"legacy-core-save");
                Need(!Directory.Exists(dir),"preserve existing test saves");string previous=CampaignSaveStore.TestDirectory;CampaignSaveStore.TestDirectory=dir;
                try
                {
                    Need(CampaignSaveStore.Save(new CampaignSaveData{campaign=campaign,run=oldRun},out string status),"write old fixture: "+status);
                    Need(CampaignSaveStore.Load(out var loaded,out status),"load old fixture: "+status);
                    var restored=RunState.Restore(loaded.run);
                    Need(restored.Supplies.TryGetValue("S13",out int a)&&a==30,"old S13 equipment not migrated");
                    Need(restored.Supplies.TryGetValue("S14",out int b)&&b==30,"old S14 equipment not migrated");
                    Need(!restored.LoadedEquipment.Contains("S13")&&!restored.LoadedEquipment.Contains("S14"),"old topping still equipment");
                    Need(CampaignSaveStore.Save(loaded,out status),"re-save migrated fixture: "+status);
                    Need(CampaignSaveStore.Load(out var twice,out status),"reload migrated save: "+status);
                    var reread=RunState.Restore(twice.run);Need(reread.Supplies["S13"]==30&&reread.Supplies["S14"]==30,"migration repeated");
                }
                finally{CampaignSaveStore.TestDirectory=previous;}
            });
            Case("purchase, payment, load and business projection",()=>{
                var run=MarketRun();int cash=run.CashCents;
                Need(run.AddSupply("S13")&&run.AddSupply("S14"),"add topping to real basket");
                Need(run.CartTotalCents==2100,"basket price");
                Need(run.Act("Cashier",true),"cashier");run.Tick(run.CheckoutDuration+.01f,true);
                Need(run.Paid&&run.CashCents==cash-2100,"payment");
                Need(run.Act("MarketCart",true),"load paid goods");
                var setup=ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run);
                Need(setup.inventory.TryGetValue("S13",out int a)&&a==30,"S13 absent from service");
                Need(setup.inventory.TryGetValue("S14",out int b)&&b==30,"S14 absent from service");
            });
            foreach(string id in new[]{"S13","S14"})Case(id+" free drink, stock and payment",()=>{
                var s=Open();var meal=MakeDrink(s,id);
                Need(meal.flavorId==id&&meal.priceCents==meal.product.priceCents+200,"wrong topping or quote");
                var copy=BusinessSession.Restore(s.CaptureSave());Need(copy.ReadyMeals.Count==1&&copy.ReadyMeals[0].flavorId==id,"save lost topping");
                StallOrder order=null;
                for(int i=0;i<120&&order==null;i++)
                {
                    s.Tick(.2f);
                    foreach(var o in s.Orders)if(!o.served&&!o.departed&&o.productId==meal.product.id&&o.requestedFlavorId==id){order=o;break;}
                }
                Need(order!=null,"no compatible customer for actual product");
                s.MarkCustomerArrived(order.id);int cash=s.CashCents;
                Need(s.ServeCounterMeal(meal.id,order.id),"serve: "+s.Message);
                Need(s.CashCents==cash+meal.priceCents&&order.paidCents==meal.priceCents,"wrong actual payment");
            });
            Case("empty stock does not create a topping option",()=>{
                var setup=Setup();setup.inventory["S13"]=0;var s=new BusinessSession(setup);
                Need(!s.AvailableFlavors(10).Contains("S13"),"empty topping offered");
                Need(s.AvailableFlavors(10).Contains("S14"),"stocked topping unavailable");
            });
            report.success=report.checks.All(x=>x.passed);
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--flavor24-report");
            if(at<0||at+1>=args.Length)throw new InvalidOperationException("Need --flavor24-report path");
            File.WriteAllText(args[at+1],JsonUtility.ToJson(report,true));
            if(!report.success)throw new Exception("Flavor24 core validation failed; see report");
        }
    }
}
