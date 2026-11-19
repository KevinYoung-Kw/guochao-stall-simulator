using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Guochao.Editor
{
    public static class OrderGuide20Validation
    {
        [Serializable] sealed class Report { public bool success; public int checks; public List<string> failures=new List<string>(); }
        static Report report;
        static void Need(bool ok,string name){report.checks++;if(!ok)report.failures.Add(name);}
        public static void Run()
        {
            report=new Report();
            try{NamedAndMissing();GenericAndFinished();ColdAndUnknown();}
            catch(Exception ex){report.failures.Add(ex.ToString());}
            report.success=report.failures.Count==0;
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--order-guide-output");
            if(index<0||index+1>=args.Length)throw new ArgumentException("--order-guide-output required");
            string path=Path.GetFullPath(args[index+1]);if(File.Exists(path))throw new IOException("Existing evidence retained");
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            if(!report.success)throw new Exception("Order guide validation failed: "+string.Join("; ",report.failures));
        }
        static BusinessSession Open()
        {
            var setup=BusinessSetup.MidgamePractice();setup.customerIntervalSeconds=9999;
            setup.inventory["meat"]=1;setup.inventory["egg"]=1;setup.inventory["rice"]=0;
            var s=new BusinessSession(setup);s.SelectedRecipes.Clear();Need(s.Open(),"Reference list may be empty");return s;
        }
        static StallOrder Order(BusinessSession s,int recipe,string flavor="")
        {
            var order=new StallOrder{id=900+s.Orders.Count,recipeIndex=recipe,arrived=true,patience=40,maxPatience=40,requestedFlavorId=flavor};s.Orders.Add(order);return order;
        }
        static void NamedAndMissing()
        {
            var s=Open();var order=Order(s,0,"S01");string before=JsonUtility.ToJson(s.CaptureSave());
            var guide=OrderCookingGuide.Read(s,order.id);
            Need(guide.ingredients.Count==2&&guide.ingredients.Any(i=>i.id=="egg")&&guide.ingredients.Any(i=>i.id=="rice"),"Fried rice shows both edible inputs, not seasoning as raw input");
            Need(guide.method=="stir_fry"&&guide.next.Contains("米饭"),"Missing rice shown on named recipe");
            Need(guide.flavor==StallInteractionInput.FlavorShortName("S01"),"Requested flavor visible");
            Need(guide.packaging.Contains("餐盒"),"Existing suitable package visible");
            for(int i=0;i<10;i++)OrderCookingGuide.Read(s,order.id);
            Need(before==JsonUtility.ToJson(s.CaptureSave()),"Reading order never changes stock, recipes, reservations, random state or save");
            Need(s.Lanes.All(l=>l.orderId<0&&l.reservedOrderId<0),"Guidance does not bind customer to cookware");
            Need(s.AddFreeIngredient(0,"egg"),"Partial recipe egg placed through real input");
            guide=OrderCookingGuide.Read(s,order.id);
            Need(guide.ingredients.First(i=>i.id=="egg").alreadyPrepared,"Egg already in pan is not falsely missing");
            Need(guide.next.Contains("米饭")&&!guide.next.Contains("鸡蛋"),"Only genuinely missing rice requested");
        }
        static void Cook(BusinessSession s,string method)
        {
            Need(s.SetFreeMethod(0,method),"Method accepted");Need(s.StartFreeCooking(0),"Actual cooking started");
            for(int i=0;i<100&&s.Lanes[0].step==KitchenStep.Heating;i++)s.Tick(.1f);
            if(s.Lanes[0].step==KitchenStep.Utensil)Need(s.Action(0),"Cookware finishing action");
            if(s.Lanes[0].step==KitchenStep.Season)Need(s.Action(0),"Seasoning action");
            Need(s.ProductAtLane(0)!=null,"Real product exists");
        }
        static void GenericAndFinished()
        {
            var s=Open();Need(s.AddFreeIngredient(0,"egg"),"Egg debited");Need(s.AddFreeIngredient(0,"meat"),"Meat debited");Cook(s,"stir_fry");
            var p=s.ProductAtLane(0);var order=Order(s,p.visualRecipeIndex);order.productId=p.id;order.productName=p.name;order.productMethod=p.method;order.productPriceCents=p.priceCents;
            var guide=OrderCookingGuide.Read(s,order.id);
            Need(guide.recipeIndex==-1,"Generic food not advertised as visual adapter fried rice");
            Need(guide.ingredients.Count==2&&guide.ingredients.All(i=>i.id!="rice"&&i.alreadyPrepared),"Generic inputs reflect real egg and meat, already in pan");
            Need(!guide.next.StartsWith("缺"),"Zero pantry does not hide cooked product");
            Need(s.PackToHand(0,"P05"),"Product packed into genuine hand slot");
            guide=OrderCookingGuide.Read(s,order.id);
            Need(guide.ready&&guide.next.Contains("直接递餐"),"Hand meal guidance offers direct handoff");
            string identity=s.HeldMeal.id;int cash=s.CashCents;
            Need(s.PlaceHeldMealOnCounter(),"Same held meal placed on counter");
            guide=OrderCookingGuide.Read(s,order.id);
            Need(guide.ready&&guide.next.Contains("取餐区"),"Counter guidance asks food then customer");
            Need(s.ReadyMeals[0].id==identity&&s.CashCents==cash,"Guidance neither replaces food identity nor settles sale");
        }
        static void ColdAndUnknown()
        {
            var s=Open();var cold=Order(s,5);var guide=OrderCookingGuide.Read(s,cold.id);
            Need(guide.method=="cold_mix"&&!guide.steps.Contains("开火")&&!guide.steps.Contains("加热"),"Cold drink does not request heat");
            var lemon=Order(s,10);Need(OrderCookingGuide.Read(s,lemon.id).method=="muddle","Lemon drink requires muddling");
            var generic=Order(s,0);generic.productId="departed-original-batch";generic.productName="鲜肉热汤";generic.productMethod="boil";
            guide=OrderCookingGuide.Read(s,generic.id);
            Need(!guide.hasExactInputs&&guide.recipeIndex==-1&&guide.ingredients.Count==0,"Lost generic provenance does not fabricate visual recipe ingredients");
            var fryerSetup=BusinessSetup.MidgamePractice();fryerSetup.equipment.Add("V13");fryerSetup.equipment.Add("U08");fryerSetup.equipment.Remove("U10");
            var fryer=new BusinessSession(fryerSetup);var fried=Order(fryer,4);
            Need(OrderCookingGuide.Read(fryer,fried.id).equipment.Contains(BusinessSession.Name("U10")),"Missing extra food tongs reported instead of owned fryer and strainer");
        }
    }
}
