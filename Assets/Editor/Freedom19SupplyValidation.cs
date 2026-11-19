using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // FREEDOM19_SUPPLY — repeat-purchase / subset-checkout / per-unit model
    // validation. Isolated RunState fixtures driven ONLY through public
    // actions (Act / AddSupply / CanAddSupply / ReturnItem / ConfirmCheckout
    // / Tick / CaptureSave / Restore) plus pure read-only projections
    // (ProcurementPlanCheck / MealPlan.Build / CommerceReceiptData.Shopping)
    // and pure statics (ProcurementInstanceKeys). No scene, no play mode, no
    // reflection into private state. Budget-failure cases use the public cash
    // setter; malformed/legacy saves are explicit isolated fixtures. Not a full
    // playthrough; Unity-runtime visual/packing conditions are listed under
    // integrationRequired, not claimed here.
    public static class Freedom19SupplyValidation
    {
        [Serializable] public sealed class CheckResult
        {
            public string id, expect, actual;
            public bool passed;
        }
        [Serializable] public sealed class Report
        {
            public bool success;
            public bool fixture = true;
            public string scope = "FREEDOM19_SUPPLY model validation; isolated RunState fixtures via public actions; static key/packing checks; no scene, no play mode, not a full playthrough";
            public int checksPassed;
            public CheckResult[] checks = Array.Empty<CheckResult>();
            public string[] integrationRequired = Array.Empty<string>();
        }

        private static readonly List<CheckResult> checks = new List<CheckResult>();

        private static void Need(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }

        private static void Case(string id, string expect, Action action)
        {
            try { action(); checks.Add(new CheckResult { id = id, expect = expect, actual = "pass", passed = true }); }
            catch (Exception error) { checks.Add(new CheckResult { id = id, expect = expect, actual = error.Message, passed = false }); }
        }

        // Fresh market-phase run with the sample stove loaded (profile=null
        // sample path exercises the unconditional Paid clear on Load).
        private static RunState MarketRun()
        {
            var run = new RunState(.05f);
            Need(run.Act("StovePickup", true), "home pickup");
            Need(run.Act("HomeCart", true), "home load");
            Need(run.Act("HomeExit", true), "depart");
            Need(run.Phase == GamePhase.Market, "market phase");
            return run;
        }

        private static void PayAtCounter(RunState run)
        {
            Need(run.Act("Cashier", true), "queue checkout: " + run.Message);
            run.Tick(run.CheckoutDuration + .01f, true);
            Need(run.Paid, "checkout paid: " + run.Message);
        }

        [MenuItem("Guochao/Validate freedom19 supply repeats")]
        public static void Run()
        {
            checks.Clear();
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--freedom19-supply-report");
            string output = at >= 0 && at + 1 < args.Length
                ? Path.GetFullPath(args[at + 1])
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../art-v19/work/supply/freedom19-supply-report.json"));

            Case("repeat-counter-food-one-basket",
                "同一个可消耗 SKU 可在同一篮买两件：AddSupply(egg_small)×2 都入篮，总价=2×单价，现金未扣",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small"), "first: " + run.Message);
                    Need(run.AddSupply("egg_small"), "second repeat refused: " + run.Message);
                    Need(run.Cart.Count == 2, "cart count " + run.Cart.Count);
                    Need(run.CartTotalCents == 2 * run.PriceCents("egg_small"), "total mismatch");
                    Need(run.CashCents == 32000, "cash moved before checkout");
                });

            Case("repeat-shelf-food-one-basket",
                "货架整箱 consumable 也可重复：E(手边有车) 连拿两箱鸡蛋入篮",
                () =>
                {
                    var run = MarketRun();
                    Need(run.Act("EggShelf", true, true), "first take");
                    Need(run.Act("EggShelf", true, true), "second take refused");
                    Need(run.Cart.Count == 2 && run.Cart[0] == "egg" && run.Cart[1] == "egg", "two egg units");
                });

            Case("primary-takes-another-r-returns-one",
                "E 对已入篮 consumable 再拿一件；ReturnItem/R 精确退回一件，其余保留",
                () =>
                {
                    var run = MarketRun();
                    Need(run.Act("EggShelf", true, true), "take one");
                    Need(run.CanPrimary("EggShelf", true, true), "primary blocked with one in basket");
                    Need(run.Act("EggShelf", true, true), "E did not take another");
                    Need(run.Cart.Count == 2, "count after E");
                    int index = run.Cart.IndexOf("egg");
                    Need(run.CanReturnItem(index, true), "return gate closed at capacity");
                    Need(run.ReturnItem(index, "EggShelf", true), "return one refused");
                    Need(run.Cart.Count == 1 && run.Cart[0] == "egg", "one egg remains");
                });

            Case("held-grocery-original-shelf-returns",
                "抱着的箱子回原货架按 E 仍是放回（不产生第二件），现金不动",
                () =>
                {
                    var run = MarketRun();
                    Need(run.Act("MeatShelf", true, false), "held take");
                    Need(run.HeldGrocery == "meat", "held meat");
                    Need(run.Act("MeatShelf", true, false), "held return refused");
                    Need(run.HeldGrocery == "" && run.Cart.Count == 0, "held not returned");
                });

            Case("two-paid-loaded-batches-same-food",
                "同一 consumable 可第二个已付/已装批次：批次1付款装车后 Paid 清零，批次2再购再付再装，Spent=两批之和",
                () =>
                {
                    var run = MarketRun();
                    int unit = run.PriceCents("rice_small");
                    Need(run.AddSupply("rice_small"), "batch1 add");
                    PayAtCounter(run);
                    Need(run.Cart.Count == 1 && run.Paid, "batch1 paid pending load");
                    Need(run.Act("MarketCart", true), "batch1 load: " + run.Message);
                    Need(run.Cargo.Count == 1 && run.Cart.Count == 0, "batch1 moved");
                    Need(!run.Paid, "Paid stayed true after load");
                    Need(run.CargoPaidCents == unit, "loaded receipt should equal batch1");
                    Need(run.AddSupply("rice_small"), "batch2 add refused: " + run.Message);
                    PayAtCounter(run);
                    Need(run.Act("MarketCart", true), "batch2 load");
                    Need(run.Cargo.Count == 2, "two units loaded");
                    Need(run.SpentCents == 2 * unit, "spent " + run.SpentCents);
                    Need(run.CashCents == 32000 - 2 * unit, "cash mismatch");
                });

            Case("subset-one-of-duplicate-units",
                "子集结账从两件相同单位中只选一件：恰好扣一件的钱，未选的一件与其余商品退回，所选留在篮中待装",
                () =>
                {
                    var run = MarketRun();
                    int egg = run.PriceCents("egg_small"), box = run.PriceCents("P02");
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small") && run.AddSupply("P02"), "basket");
                    Need(run.ConfirmCheckoutSelection(new[] { "egg_small" }), "confirm refused: " + run.Message);
                    run.Tick(run.CheckoutDuration + .01f, true);
                    Need(run.Paid && run.CashCents == 32000 - egg, "charged != one unit");
                    Need(run.Cart.Count == 1 && run.Cart[0] == "egg_small", "chosen unit retained");
                    Need(run.SpentCents == egg, "spent one unit");
                    Need(box > 0, "fixture");
                });

            Case("subset-over-selection-rejected",
                "请求件数超过篮中实际数量必须拒绝且篮/现金不动",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    Need(!run.ConfirmCheckoutSelection(new[] { "egg_small", "egg_small", "egg_small" }), "over-selection accepted");
                    Need(run.Cart.Count == 2 && !run.Paid && run.CashCents == 32000, "state mutated");
                });

            Case("subset-insufficient-cash-leaves-all",
                "现金不足：确认失败，篮内全部商品与现金原样",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    run.SetBusinessCash(run.PriceCents("egg_small") - 1);
                    Need(!run.ConfirmCheckoutSelection(new[] { "egg_small", "egg_small" }), "underfunded confirm accepted");
                    Need(run.Cart.Count == 2 && !run.Paid, "basket mutated");
                    Need(run.CashCents == run.PriceCents("egg_small") - 1, "cash mutated");
                });

            Case("selection-cannot-double-charge",
                "已确认选择服务中重复确认不能二次扣款；完成后也不能再结账",
                () =>
                {
                    var run = MarketRun();
                    int egg = run.PriceCents("egg_small");
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    Need(run.ConfirmCheckoutSelection(new[] { "egg_small" }), "first confirm");
                    Need(!run.ConfirmCheckoutSelection(new[] { "egg_small" }), "repeat confirm during service");
                    run.Tick(run.CheckoutDuration + .01f, true);
                    Need(run.CashCents == 32000 - egg && run.Paid, "charged twice or not once");
                    Need(!run.Act("Cashier", true), "second checkout after payment");
                    Need(run.CashCents == 32000 - egg, "cash changed by rejected checkout");
                });

            Case("selection-timeout-guard-survives",
                "确认子集后市场时间耗尽：只代结确认的那一件，未选退回，扣款一次",
                () =>
                {
                    var run = MarketRun();
                    int egg = run.PriceCents("egg_small");
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small") && run.AddSupply("P02"), "basket");
                    Need(run.ConfirmCheckoutSelection(new[] { "egg_small" }), "confirm");
                    run.Tick(run.Remaining + 1f, true);
                    Need(!run.Paid && run.SpentCents == egg, "timeout settled != exactly one unit");
                    Need(run.Cart.Count == 0 && run.Cargo.Count==1 && run.Cargo[0] == "egg_small", "paid selection not loaded exactly once");
                    Need(run.Phase == GamePhase.Complete, "timeout should finish the sample run");
                });

            Case("save-load-preserves-duplicate-quantities",
                "CaptureSave→JSON→Restore 往返保持同 SKU 多件；旧档（每件一次）照常通过",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small") && run.AddSupply("P02"), "basket");
                    var copy = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(run.CaptureSave()));
                    var restored = RunState.Restore(copy);
                    Need(restored.Cart.Count == 3 && restored.Cart[0] == "egg_small" && restored.Cart[1] == "egg_small" && restored.Cart[2] == "P02", "roundtrip lost occurrences");
                });

            Case("save-rejects-duplicate-durable-and-unknown",
                "存档校验：未知 id 与重复耐用设备被拒",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    var good = run.CaptureSave();
                    var badId = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(good));
                    badId.cart = new[] { "no_such_sku" };
                    bool rejected = false; try { RunState.Restore(badId); } catch (InvalidOperationException) { rejected = true; }
                    Need(rejected, "unknown cart id accepted");
                    var badDup = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(good));
                    badDup.cart = new[] { "V05", "V05" };
                    rejected = false; try { RunState.Restore(badDup); } catch (InvalidOperationException) { rejected = true; }
                    Need(rejected, "duplicate durable V05 accepted");
                    var badCross = JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(good));
                    badCross.cart = new[] { "V05" }; badCross.cargo = new[] { "V05" };
                    rejected = false; try { RunState.Restore(badCross); } catch (InvalidOperationException) { rejected = true; }
                    Need(rejected, "durable in cart and cargo accepted");
                });

            Case("e02-refill-repeatable-despite-legacy-equipment-listing",
                "旧档形态把 E02 列在 loadedEquipment 里：E02 作为可重复补充装仍可购买；耐用设备 V05 仍唯一",
                () =>
                {
                    var legacy = new RunSaveData { schemaVersion = 1, phase = GamePhase.Market, cashCents = 50000, remaining = 100,
                        loadedEquipment = new[] { "E02", "V05" }, cart = Array.Empty<string>(), cargo = Array.Empty<string>(), supplies = new RunStockSave[0] };
                    var run = RunState.Restore(legacy);
                    run.Paused=false;
                    Need(run.AddSupply("E02"), "E02 refill blocked by legacy equipment listing: " + run.Message);
                    Need(run.AddSupply("E02"), "second E02 refill refused");
                    Need(!run.AddSupply("V05"), "owned durable V05 accepted");
                });

            Case("e01-empty-coal-refill-semantics",
                "E01：煤>0 拒绝重复；煤=0 空罐补充规则保留且每件 30 热能逐件计入",
                () =>
                {
                    var fueled = new RunSaveData { schemaVersion = 1, phase = GamePhase.Market, cashCents = 50000, remaining = 100, coal = 5,
                        loadedEquipment = new[] { "E01" }, cart = Array.Empty<string>(), cargo = Array.Empty<string>(), supplies = new RunStockSave[0] };
                    var run = RunState.Restore(fueled);
                    run.Paused=false;
                    Need(!run.AddSupply("E01"), "duplicate E01 with coal>0 accepted");
                    var empty = new RunSaveData { schemaVersion = 1, phase = GamePhase.Market, cashCents = 50000, remaining = 100, coal = 0,
                        loadedEquipment = new[] { "E01" }, cart = Array.Empty<string>(), cargo = Array.Empty<string>(), supplies = new RunStockSave[0] };
                    var run2 = RunState.Restore(empty);
                    run2.Paused=false;
                    Need(run2.AddSupply("E01"), "empty-coal refill refused");
                    Need(!run2.AddSupply("E01"), "second coal box bypassed legacy ownership rule");
                    var projection = ProcurementPlanCheck.Project(run2, includeBasket: true);
                    Need(projection.Coal == 30, "coal " + projection.Coal + " != 30 for one real box");
                });

            Case("loaded-projection-exact-portions-and-energy",
                "已装批次投影按件计：egg_small×2=8 份，E02×2=+90 卡式气",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small") && run.AddSupply("E02") && run.AddSupply("E02"), "basket");
                    PayAtCounter(run);
                    Need(run.Act("MarketCart", true), "load");
                    var check = ProcurementPlanCheck.Project(run, includeBasket: false);
                    Need(check.Inventory.TryGetValue("egg", out int egg) && egg == 8, "egg portions " + egg);
                    Need(check.Cartridge == 90, "cartridge " + check.Cartridge);
                });

            Case("advisor-hypothetical-no-double-count",
                "ProcurementPlanCheck：extra/additional 同 SKU 只投影一次；真实篮/货逐件计数",
                () =>
                {
                    var run = MarketRun();
                    var single = ProcurementPlanCheck.Project(run, new[] { "P02" }, new[] { "P02" });
                    Need(single.Inventory.TryGetValue("P02", out int n) && n == 50, "hypothetical double counted: " + n);
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    var withBasket = ProcurementPlanCheck.Project(run, includeBasket: true);
                    Need(withBasket.Inventory.TryGetValue("egg", out int egg) && egg == 8, "basket occurrences lost: " + egg);
                });

            Case("cart-capacity-per-unit",
                "购物篮容量按件：9 格装 9 件散称，第 10 件拒绝且篮/现金不动",
                () =>
                {
                    var run = MarketRun();
                    for (int i = 0; i < run.CartCapacity; i++) Need(run.AddSupply("rice_small"), "add " + i + ": " + run.Message);
                    int cells = run.CartCellsUsed;
                    Need(cells == run.CartCapacity, "cells " + cells);
                    Need(!run.AddSupply("rice_small"), "over-capacity add accepted");
                    Need(run.Cart.Count == run.CartCapacity && run.CashCents == 32000, "state mutated by refused add");
                    Need(run.CanReturnItem(run.Cart.Count - 1, true), "return blocked at full capacity");
                });

            Case("instance-keys-stable-across-load-and-unique",
                "ProcurementInstanceKeys：装车前后同一单位 key 不变；loadout key 逐件唯一",
                () =>
                {
                    var cargo = new List<string> { "egg" };
                    var cart = new List<string> { "egg" };
                    string before = ProcurementInstanceKeys.Key("egg", ProcurementInstanceKeys.OrdinalOf(cargo, cart, "egg", 0));
                    var merged = new List<string>(cargo); merged.AddRange(cart);
                    string after = ProcurementInstanceKeys.Key("egg", ProcurementInstanceKeys.CargoOrdinal(merged, 1));
                    Need(before == after && before == "egg#1", "identity changed across load: " + before + " -> " + after);
                    var keys = new HashSet<string>();
                    for (int i = 0; i < merged.Count; i++) Need(keys.Add(ProcurementInstanceKeys.LoadoutKey(merged, i)), "duplicate loadout key at " + i);
                    Need(keys.Contains("cargo_egg") && keys.Contains("cargo_egg_1"), "loadout key shape");
                });

            Case("return-feasible-when-cash-short",
                "现金不足不影响退回：CanReturnItem 不依赖现金；加购本身也不预扣现金（收银时校验）",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small"), "add");
                    run.SetBusinessCash(0);
                    int index = run.Cart.IndexOf("egg_small");
                    Need(run.CanReturnItem(index, true), "return blocked by empty wallet");
                    Need(run.Cart.Count == 1 && run.CashCents == 0, "fixture state drifted");
                });

            Case("plan-empty-doesnt-gate-purchase",
                "空备货单不限制采购；MealPlan.Build 只读（现金/篮不动）；缺料计划给出非零估算",
                () =>
                {
                    var run = MarketRun();
                    Need(run.MealPlan.Count == 0, "fixture starts with a plan");
                    Need(run.AddSupply("P02"), "purchase gated by empty plan");
                    Need(run.SetMealPlan("fried_rice", 17), "SetMealPlan refused");
                    string before=JsonUtility.ToJson(run.CaptureSave());
                    MealPlanReport report = MealPlan.Build(run);
                    Need(JsonUtility.ToJson(run.CaptureSave())==before, "Build mutated run state");
                    Need(report.estimatedCents > 0, "no re-buy estimate for a missing plan");
                    MealPlanLine eggs=report.lines.Find(line=>line.key=="egg");
                    Need(eggs!=null&&eggs.productId=="egg_small"&&eggs.estimatedCents==5*run.PriceCents("egg_small"), "17 egg portions must price five 4-portion packs");
                });

            Case("legacy-loaded-v1-remains-rebuyable",
                "旧版 v1 单件已装车存档保有货物和货款，旧 Paid 标志不阻断新一批采购",
                () =>
                {
                    var original=MarketRun();
                    Need(original.AddSupply("egg_small"),"legacy basket");PayAtCounter(original);
                    Need(original.Act("MarketCart",true),"legacy load");
                    var old=original.CaptureSave();old.paid=true;old.mealPlan=null;
                    var restored=RunState.Restore(JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(old)));
                    Need(restored.Cargo.Count==1&&restored.CashCents==original.CashCents&&restored.SpentCents==original.SpentCents,"legacy ownership/payment changed");
                    restored.Paused=false;
                    Need(!restored.Paid&&restored.AddSupply("egg_small"),"legacy loaded save blocks next batch");
                    Need(restored.Cart.Count==1&&restored.Cargo.Count==1,"legacy/new quantities mixed");
                });

            Case("loaded-duplicate-save-preserves-owned-stock",
                "多件已付已装往返存档后件数、现金、货款与实际份数一致",
                () =>
                {
                    var original=MarketRun();
                    Need(original.AddSupply("egg_small")&&original.AddSupply("egg_small"),"basket");PayAtCounter(original);
                    Need(original.Act("MarketCart",true),"load");
                    var restored=RunState.Restore(JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(original.CaptureSave())));
                    Need(restored.Cargo.Count==2&&restored.CashCents==original.CashCents&&restored.CargoPaidCents==original.CargoPaidCents,"saved quantities or money drifted");
                    Need(ProcurementPlanCheck.Project(restored,includeBasket:false).Inventory["egg"]==8,"saved portions lost");
                });

            Case("receipt-shows-duplicates-truthfully",
                "CommerceReceiptData.Shopping：同 SKU 两件聚合成 quantity=2、小计=2×实际单价、账目守恒",
                () =>
                {
                    var run = MarketRun();
                    Need(run.AddSupply("egg_small") && run.AddSupply("egg_small"), "basket");
                    CommerceReceiptAudit audit = CommerceReceiptData.Shopping(run, false);
                    Need(audit.lines.Length == 1 && audit.lines[0].quantity == 2, "duplicate rows wrong");
                    Need(audit.lines[0].unitCents == run.PurchaseItemPriceCents("egg_small"), "unit price fact lost");
                    Need(audit.lines[0].subtotalCents == 2 * audit.lines[0].unitCents, "subtotal mismatch");
                    Need(audit.totalCents == audit.ledgerCents && audit.totalsMatch, "ledger mismatch");
                });

            var reportJson = new Report
            {
                success = checks.TrueForAll(c => c.passed),
                checksPassed = checks.FindAll(c => c.passed).Count,
                checks = checks.ToArray(),
                integrationRequired = new[]
                {
                    "VehicleLoadout.Validate/WorldRuntime now emit unique per-occurrence cargo instance ids ('cargo_SKU', 'cargo_SKU_n') — VehiclePacking.Plan must keep rejecting duplicate ids (it does); V10PackingValidation fixture already uses indexed ids.",
                    "GameplaySmokeChecks.RunModelChecks lines ~28-29 still assert the OLD toggle semantics (primary E returns the box); update to E-takes-another + R-returns-one contract.",
                    "Batch1RecoveryValidation 'duplicate AddSupply accepted' assertion (rice_small) encodes the old unique-consumable rule and must be re-pointed at a durable SKU.",
                    "CommerceReceiptData.Sales now calls BusinessSession.OrderDishName(order.id) per art-v19/contracts/core-api.md; compilation requires the sibling core-api merge (recipeIndex remains only a legacy icon adapter).",
                    "Unity-runtime only, not executed here: duplicate visible models on trolley/vehicle via WorldRuntime instance keys + VehicleLoadout.Apply; held-envelope prediction for repeat purchases at real contacts."
                }
            };
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(reportJson, true));
            Debug.Log("FREEDOM19_SUPPLY " + (reportJson.success ? "PASS" : "FAIL") + " checks=" + reportJson.checksPassed + "/" + checks.Count + " -> " + output);
            if (Application.isBatchMode) EditorApplication.Exit(reportJson.success ? 0 : 1);
        }
    }
}
