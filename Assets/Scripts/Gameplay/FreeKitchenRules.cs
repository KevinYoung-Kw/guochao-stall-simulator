using System;
using System.Collections.Generic;

namespace Guochao
{
    // FREEDOM19：自由厨房静态规则——材料定义（GameCatalog 真实单价）、做法
    // （真实锅具/底座/工具/能源兼容矩阵）、容量、份数、命名、公平定价与标准
    // 菜谱真实匹配。全部为纯函数，不改任何会话状态。配方目录仅是参考/匹配
    // 对象，不是操作许可。库存 1 单位 = 1 份（与采购/旧口径一致）。
    public static class FreeKitchenRules
    {
        public const string MethodStirFry="stir_fry";
        public const string MethodGriddle="griddle";
        public const string MethodBoil="boil";
        public const string MethodFry="fry";
        public const string MethodReheat="reheat";
        public const string MethodColdMix="cold_mix";
        public const string MethodMuddle="muddle";

        // 7 个现行可食库存 ID 的通用材料定义。valueCents 取 GameCatalog 真实
        // 单价（review-notes 13：米面 90/蛋 135/肉 315/饼胚 108/串 162/
        // 料理包 270/果料 198）。portionsPerUnit 一律 1（库存单位即份）。
        public sealed class MaterialDef
        {
            public string itemId,name;
            public int valueCents;
            public string[] methods;
        }
        public static readonly MaterialDef[] Materials={
            new MaterialDef{itemId="rice",name="米面",valueCents=90,methods=new[]{MethodStirFry,MethodBoil,MethodReheat}},
            new MaterialDef{itemId="egg",name="蛋菜",valueCents=135,methods=new[]{MethodStirFry,MethodGriddle,MethodBoil}},
            new MaterialDef{itemId="meat",name="鲜肉",valueCents=315,methods=new[]{MethodStirFry,MethodGriddle,MethodBoil,MethodFry}},
            new MaterialDef{itemId="dough",name="饼胚",valueCents=108,methods=new[]{MethodGriddle,MethodFry}},
            new MaterialDef{itemId="skewer",name="烤串",valueCents=162,methods=new[]{MethodGriddle,MethodBoil,MethodFry}},
            new MaterialDef{itemId="prepared",name="料理包",valueCents=270,methods=new[]{MethodReheat,MethodBoil}},
            new MaterialDef{itemId="fruit",name="鲜果",valueCents=198,methods=new[]{MethodColdMix,MethodMuddle}}};

        public static MaterialDef FindMaterial(string itemId)
        {
            if(string.IsNullOrEmpty(itemId))return null;
            foreach(MaterialDef def in Materials)if(def.itemId==itemId)return def;
            return null;
        }
        public static bool IsEdibleMaterial(string itemId)=>FindMaterial(itemId)!=null;
        // 油盐为每份交付时软扣，风味按瓶选加——都不是投料单位。
        public static bool IsAddableIngredient(string itemId)=>IsEdibleMaterial(itemId);

        public sealed class MethodDef
        {
            public string id,label,family;
            public bool cold;
            public string[] vesselIds,toolIds,baseIds;
            public string[] extraToolIds=Array.Empty<string>(); // 做法额外必需工具（炸物食品夹）
            public int capacity;
        }
        // 做法→设备映射沿用 canonical 锅具-底座兼容矩阵（CompatibleVessel 同款），
        // 不是按菜名/视觉索引。baseIds 顺序即偏好顺序（靠前的底座优先绑定）。
        public static readonly MethodDef[] Methods={
            new MethodDef{id=MethodStirFry,label="小炒",family="猛火现炒",cold=false,
                vesselIds=new[]{"V01","V02","V03","V04"},toolIds=new[]{"U01","U02","U03"},baseIds=new[]{"B04","B03","B02","B01"},capacity=0},
            new MethodDef{id=MethodGriddle,label="煎饼",family="煎烙面点",cold=false,
                vesselIds=new[]{"V05","V06","V07"},toolIds=new[]{"U04","U05"},baseIds=new[]{"B04","B03","B05","B02","B01"},capacity=2},
            new MethodDef{id=MethodBoil,label="热汤",family="汤水卤煮",cold=false,
                vesselIds=new[]{"V09","V10"},toolIds=new[]{"U11","U12"},baseIds=new[]{"B04","B03","B06","B05","B02","B01"},capacity=4},
            new MethodDef{id=MethodFry,label="炸串",family="街头油炸",cold=false,
                vesselIds=new[]{"V13"},toolIds=new[]{"U08","U09"},baseIds=new[]{"B04","B03","B05","B02"},extraToolIds=new[]{"U10"},capacity=2},
            new MethodDef{id=MethodReheat,label="复热",family="科技预制",cold=false,
                vesselIds=new[]{"RiceBucket"},toolIds=new[]{"U14"},baseIds=new[]{"B08","B07"},capacity=2},
            new MethodDef{id=MethodColdMix,label="冷调饮",family="茶饮甜水",cold=true,
                vesselIds=new[]{"V16","V09","V15"},toolIds=new[]{"U12"},baseIds=new string[]{""},capacity=4},
            new MethodDef{id=MethodMuddle,label="手锤饮",family="茶饮甜水",cold=true,
                vesselIds=new[]{"V15"},toolIds=new[]{"U13"},baseIds=new string[]{""},capacity=4}};

        public static MethodDef FindMethod(string methodId)
        {
            if(string.IsNullOrEmpty(methodId))return null;
            foreach(MethodDef def in Methods)if(def.id==methodId)return def;
            return null;
        }
        public static bool IsMethod(string methodId)=>FindMethod(methodId)!=null;

        // 现炒容量沿用母菜口径：V03 大锅 4 / V02 深锅 2 / 薄锅 1；其余按上表。
        public static int CapacityFor(MethodDef def,string vesselId)
        {
            if(def==null)return 0;
            return def.id==MethodStirFry?(vesselId=="V03"?4:vesselId=="V02"?2:1):def.capacity;
        }
        public static int CapacityFor(MethodDef def,HashSet<string> equipment)
        {
            if(def==null||equipment==null)return 0;
            foreach(string vessel in def.vesselIds)if(equipment.Contains(vessel))return CapacityFor(def,vessel);
            return 0;
        }
        public static int InputLoad(IReadOnlyList<FoodInput> inputs)
        {int load=0;if(inputs!=null)foreach(var input in inputs)if(input!=null)load=Math.Max(load,input.units);return load;}

        public static bool MethodAcceptsMaterial(MethodDef def,string itemId)
        {
            MaterialDef material=FindMaterial(itemId);if(def==null||material==null)return false;
            foreach(string id in material.methods)if(id==def.id)return true;
            return false;
        }

        // 真实硬件绑定：在已装备容器/工具/额外工具中，为做法选一口实际兼容
        // （锅具-底座矩阵）且未被其他锅位占用、能源可用的底座。能源余量为 0
        // 的底座只在没有任何可用底座时才作为兜底返回（由 Start 给出燃料拒绝）。
        // 冷制做法不绑底座（baseId=""）。返回 false = 物理上做不了这种做法。
        public static bool TryBindHardware(MethodDef def,HashSet<string> equipment,Func<string,bool> energyAvailable,IEnumerable<string> occupiedBases,
            out string vesselId,out string baseId,out string toolId,int requiredLoad=0)
        {
            vesselId="";baseId="";toolId="";
            if(def==null||equipment==null)return false;
            foreach(string extra in def.extraToolIds)if(!equipment.Contains(extra))return false;
            string chosenTool="";
            foreach(string id in def.toolIds)if(equipment.Contains(id)){chosenTool=id;break;}
            if(chosenTool=="")return false;
            var occupied=new HashSet<string>(occupiedBases??Array.Empty<string>(),StringComparer.Ordinal);
            string fallbackVessel="",fallbackBase="";
            var vessels=new List<string>(def.vesselIds);
            if(def.id==MethodStirFry)vessels.Sort((a,b)=>(b=="V04"?100:CapacityFor(def,b)).CompareTo(a=="V04"?100:CapacityFor(def,a)));
            foreach(string vessel in vessels)
            {
                if(!equipment.Contains(vessel)||CapacityFor(def,vessel)<requiredLoad)continue;
                if(def.cold){vesselId=vessel;toolId=chosenTool;return true;}
                foreach(string bottom in def.baseIds)
                {
                    if(bottom==""||!equipment.Contains(bottom)||occupied.Contains(bottom))continue;
                    if(!VesselFitsBase(vessel,bottom))continue;
                    if(fallbackVessel==""){fallbackVessel=vessel;fallbackBase=bottom;}
                    string energy=BaseEnergy(bottom);
                    if(energyAvailable!=null&&!energyAvailable(energy))continue;
                    vesselId=vessel;baseId=bottom;toolId=chosenTool;return true;
                }
            }
            if(fallbackVessel!=""){vesselId=fallbackVessel;baseId=fallbackBase;toolId=chosenTool;return true;}
            return false;
        }

        // canonical CompatibleVessel 同款矩阵（自由批次按真实容器判定，不看菜谱）。
        public static bool VesselFitsBase(string vessel,string bottom)
        {
            if(vessel=="RiceBucket")return bottom=="B07"||bottom=="B08";
            if(vessel=="V02"||vessel=="V03"||vessel=="V04")return bottom=="B03"||bottom=="B04";
            if(vessel=="V01")return bottom=="B01"||bottom=="B02"||bottom=="B03"||bottom=="B04";
            if(vessel=="V05")return bottom=="B01"||bottom=="B02"||bottom=="B03"||bottom=="B04"||bottom=="B05";
            if(vessel=="V06")return bottom=="B04";
            if(vessel=="V07")return bottom=="B03"||bottom=="B05";
            if(vessel=="V09")return bottom!="";
            if(vessel=="V10")return bottom=="B04"||bottom=="B06";
            if(vessel=="V13")return bottom=="B02"||bottom=="B03"||bottom=="B04"||bottom=="B05";
            if(vessel=="V15"||vessel=="V16")return true; // 冷制容器不挑底座
            return false;
        }

        public static string BaseEnergy(string baseId)
            =>baseId=="B01"?"coal":baseId=="B02"?"cartridge":baseId=="B03"||baseId=="B04"?"gas":baseId==""?"none":"electricity";

        // 与已投料兼容且物理可达的做法（材料全部被接受 ∧ 能绑到真实硬件）。
        public static List<MethodDef> CompatibleMethods(IReadOnlyList<FoodInput> inputs,HashSet<string> equipment,Func<string,bool> energyAvailable,IEnumerable<string> occupiedBases)
        {
            var result=new List<MethodDef>();
            foreach(MethodDef def in Methods)
            {
                if(inputs==null||inputs.Count==0)continue;
                bool ok=true;
                foreach(FoodInput input in inputs)if(input==null||input.units<=0||!MethodAcceptsMaterial(def,input.itemId)){ok=false;break;}
                if(!ok)continue;
                if(!TryBindHardware(def,equipment,energyAvailable,occupiedBases,out _,out _,out _))continue;
                result.Add(def);
            }
            return result;
        }

        // 份数推导：库存 1 单位 = 1 份（GameCatalog 口径），取各材料份数最小值。
        public static int DerivePortions(IReadOnlyList<FoodInput> inputs)
        {
            if(inputs==null||inputs.Count==0)return 0;
            int best=int.MaxValue;
            foreach(FoodInput input in inputs)
            {
                MaterialDef material=FindMaterial(input==null?null:input.itemId);
                if(material==null||input.units<=0)return 0;
                best=Math.Min(best,input.units);
            }
            return best==int.MaxValue?0:best;
        }

        // 真实标准菜谱匹配：实际投料 = 配方非油盐主料 × 同一个整数份数 k
        // （每份实际用量与配方一致；比例不符一律保持泛称，不得冒充足价菜）。
        // 做法菜系/冷热口径也必须与配方一致。
        public static int MatchRecipe(IReadOnlyList<FoodInput> inputs,string methodId,int portions)
        {
            MethodDef method=FindMethod(methodId);if(method==null)return -1;
            if(inputs==null||inputs.Count==0||portions<=0)return -1;
            var actual=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(FoodInput input in inputs)
            {
                if(input==null||input.units<=0||!IsEdibleMaterial(input.itemId))return -1;
                actual[input.itemId]=actual.TryGetValue(input.itemId,out int old)?old+input.units:input.units;
            }
            StallRecipe[] recipes=BusinessSession.Recipes;int found=-1;
            for(int i=0;i<recipes.Length;i++)
            {
                StallRecipe recipe=recipes[i];
                if(recipe.cold!=method.cold||recipe.family!=method.family)continue;
                if(recipe.microwave&&methodId!=MethodReheat)continue;
                if(recipe.id=="lemon_tea"&&methodId!=MethodMuddle)continue;
                if(recipe.cold&&recipe.id!="lemon_tea"&&methodId!=MethodColdMix)continue;
                var wanted=new Dictionary<string,int>(StringComparer.Ordinal);
                foreach(var pair in recipe.ingredients)
                    if(!SeasoningShortageRules.IsBaseSeasoning(pair.Key))wanted[pair.Key]=pair.Value;
                if(wanted.Count!=actual.Count)continue;
                bool match=true;
                foreach(var pair in wanted)
                {
                    if(!actual.TryGetValue(pair.Key,out int units)||units!=pair.Value*portions){match=false;break;}
                }
                if(match){if(found>=0)return -1;found=i;}
            }
            return found;
        }

        // The coarse catalog can describe several named foods with identical inputs.
        // Demand must not promise a specific name that the free kitchen cannot distinguish.
        public static bool RecognizesRecipe(int recipeIndex)
        {
            if(recipeIndex<0||recipeIndex>=BusinessSession.Recipes.Length)return false;
            var recipe=BusinessSession.Recipes[recipeIndex];var inputs=new List<FoodInput>();
            foreach(var pair in recipe.ingredients)if(!SeasoningShortageRules.IsBaseSeasoning(pair.Key))inputs.Add(new FoodInput(pair.Key,pair.Value));
            foreach(var method in Methods)if(MatchRecipe(inputs,method.id,1)==recipeIndex)return true;
            return false;
        }

        // 有界视觉适配索引：同名菜系的第一道菜谱（旧渲染/审计键），无则 0。
        public static int VisualRecipeIndex(string methodId)
        {
            MethodDef method=FindMethod(methodId);
            string family=method==null?"":method.family;
            StallRecipe[] recipes=BusinessSession.Recipes;
            for(int i=0;i<recipes.Length;i++)if(recipes[i].family==family)return i;
            return 0;
        }

        // 泛称命名：玩家可读食物词（顿号连接材料 + 做法词），不含内部公式符号；
        // 明细数量由 LaneContents 单独提供。
        public static string ComposeName(IReadOnlyList<FoodInput> inputs,string methodId)
        {
            MethodDef method=FindMethod(methodId);
            var names=new List<string>();
            if(inputs!=null)foreach(FoodInput input in inputs)
            {
                MaterialDef material=FindMaterial(input==null?null:input.itemId);
                if(material!=null&&input.units>0)names.Add(material.name);
            }
            return string.Join("、",names)+(method==null?"":method.label);
        }

        // 公平定价：每份 = Σ(units×真实单价)/份数 ×1.35 毛利，取整到 10 分，下限 ¥0.50；
        // 真实匹配标准菜谱时按菜谱价。quality 由会话按加工结果记录。
        public static int FairPriceCents(IReadOnlyList<FoodInput> inputs,int portions,int matchedRecipeIndex)
        {
            if(matchedRecipeIndex>=0&&matchedRecipeIndex<BusinessSession.Recipes.Length)
                return BusinessSession.Recipes[matchedRecipeIndex].priceCents;
            if(portions<=0||inputs==null||inputs.Count==0)return 0;
            int cost=0;foreach(FoodInput input in inputs)
            {
                MaterialDef material=FindMaterial(input==null?null:input.itemId);
                if(material==null||input.units<=0)return 0;
                cost+=input.units*material.valueCents;
            }
            int raw=(int)Math.Round(cost/(double)portions*1.35/10.0)*10;
            return Math.Max(50,raw);
        }

        // 按菜系给可交付包材（取同菜系菜谱的包材表，无则通用 P05 餐盒）。
        public static string[] PackagesFor(string methodId)
        {
            MethodDef method=FindMethod(methodId);
            string family=method==null?"":method.family;
            foreach(StallRecipe recipe in BusinessSession.Recipes)
                if(recipe.family==family)return (string[])recipe.packages.Clone();
            return new[]{"P05"};
        }

        public static bool IsColdMethod(string methodId){MethodDef def=FindMethod(methodId);return def!=null&&def.cold;}

        // 风味与成品菜系的真实兼容（自由批次按 product.family，不看 visualRecipeIndex）。
        public static bool FlavorCompatible(FoodProduct product,string flavorId)
        {
            StallFlavor flavor=StallFlavorMenu.Find(flavorId);
            if(flavor==null||product==null||product.family==null)return false;
            foreach(string family in flavor.families)if(family==product.family)return true;
            return false;
        }

        // 加热时长：2s + 0.5s×总投料单位（真实计时，燃料只在 StartFreeCooking 扣一次）。
        public static float HeatSeconds(IReadOnlyList<FoodInput> inputs)
        {
            int units=0;if(inputs!=null)foreach(FoodInput input in inputs)if(input!=null&&input.units>0)units+=input.units;
            return 2f+.5f*units;
        }
    }
}
