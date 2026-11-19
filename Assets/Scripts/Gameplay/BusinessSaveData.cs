using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class BusinessStockSaveData { public string id;public int count; }
    [Serializable] public sealed class BusinessProductionSaveData { public int recipeIndex,batches,portions,wasted; }
    [Serializable] public sealed class BusinessGuestPlacementSaveData { public int orderId,slot; }
    [Serializable] public sealed class BusinessPresentationSaveData
    {
        public bool hasVendorPose;
        public Vector3 vendorPosition;
        public Quaternion vendorRotation=Quaternion.identity;
        public BusinessGuestPlacementSaveData[] arrivedGuests=Array.Empty<BusinessGuestPlacementSaveData>();
    }
    // JsonUtility does not serialize Dictionary or HashSet. These explicit arrays
    // are the on-disk contract, rather than an audit DTO or a live model reference.
    [Serializable] public sealed class BusinessSetupSaveData
    {
        public int cashCents,seed,coalPoints,gasPoints,cartridgePoints,electricityPoints,fuelPoints,queueCapacity,patienceMinSeconds,patienceMaxSeconds;
        public bool stoveLoaded,practice,canReturnMarket,hasProfile;
        public float dayDuration,customerIntervalSeconds,clearDownSeconds,lastCallNoticeSeconds;
        public RunProfile profile;
        public BusinessStockSaveData[] inventory=Array.Empty<BusinessStockSaveData>();
        public string[] equipment=Array.Empty<string>();
        public static BusinessSetupSaveData Capture(BusinessSetup s)=>new BusinessSetupSaveData
        {
            cashCents=s.cashCents,seed=s.seed,coalPoints=s.coalPoints,gasPoints=s.gasPoints,cartridgePoints=s.cartridgePoints,electricityPoints=s.electricityPoints,fuelPoints=s.fuelPoints,
            queueCapacity=s.queueCapacity,patienceMinSeconds=s.patienceMinSeconds,patienceMaxSeconds=s.patienceMaxSeconds,
            stoveLoaded=s.stoveLoaded,practice=s.practice,canReturnMarket=s.canReturnMarket,dayDuration=s.dayDuration,customerIntervalSeconds=s.customerIntervalSeconds,
            clearDownSeconds=s.clearDownSeconds,lastCallNoticeSeconds=s.lastCallNoticeSeconds,hasProfile=s.profile!=null,profile=CopyProfile(s.profile),
            inventory=CaptureStock(s.inventory),equipment=Sorted(s.equipment)
        };
        public BusinessSetup Restore()=>new BusinessSetup
        {
            cashCents=cashCents,seed=seed,coalPoints=coalPoints,gasPoints=gasPoints,cartridgePoints=cartridgePoints,electricityPoints=electricityPoints,fuelPoints=fuelPoints,
            queueCapacity=queueCapacity,patienceMinSeconds=patienceMinSeconds,patienceMaxSeconds=patienceMaxSeconds,
            stoveLoaded=stoveLoaded,practice=practice,canReturnMarket=canReturnMarket,dayDuration=dayDuration,customerIntervalSeconds=customerIntervalSeconds,
            clearDownSeconds=clearDownSeconds,lastCallNoticeSeconds=lastCallNoticeSeconds,profile=hasProfile?CopyProfile(profile):null,
            inventory=RestoreStock(inventory),equipment=new HashSet<string>(equipment??Array.Empty<string>(),StringComparer.Ordinal)
        };
        internal static RunProfile CopyProfile(RunProfile p)=>p==null?null:new RunProfile{careerId=p.careerId,selectedTraits=p.selectedTraits==null?Array.Empty<string>():(string[])p.selectedTraits.Clone(),debtDifficulty=p.debtDifficulty};
        internal static string[] Sorted(IEnumerable<string> ids){var result=new List<string>(ids);result.Sort(StringComparer.Ordinal);return result.ToArray();}
        internal static BusinessStockSaveData[] CaptureStock(IDictionary<string,int> stock)
        {var result=new List<BusinessStockSaveData>();foreach(string id in Sorted(stock.Keys))result.Add(new BusinessStockSaveData{id=id,count=stock[id]});return result.ToArray();}
        internal static Dictionary<string,int> RestoreStock(BusinessStockSaveData[] values)
        {
            if(values==null)throw new ArgumentException("Missing business stock array");var result=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var value in values)
            {if(value==null||string.IsNullOrEmpty(value.id)||value.count<0||result.ContainsKey(value.id))throw new ArgumentException("Invalid or duplicate saved stock entry");result.Add(value.id,value.count);}
            return result;
        }
    }
    [Serializable] public sealed class BusinessSaveData
    {
        public const int CurrentVersion=2;
        // seasoningChargeVersion 2 = 旧规则：BeginOrder 一次扣整批 seasoning；
        // 3 = 本版：Serve 每份扣 1 份，锅内批次记 prepaidSeasoning 预付抵扣。
        // 缺省 0 与 2 同义（旧档写盘时该字段不存在）。除 2/3 外一律拒绝。
        public const int SeasoningChargeVersion2=2;
        public const int SeasoningChargeVersion3=3;
        // RISK7 支付事实版本：0/缺省 = 旧档（Serve 即收款，无风险字段），Restore 时
        // 迁移补齐逐单 paidCents；3 = 本版（逐单实际实收 + 风险债权）。除 0/3 外拒绝。
        public const int PaymentVersion3=3;
        // MENUFLEX10 菜单/锅位解耦版本：0/缺省 = 旧档静态（lanes 与菜单一一对应）；
        // 1 = 本版（lanes=物理锅位=MaxLanes，可换菜、菜单≥3）。除 0/1 外一律拒绝。
        public const int MenuFlexVersion1=1;
        // PREP12：0/缺省 = 旧档（无敲蛋中间态，KitchenLane.eggPrepPortions 补 0）；
        // 1 = 本版（蛋炒饭两拍：敲蛋下锅→待下米饭→开炒）。除 0/1 外一律拒绝。
        public const int EggPrepVersion1=1;
        // FREEDOM19：0/缺省=旧档（无自由批次字段，KitchenLane.freeBatch 补 null）；
        // 1=本版（自由批次深拷贝随锅位保存，自由生产守恒账按视觉菜谱索引另存）。
        // 除 0/1 外一律拒绝。
        public const int FreeKitchenVersion1=1;
        public const string RandomAlgorithm="System.Random.NextDouble.UnityMono.v1";
        public int schemaVersion=CurrentVersion;
        public string sessionId,randomAlgorithm=RandomAlgorithm,message;
        public BusinessSetupSaveData setup;
        public BusinessStockSaveData[] inventory=Array.Empty<BusinessStockSaveData>();
        public string[] equipment=Array.Empty<string>();
        public StallOrder[] orders=Array.Empty<StallOrder>();
        public KitchenLane[] lanes=Array.Empty<KitchenLane>();
        public int[] selectedRecipes=Array.Empty<int>();
        public BusinessProductionSaveData[] production=Array.Empty<BusinessProductionSaveData>();
        public int cashCents,revenueCents,served,lost,wastedPortions,queueTurnaways,arrivalAttempts,soldOutArrivals,coal,gas,cartridge,electricity,districtIndex,orderSerial,randomDraws,revision;
        // S00 软缺件事实；旧档（无此字段）由 JsonUtility 补 0。
        public int unseasonedPortionsServed,integrityChange;
        // 0/缺省=按 v2 计费迁移；3=新计费。见 SeasoningChargeVersion* 常量。
        // Missing JSON fields must remain 0 on Unity versions that run field
        // initializers. CaptureSave writes 3 explicitly for every new save.
        public int seasoningChargeVersion;
        // RISK7：A01 风险收款。risk=null 为旧档（Restore 迁移为空状态）；pending 未决
        // 债权只存于 risk 内，现金侧不做任何读档补扣/补发。旧 JSON 缺字段补 0/null。
        public StallRiskSaveData risk;
        public int paymentVersion;
        // MENUFLEX10：0/缺省=旧档静态锅位；1=菜单/锅位解耦。旧 JSON 缺字段补 0。
        public int menuFlexVersion;
        // PREP12：0/缺省=旧档无敲蛋中间态；1=蛋炒饭两拍下锅。旧 JSON 缺字段补 0。
        public int eggPrepVersion;
        // FREEDOM19：自由厨房版本与自由生产守恒账（recipeIndex=有界视觉适配索引）。
        // 旧 JSON 缺字段补 0/null，语义与旧档完全一致。
        public int freeKitchenVersion;
        public BusinessProductionSaveData[] freeProduction=Array.Empty<BusinessProductionSaveData>();
        // FREEDOM19 柜台餐队列（一份一装台的深拷贝）。旧 JSON 缺字段补 null/空数组
        // = 空队列，不补钱补货。
        public CounterMeal[] readyMeals=Array.Empty<CounterMeal>();
        // FULFILMENT20: one independently held, already paid-for package.
        // Presence distinguishes no meal from Unity's inline empty-object JSON.
        public CounterMeal heldMeal;
        public bool heldMealPresent;
        // RISK7 坏账面额累计（只增事实计数）与风险事件计数（issued=recovered+lost+pending）。
        public int lostRevenueCents,riskEventsIssued,riskEventsRecovered,riskEventsLost;
        public bool started,finished;
        public BusinessServicePhase servicePhase;
        public float elapsed,nextCustomer;
        // Controller checkpoints are normalized to no pending visual work. A
        // published settlement is displayed on restore, never published again.
        public bool settlementPublished;
        public int settlementPublications,selectedOrderId=-1,selectedLaneIndex=-1;
        public float simulationAccumulator;
        public BusinessPresentationSaveData presentation;
        internal static StallOrder CopyOrder(StallOrder s)=>new StallOrder{id=s.id,recipeIndex=s.recipeIndex,lane=s.lane,reservedLane=s.reservedLane,
            spawnedAt=s.spawnedAt,arrivedAt=s.arrivedAt,startedAt=s.startedAt,finishedAt=s.finishedAt,departureReason=s.departureReason,
            patience=s.patience,maxPatience=s.maxPatience,arrived=s.arrived,departed=s.departed,served=s.served,customer=s.customer,
            paidCents=s.paidCents,riskSerial=s.riskSerial,shortSeasoning=s.shortSeasoning,
            requestedFlavorId=s.requestedFlavorId??"",servedFlavorId=s.servedFlavorId??"",
            productId=s.productId??"",productName=s.productName??"",productMethod=s.productMethod??"",productFamily=s.productFamily??"",productPriceCents=s.productPriceCents};
        internal static KitchenLane CopyLane(KitchenLane s)=>new KitchenLane{index=s.index,recipeIndex=s.recipeIndex,orderId=s.orderId,reservedOrderId=s.reservedOrderId,
            batchRemaining=s.batchRemaining,toolClicks=s.toolClicks,step=s.step,remaining=s.remaining,duration=s.duration,seasoned=s.seasoned,packageId=s.packageId,energy=s.energy,prepaidSeasoning=s.prepaidSeasoning,
            pendingFlavorId=s.pendingFlavorId??"",eggPrepPortions=s.eggPrepPortions,
            freeBatch=s.freeBatch==null?null:s.freeBatch.Copy(),freeBatchPresent=s.freeBatch!=null};
        internal static CounterMeal CopyMeal(CounterMeal m)=>m==null?null:m.Copy();
    }
}
