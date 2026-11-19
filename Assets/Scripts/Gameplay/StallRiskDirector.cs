using System;
using UnityEngine;

namespace Guochao
{
    // 事件表现抽象：Director 不直接碰 Unity 场景对象，由 COOK7I 接线层/View 实现。
    // A01 的“假扫码客”正式集成优先复用已拿到餐的真实顾客（Stage 适配器实现本接口，
    // 由 Director.spawn 回调返回）；fallback 的 Spawn 演员是暂用外观。
    // IStallRiskActor.Dispose 语义由实现方自决：自建 body 则销毁；Stage 拥有的真实
    // 顾客适配器则归还 body（View 永不调用 Dispose）。
    public interface IStallRiskActor
    {
        int OrderId { get; }          // 关联订单，定位头顶图标锚点
        UnityEngine.Transform Anchor { get; } // 头顶图标/倒计时挂点
        void OnInterceptSucceeded(int collectCents); // 追回：停住、转身、交出双倍/原价
        void OnEscaped();              // 逃单成功：快步离场
        void Tick(float dt);           // 推进自管动画/移动（退场 3s 内由 Director 驱动；Stage 拥有的真实顾客由 Stage 驱动，可实现为空）
        void Dispose();                // 退场到点/Shutdown 时由唯一所有者 Director 调用一次
    }
    // 事务回调（COOK7I 实现，Director 只调用、不碰钱）：
    //   complete(serial,collectCents) → Session.TryCompleteRiskSale，返回是否首次结算
    //   writeOff(serial)              → Session.TryWriteOffRiskSale，返回是否首次结算
    // 两个回调内部必须先走 State.MarkResolved(serial,…) 判重，再做现金事务。
    public sealed class StallRiskDirector
    {
        // 结算后短暂退场展示时长（秒）：逃单快步离场/追回转身都可见，到点才彻底清理。
        public const float ExitSeconds=3f;
        public StallRiskState State { get; }
        private readonly Func<int,int,bool> complete;
        private readonly Func<int,bool> writeOff;
        private readonly Func<StallRiskEvent,IStallRiskActor> spawnActor;
        // 单一 actor 生命周期所有权：只有 Director 持有引用并负责 Dispose。
        // actor=当前事件演员；exiting=已结算、退场展示中的演员（到点 Dispose 后置空）。
        private IStallRiskActor actor;
        private IStallRiskActor exiting;
        private float exitRemaining;
        // 结果通知（HUD 浮字/音效用，展示层订阅；不增减钱货）。
        // 结算即触发（此时 actor 开始退场，尚未销毁）——View 据此收起头顶 UI、播浮字。
        public event Action<StallRiskEvent,bool> EventSettled; // (事件, 是否追回)

        public StallRiskDirector(StallRiskState state,Func<int,int,bool> completeSale,Func<int,bool> writeOffSale,Func<StallRiskEvent,IStallRiskActor> spawn)
        {
            State=state??throw new ArgumentNullException(nameof(state));
            complete=completeSale??throw new ArgumentNullException(nameof(completeSale));
            writeOff=writeOffSale??throw new ArgumentNullException(nameof(writeOffSale));
            spawnActor=spawn??throw new ArgumentNullException(nameof(spawn));
        }
        public bool HasActiveEvent=>State.HasActiveEvent;
        public StallRiskEvent ActiveEvent=>State.ActiveEvent;
        public float ActiveRemaining=>State.HasActiveEvent?State.ActiveEvent.Remaining:0f;
        public Transform ActiveAnchor=>actor==null?null:actor.Anchor;
        // View 的 Present* 前置参考：退场中的演员不占槽，成功/失败后可连续展示第二事件。
        public bool CanPresent=>actor==null;

        // 接线层在 Session 发布 RiskSalePending 时调用（此时 State.Adopt 已发生，
        // serial/issued 已在 State 内去重）。幂等：无激活事件或 serial 已结算 → false。
        // spawn 回调返回 Stage 已有的真实顾客适配器；返回 null 时 Attach 返回 false，
        // 由 View 的 PresentExistingActor/PresentActor 走另一条展示路径（不视为失败）。
        public bool Attach(StallRiskEvent adopted)
        {
            if(adopted==null||!State.HasActiveEvent||State.ActiveEvent.Serial!=adopted.Serial||State.IsResolved(adopted.Serial))return false;
            if(actor!=null)return false; // 同屏至多一个大事件
            actor=spawnActor(adopted);
            return actor!=null;
        }
        // 每帧推进（paused 时 Controller 不调用；dt<=0/NaN 不推进）。
        // 修复：正 dt 恒推进 state 时钟（20s 轮询槽 + 25s ICD，含无激活事件时），
        // 否则结算后 ICD 永不减少、第二事件永不发生。
        public void Tick(float dt)
        {
            if(dt<=0f||float.IsNaN(dt))return;
            bool timedOut=State.Tick(dt);
            actor?.Tick(dt);
            // 退场倒计时：到点才彻底清理（pause 冻结 = 外层不调用本方法，自然冻结）。
            if(exiting!=null)
            {
                exitRemaining-=dt;
                exiting.Tick(dt);
                if(exitRemaining<=0f){exiting.Dispose();exiting=null;}
            }
            if(!timedOut)return;
            // 窗口超时 = 逃单成功：记一次坏账，恰好一次。
            var e=State.ActiveEvent;
            if(e==null||State.IsResolved(e.Serial))return;
            if(writeOff(e.Serial))EventSettled?.Invoke(e,false);
            actor?.OnEscaped();BeginExit();
        }
        // 玩家点按拦截（点击人物或拦截按钮都走这里）。成功结算恰好一次。
        public bool TryIntercept()
        {
            if(!State.HasActiveEvent)return false;
            var e=State.ActiveEvent;
            if(!complete(e.Serial,e.CollectCents))return false; // 已结算过（读档重复调用防护）
            EventSettled?.Invoke(e,true);
            actor?.OnInterceptSucceeded(e.CollectCents);BeginExit();
            return true;
        }
        // 结算与清理分离：结算即开始 3s 退场展示，actor 置空让出大事件槽；
        // 若上一轮退场尚未结束（理论上窗口期远大于此，仅防御），先彻底清掉再换。
        private void BeginExit()
        {
            if(actor==null)return;
            if(exiting!=null){exiting.Dispose();exiting=null;}
            exiting=actor;exitRemaining=ExitSeconds;actor=null;
        }
        // 关摊/销毁：彻底清理两个槽位（幂等），不结算未决事件（未决债权随存档保留）。
        public void Shutdown()
        {
            actor?.Dispose();actor=null;
            exiting?.Dispose();exiting=null;exitRemaining=0f;
        }
    }
}
