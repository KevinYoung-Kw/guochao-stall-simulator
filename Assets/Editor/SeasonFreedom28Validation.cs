using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao.Editor
{
    // Scope note: Scenario B is a normal-mode (practice=false) model simulation with a single egg
    // and open queue capacity; it does not prove guaranteed demand under all market/queue states.
    public static class SeasonFreedom28Validation
    {
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
                var session = OpenAndCookToSeason();
                VerifySeasonGuidance(session);
                VerifyPackAndNaturalSale(session);
                VerifyEmergencyBagFallback();
            }
            catch (Exception ex)
            {
                report.failures.Add(ex.ToString());
            }

            report.success = report.failures.Count == 0;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--season-freedom-output");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("--season-freedom-output required");
            string path = Path.GetFullPath(args[index + 1]);
            if (File.Exists(path))
                throw new IOException("Existing evidence retained");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            if (!report.success)
                throw new Exception("Season freedom validation failed: " + string.Join("; ", report.failures));
        }

        static BusinessSession OpenAndCookToSeason(int p04Count = 1)
        {
            var setup = BusinessSetup.MidgamePractice();
            setup.practice = false;
            setup.inventory.Clear();
            setup.inventory["egg"] = 1;
            if (p04Count > 0) setup.inventory["P04"] = p04Count;
            setup.equipment.Clear();
            setup.equipment.Add("B04");
            setup.equipment.Add("V05");
            setup.equipment.Add("U05");

            var s = new BusinessSession(setup);
            s.SelectedRecipes.Clear();
            Need(s.Gas > 0, "Normal finite-time setup has real gas");
            Need(s.Open(), "Normal session opens with empty SelectedRecipes");
            Need(s.AddFreeIngredient(0, "egg"), "Single egg added through real AddFreeIngredient");
            Need(s.SetFreeMethod(0, "griddle"), "Griddle method set through real SetFreeMethod");
            Need(s.StartFreeCooking(0), "Cooking started through real StartFreeCooking");
            for (int i = 0; i < 200 && s.Lanes[0].step == KitchenStep.Heating; i++)
                s.Tick(0.1f);
            Need(s.Lanes[0].step == KitchenStep.Utensil, "Heating completed to Utensil step");
            Need(s.Action(0), "Utensil action completed");
            Need(s.Lanes[0].step == KitchenStep.Season, "Cooked egg reached Season step");
            string seasonMessage = s.Message ?? "";
            Need(seasonMessage.Contains("可调味") && seasonMessage.Contains("包材") && !seasonMessage.Contains("调味后"),
                "Cooked egg Season message mentions 可调味 and 包材 without 调味后");
            Need(s.ProductAtLane(0) != null, "Cooked egg product exists on lane 0");
            return s;
        }

        static void VerifySeasonGuidance(BusinessSession s)
        {
            string before = JsonUtility.ToJson(s.CaptureSave());

            string noPkg = FreePanGuide.Read(s, 0).next ?? "";
            Need(!string.IsNullOrWhiteSpace(noPkg), "FreePanGuide.Read(s,0).next returns guidance text");
            Need(noPkg.Contains("可调味") && noPkg.Contains("包材"),
                "Season guidance explicitly includes both 可调味 and 包材");
            Need(!noPkg.Contains("必选") && !noPkg.Contains("必须调味") && !noPkg.Contains("必须先调味") && !noPkg.Contains("先调味再"),
                "Season guidance does not claim seasoning is mandatory");

            string withP04 = FreePanGuide.Read(s, 0, "P04").next ?? "";
            Need(withP04.Contains("点锅") && (withP04.Contains("装餐") || withP04.Contains("装台") || withP04.Contains("打包")),
                "FreePanGuide.Read(s,0,\"P04\").next hints clicking pan to pack");

            string unsuitablePkg = FreePanGuide.Read(s, 0, "P01").next ?? "";
            Need(!unsuitablePkg.Contains("点锅"), "Unsuitable package P01 does not falsely hint clicking pan");

            string outOfStockPkg = FreePanGuide.Read(s, 0, "P05").next ?? "";
            Need(!outOfStockPkg.Contains("点锅"), "Out-of-stock package P05 does not falsely hint clicking pan");

            for (int i = 0; i < 10; i++)
            {
                FreePanGuide.Read(s, 0);
                FreePanGuide.Read(s, 0, "P04");
                FreePanGuide.Read(s, 0, "P01");
                FreePanGuide.Read(s, 0, "P05");
            }
            Need(before == JsonUtility.ToJson(s.CaptureSave()), "Repeated FreePanGuide reads never modify save snapshot");
        }

        static void VerifyEmergencyBagFallback()
        {
            var session = OpenAndCookToSeason(p04Count: 0);
            Need(session.Lanes[0].step == KitchenStep.Season && !session.Lanes[0].seasoned,
                "Zero-P04 fixture is in Season and unseasoned");
            Need(session.CanUseEmergencyBag(session.ProductAtLane(0)),
                "Zero-P04 fixture CanUseEmergencyBag is true");
            string noPkg = FreePanGuide.Read(session, 0).next ?? "";
            Need(noPkg.Contains("应急袋"), "Zero-P04 fixture Season guidance mentions 应急袋");
            Need(!noPkg.Contains("缺适配包材"), "Zero-P04 fixture Season guidance does not falsely report 缺适配包材");
            Need(noPkg.Contains("也可直接用应急袋"), "Zero-P04 fixture Season guidance mentions 也可直接用应急袋");
            Need(!noPkg.Contains("调味后"), "Zero-P04 fixture Season guidance does not contain 调味后");
            // 仅作为 384 宽、24px 最大字号的粗略单行宽度检验，不代表真实屏幕已测。
            Need(noPkg.Length <= 16, "Zero-P04 fixture Season guidance length <= 16 chars (rough 384px/24px single-line check)");

            Need(session.Action(0), "Zero-P04 fixture Action(0) completes default Season transition");
            Need(session.Lanes[0].step == KitchenStep.Pack, "Zero-P04 fixture lane 0 advanced to Pack step");
            Need(session.PackToHand(0, BusinessSession.EmergencyBagId),
                "Zero-P04 fixture PackToHand succeeds with EmergencyBagId");
            Need(session.HeldMeal != null && session.HeldMeal.packageId == BusinessSession.EmergencyBagId,
                "Zero-P04 fixture HeldMeal uses EmergencyBagId");
        }

        static void VerifyPackAndNaturalSale(BusinessSession s)
        {
            Need(s.Action(0), "Action completes default Season transition before packing");
            Need(s.Lanes[0].step == KitchenStep.Pack, "Lane 0 advanced to Pack step");
            Need(s.CanPackToHand(0, "P04", out string packReason), "CanPackToHand accepts P04: " + packReason);
            Need(s.PackToHand(0, "P04"), "PackToHand packs egg with P04");
            Need(s.HeldMeal != null && s.HeldMeal.packageId == "P04", "HeldMeal is present with P04");
            Need(s.ProductAtLane(0) == null && s.Lanes[0].step == KitchenStep.Idle, "Lane 0 cleared after packing");

            int matchedOrderId = -1;
            for (int i = 0; i < 100 && !s.Finished; i++)
            {
                s.Tick(0.2f);
                foreach (var order in s.Orders)
                {
                    if (!order.arrived && !order.served && !order.departed)
                        s.MarkCustomerArrived(order.id);
                }
                matchedOrderId = s.FindServingHeldOrder();
                if (matchedOrderId >= 0) break;
            }
            Need(matchedOrderId >= 0,
                "Natural customer order matched held fried egg (single-egg, open-queue normal-mode simulation; does not prove universal demand)");

            int cashBefore = s.CashCents;
            int servedBefore = s.Served;
            Need(s.CanServeHeldMeal(matchedOrderId, out string serveReason), "CanServeHeldMeal accepts matched order: " + serveReason);
            Need(s.ServeHeldMeal(matchedOrderId), "ServeHeldMeal settles sale");
            Need(s.CashCents > cashBefore, "Cash increased after sale");
            Need(s.HeldMeal == null && s.ReadyMeals.Count == 0 && s.ProductAtLane(0) == null, "Finished meal disappeared after sale");
            Need(s.Served == servedBefore + 1, "Exactly one sale settled");
            Need(!s.CanServeHeldMeal(matchedOrderId, out _), "Cannot serve held meal twice");
        }
    }
}
