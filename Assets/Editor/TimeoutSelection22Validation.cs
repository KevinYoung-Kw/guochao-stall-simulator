using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Focused public-API regression for automatic market timeout selection.
    // The fixtures exercise budget/ordering and the LoadValidator contract;
    // synthetic logical capacities are not a real 3D vehicle or full playthrough.
    public static class TimeoutSelection22Validation
    {
        [Serializable] sealed class Result { public string name, error; public bool passed; }
        [Serializable] sealed class Report
        {
            public bool success;
            public string scope = "RunState public actions; 3/6/9-cell logical capacity fixtures; no scene, physical carrying, or complete player campaign";
            public int passed, failed;
            public List<Result> cases = new List<Result>();
        }
        static void Need(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
        static RunState Market()
        {
            var run = new RunState(.25f);
            Need(run.Act("StovePickup",true),"pick up stove");
            Need(run.Act("HomeCart",true),"load stove");
            Need(run.Act("HomeExit",true),"enter market");
            return run;
        }
        static void LoadInitialCargo(RunState run)
        {
            Need(run.AddSupply("rice_small"),"add existing cargo");
            Need(run.Act("Cashier",true),"start existing cargo checkout");
            run.Tick(run.CheckoutDuration+.01f,true);
            Need(run.Paid && run.Act("MarketCart",true),"load existing paid cargo");
            Need(run.Cargo.Count==1,"existing cargo not loaded");
        }
        static void Capacity(RunState run,int cells)
        {
            // Reuse the game's logical 3-column basket-fit helper to create a
            // deterministic finite capacity. Real vehicles use VehicleLoadout.
            run.LoadValidator=(equipment,cargo)=>ShoppingBasketLayout.CanFit(cargo,cells)?"":"fixture full";
        }
        static void CostAndCapacity(int cells)
        {
            var run=Market();
            LoadInitialCargo(run);
            Capacity(run,cells);
            int fillers=cells-2;
            for(int i=0;i<fillers;i++) Need(run.AddSupply("rice_small"),"add filler "+i);
            Need(run.AddSupply("prepared_small"),"add expensive item");
            Need(run.AddSupply("egg_small"),"add affordable egg");
            int initialSpent=run.SpentCents;
            int cash=fillers*run.PriceCents("rice_small")+run.PriceCents("egg_small");
            Need(run.PriceCents("prepared_small")>run.PriceCents("egg_small"),"fixture prices");
            run.SetBusinessCash(cash);
            int payments=0; run.CheckoutCompleted+=(total)=>{payments++;};
            run.Tick(run.Remaining+1,true);
            Need(run.Phase==GamePhase.Complete && run.Cart.Count==0,"timeout did not settle");
            Need(run.Cargo.Count==cells,"expected all affordable goods plus existing cargo; got "+run.Cargo.Count);
            Need(run.Cargo[0]=="rice_small" && run.Cargo[run.Cargo.Count-1]=="egg_small","lost order or affordable egg");
            Need(!run.Cargo.Contains("prepared_small"),"bought unaffordable item");
            Need(run.CashCents==0 && run.SpentCents==initialSpent+cash && payments==1,"cash or payment count incorrect");
        }
        static void FullyAffordableOrder()
        {
            var run=Market(); LoadInitialCargo(run); Capacity(run,3);
            Need(run.AddSupply("rice_small") && run.AddSupply("egg_small") && run.AddSupply("dough_small"),"basket");
            int cash=run.CashCents,spent=run.SpentCents;
            int total=run.PriceCents("rice_small")+run.PriceCents("egg_small");
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.Count==3 && run.Cargo[1]=="rice_small" && run.Cargo[2]=="egg_small","first fitting items must keep original basket order");
            Need(run.CashCents==cash-total && run.SpentCents==spent+total,"incorrect fully affordable settlement");
        }
        static void FullyAffordableNoLoss()
        {
            var run=Market(); Capacity(run,6);
            Need(run.AddSupply("rice_small") && run.AddSupply("egg_small") && run.AddSupply("dough_small"),"basket");
            int cash=run.CashCents, spent=run.SpentCents, total=run.CartTotalCents;
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.Count==3 && run.Cargo[0]=="rice_small" && run.Cargo[1]=="egg_small" && run.Cargo[2]=="dough_small","fully affordable goods/order changed");
            Need(run.CashCents==cash-total && run.SpentCents==spent+total,"incorrect all-fit payment");
        }
        static void OversizedFirst()
        {
            var run=Market();
            Need(run.AddSupply("B03") && run.AddSupply("rice_small"),"basket");
            run.LoadValidator=(equipment,cargo)=>cargo.Contains("B03")?"large item does not fit":"";
            int cash=run.CashCents,spent=run.SpentCents,price=run.PriceCents("rice_small");
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.Count==1 && run.Cargo[0]=="rice_small","large item occupied money or space after rejection");
            Need(run.CashCents==cash-price && run.SpentCents==spent+price,"large item was charged");
        }
        static void NoValidatorBudget()
        {
            var run=Market();
            Need(run.LoadValidator==null,"fixture unexpectedly has vehicle validator");
            Need(run.AddSupply("rice_small") && run.AddSupply("prepared_small") && run.AddSupply("egg_small"),"basket");
            int budget=run.PriceCents("rice_small")+run.PriceCents("egg_small");run.SetBusinessCash(budget);
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.SequenceEqual(new[]{"rice_small","egg_small"}),"no-validator budget order changed");
            Need(run.CashCents==0 && run.SpentCents==budget,"no-validator payment mismatch");
        }
        static void ZeroBudget()
        {
            var run=Market();Capacity(run,3);
            Need(run.AddSupply("rice_small") && run.AddSupply("egg_small"),"basket");run.SetBusinessCash(0);
            int payments=0;run.CheckoutCompleted+=(total)=>{payments++;};
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.Count==0 && run.Cart.Count==0 && run.CashCents==0 && run.SpentCents==0 && payments==0,"zero budget produced goods or a payment");
            Need(run.Phase==GamePhase.Complete,"zero budget failed to progress");
        }
        static void ConfirmedSelectionGuard()
        {
            var run=Market(); Capacity(run,3);
            Need(run.AddSupply("egg_small") && run.AddSupply("rice_small"),"basket");
            int cash=run.CashCents,price=run.PriceCents("egg_small");
            Need(run.ConfirmCheckoutSelection(new[]{"egg_small"}),"confirm selection");
            run.Tick(run.Remaining+1,true);
            Need(run.Cargo.Count==1 && run.Cargo[0]=="egg_small","unselected item was bought");
            Need(run.CashCents==cash-price && run.SpentCents==price,"selected item charged incorrectly");
        }
        static void SaveBeforeTimeout()
        {
            var original=Market(); LoadInitialCargo(original); Capacity(original,3);
            Need(original.AddSupply("rice_small") && original.AddSupply("prepared_small") && original.AddSupply("egg_small"),"basket");
            original.SetBusinessCash(original.PriceCents("rice_small")+original.PriceCents("egg_small"));
            var saved=JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(original.CaptureSave()));
            var restored=RunState.Restore(saved);restored.Paused=false;Capacity(restored,3);
            original.Tick(original.Remaining+1,true);restored.Tick(restored.Remaining+1,true);
            Need(restored.Cargo.SequenceEqual(original.Cargo),"save/load changed retained goods or order");
            Need(restored.CashCents==original.CashCents && restored.SpentCents==original.SpentCents,"save/load changed payment");
            Need(restored.Cargo.Last()=="egg_small","affordable later item lost after save/load");
        }
        static void PaidCargoRetainedOnLoadFailure()
        {
            var run=Market(); Need(run.AddSupply("egg_small"),"basket");
            int cash=run.CashCents,price=run.PriceCents("egg_small");
            Need(run.Act("Cashier",true),"checkout"); run.Tick(run.CheckoutDuration+.01f,true);
            Need(run.Paid && run.CashCents==cash-price,"first payment");
            run.LoadValidator=(equipment,cargo)=>"fixture full";
            run.Tick(run.Remaining+1,true);
            Need(run.Paid && run.Cart.Count==1 && run.PendingPaidLoad,"failed load destroyed paid item");
            var saved=JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(run.CaptureSave()));
            var restored=RunState.Restore(saved);restored.Paused=false;restored.LoadValidator=(equipment,cargo)=>"";
            Need(restored.PendingPaidLoad && restored.Act("MarketCart",true),"paid item cannot be loaded after restore");
            Need(restored.Cargo.Count==1 && restored.Cargo[0]=="egg_small" && restored.CashCents==cash-price && restored.SpentCents==price,"paid item lost or charged twice");
        }
        static void SavedInterruptedSelectionStaysExclusive()
        {
            var run=Market();
            Need(run.AddSupply("egg_small") && run.AddSupply("rice_small"),"basket");
            int cash=run.CashCents;
            Need(run.ConfirmCheckoutSelection(new[]{"egg_small"}),"confirm");
            var saved=JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(run.CaptureSave()));
            var restored=RunState.Restore(saved);restored.Paused=false;Capacity(restored,3);
            restored.Tick(restored.Remaining+1,true);
            Need(restored.Cargo.Count==0 && restored.Cart.Count==0 && restored.CashCents==cash && restored.SpentCents==0,"interrupted selection bought goods after reload");
        }
        [MenuItem("Guochao/Validate timeout selection v22")]
        public static void Run()
        {
            var report=new Report();
            void Test(string name,Action action)
            {
                try { action(); report.cases.Add(new Result{name=name,passed=true});report.passed++; }
                catch(Exception e) {report.cases.Add(new Result{name=name,passed=false,error=e.ToString()});report.failed++;}
            }
            Test("affordable-after-unaffordable-capacity-3",()=>CostAndCapacity(3));
            Test("affordable-after-unaffordable-capacity-6",()=>CostAndCapacity(6));
            Test("affordable-after-unaffordable-capacity-9",()=>CostAndCapacity(9));
            Test("fully-affordable-original-order-with-limit",FullyAffordableOrder);
            Test("fully-affordable-all-fit",FullyAffordableNoLoss);
            Test("oversized-does-not-consume-budget",OversizedFirst);
            Test("no-validator-still-respects-budget",NoValidatorBudget);
            Test("zero-budget-no-payment",ZeroBudget);
            Test("confirmed-selection-remains-exclusive",ConfirmedSelectionGuard);
            Test("save-before-timeout-same-result",SaveBeforeTimeout);
            Test("paid-cargo-retained-on-load-failure",PaidCargoRetainedOnLoadFailure);
            Test("saved-interrupted-selection-remains-exclusive",SavedInterruptedSelectionStaysExclusive);
            report.success=report.failed==0;
            string[] args=Environment.GetCommandLineArgs();
            int at=Array.IndexOf(args,"--timeout22-report");
            string path=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v22/claude-basics/timeout22-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("TimeoutSelection22Validation: "+report.passed+" passed, "+report.failed+" failed; "+path);
            if(Application.isBatchMode) EditorApplication.Exit(report.success?0:1);
        }
    }
}
