using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // FULFILMENT20：手持餐核心。真实打包的餐可以独立拿在手上——放上台
    // （六位出餐台之一）或直接递给固定顾客。本文件只含手持入口与共享
    // 私有事务；旧 CanPackToCounter/PackToCounter/CanServeCounterMeal/
    // ServeCounterMeal 公开签名与语义保持不变（实现在 FreeKitchenSession.cs，
    // 内部改走本文件的共享 helpers）。手持不是第 7 个台位：一只手持有一份，
    // 放台仍受 CounterCapacity=6 约束。直递绝不把 Held 塞入 ReadyMeals 假售。
    public sealed partial class BusinessSession
    {
        // 手上那一份真实打包餐（null=空手）。独立保存、独立守恒，绝不与台位共享引用。
        public CounterMeal HeldMeal { get; private set; }

        // ── 共享：单份包装扣减（PackToCounter / PackToHand 唯一入口）──────
        // 恰扣 1 包材 + 1 份熟食 + 实际选瓶风味 1 份；热制同份结算本份基础油盐
        // （缺货只记 shortSeasoning 事实，诚信计数在真实卖出时一次性应用）。
        // 调用前必须通过 CanPackToCounter/CanPackToHand 校验；返回独立一份物理
        // 餐快照，不写入任何容器（台/手由调用方决定）。
        CounterMeal PackOnePortion(KitchenLane lane,string packageId)
        {
            var product=lane.freeBatch.product;
            bool cold=FreeKitchenRules.IsColdMethod(product.method);
            string flavorId=lane.pendingFlavorId??"";lane.pendingFlavorId="";
            bool shortSeasoning=false;
            if(!cold&&product.method!=FreeKitchenRules.MethodReheat)
            {
                if(lane.prepaidSeasoning>0)lane.prepaidSeasoning--;
                else if(Count(SeasoningShortageRules.BaseSeasoningId)>0)Inventory[SeasoningShortageRules.BaseSeasoningId]-=SeasoningShortageRules.PortionsPerOrder;
                else shortSeasoning=true;
            }
            if(flavorId!="")Inventory[flavorId]--;
            if(packageId!=EmergencyBagId)Inventory[packageId]--;
            product.remaining--;lane.batchRemaining--;
            var snapshot=product.Copy();
            snapshot.portions=snapshot.remaining=1; // 一份装的独立物理快照
            var meal=new CounterMeal{id=Guid.NewGuid().ToString("N"),packageId=packageId,flavorId=flavorId,
                product=snapshot,shortSeasoning=shortSeasoning,sourceLane=lane.index,
                priceCents=product.priceCents+(flavorId!=""?StallFlavorMenu.MarkupFor(flavorId):0)};
            if(lane.batchRemaining<=0||product.remaining<=0)ClearFreeLane(lane); // 空锅释放；有余份留在锅里继续装
            return meal;
        }

        // ── 打包到手持 ───────────────────────────────────────────────────
        public bool CanPackToHand(int laneIndex,string packageId,out string reason)
        {
            reason="";
            if(Finished){reason="今日已收摊。";return false;}
            if(HeldMeal!=null){reason="手上已经拿着一份餐，先放上台或递给顾客。";return false;}
            if(laneIndex<0||laneIndex>=Lanes.Length){reason="没有这个锅位。";return false;}
            KitchenLane lane=Lanes[laneIndex];
            var product=lane.freeBatch==null?null:lane.freeBatch.product;
            if(product==null){reason="这口锅还没有出锅的成品。";return false;}
            if(lane.step!=KitchenStep.Pack&&lane.step!=KitchenStep.ReadyStock){reason="这份成品还没轮到装餐。";return false;}
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

        // 真实打包接触帧：同 PackToCounter 的单份扣减事务，成品拿在手上。
        public bool PackToHand(int laneIndex,string packageId)
        {
            if(!CanPackToHand(laneIndex,packageId,out string reason))return Reject(reason);
            var meal=PackOnePortion(Lanes[laneIndex],packageId);
            HeldMeal=meal;
            if(AcceptingOrders)nextCustomer=Mathf.Min(nextCustomer,1f);
            Message=(meal.packageId==EmergencyBagId?"合用包材已用完，使用应急薄袋（售出国潮 −5）。":"")+meal.product.name+"已打包拿在手上（¥"+(meal.priceCents/100f).ToString("0.00")+"），可放上台或直接递给顾客。";
            Revision++;return true;
        }

        // ── 手持放台：同 id/同快照原样移动，不重复扣减、不成交 ──────────
        public bool CanPlaceHeldMeal(out string reason)
        {
            reason="";
            if(Finished){reason="今日已收摊。";return false;}
            if(HeldMeal==null||HeldMeal.product==null){reason="手上没有打包好的餐。";return false;}
            if(ReadyMeals.Count>=CounterCapacity){reason="出餐台已满，先送出一份。";return false;}
            return true;
        }

        public bool PlaceHeldMealOnCounter()
        {
            if(!CanPlaceHeldMeal(out string reason))return Reject(reason);
            ReadyMeals.Add(HeldMeal); // 同一份：id/快照/报价原样，无第二次扣减
            HeldMeal=null;
            if(ReadyMeals.Count==1&&AcceptingOrders)nextCustomer=Mathf.Min(nextCustomer,1f);
            Message="餐已放到取餐区，点想要这份餐的顾客来取。";
            Revision++;return true;
        }

        // ── 手持直递：固定目标复核 + 共同结算，恰好一次收款 ─────────────
        public bool CanServeHeldMeal(int orderId,out string reason)
        {
            reason="";
            if(Finished){reason="今日已收摊。";return false;}
            if(HeldMeal==null||HeldMeal.product==null){reason="手上没有打包好的餐。";return false;}
            var order=FindOrder(orderId);
            if(order==null){reason="没有这位顾客。";return false;}
            if(!OrderAcceptsProduct(order,HeldMeal.product,HeldMeal.flavorId))
            {reason=order.departed||order.served?"这位顾客已经离开或已成交。":"这位顾客不要这份餐。";return false;}
            return true;
        }

        public bool ServeHeldMeal(int orderId)
        {
            if(!CanServeHeldMeal(orderId,out string reason))return Reject(reason);
            var meal=HeldMeal;var order=FindOrder(orderId);
            return SettleFixedTargetSale(meal,order,()=>HeldMeal=null);
        }

        // 只读推荐：手上这份餐最老到位兼容顾客；无则 -1（餐留在手上）。
        public int FindServingHeldOrder()
        {
            var meal=HeldMeal;
            if(meal==null||meal.product==null)return -1;
            return FindBestServingOrder(meal.product,meal.flavorId);
        }

        // ── 共享：固定目标结算（ServeCounterMeal / ServeHeldMeal 唯一入口）──
        // 复核已通过（CanServe*）；风险闸门决策 → 采纳/正常两个分支与旧
        // ServeCounterMeal 逐字同语义：出餐瞬间由 removeFromHolding 把这一份
        // 物理餐从台/手移除（要么完整成交/采纳，要么原样保留——前面的拒绝分支
        // 已挡掉，这里不再失败）。包材/油盐/份数在打包时各扣过一次，此处绝不重扣；
        // 现金/营收/风险债权恰好记一次。
        bool SettleFixedTargetSale(CounterMeal meal,StallOrder order,Action removeFromHolding)
        {
            var product=meal.product;
            // 风味只有与点单一致才按足价成交（装台/打包时已扣过瓶）。
            string flavorId=meal.flavorId;
            int priceCents=meal.priceCents;
            bool flavorMissed=flavorId==""&&order.requestedFlavorId!="";
            StallRiskGateDecision gate=InvokeServeRiskGate(order.id,product.visualRecipeIndex,priceCents);
            if(gate!=null&&gate.adoptAsRisk)
            {
                StallRiskEvent risk=RiskState.Adopt(StallRiskRules.EventA01,order.id,meal.sourceLane,product.id,priceCents,gate.interceptSeconds,gate.collectCents);
                order.emergencyPackage=meal.packageId==EmergencyBagId;
                removeFromHolding();ReleaseOrderReservation(order.id);
                Served++;
                order.served=true;order.finishedAt=Elapsed;if(order.lane>=0)order.lane=-1;
                RecordFreeOrderFacts(order,product,flavorId);
                order.paidCents=0;order.riskSerial=risk.Serial;order.shortSeasoning=meal.shortSeasoning;RiskEventsIssued++;
                ApplyShortSeasoningAtSale(meal);
                Message=product.name+"已出餐，对方扫码未到账——在 "+gate.interceptSeconds.ToString("0.0")+" 秒内点击对方追回！";
                Revision++;PublishRiskSalePending(risk);return true;
            }
            order.emergencyPackage=meal.packageId==EmergencyBagId;
            removeFromHolding();ReleaseOrderReservation(order.id);
            CashCents+=priceCents;RevenueCents+=priceCents;Served++;
            order.served=true;order.finishedAt=Elapsed;if(order.lane>=0)order.lane=-1;
            RecordFreeOrderFacts(order,product,flavorId);
            order.paidCents=priceCents;order.riskSerial=0;order.shortSeasoning=meal.shortSeasoning;
            ApplyShortSeasoningAtSale(meal);
            if(flavorMissed)Message=product.name+"已出餐：未加"+Name(order.requestedFlavorId)+"，按基础价成交。";
            else Message=product.name+(flavorId!=""?"加"+StallFlavorMenu.Find(flavorId).name+"，收款":"已出餐，收款")+" ¥"+(priceCents/100f).ToString("0.00");
            Revision++;PublishSale(order.id,product.visualRecipeIndex,priceCents,meal.shortSeasoning);return true;
        }

        // Both physical holding locations supply natural customers. Packing has
        // already paid for packaging, so these meals need no spare wrapper.
        IEnumerable<CounterMeal> PackedMeals()
        {
            foreach(var meal in ReadyMeals)yield return meal;
            if(HeldMeal!=null)yield return HeldMeal;
        }

        // ── 存档：JsonUtility null → 空对象，presence 判别归一 ──────────
        static void NormalizeHeldMealSave(BusinessSaveData data)
        {
            if(data==null)return;
            var held=data.heldMeal;
            if(held==null){if(data.heldMealPresent)throw new ArgumentException("Missing marked held meal");return;}
            var product=held.product;
            if(!data.heldMealPresent&&string.IsNullOrEmpty(held.id)&&string.IsNullOrEmpty(held.packageId)&&held.priceCents==0&&(held.sourceLane==0||held.sourceLane==-1)
                &&string.IsNullOrEmpty(held.flavorId??"")&&!held.shortSeasoning
                &&(product==null||string.IsNullOrEmpty(product.id)&&string.IsNullOrEmpty(product.name)&&string.IsNullOrEmpty(product.method)
                    &&string.IsNullOrEmpty(product.family)&&(product.inputs==null||product.inputs.Length==0)
                    &&(product.packages==null||product.packages.Length==0)&&product.portions==0&&product.remaining==0
                    &&product.priceCents==0&&product.quality==0&&(product.matchedRecipeIndex==0||product.matchedRecipeIndex==-1)&&product.visualRecipeIndex==0&&(product.sourcePortions==0||product.sourcePortions==1)))
                data.heldMeal=null; // 旧档/空 JSON 的 inline 空对象 = 空手，语义不变
        }
    }
}
