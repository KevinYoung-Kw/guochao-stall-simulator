using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // S6 night online procurement wiring. The coordinator-owned GameFlow hooks
    // (CreateWorld → InstallOnlineParcelWorld, PrimaryAction → ParcelBox,
    // UpdateFocus → HasReadyOnlineParcels, Prompt) call into these members.
    public sealed partial class GameFlow
    {
        private GameObject onlineParcelBox;
        public bool HasReadyOnlineParcels
        {
            get
            {
                if(Campaign==null||run==null||run.Phase!=GamePhase.Home)return false;
                return Campaign.onlineOrders!=null&&Campaign.onlineOrders.Exists(x=>!x.received&&x.dueDay<=Campaign.dayIndex);
            }
        }
        public bool TryOrderOnline(string productId,int quotedCents,out string reason)
        {
            reason="";
            if(Campaign==null||run==null){reason="没有正在进行的经营记录。";return false;}
            bool ok=run.TryPlaceOnlineOrder(Campaign,productId,quotedCents,out reason);
            if(ok){SaveCampaign();Publish();}else if(string.IsNullOrEmpty(reason))reason=run.Message;
            return ok;
        }
        public bool TryCollectOnline(string orderId,out string reason)
        {
            reason="";
            if(Campaign==null||run==null){reason="没有正在进行的经营记录。";return false;}
            if(run.Phase!=GamePhase.Home){reason="包裹在家门口，回家才能签收。";return false;}
            if(run.Paused){reason="游戏暂停中，先继续游戏再开箱。";return false;}
            if(IsBusy){reason="正在处理其他动作，请稍候。";return false;}
            if(world==null||!world.IsNear("ParcelBox",1.55f)){reason="请先走到家门口的包裹旁。";return false;}
            if(run.HoldingStove||!string.IsNullOrEmpty(run.HeldGrocery)){reason="先腾出双手，再签收包裹。";return false;}
            bool ok=run.TryClaimOnlineOrder(Campaign,orderId,out reason);
            if(ok){world.Sync(run);SaveCampaign();Publish();}
            if(!HasReadyOnlineParcels)SetParcelBoxVisible(false);
            return ok;
        }
        private void InstallOnlineParcelWorld()
        {
            onlineParcelBox=null;
            if(world==null||run==null||run.Phase!=GamePhase.Home||!HasReadyOnlineParcels)return;
            // A small cardboard parcel beside the home cart, off the walking
            // line: a plain primitive prop (no collider), not a new AI model.
            Vector3 basePosition=world.HasAnchor("HomeCart")?world.Anchor("HomeCart"):world.HasAnchor("HomeExit")?world.Anchor("HomeExit"):world.Player!=null?world.Player.transform.position:Vector3.zero;
            Vector3 aside=new Vector3(.95f,0,.6f);
            if(world.HasAnchor("HomeCartPlacement")){var p=world.Anchors["HomeCartPlacement"];aside=p.right*.95f+p.forward*.6f;}
            var anchor=new GameObject("Anchor_ParcelBox").transform;
            anchor.SetParent(world.transform,false);anchor.position=basePosition+aside;
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var collider=box.GetComponent<Collider>();
            if(Application.isPlaying)UnityEngine.Object.Destroy(collider);else UnityEngine.Object.DestroyImmediate(collider);
            box.name="OnlineParcelBox";box.transform.SetParent(world.transform,true);
            box.transform.position=anchor.position+Vector3.up*.22f;
            box.transform.localScale=new Vector3(.55f,.44f,.42f);
            box.transform.rotation=world.HasAnchor("HomeCartPlacement")?world.Anchors["HomeCartPlacement"].rotation*Quaternion.Euler(0,14,0):Quaternion.identity;
            var renderer=box.GetComponent<Renderer>();
            if(renderer!=null)renderer.material.color=new Color(.72f,.55f,.34f);
            onlineParcelBox=box;
            world.Anchors["ParcelBox"]=anchor;
        }
        private void SetParcelBoxVisible(bool visible)
        {
            if(onlineParcelBox!=null)onlineParcelBox.SetActive(visible);
        }
        private void OpenOnlineParcels()
        {
            if(!HasReadyOnlineParcels)return;
            world?.Player?.StopMotion();
            if(world?.Player!=null)world.Player.presentationLocked=true;
            OnlineProcurementUI.OpenParcels(this);
        }
    }
}
