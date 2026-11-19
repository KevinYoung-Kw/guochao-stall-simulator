using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class CampaignStockRow
    {
        public string instanceId,id,name;
        public int quantity,acquiredDay;
    }
    [Serializable] public sealed class CampaignGearRow
    {
        public string instanceId,id,name,location;
        public float condition=100;
    }
    [Serializable] public sealed class CampaignSaleRow
    {
        public string id,name;
        public int quantity,unitPriceCents,totalCents;
    }
    [Serializable] public sealed class CampaignDayLedger
    {
        public int dayIndex,openingCashCents,purchasesCents,nightFuelCents,revenueCents,closingCashCents,served,lost,resaleIncomeCents,unseasonedPortionsServed,integrityChange;
        public long onlinePurchasesCents;
        // RISK7：A01 坏账与未决债权解释字段（旧档 JsonUtility 补 0；普通无风险日账
        // 保持 0，原有字段与行项目不变）。badDebtCents=已核销坏账面额（不冲减
        // revenueCents）；riskPendingCents=日结时仍未收回的债权面额（Controller 关摊
        // 核销后应为 0，>0 说明存在未处理债权）。
        public int badDebtCents,riskPendingCents;
        public string district,sessionId;
        public bool committed;
        public List<CampaignSaleRow> sales=new List<CampaignSaleRow>();
        public List<CampaignResaleRow> resales=new List<CampaignResaleRow>();
    }
    // Persistent ownership and day boundaries. Exploration and service snapshots
    // are the working state of that day; CommitDay is their only nightly merge.
    [Serializable] public sealed class CampaignState
    {
        public string campaignId=Guid.NewGuid().ToString("N");
        public int dayIndex=1,vehicleLevel=1,cashCents,debtCents,dayOpeningCashCents;
        public int seed=12017,gas,cartridge,coal,electricity;
        public int integrityChangeTotal;
        public float reputation;
        public RunProfile profile;
        public CampaignDayLedger lastDay;
        public List<CampaignDayLedger> history=new List<CampaignDayLedger>();
        public List<OnlineOrder> onlineOrders=new List<OnlineOrder>();
        public List<CampaignStockRow> stock=new List<CampaignStockRow>();
        public List<CampaignGearRow> equipment=new List<CampaignGearRow>();
        public string energyNote="";
        // NIGHT9 N01：夜间重器去向选择与当夜检定结果。nightLeaveVehicle=false 是
        // 现行安全默认（大件全部卸回屋，行为与旧版一致）；仅显式选择「大件留车」
        // 才对快照内实际在车的重器逐件做原表概率的 N01 检定。nightResult 随存档
        // 往返：resolved+dayIndex 双守卫使连续点击/预存失败/断点恢复不重复掷骰、
        // 不重复移走资产；旧档缺这些字段时取默认值＝安全卸回屋。securityHistory
        // 为已损财产记录（每条 losses 逐项留面额与当夜概率）。
        public bool nightLeaveVehicle;
        public NightSecurityRecord nightResult;
        public List<NightSecurityRecord> securityHistory=new List<NightSecurityRecord>();
        // NIGHT9R：下夜前的真实装载快照（重器 instanceId，收摊时实际在车上——含
        // 实际装载出摊的 service equipment，排除采购装不下带回家的）。CommitDay
        // 在夜间位置分配前重拍一次；显式「留车」只对快照内重器生效。旧档无此
        // 字段＝空列表＝没有留车候选，默认安全，行为与旧版一致。
        public List<string> nightVehicleGear=new List<string>();

        public static CampaignState New(RunProfile profile,RunState run)
        {
            if(profile==null||!profile.Validate(out string reason))throw new ArgumentException("Invalid campaign profile");
            var value=new CampaignState{profile=JsonUtility.FromJson<RunProfile>(JsonUtility.ToJson(profile)),
                cashCents=run.CashCents,dayOpeningCashCents=run.CashCents,debtCents=profile.InitialDebtCents,reputation=profile.InitialReputation,
                gas=run.GasPoints,cartridge=run.CartridgePoints,coal=run.CoalPoints,electricity=run.ElectricityPoints};
            value.RememberGear(run.HomeEquipment,"home");value.RememberGear(run.LoadedEquipment,"vehicle");
            value.SetStock(run.Supplies);return value;
        }
        private void RememberGear(IEnumerable<string> ids,string location)
        {
            foreach(string id in ids)
            {
                var row=equipment.Find(x=>x.id==id);
                if(row==null){row=new CampaignGearRow{instanceId=Guid.NewGuid().ToString("N"),id=id,name=CharacterCatalog.EquipmentName(id)};equipment.Add(row);}
                row.location=location;
            }
        }
        private void SetStock(IDictionary<string,int> values)
        {
            var previous=stock;stock=new List<CampaignStockRow>();
            foreach(string id in new SortedSet<string>(values.Keys))
            {
                if(values[id]<=0)continue;
                var old=previous.Find(x=>x.id==id);
                stock.Add(new CampaignStockRow{instanceId=old?.instanceId??Guid.NewGuid().ToString("N"),id=id,
                    name=GameCatalog.Find(id)?.name??CharacterCatalog.EquipmentName(id),quantity=values[id],acquiredDay=old?.acquiredDay??dayIndex});
            }
        }
        public bool CommitDay(RunState run,BusinessSession service,string sessionId)
        {
            if(service==null||!service.Finished)throw new InvalidOperationException("Only a closed service can settle a day.");
            if(lastDay!=null&&lastDay.committed&&lastDay.dayIndex==dayIndex)return false;
            if(history.Exists(x=>x.sessionId==sessionId&&x.committed))return false;
            run.PreservePaidCartAtSettlement();
            RememberGear(run.HomeEquipment,"home");RememberGear(run.LoadedEquipment,"vehicle");
            RememberGear(service.Equipment,"vehicle");
            var homeStock=new Dictionary<string,int>(service.Inventory);
            // Paid goods deliberately brought home after a failed load never
            // enter today's service setup, but remain owned for tomorrow.
            foreach(string id in run.ReturnedPaidCargo)
            {
                if(SupplyCatalog.IsEquipment(id)){if(id!="E02")RememberGear(new[]{id},"home");continue;}
                string key=SupplyCatalog.IngredientId(id);homeStock.TryGetValue(key,out int n);homeStock[key]=n+GameCatalog.Find(id).portions;
            }
            SetStock(homeStock);
            int tank=run.GasPoints-run.CartridgePoints;cartridge=run.CartridgePoints;coal=run.CoalPoints;electricity=run.ElectricityPoints;
            var purchasedEnergy=new List<string>(run.Cargo);purchasedEnergy.AddRange(run.ReturnedPaidCargo);
            foreach(string id in purchasedEnergy)
            {if(id=="E03")tank+=45;else if(id=="B02")cartridge+=15;else if(id=="E02")cartridge+=45;else if(id=="E01")coal+=30;else if(id=="E05")electricity+=45;}
            tank-=service.Setup.gasPoints-service.Gas;
            cartridge-=service.Setup.cartridgePoints-service.Cartridge;gas=Mathf.Max(0,tank+cartridge);
            coal-=service.Setup.coalPoints-service.Coal;electricity-=service.Setup.electricityPoints-service.Electricity;
            cashCents=service.CashCents;
            var ledger=new CampaignDayLedger{dayIndex=dayIndex,sessionId=sessionId,openingCashCents=dayOpeningCashCents,
                purchasesCents=run.SpentCents,resaleIncomeCents=run.ResaleIncomeCents,resales=new List<CampaignResaleRow>(run.Resales),revenueCents=service.RevenueCents,served=service.Served,lost=service.Lost,
                unseasonedPortionsServed=service.UnseasonedPortionsServed,integrityChange=service.IntegrityChange,
                district=service.District.name,committed=true};
            // RISK7：销售行按逐单“实际实收”拼账（含暴躁双倍追回），不再用
            // served × 固定菜价——未收回的 A01 债权/坏账一文不计入 totalCents。
            // 无风险日账逐单 paidCents==菜价，与旧口径完全一致。
            var saleGroups=new Dictionary<string,CampaignSaleRow>(StringComparer.Ordinal);
            foreach(StallOrder order in service.Orders)
            {
                if(!order.served)continue;
                string name=service.OrderDishName(order.id);
                if(string.IsNullOrEmpty(name))name="现做餐品";
                string group=name+"\u001f"+order.paidCents;
                if(!saleGroups.TryGetValue(group,out CampaignSaleRow row))
                {
                    string id="sale-"+order.id;
                    if(order.recipeIndex>=0&&order.recipeIndex<BusinessSession.Recipes.Length)
                    {
                        StallRecipe recipe=BusinessSession.Recipes[order.recipeIndex];
                        if(recipe.name==name)id=recipe.id;
                    }
                    row=new CampaignSaleRow{id=id,name=name,unitPriceCents=order.paidCents};
                    saleGroups.Add(group,row);ledger.sales.Add(row);
                }
                row.quantity++;row.totalCents+=order.paidCents;
            }
            ledger.badDebtCents=service.LostRevenueCents;
            ledger.riskPendingCents=service.PendingRiskCents;
            var notes=new List<string>();
            bool gasTank=equipment.Exists(x=>x.id=="E03"),battery=equipment.Exists(x=>x.id=="E05");
            if(gasTank&&tank<45)
            {
                if(cashCents>=2000){cashCents-=2000;ledger.nightFuelCents=2000;gas=45+cartridge;notes.Add("煤气罐补满 45 点 · ¥20");}
                else notes.Add("现金不足 ¥20，煤气保留余量，未扣款");
            }
            if(battery){electricity=Mathf.Max(electricity,45);notes.Add("蓄电瓶回家充电 · 免费补满 45 点");}
            energyNote=notes.Count==0?"能源按实际余量保留，煤块与卡式气未自动补给。":string.Join("\n",notes);
            // NIGHT9 N01：先拍收摊时的真实装载快照（此时 location 是真相），再按
            // 夜间选择分配位置——默认（或卸回屋）把大件全部卸回屋，与现行 P1 安全
            // 行为完全一致；仅显式「大件留车」时，快照内实际在车的重器留在车上并
            // 在次日 StartNextDay 做逐件确定性 N01 检定（见 NightSecurityRules）。
            NightSecurityRules.SnapshotVehicleGear(this);
            NightSecurityRules.AssignLocations(this);
            ledger.closingCashCents=cashCents;lastDay=ledger;history.Add(ledger);integrityChangeTotal+=service.IntegrityChange;
            return true;
        }
        public RunState CreateNextDay(float checkoutSeconds)
        {
            if(lastDay==null||!lastDay.committed||lastDay.dayIndex!=dayIndex)throw new InvalidOperationException("Settle this day before starting another.");
            dayIndex++;dayOpeningCashCents=cashCents;
            var next=RunState.FromCampaign(this,checkoutSeconds);
            // NIGHT9R：FromCampaign 忽略 location、把全部重器放进 HomeEquipment。此处
            // 把 N01 结算后仍实际在车（location=="vehicle"——留车且幸存）的重器移入
            // next.LoadedEquipment：HomeEquipment 列表保留（SyncEquipment/ActiveHome
            // Equipment 语义不变）、StoveLoaded 按真实未卸重器重算。卸回屋或旧档时
            // 无 vehicle 重器，本调用为空操作，次日行为与旧版一致。
            var vehicleGear=new List<string>();
            if(equipment!=null)foreach(var row in equipment)
                if(row!=null&&EquipmentVisualFactory.IsHeavy(row.id)&&row.location=="vehicle")vehicleGear.Add(row.id);
            next.KeepVehicleGear(vehicleGear);
            return next;
        }
    }

    [Serializable] public sealed class RunStockSave { public string id;public int quantity; }
    [Serializable] public sealed class RunSaveData
    {
        public int schemaVersion=1,cashCents,spentCents,resaleIncomeCents,disposedPurchaseCents,gas,cartridge,coal,electricity;
        public GamePhase phase;
        public float remaining,marketUsed,checkoutRemaining,checkoutDuration;
        public bool holdingStove,stoveLoaded,paid,loaded,timeoutSettlement,checkoutInProgress,confirmedCheckoutRequiresSelection;
        public string heldGrocery,heldEquipmentId,message;
        public RunProfile profile;
        public string[] homeEquipment,loadedEquipment,cart,cargo,returnedPaidCargo,usedBoughtIds;
        public UsedPurchasePrice[] usedPriceOverrides;
        public CampaignResaleRow[] resales;
        public RunStockSave[] supplies;
        // PLAN18: home meal plan. Old saves lack the field; Restore treats a
        // null array as an empty plan, so legacy files load unchanged.
        public MealPlanChoice[] mealPlan;
    }
    [Serializable] public sealed class WorldSaveData
    {
        public Vector3 playerPosition,cartPosition;
        public Vector3 cartAttachmentOffset,cartStandingPoint;
        public Quaternion playerRotation=Quaternion.identity,cartRotation=Quaternion.identity;
        public Quaternion cartFacing=Quaternion.identity;
        public bool hasPlayer,hasCart,cartAttached;
        public string crowdJson;
    }
    [Serializable] public sealed class CampaignSaveData
    {
        public int schemaVersion=1;
        public string savedUtc;
        public bool hasBusiness;
        public CampaignState campaign;
        public RunSaveData run;
        public BusinessSaveData business;
        public WorldSaveData world;
    }
}
