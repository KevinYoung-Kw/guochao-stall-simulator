using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // The same state model drives keyboard actions, local UI buttons and smoke tests.
    public sealed partial class RunState
    {
        public GamePhase Phase { get; private set; } = GamePhase.Home;
        public float Remaining { get; private set; } = 60;
        public int CashCents { get; private set; } = 32000;
        public int SpentCents { get; private set; }
        public int GasPoints { get; private set; }
        public int CartridgePoints { get; private set; }
        public int CoalPoints { get; private set; }
        public int ElectricityPoints { get; private set; }
        public bool HoldingStove { get; private set; }
        public string HeldGrocery { get; private set; } = "";
        public bool StoveLoaded { get; private set; }
        public bool Paid { get; private set; }
        public bool Loaded { get; private set; }
        public bool Paused { get; set; }
        public bool TimeoutSettlement { get; private set; }
        public float MarketUsed { get; private set; }
        public bool CheckoutInProgress { get; private set; }
        // CHECKOUT10: an open selection screen freezes only the market timer and
        // player movement. It is transient UI state, never persisted; loading a
        // save with it unset safely cancels an unconfirmed selection.
        public bool CheckoutSelectionOpen { get; set; }
        public float CheckoutRemaining { get; private set; }
        public float CheckoutDuration { get; }
        // CHECKOUT10: ids confirmed on the selection screen awaiting the human
        // service. null = legacy full-basket checkout. Never persisted: a save
        // taken mid-service safely cancels the unconfirmed selection instead of
        // restoring a full-basket auto-deduction.
        private List<string> pendingCheckoutSelection;
        public IReadOnlyList<string> PendingCheckoutSelection => pendingCheckoutSelection;
        // CHECKOUT10 guardrail: once a subset was successfully confirmed at
        // the counter, the market clock may never auto-buy anything outside
        // that selection (leave-counter interrupt, mid-service save/load, or
        // capacity change). Persisted: old saves lack the field and default
        // to false, preserving the legacy full-basket timeout settlement.
        public bool ConfirmedCheckoutRequiresSelection { get; private set; }
        // Safely retract an unconfirmed selection: stops the service, deducts
        // nothing, leaves the basket untouched.
        public void CancelPendingCheckout()
        { CheckoutInProgress = false; CheckoutRemaining = 0; pendingCheckoutSelection = null; }
        public RunProfile Profile { get; }
        public readonly List<string> HomeEquipment = new List<string>();
        public readonly List<string> LoadedEquipment = new List<string>();
        public readonly Dictionary<string,int> Supplies = new Dictionary<string,int>();
        public string HeldEquipmentId { get; private set; } = "";
        public string ActiveHomeEquipment => HomeEquipment.Find(id=>!LoadedEquipment.Contains(id))??"";
        public int CartCapacity => Profile?.CartCapacity??9;
        public int CartCellsUsed => ShoppingBasketLayout.UsedCells(Cart);
        public bool CanFitCart(string id)
        {var proposed=new List<string>(Cart);proposed.Add(id);return ShoppingBasketLayout.CanFit(proposed,CartCapacity);}
        public float HomeDuration => Profile?.HomeSeconds??60;
        public bool HasUnloadedEquipment => !string.IsNullOrEmpty(ActiveHomeEquipment);
        public string Message { get; private set; } = "走近灶台，按 E 拿取；再搬到门外车边。";
        public event Action<int> CheckoutCompleted;
        public readonly List<string> Cart = new List<string>();
        public readonly List<string> Cargo = new List<string>();
        // FREEDOM19: every Cart/Cargo occurrence is one real physical unit.
        // Consumables — food, condiments, packaging, and the E02 cartridge
        // refill — may repeat freely in one basket and across later paid /
        // loaded batches; durable hardware stays unique. E01 keeps its
        // authored empty-coal refill exception at the purchase gates instead
        // of becoming generally repeatable. Classification reuses the real
        // SupplyCatalog.IsEquipment, never prefix guesses.
        public static bool IsRepeatableSupply(string id)
        { return id=="E02"||!SupplyCatalog.IsEquipment(id); }
        // PLAN18: the home-authored meal plan. Pure information: editing it
        // never moves stock/cash, never fills the basket, never opens stalls.
        public readonly List<MealPlanChoice> MealPlan = new List<MealPlanChoice>();
        // portions==0 deletes; positive adds or updates. Unknown recipe ids
        // and negative amounts are rejected with false. Selection values are
        // always copied, never shared with caller-side entries.
        public bool SetMealPlan(string recipeId, int portions)
        {
            if (portions < 0) return false;
            bool known = false;
            foreach (var recipe in BusinessSession.Recipes)
                if (recipe.id == recipeId) { known = true; break; }
            if (!known) return false;
            int existing = -1;
            for (int i = 0; i < MealPlan.Count; i++)
                if (MealPlan[i] != null && MealPlan[i].recipeId == recipeId) { existing = i; break; }
            if (portions == 0)
            {
                if (existing >= 0) MealPlan.RemoveAt(existing);
                return true;
            }
            if (existing >= 0) MealPlan[existing] = new MealPlanChoice { recipeId = recipeId, portions = portions };
            else MealPlan.Add(new MealPlanChoice { recipeId = recipeId, portions = portions });
            return true;
        }
        public Func<IReadOnlyList<string>,IReadOnlyList<string>,string> LoadValidator;
        // A tunable prototype service duration, not a duration asserted by the design document.
        public RunState(float checkoutServiceSeconds = 4f, RunProfile profile = null)
        {
            Profile=profile; CheckoutDuration=profile!=null&&profile.Has("商超老会员")?.05f:Mathf.Max(.25f,checkoutServiceSeconds);
            Remaining=HomeDuration;
            if(profile==null){HomeEquipment.Add(GameCatalog.StoveId);return;}
            CashCents=profile.InitialCashCents;LoadedEquipment.Add("U01");
            foreach(string id in profile.Gear)
            {
                // FLAVOR12：开局资料给的风味瓶（如 Career2 的 S01）是消耗库存，
                // 按 StallFlavorMenu 每瓶份数进 Supplies；未实现/未知的 Sxx 走原
                // 有设备路径，绝不自造份数。
                StallFlavor flavor=StallFlavorMenu.Find(id);
                if(flavor!=null){Supplies[id]=flavor.portionsPerBottle;continue;}
                if(EquipmentVisualFactory.IsHeavy(id))HomeEquipment.Add(id);
                else if(id.StartsWith("P"))Supplies[id]=50;
                else LoadedEquipment.Add(id);
            }
            StoveLoaded=HomeEquipment.Count==0;
            foreach(string id in profile.Gear)
            {if(id=="E03")GasPoints+=45;else if(id=="B02"){GasPoints+=15;CartridgePoints+=15;}else if(id=="E01")CoalPoints+=30;else if(id=="E05")ElectricityPoints+=45;}
            Message=HomeEquipment.Count==0?"整理好随身家当，出门采购所需设备。":"把核心设备搬上门外三轮车，再出发采购。";
        }
        public int PriceCents(string id) => Mathf.RoundToInt(GameCatalog.Find(id).priceCents*(Profile?.PurchaseMultiplier??1));
        public int CartTotalCents
        {
            get { int sum = 0; foreach (string id in Cart) sum += PurchaseItemPriceCents(id); return sum; }
        }
        public string StartTime => FormatStartTime(MarketUsed);

        // Each carried carton/pack continues to occupy physical space next day.
        // Zero stock does not leave an invisible full-weight box on the vehicle.
        public IEnumerable<string> PackingSupplies
        {
            get
            {
                foreach(string id in new SortedSet<string>(Supplies.Keys))
                {
                    // FLAVOR12：风味瓶按每瓶份数折算占格，缺 GameCatalog 定义时
                    // 不再退化为 1 份一格（否则 Career2 开局 S01=40 格直接撑爆车）。
                    int unit=GameCatalog.Find(id)?.portions??StallFlavorMenu.Find(id)?.portionsPerBottle??(id.StartsWith("P")?50:1);
                    int boxes=Mathf.CeilToInt(Supplies[id]/(float)Mathf.Max(1,unit));
                    for(int i=0;i<boxes;i++)yield return id;
                }
            }
        }
        public RunSaveData CaptureSave()
        {
            var paidPrices=new List<UsedPurchasePrice>();foreach(var pair in UsedPriceOverrides)paidPrices.Add(new UsedPurchasePrice{id=pair.Key,priceCents=pair.Value});
            var quantities=new List<RunStockSave>();
            foreach(string id in new SortedSet<string>(Supplies.Keys))quantities.Add(new RunStockSave{id=id,quantity=Supplies[id]});
            return new RunSaveData{phase=Phase,cashCents=CashCents,spentCents=SpentCents,resaleIncomeCents=ResaleIncomeCents,resales=Resales.ToArray(),disposedPurchaseCents=DisposedPurchaseCents,usedBoughtIds=UsedBoughtIds.ToArray(),usedPriceOverrides=paidPrices.ToArray(),returnedPaidCargo=ReturnedPaidCargo.ToArray(),remaining=Remaining,
                gas=GasPoints,cartridge=CartridgePoints,coal=CoalPoints,electricity=ElectricityPoints,marketUsed=MarketUsed,
                holdingStove=HoldingStove,stoveLoaded=StoveLoaded,heldGrocery=HeldGrocery,heldEquipmentId=HeldEquipmentId,
                paid=Paid,loaded=Loaded,timeoutSettlement=TimeoutSettlement,confirmedCheckoutRequiresSelection=ConfirmedCheckoutRequiresSelection,
                // A selection confirmed but not yet paid is transient: the save
                // drops it (safe cancel) instead of restoring a service that
                // could deduct a stale subset.
                // A cancelled or completed service never restores a leftover
                // countdown; a save with one persisted only prompts re-queuing.
                checkoutInProgress=CheckoutInProgress&&pendingCheckoutSelection==null,
                checkoutRemaining=CheckoutInProgress?CheckoutRemaining:0,checkoutDuration=CheckoutDuration,message=Message,
                profile=Profile==null?null:JsonUtility.FromJson<RunProfile>(JsonUtility.ToJson(Profile)),
                homeEquipment=HomeEquipment.ToArray(),loadedEquipment=LoadedEquipment.ToArray(),cart=Cart.ToArray(),cargo=Cargo.ToArray(),supplies=quantities.ToArray(),
                mealPlan=CaptureMealPlan()};
        }
        public static RunState Restore(RunSaveData data)
        {
            if(data==null||data.schemaVersion!=1||!Enum.IsDefined(typeof(GamePhase),data.phase)||data.cashCents<0||data.remaining<0||data.resaleIncomeCents<0)
                throw new InvalidOperationException("经营准备存档无效或版本不兼容。");
            if(data.profile!=null&&!data.profile.Validate(out _))throw new InvalidOperationException("存档职业资料无效。");
            var state=new RunState(data.checkoutDuration,data.profile){Phase=data.phase,CashCents=data.cashCents,SpentCents=data.spentCents,ResaleIncomeCents=data.resaleIncomeCents,DisposedPurchaseCents=data.disposedPurchaseCents,
                Remaining=data.remaining,MarketUsed=data.marketUsed,GasPoints=data.gas,CartridgePoints=data.cartridge,CoalPoints=data.coal,ElectricityPoints=data.electricity,
                HoldingStove=data.holdingStove,StoveLoaded=data.stoveLoaded,HeldGrocery=data.heldGrocery??"",HeldEquipmentId=data.heldEquipmentId??"",
                Paid=data.paid,Loaded=data.loaded,TimeoutSettlement=data.timeoutSettlement,CheckoutInProgress=data.checkoutInProgress,ConfirmedCheckoutRequiresSelection=data.confirmedCheckoutRequiresSelection,
                CheckoutRemaining=data.checkoutRemaining,Message=data.message??"",Paused=true};
            state.HomeEquipment.Clear();state.LoadedEquipment.Clear();state.Supplies.Clear();
            state.HomeEquipment.AddRange(data.homeEquipment??Array.Empty<string>());state.LoadedEquipment.AddRange(data.loadedEquipment??Array.Empty<string>());
            state.Cart.AddRange(data.cart??Array.Empty<string>());state.Cargo.AddRange(data.cargo??Array.Empty<string>());
            ValidateSavedGoods(state.Cart,"购物车");
            ValidateSavedGoods(state.Cargo,"已装车货物");
            // Older sample saves left Paid true after moving the entire
            // basket to Cargo. Paid now describes only goods awaiting load.
            if(state.Cart.Count==0)state.Paid=false;
            var savedDurable=new HashSet<string>();
            foreach(string id in state.Cart)if(!IsRepeatableSupply(id)&&id!="E01")savedDurable.Add(id);
            foreach(string id in state.Cargo)
                if(!IsRepeatableSupply(id)&&id!="E01"&&savedDurable.Contains(id))
                    throw new InvalidOperationException("存档货物记录无效：耐用设备 "+id+" 同时出现在购物车与已装车清单。");
            foreach(var entry in data.supplies??Array.Empty<RunStockSave>())
            {if(entry.quantity<0||string.IsNullOrEmpty(entry.id)||state.Supplies.ContainsKey(entry.id))throw new InvalidOperationException("存档库存无效。");state.Supplies[entry.id]=entry.quantity;}
            var usedIds=new HashSet<string>();foreach(string id in data.usedBoughtIds??Array.Empty<string>())
            {if(Array.IndexOf(RecoveryRules.GuaranteedUsedIds,id)<0||!usedIds.Add(id))throw new InvalidOperationException("本日旧货限购记录无效。");state.UsedBoughtIds.Add(id);}
            foreach(var row in data.usedPriceOverrides??Array.Empty<UsedPurchasePrice>())
            {if(row==null||row.priceCents<=0||!state.UsedBoughtIds.Contains(row.id)||state.UsedPriceOverrides.ContainsKey(row.id))throw new InvalidOperationException("旧货采购记录无效。");state.UsedPriceOverrides.Add(row.id,row.priceCents);}
            if(state.DisposedPurchaseCents<0||state.DisposedPurchaseCents>state.SpentCents)throw new InvalidOperationException("已售采购成本无效。");
            state.ReturnedPaidCargo.AddRange(data.returnedPaidCargo??Array.Empty<string>());
            state.Resales.AddRange(data.resales??Array.Empty<CampaignResaleRow>());
            long resaleSum=0;foreach(var row in state.Resales){if(row==null||row.amountCents<=0||!RecoveryRules.IsDurable(row.id))throw new InvalidOperationException("旧货成交记录无效。");resaleSum+=row.amountCents;}
            if(resaleSum!=state.ResaleIncomeCents)throw new InvalidOperationException("旧货收款与成交记录不一致。");
            foreach(string id in state.ReturnedPaidCargo)if(GameCatalog.Find(id)==null)throw new InvalidOperationException("带回家货物记录无效。");
            RestoreMealPlan(state, data.mealPlan);
            return state;
        }
        // FREEDOM19 save guards: every saved entry is one real unit. Unknown
        // ids are rejected; repeats are accepted only for consumables (and the
        // legacy E01 empty-coal refill), while durable hardware must stay
        // unique inside each list (cross-list uniqueness is checked by the
        // Restore caller above). Old saves hold at most one occurrence per id
        // and therefore always pass.
        static void ValidateSavedGoods(List<string> ids,string label)
        {
            var counts=new Dictionary<string,int>();
            foreach(string id in ids)
            {
                if(string.IsNullOrEmpty(id)||GameCatalog.Find(id)==null)
                    throw new InvalidOperationException("存档"+label+"记录无效。");
                counts.TryGetValue(id,out int count);counts[id]=count+1;
            }
            foreach(var pair in counts)
                if(pair.Value>1&&!IsRepeatableSupply(pair.Key)&&pair.Key!="E01")
                    throw new InvalidOperationException("存档"+label+"记录无效：耐用设备 "+pair.Key+" 出现多次。");
        }
        // Deep copy: the save array must never alias live list entries.
        MealPlanChoice[] CaptureMealPlan()
        {
            var saved = new List<MealPlanChoice>();
            foreach(var choice in MealPlan)
                if(choice!=null) saved.Add(new MealPlanChoice{recipeId=choice.recipeId,portions=choice.portions});
            return saved.ToArray();
        }
        // Old saves carry a null array = empty plan. Invalid entries (unknown
        // recipe, non-positive portions, duplicates) are safely ignored and
        // can never leak into production data.
        static void RestoreMealPlan(RunState state, MealPlanChoice[] saved)
        {
            state.MealPlan.Clear();
            foreach(var entry in saved??Array.Empty<MealPlanChoice>())
            {
                if(entry==null||entry.portions<=0||string.IsNullOrEmpty(entry.recipeId))continue;
                if(state.MealPlan.Exists(c=>c.recipeId==entry.recipeId))continue;
                bool known=false;foreach(var recipe in BusinessSession.Recipes)if(recipe.id==entry.recipeId){known=true;break;}
                if(!known)continue;
                state.MealPlan.Add(new MealPlanChoice{recipeId=entry.recipeId,portions=entry.portions});
            }
        }
        public static RunState FromCampaign(CampaignState campaign,float checkoutSeconds)
        {
            var state=new RunState(checkoutSeconds,campaign.profile){CashCents=campaign.cashCents,GasPoints=campaign.gas,CartridgePoints=campaign.cartridge,CoalPoints=campaign.coal,ElectricityPoints=campaign.electricity};
            state.HomeEquipment.Clear();state.LoadedEquipment.Clear();state.Supplies.Clear();
            foreach(var item in campaign.equipment)
            {if(EquipmentVisualFactory.IsHeavy(item.id))state.HomeEquipment.Add(item.id);else state.LoadedEquipment.Add(item.id);}
            foreach(var item in campaign.stock)if(item.quantity>0)state.Supplies[item.id]=item.quantity;
            state.StoveLoaded=state.HomeEquipment.Count==0;
            state.Message="第 "+campaign.dayIndex+" 天 · 昨晚收好的家当和余料都在，先把今天要用的重器搬上车。";
            return state;
        }

        public static string FormatStartTime(float usedSeconds)
        {
            int minutes = 8 * 60 + Mathf.Max(0, Mathf.FloorToInt(usedSeconds + .0001f) - 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        public void Tick(float seconds, bool stillAtCashier = true)
        {
            if (Paused || (Phase != GamePhase.Home && Phase != GamePhase.Market)) return;
            // A paid basket that failed the capacity check remains owned and
            // actionable. Do not auto-settle it repeatedly or discard it.
            if(PendingPaidLoad)return;
            // CHECKOUT10: while the checkout selection screen is open the market
            // timer holds still, but this is not the global pause: business and
            // animation timelines keep running.
            if(CheckoutSelectionOpen)return;
            float elapsed = Mathf.Min(Mathf.Max(0, seconds), Remaining);
            Remaining -= elapsed;
            if (Phase == GamePhase.Market)
            {
                MarketUsed += elapsed;
                if (CheckoutInProgress && !stillAtCashier)
                {
                    bool hadSelection = pendingCheckoutSelection != null;
                    CancelPendingCheckout();
                    Message = hadSelection
                        ? "已离开收银服务区，本次结账选择未提交，尚未扣款。商品仍在购物车，请回到柜台重新确认。"
                        : "已离开收银服务区，本次排队中断，尚未扣款。请返回柜台重新排队。";
                }
                if (CheckoutInProgress)
                {
                    CheckoutRemaining = Mathf.Max(0, CheckoutRemaining - elapsed);
                    if (CheckoutRemaining <= .0001f)
                    { CheckoutInProgress = false; CompletePayment(); }
                }
            }
            if (Remaining > .0001f) return;
            Remaining = 0;
            if (Phase == GamePhase.Home)
            {
                bool leftStove = !StoveLoaded;
                EnterMarket();
                Message = leftStove ? "整备时间到：已装车物品带出，未装灶台留在屋内。" : "整备时间到：B04 已随车带到超市。";
            }
            else SettleTimeout();
        }

        public bool Act(string target, bool inRange, bool basketAtHand = true)
        {
            if (Paused || Phase == GamePhase.Complete || Phase == GamePhase.Error) return false;
            if (!inRange) return Reject("请先走近目标。" );
            if (CheckoutInProgress) return Reject("人工收银正在服务，请稍候；离开柜台会中断排队。" );
            if (Phase == GamePhase.Home)
            {
                switch (target)
                {
                    case "StovePickup":
                        if (StoveLoaded) return Reject("灶台已经装车。" );
                        if (HoldingStove)
                        {Message="已把"+CharacterCatalog.EquipmentName(HeldEquipmentId)+"放回原位。";HoldingStove=false;HeldEquipmentId="";return true;}
                        HoldingStove = true;
                        HeldEquipmentId=ActiveHomeEquipment;
                        Message = "已抱起"+CharacterCatalog.EquipmentName(HeldEquipmentId)+"，到门外车边装车。";
                        return true;
                    case "HomeCart":
                        if (!HoldingStove) return Reject(StoveLoaded ? "B04 已装车，可到出发点前往超市。" : "双手为空，请先在屋内拿取灶台。" );
                        var proposedEquipment=new List<string>(LoadedEquipment);proposedEquipment.Add(HeldEquipmentId);
                        string homeError=LoadValidator?.Invoke(proposedEquipment,Cargo);
                        if(!string.IsNullOrEmpty(homeError))return Reject(homeError);
                        if(!LoadedEquipment.Contains(HeldEquipmentId))LoadedEquipment.Add(HeldEquipmentId);
                        HeldEquipmentId="";HoldingStove=false;StoveLoaded=!HasUnloadedEquipment;
                        Message = HasUnloadedEquipment?"设备已装车，屋内还有核心家当可搬。":"家当已装车，走近出发点前往超市。";
                        return true;
                    case "HomeExit":
                        if (HoldingStove) return Reject("请先在车边放下手中的灶台。" );
                        EnterMarket();
                        return true;
                }
            }
            else
            {
                if (target == "ShoppingCart") return StowHeldGrocery();
                if (!string.IsNullOrEmpty(HeldGrocery))
                {
                    if (GameCatalog.Find(HeldGrocery).anchor != target)
                        return Reject("先把手中这一箱放进购物车，或放回原货架。");
                    string name = GameCatalog.Find(HeldGrocery).name;
                    HeldGrocery = ""; Message = $"已把{name}放回货架，现金未扣款。";
                    return true;
                }
                foreach (ItemDefinition item in GameCatalog.Items)
                    if (target == item.anchor)
                    {
                        int index = Cart.IndexOf(item.id);
                        // FREEDOM19: E on a shelf whose SKU is already in the
                        // basket takes ANOTHER real unit for consumables; only
                        // durable hardware keeps the legacy return-the-one-box
                        // behaviour (and R / receipt return still removes one
                        // occurrence explicitly).
                        if (index >= 0 && !Paid && !IsRepeatableSupply(item.id))
                            return basketAtHand ? ReturnItem(index, target, inRange) : Reject("这一箱在购物车里，请把车推到货架旁再放回。");
                        return Take(item.id, basketAtHand);
                    }
                switch (target)
                {
                    case "Cashier": return BeginCheckout();
                    case "MarketCart": return Load();
                    case "MarketExit":
                        if (Cart.Count > 0) return Reject(Paid ? "还有已付款原料待装车，请先走到摊车旁。" : "购物车尚未付款，请先到人工收银处结账。" );
                        Phase = Profile==null?GamePhase.Complete:GamePhase.SiteSelection;
                        Message = "采购完成，选择今天的街区和出摊菜单。";
                        return true;
                }
            }
            return Reject("这里没有可执行的操作。" );
        }

        public bool ReturnItem(int index, string nearbyTarget, bool inRange)
        {
            if (!CanReturnItem(index,true)) return false;
            if (CheckoutInProgress) return Reject("人工收银正在服务，离开柜台中断排队后才可放回。" );
            ItemDefinition item = GameCatalog.Find(Cart[index]);
            if (!inRange || nearbyTarget != item.anchor) return Reject("请走近这箱商品对应的货架再放回。" );
            Cart.RemoveAt(index);
            Message = $"已放回一箱{item.name}，现金未扣款。";
            return true;
        }

        // FREEDOM19: returning one basket unit is NOT a purchase — it must
        // stay legal when the basket is full or cash is short, so it cannot
        // ride on CanPrimary/E-take-another. Read-only gate: phase, unpaid,
        // not in checkout service, hands empty, real basket slot, basket at
        // hand. Range, shelf-side, facing and contact stay with ReturnItem /
        // the GameFlow action pipeline.
        public bool CanReturnItem(int index, bool basketAtHand)
        {
            if (Paused || Phase != GamePhase.Market || index < 0 || index >= Cart.Count) return false;
            if (Paid || CheckoutInProgress || !string.IsNullOrEmpty(HeldGrocery)) return false;
            return basketAtHand;
        }

        public void SetMessage(string value) { Message = value; }
        public bool CanAddSupply(string id,out string reason)
        {
            if(Phase!=GamePhase.Market){reason="采购柜台只在市场营业时间内开放。";return false;}
            if(Paused){reason="游戏暂停中，先继续游戏再采购。";return false;}
            if(Paid){reason="先把已付款商品装上摊车，再继续采购。";return false;}
            if(CheckoutInProgress){reason="正在人工收银，请完成或中断排队后再选购。";return false;}
            if(Remaining<=0){reason="采购时间已结束，今天不能再买入。";return false;}
            if(!string.IsNullOrEmpty(HeldGrocery)){reason="先把手里的商品放进篮中或放回货架。";return false;}
            // BILL9 two-channel guard: the bill/counter never sells shelf
            // cartons or night-only goods; name the correct channel instead of
            // one generic "cannot buy" line.
            if(!SupplyCatalog.IsCounterItem(id))
            {
                string channel=SupplyCatalog.Channel(id);
                if(channel=="shelf")reason="整箱原料请在货架亲手搬取；本柜台只直购五金、包材与散称小份。";
                else if(channel=="night")reason="这件是夜间专用商品，请收摊回家后网购订购。";
                else reason="这件商品市场没有在售。";
                return false;
            }
            bool emptyCoalRefill=id=="E01"&&CoalPoints==0;
            // FREEDOM19: the at-home/ownership block applies only to durable
            // hardware. Repeatable consumables — including the E02 refill,
            // which some old manifests represented inside home/loaded
            // equipment lists — must stay purchasable regardless.
            if(!IsRepeatableSupply(id)&&HomeEquipment.Contains(id)&&!emptyCoalRefill){reason="这件设备已经在家，明早可以带上；不重复购买。";return false;}
            // FREEDOM19: durable hardware must stay unique across basket,
            // cargo and vehicle, so the legacy ownership block remains for
            // every non-repeatable id (E01's empty-coal refill keeps its
            // exact old bypass of the loaded-equipment check only).
            // Consumables and the E02 refill may already sit in basket /
            // cargo / vehicle and simply add another real unit.
            if(!IsRepeatableSupply(id)&&(Cart.Contains(id)||Cargo.Contains(id)||(LoadedEquipment.Contains(id)&&!emptyCoalRefill)))
            {reason="这件设备已在车上，不重复购买。";return false;}
            if(!CanFitCart(id)){reason="购物车的空格放不下这件物品，请先放回一些商品。";return false;}
            reason="";return true;
        }
        public bool AddSupply(string id)
        {
            if(!CanAddSupply(id,out string reason))return Reject(string.IsNullOrEmpty(reason)?"现在不能购买这件商品。":reason);
            Loaded=false;Cart.Add(id);Message="已放入购物车："+GameCatalog.Find(id).name+"，到收银处统一结账。";return true;
        }
        public void SetBusinessPhase(GamePhase phase) { Phase=phase;Paused=false; }
        public void SetBusinessCash(int cents) { CashCents=Mathf.Max(0,cents); }
        public void ReturnToMarket() { Phase=GamePhase.Market;Remaining=Mathf.Max(0,120-MarketUsed);CheckoutInProgress=false;Paid=false;Paused=false; }
        public void Fail(string error) { Phase = GamePhase.Error; Paused = true; Message = error; }

        public bool CanPrimary(string target, bool inRange, bool basketAtHand = true)
        {
            if (!inRange || Paused || CheckoutInProgress || Phase == GamePhase.Complete || Phase == GamePhase.Error) return false;
            if (Phase == GamePhase.Home)
            {
                if (target == "ParcelBox") return !HoldingStove && string.IsNullOrEmpty(HeldGrocery);
                if (target == "StovePickup") return !StoveLoaded;
                if (target == "HomeCart") return HoldingStove;
                return target == "HomeExit" && !HoldingStove;
            }
            // RECOVERY12: the authored recovery-table gate shares the supply
            // counter's exact rules; price/receipt/save judgements stay in
            // RecoveryRules and TryBuyUsedEquipment/TrySellEquipment.
            if(target=="SupplyCounter"||target=="RecoveryCounter")return string.IsNullOrEmpty(HeldGrocery)&&!CheckoutInProgress;
            if (!string.IsNullOrEmpty(HeldGrocery))
                return !Paid && (target == GameCatalog.Find(HeldGrocery).anchor || (target == "ShoppingCart" && CanFitCart(HeldGrocery)));
            if (target == "ShoppingCart") return false;
            foreach (ItemDefinition item in GameCatalog.Items)
                    if (target == item.anchor)
                    {
                        bool inCart = Cart.Contains(item.id);
                        // FREEDOM19: a consumable already in the basket never
                        // blocks taking another unit; durable hardware keeps
                        // the legacy return-the-box / already-aboard rules.
                        if (inCart && !IsRepeatableSupply(item.id)) return !Paid && basketAtHand;
                        return !Paid && (Remaining > 0 && (IsRepeatableSupply(item.id) || !Cargo.Contains(item.id)) && CanFitCart(item.id));
                    }
            // CHECKOUT10: budget or full-basket load mismatch must never block
            // opening the selection screen; only a missing basket or the wrong
            // spot does. The selection itself decides what can actually be paid.
            if (target == "Cashier") return !Paid && Cart.Count > 0;
            if (target == "MarketCart") return Paid && Cart.Count > 0;
            return target == "MarketExit" && Cart.Count == 0;
        }

        private void EnterMarket()
        {
            HoldingStove = false;
            Phase = GamePhase.Market; Remaining = 120; MarketUsed = 0;
            Message = "货架整箱亲手搬，采购柜台账单直购；都经人工收银 → 车边装载 → 出口出摊。";
        }

        private bool Take(string id, bool basketAtHand)
        {
            if(Remaining<=0)return Reject("采购已结束，请处理已付款货物。");
            if (Paid) return Reject("这批原料已结账，请先装车并前往出口。" );
            if (!string.IsNullOrEmpty(HeldGrocery)) return Reject("双手已抱着一箱，请先放入购物车或放回货架。");
            // FREEDOM19: only durable hardware is blocked by an identical unit
            // already pending or loaded; consumables repeat freely.
            if (!IsRepeatableSupply(id) && (Cart.Contains(id) || Cargo.Contains(id))) return Reject("这箱样品已取走，可在明细中选择放回货架。" );
            if (!CanFitCart(id)) return Reject("购物车没有能放下这箱商品的空格。" );
            Loaded=false;
            ItemDefinition item = GameCatalog.Find(id);
            if (basketAtHand)
            {
                Cart.Add(id);
                Message = $"已取一箱{item.name} · {item.portions}份 · ¥{item.Price:0.00}，尚未付款。";
            }
            else
            {
                HeldGrocery = id;
                Message = $"正抱着一箱{item.name}，走回购物车边按 E 放入篮中。";
            }
            return true;
        }

        private bool StowHeldGrocery()
        {
            if (string.IsNullOrEmpty(HeldGrocery)) return Reject("双手没有拿着原料。");
            if (Paid || !CanFitCart(HeldGrocery)) return Reject(Paid ? "这批原料已结账。" : "购物车没有能放下这箱商品的空格。");
            string id = HeldGrocery;
            if (!IsRepeatableSupply(id) && (Cart.Contains(id) || Cargo.Contains(id))) return Reject("这箱原料已经入篮。");
            Cart.Add(id); HeldGrocery = "";
            Message = $"已把一箱{GameCatalog.Find(id).name}放入购物车，尚未付款。";
            return true;
        }

        private bool BeginCheckout()
        {
            if (Paid) return Reject("这批原料已经付款，不会重复扣款。" );
            if (Cart.Count == 0) return Reject("购物车为空，请先到货架取原料。" );
            // CHECKOUT10: the legacy full-basket path receives the same human
            // service as a selection confirm. Budget or vehicle-fit mismatches
            // do not hard-refuse here; the single real payment at service
            // completion re-validates and, on failure, deducts nothing and
            // leaves the basket untouched.
            pendingCheckoutSelection = null;
            CheckoutInProgress = true; CheckoutRemaining = CheckoutDuration;
            Message = "已加入人工收银服务，请留在柜台附近。离开会中断排队，服务结束才扣款。";
            return true;
        }

        // CHECKOUT10: gate for opening the selection screen. Requires the real
        // cashier spot and an unpaid, non-empty basket; whole-basket budget or
        // vehicle-fit failures deliberately do not block it.
        public bool CanOpenCheckoutSelection(bool inRange)
        {
            return inRange && Phase == GamePhase.Market && !Paused && !CheckoutInProgress &&
                !Paid && Cart.Count > 0 && string.IsNullOrEmpty(HeldGrocery) && Remaining > 0;
        }

        // Read-only preview used by the selection UI: ids are basket
        // OCCURRENCES, not unique SKUs — the same consumable may appear once
        // per identical unit. Every requested occurrence must map to a real
        // unit currently in the cart (never more than the cart holds), the
        // budget is checked per occurrence, and the chosen goods plus
        // already-owned cargo must fit the vehicle. Never mutates, never
        // deducts, never invents quantity.
        public bool PreviewCheckoutSelection(IReadOnlyList<string> ids, out int totalCents, out string reason)
        {
            totalCents = 0; reason = "";
            if (Phase != GamePhase.Market) { reason = "收银选择只在市场采购时可用。"; return false; }
            if (Paused) { reason = "游戏暂停中，先继续游戏再结账。"; return false; }
            if (Paid) { reason = "本篮商品已经付款。"; return false; }
            if (CheckoutInProgress) { reason = "人工收银正在服务，请完成或中断排队后再选择。"; return false; }
            if (ids == null || ids.Count == 0) { reason = "请至少勾选一件要付款的商品。"; return false; }
            var requested = new Dictionary<string, int>();
            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id) || GameCatalog.Find(id) == null)
                { reason = "所选商品无效。"; totalCents = 0; return false; }
                requested.TryGetValue(id, out int count); requested[id] = count + 1;
            }
            foreach (var pair in requested)
            {
                int inCart = 0; foreach (string cartId in Cart) if (cartId == pair.Key) inCart++;
                if (pair.Value > inCart)
                { reason = "所选件数超过了购物车中这件商品的实际数量，请重新勾选。"; totalCents = 0; return false; }
                for (int i = 0; i < pair.Value; i++) totalCents += PurchaseItemPriceCents(pair.Key);
            }
            if (totalCents > CashCents)
            { reason = $"现金不足，还差 ¥{(totalCents - CashCents) / 100f:0.00}。可减少勾选或放回商品。"; return false; }
            string loadError = CheckPendingCargo(ids);
            if (!string.IsNullOrEmpty(loadError)) { reason = loadError; return false; }
            return true;
        }

        // Commits the chosen subset to the existing human checkout service:
        // nothing is deducted here. The single real payment happens when the
        // service completes (CompletePayment), which re-validates membership,
        // cash and vehicle fit, deducts exactly once, returns the explicitly
        // unselected goods to the shelf and leaves the paid goods in the cart
        // awaiting load. Any failure deducts nothing and clears nothing.
        public bool ConfirmCheckoutSelection(IReadOnlyList<string> ids, bool stillAtCashier = true)
        {
            if (!stillAtCashier)
                return Reject("已离开收银柜台，本次选择未提交。商品仍在购物车，未扣款。");
            if (!PreviewCheckoutSelection(ids, out int total, out string reason))
                return Reject(string.IsNullOrEmpty(reason) ? "现在不能确认这份结账选择。" : reason);
            pendingCheckoutSelection = new List<string>(ids);
            ConfirmedCheckoutRequiresSelection = true;
            CheckoutInProgress = true; CheckoutRemaining = CheckoutDuration;
            Message = $"已确认 {ids.Count} 件待付款 · ¥{total / 100f:0.00}，人工收银服务中。请留在柜台附近，离开会中断，服务结束才扣款。";
            return true;
        }

        private bool CompletePayment()
        {
            if (Paid) return false;
            var chosen = pendingCheckoutSelection;
            pendingCheckoutSelection = null;
            if (chosen != null)
            {
                // FREEDOM19: chosen ids are occurrences. Validate each id's
                // requested count against the real cart counts (per
                // occurrence), price per occurrence, and only then deduct.
                int total = 0; var requested = new Dictionary<string, int>();
                foreach (string id in chosen)
                {
                    if (string.IsNullOrEmpty(id) || GameCatalog.Find(id) == null)
                    { CheckoutInProgress = false; CheckoutRemaining = 0;
                      return Reject("所选商品已不在购物车，本次未扣款，所选与未选商品均未变动。请重新确认。"); }
                    requested.TryGetValue(id, out int count); requested[id] = count + 1;
                }
                foreach (var pair in requested)
                {
                    int inCart = 0; foreach (string cartId in Cart) if (cartId == pair.Key) inCart++;
                    if (pair.Value > inCart)
                    { CheckoutInProgress = false; CheckoutRemaining = 0;
                      return Reject("所选商品已不在购物车，本次未扣款，所选与未选商品均未变动。请重新确认。"); }
                    for (int i = 0; i < pair.Value; i++) total += PurchaseItemPriceCents(pair.Key);
                }
                if (total > CashCents)
                { CheckoutInProgress = false; CheckoutRemaining = 0;
                  return Reject($"现金不足，还差 ¥{(total - CashCents) / 100f:0.00}。本次未扣款，所选与未选商品均未变动。"); }
                string loadError = CheckPendingCargo(chosen);
                if (!string.IsNullOrEmpty(loadError))
                { CheckoutInProgress = false; CheckoutRemaining = 0;
                  return Reject(loadError + " 本次未扣款，所选与未选商品均未变动。"); }
                // Retain exactly the chosen occurrences (one cart slot each);
                // every unselected occurrence returns to the shelf uncharged.
                int returned = 0;
                for (int i = Cart.Count - 1; i >= 0; i--)
                {
                    string id = Cart[i];
                    if (requested.TryGetValue(id, out int keep) && keep > 0) { requested[id] = keep - 1; continue; }
                    Cart.RemoveAt(i); returned++;
                }
                CheckoutInProgress = false; CheckoutRemaining = 0;
                CashCents -= total; SpentCents += total; Paid = true;
                Message = $"已付 ¥{total / 100f:0.00}，所选商品留在购物车待装车。" +
                    (returned > 0 ? $"未勾选的 {returned} 件已退回货架，未扣款。" : "");
                var selectionObservers = CheckoutCompleted;
                if (selectionObservers != null) foreach (Action<int> observer in selectionObservers.GetInvocationList())
                    try { observer(total); } catch (Exception error) { Debug.LogWarning("Checkout observer failed after selection payment: " + error.Message); }
                return true;
            }
            string fullLoadError = CheckPendingCargo(Cart);
            if (!string.IsNullOrEmpty(fullLoadError))
            { CheckoutInProgress = false; CheckoutRemaining = 0;
              return Reject(fullLoadError + " 本次未扣款，购物车未变动。"); }
            int fullTotal = CartTotalCents;
            if (fullTotal > CashCents)
            { CheckoutInProgress = false; CheckoutRemaining = 0;
              return Reject($"现金不足，还差 ¥{(fullTotal - CashCents) / 100f:0.00}。本次未扣款，购物车未变动。"); }
            CheckoutInProgress = false; CheckoutRemaining = 0;
            CashCents -= fullTotal; SpentCents += fullTotal; Paid = true;
            Message = $"已付 ¥{fullTotal / 100f:0.00}，原料仍在购物车。请到室外摊车旁装载。";
            var observers=CheckoutCompleted;
            if(observers!=null)foreach(Action<int> observer in observers.GetInvocationList())
                try{observer(fullTotal);}catch(Exception error){Debug.LogWarning("Checkout observer failed after payment: "+error.Message);}
            return true;
        }

        private bool Load()
        {
            if (Cart.Count == 0) return Reject(Loaded ? "原料已装好，请走近出口。" : "没有待装原料。" );
            if (!Paid) return Reject("原料尚未付款，请先到人工收银处。" );
            string loadError=CheckPendingCargo(Cart);if(!string.IsNullOrEmpty(loadError))return Reject(loadError);
            Cargo.AddRange(Cart); Cart.Clear(); Loaded = true;
            // FREEDOM19: the previous batch stays owned through Cargo/SpentCents
            // (and the loaded receipt reads CargoPaidCents), so the paid flag
            // must clear for every profile — a later, legitimately purchased
            // batch can then be checked out and paid on its own.
            Paid=false;
            Message = "采购物品已装车，已有家当保留。走近出口选择出摊地点。";
            return true;
        }

        private string CheckPendingCargo(IReadOnlyList<string> pending)
        {var proposed=new List<string>(Cargo);foreach(string id in pending)proposed.Add(id);return LoadValidator?.Invoke(LoadedEquipment,proposed)??"";}

        private void SettleTimeout()
        {
            // The playable sample exposes one box per category (61.20 total),
            // within its 320.00 cash and the cart's three existing cargo positions.
            CheckoutInProgress = false; CheckoutRemaining = 0;
            // CHECKOUT10 guardrail: once a subset was confirmed at the counter,
            // the clock may only ever settle that subset — through the single
            // real payment path — and nothing else in the basket.
            if (ConfirmedCheckoutRequiresSelection)
            {
                SettleConfirmedSelectionTimeout();
                return;
            }
            // Legacy unconfirmed path (design case 6): full-basket settlement
            // via the ordinary CompletePayment/Load entry, unchanged.
            bool returnedHeld = !string.IsNullOrEmpty(HeldGrocery);
            HeldGrocery = "";
            int returnedForBudget=0,returnedForSpace=0;
            if(!Paid)
            {
                // Decide money and space for each occurrence together. A box
                // that cannot be afforded must not reserve vehicle space and
                // force a later affordable box back to the shelf.
                int budget=CashCents;
                var accepted=new List<string>();
                for(int i=0;i<Cart.Count;)
                {
                    string id=Cart[i];
                    int price=PurchaseItemPriceCents(id);
                    if(price>budget){Cart.RemoveAt(i);returnedForBudget++;continue;}
                    accepted.Add(id);
                    if(!string.IsNullOrEmpty(CheckPendingCargo(accepted)))
                    {accepted.RemoveAt(accepted.Count-1);Cart.RemoveAt(i);returnedForSpace++;continue;}
                    budget-=price;i++;
                }
            }
            if(Cart.Count>0&&!Paid)CompletePayment();
            if(Cart.Count>0&&!Load())
            {
                TimeoutSettlement=true;
                Message="采购已结束，已付款货物仍在篮中。可出售车上旧设备腾出空间后装车，或带已付货物回家整理。";
                return;
            }
            TimeoutSettlement = true; Phase = Profile==null?GamePhase.Complete:GamePhase.SiteSelection;
            Message = $"采购时间到：购物车原料已代结并装车，共扣款 ¥{SpentCents / 100f:0.00}。准备完成，开摊时刻 {StartTime}。";
            if (returnedHeld) Message += " 手中尚未入篮的箱子已放回货架，未扣款。";
            if(returnedForBudget>0)Message+=" 现金不足的 "+returnedForBudget+" 件已退回，未扣款。";
            if(returnedForSpace>0)Message+=" 车上无法装下的 "+returnedForSpace+" 件已退回，未扣款。";
        }

        // Guarded timeout: the confirmed service may still be standing at the
        // counter when the clock runs out; settle exactly the confirmed
        // selection through the same single CompletePayment() path. If the
        // service was cancelled (left counter / loaded save) or the selection
        // can no longer be paid, every unpaid item goes back to the shelf and
        // nothing is deducted.
        private void SettleConfirmedSelectionTimeout()
        {
            bool returnedHeld = !string.IsNullOrEmpty(HeldGrocery);
            HeldGrocery = "";
            if (Paid)
            {
                // The service completed before the clock ran out: only the
                // confirmed selection was paid. Just finish the load.
                if (Cart.Count > 0 && !Load())
                {
                    TimeoutSettlement = true;
                    Message = "采购已结束，已付款货物仍在篮中。可出售车上旧设备腾出空间后装车，或带已付货物回家整理。";
                    return;
                }
                TimeoutSettlement = true; Phase = Profile==null?GamePhase.Complete:GamePhase.SiteSelection;
                Message = $"采购时间到：柜台已确认所选已付款并装车，共扣款 ¥{SpentCents / 100f:0.00}。准备完成，开摊时刻 {StartTime}。";
                if (returnedHeld) Message += " 手中尚未入篮的箱子已放回货架，未扣款。";
                return;
            }
            // Keep pendingCheckoutSelection set here: CompletePayment() owns
            // consuming and clearing it, so the timeout pays exactly the
            // confirmed selection and never silently falls back to the
            // whole basket. On failure it returns false with nothing
            // deducted, and the shared tail below returns every unpaid item.
            if (!Paid && pendingCheckoutSelection != null)
            {
                if (CompletePayment())
                {
                    if (Cart.Count > 0 && !Load())
                    {
                        TimeoutSettlement = true;
                        Message = "采购已结束，已付款货物仍在篮中。可出售车上旧设备腾出空间后装车，或带已付货物回家整理。";
                        return;
                    }
                    TimeoutSettlement = true; Phase = Profile==null?GamePhase.Complete:GamePhase.SiteSelection;
                    Message = $"采购时间到：柜台已确认所选已代结并装车，共扣款 ¥{SpentCents / 100f:0.00}。准备完成，开摊时刻 {StartTime}。";
                    if (returnedHeld) Message += " 手中尚未入篮的箱子已放回货架，未扣款。";
                    return;
                }
                // Selection no longer payable: CompletePayment already explained
                // why and deducted nothing; the whole unpaid basket returns.
                // ConfirmedCheckoutRequiresSelection stays set: the clock run
                // out is an interruption, not a completed selection settle.
            }
            int returned = Cart.Count;
            Cart.Clear();
            Paid = false;
            TimeoutSettlement = true; Phase = Profile==null?GamePhase.Complete:GamePhase.SiteSelection;
            string reason = Message;
            Message = "采购时间到：本次结账未完成，购物车 " + returned + " 件已退回货架，未扣款。";
            if (!string.IsNullOrEmpty(reason)) Message += "（" + reason + "）";
            if (returnedHeld) Message += " 手中尚未入篮的箱子已放回货架，未扣款。";
        }

        private bool Reject(string value) { Message = value; return false; }
    }
}
