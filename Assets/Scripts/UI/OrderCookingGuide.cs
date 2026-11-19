using System;
using System.Collections.Generic;
using System.Linq;

namespace Guochao
{
    // Read-only order guidance. Generic food is described from its real inputs,
    // never from visualRecipeIndex (which is only a mesh adapter).
    public static class OrderCookingGuide
    {
        public sealed class Ingredient
        {
            public string id;
            public int units,available,inPan;
            public bool alreadyPrepared;
        }
        public sealed class Readout
        {
            public string title="",method="",steps="",equipment="",packaging="",flavor="",next="";
            public int recipeIndex=-1;
            public bool hasExactInputs,ready;
            public readonly List<Ingredient> ingredients=new List<Ingredient>();
        }
        public static string MethodLabel(string id)
        {
            switch(id){case "stir_fry":return "炒";case "griddle":return "煎";case "boil":return "煮";case "fry":return "炸";case "reheat":return "复热";case "cold_mix":return "冷调";case "muddle":return "手锤";default:return "做法";}
        }
        public static string RecipeMethod(int index)
        {
            if(index<0||index>=BusinessSession.Recipes.Length)return "";
            StallRecipe recipe=BusinessSession.Recipes[index];
            if(recipe.id=="lemon_tea")return "muddle";
            if(recipe.cold)return "cold_mix";
            if(recipe.microwave)return "reheat";
            foreach(var method in FreeKitchenRules.Methods)if(method.family==recipe.family)return method.id;
            return "";
        }
        static bool Matches(StallOrder order,FoodProduct product)
        {
            if(product==null)return false;
            if(!string.IsNullOrEmpty(order.productId))
                return product.id==order.productId||(product.name==order.productName&&product.priceCents==order.productPriceCents);
            return product.matchedRecipeIndex==order.recipeIndex;
        }
        static string Item(string id)=>BusinessWorldHUD.ShortItem(id);
        public static Readout Read(BusinessSession session,int orderId)
        {
            var result=new Readout();
            StallOrder order=session?.FindOrder(orderId);
            if(order==null)return result;
            result.title=session.OrderDishName(orderId);
            result.flavor=string.IsNullOrEmpty(order.requestedFlavorId)?"原味":StallInteractionInput.FlavorShortName(order.requestedFlavorId);
            bool generic=!string.IsNullOrEmpty(order.productId);
            FoodProduct source=null;
            CounterMeal readyMeal=null;
            if(session.HeldMeal!=null&&Matches(order,session.HeldMeal.product))
            {
                source=session.HeldMeal.product;
                if(session.CanServeHeldMeal(orderId,out _)){readyMeal=session.HeldMeal;result.next="已装在手上 · 点这位顾客直接递餐";}
            }
            foreach(var meal in session.ReadyMeals)
            {
                if(!Matches(order,meal.product))continue;
                if(source==null)source=meal.product;
                if(readyMeal==null&&session.CanServeCounterMeal(meal.id,orderId,out _))
                {readyMeal=meal;result.next="取餐区已有成品 · 点食物，再点这位顾客";}
            }
            int matchingLane=-1;
            for(int i=0;i<session.Lanes.Length;i++)
            {
                var lane=session.Lanes[i];var product=session.ProductAtLane(i);
                bool matches=Matches(order,product);
                if(!matches&&!generic&&lane.freeBatch!=null&&lane.freeBatch.inputs.Length>0)
                {
                    int portions=FreeKitchenRules.DerivePortions(lane.freeBatch.inputs);
                    matches=FreeKitchenRules.MatchRecipe(lane.freeBatch.inputs,lane.freeBatch.method,portions)==order.recipeIndex;
                }
                if(!matches&&!generic&&lane.freeBatch==null&&lane.recipeIndex==order.recipeIndex&&lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.Dirty)matches=true;
                if(!matches)continue;
                if(source==null&&product!=null)source=product;
                if(matchingLane<0)matchingLane=i;
            }
            result.recipeIndex=generic?(source?.matchedRecipeIndex??-1):order.recipeIndex;
            result.method=generic?(source?.method??order.productMethod):RecipeMethod(order.recipeIndex);
            string[] packages;
            if(generic&&source!=null)
            {
                // Show the complete original batch ratio, not rounded fractions
                // which would falsely promise that another mixture is identical.
                foreach(var input in source.inputs??Array.Empty<FoodInput>())
                    if(input!=null&&input.units>0)result.ingredients.Add(new Ingredient{id=input.itemId,units=input.units,available=session.Count(input.itemId)});
                packages=source.packages??Array.Empty<string>();
                result.hasExactInputs=result.ingredients.Count>0;
            }
            else if(!generic&&order.recipeIndex>=0&&order.recipeIndex<BusinessSession.Recipes.Length)
            {
                var recipe=BusinessSession.Recipes[order.recipeIndex];
                foreach(var input in recipe.ingredients)
                    if(FreeKitchenRules.IsEdibleMaterial(input.Key))result.ingredients.Add(new Ingredient{id=input.Key,units=input.Value,available=session.Count(input.Key)});
                packages=recipe.packages;result.hasExactInputs=true;
            }
            else packages=FreeKitchenRules.PackagesFor(result.method);
            result.ready=readyMeal!=null;
            foreach(var input in result.ingredients)input.alreadyPrepared=result.ready||matchingLane>=0;
            // A partial recipe already in a pan must not be counted as missing
            // pantry stock. Suggest that pan without reserving it or its order.
            int partialLane=-1;
            if(!result.ready&&matchingLane<0&&result.hasExactInputs)
            {
                for(int i=0;i<session.Lanes.Length;i++)
                {
                    var lane=session.Lanes[i];var batch=lane.freeBatch;
                    if(lane.step!=KitchenStep.Prep||batch==null||batch.inputs.Length==0)continue;
                    bool compatible=batch.inputs.All(p=>p!=null&&result.ingredients.Any(r=>r.id==p.itemId&&p.units<=r.units));
                    if(!compatible||!session.FreeMethods(i).Contains(result.method))continue;
                    partialLane=i;
                    foreach(var input in result.ingredients)
                    {input.inPan=batch.inputs.Where(p=>p.itemId==input.id).Sum(p=>p.units);input.alreadyPrepared=input.inPan>=input.units;}
                    break;
                }
            }
            string chosenPackage=packages.FirstOrDefault(p=>session.Count(p)>0);
            result.packaging=chosenPackage!=null?Item(chosenPackage)+" ×1":packages.Length>0?"缺包材 · "+Item(packages[0]):"选择适配包材";
            if(readyMeal!=null)result.packaging="已装好 · "+Item(readyMeal.packageId);
            var def=FreeKitchenRules.FindMethod(result.method);
            int actualLane=matchingLane>=0?matchingLane:partialLane;
            FreeKitchenBatch actualBatch=actualLane>=0?session.Lanes[actualLane].freeBatch:null;
            if(actualBatch!=null)
                result.equipment=(actualLane+1)+"号锅："+Item(actualBatch.vesselId)+" · "+Item(actualBatch.toolId);
            else if(def!=null&&FreeKitchenRules.TryBindHardware(def,session.Equipment,null,null,out string vessel,out _,out string tool))
                result.equipment="配套："+Item(vessel)+" · "+Item(tool);
            else if(def==null)result.equipment="按现有成品出餐";
            else
            {
                var missingEquipment=new List<string>();
                if(!def.vesselIds.Any(session.Equipment.Contains))missingEquipment.Add(Item(def.vesselIds[0]));
                if(!def.toolIds.Any(session.Equipment.Contains))missingEquipment.Add(Item(def.toolIds[0]));
                foreach(string extra in def.extraToolIds)if(!session.Equipment.Contains(extra))missingEquipment.Add(Item(extra));
                if(!def.cold&&!def.baseIds.Any(session.Equipment.Contains))missingEquipment.Add("适配炉具");
                result.equipment=missingEquipment.Count>0?"缺："+string.Join("、",missingEquipment):"锅具与炉具不适配";
            }
            switch(result.method)
            {
                case "cold_mix":result.steps="放果料 → 冷调 → 舀取装杯";break;
                case "muddle":result.steps="放果料 → 手锤 → 装杯";break;
                case "reheat":result.steps="放食材 → 复热 → 取出装盒";break;
                case "griddle":result.steps="放食材 → 煎制 / 翻面 → 装袋";break;
                case "boil":result.steps="放食材 → 煮熟 → 盛汤装碗";break;
                case "fry":result.steps="放食材 → 油炸 → 控油装盘";break;
                default:result.steps="放食材 → 开火 / 翻炒 → 装盒";break;
            }
            if(!result.hasExactInputs){result.steps="自由组合 · 按现有成品出餐";result.next="这份餐的原批次已用完，可先做手边食材";}
            else if(!result.ready&&matchingLane>=0)
            {
                var lane=session.Lanes[matchingLane];string number=(matchingLane+1)+"号锅 · ";
                switch(lane.step)
                {
                    case KitchenStep.Prep:result.next=number+"已投料，选做法开始制作";break;
                    case KitchenStep.Heating:result.next=number+(def!=null&&def.cold?"调制中":session.RequiresManualHeatAction(matchingLane)?"加热中，可点锅安排起锅":"正在加工，完成后装盒");break;
                    case KitchenStep.Utensil:result.next=number+"点锅完成翻炒 / 取出";break;
                    case KitchenStep.Season:result.next=number+"选择口味，再装盒";break;
                    default:result.next=number+(lane.freeBatch==null?"已做好，选包材完成出餐":"已做好，选择包材装好拿起");break;
                }
            }
            else if(!result.ready)
            {
                var missing=result.ingredients.Where(i=>i.available+i.inPan<i.units).Select(i=>Item(i.id)).ToArray();
                result.next=missing.Length>0?"缺 "+string.Join("、",missing)+" · 可先做别的搭配":"把上面的食材放进空锅，再选“"+MethodLabel(result.method)+"”";
                if(partialLane>=0&&missing.Length==0)
                    result.next=(partialLane+1)+"号锅 · 再放 "+string.Join("、",result.ingredients.Where(i=>!i.alreadyPrepared).Select(i=>Item(i.id)))+"，然后选“"+MethodLabel(result.method)+"”";
            }
            if(!result.ready&&!string.IsNullOrEmpty(order.requestedFlavorId)&&session.Count(order.requestedFlavorId)<=0)
                result.flavor+=" · 缺货";
            if(!result.ready&&def!=null&&!def.cold&&result.method!="reheat"&&session.Count("seasoning")<=0)
                result.flavor+=" · 缺油盐";
            return result;
        }
    }
}
