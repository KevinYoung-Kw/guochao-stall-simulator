using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Guochao.Editor
{
 public static class EmergencyBag24Validation
 {
  [Serializable] class Report { public bool success;public string[] passed,errors;public string scope="Unity model transaction tests; no physical actor/UI/browser claim."; }
  static readonly List<string> passed=new List<string>(),errors=new List<string>();
  static void Need(bool v,string name){if(!v)throw new Exception(name);passed.Add(name);}
  static BusinessSetup Setup(int packs=0)=>new BusinessSetup { cashCents=5000,seed=12017,dayDuration=300,gasPoints=8,stoveLoaded=true,queueCapacity=4,customerIntervalSeconds=.5f,patienceMinSeconds=90,patienceMaxSeconds=90,equipment=new HashSet<string>{"B04","V02","U02"},inventory=new Dictionary<string,int>{{"egg",3},{"seasoning",3},{"P05",packs}} };
  static BusinessSession Open(BusinessSetup setup){var s=new BusinessSession(setup);s.SelectedRecipes.Clear();Need(s.Open(),"open");return s;}
  static FoodProduct Cook(BusinessSession s){Need(s.AddFreeIngredient(0,"egg"),"egg inserted");Need(s.SetFreeMethod(0,"stir_fry")&&s.StartFreeCooking(0),"start egg");for(int i=0;i<100&&s.Lanes[0].step==KitchenStep.Heating;i++)s.Tick(.1f);Need(s.Lanes[0].step==KitchenStep.Utensil&&s.Action(0),"real cooked");if(s.Lanes[0].step==KitchenStep.Season)Need(s.Action(0),"real seasoning");Need(s.Lanes[0].step==KitchenStep.Pack&&s.ProductAtLane(0)!=null,"real product");return s.ProductAtLane(0);}
  static StallOrder Customer(BusinessSession s,string productId){for(int i=0;i<200;i++){var order=s.Orders.Find(o=>o.productId==productId&&!o.departed&&!o.served);if(order!=null){if(!order.arrived)Need(s.MarkCustomerArrived(order.id),"arrived");return order;}s.Tick(.2f);}throw new Exception("no natural customer for product "+productId);}
  static BusinessSession Roundtrip(BusinessSession s)=>BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(s.CaptureSave())));
  static void Audit(BusinessSession s){var a=s.CaptureAudit();Need(a.cashConserved&&a.packagesConserved&&a.nonNegativeInventory&&a.nonNegativeEnergy,"ledgers conserved");foreach(var r in a.recipeProduction)Need(r.portionsConserved,"portions "+r.recipe);}
  static void Direct()
  {
   var setup=Setup();setup.inventory["P04"]=1;var s=Open(setup);var p=Cook(s);var o=Customer(s,p.id);Need(s.AvailableLanePackages(0).Contains(BusinessSession.EmergencyBagId),"no stock -> emergency choice");Need(s.CanPackToHand(0,BusinessSession.EmergencyBagId,out _)&&s.PackToHand(0,BusinessSession.EmergencyBagId),"reserve packing to hand");Need(!s.Inventory.ContainsKey(BusinessSession.EmergencyBagId)&&s.Count("P05")==0&&s.Count("P04")==1&&s.HeldMeal.packageId==BusinessSession.EmergencyBagId,"no free normal stock");Audit(s);s=Roundtrip(s);Need(s.HeldMeal!=null&&s.HeldMeal.packageId==BusinessSession.EmergencyBagId,"held persists");int before=s.CashCents,price=s.HeldMeal.priceCents;Need(s.ServeHeldMeal(o.id),"actual direct sale");Need(s.CashCents==before+price&&s.RevenueCents==price,"cash once");Need(s.EmergencyPackagesSold==1&&s.EmergencyPackagingScore==-5,"score exact");Need(!s.ServeHeldMeal(o.id)&&s.CashCents==before+price,"sale cannot repeat");Audit(s);s=Roundtrip(s);Need(s.EmergencyPackagesSold==1&&s.EmergencyPackagingScore==-5&&s.Orders.Find(x=>x.id==o.id).emergencyPackage,"sale facts persist");Audit(s);
  }
  static void Counter()
  {
   var s=Open(Setup());var p=Cook(s);var o=Customer(s,p.id);Need(s.PackToCounter(0,BusinessSession.EmergencyBagId),"reserve packing to counter");s=Roundtrip(s);Need(s.ReadyMeals.Count==1&&s.ReadyMeals[0].packageId==BusinessSession.EmergencyBagId,"counter persists");var id=s.ReadyMeals[0].id;Need(s.ServeCounterMeal(id,o.id),"actual counter sale");Need(s.EmergencyPackagesSold==1&&s.EmergencyPackagingScore==-5,"counter score once");Audit(s);
  }
  static void Normal()
  {
   var s=Open(Setup(1));Cook(s);Need(!s.CanPackToHand(0,BusinessSession.EmergencyBagId,out _),"emergency denied with physical pack");Need(!s.AvailableLanePackages(0).Contains(BusinessSession.EmergencyBagId),"normal priority");Need(s.PackToHand(0,"P05")&&s.Count("P05")==0,"normal consumed once");Need(s.HeldMeal.packageId=="P05"&&s.EmergencyPackagingScore==0,"normal not penalized");Audit(s);s=Roundtrip(s);Need(s.HeldMeal.packageId=="P05","normal old compatible");Audit(s);
   var o=Customer(s,s.HeldMeal.product.id);Need(s.ServeHeldMeal(o.id),"normal actual sale");
   string legacy=JsonUtility.ToJson(s.CaptureSave()).Replace("\"emergencyPackage\":false,","");
   Need(!legacy.Contains("\"emergencyPackage\""),"simulate pre-emergency saved order");
   s=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(legacy));Need(s.EmergencyPackagesSold==0&&s.EmergencyPackagingScore==0,"genuine old save restored");Audit(s);
   Cook(s);Need(s.CanPackToHand(0,BusinessSession.EmergencyBagId,out _),"fallback starts after last normal pack");
  }
  static void Campaign()
  {
   var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};Need(profile.Validate(out _),"profile valid");var run=new RunState(.25f,profile);var campaign=CampaignState.New(profile,run);var setup=Setup();setup.cashCents=run.CashCents;var s=Open(setup);var p=Cook(s);var o=Customer(s,p.id);Need(s.PackToHand(0,BusinessSession.EmergencyBagId)&&s.ServeHeldMeal(o.id),"campaign sale");s.Close();Need(campaign.CommitDay(run,s,s.SessionId),"campaign commit");Need(campaign.lastDay.emergencyPackagesUsed==1&&campaign.lastDay.packagingScoreDelta==-5&&campaign.packagingScoreTotal==-5,"daily score committed");Need(!campaign.CommitDay(run,s,s.SessionId)&&campaign.packagingScoreTotal==-5,"day commit once");var json=CampaignSaveStore.Encode(new CampaignSaveData{campaign=campaign,run=run.CaptureSave()});var decoded=CampaignSaveStore.Decode(json);Need(decoded.campaign.packagingScoreTotal==-5,"campaign save score roundtrip");var next=decoded.campaign.CreateNextDay(.25f);Need(decoded.campaign.dayIndex==2&&decoded.campaign.packagingScoreTotal==-5&&!next.Supplies.ContainsKey(BusinessSession.EmergencyBagId),"next day score survives no free stock");
  }
  public static void Run(){string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--emergency24-output");string path=args[at+1];try{Direct();Counter();Normal();Campaign();}catch(Exception e){errors.Add(e.ToString());}var report=new Report{success=errors.Count==0,passed=passed.ToArray(),errors=errors.ToArray()};File.WriteAllText(path,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);}
 }
}
