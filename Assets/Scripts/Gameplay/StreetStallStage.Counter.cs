using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // 出餐台 + 自由锅视觉（FREEDOM19）：右前方独立折叠餐台，6 个固定有界餐位。
    // 每个 ReadyMeals 项是一个真实可点的 CounterMeal 目标（itemId=meal.id），
    // 无餐则无可点餐物；Serving 底座目标只做“打包入台”指令入口，绝不自动
    // 发顾客。台面/站位/手点沿用既有 Solid/臂长口径，不缩碰撞、不传送。
    public sealed partial class StreetStallStage
    {
        private const int CounterSlotCount=6;
        private const float ServingCounterZ=.40f,ServingCounterDepth=.25f;
        private const float ServingCounterFront=ServingCounterZ-ServingCounterDepth*.5f,ServingCounterBack=ServingCounterZ+ServingCounterDepth*.5f;
        public float LastCounterPlacementError {get;private set;}=float.PositiveInfinity;
        private readonly List<CounterMealSlot> counterSlots=new List<CounterMealSlot>();
        private readonly HashSet<string> presentedMealIds=new HashSet<string>();
        // PerformCounterDelivery 临时隐藏的槽位（接触帧拿走模型防复制），CompleteWork 恢复。
        private CounterMealSlot hiddenMealSlot;
        // 库存陈列减量：食材托盘/包材堆的可见内容与真实库存同步。
        private readonly Dictionary<string,Transform> ingredientTrayNodes=new Dictionary<string,Transform>();
        private readonly Dictionary<string,bool> ingredientTrayFull=new Dictionary<string,bool>();
        private readonly Dictionary<string,List<Transform>> packageStockNodes=new Dictionary<string,List<Transform>>();
        private readonly Dictionary<string,Transform> packageTopNodes=new Dictionary<string,Transform>();
        // 每锅内容签名（inputs+units+method+vessel+product），变了才重建。
        private readonly List<string> laneVisualSignatures=new List<string>();
        private readonly List<string> laneToolVisualSignatures=new List<string>();
        private sealed class CounterMealSlot
        {
            public Transform tray,mealVisual;
            public string mealId; // null = 空位
            public Vector3 stand;
        }
        // 台面起点：取实际已建包材工位最右 x，向右让出整段餐台（与包材排完全
        // 分离）。真实浅进深取餐台同时开放摊主侧和顾客侧；渲染与SolidBox同尺寸。
        // 餐盒重心在后沿内3cm，边沿供顾客抓握；角色碰撞包络不变。
        private void BuildCounter()
        {
            float maxPackX=2.50f;
            foreach(Station pack in packages.Values)maxPackX=Mathf.Max(maxPackX,pack.stand.x);
            float firstSlotX=maxPackX+.55f+.20f;
            float lastSlotX=firstSlotX+(CounterSlotCount-1)*.40f;
            float leftEdge=firstSlotX-.24f,rightEdge=lastSlotX+.24f;
            float shelfCenter=(leftEdge+rightEdge)*.5f,shelfWidth=rightEdge-leftEdge;
            Transform shelf=Group("Serving counter shelf",cooking);
            Shape("Serving shelf top",shelf,new Vector3(shelfCenter,.905f,ServingCounterZ),new Vector3(shelfWidth,.05f,ServingCounterDepth),steel);
            Shape("Serving shelf wood front lip",shelf,new Vector3(shelfCenter,.945f,ServingCounterFront+.015f),new Vector3(shelfWidth,.055f,.05f),wood);
            Shape("Serving shelf red end cap",shelf,new Vector3(leftEdge+.03f,.93f,ServingCounterZ),new Vector3(.06f,.12f,ServingCounterDepth),rust);
            for(int leg=0;leg<2;leg++)
            {
                Vector3 legAt=new Vector3(shelfCenter+(leg==0?-1:1)*(shelfWidth*.5f-.10f),.45f,ServingCounterZ);
                Shape("Serving shelf leg",shelf,legAt,new Vector3(.055f,.90f,.055f),steel);
                SolidBox("Serving shelf leg "+leg,legAt,new Vector3(.055f,.90f,.055f));
            }
            SolidBox("Serving shelf",new Vector3(shelfCenter,.905f,ServingCounterZ),new Vector3(shelfWidth,.05f,ServingCounterDepth));
            Material brass=Paint("Warm brass tag",new Color(.80f,.62f,.28f),.45f);
            for(int i=0;i<CounterSlotCount;i++)
            {
                float x=firstSlotX+i*.40f;
                Transform tray=Group("Counter tray "+i,cooking);tray.position=new Vector3(x,.9425f,ServingCounterZ);
                Shape("Cream counter tray",tray,Vector3.zero,new Vector3(.34f,.022f,ServingCounterDepth),cream);
                Shape("Tray wood rim front",tray,new Vector3(0,.022f,-ServingCounterDepth*.5f+.015f),new Vector3(.34f,.045f,.03f),wood);
                // Open customer edge: package centre stays supported, its rim is reachable from the aisle.
                for(int side=-1;side<=1;side+=2)Shape("Tray wood rim side",tray,new Vector3(side*.155f,.022f,0),new Vector3(.03f,.045f,ServingCounterDepth),wood);
                Shape("Brass slot tag",tray,new Vector3(0,.025f,-ServingCounterDepth*.5f+.025f),new Vector3(.05f,.012f,.02f),brass);
                var slot=new CounterMealSlot{tray=tray,stand=new Vector3(x-.10f,0,ServingCounterFront-.28f)};
                counterSlots.Add(slot);
            }
            // Serving 底座目标：入台指令入口，独立点击区，不属于任何餐。
            Transform servingBase=Group("Serving base target",cooking);servingBase.position=new Vector3(leftEdge-.28f,.945f,ServingCounterZ);
            Shape("Serving base brass bell",servingBase,new Vector3(0,.05f,0),new Vector3(.07f,.09f,.07f),brass,PrimitiveType.Cylinder);
            Target(servingBase,StallInteractionKind.Serving,"",-1,servingBase.position+new Vector3(0,.22f,0),new Vector3(.26f,.26f,.30f));
            // 取景：开局无 meal 目标时也必须完整可见最远空槽（镜头仍固定 2.5D，
            // 只随视口宽高比重算，不跟鼠标/动作动）。
            float maximumCustomerRadius=.25f;
            foreach(var asset in customerAssets)maximumCustomerRadius=Mathf.Max(maximumCustomerRadius,NpcBodyProfile.ForArchetype(asset.Definition.Id).Radius);
            // Direct handoff bay lives beside the shelf. Include both real bodies
            // before opening; no cursor/action-triggered camera movement.
            operatingRight=Mathf.Max(operatingRight,rightEdge+maximumCustomerRadius*2f+.34f+.15f);
            operatingLeft=Mathf.Min(operatingLeft,leftEdge-.55f);
        }
        // 食材托盘内容（含真实 prepared Aholo 网格复用）；hasStock=false 时空盘。
        private void PopulateIngredientTray(Transform tray,string id,bool hasStock)
        {
            Shape("Wood tray base",tray,Vector3.zero,new Vector3(.43f,.04f,.27f),wood);
            for(int side=-1;side<=1;side+=2)
            {Shape("Tray side",tray,new Vector3(side*.205f,.065f,0),new Vector3(.025f,.14f,.27f),wood);Shape("Tray edge",tray,new Vector3(0,.065f,side*.125f),new Vector3(.43f,.14f,.025f),wood);}
            if(!hasStock)return;
            if(id=="prepared"){if(GeneratedIngredientAssets.TryPopulatePrepared(tray))return;} // 真实封包网格，失败走下方通用兜底
            var rnd=new System.Random(id.GetHashCode());
            if(id=="egg")for(int i=0;i<8;i++)Shape("Egg",tray,new Vector3((i%4-1.5f)*.09f,.10f,(i/4-.5f)*.10f),new Vector3(.075f,.095f,.075f),dough,PrimitiveType.Sphere);
            else if(id=="dough")for(int i=0;i<3;i++)Shape("Pastry stack",tray,new Vector3(0,.07f+i*.023f,0),new Vector3(.24f,.014f,.22f),rice,PrimitiveType.Cylinder);
            else for(int i=0;i<(id=="rice"?55:12);i++)
            {Vector3 p=new Vector3(((float)rnd.NextDouble()-.5f)*.34f,.075f+(float)rnd.NextDouble()*.025f,((float)rnd.NextDouble()-.5f)*.18f);Shape(id,tray,p,id=="rice"?new Vector3(.025f,.015f,.045f):new Vector3(.055f,.04f,.065f),id=="rice"?rice:id=="fruit"?green:sausage,id=="rice"?PrimitiveType.Sphere:PrimitiveType.Cube);}
        }
        // 库存陈列减量：食材托盘满/空重建、包材堆按份数显隐；库存 0 时目标仍可
        // 点（短“用完”信息由 HUD 负责），但绝不仍画满盒满盘。
        private void UpdateStockDisplays(BusinessSession session)
        {
            foreach(var pair in ingredientTrayNodes)
            {
                int stock=session.Count(pair.Key);
                bool full=stock>0;
                if(ingredientTrayFull.TryGetValue(pair.Key,out bool was)&&was==full)continue;
                Transform tray=pair.Value;
                foreach(Transform child in tray)if(child.name!="HUD anchor"){child.gameObject.SetActive(false);Destroy(child.gameObject);}
                PopulateIngredientTray(tray,pair.Key,full);
                CombineDecorations(tray);
                ingredientTrayFull[pair.Key]=full;
            }
            foreach(var pair in packageStockNodes)
            {
                int stock=session.Count(pair.Key);
                int shown=Mathf.Clamp(stock,0,pair.Value.Count);
                for(int n=0;n<pair.Value.Count;n++)if(pair.Value[n]!=null)pair.Value[n].gameObject.SetActive(n<shown);
                if(packageTopNodes.TryGetValue(pair.Key,out Transform top)&&top!=null)top.gameObject.SetActive(stock>0);
            }
        }
        // ── 自由锅事实 helpers ──
        private static string FreeLaneMethod(KitchenLane lane)
            =>lane!=null&&lane.freeBatch!=null&&!string.IsNullOrEmpty(lane.freeBatch.method)?lane.freeBatch.method:null;
        private static FoodInput[] FreeLaneInputs(KitchenLane lane)
            =>lane!=null&&lane.freeBatch!=null?lane.freeBatch.inputs:null;
        private static bool FreeLaneHasInputs(KitchenLane lane)
        {
            FoodInput[] inputs=FreeLaneInputs(lane);
            if(inputs==null)return false;
            foreach(FoodInput input in inputs)if(input!=null&&input.units>0)return true;
            return false;
        }
        private static bool LaneIsCold(KitchenLane lane)
        {
            string m=FreeLaneMethod(lane);
            if(m!=null)return m=="cold_mix"||m=="muddle";
            return BusinessSession.Recipes[lane.recipeIndex].cold;
        }
        private static float LaneDepth(KitchenLane lane)
        {
            string m=FreeLaneMethod(lane);
            if(m=="boil")return .23f;
            if(m=="griddle")return .045f;
            if(m=="cold_mix"||m=="muddle")return .06f;
            if(m!=null)return .14f;
            int r=lane.recipeIndex;bool deep=r==3||r==5;
            return deep?.23f:BusinessSession.ParentRecipeIndex(r)==0?.14f:.045f;
        }
        // PerformDeliveryInteraction 的锅上动作：自由批用真实 method/step 事实，
        // legacy 走原母菜系口径。
        private VendorWorkAction PanWorkAction(StallInteractionKind kind,int lane)
        {
            if(kind==StallInteractionKind.Packaging)return VendorWorkAction.Pack;
            if(kind==StallInteractionKind.Seasoning)return VendorWorkAction.Season;
            if(kind==StallInteractionKind.Ingredient)return VendorWorkAction.PourIngredient;
            KitchenLane target=activeSession.Lanes[lane];
            string m=FreeLaneMethod(target);
            if(m!=null)
            {
                if(target.step==KitchenStep.ReadyStock)return VendorWorkAction.Scoop;
                if(target.step==KitchenStep.Dirty)return VendorWorkAction.Clean;
                if(m=="cold_mix"||m=="muddle")return VendorWorkAction.Scoop;
                if(m=="griddle"||m=="fry")return VendorWorkAction.Flip;
                return VendorWorkAction.StirFry;
            }
            return target.step==KitchenStep.ReadyStock?VendorWorkAction.Scoop:target.step==KitchenStep.Dirty?VendorWorkAction.Clean:BusinessSession.Recipes[target.recipeIndex].cold?VendorWorkAction.Scoop:BusinessSession.ParentRecipeIndex(target.recipeIndex)==2?VendorWorkAction.Flip:VendorWorkAction.StirFry;
        }
        // 签名：inputs 各项 id+units + method + vesselId + 是否有 product；legacy 用
        // recipeIndex。签名不变不动，变了才重建锅内食物与锅具外观（视觉 only）。
        private static string LaneVisualSignature(KitchenLane lane)
        {
            if(lane.freeBatch==null)return "L"+lane.recipeIndex;
            var builder=new System.Text.StringBuilder();
            builder.Append('F').Append(lane.freeBatch.method).Append('|').Append(lane.freeBatch.vesselId).Append('|').Append(lane.freeBatch.toolId).Append('|').Append(lane.freeBatch.baseId).Append('|').Append(lane.freeBatch.product!=null?"P":"-").Append('|');
            FoodInput[] inputs=lane.freeBatch.inputs;
            if(inputs!=null)foreach(FoodInput input in inputs)if(input!=null)builder.Append(input.itemId).Append('x').Append(input.units).Append(';');
            return builder.ToString();
        }
        private void UpdateFreeLaneVisual(int laneIndex,KitchenLane lane)
        {
            if(laneIndex<0||laneIndex>=foodGroups.Count||foodGroups[laneIndex]==null||dishes.Count<=laneIndex||dishes[laneIndex]==null)return;
            UpdateFreeLaneToolVisual(laneIndex,lane);
            while(laneVisualSignatures.Count<=laneIndex)laneVisualSignatures.Add(null);
            string signature=LaneVisualSignature(lane);
            if(laneVisualSignatures[laneIndex]==signature)return;
            laneVisualSignatures[laneIndex]=signature;
            float depth=LaneDepth(lane);
            Transform dish=dishes[laneIndex];
            Transform fresh=Group("Actual food in cookware",dish);
            if(lane.freeBatch!=null)BuildFreeFood(fresh,lane.freeBatch.inputs,lane.freeBatch.method,lane.freeBatch.product!=null,depth,1f);
            else BuildFood(fresh,lane.recipeIndex,depth);
            fresh.gameObject.SetActive(false);
            Transform oldFood=foodGroups[laneIndex];foodGroups[laneIndex]=fresh;
            if(oldFood!=null){oldFood.gameObject.SetActive(false);Destroy(oldFood.gameObject);}
            // 锅具外观：按真实 method/vessel 换锅深与工具（只视觉，不动碰撞/站位）。
            bool useGeneratedWok=lane.freeBatch!=null ? lane.freeBatch.vesselId=="V02" :
                activeSession.Equipment.Contains("V02")&&BusinessSession.ParentRecipeIndex(lane.recipeIndex)==0;
            float radius=lane.freeBatch!=null&&(FreeLaneMethod(lane)=="cold_mix"||FreeLaneMethod(lane)=="muddle")?.19f:.25f;
            BuildCookwareVisual(dish,depth,useGeneratedWok,radius);
            dish.name=activeSession.LaneDishName(laneIndex)+" cookware";
        }
        // Food follows committed inputs immediately, even during recovery. Only tools
        // wait for an idle actor: an in-use toolGrip must remain valid through recovery.
        private void UpdateFreeLaneToolVisual(int laneIndex,KitchenLane lane)
        {
            if(WorkActorBusy||laneIndex<0||laneIndex>=panStations.Count)return;
            while(laneToolVisualSignatures.Count<=laneIndex)laneToolVisualSignatures.Add(null);
            string signature=lane.freeBatch==null?"L"+lane.recipeIndex:lane.freeBatch.method+"|"+lane.freeBatch.toolId;
            if(laneToolVisualSignatures[laneIndex]==signature)return;
            laneToolVisualSignatures[laneIndex]=signature;
            if(laneIndex<panStations.Count&&panStations[laneIndex]!=null)
            {
                Station station=panStations[laneIndex];
                if(station.tool!=null)
                {
                    foreach(Transform child in station.tool){child.gameObject.SetActive(false);Destroy(child.gameObject);}
                    station.toolGrip=null;
                    string m=FreeLaneMethod(lane);
                    bool cold=m=="cold_mix"||m=="muddle";
                    if(cold||BusinessSession.Recipes[lane.recipeIndex].id=="lemon_tea")
                    {
                        Between("Wooden muddler handle",station.tool,new Vector3(0,0,-.06f),new Vector3(0,.05f,.16f),.022f,wood);
                        Shape("Wooden muddler head",station.tool,new Vector3(0,.05f,.20f),new Vector3(.05f,.09f,.05f),wood,PrimitiveType.Cylinder);
                    }
                    else
                    {
                        Shape("Wood spatula grip",station.tool,Vector3.zero,new Vector3(.045f,.035f,.18f),wood);
                        Between("Steel spatula stem",station.tool,new Vector3(0,0,.08f),new Vector3(0,.15f,.49f),.025f,steel);
                        Shape("Steel spatula head",station.tool,new Vector3(0,.17f,.53f),new Vector3(.12f,.018f,.11f),steel);
                    }
                    station.toolGrip=Group("Actual spatula palm grip",station.tool);
                }
            }
        }
        // 自由成品/在锅内容：逐项输入决定形态与可见 units 数量；只有真实 rice 才
        // 画米。无输入时不生米（空锅由 Present 隐藏）。
        private void BuildFreeFood(Transform parent,FoodInput[] inputs,string method,bool cooked,float depth,float scale)
        {
            bool any=false;
            if(inputs!=null)foreach(FoodInput input in inputs)if(input!=null&&input.units>0){any=true;break;}
            if(!any)return; // 无输入不生任何料（空锅由 Present 隐藏）
            bool liquid=method=="boil"||method=="cold_mix"||method=="muddle";
            if(liquid)Shape("Liquid surface",parent,new Vector3(0,depth*.62f,0),new Vector3(.35f,.016f,.35f),method=="boil"?dough:TeaAmber,PrimitiveType.Cylinder);
            int slotSeed=17;
            foreach(FoodInput input in inputs)
            {
                if(input==null||input.units<=0)continue;
                slotSeed+=input.itemId==null?0:input.itemId.GetHashCode();
                var rnd=new System.Random(slotSeed);
                int pieces=Mathf.Clamp(input.units,1,14);
                string id=input.itemId;
                for(int i=0;i<pieces;i++)
                {
                    float a=(float)rnd.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rnd.NextDouble())*.15f;
                    Vector3 p=new Vector3(Mathf.Sin(a)*r,liquid?depth*.62f+.03f:.06f+(1-r/.18f)*.03f+(float)rnd.NextDouble()*.02f,Mathf.Cos(a)*r);
                    if(id=="rice")
                    {
                        Shape("Rice grain",parent,p,new Vector3(.02f,.014f,.034f),rice,PrimitiveType.Sphere);
                        if(i==0)Shape("Rice mound",parent,new Vector3(0,.05f,0),new Vector3(.32f,.055f,.30f),rice,PrimitiveType.Sphere);
                    }
                    else if(id=="egg")Shape(cooked?"Cooked egg curd":"Raw egg white",parent,p,cooked?new Vector3(.045f,.022f,.04f):new Vector3(.05f,.012f,.045f),cooked?egg:white,PrimitiveType.Sphere);
                    else if(id=="meat"||id=="sausage"||id=="skewer")Shape("Meat piece",parent,p,new Vector3(.05f,.035f,.045f),sausage,PrimitiveType.Sphere);
                    else if(id=="dough")Shape("Dough piece",parent,p,new Vector3(.055f,.02f,.05f),dough,PrimitiveType.Cylinder);
                    else if(id=="green"||id=="scallion")Shape("Scallion bit",parent,p,new Vector3(.022f,.009f,.018f),green);
                    else if(id=="fruit")Shape("Fruit cube",parent,p,new Vector3(.035f,.03f,.035f),green,PrimitiveType.Cube);
                    else if(id=="prepared")Shape("Prepared pouch",parent,p,new Vector3(.05f,.025f,.04f),dough);
                    else Shape("Ingredient bit "+id,parent,p,new Vector3(.04f,.025f,.04f),sausage,PrimitiveType.Sphere);
                }
            }
            parent.localScale=Vector3.one*scale;
            CombineDecorations(parent);
        }
        // 台餐内容物：优先 product 快照（真实 method/inputs）；打包过程中的锅里
        // 携带份用该 lane 的 freeBatch.inputs；legacy 无可考输入时不冒名炒饭，
        // 给中性盒装轮廓。
        private void BuildMealFoodVisual(Transform parent,FoodProduct product,FoodInput[] fallbackInputs,bool cooked)
        {
            if(product!=null){BuildFreeFood(parent,product.inputs,product.method,cooked,.10f,1f);return;}
            if(fallbackInputs!=null&&fallbackInputs.Length>0){BuildFreeFood(parent,fallbackInputs,null,cooked,.10f,1f);return;}
            Shape("Neutral meal base",parent,new Vector3(0,.04f,0),new Vector3(.16f,.03f,.15f),rice,PrimitiveType.Sphere);
        }
        // Package silhouettes follow the actual purchased container on the table and in hand.
        private void BuildPackageContents(Transform parent,string packageId,FoodProduct product)
        {
            Transform shell=Group("Actual package "+packageId,parent);
            float contentHeight=.04f,contentScale=.56f;
            if(packageId==BusinessSession.EmergencyBagId)
            {
                Shape("Emergency plastic bag",shell,new Vector3(0,.07f,0),new Vector3(.20f,.14f,.13f),cream);
                Shape("Emergency bag handle",shell,new Vector3(0,.16f,0),new Vector3(.10f,.04f,.03f),white);
                return;
            }
            if(packageId=="P08"||packageId=="P12")
            {
                Bowl(shell,.075f,.18f,packageId=="P12"?wood:white);
                Shape("Cup lid rim",shell,new Vector3(0,.185f,0),new Vector3(.16f,.012f,.16f),cream,PrimitiveType.Cylinder);
                contentHeight=.13f;contentScale=.38f;
            }
            else if(packageId=="P07"||packageId=="P10")
            {
                Bowl(shell,.125f,.10f,packageId=="P10"?cream:white);contentHeight=.04f;contentScale=.60f;
            }
            else if(packageId=="P04"||packageId=="P09")
            {
                Shape("Paper food bag",shell,new Vector3(0,.075f,0),new Vector3(.22f,.15f,.14f),dough);
                Shape("Folded paper top",shell,new Vector3(0,.155f,.025f),new Vector3(.20f,.012f,.08f),wood);
                if(packageId=="P09")Shape("Red paper band",shell,new Vector3(0,.07f,-.073f),new Vector3(.23f,.055f,.012f),rust);
                return;
            }
            else
            {
                Material mat=packageId=="P03"||packageId=="P11"?wood:white;
                Shape("Food tray base",shell,new Vector3(0,.012f,0),new Vector3(.25f,.024f,.19f),mat);
                for(int side=-1;side<=1;side+=2)
                {Shape("Tray long rim",shell,new Vector3(side*.12f,.035f,0),new Vector3(.015f,.06f,.19f),mat);Shape("Tray short rim",shell,new Vector3(0,.035f,side*.09f),new Vector3(.25f,.06f,.015f),mat);}
                if(packageId=="P06")Shape("Meal divider",shell,new Vector3(0,.04f,0),new Vector3(.015f,.065f,.18f),mat);
            }
            Transform contents=Group("Actual contents",shell);contents.localPosition=new Vector3(0,contentHeight,0);
            BuildMealFoodVisual(contents,product,null,true);contents.localScale=Vector3.one*contentScale;
        }
        private void BuildPackedMealVisual(Transform parent,CounterMeal meal)
        {if(meal!=null)BuildPackageContents(parent,meal.packageId,meal.product);}
        // The same resting origin and rim grip are used by the shelf model and
        // both hand transfers. Bowls/cups extend .02 below their mesh origin.
        private static Vector3 CounterRestPosition(CounterMealSlot slot,string packageId)
        {
            bool bowl=packageId=="P07"||packageId=="P08"||packageId=="P10"||packageId=="P12";
            return slot.tray.position+Vector3.forward*(ServingCounterDepth*.5f-.030f)+Vector3.up*(.011f+(bowl?.020f:0));
        }
        private static Vector3 PackageGripOffset(string packageId)
        {
            if(packageId=="P08"||packageId=="P12")return new Vector3(.04f,.10f,-.075f);
            if(packageId=="P07"||packageId=="P10")return new Vector3(.065f,.065f,-.105f);
            if(packageId=="P04"||packageId=="P09")return new Vector3(.065f,.085f,-.070f);
            return new Vector3(.085f,.055f,-.095f);
        }
        private Station CounterReachStation(CounterMealSlot slot,string packageId,string name,Quaternion rotation)
        {
            Vector3 grip=CounterRestPosition(slot,packageId)+PackageGripOffset(packageId);
            // Face the counter, with the rim beside the right shoulder. Turning
            // toward the right-hand grip itself would consume the arm span sideways.
            var station=new Station{stand=slot.stand,right=Group(name,cooking),facingPoint=new Vector3(slot.stand.x,grip.y,grip.z)};
            station.right.SetPositionAndRotation(grip,rotation);
            return station;
        }
        private CounterMealSlot FindCounterSlot(string mealId)
        {
            foreach(CounterMealSlot slot in counterSlots)if(slot.mealId==mealId)return slot;
            return null;
        }
        private CounterMealSlot FirstFreeCounterSlot()
        {
            foreach(CounterMealSlot slot in counterSlots)if(slot.mealId==null)return slot;
            return null;
        }
        // Present 刷新：只在 meal id 集合变更时创建/删除，单个槽不跳位置。
        private void RefreshCounterMeals(BusinessSession session)
        {
            if(session==null||counterSlots.Count==0)return;
            var current=new HashSet<string>();
            foreach(CounterMeal meal in session.ReadyMeals)if(meal!=null&&meal.id!=null)current.Add(meal.id);
            foreach(CounterMeal meal in session.ReadyMeals)
            {
                if(meal==null||meal.id==null||presentedMealIds.Contains(meal.id))continue;
                CounterMealSlot slot=FirstFreeCounterSlot();
                if(slot==null)continue; // 核心容量同样为 6，理论不溢出；溢出时下一轮再补
                slot.mealId=meal.id;
                Transform visual=Group("Counter meal "+meal.id,slot.tray);
                visual.position=CounterRestPosition(slot,meal.packageId);
                BuildPackedMealVisual(visual,meal);
                Target(visual,StallInteractionKind.CounterMeal,meal.id,-1,slot.tray.position+new Vector3(0,.30f,0),new Vector3(.36f,.30f,.44f));
                slot.mealVisual=visual;
                presentedMealIds.Add(meal.id);
            }
            for(int i=0;i<counterSlots.Count;i++)
            {
                CounterMealSlot slot=counterSlots[i];
                if(slot.mealId!=null&&!current.Contains(slot.mealId))
                {
                    if(hiddenMealSlot==slot)hiddenMealSlot=null;
                    if(slot.mealVisual!=null)Destroy(slot.mealVisual.gameObject);
                    presentedMealIds.Remove(slot.mealId);
                    slot.mealVisual=null;slot.mealId=null;
                }
            }
        }
        // 打包入台：锅 Scoop 拿份（contact 只 attach）→ Pack contact 换持装盒成品
        // → 台面平放接触帧 commit（PackToCounter：扣 1 包材 + 1 份、加 ReadyMeals），
        // 收手完成才清理临时接触点并收束。绝不发顾客。
        public void PerformCounterPackaging(int lane,string packageId,Action contact,Action finished)
        {
            if(WorkActorBusy){finished?.Invoke();return;}
            if(lane<0||lane>=panStations.Count){WorkFailed?.Invoke("invalid_lane");finished?.Invoke();return;}
            BusinessSession session=activeSession;
            if(session==null){WorkFailed?.Invoke("no_session");finished?.Invoke();return;}
            if(!packages.TryGetValue(packageId,out Station pack)){WorkFailed?.Invoke("no_package_"+packageId);finished?.Invoke();return;}
            CounterMealSlot slot=FirstFreeCounterSlot();
            if(slot==null){WorkFailed?.Invoke("counter_full");finished?.Invoke();return;}
            if(!session.CanPackToCounter(lane,packageId,out string reason)){WorkFailed?.Invoke(reason??"cannot_pack");finished?.Invoke();return;}
            sequenceBusy=true;sequenceFinished=finished;
            Station pan=panStations[lane];
            FoodInput[] batchInputs=session.Lanes[lane].freeBatch!=null?session.Lanes[lane].freeBatch.inputs:null;
            Action done=CompleteWork;
            FoodProduct product=session.ProductAtLane(lane);
            Quaternion packedGripRotation=Quaternion.identity;
            LastCounterPlacementError=float.PositiveInfinity;
            Execute(VendorWorkAction.Scoop,pan,.70f,()=>
            {
                carriedProp=Group("Portion to counter pack",cooking);carriedProp.position=pan.left.position;
                Bowl(carriedProp,.115f,.025f,cream);
                Transform portion=Group("Carried portion",carriedProp);BuildMealFoodVisual(portion,product,batchInputs,true);portion.localScale=Vector3.one*.55f;
                Transform grip=Group("Left serving grip",carriedProp);grip.rotation=pan.left.rotation;
                if(!workActor.AttachVisualProp(carriedProp,grip,true)){workActor.Abort("Cooked serving missed the left palm contact");return;}
            },()=>Execute(VendorWorkAction.Pack,pack,.70f,()=>
            {
                if(carriedProp){workActor.ReleaseVisualProp();Destroy(carriedProp.gameObject);carriedProp=null;}
                // Pack commits during the closing gesture, after the palm has moved
                // from the station grip. The completed package begins in that palm.
                carriedProp=Group("Packed meal to counter",cooking);
                carriedProp.SetPositionAndRotation(workActor.RightPalmPosition-PackageGripOffset(packageId),Quaternion.identity);
                BuildPackageContents(carriedProp,packageId,product);
                Transform grip=Group("Packed handhold contact",carriedProp);grip.localPosition=PackageGripOffset(packageId);
                packedGripRotation=workActor.RightPalmRotation;grip.rotation=packedGripRotation;
                if(!workActor.AttachVisualProp(carriedProp,grip)){workActor.Abort("Packed meal missed the reached palm contact");return;}
            },()=>
            {
                Station place=CounterReachStation(slot,packageId,"Counter place contact",packedGripRotation);
                // This reach-and-retract gesture reaches the rim exactly at .32,
                // then lifts the empty hand. Serve intentionally pushes forward
                // for a customer handoff and is not a table-placement trajectory.
                Execute(VendorWorkAction.PickIngredient,place,.70f,()=>
                {
                    LastCounterPlacementError=carriedProp?Vector3.Distance(carriedProp.position,CounterRestPosition(slot,packageId)):float.PositiveInfinity;
                    contact?.Invoke();workActor.ReleaseVisualProp();
                },()=>
                {if(place.right!=null)Destroy(place.right.gameObject);done();});
            }));
        }

        // 台面取餐 → 指定顾客服务侧 Serve 接触 commit。固定 orderId，无 resolver
        // 换人；顾客离开 / meal 不存在 / 不可达：failed + finished，台餐保留。
        // pickup contact 只隐藏源模型 + attach 手持；pickup finished 才发起 Serve。
        public void PerformCounterDelivery(string mealId,int orderId,Action contact,Action finished,Action failed)
        {
            if(WorkActorBusy){failed?.Invoke();finished?.Invoke();return;}
            BusinessSession session=activeSession;
            CounterMealSlot slot=mealId==null?null:FindCounterSlot(mealId);
            if(session==null||slot==null){failed?.Invoke();finished?.Invoke();return;}
            CounterMeal meal=null;
            foreach(CounterMeal m in session.ReadyMeals)if(m!=null&&m.id==mealId){meal=m;break;}
            if(meal==null){failed?.Invoke();finished?.Invoke();return;}
            if(!IsOrderServable(orderId)){failed?.Invoke();finished?.Invoke();return;}
            if(!TryGetGuestServicePoint(orderId,out Vector3 stand,out Vector3 hand)){failed?.Invoke();finished?.Invoke();return;}
            sequenceBusy=true;sequenceFinished=finished;
            Station pick=CounterReachStation(slot,meal.packageId,"Counter meal pickup contact",Quaternion.LookRotation(Vector3.forward,Vector3.up));
            Action done=CompleteWork;
            Execute(VendorWorkAction.PickIngredient,pick,.55f,()=>
            {
                // 真实接触帧：隐藏台餐源模型（防复制）并 attach 手持同款。
                Quaternion pickupRotation=pick.right.rotation;
                hiddenMealSlot=slot;
                if(slot.mealVisual!=null){foreach(Renderer renderer in slot.mealVisual.GetComponentsInChildren<Renderer>())renderer.enabled=false;var target=slot.mealVisual.GetComponent<StallInteractionTarget>();if(target!=null)target.enabled=false;}
                carriedProp=Group("Held counter meal "+mealId,cooking);
                carriedProp.SetPositionAndRotation(CounterRestPosition(slot,meal.packageId),Quaternion.identity);
                BuildPackageContents(carriedProp,meal.packageId,meal.product);
                Transform grip=Group("Handheld meal contact",carriedProp);grip.localPosition=PackageGripOffset(meal.packageId);grip.rotation=pickupRotation;
                if(!workActor.AttachVisualProp(carriedProp,grip)){workActor.Abort("Held counter meal was not at the reached palm contact");return;}
            },()=>
            {
                if(pick.right!=null)Destroy(pick.right.gameObject); // pickup 结束才销毁接触点
                // 取餐这段走位期间顾客可能离开：复核失败保留台餐（模型由 CompleteWork 恢复）。
                if(!IsOrderServable(orderId)||!TryGetGuestServicePoint(orderId,out stand,out hand)){failed?.Invoke();done();return;}
                Station serve=TransientServiceStation(stand,hand);
                Execute(VendorWorkAction.Serve,serve,.75f,()=>
                {
                    if(IsOrderServable(orderId))contact?.Invoke(); // ServeCounterMeal：移除 meal + 真实收款/风险
                    else failed?.Invoke();
                },()=>
                {
                    DisposeTransientServiceStation(serve);
                    workActor.ReleaseVisualProp();
                    done();
                });
            });
        }
        // CompleteWork 钩子：交付失败/动作中断后恢复被临时隐藏的槽位模型。
        private void RestoreCounterMealVisual()
        {
            if(hiddenMealSlot==null)return;
            if(hiddenMealSlot.mealVisual!=null&&hiddenMealSlot.mealId!=null)
            {
                foreach(Renderer renderer in hiddenMealSlot.mealVisual.GetComponentsInChildren<Renderer>())renderer.enabled=true;
                var target=hiddenMealSlot.mealVisual.GetComponent<StallInteractionTarget>();if(target!=null)target.enabled=true;
            }
            hiddenMealSlot=null;
        }
    }
}
