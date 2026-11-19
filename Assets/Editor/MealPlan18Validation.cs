using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao
{
    // PLAN18 batch validation. Runs headless via -executeMethod
    // Guochao.MealPlan18Validation.Run. Writes a JSON report to the path
    // after --meal-plan18-report on the command line, or to a temp file next
    // to the project when the flag is absent. Any failed assertion throws.
    public static class MealPlan18Validation
    {
        [Serializable] sealed class Report { public bool pass; public string[] checks = Array.Empty<string>(); public string error = ""; }

        static readonly List<string> checks = new List<string>();

        public static void Run()
        {
            checks.Clear();
            var report = new Report();
            try
            {
                SharedIngredientsMergeAcrossDishes();
                SharedPackagesNotDoubleAllocated();
                OwnedPackagesNeedNoPurchase();
                BasketPackagesCoverPlanWithoutExtraBuy();
                HomeGearWaitsForLoadingAndRealZeroFuel();
                HomeGasSevenStillNotCookable();
                NoPlanStillReportsCookable();
                LegacySaveWithoutMealPlanRestoresEmpty();
                SaveRoundTripDeepCopy();
                ResourceInfoDoesNotAddAnotherPurchasePrice();
                HomeStoveWithOneMissingToolIsReused();
                SinglePackageAlreadyInBasketCannotBeQuotedAgain();
                UnresolvedEquipmentRemainsVisibleWithoutPrice();
                MultipleBasesDoNotInventAnEnergyQuote();
                report.pass = true;
            }
            catch (Exception error)
            {
                report.pass = false;
                report.error = error.ToString();
            }
            report.checks = checks.ToArray();
            string json = JsonUtility.ToJson(report, true);
            string path = ReportPath();
            File.WriteAllText(path, json);
            Debug.Log("MealPlan18 report: " + path + "\n" + json);
            if (!report.pass) throw new InvalidOperationException("MealPlan18 validation failed: " + report.error);
        }

        static string ReportPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--meal-plan18-report") return args[i + 1];
            return Path.Combine(Application.dataPath, "..", "mealplan18-report.json");
        }

        static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("assertion failed: " + name);
            checks.Add("PASS " + name);
        }

        static RunState NewRun()
        {
            // Profile-less run: HomeEquipment={B04}. All stock/equipment below
            // is arranged through public lists or a legal Restore only.
            return new RunState(0.25f, null);
        }

        static int IndexOfDish(string id)
        {
            for (int i = 0; i < BusinessSession.Recipes.Length; i++)
                if (BusinessSession.Recipes[i].id == id) return i;
            return -1;
        }

        static void SinglePackageAlreadyInBasketCannotBeQuotedAgain()
        {
            var run = NewRun();
            run.HomeEquipment.Clear();
            run.LoadedEquipment.AddRange(new[] { "B02", "V13", "U08", "U10" });
            run.Cargo.Add("E02");
            run.Cart.Add("P03");
            run.Supplies["skewer"] = 99;
            run.Supplies["seasoning"] = 99;
            run.SetMealPlan("fried_skewer", 99);
            var report = MealPlan.Build(run);
            var pack = report.lines.Find(line => line.key == "P03");
            Check(pack != null && pack.required == 99 && pack.inBasket == 50 && pack.missing == 49,
                "99 skewers retain the real 49-pack shortage after the basket's fifty");
            Check(pack.estimatedCents == 0 && report.estimatedCents == 0 && pack.note.Contains("不能重复"),
                "unbuyable second P03 is explained and never quoted");
        }

        static void UnresolvedEquipmentRemainsVisibleWithoutPrice()
        {
            var run = NewRun();
            run.HomeEquipment.Clear();
            run.SetMealPlan("rose_jelly", 5);
            var report = MealPlan.Build(run);
            var equipment = report.lines.Find(line => line.key == "equipment:rose_jelly");
            Check(equipment != null && equipment.estimateUncertain && equipment.equipment && equipment.estimatedCents == 0,
                "unavailable full ice-jelly equipment combination remains a visible unpriced gap");
            Check(equipment.note.Contains(BusinessSession.Name("V16")),
                "unresolved equipment names the vessel from production Missing");
            Check(!report.summary.Contains("缺什么都不耽误"), "summary does not promise opening without requirements");
        }

        static void MultipleBasesDoNotInventAnEnergyQuote()
        {
            var run = NewRun();
            run.HomeEquipment.Clear();
            run.LoadedEquipment.AddRange(new[] { "B02", "B04", "V01", "U01", "E03" });
            run.Supplies["rice"] = 10;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P05"] = 10;
            run.SetMealPlan("plain_rice", 5);
            var report = MealPlan.Build(run);
            var fuel = report.lines.Find(line => line.key == "energy:plain_rice");
            Check(fuel != null && fuel.estimateUncertain && fuel.estimatedCents == 0,
                "multiple eligible burners expose an unpriced energy estimate instead of guessing");
            Check(!report.lines.Exists(line => (line.key == "gas" || line.key == "cartridge") && line.estimatedCents > 0),
                "uncertain burner choice adds no refill quotation");
            var empty = MealPlan.Build(NewRun());
            Check(!empty.summary.Contains("没有需要补的货"), "an empty plan does not claim supplies are complete");
        }

        static void ResourceInfoDoesNotAddAnotherPurchasePrice()
        {
            var run = NewRun();
            run.HomeEquipment.Clear();
            run.LoadedEquipment.AddRange(new[] { "B02", "V01", "U01" });
            run.Cargo.Add("E02");
            run.Supplies["rice"] = 5;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P05"] = 10;
            run.SetMealPlan("plain_rice", 10);
            var report = MealPlan.Build(run);
            var rice = report.lines.Find(line => line.key == "rice");
            Check(rice != null && rice.missing == 5 && rice.productId == "rice_small",
                "five rice portions missing: suggest the actual five-portion SKU");
            int expected = run.PriceCents("rice_small");
            Check(rice.estimatedCents == expected && report.estimatedCents == expected,
                "information anchor does not charge the full rice carton before the small SKU");
        }

        static void HomeStoveWithOneMissingToolIsReused()
        {
            var run = NewRun();
            run.HomeEquipment.Clear();
            run.HomeEquipment.AddRange(new[] { "B04", "V01", "E03" });
            run.Supplies["rice"] = 10;
            run.Supplies["egg"] = 10;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P02"] = 10;
            var saved = run.CaptureSave();
            saved.gas = 7;
            run = RunState.Restore(saved);
            run.SetMealPlan("fried_rice", 3);
            var report = MealPlan.Build(run);
            var tool = report.lines.Find(line => line.key == "U01");
            Check(tool != null && tool.missing == 1 && tool.estimatedCents == run.PriceCents("U01"),
                "home B04 and V01 need only one missing U01");
            Check(!report.lines.Exists(line => line.equipment && line.key.StartsWith("B", StringComparison.Ordinal) && line.estimatedCents > 0),
                "home B04 is not discarded and replaced with a new burner");
            Check(report.estimatedCents == run.PriceCents("U01") && run.GasPoints == 7,
                "one-tool estimate reuses actual seven gas points without refilling");
        }

        // 1) Two dishes that both consume rice: required totals must merge.
        static void SharedIngredientsMergeAcrossDishes()
        {
            var run = NewRun();
            Check(run.SetMealPlan("fried_rice", 3), "set fried_rice x3");
            Check(run.SetMealPlan("plain_rice", 2), "set plain_rice x2");
            var report = MealPlan.Build(run);
            var rice = report.lines.Find(l => l.key == "rice");
            Check(rice != null, "rice line exists");
            Check(rice.required == 5, "rice required merged to 5, got " + rice.required);
        }

        // 2) Owned P05 packs must be allocated across dishes without reuse.
        static void SharedPackagesNotDoubleAllocated()
        {
            var run = NewRun();
            run.Supplies["P05"] = 5;
            Check(run.SetMealPlan("fried_rice", 5), "set fried_rice x5");
            Check(run.SetMealPlan("plain_rice", 5), "set plain_rice x5");
            var report = MealPlan.Build(run);
            int total = 0;
            foreach (var line in report.lines)
                if (line.key.StartsWith("P", StringComparison.Ordinal)) total += line.required;
            Check(total == 10, "package demand totals 10 across both dishes, got " + total);
            var p05 = report.lines.Find(l => l.key == "P05");
            Check(p05 != null && p05.owned == 5, "P05 owned=5 surfaced");
            var extra = report.lines.Find(l => l.key.StartsWith("P", StringComparison.Ordinal) && l.key != "P05");
            Check(extra != null && extra.missing > 0, "shortfall stays visible as a second package line");
        }

        // 3) Fully owned packaging costs nothing and adds no phantom line.
        static void OwnedPackagesNeedNoPurchase()
        {
            var run = NewRun();
            run.Supplies["P05"] = 10;
            Check(run.SetMealPlan("fried_rice", 5), "set fried_rice x5");
            Check(run.SetMealPlan("plain_rice", 5), "set plain_rice x5");
            var report = MealPlan.Build(run);
            var p05 = report.lines.Find(l => l.key == "P05");
            Check(p05 != null && p05.required == 10 && p05.missing == 0 && p05.estimatedCents == 0,
                "owned P05 covers all 10 with zero cost");
            Check(!report.lines.Exists(l => l.key.StartsWith("P", StringComparison.Ordinal) && l.key != "P05" && l.estimatedCents > 0),
                "no phantom P02 pricing when stock is sufficient");
            Check(report.estimatedCents == 0 || !report.lines.Exists(l => l.key == "P02"),
                "report total unaffected by unneeded package buys");
        }

        // 4) An unpaid basket pack joins the pool before any new SKU is named.
        static void BasketPackagesCoverPlanWithoutExtraBuy()
        {
            var run = NewRun();
            run.Cart.Add("P05"); // unpaid basket, 50 portions
            Check(run.SetMealPlan("fried_rice", 5), "set fried_rice x5");
            var report = MealPlan.Build(run);
            var p05 = report.lines.Find(l => l.key == "P05");
            Check(p05 != null && p05.inBasket == 50 && p05.missing == 0 && p05.estimatedCents == 0,
                "basket P05 absorbed by the pool first");
            Check(!report.lines.Exists(l => l.key == "P02"),
                "no P02 suggestion while the basket already covers the plan");
        }

        // 5) Gear at home waits for loading; a real zero gas pool stays zero —
        //    nothing is re-bought and no fuel is invented.
        static void HomeGearWaitsForLoadingAndRealZeroFuel()
        {
            var run = NewRun(); // B04 already at home
            run.HomeEquipment.Add("V01");
            run.HomeEquipment.Add("U01");
            run.HomeEquipment.Add("E03");
            run.Supplies["rice"] = 10;
            run.Supplies["egg"] = 10;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P02"] = 10;
            Check(run.SetMealPlan("fried_rice", 3), "set fried_rice x3");
            var report = MealPlan.Build(run);
            foreach (var line in report.lines)
                if ((line.key == "B04" || line.key == "V01" || line.key == "E03") && line.estimatedCents > 0)
                    Check(false, "home gear must not be re-bought: " + line.key);
            var b04 = report.lines.Find(l => l.key == "B04" && l.channel == "home");
            var v01 = report.lines.Find(l => l.key == "V01" && l.channel == "home");
            Check(b04 != null && b04.owned == 1, "B04 surfaced as home-owned, waiting to load");
            Check(v01 != null && v01.owned == 1, "V01 surfaced as home-owned, waiting to load");
            var gas = report.lines.Find(l => l.key == "gas");
            Check(gas != null, "gas line exists for the real shortage");
            Check(gas.owned == 0, "real gas pool is zero, got " + gas.owned);
            Check(gas.missing > 0, "gas shortage is shown");
            Check(gas.estimatedCents == 0, "empty-tank E03 cannot be re-bought, no fake price");
            Check(!report.cookableNow.Contains(IndexOfDish("fried_rice")),
                "home gear is not treated as loaded for cookability");
        }

        // 6) Seven real gas points with E03 at home: the line shows 7, yet the
        //    dish still cannot cook because the stove never left the house.
        static void HomeGasSevenStillNotCookable()
        {
            var run = NewRun();
            run.HomeEquipment.Add("V01");
            run.HomeEquipment.Add("U01");
            run.HomeEquipment.Add("E03");
            run.Supplies["rice"] = 10;
            run.Supplies["egg"] = 10;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P02"] = 10;
            var data = run.CaptureSave(); // legal payload, then the real fuel:
            data.gas = 7;
            data.homeEquipment = new[] { "B04", "V01", "U01", "E03" };
            data.loadedEquipment = new string[0];
            var restored = RunState.Restore(data);
            Check(restored.GasPoints == 7, "restore keeps the seven gas points");
            Check(restored.SetMealPlan("fried_rice", 3), "set fried_rice x3 on restored run");
            var report = MealPlan.Build(restored);
            var gas = report.lines.Find(l => l.key == "gas");
            Check(gas != null && gas.owned == 7, "line shows the real 7-point pool, got " + (gas == null ? "no line" : gas.owned.ToString()));
            Check(gas.missing == 0, "seven points cover three batches");
            Check(!report.cookableNow.Contains(IndexOfDish("fried_rice")),
                "seven points at home still do not cook: the stove is not aboard");
        }

        // 7) No plan at all: everything truly cookable still shows up.
        static void NoPlanStillReportsCookable()
        {
            var run = NewRun();
            run.LoadedEquipment.Add("B02");
            run.LoadedEquipment.Add("V01");
            run.LoadedEquipment.Add("U01");
            run.Cargo.Add("E02");
            run.Supplies["egg"] = 5;
            run.Supplies["seasoning"] = 10;
            run.Supplies["P05"] = 5;
            var report = MealPlan.Build(run); // no SetMealPlan at all
            Check(report.cookableNow.Contains(IndexOfDish("egg_greens")),
                "egg_greens cookable without any plan");
            Check(report.summary.Contains("手头可做"), "empty-plan summary points at on-hand dishes");
            Check(!report.summary.Contains("把菜加入计划"), "no 'add a dish first' wording");
        }

        // 8) A legacy save JSON without the mealPlan field restores an empty
        //    plan instead of throwing; dirty entries are dropped.
        static void LegacySaveWithoutMealPlanRestoresEmpty()
        {
            var run = NewRun();
            run.SetMealPlan("fried_rice", 2);
            string json = JsonUtility.ToJson(run.CaptureSave());
            Check(json.Contains("mealPlan"), "captured save serializes the plan field");
            var legacy = NewRun();
            var data = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(legacy.CaptureSave()));
            data.mealPlan = null; // old files simply lack the array
            var restored = RunState.Restore(data);
            Check(restored.MealPlan.Count == 0, "legacy null mealPlan restores empty");
            var dirty = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(run.CaptureSave()));
            dirty.mealPlan = new[] { new MealPlanChoice { recipeId = "no_such_dish", portions = 2 },
                                     new MealPlanChoice { recipeId = "fried_rice", portions = 0 } };
            var cleaned = RunState.Restore(dirty);
            Check(cleaned.MealPlan.Count == 0, "unknown id and zero portions safely ignored");
        }

        // 9) Capture → JSON → Restore keeps the plan and deep-copies it.
        static void SaveRoundTripDeepCopy()
        {
            var run = NewRun();
            Check(run.SetMealPlan("soup", 3), "set soup x3");
            Check(!run.SetMealPlan("ghost_dish", 1), "unknown recipe rejected");
            Check(!run.SetMealPlan("soup", -1), "negative portions rejected");
            run.SetMealPlan("soup", 5);
            Check(run.MealPlan.Count == 1 && run.MealPlan[0].portions == 5, "update-in-place");
            var data = run.CaptureSave();
            Check(data.mealPlan != null && data.mealPlan.Length == 1, "captured mealPlan present");
            data.mealPlan[0].portions = 99; // mutating the save must not leak
            Check(run.MealPlan[0].portions == 5, "capture is a deep copy");
            var restored = RunState.Restore(JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(data)));
            Check(restored.MealPlan.Count == 1 && restored.MealPlan[0].recipeId == "soup", "restore keeps planned dish");
            Check(!ReferenceEquals(restored.MealPlan[0], data.mealPlan[0]), "restore copies entries, no shared refs");
            Check(restored.Cart.Count == 0 && restored.CashCents == run.CashCents, "plan never buys or spends");
            run.SetMealPlan("soup", 0);
            Check(run.MealPlan.Count == 0, "portions=0 deletes");
        }
    }
}
