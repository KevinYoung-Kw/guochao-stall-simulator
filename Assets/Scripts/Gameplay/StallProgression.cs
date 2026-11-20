using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao
{
    [Serializable]
    public sealed class StallTalentNodeState
    {
        public string id;
        public int rank;
    }

    public sealed class StallTalentDefinition
    {
        public readonly string id, name, branch;
        public readonly int tier, costPerRank, maxRank;
        public StallTalentDefinition(string id, string name, string branch, int tier, int costPerRank, int maxRank)
        { this.id=id;this.name=name;this.branch=branch;this.tier=tier;this.costPerRank=costPerRank;this.maxRank=maxRank; }
    }
    // Formal campaign owner progression (separate from starting career/traits).
    // Stored as exact integer half-XP (1 XP = 2 half-XP) so 2.5 / 0.5 / 84.5 XP
    // never suffer floating-point drift across saves or day settlements.
    // Field initializers are deliberately omitted so pre-progression legacy JSON
    // deserializes as all-zero (version == 0) and is unambiguously distinguished
    // from tampered v1 data during CampaignSaveStore.Decode.
    [Serializable]
    public sealed class StallProgressionState
    {
        public int version;
        public int level;
        public int xpHalves;
        public int availablePoints;
        public List<StallTalentNodeState> nodes;

        public static StallProgressionState CreateDefault() => new StallProgressionState
        {
            version = StallProgressionRules.CurrentVersion,
            level = StallProgressionRules.MinLevel,
            xpHalves = 0,
            availablePoints = 0,
            nodes = new List<StallTalentNodeState>()
        };

        public StallProgressionState Copy()
        {
            var result=new StallProgressionState{version=version,level=level,xpHalves=xpHalves,
                availablePoints=availablePoints,nodes=new List<StallTalentNodeState>()};
            if(nodes!=null)foreach(var node in nodes)
                result.nodes.Add(node==null?null:new StallTalentNodeState{id=node.id,rank=node.rank});
            return result;
        }
    }

    public struct StallProgressionDaySummary
    {
        public int paidServedOrders;
        public int flavorMatchedOrders;
        public int netCashChangeCents;
        public int orderXpHalves;
        public int flavorBonusXpHalves;
        public int profitXpHalves;
        public int TotalXpHalves => orderXpHalves + flavorBonusXpHalves + profitXpHalves;
    }

    // Vertical slice rules for owner XP, level-up (Lv1..40, 60 + 25*n XP/level,
    // 1 talent point/level), and Tier-1 Branch-B talent "和气生财" (2 ranks,
    // 1 point/rank, customer patience +8%/rank for formal campaign runs only).
    public static class StallProgressionRules
    {
        public const int CurrentVersion = 1;
        public const int MinLevel = 1;
        public const int MaxLevel = 40;

        // Exact half-XP constants: 2.5 XP = 5 halves, 0.5 XP = 1 half, 1 XP = 2 halves.
        public const int OrderBaseXpHalves = 5;
        public const int FlavorBonusXpHalves = 1;
        public const int ProfitStepCents = 5000;
        public const int ProfitStepXpHalves = 2;

        // Tier-1 Branch-B talent: 和气生财 (Amiable Business).
        public const string AmiableTalentId = "B1_amiable";
        public const string AmiableTalentName = "和气生财";
        public const int AmiableMaxRank = 2;
        public const int AmiablePointCost = 1;
        public const int AmiablePatiencePercentPerRank = 8;
        // Only nodes with actual effects belong in this catalog. Further nodes
        // can be added with their own effects without changing the save shape.
        private static readonly StallTalentDefinition[] nodeCatalog = {
            new StallTalentDefinition(AmiableTalentId,AmiableTalentName,"B",1,AmiablePointCost,AmiableMaxRank)
        };
        public static IReadOnlyList<StallTalentDefinition> Nodes => Array.AsReadOnly(nodeCatalog);

        public static StallTalentDefinition FindNode(string id)
        {foreach(var node in nodeCatalog)if(string.Equals(node.id,id,StringComparison.Ordinal))return node;return null;}
        public static int NodeRank(StallProgressionState state,string id)
        {
            if(state?.nodes==null||string.IsNullOrEmpty(id))return 0;
            foreach(var node in state.nodes)if(node!=null&&string.Equals(node.id,id,StringComparison.Ordinal))return node.rank;
            return 0;
        }
        public static int RequiredBranchPoints(int tier)
        {switch(tier){case 1:return 0;case 2:return 4;case 3:return 10;case 4:return 18;default:return int.MaxValue;}}
        public static int BranchPoints(StallProgressionState state,string branch)
        {
            int total=0;if(state?.nodes==null)return total;
            foreach(var node in state.nodes){var def=node==null?null:FindNode(node.id);if(def!=null&&def.branch==branch)total+=node.rank*def.costPerRank;}
            return total;
        }

        public static int XpToNextLevel(int level)
        {
            if (level < MinLevel || level >= MaxLevel) return 0;
            return 60 + 25 * level;
        }

        public static int XpHalvesToNextLevel(int level) => XpToNextLevel(level) * 2;

        public static string FormatXp(int xpHalves)
        {
            long abs = Math.Abs((long)xpHalves);
            string sign = xpHalves < 0 ? "-" : "";
            return (abs & 1L) == 0 ? sign + (abs / 2) : sign + (abs / 2) + ".5";
        }

        public static int PatienceBonusPercent(int amiableRank)
            => Mathf.Clamp(amiableRank, 0, AmiableMaxRank) * AmiablePatiencePercentPerRank;

        // Integer half-away-from-zero scaling: e.g. 35..54 -> 38..58 (+8%), 41..63 (+16%).
        public static int ScalePatienceSeconds(int baseSeconds, int amiableRank)
        {
            int rank = Mathf.Clamp(amiableRank, 0, AmiableMaxRank);
            if (baseSeconds <= 0 || rank == 0) return baseSeconds;
            int percent = 100 + rank * AmiablePatiencePercentPerRank;
            return (int)((baseSeconds * (long)percent + 50L) / 100L);
        }

        public static bool IsLegacyEmpty(StallProgressionState state)
            => state == null || (state.version == 0 && state.level == 0 && state.xpHalves == 0 &&
                state.availablePoints == 0 && (state.nodes == null || state.nodes.Count == 0));

        public static StallProgressionState EnsureInitialized(CampaignState campaign)
        {
            if (campaign == null) return null;
            if (IsLegacyEmpty(campaign.progression))
                campaign.progression = StallProgressionState.CreateDefault();
            if(campaign.progression.nodes==null)campaign.progression.nodes=new List<StallTalentNodeState>();
            return campaign.progression;
        }

        public static int AmiableRank(CampaignState campaign)
        {
            if (campaign == null || IsLegacyEmpty(campaign.progression)) return 0;
            return Mathf.Clamp(NodeRank(campaign.progression,AmiableTalentId),0,AmiableMaxRank);
        }

        // Pure read-only evaluation of real settled orders and actual ledger cash delta.
        // Only paid served orders (order.served && order.paidCents > 0) grant base +2.5 XP.
        // Flavor bonus (+0.5 XP) requires a non-empty requestedFlavorId matching servedFlavorId.
        // The cash delta is a provisional proxy until sold-ingredient costs are tracked:
        // it includes equipment purchases, resale and refills, so it is not accounting profit.
        public static StallProgressionDaySummary EvaluateDay(BusinessSession service, int openingCashCents, int closingCashCents)
        {
            var summary = new StallProgressionDaySummary();
            if (service == null || (service.Setup != null && service.Setup.practice)) return summary;

            if (service.Orders != null)
            {
                foreach (StallOrder order in service.Orders)
                {
                    if (order == null || !order.served || order.paidCents <= 0) continue;
                    summary.paidServedOrders++;
                    summary.orderXpHalves += OrderBaseXpHalves;
                    if (!string.IsNullOrEmpty(order.requestedFlavorId) &&
                        string.Equals(order.requestedFlavorId, order.servedFlavorId, StringComparison.Ordinal))
                    {
                        summary.flavorMatchedOrders++;
                        summary.flavorBonusXpHalves += FlavorBonusXpHalves;
                    }
                }
            }

            long netCash = (long)closingCashCents - openingCashCents;
            summary.netCashChangeCents = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, netCash));
            if (netCash >= ProfitStepCents)
            {
                long steps = netCash / ProfitStepCents;
                long profitHalves = steps * ProfitStepXpHalves;
                summary.profitXpHalves = profitHalves > int.MaxValue ? int.MaxValue : (int)profitHalves;
            }

            return summary;
        }

        // Applies non-negative half-XP to progression state, performing all exact level-ups
        // up to MaxLevel and clamping remaining XP to 0 at Lv40. Returns level-ups earned.
        public static int AddXpHalves(StallProgressionState state, int xpHalves)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (xpHalves <= 0 || state.level >= MaxLevel)
            {
                if (state.level >= MaxLevel)
                {
                    state.level = MaxLevel;
                    state.xpHalves = 0;
                }
                return 0;
            }

            int levelUps = 0;
            long pool = (long)state.xpHalves + xpHalves;
            while (state.level < MaxLevel)
            {
                int need = XpHalvesToNextLevel(state.level);
                if (pool < need) break;
                pool -= need;
                state.level++;
                state.availablePoints++;
                levelUps++;
            }

            if (state.level >= MaxLevel)
            {
                state.level = MaxLevel;
                state.xpHalves = 0;
            }
            else
            {
                state.xpHalves = (int)pool;
            }

            return levelUps;
        }

        // Invoked exclusively inside CampaignState.CommitDay after the day-settlement
        // idempotency guards and after closingCashCents is finalized.
        public static void ApplyDaySettlement(CampaignState campaign, BusinessSession service, CampaignDayLedger ledger)
        {
            if (campaign == null || service == null || ledger == null) return;
            if (service.Setup != null && service.Setup.practice) return;

            StallProgressionState state = EnsureInitialized(campaign);
            StallProgressionDaySummary summary = EvaluateDay(service, ledger.openingCashCents, ledger.closingCashCents);
            int levelUps = AddXpHalves(state, summary.TotalXpHalves);

            ledger.progressionXpHalves = summary.TotalXpHalves;
            ledger.progressionOrderXpHalves = summary.orderXpHalves;
            ledger.progressionFlavorXpHalves = summary.flavorBonusXpHalves;
            ledger.progressionProfitXpHalves = summary.profitXpHalves;
            ledger.progressionLevelUps = levelUps;
        }

        public static bool CanUpgradeNode(CampaignState campaign,string nodeId,out string reason)
        {
            if (campaign == null)
            {
                reason = "没有正在进行的正式经营档。";
                return false;
            }

            StallTalentDefinition def=FindNode(nodeId);
            if(def==null){reason="这个天赋暂未开放。";return false;}
            StallProgressionState state = EnsureInitialized(campaign);
            if (NodeRank(state,def.id) >= def.maxRank)
            {
                reason = "「" + def.name + "」已满级。";
                return false;
            }
            int required=RequiredBranchPoints(def.tier);
            if(BranchPoints(state,def.branch)<required)
            {reason="本分支需先投入 "+required+" 点。";return false;}
            if (state.availablePoints < def.costPerRank)
            {
                reason = "可用天赋点不足。";
                return false;
            }

            reason = "";
            return true;
        }

        public static bool TryUpgradeNode(CampaignState campaign,string nodeId,out string reason)
        {
            if (!CanUpgradeNode(campaign,nodeId,out reason)) return false;
            var def=FindNode(nodeId);
            StallProgressionState state = EnsureInitialized(campaign);
            state.availablePoints -= def.costPerRank;
            var selected=state.nodes.Find(x=>x!=null&&x.id==nodeId);
            if(selected==null){selected=new StallTalentNodeState{id=nodeId};state.nodes.Add(selected);}
            selected.rank++;
            reason = "「" + def.name + "」已升至 " + selected.rank + "/" + def.maxRank + " 级。";
            return true;
        }
        public static bool CanUpgradeAmiable(CampaignState campaign,out string reason)=>CanUpgradeNode(campaign,AmiableTalentId,out reason);
        public static bool TryUpgradeAmiable(CampaignState campaign,out string reason)=>TryUpgradeNode(campaign,AmiableTalentId,out reason);

        // Adjusts formal business setup patience bounds using the unlocked "和气生财" rank.
        // Practice setups are explicitly left untouched.
        public static void ApplyBusinessSetup(CampaignState campaign, BusinessSetup setup)
        {
            if (campaign == null || setup == null || setup.practice) return;
            int rank = AmiableRank(campaign);
            if (rank <= 0) return;

            int scaledMin = ScalePatienceSeconds(setup.patienceMinSeconds, rank);
            int scaledMax = ScalePatienceSeconds(setup.patienceMaxSeconds, rank);
            setup.patienceMinSeconds = scaledMin;
            setup.patienceMaxSeconds = Math.Max(scaledMin, scaledMax);
        }

        // Strict save boundary for CampaignSaveStore.Decode:
        //  - Legacy saves missing progression fields normalize safely to Lv1 / 0 XP / 0 pts / rank 0
        //    without backfilling XP for historical days.
        //  - Out-of-range, negative, or non-conserving progression values are explicitly rejected.
        public static void ValidateAndNormalizeSave(CampaignState campaign)
        {
            if (campaign == null) throw new InvalidDataException("存档缺少经营状态。");

            if (IsLegacyEmpty(campaign.progression))
            {
                campaign.progression = StallProgressionState.CreateDefault();
            }
            else
            {
                StallProgressionState p = campaign.progression;
                if (p.version != CurrentVersion)
                    throw new InvalidDataException("存档摊主成长版本无效。");
                if (p.level < MinLevel || p.level > MaxLevel)
                    throw new InvalidDataException("存档摊主等级越界。");
                if (p.xpHalves < 0)
                    throw new InvalidDataException("存档摊主经验为负。");
                if (p.level == MaxLevel)
                {
                    if (p.xpHalves != 0)
                        throw new InvalidDataException("存档满级摊主经验超出上限。");
                }
                else if (p.xpHalves >= XpHalvesToNextLevel(p.level))
                {
                    throw new InvalidDataException("存档摊主经验超出当前等级上限。");
                }
                if (p.availablePoints < 0)
                    throw new InvalidDataException("存档可用天赋点无效。");
                if(p.nodes==null)p.nodes=new List<StallTalentNodeState>();
                var ids=new HashSet<string>(StringComparer.Ordinal);long invested=0;
                foreach(var node in p.nodes)
                {
                    if(node==null||string.IsNullOrEmpty(node.id)||!ids.Add(node.id))throw new InvalidDataException("存档天赋节点无效或重复。");
                    var def=FindNode(node.id);
                    if(def==null||node.rank<1||node.rank>def.maxRank)throw new InvalidDataException("存档天赋节点未知或等级越界。");
                    invested+=(long)node.rank*def.costPerRank;
                }
                long expectedTotalPoints = (long)p.level - MinLevel;
                long accountedPoints = (long)p.availablePoints + invested;
                if (accountedPoints != expectedTotalPoints)
                    throw new InvalidDataException("存档天赋点与摊主等级不守恒。");
            }

            if (campaign.history != null)
            {
                foreach (CampaignDayLedger day in campaign.history)
                    ValidateLedgerProgression(day);
            }
            ValidateLedgerProgression(campaign.lastDay);
        }

        private static void ValidateLedgerProgression(CampaignDayLedger day)
        {
            if (day == null) return;
            if (day.progressionXpHalves < 0 || day.progressionOrderXpHalves < 0 ||
                day.progressionFlavorXpHalves < 0 || day.progressionProfitXpHalves < 0 ||
                day.progressionLevelUps < 0)
                throw new InvalidDataException("日账摊主经验记录无效。");

            long sum = (long)day.progressionOrderXpHalves + day.progressionFlavorXpHalves + day.progressionProfitXpHalves;
            if (sum != day.progressionXpHalves)
                throw new InvalidDataException("日账摊主经验明细不守恒。");

            if (day.progressionOrderXpHalves % OrderBaseXpHalves != 0 ||
                day.progressionProfitXpHalves % ProfitStepXpHalves != 0)
                throw new InvalidDataException("日账摊主经验步进无效。");

            int paidOrders = day.progressionOrderXpHalves / OrderBaseXpHalves;
            if (day.progressionFlavorXpHalves > (long)paidOrders * FlavorBonusXpHalves)
                throw new InvalidDataException("日账加料经验超出实收成交单数。");

            if (day.progressionLevelUps > MaxLevel - MinLevel)
                throw new InvalidDataException("日账摊主升级次数越界。");
        }
    }

    public sealed partial class GameFlow
    {
        // Night-hub talent upgrade transaction: validates formal campaign Night phase,
        // invests 1 talent point into "和气生财", persists immediately via SaveCampaign,
        // and leaves existing UI snapshots untouched except standard Publish refresh.
        public bool TryUpgradeAmiableTalent(out string feedback)
        {
            if (Campaign == null || run == null || run.Phase != GamePhase.Night || business != null)
            {
                feedback = "仅正式经营夜间账本可分配天赋点。";
                return false;
            }

            var before=StallProgressionRules.EnsureInitialized(Campaign).Copy();
            if (!StallProgressionRules.TryUpgradeAmiable(Campaign, out string upgradeMessage))
            {
                feedback = upgradeMessage;
                return false;
            }

            bool saved = SaveCampaign();
            if (!saved)
            {
                Campaign.progression=before;
                NightHubUI.RefreshProgressionDisplay();
                feedback=CampaignUI.SaveFeedback(SaveStatus);
                return false;
            }
            Publish();
            NightHubUI.RefreshProgressionDisplay();
            feedback = upgradeMessage + "（" + CampaignUI.SaveFeedback(SaveStatus) + "）";
            return true;
        }
    }
}
