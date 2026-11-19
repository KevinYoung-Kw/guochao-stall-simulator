using System;
using System.Collections.Generic;

namespace Guochao
{
    // A read-only shopping suggestion for the six implemented recipes. The
    // real BusinessSession decides compatibility; this class only enumerates
    // products the market actually sells. No money, stock or home gear moves.
    public sealed class OpeningProcurementPlan
    {
        public string menu;
        public string[] supplies,shelves;
        public int totalCents,recipeIndex;
        public string gaps;
        public bool Affordable(RunState run)=>totalCents+(run.Paid?0:run.CartTotalCents)<=run.CashCents;

        public static OpeningProcurementPlan For(RunState run)
        {
            var carried=ProcurementPlanCheck.Project(run);
            int preferred=PreferredRecipe(run,carried);
            OpeningProcurementPlan best=null;
            for(int recipe=0;recipe<BusinessSession.Recipes.Length;recipe++)
            {
                StallRecipe definition=BusinessSession.Recipes[recipe];
                var common=EssentialConsumablesAndTools(run,carried,definition);
                if(common==null)continue;
                var bases=new List<string>{""};
                if(!definition.cold&&!definition.microwave)
                    foreach(var item in SupplyCatalog.Items)
                        if(item.id.StartsWith("B",StringComparison.Ordinal)&&CanSuggest(run,item.id))bases.Add(item.id);
                var vessels=new List<string>{""};
                foreach(string vessel in definition.vessels)if(CanSuggest(run,vessel))vessels.Add(vessel);
                var fuels=new List<string>{""};
                if(!definition.cold)foreach(string fuel in new[]{"E01","E02","E03"})if(CanSuggest(run,fuel))fuels.Add(fuel);
                var seen=new HashSet<string>();
                foreach(string bottom in bases)foreach(string vessel in vessels)foreach(string fuel in fuels)
                {
                    var buys=new List<string>(common);Add(buys,bottom);Add(buys,vessel);Add(buys,fuel);
                    buys.Sort(StringComparer.Ordinal);
                    if(!seen.Add(string.Join("|",buys)))continue;
                    var projection=ProcurementPlanCheck.Project(run,buys);
                    if(!projection.Valid(run,recipe))continue;
                    OpeningProcurementPlan candidate=Make(run,recipe,buys);
                    if(Better(run,candidate,best,preferred))best=candidate;
                }
            }
            if(best==null)
            {
                // Do not advertise incomplete purchases as a working recipe.
                // The exact real-rule gap remains visible for recovery choices.
                return new OpeningProcurementPlan{menu=BusinessSession.Recipes[preferred].name,recipeIndex=preferred,
                    supplies=Array.Empty<string>(),shelves=Array.Empty<string>(),gaps=carried.Missing(run,preferred)};
            }
            // Oil/salt is optional for opening, but a normal affordable plan
            // includes one pack to avoid the documented integrity penalty.
            // Never crowd out hard requirements or make a viable plan unaffordable.
            if(best.gaps==""&&best.Affordable(run)&&(!carried.Inventory.TryGetValue("seasoning",out int seasoning)||seasoning<=0))
            {
                string salt=Cheapest(run,item=>!SupplyCatalog.IsEquipment(item.id)&&SupplyCatalog.IngredientId(item.id)=="seasoning");
                if(salt!=null)
                {
                    var buys=new List<string>(best.supplies);buys.AddRange(best.shelves);Add(buys,salt);
                    var stocked=Make(run,best.recipeIndex,buys);
                    if(stocked.gaps==""&&stocked.Affordable(run))best=stocked;
                }
            }
            return best;
        }

        private static int PreferredRecipe(RunState run,ProcurementPlanCheck carried)
        {
            if(carried.Equipment.Contains("B07")||carried.Equipment.Contains("B08"))return 1;
            if(run.Profile?.careerId=="career5"||run.Profile?.careerId=="career6"||run.Profile?.careerId=="career8")return 5;
            if(carried.Equipment.Contains("V08"))return 3;
            return 0;
        }

        private static List<string> EssentialConsumablesAndTools(RunState run,ProcurementPlanCheck carried,StallRecipe recipe)
        {
            var buys=new List<string>();
            bool hasTool=false;foreach(string tool in recipe.tools)hasTool|=carried.Equipment.Contains(tool);
            if(!hasTool)
            {
                string tool=Cheapest(run,item=>Array.IndexOf(recipe.tools,item.id)>=0);
                if(tool==null)return null;Add(buys,tool);
            }
            // The production fried-skewer recipe also explicitly requires U10
            // in Missing(), in addition to its choice of strainer.
            if(recipe.id=="fried_skewer"&&!carried.Equipment.Contains("U10"))
            {if(!CanSuggest(run,"U10"))return null;Add(buys,"U10");}
            foreach(var ingredient in recipe.ingredients)
            {
                if(SeasoningShortageRules.IsBaseSeasoning(ingredient.Key))continue;
                carried.Inventory.TryGetValue(ingredient.Key,out int quantity);
                if(quantity>=ingredient.Value)continue;
                string product=Cheapest(run,item=>!SupplyCatalog.IsEquipment(item.id)&&
                    SupplyCatalog.IngredientId(item.id)==ingredient.Key&&item.portions+quantity>=ingredient.Value);
                if(product==null)return null;Add(buys,product);
            }
            bool hasPackage=false;
            foreach(string package in recipe.packages)
                if(carried.Inventory.TryGetValue(package,out int quantity)&&quantity>0)hasPackage=true;
            if(!hasPackage)
            {
                string package=Cheapest(run,item=>Array.IndexOf(recipe.packages,item.id)>=0);
                if(package==null)return null;Add(buys,package);
            }
            return buys;
        }

        private static bool CanSuggest(RunState run,string id)
        {
            // Find may expose metadata for old-goods-only products (e.g. U01).
            // Suggestions may only use actual retail cards or physical shelves.
            if(!Array.Exists(SupplyCatalog.Items,item=>item.id==id)&&!Array.Exists(GameCatalog.Items,item=>item.id==id))return false;
            if(run.Cart.Contains(id)||run.Cargo.Contains(id))return false;
            bool emptyCoal=id=="E01"&&run.CoalPoints==0;
            // The actual purchase path rejects duplicate equipment at home or
            // aboard. Empty E01 is the explicit refill exception in RunState.
            if(run.HomeEquipment.Contains(id)&&!emptyCoal)return false;
            if(run.LoadedEquipment.Contains(id)&&!emptyCoal)return false;
            return true;
        }

        private static string Cheapest(RunState run,Predicate<ItemDefinition> accepts)
        {
            string best=null;int cost=int.MaxValue;
            void Consider(ItemDefinition item)
            {
                if(!accepts(item)||!CanSuggest(run,item.id))return;
                int price=run.PriceCents(item.id);
                if(price<cost){best=item.id;cost=price;}
            }
            foreach(var item in SupplyCatalog.Items)Consider(item);
            foreach(var item in GameCatalog.Items)Consider(item);
            return best;
        }
        private static void Add(List<string> buys,string id)
        {if(!string.IsNullOrEmpty(id)&&!buys.Contains(id))buys.Add(id);}

        private static OpeningProcurementPlan Make(RunState run,int recipe,List<string> buys)
        {
            var supplies=new List<string>();var shelves=new List<string>();int cost=0;
            foreach(string id in buys)
            {
                if(SupplyCatalog.Find(id)!=null)supplies.Add(id);else shelves.Add(id);
                cost+=run.PriceCents(id);
            }
            var pending=new List<string>(run.Cart);pending.AddRange(buys);
            string gap=ShoppingBasketLayout.CanFit(pending,run.CartCapacity)?"":"购物车空间不足，请先装车或放回部分商品";
            // LoadValidator updates the live VehicleLoadout plan and may create
            // measured model prototypes. A read-only advisor must not call it;
            // checkout/loading still perform that real physical validation.
            return new OpeningProcurementPlan{menu=BusinessSession.Recipes[recipe].name,recipeIndex=recipe,
                supplies=supplies.ToArray(),shelves=shelves.ToArray(),totalCents=cost,gaps=gap};
        }
        private static bool Better(RunState run,OpeningProcurementPlan candidate,OpeningProcurementPlan current,int preferred)
        {
            if(current==null)return true;
            int Rank(OpeningProcurementPlan plan)=>plan.gaps!=""?2:plan.Affordable(run)?0:1;
            int rank=Rank(candidate),oldRank=Rank(current);
            if(rank!=oldRank)return rank<oldRank;
            if(candidate.totalCents!=current.totalCents)return candidate.totalCents<current.totalCents;
            if((candidate.recipeIndex==preferred)!=(current.recipeIndex==preferred))return candidate.recipeIndex==preferred;
            int count=candidate.supplies.Length+candidate.shelves.Length,oldCount=current.supplies.Length+current.shelves.Length;
            return count<oldCount;
        }
    }
}
