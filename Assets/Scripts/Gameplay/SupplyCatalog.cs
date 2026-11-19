using System;
using UnityEngine;

namespace Guochao
{
    public static class SupplyCatalog
    {
        static ItemDefinition D(string id,string name,int price,int amount=1)=>new ItemDefinition(id,name,amount,price,"SupplyCounter",new Color(.60f,.57f,.36f));
        public static readonly ItemDefinition[] Items={
            D("B01","蜂窝煤泥炉",3500),D("B02","便携卡式炉",6500),D("B03","单眼猛火灶",16000),
            D("V01","二手薄铁炒锅",3500),D("V02","章丘锻打深口铁锅",14000),D("V05","圆形平底鏊",11000),D("V08","砂锅三联架",7500),D("V09","深口高汤桶",8500),D("V13","单缸深炸锅",9500),D("V15","手打茶饮冰槽台",13000),
            D("U01","起毛旧木铲",500),D("U02","熟铁锻打大弯铲",3500),D("U04","竹蜻蜓刮板",1200),D("U06","刷酱笔",1000),D("U10","食品长夹",1800),D("U08","粗孔铁丝笊篱",1500),D("U11","普通塑料短柄勺",600),D("U14","料理剪",1500),D("U12","长柄深汤勺",3800),D("U13","柠檬暴打棒",5500),
            D("RiceBucket","保温饭桶",4500),D("E01","蜂窝煤块箱·30热能",2000),D("E03","煤气罐·45热能",8000),D("E02","卡式丁烷气瓶组·3罐45热能",2500),
            // Packaging singles/50-packs. P02/P04/P05/P03/P07/P08 were already here;
            // P06/P09/P10/P11/P12 fill every package alias the six base recipes and
            // the content library (CharacterCatalog equipment table) reference, at
            // the exact authored prices (¥30/38/45/68/70 per 50-pack), so no recipe
            // package option points at goods the market can never sell.
            D("P02","发泡饭盒·50个",1200,50),D("P04","牛皮纸袋·50个",1000,50),D("P05","PP饭盒·50个",2500,50),D("P03","竹签纸托·50个",900,50),D("P07","防烫汤碗·50个",2800,50),D("P08","封口杯·50个",2200,50),
            D("P06","分格大餐盒·50个",3000,50),D("P09","国潮腰封纸袋·50个",3800,50),D("P10","青花瓷纸瓦罐·50个",4500,50),D("P11","竹编食盒·50个",6800,50),D("P12","毛竹筒杯·50个",7000,50),
            // FLAVOR11: three authored flavor seasonings from the content library
            // seasoning table (《道具、菜谱与动态事件内容库》5.1 B 类):
            // S01 街头辣椒油 ¥0.2/份×40, S02 现剁蒜蓉 ¥0.3/份×30, S05 番茄甜辣酱
            // ¥0.2/份×40. The bottle price is exactly the authored unit price
            // times the authored portion count (¥8 / ¥9 / ¥8) — no markup.
            // They sit in their own counter category 调味酱料 and stay fully
            // independent of the S00 通用油盐酱料大桶 (seasoning), which keeps
            // its own soft-gate shortage rule.
            D("S01","街头辣椒油",800,40),D("S02","现剁蒜蓉",900,30),D("S05","番茄甜辣酱",800,40),
            // Bulk loose goods mirror the GameCatalog shelf cartons at the same
            // 1.11x per-portion premium the five existing *_small bags already use
            // (rice 90→100, egg 135→150, meat 315→350, dough 108→120, seasoning
            // 45→50 per portion). They exist so one counter basket can cover a
            // complete recipe set without a forced second trip to the physical
            // shelves; the full-size shelf cartons remain the cheaper per-portion
            // route. Sources: carton prices GameCatalog.cs; authored portion prices
            // CharacterCatalog.json (prepared 270/份, fruit≈198/份, skewer 162/份).
            D("rice_small","散称米面·5份",500,5),D("egg_small","散称蛋菜·4份",600,4),D("meat_small","散称鲜肉·3份",1050,3),D("seasoning_small","散装油盐·10份",500,10),D("dough_small","散称饼胚·5份",600,5),
            D("skewer_small","散称串串半成品·5份",900,5),D("prepared_small","散称料理包·3份",900,3),D("fruit_small","散称果料·5份",1100,5),
            // SHOP7B: E13 fills the authored hardware-zone gap. Content library
            // equipment table row E13: 高音收款大喇叭, 车把挂件槽, 商超五金区（¥60）.
            // Price ¥6000 cents is the authored ¥60 exactly — no invented markup
            // or discount. Only the ownership path is wired here (counter →
            // payment → load → campaign equipment → BusinessSetup.equipment);
            // the authored 震慑假扫码 / 20:00 低音 effects have no system in this
            // build, so no fake immunity or volume switch is granted anywhere.
            D("E13","高音收款大喇叭",6000)
        };
        // Night-online-only goods. They resolve through Find so the campaign
        // stock/commit paths can price and portion them, but they are NOT in
        // Items, hence never offered at the physical supply counter.
        static readonly ItemDefinition[] OnlineOnly={
            D("V03","双耳工地大生铁锅",19000),D("U03","耐高温硅胶防粘铲",2800)};

        // BILL9 — the market has two separate purchase channels and the
        // rejection text must name the right one:
        //   "counter" 账单直购： SupplyCatalog.Items, picked into the cart at the
        //             SupplyCounter and paid once at the human cashier.
        //   "shelf"   货架整箱： GameCatalog.Items, physically carried from the
        //             shelf anchor; never orderable through the bill/counter.
        //   "night"   夜间专用： resolvable (pricing/save) but only sold by the
        //             night online shop; never at the counter or the shelves.
        public static ItemDefinition Find(string id)=>Array.Find(Items,x=>x.id==id)??Array.Find(OnlineOnly,x=>x.id==id)??RecoveryRules.UsedDefinition(id);
        public static bool IsCounterItem(string id)=>Array.Find(Items,x=>x.id==id)!=null;
        public static bool IsShelfCarton(string id)=>Array.Exists(GameCatalog.Items,item=>item.id==id);
        // BILL9R: night-online membership comes from the exact OnlineOnly table
        // or the night online catalogue (OnlineCatalog.FindSku) — never from
        // Find's RecoveryRules.UsedDefinition fallback, or a second-hand market
        // item would smuggle into the night channel. V03/U03 stay night; Find
        // itself is untouched so save/commit pricing keeps its used fallback.
        public static bool IsOnlineOnly(string id)=>!IsCounterItem(id)&&!IsShelfCarton(id)&&(Array.Find(OnlineOnly,x=>x.id==id)!=null||OnlineCatalog.FindSku(id)!=null);
        public static string Channel(string id)=>IsCounterItem(id)?"counter":IsShelfCarton(id)?"shelf":IsOnlineOnly(id)?"night":"unknown";
        public static bool IsEquipment(string id)=>id=="RiceBucket"||id.Length==3&&(id[0]=='B'||id[0]=='V'||id[0]=='U'||id[0]=='E');
        public static string IngredientId(string id)=>id.EndsWith("_small")?id.Substring(0,id.Length-6):id;
        // Counter bill categories (BILL9 精选分类): 补小份 / 包材 / 五金工具 /
        // 灶具锅具 / 能源 / 调味酱料. E13 is an authored hardware-zone handlebar
        // accessory (五金挂件) and shelves under 五金工具. The three S-seasoning
        // bottles (S01/S02/S05) take the new 调味酱料 tab; shelf cartons and
        // online-only ids have no counter category.
        public static string Category(string id)
        {
            if(id.EndsWith("_small"))return "补小份";
            if(id.Length==3&&id[0]=='P')return "包材";
            if(id.Length==3&&id[0]=='U'||id=="E13")return "五金工具";
            if(id.Length==3&&(id[0]=='B'||id[0]=='V')||id=="RiceBucket")return "灶具锅具";
            if(id.Length==3&&id[0]=='E')return "能源";
            if(id.Length==3&&id[0]=='S')return "调味酱料";
            return "";
        }
    }
}
