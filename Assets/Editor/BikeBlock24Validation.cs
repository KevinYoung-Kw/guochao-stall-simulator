using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Deterministic session-level regression. The first sale uses the real
    // recipe, inventory and payment path; no balance or inventory injection.
    // It does not claim a human-played shift or visual/input acceptance.
    public static class BikeBlock24Validation
    {
        [Serializable] private sealed class Report
        {
            public bool success;
            public string scope="Real BusinessSession transactions, saved-state round trip and risk mutual exclusion; no graphical or hardware-input acceptance.";
            public int assertions;
            public string failure="";
            public int seed;
        }
        private static int assertions;
        private static void Need(bool condition,string what)
        {if(!condition)throw new Exception(what);assertions++;}

        internal static BusinessSession MakeSession(int seed)
        {
            var setup=new BusinessSetup{
                seed=seed,practice=true,stoveLoaded=true,cashCents=15000,gasPoints=45,
                dayDuration=300,clearDownSeconds=15,customerIntervalSeconds=12,
                patienceMinSeconds=70,patienceMaxSeconds=90,
                equipment=new HashSet<string>{"B03","V01","U01"},
                inventory=new Dictionary<string,int>{{"rice",15},{"egg",15},{"seasoning",20},{"P05",20}}};
            var s=new BusinessSession(setup);
            s.SelectedRecipes.Clear();s.SelectedRecipes.Add(0);
            Need(s.Open(),"Could not open real session: "+s.Message);
            return s;
        }
        internal static void Tick(BusinessSession s,float dt)
        {s.Tick(dt);s.RiskState.Tick(dt);}

        internal static void Sale(BusinessSession s)
        {
            StallOrder order=null;
            for(int i=0;i<500&&order==null;i++)
            {
                order=s.Orders.Find(o=>!o.served&&!o.departed&&o.recipeIndex==0);
                if(order==null)Tick(s,.05f);
            }
            Need(order!=null,"A naturally generated order was expected");
            if(!order.arrived)Need(s.MarkCustomerArrived(order.id),"Could not mark naturally generated customer as arrived");
            int served=s.Served;
            Need(s.BeginOrder(order.id,0),"First egg step failed: "+s.Message);
            Need(s.AddRiceForOrder(order.id,0),"Second rice step failed: "+s.Message);
            for(int i=0;i<800&&s.Lanes[0].step!=KitchenStep.Pack;i++)
            {
                if(s.Lanes[0].step==KitchenStep.Utensil||s.Lanes[0].step==KitchenStep.Season)s.Action(0);
                Tick(s,.05f);
            }
            Need(s.Lanes[0].step==KitchenStep.Pack,"Recipe did not reach packaging: "+s.Message);
            int before=s.CashCents;Need(s.ServeToOrder(0,"P05",order.id),"Real first sale failed: "+s.Message);
            Need(s.Served==served+1&&s.CashCents>before&&s.CaptureAudit().cashConserved,"Sale must pay once and conserve cash");
        }
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--bike24-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --bike24-output <path>");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Refusing to replace earlier report");
            var report=new Report();
            try
            {
                BusinessSession active=null;
                for(int seed=12017;seed<12032&&active==null;seed++)
                {
                    var s=MakeSession(seed);Sale(s);
                    for(int i=0;i<5000&&!s.BikeBlocked&&!s.Finished;i++)Tick(s,.05f);
                    if(s.BikeBlocked){active=s;report.seed=seed;}
                }
                Need(active!=null,"No natural B05 trigger found in deterministic seed range");
                var a=active;Need(a.BikeVisibleMask==3&&a.BikeBlockRemaining==2,"Two real bike states must be active");
                Need(Mathf.Approximately(a.CustomerArrivalRateMultiplier,.5f),"Active bike event must halve arrival-clock rate");
                Need(a.RiskState.IcdRemaining>0,"B05 must reserve the shared A01 cooldown");
                int cash=a.CashCents,rice=a.Count("rice"),egg=a.Count("egg");
                var before=a.CaptureSave();float timer=before.nextCustomer;
                if(timer>.2f){Tick(a,.1f);float delta=timer-a.CaptureSave().nextCustomer;Need(Mathf.Abs(delta-.05f)<.001f,"Arrival clock did not move at half speed: "+delta);}
                // With a realistic sale in progress, B05 must suppress A01's
                // gate rather than losing the sale or creating an overlapping debt.
                var whileBlocked=BusinessSession.Restore(a.CaptureSave());int gateCalls=0;
                whileBlocked.ServeRiskGate=(order,recipe,price)=>{gateCalls++;return new StallRiskGateDecision(true,price,3f);};
                Sale(whileBlocked);
                Need(gateCalls==0&&!whileBlocked.RiskState.HasActiveEvent&&whileBlocked.BikeBlocked,"B05 must exclude A01 while a real sale still succeeds");
                // Position a normal A01 in its 3s window just before the B05
                // poll becomes due; no real player payment is mutated by this
                // isolated state fixture.
                var riskFirst=MakeSession(12017);Sale(riskFirst);
                for(int i=0;i<380;i++)Tick(riskFirst,.05f);
                Need(riskFirst.BikeBlockPollRemaining>0&&!riskFirst.BikeBlocked,"Unexpected bicycle event before poll");
                var pending=riskFirst.RiskState.Adopt("A01",1,0,"fried_rice",1000,3f,1000);
                for(int i=0;i<30;i++)Tick(riskFirst,.05f);
                Need(!riskFirst.BikeBlocked&&riskFirst.RiskState.HasActiveEvent&&riskFirst.BikeBlockPollRemaining<=.001f,"An active A01 must prevent B05 at the poll");
                Need(riskFirst.RiskState.MarkResolved(pending.Serial,false),"Could not close isolated risk fixture");
                Need(a.TryMoveBike(1),"Visible bike 1 should move");
                Need(a.BikeVisibleMask==1&&a.BikeBlockRemaining==1,"First action must remove only selected bike");
                Need(!a.TryMoveBike(1)&&a.BikeVisibleMask==1,"Removed bike cannot be cleared a second time");
                var saved=a.CaptureSave();var restored=BusinessSession.Restore(JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(saved)));
                Need(restored.BikeBlocked&&restored.BikeVisibleMask==1&&restored.BikeBlockRemaining==1,"Save/load lost remaining bicycle");
                Need(restored.TryMoveBike(0)&&!restored.BikeBlocked&&restored.BikeVisibleMask==0,"Second distinct click must clear event");
                Need(Mathf.Approximately(restored.CustomerArrivalRateMultiplier,1f),"Cleared event must restore arrival-clock rate");
                Need(restored.CashCents==cash&&restored.Count("rice")==rice&&restored.Count("egg")==egg,"Moving bicycles unexpectedly changed money or ingredients");
                var old=restored.CaptureSave();old.bikeBlockVersion=0;old.bikeBlockMask=0;old.bikeBlockRemaining=0;old.bikeBlockActive=false;old.bikeBlockTriggered=false;old.bikeBlockPollRemaining=0;
                var migrated=BusinessSession.Restore(old);Need(!migrated.BikeBlocked&&!migrated.BikeBlockTriggeredToday,"Legacy save must restore without an active bicycle event");
                Need(migrated.CashCents==restored.CashCents&&migrated.Count("rice")==restored.Count("rice"),"Legacy migration changed money or stock");
                a.Close();Need(!a.BikeBlocked,"Closing should clear active event");
                report.success=true;
            }
            catch(Exception error){report.failure=error.ToString();Debug.LogError(report.failure);}
            report.assertions=assertions;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
