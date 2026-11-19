using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Production transactions and serialization only. Scene reachability and
    // three-second opening feedback require the separate visual playthrough.
    public static class OnlineProcurementValidation
    {
        [Serializable] public sealed class Check { public string name,detail;public bool passed; }
        [Serializable] public sealed class Report
        {public bool success;public string scope="Isolated production procurement transactions and save boundaries; no physical/UI claim";public Check[] checks;}
        sealed class Context {public RunState run;public CampaignState campaign;}
        static readonly List<Check> checks=new List<Check>();
        public static void Run()
        {
            checks.Clear();
            Case("authored-prices-and-new-retail",Prices);
            Case("night-debit-and-json-aliases",NightDebit);
            Case("due-date-capacity-double-claim",ClaimBoundaries);
            Case("two-identical-packages-and-day-settlement",MultiplePackages);
            Case("tampered-orders-and-legacy-save",SaveValidation);
            Case("checkout-event-once-after-payment",CheckoutEvent);
            bool success=checks.All(x=>x.passed);
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--online-procurement-output");
            string path=Path.GetFullPath(i>=0&&i+1<args.Length?args[i+1]:Path.Combine(Application.dataPath,"../../art-v15/qa/online-procurement.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(new Report{success=success,checks=checks.ToArray()},true));
            Debug.Log((success?"ONLINE_PROCUREMENT_PASS ":"ONLINE_PROCUREMENT_FAIL ")+path);
            if(Application.isBatchMode)EditorApplication.Exit(success?0:1);
        }
        static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static void Case(string name,Func<string> body)
        {try{checks.Add(new Check{name=name,passed=true,detail=body()});}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.ToString()});}}
        static RunProfile Profile(string career="career6")=>new RunProfile{careerId=career,selectedTraits=Array.Empty<string>()};
        static Context Night()
        {
            var p=Profile();Need(p.Validate(out var reason),reason);
            var save=new RunState(.25f,p).CaptureSave();save.homeEquipment=save.loadedEquipment=Array.Empty<string>();save.supplies=Array.Empty<RunStockSave>();save.gas=save.cartridge=save.coal=save.electricity=0;
            var run=RunState.Restore(save);run.Paused=false;var campaign=CampaignState.New(p,run);
            var service=new BusinessSession(ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run));
            service.Close();Need(campaign.CommitDay(run,service,service.SessionId),"fixture day settle");
            run=RunState.FromCampaign(campaign,.25f);run.SetBusinessPhase(GamePhase.Night);run.Paused=true;
            return new Context{run=run,campaign=campaign};
        }
        static string Facts(Context c)
        {var save=c.run.CaptureSave();save.message="";return JsonUtility.ToJson(save)+JsonUtility.ToJson(c.campaign);}
        static Context Roundtrip(Context c)
        {
            var data=CampaignSaveStore.Decode(CampaignSaveStore.Encode(new CampaignSaveData{campaign=c.campaign,run=c.run.CaptureSave()}));
            return new Context{campaign=data.campaign,run=RunState.Restore(data.run)};
        }
        static string Buy(Context c,string id,int expected)
        {
            int price=OnlineCatalog.QuoteCents(c.run.Profile,id);Need(price==expected,"price "+id+": "+price+" != "+expected);
            Need(c.run.TryPlaceOnlineOrder(c.campaign,id,price,out string reason),reason);
            return c.campaign.onlineOrders[c.campaign.onlineOrders.Count-1].id;
        }
        static string Prices()
        {
            var p=Profile();Need(OnlineCatalog.QuoteCents(p,"B03")==12000,"B03 online120");Need(OnlineCatalog.QuoteCents(p,"V02")==10500,"V02 online105");
            Need(OnlineCatalog.QuoteCents(p,"P02")==960,"P02 12*0.8");Need(OnlineCatalog.QuoteCents(Profile("career3"),"P02")==845,"P02 career3 round 12*0.8*0.88");
            foreach(string id in new[]{"V02","U01","U02"})Need(SupplyCatalog.Items.Any(x=>x.id==id),"missing ordinary retail "+id);
            Need(SupplyCatalog.Find("V02").priceCents==14000&&SupplyCatalog.Find("U01").priceCents==500&&SupplyCatalog.Find("U02").priceCents==3500,"retail authored prices");
            Need(GameCatalog.Find("rice").priceCents==1350&&GameCatalog.Find("seasoning").priceCents==2250,"bulk 0.9 repriced");
            var c=Night();c.run.SetBusinessPhase(GamePhase.Market);Need(c.run.AddSupply("U01"),"new retail U01 still blocked");Need(c.run.CashCents==c.campaign.cashCents&&!c.run.Paid,"retail add paid/created money");
            return "Authored retail/online/package/career prices, half-cent rounding; U01 enters unpaid basket without debit.";
        }
        static string NightDebit()
        {
            var c=Night();int start=c.run.CashCents;string before=Facts(c);
            Need(!c.run.TryPlaceOnlineOrder(c.campaign,"B03",11999,out _)&&Facts(c)==before,"stale quote changed facts");
            Buy(c,"B03",12000);Need(c.run.CashCents==start-12000&&c.campaign.cashCents==c.run.CashCents,"debit once");
            Need(c.run.SpentCents==0&&c.run.Cargo.Count==0&&!c.run.LoadedEquipment.Contains("B03")&&!c.campaign.equipment.Any(x=>x.id=="B03"),"overnight inventory grant");
            before=Facts(c);Need(!c.run.TryPlaceOnlineOrder(c.campaign,"B03",12000,out _)&&Facts(c)==before,"durable duplicate order");
            c=Roundtrip(c);Buy(c,"P02",960);
            Need(c.campaign.lastDay.onlinePurchasesCents==12960&&c.campaign.history[0].onlinePurchasesCents==12960,"JSON-separated ledgers diverged or double-added");
            Need(c.campaign.lastDay.closingCashCents==start-12960&&c.campaign.history[0].closingCashCents==start-12960,"ledger closing cash");
            return "Stale quote and duplicate durable are atomic; night purchases debit once with no usable goods; JSON ledger aliases remain equal.";
        }
        static string ClaimBoundaries()
        {
            var c=Night();string id=Buy(c,"B03",12000);int cash=c.run.CashCents;string before=Facts(c);
            Need(!c.run.TryClaimOnlineOrder(c.campaign,id,out _)&&Facts(c)==before,"night delivered early");
            c.run.SetBusinessPhase(GamePhase.Home);before=Facts(c);
            Need(!c.run.TryClaimOnlineOrder(c.campaign,id,out _)&&Facts(c)==before,"future parcel delivered before due day");
            c.run=c.campaign.CreateNextDay(.25f);c.run.LoadValidator=(gear,cargo)=>"载重不足";before=Facts(c);
            Need(!c.run.TryClaimOnlineOrder(c.campaign,id,out _)&&Facts(c)==before,"failed load swallowed parcel");
            c=Roundtrip(c);c.run.Paused=false;c.run.LoadValidator=(gear,cargo)=>null;
            Need(c.run.TryClaimOnlineOrder(c.campaign,id,out var reason),reason);Need(c.run.LoadedEquipment.Contains("B03")&&c.campaign.equipment.Any(x=>x.id=="B03"&&x.condition==100),"claimed durable not usable");
            Need(c.run.CashCents==cash&&c.run.SpentCents==0,"claim charged again");before=Facts(c);
            Need(!c.run.TryClaimOnlineOrder(c.campaign,id,out _)&&Facts(c)==before,"double claim mutated facts");
            c=Roundtrip(c);c.run.Paused=false;before=Facts(c);Need(!c.run.TryClaimOnlineOrder(c.campaign,id,out _)&&Facts(c)==before,"reload revived parcel");
            return "No night receipt; full vehicle retains paid parcel through save; next-day durable delivered once, no second charge or reload revival.";
        }
        static string MultiplePackages()
        {
            var c=Night();string a=Buy(c,"P02",960),b=Buy(c,"P02",960),s=Buy(c,"seasoning",2500);int cash=c.run.CashCents;
            c.run=c.campaign.CreateNextDay(.25f);c.run.LoadValidator=(gear,cargo)=>null;
            Need(c.run.TryClaimOnlineOrder(c.campaign,a,out var reason),reason);Need(c.run.TryClaimOnlineOrder(c.campaign,b,out reason),reason);Need(c.run.TryClaimOnlineOrder(c.campaign,s,out reason),reason);
            var setup=ProcurementPlanCheck.Project(c.run,includeBasket:false).ToSetup(c.run);
            Need(setup.inventory.TryGetValue("P02",out int boxes)&&boxes==100,"identical packs collapsed by cargo mapper");Need(setup.inventory.TryGetValue("seasoning",out int salt)&&salt==50,"seasoning quantity");
            var service=new BusinessSession(setup);service.Close();Need(c.campaign.CommitDay(c.run,service,service.SessionId),"day2 settle");
            Need(c.campaign.lastDay.purchasesCents==0&&c.campaign.lastDay.onlinePurchasesCents==0&&c.campaign.cashCents==cash,"delivery double-counted as day2 purchase");
            Need(!c.campaign.CommitDay(c.run,service,service.SessionId),"duplicate settlement");
            var next=c.campaign.CreateNextDay(.25f);Need(next.Supplies["P02"]==100&&next.Supplies["seasoning"]==50,"cross-day stock missing/doubled");
            return "Two separately paid identical packages yield 100 usable boxes; 50 seasoning portions persist; delivery and repeated settlement never duplicate cost or quantity.";
        }
        static string SaveValidation()
        {
            var c=Night();string id=Buy(c,"P02",960);c=Roundtrip(c);
            c.campaign.onlineOrders[0].dueDay=1;bool rejected=false;try{Roundtrip(c);}catch{rejected=true;}Need(rejected,"invalid delivery date accepted");
            c=Night();
            var legacy=JsonUtility.FromJson<CampaignSaveEnvelope>(CampaignSaveStore.Encode(new CampaignSaveData{campaign=c.campaign,run=c.run.CaptureSave()}));
            legacy.payload=legacy.payload.Replace(",\"onlineOrders\":[]","").Replace(",\"onlinePurchasesCents\":0","");
            Need(!legacy.payload.Contains("\"onlineOrders\":")&&!legacy.payload.Contains("\"onlinePurchasesCents\":"),"legacy fixture still contains online fields");
            // Preserve the real disk boundary: only Decode normalizes Unity's
            // inline null BusinessSaveData using the persisted hasBusiness flag.
            using(var hash=System.Security.Cryptography.SHA256.Create())
                legacy.sha256=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(legacy.payload))).Replace("-","").ToLowerInvariant();
            var restored=CampaignSaveStore.Decode(JsonUtility.ToJson(legacy));
            Need(restored.run.phase==GamePhase.Night&&!restored.hasBusiness&&restored.business==null,"legacy night restored a phantom business");
            Need(restored.campaign.cashCents==c.campaign.cashCents&&restored.run.cashCents==c.run.CashCents&&restored.campaign.onlineOrders.Count==0&&restored.campaign.lastDay.onlinePurchasesCents==0,"legacy field absence changed cash or created orders");
            return "Invalid due dates rejected; pre-online field absence restores with unchanged cash and no orders.";
        }
        static string CheckoutEvent()
        {
            var c=Night();c.run.SetBusinessPhase(GamePhase.Market);int calls=0,amount=0,cash=c.run.CashCents;bool committedAtEvent=false;
            c.run.CheckoutCompleted+=value=>{throw new InvalidOperationException("Declared subscriber failure fixture");};
            c.run.CheckoutCompleted+=value=>{calls++;amount=value;committedAtEvent=c.run.Paid&&c.run.CashCents==cash-value;};
            Need(c.run.AddSupply("rice_small")&&c.run.AddSupply("seasoning_small"),"checkout fixture basket");
            Need(c.run.Act("Cashier",true),"checkout fixture cashier");c.run.Tick(1);c.run.Tick(1);
            Need(calls==1&&amount==1000&&c.run.SpentCents==1000&&committedAtEvent,"checkout observer repeated, preceded or interrupted payment");
            var restored=RunState.Restore(c.run.CaptureSave());restored.Paused=false;restored.Tick(1);
            Need(restored.Paid&&restored.SpentCents==1000&&calls==1,"read save replayed receipt");
            return "One normal checkout notification after cash commit; failing observer isolated; no repeat tick or save-read replay.";
        }
    }
}
