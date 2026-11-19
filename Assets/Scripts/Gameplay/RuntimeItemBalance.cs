using UnityEngine;

namespace Guochao
{
    // Explicit prototype balancing inputs where the source gives no gross mass.
    // Documented B/V/E masses always win. These are not manufacturer measurements.
    public static class RuntimeItemBalance
    {
        public static float WeightKg(string id)
        {
            var known=PackingCatalog.Find(id);if(known!=null&&known.weightKg>=0)return known.weightKg;
            switch(id)
            {
                case "E01":return 5; case "E02":return 1.2f; case "E03":return 12;
                case "E08":return 8;case "E10":return 1.8f;case "E15":return 4;
                case "RiceBucket":return 5;
                case "rice":return 6;case "meat":return 4;case "egg":return 3;
                case "dough":return 5;case "skewer":return 5;case "prepared":return 10;
                case "fruit":return 5;case "seasoning":return 6;
                case "rice_small":return 2;case "meat_small":return 1.2f;
                case "egg_small":return 1;case "seasoning_small":return 1.2f;
                // Same transport tuning as the 15-portion / 5 kg dough carton.
                case "dough_small":return 5f/3f;
            }
            if(id.StartsWith("U"))return .35f;
            if(id.StartsWith("P"))return .6f;
            if(id.StartsWith("S"))return .8f;
            return -1;
        }
        public static void Configure(PackingItem item)
        {
            string canonical=SupplyCatalog.IngredientId(item.itemId);
            if(canonical!=item.itemId)item.specification=PackingCatalog.Find(canonical);
            if(item.itemId=="RiceBucket")item.specification=new ItemPackingSpec("RiceBucket",PackingTag.Cooler,1,2,5,"实现补充：饭桶运输1×2、5kg为平衡参数，非原文编号");
        }
    }
}
