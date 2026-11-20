namespace Guochao
{
    // FLAVOR11 风味加料静态菜单（原文《道具、菜谱与动态事件内容库》B 表原味）：
    // S01 街头辣椒油、S02 现剁蒜蓉、S05 番茄甜辣酱、S13 黑糖珍珠、S14 椰果；原 seasoning 油盐基底保留不动。
    // 库存口径：Inventory[flavorId] = 剩余份数；整瓶采购（40/30/40/30/30 份、¥8/¥9/¥8/¥12/¥9）
    // 由采购工位接线，本模块只做兼容判定、加价与递餐一次性扣量，不经手钱货采购。
    public sealed class StallFlavor
    {
        public string id,name;
        // 兼容的菜系（与 StallRecipe.family 全名匹配；静态菜单按完整 recipe family 判定）
        public string[] families;
        public int bottlePriceCents;      // 整瓶进价（采购侧参考）
        public int portionsPerBottle;     // 每瓶可用份数
        public int markupCentsPerPortion; // 每份实际加出去的加价
    }
    public static class StallFlavorMenu
    {
        public static readonly StallFlavor[] Flavors={
            new StallFlavor{id="S01",name="街头辣椒油",families=new[]{"猛火现炒","煎烙面点","汤水卤煮","街头油炸"},bottlePriceCents=800,portionsPerBottle=40,markupCentsPerPortion=100},
            new StallFlavor{id="S02",name="现剁蒜蓉",families=new[]{"猛火现炒","煎烙面点","街头油炸"},bottlePriceCents=900,portionsPerBottle=30,markupCentsPerPortion=100},
            new StallFlavor{id="S05",name="番茄甜辣酱",families=new[]{"煎烙面点","街头油炸"},bottlePriceCents=800,portionsPerBottle=40,markupCentsPerPortion=100},
            // S13/S14（破产奶茶店老板职业初始物件）：仅适配茶饮甜水；
            // 整瓶进价= authored 单价×份数（0.4×30=¥12 → 1200 分；0.3×30=¥9 → 900 分），建议加价 ¥2/份。
            new StallFlavor{id="S13",name="黑糖珍珠",families=new[]{"茶饮甜水"},bottlePriceCents=1200,portionsPerBottle=30,markupCentsPerPortion=200},
            new StallFlavor{id="S14",name="椰果",families=new[]{"茶饮甜水"},bottlePriceCents=900,portionsPerBottle=30,markupCentsPerPortion=200}};
        public static StallFlavor Find(string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            foreach(StallFlavor flavor in Flavors)if(flavor.id==id)return flavor;
            return null;
        }
        public static bool IsFlavor(string id)=>Find(id)!=null;
        public static int MarkupFor(string id){StallFlavor flavor=Find(id);return flavor==null?0:flavor.markupCentsPerPortion;}
        // 是否配菜型：按完整 recipe family 判定（茶饮甜水等未列菜系一律不兼容）。
        public static bool Compatible(StallRecipe recipe,string flavorId)
        {
            StallFlavor flavor=Find(flavorId);if(flavor==null||recipe==null)return false;
            foreach(string family in flavor.families)if(recipe.family==family)return true;
            return false;
        }
    }
}
