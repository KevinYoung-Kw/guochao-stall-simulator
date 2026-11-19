using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Guochao
{
    public static class BusinessInteractionProbe
    {
        [Serializable] public sealed class Part { public int serial,part,count;public string id,data; }
        [Serializable] public sealed class Target
        {
            public string kind,itemId;
            public int lane,orderId;
            public Vector3 world,hitScreen,anchorScreen;
            public Rect readoutScreen;
        }
        [Serializable] public sealed class Sample
        {
            public string schema="guochao.business.interaction.v1",focus,carriedPackage;
            public int width,height,selectedOrder,selectedLane,draggedDishLane;
            public bool paused,actorBusy,modal;
            public Vector3 actorPosition;
            public Target[] targets;
            public BusinessAuditState business;
            public VendorWorkAudit vendor;
        }
        public static Sample Capture(BusinessController owner)
        {
            var targets=new List<Target>();
            foreach(var target in owner.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                Collider collider=target.GetComponentInChildren<Collider>();Vector3 hit=collider!=null?collider.bounds.center:target.transform.position;
                targets.Add(new Target{kind=target.kind.ToString(),itemId=target.itemId,lane=target.lane,orderId=target.orderId,world=target.WorldAnchor,
                    hitScreen=owner.Stage.View.WorldToScreenPoint(hit),anchorScreen=owner.Stage.View.WorldToScreenPoint(target.WorldAnchor),readoutScreen=owner.HUD.ReadoutScreenRect(target)});
            }
            return new Sample{width=Screen.width,height=Screen.height,selectedOrder=owner.SelectedOrderId,selectedLane=owner.SelectedLaneIndex,
                paused=owner.IsPaused,actorBusy=owner.Stage.WorkActorBusy,actorPosition=owner.Stage.WorkActorPosition,modal=owner.HUD.HasBusinessOverlay,
                focus=EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null?EventSystem.current.currentSelectedGameObject.name:"",
                carriedPackage=owner.InputHandler!=null?owner.InputHandler.CarriedPackageId:"",draggedDishLane=owner.InputHandler!=null?owner.InputHandler.DraggedDishLane:-1,targets=targets.ToArray(),business=owner.CaptureAudit(),vendor=owner.Stage.WorkAudit};
        }
    }
}
