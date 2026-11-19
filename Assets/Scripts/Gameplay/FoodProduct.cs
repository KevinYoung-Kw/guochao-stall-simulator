using System;

namespace Guochao
{
    // FREEDOM19：一批自由投料中真实消耗的一种材料（1 单位 = 1 个真实库存单位）。
    [Serializable] public sealed class FoodInput
    {
        public string itemId;
        public int units;
        public FoodInput(){}
        public FoodInput(string itemId,int units){this.itemId=itemId;this.units=units;}
        public FoodInput Copy()=>new FoodInput{itemId=itemId,units=units};
    }

    // FREEDOM19：独立成品。identity/名称/价格一律以本结构实际字段为准；
    // matchedRecipeIndex=-1 表示未命中标准菜谱（泛称成品），visualRecipeIndex
    // 仅为有界渲染适配（恒在 [0,Recipes.Length)），永不决定名称/价格。
    [Serializable] public sealed class FoodProduct
    {
        public string id,name,method,family;
        public int matchedRecipeIndex=-1,visualRecipeIndex,portions,remaining,priceCents;
        public float quality;
        public int sourcePortions=1; // input provenance denominator, retained by one-serving snapshots
        public FoodInput[] inputs=Array.Empty<FoodInput>();
        public string[] packages=Array.Empty<string>();
        public FoodProduct Copy()
        {
            var clone=new FoodProduct{id=id,name=name,method=method,family=family,
                matchedRecipeIndex=matchedRecipeIndex,visualRecipeIndex=visualRecipeIndex,
                portions=portions,remaining=remaining,priceCents=priceCents,quality=quality,sourcePortions=sourcePortions};
            var inputs=new FoodInput[this.inputs==null?0:this.inputs.Length];
            for(int i=0;i<inputs.Length;i++)inputs[i]=this.inputs[i]==null?null:this.inputs[i].Copy();
            clone.inputs=inputs;
            clone.packages=packages==null?Array.Empty<string>():(string[])packages.Clone();
            return clone;
        }
    }

    // FREEDOM19：锅位上的自由批次（可空 = 旧式菜谱批次）。inputs 为已真实扣库存
    // 的投料清单；method 为做法 id（""=未选定）；product 只在真实加工完成
    // （Action 接触帧）后创建，之前恒 null。
    [Serializable] public sealed class FreeKitchenBatch
    {
        public string id="";
        public string method="";
        public FoodInput[] inputs=Array.Empty<FoodInput>();
        public FoodProduct product;
        public bool productPresent; // explicit null marker for Unity inline-object JSON
        // 真实绑定的硬件事实（由 FreeKitchenRules.TryBindHardware 按兼容矩阵选定，
        // 绝不由 visualRecipeIndex 推出；冷制 baseId=""）。Stage/柜台渲染同一事实。
        public string vesselId="",toolId="",baseId="";
        // 真实锅具容量（份）：投料组合份数不得超过；由设备规则在批次建立时确定。
        public int capacity=1;
        public FreeKitchenBatch Copy()
        {
            var clone=new FreeKitchenBatch{id=id,method=method,capacity=capacity,
                vesselId=vesselId,toolId=toolId,baseId=baseId,
                product=product==null?null:product.Copy(),productPresent=product!=null};
            var inputs=new FoodInput[this.inputs==null?0:this.inputs.Length];
            for(int i=0;i<inputs.Length;i++)inputs[i]=this.inputs[i]==null?null:this.inputs[i].Copy();
            clone.inputs=inputs;
            return clone;
        }
    }

    // FREEDOM19 柜台餐（pack-first）：一次真实装台事务产生的独立一份物理餐。
    // product 是一份装的深拷贝快照（identity/名称/做法/品质/报价事实在批次
    // 清空后仍然稳定）；flavorId=装台时实际选瓶（""=无）；shortSeasoning=
    // 装台时记录的热制缺油盐事实（诚信计数在真正卖出时一次性应用）；
    // priceCents=装台时冻结的报价（基础价+所选风味加价）。
    [Serializable] public sealed class CounterMeal
    {
        public string id="",packageId="",flavorId="";
        public FoodProduct product;
        public bool shortSeasoning;
        public int priceCents;
        // 产出锅位（A01 采纳与审计用；柜台交付不依赖旧 order.lane 关联）。
        public int sourceLane=-1;
        public CounterMeal Copy()=>new CounterMeal{id=id,packageId=packageId,flavorId=flavorId,
            shortSeasoning=shortSeasoning,sourceLane=sourceLane,priceCents=priceCents,product=product==null?null:product.Copy()};
    }
}
