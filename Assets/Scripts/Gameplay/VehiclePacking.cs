using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Flags] public enum PackingTag
    {
        None=0,Small=1,Ingredient=2,Seasoning=4,Packaging=8,Core=16,Vessel=32,
        Energy=64,Cooler=128,External=256,Tool=512,DeckVessel=1024,Lock=2048,Umbrella=4096,All=8191
    }

    [Serializable] public sealed class ContainerSpec
    {
        public string id,label,source;
        public int columns,rows,stackUnits=1;
        // -1 means the source gives no numerical limit; it is not a made-up limit.
        public float weightLimitKg=-1,spoilageMultiplier=1;
        public PackingTag allowed;
        public bool requiresUpgrade;
        public ContainerSpec(string key,string name,int x,int y,PackingTag tags,string reference)
        {id=key;label=name;columns=x;rows=y;allowed=tags;source=reference;}
        public bool Allows(ItemPackingSpec item)=>item!=null&&(allowed&item.tags)!=0;
        public static ContainerSpec Bag()=>new ContainerSpec("bag","随身挎包",2,3,PackingTag.Small|PackingTag.Ingredient|PackingTag.Seasoning|PackingTag.Packaging,"设定 S2：2×3，可扩至3×3；未给固定承重");
        public static ContainerSpec ShoppingCart()=>new ContainerSpec("shopping_cart","超市购物车",3,6,PackingTag.All,"用户反馈调参：3×6（原文设定 S2：3×3），允许全部标签；未给固定承重");
        public static ContainerSpec Trike()=>new ContainerSpec("trike","Lv.1 车斗",4,6,PackingTag.Core|PackingTag.Ingredient|PackingTag.Cooler|PackingTag.External|PackingTag.DeckVessel|PackingTag.Energy|PackingTag.Vessel,"总表载具：4×6、100kg；能源库允许气罐/电瓶放车斗"){weightLimitKg=100};
        public static ContainerSpec UnderBay()=>new ContainerSpec("under_bay","能源暗仓",2,2,PackingTag.Energy,"设定 S2：2×2，改装解锁"){requiresUpgrade=true,spoilageMultiplier=-1};
        public static ContainerSpec ToolRack()=>new ContainerSpec("tool_rack","工具挂杆",6,1,PackingTag.Tool,"设定 S2：6钩，小工具不占台面网格"){spoilageMultiplier=-1};
        public static ContainerSpec Packaging()=>new ContainerSpec("packaging","包材弹夹槽",3,1,PackingTag.Packaging,"设定 S2：3组×50，可升级100"){stackUnits=50,spoilageMultiplier=-1};
        public static ContainerSpec Seasoning()=>new ContainerSpec("seasoning_rack","调味转盘架",6,1,PackingTag.Seasoning,"设定 S2：6格，腐败系数0.5"){spoilageMultiplier=.5f};
    }

    [Serializable] public sealed class ItemPackingSpec
    {
        public string id,source;
        public PackingTag tags;
        public int columns,rows;
        public float weightKg=-1;
        public string[] compatibleParents=Array.Empty<string>();
        public int socketUse=1;
        public bool HasGridFootprint=>columns>0&&rows>0;
        public ItemPackingSpec(string key,PackingTag kind,int x=0,int y=0,float mass=-1,string reference="原文未给网格/重量；需实物数据")
        {id=key;tags=kind;columns=x;rows=y;weightKg=mass;source=reference;}
    }

    public static class PackingCatalog
    {
        private static readonly Dictionary<string,ItemPackingSpec> items=Build();
        private static Dictionary<string,ItemPackingSpec> Build()
        {
            var d=new Dictionary<string,ItemPackingSpec>(StringComparer.Ordinal);
            int[,] coreCells={{1,1},{1,1},{1,2},{2,2},{1,2},{2,3},{2,2},{2,2}};
            float[] coreMass={9,3,12,22,10,18,14,22};
            for(int i=0;i<8;i++){string id="B"+(i+1).ToString("00");d[id]=new ItemPackingSpec(id,PackingTag.Core,coreCells[i,0],coreCells[i,1],coreMass[i],"内容库1.1：底座原文占格及kg");}
            float[] vesselMass={2.5f,5,9,16,6.5f,14,15,6,5.5f,10,2,8.5f,4,8,8,4.5f};
            string[][] parents={
                new[]{"B01","B02","B03","B04"},new[]{"B03","B04"},new[]{"B03","B04"},new[]{"B03","B04"},
                new[]{"B01","B02","B03","B04","B05"},new[]{"B04"},new[]{"B03","B05"},new[]{"B01","B02","B03","B04"},
                new[]{"B01","B02","B03","B04","B05","B06"},new[]{"B04","B06"},new[]{"V09","V10"},new[]{"B03","B04","B06"},
                new[]{"B02","B03","B04","B05"},new[]{"V13"},Array.Empty<string>(),Array.Empty<string>()};
            for(int i=0;i<16;i++)
            {
                string id="V"+(i+1).ToString("00");bool deck=i>=14;
                d[id]=new ItemPackingSpec(id,deck?PackingTag.DeckVessel:PackingTag.Vessel,deck?1:0,deck?(i==14?2:1):0,vesselMass[i],
                    "内容库1.2：锅具吸附子槽；V15为台面1×2，V16为台面1×1；其余未给储运网格")
                    {compatibleParents=parents[i],socketUse=i==5?2:1};
            }
            d["E01"]=new ItemPackingSpec("E01",PackingTag.Energy,1,1,-1,"内容库能源：车斗/底仓1×1；未给整箱kg");
            d["E02"]=new ItemPackingSpec("E02",PackingTag.Energy|PackingTag.Small,1,1,-1,"内容库能源：挎包/底仓1×1；未给整组kg");
            d["E03"]=new ItemPackingSpec("E03",PackingTag.Energy,1,1,-1,"内容库能源：车斗/底仓1×1；5kg是罐内燃气标称，未给罐体总质量");
            d["E04"]=new ItemPackingSpec("E04",PackingTag.Energy,1,2,31,"内容库能源：车斗/底仓1×2，总重31kg");
            d["E05"]=new ItemPackingSpec("E05",PackingTag.Energy,2,2,25,"内容库能源：车斗/底仓2×2，25kg");
            d["E06"]=new ItemPackingSpec("E06",PackingTag.Energy,1,2,12,"内容库能源：车斗/底仓1×2，12kg");
            d["E07"]=new ItemPackingSpec("E07",PackingTag.Cooler,1,2,-1,"内容库E07：车斗1×2，内部2格");
            d["E08"]=new ItemPackingSpec("E08",PackingTag.Cooler,2,2,-1,"内容库E08：车斗2×2，内部4格");
            d["E14"]=new ItemPackingSpec("E14",PackingTag.External,2,2,-1,"内容库E14：折叠外摆2×2");
            d["E10"]=new ItemPackingSpec("E10",PackingTag.Lock|PackingTag.Small,0,0,-1,"内容库E10：车体锁槽，上限2把，不占台面");
            d["E15"]=new ItemPackingSpec("E15",PackingTag.Umbrella,0,0,-1,"内容库E15：车侧伞架槽，不占台面");
            // SHOP7B minimal wiring: E13 (高音收款大喇叭) joins the market counter,
            // and every counter product must resolve a packing type or the real
            // CanAddSupply/CanFitCart path rejects it as "未登记占格类型". Authored
            // slot is the handlebar hook (车把挂件槽, 12% theft) with no deck
            // footprint — same treatment as E10/E15. Basket cell cost falls back to
            // the existing Small tuning like other small tools; no authored
            // transport rectangle is invented.
            d["E13"]=new ItemPackingSpec("E13",PackingTag.Tool|PackingTag.Small,0,0,-1,"内容库E13：车把挂件槽，不占台面");
            d["RiceBucket"]=new ItemPackingSpec("RiceBucket",PackingTag.DeckVessel,0,0,-1,"原文要求饭桶，未提供占格/重量，需明确补充数据");
            for(int i=1;i<=15;i++){string id="U"+i.ToString("00");d[id]=new ItemPackingSpec(id,PackingTag.Tool|PackingTag.Small,0,0,-1,"内容库1.3：工具挂杆独立挂钩，不占台面；挎包/购物车占格及kg未标");}
            for(int i=1;i<=12;i++){string id="P"+i.ToString("00");d[id]=new ItemPackingSpec(id,PackingTag.Packaging|PackingTag.Small,0,0,-1,"内容库1.4：每捆50，包材弹夹槽；通用网格与kg未标");}
            for(int i=0;i<=23;i++){string id="S"+i.ToString("00");d[id]=new ItemPackingSpec(id,PackingTag.Seasoning|PackingTag.Small,0,0,-1,"调味品使用调味架独立格；通用网格与kg未标");}
            foreach(string id in new[]{"rice","meat","egg","dough","skewer","prepared","fruit","seasoning"})
                d[id]=new ItemPackingSpec(id,PackingTag.Ingredient,1,1,-1,"交互架构1.2：标准食材周转筐1×1；本箱总质量待配置");
            return d;
        }
        public static ItemPackingSpec Find(string id)=>id!=null&&items.TryGetValue(id,out ItemPackingSpec item)?item:null;
    }

    [Serializable] public sealed class PackingItem
    {
        public string instanceId,itemId;
        // Metres in the object's upright root axes, including its existing scale.
        public Bounds bounds;
        // Actual flat cabinet surface, measured independently of handles/controls.
        public bool hasTopSupport;
        public Bounds topSupportBounds;
        public bool hasBottomSupport;
        public Bounds bottomSupportBounds;
        public float measuredWeightKg=-1;
        public Vector2Int gridOverride;
        public int units=1;
        public ItemPackingSpec specification;
        public ItemPackingSpec Spec=>specification??PackingCatalog.Find(itemId);
        public float WeightKg=>measuredWeightKg>=0?measuredWeightKg:Spec?.weightKg??-1;
        public static PackingItem Measure(string instanceId,string itemId,Transform model,float measuredWeightKg=-1)
        {
            bool any=false;Bounds box=new Bounds(),top=new Bounds();bool hasTop=false;Quaternion inverse=Quaternion.Inverse(model.rotation);
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;
                // Mesh-local bounds avoid inflating a rotated object's renderer AABB.
                Bounds local=renderer.localBounds;Bounds part=new Bounds();bool partFirst=true;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 p=local.center+Vector3.Scale(local.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    p=inverse*(renderer.transform.TransformPoint(p)-model.position);
                    if(!any){box=new Bounds(p,Vector3.zero);any=true;}else box.Encapsulate(p);
                    if(partFirst){part=new Bounds(p,Vector3.zero);partFirst=false;}else part.Encapsulate(p);
                }
                if((itemId=="B07"||itemId=="B08")&&renderer.name=="EnamelCabinet"){top=part;hasTop=true;}
            }
            if(!any)throw new ArgumentException("Cannot pack a model without actual renderer bounds: "+instanceId);
            var result=new PackingItem{instanceId=instanceId,itemId=itemId,bounds=box,measuredWeightKg=measuredWeightKg,hasTopSupport=hasTop,topSupportBounds=top};
            if(itemId=="RiceBucket"&&RiceBucketContactGeometry.TryMeasure(model,out Bounds bottom))
            {
                result.hasBottomSupport=true;
                result.bottomSupportBounds=bottom;
            }
            return result;
        }
    }

    [Serializable] public sealed class PackingZone
    {
        public string id,containerId;
        public Bounds interior;
        public PackingTag allowed;
        public bool requiresUnderBay;
        public PackingZone(string name,string container,Bounds volume,PackingTag tags,bool upgraded=false)
        {id=name;containerId=container;interior=volume;allowed=tags;requiresUnderBay=upgraded;}
    }
    [Serializable] public sealed class VehiclePackingGeometry
    {
        public Vector3 origin;
        public Quaternion rotation=Quaternion.identity;
        public readonly List<PackingZone> zones=new List<PackingZone>();
        public readonly List<Bounds> obstacles=new List<Bounds>();
        public readonly Dictionary<string,ContainerSpec> containers=new Dictionary<string,ContainerSpec>();
        public readonly Dictionary<string,Vector3> preferredPoints=new Dictionary<string,Vector3>();
        public float vehicleWeightLimitKg=100;
        public Vector3 ToLocal(Vector3 point)=>Quaternion.Inverse(rotation)*(point-origin);
        public Vector3 ToWorld(Vector3 point)=>origin+rotation*point;
        public void AddZone(PackingZone zone,ContainerSpec container){zones.Add(zone);containers[container.id]=container;}
        public void AddUnderBay(Bounds measuredInteriorInPackingFrame)
        {AddZone(new PackingZone("under_bay","under_bay",measuredInteriorInPackingFrame,PackingTag.Energy,true),ContainerSpec.UnderBay());}

        // These dimensions are measured for the current CartAI, not inferred from
        // the logical 4×6 cells. Positions and heading come from imported nodes.
        public static VehiclePackingGeometry FromCartAI(Transform vehicle)
        {
            Transform deck=Find(vehicle,"CartDeck"),stove=Find(vehicle,"StoveLoadedPlacement");
            Transform rear0=Find(vehicle,"CargoSlot0"),rear1=Find(vehicle,"CargoSlot1"),rear2=Find(vehicle,"CargoSlot2");
            if(deck==null||stove==null||rear0==null||rear1==null||rear2==null)throw new ArgumentException("CartAI packing requires CartDeck, StoveLoadedPlacement and CargoSlot0..2.");
            Vector3 rear=(rear0.position+rear1.position+rear2.position)/3;
            Vector3 longitudinal=rear-deck.position;longitudinal.y=0;
            if(longitudinal.sqrMagnitude<.01f)throw new ArgumentException("Cannot derive vehicle direction from coincident deck/rear anchors.");
            var g=new VehiclePackingGeometry{origin=deck.position,rotation=Quaternion.LookRotation(longitudinal.normalized,Vector3.up)};
            ContainerSpec trike=ContainerSpec.Trike();
            // art-v8/cart-rack/build_rack.py: usable deck source x .37..1.51,
            // y ±.42; measured rear clear interior .432 ×1.152, floor at anchors.
            g.AddZone(new PackingZone("main_deck","trike",new Bounds(new Vector3(0,.7f,0),new Vector3(.84f,1.4f,1.14f)),trike.allowed),trike);
            Vector3 rearLocal=g.ToLocal(rear);float rearHeight=deck.position.y-rear.y-.025f;
            if(rearHeight<=0)throw new ArgumentException("Rear basket floor must be below the measured deck.");
            g.AddZone(new PackingZone("rear_basket","trike",new Bounds(rearLocal+Vector3.up*(rearHeight*.5f),new Vector3(1.152f,rearHeight,.432f)),PackingTag.Ingredient),trike);
            g.preferredPoints["core"]=g.ToLocal(stove.position);
            g.preferredPoints["rear0"]=g.ToLocal(rear0.position);g.preferredPoints["rear1"]=g.ToLocal(rear1.position);g.preferredPoints["rear2"]=g.ToLocal(rear2.position);
            if(vehicle.GetComponent<VehicleContainerModules>()!=null)VehicleContainerModules.ContributeGeometry(g);
            // No invented space inside Collider_RearBody. A measured, unlocked
            // under-bay and authored tool/packaging zones must be supplied explicitly.
            return g;
        }
        private static Transform Find(Transform root,string name)
        {foreach(Transform child in root.GetComponentsInChildren<Transform>(true))if(child.name==name)return child;return null;}
    }

    [Serializable] public sealed class PackingOptions
    {
        public bool underBayUnlocked;
        public bool transportMode=true;
        // Explicit v10 transport tuning; the source gives vessel sockets, not a
        // universal shopping/transport rectangle for loose spare pots.
        public Vector2Int spareVesselTransportCells=Vector2Int.one;
        public bool allowEnergyOnDeck=true,allowIngredientsOnDeck=true;
        public bool requireKnownWeights=true;
        public int searchBudget=50000;
        public float clearance=.005f;
    }
    [Serializable] public sealed class PackedPlacement
    {
        public string instanceId,itemId,zoneId,containerId,parentInstanceId,supportInstanceId;
        public Vector3 position;
        public Quaternion rotation;
        public Bounds boundsInPackingFrame;
        public RectInt logicalCells;
        public int quarterTurns;
        // Apply changes pose only. Original mesh/transform scale is never changed.
        public void Apply(Transform model)=>model.SetPositionAndRotation(position,rotation);
    }
    [Serializable] public sealed class PackingPlan
    {
        public bool success,weightComplete,searchExhausted;
        public string failureCode="",reason="";
        public float knownWeightKg;
        public int searchedCandidates;
        public readonly List<string> unknownWeightItems=new List<string>();
        public readonly List<PackedPlacement> placements=new List<PackedPlacement>();
        public PackedPlacement Find(string instanceId)=>placements.Find(p=>p.instanceId==instanceId);
        public bool FullyValidated=>success&&weightComplete;
    }

    public static class VehiclePacking
    {
        public static PackingPlan Plan(IReadOnlyList<PackingItem> input,VehiclePackingGeometry geometry,PackingOptions options=null)
        {
            var plan=new PackingPlan();options=options??new PackingOptions();
            if(input==null||geometry==null){plan.failureCode="MissingInput";plan.reason="缺少物品或实际容器几何。";return plan;}
            var keys=new HashSet<string>();var items=new List<PackingItem>();
            foreach(PackingItem item in input)
            {
                if(item==null||string.IsNullOrEmpty(item.instanceId)||!keys.Add(item.instanceId)||item.Spec==null)
                {plan.failureCode="UnknownOrDuplicateItem";plan.reason="物品实例ID重复，或没有已配置的容器类型数据。";return plan;}
                if(item.bounds.size.x<=0||item.bounds.size.y<=0||item.bounds.size.z<=0)
                {plan.failureCode="MissingMeasuredBounds";plan.reason=item.instanceId+"没有真实三维包络。";return plan;}
                if(item.WeightKg<0)plan.unknownWeightItems.Add(item.instanceId);else plan.knownWeightKg+=item.WeightKg;
                items.Add(item);
            }
            plan.weightComplete=plan.unknownWeightItems.Count==0;
            if(plan.knownWeightKg>geometry.vehicleWeightLimitKg)
            {plan.failureCode="Overweight";plan.reason="已知总质量 "+plan.knownWeightKg.ToString("0.##")+"kg 超过载具 "+geometry.vehicleWeightLimitKg.ToString("0.##")+"kg。";return plan;}
            if(options.requireKnownWeights&&!plan.weightComplete)
            {plan.failureCode="UnspecifiedWeight";plan.reason="缺少整件质量，不能宣称已通过承重验收："+string.Join("、",plan.unknownWeightItems);return plan;}
            items.Sort((a,b)=>{int priority=Priority(a.Spec).CompareTo(Priority(b.Spec));return priority!=0?priority:(b.bounds.size.x*b.bounds.size.z).CompareTo(a.bounds.size.x*a.bounds.size.z)!=0?
                (b.bounds.size.x*b.bounds.size.z).CompareTo(a.bounds.size.x*a.bounds.size.z):string.CompareOrdinal(a.instanceId,b.instanceId);});
            var solver=new Solver(items,geometry,options,plan);
            plan.success=solver.Place(0);
            if(!plan.success)
            {
                plan.placements.Clear();plan.searchExhausted=plan.searchedCandidates>=options.searchBudget;
                plan.failureCode=plan.searchExhausted?"SearchBudgetExceeded":"NoSupportedLayout";
                plan.reason=plan.searchExhausted?"布局搜索预算已用完；不能据此断言物理上绝无解。":solver.LastFailure;
            }
            else if(!plan.weightComplete)plan.reason="占格和实物布局通过；仍缺整件质量："+string.Join("、",plan.unknownWeightItems);
            return plan;
        }
        private static int Priority(ItemPackingSpec s)=>(s.tags&PackingTag.Core)!=0?0:(s.tags&PackingTag.DeckVessel)!=0?1:(s.tags&PackingTag.Energy)!=0?2:
            (s.tags&PackingTag.Cooler)!=0?3:(s.tags&PackingTag.Vessel)!=0?(Array.Exists(s.compatibleParents,p=>p.StartsWith("V",StringComparison.Ordinal))?5:4):(s.tags&PackingTag.Ingredient)!=0?6:7;

        private sealed class Solver
        {
            private readonly List<PackingItem> items;private readonly VehiclePackingGeometry g;private readonly PackingOptions options;private readonly PackingPlan plan;
            public string LastFailure="真实容器空间不足或没有兼容容器。";
            public Solver(List<PackingItem> source,VehiclePackingGeometry geometry,PackingOptions settings,PackingPlan result){items=source;g=geometry;options=settings;plan=result;}
            public bool Place(int index)
            {
                if(index>=items.Count)return true;if(plan.searchedCandidates>=options.searchBudget)return false;
                PackingItem item=items[index];ItemPackingSpec spec=item.Spec;
                if(options.transportMode&&item.itemId=="RiceBucket"&&PlaceCabinetTop(index,item))return true;
                bool vessel=(spec.tags&PackingTag.Vessel)!=0;
                if(vessel)
                {
                    if(!(options.transportMode&&item.itemId=="V08")&&PlaceVessel(index,item))return true;
                    if(!options.transportMode)return false;
                    if(PlaceTransportStack(index,item))return true;
                }
                var zones=new List<PackingZone>(g.zones);
                zones.Sort((a,b)=>ZonePriority(a,spec).CompareTo(ZonePriority(b,spec)));
                bool compatible=false;
                foreach(PackingZone zone in zones)
                {
                    if((zone.allowed&spec.tags)==0 && !(vessel&&options.transportMode&&(zone.id=="main_deck"||zone.id=="rear_basket")))continue;
                    if(zone.requiresUnderBay&&!options.underBayUnlocked)continue;
                    if(zone.id=="main_deck"&&((spec.tags&PackingTag.Energy)!=0&&!options.allowEnergyOnDeck||(spec.tags&PackingTag.Ingredient)!=0&&!options.allowIngredientsOnDeck))continue;
                    compatible=true;
                    if(!g.containers.TryGetValue(zone.containerId,out ContainerSpec container)||!container.Allows(spec))continue;
                    if(item.units>container.stackUnits&&(spec.tags&PackingTag.Packaging)!=0){LastFailure=item.itemId+"超过单组包材数量，请拆成独立捆实例。";continue;}
                    for(int turn=0;turn<2;turn++)
                    {
                        Vector3 size=RotatedSize(item.bounds.size,turn);
                        if(size.x+options.clearance*2>zone.interior.size.x||size.z+options.clearance*2>zone.interior.size.z||size.y>zone.interior.size.y)continue;
                        if(!TryCells(item,container,turn,out RectInt cells)){LastFailure=item.itemId+"的原文占格在"+container.label+"中放不下，或尚未配置通用网格占格。";continue;}
                        foreach(Vector3 center in Candidates(zone,item,size,turn))
                        {
                            if(plan.searchedCandidates++>=options.searchBudget)return false;
                            Bounds bounds=new Bounds(center,size);if(!Inside(bounds,zone.interior)||!Clear(bounds))continue;
                            PackedPlacement placed=Placement(item,zone.id,zone.containerId,bounds,turn,cells,"");
                            plan.placements.Add(placed);if(Place(index+1))return true;plan.placements.RemoveAt(plan.placements.Count-1);
                        }
                    }
                }
                if(!compatible)LastFailure=item.itemId+"没有已测量的兼容容器（底仓需解锁及实际空腔，工具/包材需独立挂槽几何）。";
                return false;
            }
            private bool PlaceVessel(int index,PackingItem item)
            {
                var parents=new List<PackedPlacement>(plan.placements);
                foreach(PackedPlacement parent in parents)
                {
                    if(Array.IndexOf(item.Spec.compatibleParents,parent.itemId)<0)continue;
                    int capacity=parent.itemId=="B04"?2:1,used=0;
                    foreach(var p in plan.placements)if(p.parentInstanceId==parent.instanceId)used+=items.Find(i=>i.instanceId==p.instanceId).Spec.socketUse;
                    if(used+item.Spec.socketUse>capacity)continue;
                    PackingZone zone=g.zones.Find(z=>z.id=="main_deck");if(zone==null)continue;
                    for(int socket=0;socket<capacity;socket++)for(int turn=0;turn<2;turn++)
                    {
                        Vector3 size=RotatedSize(item.bounds.size,turn),center=parent.boundsInPackingFrame.center;
                        if(capacity>1&&item.Spec.socketUse==1)
                        {
                            bool alongX=parent.boundsInPackingFrame.size.x>=parent.boundsInPackingFrame.size.z;
                            float offset=(socket==0?-1:1)*(alongX?parent.boundsInPackingFrame.size.x:parent.boundsInPackingFrame.size.z)*.29f;
                            center+=alongX?Vector3.right*offset:Vector3.forward*offset;
                        }
                        center.y=parent.boundsInPackingFrame.max.y+options.clearance+size.y*.5f;
                        Bounds bounds=new Bounds(center,size);if(plan.searchedCandidates++>=options.searchBudget)return false;
                        if(!Inside(bounds,zone.interior)||!Clear(bounds))continue;
                        var placed=Placement(item,"vessel_socket","vessel_socket",bounds,turn,new RectInt(-1,-1,0,0),parent.instanceId);
                        plan.placements.Add(placed);if(Place(index+1))return true;plan.placements.RemoveAt(plan.placements.Count-1);
                    }
                }
                LastFailure=item.itemId+"没有兼容且空间足够的底座/锅具子槽。";return false;
            }
            private bool PlaceCabinetTop(int index,PackingItem item)
            {
                // Explicit transport tuning: a <=8 kg insulated bucket can rest
                // on a measured closed cabinet, never a burner or battery terminal.
                // Its lid may overhang the supporting body footprint by <=1.5 cm.
                if(item.WeightKg<0||item.WeightKg>8)return false;
                PackingZone zone=g.zones.Find(z=>z.id=="main_deck");
                if(zone==null||!g.containers.TryGetValue("trike",out ContainerSpec container))return false;
                foreach(var support in new List<PackedPlacement>(plan.placements))
                {
                    PackingItem supportItem=items.Find(i=>i.instanceId==support.instanceId);
                    if(supportItem==null||!supportItem.hasTopSupport)continue;
                    Quaternion supportRotation=Quaternion.Euler(0,support.quarterTurns*90,0);
                    Vector3 supportRoot=g.ToLocal(support.position);
                    Bounds surface=new Bounds(supportRoot+supportRotation*supportItem.topSupportBounds.center,RotatedSize(supportItem.topSupportBounds.size,support.quarterTurns));
                    for(int turn=0;turn<2;turn++)
                    {
                        if(!TryCells(item,container,turn,out RectInt cells))continue;
                        Vector3 size=RotatedSize(item.bounds.size,turn);
                        Vector3 center=surface.center;
                        if(item.hasBottomSupport)
                        {
                            Vector3 footSize=RotatedSize(item.bottomSupportBounds.size,turn);
                            if(footSize.x>surface.size.x-.002f||footSize.z>surface.size.z-.002f)continue;
                            Vector3 footOffset=Quaternion.Euler(0,turn*90,0)*(item.bottomSupportBounds.center-item.bounds.center);
                            center.x-=footOffset.x;center.z-=footOffset.z;
                        }
                        else if(size.x>surface.size.x+.03f||size.z>surface.size.z+.03f)continue;
                        center.y=surface.max.y+options.clearance+size.y*.5f;
                        Bounds bounds=new Bounds(center,size);if(plan.searchedCandidates++>=options.searchBudget)return false;
                        if(!Inside(bounds,zone.interior)||!Clear(bounds))continue;
                        var placed=Placement(item,"cabinet_transport_top","trike",bounds,turn,cells,"");placed.supportInstanceId=support.instanceId;
                        plan.placements.Add(placed);if(Place(index+1))return true;plan.placements.RemoveAt(plan.placements.Count-1);
                    }
                }
                return false;
            }
            private bool PlaceTransportStack(int index,PackingItem item)
            {
                if(item.itemId!="V01"&&item.itemId!="V02"&&item.itemId!="V05")return false;
                PackingZone zone=g.zones.Find(z=>z.id=="main_deck");
                if(zone==null||!g.containers.TryGetValue("trike",out ContainerSpec container))return false;
                var supports=new List<PackedPlacement>(plan.placements);
                foreach(var support in supports)
                {
                    if(support.itemId!="V01"&&support.itemId!="V02"&&support.itemId!="V05")continue;
                    // Empty spare pans may be stacked for transport. This is a
                    // supported physical stack, NOT a second working burner socket.
                    for(int turn=0;turn<2;turn++)
                    {
                        if(!TryCells(item,container,turn,out RectInt cells))continue;
                        Vector3 size=RotatedSize(item.bounds.size,turn);
                        if(size.x>support.boundsInPackingFrame.size.x+.03f||size.z>support.boundsInPackingFrame.size.z+.03f)continue;
                        Vector3 center=support.boundsInPackingFrame.center;center.y=support.boundsInPackingFrame.max.y+options.clearance+size.y*.5f;
                        Bounds bounds=new Bounds(center,size);if(plan.searchedCandidates++>=options.searchBudget)return false;
                        if(!Inside(bounds,zone.interior)||!Clear(bounds))continue;
                        var placed=Placement(item,"transport_stack","trike",bounds,turn,cells,"");placed.supportInstanceId=support.instanceId;
                        plan.placements.Add(placed);if(Place(index+1))return true;plan.placements.RemoveAt(plan.placements.Count-1);
                    }
                }
                return false;
            }
            private int ZonePriority(PackingZone zone,ItemPackingSpec spec)
            {if((spec.tags&PackingTag.Energy)!=0)return zone.id=="under_bay"?0:1;if((spec.tags&PackingTag.Ingredient)!=0)return zone.id=="rear_basket"?0:1;return zone.id=="main_deck"?0:1;}
            private bool TryCells(PackingItem item,ContainerSpec container,int turn,out RectInt cells)
            {
                Vector2Int size=item.gridOverride.x>0&&item.gridOverride.y>0?item.gridOverride:new Vector2Int(item.Spec.columns,item.Spec.rows);
                if(container.id=="tool_rack"||container.id=="packaging"||container.id=="seasoning_rack"||container.id=="lock_slots"||container.id=="umbrella_slot")size=Vector2Int.one;
                if((item.Spec.tags&PackingTag.Vessel)!=0&&options.transportMode&&size.x<=0)size=options.spareVesselTransportCells;
                if(turn%2==1)size=new Vector2Int(size.y,size.x);
                cells=new RectInt(-1,-1,0,0);if(size.x<=0||size.y<=0)return false;
                for(int y=0;y<=container.rows-size.y;y++)for(int x=0;x<=container.columns-size.x;x++)
                {
                    var trial=new RectInt(x,y,size.x,size.y);bool available=true;
                    foreach(var p in plan.placements)if(p.containerId==container.id&&p.logicalCells.Overlaps(trial)){available=false;break;}
                    if(available){cells=trial;return true;}
                }
                return false;
            }
            private IEnumerable<Vector3> Candidates(PackingZone zone,PackingItem item,Vector3 size,int parentTurn)
            {
                float bottom=zone.containerId=="tool_rack"||zone.containerId=="lock_slots"?zone.interior.max.y-size.y-options.clearance:zone.interior.min.y+options.clearance;
                var xs=new List<float>{zone.interior.min.x+size.x*.5f+options.clearance,zone.interior.center.x,zone.interior.max.x-size.x*.5f-options.clearance};
                var zs=new List<float>{zone.interior.min.z+size.z*.5f+options.clearance,zone.interior.center.z,zone.interior.max.z-size.z*.5f-options.clearance};
                if(zone.id=="main_deck"&&(item.Spec.tags&PackingTag.Core)!=0)
                {
                    foreach(PackingItem child in items)
                    {
                        ItemPackingSpec childSpec=child.Spec;
                        if(childSpec==null||(childSpec.tags&PackingTag.Vessel)==0||childSpec.compatibleParents==null||Array.IndexOf(childSpec.compatibleParents,item.itemId)<0)continue;
                        for(int turn=0;turn<2;turn++)
                        {
                            float halfZ=RotatedSize(child.bounds.size,turn).z*.5f;
                            zs.Add(zone.interior.min.z+halfZ+options.clearance);
                            zs.Add(zone.interior.max.z-halfZ-options.clearance);
                        }
                    }
                }
                if(zone.id=="main_deck" && item.hasTopSupport)
                {
                    Quaternion parentRotation=Quaternion.Euler(0,parentTurn*90,0);
                    Vector3 parentOffset=parentRotation*(item.bounds.center-item.topSupportBounds.center);
                    foreach(PackingItem child in items)
                    {
                        if(child.itemId!="RiceBucket" || !child.hasBottomSupport)continue;
                        for(int childTurn=0;childTurn<2;childTurn++)
                        {
                            Quaternion childRotation=Quaternion.Euler(0,childTurn*90,0);
                            Vector3 footOffset=childRotation*(child.bottomSupportBounds.center-child.bounds.center);
                            Vector3 childSize=RotatedSize(child.bounds.size,childTurn);
                            xs.Add(zone.interior.min.x+childSize.x*.5f+options.clearance+footOffset.x+parentOffset.x);
                            xs.Add(zone.interior.max.x-childSize.x*.5f-options.clearance+footOffset.x+parentOffset.x);
                            zs.Add(zone.interior.min.z+childSize.z*.5f+options.clearance+footOffset.z+parentOffset.z);
                            zs.Add(zone.interior.max.z-childSize.z*.5f-options.clearance+footOffset.z+parentOffset.z);
                        }
                    }
                }
                foreach(var point in g.preferredPoints.Values){xs.Add(point.x);zs.Add(point.z);}
                foreach(var p in plan.placements)
                {xs.Add(p.boundsInPackingFrame.max.x+size.x*.5f+options.clearance);xs.Add(p.boundsInPackingFrame.min.x-size.x*.5f-options.clearance);zs.Add(p.boundsInPackingFrame.max.z+size.z*.5f+options.clearance);zs.Add(p.boundsInPackingFrame.min.z-size.z*.5f-options.clearance);}
                // Bottom-left and edge-aligned candidates avoid a guessed physical
                // tile size; the source grid and the measured shape are both checked.
                xs.Sort();zs.Sort();
                for(int i=xs.Count-1;i>0;i--)if(xs[i]-xs[i-1]<.0001f)xs.RemoveAt(i);
                for(int i=zs.Count-1;i>0;i--)if(zs[i]-zs[i-1]<.0001f)zs.RemoveAt(i);
                // Start cores at the imported stove point. Edge-first placement
                // stranded wider child pans off the deck and exhausted the budget
                // before ever trying this valid central working socket.
                if((item.Spec.tags&PackingTag.Core)!=0&&g.preferredPoints.TryGetValue("core",out Vector3 anchor))
                {
                    xs.Sort((a,b)=>{int near=Mathf.Abs(a-anchor.x).CompareTo(Mathf.Abs(b-anchor.x));return near!=0?near:a.CompareTo(b);});
                    zs.Sort((a,b)=>{int near=Mathf.Abs(a-anchor.z).CompareTo(Mathf.Abs(b-anchor.z));return near!=0?near:a.CompareTo(b);});
                }
                foreach(float z in zs)foreach(float x in xs)
                {
                    Vector3 center=new Vector3(x,bottom+size.y*.5f,z);
                    // A preferred point from another compartment is not a candidate
                    // in this volume. Prune before spending geometric search budget.
                    if(Inside(new Bounds(center,size),zone.interior))yield return center;
                }
            }
            private bool Inside(Bounds b,Bounds container)
            {return b.min.x>=container.min.x+options.clearance-.00001f&&b.max.x<=container.max.x-options.clearance+.00001f&&b.min.z>=container.min.z+options.clearance-.00001f&&b.max.z<=container.max.z-options.clearance+.00001f&&b.min.y>=container.min.y-.00001f&&b.max.y<=container.max.y+.00001f;}
            private bool Clear(Bounds bounds)
            {
                foreach(Bounds obstacle in g.obstacles)if(Overlap(bounds,obstacle,options.clearance))return false;
                foreach(var placed in plan.placements)if(Overlap(bounds,placed.boundsInPackingFrame,options.clearance))return false;
                return true;
            }
            private PackedPlacement Placement(PackingItem item,string zone,string container,Bounds bounds,int turn,RectInt cells,string parent)
            {
                Quaternion localRotation=Quaternion.Euler(0,turn*90,0);
                Vector3 rootInFrame=bounds.center-localRotation*item.bounds.center;
                return new PackedPlacement{instanceId=item.instanceId,itemId=item.itemId,zoneId=zone,containerId=container,parentInstanceId=parent,
                    position=g.ToWorld(rootInFrame),rotation=g.rotation*localRotation,boundsInPackingFrame=bounds,quarterTurns=turn,logicalCells=cells};
            }
        }
        private static Vector3 RotatedSize(Vector3 size,int turn)=>turn%2==0?size:new Vector3(size.z,size.y,size.x);
        public static bool Overlap(Bounds a,Bounds b,float gap=0)
        {return a.min.x<b.max.x+gap-.00001f&&a.max.x>b.min.x-gap+.00001f&&a.min.y<b.max.y+gap-.00001f&&a.max.y>b.min.y-gap+.00001f&&a.min.z<b.max.z+gap-.00001f&&a.max.z>b.min.z-gap+.00001f;}
    }
}
