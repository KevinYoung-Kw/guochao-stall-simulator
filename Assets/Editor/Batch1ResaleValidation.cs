using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Isolated model verification, not physical gameplay or UI confirmation.
    // Ownership fixtures use the public save/restore boundary; all observed
    // payment, resale, cooking, settlement and disk operations are production.
    public static class Batch1ResaleValidation
    {
        [Serializable] public sealed class Check
        { public string id,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool success,complete,fixture=true;
            public string scope="Production RunState / CampaignState model transactions; no UI, collision, travel or handover timing claim";
            public Check[] checks; public string[] errors,pending;
        }
        sealed class Context { public RunState run; public CampaignState campaign; }
        static readonly List<Check> checks=new List<Check>();
        static readonly List<string> errors=new List<string>();
        static readonly List<string> pending=new List<string>();
        static GameObject mapperObject; static GameFlow mapper;
        static string output,saveRoot;
        const float Step=1f/60f;

        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--batch1-resale-output");
            output=Path.GetFullPath(at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.dataPath,"../../art-v14/claude-batch-1/resale/batch1-resale.json"));
            saveRoot=Path.Combine(Path.GetDirectoryName(output),"resale-fixtures-"+Guid.NewGuid().ToString("N"));
            checks.Clear();errors.Clear();pending.Clear();
            string previousDirectory=CampaignSaveStore.TestDirectory;
            Application.logMessageReceived+=CaptureError;
            try
            {
                mapperObject=new GameObject("Batch1ResaleProductionMapper");mapperObject.SetActive(false);
                mapper=mapperObject.AddComponent<GameFlow>();
                Case("quote-authored-examples",Quotes);
                Case("eligibility-and-rejections",Eligibility);
                Case("paid-basket-cancel-confirm-disk",PaidBasketAndDisk);
                Case("paid-cargo-sale",PaidCargo);
                Case("ownership-aliases-energy",EnergyAndAliases);
                Case("legacy-paid-load-home",LegacyPaidHome);
                Case("integrity-two-days-once",IntegrityDays);
                Case("two-yuan-recovery-to-next-day",TwoYuanRecovery);
                Case("used-wood-spatula-no-arbitrage",UsedSpatula);
                pending.Add("Physical/UI confirmation, cancel button, counter range, and handover duration are outside this isolated model suite.");
            }
            catch(Exception e){errors.Add(e.ToString());}
            finally
            {
                CampaignSaveStore.TestDirectory=previousDirectory;
                if(mapperObject!=null)UnityEngine.Object.DestroyImmediate(mapperObject);
                mapperObject=null;mapper=null;Application.logMessageReceived-=CaptureError;
            }
            bool success=checks.Count==9&&checks.All(x=>x.passed)&&errors.Count==0;
            var report=new Report{success=success,complete=success&&pending.Count==0,checks=checks.ToArray(),errors=errors.ToArray(),pending=pending.ToArray()};
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log((success?"BATCH1_RESALE_MODEL_PASS ":"BATCH1_RESALE_MODEL_FAIL ")+output);
            if(Application.isBatchMode)EditorApplication.Exit(success?0:1);
        }
        static void CaptureError(string text,string stack,LogType type)
        {if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(text+"\n"+stack);}
        static void Need(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
        static void Case(string id,Func<string> body)
        {try{checks.Add(new Check{id=id,passed=true,detail=body()});}catch(Exception e){checks.Add(new Check{id=id,passed=false,detail=e.ToString()});}}
        static RunProfile Profile(string career="career6",params string[] traits)
        {
            var p=new RunProfile{careerId=career,selectedTraits=traits??Array.Empty<string>()};
            if(p.Balance<0)p.selectedTraits=p.selectedTraits.Concat(new[]{"迟钝学者"}).ToArray();
            Need(p.Validate(out string reason),"invalid test profile: "+reason);return p;
        }
        static Context Fixture(RunProfile profile,string[] gear,int gas=0,int cartridge=0,int electric=0,RunStockSave[] stock=null)
        {
            // Explicit ownership fixture. Career starting cash is never raised.
            var d=new RunState(.25f,profile).CaptureSave();d.homeEquipment=Array.Empty<string>();
            d.loadedEquipment=gear;d.supplies=stock??Array.Empty<RunStockSave>();d.gas=gas;d.cartridge=cartridge;d.electricity=electric;d.coal=0;
            var run=RunState.Restore(d);run.Paused=false;
            var campaign=CampaignState.New(profile,run);Need(run.Act("HomeExit",true),"market entry refused");
            return new Context{run=run,campaign=campaign};
        }
        static BusinessSetup Setup(RunState run)
        {typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mapper,run);return mapper.BuildBusinessSetup();}
        static void Pay(RunState run)
        {
            int cash=run.CashCents,spent=run.SpentCents,total=run.CartTotalCents;
            Need(run.Act("Cashier",true),"cashier refused: "+run.Message);run.Tick(run.CheckoutDuration+.001f);
            Need(run.Paid&&run.CashCents==cash-total&&run.SpentCents==spent+total,"payment did not debit exactly once");
        }
        static int Quote(Context c,string id,int expected)
        {var q=RecoveryRules.Quote(c.run,c.campaign,id);Need(q!=null&&q.priceCents==expected,"quote "+id+" expected "+expected+", got "+(q==null?"null":q.priceCents.ToString()));return q.priceCents;}
        static void Sell(Context c,string id,int expected)
        {Need(c.run.TrySellEquipment(c.campaign,id,Quote(c,id,expected)),"sale refused: "+c.run.Message);}
        static string Facts(Context c)
        {var d=c.run.CaptureSave();d.message="";return JsonUtility.ToJson(d)+JsonUtility.ToJson(c.campaign);}
        static Context Disk(Context c,string slot,BusinessSession service=null)
        {
            CampaignSaveStore.TestDirectory=Path.Combine(saveRoot,slot);
            Need(CampaignSaveStore.Save(new CampaignSaveData{campaign=c.campaign,run=c.run.CaptureSave(),business=service?.CaptureSave()},out string saved),saved);
            Need(CampaignSaveStore.Load(out var data,out string loaded),loaded);
            var run=RunState.Restore(data.run);Need(run.Paused,"restored run must await resume");run.Paused=false;
            return new Context{run=run,campaign=data.campaign};
        }
        static string Quotes()
        {
            void Example(RunProfile p,string id,float condition,int expected)
            {var c=Fixture(p,new[]{id});c.campaign.equipment.Find(x=>x.id==id).condition=condition;string before=Facts(c);Quote(c,id,expected);Need(Facts(c)==before,"quotation mutated ownership/cash");}
            // Independently calculated cents from authored base prices, not a
            // second call to the formula under test. Half cents round away.
            Example(Profile(),"V05",100,4950);
            Example(Profile(),"V05",50,2475);
            Example(Profile(),"V01",50,788);
            Example(Profile("career1"),"V05",100,5024);
            Example(Profile("career2"),"V05",100,5099);
            Example(Profile("career1","嘴甜"),"V05",100,5099);
            Example(Profile("career1","科技与狠活大师"),"B07",100,14251);
            Example(Profile("career1","嘴甜","科技与狠活大师"),"B08",100,77868);
            Example(Profile("career1","嘴甜","科技与狠活大师"),"V05",100,5099);
            var invalid=Fixture(Profile(),new[]{"V05"});
            foreach(float condition in new[]{0f,-1f,101f,float.NaN,float.PositiveInfinity})
            {invalid.campaign.equipment[0].condition=condition;Need(RecoveryRules.Quote(invalid.run,invalid.campaign,"V05")==null,"invalid/no-value condition quoted: "+condition);}
            return "9 authored-price examples: 45%, 50% condition, 0/1/2 haggling, sweet talk, microwave-only ×1.2 and half-cent rounding; read-only quotation; invalid conditions rejected";
        }
        static string Eligibility()
        {
            var c=Fixture(Profile("career1"),new[]{"U01","E01"},stock:new[]{new RunStockSave{id="rice",quantity=5},new RunStockSave{id="P02",quantity=50}});
            Need(c.run.AddSupply("V01"),"unpaid fixture add");
            foreach(string id in new[]{"V01","rice","P02","E01","E02","unknown"})
            {string before=Facts(c);Need(RecoveryRules.Quote(c.run,c.campaign,id)==null,"non-saleable item quoted: "+id);Need(!c.run.TrySellEquipment(c.campaign,id,1),"non-saleable item sold: "+id);Need(Facts(c)==before,"rejected item changed authoritative facts: "+id);}
            var p=Profile("career2");var remote=new RunState(.25f,p);var camp=CampaignState.New(p,remote);
            Need(remote.HomeEquipment.Contains("B03")&&!remote.LoadedEquipment.Contains("B03"),"remote-home fixture wrong");
            Need(remote.Act("HomeExit",true),"remote market entry");
            Need(RecoveryRules.Quote(remote,camp,"B03")==null&&!remote.TrySellEquipment(camp,"B03",1),"remote home stove sold");
            c.run.SetBusinessPhase(GamePhase.Home);Need(RecoveryRules.Quote(c.run,c.campaign,"U01")==null,"quote outside market");
            return "unpaid basket, remote home, ingredient, packaging, coal/butane consumables, unknown id and non-market phase rejected; cash/stock/records unchanged";
        }
        static string PaidBasketAndDisk()
        {
            var c=Fixture(Profile(),new[]{"U01"});int opening=c.run.CashCents;
            Need(c.run.AddSupply("V05"),"basket add");Pay(c.run);
            Need(!c.campaign.equipment.Any(x=>x.id=="V05")&&c.run.Paid&&c.run.Cart.Contains("V05"),"new paid basket ownership fixture");
            string before=Facts(c);int price=Quote(c,"V05",4950);
            // Model cancellation is deliberately no commit; this does not test a UI button.
            Quote(c,"V05",4950);Need(Facts(c)==before,"view/cancel mutated facts");
            Need(!c.run.TrySellEquipment(c.campaign,"V05",price+1)&&Facts(c)==before,"stale quote committed");
            Need(c.run.TrySellEquipment(c.campaign,"V05",price),"confirmed basket sale");
            Need(c.run.CashCents==opening-11000+4950&&c.run.SpentCents==11000&&c.run.ResaleIncomeCents==4950&&c.run.Resales.Count==1,"purchase/resale accounting");
            Need(!c.run.Paid&&c.run.Cart.Count==0&&!c.run.Cargo.Contains("V05"),"sold paid item retained");
            before=Facts(c);Need(!c.run.TrySellEquipment(c.campaign,"V05",price)&&Facts(c)==before,"duplicate sale changed facts");
            c=Disk(c,"paid-basket");before=Facts(c);
            Need(RecoveryRules.Quote(c.run,c.campaign,"V05")==null&&!c.run.TrySellEquipment(c.campaign,"V05",price)&&Facts(c)==before,"sale resurrected after disk restore");
            Need(c.run.Act("MarketExit",true),"exit after sale");var service=new BusinessSession(Setup(c.run));service.Close();
            Need(c.campaign.CommitDay(c.run,service,service.SessionId),"resale zero-day settlement");
            var day=c.campaign.lastDay;
            Need(day.openingCashCents==opening&&day.purchasesCents==11000&&day.resaleIncomeCents==4950&&day.revenueCents==0&&day.resales.Count==1,"resale ledger missing/misclassified");
            Need(day.closingCashCents==opening-11000+4950,"resale treated as refund or duplicate income");
            c=Disk(c,"paid-basket-settled",service);var next=c.campaign.CreateNextDay(.25f);
            Need(!next.HomeEquipment.Contains("V05")&&!next.LoadedEquipment.Contains("V05")&&next.ResaleIncomeCents==0&&next.Resales.Count==0,"sold item or prior daily income carried forward");
            return "paid V05: 60000−11000+4950=53950 cents, no refund; quote/cancel/stale/duplicate atomic; real disk restore and next-day ownership do not revive it";
        }
        static string PaidCargo()
        {
            var c=Fixture(Profile(),new[]{"U01"});Need(c.run.AddSupply("V05"),"cargo fixture basket");Pay(c.run);
            Need(c.run.Act("MarketCart",true)&&c.run.Cargo.Contains("V05")&&!c.run.Paid,"cargo loading");
            Sell(c,"V05",4950);Need(!c.run.Cargo.Contains("V05")&&!Setup(c.run).equipment.Contains("V05"),"sold cargo enters setup");
            return "newly paid and loaded V05 sold once; no cargo or service-setup resurrection";
        }
        static string EnergyAndAliases()
        {
            var c=Fixture(Profile(),new[]{"E03","B02","E05","U01"},gas:60,cartridge:15,electric:45);
            // A heavy object's home list is also its original ownership alias.
            c.run.HomeEquipment.Add("E03");int cash=c.run.CashCents;
            Sell(c,"E03",3600);
            Need(!c.run.HomeEquipment.Contains("E03")&&!c.run.LoadedEquipment.Contains("E03")&&!c.campaign.equipment.Any(x=>x.id=="E03"),"tank ownership alias retained");
            Need(c.run.GasPoints==15&&c.run.CartridgePoints==15&&c.campaign.gas==15,"tank sale removed cartridge or left tank gas");
            Sell(c,"B02",2925);Need(c.run.GasPoints==0&&c.run.CartridgePoints==0&&c.campaign.gas==0&&c.campaign.cartridge==0,"cartridge stove retained its energy");
            Sell(c,"E05",13500);Need(c.run.ElectricityPoints==0&&c.campaign.electricity==0,"battery sale retained electricity");
            var setup=Setup(c.run);Need(setup.gasPoints==0&&setup.cartridgePoints==0&&setup.electricityPoints==0,"sold energy reaches service");
            Need(c.run.CashCents==cash+20025&&c.run.ResaleIncomeCents==20025&&c.run.Resales.Count==3,"energy equipment sale total");
            // Owned held gear, with no loaded alias, still has one authoritative row.
            var h=Fixture(Profile(),new[]{"U14"});var d=h.run.CaptureSave();d.loadedEquipment=Array.Empty<string>();d.homeEquipment=new[]{"U14"};d.holdingStove=true;d.heldEquipmentId="U14";
            h.run=RunState.Restore(d);h.run.Paused=false;Sell(h,"U14",675);
            Need(!h.run.HoldingStove&&h.run.HeldEquipmentId==""&&h.run.HomeEquipment.Count==0&&!h.campaign.equipment.Any(x=>x.id=="U14"),"held ownership retained");
            return "E03 removes only 45 tank points; B02 removes 15 cartridge points; E05 removes 45 electric points; aliases and held ownership atomically cleared; 20025 cents once";
        }
        static string LegacyPaidHome()
        {
            var c=Fixture(Profile(),new[]{"U01"});int opening=c.run.CashCents;
            foreach(string id in new[]{"E03","E02","rice_small"})Need(c.run.AddSupply(id),"legacy basket "+id);Pay(c.run);
            int afterPay=c.run.CashCents,spent=c.run.SpentCents;
            string json=JsonUtility.ToJson(c.run.CaptureSave());
            string pattern="(?<before>[{,])\\s*\"(?:resaleIncomeCents|resales|returnedPaidCargo|disposedPurchaseCents|usedBoughtIds|usedPriceOverrides)\"\\s*:\\s*(?:0|\\[\\])\\s*(?<after>,?)";
            for(int i=0;i<6;i++)json=Regex.Replace(json,pattern,m=>m.Groups["after"].Value==","?m.Groups["before"].Value:m.Groups["before"].Value=="{"?"{":"");
            Need(!json.Contains("\"resales\"")&&!json.Contains("\"resaleIncomeCents\"")&&!json.Contains("\"returnedPaidCargo\""),"legacy JSON still contains new fields");
            c.run=RunState.Restore(JsonUtility.FromJson<RunSaveData>(json));c.run.Paused=false;
            // Explicit model fault injection: simulates a saved paid basket
            // rejected by the current vehicle capacity check. Not a 3D fit test.
            c.run.LoadValidator=(equipment,cargo)=>"fixture: saved paid cargo no longer fits";
            c.run.Tick(200);
            Need(c.run.PendingPaidLoad&&c.run.Phase==GamePhase.Market&&c.run.Cart.Count==3&&c.run.Cargo.Count==0,"timeout lost paid ownership");
            Need(c.run.CashCents==afterPay&&c.run.SpentCents==spent&&c.run.ResaleIncomeCents==0,"old paid basket charged again");
            c.run.Tick(200);Need(c.run.PendingPaidLoad&&c.run.CashCents==afterPay,"repeated timeout altered paid basket");
            Need(!c.run.AddSupply("P02"),"timeout allowed new purchase");
            c=Disk(c,"legacy-paid-pending");Need(c.run.PendingPaidLoad&&c.run.Paid&&c.run.Cart.Count==3,"pending ownership lost in disk restore");
            Need(c.run.ReturnPaidCargoHome(),"return-paid-home refused");
            Need(c.run.Cart.Count==0&&!c.run.Paid&&c.run.ReturnedPaidCargo.Count==3&&c.run.Cargo.Count==0,"return moved/duplicated wrong ownership");
            var setup=Setup(c.run);
            Need(!setup.equipment.Contains("E03")&&setup.gasPoints==0&&setup.cartridgePoints==0&&!setup.inventory.ContainsKey("rice"),"home-returned goods entered today's service");
            var service=new BusinessSession(setup);service.Close();Need(c.campaign.CommitDay(c.run,service,service.SessionId),"zero-day home commit");
            Need(c.campaign.stock.Single(x=>x.id=="rice").quantity==5&&c.campaign.equipment.Count(x=>x.id=="E03")==1&&!c.campaign.equipment.Any(x=>x.id=="E02"),"home goods missing/duplicated");
            Need(c.campaign.gas==90&&c.campaign.cartridge==45,"returned E03/E02 energy missing or counted twice");
            Need(c.campaign.lastDay.revenueCents==0&&c.campaign.lastDay.purchasesCents==11000&&c.campaign.cashCents==opening-11000,"return incorrectly refunded or earned revenue");
            c=Disk(c,"legacy-paid-home-settled",service);string frozen=JsonUtility.ToJson(c.campaign);
            Need(!c.campaign.CommitDay(c.run,service,service.SessionId)&&JsonUtility.ToJson(c.campaign)==frozen,"restored settlement duplicated returned cargo");
            var next=c.campaign.CreateNextDay(.25f);Need(next.Supplies["rice"]==5&&next.HomeEquipment.Contains("E03")&&next.GasPoints==90&&next.CartridgePoints==45&&next.ReturnedPaidCargo.Count==0,"next day home goods/energy incorrect");
            return "real paid E03+E02+rice (11000 cents), old JSON without recovery fields; injected capacity rejection retains ownership at timeout; home return never feeds today; disk/next day preserve 5 rice and 45+45 gas once";
        }
        static void ServeOne(BusinessSession service,string package)
        {
            StallOrder order=null;
            for(int frame=0;frame<24000&&order==null;frame++)
            {
                order=service.Orders.Find(o=>!o.served&&!o.departed&&o.lane<0&&o.reservedLane<0);
                if(order!=null&&!order.arrived)Need(service.MarkCustomerArrived(order.id),"arrival refused");
                if(order==null)service.Tick(Step);
            }
            Need(order!=null&&service.BeginOrder(order.id,0),"begin portion: "+service.Message);
            for(int frame=0;frame<3600&&service.Lanes[0].step!=KitchenStep.Pack;frame++)
            {
                var lane=service.Lanes[0];
                if(lane.step==KitchenStep.Utensil||lane.step==KitchenStep.Season&&lane.remaining<=0)Need(service.Action(0),"cooking action");
                if(service.Lanes[0].step!=KitchenStep.Pack)service.Tick(Step);
            }
            Need(service.Serve(0,package),"serve refused: "+service.Message);
        }
        static string IntegrityDays()
        {
            var c=Fixture(Profile(),new[]{"V09","U12"},stock:new[]{new RunStockSave{id="fruit",quantity=60},new RunStockSave{id="P08",quantity=50}});
            int opening=c.run.CashCents;
            for(int day=1;day<=2;day++)
            {
                if(c.run.Phase==GamePhase.Home)
                {
                    // Real home pickup/load actions; no equipment is moved free.
                    while(c.run.HasUnloadedEquipment){Need(c.run.Act("StovePickup",true),"next-day pickup");Need(c.run.Act("HomeCart",true),"next-day load");}
                    Need(c.run.Act("HomeExit",true),"next-day market");
                }
                Need(c.run.Act("MarketExit",true),"integrity exit");var service=new BusinessSession(Setup(c.run));
                Need(service.SelectedRecipes.SequenceEqual(new[]{5}),"cold-only menu fixture");Need(service.Open(),"cold open");ServeOne(service,"P08");
                Need(service.Served==1&&service.UnseasonedPortionsServed==1&&service.IntegrityChange==-10&&service.RevenueCents==700,"actual short-seasoned sale facts");
                service.Close();Need(c.campaign.CommitDay(c.run,service,service.SessionId),"day commit refused");
                Need(c.campaign.integrityChangeTotal==-10*day&&c.campaign.history.Count==day&&c.campaign.lastDay.integrityChange==-10,"integrity accumulated incorrectly");
                Need(c.campaign.cashCents==opening+700*day&&c.campaign.lastDay.nightFuelCents==0,"cold-day cash conservation");
                c=Disk(c,"integrity-day-"+day,service);string frozen=JsonUtility.ToJson(c.campaign);
                Need(!c.campaign.CommitDay(c.run,service,service.SessionId)&&JsonUtility.ToJson(c.campaign)==frozen,"disk restore double-committed integrity");
                if(day==1)c.run=c.campaign.CreateNextDay(.25f);
            }
            Need(c.campaign.history.Sum(x=>x.integrityChange)==-20&&c.campaign.history.Sum(x=>x.unseasonedPortionsServed)==2,"history aggregate mismatch");
            return "two real cold-drink sales over two days, normal 35–54s patience and 16s arrivals: -10 then -20 integrity, +700 then +1400 cents; disk load and duplicate commit apply nothing twice";
        }
        static void BuyUsed(Context c,string id,int expected)
        {
            var quote=RecoveryRules.UsedQuote(c.run,c.campaign,id);
            Need(quote!=null&&quote.condition==50&&quote.priceCents==expected,"used quote "+id+" does not match 50% condition at 65% purchase factor");
            Need(c.run.TryBuyUsedEquipment(c.campaign,id,quote.priceCents),"used purchase "+id+": "+c.run.Message);
            Need(c.run.Paid&&c.run.Cart.Contains(id)&&c.run.PurchaseItemPriceCents(id)==expected,"used item not in paid basket at actual purchase price");
            Need(c.campaign.equipment.Single(x=>x.id==id).condition==50,"used condition lost at purchase");
        }
        static string TwoYuanRecovery()
        {
            // This case starts at the real career-1 constructor. It does not
            // inject ownership, cash, stock, patience, or recipe state.
            var profile=Profile("career1");var run=new RunState(.25f,profile);
            var c=new Context{run=run,campaign=CampaignState.New(profile,run)};
            Need(run.CashCents==15000&&run.LoadedEquipment.Contains("U01"),"normal career1 initial cash/basic spatula changed");
            Need(run.Act("HomeExit",true),"normal market entry");
            Need(run.AddSupply("V05")&&run.AddSupply("U12"),"mistaken purchase basket");Pay(run);
            Need(run.CashCents==200&&run.SpentCents==14800,"normal mistaken purchase must leave exactly ¥2");
            Need(new BusinessSession(Setup(run)).SelectedRecipes.Count==0,"mistaken paid basket unexpectedly opens a menu");
            string before=Facts(c);Need(!run.TryBuyUsedEquipment(c.campaign,"B01",1138)&&Facts(c)==before,"¥2 bought a ¥11.38 stove or debited on rejection");
            Sell(c,"V05",5024);Sell(c,"U12",1736);
            Need(run.CashCents==6960&&run.ResaleIncomeCents==6760&&run.DisposedPurchaseCents==14800,"two resale proceeds/removed purchase costs");
            Need(RecoveryRules.UsedQuote(run,c.campaign,"U01")==null,"normal starting U01 should not be duplicated");
            BuyUsed(c,"B01",1138);BuyUsed(c,"V01",1138);
            Need(run.CashCents==4684&&run.CartTotalCents==2276,"used basket receipt charged retail price");
            Need(!run.AddSupply("E01"),"unpaid supplies mixed into paid used basket");
            c=Disk(c,"two-yuan-used-paid");run=c.run;
            Need(run.CashCents==4684&&run.CartTotalCents==2276&&run.UsedBoughtIds.Count==2,"used prices/limit lost on real disk restore");
            Need(run.Act("MarketCart",true),"used equipment load");
            foreach(string id in new[]{"E01","P02","rice_small","egg_small"})Need(run.AddSupply(id),"hard requirement basket "+id+": "+run.Message);
            Need(run.CartTotalCents==4300,"hard requirement quotation changed");Pay(run);
            Need(run.CashCents==384&&run.SpentCents==21376,"opening recovery budget expected ¥3.84");
            Need(run.Act("MarketCart",true),"hard supplies load");
            Need(run.CargoPaidCents==6576&&run.SpentCents==run.DisposedPurchaseCents+run.CargoPaidCents,"receipt cargo costs do not reconcile after resale");
            Need(run.Act("MarketExit",true),"recoverable market exit");
            var service=new BusinessSession(Setup(run));
            Need(service.SelectedRecipes.SequenceEqual(new[]{0})&&service.Missing(0)==""&&!service.SeasoningAvailable,"recovered equipment does not open a soft-shortage rice menu");
            Need(service.Open(),"recovered rice open");ServeOne(service,"P02");
            Need(service.Served==1&&service.RevenueCents==1000&&service.CashCents==1384&&service.IntegrityChange==-10,"recovered first sale did not earn exactly ¥10 with recorded seasoning shortage");
            var audit=service.CaptureAudit();Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeInventory&&audit.nonNegativeEnergy,"recovered sale audit");
            service.Close();Need(c.campaign.CommitDay(run,service,service.SessionId),"recovered day settlement");
            var ledger=c.campaign.lastDay;
            Need(ledger.openingCashCents==15000&&ledger.purchasesCents==21376&&ledger.resaleIncomeCents==6760&&ledger.revenueCents==1000&&ledger.nightFuelCents==0&&ledger.closingCashCents==1384,"recovered full-day cash ledger");
            Need(ledger.closingCashCents==ledger.openingCashCents-ledger.purchasesCents+ledger.resaleIncomeCents+ledger.revenueCents-ledger.nightFuelCents,"recovery cash not conserved");
            c=Disk(c,"two-yuan-settled",service);var next=c.campaign.CreateNextDay(.25f);
            Need(next.CashCents==1384&&next.Supplies["rice"]==4&&next.Supplies["egg"]==3&&next.Supplies["P02"]==49&&next.CoalPoints==29,"recovered next-day stock/cash/fuel");
            Need(c.campaign.integrityChangeTotal==-10&&next.SpentCents==0&&next.ResaleIncomeCents==0&&next.UsedBoughtIds.Count==0,"recovery next-day daily counters or integrity");
            Need(!c.campaign.equipment.Any(x=>x.id=="V05"||x.id=="U12")&&c.campaign.equipment.Single(x=>x.id=="B01").condition==50&&c.campaign.equipment.Single(x=>x.id=="V01").condition==50,"sold gear revived or used condition repaired free");
            return "REAL career1 model start: ¥150→wrong purchases ¥2→sell V05/U12 ¥69.60→used B01/V01 (existing U01 retained)→fuel/pack/rice/egg ¥3.84→one rice sale ¥13.84; disk and next day: rice4/egg3/pack49/coal29, integrity -10, condition50 retained; no physical movement claim";
        }
        static string UsedSpatula()
        {
            // Explicit missing-tool save fixture, distinct from the normal
            // career constructor (which already includes U01).
            // Original content library U01 explicitly permits supermarket ¥5.
            // Verify that channel separately; it must still wait for checkout.
            var retail=Fixture(Profile(),Array.Empty<string>());int retailCash=retail.run.CashCents;
            Need(retail.run.AddSupply("U01")&&retail.run.Cart.Contains("U01")&&retail.run.CartTotalCents==500,
                "U01 original-table retail offer missing or mispriced");
            Need(retail.run.CashCents==retailCash&&!retail.run.Paid&&!retail.run.LoadedEquipment.Contains("U01"),
                "Retail selection must not debit or equip before checkout");
            var c=Fixture(Profile(),Array.Empty<string>());int cash=c.run.CashCents;
            BuyUsed(c,"U01",163);Sell(c,"U01",113);
            Need(c.run.CashCents==cash-50&&c.run.DisposedPurchaseCents==163&&c.run.ResaleIncomeCents==113,"used spatula resale sold at new condition or lost cost");
            Need(RecoveryRules.UsedQuote(c.run,c.campaign,"U01")==null,"sold daily used item restocked");
            c=Disk(c,"used-spatula-limit");string before=Facts(c);
            Need(!c.run.TryBuyUsedEquipment(c.campaign,"U01",163)&&Facts(c)==before,"daily limit bypassed by disk restore");
            Need(c.run.UsedBoughtIds.SequenceEqual(new[]{"U01"})&&!c.campaign.equipment.Any(x=>x.id=="U01"),"used item revived or daily purchase history removed");
            return "missing-tool fixture only: used U01 buys for163c (50%×65%), resells113c (50%×45%), loses50c; same-day supply does not replenish after sale or disk restore";
        }
    }
}
