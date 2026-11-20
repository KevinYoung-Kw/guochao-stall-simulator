using System;
using UnityEngine;

namespace Guochao
{
    // B05 共享单车堵摊的只读视图快照，供外层 View/Controller 查询展示与点击判定，
    // 不携带任何场景节点或 UI 实现。
    public struct BikeBlockViewState
    {
        public string eventId;
        public bool active;
        public bool triggeredToday;
        public bool canClear;
        public int remainingBikes;
        public int totalBikes;
        public float arrivalRateMultiplier;
        public float pollRemainingSeconds;
        public float globalIcdRemainingSeconds;
    }

    public sealed partial class BusinessSession
    {
        public const string BikeBlockEventId="B05";
        public const int BikeBlockInitialCount=2;
        public const float BikeBlockPollIntervalSeconds=20f;
        public const float BikeBlockTriggerChance=0.20f;
        public const float BikeBlockArrivalRateMultiplier=0.5f;

        private bool bikeBlockActive;
        private bool bikeBlockTriggered;
        private int bikeBlockRemaining;
        private int bikeBlockMask;
        private float bikeBlockPollRemaining=BikeBlockPollIntervalSeconds;

        // 外层视图只读查询 API
        public bool HasActiveBikeBlock=>bikeBlockActive&&bikeBlockRemaining>0;
        public bool BikeBlocked=>HasActiveBikeBlock;
        // Each bit identifies a physical bike. An already removed bike cannot
        // be clicked again to make its neighbour disappear.
        public int BikeVisibleMask=>HasActiveBikeBlock?bikeBlockMask:0;
        public int BikeBlockRemaining=>HasActiveBikeBlock?bikeBlockRemaining:0;
        public int BikeBlockClearedClicks=>HasActiveBikeBlock?BikeBlockInitialCount-bikeBlockRemaining:0;
        public bool BikeBlockTriggeredToday=>bikeBlockTriggered;
        public float BikeBlockPollRemaining=>bikeBlockPollRemaining;
        public float CustomerArrivalRateMultiplier=>HasActiveBikeBlock?BikeBlockArrivalRateMultiplier:1f;
        public bool CanClearBikeBlock=>Started&&!Finished&&HasActiveBikeBlock;

        // 纯通知事件（与 SaleCompleted / BatchBurned 同原则：账本/状态与 Revision 先落定，
        // 逐订阅者隔离异常，绝不破坏主流程）。
        public event Action<int> BikeBlockTriggered;
        public event Action<int> BikeBlockProgressed;
        // clearedByClosing: false = 玩家点击 2 次清除；true = 营业结束自动清除。
        public event Action<bool> BikeBlockCleared;

        public BikeBlockViewState GetBikeBlockState()=>new BikeBlockViewState
        {
            eventId=BikeBlockEventId,
            active=HasActiveBikeBlock,
            triggeredToday=bikeBlockTriggered,
            canClear=CanClearBikeBlock,
            remainingBikes=BikeBlockRemaining,
            totalBikes=BikeBlockInitialCount,
            arrivalRateMultiplier=CustomerArrivalRateMultiplier,
            pollRemainingSeconds=bikeBlockPollRemaining,
            globalIcdRemainingSeconds=RiskState!=null?RiskState.IcdRemaining:0f
        };

        public string DescribeBikeBlock()
        {
            if(!HasActiveBikeBlock)return "";
            return "摊前有 "+bikeBlockRemaining+" 台共享单车阻拦（客流减半），点击挪开。";
        }

        // 玩家点击清除单车：每次清除 1 台，共点击 2 次完全清除。
        // 不扣钱、不损货；非法时机（未开摊/已收摊/当前未堵摊）为纯空操作。
        public bool TryMoveBike(int bikeIndex)
        {
            if(!CanClearBikeBlock||bikeIndex<0||bikeIndex>=BikeBlockInitialCount)return false;
            int bit=1<<bikeIndex;
            if((bikeBlockMask&bit)==0)return false;
            bikeBlockMask&=~bit;
            bikeBlockRemaining--;
            if(bikeBlockRemaining>0)
            {
                Message="已挪开 1 台共享单车，还剩 "+bikeBlockRemaining+" 台挡在摊前，再点一次清除。";
                Revision++;
                PublishBikeBlockProgressed(bikeBlockRemaining);
                return true;
            }
            bikeBlockRemaining=0;
            bikeBlockActive=false;
            Message="摊前共享单车已全部挪开，客流恢复正常。";
            Revision++;
            PublishBikeBlockCleared(false);
            return true;
        }

        private void ResetBikeBlockForOpen()
        {
            bikeBlockActive=false;
            bikeBlockTriggered=false;
            bikeBlockRemaining=0;
            bikeBlockMask=0;
            bikeBlockPollRemaining=BikeBlockPollIntervalSeconds;
        }

        // 仅在 BusinessSession.Tick(dt) 中推进（暂停时不调用 Tick 即天然冻结）。
        // 规则：
        // 1) 仅在营业可接单、未临近关摊、首次成交（Served>0）之后、且当日未触发过时计时；
        // 2) 每 20 秒检查一次，不每帧抽签；
        // 3) 若 A01 激活或 25s 全局 ICD 仍在冷却，则保持轮询就绪（0s）且不消耗 RandomValue；
        // 4) 仅在全部前置条件满足时调用一次现有 RandomValue()，绝不按自然客流间隔增加额外抽签。
        private void TickBikeBlock(float dt)
        {
            if(!Finite(dt)||dt<=0f)return;
            if(!AcceptingOrders||Remaining<=.0001f||Served<=0||bikeBlockTriggered||bikeBlockActive)return;
            bikeBlockPollRemaining=Mathf.Max(0f,bikeBlockPollRemaining-dt);
            if(bikeBlockPollRemaining>0f)return;
            if(IsRiskBlockedForBikeBlock())return;
            bikeBlockPollRemaining=BikeBlockPollIntervalSeconds;
            if(RandomValue()<BikeBlockTriggerChance)
                ActivateBikeBlock();
        }

        private bool IsRiskBlockedForBikeBlock()
            =>RiskState!=null&&(RiskState.HasActiveEvent||RiskState.IsGlobalIcdActive);

        private void ActivateBikeBlock()
        {
            bikeBlockActive=true;
            bikeBlockTriggered=true;
            bikeBlockRemaining=BikeBlockInitialCount;
            bikeBlockMask=(1<<BikeBlockInitialCount)-1;
            if(RiskState!=null)RiskState.StartSharedIcd();
            Message="2 台共享单车停在摊前挡路，客流减半！点击 2 次挪开单车。";
            Revision++;
            PublishBikeBlockTriggered(bikeBlockRemaining);
        }

        // Close() 专用：先同步清理状态（不扣钱、不损货、不动 A01 债权/审计），
        // 返回原先是否处于堵摊状态，由 Close() 在 Revision++ 完成后统一发事件。
        private bool ClearBikeBlockStateOnly()
        {
            if(!bikeBlockActive&&bikeBlockRemaining==0)return false;
            bikeBlockActive=false;
            bikeBlockRemaining=0;
            bikeBlockMask=0;
            return true;
        }

        private void RestoreBikeBlockSave(BusinessSaveData data)
        {
            if(data.bikeBlockVersion==0)
            {
                bikeBlockActive=false;
                bikeBlockTriggered=false;
                bikeBlockRemaining=0;
                bikeBlockMask=0;
                bikeBlockPollRemaining=BikeBlockPollIntervalSeconds;
                return;
            }
            bikeBlockActive=data.bikeBlockActive;
            bikeBlockTriggered=data.bikeBlockTriggered;
            bikeBlockRemaining=data.bikeBlockRemaining;
            bikeBlockMask=data.bikeBlockMask;
            bikeBlockPollRemaining=data.bikeBlockPollRemaining;
        }

        private static void ValidateBikeBlockSave(BusinessSaveData d)
        {
            if(d.bikeBlockVersion!=0&&d.bikeBlockVersion!=BusinessSaveData.BikeBlockVersion1)
                throw new ArgumentException("Unsupported bike block version");
            if(!Finite(d.bikeBlockPollRemaining)||d.bikeBlockPollRemaining<0f||d.bikeBlockPollRemaining>BikeBlockPollIntervalSeconds+.01f)
                throw new ArgumentException("Invalid saved bike block poll timer");
            if(d.bikeBlockRemaining<0||d.bikeBlockRemaining>BikeBlockInitialCount)
                throw new ArgumentException("Invalid saved bike block count");
            if(d.bikeBlockMask<0||d.bikeBlockMask>3||
                ((d.bikeBlockMask&1)!=0?1:0)+((d.bikeBlockMask&2)!=0?1:0)!=d.bikeBlockRemaining)
                throw new ArgumentException("Invalid saved bike block mask");
            if(d.bikeBlockVersion==0)
            {
                if(d.bikeBlockActive||d.bikeBlockTriggered||d.bikeBlockRemaining!=0||d.bikeBlockPollRemaining!=0f)
                    throw new ArgumentException("Legacy save carries bike block state without version");
                return;
            }
            if(d.bikeBlockActive)
            {
                if(!d.started||d.finished)throw new ArgumentException("Saved bike block is active outside open business");
                if(!d.bikeBlockTriggered||d.bikeBlockRemaining<1)throw new ArgumentException("Saved active bike block is inconsistent");
                if(d.served<=0)throw new ArgumentException("Saved active bike block precedes first sale");
                if(d.risk!=null&&d.risk.pending!=null&&d.risk.pending.Length>0)throw new ArgumentException("Saved bike block overlaps an active risk event");
            }
            else if(d.bikeBlockRemaining!=0)
            {
                throw new ArgumentException("Inactive bike block carries remaining bikes");
            }
            if(d.bikeBlockTriggered&&(!d.started||d.served<=0))
                throw new ArgumentException("Saved bike block trigger precedes open business or first sale");
        }

        private void PublishBikeBlockTriggered(int remainingBikes)
        {
            if(BikeBlockTriggered==null)return;
            foreach(Action<int> callback in BikeBlockTriggered.GetInvocationList())
                try{callback(remainingBikes);}
                catch(Exception error){Debug.LogError("BikeBlockTriggered subscriber failed (state already settled): "+error);}
        }

        private void PublishBikeBlockProgressed(int remainingBikes)
        {
            if(BikeBlockProgressed==null)return;
            foreach(Action<int> callback in BikeBlockProgressed.GetInvocationList())
                try{callback(remainingBikes);}
                catch(Exception error){Debug.LogError("BikeBlockProgressed subscriber failed (state already settled): "+error);}
        }

        private void PublishBikeBlockCleared(bool clearedByClosing)
        {
            if(BikeBlockCleared==null)return;
            foreach(Action<bool> callback in BikeBlockCleared.GetInvocationList())
                try{callback(clearedByClosing);}
                catch(Exception error){Debug.LogError("BikeBlockCleared subscriber failed (state already settled): "+error);}
        }
    }
}
