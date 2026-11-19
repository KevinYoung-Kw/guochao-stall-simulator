using System;
using UnityEngine;

namespace Guochao
{
    public sealed partial class GameFlow
    {
        public CampaignState Campaign {get;private set;}
        public bool HasCampaignSave=>CampaignSaveStore.Exists;
        public string SaveStatus {get;private set;}="";
        private bool restoringCampaign,autoSavePending;
        private float autoSaveAt;
        private void QueueAutoSave()
        {if(Campaign==null||restoringCampaign)return;autoSavePending=true;autoSaveAt=Time.unscaledTime+.8f;}
        private void LateUpdate()
        {
            if(!autoSavePending||Time.unscaledTime<autoSaveAt||IsBusy||CharacterCreationUI.IsOpen||restoringCampaign)return;
            autoSavePending=false;SaveCampaign();
        }
        private void OnBusinessPhase(GamePhase phase)
        {run.SetBusinessPhase(phase);Publish();QueueAutoSave();}
        private void OnBusinessCash(int cash)
        {run.SetBusinessCash(cash);if(!restoringCampaign)CommitCampaignDay();}
        private void CommitCampaignDay()
        {
            if(Campaign==null||business?.Session==null||business.Session.Setup.practice||!business.Session.Finished)return;
            Campaign.CommitDay(run,business.Session,business.Session.SessionId);run.SetBusinessCash(Campaign.cashCents);
        }
        public void EnterNightHub()
        {
            if(Campaign==null||business?.Session==null||!business.Session.Finished)return;
            CommitCampaignDay();CampaignMenuUI.Close();business.Cancel();business=null;
            run=RunState.FromCampaign(Campaign,checkoutServiceSeconds);run.SetBusinessPhase(GamePhase.Night);run.Paused=true;
            CreateWorld(GamePhase.Home);PresentNightWorld();GetComponent<ArtHUD>()?.SetGameplayVisible(false);
            NightHubUI.Open(this);SaveCampaign();Publish();
        }
        private void PresentNightWorld()
        {
            if(world==null)return;
            foreach(Light light in world.GetComponentsInChildren<Light>())
            {
                if(light.type==LightType.Directional){light.color=new Color(1,.77f,.53f);light.intensity=.85f;light.transform.rotation=Quaternion.Euler(29,-38,0);}
            }
            world.Sync(run);
            if(world.Player!=null){world.Player.StopMotion();world.Player.inputEnabled=false;world.Player.presentationLocked=true;}
        }
        public void StartNextDay()
        {
            if(Campaign==null||run.Phase!=GamePhase.Night)return;
            // A failed write leaves the current night intact and reviewable.
            if(!SaveCampaign())return;
            // NIGHT9 N01：睡一觉时结算今晚的留车检定（逐件独立、同日固定种子）。保存
            // 已先行落盘，检定后再经下方 SaveCampaign 持久化——预存失败/断点恢复重进
            // 此函数会拿到同一 resolved 记录，不重复掷骰、不重复移走资产。幸存留车重器由 CreateNextDay→KeepVehicleGear 直接留在次日车上（含原
            // HomeEquipment 列表语义不变，见 NightSecurity.cs）。
            NightSecurityRecord nightRecord=NightSecurityRules.Resolve(Campaign);
            string nightNotice=nightRecord!=null?nightRecord.notice:"";
            RunState next=Campaign.CreateNextDay(checkoutServiceSeconds);
            if(!string.IsNullOrEmpty(nightNotice))next.SetMessage(nightNotice);
            // 当夜选择与结果已消费：结果在 securityHistory（已损财产记录），choice 复位，
            // 下一夜回到安全默认。
            Campaign.nightLeaveVehicle=false;Campaign.nightResult=null;
            NightHubUI.Close();CampaignMenuUI.Close();transition?.Cancel();CancelItemAction();run=next;focus="";
            GetComponent<ArtHUD>()?.SetGameplayVisible(true);CreateWorld();SaveCampaign();Publish();
        }
        private WorldSaveData CaptureWorld()
        {
            if(world==null)return null;
            var data=new WorldSaveData{hasPlayer=world.Player!=null};
            if(data.hasPlayer){data.playerPosition=world.Player.transform.position;data.playerRotation=world.Player.visual.rotation;}
            world.ShoppingMotor?.CapturePose(data);
            data.crowdJson=world.GetComponentInChildren<CustomerCrowd>()?.CaptureSaveJson();
            return data;
        }
        private void RestoreWorld(WorldSaveData data)
        {
            if(data==null||world==null)return;
            if(data.hasPlayer&&world.Player!=null)
            {world.Player.Teleport(data.playerPosition);world.Player.visual.rotation=data.playerRotation;}
            world.ShoppingMotor?.RestorePose(data);
            world.GetComponentInChildren<CustomerCrowd>()?.RestoreSaveJson(data.crowdJson);
            Physics.SyncTransforms();world.Sync(run);
        }
        public bool SaveCampaign()
        {
            if(Campaign==null||run==null||business?.Session.Setup.practice==true)
            {SaveStatus="练习不覆盖正式经营进度。";return false;}
            if(IsTransitioning){SaveStatus="正在转场，落地后会自动保存。";QueueAutoSave();return false;}
            try
            {
                // Cancelling an uncommitted reach changes no inventory. A reach
                // whose contact already committed is retained in the run state.
                CancelItemAction();world?.Sync(run);
                BusinessSaveData service=business?.CaptureForSave();
                CampaignSaveData data=new CampaignSaveData{savedUtc=DateTime.UtcNow.ToString("O"),campaign=Campaign,
                    run=run.CaptureSave(),business=service,world=CaptureWorld()};
                bool ok=CampaignSaveStore.Save(data,out string status);SaveStatus=status;return ok;
            }
            catch(Exception error){SaveStatus="未能保存："+error.Message;Debug.LogError(error);return false;}
        }
        public void SaveAndReturnToTitle()
        {
            if(!SaveCampaign())return;
            autoSavePending=false;CampaignMenuUI.Close();NightHubUI.Close();OpenCharacterCreation();
        }
        public bool ContinueCampaign()
        {
            if(!CampaignSaveStore.Load(out var data,out string status)){SaveStatus=status;return false;}
            try
            {
                // Validate the model before touching the currently shown game.
                RunState restored=RunState.Restore(data.run);
                if(data.business!=null)BusinessSession.Restore(data.business);
                restoringCampaign=true;autoSavePending=false;
                CharacterCreationUI.Dismiss();CampaignMenuUI.Close();NightHubUI.Close();SupplyShop.Close();
                transition?.Cancel();CancelItemAction();business?.Cancel();business=null;
                Campaign=data.campaign;run=restored;focus="";holdSnapshot=false;initialized=true;
                if(data.run.phase==GamePhase.Night)
                {
                    CreateWorld(GamePhase.Home);PresentNightWorld();GetComponent<ArtHUD>()?.SetGameplayVisible(false);NightHubUI.Open(this);
                }
                else if(data.business!=null)
                {
                    if(world!=null){world.gameObject.SetActive(false);world.View.enabled=false;}
                    GetComponent<ArtHUD>()?.SetGameplayVisible(false);
                    business=BusinessController.BeginRestored(data.business,OnBusinessPhase,OnBusinessCash,()=>{
                        business=null;run.ReturnToMarket();CreateWorld();GetComponent<ArtHUD>()?.SetGameplayVisible(true);QueueAutoSave();
                    },EnterNightHub);
                    if(business.Session.Finished)CommitCampaignDay();
                    run.Paused=!business.Session.Finished;
                    if(run.Paused)CampaignMenuUI.Open(this);
                }
                else
                {
                    GetComponent<ArtHUD>()?.SetGameplayVisible(true);CreateWorld();RestoreWorld(data.world);run.Paused=true;CampaignMenuUI.Open(this);
                }
                SaveStatus=status;Publish();return true;
            }
            catch(Exception error){SaveStatus="进度未能恢复："+error.Message;Debug.LogError(error);return false;}
            finally{restoringCampaign=false;}
        }
    }
}
