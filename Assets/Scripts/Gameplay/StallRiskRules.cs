using System;

namespace Guochao
{
    // EVENT7：A01 假扫码逃单客 的规则层（纯静态、可单测、不持有 Unity 场景状态）。
    //
    // 原文依据（不得改写）：
    //  · 内容库 4.2 A01：假滴一声扫码伸手溜走 → Tap 点按，3.0s 内拍手追回；
    //    暴躁特质吓出双倍；装【E13 高音收款大喇叭 / 智能自助点餐收款屏】静默免疫。
    //  · 天赋树总表：career1「纯正街溜子」自带【街头老油条】
    //    “逃单、熊孩子拦截窗口 +1.5s”；特质【暴躁脾气】“拍打逃单/赖账客对方付双倍”。
    //  · 设定文档 S7：四指标（Exposure/Integrity/Hype/Risk）+ 20s 轮询、全局 ICD 25s。
    //
    // 本批范围说明：当前工程没有四指标体系（GP-054 缺失）。A01 属 Risk 维度，
    // 在缺少 Risk 指标的情况下采用保守、可解释的最小触发策略（见
    // ShouldAdoptServe），后续接入完整指标后只改这一个函数。
    public static class StallRiskRules
    {
        public const string EventA01="A01";
        public const string EventA01Name="假扫码逃单客";
        // S7 调度常量
        public const float PollIntervalSeconds=20f;   // 轮询周期：每 20s 至多一次确定性抽取
        public const float IcdSeconds=25f;            // 全局 ICD：结算后 25s 内不再发事件
        // A01 响应窗口（秒）
        public const float BaseInterceptSeconds=3f;
        // 原文“智能自助点餐收款屏”（老王车行 ¥2500，免疫逃单/赊账/砍价）。
        // 该道具在现行 Equipment 目录尚无资产，冻结一个保留 ID；COOK7I/道具任务
        // 实现该车行件时必须使用此 ID，Rules 已按此判定免疫。
        public const string SelfServeKioskEquipmentId="E22";
        // E13 高音收款大喇叭：Equipment 目录已存在（CharacterCatalog.json E13）。
        public const string LoudspeakerEquipmentId="E13";
        // 暴躁特质（CharacterCatalog.json traits id="暴躁脾气"）。
        public const string IrritableTraitId="暴躁脾气";
        // 街头老油条拦截窗口加成由 career1「纯正街溜子」先天自带
        // （CharacterCatalog.json careers career1.innate 明确写出 +1.5s）。
        public const string StreetwiseCareerId="career1";
        public const float StreetwiseBonusSeconds=1.5f;

        public static bool IsImmune(System.Collections.Generic.IEnumerable<string> equipment)
        {
            foreach(string id in equipment)
                if(id==LoudspeakerEquipmentId||id==SelfServeKioskEquipmentId)return true;
            return false;
        }
        public static bool IsImmune(BusinessSetup setup)
        {
            if(setup==null)return false;
            return IsImmune(setup.equipment);
        }
        public static bool HasStreetwise(BusinessSetup setup)
            =>setup!=null&&setup.profile!=null&&setup.profile.careerId==StreetwiseCareerId;
        public static bool HasIrritable(BusinessSetup setup)
            =>setup!=null&&setup.profile!=null&&setup.profile.Has(IrritableTraitId);

        // A01 拦截窗口：3.0s 基础；街头老油条 +1.5s；免疫道具时无窗口（不触发事件）。
        public static float ComputeInterceptSeconds(BusinessSetup setup)
        {
            if(IsImmune(setup))return 0f;
            float seconds=BaseInterceptSeconds;
            if(HasStreetwise(setup))seconds+=StreetwiseBonusSeconds;
            return seconds;
        }
        // 拦截成功实收金额：暴躁脾气吓出双倍（原文），其余原价。
        public static int ComputeCollectCents(BusinessSetup setup,int priceCents)
            =>HasIrritable(setup)?priceCents*2:priceCents;

        // ── EVENT7R：20s 轮询闸门（补原文 S7“每 20s 轮询”缺失的时钟）──
        // 轮询时钟存于 StallRiskState.PollRemaining（随存档往返，旧档缺省 0 = 就绪）。
        // 真实接线推荐形态（不在每个 Serve 上乱抽）：
        //   if(!StallRiskRules.TryBeginPoll(riskState)) return null;   // 不在 20s 轮询窗口 → 不 Draw、不产生事件
        //   double draw=riskState.Draw();                              // 只有真正轮询才计数，读档可续放
        //   if(!StallRiskRules.ShouldAdoptServe(setup,riskState,session.Served,draw)) return null;
        //   return new StallRiskGateDecision{ … };
        // 采纳成功后 state.Adopt 会同时重置轮询槽与 ICD，下一窗口 = max(20s 轮询, 25s ICD)。
        public static bool CanPoll(StallRiskState state)=>state!=null&&state.CanPoll;
        public static bool TryBeginPoll(StallRiskState state)=>state!=null&&state.TryBeginPoll();

        // ── 保守的最小触发策略（无四指标体系的替代，只此一处，后续接入 Risk 指标后替换）──
        // 纯概率检查（EVENT7R 拆分）：不读写 State、不消耗轮询槽，连续调用互不污染，
        // 供测试与外层闸门组合使用。免疫/首单保护在此保留。
        // 命中概率按营业阶段爬坡：第 1~3 单后 0，第 4~9 单 0.10，第 10 单起 0.15。
        public static bool ShouldAdoptServe(BusinessSetup setup,int servedCount,double draw)
        {
            if(setup==null||setup.practice)return false; // 练习用显式 fixture
            if(IsImmune(setup))return false;
            if(servedCount<1)return false;               // 首单先完成正常交易
            if(draw<0||draw>=1)return false;
            double chance=servedCount<4?0:servedCount<10?.10:.15;
            return draw<chance;
        }
        // 带运行状态闸门的完整入口（向后兼容原签名）。不含轮询消耗——
        // 20s 轮询由外层 TryBeginPoll 消耗，25s ICD 与激活事件在此检查。
        // 返回 true 表示本次 Serve 应被采纳为 A01 风险收款。
        public static bool ShouldAdoptServe(BusinessSetup setup,StallRiskState state,int servedCount,double draw)
        {
            if(state==null)return false;
            if(state.HasActiveEvent||state.IcdRemaining>0f)return false;
            return ShouldAdoptServe(setup,servedCount,draw);
        }
        // 练习/测试显式 fixture：置位“下一次 Serve 采纳为 A01”，不只有调试按键入口。
        // 采纳时由接线层调用 state.Adopt(...) 并传入实际售价计算的 collectCents。
        public static void ForceAdoptNextServe(StallRiskState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            state.ForceNextAdoptA01=true;
        }
    }
}
