namespace Guochao
{
    // S00 基础调料规则（原文《道具、菜谱与动态事件内容库》5.1 A 类 /
    // 《设定文档》S4）：通用油盐酱料不加价、每单必耗，每单自动扣 1 份；
    // 缺货时全部菜品味效 -10、诚信度 -10。适用全部菜品，与配方主料耗用
    // 分离；lane.seasoned 仍只表示“调味工序已完成”这一工序状态。
    // 原文没有味效到售价的唯一映射，本批只记录事实，不改动基础价。
    public static class SeasoningShortageRules
    {
        public const string BaseSeasoningId="seasoning";
        public const int PortionsPerOrder=1;
        public const int FlavorEffectPerShortPortion=-10;
        public const int IntegrityDeltaPerShortPortion=-10;
        public static bool IsBaseSeasoning(string id)=>id==BaseSeasoningId;
        public static string PortionWarning=>"缺油盐酱料：仍可出餐，本份诚信 -10";
    }
}
