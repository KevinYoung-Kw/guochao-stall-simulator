using System;
using UnityEngine;

namespace Guochao
{
    // Claude authored the initial held-meal presentation scaffold; v20 review
    // completes callback-only commits and measured customer-side fulfilment.
    public sealed partial class StreetStallStage
    {
        private Transform heldProp,heldGrip;
        private string heldMealId;
        private bool heldMealInHand;
        private ServiceVisit serviceVisit;
        public bool HasHeldMealInHand=>heldMealInHand&&heldProp!=null;
        public string HeldMealVisualId=>HasHeldMealInHand?heldMealId:"";
        public float LastCustomerPickupContactError {get;private set;}=float.PositiveInfinity;
        public string LastPickupRejection {get;private set;}="";
        public float LastDirectHandoffContactError {get;private set;}=float.PositiveInfinity;
        private sealed class ServiceVisit
        {
            public Guest guest;public CustomerPickupReach reach;public CounterMealSlot slot;
            public string mealId;public int orderId;public bool direct,returning,gestureStarted,vendorArrived,vendorFinished,received,closed;
            public Vector3 destination,vendorStand;public Quaternion facing=Quaternion.LookRotation(Vector3.back,Vector3.up);public Transform customerGrip,vendorGrip,vendorLeftGrip;
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
        public bool IsCounterMealReserved(string mealId)=>serviceVisit!=null&&!serviceVisit.returning&&!serviceVisit.direct&&serviceVisit.mealId==mealId;
        private CustomerPickupReach ReachFor(Guest guest)
        {var reach=guest.root.GetComponent<CustomerPickupReach>();return reach?reach:guest.root.gameObject.AddComponent<CustomerPickupReach>();}
        private Transform CustomerPackageGrip(Transform package,string packageId)
        {
            Transform grip=Group("Customer package edge",package);Vector3 offset=PackageGripOffset(packageId);offset.x=-offset.x;offset.z=-offset.z;grip.localPosition=offset;
            grip.rotation=Quaternion.LookRotation(Vector3.back,Vector3.up);return grip;
        }
        public bool BeginCustomerPickup(string mealId,int orderId,Action contact,Action finished,Action failed)
        {
            LastPickupRejection="";
            if(serviceVisit!=null||activeSession==null||!activeSession.CanServeCounterMeal(mealId,orderId,out _))return false;
            CounterMealSlot slot=FindCounterSlot(mealId);
            if(slot==null||!slot.mealVisual||!guests.TryGetValue(orderId,out Guest guest)||!IsOrderServable(orderId))return false;
            CounterMeal meal=null;foreach(var m in activeSession.ReadyMeals)if(m.id==mealId){meal=m;break;}if(meal==null)return false;
            CustomerPickupReach reach=ReachFor(guest);if(!reach.BindSide(false))return false;
            Transform grip=CustomerPackageGrip(slot.mealVisual,meal.packageId);
            float standZ=ServingCounterBack+guest.agent.Profile.Radius+.035f;
            bool planned=reach.TryPlanApproach(grip,standZ,p=>guestNavigation.CanOccupy(p,guest.agent.Profile,guest.agent),out Vector3 stand,out Quaternion facing);
            if(!planned)
            {LastPickupRejection="No collision-clear, measured-arm-reachable stance; grip="+grip.position.ToString("F3")+" "+reach.ReachDiagnostic;Debug.LogWarning("PICKUP_REJECTED "+LastPickupRejection);Destroy(grip.gameObject);return false;}
            LastCustomerPickupContactError=float.PositiveInfinity;
            serviceVisit=new ServiceVisit{guest=guest,reach=reach,slot=slot,mealId=mealId,orderId=orderId,destination=stand,facing=facing,customerGrip=grip,contact=contact,finished=finished,failed=failed};
            guest.agent.AllowDynamicDetours=true;return true;
        }
        public void PerformHeldMealDelivery(int orderId,Action contact,Action finished,Action failed)
        {
            if(WorkActorBusy||serviceVisit!=null){failed?.Invoke();RejectVendorWork("取餐口正忙，稍等一下",finished);return;}
            var session=activeSession;RestoreHeldMealVisual(session);
            if(session==null||!HasHeldMealInHand||!session.CanServeHeldMeal(orderId,out _)||!guests.TryGetValue(orderId,out Guest guest)||!IsOrderServable(orderId))
            {failed?.Invoke();finished?.Invoke();return;}
            CustomerPickupReach reach=ReachFor(guest);if(!reach.BindSide(true)){failed?.Invoke();finished?.Invoke();return;}
            float x=counterSlots[counterSlots.Count-1].tray.position.x+.24f+guest.agent.Profile.Radius+.34f;
            Vector3 vendorStand=new Vector3(x,0,.12f),customerStand=new Vector3(x,0,.12f+.25f+guest.agent.Profile.Radius+.035f);
            if(!guestNavigation.CanOccupy(customerStand,guest.agent.Profile,guest.agent)){failed?.Invoke();finished?.Invoke();return;}
            var visit=new ServiceVisit{guest=guest,reach=reach,direct=true,mealId=session.HeldMeal.id,orderId=orderId,destination=customerStand,vendorStand=vendorStand,contact=contact,finished=finished,failed=failed};
            serviceVisit=visit;sequenceBusy=true;sequenceFinished=finished;guest.agent.AllowDynamicDetours=true;LastDirectHandoffContactError=float.PositiveInfinity;
            if(!workActor.MoveTo(vendorStand,vendorStand+Vector3.forward,()=>visit.vendorArrived=true,WorkRoute(player.position,vendorStand)))CancelVisit(visit,true);
        }
        public bool CancelCustomerPickup(string mealId)
        {var visit=serviceVisit;if(visit==null||visit.direct||visit.returning||visit.mealId!=mealId||visit.received)return false;CancelVisit(visit,true);return true;}
        public void CancelAllCustomerPickups()
        {
            var visit=serviceVisit;if(visit==null)return;
            if(!visit.received)CancelVisit(visit,true);
            else if(visit.direct)CancelVendorService();
        }
        // Kitchen re-targeting cancels only the vendor's work. A customer who is
        // walking to the counter remains independent. A committed direct handoff
        // keeps its actual package and finishes the receiving arm after checkpoint.
        private void CancelVendorService()
        {
            var visit=serviceVisit;if(visit==null||!visit.direct||visit.returning)return;
            if(!visit.received){CancelVisit(visit,true);return;}
            visit.vendorFinished=true;sequenceFinished=null;
            if(!visit.reach.IsBusy)FinishVisit(visit);
        }
        private void CancelVisit(ServiceVisit visit,bool reportFailure)
        {
            if(visit==null||visit.closed)return;
            if(reportFailure&&!visit.returning)Debug.LogWarning("SERVICE_VISIT_CANCELLED order="+visit.orderId+" direct="+visit.direct+" received="+visit.received+" "+visit.reach?.ReachDiagnostic);
            visit.reach?.Cancel();visit.returning=true;visit.gestureStarted=false;
            if(visit.customerGrip){Destroy(visit.customerGrip.gameObject);visit.customerGrip=null;}
            if(visit.vendorGrip){Destroy(visit.vendorGrip.gameObject);visit.vendorGrip=null;}
            if(visit.vendorLeftGrip){Destroy(visit.vendorLeftGrip.gameObject);visit.vendorLeftGrip=null;}
            if(reportFailure){Action failure=visit.failed;visit.failed=null;failure?.Invoke();}
            Action done=visit.finished;visit.finished=null;visit.contact=null;
            if(visit.direct){workActor.Cancel();sequenceFinished=null;CompleteWork();}
            done?.Invoke();
        }
        private bool IsVisiting(Guest guest)=>serviceVisit!=null&&serviceVisit.guest==guest;
        private float FinishPickupApproach(Guest guest,Vector3 destination,float dt)
        {
            // Ordinary crowd slots accept 7cm proximity. A short-arm contact
            // needs the actual planned stance: finish those centimetres with
            // the SAME full-capsule sweep and CharacterController, never a warp.
            Vector3 start=guest.root.position,goal=destination;goal.y=start.y;
            if(Vector3.Distance(start,goal)<=.003f)return 0;
            Vector3 next=Vector3.MoveTowards(start,goal,.65f*dt);
            if(!guestNavigation.CanReachDirect(guest.agent,next))
            {guest.agent.BlockedSeconds+=dt;guest.agent.BlockedReason="pickup-final-approach-blocked";return 0;}
            guest.agent.Controller.Move(next-start);Physics.SyncTransforms();
            Vector3 delta=guest.root.position-start;delta.y=0;
            guest.agent.Travelled+=delta.magnitude;guest.agent.Velocity=delta/Mathf.Max(.0001f,dt);
            if(delta.sqrMagnitude<.0000001f){guest.agent.BlockedSeconds+=dt;guest.agent.BlockedReason="pickup-final-controller-blocked";}
            else{guest.agent.BlockedSeconds=0;guest.agent.BlockedReason="";}
            return delta.magnitude;
        }
        private void TickServiceVisit(float dt)
        {
            var visit=serviceVisit;if(visit==null||dt<=0)return;Guest guest=visit.guest;
            if(!guest.root||!guest.root.gameObject.activeInHierarchy){CancelVisit(visit,true);serviceVisit=null;return;}
            StallOrder order=activeSession.FindOrder(visit.orderId);
            if(!visit.received&&!visit.returning&&(order==null||order.departed||order.served||
                (visit.direct?activeSession.HeldMeal?.id!=visit.mealId:!SessionHasCounterMeal(visit.mealId))))CancelVisit(visit,true);
            if(visit.returning)
            {
                if(order==null||order.departed||order.served){visit.closed=true;guest.agent.AllowDynamicDetours=false;serviceVisit=null;return;}
                Vector3 back=QueuePoints[guest.slot];float moved=guestNavigation.Step(guest.agent,back,1.32f,dt);guest.walking=moved>.0002f;Play(guest,guest.walking);
                if(guestNavigation.HasArrived(guest.agent,back)){visit.closed=true;guest.agent.AllowDynamicDetours=false;serviceVisit=null;}return;
            }
            if(!visit.gestureStarted)
            {
                bool finalPickupStep=!visit.direct&&guestNavigation.HasArrived(guest.agent,visit.destination);
                float moved=finalPickupStep?FinishPickupApproach(guest,visit.destination,dt):guestNavigation.Step(guest.agent,visit.destination,1.32f,dt);
                guest.walking=moved>.0002f;Play(guest,guest.walking);
                if(guest.agent.BlockedSeconds>6){CancelVisit(visit,true);return;}
                if(!guestNavigation.HasArrived(guest.agent,visit.destination,visit.direct?.07f:.003f))return;
                guest.root.rotation=Quaternion.RotateTowards(guest.root.rotation,visit.facing,190*dt);
                if(Quaternion.Angle(guest.root.rotation,visit.facing)>2)return;
                if(visit.direct)
                {
                    if(!visit.vendorArrived)return;StartDirectGesture(visit);
                }
                else StartPickupGesture(visit);
            }
            visit.reach.Tick(dt);
            if(visit.received&&!visit.reach.IsBusy&&(!visit.direct||visit.vendorFinished))FinishVisit(visit);
        }
        private void StartPickupGesture(ServiceVisit visit)
        {
            visit.gestureStarted=true;Play(visit.guest,false);
            Debug.Log("PICKUP_AT_STANCE order="+visit.orderId+" actual="+visit.guest.root.position.ToString("F4")+" planned="+visit.destination.ToString("F4")+" facingError="+Quaternion.Angle(visit.guest.root.rotation,visit.facing).ToString("F2")+" "+visit.reach.ReachDiagnostic);
            if(!visit.reach.Begin(visit.customerGrip,false,()=>
            {
                if(!activeSession.CanServeCounterMeal(visit.mealId,visit.orderId,out _)){CancelVisit(visit,true);return;}
                LastCustomerPickupContactError=visit.reach.LastContactError;
                if(LastCustomerPickupContactError>.025f){CancelVisit(visit,true);return;}
                Transform visual=visit.slot.mealVisual;visit.contact?.Invoke();
                if(SessionHasCounterMeal(visit.mealId)){CancelVisit(visit,true);return;}
                DisableMealTarget(visual);visit.slot.mealVisual=null;visit.slot.mealId=null;presentedMealIds.Remove(visit.mealId);
                visit.received=true;visit.reach.AcceptPackage(visual,visit.customerGrip);
            },()=>FinishVisit(visit),()=>CancelVisit(visit,true)))CancelVisit(visit,true);
        }
        private void StartDirectGesture(ServiceVisit visit)
        {
            Transform shoulder=NamedBone(player,"RightArm"),customerShoulder=NamedBone(visit.guest.root,"LeftArm");
            if(!shoulder||!customerShoulder){CancelVisit(visit,true);return;}
            visit.gestureStarted=true;
            Vector3 point=(shoulder.position+customerShoulder.position)*.5f;point.y=Mathf.Min(shoulder.position.y,customerShoulder.position.y)-.04f;
            visit.vendorGrip=Group("Direct physical handoff",cooking);visit.vendorGrip.SetPositionAndRotation(point,heldGrip.localRotation);
            Transform vendorLeftShoulder=NamedBone(player,"LeftArm");
            if(!vendorLeftShoulder){CancelVisit(visit,true);return;}
            // Pack's actor contract always solves both hands. Explicitly keep
            // the free hand bent at the waist; the ground-level facing point
            // must never become its fallback physical contact target.
            visit.vendorLeftGrip=Group("Direct free hand rest",cooking);
            visit.vendorLeftGrip.SetPositionAndRotation(vendorLeftShoulder.position+Vector3.down*.24f+Vector3.forward*.10f,workActor.LeftPalmRotation);
            Station station=new Station{stand=visit.vendorStand,right=visit.vendorGrip,left=visit.vendorLeftGrip,facingPoint=visit.vendorStand+Vector3.forward};
            Execute(VendorWorkAction.Pack,station,3.4f,()=>
            {
                if(activeSession.HeldMeal?.id!=visit.mealId||!heldProp){CancelVisit(visit,true);return;}
                visit.customerGrip=CustomerPackageGrip(heldProp,activeSession.HeldMeal.packageId);
                if(!visit.reach.Begin(visit.customerGrip,true,()=>
                {
                    if(!activeSession.CanServeHeldMeal(visit.orderId,out _)){CancelVisit(visit,true);return;}
                    LastDirectHandoffContactError=Mathf.Max(visit.reach.LastContactError,Vector3.Distance(workActor.RightPalmPosition,heldGrip.position));
                    if(LastDirectHandoffContactError>.025f){CancelVisit(visit,true);return;}
                    Transform visual=heldProp;Vector3 at=visual.position;Quaternion rotation=visual.rotation;visit.contact?.Invoke();
                    if(activeSession.HeldMeal!=null){CancelVisit(visit,true);return;}
                    workActor.ReleaseVisualProp(false);visual.SetPositionAndRotation(at,rotation);visual.gameObject.SetActive(true);
                    heldProp=heldGrip=null;heldMealInHand=false;heldMealId=null;visit.received=true;visit.reach.AcceptPackage(visual,visit.customerGrip);
                },()=>{if(visit.vendorFinished)FinishVisit(visit);},()=>CancelVisit(visit,true)))CancelVisit(visit,true);
            },()=>{visit.vendorFinished=true;if(visit.received&&!visit.reach.IsBusy)FinishVisit(visit);});
        }
        private static Transform NamedBone(Transform root,string suffix)
        {foreach(var t in root.GetComponentsInChildren<Transform>(true)){string n=t.name;int at=n.LastIndexOf(':');if(at>=0)n=n.Substring(at+1);if(n==suffix||n=="mixamorig"+suffix)return t;}return null;}
        private static void DisableMealTarget(Transform visual)
        {if(!visual)return;var target=visual.GetComponent<StallInteractionTarget>();if(target){target.enabled=false;if(target.HitCollider)target.HitCollider.enabled=false;}}
        private void FinishVisit(ServiceVisit visit)
        {
            if(visit==null||visit.closed||!visit.received||visit.reach.IsBusy||visit.direct&&!visit.vendorFinished)return;
            visit.closed=true;visit.guest.agent.AllowDynamicDetours=false;if(visit.vendorGrip)Destroy(visit.vendorGrip.gameObject);if(visit.vendorLeftGrip)Destroy(visit.vendorLeftGrip.gameObject);
            if(serviceVisit==visit)serviceVisit=null;Action done=visit.finished;visit.finished=null;
            if(visit.direct){sequenceFinished=null;CompleteWork();}done?.Invoke();
        }
    }
}
