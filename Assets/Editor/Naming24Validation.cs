using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Guochao.Editor
{
 public static class Naming24Validation
 {
  [Serializable] class Report { public bool success;public int checks,combinations;public string[] passed,errors;public string scope="Actual Unity model sessions and saved order/meal transactions; scripted interactions, not a browser or OS-pointer test."; }
  static readonly List<string> passed=new List<string>(),errors=new List<string>();static int combinations;
  static void Need(bool v,string name){if(!v)throw new Exception(name);passed.Add(name);}
  static BusinessSetup Setup(int cents=5000)=>new BusinessSetup{cashCents=cents,seed=12017,dayDuration=300,gasPoints=20,stoveLoaded=true,queueCapacity=4,customerIntervalSeconds=.5f,patienceMinSeconds=90,patienceMaxSeconds=90,equipment=new HashSet<string>{"B04","V02","V05","U02","U04"},inventory=new Dictionary<string,int>{{"egg",6},{"rice",3},{"meat",3},{"seasoning",6},{"P05",6},{"P04",6}}};
  static BusinessSession Open(BusinessSetup setup){var s=new BusinessSession(setup);s.SelectedRecipes.Clear();Need(s.Open(),"open");return s;}
  static FoodProduct Cook(BusinessSession s,string method,params string[] ids){foreach(var id in ids)Need(s.AddFreeIngredient(0,id),"ingredient "+id);Need(s.SetFreeMethod(0,method),"set "+method);Need(s.StartFreeCooking(0),"start "+method);for(int i=0;i<120&&s.Lanes[0].step==KitchenStep.Heating;i++)s.Tick(.1f);Need(s.Lanes[0].step==KitchenStep.Utensil&&s.Action(0),"finish heat");if(s.Lanes[0].step==KitchenStep.Season)Need(s.Action(0),"season");Need(s.ProductAtLane(0)!=null&&s.Lanes[0].step==KitchenStep.Pack,"product ready");return s.ProductAtLane(0);}
  static StallOrder Customer(BusinessSession s,string id){for(int i=0;i<200;i++){var o=s.Orders.Find(x=>x.productId==id&&!x.departed&&!x.served);if(o!=null){if(!o.arrived)Need(s.MarkCustomerArrived(o.id),"arrived");return o;}s.Tick(.2f);}throw new Exception("natural customer absent");}
  static BusinessSession Restore(BusinessSaveData data)=>BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(data)));
  static void Audit(BusinessSession s){var a=s.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"cash and stock conservation");foreach(var x in a.recipeProduction)Need(x.portionsConserved,"portion conservation "+x.recipe);}
  static void Matrix()
  {
   foreach(var method in FreeKitchenRules.Methods)
   {
    var available=FreeKitchenRules.Materials.Where(x=>FreeKitchenRules.MethodAcceptsMaterial(method,x.itemId)).ToArray();
    for(int mask=1;mask<(1<<available.Length);mask++)
    {
     var list=new List<FoodInput>();foreach(var x in available)if((mask&(1<<Array.IndexOf(available,x)))!=0)list.Add(new FoodInput(x.itemId,1));
     var reverse=new List<FoodInput>(list);reverse.Reverse();var name=FreeKitchenRules.ComposeName(list,method.id);
     string expected=method.label+string.Join("、",FreeKitchenRules.Materials.Where(x=>list.Any(y=>y.itemId==x.itemId)).Select(x=>x.name));
     Need(name==expected&&name==FreeKitchenRules.ComposeName(reverse,method.id),"matrix identity "+method.id+"/"+mask);
     Need(list.Any(x=>x.itemId=="dough")||!name.Contains("饼"),"no phantom dough "+name);
     Need(list.Any(x=>x.itemId=="skewer")||!name.Contains("串"),"no phantom skewer "+name);
     Need(!name.Contains("青菜")&&!name.Contains("葱")&&!name.Contains("火腿"),"no phantom modifiers "+name);combinations++;
    }
   }
   Need(combinations==65,"all compatible nonempty material combinations checked");
   Need(FreeKitchenRules.ComposeName(new[]{new FoodInput("egg",1)},"griddle")=="煎鸡蛋","one egg fried naming");
   Need(FreeKitchenRules.ComposeName(new[]{new FoodInput("egg",1)},"stir_fry")=="炒鸡蛋","one egg stir naming");

  }
  static void RealFoods()
  {
   var s=Open(Setup());var p=Cook(s,"griddle","egg");Need(s.LaneDishName(0)=="煎鸡蛋"&&p.DisplayName=="煎鸡蛋"&&p.name=="蛋菜煎饼","actual griddle egg name");var order=Customer(s,p.id);Need(s.OrderDishName(order.id)=="煎鸡蛋","natural order truthful");Need(s.PackToHand(0,"P04"),"actual egg hand pack");int cash=s.CashCents,price=s.HeldMeal.priceCents;Need(s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"actual egg sale");Audit(s);
   var t=Open(Setup());var q=Cook(t,"stir_fry","egg");Need(q.matchedRecipeIndex==8&&q.name=="家常青菜滑蛋"&&q.DisplayName=="炒鸡蛋"&&t.LaneDishName(0)=="炒鸡蛋","matched old recipe has actual egg name");
   var u=Open(Setup());var r=Cook(u,"stir_fry","egg","meat");Need(r.DisplayName=="炒鸡蛋、鲜肉"&&!r.DisplayName.Contains("青菜"),"mixed real ingredients");
  }
  static void HistoricalMatched()
  {
   var s=Open(Setup());var product=Cook(s,"stir_fry","egg");Need(product.matchedRecipeIndex==8,"old matched fixture");var saved=s.CaptureSave();saved.lanes[0].freeBatch.product.name="家常青菜滑蛋";s=Restore(saved);
   Need(s.ProductAtLane(0).name=="家常青菜滑蛋"&&s.LaneDishName(0)=="炒鸡蛋","old product accepted identity preserved readable");
   var order=Customer(s,s.ProductAtLane(0).id);Need(order.productName=="家常青菜滑蛋"&&s.OrderDishName(order.id)=="炒鸡蛋","old natural order truthful");Need(s.PackToHand(0,"P05"),"pack old matched");int cash=s.CashCents,price=s.HeldMeal.priceCents;Need(s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"sell old product once");s=Restore(s.CaptureSave());Need(s.OrderDishName(order.id)=="炒鸡蛋"&&!s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"old sold order restore no duplicate");Audit(s);
  }
  static void HistoricalCrossBatch()
  {
   var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};Need(profile.Validate(out _),"profile valid");var run=new RunState(.25f,profile);var campaign=CampaignState.New(profile,run);var s=Open(Setup(run.CashCents));
   var first=Cook(s,"griddle","egg");string firstId=first.id;Need(s.PackToCounter(0,"P04"),"first packed");var order=Customer(s,firstId);
   var second=Cook(s,"griddle","egg");string secondId=second.id;Need(secondId!=firstId&&s.PackToCounter(0,"P04"),"second distinct batch");string meal2=s.ReadyMeals.Find(x=>x.product.id==secondId).id;
   var save=s.CaptureSave();save.readyMeals.First(x=>x.product.id==firstId).product.name="蛋菜煎饼";var oldOrder=save.orders.First(x=>x.id==order.id);oldOrder.productName="蛋菜煎饼";oldOrder.productInputs=Array.Empty<FoodInput>();oldOrder.productSourcePortions=0;
   s=Restore(save);Need(s.ReadyMeals.First(x=>x.product.id==firstId).product.name=="蛋菜煎饼","old generic product stored identity preserved");Need(s.OrderDishName(order.id)=="煎鸡蛋","old generic order displayed truthfully");
   Need(s.CanServeCounterMeal(meal2,order.id,out _),"old order matches new different product id");int cash=s.CashCents,price=s.ReadyMeals.First(x=>x.id==meal2).priceCents;Need(s.ServeCounterMeal(meal2,order.id)&&s.CashCents==cash+price,"cross batch sale once");
   var served=s.FindOrder(order.id);Need(served.productId==secondId&&served.productName=="蛋菜煎饼"&&served.productPriceCents==price,"legacy stored identity and quote kept");Need(s.OrderDishName(order.id)=="煎鸡蛋","new displayed name after sale");Need(!s.ServeCounterMeal(meal2,order.id)&&s.CashCents==cash+price,"cross batch repeat rejected");Audit(s);
   var sold=Restore(s.CaptureSave());Need(sold.OrderDishName(order.id)=="煎鸡蛋"&&sold.CashCents==cash+price,"old sold snapshot restored");Audit(sold);sold.Close();Need(campaign.CommitDay(run,sold,sold.SessionId),"real day settlement");Need(campaign.lastDay.sales.Any(x=>x.name=="煎鸡蛋"&&x.totalCents==price&&x.id.StartsWith("sale-"))&&campaign.cashCents==cash+price,"correct generic day receipt, neutral icon identity and cash");Need(CommerceReceiptData.Sales(sold).lines.Single().recipeIndex==-1,"generic real-time receipt does not impersonate recipe");Need(!campaign.CommitDay(run,sold,sold.SessionId)&&campaign.cashCents==cash+price,"no double day settlement");var disk=CampaignSaveStore.Decode(CampaignSaveStore.Encode(new CampaignSaveData{campaign=campaign,run=run.CaptureSave()}));Need(disk.campaign.lastDay.sales.Any(x=>x.name=="煎鸡蛋"&&x.totalCents==price),"receipt roundtrip");disk.campaign.CreateNextDay(.25f);Need(disk.campaign.dayIndex==2&&disk.campaign.cashCents==cash+price,"next day cash once");
  }
  static void RiskIdentity()
  {
   var s=Open(Setup());var product=Cook(s,"stir_fry","egg");string id=product.id,name=product.name;int quote=product.priceCents;
   var order=Customer(s,id);Need(s.PackToCounter(0,"P05"),"risk pack");var meal=s.ReadyMeals[0];s.ServeRiskGate=(oid,r,price)=>new StallRiskGateDecision(true,price*2,4.5f);
   int cash=s.CashCents;Need(s.ServeCounterMeal(meal.id,order.id)&&s.CashCents==cash,"risk delivered without imaginary cash");
   Need(order.productId==id&&order.productName==name&&order.productPriceCents==quote&&s.OrderDishName(order.id)=="炒鸡蛋","risk legacy identity and truthful display");
   var restored=Restore(s.CaptureSave());var active=restored.RiskState.ActiveEvent;Need(active!=null&&restored.OrderDishName(order.id)=="炒鸡蛋","risk snapshot and display restored");
   Need(restored.TryCompleteRiskSale(active.Serial,active.CollectCents)&&restored.CashCents==cash+active.CollectCents,"risk recovery credited once");
   Need(!restored.TryCompleteRiskSale(active.Serial,active.CollectCents)&&restored.CashCents==cash+active.CollectCents,"risk recovery cannot repeat");
   var sold=Restore(restored.CaptureSave());var so=sold.FindOrder(order.id);Need(so.productId==id&&so.productName==name&&so.productPriceCents==quote&&sold.OrderDishName(order.id)=="炒鸡蛋","risk settled identity and display restored");Audit(sold);
  }
  static void ReversedInputs()
  {
   var s=Open(Setup());var a=Cook(s,"stir_fry","egg","meat");string aid=a.id,oldA=a.name;int price=a.priceCents;Need(s.PackToCounter(0,"P05"),"forward batch packed");var order=Customer(s,aid);
   var b=Cook(s,"stir_fry","meat","egg");string bid=b.id;Need(bid!=aid&&b.name!=oldA&&b.priceCents==price&&b.DisplayName=="炒鸡蛋、鲜肉","real reverse batch has same inputs and price");Need(s.PackToCounter(0,"P05"),"reverse batch packed");string meal2=s.ReadyMeals.First(x=>x.product.id==bid).id;
   Need(s.CanServeCounterMeal(meal2,order.id,out _),"new order snapshot accepts reversed ingredient order");
   var saved=s.CaptureSave();var legacy=saved.orders.First(x=>x.id==order.id);legacy.productInputs=Array.Empty<FoodInput>();legacy.productSourcePortions=0;s=Restore(saved);
   Need(s.CanServeCounterMeal(meal2,order.id,out _),"old order with source accepts reversed ingredient order");int cash=s.CashCents;
   Need(s.ServeCounterMeal(meal2,order.id)&&s.CashCents==cash+price,"reverse batch actual sale paid once");Need(!s.ServeCounterMeal(meal2,order.id)&&s.CashCents==cash+price,"reverse batch repeat rejected");
   var sold=Restore(s.CaptureSave());Need(sold.OrderDishName(order.id)=="炒鸡蛋、鲜肉"&&sold.CashCents==cash+price,"reverse batch readable name and payment persist");Audit(sold);
  }
  static void HistoricalMissingSource()
  {
   var s=Open(Setup());var p=Cook(s,"griddle","egg");var order=Customer(s,p.id);
   var save=s.CaptureSave();var old=save.orders.First(x=>x.id==order.id);old.productId="legacy-source-sold-out";old.productInputs=Array.Empty<FoodInput>();old.productSourcePortions=0;
   s=Restore(save);Need(s.OrderDishName(order.id)=="蛋菜煎饼","old unknown source keeps old display name");
   Need(s.PackToHand(0,"P04"),"replacement batch packed");int cash=s.CashCents,price=s.HeldMeal.priceCents;
   Need(s.CanServeHeldMeal(order.id,out _)&&s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"old missing source accepts exact legacy identity once");
   Need(!s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"old missing source no repeated payment");
   s=Restore(s.CaptureSave());Need(s.OrderDishName(order.id)=="煎鸡蛋"&&s.CashCents==cash+price,"known sale snapshot survives restore");Audit(s);
  }
  static void Invalid()
  {
   var s=Open(Setup());Cook(s,"griddle","egg");var bad=s.CaptureSave();bad.lanes[0].freeBatch.product.name="豪华龙虾";bool rejected=false;try{Restore(bad);}catch(ArgumentException){rejected=true;}Need(rejected,"fabricated old product name rejected");
   var product=s.ProductAtLane(0);var fake=new StallOrder{productId="different",productName="鲜肉煎饼",productMethod="griddle",productPriceCents=product.priceCents,recipeIndex=2};Need(!s.SameOrderProduct(fake,product),"old different ingredient cannot match");fake.productName="蛋菜煎饼";fake.productMethod="fry";fake.productInputs=new[]{new FoodInput("egg",1)};fake.productSourcePortions=1;Need(!s.SameOrderProduct(fake,product),"known snapshot different method cannot match");
   fake.productMethod="griddle";fake.productInputs=Array.Empty<FoodInput>();fake.productSourcePortions=0;s.Orders.Add(fake);Need(s.SameOrderProduct(fake,product),"unknown old source preserves exact name and price fallback");Need(s.OrderDishName(fake.id)=="蛋菜煎饼","unknown old order retains historical name");
   var a=new[]{new FoodInput("rice",4),new FoodInput("egg",1)};var b=new[]{new FoodInput("rice",1),new FoodInput("egg",3)};
   int priceA=FreeKitchenRules.FairPriceCents(a,1,-1),priceB=FreeKitchenRules.FairPriceCents(b,1,-1);
   Need(priceA==priceB&&FreeKitchenRules.LegacyComposeName(a,"stir_fry")==FreeKitchenRules.LegacyComposeName(b,"stir_fry"),"same name and price different ratios fixture");
   var ratioOrder=new StallOrder{productId="oldA",productName=FreeKitchenRules.LegacyComposeName(a,"stir_fry"),productMethod="stir_fry",productPriceCents=priceA,productInputs=a,productSourcePortions=1};
   var wrongProduct=new FoodProduct{id="other",name=ratioOrder.productName,method="stir_fry",priceCents=priceB,inputs=b,sourcePortions=1};
   Need(s.SameOrderProduct(ratioOrder,wrongProduct),"same ingredient kinds and price allow different ratios");
  }
  static void SamePriceDifferentRatio()
  {
   var setup=Setup();setup.equipment.Remove("V02");setup.equipment.Add("V03");setup.inventory["rice"]=8;setup.inventory["egg"]=8;
   var s=Open(setup);var a=Cook(s,"stir_fry","rice","rice","rice","rice","egg");string first=a.id;int quote=a.priceCents;Need(s.PackToCounter(0,"P05"),"ratio first packed");var order=Customer(s,first);
   var b=Cook(s,"stir_fry","rice","egg","egg","egg");Need(b.priceCents==quote&&a.DisplayName==b.DisplayName,"real ratios have same price and name");string second=b.id;Need(s.PackToCounter(0,"P05"),"ratio second packed");var meal=s.ReadyMeals.First(x=>x.product.id==second);
   Need(s.CanServeCounterMeal(meal.id,order.id,out _),"same kinds same price accepts different actual ratio");int cash=s.CashCents;
   Need(s.ServeCounterMeal(meal.id,order.id)&&s.CashCents==cash+quote,"different ratio real customer pays once");Need(!s.ServeCounterMeal(meal.id,order.id)&&s.CashCents==cash+quote,"different ratio cannot sell twice");
   var restored=Restore(s.CaptureSave());Need(restored.CashCents==cash+quote&&restored.FindOrder(order.id).served,"different ratio settlement restored");Audit(restored);
  }
  static void DifferentIngredients()
  {
   var setup=Setup();setup.equipment.Remove("V02");setup.equipment.Add("V03");setup.inventory["rice"]=6;setup.inventory["egg"]=6;
   var s=Open(setup);var a=Cook(s,"stir_fry","egg","egg","meat");int quote=a.priceCents;string aid=a.id;Need(s.PackToCounter(0,"P05"),"different kind first packed");var order=Customer(s,aid);
   var b=Cook(s,"stir_fry","rice","rice","rice","meat");Need(b.priceCents==quote,"different ingredients same price fixture");string bid=b.id;Need(s.PackToCounter(0,"P05"),"different kind second packed");var meal=s.ReadyMeals.First(x=>x.product.id==bid);int cash=s.CashCents;
   Need(!s.CanServeCounterMeal(meal.id,order.id,out _)&&!s.ServeCounterMeal(meal.id,order.id)&&s.CashCents==cash&&!order.served,"different real ingredient kinds cannot satisfy order");Audit(s);
  }
  static void MatchedReceipts()
  {
   var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};var run=new RunState(.25f,profile);var campaign=CampaignState.New(profile,run);var s=Open(Setup(run.CashCents));
   var product=Cook(s,"stir_fry","egg");int price=product.priceCents;var order=Customer(s,product.id);Need(s.PackToHand(0,"P05"),"matched receipt packaged");int cash=s.CashCents;Need(s.ServeHeldMeal(order.id)&&s.CashCents==cash+price,"matched receipt actual sale");
   var receipt=CommerceReceiptData.Sales(s);Need(receipt.totalsMatch&&receipt.totalCents==price&&receipt.lines.Single().recipeIndex==8&&receipt.lines.Single().name=="炒鸡蛋","matched real-time receipt retains true recipe identity and amount");
   s.Close();Need(campaign.CommitDay(run,s,s.SessionId),"matched day committed");var row=campaign.lastDay.sales.Single();Need(row.id=="egg_greens"&&row.name=="炒鸡蛋"&&row.totalCents==price&&campaign.cashCents==cash+price,"matched nightly ledger icon id and cash");
   var disk=CampaignSaveStore.Decode(CampaignSaveStore.Encode(new CampaignSaveData{campaign=campaign,run=run.CaptureSave()}));Need(disk.campaign.lastDay.sales.Single().id=="egg_greens"&&disk.campaign.lastDay.sales.Single().name=="炒鸡蛋"&&disk.campaign.cashCents==cash+price,"matched receipt and cash persist");
   Need(!disk.campaign.CommitDay(run,s,s.SessionId)&&disk.campaign.cashCents==cash+price,"matched day cannot be committed twice");
   Need(BusinessSession.VerifiedOrderRecipeIndex(new StallOrder{recipeIndex=8,productId="old",productName="家常青菜滑蛋",productInputs=Array.Empty<FoodInput>()})==8,"old matched order keeps old recipe identification");
   Need(BusinessSession.VerifiedOrderRecipeIndex(new StallOrder{recipeIndex=2,productId="old",productName="蛋菜煎饼",productInputs=Array.Empty<FoodInput>()})==-1,"old generic order cannot pretend to be recipe");
  }
  static void LostProductGuide()
  {
   var setup=Setup();setup.inventory["egg"]=1;var s=Open(setup);var product=Cook(s,"stir_fry","egg");var original=Customer(s,product.id);Need(s.PackToHand(0,"P05"),"guide fixture packed");Need(s.ServeHeldMeal(original.id),"guide fixture source sold");
   var pending=new StallOrder{id=original.id+10000,productId=original.productId,productName=original.productName,productMethod=original.productMethod,productPriceCents=original.productPriceCents,recipeIndex=original.recipeIndex,productInputs=new[]{new FoodInput("egg",1)},productSourcePortions=1};s.Orders.Add(pending);
   var before=JsonUtility.ToJson(s.CaptureSave());var guide=OrderCookingGuide.Read(s,pending.id);var after=JsonUtility.ToJson(s.CaptureSave());
   Need(guide.hasExactInputs&&guide.ingredients.Count==1&&guide.ingredients[0].id=="egg"&&guide.ingredients[0].available==0&&guide.next.Contains("缺"),"missing source shows actual snapshot and shortage");Need(before==after,"replenishment guide is read-only");
   var old=new StallOrder{id=pending.id+1,productId=pending.productId,productName=pending.productName,productMethod=pending.productMethod,productPriceCents=pending.productPriceCents,recipeIndex=pending.recipeIndex};s.Orders.Add(old);var oldGuide=OrderCookingGuide.Read(s,old.id);
   Need(!oldGuide.hasExactInputs&&oldGuide.next.Contains("原批次已用完"),"old snapshotless lost source keeps original guidance");
  }
  public static void Run(){var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--naming24-output");string path=args[at+1];try{Matrix();RealFoods();HistoricalMatched();HistoricalCrossBatch();ReversedInputs();HistoricalMissingSource();RiskIdentity();Invalid();SamePriceDifferentRatio();DifferentIngredients();MatchedReceipts();LostProductGuide();}catch(Exception e){errors.Add(e.ToString());}var r=new Report{success=errors.Count==0,checks=passed.Count,combinations=combinations,passed=passed.ToArray(),errors=errors.ToArray()};File.WriteAllText(path,JsonUtility.ToJson(r,true));EditorApplication.Exit(r.success?0:1);}
 }
}
