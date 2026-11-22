using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Isolated model/save regression; fixtures supply cash and completed day.
    // Does not certify physical vehicle geometry or UI interaction.
    public static class VehicleUpgrade26Validation
    {
        [Serializable] public sealed class Check { public string name, error; public bool passed; }
        [Serializable] public sealed class Report { public bool success; public Check[] checks; }
        private static readonly List<Check> checks = new List<Check>();

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--vehicle-upgrade-output");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("Output path required");
            string output = Path.GetFullPath(args[at + 1]);
            if (File.Exists(output)) throw new IOException("Refusing to overwrite evidence");
            string originalDirectory = CampaignSaveStore.TestDirectory;
            CampaignSaveStore.TestDirectory = Path.Combine(Path.GetDirectoryName(output), "isolated-save-" + Guid.NewGuid().ToString("N"));
            try
            {
                CheckCase("authored tiers and unsupported saved levels", Tiers);
                CheckCase("successful purchase: separate ledger mirrors, real save, reload, next day", SuccessfulPurchase);
                CheckCase("same ledger object is charged once", SameLedger);
                CheckCase("save false, null and exception rollback", SaveFailures);
                CheckCase("old zero level, insufficient cash and mismatch rejected", Eligibility);
                CheckCase("matching session identity and legacy JSON field", SessionAndLegacy);
                CheckCase("real disk write failure rolls back", DiskFailure);
                CheckCase("night online purchase before and after upgrade", OnlineOrderSequence);
            }
            finally { CampaignSaveStore.TestDirectory = originalDirectory; }
            var report = new Report { checks = checks.ToArray(), success = checks.TrueForAll(x => x.passed) };
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("VEHICLE_UPGRADE_26 " + (report.success ? "PASS " : "FAIL ") + output);
            if (Application.isBatchMode) EditorApplication.Exit(report.success ? 0 : 1);
        }

        private static void CheckCase(string name, Action body)
        {
            try { body(); checks.Add(new Check { name = name, passed = true, error = "" }); }
            catch (Exception e) { checks.Add(new Check { name = name, passed = false, error = e.ToString() }); }
        }
        private static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        private static void Fixture(int cents, bool alias, out CampaignState c, out RunState r)
        {
            var profile = new RunProfile { careerId = "career1", selectedTraits = Array.Empty<string>() };
            Need(profile.Validate(out string reason), reason);
            r = new RunState(.25f, profile); r.SetBusinessCash(cents); r.SetBusinessPhase(GamePhase.Night);
            c = CampaignState.New(profile, r);
            c.lastDay = new CampaignDayLedger { dayIndex = 1, sessionId = "day-one", committed = true, closingCashCents = cents };
            c.history.Add(alias ? c.lastDay : new CampaignDayLedger { dayIndex = 1, sessionId = "day-one", committed = true, closingCashCents = cents });
        }
        private static bool Save(CampaignState c, RunState r)
        { return CampaignSaveStore.Save(new CampaignSaveData { campaign = c, run = r.CaptureSave() }, out _); }
        private static void Tiers()
        {
            var one = VehicleUpgradeRules.Get(1); var legacy = VehicleUpgradeRules.Get(0); var two = VehicleUpgradeRules.Get(2);
            Need(one != null && legacy != null && one.Level == 1 && legacy.Level == 1 && one.Columns == 4 && one.Rows == 6 && one.WeightLimitKg == 100, "Lv1 data");
            Need(two != null && two.Level == 2 && two.Columns == 5 && two.Rows == 6 && two.WeightLimitKg == 180 && two.PriceCents == 100000, "Lv2 data");
            Need(!VehicleUpgradeRules.TryGet(3, out var missing, out var why) && missing == null && !string.IsNullOrEmpty(why), "Lv3 must not masquerade as Lv2");
        }
        private static void SuccessfulPurchase()
        {
            Fixture(150000, false, out var c, out var r); int calls = 0;
            Need(VehicleUpgradeTransaction.TryUpgrade(c, r, () => { calls++; return Save(c, r); }, out string msg), msg);
            Need(calls == 1 && c.vehicleLevel == 2 && c.cashCents == 50000 && r.CashCents == 50000, "upgrade cash/level");
            Need(c.lastDay.closingCashCents == 50000 && c.history[0].closingCashCents == 50000 && c.lastDay.vehicleUpgradeCents == 100000 && c.history[0].vehicleUpgradeCents == 100000, "ledger mirror");
            Need(!VehicleUpgradeTransaction.TryUpgrade(c, r, () => { calls++; return true; }, out _) && calls == 1, "duplicate debit/save");
            Need(CampaignSaveStore.Load(out var saved, out string status), status);
            Need(saved.campaign.vehicleLevel == 2 && saved.campaign.cashCents == 50000 && saved.run.cashCents == 50000 && saved.campaign.lastDay.vehicleUpgradeCents == 100000 && saved.campaign.history[0].vehicleUpgradeCents == 100000, "save/load values");
            var next = saved.campaign.CreateNextDay(.25f);
            Need(saved.campaign.dayIndex == 2 && saved.campaign.vehicleLevel == 2 && next.CashCents == 50000, "next day values");
        }
        private static void SameLedger()
        {
            Fixture(150000, true, out var c, out var r);
            Need(VehicleUpgradeTransaction.TryUpgrade(c, r, () => true, out string msg), msg);
            Need(c.lastDay.vehicleUpgradeCents == 100000 && c.history[0].vehicleUpgradeCents == 100000 && c.cashCents == 50000, "alias charged twice");
        }
        private static void SaveFailures()
        {
            for (int mode = 0; mode < 3; mode++)
            {
                Fixture(150000, mode == 0, out var c, out var r);
                Func<bool> save = mode == 0 ? (Func<bool>)(() => false) : mode == 1 ? null : (Func<bool>)(() => throw new IOException("injected"));
                Need(!VehicleUpgradeTransaction.TryUpgrade(c, r, save, out _), "failed save accepted");
                Need(c.vehicleLevel == 1 && c.cashCents == 150000 && r.CashCents == 150000 && c.lastDay.closingCashCents == 150000 && c.history[0].closingCashCents == 150000 && c.lastDay.vehicleUpgradeCents == 0 && c.history[0].vehicleUpgradeCents == 0, "failed save did not rollback");
            }
        }
        private static void Eligibility()
        {
            Fixture(99999, false, out var poor, out var poorRun);
            Need(!VehicleUpgradeTransaction.TryUpgrade(poor, poorRun, () => true, out _) && poor.cashCents == 99999, "insufficient cash");
            Fixture(150000, false, out var invalid, out var invalidRun); invalid.history[0].closingCashCents--;
            Need(!VehicleUpgradeTransaction.TryUpgrade(invalid, invalidRun, () => true, out _) && invalid.cashCents == 150000, "ledger mismatch");
            Fixture(150000, false, out var legacy, out var legacyRun); legacy.vehicleLevel = 0;
            Need(VehicleUpgradeTransaction.TryUpgrade(legacy, legacyRun, () => true, out _) && legacy.vehicleLevel == 2, "legacy level zero");
        }
        private static void SessionAndLegacy()
        {
            Fixture(150000, false, out var c, out var r);
            c.history.Insert(0, new CampaignDayLedger { dayIndex = 1, sessionId = "different-session", committed = true, closingCashCents = 17 });
            Need(VehicleUpgradeTransaction.TryUpgrade(c, r, () => true, out string msg), msg);
            Need(c.history[0].closingCashCents == 17 && c.history[0].vehicleUpgradeCents == 0 && c.history[1].closingCashCents == 50000, "wrong ledger selected");
            string json = JsonUtility.ToJson(c).Replace(",\"vehicleUpgradeCents\":100000", "").Replace(",\"vehicleUpgradeCents\":0", "");
            Need(!json.Contains("vehicleUpgradeCents"), "old JSON fixture still has new field");
            var restored = JsonUtility.FromJson<CampaignState>(json);
            Need(restored.lastDay.vehicleUpgradeCents == 0 && restored.history[1].vehicleUpgradeCents == 0, "missing old field must default to zero");
        }
        private static void DiskFailure()
        {
            Fixture(150000, false, out var c, out var r);
            string old = CampaignSaveStore.TestDirectory;
            string blocker = Path.Combine(Path.GetDirectoryName(old), "not-a-directory-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "occupied");
            try
            {
                CampaignSaveStore.TestDirectory = blocker;
                Need(!VehicleUpgradeTransaction.TryUpgrade(c, r, () => Save(c, r), out _), "failed disk write accepted");
                Need(c.vehicleLevel == 1 && c.cashCents == 150000 && r.CashCents == 150000 && c.lastDay.vehicleUpgradeCents == 0 && c.history[0].vehicleUpgradeCents == 0, "real disk failure did not rollback");
            }
            finally { CampaignSaveStore.TestDirectory = old; }
        }
        private static void OnlineOrderSequence()
        {
            for (int orderFirst = 0; orderFirst < 2; orderFirst++)
            {
                Fixture(150000, false, out var c, out var r);
                int quote = OnlineCatalog.QuoteCents(r.Profile, "P02");
                Need(quote > 0, "online SKU quote missing");
                if (orderFirst == 1) Need(r.TryPlaceOnlineOrder(c, "P02", quote, out string reason1), reason1);
                Need(VehicleUpgradeTransaction.TryUpgrade(c, r, () => true, out string reason2), reason2);
                if (orderFirst == 0) Need(r.TryPlaceOnlineOrder(c, "P02", quote, out string reason3), reason3);
                int expected = 150000 - 100000 - quote;
                Need(c.cashCents == expected && r.CashCents == expected && c.lastDay.closingCashCents == expected && c.history[0].closingCashCents == expected, "night cash mismatch");
                Need(c.lastDay.onlinePurchasesCents == quote && c.history[0].onlinePurchasesCents == quote && c.lastDay.vehicleUpgradeCents == 100000 && c.history[0].vehicleUpgradeCents == 100000, "night expenses mismatch");
                var roundtrip = CampaignSaveStore.Decode(CampaignSaveStore.Encode(new CampaignSaveData { campaign = c, run = r.CaptureSave() }));
                Need(roundtrip.campaign.cashCents == expected && roundtrip.run.cashCents == expected && roundtrip.campaign.lastDay.vehicleUpgradeCents == 100000, "night transaction roundtrip");
            }
        }
    }
}
