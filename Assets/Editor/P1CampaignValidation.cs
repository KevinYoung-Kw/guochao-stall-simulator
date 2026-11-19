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
    // Production-model integration plus actual factory bounds. This driver supplies
    // valid local interaction intents; it does not certify keyboard traversal/animation.
    public static class P1CampaignValidation
    {
        [Serializable] public sealed class Check { public string name;public bool passed;public string detail; }
        [Serializable] public sealed class Report
        {
            public bool success;public string scope="Production business/campaign/file integration + actual CartAI/factory packing; not hardware input or animated service";
            public string testSaveDirectory;public Check[] checks;public string[] runtimeErrors;
        }
        static readonly List<Check> checks=new List<Check>();
        static readonly List<string> errors=new List<string>();
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static GameObject fixture;static VehicleLoadout packing;static GameFlow mapper;static string output;
        static CampaignState completedCampaign;static RunState secondDay;
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--campaign-validation-output");
            output=Path.GetFullPath(at>=0&&at+1<args.Length?args[at+1]:Path.Combine(Application.dataPath,"../../art-v13-implementation/p1/campaign-validation.json"));
            checks.Clear();errors.Clear();string previousDirectory=CampaignSaveStore.TestDirectory;
            string saveDirectory=Path.Combine(Path.GetDirectoryName(output),"isolated-campaign-"+Guid.NewGuid().ToString("N"));
            Application.logMessageReceived+=CaptureError;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                fixture=new GameObject("P1CampaignIntegration");
                var cart=new GameObject("MeasuredCartAI");cart.transform.SetParent(fixture.transform,false);
                var resource=Resources.Load<GameObject>("Models/CartAI");Need(resource!=null,"Actual CartAI missing");UnityEngine.Object.Instantiate(resource,cart.transform,false);
                packing=cart.AddComponent<VehicleLoadout>();packing.Initialize(cart.transform,Make);
                var map=new GameObject("ProductionSetupMapper");map.transform.SetParent(fixture.transform,false);map.SetActive(false);mapper=map.AddComponent<GameFlow>();
                CheckCase("normal profile: paid pending load -> cooking -> settle -> second paid procurement",CrossDay);
                CheckCase("unloaded energy cannot power service; actual reserves survive",UnloadedEnergy);
                CheckCase("E03 night refill costs 20; E05 free; coal/cartridge unchanged; insufficient cash",NightEnergy);
                CheckCase("B02 cartridge cannot supply B03 when E03 remains home",MixedEnergyUnavailable);
                CheckCase("B03 cooking consumes tank rather than the carried B02 cartridge",MixedEnergyConsumption);
                CheckCase("B02 cooking consumes cartridge rather than the carried E03 tank",CartridgeConsumption);
                CheckCase("zero and multi-box persistent stock occupy actual quantity",PackingQuantities);
                CampaignSaveStore.TestDirectory=saveDirectory;
                CheckCase("checksummed current/backup + incompatible versions + independent new-campaign archive",Persistence);
            }
            catch(Exception e){checks.Add(new Check{name="fixture",passed=false,detail=e.ToString()});}
            finally
            {
                if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture);fixture=null;mapper=null;packing=null;
                CampaignSaveStore.TestDirectory=previousDirectory;Application.logMessageReceived-=CaptureError;
            }
            var report=new Report{success=checks.All(x=>x.passed)&&errors.Count==0,checks=checks.ToArray(),runtimeErrors=errors.ToArray(),testSaveDirectory=saveDirectory};
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log((report.success?"P1_CAMPAIGN_PASS ":"P1_CAMPAIGN_FAIL ")+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static void CaptureError(string message,string stack,LogType type)
        {if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void CheckCase(string name,Func<string> body)
        {try{checks.Add(new Check{name=name,passed=true,detail=body()});}catch(Exception e){checks.Add(new Check{name=name,passed=false,detail=e.ToString()});}}
        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static GameObject Make(string id,Transform parent)
        {
            if(SupplyCatalog.IsEquipment(id)||id.StartsWith("P")||id.StartsWith("S"))return EquipmentVisualFactory.Create(id,parent);
            return (GameObject)typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{id,parent,.36f});
        }
        static RunProfile Profile(string career)
        {var p=new RunProfile{careerId=career,selectedTraits=career=="career1"?Array.Empty<string>():new[]{"迟钝学者"}};Need(p.Validate(out string reason),reason);return p;}
        static BusinessSetup Setup(RunState run)
        {typeof(GameFlow).GetField("run",Hidden).SetValue(mapper,run);return mapper.BuildBusinessSetup();}
        static void BindPacking(RunState run)
        {run.LoadValidator=(gear,cargo)=>packing.Validate(gear,cargo,run.PackingSupplies);}
        static void LoadHome(RunState run)
        {
            run.Paused=false;BindPacking(run);
            for(int guard=0;run.HasUnloadedEquipment&&guard<20;guard++)
            {Need(run.Act("StovePickup",true),run.Message);Need(run.Act("HomeCart",true),"Home load: "+run.Message);}
            Need(!run.HasUnloadedEquipment,"Home equipment did not finish loading");
            Need(run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"Cannot enter market: "+run.Message);
        }
        static void Basket(RunState run,string id)
        {
            if(SupplyCatalog.Find(id)!=null)Need(run.AddSupply(id),"Basket "+id+": "+run.Message);
            else
            {
                Need(run.Act(GameCatalog.Find(id).anchor,true,false),"Pickup "+id+": "+run.Message);
                Need(run.HeldGrocery==id&&!run.Cart.Contains(id),"Free pickup added remote basket stock");
                Need(run.Act("ShoppingCart",true)&&run.HeldGrocery=="","Held carton did not enter basket locally");
            }
        }
        static void Pay(RunState run)
        {
            int cash=run.CashCents,spent=run.SpentCents,total=run.CartTotalCents;
            Need(run.Act("Cashier",true),"Checkout start: "+run.Message);run.Tick(run.CheckoutDuration,true);
            Need(run.Paid&&run.CashCents==cash-total&&run.SpentCents==spent+total,"Checkout failed exact debit");
        }
        static int CookOne(BusinessSession service)
        {
            Need(service.SelectedRecipes.Count>0&&service.Open(),"Purchased menu cannot open: "+service.Message);
            service.Tick(1);Need(service.Orders.Count>0,"No order from ordinary session");var order=service.Orders[0];
            service.MarkCustomerArrived(order.id);int lane=Array.FindIndex(service.Lanes,x=>x.recipeIndex==order.recipeIndex);
            Need(lane>=0&&service.BeginOrder(order.id,lane),"Cannot start first genuine order: "+service.Message);
            for(int frame=0;frame<5400&&!order.served&&!order.departed;frame++)
            {
                var state=service.Lanes[lane];
                if(state.step==KitchenStep.Utensil||state.step==KitchenStep.Season&&state.remaining<=0)Need(service.Action(lane),"Cooking action refused");
                else if(state.step==KitchenStep.Pack)
                {string pack=Array.Find(BusinessSession.Recipes[order.recipeIndex].packages,x=>service.Count(x)>0);Need(pack!=null&&service.Serve(lane,pack),"Package/serve failed");}
                if(!order.served)service.Tick(1f/60f);
            }
            Need(order.served&&service.Served==1,"First ordinary order expired or failed");
            var audit=service.CaptureAudit();Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeEnergy&&audit.recipeProduction.All(x=>x.portionsConserved),"Service conservation failed");
            return order.recipeIndex;
        }
        static string CrossDay()
        {
            var profile=Profile("career4");var run=new RunState(4,profile);var campaign=CampaignState.New(profile,run);int initial=run.CashCents;
            var initialGear=campaign.equipment.ToDictionary(x=>x.id,x=>x.instanceId);LoadHome(run);
            var recommendation=OpeningProcurementPlan.For(run);var owned=new HashSet<string>(run.LoadedEquipment);owned.UnionWith(run.Supplies.Keys);
            foreach(string id in recommendation.supplies)if(!owned.Contains(id))Basket(run,id);
            foreach(string id in recommendation.shelves)if(!owned.Contains(id))Basket(run,id);
            Pay(run);int paidCash=run.CashCents,paidSpent=run.SpentCents;string basket=string.Join(",",run.Cart);
            var paidEnvelope=CampaignSaveStore.Decode(CampaignSaveStore.Encode(Snapshot(campaign,run)));
            run=RunState.Restore(paidEnvelope.run);run.Paused=false;BindPacking(run);
            Need(run.Paid&&run.CashCents==paidCash&&run.SpentCents==paidSpent&&string.Join(",",run.Cart)==basket,"Paid-unloaded restore changed goods/cash");
            Need(!run.Act("Cashier",true),"Paid batch could be charged again");run.Tick(.2f);Need(run.CashCents==paidCash,"Paid snapshot ticking charged twice");
            Need(run.Act("MarketCart",true)&&run.CashCents==paidCash&&run.Cart.Count==0,"Paid goods did not load once");
            Need(!run.Act("MarketCart",true)&&run.CashCents==paidCash,"Duplicate load changed cash");
            Need(run.Act("MarketExit",true),run.Message);var setup=Setup(run);
            Need(!setup.practice&&setup.profile.careerId==profile.careerId,"Normal profile replaced by practice fixture");
            var service=new BusinessSession(setup);Need(service.Missing(0)=="","Purchased rice menu missing "+service.Missing(0));int recipe=CookOne(service);
            int revenue=service.RevenueCents;service.Close();
            Need(campaign.CommitDay(run,service,service.SessionId),"First day did not settle");
            string settled=JsonUtility.ToJson(campaign);Need(!campaign.CommitDay(run,service,service.SessionId)&&JsonUtility.ToJson(campaign)==settled,"Duplicate settlement mutated campaign");
            Need(campaign.lastDay.nightFuelCents==2000,"Used purchased E03 did not refill for 20");
            Need(campaign.cashCents==initial-paidSpent+revenue-2000&&campaign.lastDay.purchasesCents==paidSpent,"Cross-day cash equation failed");
            foreach(var p in service.Inventory)Need((campaign.stock.Find(x=>x.id==p.Key)?.quantity??0)==p.Value,"Night stock changed "+p.Key);
            foreach(var p in initialGear)Need(campaign.equipment.Find(x=>x.id==p.Key)?.instanceId==p.Value,"Starter gear recreated "+p.Key);
            Need(campaign.equipment.Count(x=>x.id=="E03")==1,"Purchased tank lost or duplicated");
            var next=campaign.CreateNextDay(4);Need(campaign.dayIndex==2&&next.CashCents==campaign.cashCents&&next.SpentCents==0,"Day 2 granted initial money or kept previous spend");
            Need(next.GasPoints==45&&next.CartridgePoints==0,"Night tank refill not inherited");
            string day2Before=JsonUtility.ToJson(campaign);Need(!campaign.CommitDay(run,service,service.SessionId)&&JsonUtility.ToJson(campaign)==day2Before,"Old session settled again after day advance");
            Rejected(()=>campaign.CreateNextDay(4),"Repeated NextDay skipped an unsettled day");
            foreach(var p in service.Inventory)Need((next.Supplies.TryGetValue(p.Key,out int n)?n:0)==p.Value,"Day 2 lost leftovers "+p.Key);
            var nextGear=new List<string>(next.LoadedEquipment);nextGear.AddRange(next.HomeEquipment);
            Need(nextGear.Count==campaign.equipment.Count&&nextGear.Distinct().Count()==nextGear.Count,"Day 2 duplicated starter equipment");
            LoadHome(next);int beforeNextPurchase=next.CashCents,leftRice=next.Supplies["rice"],leftPackages=next.Supplies["P05"];
            Basket(next,"P05");int nextPrice=next.CartTotalCents;Pay(next);Need(next.Act("MarketCart",true),next.Message);
            var nextSetup=Setup(next);Need(next.CashCents==beforeNextPurchase-nextPrice&&nextSetup.inventory["rice"]==leftRice&&nextSetup.inventory["P05"]==leftPackages+50,"Second procurement regranted funds/lost leftovers");
            Need(nextSetup.gasPoints==45,"Second-day business energy did not carry over");
            completedCampaign=campaign;secondDay=next;
            return "career4 ordinary "+BusinessSession.Recipes[recipe].id+" served; first purchase="+paidSpent+", revenue="+revenue+", nightFuel=2000, day2Cash="+next.CashCents+", gearIDs stable, paid-pending snapshot exact, real packing accepted both days";
        }
        static string UnloadedEnergy()
        {
            foreach(string career in new[]{"career2","career3","career7"})
            {
                var run=new RunState(4,Profile(career));int gas=run.GasPoints,coal=run.CoalPoints,electricity=run.ElectricityPoints;
                var setup=Setup(run);Need(setup.gasPoints==0&&setup.cartridgePoints==0&&setup.electricityPoints==0,"Unloaded energy powered business "+career);
                Need(run.GasPoints==gas&&run.CoalPoints==coal&&run.ElectricityPoints==electricity,"Mapping consumed unloaded reserves");
                LoadHome(run);setup=Setup(run);Need(setup.gasPoints==gas-run.CartridgePoints&&setup.cartridgePoints==run.CartridgePoints&&setup.electricityPoints==electricity,"Loaded energy not exposed "+career);
            }
            return "E03 tank, E05 battery, and B02 cartridge are excluded until loaded; BuildBusinessSetup preserves untouched reserves";
        }
        static string NightEnergy()
        {
            // Explicit late-day state fixture; no new-game grants or made-up purchases.
            foreach(bool tankLoaded in new[]{false,true})
            {
                var p=Profile("career2");var original=new RunState(4,p);var c=CampaignState.New(p,original);
                c.cashCents=10000;c.gas=23;c.cartridge=7;c.coal=11;c.electricity=9;
                foreach(string id in new[]{"B02","E01","E05"})if(!c.equipment.Exists(x=>x.id==id))c.equipment.Add(new CampaignGearRow{id=id,instanceId="fixture-"+id,name=id,location="home"});
                var r=RunState.FromCampaign(c,4);r.Paused=false;
                if(tankLoaded){r.HomeEquipment.Remove("E03");r.LoadedEquipment.Add("E03");}
                var s=new BusinessSession(Setup(r));s.Close();Need(c.CommitDay(r,s,s.SessionId),"Energy day did not commit");
                Need(c.cashCents==8000&&c.lastDay.nightFuelCents==2000&&c.gas==52&&c.cartridge==7,"Tank refill price/quantity incorrect");
                Need(c.electricity==45&&c.coal==11,"Battery free refill or coal preservation incorrect");
                var next=c.CreateNextDay(4);Need(next.GasPoints==52&&next.CartridgePoints==7&&next.CoalPoints==11&&next.ElectricityPoints==45,"Night reserves not inherited");
            }
            var poorProfile=Profile("career2");var poorRun=new RunState(4,poorProfile);var poor=CampaignState.New(poorProfile,poorRun);poor.cashCents=1999;poor.gas=12;
            poorRun=RunState.FromCampaign(poor,4);var poorService=new BusinessSession(Setup(poorRun));poorService.Close();poor.CommitDay(poorRun,poorService,poorService.SessionId);
            Need(poor.cashCents==1999&&poor.gas==12&&poor.lastDay.nightFuelCents==0,"Unaffordable refill charged or supplied free gas");
            var seven=Profile("career7");var coalBase=new RunState(4,seven);var coalCampaign=CampaignState.New(seven,coalBase);coalCampaign.gas=6;coalCampaign.cartridge=6;coalCampaign.coal=4;
            coalBase=RunState.FromCampaign(coalCampaign,4);var coalService=new BusinessSession(Setup(coalBase));coalService.Close();coalCampaign.CommitDay(coalBase,coalService,coalService.SessionId);
            Need(coalCampaign.gas==6&&coalCampaign.cartridge==6&&coalCampaign.coal==4&&coalCampaign.lastDay.nightFuelCents==0,"Coal/cartridge got automatic nightly refill");
            return "Owned E03: 16->45 tank + unchanged 7 cartridge costs 2000; E05 9->45 free; coal11 stable; cash1999 refuses tank refill; no-E03 cartridge6/coal4 stay unchanged";
        }
        static RunState MixedEnergyRun(bool tankLoaded)
        {
            var original=new RunState(4,Profile("career4"));var data=original.CaptureSave();
            data.gas=tankLoaded?23:7;data.cartridge=7;
            data.homeEquipment=tankLoaded?Array.Empty<string>():new[]{"E03"};
            data.loadedEquipment=tankLoaded?new[]{"B03","V02","U02","U01","S02","B02","E03"}:new[]{"B03","V02","U02","U01","S02","B02"};
            data.supplies=new[]{new RunStockSave{id="rice",quantity=5},new RunStockSave{id="egg",quantity=4},new RunStockSave{id="seasoning",quantity=10},new RunStockSave{id="P05",quantity=50}};
            var run=RunState.Restore(data);run.Paused=false;return run;
        }
        static string MixedEnergyUnavailable()
        {
            var run=MixedEnergyRun(false);var session=new BusinessSession(Setup(run));
            Need(session.Missing(0)!="","E03 is home; only loaded B02 cartridge has gas, yet B03 + V02 menu is cookable (V02 cannot use B02). Merged Gas pool supplies wrong base.");
            return "Loaded B02 cartridge never supplies the incompatible B03/V02 station when tank is left home";
        }
        static string MixedEnergyConsumption()
        {
            var run=MixedEnergyRun(true);var campaign=CampaignState.New(run.Profile,run);var session=new BusinessSession(Setup(run));CookOne(session);session.Close();
            Need(session.CaptureAudit().gasUsed==1&&session.CaptureAudit().cartridgeUsed==0,"B03 cooking consumed wrong pool before settlement");
            var saved=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(session.CaptureSave())));
            Need(saved.Gas==15&&saved.Cartridge==7&&saved.Lanes[0].energy=="gas","Separate tank/cartridge state did not survive JSON restore");
            Need(campaign.CommitDay(run,saved,saved.SessionId),"Mixed-energy day did not commit");
            Need(campaign.cartridge==7&&campaign.gas==52,"B03/V02 cooked using tank; cartridge should remain7 after night tank refill45, actual cartridge="+campaign.cartridge+", gas="+campaign.gas);
            return "B03/V02 uses tank16->15; cartridge7 unchanged; night tank45 + cartridge7 =52";
        }
        static string CartridgeConsumption()
        {
            var run=MixedEnergyRun(true);run.LoadedEquipment.Remove("B03");run.LoadedEquipment.Remove("V02");run.LoadedEquipment.Add("V01");
            var session=new BusinessSession(Setup(run));CookOne(session);
            Need(session.Gas==16&&session.Cartridge==6&&session.Lanes[0].energy=="cartridge","B02 consumed tank instead of one cartridge unit");
            var snapshot=JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(session.CaptureSave()));var restored=BusinessSession.Restore(snapshot);
            Need(restored.Cartridge==6&&restored.Gas==16&&restored.CaptureAudit().cartridgeUsed==1&&restored.CaptureAudit().gasUsed==0,"Cartridge debit or audit changed on restore");
            snapshot.cartridge=-1;Rejected(()=>BusinessSession.Restore(snapshot),"Negative cartridge save accepted");
            return "B02/V01 consumes cartridge7->6, tank16 unchanged; snapshot preserves exact sources and audit, negative saved cartridge rejected";
        }
        static string PackingQuantities()
        {
            var run=new RunState(4,Profile("career1"));run.LoadedEquipment.Clear();run.Supplies.Clear();
            run.Supplies["P02"]=0;run.Supplies["rice"]=0;Need(!run.PackingSupplies.Any(),"Zero inventory occupies a box");
            run.Supplies["P02"]=51;Need(run.PackingSupplies.Count(x=>x=="P02")==2,"51 packages collapsed into one 50-unit pack");
            string error=packing.Validate(run.LoadedEquipment,run.Cargo,run.PackingSupplies);Need(error=="","Two actual pack volumes rejected: "+error);
            Need(packing.Plan.placements.Count==2&&packing.Plan.placements.Select(x=>x.instanceId).Distinct().Count()==2,"Multiple packages share an instance/pose");
            Need(!VehiclePacking.Overlap(packing.Plan.placements[0].boundsInPackingFrame,packing.Plan.placements[1].boundsInPackingFrame,.005f),"Two actual packages overlap");
            run.Supplies["rice"]=31;Need(run.PackingSupplies.Count(x=>x=="rice")==3,"31 rice units collapsed into <=2 cartons");
            run.Supplies["P02"]=0;run.Supplies["rice"]=0;Need(packing.Validate(run.LoadedEquipment,run.Cargo,run.PackingSupplies)==""&&packing.Plan.placements.Count==0,"Zero stock retained old packing instances");
            return "0 stock = 0 models; P02 quantity51 = 2 separate non-overlapping measured packs; rice31 = 3 cartons; depleted stock clears plan";
        }
        static CampaignSaveData Snapshot(CampaignState c,RunState r)=>new CampaignSaveData{campaign=c,run=r.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
        static void Save(CampaignSaveData data){Need(CampaignSaveStore.Save(data,out string status),status);}
        static void Rejected(Action action,string message){try{action();}catch(Exception){return;}throw new InvalidOperationException(message);}
        static string Persistence()
        {
            var p=Profile("career1");var r=new RunState(4,p);var c=CampaignState.New(p,r);var first=Snapshot(c,r);Save(first);
            r.SetBusinessCash(r.CashCents-100);var second=Snapshot(c,r);Save(second);
            string current=Path.Combine(CampaignSaveStore.DirectoryPath,"current.json"),backup=Path.Combine(CampaignSaveStore.DirectoryPath,"backup.json");
            File.WriteAllText(current,"broken current");Need(CampaignSaveStore.Load(out var recovered,out string status)&&recovered.run.cashCents==first.run.cashCents&&status.Contains("备份"),"Corrupt current failed valid-backup recovery");
            string goodBackup=File.ReadAllText(backup);Save(second);Need(File.ReadAllText(backup)==goodBackup,"Corrupt current overwrote valid backup during save");
            var future=JsonUtility.FromJson<CampaignSaveData>(JsonUtility.ToJson(second));future.schemaVersion=2;future.business=second.business;
            bool schemaRejected=false;try{CampaignSaveStore.Decode(CampaignSaveStore.Encode(future));}catch(InvalidDataException ex){schemaRejected=ex.Message.Contains("存档格式不兼容");}
            Need(schemaRejected,"Unknown campaign version was not specifically rejected by schema validation");
            var envelope=JsonUtility.FromJson<CampaignSaveEnvelope>(CampaignSaveStore.Encode(second));envelope.version=2;
            Rejected(()=>CampaignSaveStore.Decode(JsonUtility.ToJson(envelope)),"Unknown envelope version accepted");
            envelope=JsonUtility.FromJson<CampaignSaveEnvelope>(CampaignSaveStore.Encode(second));envelope.payload+=" ";
            Rejected(()=>CampaignSaveStore.Decode(JsonUtility.ToJson(envelope)),"Changed payload accepted without matching checksum");
            Need(CampaignSaveStore.ArchiveBeforeNewRun(out status),status);string archivePath=Path.Combine(CampaignSaveStore.DirectoryPath,"previous-"+c.campaignId+".json");
            Need(File.Exists(archivePath),"Independent previous-campaign archive missing");string archived=File.ReadAllText(archivePath);
            var newRun=new RunState(4,p);var newCampaign=CampaignState.New(p,newRun);Need(c.campaignId!=newCampaign.campaignId,"New campaign reused identity");
            Save(Snapshot(newCampaign,newRun));for(int i=0;i<4;i++){newRun.SetBusinessCash(newRun.CashCents-10);Save(Snapshot(newCampaign,newRun));}
            Need(File.ReadAllText(archivePath)==archived&&CampaignSaveStore.Decode(archived).campaign.campaignId==c.campaignId,"Rolling backups overwrote old campaign archive");
            Need(CampaignSaveStore.Load(out var loaded,out status)&&loaded.campaign.campaignId==newCampaign.campaignId,"Load selected archive instead of new active campaign");
            if(completedCampaign!=null&&secondDay!=null)
            {
                var crossDay=CampaignSaveStore.Decode(CampaignSaveStore.Encode(Snapshot(completedCampaign,secondDay)));
                Need(crossDay.campaign.dayIndex==2&&crossDay.run.cashCents==secondDay.CashCents&&crossDay.campaign.history.Count==1,"Actual day2 campaign file roundtrip lost ledger/cash");
            }
            return "Corrupt current recovers verified backup; corrupt file never rotates over it; schema/envelope/hash rejected; 5 new-campaign saves leave previous-ID archive byte-identical; all files isolated from user saves";
        }
    }
}
