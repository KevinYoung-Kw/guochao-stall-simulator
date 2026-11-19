using System;
using System.Collections.Generic;

namespace Guochao
{
    // Read-only projection of vehicle stock, optionally including the current
    // basket and a proposed shopping list. Product IDs are classified here,
    // never by the caller. Home equipment remains at home.
    public sealed class ProcurementPlanCheck
    {
        public readonly HashSet<string> Equipment=new HashSet<string>();
        public readonly Dictionary<string,int> Inventory=new Dictionary<string,int>();
        public int Coal,Gas,Cartridge,Electricity;
        public bool StoveLoaded;

        // The two item sequences remain compatible with the original advisor
        // entry point; both accept any catalog product ID, not separate types.
        // includeBasket=false is the same carried-stock projection used by
        // production BuildBusinessSetup before adding its day/scene metadata.
        public static ProcurementPlanCheck Project(RunState run,IEnumerable<string> extraItems=null,IEnumerable<string> additionalItems=null,bool includeBasket=true)
        {
            if(run==null)throw new ArgumentNullException(nameof(run));
            var check=new ProcurementPlanCheck();
            check.Equipment.UnionWith(run.LoadedEquipment);
            // Existing equipment grants no new fuel: only its remaining pool
            // is usable, and only when the corresponding hardware is aboard.
            check.Gas=check.Equipment.Contains("E03")?run.GasPoints-run.CartridgePoints:0;
            check.Cartridge=check.Equipment.Contains("B02")?run.CartridgePoints:0;
            check.Coal=check.Equipment.Contains("E01")?run.CoalPoints:0;
            check.Electricity=check.Equipment.Contains("E05")?run.ElectricityPoints:0;
            foreach(var pair in run.Supplies)check.Inventory[pair.Key]=pair.Value;
            // FREEDOM19: every real basket/cargo entry is one physical unit,
            // so identical consumables are projected per occurrence — a HashSet
            // here silently discarded repeat units and under-reported stock
            // and energy. extraItems/additionalItems are one shared
            // HYPOTHETICAL suggestion channel (callers historically pass the
            // same recommended SKU through both): within it each distinct SKU
            // is projected exactly once, never twice for being named in both
            // lists. Hardware stays unique; purchased E02/E01 refill fuel is
            // counted per real unit by AddProduct.
            if(run.Cargo!=null)foreach(string id in run.Cargo)check.AddProduct(id);
            if(includeBasket&&run.Cart!=null)foreach(string id in run.Cart)check.AddProduct(id);
            var hypothetical=new HashSet<string>();
            if(extraItems!=null)foreach(string id in extraItems)if(hypothetical.Add(id))check.AddProduct(id);
            if(additionalItems!=null)foreach(string id in additionalItems)if(hypothetical.Add(id))check.AddProduct(id);
            check.StoveLoaded=run.Profile==null?run.StoveLoaded:check.Equipment.Contains("B04");
            return check;
        }

        private void AddProduct(string id)
        {
            ItemDefinition item=GameCatalog.Find(id);
            if(item==null)throw new ArgumentException("Unknown procurement product: "+id);
            // E02 is a purchased refill, not a permanent item of equipment.
            if(id=="E02"){Cartridge+=45;return;}
            if(SupplyCatalog.IsEquipment(id))
            {
                Equipment.Add(id);
                // A real refill adds fuel even when the durable E01 container
                // was already aboard but empty. Under FREEDOM19 each basket /
                // cargo occurrence is a real unit, so fuel accrues per
                // occurrence through the caller's per-occurrence loop.
                if(id=="E01")Coal+=30;else if(id=="E03")Gas+=45;
                else if(id=="B02")Cartridge+=15;else if(id=="E05")Electricity+=45;
                return;
            }
            string key=SupplyCatalog.IngredientId(id);
            Inventory.TryGetValue(key,out int current);Inventory[key]=current+item.portions;
        }

        public BusinessSetup ToSetup(RunState run)
        {
            return new BusinessSetup{cashCents=run.CashCents,profile=run.Profile,stoveLoaded=StoveLoaded,
                coalPoints=Coal,gasPoints=Gas,cartridgePoints=Cartridge,electricityPoints=Electricity,
                equipment=new HashSet<string>(Equipment),inventory=new Dictionary<string,int>(Inventory)};
        }
        public string Missing(RunState run,int recipeIndex)=>new BusinessSession(ToSetup(run)).Missing(recipeIndex);
        public bool Valid(RunState run,int recipeIndex)=>Missing(run,recipeIndex)=="";
    }
}
