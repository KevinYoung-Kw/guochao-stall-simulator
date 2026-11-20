using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // FULFILMENT20: Codex implementation after the delegated CLI stopped before edits.
    // The vendor owns one physical held meal. Customer pickup uses an independent,
    // transient reservation, and only an actual contact callback can commit a sale.
    public sealed partial class BusinessController
    {
        private enum FulfilmentWork { None, Pack, Place, Direct }
        private sealed class PickupReservation
        {
            public string mealId;
            public int orderId;
            public bool committed;
        }
        private readonly Dictionary<string,PickupReservation> mealPickups=new Dictionary<string,PickupReservation>();
        private readonly Dictionary<int,PickupReservation> orderPickups=new Dictionary<int,PickupReservation>();
        public string HeldMealId=>Session?.HeldMeal?.id??"";
        public bool IsPackingMeal
        {
            get
            {
                if(activeWork!=null&&activeWork.fulfilment==FulfilmentWork.Pack)return true;
                foreach(var work in pendingWork)if(work.fulfilment==FulfilmentWork.Pack)return true;
                return false;
            }
        }
        public string ServiceStatusText
        {
            get
            {
                if(Session?.HeldMeal!=null)return "手持 · "+Session.HeldMeal.product.DisplayName+" · 放上出餐台或递给顾客";
                if(IsPackingMeal)return "正在装餐 · 装好后拿在手上";
                if(mealPickups.Count>0)return mealPickups.Count+" 位顾客正在取餐 · 可以继续备菜";
                return "做好装餐 · 放台或直接递餐";
            }
        }
        private bool ServiceAvailable()
        {
            if(CanInteract)return true;
            return InteractionRejected(IsPaused?"已暂停 · 继续后操作":Session==null||!Session.Started?"先开摊再备餐":Session.Finished?"今日已收摊":"正在布置摊位 · 稍等片刻");
        }
        private bool KitchenHandsAvailable()
        {
            if(Session?.HeldMeal==null)return true;
            return InteractionRejected("手上拿着餐 · 先放上出餐台或递给顾客");
        }
        private bool HeldActionQueued()
        {
            if(activeWork!=null&&(activeWork.fulfilment==FulfilmentWork.Place||activeWork.fulfilment==FulfilmentWork.Direct))return true;
            foreach(var work in pendingWork)if(work.fulfilment==FulfilmentWork.Place||work.fulfilment==FulfilmentWork.Direct)return true;
            return false;
        }
        public bool PackMeal(int laneIndex=-1,string packageId=null)=>QueuePackMeal(laneIndex,packageId,false);
        private bool QueuePackMeal(int laneIndex,string packageId,bool placeAfter)
        {
            if(!ServiceAvailable()||!KitchenHandsAvailable())return false;
            if(IsPackingMeal)return InteractionRejected("已经安排装餐 · 等手上这一步完成");
            int lane=laneIndex;
            if(lane<0)
            {
                lane=SelectedLaneIndex;
                if(lane<0||lane>=Session.Lanes.Length||!LaneHasCounterProduct(lane))lane=FindCounterLane();
            }
            if(lane<0||lane>=Session.Lanes.Length)return InteractionRejected("先做出一份成品，再选包材装餐");
            KitchenLane source=Session.Lanes[lane];
            if(source.step!=KitchenStep.Season&&source.step!=KitchenStep.Pack&&source.step!=KitchenStep.ReadyStock)
                return InteractionRejected("这口锅还没做好 · 熟了再装餐");
            if(Session.ProductAtLane(lane)==null)return InteractionRejected("这口锅还没有能装出的成品");
            string[] packages=Session.AvailableLanePackages(lane);
            if(string.IsNullOrEmpty(packageId))foreach(string candidate in packages)if(candidate==BusinessSession.EmergencyBagId||Session.Count(candidate)>0){packageId=candidate;break;}
            if(packageId!=BusinessSession.EmergencyBagId&&(string.IsNullOrEmpty(packageId)||Session.Count(packageId)<=0))return InteractionRejected("合用的包材用完了 · 先补货");
            if(Array.IndexOf(packages,packageId)<0)return InteractionRejected("这种包材不适合这份餐");
            if(source.step!=KitchenStep.Season&&!HasPendingSeasoning(lane)&&source.seasoned
                &&!Session.CanPackToHand(lane,packageId,out string packReason))return InteractionRejected(packReason);
            if(placeAfter&&CounterFull())return InteractionRejected("出餐台已满 · 也可以选包材拿在手上递给顾客");
            int prerequisites=(!source.seasoned&&!HasPendingIntent(StallInteractionKind.Seasoning,lane)?1:0)
                +(source.step==KitchenStep.ReadyStock&&!source.seasoned&&!HasScoopIntent(lane)?1:0);
            if(pendingWork.Count+prerequisites+1>PendingWorkCapacity)return InteractionRejected("待办满了 · 先完成手头的动作");
            SelectedLaneIndex=lane;
            // Existing chosen-flavour intent stays ahead of the package. A default
            // seasoning action is added only if this cooked portion still needs it.
            if(source.step==KitchenStep.ReadyStock&&!source.seasoned&&!HasScoopIntent(lane))
                Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=lane,order=-1,step=KitchenStep.ReadyStock,label="盛出熟食"},false);
            if(!source.seasoned&&!HasPendingIntent(StallInteractionKind.Seasoning,lane))
                Enqueue(new WorkIntent{kind=StallInteractionKind.Seasoning,lane=lane,order=-1,item="seasoning",step=KitchenStep.Season,label="调味"},false);
            var work=new WorkIntent{kind=StallInteractionKind.Serving,fulfilment=FulfilmentWork.Pack,lane=lane,order=-1,item=packageId,
                step=KitchenStep.Pack,label="装餐拿起",placeAfterPack=placeAfter,batchId=source.freeBatch?.id??"",productId=Session.ProductAtLane(lane)?.id??""};
            bool queued=Enqueue(work,false);
            if(queued&&packageId==BusinessSession.EmergencyBagId)ShowInteractionHint("合用包材用完 · 使用应急薄袋，售出国潮 −5");
            return queued;
        }
        public bool PlaceHeldMeal()
        {
            if(!ServiceAvailable())return false;
            if(HeldActionQueued())return InteractionRejected("正在安排手上的餐 · 等这一步完成");
            if(!Session.CanPlaceHeldMeal(out string reason))return InteractionRejected(reason);
            return Enqueue(new WorkIntent{kind=StallInteractionKind.CounterMeal,fulfilment=FulfilmentWork.Place,lane=-1,order=-1,
                item=HeldMealId,counterMeal=HeldMealId,step=KitchenStep.Pack,label="放上出餐台"},false);
        }
        public bool DeliverHeldMealTo(int orderId)
        {
            if(!ServiceAvailable())return false;
            if(HeldActionQueued())return InteractionRejected("正在安排手上的餐 · 等这一步完成");
            if(orderPickups.ContainsKey(orderId))return InteractionRejected("这位顾客已经在取餐了");
            if(!Session.CanServeHeldMeal(orderId,out string reason))return InteractionRejected(reason);
            return Enqueue(new WorkIntent{kind=StallInteractionKind.CounterMeal,fulfilment=FulfilmentWork.Direct,lane=-1,order=orderId,
                item=HeldMealId,counterMeal=HeldMealId,step=KitchenStep.Pack,label="手持递餐"},false);
        }
        private bool HasPendingSeasoning(int lane)
        {
            if(activeWork!=null&&!activeWork.committed&&activeWork.kind==StallInteractionKind.Seasoning&&activeWork.lane==lane)return true;
            foreach(var work in pendingWork)if(work.kind==StallInteractionKind.Seasoning&&work.lane==lane)return true;
            return false;
        }
        private bool HeldWorkValid(WorkIntent work)
        {
            if(Session.HeldMeal==null||Session.HeldMeal.id!=work.counterMeal)return false;
            if(work.fulfilment==FulfilmentWork.Place)return true;
            var order=Session.FindOrder(work.order);
            return order!=null&&!order.served&&!order.departed&&!orderPickups.ContainsKey(work.order);
        }
        private void FinishFulfilmentWork(WorkIntent work)
        {
            if(work.fulfilment==FulfilmentWork.Pack&&work.committed&&work.placeAfterPack
                &&Session.HeldMeal!=null&&Session.HeldMeal.id==work.counterMeal&&!HeldActionQueued())PlaceHeldMeal();
        }
        public int PickupOrderForMeal(string mealId)
        {return !string.IsNullOrEmpty(mealId)&&mealPickups.TryGetValue(mealId,out var pickup)?pickup.orderId:-1;}
        private bool IsCurrentPickup(PickupReservation pickup)
        {return mealPickups.TryGetValue(pickup.mealId,out var meal)&&ReferenceEquals(meal,pickup)
            &&orderPickups.TryGetValue(pickup.orderId,out var order)&&ReferenceEquals(order,pickup);}
        private bool HasDirectDeliveryFor(int orderId)
        {
            if(activeWork!=null&&activeWork.fulfilment==FulfilmentWork.Direct&&activeWork.order==orderId)return true;
            foreach(var work in pendingWork)if(work.fulfilment==FulfilmentWork.Direct&&work.order==orderId)return true;
            return false;
        }
        public bool RequestCounterPickup(string mealId,int orderId)
        {
            if(!ServiceAvailable())return false;
            if(string.IsNullOrEmpty(mealId)||FindCounterMeal(mealId)==null)return InteractionRejected("先在出餐台挑一份餐");
            if(mealPickups.ContainsKey(mealId)||Stage.IsCounterMealReserved(mealId))return InteractionRejected("这份餐已经有人来取了");
            if(orderPickups.ContainsKey(orderId)||HasDirectDeliveryFor(orderId))return InteractionRejected("这位顾客已经安排好取餐了");
            if(!Session.CanServeCounterMeal(mealId,orderId,out string reason))return InteractionRejected(reason);
            var pickup=new PickupReservation{mealId=mealId,orderId=orderId};
            mealPickups.Add(mealId,pickup);orderPickups.Add(orderId,pickup);
            bool started=Stage.BeginCustomerPickup(mealId,orderId,()=>CommitCustomerPickup(pickup),
                ()=>FinishCustomerPickup(pickup,false),()=>FinishCustomerPickup(pickup,true));
            if(!started){RemovePickup(pickup);return InteractionRejected("顾客暂时走不到取餐台 · 餐还在台上");}
            if(SelectedCounterMealId==mealId)SelectedCounterMealId="";
            ShowInteractionHint("顾客来取这份餐 · 可以继续备菜");return true;
        }
        private void CommitCustomerPickup(PickupReservation pickup)
        {
            if(!IsCurrentPickup(pickup)||pickup.committed||!CanInteract)return;
            if(!Session.CanServeCounterMeal(pickup.mealId,pickup.orderId,out string reason))
            {ShowInteractionHint(reason);return;}
            pickup.committed=Session.ServeCounterMeal(pickup.mealId,pickup.orderId);
            if(pickup.committed){ShowInteractionHint(Session.Message);InteractionChanged?.Invoke();}
        }
        private void FinishCustomerPickup(PickupReservation pickup,bool failed)
        {
            if(!IsCurrentPickup(pickup))return;
            RemovePickup(pickup);
            if(!pickup.committed)ShowInteractionHint("取餐没完成 · 餐还在台上，可以另选顾客");
        }
        private void RemovePickup(PickupReservation pickup)
        {
            if(mealPickups.TryGetValue(pickup.mealId,out var meal)&&ReferenceEquals(meal,pickup))mealPickups.Remove(pickup.mealId);
            if(orderPickups.TryGetValue(pickup.orderId,out var order)&&ReferenceEquals(order,pickup))orderPickups.Remove(pickup.orderId);
            InteractionChanged?.Invoke();
        }
        public bool CancelCounterPickup(string mealId)
        {
            if(!ServiceAvailable())return false;
            if(string.IsNullOrEmpty(mealId)||!mealPickups.TryGetValue(mealId,out var pickup))return InteractionRejected("这份餐没有正在等待的取餐安排");
            if(pickup.committed)return InteractionRejected("顾客已经拿到了这份餐");
            RemovePickup(pickup); // Invalidate callbacks before asking the physical actor to return.
            Stage.CancelCustomerPickup(mealId);
            ShowInteractionHint("已取消取餐 · 餐留在出餐台");return true;
        }
        private void ClearCustomerPickups()
        {
            mealPickups.Clear();orderPickups.Clear();
            Stage?.CancelAllCustomerPickups();
            InteractionChanged?.Invoke();
        }
        private void TickCustomerPickups()
        {
            if(mealPickups.Count==0)return;
            var stale=new List<PickupReservation>();
            foreach(var pickup in mealPickups.Values)
            {
                if(pickup.committed)continue; // Allow the real receive gesture to finish.
                var order=Session.FindOrder(pickup.orderId);
                if(order==null||order.served||order.departed||FindCounterMeal(pickup.mealId)==null)stale.Add(pickup);
            }
            foreach(var pickup in stale)
            {RemovePickup(pickup);Stage.CancelCustomerPickup(pickup.mealId);ShowInteractionHint("取餐已取消 · 台上的餐可以留给下一位顾客");}
        }
        public void ShowCustomerGuide(int orderId)
        {
            if(!ServiceAvailable())return;
            var world=HUD!=null?HUD.GetComponentInChildren<BusinessWorldHUD>(true):null;
            if(world!=null)world.ShowOrderGuide(orderId);
            else ShowInteractionHint(Session.OrderDishName(orderId)+" · 按手头食材备餐，做好后再递给顾客");
        }
    }
}
