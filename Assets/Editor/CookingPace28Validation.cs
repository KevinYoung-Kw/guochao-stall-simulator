using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao.Editor
{
    public static class CookingPace28Validation
    {
        const float ExpectedFreeWindowSeconds = 20f;
        const float WindowToleranceSeconds = 0.05f;

        [Serializable]
        sealed class Report
        {
            public bool success;
            public int checks;
            public List<string> failures = new List<string>();
        }

        static Report report;

        static void Need(bool ok, string name)
        {
            report.checks++;
            if (!ok) report.failures.Add(name);
        }

        public static void Run()
        {
            report = new Report();
            try
            {
                FreeGriddleTwentySecondWindowAndPlateOut();
                FreeGriddleBurnAfterWindowCountsWasteOnce();
                V04AutoWokAndColdWaitNoBurn();
                LegacySaveUtensilWindowPreservedOnRestore();
            }
            catch (Exception ex)
            {
                report.failures.Add(ex.ToString());
            }

            report.success = report.failures.Count == 0;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--cooking-pace28-output");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("--cooking-pace28-output required");

            string path = Path.GetFullPath(args[index + 1]);
            if (File.Exists(path))
                throw new IOException("Existing evidence retained");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success)
                throw new Exception("Cooking pace 28 validation failed: " + string.Join("; ", report.failures));
        }

        static BusinessSession Open(Action<BusinessSetup> configure = null)
        {
            var setup = BusinessSetup.MidgamePractice();
            setup.customerIntervalSeconds = 9999;
            configure?.Invoke(setup);
            var session = new BusinessSession(setup);
            session.SelectedRecipes.Clear();
            Need(session.Open(), "MidgamePractice session opens cleanly");
            return session;
        }

        static void TickSeconds(BusinessSession session, float seconds, float step = 0.25f)
        {
            int steps = Mathf.CeilToInt(seconds / step);
            float remaining = seconds;
            for (int i = 0; i < steps && remaining > 0f; i++)
            {
                float dt = Mathf.Min(step, remaining);
                session.Tick(dt);
                remaining -= dt;
            }
        }

        static void FreeGriddleTwentySecondWindowAndPlateOut()
        {
            var s = Open();
            int eggBefore = s.Inventory["egg"];
            int gasBefore = s.Gas;
            int cashBefore = s.CashCents;

            Need(s.AddFreeIngredient(0, "egg"), "Real egg added to free lane 0");
            Need(s.Inventory["egg"] == eggBefore - 1 && s.Gas == gasBefore && s.CashCents == cashBefore,
                "AddFreeIngredient debits exactly 1 egg without altering gas or cash");
            Need(s.Lanes[0].step == KitchenStep.Prep && s.Lanes[0].freeBatch != null,
                "Free lane enters Prep with live batch");

            Need(s.SetFreeMethod(0, FreeKitchenRules.MethodGriddle), "Free method switched to griddle");
            Need(s.Lanes[0].freeBatch.method == FreeKitchenRules.MethodGriddle
                && s.Inventory["egg"] == eggBefore - 1 && s.Gas == gasBefore && s.CashCents == cashBefore,
                "SetFreeMethod binds griddle without extra inventory, gas, or cash debit");

            float expectedHeat = FreeKitchenRules.HeatSeconds(s.Lanes[0].freeBatch.inputs);
            Need(s.StartFreeCooking(0), "Free griddle cooking started");
            Need(s.Lanes[0].step == KitchenStep.Heating
                && s.Gas == gasBefore - 1
                && s.Inventory["egg"] == eggBefore - 1
                && s.CashCents == cashBefore
                && s.Lanes[0].batchRemaining == 1,
                "StartFreeCooking enters Heating, debits 1 gas, keeps egg/cash intact, and tracks 1 portion");
            Need(Mathf.Abs(s.Lanes[0].duration - expectedHeat) <= 0.01f,
                "Heating duration matches FreeKitchenRules.HeatSeconds");

            Need(!s.Action(0), "Early Action during Heating returns false");
            Need(s.Lanes[0].step == KitchenStep.Heating
                && s.ProductAtLane(0) == null
                && s.Lanes[0].freeBatch != null
                && s.Lanes[0].batchRemaining == 1,
                "Early Action during Heating produces no dish and stays in Heating");

            float heatingGreenWindow = s.GreenWindowSeconds(0);

            for (int i = 0; i < 100 && s.Lanes[0].step == KitchenStep.Heating; i++)
                s.Tick(0.1f);

            Need(s.Lanes[0].step == KitchenStep.Utensil, "Free griddle reaches Utensil after heating");
            float actualDuration = s.Lanes[0].duration;
            float utensilGreenWindow = s.GreenWindowSeconds(0);

            Need(Mathf.Abs(actualDuration - ExpectedFreeWindowSeconds) <= WindowToleranceSeconds,
                "Free griddle Utensil duration is ~20s (actual=" + actualDuration.ToString("0.00") + ")");
            Need(Mathf.Abs(s.Lanes[0].remaining - actualDuration) <= WindowToleranceSeconds,
                "Free griddle starts Utensil with full window remaining");
            Need(Mathf.Abs(heatingGreenWindow - actualDuration) <= WindowToleranceSeconds,
                "GreenWindowSeconds during Heating matches actual Utensil lane.duration (heating="
                + heatingGreenWindow.ToString("0.00") + ", actual=" + actualDuration.ToString("0.00") + ")");
            Need(Mathf.Abs(utensilGreenWindow - actualDuration) <= WindowToleranceSeconds,
                "GreenWindowSeconds during Utensil matches lane.duration");

            TickSeconds(s, 10f, 0.25f);
            Need(s.Lanes[0].step == KitchenStep.Utensil,
                "After waiting 10 real seconds, free griddle is still in Utensil");
            Need(s.Lanes[0].remaining > 9.5f && s.Lanes[0].remaining < 10.5f,
                "After waiting 10 real seconds, ~10s remain in Utensil window (remaining="
                + s.Lanes[0].remaining.ToString("0.00") + ")");
            Need(s.WastedPortions == 0, "No portion wasted during 10s wait inside 20s window");

            Need(s.Action(0), "Action plates out free griddle egg after 10s wait");
            FoodProduct product = s.ProductAtLane(0);
            Need(product != null, "Real FoodProduct exists after plating out");
            Need(product != null
                && product.method == FreeKitchenRules.MethodGriddle
                && product.portions == 1
                && product.inputs != null
                && product.inputs.Length == 1
                && product.inputs[0].itemId == "egg"
                && product.inputs[0].units == 1,
                "Plated product preserves real griddle method and egg input");
            Need(s.Lanes[0].step == KitchenStep.Season && s.WastedPortions == 0,
                "Plated hot free griddle dish advances to Season with zero waste");
        }

        static void FreeGriddleBurnAfterWindowCountsWasteOnce()
        {
            var s = Open();
            Need(s.AddFreeIngredient(0, "egg"), "Burn check: egg added");
            Need(s.SetFreeMethod(0, FreeKitchenRules.MethodGriddle), "Burn check: griddle selected");
            Need(s.StartFreeCooking(0), "Burn check: heating started");

            for (int i = 0; i < 100 && s.Lanes[0].step == KitchenStep.Heating; i++)
                s.Tick(0.1f);

            Need(s.Lanes[0].step == KitchenStep.Utensil, "Burn check: reached Utensil");
            float window = s.Lanes[0].duration;
            Need(Mathf.Abs(window - ExpectedFreeWindowSeconds) <= WindowToleranceSeconds,
                "Burn check: Utensil window is ~20s (actual=" + window.ToString("0.00") + ")");

            TickSeconds(s, window - 0.25f, 0.25f);
            Need(s.Lanes[0].step == KitchenStep.Utensil
                && Mathf.Abs(s.Lanes[0].remaining - 0.25f) <= 0.05f
                && s.WastedPortions == 0,
                "At window - 0.25s, free griddle is still Utensil with ~0.25s remaining and zero waste (remaining="
                + s.Lanes[0].remaining.ToString("0.00") + ")");

            TickSeconds(s, 0.25f, 0.25f);
            Need(s.Lanes[0].step == KitchenStep.Dirty, "At exact 20s Utensil boundary, pan burns to Dirty");
            Need(s.Lanes[0].freeBatch == null && s.ProductAtLane(0) == null && s.Lanes[0].batchRemaining == 0,
                "Burned free batch is cleared with no sellable product");
            Need(s.WastedPortions == 1,
                "Burned 1-portion free egg records WastedPortions == 1 (actual=" + s.WastedPortions + ")");

            TickSeconds(s, 10f, 0.25f);
            Need(s.Lanes[0].step == KitchenStep.Dirty && s.WastedPortions == 1,
                "Continuing to tick a Dirty lane does not double-count WastedPortions");

            Need(s.Action(0), "Dirty lane can be cleaned via Action");
            Need(s.Lanes[0].step == KitchenStep.Idle && s.WastedPortions == 1,
                "Cleaning Dirty lane returns to Idle and keeps WastedPortions == 1");
        }

        static void V04AutoWokAndColdWaitNoBurn()
        {
            // V04 automatic wok finishes free stir-fry automatically without opening a burnable Utensil window.
            var wokSession = Open(setup => setup.equipment.Add("V04"));
            Need(wokSession.AddFreeIngredient(0, "egg"), "V04 check: egg added");
            Need(wokSession.SetFreeMethod(0, FreeKitchenRules.MethodStirFry), "V04 check: stir_fry selected");
            Need(wokSession.StartFreeCooking(0), "V04 check: heating started");
            Need(wokSession.Lanes[0].freeBatch != null && wokSession.Lanes[0].freeBatch.vesselId == "V04",
                "V04 check: stir_fry binds V04 automatic wok");
            Need(!wokSession.RequiresManualHeatAction(0),
                "V04 check: automatic stir_fry does not require manual heat action");

            for (int i = 0; i < 100 && wokSession.Lanes[0].step == KitchenStep.Heating; i++)
                wokSession.Tick(0.1f);

            FoodProduct wokProduct = wokSession.ProductAtLane(0);
            Need(wokSession.Lanes[0].step == KitchenStep.Season && wokProduct != null,
                "V04 check: heating completes automatically into Season with a real product");
            Need(wokProduct != null && Mathf.Abs(wokProduct.quality - 1.1f) <= 0.01f,
                "V04 check: automatic wok retains 1.1 quality bonus");

            TickSeconds(wokSession, 25f, 0.25f);
            Need(wokSession.Lanes[0].step == KitchenStep.Season && wokSession.WastedPortions == 0,
                "V04 check: auto-finished product does not burn on subsequent ticks");

            // Cold free method waits in Utensil indefinitely without burning or consuming gas.
            var coldSession = Open();
            int coldGasBefore = coldSession.Gas;
            Need(coldSession.AddFreeIngredient(0, "fruit"), "Cold check: fruit added");
            Need(coldSession.SetFreeMethod(0, FreeKitchenRules.MethodColdMix), "Cold check: cold_mix selected");
            Need(coldSession.StartFreeCooking(0), "Cold check: cold_mix started");
            Need(coldSession.Lanes[0].step == KitchenStep.Utensil && coldSession.Gas == coldGasBefore,
                "Cold check: starts directly in Utensil without consuming gas");

            TickSeconds(coldSession, 30f, 0.25f);
            Need(coldSession.Lanes[0].step == KitchenStep.Utensil && coldSession.WastedPortions == 0,
                "Cold check: waiting 30 real seconds in Utensil never burns");

            Need(coldSession.Action(0), "Cold check: Action finishes cold_mix after 30s wait");
            FoodProduct coldProduct = coldSession.ProductAtLane(0);
            Need(coldSession.Lanes[0].step == KitchenStep.Pack
                && coldProduct != null
                && coldProduct.method == FreeKitchenRules.MethodColdMix
                && coldSession.WastedPortions == 0,
                "Cold check: finished cold_mix advances to Pack with real product and zero waste");
        }

        static void LegacySaveUtensilWindowPreservedOnRestore()
        {
            var s = Open();
            Need(s.AddFreeIngredient(0, "egg"), "Legacy save check: egg added");
            Need(s.SetFreeMethod(0, FreeKitchenRules.MethodGriddle), "Legacy save check: griddle selected");
            Need(s.StartFreeCooking(0), "Legacy save check: heating started");

            for (int i = 0; i < 100 && s.Lanes[0].step == KitchenStep.Heating; i++)
                s.Tick(0.1f);

            Need(s.Lanes[0].step == KitchenStep.Utensil, "Legacy save check: reached Utensil");

            BusinessSaveData snapshot = s.CaptureSave();
            snapshot.lanes[0].duration = 8f;
            snapshot.lanes[0].remaining = 3f;

            BusinessSession restored = BusinessSession.Restore(snapshot);
            Need(restored.Lanes[0].step == KitchenStep.Utensil,
                "Legacy save check: restored lane remains in Utensil");
            Need(Mathf.Abs(restored.Lanes[0].duration - 8f) <= 0.01f,
                "Legacy save check: restored lane duration is 8s (actual="
                + restored.Lanes[0].duration.ToString("0.00") + ")");
            Need(Mathf.Abs(restored.Lanes[0].remaining - 3f) <= 0.01f,
                "Legacy save check: restored lane remaining is 3s (actual="
                + restored.Lanes[0].remaining.ToString("0.00") + ")");
            Need(Mathf.Abs(restored.GreenWindowSeconds(0) - 8f) <= 0.01f,
                "Legacy save check: GreenWindowSeconds(0) returns 8s (actual="
                + restored.GreenWindowSeconds(0).ToString("0.00") + ")");

            TickSeconds(restored, 2.75f, 0.25f);
            Need(restored.Lanes[0].step == KitchenStep.Utensil
                && Mathf.Abs(restored.Lanes[0].remaining - 0.25f) <= 0.05f
                && restored.WastedPortions == 0,
                "Legacy save check: after 2.75s tick, lane is still in Utensil window with ~0.25s remaining and zero waste (remaining="
                + restored.Lanes[0].remaining.ToString("0.00") + ")");

            TickSeconds(restored, 0.25f, 0.25f);
            Need(restored.Lanes[0].step == KitchenStep.Dirty,
                "Legacy save check: final 0.25s tick burns lane to Dirty");
            Need(restored.Lanes[0].freeBatch == null
                && restored.ProductAtLane(0) == null
                && restored.Lanes[0].batchRemaining == 0,
                "Legacy save check: burned lane clears freeBatch and leaves no product");
            Need(restored.WastedPortions == 1,
                "Legacy save check: burn records WastedPortions == 1 (actual=" + restored.WastedPortions + ")");

            TickSeconds(restored, 5f, 0.25f);
            Need(restored.Lanes[0].step == KitchenStep.Dirty && restored.WastedPortions == 1,
                "Legacy save check: subsequent ticks do not double-count WastedPortions");
        }
    }
}
