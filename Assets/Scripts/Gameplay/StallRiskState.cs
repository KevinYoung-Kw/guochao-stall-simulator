using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // A01（及后续事件的通用骨架）的一次事件实例。Director 持有它推进倒计时，
    // View 读取它做头顶图标；Rules/Director/View 都不增减正式钱货。
    public sealed class StallRiskEvent
    {
        public string EventId;        // 现仅 "A01"
        public int Serial;            // 全局事件序号（去重/审计）
        public int OrderId;           // 被采纳的 Serve 订单
        public int LaneIndex;
        public int PriceCents;        // 应收（原价）
        public int CollectCents;      // 拦截成功实收（暴躁双倍）
        public float InterceptSeconds;// 拦截窗口总长
        public float Remaining;       // 剩余窗口（pause 时冻结）
        public bool Resolved;         // 成功或失败都已一次性结算
        public bool Succeeded;
        public string RecipeId="";
    }

    // 可序列化的风险运行状态。旧档缺字段（JsonUtility 补默认 0/null）= 空状态迁移。
    // EVENT7R：新增 pollRemaining（20s 轮询槽）。旧档该字段缺省为 0 = 轮询就绪，向后兼容。
    [Serializable] public sealed class StallRiskSaveData
    {
        public const int SchemaVersion=1;
        public int schema=SchemaVersion;
        public int serial;                  // 已发出事件最大序号（去重）
        public int[] issuedSerials=Array.Empty<int>();   // 已发出（含已结算）
        public int[] resolvedSerials=Array.Empty<int>(); // 已一次性结算（成功+失败）
        public float icdRemaining;          // 全局冷却剩余（25s ICD）
        public float pollRemaining;         // 轮询槽剩余（20s 轮询；EVENT7R 新增，旧档缺省 0=就绪）
        public int randomDraws;             // 确定性随机调用计数
        public bool forceNextAdoptA01;      // 练习 fixture 位（存档保留，但不重复扣钱）
        // 未结算债权：Serve 已交付、现金未收。读档后只结算一次。
        [Serializable] public sealed class PendingEntry
        {
            public int serial,orderId,laneIndex,priceCents,collectCents;
            public float interceptSeconds,remaining;
            public string eventId="",recipeId="";
        }
        public PendingEntry[] pending=Array.Empty<PendingEntry>();
    }

    // 运行时封装。所有随机只经 Draw()（计数进存档，读档重放一致）。
    public sealed class StallRiskState
    {
        public const string Schema="guochao.stall-risk.v1";
        public int Serial { get; private set; }
        public float IcdRemaining { get; private set; }
        public float PollRemaining { get; private set; }   // EVENT7R：20s 轮询槽剩余
        public bool CanPoll=>PollRemaining<=0f;
        public bool IsGlobalIcdActive=>IcdRemaining>0f;
        public bool IsGlobalIcdReady=>IcdRemaining<=0f;
        public int RandomDraws { get; private set; }
        public bool ForceNextAdoptA01 { get; set; }
        public bool HasActiveEvent=>active!=null;
        public StallRiskEvent ActiveEvent=>active;
        private readonly SortedSet<int> issued=new SortedSet<int>(),resolved=new SortedSet<int>();
        private StallRiskEvent active;
        private readonly System.Random random;

        // EVENT7R：System.Random 接受任意 int 种子（含负数），原先拒绝负种子会作废有效存档。
        public StallRiskState(int seed)
        {
            random=new System.Random(seed==0?12017:seed); // seed==0 保持历史映射，向后兼容
        }
        public double Draw(){RandomDraws++;return random.NextDouble();}
        public void RestoreDraws(int count){for(int i=0;i<count;i++)random.NextDouble();RandomDraws=count;}

        // 开启一个 20s 轮询周期（EVENT7R：原文 S7 每 20s 轮询，独立于 25s ICD）。
        // 真实接线：CanPoll/TryBeginPoll 通过 StallRiskRules 入口调用；已开启则拒绝，不重复消耗抽取。
        public bool TryBeginPoll()
        {
            if(PollRemaining>0f)return false;
            PollRemaining=StallRiskRules.PollIntervalSeconds;
            return true;
        }

        // B05 等非债权事件共享 25s 全局冷却：只设置/延长 IcdRemaining，绝不修改
        // Serial / issued / resolved / active / RandomDraws，不污染 A01 债权与审计。
        public void StartSharedIcd()=>StartSharedIcd(StallRiskRules.IcdSeconds);
        public void StartSharedIcd(float seconds)
        {
            if(!IsFinite(seconds)||seconds<=0f)throw new ArgumentException("Shared ICD must be a finite positive number of seconds");
            if(seconds>IcdRemaining)IcdRemaining=seconds;
        }

        // 采纳一次 Serve 为风险事件。serial/issued 去重：同一 serial 永不二次发出。
        // 采纳即重置 25s ICD 与 20s 轮询槽（下一事件最早在两者都到期之后）。
        public StallRiskEvent Adopt(string eventId,int orderId,int laneIndex,string recipeId,int priceCents,float interceptSeconds,int collectCents)
        {
            if(active!=null)throw new InvalidOperationException("A risk event is already active");
            // 参数守卫必须先于任何状态推进：非有限/非正窗口、负订单/锅位 ID、
            // 负金额一律拒绝，非法输入不递增 Serial、不登记 issued、不重置
            // ICD/轮询时钟（RISK7PAYFIX）。不新增任意上限，不改正式 3/4.5s 窗口。
            if(!IsFinite(interceptSeconds)||interceptSeconds<=0f)throw new ArgumentException("Intercept window must be a finite positive number of seconds");
            if(orderId<0||laneIndex<0)throw new ArgumentException("Cannot adopt a serve with a negative order/lane id");
            if(priceCents<0||collectCents<0)throw new ArgumentException("Cannot adopt a serve with a negative amount");
            int serial=++Serial;
            issued.Add(serial);
            active=new StallRiskEvent{EventId=eventId,Serial=serial,OrderId=orderId,LaneIndex=laneIndex,RecipeId=recipeId??"",
                PriceCents=priceCents,CollectCents=collectCents,InterceptSeconds=interceptSeconds,Remaining=interceptSeconds};
            IcdRemaining=StallRiskRules.IcdSeconds;
            PollRemaining=StallRiskRules.PollIntervalSeconds;
            return active;
        }
        // 推进运行时钟：20s 轮询槽与 25s ICD 每帧照走（含无激活事件时，否则结算后
        // 第二事件永不发生——EVENT7R 修复的核心缺陷）；有激活事件时同时推进拦截窗口。
        // 防御 NaN/Infinity/负 dt：不推进（paused 由外层不调用 Tick 保证）。
        // 返回 true 表示刚超时未拦截（由 Director 走失败结算）。
        public bool Tick(float dt)
        {
            if(!IsFinite(dt)||dt<=0f)return false;
            PollRemaining=Mathf.Max(0f,PollRemaining-dt);
            IcdRemaining=Mathf.Max(0f,IcdRemaining-dt);
            if(active==null||active.Resolved)return false;
            active.Remaining-=dt;
            return active.Remaining<=0f;
        }
        public bool IsResolved(int serial)=>resolved.Contains(serial);
        // 成功/失败都一次性：重复调用返回 false，读档后不重复结算。
        // EVENT7R：只允许结算当前 Active serial，不能给任意历史 issued 补结算。
        public bool MarkResolved(int serial,bool succeeded)
        {
            if(active==null||active.Serial!=serial)return false;
            if(!issued.Contains(serial)||!resolved.Add(serial))return false;
            active.Resolved=true;active.Succeeded=succeeded;active=null;
            return true;
        }
        // ── 保存/恢复 ──
        public StallRiskSaveData Capture()
        {
            var pendingList=new List<StallRiskSaveData.PendingEntry>();
            if(active!=null&&!active.Resolved)
                pendingList.Add(new StallRiskSaveData.PendingEntry{serial=active.Serial,orderId=active.OrderId,laneIndex=active.LaneIndex,
                    priceCents=active.PriceCents,collectCents=active.CollectCents,interceptSeconds=active.InterceptSeconds,
                    remaining=Mathf.Max(0f,active.Remaining),eventId=active.EventId,recipeId=active.RecipeId});
            return new StallRiskSaveData{schema=StallRiskSaveData.SchemaVersion,serial=Serial,icdRemaining=IcdRemaining,
                pollRemaining=PollRemaining,randomDraws=RandomDraws,forceNextAdoptA01=ForceNextAdoptA01,
                issuedSerials=ToArray(issued),resolvedSerials=ToArray(resolved),pending=pendingList.ToArray()};
        }
        // 旧档缺 risk 字段：data 为 null → 全新空状态（迁移）。脏/非法值拒绝。
        public static StallRiskState Restore(StallRiskSaveData data,int seed)
        {
            var state=new StallRiskState(seed);
            if(data==null)return state; // 旧档迁移
            if(data.schema!=StallRiskSaveData.SchemaVersion)throw new ArgumentException("Unsupported stall-risk schema "+data.schema);
            // 有限 float（NaN 与 Infinity 都拒绝；注释原本就说“有限”但只验了 NaN，
            // RISK7PAYFIX 补齐）、非负计数（EVENT7R）
            if(!IsFinite(data.icdRemaining)||data.icdRemaining<0f
                ||!IsFinite(data.pollRemaining)||data.pollRemaining<0f
                ||data.serial<0||data.randomDraws<0||data.randomDraws>1000000)
                throw new ArgumentException("Invalid stall-risk counters");
            state.Serial=data.serial;state.IcdRemaining=data.icdRemaining;state.PollRemaining=data.pollRemaining;
            state.ForceNextAdoptA01=data.forceNextAdoptA01;
            foreach(int s in data.issuedSerials??Array.Empty<int>())if(s<=0||s>data.serial)throw new ArgumentException("Invalid issued serial");else state.issued.Add(s);
            foreach(int s in data.resolvedSerials??Array.Empty<int>())if(!state.issued.Contains(s))throw new ArgumentException("Resolved without issue");else state.resolved.Add(s);
            state.RestoreDraws(data.randomDraws);
            if((data.pending??Array.Empty<StallRiskSaveData.PendingEntry>()).Length>1)throw new ArgumentException("More than one pending risk event");
            if(data.pending!=null&&data.pending.Length==1)
            {
                var p=data.pending[0];
                // pending serial 必须确实在 issued 且未结算；金额非负、order/lane 有效、
                // 窗口有限（NaN/Infinity 都拒绝，RISK7PAYFIX）
                if(p==null||p.serial<=0||p.serial>data.serial||!state.issued.Contains(p.serial))throw new ArgumentException("Invalid pending risk serial");
                if(state.resolved.Contains(p.serial))throw new ArgumentException("Pending already resolved");
                if(p.priceCents<0||p.collectCents<0)throw new ArgumentException("Invalid pending risk amount");
                if(p.orderId<0||p.laneIndex<0)throw new ArgumentException("Invalid pending risk order/lane");
                if(!IsFinite(p.interceptSeconds)||!IsFinite(p.remaining)
                    ||p.interceptSeconds<=0f||p.remaining<0f||p.remaining>p.interceptSeconds+.01f)throw new ArgumentException("Invalid pending risk window");
                state.active=new StallRiskEvent{EventId=p.eventId,Serial=p.serial,OrderId=p.orderId,LaneIndex=p.laneIndex,RecipeId=p.recipeId??"",
                    PriceCents=p.priceCents,CollectCents=p.collectCents,InterceptSeconds=p.interceptSeconds,Remaining=p.remaining};
            }
            // 孤儿债权（EVENT7R）：无 pending 却存在已发出未结算 serial = 存档不一致，拒绝。
            if(data.pending==null||data.pending.Length==0)
                foreach(int s in state.issued)
                    if(!state.resolved.Contains(s))
                        throw new ArgumentException("Orphan unresolved issued risk serial "+s);
            return state;
        }
        private static int[] ToArray(SortedSet<int> set){var a=new int[set.Count];set.CopyTo(a);return a;}
        // 有限 = 既非 NaN 也非 Infinity（RISK7PAYFIX：注释与实现此前不一致）。
        private static bool IsFinite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
