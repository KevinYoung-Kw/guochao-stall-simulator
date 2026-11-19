using System;
using System.Collections.Generic;

namespace Guochao
{
    // PLAN18 — the home-authored meal plan and its read-only procurement
    // projection. Pure data: SetMealPlan never touches stock, cash, Cart,
    // Cargo or the business clock, and MealPlan.Build never mutates the run.
    [Serializable]
    public sealed class MealPlanChoice
    {
        public string recipeId;
        public int portions;
    }

    public sealed class MealPlanLine
    {
        // Stable resource key (rice / P05 / B04 / seasoning / gas ...).
        public string key;
        public string name;
        // The actual suggested SKU to pick up; never a resource key.
        public string productId = "";
        // shelf / counter / night / home / none
        public string channel = "none";
        // Real world anchor id when one exists (RiceShelf / SupplyCounter);
        // empty when there is no confirmable anchor.
        public string location = "";
        public string note = "";
        // atHome is the subset of owned energy still physically at home; it
        // is not an extra quantity and cannot power equipment before loading.
        public int required, owned, atHome, inBasket, paidCargo, paidWaiting, missing, estimatedCents;
        public bool equipment, optional, estimateUncertain;
    }

    public sealed class MealPlanReport
    {
        public readonly List<MealPlanLine> lines = new List<MealPlanLine>();
        // Indexes into BusinessSession.Recipes that can truly start right now
        // against loaded stock (ProcurementPlanCheck includeBasket:false →
        // BusinessSession.Missing). Covers every recipe, planned or not.
        public readonly List<int> cookableNow = new List<int>();
        public readonly List<string> methodsNow = new List<string>();
        public int estimatedCents;
        public string summary = "";
    }

    public static class MealPlan
    {
        const int CoalSlot = 0, GasSlot = 1, CartSlot = 2, ElecSlot = 3;

        public static MealPlanReport Build(RunState run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            var report = new MealPlanReport();

            // Resolve planned recipes against the live source table. A null
            // entry can only appear if someone poked the public list directly;
            // skip it instead of crashing.
            var planned = new List<KeyValuePair<StallRecipe, int>>();
            var seen = new HashSet<int>();
            if (run.MealPlan != null)
                foreach (var choice in run.MealPlan)
                {
                    if (choice == null) continue;
                    int index = Array.FindIndex(BusinessSession.Recipes, r => r.id == choice.recipeId);
                    if (index < 0 || choice.portions <= 0 || !seen.Add(index)) continue;
                    planned.Add(new KeyValuePair<StallRecipe, int>(BusinessSession.Recipes[index], choice.portions));
                }

            // ── Pools ──────────────────────────────────────────────────────
            // paidWaiting = the paid basket only (Cart while Paid=true).
            // paidCargo   = loaded Cargo + paid basket.
            var ownedInv = new Dictionary<string, int>(run.Supplies ?? new Dictionary<string, int>());
            var ownedEquip = new HashSet<string>(run.LoadedEquipment);
            var homeOnly = new List<string>();
            foreach (string id in run.HomeEquipment)
                if (!run.LoadedEquipment.Contains(id) && !homeOnly.Contains(id)) homeOnly.Add(id);
            // Real remaining fuel, counted wherever the hardware actually is.
            // Owning hardware never grants fresh fuel beyond the real pool.
            var ownedFuel = new int[4];
            bool hasGasHardware = ownedEquip.Contains("E03") || homeOnly.Contains("E03");
            ownedFuel[GasSlot] = hasGasHardware ? run.GasPoints - run.CartridgePoints : 0;
            ownedFuel[CartSlot] = ownedEquip.Contains("B02") || homeOnly.Contains("B02") ? run.CartridgePoints : 0;
            ownedFuel[CoalSlot] = ownedEquip.Contains("E01") || homeOnly.Contains("E01") ? run.CoalPoints : 0;
            ownedFuel[ElecSlot] = ownedEquip.Contains("E05") || homeOnly.Contains("E05") ? run.ElectricityPoints : 0;

            var basketInv = new Dictionary<string, int>(); var basketEquip = new HashSet<string>(); var basketFuel = new int[4];
            var waitingInv = new Dictionary<string, int>(); var waitingEquip = new HashSet<string>(); var waitingFuel = new int[4];
            var cargoInv = new Dictionary<string, int>(); var cargoEquip = new HashSet<string>(); var cargoFuel = new int[4];
            if (!run.Paid) foreach (string id in run.Cart) AddToPools(run, id, basketInv, basketEquip, basketFuel);
            else foreach (string id in run.Cart) AddToPools(run, id, waitingInv, waitingEquip, waitingFuel);
            foreach (string id in run.Cargo) AddToPools(run, id, cargoInv, cargoEquip, cargoFuel);
            // paidCargo = Cargo ∪ paid basket.
            foreach (var pair in waitingInv) { cargoInv.TryGetValue(pair.Key, out int c); cargoInv[pair.Key] = c + pair.Value; }
            foreach (string id in waitingEquip) cargoEquip.Add(id);
            for (int i = 0; i < 4; i++) cargoFuel[i] += waitingFuel[i];

            bool pricedAll = true;
            var pricedUnits = new Dictionary<string,int>();
            void DescribeSku(MealPlanLine line, ItemDefinition sku)
            {
                if (sku == null) return;
                line.productId = sku.id;
                line.channel = SupplyCatalog.Channel(sku.id);
                line.location = line.channel == "shelf" ? sku.anchor : line.channel == "counter" ? "SupplyCounter" : "";
            }
            // FREEDOM19: repeat purchases are legal for consumables, so a gap
            // prices as ceil(missing / per-unit amount) purchase units. That
            // arithmetic is ONLY valid for repeatable goods: durable hardware
            // (E03 tanks, E01 coal boxes beyond the empty-coal exception,
            // vessels, tools) stays unique at the real purchase gate, so a
            // demand above one obtainable unit is priced for the single
            // legal unit and the rest of the gap is reported honestly as
            // unpriced/uncertain instead of advertising quantity CanAddSupply
            // would refuse. The first line referencing a SKU carries the
            // merged price; a later line only gets the merge note when the
            // same SKU genuinely still has unmet demand here.
            void PriceSku(MealPlanLine line, ItemDefinition sku, int amountPerUnit = 0)
            {
                if (sku == null) return;
                DescribeSku(line, sku);
                if (line.missing <= 0) return;
                if (line.channel != "counter" && line.channel != "shelf")
                {
                    pricedAll = false;
                    line.note = "今天市场没有这件商品的购买渠道，本项未计价。";
                    return;
                }
                bool repeatable = RunState.IsRepeatableSupply(sku.id);
                int perUnit = amountPerUnit > 0 ? amountPerUnit : Math.Max(1, sku.portions);
                int units = (line.missing + perUnit - 1) / perUnit;
                if (!repeatable && units > 1)
                {
                    units=1;pricedAll=false;line.estimateUncertain=true;
                    line.note="这件货只能再买一件，补充后仍差 "+(line.missing-perUnit)+"，剩余缺口未计价。";
                }
                // Resource demands are aggregated above. An equipment line
                // and its fuel line can describe the same physical unit;
                // charge only any additional units not yet priced.
                pricedUnits.TryGetValue(sku.id,out int alreadyPriced);
                if(units<=alreadyPriced)
                {
                    if(string.IsNullOrEmpty(line.note))line.note="已含在同款商品的采购估算中。";
                    return;
                }
                pricedUnits[sku.id]=units;
                line.estimatedCents = (units-alreadyPriced) * run.PriceCents(sku.id);
                report.estimatedCents += line.estimatedCents;
                if (units > 1)
                    line.note = string.IsNullOrEmpty(line.note)
                        ? "还差 " + line.missing + "，每件含 " + perUnit + "，需再买 " + units + " 件。"
                        : line.note;
            }
            void Uncertain(string key, string name, string note, bool equipment)
            {
                pricedAll = false;
                report.lines.Add(new MealPlanLine { key = key, name = name, note = note,
                    equipment = equipment, estimateUncertain = true });
            }
            ItemDefinition InfoSku(string key)
            {
                // A real SKU the player can look at/ping, even when nothing
                // needs buying: prefer an owned/basket/cargo unit, else the
                // cheapest retail option. Information only — stock unchanged.
                foreach (var inv in new[] { ownedInv, basketInv, cargoInv })
                    if (inv.TryGetValue(key, out int qty) && qty > 0)
                    {
                        var own = SupplyCatalog.Find(key) ?? GameCatalog.Find(key);
                        if (own != null) return own;
                    }
                return CheapestRetail(run, key);
            }

            // ── Ingredients (BOM × portions, merged across dishes) ─────────
            var requiredLong = new Dictionary<string, long>();
            foreach (var pair in planned)
                foreach (var ingredient in pair.Key.ingredients)
                {
                    requiredLong.TryGetValue(ingredient.Key, out long current);
                    requiredLong[ingredient.Key] = current + (long)ingredient.Value * pair.Value;
                }
            foreach (var pair in requiredLong)
            {
                string key = pair.Key;
                bool optional = SeasoningShortageRules.IsBaseSeasoning(key);
                int required = Clamp(pair.Value);
                var line = new MealPlanLine
                {
                    key = key,
                    name = BusinessSession.Name(key),
                    optional = optional,
                    required = required,
                    owned = ownedInv.TryGetValue(key, out int o) ? o : 0,
                    inBasket = basketInv.TryGetValue(key, out int b) ? b : 0,
                    paidCargo = cargoInv.TryGetValue(key, out int p) ? p : 0,
                    paidWaiting = waitingInv.TryGetValue(key, out int w) ? w : 0,
                };
                line.missing = Math.Max(0, line.required - line.owned - line.inBasket - line.paidCargo);
                DescribeSku(line, InfoSku(key));
                if (line.missing > 0)
                {
                    var sku = CheapestRetail(run, key);
                    if (sku != null)
                        PriceSku(line, sku);
                    if (line.estimatedCents == 0 && sku == null)
                    { pricedAll = false; line.note = string.IsNullOrEmpty(line.note) ? "今天市场没有这件货的在售渠道，这个缺口还没法计价。" : line.note; }
                }
                if (optional && line.note == "")
                    line.note = "油盐是调味不是门槛：没有也能出餐，只是每份口碑会吃亏。";
                report.lines.Add(line);
            }

            // ── Packaging: shared pool (owned + basket + paid), merged once ─
            var pkgRequired = new Dictionary<string, int>();
            var pkgPool = new Dictionary<string, int>();
            void PoolPackages(Dictionary<string, int> inv)
            {
                foreach (var pair in inv)
                    if (pair.Key.StartsWith("P", StringComparison.Ordinal))
                    { pkgPool.TryGetValue(pair.Key, out int c); pkgPool[pair.Key] = c + pair.Value; }
            }
            PoolPackages(ownedInv); PoolPackages(basketInv); PoolPackages(cargoInv);
            foreach (var pair in planned)
            {
                var recipe = pair.Key; int remaining = pair.Value;
                foreach (string pkg in recipe.packages)
                    if (pkgPool.TryGetValue(pkg, out int have) && have >= remaining)
                    { pkgPool[pkg] = have - remaining; Add(pkgRequired, pkg, remaining); remaining = 0; break; }
                if (remaining > 0)
                    foreach (string pkg in recipe.packages)
                    {
                        if (remaining <= 0) break;
                        if (!pkgPool.TryGetValue(pkg, out int have) || have <= 0) continue;
                        int take = Math.Min(have, remaining);
                        pkgPool[pkg] = have - take; Add(pkgRequired, pkg, take); remaining -= take;
                    }
                if (remaining > 0)
                {
                    // Demand with no covering stock anywhere: attach it to the
                    // cheapest real option; the merged line below prices it.
                    var sku = CheapestPackage(run, recipe);
                    if (sku != null) Add(pkgRequired, sku.id, remaining);
                    else Add(pkgRequired, recipe.packages[0], remaining); // keep the gap visible
                }
            }
            foreach (var pair in pkgRequired)
            {
                var line = new MealPlanLine
                {
                    key = pair.Key,
                    name = BusinessSession.Name(pair.Key),
                    required = pair.Value,
                    owned = ownedInv.TryGetValue(pair.Key, out int o2) ? o2 : 0,
                    inBasket = basketInv.TryGetValue(pair.Key, out int b2) ? b2 : 0,
                    paidCargo = cargoInv.TryGetValue(pair.Key, out int p2) ? p2 : 0,
                    paidWaiting = waitingInv.TryGetValue(pair.Key, out int w2) ? w2 : 0,
                };
                line.missing = Math.Max(0, line.required - line.owned - line.inBasket - line.paidCargo);
                var sku = SupplyCatalog.Find(pair.Key) ?? GameCatalog.Find(pair.Key);
                if (sku != null)
                {
                    // FREEDOM19: PriceSku converts the package gap into the
                    // real number of 50-packs (or single packages) to buy and
                    // flags missing channels honestly.
                    PriceSku(line, sku);
                }
                else { pricedAll = false; line.note = "这件包材今天没有在售渠道，缺口未计价。"; }
                report.lines.Add(line);
            }

            // ── Equipment: plan-owned set, real Missing validation ─────────
            // Everything the plan may count on: loaded, at home, in the
            // basket, paid-and-waiting, already loaded cargo, and combos the
            // plan itself suggests below. Home gear adds no fuel and is never
            // passed to AddProduct as a new purchase.
            var planOwned = new HashSet<string>(ownedEquip);
            foreach (string id in homeOnly) planOwned.Add(id);
            foreach (string id in basketEquip) planOwned.Add(id);
            foreach (string id in waitingEquip) planOwned.Add(id);
            foreach (string id in cargoEquip) planOwned.Add(id);

            var carried = ProcurementPlanCheck.Project(run, includeBasket: false);
            var stocked = new BusinessSession(carried.ToSetup(run));

            // Chosen combos: per dish, either fully covered by planOwned or
            // the cheapest real purchase that the production equipment check
            // accepts. Only brand-new candidates go through AddProduct.
            var suggested = new HashSet<string>();
            var unresolvedEquipment = new HashSet<int>();
            var chosenByDish = new List<KeyValuePair<int, List<string>>>();
            foreach (var pair in planned)
            {
                int index = Array.IndexOf(BusinessSession.Recipes, pair.Key);
                if (PlanCovered(pair.Key, index, planOwned, suggested)) continue;
                var combo = CheapestCombo(run, pair.Key, planOwned, suggested);
                if (combo == null)
                {
                    unresolvedEquipment.Add(index);
                    var check = ProcurementPlanCheck.Project(run);
                    foreach (string id in planOwned) check.Equipment.Add(id);
                    check.StoveLoaded = check.Equipment.Contains("B04");
                    string missing = new BusinessSession(check.ToSetup(run)).Missing(index, false);
                    Uncertain("equipment:" + pair.Key.id, pair.Key.name + " · 设备",
                        (missing == "" ? "设备搭配还需确认。" : "设备待补：" + missing + "。") +
                        "尚未找到完整的采购搭配，本项未计价。", true);
                    continue;
                }
                chosenByDish.Add(new KeyValuePair<int, List<string>>(index, combo));
                foreach (string id in combo)
                    if (!planOwned.Contains(id)) { suggested.Add(id); planOwned.Add(id); }
            }

            // One line per involved equipment id: owned (home/loaded/basket/
            // paid) and/or genuinely new. Never listed unless a planned dish
            // actually relies on it.
            var equipLines = new Dictionary<string, MealPlanLine>();
            foreach (var dish in chosenByDish)
                foreach (string id in dish.Value)
                    if (!equipLines.TryGetValue(id, out _))
                        equipLines[id] = new MealPlanLine { key = id, name = BusinessSession.Name(id), equipment = true, required = 1 };
            // Also surface home/loaded gear that already covers a dish.
            foreach (var pair in planned)
            {
                int index = Array.IndexOf(BusinessSession.Recipes, pair.Key);
                if (stocked.Missing(index, false) == "")
                {
                    // Loaded gear already suffices; nothing to surface.
                    continue;
                }
                // Find a sufficient, minimal set of the player's home items
                // for this plan. A compatible base already in the vehicle (or
                // basket) means an alternate one at home is optional, not a
                // requirement. Check the same equipment rules used above,
                // including newly suggested equipment, rather than matching
                // any Bxx prefix or asking the player to bring every stove.
                var equipmentForDish = new HashSet<string>(planOwned);
                if (!PlanCovered(pair.Key, index, equipmentForDish, suggested)) continue;
                foreach (string id in homeOnly)
                {
                    if (!UsedBy(id, pair.Key) || !equipmentForDish.Remove(id)) continue;
                    if (!PlanCovered(pair.Key, index, equipmentForDish, suggested))
                        equipmentForDish.Add(id);
                }
                foreach (string id in homeOnly)
                    if (UsedBy(id, pair.Key) && equipmentForDish.Contains(id))
                    {
                        if (!equipLines.TryGetValue(id, out var line))
                        { line = new MealPlanLine { key = id, name = BusinessSession.Name(id), equipment = true, required = 1 }; equipLines[id] = line; }
                    }
            }
            foreach (var pair in equipLines)
            {
                string id = pair.Key;
                var line = pair.Value;
                line.owned = ownedEquip.Contains(id) || homeOnly.Contains(id) ? 1 : 0;
                line.inBasket = basketEquip.Contains(id) ? 1 : 0;
                line.paidCargo = cargoEquip.Contains(id) ? 1 : 0;
                line.paidWaiting = waitingEquip.Contains(id) ? 1 : 0;
                line.missing = Math.Max(0, line.required - line.owned - line.inBasket - line.paidCargo);
                var sku = GameCatalog.Find(id) ?? SupplyCatalog.Find(id);
                if (sku != null) PriceSku(line, sku);
                if (homeOnly.Contains(id) && !ownedEquip.Contains(id))
                { line.channel = "home"; line.location = ""; line.note = "已在家中，出摊前要搬上车才管用。"; }
                else if (ownedEquip.Contains(id))
                { line.note = string.IsNullOrEmpty(line.note) ? "已装车，随时能用。" : line.note; }
                if (line.channel == "night")
                { pricedAll = false; line.note = string.IsNullOrEmpty(line.note) ? "只有夜间网店卖这件，白天市场买不到，未计价。" : line.note; }
                else if (line.channel != "counter" && line.channel != "shelf" && line.channel != "home")
                { pricedAll = false; line.note = string.IsNullOrEmpty(line.note) ? "这件设备今天没有在售渠道，缺口未计价。" : line.note; }
                report.lines.Add(line);
            }

            // ── Fuel: real pools, plan-chosen base, honest gaps ────────────
            var fuelDemand = new Dictionary<string, int>();
            string HomeEnergyHints(StallRecipe recipe, int recipeIndex)
            {
                var hints = new List<string>();
                var includedEnergy = new HashSet<string>();
                string[] candidates = BaseCandidates(recipe);
                foreach (string candidate in candidates)
                {
                    if (!planOwned.Contains(candidate)) continue;
                    string energy = BaseEnergy(candidate);
                    if (energy == "none" || energy == "" || includedEnergy.Contains(energy)) continue;
                    string device = energy == "gas" ? "E03" : energy == "cartridge" ? "B02" : energy == "coal" ? "E01" : "E05";
                    if (!homeOnly.Contains(device) || basketEquip.Contains(device) || cargoEquip.Contains(device)) continue;

                    // Test this candidate against the actual recipe/equipment
                    // rules with the other candidate bases removed. A stove
                    // that cannot use the player's vessel is not a real option
                    // and must not generate an unrelated home-energy hint.
                    var oneBase = new HashSet<string>(planOwned);
                    foreach (string other in candidates) if (other != candidate) oneBase.Remove(other);
                    if (!PlanCovered(recipe, recipeIndex, oneBase, new HashSet<string>())) continue;

                    includedEnergy.Add(energy);
                    string type = energy == "gas" ? "煤气炉" : energy == "cartridge" ? "卡式炉" : energy == "coal" ? "煤炉" : "用电设备";
                    string name = energy == "gas" ? "煤气罐" : energy == "cartridge" ? "卡式炉" : energy == "coal" ? "蜂窝煤" : "电瓶";
                    hints.Add(type + "：" + name + "在家（余 " + Math.Max(0, ownedFuel[EnergySlot(energy)]) + " 点）");
                }
                return hints.Count == 0 ? "" : "选用相应炉具才需装车：" + string.Join("；", hints) + "。";
            }
            foreach (var pair in planned)
            {
                int index = Array.IndexOf(BusinessSession.Recipes, pair.Key);
                var recipe = pair.Key;
                if (recipe.cold) continue;
                int baseCount = 0;
                foreach (string candidate in BaseCandidates(recipe))
                    if (planOwned.Contains(candidate)) baseCount++;
                string bottom = PlanBaseFor(recipe, planOwned);
                if (unresolvedEquipment.Contains(index) || baseCount != 1 || suggested.Contains(bottom))
                {
                    string hint = HomeEnergyHints(recipe, index);
                    Uncertain("energy:" + recipe.id, recipe.name + " · 能源",
                        hint == "" ? "先确认使用的炉具与能源余量，再估算补充数量；本项暂不计价。" : hint, false);
                    continue;
                }
                if (bottom == "")
                {
                    // No owned base at all: the combo suggestion above names
                    // one (priced in its equipment line); count its demand.
                    foreach (var dish in chosenByDish)
                        if (dish.Key == index)
                            foreach (string id in dish.Value)
                                if (id.Length == 3 && id[0] == 'B') bottom = id;
                    if (bottom == "")
                    {
                        Uncertain("energy:" + recipe.id, recipe.name + " · 能源",
                            "使用的炉具尚未确定，能源用量暂不计价。", false);
                        continue;
                    }
                }
                string energy = BaseEnergy(bottom);
                if (energy == "none" || energy == "") continue;
                int pot = PotCapacity(recipe, planOwned);
                int batches = (pair.Value + Math.Max(1, pot) - 1) / Math.Max(1, pot);
                Add(fuelDemand, energy, batches);
            }
            foreach (var pair in fuelDemand)
            {
                string energy = pair.Key;
                int slot = EnergySlot(energy);
                string homeDevice = energy == "gas" ? "E03" : energy == "cartridge" ? "B02" : energy == "coal" ? "E01" : "E05";
                bool homeEnergy = homeOnly.Contains(homeDevice) && !basketEquip.Contains(homeDevice) && !cargoEquip.Contains(homeDevice);
                var line = new MealPlanLine
                {
                    key = energy,
                    name = energy == "gas" ? "煤气余量" : energy == "cartridge" ? "卡式气余量" : energy == "coal" ? "蜂窝煤热能" : "电量",
                    required = pair.Value,
                    owned = Math.Max(0, ownedFuel[slot]),
                    atHome = homeEnergy ? Math.Max(0, ownedFuel[slot]) : 0,
                    inBasket = Math.Max(0, basketFuel[slot]),
                    paidCargo = Math.Max(0, cargoFuel[slot]),
                    paidWaiting = Math.Max(0, waitingFuel[slot]),
                };
                line.missing = Math.Max(0, line.required - line.owned - line.inBasket - line.paidCargo);
                if (line.missing > 0)
                {
                    var refill = CheapestFuel(run, energy, planOwned, basketEquip, cargoEquip);
                    if (refill != null)
                    {
                        // Energy gaps price by the refill's authored energy
                        // content per unit (E02 = 45, E01 = 30, ...), not its
                        // portions field.
                        PriceSku(line, refill, EnergyPerUnit(refill.id));
                        if (line.estimatedCents > 0)
                            line.note = "按每锅 1 点、大锅 4 份/深锅 2 份/普通锅 1 份估算；不足部分见上面这件补充装。";
                    }
                    else
                    {
                        pricedAll = false;
                        if (energy == "gas")
                            line.note = hasGasHardware
                                ? "煤气余量不够；一罐只能买一次，今天没有补充装可买，这个缺口没法补。"
                                : "没有煤气罐，也没有可买的补充渠道；需要一罐新煤气（见设备行）。";
                        else line.note = "这类能源今天没有能买到的补充装，缺口未计价。";
                    }
                }
                if (homeEnergy)
                {
                    string deviceName = energy == "gas" ? "煤气罐" : energy == "cartridge" ? "卡式炉" : energy == "coal" ? "蜂窝煤" : "电瓶";
                    string homeNote = deviceName + "在家（余 " + line.atHome + " 点），先装车。";
                    line.note = homeNote + (string.IsNullOrEmpty(line.note) ? "" : " " + line.note);
                }
                report.lines.Add(line);
            }

            // ── Cookability: every recipe, not just planned ones ───────────
            for (int i = 0; i < BusinessSession.Recipes.Length; i++)
                if (stocked.Missing(i) == "") report.cookableNow.Add(i);
            stocked.GetStockedFreeMethods(report.methodsNow);

            int cookable = report.cookableNow.Count;
            if (planned.Count == 0)
                report.summary = "未列采购清单 · 手头可做 " + cookable + " 道";
            else
                report.summary = "计划 " + planned.Count + " 道菜 · 现在就能开工 " +
                    CountPlannedCookable(planned, report) + "/" + planned.Count +
                    " · 手头一共能做 " + cookable + " 道" +
                    " · 建议采购约 ¥" + (report.estimatedCents / 100f).ToString("0.00") +
                    (pricedAll ? "" : "（部分用量或采购渠道待确认，未计价）") +
                    "。也可以改做手头已有的菜。";
            return report;
        }

        // ── helpers ────────────────────────────────────────────────────────
        static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

        static int CountPlannedCookable(List<KeyValuePair<StallRecipe, int>> planned, MealPlanReport report)
        {
            int count = 0;
            foreach (var pair in planned)
                if (report.cookableNow.Contains(Array.IndexOf(BusinessSession.Recipes, pair.Key))) count++;
            return count;
        }

        static bool UsedBy(string id, StallRecipe recipe)
            => !string.IsNullOrEmpty(id) && (Array.IndexOf(recipe.vessels, id) >= 0 || Array.IndexOf(recipe.tools, id) >= 0
               || recipe.id == "fried_skewer" && id == "U10"
               || !recipe.cold && Array.IndexOf(BaseCandidates(recipe), id) >= 0);

        static int PotCapacity(StallRecipe recipe, HashSet<string> equipment)
        {
            bool wok = BusinessSession.ParentRecipeIndex(Array.IndexOf(BusinessSession.Recipes, recipe)) == 0;
            if (wok) return equipment.Contains("V03") ? 4 : equipment.Contains("V02") ? 2 : 1;
            return Math.Max(1, recipe.batchSize);
        }

        static void Add(Dictionary<string, int> map, string key, int amount)
        { map.TryGetValue(key, out int current); map[key] = current + amount; }

        static string BaseEnergy(string id)
            => id == "B01" ? "coal" : id == "B02" ? "cartridge" : id == "B03" || id == "B04" ? "gas"
            : id == "" ? "none" : "electricity";

        static int EnergySlot(string energy)
            => energy == "coal" ? CoalSlot : energy == "gas" ? GasSlot : energy == "cartridge" ? CartSlot : ElecSlot;

        static string[] BaseCandidates(StallRecipe recipe)
            => recipe.microwave ? new[] { "B08", "B07" }
            : recipe.id == "fried_rice" ? new[] { "B04", "B03", "B02", "B01" }
            : recipe.id == "fried_skewer" ? new[] { "B04", "B03", "B05", "B02" }
            : recipe.id == "soup" ? new[] { "B04", "B03", "B06", "B05", "B02", "B01" }
            : new[] { "B04", "B03", "B05", "B02", "B01" };

        // First owned compatible base, with the production fallback of "any
        // owned base" when none has fuel left — so an empty tank still shows
        // a gap line instead of vanishing.
        static string PlanBaseFor(StallRecipe recipe, HashSet<string> equipment)
        {
            string fallback = "";
            foreach (string id in BaseCandidates(recipe))
            {
                if (!equipment.Contains(id)) continue;
                if (fallback == "") fallback = id;
            }
            return fallback;
        }

        // Equipment-only gap against a given equipment set, via the real
        // production Missing (fuel/stock excluded by includeStock:false).
        static bool PlanCovered(StallRecipe recipe, int index, HashSet<string> equipment, HashSet<string> extra)
        {
            var setup = new BusinessSetup
            {
                stoveLoaded = true,
                equipment = new HashSet<string>(equipment),
                inventory = new Dictionary<string, int>(),
                coalPoints = 30, gasPoints = 45, cartridgePoints = 45, electricityPoints = 45,
            };
            foreach (string id in extra) setup.equipment.Add(id);
            return new BusinessSession(setup).Missing(index, false) == "";
        }

        static void AddToPools(RunState run, string id, Dictionary<string, int> inv, HashSet<string> equip, int[] fuel)
        {
            if (id == "E02") { fuel[CartSlot] += 45; return; }
            if (SupplyCatalog.IsEquipment(id))
            {
                equip.Add(id);
                if (id == "E01") fuel[CoalSlot] += 30;
                else if (id == "E03") fuel[GasSlot] += 45;
                else if (id == "B02") fuel[CartSlot] += 15;
                else if (id == "E05") fuel[ElecSlot] += 45;
                return;
            }
            var item = GameCatalog.Find(id);
            if (item == null) return;
            string key = SupplyCatalog.IngredientId(id);
            inv.TryGetValue(key, out int current); inv[key] = current + item.portions;
        }

        // Cheapest actually-retailed SKU for a resource key. FREEDOM19: the
        // same consumable may be bought repeatedly, so an id already in the
        // basket or cargo is no longer excluded — the estimate covers the
        // extra units. null = nothing on sale today.
        static ItemDefinition CheapestRetail(RunState run, string key)
        {
            ItemDefinition best = null; int cost = int.MaxValue;
            void Consider(ItemDefinition item)
            {
                if (item == null) return;
                if (SupplyCatalog.IsEquipment(item.id)) return;
                if (SupplyCatalog.IngredientId(item.id) != key) return;
                int price = run.PriceCents(item.id);
                if (price < cost) { best = item; cost = price; }
            }
            foreach (var item in GameCatalog.Items) Consider(item);
            foreach (var item in SupplyCatalog.Items) Consider(item);
            return best;
        }

        static ItemDefinition CheapestPackage(RunState run, StallRecipe recipe)
        {
            ItemDefinition best = null; int cost = int.MaxValue;
            foreach (string pkg in recipe.packages)
            {
                // Packaging repeats freely now; only retail existence and
                // price decide the suggestion.
                var item = SupplyCatalog.Find(pkg) ?? GameCatalog.Find(pkg);
                if (item == null) continue;
                if (!SupplyCatalog.IsCounterItem(pkg) && !SupplyCatalog.IsShelfCarton(pkg)) continue;
                int price = run.PriceCents(pkg);
                if (price < cost) { best = item; cost = price; }
            }
            return best;
        }

        // Real energy content per purchased refill unit, matching the
        // production projection in ProcurementPlanCheck.AddProduct.
        static int EnergyPerUnit(string id)
            => id == "E02" ? 45 : id == "E01" ? 30 : id == "E03" ? 45 : id == "E05" ? 45 : id == "B02" ? 15 : 0;

        // One compatible heat base + vessel + tool (+U10 for fried_skewer),
        // validated through the real BusinessSession equipment Missing.
        static List<string> CheapestCombo(RunState run, StallRecipe recipe, HashSet<string> planOwned, HashSet<string> alreadySuggested)
        {
            var bases = new List<string> { "" };
            if (!recipe.cold && !recipe.microwave)
                foreach (var item in SupplyCatalog.Items)
                    if (item.id.Length == 3 && item.id[0] == 'B' && CanBuy(run, item.id)) bases.Add(item.id);
            if (recipe.microwave)
                foreach (var item in SupplyCatalog.Items)
                    if ((item.id == "B07" || item.id == "B08") && CanBuy(run, item.id)) bases.Add(item.id);
            var vessels = new List<string> { "" };
            foreach (string vessel in recipe.vessels) if (CanBuy(run, vessel)) vessels.Add(vessel);
            var tools = new List<string> { "" };
            foreach (string tool in recipe.tools) if (CanBuy(run, tool)) tools.Add(tool);

            int index = Array.IndexOf(BusinessSession.Recipes, recipe);
            List<string> best = null; int bestCost = int.MaxValue;
            foreach (string bottom in bases) foreach (string vessel in vessels) foreach (string tool in tools)
            {
                var buys = new List<string>();
                if (!string.IsNullOrEmpty(bottom)) buys.Add(bottom);
                if (!string.IsNullOrEmpty(vessel)) buys.Add(vessel);
                if (!string.IsNullOrEmpty(tool)) buys.Add(tool);
                if (recipe.id == "fried_skewer" && !planOwned.Contains("U10") && !alreadySuggested.Contains("U10"))
                { if (!CanBuy(run, "U10")) continue; buys.Add("U10"); }
                // Only genuinely new candidates go through AddProduct; the
                // plan-owned set is unioned in afterwards, adding no fuel.
                var fresh = new List<string>();
                foreach (string id in buys) if (!planOwned.Contains(id) && !alreadySuggested.Contains(id)) fresh.Add(id);
                var projection = ProcurementPlanCheck.Project(run, fresh, includeBasket: false);
                foreach (string id in planOwned) projection.Equipment.Add(id);
                foreach (string id in alreadySuggested) projection.Equipment.Add(id);
                // This is the plan's hypothetical loaded set, not the live
                // vehicle. Keep its B04 flag consistent so ToSetup does not
                // discard an owned home stove while checking one missing tool.
                projection.StoveLoaded = projection.Equipment.Contains("B04");
                var session = new BusinessSession(projection.ToSetup(run));
                if (session.Missing(index, false) != "") continue;
                int cost = 0;
                foreach (string id in fresh) cost += run.PriceCents(id);
                if (cost < bestCost) { best = buys; bestCost = cost; }
            }
            return best;
        }

        static bool CanBuy(RunState run, string id)
        {
            if (!SupplyCatalog.IsCounterItem(id) && !SupplyCatalog.IsShelfCarton(id)) return false;
            if (run.Cart.Contains(id) || run.Cargo.Contains(id)) return false;
            bool emptyCoal = id == "E01" && run.CoalPoints == 0;
            if (run.HomeEquipment.Contains(id) && !emptyCoal) return false;
            if (run.LoadedEquipment.Contains(id) && !emptyCoal) return false;
            return true;
        }

        // A real recharge product for a genuinely short energy pool.
        static ItemDefinition CheapestFuel(RunState run, string energy, HashSet<string> planOwned, HashSet<string> basketEquip, HashSet<string> cargoEquip)
        {
            string[] candidates = energy == "gas" ? new[] { "E03" }
                : energy == "cartridge" ? new[] { "E02", "B02" }
                : energy == "coal" ? new[] { "E01" }
                : new[] { "E05" };
            ItemDefinition best = null; int cost = int.MaxValue;
            foreach (string id in candidates)
            {
                if (id == "E02")
                {
                    // FREEDOM19: a refill pack is a repeatable consumable —
                    // units already in basket/cargo never block buying more.
                }
                else
                {
                    // Hardware under the one-unit rule: a second copy can
                    // never be bought. Empty E01 is the refill exception.
                    bool exists = planOwned.Contains(id) || basketEquip.Contains(id) || cargoEquip.Contains(id);
                    if (exists && !(id == "E01" && run.CoalPoints == 0)) continue;
                }
                var item = SupplyCatalog.Find(id);
                if (item == null || (!SupplyCatalog.IsCounterItem(id) && !SupplyCatalog.IsShelfCarton(id))) continue;
                int price = run.PriceCents(id);
                if (price < cost) { best = item; cost = price; }
            }
            return best;
        }
    }
}
