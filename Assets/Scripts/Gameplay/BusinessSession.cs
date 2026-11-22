using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    public sealed class BusinessSetup
    {
        public int cashCents,seed=12017,coalPoints,gasPoints,cartridgePoints,electricityPoints,fuelPoints;
        public bool stoveLoaded,practice,canReturnMarket=true;
        public float dayDuration=420;
        public int queueCapacity=4;
        // Physical single-vendor tuning, calibrated separately from the abstract
        // economy model's 10 s interval. Heat windows and patience stay unchanged.
        public float customerIntervalSeconds=16;
        public float clearDownSeconds=35,lastCallNoticeSeconds=15;
        public int patienceMinSeconds=35,patienceMaxSeconds=54;
        public RunProfile profile;
        public Dictionary<string,int> inventory=new Dictionary<string,int>();
        public HashSet<string> equipment=new HashSet<string>();
        public static BusinessSetup MidgamePractice()
        {
            return new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=120000,seed=12017,dayDuration=300,gasPoints=45,customerIntervalSeconds=16,patienceMinSeconds=35,patienceMaxSeconds=54,
                equipment=new HashSet<string>{"B04","V02","V05","U02","U05","U15","V15","V16","U12","U13"},
                inventory=new Dictionary<string,int>{{"rice",60},{"egg",48},{"dough",45},{"skewer",45},{"seasoning",80},{"fruit",40},{"P05",50},{"P04",50},{"P08",50},
                    // FLAVOR11：练习档带少量真实风味库存，供选瓶/递餐全链路验收。
                    {"S01",20},{"S02",15},{"S05",20}}};
        }
        public BusinessSetup Copy()
        {
            return new BusinessSetup{cashCents=cashCents,seed=seed,coalPoints=coalPoints,gasPoints=gasPoints,cartridgePoints=cartridgePoints,electricityPoints=electricityPoints,fuelPoints=fuelPoints,
                stoveLoaded=stoveLoaded,practice=practice,canReturnMarket=canReturnMarket,dayDuration=dayDuration,queueCapacity=queueCapacity,customerIntervalSeconds=customerIntervalSeconds,clearDownSeconds=clearDownSeconds,lastCallNoticeSeconds=lastCallNoticeSeconds,patienceMinSeconds=patienceMinSeconds,patienceMaxSeconds=patienceMaxSeconds,
                inventory=new Dictionary<string,int>(inventory),equipment=new HashSet<string>(equipment),
                profile=profile==null?null:new RunProfile{careerId=profile.careerId,selectedTraits=(string[])profile.selectedTraits.Clone(),debtDifficulty=profile.debtDifficulty}};
        }
    }
    [Serializable] public sealed class BusinessInventoryAudit { public string id;public int initial,remaining,used; }
    [Serializable] public sealed class BusinessOrderAudit { public int id,lane,reservedLane;public bool arrived;public string recipe,status,departureReason,requestedFlavorId,servedFlavorId,productId,productName;public float patience,maxPatience,spawnedAt,arrivedAt,startedAt,finishedAt; }
    [Serializable] public sealed class BusinessLaneAudit { public int index,orderId,reservedOrderId,batchRemaining,toolClicks,prepaidSeasoning,eggPrepPortions;public string recipe,step,energy;public float remaining,duration;public bool seasoned; }
    [Serializable] public sealed class BusinessRecipeAudit { public string recipe;public int batches,portionsStarted,served,wasted,remaining;public bool portionsConserved; }
    [Serializable] public sealed class BusinessModelAudit { public string model;public int activeRenderers,inFrustumRenderers,renderedLastFrame;public float[] worldCenter,worldSize,viewportMinMax; }
    [Serializable] public sealed class BusinessAuditState
    {
        public string schema="guochao.business.audit.v2",scenario,phase,servicePhase,district,careerId;
        public bool acceptingOrders;
        public bool started,finished,paused,opening,settlementPublished,cashConserved,packagesConserved,nonNegativeInventory,nonNegativeEnergy,cameraActive,canReturnMarket;
        public int arrivalAttempts,queueTurnaways,soldOutArrivals;
        public int unseasonedPortionsServed,integrityChange,emergencyPackagesUsed,packagingScoreDelta;
        public int seed,randomDraws,orderSerial,initialCashCents,cashCents,revenueCents,served,lost,wastedPortions,gas,cartridge,coal,electricity,gasUsed,cartridgeUsed,coalUsed,electricityUsed,packageUnitsUsed,visibleCustomerActors,walkingCustomerActors,settlementPublications;
        // RISK7：A01 风险收款审计（只增事实计数）。lostRevenueCents=坏账面额累计，
        // 不冲减已实收现金。
        public int riskEventsIssued,riskEventsRecovered,riskEventsLost,lostRevenueCents;
        // B05：共享单车堵摊审计状态（独立于 A01 债权计数）。
        public bool bikeBlockActive,bikeBlockTriggered;
        public int bikeBlockRemaining;
        public float bikeBlockPollRemaining,customerArrivalRateMultiplier;
        public float elapsed,remaining,duration,nextCustomerSeconds,customerIntervalSeconds,lastOrderAt,clearDownSeconds;
        public int patienceMinSeconds,patienceMaxSeconds;
        public string[] selectedRecipes,equipment,selectedTraits;
        public BusinessInventoryAudit[] inventory;
        public BusinessOrderAudit[] orders;
        public BusinessLaneAudit[] lanes;
        public BusinessModelAudit[] sceneModels;
        public CounterMeal[] readyMeals;
        public CounterMeal heldMeal;
        public FreeKitchenBatch[] freeBatches;
        public BusinessRecipeAudit[] recipeProduction;
    }
    public sealed class StallRecipe
    {
        public string id,name,family,toolLabel;
        public string[] vessels,tools,packages;
        public Dictionary<string,int> ingredients;
        public int priceCents,batchSize;
        public float heatSeconds,greenSeconds;
        public bool microwave,cold;
    }
    public sealed class StallDistrict
    {
        public string name,description,regulation;
        public float[] weights;
        public StallDistrict(string n,string d,string r,float[] w){name=n;description=d;regulation=r;weights=w;}
    }
    public enum KitchenStep { Idle,Prep,Heating,Utensil,Season,Pack,ReadyStock,Dirty }
    public enum BusinessServicePhase { Planning,Open,LastCall,Clearing,Closed }
    [Serializable] public sealed class StallOrder
    {
        public int id,recipeIndex,lane=-1,reservedLane=-1;
        public float spawnedAt,arrivedAt=-1,startedAt=-1,finishedAt=-1;
        public string departureReason="";
        public float patience,maxPatience;
        public bool arrived,departed,served;
        public string customer;
        // RISK7 单次支付事实：paidCents=本单实际已收现金（正常成交=菜价；A01 采纳后
        // 为 0，追回后=实际追回额含暴躁双倍；坏账恒 0）。riskSerial=0 非风险单；
        // >0 = 对应 A01 事件序号。shortSeasoning 复刻出餐时缺油盐事实，追回结算
        // 重放 SaleCompleted 时不丢。旧档三字段缺省 0，由 Restore 迁移补齐。
        public int paidCents,riskSerial;
        public bool shortSeasoning;
        public bool emergencyPackage; // 旧档默认 false，真实售出的备用袋事实
        // FLAVOR11：requestedFlavorId=顾客点单时指定的风味（""=原味、不加价）。
        // 缺省 "" 保证旧档/旧 JSON 天然兼容。仅在玩家真实选瓶且与点单一致时才
        // 在递餐帧扣 1 份风味并加价；servedFlavorId=本单实际加上的风味（收据用）。
        public string requestedFlavorId="",servedFlavorId="";
        // FREEDOM19：交付时记录的真实成品事实（散客/泛称成品与旧菜谱订单同档保存）。
        // 收束语/收据/小票在批次清空后仍读这些字段，绝不回退到 visualRecipeIndex。
        // 旧档缺字段（JsonUtility 补 ""/0）= 旧菜谱订单，语义不变。
        public string productId="",productName="",productMethod="",productFamily="";
        public int productPriceCents;
        // Optional presentation/verification snapshot for newly created product orders.
        // Old saves omit it; stored product identity and quote never change.
        public FoodInput[] productInputs=Array.Empty<FoodInput>();
        public int productSourcePortions;
    }
    // RISK7（EVENT7R 冻结 DTO，字段名不得改）：Serve 前风险闸门决策。
    [Serializable] public sealed class StallRiskGateDecision
    {
        public bool adoptAsRisk;       // 是否采纳本次 Serve 为风险收款
        public int collectCents;       // 拦截成功实收（接线层用 StallRiskRules.ComputeCollectCents 算好）
        public float interceptSeconds; // 拦截窗口（接线层用 StallRiskRules.ComputeInterceptSeconds 算好）
        public StallRiskGateDecision(){}
        public StallRiskGateDecision(bool adopt,int collectCents,float interceptSeconds){adoptAsRisk=adopt;this.collectCents=collectCents;this.interceptSeconds=interceptSeconds;}
    }
    [Serializable] public sealed class KitchenLane
    {
        public int index,recipeIndex,orderId=-1,reservedOrderId=-1,batchRemaining,toolClicks;
        public KitchenStep step;
        public float remaining,duration;
        public bool seasoned;
        public string packageId="",energy="";
        // FLAVOR11：本锅这一份已实际选瓶、等待递餐帧一次性结算的风味。
        // Serve 时一斗即清（one-shot）：绝不让多批同锅把某一份的习惯复制给别的顾客。
        public string pendingFlavorId="";
        // v3+：本批剩余份数中已按旧 v2 规则（BeginOrder 一次扣整批 seasoning）
        // 预付的油盐份数。仅旧档迁移时写入；Serve 逐份抵扣至 0，新批次恒 0。
        // 0 ≤ prepaidSeasoning ≤ batchRemaining，由 ValidateSave 保证。
        public int prepaidSeasoning;
        // PREP12：敲蛋下锅后的待下米饭中间态（仅 step==Idle 时可 >0）。
        // = 已真实扣库存的蛋液批份数；玩家再真实接触米面后归 0 并进入 Prep。
        // 旧档无此字段（JsonUtility 补 0）= 不在中间态，读档行为不变。
        public int eggPrepPortions;
        // FREEDOM19：本锅的自由批次（null=旧式菜谱批次）。Prep 步且 freeBatch!=null
        // 表示“收集投料中”，计时原地冻结，与旧菜谱 Prep 推进互不干扰。
        public FreeKitchenBatch freeBatch;
        public bool freeBatchPresent; // DTO presence; CaptureSave sets from actual runtime batch
    }
    // The one source of truth for both normal procurement and the explicit practice fixture.
    // UI and scene never grant stock or money and never advance cooking independently.
    public sealed partial class BusinessSession
    {
        public string SessionId { get; private set; }=Guid.NewGuid().ToString("N");
        public readonly BusinessSetup Setup;
        public readonly Dictionary<string,int> Inventory;
        public readonly HashSet<string> Equipment;
        public readonly List<StallOrder> Orders=new List<StallOrder>();
        public readonly List<int> SelectedRecipes=new List<int>();
        public KitchenLane[] Lanes { get; private set; }=Array.Empty<KitchenLane>();
        public int CashCents { get; private set; }
        public int RevenueCents { get; private set; }
        public int Served { get; private set; }
        public int Lost { get; private set; }
        public int WastedPortions { get; private set; }
        public int QueueTurnaways { get; private set; }
        public int ArrivalAttempts { get; private set; }
        public int SoldOutArrivals { get; private set; }
        // S00 base-seasoning facts (见 SeasoningShortageRules): 每份成交时扣 1 份
        // 基础油盐；缺货该份计入未调味份数并累计诚信变化。均为只增事实计数，
        // 存档恢复天然幂等，不会重复扣减。
        public int UnseasonedPortionsServed { get; private set; }
        public int IntegrityChange { get; private set; }
        public int Coal { get; private set; }
        public int Gas { get; private set; }
        public int Cartridge { get; private set; }
        public int Electricity { get; private set; }
        public int DistrictIndex { get; private set; }
        public float Elapsed { get; private set; }
        public float Remaining=>Mathf.Max(0,Setup.dayDuration-Elapsed);
        public bool Started { get; private set; }
        public bool Finished { get; private set; }
        public BusinessServicePhase ServicePhase { get; private set; }=BusinessServicePhase.Planning;
        public bool AcceptingOrders=>Started&&!Finished&&(ServicePhase==BusinessServicePhase.Open||ServicePhase==BusinessServicePhase.LastCall);
        public float ClearDownSeconds=>Mathf.Clamp(Setup.clearDownSeconds,0,Setup.dayDuration);
        public float LastOrderAt=>Mathf.Max(0,Setup.dayDuration-ClearDownSeconds);
        public float SecondsUntilLastOrder=>Mathf.Max(0,LastOrderAt-Elapsed);
        public string Message { get; private set; }="先选街区，再确认带得出的菜单。";
        public int Revision { get; private set; }
        // Fired exactly once after a fully successful Serve (per-portion stock/
        // seasoning debit, cash/revenue/served/order/Revision all settled).
        // recipeIndex indexes directly into Recipes[]. Presentation-only
        // subscribers (e.g. customer reactions); they grant nothing.
        public event Action<int,int,int,bool> SaleCompleted;
        // RISK7：A01 采纳分支的交付通知。账本侧此时已完成出餐全部扣减（包材/
        // 油盐/批次/订单交付/Served），唯独现金与 Revenue 未记。发布原则与
        // SaleCompleted 相同：账本先落定，订阅者异常只记录、不破坏已完成交易。
        public event Action<StallRiskEvent> RiskSalePending;
        // RISK7：Serve 前风险闸门（orderId, recipeIndex, priceCents）。null 或未
        // 注入 = 风险关闭，走原有正常收款。接线层见 implementation.md 冻结示例。
        public Func<int,int,int,StallRiskGateDecision> ServeRiskGate;
        // RISK7：A01 风险事件运行状态。构造与 Restore 拥有同一实例；场景组经此
        // 接 StallRiskRules/Director（轮询/ICD/倒计时），不在 Session 外另建。
        public StallRiskState RiskState { get; private set; }
        // RISK7：坏账面额累计（只增事实计数，审计用），绝不冲减 CashCents。
        public int LostRevenueCents { get; private set; }
        public int RiskEventsIssued { get; private set; }
        public int RiskEventsRecovered { get; private set; }
        public int RiskEventsWrittenOff { get; private set; }
        // 当前未决债权面额（0=无）。日账/单据用它解释“已出餐未收款”。
        public int PendingRiskCents=>RiskState!=null&&RiskState.ActiveEvent!=null&&!RiskState.ActiveEvent.Resolved?RiskState.ActiveEvent.PriceCents:0;
        // 纯通知：某锅批次报废（糊锅）。在 WastePortions/批次账结清后发出，
        // 事务本身不依赖任何订阅者；风险组可订阅做后果，不影响烹饪守恒。
        public event Action<int,int,int> BatchBurned;
        public int MaxLanes=>Equipment.Contains("B04")?2:1;
        // MENUFLEX10R：菜单（SelectedRecipes）是备货/营业承诺，与物理锅位（Lanes）
        // 解耦——MaxLanes 只管同一时刻可操作的锅位数，不再限制菜单大小。开摊下限
        // MinMenuToOpen=1（实际旧反馈：=3 会把只带够一道、尤其只买米无蛋的玩家锁死
        // 在开摊前），单菜即可开摊收款，不再造任何门槛。默认自动带回前两道熟悉
        // 练习菜（AutoSelectMenuCount=2），第三道及以后由玩家自由勾选。
        public const int MinMenuToOpen=1;
        public const int AutoSelectMenuCount=2;
        public StallDistrict District=>Districts[DistrictIndex];
        private readonly System.Random random;
        private float nextCustomer;
        private int orderSerial,randomDraws;
        private readonly Dictionary<int,int> batchesStarted=new Dictionary<int,int>(),portionsStarted=new Dictionary<int,int>(),portionsWasted=new Dictionary<int,int>();

        public static readonly StallDistrict[] Districts={
            new StallDistrict("城中村老街口","煎烙 ×1.2 · 汤卤 ×1.2","监管低",new[]{1f,1f,1.2f,1.2f,1f,1f}),
            new StallDistrict("建筑工地门口","现炒 ×1.4 · 预制 ×1.3","无监管",new[]{1.4f,1.3f,1f,1f,1f,1f}),
            new StallDistrict("重点中学门口","煎烙 ×1.4 · 油炸 ×1.3","监管中",new[]{1f,1f,1.4f,1f,1.3f,1.2f}),
            new StallDistrict("CBD 写字楼后巷","现炒、茶饮 ×1.2 · 预制 ×0.6","监管高",new[]{1.2f,.6f,1f,1f,1f,1.2f}),
            new StallDistrict("大学城酒吧夜市","油炸 ×1.4 · 煎烙、茶饮 ×1.2","合规夜市",new[]{1f,1f,1.2f,1f,1.4f,1.2f}),
            new StallDistrict("国潮文旅步行街","茶饮 ×1.3 · 国潮招牌偏好","合规街区",new[]{1f,1f,1f,1f,1f,1.3f})};
        // PANTRY9 简谱菜：只买手边散称原料也能开工的“低配真菜”，追加在既有
        // 六菜（索引 0~5）之后，旧索引/旧存档/旧夹具一律不变。价格按内容库
        // 加料价取舍得当：素炒饭=蛋炒饭¥10−加蛋¥2（S10 authored），素饼=手抓饼
        // ¥8−火腿/里脊¥3（S12 authored ¥4~5 区间下沿），滑蛋/鲜肉小炒按 authored
        // 每份成本（egg ¥1.35 / meat ¥3.15）+ 加料区间定价。包材按内容库逐菜
        // 适配（炒饭→发泡/PP/大餐盒，饼→牛皮袋/醒狮袋），不与母菜共用全套。
        public static readonly StallRecipe[] Recipes={
            Recipe("fried_rice","街头葱花蛋炒饭","猛火现炒",1000,new[]{"V01","V02","V03","V04"},new[]{"U01","U02","U03"},new[]{"P02","P05","P06","P11"},new[]{"rice","egg","seasoning"},7,2.5f,1,"翻炒"),
            Recipe("prepared_rice","鱼香肉丝料理盖饭","科技预制",1300,new[]{"RiceBucket"},new[]{"U14"},new[]{"P02","P05","P06"},new[]{"rice","prepared"},6,4,1,"取出料理包",true),
            Recipe("flatbread","卫龙火腿手抓饼","煎烙面点",800,new[]{"V05","V06","V07"},new[]{"U04","U05"},new[]{"P04","P09"},new[]{"dough","skewer","seasoning"},6.5f,4,1,"翻面"),
            Recipe("soup","街头萝卜鱼蛋热汤","汤水卤煮",1000,new[]{"V09","V10"},new[]{"U11","U12"},new[]{"P07","P10"},new[]{"egg","skewer","seasoning"},12,5,20,"搅匀盛汤"),
            Recipe("fried_skewer","炸开花淀粉肠里脊串","街头油炸",800,new[]{"V13"},new[]{"U08","U09"},new[]{"P03"},new[]{"skewer","seasoning"},8.5f,3,5,"起锅控油"),
            Recipe("plum_drink","古法冰镇桂花酸梅汤","茶饮甜水",700,new[]{"V16","V09"},new[]{"U12"},new[]{"P08","P12"},new[]{"fruit"},0,6,30,"定量舀取",false,true),
            Recipe("plain_rice","街头素香炒饭","猛火现炒",800,new[]{"V01","V02","V03","V04"},new[]{"U01","U02","U03"},new[]{"P02","P05","P06"},new[]{"rice","seasoning"},2.2f,2.5f,1,"翻炒"),
            Recipe("plain_flatbread","葱香素手抓饼","煎烙面点",500,new[]{"V05","V06","V07"},new[]{"U04","U05"},new[]{"P04","P09"},new[]{"dough","seasoning"},6f,4,6,"翻面"),
            Recipe("egg_greens","家常青菜滑蛋","猛火现炒",700,new[]{"V01","V02","V03","V04"},new[]{"U01","U02","U03"},new[]{"P02","P05"},new[]{"egg","seasoning"},2.8f,3,2,"滑炒"),
            Recipe("meat_bites","街头鲜肉小炒","猛火现炒",1200,new[]{"V01","V02","V03","V04"},new[]{"U01","U02","U03"},new[]{"P05","P06"},new[]{"meat","seasoning"},3.2f,3,2,"爆炒"),
            // FLAVOR11 真饮品（内容库原文，追加在 0..9 之后，旧索引/旧存档一律不变）：
            // 冷制饮品—— Prep 直通调味、不耗燃料（Energy="none"）、递餐不扣油盐、
            // 容器/工具/包材全部为采购侧已有真实词条（V15 冰槽台/U13 暴打棒/
            // V16 木桶/U12 深汤勺/P08 封口杯/P07 防烫汤碗）。
            Recipe("lemon_tea","暴打手锤柠檬茶","茶饮甜水",1300,new[]{"V15"},new[]{"U13"},new[]{"P08"},new[]{"fruit"},0,6,30,"暴打锤香",false,true),
            Recipe("rose_jelly","红糖玫瑰冰粉","茶饮甜水",1200,new[]{"V16"},new[]{"U12"},new[]{"P07","P08"},new[]{"fruit"},0,6,30,"舀取装碗",false,true)};
        private static StallRecipe Recipe(string id,string name,string family,int price,string[] vessels,string[] tools,string[] packages,string[] raw,float heat,float window,int batch,string action,bool microwave=false,bool cold=false)
        {
            var ingredients=new Dictionary<string,int>();foreach(string item in raw)ingredients[item]=1;
            return new StallRecipe{id=id,name=name,family=family,priceCents=price,vessels=vessels,tools=tools,packages=packages,ingredients=ingredients,heatSeconds=heat,greenSeconds=window,batchSize=batch,toolLabel=action,microwave=microwave,cold=cold};
        }
        // PANTRY9 菜系/母菜式映射：简谱菜挂到既有母菜，火候窗口、自动锅、批量
        // 与街区口味权重全部沿用母菜口径，保证 0~5 的既有行为逐字节不变。
        // 新增索引必须在这里登记，不得散落的 if(index==6) 特例。
        // FLAVOR11：两款新饮品挂母菜 5（酸梅汤），街区权重/批量/冷制口径全部沿用
        // 茶饮甜水家族；新增索引必须在这里登记，不得散落 if(index==10) 特例。
        public static int ParentRecipeIndex(int index)
            =>index==6?0:index==7?2:index==8?0:index==9?0:index==10?5:index==11?5:index;
        // 现炒系（母菜 0）：同一口炒锅的批量/绿区/自动炒锅口径。
        static bool IsWokStyle(int index)=>ParentRecipeIndex(index)==0;
        // PREP12：需要“先敲蛋进锅 → 再下米面”两拍真实接触的现炒菜
        // （配方同时含蛋与米面；当前仅街头葱花蛋炒饭）。其余菜一步投料不变。
        public static bool RequiresEggCrack(int recipeIndex)
        {
            if(recipeIndex<0||recipeIndex>=Recipes.Length)return false;
            var ingredients=Recipes[recipeIndex].ingredients;
            return ingredients.ContainsKey("egg")&&ingredients.ContainsKey("rice");
        }
        public BusinessSession(BusinessSetup setup)
        {
            if(setup==null)throw new ArgumentNullException(nameof(setup));Setup=setup.Copy();CashCents=Mathf.Max(0,Setup.cashCents);
            Inventory=new Dictionary<string,int>(Setup.inventory);Equipment=new HashSet<string>(Setup.equipment);
            if(!Setup.stoveLoaded)Equipment.Remove("B04");
            Coal=Mathf.Max(0,Setup.coalPoints);Gas=Mathf.Max(0,Setup.gasPoints);Cartridge=Mathf.Max(0,Setup.cartridgePoints);Electricity=Mathf.Max(0,Setup.electricityPoints);
            random=new System.Random(Setup.seed);
            RiskState=new StallRiskState(Setup.seed);
            // MENUFLEX10R：自动选择至多 AutoSelectMenuCount 道既有首选可售菜
            // （默认前两道熟悉练习菜），库存/设备不够时带几道算几道；Min=1 保证
            // 默认菜单（哪怕只有一道素炒饭）永远可直接开摊，不凭空加菜。
            for(int i=0;i<Recipes.Length&&SelectedRecipes.Count<AutoSelectMenuCount;i++)if(Missing(i)=="")SelectedRecipes.Add(i);
        }
        public int Count(string id)=>Inventory.TryGetValue(id,out int n)?n:0;
        // PANTRY9：街区权重 authored 表只有既有 6 菜（float[6]，静态非存档）。简谱菜
        // 按母菜映射取权重（素炒饭/滑蛋/鲜肉小炒随现炒，素饼随煎烙），旧街区表与
        // 旧档不动，点单随机序列对 0~5 菜单逐字节不变。
        public float DistrictWeight(int index)
            =>index<District.weights.Length?District.weights[index]:District.weights[ParentRecipeIndex(index)];
        // PANTRY9 菜单“够 N 份”：按真实主料库存（油盐为每份软扣，不限制开工显示）
        // 当前还能开工的整锅份数下限；0 = 主料断供。只读，不改任何状态。
        public int AffordablePortions(int index)
        {
            if(index<0||index>=Recipes.Length)return 0;
            StallRecipe recipe=Recipes[index];int best=int.MaxValue;
            foreach(var ingredient in recipe.ingredients)
            {
                if(SeasoningShortageRules.IsBaseSeasoning(ingredient.Key))continue;
                if(ingredient.Value<=0)continue;
                best=Mathf.Min(best,Count(ingredient.Key)/ingredient.Value);
            }
            return best==int.MaxValue?0:Mathf.Max(0,best);
        }
        public BusinessSaveData CaptureSave()
        {
            var savedOrders=new List<StallOrder>();foreach(var order in Orders)savedOrders.Add(BusinessSaveData.CopyOrder(order));
            var savedLanes=new List<KitchenLane>();foreach(var lane in Lanes)savedLanes.Add(BusinessSaveData.CopyLane(lane));
            var made=new List<BusinessProductionSaveData>();var keys=new SortedSet<int>(batchesStarted.Keys);keys.UnionWith(portionsStarted.Keys);keys.UnionWith(portionsWasted.Keys);
            foreach(int key in keys)made.Add(new BusinessProductionSaveData{recipeIndex=key,batches=ReadCount(batchesStarted,key),portions=ReadCount(portionsStarted,key),wasted=ReadCount(portionsWasted,key)});
            var freeMade=new List<BusinessProductionSaveData>();var freeKeys=new SortedSet<int>(freeBatchesStarted.Keys);freeKeys.UnionWith(freePortionsStarted.Keys);freeKeys.UnionWith(freePortionsWasted.Keys);
            foreach(int key in freeKeys)freeMade.Add(new BusinessProductionSaveData{recipeIndex=key,batches=ReadCount(freeBatchesStarted,key),portions=ReadCount(freePortionsStarted,key),wasted=ReadCount(freePortionsWasted,key)});
            var savedMeals=new List<CounterMeal>();foreach(var meal in ReadyMeals)savedMeals.Add(BusinessSaveData.CopyMeal(meal));
            return new BusinessSaveData{sessionId=SessionId,setup=BusinessSetupSaveData.Capture(Setup),inventory=BusinessSetupSaveData.CaptureStock(Inventory),equipment=BusinessSetupSaveData.Sorted(Equipment),
                orders=savedOrders.ToArray(),lanes=savedLanes.ToArray(),selectedRecipes=SelectedRecipes.ToArray(),production=made.ToArray(),
                cashCents=CashCents,revenueCents=RevenueCents,served=Served,lost=Lost,wastedPortions=WastedPortions,queueTurnaways=QueueTurnaways,arrivalAttempts=ArrivalAttempts,soldOutArrivals=SoldOutArrivals,
                coal=Coal,gas=Gas,cartridge=Cartridge,electricity=Electricity,districtIndex=DistrictIndex,orderSerial=orderSerial,randomDraws=randomDraws,revision=Revision,
                unseasonedPortionsServed=UnseasonedPortionsServed,integrityChange=IntegrityChange,seasoningChargeVersion=BusinessSaveData.SeasoningChargeVersion3,
                menuFlexVersion=BusinessSaveData.MenuFlexVersion1,
                eggPrepVersion=BusinessSaveData.EggPrepVersion1,
                freeKitchenVersion=BusinessSaveData.FreeKitchenVersion1,freeProduction=freeMade.ToArray(),readyMeals=savedMeals.ToArray(),
                heldMeal=BusinessSaveData.CopyMeal(HeldMeal),heldMealPresent=HeldMeal!=null,
                risk=RiskState.Capture(),paymentVersion=BusinessSaveData.PaymentVersion3,
                lostRevenueCents=LostRevenueCents,riskEventsIssued=RiskEventsIssued,riskEventsRecovered=RiskEventsRecovered,riskEventsLost=RiskEventsWrittenOff,
                bikeBlockVersion=BusinessSaveData.BikeBlockVersion1,bikeBlockActive=bikeBlockActive,bikeBlockTriggered=bikeBlockTriggered,
                bikeBlockRemaining=bikeBlockRemaining,bikeBlockMask=bikeBlockMask,bikeBlockPollRemaining=bikeBlockPollRemaining,
                started=Started,finished=Finished,servicePhase=ServicePhase,elapsed=Elapsed,nextCustomer=nextCustomer,message=Message};
        }
        // Unity serializes inline null custom classes as empty objects. Presence markers
        // distinguish absent lane/product state from malformed populated save data.
        static void NormalizeInlineFreeKitchenObjects(BusinessSaveData data)
        {
            if(data==null||data.lanes==null)return;
            foreach(var lane in data.lanes)
            {
                if(lane==null)continue;
                var batch=lane.freeBatch;
                if(batch==null){if(lane.freeBatchPresent)throw new ArgumentException("Missing marked free batch");continue;}
                var product=batch.product;
                if(product==null&&batch.productPresent)throw new ArgumentException("Missing marked free product");
                if(!batch.productPresent&&product!=null&&string.IsNullOrEmpty(product.id)&&string.IsNullOrEmpty(product.name)
                    &&string.IsNullOrEmpty(product.method)&&string.IsNullOrEmpty(product.family)
                    &&(product.inputs==null||product.inputs.Length==0)&&(product.packages==null||product.packages.Length==0)
                    &&product.portions==0&&product.remaining==0&&product.priceCents==0&&product.quality==0
                    &&(product.matchedRecipeIndex==0||product.matchedRecipeIndex==-1)&&product.visualRecipeIndex==0&&(product.sourcePortions==0||product.sourcePortions==1))
                    batch.product=null;
                if(!lane.freeBatchPresent&&string.IsNullOrEmpty(batch.id)&&string.IsNullOrEmpty(batch.method)
                    &&(batch.inputs==null||batch.inputs.Length==0)&&batch.product==null&&(batch.capacity==0||batch.capacity==1)
                    &&string.IsNullOrEmpty(batch.vesselId)&&string.IsNullOrEmpty(batch.baseId)&&string.IsNullOrEmpty(batch.toolId))
                    lane.freeBatch=null;
            }
        }
        public static BusinessSession Restore(BusinessSaveData data)
        {
            NormalizeInlineFreeKitchenObjects(data);
            NormalizeHeldMealSave(data);
            // RISK7 旧档迁移（paymentVersion 0/缺省 = 风险收款不存在的老格式）：
            // 老档 served 订单的 paidCents/riskSerial/shortSeasoning 字段不存在（JsonUtility
            // 补 0），而老规则 Serve 即收款。因此为所有“非风险”的已服务订单补
            // paidCents=菜价，使营收守恒与单据/日账口径一致；现金/库存/营收数字本身
            // 一律不动，不补发任何事件。
            // RISK7PAYFIX：迁移只碰结构合法的数组/索引——orders 缺失或 served 订单的
            // recipeIndex 越界一律明确 ArgumentException（不 NullReference/越界），
            // 且不改现金/库存/营收任何数字。
            if(data!=null&&data.paymentVersion==0)
            {
                if(data.orders==null)throw new ArgumentException("Missing business orders array for legacy payment migration");
                foreach(var order in data.orders)
                    if(order!=null&&order.served&&order.riskSerial==0&&order.paidCents==0)
                    {
                        if(order.recipeIndex<0||order.recipeIndex>=Recipes.Length)throw new ArgumentException("Invalid recipe index in legacy payment migration");
                        order.paidCents=Recipes[order.recipeIndex].priceCents;
                    }
            }
            if(data!=null&&data.freeKitchenVersion==0)
            {if(data.freeProduction==null)data.freeProduction=Array.Empty<BusinessProductionSaveData>();if(data.readyMeals==null)data.readyMeals=Array.Empty<CounterMeal>();}
            ValidateSave(data);BusinessSetup setup=data.setup.Restore();var result=new BusinessSession(setup);
            result.SessionId=data.sessionId;result.Inventory.Clear();foreach(var pair in BusinessSetupSaveData.RestoreStock(data.inventory))result.Inventory.Add(pair.Key,pair.Value);
            result.Equipment.Clear();foreach(string id in data.equipment)result.Equipment.Add(id);
            result.SelectedRecipes.Clear();result.SelectedRecipes.AddRange(data.selectedRecipes);
            result.Orders.Clear();foreach(var order in data.orders)result.Orders.Add(BusinessSaveData.CopyOrder(order));
            result.Lanes=new KitchenLane[data.lanes.Length];for(int i=0;i<data.lanes.Length;i++)result.Lanes[i]=BusinessSaveData.CopyLane(data.lanes[i]);
            // COLD12 旧档迁移：旧流程把冷制锅停在 Season（把它当调味步）。新口径下
            // 酸梅汤/冰粉/柠檬茶 Prep 完成即 seasoned，冷制没有、也永远不需要基础油盐
            // 调味步——统一迁移为 seasoned=true、step=Pack，走 BeginOrder/ScoopBatch 的
            // 既有 seasoned 入口直接选包材出餐，不弹“缺油盐扣诚信”的假提示。幂等：
            // 新档冷制锅不会再写出 Season，迁移只发生一次。
            foreach(var lane in result.Lanes)if(lane!=null&&Recipes[lane.recipeIndex].cold&&lane.step==KitchenStep.Season)
            {lane.step=KitchenStep.Pack;lane.seasoned=true;lane.remaining=0;}
            // MENUFLEX10 旧档迁移：v0 档锅位与菜单一一对应且不超过旧 MaxLanes；读档后
            // 把锅位补齐到当前物理锅位数（新锅 Idle、挂菜单首菜），旧锅的菜/批次/绑定
            // 一律原样。现金/库存/营收数字不动；补齐只发生一次（写盘即升级为 v1）。
            if(data.menuFlexVersion==0&&result.Started&&result.Lanes.Length<result.MaxLanes)
            {
                var padded=new KitchenLane[result.MaxLanes];
                for(int i=0;i<result.Lanes.Length;i++)padded[i]=result.Lanes[i];
                for(int i=result.Lanes.Length;i<padded.Length;i++)padded[i]=new KitchenLane{index=i,recipeIndex=result.SelectedRecipes.Count>0?result.SelectedRecipes[0]:0,step=KitchenStep.Idle};
                result.Lanes=padded;
            }
            foreach(var value in data.production){result.batchesStarted.Add(value.recipeIndex,value.batches);result.portionsStarted.Add(value.recipeIndex,value.portions);result.portionsWasted.Add(value.recipeIndex,value.wasted);}
            // FREEDOM19：自由生产守恒账（旧档 freeProduction 缺省空数组 = 无自由批次）。
            if(data.freeProduction!=null)foreach(var value in data.freeProduction){result.freeBatchesStarted.Add(value.recipeIndex,value.batches);result.freePortionsStarted.Add(value.recipeIndex,value.portions);result.freePortionsWasted.Add(value.recipeIndex,value.wasted);}
            // FREEDOM19：柜台餐队列（旧档缺省 = 空）。逐份深拷贝，绝不与锅位共享引用。
            if(data.readyMeals!=null)foreach(var meal in data.readyMeals)result.ReadyMeals.Add(BusinessSaveData.CopyMeal(meal));
            result.HeldMeal=BusinessSaveData.CopyMeal(data.heldMeal);
            result.CashCents=data.cashCents;result.RevenueCents=data.revenueCents;result.Served=data.served;result.Lost=data.lost;result.WastedPortions=data.wastedPortions;
            result.QueueTurnaways=data.queueTurnaways;result.ArrivalAttempts=data.arrivalAttempts;result.SoldOutArrivals=data.soldOutArrivals;
            result.Coal=data.coal;result.Gas=data.gas;result.Cartridge=data.cartridge;result.Electricity=data.electricity;result.DistrictIndex=data.districtIndex;result.orderSerial=data.orderSerial;
            result.UnseasonedPortionsServed=data.unseasonedPortionsServed;result.IntegrityChange=data.integrityChange;
            result.Elapsed=data.elapsed;result.nextCustomer=data.nextCustomer;result.Started=data.started;result.Finished=data.finished;result.ServicePhase=data.servicePhase;result.Message=data.message;result.Revision=data.revision;
            result.LostRevenueCents=data.lostRevenueCents;result.RiskEventsIssued=data.riskEventsIssued;result.RiskEventsRecovered=data.riskEventsRecovered;result.RiskEventsWrittenOff=data.riskEventsLost;
            // RISK7：风险运行状态与业务档同一生命周期。旧档 risk=null → 空状态迁移；
            // 非法/NaN/负数/孤儿债权由 StallRiskState.Restore 明确拒绝，不吞校验。
            result.RiskState=StallRiskState.Restore(data.risk,setup.seed);
            // B05：恢复共享单车堵摊状态；v0 旧档迁移为未触发/未激活/20s 轮询就绪。
            result.RestoreBikeBlockSave(data);
            // The only random entry point in this model is NextDouble. Replaying
            // its count restores the stream without generating a customer/order.
            for(int i=0;i<data.randomDraws;i++)result.random.NextDouble();result.randomDraws=data.randomDraws;
            result.ReleaseAllReservations();
            // 旧 v2 计费迁移：旧规则在 BeginOrder 一次扣整批 seasoning。对锅内仍
            // 存在的批次，按“旧配方是否含 seasoning”把已扣的批内份数记为预付
            // 抵扣（prepaidSeasoning），Serve 时逐份抵减、不再重复扣库存也不按
            // 缺料罚诚信。只读 fact（现金/库存/诚信事实）保持原样：不退钱不退料。
            // prepared_rice/plum_drink 旧配方无 seasoning，不虚构预付；Idle/Dirty/
            // 无批次不建 credit；新计费档保留剩余 credit，不再执行迁移。
            if(data.seasoningChargeVersion==0||data.seasoningChargeVersion==BusinessSaveData.SeasoningChargeVersion2)
            {
                foreach(var lane in result.Lanes)
                {
                    if(lane.freeBatch!=null)continue; // FREEDOM19：自由批次从未按旧 v2 一次扣批 seasoning
                    if(lane.batchRemaining<=0)continue;
                    if(lane.step==KitchenStep.Idle||lane.step==KitchenStep.Dirty)continue;
                    if(!Recipes[lane.recipeIndex].ingredients.ContainsKey(SeasoningShortageRules.BaseSeasoningId))continue;
                    // The old batch already paid. Current pantry stock is what
                    // remained AFTER that debit, so it must not cap the credit.
                    lane.prepaidSeasoning=lane.batchRemaining;
                }
                result.Revision++;
            }
            // RISK7：未决债权必须对应一张真实交付的合法订单/菜品，否则拒绝读档
            // （不吞校验）。校验绑定：订单已 served、债权序号一致、未收款、
            // 锅位/菜品与债权匹配、面额=菜价。
            var pendingEvent=result.RiskState.ActiveEvent;
            if(pendingEvent!=null)
            {
                var pendingOrder=result.FindOrder(pendingEvent.OrderId);
                // FREEDOM19：散客/泛称成品的债权面额与菜谱 id 以真实成品事实为准。
                int pendingFace=pendingOrder!=null&&pendingOrder.productId!=""
                    ?pendingOrder.productPriceCents+StallFlavorMenu.MarkupFor(pendingOrder.servedFlavorId)
                    :pendingOrder!=null&&pendingOrder.recipeIndex>=0&&pendingOrder.recipeIndex<Recipes.Length
                    ?Recipes[pendingOrder.recipeIndex].priceCents+StallFlavorMenu.MarkupFor(pendingOrder.servedFlavorId):0;
                if(pendingOrder==null||!pendingOrder.served||pendingOrder.riskSerial!=pendingEvent.Serial||pendingOrder.paidCents!=0
                    ||pendingOrder.recipeIndex<0||pendingOrder.recipeIndex>=Recipes.Length
                    ||(pendingOrder.productId!=""?pendingEvent.RecipeId!=pendingOrder.productId:pendingEvent.RecipeId!=Recipes[pendingOrder.recipeIndex].id)
                    // FLAVOR11：面额=基础价+实际加上的风味加价（老档 servedFlavorId="" 即原价，口径不变）。
                    ||pendingEvent.PriceCents!=pendingFace
                    // 冻结实收只在原价～暴躁双倍之间（RISK7PAYFIX）：坏保存不能挂一个
                    // 任意大的 CollectCents 等追回时领走超额现金。
                    ||pendingEvent.CollectCents<pendingEvent.PriceCents||pendingEvent.CollectCents>pendingEvent.PriceCents*2
                    // FREEDOM19：成品订单的债权不校验锅位菜（锅位可能已释放/换菜）；
                    // 身份/面额已由 productId + pendingFace 绑定。
                    ||pendingEvent.LaneIndex<0||pendingEvent.LaneIndex>=result.Lanes.Length
                    ||(pendingOrder.productId==""&&pendingEvent.LaneIndex>=0&&result.Lanes[pendingEvent.LaneIndex].recipeIndex!=pendingOrder.recipeIndex))
                    throw new ArgumentException("Pending risk claim does not match a delivered order");
            }
            BusinessAuditState audit=result.CaptureAudit();
            if(!audit.cashConserved||!audit.packagesConserved||!audit.nonNegativeEnergy||!audit.nonNegativeInventory)throw new ArgumentException("Saved business ledger is inconsistent");
            foreach(var recipe in audit.recipeProduction)if(!recipe.portionsConserved)throw new ArgumentException("Saved batch ledger is inconsistent: "+recipe.recipe);
            return result;
        }
        public void ReleaseAllReservations()
        {
            bool changed=false;foreach(var order in Orders)if(order.reservedLane>=0){order.reservedLane=-1;changed=true;}
            foreach(var lane in Lanes)if(lane.reservedOrderId>=0){lane.reservedOrderId=-1;changed=true;}
            if(changed)Revision++;
        }
        private static void ValidateSave(BusinessSaveData d)
        {
            if(d==null||d.schemaVersion!=BusinessSaveData.CurrentVersion||d.randomAlgorithm!=BusinessSaveData.RandomAlgorithm||d.setup==null||string.IsNullOrEmpty(d.sessionId))throw new ArgumentException("Unsupported or incomplete business save");
            if(d.orders==null||d.lanes==null||d.selectedRecipes==null||d.production==null||d.equipment==null)throw new ArgumentException("Missing business state arrays");
            ValidateEquipment(d.equipment);ValidateEquipment(d.setup.equipment);
            if(!Finite(d.elapsed)||!Finite(d.nextCustomer)||!Finite(d.setup.dayDuration)||d.setup.dayDuration<=0||d.elapsed<0||d.elapsed>d.setup.dayDuration+.01f||d.randomDraws<0||d.randomDraws>1000000)throw new ArgumentException("Invalid business clock or random state");
            if(d.districtIndex<0||d.districtIndex>=Districts.Length||d.cashCents<0||d.revenueCents<0||d.served<0||d.lost<0||d.coal<0||d.gas<0||d.cartridge<0||d.electricity<0||d.orderSerial<0||d.wastedPortions<0||d.queueTurnaways<0||d.arrivalAttempts<0||d.soldOutArrivals<0||d.unseasonedPortionsServed<0||d.integrityChange>0)throw new ArgumentException("Invalid business counters");
            if(d.setup.gasPoints<0||d.setup.cartridgePoints<0||d.setup.coalPoints<0||d.setup.electricityPoints<0||d.gas>d.setup.gasPoints||d.cartridge>d.setup.cartridgePoints||d.coal>d.setup.coalPoints||d.electricity>d.setup.electricityPoints)throw new ArgumentException("Saved fuel pool exceeds its supplied source");
            if(!Finite(d.setup.customerIntervalSeconds)||d.setup.customerIntervalSeconds<=0||!Finite(d.setup.clearDownSeconds)||!Finite(d.setup.lastCallNoticeSeconds)||d.setup.patienceMinSeconds<=0||d.setup.patienceMaxSeconds<d.setup.patienceMinSeconds||d.setup.queueCapacity<1)throw new ArgumentException("Invalid saved business setup");
            if(d.settlementPublications<0||d.settlementPublications>1||d.settlementPublished!=(d.settlementPublications==1))throw new ArgumentException("Invalid saved settlement publication state");
            if(d.settlementPublished&&!d.finished||d.finished&&d.servicePhase!=BusinessServicePhase.Closed||!d.started&&d.lanes.Length!=0)throw new ArgumentException("Invalid business lifecycle state");
            var menu=new HashSet<int>();foreach(int index in d.selectedRecipes)if(index<0||index>=Recipes.Length||!menu.Add(index))throw new ArgumentException("Invalid saved menu");
            // MENUFLEX10：menuFlexVersion 1 = 菜单与锅位解耦（锅位=物理位置=MaxLanes，
            // lane.recipeIndex 可自由换菜，不再等于 selectedRecipes[i]）；0/缺省 = 旧档
            // 静态一一对应。两种形状各自校验，旧档由 Restore 迁移补齐。
            if(d.menuFlexVersion!=0&&d.menuFlexVersion!=BusinessSaveData.MenuFlexVersion1)throw new ArgumentException("Unsupported menu flex version");
            // PREP12：0/缺省=旧档（无蛋液中间态字段，JsonUtility 补 0）；1=本版。
            if(d.eggPrepVersion!=0&&d.eggPrepVersion!=BusinessSaveData.EggPrepVersion1)throw new ArgumentException("Unsupported egg prep version");
            // FREEDOM19：0/缺省=旧档（无自由批次）；1=本版。除 0/1 外拒绝。
            if(d.freeKitchenVersion!=0&&d.freeKitchenVersion!=BusinessSaveData.FreeKitchenVersion1)throw new ArgumentException("Unsupported free kitchen version");
            if(d.freeProduction==null)throw new ArgumentException("Missing free production ledger");
            var freeProduction=new HashSet<int>();foreach(var p in d.freeProduction)if(p==null||p.recipeIndex<0||p.recipeIndex>=Recipes.Length||!freeProduction.Add(p.recipeIndex)||p.batches<0||p.portions<0||p.wasted<0)throw new ArgumentException("Invalid saved free production ledger");
            // FREEDOM19：柜台餐队列校验（唯一 id、合法成品/包材/报价/份数）。
            if(d.readyMeals==null||d.readyMeals.Length>CounterCapacity)throw new ArgumentException("Invalid ready meals array");
            var mealIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var meal in d.readyMeals){ValidateMeal(meal,mealIds);if(meal.sourceLane<0||meal.sourceLane>=d.lanes.Length)throw new ArgumentException("Invalid meal source lane");}
            if(d.heldMeal!=null)
            {
                ValidateMeal(d.heldMeal,mealIds);
                if(d.heldMeal.sourceLane<0||d.heldMeal.sourceLane>=d.lanes.Length)throw new ArgumentException("Invalid held meal source lane");
            }
            if(d.menuFlexVersion==BusinessSaveData.MenuFlexVersion1)
            {
                if(d.started&&d.lanes.Length!=MaxLanesForEquipment(d.equipment))throw new ArgumentException("Saved cookware does not match physical burners");
            }
            else if(d.started&&(d.lanes.Length!=d.selectedRecipes.Length||d.lanes.Length==0))throw new ArgumentException("Saved cookware does not match menu");
            var ids=new HashSet<int>();int served=0,lost=0;
            foreach(var o in d.orders)
            {
                if(o==null||o.id<=0||o.id>d.orderSerial||!ids.Add(o.id)||o.recipeIndex<0||o.recipeIndex>=Recipes.Length||o.lane < -1||o.lane>=d.lanes.Length||!Finite(o.patience)||!Finite(o.maxPatience)||o.maxPatience<=0||o.served&&o.departed||o.arrived&&o.arrivedAt<0)throw new ArgumentException("Invalid saved customer order");
                if(o.served)served++;if(o.departed)lost++;
            }
            if(served!=d.served||lost!=d.lost)throw new ArgumentException("Saved served/lost counts do not match orders");
            if(d.unseasonedPortionsServed>d.served||(long)d.integrityChange!=(long)d.unseasonedPortionsServed*SeasoningShortageRules.IntegrityDeltaPerShortPortion)throw new ArgumentException("Saved seasoning penalties do not match served portions");
            for(int i=0;i<d.lanes.Length;i++)
            {
                var lane=d.lanes[i];if(lane==null||lane.index!=i||lane.recipeIndex<0||lane.recipeIndex>=Recipes.Length||(d.menuFlexVersion==0&&lane.recipeIndex!=d.selectedRecipes[i])||!Enum.IsDefined(typeof(KitchenStep),lane.step)||lane.batchRemaining<0||!Finite(lane.remaining)||!Finite(lane.duration))throw new ArgumentException("Invalid saved cookware state");
                if(lane.energy!=""&&lane.energy!="none"&&lane.energy!="gas"&&lane.energy!="cartridge"&&lane.energy!="coal"&&lane.energy!="electricity")throw new ArgumentException("Unknown saved cookware fuel source");
                // prepaidSeasoning（旧 v2 迁移的预付油盐）逐份抵扣、只减不增，
                // 存档原样往返；任何版本下不得超过锅内剩余份数。
                if(lane.prepaidSeasoning>lane.batchRemaining)throw new ArgumentException("Prepaid seasoning exceeds remaining batch portions");
                // FLAVOR12：锅上待结算风味空串/缺省 null 都=未加味（正常存档），
                // 一律规范化为 ""；仅非空时才必须是已知风味（份数在 Serve 接触帧才校验）。
                lane.pendingFlavorId=lane.pendingFlavorId??"";
                if(lane.pendingFlavorId!=""&&!StallFlavorMenu.IsFlavor(lane.pendingFlavorId))throw new ArgumentException("Unknown pending flavor");
                // PREP12：蛋液中间态只存在于 Idle 空批次锅，且必须是两拍现炒菜。
                if(lane.eggPrepPortions<0||lane.eggPrepPortions>64)throw new ArgumentException("Invalid saved egg prep portions");
                if(lane.eggPrepPortions>0&&(lane.step!=KitchenStep.Idle||lane.batchRemaining>0||!RequiresEggCrack(lane.recipeIndex)))throw new ArgumentException("Egg prep portions outside the idle intermediate state");
                if(lane.prepaidSeasoning>0&&(lane.step==KitchenStep.Idle||lane.step==KitchenStep.Dirty||!Recipes[lane.recipeIndex].ingredients.ContainsKey(SeasoningShortageRules.BaseSeasoningId)))throw new ArgumentException("Prepaid seasoning is not attached to a paid batch");
                ValidateFreeBatch(lane,i,d.equipment);
                if(lane.orderId>=0){StallOrder o=Array.Find(d.orders,x=>x.id==lane.orderId);if(o==null||o.served||o.departed&&!d.finished||o.lane!=i||o.recipeIndex!=lane.recipeIndex)throw new ArgumentException("Saved cookware/order binding is inconsistent");}
            }
            var production=new HashSet<int>();foreach(var p in d.production)if(p==null||p.recipeIndex<0||p.recipeIndex>=Recipes.Length||!production.Add(p.recipeIndex)||p.batches<0||p.portions<0||p.wasted<0)throw new ArgumentException("Invalid saved production ledger");
            // 计费版本只接受 2（旧，缺省 0 同义）/3（本版）。见 BusinessSaveData.SeasoningChargeVersion*。
            if(d.seasoningChargeVersion!=0&&d.seasoningChargeVersion!=BusinessSaveData.SeasoningChargeVersion2&&d.seasoningChargeVersion!=BusinessSaveData.SeasoningChargeVersion3)throw new ArgumentException("Unsupported seasoning charge version");
            // RISK7：支付事实/坏账计数守恒。逐单实收之和必须恰等于营收（正常=菜价、
            // 风险未收=0、追回=实收含双倍、坏账=0）；未服务订单不得携带支付事实；
            // 无 risk 状态的老档不得有风险计数。
            if(d.paymentVersion!=0&&d.paymentVersion!=BusinessSaveData.PaymentVersion3)throw new ArgumentException("Unsupported payment version");
            if(d.lostRevenueCents<0||d.riskEventsIssued<0||d.riskEventsRecovered<0||d.riskEventsLost<0||d.riskEventsRecovered+d.riskEventsLost>d.riskEventsIssued)throw new ArgumentException("Invalid risk payment counters");
            int collected=0;
            foreach(var o in d.orders)
            {
                if(o.paidCents<0||o.riskSerial<0)throw new ArgumentException("Invalid saved payment facts");
                // FLAVOR12：风味字段允许旧档/旧 JSON 缺省 null，规范化为 ""（=原味）
                // 后走原基础价校验，不拒绝正常存档、不重复补钱、不改老档数字。
                o.requestedFlavorId=o.requestedFlavorId??"";
                o.servedFlavorId=o.servedFlavorId??"";
                // FREEDOM19：成品事实字段允许旧档缺省 null，规范化为 ""（=旧菜谱订单）。
                o.productId=o.productId??"";o.productName=o.productName??"";o.productMethod=o.productMethod??"";o.productFamily=o.productFamily??"";
                if(o.productId!=""&&(o.productName==""||o.productPriceCents<=0||FreeKitchenRules.FindMethod(o.productMethod)==null))throw new ArgumentException("Invalid saved product facts");
                if(o.productId==""&&(o.productName!=""||o.productPriceCents!=0))throw new ArgumentException("Saved product facts without product identity");
                // New optional snapshots are accepted only when they exactly
                // reproduce the historical identity, method, recipe and price.
                if(o.productInputs!=null&&o.productInputs.Length>0)
                {
                    if(o.productId==""||o.productSourcePortions<1||o.productSourcePortions!=FreeKitchenRules.DerivePortions(o.productInputs))throw new ArgumentException("Invalid order ingredient provenance");
                    var seenInputs=new HashSet<string>();var def=FreeKitchenRules.FindMethod(o.productMethod);
                    foreach(var ingredient in o.productInputs)
                        if(ingredient==null||ingredient.units<1||ingredient.units>64||!seenInputs.Add(ingredient.itemId)||!FreeKitchenRules.MethodAcceptsMaterial(def,ingredient.itemId))throw new ArgumentException("Invalid order ingredient snapshot");
                    int matchedOrder=FreeKitchenRules.MatchRecipe(o.productInputs,o.productMethod,o.productSourcePortions);
                    string expectedName=matchedOrder>=0?Recipes[matchedOrder].name:FreeKitchenRules.LegacyComposeName(o.productInputs,o.productMethod);
                    if(o.productName!=expectedName||o.productPriceCents!=FreeKitchenRules.FairPriceCents(o.productInputs,o.productSourcePortions,matchedOrder)||o.recipeIndex!=(matchedOrder>=0?matchedOrder:FreeKitchenRules.VisualRecipeIndex(o.productMethod)))throw new ArgumentException("Order ingredient snapshot drift");
                }
                else if(o.productSourcePortions!=0)throw new ArgumentException("Order portions without ingredient snapshot");
                if(o.servedFlavorId!=""&&!StallFlavorMenu.IsFlavor(o.servedFlavorId))throw new ArgumentException("Unknown served flavor");
                if(o.requestedFlavorId!=""&&!StallFlavorMenu.IsFlavor(o.requestedFlavorId))throw new ArgumentException("Unknown requested flavor");
                // 实际加上的风味必须与点单一致且真的兼容该菜系（Serve 代码路径也只能
                // 产生这种组合），防止手工构造的存档给 incompatible 菜品塞加价。
                if(o.servedFlavorId!=""&&(o.servedFlavorId!=o.requestedFlavorId
                    ||(o.productId!=""?!FreeKitchenRules.FlavorCompatible(new FoodProduct{family=o.productFamily},o.servedFlavorId):!StallFlavorMenu.Compatible(Recipes[o.recipeIndex],o.servedFlavorId))))throw new ArgumentException("Saved served flavor does not match order");
                if(o.emergencyPackage&&(!o.served||o.productId==""))throw new ArgumentException("Invalid emergency package sale fact");
                if(o.served)
                {
                    collected+=o.paidCents;
                    // FREEDOM19：散客/泛称成品按真实成品价 + 实际风味加价核验收款。
                    int expected=o.productId!=""?o.productPriceCents+StallFlavorMenu.MarkupFor(o.servedFlavorId):Recipes[o.recipeIndex].priceCents+StallFlavorMenu.MarkupFor(o.servedFlavorId);
                    if(o.riskSerial==0&&o.paidCents!=expected)throw new ArgumentException("Saved non-risk order payment does not match recipe price");
                    if(d.risk!=null&&o.riskSerial>d.risk.serial)throw new ArgumentException("Saved risk serial exceeds issued serials");
                    if(d.risk==null&&o.riskSerial!=0)throw new ArgumentException("Saved risk payment without risk state");
                }
                else if(o.riskSerial!=0||o.paidCents!=0||o.servedFlavorId!="")throw new ArgumentException("Unserved order carries payment facts");
            }
            if(collected!=d.revenueCents)throw new ArgumentException("Saved revenue does not match collected order payments");
            if(d.risk==null&&(d.lostRevenueCents!=0||d.riskEventsIssued!=0||d.riskEventsRecovered!=0||d.riskEventsLost!=0))throw new ArgumentException("Risk counters without risk state");
            if(d.risk!=null&&d.riskEventsIssued!=d.risk.serial)throw new ArgumentException("Risk issued counter does not match risk state");
            ValidateBikeBlockSave(d);
            foreach(var lane in d.lanes)if(lane!=null&&lane.prepaidSeasoning<0)throw new ArgumentException("Negative prepaid seasoning");
        }
        // FREEDOM19：自由批次的存档结构校验——合法身份、有限非负、深拷贝形状、
        // 状态/成品/锅内份数一致、绝不与旧 v2 预付油盐或蛋液中间态共存。
        private static void ValidateFreeBatch(KitchenLane lane,int laneIndex,string[] equipment)
        {
            var batch=lane.freeBatch;if(batch==null)return;
            if(string.IsNullOrEmpty(batch.id)||batch.capacity<1||batch.capacity>64)throw new ArgumentException("Invalid saved free batch identity");
            if(lane.prepaidSeasoning!=0||lane.eggPrepPortions>0)throw new ArgumentException("Free batch carries legacy seasoning/egg-prep state");
            var inputs=batch.inputs??Array.Empty<FoodInput>();
            if(inputs.Length==0||inputs.Length>16)throw new ArgumentException("Invalid saved free batch inputs");
            var seen=new HashSet<string>(StringComparer.Ordinal);int totalUnits=0;
            foreach(var input in inputs)
            {
                if(input==null||input.units<=0||input.units>64||!FreeKitchenRules.IsEdibleMaterial(input.itemId)||!seen.Add(input.itemId))throw new ArgumentException("Invalid saved free batch input");
                totalUnits+=input.units;
            }
            if(totalUnits>64)throw new ArgumentException("Saved free batch exceeds unit bounds");
            var method=FreeKitchenRules.FindMethod(batch.method??"");
            if(batch.method==null)throw new ArgumentException("Null saved free batch method");
            if(batch.method!=""&&method==null)throw new ArgumentException("Unknown saved free batch method");
            foreach(var input in inputs)if(batch.method!=""&&!FreeKitchenRules.MethodAcceptsMaterial(method,input.itemId))throw new ArgumentException("Saved free batch input does not fit its method");
            var product=batch.product;
            bool coldBatch=FreeKitchenRules.IsColdMethod(batch.method);
            // 硬件事实与做法一致：定了做法就必须绑到真实容器/工具（冷制不绑底座）。
            if(batch.vesselId==""||batch.toolId==""||Array.IndexOf(equipment,batch.vesselId)<0||Array.IndexOf(equipment,batch.toolId)<0
                ||batch.baseId!=""&&Array.IndexOf(equipment,batch.baseId)<0)throw new ArgumentException("Free batch hardware is not equipped");
            bool validHardware=false;
            foreach(var candidate in FreeKitchenRules.Methods)
            {
                if(batch.method!=""&&candidate.id!=batch.method)continue;
                if(Array.IndexOf(candidate.vesselIds,batch.vesselId)<0||Array.IndexOf(candidate.toolIds,batch.toolId)<0)continue;
                bool fits=candidate.cold?batch.baseId=="":Array.IndexOf(candidate.baseIds,batch.baseId)>=0&&FreeKitchenRules.VesselFitsBase(batch.vesselId,batch.baseId);
                foreach(var input in inputs)fits&=FreeKitchenRules.MethodAcceptsMaterial(candidate,input.itemId);
                foreach(var tool in candidate.extraToolIds)fits&=Array.IndexOf(equipment,tool)>=0;
                if(fits&&batch.capacity==FreeKitchenRules.CapacityFor(candidate,batch.vesselId)&&FreeKitchenRules.InputLoad(inputs)<=batch.capacity)validHardware=true;
            }
            if(!validHardware)throw new ArgumentException("Free batch hardware/capacity mismatch");
            if(product==null)
            {
                // 未出品：收集(Prep)/加热(Heating)/出锅窗口(Utensil)/糊锅(Dirty)。
                // 开工即入账份数，所以加热/出锅窗口段 batchRemaining 必须 ≥1。
                if(lane.step!=KitchenStep.Prep&&lane.step!=KitchenStep.Heating&&lane.step!=KitchenStep.Utensil&&lane.step!=KitchenStep.Dirty)throw new ArgumentException("Free batch without product in a serving state");
                if(lane.batchRemaining<0)throw new ArgumentException("Negative free batch remaining");
                if(lane.step!=KitchenStep.Prep&&lane.step!=KitchenStep.Dirty&&lane.batchRemaining<1)throw new ArgumentException("Started free batch without portions");
                if(lane.step==KitchenStep.Prep&&lane.batchRemaining!=0)throw new ArgumentException("Unstarted batch carries cooked portions");
                if(lane.step!=KitchenStep.Prep&&lane.step!=KitchenStep.Dirty&&lane.batchRemaining!=FreeKitchenRules.DerivePortions(inputs))throw new ArgumentException("Started batch yield mismatch");
                return;
            }
            if(product.id!=batch.id||product.method!=batch.method||product.portions<1||product.remaining<0||product.remaining>product.portions||product.remaining!=lane.batchRemaining)throw new ArgumentException("Saved free product does not match its batch");
            if(product.matchedRecipeIndex<-1||product.matchedRecipeIndex>=Recipes.Length||product.visualRecipeIndex<0||product.visualRecipeIndex>=Recipes.Length)throw new ArgumentException("Saved free product recipe indices out of bounds");
            if(string.IsNullOrEmpty(product.name)||product.priceCents<0||product.priceCents>1000000||!Finite(product.quality)||product.quality<0||product.quality>2)throw new ArgumentException("Invalid saved free product facts");
            if(product.inputs==null||product.inputs.Length!=inputs.Length)throw new ArgumentException("Saved free product inputs do not match its batch");
            for(int i=0;i<inputs.Length;i++)if(product.inputs[i]==null||product.inputs[i].itemId!=inputs[i].itemId||product.inputs[i].units!=inputs[i].units)throw new ArgumentException("Saved free product input drift");
            if(product.packages==null||product.packages.Length==0)throw new ArgumentException("Saved free product without packages");
            foreach(string package in product.packages)if(string.IsNullOrEmpty(package)||package.Length>8)throw new ArgumentException("Invalid saved free product package");
            ValidateProductFacts(product);
            if(product.portions!=product.sourcePortions||product.visualRecipeIndex!=lane.recipeIndex)throw new ArgumentException("Free batch identity drift");
            bool cold=FreeKitchenRules.IsColdMethod(batch.method);
            if(cold&&!lane.seasoned)throw new ArgumentException("Cold free product not seasoned");
            if(lane.step!=KitchenStep.Season&&lane.step!=KitchenStep.Pack&&lane.step!=KitchenStep.ReadyStock)throw new ArgumentException("Free product in a non-serving state");
        }
        // FREEDOM19：柜台餐存档校验——独立一份、identity 合法、报价/包材/风味合法。
        private static void ValidateMeal(CounterMeal meal,HashSet<string> mealIds)
        {
            if(meal==null||string.IsNullOrEmpty(meal.id)||!mealIds.Add(meal.id))throw new ArgumentException("Invalid or duplicate saved meal identity");
            if(string.IsNullOrEmpty(meal.packageId)||meal.packageId.Length>8)throw new ArgumentException("Invalid saved meal package");
            if(meal.priceCents<0||meal.priceCents>1000000)throw new ArgumentException("Invalid saved meal quote");
            if(meal.flavorId!=""){meal.flavorId=meal.flavorId??"";if(!StallFlavorMenu.IsFlavor(meal.flavorId))throw new ArgumentException("Unknown saved meal flavor");}
            var product=meal.product;
            if(product==null||string.IsNullOrEmpty(product.id)||string.IsNullOrEmpty(product.name)||string.IsNullOrEmpty(product.method)
                ||FreeKitchenRules.FindMethod(product.method)==null||product.remaining!=1||product.portions!=1
                ||product.matchedRecipeIndex<-1||product.matchedRecipeIndex>=Recipes.Length
                ||product.visualRecipeIndex<0||product.visualRecipeIndex>=Recipes.Length
                ||product.priceCents<0||product.priceCents>1000000||!Finite(product.quality)||product.quality<0||product.quality>2)
                throw new ArgumentException("Invalid saved meal product");
            if(product.inputs==null||product.inputs.Length==0||product.inputs.Length>16)throw new ArgumentException("Invalid saved meal product inputs");
            foreach(var input in product.inputs)if(input==null||input.units<=0||!FreeKitchenRules.IsEdibleMaterial(input.itemId))throw new ArgumentException("Invalid saved meal product input");
            if(product.packages==null||product.packages.Length==0)throw new ArgumentException("Saved meal product without packages");
            foreach(string package in product.packages)if(string.IsNullOrEmpty(package)||package.Length>8)throw new ArgumentException("Invalid saved meal product package");
            ValidateProductFacts(product);
            if((meal.packageId!=EmergencyBagId&&Array.IndexOf(product.packages,meal.packageId)<0)||meal.priceCents!=product.priceCents+StallFlavorMenu.MarkupFor(meal.flavorId)
                ||meal.flavorId!=""&&!FreeKitchenRules.FlavorCompatible(product,meal.flavorId))throw new ArgumentException("Saved meal quote or flavor mismatch");
        }
        static void ValidateProductFacts(FoodProduct product)
        {
            var method=FreeKitchenRules.FindMethod(product.method);
            if(method==null||product.sourcePortions<1||product.sourcePortions!=FreeKitchenRules.DerivePortions(product.inputs)||product.family!=method.family)throw new ArgumentException("Invalid product provenance");
            var keys=new HashSet<string>();foreach(var input in product.inputs)if(input==null||input.units<1||input.units>64||!keys.Add(input.itemId)||!FreeKitchenRules.MethodAcceptsMaterial(method,input.itemId))throw new ArgumentException("Invalid product inputs");
            int matched=FreeKitchenRules.MatchRecipe(product.inputs,product.method,product.sourcePortions);
            int visual=matched>=0?matched:FreeKitchenRules.VisualRecipeIndex(product.method);
            string name=matched>=0?Recipes[matched].name:FreeKitchenRules.LegacyComposeName(product.inputs,product.method);
            if(matched!=product.matchedRecipeIndex||visual!=product.visualRecipeIndex||name!=product.name
                ||product.priceCents!=FreeKitchenRules.FairPriceCents(product.inputs,product.sourcePortions,matched))throw new ArgumentException("Product identity or price drift");
            var packages=FreeKitchenRules.PackagesFor(product.method);
            if(product.packages==null||product.packages.Length!=packages.Length)throw new ArgumentException("Product package set drift");
            for(int i=0;i<packages.Length;i++)if(product.packages[i]!=packages[i])throw new ArgumentException("Product package set drift");
        }
        private static int MaxLanesForEquipment(string[] equipment)
            =>equipment!=null&&Array.IndexOf(equipment,"B04")>=0?2:1;
        private static void ValidateEquipment(string[] ids)
        {
            if(ids==null)throw new ArgumentException("Missing saved equipment array");var known=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in ids)if(string.IsNullOrEmpty(id)||!known.Add(id))throw new ArgumentException("Invalid or duplicate saved equipment identity");
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private bool Any(string[] ids){foreach(string id in ids)if(Equipment.Contains(id))return true;return false;}
        private string Energy(StallRecipe recipe)
        {
            if(recipe.cold)return "none";
            return BaseEnergy(BaseFor(recipe));
        }
        private static string BaseEnergy(string id)=>id=="B01"?"coal":id=="B02"?"cartridge":id=="B03"||id=="B04"?"gas":id==""?"missing":"electricity";
        private string BaseFor(StallRecipe recipe)
        {
            string[] candidates=recipe.microwave?new[]{"B08","B07"}:recipe.id=="fried_rice"?new[]{"B04","B03","B02","B01"}:
                recipe.id=="fried_skewer"?new[]{"B04","B03","B05","B02"}:recipe.id=="soup"?new[]{"B04","B03","B06","B05","B02","B01"}:new[]{"B04","B03","B05","B02","B01"};
            string fallback="";
            foreach(string id in candidates)if(Equipment.Contains(id))
            {if(fallback=="")fallback=id;if(EnergyAvailable(BaseEnergy(id))&&CompatibleVessel(recipe,id))return id;}
            return fallback;
        }
        private bool CompatibleVessel(StallRecipe recipe,string bottom)
        {
            foreach(string id in recipe.vessels)if(Equipment.Contains(id))
            {
                if(recipe.cold)return true;
                if(id=="RiceBucket"&&(bottom=="B07"||bottom=="B08"))return true;
                if((id=="V02"||id=="V03"||id=="V04")&&(bottom=="B03"||bottom=="B04"))return true;
                if(id=="V01"&&(bottom=="B01"||bottom=="B02"||bottom=="B03"||bottom=="B04"))return true;
                if(id=="V05"&&(bottom=="B01"||bottom=="B02"||bottom=="B03"||bottom=="B04"||bottom=="B05"))return true;
                if(id=="V06"&&bottom=="B04"||id=="V07"&&(bottom=="B03"||bottom=="B05"))return true;
                if(id=="V09"&&bottom!=""||id=="V10"&&(bottom=="B04"||bottom=="B06"))return true;
                if(id=="V13"&&(bottom=="B02"||bottom=="B03"||bottom=="B04"||bottom=="B05"))return true;
            }
            return false;
        }
        private bool EnergyAvailable(string energy)=>energy=="none"||energy=="gas"&&Gas>0||energy=="cartridge"&&Cartridge>0||energy=="coal"&&Coal>0||energy=="electricity"&&Electricity>0;
        public string Missing(int index,bool includeStock=true)
        {
            StallRecipe recipe=Recipes[index];var missing=new List<string>();
            string energy=Energy(recipe);
            if(energy=="missing")missing.Add(recipe.microwave?"微波底座":"热源底座");
            else if(includeStock&&!EnergyAvailable(energy))missing.Add(energy=="gas"?"煤气罐余量":energy=="cartridge"?"卡式气余量":energy=="coal"?"蜂窝煤":"电量");
            if(!Any(recipe.vessels))missing.Add(Name(recipe.vessels[0]));
            else if(!CompatibleVessel(recipe,BaseFor(recipe)))missing.Add("适配此底座的锅具");
            if(!Any(recipe.tools))missing.Add(Name(recipe.tools[0]));
            if(index==4&&!Equipment.Contains("U10"))missing.Add("食品夹");
            if(includeStock)
            {
                foreach(var ingredient in recipe.ingredients)if(!SeasoningShortageRules.IsBaseSeasoning(ingredient.Key)&&Count(ingredient.Key)<ingredient.Value)missing.Add(Name(ingredient.Key));
                bool package=false;foreach(string p in recipe.packages)if(Count(p)>0)package=true;
                if(!package)missing.Add(Name(recipe.packages[0]));
            }
            return string.Join(" / ",missing);
        }
        // Base seasoning is a soft gate. This slice records its integrity cost;
        // the full flavor/quality pricing model remains a separate requirement.
        public bool SeasoningAvailable=>Count(SeasoningShortageRules.BaseSeasoningId)>0;
        // ── FLAVOR11 风味加料（B 表 S01/S02/S05）────────────────────────────
        // 是否配菜型：按完整 recipe family 判定，静态可用（不查库存）。
        public bool CanUseFlavor(int recipeIndex,string flavorId)
        {
            if(recipeIndex<0||recipeIndex>=Recipes.Length)return false;
            return StallFlavorMenu.Compatible(Recipes[recipeIndex],flavorId);
        }
        // 生成选项用：仅列出“兼容且确有库存”的风味；没货即不生成该选项。
        // 只读，不改状态，不消耗随机数。
        public List<string> AvailableFlavors(int recipeIndex)
        {
            var result=new List<string>();
            if(recipeIndex<0||recipeIndex>=Recipes.Length)return result;
            foreach(StallFlavor flavor in StallFlavorMenu.Flavors)
                if(Count(flavor.id)>0&&StallFlavorMenu.Compatible(Recipes[recipeIndex],flavor.id))result.Add(flavor.id);
            return result;
        }
        // 顾客点单风味：只有兼容风味有库存时才可能指定（没货不生成需求）；
        // 初期普通菜大多原味——按概率指定，不强制加味。随机流只在新会话出现。
        const float FlavorRequestChance=.35f;
        private string DrawRequestedFlavor(int recipeIndex)
        {
            var options=AvailableFlavors(recipeIndex);
            if(options.Count==0)return "";
            if(RandomValue()>=FlavorRequestChance)return "";
            return options[(int)(RandomValue()*options.Count)];
        }
        public string SoftMissing(int index)
        {
            if(index<0||index>=Recipes.Length)return "";
            // FLAVOR12：cold 饮品/冰粉不扣基础油盐，缺油盐提示对它们是假告警。
            if(Recipes[index].cold)return "";
            return SeasoningAvailable?"":"油盐不足，仍可出餐；缺调味成交每份诚信 "+SeasoningShortageRules.IntegrityDeltaPerShortPortion;
        }
        public static string Name(string id)
        {
            switch(id){case "rice":return "米面";case "egg":return "鸡蛋";case "meat":return "鲜肉";case "dough":return "饼胚";case "skewer":return "串串半成品";case "prepared":return "料理包";case "fruit":return "鲜果";case "seasoning":return "油盐酱料";}
            // FLAVOR11：风味瓶库存（份）在缺货提示里显示中文名。
            if(StallFlavorMenu.IsFlavor(id))return StallFlavorMenu.Find(id).name;
            return id=="RiceBucket"?"保温饭桶":CharacterCatalog.EquipmentName(id);
        }
        public bool SelectDistrict(int index)
        {if(Started||index<0||index>=Districts.Length)return false;DistrictIndex=index;Revision++;return true;}
        public bool ToggleRecipe(int index)
        {
            if(Finished||index<0||index>=Recipes.Length)return false;
            // A reference plan is freely editable, including missing ingredients
            // or an empty list. Real work still checks inventory and hardware.
            if(SelectedRecipes.Contains(index))SelectedRecipes.Remove(index);
            else SelectedRecipes.Add(index);
            Revision++;return true;
        }
        // MENUFLEX10 换菜（锅位级，不是菜单级）：只有真正空闲的锅才可换——
        // Idle、无绑定订单、无取料预留、无剩余批次、非糊锅待清。有余份/在锅有预留/
        // 在忙的锅一律冻结，不能穿锅换菜，也不能用 A 锅卖 B 菜（Serve 仍按
        // lane.recipeIndex 与订单菜严格对齐）。目标菜必须在菜单内，或有一份真实
        // 在等这道菜的原订单（营业中撤菜后，现单原菜仍可换锅完成）。
        public bool CanLaneSwitchRecipe(int laneIndex,int recipeIndex)
        {
            if(!Started||Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            if(recipeIndex<0||recipeIndex>=Recipes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            if(lane.recipeIndex==recipeIndex)return true;
            // PREP12：锅里有已扣库存的蛋液，冻结换菜（蛋液真实留在锅里等米面）。
            if(lane.eggPrepPortions>0)return false;
            if(lane.step!=KitchenStep.Idle||lane.orderId>=0||lane.reservedOrderId>=0)return false;
            if(!MenuOrLiveOrder(recipeIndex))return false;
            // 只查设备级资格（热源/容器/工具）；库存由开工事务在投料接触帧复验，
            // 换菜本身不扣任何库存/能源/钱。
            return Missing(recipeIndex,false)=="";
        }
        public bool SwitchLaneRecipe(int laneIndex,int recipeIndex)
        {
            if(!CanLaneSwitchRecipe(laneIndex,recipeIndex))return false;
            KitchenLane lane=Lanes[laneIndex];
            if(lane.recipeIndex==recipeIndex)return true;
            lane.recipeIndex=recipeIndex;Message=Recipes[recipeIndex].name+"已安排到 "+(laneIndex+1)+" 号锅。";Revision++;return true;
        }
        private bool MenuOrLiveOrder(int recipeIndex)
        {
            if(SelectedRecipes.Contains(recipeIndex))return true;
            foreach(StallOrder order in Orders)if(order.recipeIndex==recipeIndex&&order.arrived&&!order.served&&!order.departed)return true;
            return false;
        }
        // 只读探测：哪口锅已在做这道菜，或哪口锅可换做这道菜；无则 -1。
        // 成功换菜请走 SwitchLaneRecipe（本方法除探测外不改状态）。
        public int FindSwitchableIdleLane(int recipeIndex)
        {
            if(!Started||Finished)return -1;
            int idle=-1;
            for(int i=0;i<Lanes.Length;i++)
            {
                if(Lanes[i].recipeIndex==recipeIndex)return i;
                if(idle<0&&CanLaneSwitchRecipe(i,recipeIndex))idle=i;
            }
            return idle;
        }
        // FREEDOM19：开摊不再有菜单/备货门槛——物资、设备、容量、能源在真实
        // 投料/开火接触帧逐项起作用；空摊也可进入营业，只是不会产生幻影订单。
        public bool Open()
        {
            if(Started||Finished)return false;
            Started=true;
            // MENUFLEX10R：锅位是物理位置（数量=MaxLanes），初始各挂菜单里的一道菜；
            // 菜单小于锅位数时，多余锅位重复挂最后一道菜单菜（开摊后即可换菜）。
            // MinMenuToOpen=1，菜单只有一道时不越界、照样开摊。
            Lanes=new KitchenLane[MaxLanes];
            for(int i=0;i<Lanes.Length;i++)Lanes[i]=new KitchenLane{index=i,recipeIndex=SelectedRecipes.Count>0?SelectedRecipes[Mathf.Min(i,SelectedRecipes.Count-1)]:0,step=KitchenStep.Idle};
            nextCustomer=.8f;ResetBikeBlockForOpen();Message="先备菜，装好拿起；可以直接递餐，也可以放到取餐区。";UpdateServicePhase();Revision++;return true;
        }
        public void Tick(float dt)
        {
            if(!Started||Finished||dt<=0)return;dt=Mathf.Min(dt,Remaining);Elapsed+=dt;UpdateServicePhase();
            foreach(StallOrder order in Orders)
            {
                if(order.departed||order.served||!order.arrived)continue;order.patience-=dt;
                if(order.patience<=0)
                {
                    order.departed=true;order.finishedAt=Elapsed;order.departureReason=order.lane>=0?"patience_during_service":"patience_waiting";ReleaseOrderReservation(order.id);Lost++;Revision++;
                    if(order.lane>=0)
                    {
                        KitchenLane lane=Lanes[order.lane];lane.orderId=-1;
                        if(lane.step==KitchenStep.Season||lane.step==KitchenStep.Pack)lane.step=KitchenStep.ReadyStock;
                    }
                    Message="有顾客等不及离开了。已做好的食物仍可留给下一位。";
                }
            }
            foreach(KitchenLane lane in Lanes)
            {
                // FREEDOM19：自由批次的 Prep 是“收集投料中”，不随时间推进；
                // 只有显式 StartFreeCooking 之后才进入真实加热/手作计时。
                if(lane.step==KitchenStep.Prep&&lane.freeBatch!=null)continue;
                if(lane.step==KitchenStep.Season&&lane.remaining>0){lane.remaining=Mathf.Max(0,lane.remaining-dt);continue;}
                // COLD12：冷制饮品/冰粉的 Utensil 是“待手锤/待舀取”的冷等待——
                // 计时原地冻结，绝不递减出烧锅，玩家取消/走开也不糊一锅饮料。
                if(lane.step==KitchenStep.Utensil&&Recipes[lane.recipeIndex].cold)continue;
                // FREEDOM19：自由批次冷制 Utensil（冷调/锤香待手作）同样冻结计时。
                if(lane.step==KitchenStep.Utensil&&lane.freeBatch!=null&&FreeKitchenRules.IsColdMethod(lane.freeBatch.method))continue;
                if(lane.step!=KitchenStep.Prep&&lane.step!=KitchenStep.Heating&&lane.step!=KitchenStep.Utensil)continue;
                lane.remaining-=dt;
                if(lane.remaining>0)continue;
                StallRecipe recipe=Recipes[lane.recipeIndex];
                if(lane.step==KitchenStep.Prep)
                {
                    if(recipe.cold)
                    {
                        // COLD12：酸梅汤/冰粉 Prep 完成即“已调味可装杯/碗”（seasoned=true，
                        // 递餐不扣油盐、ReadyStock 盛出直接 Pack）；柠檬茶先走一次手锤。
                        if(RequiresColdHandAction(recipe)){lane.step=KitchenStep.Utensil;lane.toolClicks=0;lane.remaining=lane.duration=recipe.greenSeconds;Message=recipe.name+"基底就绪，点击"+recipe.toolLabel+"。";}
                        else{lane.seasoned=true;lane.step=lane.orderId>=0?KitchenStep.Pack:KitchenStep.ReadyStock;lane.remaining=0;}
                    }
                    else{lane.step=KitchenStep.Heating;lane.remaining=lane.duration=HeatTime(recipe);}
                }
                else if(lane.step==KitchenStep.Heating)
                {
                    // FREEDOM19：自由批次加热完成；V04 自动炒锅保留既有红利——
                    // 现炒自动出锅（品质见 FinishFreeUtensil），其余进 20 秒真实绿区。
                    if(lane.freeBatch!=null)
                    {
                        if(lane.freeBatch.vesselId=="V04"&&lane.freeBatch.method==FreeKitchenRules.MethodStirFry){FinishFreeUtensil(lane);}
                        else{lane.step=KitchenStep.Utensil;lane.toolClicks=0;lane.remaining=lane.duration=FreeHotWindowSeconds;Message="火候正好，点击锅具出锅。";}
                    }
                    else
                    {
                    bool automatic=HeatIsAutomatic(recipe,lane.recipeIndex);
                    if(automatic){lane.step=lane.orderId>=0?KitchenStep.Season:KitchenStep.ReadyStock;lane.remaining=0;Message="受热完成，进入调味。";}
                    else{lane.step=KitchenStep.Utensil;lane.toolClicks=0;lane.remaining=lane.duration=recipe.greenSeconds+(IsWokStyle(lane.recipeIndex)&&Equipment.Contains("U02")?1.5f:0);Message=recipe.name+"火候正好，点击"+recipe.toolLabel+"。";}
                    }
                }
                else
                {
                    int wasted=lane.batchRemaining;
                    if(lane.freeBatch!=null)
                    {
                        // FREEDOM19：自由批次错过出锅窗口——报废开工时入账的真实份数，
                        // 只记自由账，不与旧菜谱账重复累计；批次结清，成品不可再售。
                        WastedPortions+=wasted;AddCount(freePortionsWasted,lane.recipeIndex,wasted);
                        lane.freeBatch=null;Message="错过出锅时机，这锅已糊。清理后再接单。";
                    }
                    else
                    {
                        WastedPortions+=wasted;AddCount(portionsWasted,lane.recipeIndex,wasted);
                        Message="错过出锅时机，这锅已糊。清理后再接单。";
                    }
                    lane.batchRemaining=0;lane.prepaidSeasoning=0;lane.step=KitchenStep.Dirty;Unassign(lane);PublishBatchBurned(lane.index,lane.recipeIndex,wasted);
                }
                Revision++;
            }
            if(AcceptingOrders)TickBikeBlock(dt);
            if(AcceptingOrders)nextCustomer-=dt*CustomerArrivalRateMultiplier;
            if(AcceptingOrders&&nextCustomer<=0)
            {
                SpawnOrder();
                // Keep a finite peak/trough around the measured nominal rate.
                float wave=1f+.20f*Mathf.Sin(Elapsed/Setup.dayDuration*Mathf.PI*8);
                nextCustomer=Mathf.Max(1,Setup.customerIntervalSeconds*wave+((float)RandomValue()-.5f));
            }
            if(Remaining<=.0001f)Close();
        }
        private void UpdateServicePhase()
        {
            BusinessServicePhase next=Finished?BusinessServicePhase.Closed:!Started?BusinessServicePhase.Planning:
                Elapsed>=LastOrderAt?BusinessServicePhase.Clearing:
                SecondsUntilLastOrder<=Mathf.Max(0,Setup.lastCallNoticeSeconds)?BusinessServicePhase.LastCall:BusinessServicePhase.Open;
            if(next==ServicePhase)return;ServicePhase=next;Revision++;
            if(next==BusinessServicePhase.LastCall)Message="最后接单：即将停止接新客，已接订单照常制作。";
            else if(next==BusinessServicePhase.Clearing)Message="停止接新客，清台完成已有订单；收摊时间不延长。";
        }
        private float HeatTime(StallRecipe recipe)
        {
            if(recipe.microwave)return Equipment.Contains("B08")?3.5f:6;
            string bottom=BaseFor(recipe);float fire=bottom=="B04"?1.25f:bottom=="B03"?1.15f:bottom=="B05"?1.1f:bottom=="B02"?.85f:.75f;
            return recipe.heatSeconds/fire*(recipe.id=="fried_rice"&&Equipment.Contains("V03")&&!Equipment.Contains("U02")?1.2f:1);
        }
        // The one authority for "this pan never opens a manual green window":
        // microwaves and automatic vessels finish heating on their own. Cold
        // recipes never heat either — except lemon_tea, which is cold-made but
        // still needs one manual muddler action (Prep → Utensil, no flame).
        // PANTRY9：自动炒锅（V04）对整组现炒系（含简谱素炒饭/滑蛋/鲜肉小炒）生效，
        // 口径与母菜 fried_rice 完全一致。
        private bool HeatIsAutomatic(StallRecipe recipe,int recipeIndex)
            =>recipe.microwave||IsWokStyle(recipeIndex)&&Equipment.Contains("V04")||recipeIndex==4&&Equipment.Contains("V14");
        // COLD12：柠檬茶是冷制饮品中唯一需要一次真实手锤的菜（Prep 完成进
        // Utensil 等待锤香，不点火、等待不烧锅）；酸梅汤/冰粉 Prep 直通可售。
        static bool RequiresColdHandAction(StallRecipe recipe)=>recipe.cold&&recipe.id=="lemon_tea";
        public bool RequiresManualHeatAction(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            // Reviewer release fix: the free state machine always waits for a real
            // finishing contact except an actual V04 stir-fry batch. Its legacy
            // visual recipe must not suppress cold mixing or microwave pickup.
            if(lane.freeBatch!=null)
                return !(lane.freeBatch.vesselId=="V04"&&lane.freeBatch.method==FreeKitchenRules.MethodStirFry);
            StallRecipe recipe=Recipes[lane.recipeIndex];
            return !recipe.cold&&!HeatIsAutomatic(recipe,lane.recipeIndex)||RequiresColdHandAction(recipe);
        }
        public float SecondsUntilUtensil(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return float.PositiveInfinity;KitchenLane lane=Lanes[laneIndex];
            if(lane.step==KitchenStep.Utensil)return 0;
            if(!RequiresManualHeatAction(laneIndex))return float.PositiveInfinity;
            if(lane.step==KitchenStep.Heating)return lane.remaining;
            // COLD12：冷制（柠檬茶）Prep 之后没有加热段，到锤香窗口只剩 Prep 计时。
            if(lane.step==KitchenStep.Prep)return lane.remaining+(Recipes[lane.recipeIndex].cold?0:HeatTime(Recipes[lane.recipeIndex]));
            return float.PositiveInfinity;
        }
        private const float FreeHotWindowSeconds = 20f;
        public float GreenWindowSeconds(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return 0;
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null&&!FreeKitchenRules.IsColdMethod(lane.freeBatch.method))
                return lane.step==KitchenStep.Utensil?lane.duration:FreeHotWindowSeconds;
            int recipe=lane.recipeIndex;
            return Recipes[recipe].greenSeconds+(IsWokStyle(recipe)&&Equipment.Contains("U02")?1.5f:0);
        }
        private void SpawnOrder()
        {
            ArrivalAttempts++;int count=0;foreach(var order in Orders)if(!order.departed&&!order.served)count++;
            if(count>=Mathf.Max(1,Setup.queueCapacity)){QueueTurnaways++;return;}
            var menu=new List<int>();float sum=0;
            foreach(int index in SelectedRecipes)
            {
                bool cooked=false;foreach(var lane in Lanes)if(lane.batchRemaining>0&&lane.step!=KitchenStep.Dirty&&(lane.freeBatch==null?lane.recipeIndex==index:lane.freeBatch.product!=null&&lane.freeBatch.product.matchedRecipeIndex==index))cooked=true;
                bool pack=false;foreach(string id in Recipes[index].packages)pack|=Count(id)>0;
                bool legacyCooked=false;foreach(var lane in Lanes)if(lane.freeBatch==null&&lane.recipeIndex==index&&lane.batchRemaining>0&&lane.step!=KitchenStep.Dirty)legacyCooked=true;
                bool identifiable=FreeKitchenRules.RecognizesRecipe(index);
                if(identifiable&&Missing(index)==""||cooked&&pack&&(identifiable||legacyCooked)){menu.Add(index);sum+=DistrictWeight(index);}
            }
            // FREEDOM19 单一加权池：旧菜谱菜单项 + 锅内真实成品 + 柜台已装餐。
            // 成品供应按 identity 扣掉已被挂单占用的份数，防止同一批供应被
            // 多个同时在等的订单超卖；没有兼容包材的成品不进池。
            int PendingForProduct(string productId,string flavor){int n=0;foreach(var o in Orders)if(o.productId==productId&&(o.requestedFlavorId??"")==flavor&&!o.served&&!o.departed)n++;return n;}
            int PackedCount(string productId,string flavor){int n=0;foreach(var meal in PackedMeals())if(meal.product!=null&&meal.product.id==productId&&(meal.flavorId??"")==flavor)n++;return n;}
            // An emergency wrapper is available after a real cooked portion exists.
            bool PackAvailable(FoodProduct p){foreach(string pk in p.packages)if(Count(pk)>0)return true;return CanUseEmergencyBag(p);}
            var pool=new List<KeyValuePair<float,object>>();
            foreach(int index in menu)pool.Add(new KeyValuePair<float, object>(DistrictWeight(index),index));
            foreach(var lane in Lanes)
            {
                var product=lane.freeBatch==null?null:lane.freeBatch.product;
                if(product==null)continue;
                if(lane.step!=KitchenStep.ReadyStock&&lane.step!=KitchenStep.Season&&lane.step!=KitchenStep.Pack)continue;
                int meals=PackedCount(product.id,"");
                if(product.remaining+meals-PendingForProduct(product.id,"")<=0)continue;
                if(!PackAvailable(product))continue;
                pool.Add(new KeyValuePair<float, object>(DistrictWeight(lane.recipeIndex),product));
            }
            foreach(var meal in PackedMeals())
            {
                var product=meal.product;if(product==null)continue;
                string flavor=meal.flavorId??"";int meals=PackedCount(product.id,flavor);
                if(meals-PendingForProduct(product.id,flavor)<=0)continue;
                pool.Add(new KeyValuePair<float, object>(DistrictWeight(product.visualRecipeIndex),meal));
            }
            if(pool.Count==0){SoldOutArrivals++;Message=HeldMeal!=null?"手上有餐，可以递给想吃这份的食客。":ReadyMeals.Count>0?"取餐区有餐，点想要这份餐的食客来取。":"先做好一份，再招呼路过的食客。";return;}
            float poolSum=0;foreach(var entry in pool)poolSum+=entry.Key;
            double pick=RandomValue()*Mathf.Max(.0001f,poolSum);
            object picked=pool[pool.Count-1].Value;
            foreach(var entry in pool){pick-=entry.Key;if(pick<=0){picked=entry.Value;break;}}
            float patience=Setup.patienceMinSeconds+(int)(RandomValue()*Mathf.Max(1,Setup.patienceMaxSeconds-Setup.patienceMinSeconds));
            string[] names={"赶时间的上班族","街坊阿姨","夜市常客","放学的学生","路过的食客"};
            if(picked is CounterMeal counterMeal)
            {
                // FREEDOM19 散客：只来自真实供应（柜台餐/锅内成品），点菜即拷贝稳定成品事实。
                var product=counterMeal.product;
                var walkOrder=new StallOrder{id=++orderSerial,recipeIndex=product.visualRecipeIndex,patience=patience,maxPatience=patience,spawnedAt=Elapsed,customer=names[(orderSerial-1)%names.Length],
                    productId=product.id,productName=product.name,productMethod=product.method,productFamily=product.family,productPriceCents=product.priceCents,productInputs=CloneInputs(product.inputs),productSourcePortions=product.sourcePortions,requestedFlavorId=counterMeal.flavorId};
                Orders.Add(walkOrder);Revision++;return;
            }
            if(picked is FoodProduct walkProduct)
            {
                var walkOrder=new StallOrder{id=++orderSerial,recipeIndex=walkProduct.visualRecipeIndex,patience=patience,maxPatience=patience,spawnedAt=Elapsed,customer=names[(orderSerial-1)%names.Length],
                    productId=walkProduct.id,productName=walkProduct.name,productMethod=walkProduct.method,productFamily=walkProduct.family,productPriceCents=walkProduct.priceCents,productInputs=CloneInputs(walkProduct.inputs),productSourcePortions=walkProduct.sourcePortions};
                Orders.Add(walkOrder);Revision++;return;
            }
            int chosen=(int)picked;
            var created=new StallOrder{id=++orderSerial,recipeIndex=chosen,patience=patience,maxPatience=patience,spawnedAt=Elapsed,customer=names[(orderSerial-1)%names.Length]};
            // FLAVOR11：顾客指定风味只从“有库存的兼容风味”里抽；未指定=原味基础价。
            created.requestedFlavorId=DrawRequestedFlavor(chosen);
            Orders.Add(created);Revision++;
        }
        public StallOrder FindOrder(int id)=>Orders.Find(order=>order.id==id);
        public bool MarkCustomerArrived(int id)
        {
            StallOrder order=FindOrder(id);if(order==null||order.arrived||order.departed||order.served||Finished)return false;
            order.arrived=true;order.arrivedAt=Elapsed;Revision++;return true;
        }
        // Reserving an intent claims no inventory/energy/money. It only prevents
        // two queued pickups from choosing the same order or physical pan.
        public bool ReserveOrder(int id,int laneIndex)
        {
            if(!Started||Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            StallOrder order=FindOrder(id);KitchenLane lane=Lanes[laneIndex];
            if(order==null||!order.arrived||order.departed||order.served||order.lane>=0||order.reservedLane>=0||lane.reservedOrderId>=0)return false;
            if(order.recipeIndex!=lane.recipeIndex||(lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.ReadyStock))return false;
            order.reservedLane=laneIndex;lane.reservedOrderId=id;Revision++;return true;
        }
        public void ReleaseOrderReservation(int id)
        {
            StallOrder order=FindOrder(id);if(order==null||order.reservedLane<0)return;
            int lane=order.reservedLane;order.reservedLane=-1;
            if(lane<Lanes.Length&&Lanes[lane].reservedOrderId==id)Lanes[lane].reservedOrderId=-1;
            Revision++;
        }
        // PREP12：锅内是否处于“已敲蛋、待下米饭”中间态（供 Controller/UIPresenter 短状态）。
        public bool LaneNeedsRice(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            return lane.step==KitchenStep.Idle&&lane.eggPrepPortions>0;
        }
        // PREP12：控制台/UIPresenter 用的一句短状态：""(无关) | "待敲蛋" | "待下米饭"。
        public string DescribeEggPrep(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return "";
            KitchenLane lane=Lanes[laneIndex];
            if(!RequiresEggCrack(lane.recipeIndex))return "";
            if(lane.eggPrepPortions>0)return "待下米饭";
            return lane.step==KitchenStep.Idle?"待敲蛋":"";
        }
        // PREP12：这口锅下一步需要的真实主料（""= 非两拍菜或无需投料）。
        // 空锅先返回 "egg"（敲蛋下锅）；已有蛋液返回 "rice"（下米面开炒）。
        public string NextPrepIngredient(int laneIndex)
        {
            if(laneIndex<0||laneIndex>=Lanes.Length)return "";
            KitchenLane lane=Lanes[laneIndex];
            if(!RequiresEggCrack(lane.recipeIndex)||lane.step!=KitchenStep.Idle)return "";
            if(lane.eggPrepPortions>0)
            {
                foreach(var ingredient in Recipes[lane.recipeIndex].ingredients)
                    if(!SeasoningShortageRules.IsBaseSeasoning(ingredient.Key)&&ingredient.Key!="egg")return ingredient.Key;
                return "";
            }
            return "egg";
        }
        public bool BeginOrder(int id,int laneIndex)
        {
            if(!Started||Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            var order=FindOrder(id);KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null)return Reject("这口锅是自由批次，按投料出锅出售。");
            if(order==null||!order.arrived||order.departed||order.served||order.lane>=0)return Reject("这张订单已处理或离开。");
            if(order.recipeIndex!=lane.recipeIndex)return Reject("这道菜要用对应的锅位。");
            if(lane.reservedOrderId>=0&&lane.reservedOrderId!=id||order.reservedLane>=0&&order.reservedLane!=laneIndex)return Reject("这个锅位已有另一份取料安排。");
            if(lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.ReadyStock)return Reject("这个锅位还在忙。");
            StallRecipe recipe=Recipes[lane.recipeIndex];
            if(lane.step==KitchenStep.Idle)
            {
                // PREP12：两拍现炒（蛋炒饭）的取料接触只完成“敲蛋下锅”——
                // 真实扣一批蛋、进入待下米饭中间态；米面由玩家第二次真实接触推进。
                // 旧诊断/旧入口经 BeginOrder 同样只到中间态，公开签名不变。
                if(RequiresEggCrack(lane.recipeIndex))return BeginEggCrackTransaction(lane,id);
                if(!BeginBatchTransaction(lane))return false;
            }
            else{lane.step=lane.seasoned?KitchenStep.Pack:KitchenStep.Season;lane.remaining=lane.seasoned?0:recipe.id=="soup"||recipe.cold?.5f:0;}
            ReleaseOrderReservation(id);lane.orderId=id;order.lane=laneIndex;order.startedAt=Elapsed;Message=lane.step==KitchenStep.Prep?"已投料："+recipe.name+"。":lane.seasoned?"盛出已熟余份，选择包材出餐。":"已盛出熟食，完成调味后封装。";Revision++;return true;
        }
        // 无订单备餐开工：与 BeginOrder 共用同一段投料/能源/批量事务，只在
        // 真实 Actor 取料倒锅接触帧被调用一次。锅保持 orderId=-1，完整走
        // Prep/Heating/Utensil/Season/Pack/ReadyStock；守恒审计与有订单批次
        // 完全一致（batchesStarted/portionsStarted 同时入账）。
        public bool BeginBatch(int laneIndex)
        {
            if(!Started||Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null)return Reject("这口锅是自由批次，直接投料即可。");
            if(lane.step!=KitchenStep.Idle)return Reject("这个锅位还在忙。");
            if(lane.reservedOrderId>=0)return Reject("这个锅位已有取料安排。");
            // PREP12：两拍现炒的无订单备餐第一拍同样只是敲蛋下锅。
            if(RequiresEggCrack(lane.recipeIndex))return BeginEggCrackTransaction(lane,-1);
            if(!BeginBatchTransaction(lane))return false;
            lane.orderId=-1;Message="已投料备餐："+Recipes[lane.recipeIndex].name+"，熟后卖给任一同菜顾客。";Revision++;return true;
        }
        // PREP12：待下米饭锅的第二拍真实接触（米面下锅）。
        // 有订单：订单必须在敲蛋时已绑定本锅；无订单：锅内留有蛋液批次即可。
        public bool AddRiceForOrder(int orderId,int laneIndex)
        {
            if(!Started||Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            var order=FindOrder(orderId);KitchenLane lane=Lanes[laneIndex];
            if(order==null||order.departed||order.served)return Reject("这张订单已处理或离开。");
            if(order.recipeIndex!=lane.recipeIndex)return Reject("这道菜要用对应的锅位。");
            if(lane.orderId!=orderId)return Reject("这口锅没有这份订单的蛋液。");
            return AddRiceToEggPrep(lane);
        }
        public bool AddRiceBatch(int laneIndex)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            return AddRiceToEggPrep(Lanes[laneIndex]);
        }
        private bool AddRiceToEggPrep(KitchenLane lane)
        {
            if(lane.step!=KitchenStep.Idle||lane.eggPrepPortions<=0)return Reject("这口锅不在待下米饭状态。");
            // 米面（及批量内其余主料）在接触帧复验；蛋已在敲蛋时按同批扣过，
            // BeginBatchTransaction 跳过已扣蛋，绝不重复扣减。
            return BeginBatchTransaction(lane,lane.eggPrepPortions);
        }
        // PREP12 敲蛋事务：只用主料（蛋+米面）约束批量、只扣蛋、不点能源。
        // 能源与批次账在米面下锅那一拍才走 BeginBatchTransaction。
        // orderId>=0 时完成订单绑定（等同 BeginOrder 的绑定语义）。
        private bool BeginEggCrackTransaction(KitchenLane lane,int orderId)
        {
            if(lane.eggPrepPortions>0||lane.orderId>=0)return Reject("这口锅已有蛋液，先下米面。");
            StallRecipe recipe=Recipes[lane.recipeIndex];
            string equipMissing=Missing(lane.recipeIndex,false);
            if(equipMissing!="")return Reject("暂时缺少："+equipMissing);
            int batch=recipe.batchSize;
            if(IsWokStyle(lane.recipeIndex))batch=Equipment.Contains("V03")?4:Equipment.Contains("V02")?2:1;
            foreach(var ingredient in recipe.ingredients)
            {
                if(SeasoningShortageRules.IsBaseSeasoning(ingredient.Key))continue;
                batch=Mathf.Min(batch,Count(ingredient.Key)/ingredient.Value);
            }
            if(batch<=0)return Reject("暂时缺少："+Missing(lane.recipeIndex));
            Inventory["egg"]-=recipe.ingredients["egg"]*batch;
            lane.eggPrepPortions=batch;
            if(orderId>=0)
            {
                var order=FindOrder(orderId);
                ReleaseOrderReservation(orderId);
                lane.orderId=orderId;order.lane=lane.index;order.startedAt=Elapsed;
                Message="已敲蛋下锅（"+batch+"份蛋液）· 再点米面下锅开炒";
            }
            else Message="已敲蛋下锅备餐（"+batch+"份蛋液）· 再点米面下锅开炒";
            Revision++;return true;
        }
        // 无订单批次的盛出：ReadyStock → Season/Pack，不绑定任何顾客。
        // 与 BeginOrder 的 ReadyStock 分支同一段状态推进，只是不建立订单绑定。
        public bool ScoopBatch(int laneIndex)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            if(lane.freeBatch!=null)return Reject("这口锅是自由批次，按真实成品出餐。");
            if(lane.step!=KitchenStep.ReadyStock||lane.batchRemaining<=0)return Reject("锅里还没有可盛出的熟食。");
            StallRecipe recipe=Recipes[lane.recipeIndex];
            lane.step=lane.seasoned?KitchenStep.Pack:KitchenStep.Season;lane.remaining=lane.seasoned?0:recipe.id=="soup"||recipe.cold?.5f:0;
            Message=lane.seasoned?"盛出已熟余份，选择包材出餐。":"已盛出熟食，完成调味后封装。";Revision++;return true;
        }
        // BeginOrder（Idle 锅投料）与 BeginBatch 唯一共享的扣减事务：
        // 缺件检查、整批主料、能源、批次账、Prep 计时。调用前必须已确认
        // Started、锅为 Idle。失败只写 Message，不产生任何扣减。
        // eggPrepped>0 = PREP12 续炒：批量固定为已扣蛋的份数，蛋不再重复扣，
        // 缺件检查只验设备与批量内其余主料（包材到出餐才需要，不卡下锅）。
        private bool BeginBatchTransaction(KitchenLane lane,int eggPrepped=0)
        {
            StallRecipe recipe=Recipes[lane.recipeIndex];
            if(eggPrepped>0)
            {
                string equipMissing=Missing(lane.recipeIndex,false);
                if(equipMissing!="")return Reject("暂时缺少："+equipMissing);
                foreach(var ingredient in recipe.ingredients)
                {
                    if(SeasoningShortageRules.IsBaseSeasoning(ingredient.Key)||ingredient.Key=="egg")continue;
                    if(Count(ingredient.Key)<ingredient.Value*eggPrepped)return Reject("暂时缺少："+Name(ingredient.Key));
                }
            }
            else
            {
                string missing=Missing(lane.recipeIndex);if(missing!="")return Reject("暂时缺少："+missing);
            }
            int batch=eggPrepped>0?eggPrepped:recipe.batchSize;
            // PANTRY9：现炒系简谱菜与母菜共用批量口径（V03 大锅 4 / V02 深锅 2 / 薄锅 1）。
            if(eggPrepped<=0&&IsWokStyle(lane.recipeIndex))batch=Equipment.Contains("V03")?4:Equipment.Contains("V02")?2:1;
            // S00：基础油盐不按批量投料，改在 Serve 时每份扣 1 份；批量只受
            // 真实主料限制，剩 1 份油盐不会再卡小整锅。
            if(eggPrepped<=0)
                foreach(var ingredient in recipe.ingredients)if(!SeasoningShortageRules.IsBaseSeasoning(ingredient.Key))batch=Mathf.Min(batch,Count(ingredient.Key)/ingredient.Value);
            if(batch<=0)return Reject("暂时缺少："+Missing(lane.recipeIndex));
            foreach(var ingredient in recipe.ingredients)if(!SeasoningShortageRules.IsBaseSeasoning(ingredient.Key))
            {
                int need=ingredient.Value*batch;
                if(eggPrepped>0&&ingredient.Key=="egg")need=Mathf.Max(0,need-eggPrepped); // 蛋已在敲蛋时扣过
                Inventory[ingredient.Key]-=need;
            }
            AddCount(batchesStarted,lane.recipeIndex,1);AddCount(portionsStarted,lane.recipeIndex,batch);lane.batchRemaining=batch;lane.energy=Energy(recipe);lane.seasoned=false;lane.prepaidSeasoning=0;lane.eggPrepPortions=0;
            if(lane.energy=="gas")Gas--;else if(lane.energy=="cartridge")Cartridge--;else if(lane.energy=="coal")Coal--;else if(lane.energy=="electricity")Electricity--;
            lane.step=KitchenStep.Prep;lane.remaining=lane.duration=recipe.cold?.5f:.65f;
            return true;
        }
        public bool Action(int laneIndex)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            // FREEDOM19：自由批次走真实批次状态机（出锅/调味/盛出/清理）。
            if(lane.freeBatch!=null)return ActionFreeLane(lane);
            if(lane.step==KitchenStep.Utensil)
            {
                if(Recipes[lane.recipeIndex].cold)
                {
                    // COLD12：柠檬茶手锤完成——冷制无调味步，直接视为已调味可装杯。
                    lane.seasoned=true;lane.step=lane.orderId>=0?KitchenStep.Pack:KitchenStep.ReadyStock;lane.remaining=0;Message=Recipes[lane.recipeIndex].name+"锤香完成，可以装杯出餐。";
                }
                else
                {
                    if(lane.recipeIndex==3&&!Equipment.Contains("U12")&&lane.toolClicks++==0){Message="短柄勺还需盛一下。";Revision++;return true;}
                    lane.step=lane.orderId>=0?KitchenStep.Season:KitchenStep.ReadyStock;
                    lane.remaining=lane.recipeIndex==4&&!Equipment.Contains("U09")?1:0;Message=lane.remaining>0?"提起笊篱，沥油 1 秒。":"火候正好，接下来调味。";
                }
            }
            else if(lane.step==KitchenStep.Season)
            {
                if(lane.remaining>0)return false;
                lane.seasoned=true;lane.step=KitchenStep.Pack;
                // FLAVOR12：cold 饮品/冰粉递餐不扣油盐，不给“缺油盐扣诚信”的假提示。
                Message=Recipes[lane.recipeIndex].cold||SeasoningAvailable||lane.prepaidSeasoning>0?"调味完成，选择适配包材出餐。":"油盐不足，按原样封装；本份成交将扣诚信。";
            }
            else if(lane.step==KitchenStep.Dirty){lane.step=KitchenStep.Idle;lane.prepaidSeasoning=0;Message="锅位已清理。";}
            else return false;
            Revision++;return true;
        }
        // FLAVOR11 真实选瓶：只在锅真正到达 drained Season 步骤的接触帧调用。
        // 校验风味存在/菜系兼容/有货后才记到 lane.pendingFlavorId；扣量仍只在
        // 递餐帧一次性发生。同一步点击原味（无参 Action/UseSeasoning(int)）不变。
        public bool Action(int laneIndex,string flavorId)
        {
            if(Finished)return false;
            if(IsFreeLane(laneIndex)&&SeasoningShortageRules.IsBaseSeasoning(flavorId))
            {
                var freeLane=Lanes[laneIndex];
                if(freeLane.freeBatch.product==null||freeLane.remaining>0)return false;
                if(freeLane.step!=KitchenStep.Season&&freeLane.step!=KitchenStep.Pack&&freeLane.step!=KitchenStep.ReadyStock)return false;
                if(freeLane.step==KitchenStep.ReadyStock&&!ScoopFreeBatch(freeLane))return false;
                if(freeLane.step==KitchenStep.Season&&!ActionFreeLane(freeLane))return false;
                freeLane.pendingFlavorId="";Message="这份做原味，装好再上桌。";Revision++;return true;
            }
            if(!StallFlavorMenu.IsFlavor(flavorId))return Reject("没有这种风味调料。");
            if(laneIndex<0||laneIndex>=Lanes.Length)return false;
            KitchenLane lane=Lanes[laneIndex];
            // FREEDOM19：自由批次按实际成品菜系校验风味，不看 visualRecipeIndex。
            if(lane.freeBatch!=null)
            {
                var product=lane.freeBatch.product;
                if(product==null)return Reject("成品出锅后才能选风味。");
                if((lane.step!=KitchenStep.Season&&lane.step!=KitchenStep.Pack&&lane.step!=KitchenStep.ReadyStock)||lane.remaining>0)return false;
                if(!FreeKitchenRules.FlavorCompatible(product,flavorId))return Reject(StallFlavorMenu.Find(flavorId).name+"不适合"+product.DisplayName+"。");
                if(Count(flavorId)<=0)return Reject(StallFlavorMenu.Find(flavorId).name+"已经用完 · 先补货");
                if(lane.step!=KitchenStep.Pack&&!ActionFreeLane(lane))return false;
                lane.pendingFlavorId=flavorId;Revision++;
                Message=product.DisplayName+"已加"+StallFlavorMenu.Find(flavorId).name;
                return true;
            }
            if(lane.step!=KitchenStep.Season||lane.remaining>0)return false;
            if(!CanUseFlavor(lane.recipeIndex,flavorId))return Reject(StallFlavorMenu.Find(flavorId).name+"不适合"+Recipes[lane.recipeIndex].name+"。");
            if(Count(flavorId)<=0)return Reject(StallFlavorMenu.Find(flavorId).name+"已经用完 · 先补货");
            if(!Action(laneIndex))return false;
            lane.pendingFlavorId=flavorId;Revision++;
            Message=Recipes[lane.recipeIndex].name+"已加"+StallFlavorMenu.Find(flavorId).name;
            return true;
        }
        // orderId<0：优先使用锅的绑定订单，否则自动匹配"同 recipe、
        // 已 arrived、未 served/departed、等待最久"的订单。找不到合法顾客时
        // 拒绝且不扣包材/油盐/现金，熟食原样留在锅里（step 仍是 Pack）。
        // 显式 orderId 指到已离开/已服务/菜品不符的订单同样拒绝，不静默改绑。
        // 2 参 Serve 是既有公开契约（旧反射/旧测试按此签名取方法），保持
        // 原样；需要显式订单的 3 参逻辑走 ServeToOrder，不共用可选参数签名。
        public bool Serve(int laneIndex,string package)
        {return ServeToOrder(laneIndex,package,-1);}
        public bool ServeToOrder(int laneIndex,string package,int orderId)
        {
            if(Finished||laneIndex<0||laneIndex>=Lanes.Length)return false;KitchenLane lane=Lanes[laneIndex];
            // FREEDOM19：自由成品走 pack-first 柜台全链（PackToCounter → ServeCounterMeal）；
            // ServeToOrder 保持旧菜谱路径与本方法签名不变。
            if(lane.freeBatch!=null)return Reject("这口锅是自由批次：先装台，再递给顾客。");
            if(lane.step!=KitchenStep.Pack)return Reject("先完成受热和调味。");
            StallRecipe recipe=Recipes[lane.recipeIndex];if(Array.IndexOf(recipe.packages,package)<0)return Reject("这种包材不适合这道菜。");
            if(Count(package)<=0)return Reject("包材已经用完。");
            StallOrder order=FindOrder(orderId>=0?orderId:lane.orderId);
            if(order!=null&&(order.departed||order.served||order.recipeIndex!=lane.recipeIndex))order=null;
            if(order==null&&orderId>=0)return orderId==lane.orderId?Reject("顾客已经离开，请把这份留给下一位。"):Reject("这份订单已离开或不是这道菜。");
            if(order==null)order=FindWaitingOrder(lane.recipeIndex);
            if(order==null)return Reject("还没有等这份菜的顾客 · 熟食留在锅里。");
            // FLAVOR12：递餐帧一次性结算风味——只有“顾客指定 ∧ 玩家实际选同一瓶 ∧
            // 仍有库存”才加价并记 servedFlavorId；选错兼容瓶也真实消耗 1 份但按基础价
            // 成交（见 ResolveServeFlavor）。lane.pendingFlavorId 此处一斗即清。
            string flavorId=ResolveServeFlavor(lane,order);
            int priceCents=recipe.priceCents+(flavorId!=""?StallFlavorMenu.MarkupFor(flavorId):0);
            bool flavorMissed=flavorId==""&&order.requestedFlavorId!="";
            // RISK7：全部校验通过、任何扣减发生之前，先取风险闸门决策并校验其
            // 合法性（active 状态/金额/窗口）。坏决策一律降级为正常成交并告警，
            // 绝不产生半次交易；未注入闸门时原回归行为分毫不变。
            StallRiskGateDecision gate=InvokeServeRiskGate(order.id,lane.recipeIndex,priceCents);
            if(gate!=null&&gate.adoptAsRisk)
            {
                // 先 Adopt（可能因并发激活事件抛错），成功后再提交全部出餐扣减，
                // 保证事务要么完整要么不发生。
                StallRiskEvent risk=RiskState.Adopt(StallRiskRules.EventA01,order.id,laneIndex,recipe.id,priceCents,gate.interceptSeconds,gate.collectCents);
                bool shortRisk=DebitSeasoningForServe(lane);
                Inventory[package]--;lane.batchRemaining--;Served++;
                order.served=true;order.finishedAt=Elapsed;if(order.lane==laneIndex)order.lane=-1;lane.orderId=-1;lane.step=lane.batchRemaining>0?KitchenStep.ReadyStock:KitchenStep.Idle;
                // 单次支付事实：已交付未收款；出餐风味/缺油盐事实随单保存，追回结算
                // 重放 SaleCompleted 时不丢。
                order.paidCents=0;order.riskSerial=risk.Serial;order.shortSeasoning=shortRisk;order.servedFlavorId=flavorId;RiskEventsIssued++;
                Message=recipe.name+(flavorId!=""?"加"+StallFlavorMenu.Find(flavorId).name:"")+"已出餐，对方扫码未到账——在 "+gate.interceptSeconds.ToString("0.0")+" 秒内点击对方追回！";
                Revision++;PublishRiskSalePending(risk);return true;
            }
            // S00：每单自动扣 1 份基础油盐。先抵旧 v2 批次已预付的份（迁移一次
            // 性建立，只减不增），不足再扣库存；库存也缺才按缺料记事实。放在全部
            // 拒绝分支之后，失败/重复 Serve 不抵扣不扣减不处罚。
            bool shortSeasoning=DebitSeasoningForServe(lane);
            Inventory[package]--;lane.batchRemaining--;CashCents+=priceCents;RevenueCents+=priceCents;Served++;
            order.served=true;order.finishedAt=Elapsed;if(order.lane==laneIndex)order.lane=-1;lane.orderId=-1;lane.step=lane.batchRemaining>0?KitchenStep.ReadyStock:KitchenStep.Idle;
            order.paidCents=priceCents;order.riskSerial=0;order.shortSeasoning=shortSeasoning;order.servedFlavorId=flavorId;
            if(flavorMissed)
                Message=recipe.name+"已出餐：未加"+Name(order.requestedFlavorId)+"，按基础价 ¥"+(recipe.priceCents/100f).ToString("0.00")+"成交（顾客有点小失望）";
            else if(shortSeasoning)
                Message=recipe.name+(flavorId!=""?"加"+StallFlavorMenu.Find(flavorId).name:"")+"已成交"+(flavorId!=""?"（含加料 ¥"+(StallFlavorMenu.MarkupFor(flavorId)/100f).ToString("0.00")+"）":"")+"。缺油盐，本份诚信 -10（今日累计 "+IntegrityChange+"）";
            else
                Message=recipe.name+(flavorId!=""?"加"+StallFlavorMenu.Find(flavorId).name+"，收款":"已出餐，收款")+" ¥"+(priceCents/100f).ToString("0.00");Revision++;
            PublishSale(order.id,lane.recipeIndex,priceCents,shortSeasoning);return true;
        }
        // FLAVOR12 风味结算：锅上实际选过瓶就在递餐帧真实扣 1 份（选错兼容瓶也
        // 照样消耗，成本由玩家承担）；但只有“扣成功 ∧ 与顾客指定的 requestedFlavorId
        // 一致”才返回该风味进入 servedFlavorId/订单加价，否则本单按基础价成交、
        // 不把价差当获赠。lane.pendingFlavorId 无论成败都清空，一次性、不跨顾客复制。
        private string ResolveServeFlavor(KitchenLane lane,StallOrder order)
        {
            string chosen=lane.pendingFlavorId;lane.pendingFlavorId="";
            if(chosen=="")return "";
            if(Count(chosen)<=0)return "";
            Inventory[chosen]--;
            if(order.requestedFlavorId==""||chosen!=order.requestedFlavorId)return "";
            return chosen;
        }
        // S00 逐份油盐扣减（预付抵扣 → 库存 → 缺料事实），返回该份是否缺油盐。
        // ServeToOrder 正常/采纳两个分支共用，保证扣减口径完全一致、只此一次。
        // FLAVOR12：所有 cold 饮品/冰粉（酸梅汤、柠檬茶、玫瑰冰粉，recipe.cold）
        // 递餐一律不扣基础油盐——只耗果料与包材；热菜 0..4/6..9 逐字节不变。
        private bool DebitSeasoningForServe(KitchenLane lane)
        {
            if(Recipes[lane.recipeIndex].cold)return false;
            if(lane.prepaidSeasoning>0){lane.prepaidSeasoning--;return false;}
            if(Count(SeasoningShortageRules.BaseSeasoningId)>0){Inventory[SeasoningShortageRules.BaseSeasoningId]-=SeasoningShortageRules.PortionsPerOrder;return false;}
            UnseasonedPortionsServed+=SeasoningShortageRules.PortionsPerOrder;IntegrityChange+=SeasoningShortageRules.IntegrityDeltaPerShortPortion;return true;
        }
        // RISK7：Serve 前调用接线层注入的 ServeRiskGate。任何异常/坏决策都不允许
        // 破坏本次 Serve：返回 null = 走正常成交。
        private StallRiskGateDecision InvokeServeRiskGate(int orderId,int recipeIndex,int priceCents)
        {
            if(ServeRiskGate==null||RiskState==null||HasActiveBikeBlock)return null;
            StallRiskGateDecision gate;
            try{gate=ServeRiskGate(orderId,recipeIndex,priceCents);}
            catch(Exception error){Debug.LogError("ServeRiskGate threw; sale proceeds as normal: "+error);return null;}
            if(gate==null||!gate.adoptAsRisk)return null;
            // 采纳前合法性校验：已有激活事件/坏窗口/坏金额一律忽略并告警。
            // 窗口必须有限（NaN 与 Infinity 都拒绝，RISK7PAYFIX）；坏决策返回 null
            // 走既有正常成交路径，绝不产生半次交易。
            if(RiskState.HasActiveEvent){Debug.LogWarning("ServeRiskGate adopted while a risk event is active; ignored.");return null;}
            if(!Finite(gate.interceptSeconds)||gate.interceptSeconds<=0f){Debug.LogWarning("ServeRiskGate returned an invalid intercept window; ignored.");return null;}
            if(priceCents<0||gate.collectCents<priceCents||gate.collectCents>priceCents*2){Debug.LogWarning("ServeRiskGate returned an invalid collect amount; ignored.");return null;}
            return gate;
        }
        public bool HasPendingRiskOrder(int orderId)
            =>RiskState!=null&&RiskState.ActiveEvent!=null&&!RiskState.ActiveEvent.Resolved&&RiskState.ActiveEvent.OrderId==orderId;
        // RISK7 追回：必须命中当前 active serial 与原冻结 CollectCents；MarkResolved
        // 先判重（读档后不重复结算），随后 Cash 与 Revenue 同时加实际 collectCents
        // （暴躁双倍也现金守恒：CashConserved=initial+Revenue 恒成立），对该订单发布
        // SaleCompleted 恰好一次（含原 shortSeasoning 事实）；不再扣包材/食材/油盐。
        public bool TryCompleteRiskSale(int serial,int collectCents)
        {
            StallRiskEvent active=RiskState!=null?RiskState.ActiveEvent:null;
            if(active==null||active.Resolved||active.Serial!=serial)return false;
            if(collectCents!=active.CollectCents){Debug.LogWarning("TryCompleteRiskSale amount does not match the frozen collect amount; rejected.");return false;}
            StallOrder order=FindOrder(active.OrderId);
            if(order==null||!order.served||order.riskSerial!=serial||order.paidCents!=0)return false;
            if(!RiskState.MarkResolved(serial,true))return false;
            CashCents+=collectCents;RevenueCents+=collectCents;order.paidCents=collectCents;RiskEventsRecovered++;
            Message="追回逃单款 ¥"+(collectCents/100f).ToString("0.00")+"（"+recipeName(order.recipeIndex)+"）。";
            Revision++;PublishSale(order.id,order.recipeIndex,collectCents,order.shortSeasoning);return true;
        }
        // RISK7 漏拦/超时坏账：只标 resolved 并 LostRevenueCents+=原应收（只增事实
        // 计数），现金/Revenue 不动、不重扣库存、不重放收入。
        public bool TryWriteOffRiskSale(int serial)
        {
            StallRiskEvent active=RiskState!=null?RiskState.ActiveEvent:null;
            if(active==null||active.Resolved||active.Serial!=serial)return false;
            StallOrder order=FindOrder(active.OrderId);
            if(order==null||!order.served||order.riskSerial!=serial||order.paidCents!=0)return false;
            int face=active.PriceCents;
            if(!RiskState.MarkResolved(serial,false))return false;
            LostRevenueCents+=face;RiskEventsWrittenOff++;
            Message="逃单未追回，本单坏账 ¥"+(face/100f).ToString("0.00")+"（已出餐未收款，不计营收）。";
            Revision++;return true;
        }
        // RISK7 关摊统一入口（Controller 在日结发布前调用一次）：尚有未决债权则
        // 按坏账一次性核销。保存/暂停/销毁本身不记损失，此处只处理显式关摊。
        public bool ResolvePendingRiskOnClose()
        {
            StallRiskEvent active=RiskState!=null?RiskState.ActiveEvent:null;
            if(active==null||active.Resolved)return false;
            return TryWriteOffRiskSale(active.Serial);
        }
        private static string recipeName(int recipeIndex)
            =>recipeIndex>=0&&recipeIndex<Recipes.Length?Recipes[recipeIndex].name:"未知菜品";
        // 无订单售出/改绑的自动匹配：同菜、已到位、未完成、等待最久
        // （arrivedAt 早者优先，同刻按订单号）。只读，不改任何状态。
        public StallOrder FindWaitingOrder(int recipeIndex)
        {
            if(recipeIndex<0||recipeIndex>=Recipes.Length)return null;
            StallOrder best=null;
            foreach(StallOrder candidate in Orders)
                if(candidate.recipeIndex==recipeIndex&&candidate.arrived&&!candidate.served&&!candidate.departed)
                    if(best==null||candidate.arrivedAt<best.arrivedAt||candidate.arrivedAt==best.arrivedAt&&candidate.id<best.id)best=candidate;
            return best;
        }
        // The ledger is fully settled before the announcement: a subscriber
        // error is logged, never rethrown into the Serve caller as a
        // half-transaction, and can neither repeat nor undo the sale.
        private void PublishSale(int orderId,int recipeIndex,int paidCents,bool shortSeasoning)
        {
            if(SaleCompleted==null)return;
            foreach(Action<int,int,int,bool> callback in SaleCompleted.GetInvocationList())
                try{callback(orderId,recipeIndex,paidCents,shortSeasoning);}
                catch(Exception error){Debug.LogError("SaleCompleted subscriber failed (sale already settled): "+error);}
        }
        private void PublishBatchBurned(int laneIndex,int recipeIndex,int portions)
        {
            if(BatchBurned==null)return;
            foreach(Action<int,int,int> callback in BatchBurned.GetInvocationList())
                try{callback(laneIndex,recipeIndex,portions);}
                catch(Exception error){Debug.LogError("BatchBurned subscriber failed (waste already settled): "+error);}
        }
        // RISK7：与 SaleCompleted 同原则的回调隔离——采纳分支的账本（出餐全部
        // 扣减 + Adopt）先落定，再发布；订阅者异常只记录，不重复也不撤销采纳。
        private void PublishRiskSalePending(StallRiskEvent risk)
        {
            if(RiskSalePending==null)return;
            foreach(Action<StallRiskEvent> callback in RiskSalePending.GetInvocationList())
                try{callback(risk);}
                catch(Exception error){Debug.LogError("RiskSalePending subscriber failed (delivery already settled): "+error);}
        }
        private void Unassign(KitchenLane lane)
        {var order=FindOrder(lane.orderId);if(order!=null&&!order.departed&&!order.served)order.lane=-1;lane.orderId=-1;}
        public void Close()
        {
            if(Finished)return;Finished=true;UpdateServicePhase();
            bool hadBikeBlock=ClearBikeBlockStateOnly();
            foreach(var order in Orders)if(!order.served&&!order.departed){order.departed=true;order.finishedAt=Elapsed;order.departureReason="closing";ReleaseOrderReservation(order.id);Lost++;}
            Message="今日收摊，核对营收与剩余原料。";Revision++;
            if(hadBikeBlock)PublishBikeBlockCleared(true);
        }
        private static void AddCount(Dictionary<int,int> counts,int key,int n){counts[key]=(counts.TryGetValue(key,out int old)?old:0)+n;}
        private static int ReadCount(Dictionary<int,int> counts,int key)=>counts.TryGetValue(key,out int n)?n:0;
        private double RandomValue(){randomDraws++;return random.NextDouble();}
        public BusinessAuditState CaptureAudit()
        {
            var raw=new List<BusinessInventoryAudit>();var keys=new SortedSet<string>(Setup.inventory.Keys);keys.UnionWith(Inventory.Keys);
            bool nonnegative=true;int usedPackages=0;
            foreach(string id in keys)
            {
                int initial=Setup.inventory.TryGetValue(id,out int value)?value:0,remaining=Count(id);
                raw.Add(new BusinessInventoryAudit{id=id,initial=initial,remaining=remaining,used=initial-remaining});nonnegative&=remaining>=0;
                if(id.StartsWith("P",StringComparison.Ordinal))usedPackages+=initial-remaining;
            }
            var orders=new List<BusinessOrderAudit>();foreach(var order in Orders)orders.Add(new BusinessOrderAudit{id=order.id,lane=order.lane,recipe=Recipes[order.recipeIndex].id,
                status=order.served?"served":order.departed?"left":!order.arrived?"arriving":order.lane>=0?"working":order.reservedLane>=0?"reserved":"waiting",patience=order.patience,maxPatience=order.maxPatience,reservedLane=order.reservedLane,arrived=order.arrived,spawnedAt=order.spawnedAt,arrivedAt=order.arrivedAt,startedAt=order.startedAt,finishedAt=order.finishedAt,departureReason=order.departureReason,
                requestedFlavorId=order.requestedFlavorId,servedFlavorId=order.servedFlavorId,productId=order.productId,productName=order.productName});
            var lanes=new List<BusinessLaneAudit>();foreach(var lane in Lanes)lanes.Add(new BusinessLaneAudit{index=lane.index,orderId=lane.orderId,reservedOrderId=lane.reservedOrderId,batchRemaining=lane.batchRemaining,toolClicks=lane.toolClicks,
                recipe=Recipes[lane.recipeIndex].id,step=lane.step.ToString(),energy=lane.energy,remaining=lane.remaining,duration=lane.duration,seasoned=lane.seasoned,prepaidSeasoning=lane.prepaidSeasoning,eggPrepPortions=lane.eggPrepPortions});
            var selected=new List<string>();foreach(int recipe in SelectedRecipes)selected.Add(Recipes[recipe].id);
            var equipment=new List<string>(Equipment);equipment.Sort(StringComparer.Ordinal);
            var production=new List<BusinessRecipeAudit>();
            // MENUFLEX10：营业中撤菜后锅内仍可能有该菜的批次，换菜也会使锅位菜超出
            // 菜单；生产守恒覆盖 菜单∪锅位菜 的并集（去重排序，只读）。
            var productionRecipes=new SortedSet<int>(SelectedRecipes);foreach(var lane in Lanes)productionRecipes.Add(lane.recipeIndex);
            productionRecipes.UnionWith(portionsStarted.Keys);productionRecipes.UnionWith(freePortionsStarted.Keys);
            foreach(var order in Orders)productionRecipes.Add(order.recipeIndex);
            foreach(var meal in PackedMeals())productionRecipes.Add(meal.product.visualRecipeIndex);
            foreach(int recipe in productionRecipes)
            {
                int served=0,left=0;foreach(var order in Orders)if(order.recipeIndex==recipe&&order.served)served++;
                foreach(var lane in Lanes)if(lane.recipeIndex==recipe)left+=lane.batchRemaining;
                // FREEDOM19：柜台上已装台的每一份仍是真实在库供应，计入份数守恒。
                foreach(var meal in PackedMeals())if(meal.product!=null&&meal.product.visualRecipeIndex==recipe)left++;
                int made=ReadCount(portionsStarted,recipe)+ReadCount(freePortionsStarted,recipe),waste=ReadCount(portionsWasted,recipe)+ReadCount(freePortionsWasted,recipe);
                production.Add(new BusinessRecipeAudit{recipe=Recipes[recipe].id,batches=ReadCount(batchesStarted,recipe)+ReadCount(freeBatchesStarted,recipe),portionsStarted=made,served=served,wasted=waste,remaining=left,portionsConserved=made==served+waste+left});
            }
            int emergencyOnHand=0;foreach(var meal in PackedMeals())if(meal.packageId==EmergencyBagId)emergencyOnHand++;
            int emergencyUsed=EmergencyPackagesSold+emergencyOnHand;
            var tableAudit=new List<CounterMeal>();foreach(var meal in ReadyMeals)tableAudit.Add(meal.Copy());
            var batchesAudit=new List<FreeKitchenBatch>();foreach(var lane in Lanes)if(lane.freeBatch!=null)batchesAudit.Add(lane.freeBatch.Copy());
            return new BusinessAuditState{readyMeals=tableAudit.ToArray(),heldMeal=BusinessSaveData.CopyMeal(HeldMeal),freeBatches=batchesAudit.ToArray(),scenario=Setup.practice?"day12_practice":"procured_run",phase=Finished?"DaySummary":Started?"Business":"SiteSelection",district=District.name,
                careerId=Setup.profile?.careerId??"",selectedTraits=Setup.profile==null?Array.Empty<string>():(string[])Setup.profile.selectedTraits.Clone(),
                servicePhase=ServicePhase.ToString(),acceptingOrders=AcceptingOrders,lastOrderAt=LastOrderAt,clearDownSeconds=ClearDownSeconds,
                arrivalAttempts=ArrivalAttempts,queueTurnaways=QueueTurnaways,soldOutArrivals=SoldOutArrivals,unseasonedPortionsServed=UnseasonedPortionsServed,integrityChange=IntegrityChange,started=Started,finished=Finished,canReturnMarket=Setup.canReturnMarket&&!Started,seed=Setup.seed,randomDraws=randomDraws,orderSerial=orderSerial,initialCashCents=Mathf.Max(0,Setup.cashCents),cashCents=CashCents,
                revenueCents=RevenueCents,served=Served,lost=Lost,wastedPortions=WastedPortions,gas=Gas,cartridge=Cartridge,coal=Coal,electricity=Electricity,
                gasUsed=Mathf.Max(0,Setup.gasPoints)-Gas,cartridgeUsed=Mathf.Max(0,Setup.cartridgePoints)-Cartridge,coalUsed=Mathf.Max(0,Setup.coalPoints)-Coal,electricityUsed=Mathf.Max(0,Setup.electricityPoints)-Electricity,
                packageUnitsUsed=usedPackages,emergencyPackagesUsed=emergencyUsed,packagingScoreDelta=EmergencyPackagingScore,cashConserved=CashCents==Mathf.Max(0,Setup.cashCents)+RevenueCents,
                // FREEDOM19：包材去向 = 已成交 + 柜台上仍 packed 的餐（一份一扣，不重复）。
                packagesConserved=usedPackages+emergencyUsed==Served+ReadyMeals.Count+(HeldMeal!=null?1:0),nonNegativeInventory=nonnegative,
                riskEventsIssued=RiskEventsIssued,riskEventsRecovered=RiskEventsRecovered,riskEventsLost=RiskEventsWrittenOff,lostRevenueCents=LostRevenueCents,
                bikeBlockActive=HasActiveBikeBlock,bikeBlockTriggered=bikeBlockTriggered,bikeBlockRemaining=BikeBlockRemaining,
                bikeBlockPollRemaining=bikeBlockPollRemaining,customerArrivalRateMultiplier=CustomerArrivalRateMultiplier,
                nonNegativeEnergy=Gas>=0&&Cartridge>=0&&Coal>=0&&Electricity>=0,elapsed=Elapsed,remaining=Remaining,duration=Setup.dayDuration,nextCustomerSeconds=nextCustomer,
                customerIntervalSeconds=Setup.customerIntervalSeconds,patienceMinSeconds=Setup.patienceMinSeconds,patienceMaxSeconds=Setup.patienceMaxSeconds,
                recipeProduction=production.ToArray(),inventory=raw.ToArray(),orders=orders.ToArray(),lanes=lanes.ToArray(),selectedRecipes=selected.ToArray(),equipment=equipment.ToArray()};
        }
        private bool Reject(string message){Message=message;Revision++;return false;}
    }
}
