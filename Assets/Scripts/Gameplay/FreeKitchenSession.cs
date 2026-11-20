using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // FREEDOM19：自由厨房 + 柜台装餐 会话扩展。BusinessSession 为 partial
    // （声明见 BusinessSession.cs）；本文件只含自由批次/柜台餐的入口与事务，
    // 旧式菜谱路径逐字节保留在原文件。所有 Can/Find 为纯查询，不改任何状态。
    public sealed partial class BusinessSession
    {
        // 每视觉菜谱索引的自由生产守恒账（份数），随存档往返。
        private readonly Dictionary<int,int> freeBatchesStarted=new Dictionary<int,int>(),
            freePortionsStarted=new Dictionary<int,int>(),freePortionsWasted=new Dictionary<int,int>();
        // 柜台上一份一份的物理餐（pack-first；旧 v2 档为空队列）。
        public readonly List<CounterMeal> ReadyMeals=new List<CounterMeal>();

        public const int CounterCapacity=6;
        // Reviewer correction: B04 has two burners; cookware remains one physical item.
        HashSet<string> OccupiedBases(int exceptLane)
        {
            var counts=new Dictionary<string,int>();
            foreach(var lane in Lanes)if(lane.index!=exceptLane&&lane.freeBatch!=null)
            {string id=lane.freeBatch.baseId;if(id!="")counts[id]=counts.TryGetValue(id,out int n)?n+1:1;}
            var full=new HashSet<string>();foreach(var pair in counts)if(pair.Value>=(pair.Key=="B04"?2:1))full.Add(pair.Key);return full;
        }
        HashSet<string> AvailableKitchenEquipment(int exceptLane)
        {
            var available=new HashSet<string>(Equipment);
            foreach(var lane in Lanes)if(lane.index!=exceptLane&&lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.Dirty)
            {
                if(lane.freeBatch!=null)available.Remove(lane.freeBatch.vesselId);
                else foreach(string vessel in Recipes[lane.recipeIndex].vessels)if(available.Remove(vessel))break;
            }
            return available;
        }
        bool TryFreeHardware(FreeKitchenRules.MethodDef method,int lane,out string vessel,out string bottom,out string tool,int requiredLoad=0)
            =>FreeKitchenRules.TryBindHardware(method,AvailableKitchenEquipment(lane),EnergyAvailable,OccupiedBases(lane),out vessel,out bottom,out tool,requiredLoad);
        List<FreeKitchenRules.MethodDef> FreeOptions(FoodInput[] inputs,int lane)
        {
            var options=new List<FreeKitchenRules.MethodDef>();
            foreach(var def in FreeKitchenRules.Methods)
            {
                bool compatible=true;foreach(var input in inputs)if(!FreeKitchenRules.MethodAcceptsMaterial(def,input.itemId))compatible=false;
                if(compatible&&TryFreeHardware(def,lane,out string vessel,out _,out _,FreeKitchenRules.InputLoad(inputs))&&FreeKitchenRules.InputLoad(inputs)<=FreeKitchenRules.CapacityFor(def,vessel))options.Add(def);
            }
            return options;
        }
        static FoodInput[] WithIngredient(FreeKitchenBatch batch,string itemId)
        {
            var inputs=new List<FoodInput>();bool found=false;
            if(batch!=null)foreach(var input in batch.inputs){var copy=input.Copy();if(copy.itemId==itemId){copy.units++;found=true;}inputs.Add(copy);}
            if(!found)inputs.Add(new FoodInput(itemId,1));return inputs.ToArray();
        }
        // Preview for the departure checklist, before any equipment is occupied.
        internal void GetStockedFreeMethods(List<string> result)
        {
            foreach (var method in FreeKitchenRules.Methods)
            {
                if (!TryFreeHardware(method, -1, out _, out string bottom, out _, 1)) continue;
                if (!method.cold && !EnergyAvailable(FreeKitchenRules.BaseEnergy(bottom))) continue;
                foreach (var item in Inventory)
                {
                    if (item.Value < 1 || !FreeKitchenRules.IsAddableIngredient(item.Key) ||
                        !FreeKitchenRules.MethodAcceptsMaterial(method, item.Key)) continue;
                    result.Add(method.id);
                    break;
                }
            }
        }
        public bool IsFreeLane(int laneIndex)=>laneIndex>=0&&laneIndex<Lanes.Length&&Lanes[laneIndex].freeBatch!=null;
        public string[] FreeMethods(int laneIndex)
        {
            var result=new List<string>();if(laneIndex<0||laneIndex>=Lanes.Length)return result.ToArray();
            var inputs=Lanes[laneIndex].freeBatch?.inputs??Array.Empty<FoodInput>();
            foreach(var def in FreeOptions(inputs,laneIndex))result.Add(def.id);return result.ToArray();
        }
        public bool CanAddFreeIngredient(int laneIndex,string itemId,out string reason)
        {
            reason="";
            if(!Started||Finished){reason="先开摊再备菜。";return false;}
            if(laneIndex<0||laneIndex>=Lanes.Length){reason="没有这个锅位。";return false;}
            if(!FreeKitchenRules.IsAddableIngredient(itemId)){reason="这件物品不能直接下锅。";return false;}
            if(Count(itemId)<1){reason=Name(itemId)+"用完了。";return false;}
            var lane=Lanes[laneIndex];var batch=lane.freeBatch;
            if(batch==null&&(lane.step!=KitchenStep.Idle||lane.orderId>=0||lane.reservedOrderId>=0||lane.eggPrepPortions>0)
                ||batch!=null&&(lane.step!=KitchenStep.Prep||batch.product!=null)){reason="这口锅正忙，换个空锅。";return false;}
            var next=WithIngredient(batch,itemId);var options=FreeOptions(next,laneIndex);
            if(batch!=null&&batch.method!="")options.RemoveAll(method=>method.id!=batch.method);
            if(options.Count==0){reason="这套锅具装不下，或做不了这份搭配。";return false;}
            return true;
        }

        public int FindFreeLane(string itemId)
        {
            if(!FreeKitchenRules.IsAddableIngredient(itemId))return -1;
            int idle=-1;
            for(int i=0;i<Lanes.Length;i++)
            {
                KitchenLane lane=Lanes[i];
                if(lane.freeBatch!=null&&lane.freeBatch.product==null&&lane.step==KitchenStep.Prep
                    &&CanAddFreeIngredient(i,itemId,out _))return i;
                if(idle<0&&lane.freeBatch==null&&lane.step==KitchenStep.Idle&&lane.orderId<0&&lane.reservedOrderId<0
                    &&CanAddFreeIngredient(i,itemId,out _))idle=i;
            }
            return idle;
        }

        // ── 投料：真实 Actor 接触帧，恰扣 1 个所选库存单位 ─────────────
        public bool AddFreeIngredient(int laneIndex,string itemId)
        {
            if(!CanAddFreeIngredient(laneIndex,itemId,out string reason))return Reject(reason);
            var lane=Lanes[laneIndex];var inputs=WithIngredient(lane.freeBatch,itemId);
            var choices=FreeOptions(inputs,laneIndex);var method=choices[0];
            if(lane.freeBatch!=null&&lane.freeBatch.method!="")method=FreeKitchenRules.FindMethod(lane.freeBatch.method);
            if(!TryFreeHardware(method,laneIndex,out string vessel,out string bottom,out string tool,FreeKitchenRules.InputLoad(inputs)))return false;
            if(lane.freeBatch==null)lane.freeBatch=new FreeKitchenBatch{id=Guid.NewGuid().ToString("N")};
            lane.freeBatch.inputs=inputs;lane.freeBatch.vesselId=vessel;lane.freeBatch.baseId=bottom;lane.freeBatch.toolId=tool;
            lane.freeBatch.capacity=FreeKitchenRules.CapacityFor(method,vessel);
            lane.recipeIndex=FreeKitchenRules.VisualRecipeIndex(method.id);Inventory[itemId]--;
            lane.step=KitchenStep.Prep;lane.remaining=lane.duration=0;
            Message="已放入"+Name(itemId)+"，备好后开火。";Revision++;return true;
        }
        public bool SetFreeMethod(int laneIndex,string methodId)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            var lane=Lanes[laneIndex];var batch=lane.freeBatch;var method=FreeKitchenRules.FindMethod(methodId);
            if(batch==null||batch.product!=null||lane.step!=KitchenStep.Prep||method==null)return Reject("这口锅现在不能换做法。");
            foreach(var input in batch.inputs)if(!FreeKitchenRules.MethodAcceptsMaterial(method,input.itemId))return Reject("这些食材不适合这种做法。");
            if(!TryFreeHardware(method,laneIndex,out string vessel,out string bottom,out string tool,FreeKitchenRules.InputLoad(batch.inputs))
                ||FreeKitchenRules.InputLoad(batch.inputs)>FreeKitchenRules.CapacityFor(method,vessel))return Reject("这套锅具做不了这份搭配。");
            batch.method=methodId;batch.vesselId=vessel;batch.baseId=bottom;batch.toolId=tool;batch.capacity=FreeKitchenRules.CapacityFor(method,vessel);
            lane.recipeIndex=FreeKitchenRules.VisualRecipeIndex(methodId);Message="准备"+method.label+"，食材已留在锅里。";Revision++;return true;
        }
        public bool StartFreeCooking(int laneIndex)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            var lane=Lanes[laneIndex];var batch=lane.freeBatch;
            if(batch==null||batch.product!=null||lane.step!=KitchenStep.Prep||batch.inputs.Length==0)return Reject("先把食材放进锅里。");
            var options=FreeOptions(batch.inputs,laneIndex);string methodId=batch.method;
            if(methodId==""){if(options.Count!=1)return Reject("选一种做法，再开始。" );methodId=options[0].id;}
            var method=FreeKitchenRules.FindMethod(methodId);
            if(method==null||!TryFreeHardware(method,laneIndex,out string vessel,out string bottom,out string tool,FreeKitchenRules.InputLoad(batch.inputs)))return Reject("这套锅具暂时用不了。");
            foreach(var input in batch.inputs)if(!FreeKitchenRules.MethodAcceptsMaterial(method,input.itemId))return Reject("这些食材不适合这种做法。");
            int portions=FreeKitchenRules.DerivePortions(batch.inputs),capacity=FreeKitchenRules.CapacityFor(method,vessel);
            if(portions<1||FreeKitchenRules.InputLoad(batch.inputs)>capacity)return Reject("这口锅装不下这批食材。");
            string energy=method.cold?"none":FreeKitchenRules.BaseEnergy(bottom);
            if(!EnergyAvailable(energy))return Reject("燃料不够了，先补充能源。");
            // All failure checks precede any mutation or energy charge.
            batch.method=methodId;batch.vesselId=vessel;batch.baseId=bottom;batch.toolId=tool;batch.capacity=capacity;
            int matched=FreeKitchenRules.MatchRecipe(batch.inputs,methodId,portions);
            lane.recipeIndex=matched>=0?matched:FreeKitchenRules.VisualRecipeIndex(methodId);
            lane.energy=energy;lane.seasoned=method.cold;lane.toolClicks=0;
            if(energy=="gas")Gas--;else if(energy=="cartridge")Cartridge--;else if(energy=="coal")Coal--;else if(energy=="electricity")Electricity--;
            lane.step=method.cold?KitchenStep.Utensil:KitchenStep.Heating;
            lane.remaining=lane.duration=method.cold?0:FreeKitchenRules.HeatSeconds(batch.inputs);
            AddCount(freeBatchesStarted,lane.recipeIndex,1);AddCount(freePortionsStarted,lane.recipeIndex,portions);lane.batchRemaining=portions;
            Message=method.cold?"食材备好了，开始调制。":"开火了，留意出锅时机。";Revision++;return true;
        }

        // ── 出锅/收尾：真实 Action 接触帧创建独立成品 ──────────────────
        bool FinishFreeUtensil(KitchenLane lane)
        {
            var batch=lane.freeBatch;
            int portions=Math.Max(1,lane.batchRemaining);
            int matched=FreeKitchenRules.MatchRecipe(batch.inputs,batch.method,portions);
            var product=new FoodProduct{
                id=batch.id,method=batch.method,family=FreeKitchenRules.FindMethod(batch.method).family,
                matchedRecipeIndex=matched,visualRecipeIndex=matched>=0?matched:FreeKitchenRules.VisualRecipeIndex(batch.method),
                portions=portions,remaining=portions,sourcePortions=portions,
                // V04 自动炒锅的既有红利：出锅品质更好，且加热完成自动出锅（见 Tick）。
                quality=batch.vesselId=="V04"?1.1f:1f,
                inputs=CloneInputs(batch.inputs),packages=FreeKitchenRules.PackagesFor(batch.method),
                priceCents=FreeKitchenRules.FairPriceCents(batch.inputs,portions,matched)};
            product.name=matched>=0?Recipes[matched].name:FreeKitchenRules.ComposeName(batch.inputs,batch.method);
            batch.product=product;
            bool cold=FreeKitchenRules.IsColdMethod(batch.method);
            lane.seasoned=cold||batch.method==FreeKitchenRules.MethodReheat;
            lane.step=lane.seasoned?KitchenStep.Pack:KitchenStep.Season;lane.remaining=0;
            Message=product.name+"出锅（"+portions+" 份）· "+(cold?"可装台出餐。":"调味后可装台出餐。");
            Revision++;return true;
        }

        static FoodInput[] CloneInputs(FoodInput[] inputs)
        {
            var clone=new FoodInput[inputs==null?0:inputs.Length];
            for(int i=0;i<clone.Length;i++)clone[i]=inputs[i]==null?null:inputs[i].Copy();
            return clone;
        }

        // 自由锅位的 ReadyStock 盛出：与旧 ScoopBatch 同语义。
        bool ScoopFreeBatch(KitchenLane lane)
        {
            var product=lane.freeBatch==null?null:lane.freeBatch.product;
            if(product==null||lane.batchRemaining<=0)return Reject("锅里还没有可盛出的熟食。");
            bool cold=FreeKitchenRules.IsColdMethod(lane.freeBatch.method);
            lane.step=lane.seasoned||cold?KitchenStep.Pack:KitchenStep.Season;lane.remaining=0;
            Message=lane.seasoned||cold?"盛出已熟余份，可装台出餐。":"已盛出熟食，完成调味后封装。";
            Revision++;return true;
        }

        // ── 自由锅位状态机（Action 分发）───────────────────────────────
        bool ActionFreeLane(KitchenLane lane)
        {
            if(lane.step==KitchenStep.Utensil)return FinishFreeUtensil(lane);
            if(lane.step==KitchenStep.Season)
            {
                if(lane.remaining>0)return false;
                lane.seasoned=true;lane.step=KitchenStep.Pack;
                bool cold=FreeKitchenRules.IsColdMethod(lane.freeBatch.method);
                Message=cold||SeasoningAvailable?"调味完成，选择适配包材装台。":"油盐不足，按原样封装；本份成交将扣诚信。";
                Revision++;return true;
            }
            if(lane.step==KitchenStep.ReadyStock)return ScoopFreeBatch(lane);
            if(lane.step==KitchenStep.Dirty){ClearFreeLane(lane);Message="锅位已清理。";Revision++;return true;}
            return false;
        }

        // ── 展示/文本事实 ──────────────────────────────────────────────
        public string LaneDishName(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return "";
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null)
            {
                if(lane.freeBatch.product!=null)return lane.freeBatch.product.name;
                return FreeKitchenRules.ComposeName(lane.freeBatch.inputs,lane.freeBatch.method);
            }
            return recipeName(lane.recipeIndex);
        }
        public string LaneContents(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return "";
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch==null||lane.freeBatch.inputs==null||lane.freeBatch.inputs.Length==0)return "";
            var parts=new List<string>();
            foreach(var input in lane.freeBatch.inputs)if(input!=null&&input.units>0)parts.Add(Name(input.itemId)+"×"+input.units);
            return string.Join("+",parts);
        }
        public string[] LanePackages(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return Array.Empty<string>();
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null)
            {
                if(lane.freeBatch.product!=null)return (string[])lane.freeBatch.product.packages.Clone();
                return FreeKitchenRules.PackagesFor(lane.freeBatch.method);
            }
            return (string[])Recipes[lane.recipeIndex].packages.Clone();
        }
        public FoodProduct ProductAtLane(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return null;
            KitchenLane lane=Lanes[laneIndex];
            return lane.freeBatch==null?null:lane.freeBatch.product;
        }

        // ── 柜台装餐：先真实装台，无顾客、无收款 ────────────────────────
        CounterMeal FindMeal(string mealId)
        {
            if(string.IsNullOrEmpty(mealId))return null;
            foreach(var meal in ReadyMeals)if(meal.id==mealId)return meal;
            return null;
        }

        public bool CanPackToCounter(int laneIndex,string packageId,out string reason)
        {
            reason="";
            if(Finished){reason="今日已收摊。";return false;}
            if(ReadyMeals.Count>=CounterCapacity){reason="出餐台已满，先送出一份。";return false;}
            if(laneIndex<0||laneIndex>=Lanes.Length){reason="没有这个锅位。";return false;}
            KitchenLane lane=Lanes[laneIndex];
            var product=lane.freeBatch==null?null:lane.freeBatch.product;
            if(product==null){reason="这口锅还没有出锅的成品。";return false;}
            if(lane.step!=KitchenStep.Pack&&lane.step!=KitchenStep.ReadyStock){reason="这份成品还没轮到装台。";return false;}
            if(lane.batchRemaining<=0||product.remaining<=0){reason="这份成品已经装完了。";return false;}
            if(packageId==EmergencyBagId)
            {if(!CanUseEmergencyBag(product)){reason="还有合用包材，先使用现有包材。";return false;}}
            else
            {if(Array.IndexOf(product.packages,packageId)<0){reason="这种包材不适合这份成品。";return false;}
             if(Count(packageId)<=0){reason="包材已经用完。";return false;}}
            string pending=lane.pendingFlavorId??"";
            if(pending!="")
            {
                if(!FreeKitchenRules.FlavorCompatible(product,pending)){reason=StallFlavorMenu.Find(pending).name+"不适合"+product.name+"。";return false;}
                if(Count(pending)<=0){reason=StallFlavorMenu.Find(pending).name+"已经用完 · 先补货";return false;}
            }
            return true;
        }

        // 真实装台接触事务：恰扣 1 包材 + 1 份熟食 + 实际选瓶风味 1 份；
        // 热制同时结算本份基础油盐（缺货只记事实，诚信计数在卖出时应用）。
        public bool PackToCounter(int laneIndex,string packageId)
        {
            if(!CanPackToCounter(laneIndex,packageId,out string reason))return Reject(reason);
            var meal=PackOnePortion(Lanes[laneIndex],packageId);
            ReadyMeals.Add(meal);
            if(ReadyMeals.Count==1&&AcceptingOrders)nextCustomer=Math.Min(nextCustomer,1f);
            Message=meal.product.name+"已放到取餐区（¥"+(meal.priceCents/100f).ToString("0.00")+"），点想要这份餐的顾客来取。";
            Revision++;return true;
        }

        // ── 顾客匹配：只读推荐，最老到位兼容顾客；无则 -1（餐保留）─────
        static bool OrderAcceptsProduct(StallOrder order,FoodProduct product,string packagedFlavor)
        {
            if(order==null||product==null||!order.arrived||order.served||order.departed)return false;
            // 实际包装风味与点单冲突时不兼容（选了别的瓶不能当指定风味足价卖）。
            if((packagedFlavor??"")!=(order.requestedFlavorId??""))return false;
            if(order.productId!="")return order.productId==product.id||order.productName==product.name&&order.productPriceCents==product.priceCents;
            return product.matchedRecipeIndex>=0&&product.matchedRecipeIndex==order.recipeIndex;
        }
        public int FindServingOrder(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return -1;
            KitchenLane lane=Lanes[laneIndex];
            var product=lane.freeBatch==null?null:lane.freeBatch.product;
            if(product==null)return -1;
            return FindBestServingOrder(product,lane.pendingFlavorId??"");
        }
        public int FindServingCounterOrder(string mealId)
        {
            var meal=FindMeal(mealId);
            if(meal==null||meal.product==null)return -1;
            return FindBestServingOrder(meal.product,meal.flavorId);
        }
        int FindBestServingOrder(FoodProduct product,string packagedFlavor)
        {
            StallOrder best=null;
            foreach(var order in Orders)
                if(OrderAcceptsProduct(order,product,packagedFlavor)&&(best==null||order.arrivedAt<best.arrivedAt||order.arrivedAt==best.arrivedAt&&order.id<best.id))
                    best=order;
            return best==null?-1:best.id;
        }

        // ── 柜台出餐：真实交付接触，固定目标复核，恰好一次收款 ──────────
        public bool CanServeCounterMeal(string mealId,int orderId,out string reason)
        {
            reason="";
            if(Finished){reason="今日已收摊。";return false;}
            var meal=FindMeal(mealId);
            if(meal==null||meal.product==null){reason="柜台上没有这份餐。";return false;}
            var order=FindOrder(orderId);
            if(order==null){reason="没有这位顾客。";return false;}
            if(!OrderAcceptsProduct(order,meal.product,meal.flavorId))
            {reason=order.departed||order.served?"这位顾客已经离开或已成交。":"这位顾客不要这份餐。";return false;}
            return true;
        }

        public bool ServeCounterMeal(string mealId,int orderId)
        {
            if(!CanServeCounterMeal(mealId,orderId,out string reason))return Reject(reason);
            var meal=FindMeal(mealId);var order=FindOrder(orderId);
            return SettleFixedTargetSale(meal,order,()=>ReadyMeals.Remove(meal));
        }
        // 缺油盐事实在真实卖出时一次性计入诚信（装台只记录事实，不预扣计数）。
        void ApplyShortSeasoningAtSale(CounterMeal meal)
        {
            if(!meal.shortSeasoning)return;
            UnseasonedPortionsServed+=SeasoningShortageRules.PortionsPerOrder;
            IntegrityChange+=SeasoningShortageRules.IntegrityDeltaPerShortPortion;
        }

        // ── 订单事实（收束语/收据/小票用，交付后仍稳定）────────────────
        static void RecordFreeOrderFacts(StallOrder order,FoodProduct product,string flavorId)
        {
            order.recipeIndex=product.visualRecipeIndex;order.productId=product.id;order.productName=product.name;order.productMethod=product.method;
            order.productFamily=product.family;order.productPriceCents=product.priceCents;
            order.servedFlavorId=flavorId;
        }
        public string OrderDishName(int orderId)
        {
            var order=FindOrder(orderId);
            if(order==null)return "";
            if(order.productName!="")return order.productName;
            return recipeName(order.recipeIndex);
        }
        // 稳定报价：始终 = 基础价 + 点单指定风味的合格加价（与实收无关；风险未收单
        // 报价也不变）。实收另读 order.paidCents。
        public int OrderPriceCents(int orderId)
        {
            var order=FindOrder(orderId);
            if(order==null)return 0;
            if(order.productName!="")
            {
                int price=order.productPriceCents;
                if(order.requestedFlavorId!=""&&FreeKitchenRules.FlavorCompatible(new FoodProduct{family=order.productFamily},order.requestedFlavorId))
                    price+=StallFlavorMenu.MarkupFor(order.requestedFlavorId);
                return price;
            }
            if(order.recipeIndex<0||order.recipeIndex>=Recipes.Length)return 0;
            return Recipes[order.recipeIndex].priceCents+StallFlavorMenu.MarkupFor(order.requestedFlavorId);
        }

        void ClearFreeLane(KitchenLane lane)
        {
            lane.freeBatch=null;lane.batchRemaining=0;lane.prepaidSeasoning=0;lane.seasoned=false;
            lane.step=KitchenStep.Idle;lane.remaining=lane.duration=0;lane.energy="";lane.pendingFlavorId="";lane.packageId="";
        }
    }
}
