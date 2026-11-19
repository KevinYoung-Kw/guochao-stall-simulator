using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // v21 Codex targeted service fix on the reviewed v20 Claude-derived scaffold.
    // Short service animations do not gate settlement on pathfinding or precise IK.
    public sealed partial class StreetStallStage
    {
        private Transform heldProp,heldGrip;
        private string heldMealId;
        private bool heldMealInHand;
        private readonly List<ServiceVisit> serviceVisits=new List<ServiceVisit>();
        public bool HasHeldMealInHand=>heldMealInHand&&heldProp!=null;
        public string HeldMealVisualId=>HasHeldMealInHand?heldMealId:"";
        public float LastCustomerPickupContactError {get;private set;}=float.PositiveInfinity;
        public string LastPickupRejection {get;private set;}="";
        public float LastDirectHandoffContactError {get;private set;}=float.PositiveInfinity;
        private sealed class ServiceVisit
        {
            public Guest guest;public CounterMealSlot slot;public Transform visual,hand,startParent;
            public string mealId;public int orderId;public bool direct,received,closed,committing,cancelRequested;
            public Vector3 startPosition;public Quaternion startRotation;public float elapsed;
            public Action contact,finished,failed;
        }
        private void ClearHeldProp()
        {heldMealInHand=false;heldMealId=null;if(heldProp)Destroy(heldProp.gameObject);heldProp=null;heldGrip=null;}
        private bool BuildHeldProp(CounterMeal meal)
        {
            if(meal==null||workActor==null)return false;ClearHeldProp();
            heldProp=Group("Held meal "+meal.id,cooking);heldProp.SetPositionAndRotation(workActor.RightPalmPosition-PackageGripOffset(meal.packageId),Quaternion.identity);
            BuildPackageContents(heldProp,meal.packageId,meal.product);heldGrip=Group("Held meal handhold contact",heldProp);heldGrip.localPosition=PackageGripOffset(meal.packageId);heldGrip.rotation=workActor.RightPalmRotation;
            if(!workActor.AttachVisualProp(heldProp,heldGrip)){ClearHeldProp();return false;}
            heldMealInHand=true;heldMealId=meal.id;return true;
        }
        private void RestoreHeldMealVisual(BusinessSession session)
        {
            // A synchronous sale/checkpoint callback may clear HeldMeal before
            // its travelling visual has been transferred to the receiver.
            foreach(var visit in serviceVisits)if(visit.direct&&visit.committing)return;
            if(session==null)return;
            if(session.HeldMeal==null){if(heldMealInHand)ClearHeldProp();return;}
            if(HasHeldMealInHand&&heldMealId==session.HeldMeal.id&&heldProp.gameObject.activeInHierarchy&&workActor.CaptureAudit().carryingVisualProp)return;
            if(WorkActorBusy)return;BuildHeldProp(session.HeldMeal);
        }
        private bool RejectVendorWork(string reason,Action finished)
        {WorkFailed?.Invoke(reason);finished?.Invoke();return false;}
        public void PerformPackToHand(int lane,string packageId,Action contact,Action finished)
        {
            if(WorkActorBusy){RejectVendorWork("手头的动作还没做完",finished);return;}
            BusinessSession session=activeSession;
            if(session==null||lane<0||lane>=panStations.Count){RejectVendorWork("没有可装餐的锅位",finished);return;}
            if(!packages.TryGetValue(packageId,out Station pack)){RejectVendorWork("包材不在摊上",finished);return;}
            if(!session.CanPackToHand(lane,packageId,out string reason)){RejectVendorWork(reason,finished);return;}
            sequenceBusy=true;sequenceFinished=finished;Station pan=panStations[lane];
            FoodProduct product=session.ProductAtLane(lane);FoodInput[] inputs=session.Lanes[lane].freeBatch?.inputs;
            Execute(VendorWorkAction.Scoop,pan,.70f,()=>
            {
                carriedProp=Group("Portion to hand pack",cooking);carriedProp.position=pan.left.position;Bowl(carriedProp,.115f,.025f,cream);
                Transform portion=Group("Carried portion",carriedProp);BuildMealFoodVisual(portion,product,inputs,true);portion.localScale=Vector3.one*.55f;
                Transform grip=Group("Left serving grip",carriedProp);grip.rotation=pan.left.rotation;
                if(!workActor.AttachVisualProp(carriedProp,grip,true))workActor.Abort("Cooked serving missed the measured left palm");
            },()=>Execute(VendorWorkAction.Pack,pack,.70f,()=>
            {
                if(carriedProp){workActor.ReleaseVisualProp();Destroy(carriedProp.gameObject);carriedProp=null;}
                contact?.Invoke(); // Controller is the sole economic writer.
                CounterMeal held=session.HeldMeal;
                if(held==null||held.packageId!=packageId||held.product?.id!=product?.id){workActor.Abort("Packing transaction did not produce the requested held meal");return;}
                if(!BuildHeldProp(held))workActor.Abort("Packed meal missed the measured right palm");
            },CompleteWork));
        }
        public void PerformPlaceHeldMeal(Action contact,Action finished)
        {
            if(WorkActorBusy){RejectVendorWork("手头的动作还没做完",finished);return;}
            var session=activeSession;RestoreHeldMealVisual(session);
            if(session==null||session.HeldMeal==null||!HasHeldMealInHand){RejectVendorWork("手上还没有装好的餐",finished);return;}
            if(!session.CanPlaceHeldMeal(out string reason)){RejectVendorWork(reason,finished);return;}
            CounterMealSlot slot=FirstFreeCounterSlot();if(slot==null){RejectVendorWork("取餐区已经放满了",finished);return;}
            CounterMeal meal=session.HeldMeal;sequenceBusy=true;sequenceFinished=finished;LastCounterPlacementError=float.PositiveInfinity;
            Station place=CounterReachStation(slot,meal.packageId,"Held meal place contact",heldGrip.localRotation);
            Execute(VendorWorkAction.PickIngredient,place,.70f,()=>
            {
                if(!heldProp||session.HeldMeal?.id!=meal.id){workActor.Abort("Held meal identity changed before table contact");return;}
                Vector3 at=heldProp.position;Quaternion rotation=heldProp.rotation;
                LastCounterPlacementError=Vector3.Distance(at,CounterRestPosition(slot,meal.packageId));
                if(LastCounterPlacementError>workActor.contactTolerance){workActor.Abort("Held container missed the supported table pose: "+LastCounterPlacementError.ToString("F4"));return;}
                contact?.Invoke();
                if(session.HeldMeal!=null||!SessionHasCounterMeal(meal.id)){workActor.Abort("Placement transaction was not accepted");return;}
                Transform visual=heldProp;workActor.ReleaseVisualProp(false);visual.SetParent(slot.tray,true);visual.SetPositionAndRotation(at,rotation);visual.gameObject.SetActive(true);
                heldMealInHand=false;heldMealId=null;heldProp=heldGrip=null;
                slot.mealId=meal.id;slot.mealVisual=visual;presentedMealIds.Add(meal.id);
                Target(visual,StallInteractionKind.CounterMeal,meal.id,-1,slot.tray.position+Vector3.up*.30f,new Vector3(.36f,.30f,.32f));
            },()=>{if(place.right)Destroy(place.right.gameObject);CompleteWork();});
        }
        private bool SessionHasCounterMeal(string id)
        {if(activeSession==null)return false;foreach(var meal in activeSession.ReadyMeals)if(meal.id==id)return true;return false;}
        public bool IsCounterMealReserved(string mealId)
        {foreach(var v in serviceVisits)if(!v.closed&&!v.direct&&v.mealId==mealId)return true;return false;}
        private bool GuestReserved(int orderId)
        {foreach(var v in serviceVisits)if(!v.closed&&v.orderId==orderId)return true;return false;}
        public bool BeginCustomerPickup(string mealId,int orderId,Action contact,Action finished,Action failed)
        {
            LastPickupRejection="";
            if(activeSession==null||GuestReserved(orderId)||IsCounterMealReserved(mealId)||!activeSession.CanServeCounterMeal(mealId,orderId,out _))return false;
            var slot=FindCounterSlot(mealId);
            if(slot==null||!slot.mealVisual||!guests.TryGetValue(orderId,out Guest guest)||!IsOrderServable(orderId))return false;
            var visit=new ServiceVisit{guest=guest,slot=slot,mealId=mealId,orderId=orderId,visual=slot.mealVisual,contact=contact,finished=finished,failed=failed};
            BeginShortService(visit);return true;
        }
        public void PerformHeldMealDelivery(int orderId,Action contact,Action finished,Action failed)
        {
            if(WorkActorBusy||GuestReserved(orderId)){failed?.Invoke();finished?.Invoke();return;}
            RestoreHeldMealVisual(activeSession);
            if(activeSession==null||!HasHeldMealInHand||!activeSession.CanServeHeldMeal(orderId,out _)||!guests.TryGetValue(orderId,out Guest guest)||!IsOrderServable(orderId))
            {failed?.Invoke();finished?.Invoke();return;}
            var visit=new ServiceVisit{guest=guest,direct=true,mealId=activeSession.HeldMeal.id,orderId=orderId,visual=heldProp,contact=contact,finished=finished,failed=failed};
            sequenceBusy=true;sequenceFinished=null;
            Vector3 at=heldProp.position;Quaternion rotation=heldProp.rotation;workActor.ReleaseVisualProp(false);heldProp.SetPositionAndRotation(at,rotation);
            BeginShortService(visit);
        }
        private void BeginShortService(ServiceVisit visit)
        {
            visit.startPosition=visit.visual.position;visit.startRotation=visit.visual.rotation;visit.startParent=visit.visual.parent;
            visit.visual.SetParent(cooking,true);DisableMealTarget(visit.visual);
            visit.hand=NamedBone(visit.guest.root,"RightHand");
            if(visit.guest.presentation!=null){visit.guest.presentation.ExternalReceive=true;visit.guest.presentation.ExternalReceiveBusy=true;}
            visit.guest.walking=false;Play(visit.guest,false);serviceVisits.Add(visit);
        }
        public bool CancelCustomerPickup(string mealId)
        {foreach(var visit in serviceVisits.ToArray())if(!visit.direct&&visit.mealId==mealId&&!visit.received){CancelVisit(visit,true);return true;}return false;}
        public void CancelAllCustomerPickups()
        {foreach(var visit in serviceVisits.ToArray())if(visit.received)FinishVisit(visit);else CancelVisit(visit,true);}
        private void CancelVendorService()
        {foreach(var visit in serviceVisits.ToArray())if(visit.direct){if(visit.received)FinishVisit(visit);else CancelVisit(visit,true);}}
        private bool IsVisiting(Guest guest)
        {foreach(var visit in serviceVisits)if(!visit.closed&&visit.guest==guest)return true;return false;}
        private void CancelVisit(ServiceVisit visit,bool reportFailure)
        {
            if(visit==null||visit.closed)return;
            if(visit.committing){visit.cancelRequested=true;return;}
            if(visit.received){FinishVisit(visit);return;}
            visit.closed=true;serviceVisits.Remove(visit);
            if(visit.guest.presentation!=null){visit.guest.presentation.ExternalReceiveBusy=false;visit.guest.presentation.ExternalReceive=false;}
            if(visit.visual)
            {
                visit.visual.SetParent(visit.startParent,true);visit.visual.SetPositionAndRotation(visit.startPosition,visit.startRotation);
                var target=visit.visual.GetComponent<StallInteractionTarget>();if(target){target.enabled=true;if(target.HitCollider)target.HitCollider.enabled=true;}
            }
            if(visit.direct)
            {
                sequenceFinished=null;sequenceBusy=false;
                if(activeSession?.HeldMeal?.id==visit.mealId)BuildHeldProp(activeSession.HeldMeal);
                CompleteWork();
            }
            Action failure=visit.failed,done=visit.finished;visit.contact=visit.failed=visit.finished=null;
            if(reportFailure)failure?.Invoke();done?.Invoke();
        }
        // v21: a short readable service animation, not an IK contact test.
        // Characters stay on their collision-safe standing spots; only the food
        // visual travels. The existing Controller callback alone changes money.
        private void TickServiceVisit(float dt)
        {
            if(dt<=0||activeSession==null)return;
            foreach(var visit in serviceVisits.ToArray())
            {
                if(visit.closed)continue;
                if(!visit.guest.root||!visit.guest.root.gameObject.activeInHierarchy){CancelVisit(visit,true);continue;}
                var order=activeSession.FindOrder(visit.orderId);
                if(!visit.received&&(order==null||order.departed||order.served||
                    (visit.direct?activeSession.HeldMeal?.id!=visit.mealId:!SessionHasCounterMeal(visit.mealId)))){CancelVisit(visit,true);continue;}
                if(!visit.visual){CancelVisit(visit,true);continue;}
                visit.elapsed+=dt;
                Vector3 look=player.position-visit.guest.root.position;look.y=0;
                if(look.sqrMagnitude>.001f)visit.guest.root.rotation=Quaternion.RotateTowards(visit.guest.root.rotation,Quaternion.LookRotation(look),300*dt);
                Vector3 end=visit.hand?visit.hand.position:visit.guest.root.position+Vector3.up*1.0f;
                float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(visit.elapsed/.42f));
                if(!visit.received)
                {
                    visit.visual.position=Vector3.Lerp(visit.startPosition,end,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.24f);
                    visit.visual.rotation=Quaternion.Slerp(visit.startRotation,visit.guest.root.rotation,t);
                    if(visit.elapsed>=.42f)
                    {
                        bool allowed=visit.direct?activeSession.CanServeHeldMeal(visit.orderId,out _):activeSession.CanServeCounterMeal(visit.mealId,visit.orderId,out _);
                        if(!allowed){CancelVisit(visit,true);continue;}
                        Action commit=visit.contact;visit.contact=null;visit.committing=true;
                        try{commit?.Invoke();}finally{visit.committing=false;}
                        bool sold=visit.direct?activeSession.HeldMeal?.id!=visit.mealId:!SessionHasCounterMeal(visit.mealId);
                        if(!sold){CancelVisit(visit,true);continue;}
                        visit.received=true;
                        if(visit.direct){heldProp=heldGrip=null;heldMealInHand=false;heldMealId=null;}
                        else{visit.slot.mealVisual=null;visit.slot.mealId=null;presentedMealIds.Remove(visit.mealId);}
                        // The callback may synchronously request checkpoint/cancel.
                        // Economic state is authoritative even in that reentrant path.
                        if(visit.closed){if(visit.visual)Destroy(visit.visual.gameObject);continue;}
                        visit.visual.SetParent(visit.hand?visit.hand:visit.guest.root,true);visit.visual.position=end;
                    }
                }
                if(visit.received&&(visit.elapsed>=.55f||visit.cancelRequested))FinishVisit(visit);
            }
        }
        private static Transform NamedBone(Transform root,string suffix)
        {foreach(var t in root.GetComponentsInChildren<Transform>(true)){string n=t.name;int at=n.LastIndexOf(':');if(at>=0)n=n.Substring(at+1);if(n==suffix||n=="mixamorig"+suffix)return t;}return null;}
        private static void DisableMealTarget(Transform visual)
        {if(!visual)return;var target=visual.GetComponent<StallInteractionTarget>();if(target){target.enabled=false;if(target.HitCollider)target.HitCollider.enabled=false;}}
        private void FinishVisit(ServiceVisit visit)
        {
            if(visit==null||visit.closed||!visit.received)return;
            visit.closed=true;serviceVisits.Remove(visit);
            if(visit.guest.presentation!=null)visit.guest.presentation.ExternalReceiveBusy=false;
            Action done=visit.finished;visit.finished=visit.failed=visit.contact=null;
            if(visit.direct){sequenceFinished=null;CompleteWork();}done?.Invoke();
        }
    }
}
