using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Actual BusinessSession cooking, naturally spawned customers, payment and
    // CampaignState settlement. A declared 82.5-XP boundary fixture tests
    // an earned level without pretending a whole campaign was played.
    public static class StallProgression24Validation
    {
        [Serializable] public sealed class Check { public string name,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public bool success; public List<Check> checks=new List<Check>();
            public string scope="Real model cooking, natural customers, payment, settlement and save roundtrips. Near-threshold XP is a declared fixture; no complete human campaign or actor motion is claimed.";
        }
        static Report report; static string output,saveDir;
        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static int Rank(StallProgressionState p)=>StallProgressionRules.NodeRank(p,StallProgressionRules.AmiableTalentId);
        static void Case(string name,Action run)
        {
            var item=new Check{name=name};report.checks.Add(item);
            try{run();item.passed=true;item.detail="passed";}catch(Exception e){item.detail=e.ToString();}
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--progression24-output");
            if(i<0||i+1>=args.Length)throw new ArgumentException("--progression24-output required");
            output=Path.GetFullPath(args[i+1]);if(File.Exists(output))throw new IOException("Preserve prior report");
            Directory.CreateDirectory(Path.GetDirectoryName(output));saveDir=Path.Combine(Path.GetDirectoryName(output),"test-saves");
            if(Directory.Exists(saveDir))throw new IOException("Preserve previous test saves");
            string before=CampaignSaveStore.TestDirectory;CampaignSaveStore.TestDirectory=saveDir;report=new Report();
            try
            {
                Case("real paid order -> level -> save -> next day patience",PaidSaleAndNextDay);
                Case("real requested flavor and actual payment bonus",FlavorSale);
                Case("real unpaid risk and recovery counted once",RiskPayment);
                Case("actual shopping cash outflow and negative daily cash",PurchasedCashOutflow);
                Case("legacy save, malformed data, cap, practice isolation",SaveAndBoundaries);
                Case("failed save rolls talent purchase back",FailedSaveRollback);
            }
            finally{CampaignSaveStore.TestDirectory=before;}
            report.success=report.checks.Count==6&&report.checks.All(x=>x.passed);
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("PROGRESSION24_"+(report.success?"PASS":"FAIL")+" "+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static CampaignState Campaign(out RunState run)
        {
            var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};
            Need(profile.Validate(out string why),"Profile invalid: "+why);
            run=new RunState(4f,profile);return CampaignState.New(profile,run);
        }
        static BusinessSetup Setup(int cash,bool practice=false)
        {
            return new BusinessSetup{cashCents=cash,practice=practice,seed=12017,dayDuration=300,stoveLoaded=true,gasPoints=20,
                customerIntervalSeconds=1,queueCapacity=12,patienceMinSeconds=35,patienceMaxSeconds=54,
                equipment=new HashSet<string>{"B04","V02","U02","V15","U13","V16","U12"},
                inventory=new Dictionary<string,int>{{"egg",5},{"meat",5},{"seasoning",5},{"S01",5},{"P05",5}}};
        }
        static BusinessSession Open(BusinessSetup setup)
        {
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();Need(session.Open(),"Cannot open: "+session.Message);return session;
        }
        static BusinessSession Service(int cash,bool practice=false)=>Open(Setup(cash,practice));
        static StallOrder RealSale(BusinessSession s,bool flavored=false,int risk=0)
        {
            Need(s.AddFreeIngredient(0,"egg")&&s.AddFreeIngredient(0,"meat"),"Ingredients rejected");
            Need(s.SetFreeMethod(0,"stir_fry")&&s.StartFreeCooking(0),"Cooking rejected");
            for(int i=0;i<160&&s.Lanes[0].step==KitchenStep.Heating;i++)s.Tick(.1f);
            if(s.Lanes[0].step==KitchenStep.Utensil)Need(s.Action(0),"Utensil action rejected");
            if(s.Lanes[0].step==KitchenStep.Season)Need(s.Action(0,"seasoning"),"Seasoning rejected");
            Need(s.ProductAtLane(0)!=null&&s.Lanes[0].step==KitchenStep.Pack,"No cooked product");
            if(flavored)Need(s.Action(0,"S01"),"Flavor action rejected");
            Need(s.PackToHand(0,"P05"),"Packing rejected");var meal=s.HeldMeal;
            StallOrder order=null;
            for(int step=0;step<50;step++)
            {
                order=s.Orders.FirstOrDefault(o=>!o.served&&!o.departed&&o.productId==meal.product.id&&(o.requestedFlavorId??"")==meal.flavorId);
                if(order!=null)break;s.Tick(.2f);
            }
            Need(order!=null,"No natural customer for this product");
            Need(s.MarkCustomerArrived(order.id)||order.arrived,"Customer did not arrive");
            if(risk>0)s.ServeRiskGate=(id,recipe,price)=>new StallRiskGateDecision{adoptAsRisk=true,collectCents=price,interceptSeconds=5};
            int priceCents=meal.priceCents;
            Need(s.ServeHeldMeal(order.id),"Actual delivery failed: "+s.Message);
            Need(order.served,"Order was not marked served");
            if(risk==2)
            {
                Need(order.paidCents==0&&s.TryCompleteRiskSale(order.riskSerial,priceCents),"Risk recovery failed");
                Need(!s.TryCompleteRiskSale(order.riskSerial,priceCents),"Risk recovered twice");
            }
            else if(risk==1){Need(order.paidCents==0,"Risk falsely paid");Need(s.ResolvePendingRiskOnClose(),"Risk loss did not settle");}
            else Need(order.paidCents==priceCents&&priceCents>0,"Actual payment missing");
            return order;
        }
        static GameFlow Flow(CampaignState campaign,RunState run,out GameObject root)
        {
            root=new GameObject("Progression isolated flow");root.SetActive(false);var flow=root.AddComponent<GameFlow>();
            typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);
            typeof(GameFlow).GetProperty("Campaign",BindingFlags.Instance|BindingFlags.Public).GetSetMethod(true).Invoke(flow,new object[]{campaign});
            return flow;
        }
        static void PaidSaleAndNextDay()
        {
            var campaign=Campaign(out var run);campaign.progression=StallProgressionState.CreateDefault();campaign.progression.xpHalves=165; // declared 82.5-XP boundary fixture
            var s=Service(run.CashCents);var order=RealSale(s);Need(order.requestedFlavorId=="","Expected plain order");s.Close();
            Need(campaign.CommitDay(run,s,s.SessionId),"Day did not settle");
            Need(campaign.lastDay.progressionOrderXpHalves==5&&campaign.progression.level==2&&campaign.progression.availablePoints==1,"Paid order did not cross exact 85-XP boundary");
            string frozen=JsonUtility.ToJson(campaign);Need(!campaign.CommitDay(run,s,s.SessionId)&&JsonUtility.ToJson(campaign)==frozen,"Repeated settlement mutated XP");
            run.SetBusinessPhase(GamePhase.Night);var flow=Flow(campaign,run,out var root);
            try
            {
                Need(flow.TryUpgradeAmiableTalent(out string msg),"Talent purchase/save failed: "+msg);
                Need(Rank(campaign.progression)==1&&campaign.progression.availablePoints==0,"Talent point not spent once");
                Need(!flow.TryUpgradeAmiableTalent(out _),"Talent purchased without point");
                Need(CampaignSaveStore.Load(out var loaded,out _),"Saved campaign did not load");
                Need(loaded.campaign.progression.level==2&&Rank(loaded.campaign.progression)==1,"Saved talent missing");
                var next=campaign.CreateNextDay(4f);Need(campaign.dayIndex==2,"Next day did not advance");
                typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,next);
                var setup=flow.BuildBusinessSetup();
                Need(setup.patienceMinSeconds==38&&setup.patienceMaxSeconds==58,"Next day setup has no 8% patience effect");
                var fixture=Setup(next.CashCents);setup.stoveLoaded=fixture.stoveLoaded;setup.equipment=fixture.equipment;
                setup.inventory=fixture.inventory;setup.gasPoints=fixture.gasPoints;setup.customerIntervalSeconds=1;setup.queueCapacity=12;setup.dayDuration=300;
                var live=Open(setup); // production-built setup itself carries the unlocked patience bonus
                var nextOrder=RealSale(live);Need(nextOrder.maxPatience>=38&&nextOrder.maxPatience<58,"Actual next-day customer ignored talent");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        static void FlavorSale()
        {
            var campaign=Campaign(out var run);var s=Service(run.CashCents);var o=RealSale(s,true);
            Need(!string.IsNullOrEmpty(o.requestedFlavorId)&&o.requestedFlavorId==o.servedFlavorId,"Expected true flavor match");s.Close();
            Need(campaign.CommitDay(run,s,s.SessionId),"Flavor day failed to settle");
            Need(campaign.lastDay.progressionOrderXpHalves==5&&campaign.lastDay.progressionFlavorXpHalves==1,"Flavor XP not 2.5 + 0.5");
        }
        static void RiskPayment()
        {
            foreach(int risk in new[]{1,2})
            {
                var campaign=Campaign(out var run);var s=Service(run.CashCents);var o=RealSale(s,false,risk);s.Close();
                Need(campaign.CommitDay(run,s,s.SessionId),"Risk day failed to settle");
                Need(campaign.lastDay.progressionOrderXpHalves==(risk==1?0:5),"Unpaid/recovered risk XP incorrect");
                Need((o.paidCents>0)==(risk==2),"Risk payment fact incorrect");
            }
        }
        static void PurchasedCashOutflow()
        {
            var campaign=Campaign(out var run);run.SetBusinessPhase(GamePhase.Market);
            Need(run.AddSupply("P05"),"Actual packaging purchase could not be selected: "+run.Message);
            int cost=run.CartTotalCents;Need(cost>0&&run.Act("Cashier",true),"Actual cashier failed: "+run.Message);
            run.Tick(run.CheckoutDuration,true);Need(run.Paid&&run.SpentCents==cost,"Actual purchase did not debit");
            var s=Service(run.CashCents);s.Close();Need(campaign.CommitDay(run,s,s.SessionId),"Purchase day failed to settle");
            Need(campaign.lastDay.closingCashCents-campaign.lastDay.openingCashCents<0,"Actual purchase did not cause negative cash movement");
            Need(campaign.lastDay.progressionProfitXpHalves==0,"Negative cash movement awarded XP");
            var positive=StallProgressionRules.EvaluateDay(s,10000,15000);Need(positive.profitXpHalves==2,"Positive ¥50 cash movement did not award 1 XP");
        }
        static void SaveAndBoundaries()
        {
            var p=StallProgressionState.CreateDefault();Need(StallProgressionRules.AddXpHalves(p,169)==0&&p.level==1&&p.xpHalves==169,"84.5 XP crossed threshold");
            Need(StallProgressionRules.AddXpHalves(p,1)==1&&p.level==2&&p.availablePoints==1,"85 XP did not cross threshold");
            StallProgressionRules.AddXpHalves(p,1000000);Need(p.level==40&&p.xpHalves==0,"Max level did not cap");
            var campaign=Campaign(out var run);var practice=Service(run.CashCents,true);RealSale(practice);practice.Close();
            var ledger=new CampaignDayLedger{openingCashCents=0,closingCashCents=100000};StallProgressionRules.ApplyDaySettlement(campaign,practice,ledger);
            Need(campaign.progression==null&&ledger.progressionXpHalves==0,"Practice modified campaign progression");
            run.SetBusinessPhase(GamePhase.Night);var data=new CampaignSaveData{campaign=campaign,run=run.CaptureSave()};
            var loaded=CampaignSaveStore.Decode(CampaignSaveStore.Encode(data));Need(loaded.campaign.progression.level==1&&loaded.campaign.progression.availablePoints==0,"Legacy/default save failed");
            loaded.campaign.progression.level=41;bool rejected=false;try{CampaignSaveStore.Decode(CampaignSaveStore.Encode(loaded));}catch(InvalidDataException){rejected=true;}
            Need(rejected,"Out-of-range save accepted");
            var setup=new BusinessSetup{practice=true,patienceMinSeconds=35,patienceMaxSeconds=54};
            var bonus=Campaign(out _);bonus.progression=StallProgressionState.CreateDefault();bonus.progression.level=3;bonus.progression.availablePoints=2;
            Need(!StallProgressionRules.TryUpgradeNode(bonus,"unknown",out _)&&bonus.progression.availablePoints==2,"Unknown node consumed a point");
            Need(StallProgressionRules.RequiredBranchPoints(2)==4&&StallProgressionRules.RequiredBranchPoints(3)==10&&StallProgressionRules.RequiredBranchPoints(4)==18,"Branch tier gates differ from design");
            Need(StallProgressionRules.TryUpgradeAmiable(bonus,out _)&&StallProgressionRules.TryUpgradeAmiable(bonus,out _),"Valid node investments failed");
            Need(!StallProgressionRules.TryUpgradeAmiable(bonus,out _)&&StallProgressionRules.BranchPoints(bonus.progression,"B")==2,"Node max or branch accounting failed");
            var copy=bonus.progression.Copy();copy.nodes[0].rank=1;Need(Rank(bonus.progression)==2,"Progression copy shared node state");
            var malformed=Campaign(out var malformedRun);malformedRun.SetBusinessPhase(GamePhase.Night);malformed.progression=StallProgressionState.CreateDefault();
            malformed.progression.level=3;malformed.progression.nodes.Add(new StallTalentNodeState{id=StallProgressionRules.AmiableTalentId,rank=1});
            malformed.progression.nodes.Add(new StallTalentNodeState{id=StallProgressionRules.AmiableTalentId,rank=1});
            var invalidData=new CampaignSaveData{campaign=malformed,run=malformedRun.CaptureSave()};bool duplicateRejected=false;
            try{CampaignSaveStore.Decode(CampaignSaveStore.Encode(invalidData));}catch(InvalidDataException){duplicateRejected=true;}
            Need(duplicateRejected,"Duplicate saved node accepted");
            malformed.progression.nodes.Clear();malformed.progression.nodes.Add(new StallTalentNodeState{id="not-implemented",rank=2});bool unknownRejected=false;
            try{CampaignSaveStore.Decode(CampaignSaveStore.Encode(invalidData));}catch(InvalidDataException){unknownRejected=true;}
            Need(unknownRejected,"Unknown saved node accepted");
            StallProgressionRules.ApplyBusinessSetup(bonus,setup);Need(setup.patienceMinSeconds==35&&setup.patienceMaxSeconds==54,"Practice patience modified");
        }
        static void FailedSaveRollback()
        {
            var campaign=Campaign(out var run);campaign.progression=StallProgressionState.CreateDefault();
            campaign.progression.level=2;campaign.progression.availablePoints=1;run.SetBusinessPhase(GamePhase.Night);
            var flow=Flow(campaign,run,out var root);string previous=CampaignSaveStore.TestDirectory;
            string file=Path.Combine(Path.GetDirectoryName(output),"unwritable-save-target");File.WriteAllText(file,"not a directory");
            CampaignSaveStore.TestDirectory=file;
            try
            {
                Need(!flow.TryUpgradeAmiableTalent(out _),"Failed disk write accepted an upgrade");
                Need(Rank(campaign.progression)==0&&campaign.progression.availablePoints==1,"Failed save failed to restore original points");
            }
            finally{CampaignSaveStore.TestDirectory=previous;UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
