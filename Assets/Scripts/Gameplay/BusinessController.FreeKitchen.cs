using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Free kitchen focus and counter selection. Recipes/orders do not gate cooking.
    // Packing goes to hand; a selected counter meal is collected by its chosen customer.
    public sealed partial class BusinessController
    {
        // ── 出餐台成品焦点（协作固定接口）──────────────────────────────
        public string SelectedCounterMealId { get; private set; }="";
        private int selectionWorkSerial;
        public bool ServiceUsesCounterMeal=>SelectedCounterMealId!=""&&FindCounterMeal(SelectedCounterMealId)!=null;
        public bool HasServiceMeal=>ServiceUsesCounterMeal||Session?.HeldMeal!=null;
        // One priority for mouse, order cards and keyboard: explicit counter
        // selection, then the held meal. A mismatch never silently swaps food.
        public bool CanServeFocusedMealTo(int orderId,out string reason)
        {
            if(ServiceUsesCounterMeal)return Session.CanServeCounterMeal(SelectedCounterMealId,orderId,out reason);
            if(Session?.HeldMeal!=null)return Session.CanServeHeldMeal(orderId,out reason);
            reason="先挑一份成品";return false;
        }
        public bool ClickCustomerForService(int orderId)
        {
            if(!ServiceAvailable())return false;
            TickCounterSelection();
            if(TryRiskIntercept(orderId))return true;
            if(ServiceUsesCounterMeal)return RequestCounterPickup(SelectedCounterMealId,orderId);
            if(Session.HeldMeal!=null)return DeliverHeldMealTo(orderId);
            ShowCustomerGuide(orderId);return true;
        }
        // 只选真实 ReadyMeals 中的餐；传空串=清除选餐（UI 取消选餐依赖此签名）。
        public bool SelectCounterMeal(string mealId)
        {
            if(!CanInteract)return false;
            if(string.IsNullOrEmpty(mealId)){SelectedCounterMealId="";InteractionChanged?.Invoke();return true;}
            // Pointing at a ready meal does not occupy the vendor's hands:
            // the customer collects it while another packed meal may be held.
            if(PickupOrderForMeal(mealId)>=0)return InteractionRejected("这份餐已经有人来取了");
            if(FindCounterMeal(mealId)==null)return InteractionRejected("出餐台上没有这份餐");
            SelectedCounterMealId=mealId;selectionWorkSerial=workSerial;InteractionChanged?.Invoke();
            ShowInteractionHint("挑好了 · 点想要它的顾客来取餐");return true;
        }
        // UI 点顾客入口：必须已选餐且 CanServeCounterMeal 合适才入队；入队即确定
        // 餐与客人，之后换焦点不影响已排任务；接触帧 ServeCounterMeal 再复核，
        // 客人走了不改送别人，也不在本入口替玩家挑别的餐。
        public bool DeliverCounterMealTo(int orderId)=>RequestCounterPickup(SelectedCounterMealId,orderId);
        // The counter button explicitly places food; packaging by itself leaves it in hand.
        public bool UseServingCounter(int laneIndex=-1)
        {
            if(!ServiceAvailable())return false;
            return Session.HeldMeal!=null?PlaceHeldMeal():QueuePackMeal(laneIndex,null,true);
        }
        // 开火做法入口（协作固定接口）：委托 Session.SetFreeMethod，成功重建锅内造型。
        public bool SetFreeCookingMethod(int laneIndex,string method)
        {
            if(!CanInteract||!KitchenHandsAvailable())return false;
            if(laneIndex<0||laneIndex>=Session.Lanes.Length)return InteractionRejected("先选中一口锅");
            if(!Session.SetFreeMethod(laneIndex,method))return InteractionRejected(Session.Message!=""?Session.Message:"这台设备不支持这种做法");
            Stage?.RebuildLaneFood(laneIndex,Session);
            ShowInteractionHint("已换做法");return true;
        }
        // ── 自由逐项投料 ──────────────────────────────────────────────
        // 通用入口：先看选中锅位是否收这种料，再 FindFreeLane（先兼容已有备料锅，
        // 后空锅）。失败给出明确原因；绝不回落旧 BeginBatch 整批扣料。
        private bool TryFreeIngredient(string itemId,int lane,ref string reason)
        {
            if(Session==null||!Session.Started)return false;
            if(lane>=0)return TryFreeIngredientAtLane(lane,itemId,ref reason);
            int preferred=SelectedLaneIndex;
            string preferredReason=null;
            if(preferred>=0&&preferred<Session.Lanes.Length)
            {
                if(HasPendingFreeInput(preferred,itemId)){reason="这份料已经在拿来的路上";return false;}
                if(CanQueueFreeIngredient(preferred,itemId,out preferredReason,out bool hasPendingOnPreferred))
                    return EnqueueFreeInput(preferred,itemId,ref reason);
                reason=preferredReason;
                if(hasPendingOnPreferred)return false;
            }
            int reserved=CountReservedFreeInputUnits(itemId);
            int free=Session.FindFreeLane(itemId,CollectPendingFreeInputs,reserved);
            if(free>=0)
            {
                if(HasPendingFreeInput(free,itemId)){reason="这份料已经在拿来的路上";return false;}
                if(CanQueueFreeIngredient(free,itemId,out string freeReason,out _))
                    return EnqueueFreeInput(free,itemId,ref reason,free!=preferred?preferredReason:null);
                reason=freeReason;return false;
            }
            if(string.IsNullOrEmpty(reason))reason="没有能下这种料的锅 · 先点一口空锅";
            return false;
        }
        private bool TryFreeIngredientAtLane(int lane,string itemId,ref string reason)
        {
            // 同锅不同料可排队；只有同一料已有未下锅的安排在先才拒绝。
            if(HasPendingFreeInput(lane,itemId)){reason="这份料已经在拿来的路上";return false;}
            if(!CanQueueFreeIngredient(lane,itemId,out reason,out _))return false;
            return EnqueueFreeInput(lane,itemId,ref reason);
        }
        private bool CanQueueFreeIngredient(int lane,string itemId,out string reason,out bool hasPendingOnLane)
        {
            var pending=CollectPendingFreeInputs(lane);
            hasPendingOnLane=pending.Count>0;
            int reserved=CountReservedFreeInputUnits(itemId);
            if(Session.CanAddFreeIngredient(lane,itemId,pending,reserved,out reason))return true;
            if(!string.IsNullOrEmpty(reason)&&!string.IsNullOrEmpty(itemId))
            {
                string internalName=BusinessSession.Name(itemId);
                string playerName=BusinessWorldHUD.ShortItem(itemId);
                if(!string.IsNullOrEmpty(internalName)&&!string.IsNullOrEmpty(playerName)&&internalName!=playerName)
                    reason=reason.Replace(internalName,playerName);
            }
            return false;
        }
        // 只读预测：本锅尚未 committed 的 activeWork + pendingWork 中的自由投料，按实际先后顺序。
        private IReadOnlyList<string> CollectPendingFreeInputs(int lane)
        {
            var inputs=new List<string>();
            if(activeWork!=null&&activeWork.freeInput&&!activeWork.committed&&activeWork.lane==lane&&!string.IsNullOrEmpty(activeWork.item))
                inputs.Add(activeWork.item);
            foreach(WorkIntent work in pendingWork)
                if(work.freeInput&&!work.committed&&work.lane==lane&&!string.IsNullOrEmpty(work.item))
                    inputs.Add(work.item);
            return inputs;
        }
        // 统计所有锅位尚未 committed 的同 SKU 投料意向，防止多锅预订超库存（已 committed 不二次占用）。
        private int CountReservedFreeInputUnits(string itemId)
        {
            if(string.IsNullOrEmpty(itemId))return 0;
            int count=0;
            if(activeWork!=null&&activeWork.freeInput&&!activeWork.committed&&activeWork.item==itemId)count++;
            foreach(WorkIntent work in pendingWork)
                if(work.freeInput&&!work.committed&&work.item==itemId)count++;
            return count;
        }
        // 仅当同锅有尚未接触/未 committed 且锅仍在 Prep 的开火动作时，才允许安全中断并重排。
        private bool CanDeferActiveFreeStart(int lane)
        {
            if(activeWork==null||!activeWork.freeStart||activeWork.committed||activeWork.lane!=lane)return false;
            if(Session==null||lane<0||lane>=Session.Lanes.Length)return false;
            if(Session.Lanes[lane].step!=KitchenStep.Prep||Session.Lanes[lane].freeBatch==null)return false;
            if(Stage!=null&&Stage.WorkAudit!=null&&Stage.WorkAudit.contactCommitted)return false;
            return true;
        }
        private static string FormatFreeInputHint(int lane,string itemId,string fallbackReason)
        {
            string action="拿"+BusinessWorldHUD.ShortItem(itemId)+" → "+(lane+1)+"号锅";
            if(string.IsNullOrEmpty(fallbackReason))return action;
            string detail=fallbackReason.EndsWith("。",StringComparison.Ordinal)?fallbackReason:fallbackReason+"。";
            return detail+"已安排"+action;
        }
        private bool EnqueueFreeInput(int lane,string itemId,ref string reason,string fallbackReason=null)
        {
            if(!KitchenHandsAvailable())return false;
            bool deferActiveStart=CanDeferActiveFreeStart(lane);
            int requiredSlots=deferActiveStart?2:1;
            if(pendingWork.Count+requiredSlots>PendingWorkCapacity)
            {
                reason="待办满了 · 先等手头的事做完";
                return InteractionRejected(reason);
            }
            WorkIntent deferredStart=null;
            if(deferActiveStart)
            {
                deferredStart=activeWork;activeWork=null;
                Stage?.CancelWork();
                deferredStart.startedAt=-1;deferredStart.contactAt=-1;deferredStart.pathStart=0;
            }
            SelectedLaneIndex=lane;
            var work=new WorkIntent{kind=StallInteractionKind.Ingredient,lane=lane,order=-1,item=itemId,step=KitchenStep.Idle,label="投料",freeInput=true};
            work.id=++workSerial;work.queuedAt=Session.Elapsed;pendingWork.Add(work);
            if(deferredStart!=null)pendingWork.Add(deferredStart);
            ShowInteractionHint(FormatFreeInputHint(lane,itemId,fallbackReason));
            InteractionChanged?.Invoke();
            if(!pumping)PumpWork();return true;
        }
        // 同一锅是否已有这份料的未下锅安排（已下锅的照常继续，不挡新料）。
        private bool HasPendingFreeInput(int lane,string itemId)
        {
            if(activeWork!=null&&activeWork.freeInput&&!activeWork.committed&&activeWork.lane==lane&&activeWork.item==itemId)return true;
            foreach(WorkIntent work in pendingWork)if(work.freeInput&&work.lane==lane&&work.item==itemId)return true;
            return false;
        }
        // 同一锅是否还有任何没下锅的料（开火/装盒要等它们都下锅）。
        private bool HasPendingFreeInputs(int lane)
        {
            if(activeWork!=null&&activeWork.freeInput&&!activeWork.committed&&activeWork.lane==lane)return true;
            foreach(WorkIntent work in pendingWork)if(work.freeInput&&work.lane==lane)return true;
            return false;
        }
        private bool HasPendingFreeStart(int lane)
        {if(activeWork!=null&&activeWork.freeStart&&activeWork.lane==lane)return true;foreach(WorkIntent work in pendingWork)if(work.freeStart&&work.lane==lane)return true;return false;}
        // ── 自由锅具点击：放料 / 开火 / 火候 / 上台 ───────────────────
        private bool UseCookwareFree(int laneIndex)
        {
            KitchenLane lane=Session.Lanes[laneIndex];SelectedLaneIndex=laneIndex;
            switch(lane.step)
            {
                case KitchenStep.Dirty:
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.Dirty,label="清理锅位"},false);
                case KitchenStep.ReadyStock:
                case KitchenStep.Pack:
                    return InteractionRejected("做好了 · 点出餐口装盒上台");
                case KitchenStep.Season:
                    return InteractionRejected(lane.remaining>0?"正在盛装 / 沥油":"点调味瓶安排调味");
                case KitchenStep.Heating:
                case KitchenStep.Utensil:
                    if(!Session.RequiresManualHeatAction(laneIndex))return InteractionRejected("这口锅自动恒温完成，无需手动火候");
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.Utensil,label="掌握火候"},false);
                default:
                    // Idle（含首料正在搬运）与 Prep：可排开火。真正执行等所有先
                    // 排好的料都下锅、锅进入 Prep 之后；能源只花一次。
                    if(lane.freeBatch==null&&!HasPendingFreeInputs(laneIndex))return InteractionRejected("空锅 · 先放食材，再选做法");
                    if(HasPendingFreeStart(laneIndex))return InteractionRejected("已经在安排开火");
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.Prep,label="开火烹制",freeStart=true},false);
            }
        }
        // ── 出餐台成品查询与焦点维护 ─────────────────────────────────
        private CounterMeal FindCounterMeal(string mealId)
        {
            if(Session==null||string.IsNullOrEmpty(mealId)||Session.ReadyMeals==null)return null;
            foreach(CounterMeal meal in Session.ReadyMeals)if(meal!=null&&meal.id==mealId)return meal;
            return null;
        }
        private bool CounterFull()=>Session!=null&&Session.ReadyMeals!=null&&Session.ReadyMeals.Count>=BusinessSession.CounterCapacity;
        private bool LaneHasCounterProduct(int lane)
        {
            if(lane<0||lane>=Session.Lanes.Length)return false;
            if(Session.ProductAtLane(lane)!=null)return true;
            KitchenLane target=Session.Lanes[lane];
            return target.step==KitchenStep.ReadyStock||target.step==KitchenStep.Pack||target.freeBatch!=null&&target.freeBatch.product!=null;
        }
        private int FindCounterLane()
        {
            for(int i=0;i<Session.Lanes.Length;i++)if(LaneHasCounterProduct(i))return i;
            return -1;
        }
        // 选中的餐被送走或撤下后清焦点（由核心从 ReadyMeals 移除）。
        private void TickCounterSelection()
        {
            if(SelectedCounterMealId=="")return;
            // A new packing intent starts a new hand-serving context. Clear
            // an older selection, but preserve an explicit selection made
            // after that intent (including while holding the resulting meal).
            bool newerPack=activeWork!=null&&activeWork.fulfilment==FulfilmentWork.Pack&&activeWork.id>selectionWorkSerial;
            foreach(var work in pendingWork)
                newerPack|=work.fulfilment==FulfilmentWork.Pack&&work.id>selectionWorkSerial;
            if(!newerPack&&FindCounterMeal(SelectedCounterMealId)!=null)return;
            SelectedCounterMealId="";InteractionChanged?.Invoke();
        }
    }
}
