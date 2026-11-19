using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // NIGHT9R（根审查修订）：N01 废品飞贼撬大件 —— 夜间留车重器防盗检定。
    //
    // 原文依据（不得改写）：
    //  · 内容库 4.1 N01「废品飞贼撬大件」：将重型底座/气罐/蓄电瓶裸留在无卷帘门
    //    车上，次日按件损失；防范＝睡前搬回出租屋 / U型大铁锁 / 升 Lv.3 卷帘车。
    //  · 设定文档 S1.1：失窃按每一件核心资产单独检定；逐件基础失窃率见原具体表：
    //    B01 15% · B02 25% · B03 38% · B04 45% · B05 35% · B06 38% · B07 35% · B08 45%
    //    · E03 35% · E04 40% · E05 42% · E06 45%。
    //  · phase-plan §4 车阶规则（已存在的车阶防盗规则）：Lv1 裸车无减免；
    //    Lv2 防盗减免 25%；Lv3 卷帘免卸货、防盗减免 100%；Lv4 同卷帘规格。
    //
    // 本批范围（最小切片，修复两处实质偏离）：
    //  · 偏离一（逐件规则被每晚统一随机替代）：留车不再「每晚统一抽一次、只偷最
    //    贵单件」。仅收摊前实际在车上的重器（含当日实际装载出摊的 service
    //    equipment；采购装不下带回家的不算）进入候选；每一件按上表各自独立检定
    //    （同夜可能多件失窃），车阶折减/卷帘免疫保持不变；表外重器（E08/V15/V08）
    //    退回 S1.1 的 35%~45% 残值区间。检定序列＝同日固定种子＋台账稳定顺序，
    //    保存/读档不重掷、不重复移走资产。
    //  · 偏离二（RunState.FromCampaign 忽略 location）：付风险留车成功后次日却要把
    //    重器重新搬一遍。现由 CreateNextDay 调用 RunState.KeepVehicleGear（本文件
    //    内的 partial 方法）：幸存留车重器进入 next.LoadedEquipment，HomeEquipment
    //    列表原样保留（WorldRuntime.SyncEquipment 遍历它，ActiveHomeEquipment 自动
    //    排除已装），StoveLoaded 按真实未卸重器重算——次日看得见、无需重搬；旧档
    //    无 location/快照字段时行为与旧版完全一致。
    //  · 被偷大件真实移出设备台账并随罐带走对应能源（E03 煤气罐 → 罐内余气清零；
    //    E05 蓄电瓶 → 电量清零），日结财产记录（securityHistory/losses）逐项留面额；
    //    次日提示点名实际丢失的商品与名称。
    //  · U 型大铁锁 / 车辆改造属车行服务，现行目录没有该资产，本批不发明拥有权；
    //    锁具/N02/赎回（X04）另期。
    [Serializable] public sealed class NightSecurityLoss
    {
        public string id="",name="",instanceId="";
        public int chanceBps;                               // 该件当夜最终检定概率（万分位）
        public int lossCents;                               // 按目录新价记录的已损财产面额
    }
    [Serializable] public sealed class NightSecurityRecord
    {
        public int dayIndex;                                // 所属夜晚（ campaign.dayIndex ）
        public string choice="unload_home";                 // unload_home | leave_vehicle
        public bool resolved;                               // 该夜是否已结算（防重复掷/重复扣）
        public bool stolen;
        public string stolenId="",stolenName="",stolenInstanceId="";
        public int chanceBps;                               // 首失件当夜检定概率（万分位，审计用）
        public int lossCents;                               // 当夜已损财产面额合计
        public string notice="";                            // 次日早晨提示
        public List<NightSecurityLoss> losses=new List<NightSecurityLoss>();
    }

    public static class NightSecurityRules
    {
        public const string EventN01="N01";
        public const string EventN01Name="废品飞贼撬大件";
        // S1.1 原具体表：逐件核心资产基础失窃率（万分位）。只覆盖已有这类核心资产。
        static readonly Dictionary<string,int> ItemChanceBps=new Dictionary<string,int>
        {
            {"B01",1500},{"B02",2500},{"B03",3800},{"B04",4500},
            {"B05",3500},{"B06",3800},{"B07",3500},{"B08",4500},
            {"E03",3500},{"E04",4000},{"E05",4200},{"E06",4500},
        };
        // 表外重器退回 S1.1 基础区间 35%~45%（无卷帘裸车）。
        public const float BaseTheftChanceMin=.35f,BaseTheftChanceMax=.45f;
        // 已存在的车阶防盗规则（phase-plan §4）：Lv2 减免 25%，Lv3/Lv4 卷帘 100%。
        public static float VehicleTheftMultiplier(int vehicleLevel)
        {
            if(vehicleLevel>=3)return 0f;
            if(vehicleLevel==2)return .75f;
            return 1f;
        }
        public static bool IsRollerVehicle(int vehicleLevel)=>vehicleLevel>=3;
        public static int RollSeed(CampaignState campaign)
        {
            if(campaign==null)throw new ArgumentNullException(nameof(campaign));
            return campaign.seed+campaign.dayIndex*7919;
        }

        // 夜间页面两个清楚选项：默认卸回屋（安全）；显式留车才承担 N01 风险。
        public static void ChooseUnloadHome(CampaignState campaign)
        {
            if(campaign==null)return;
            campaign.nightLeaveVehicle=false;AssignLocations(campaign);
        }
        public static void ChooseLeaveOnVehicle(CampaignState campaign)
        {
            if(campaign==null)return;
            campaign.nightLeaveVehicle=true;AssignLocations(campaign);
        }
        // 收摊日结末段的位置分配：CommitDay 已把 run.LoadedEquipment 与实际出摊的
        // service.Equipment 标为「车上」、带回家的标为「家中」。此处只对重器生效：
        // 卸回屋＝全部回屋（现行安全行为）；留车＝仅收摊前真实在车（nightVehicleGear
        // 快照）的重器留车，其余仍在家中——不再出现撒手后仍写「带回家」的假标签，
        // 也不把实际在家的大件凭空挪上车。
        public static void AssignLocations(CampaignState campaign)
        {
            if(campaign==null||campaign.equipment==null)return;
            bool leave=campaign.nightLeaveVehicle;
            var snap=campaign.nightVehicleGear;
            foreach(var item in campaign.equipment)
            {
                if(!EquipmentVisualFactory.IsHeavy(item.id)){item.location="vehicle";continue;}
                bool onVehicle=leave&&snap!=null&&snap.Contains(item.instanceId??"");
                item.location=onVehicle?"vehicle":"home";
            }
        }
        // 下夜前的真实装载快照（CommitDay 在位置分配前调用一次）：此时台账 location
        // 是收摊时的真实位置——车上＝实际装载（含出摊设备），家中＝卸下的与采购
        // 装不下带回家的。旧档无该字段时为空＝没有留车候选，默认安全。
        public static void SnapshotVehicleGear(CampaignState campaign)
        {
            if(campaign==null||campaign.equipment==null)return;
            if(campaign.nightVehicleGear==null)campaign.nightVehicleGear=new List<string>();
            campaign.nightVehicleGear.Clear();
            foreach(var row in campaign.equipment)
                if(row!=null&&EquipmentVisualFactory.IsHeavy(row.id)&&row.location=="vehicle")
                    campaign.nightVehicleGear.Add(row.instanceId??"");
        }

        // 生产入口：确定性检定（StartNextDay 成功路径调用一次）。同一日固定种子，
        // 候选按台账稳定顺序逐件取独立随机数——保存/读档后重进得到完全相同序列。
        public static NightSecurityRecord Resolve(CampaignState campaign)
        {
            var random=new System.Random(RollSeed(campaign));
            return ResolveCore(campaign,0.0,()=>random.NextDouble(),random);
        }
        // 确定性核心（可复测）：同一 chanceDraw/hitDraw 应用于每一件候选——(0,0) 使
        // 全部候选必被偷，旧测试语义不变。
        public static NightSecurityRecord Resolve(CampaignState campaign,double chanceDraw,double hitDraw)
        {
            return ResolveCore(campaign,chanceDraw,()=>hitDraw,null);
        }
        // 逐件独立抽（可复测）：hitDraws 按候选台账顺序取值，越界视为未抽中；
        // 用于验证同夜分包——一件被偷、另一件幸存。
        public static NightSecurityRecord Resolve(CampaignState campaign,double chanceDraw,IReadOnlyList<double> hitDraws)
        {
            int at=0;
            return ResolveCore(campaign,chanceDraw,()=>hitDraws!=null&&at<hitDraws.Count?hitDraws[at++]:1.0,null);
        }
        // 同一 dayIndex 已 resolved 时直接返回原记录——连续点击、预存失败、断点恢复
        // 都不会重复掷骰或重复移走资产。
        static NightSecurityRecord ResolveCore(CampaignState campaign,double chanceDraw,Func<double> nextHitDraw,System.Random fallbackRandom)
        {
            if(campaign==null)throw new ArgumentNullException(nameof(campaign));
            if(campaign.nightResult!=null&&campaign.nightResult.resolved&&campaign.nightResult.dayIndex==campaign.dayIndex)
                return campaign.nightResult;
            if(campaign.securityHistory==null)campaign.securityHistory=new List<NightSecurityRecord>();
            var record=new NightSecurityRecord{dayIndex=campaign.dayIndex,
                choice=campaign.nightLeaveVehicle?"leave_vehicle":"unload_home"};
            campaign.nightResult=record;
            if(!campaign.nightLeaveVehicle)
            {
                // 安全默认：大件已卸回屋，不掷骰、不失窃、不留风险记录。
                record.resolved=true;return record;
            }
            var candidates=new List<CampaignGearRow>();
            if(campaign.equipment!=null)foreach(var row in campaign.equipment)
                if(row!=null&&!string.IsNullOrEmpty(row.id)&&EquipmentVisualFactory.IsHeavy(row.id)&&row.location=="vehicle")
                    candidates.Add(row);
            if(candidates.Count==0)
            {
                record.resolved=true;record.notice="昨晚没有大件留在车上，一夜平安。";
                campaign.securityHistory.Add(record);return record;
            }
            if(IsRollerVehicle(campaign.vehicleLevel))
            {
                record.resolved=true;record.chanceBps=0;
                record.notice="卷帘车落锁，大件留在车里过夜也平安无事。";
                campaign.securityHistory.Add(record);return record;
            }
            float multiplier=VehicleTheftMultiplier(campaign.vehicleLevel);
            foreach(CampaignGearRow row in candidates)
            {
                float baseChance;
                if(ItemChanceBps.TryGetValue(row.id,out int tableBps))baseChance=tableBps/10000f;
                else
                {
                    float band=fallbackRandom!=null?(float)fallbackRandom.NextDouble():Mathf.Clamp01((float)chanceDraw);
                    baseChance=Mathf.Lerp(BaseTheftChanceMin,BaseTheftChanceMax,band);
                }
                float chance=baseChance*multiplier;
                int chanceBps=(int)Math.Round(chance*10000d,MidpointRounding.AwayFromZero);
                double hitDraw=nextHitDraw!=null?nextHitDraw():1.0;
                if(hitDraw<0||hitDraw>=chance)continue;          // 该件独立检定：未抽中
                // 真实移出：台账行删除；随罐能源一并损失（E03 罐气清零 / E05 电量清零）。
                var loss=new NightSecurityLoss{id=row.id,instanceId=row.instanceId??"",chanceBps=chanceBps,
                    lossCents=CatalogValue(row.id),name=!string.IsNullOrEmpty(row.name)?row.name:CharacterCatalog.EquipmentName(row.id)};
                campaign.equipment.Remove(row);
                if(row.id=="E03")campaign.gas=campaign.cartridge;
                else if(row.id=="E05")campaign.electricity=0;
                if(record.losses.Count==0)
                {record.stolen=true;record.stolenId=loss.id;record.stolenName=loss.name;record.stolenInstanceId=loss.instanceId;record.chanceBps=chanceBps;}
                record.losses.Add(loss);record.lossCents+=loss.lossCents;
            }
            record.stolen=record.losses.Count>0;
            if(record.stolen)
            {
                var names=new List<string>();foreach(var loss in record.losses)names.Add("【"+loss.name+"】");
                record.notice="天亮了：昨晚废品飞贼撬走了"+string.Join("",names)+"，共损失 "+CampaignUI.Money(Mathf.Max(0,record.lossCents))
                    +"。被撬走的大件连同罐内能源一起消失，出摊前去市场重新置办。";
            }
            else record.notice="昨晚把大件留在车上过夜，巷口那伙人没敢下手，家当还在车上，省了早晨搬运的力气。";
            record.resolved=true;
            campaign.securityHistory.Add(record);
            return record;
        }
        static int CatalogValue(string id)=>SupplyCatalog.Find(id)?.priceCents??0;
    }

    // NIGHT9R：RunState 为 partial —— 留车幸存重器的次日装载恢复只在这里扩展，
    // 不改 RunState.cs 原文件。FromCampaign 把全部重器放进 HomeEquipment 并忽略
    // location；CreateNextDay 在 N01 结算后调用 KeepVehicleGear：
    //  · 幸存留车重器（台账 location=="vehicle"）加入 next.LoadedEquipment；
    //  · HomeEquipment 列表原样保留（WorldRuntime.SyncEquipment 遍历它渲染屋内家
    //    当，ActiveHomeEquipment=HomeEquipment−LoadedEquipment 自动排除已装件）；
    //  · StoveLoaded 按真实未卸重器重算——次日车上看得见、无需重搬；被偷件已不在
    //    台账，自然不会被保留；卸回屋选择下 location 全 home，本方法为空操作，
    //    旧档（无 location 概念）行为与旧版完全一致。
    public sealed partial class RunState
    {
        internal void KeepVehicleGear(IEnumerable<string> ids)
        {
            if(ids==null)return;
            foreach(string id in ids)
            {
                if(string.IsNullOrEmpty(id)||LoadedEquipment.Contains(id))continue;
                if(HomeEquipment.Contains(id))LoadedEquipment.Add(id);
            }
            StoveLoaded=!HasUnloadedEquipment;
            if(StoveLoaded)Message="昨晚留车的重器还在车上，不用再搬，直接出门采购。";
        }
    }
}
