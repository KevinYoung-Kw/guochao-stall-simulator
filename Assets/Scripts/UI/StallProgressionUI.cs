using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Night ledger progression page for the vertical slice:
    // displays owner level, XP to next level, available talent points, honest day-settlement
    // XP breakdown, and the single implemented Tier-1 Branch-B talent "和气生财" (0..2).
    // Uses the existing rice-white / ink-blue / vermilion / warm-gold palette (no green).
    public sealed class StallProgressionUI : MonoBehaviour
    {
        private static StallProgressionUI active;
        public static bool IsOpen => active != null;

        private GameFlow flow;
        private CampaignFocusScope focusScope;
        private Text levelBadge, pointsBadge, xpHeadline, daySummaryText, ruleNoteText;
        private Text talentTitleText, currentEffectText, nextEffectText, statusText;
        private RectTransform progressFill;
        private Button upgradeButton, closeButton;
        private Text upgradeButtonLabel;
        private CampaignPaper upgradeButtonFace;
        private string feedback = "";
        private bool feedbackError;

        public static void Open(GameFlow game)
        {
            if (game?.Campaign == null || game.Run == null || game.Run.Phase != GamePhase.Night) return;
            if (OnlineProcurementUI.IsOpen) return;
            Close();
            active = new GameObject("Night owner progression").AddComponent<StallProgressionUI>();
            active.flow = game;
            active.Build();
        }

        public static void Close()
        {
            if (active == null) return;
            var closing = active;
            active = null;
            closing.focusScope?.Restore();
            closing.gameObject.SetActive(false);
            Destroy(closing.gameObject);
            NightHubUI.RefreshProgressionDisplay();
        }

        private void Build()
        {
            RectTransform safe = CampaignUI.Canvas(transform, "Owner progression canvas", 1450,
                CampaignUI.WithAlpha(CampaignUI.Ink, .62f));
            RectTransform sheet = CampaignUI.Paper(safe, "Owner progression sheet",
                new Vector2(1080, 820), CampaignUI.PaperColor);

            CampaignUI.Text(sheet, "摊主成长", new Vector2(-220, 355), new Vector2(540, 76),
                42, CampaignUI.Ink, TextAnchor.MiddleLeft, true);
            CampaignUI.Text(sheet, "每晚收摊结算经验，升级获得天赋点",
                new Vector2(-200, 304), new Vector2(580, 38), 22, CampaignUI.Muted);
            levelBadge = CampaignUI.Text(sheet, "", new Vector2(345, 356), new Vector2(310, 48),
                32, CampaignUI.Ink, TextAnchor.MiddleRight, true);
            pointsBadge = CampaignUI.Text(sheet, "", new Vector2(345, 304), new Vector2(310, 38),
                22, CampaignUI.Vermilion, TextAnchor.MiddleRight, true);
            CampaignUI.Rule(sheet, new Vector2(0, 274), new Vector2(1000, 2));

            // Experience & level progress card (parchment + warm-gold progress bar).
            RectTransform xpCard = CampaignUI.Paper(sheet, "Experience summary card",
                new Vector2(1000, 276), CampaignUI.Background);
            xpCard.anchoredPosition = new Vector2(0, 118);

            xpHeadline = CampaignUI.Text(xpCard, "", new Vector2(0, 96), new Vector2(940, 44),
                24, CampaignUI.Ink, TextAnchor.MiddleLeft, true);

            RectTransform barRail = CampaignUI.Node(xpCard, "XP progress rail");
            barRail.anchoredPosition = new Vector2(0, 52);
            barRail.sizeDelta = new Vector2(940, 22);
            Image railImage = barRail.gameObject.AddComponent<Image>();
            railImage.color = CampaignUI.Pale;
            railImage.raycastTarget = false;

            progressFill = CampaignUI.Node(barRail, "XP progress fill");
            progressFill.anchorMin = new Vector2(0, 0);
            progressFill.anchorMax = new Vector2(0, 1);
            progressFill.pivot = new Vector2(0, .5f);
            progressFill.anchoredPosition = Vector2.zero;
            progressFill.sizeDelta = new Vector2(0, 0);
            Image fillImage = progressFill.gameObject.AddComponent<Image>();
            fillImage.color = CampaignUI.Gold;
            fillImage.raycastTarget = false;

            daySummaryText = CampaignUI.Text(xpCard, "", new Vector2(0, 4), new Vector2(940, 56),
                22, CampaignUI.Ink, TextAnchor.MiddleLeft);
            ruleNoteText = CampaignUI.Text(xpCard,
                "出餐 +2.5 XP　·　满足加料 +0.5 XP\n当日现金净增每满 ¥50 +1 XP",
                new Vector2(0, -76), new Vector2(940, 96), 22, CampaignUI.Muted, TextAnchor.MiddleLeft);

            // Implemented talent section — strictly shows only the real playable node.
            CampaignUI.Text(sheet, "已开放天赋节点 · 分支 B 风味经营（一阶）",
                new Vector2(-220, -48), new Vector2(560, 40), 24, CampaignUI.Ink, TextAnchor.MiddleLeft, true);

            RectTransform talentCard = CampaignUI.Paper(sheet, "Talent B1 amiable card",
                new Vector2(1000, 198), CampaignUI.Background);
            talentCard.anchoredPosition = new Vector2(0, -172);

            talentTitleText = CampaignUI.Text(talentCard, "", new Vector2(-145, 56), new Vector2(650, 44),
                28, CampaignUI.Ink, TextAnchor.MiddleLeft, true);
            currentEffectText = CampaignUI.Text(talentCard, "", new Vector2(-145, 4), new Vector2(650, 44),
                22, CampaignUI.Ink, TextAnchor.MiddleLeft);
            nextEffectText = CampaignUI.Text(talentCard, "", new Vector2(-145, -52), new Vector2(650, 62),
                22, CampaignUI.Muted, TextAnchor.MiddleLeft);

            upgradeButton = CampaignUI.Button(talentCard, "升级并保存", new Vector2(345, 0),
                new Vector2(250, 66), OnUpgradeClicked, true);
            upgradeButton.gameObject.name = "UpgradeAmiable";
            upgradeButtonLabel = upgradeButton.GetComponentInChildren<Text>();
            upgradeButtonFace = upgradeButton.GetComponent<CampaignPaper>();

            CampaignUI.Rule(sheet, new Vector2(0, -296), new Vector2(1000, 2));
            statusText = CampaignUI.Text(sheet, "", new Vector2(-135, -352), new Vector2(730, 72),
                22, CampaignUI.Muted, TextAnchor.MiddleLeft);
            closeButton = CampaignUI.Button(sheet, "返回账本", new Vector2(375, -352),
                new Vector2(220, 58), Close);
            closeButton.gameObject.name = "CloseProgression";

            Refresh();
            focusScope = new CampaignFocusScope(transform);
            CampaignUI.Focus(upgradeButton.interactable ? upgradeButton : closeButton);
        }

        private void OnUpgradeClicked()
        {
            if (flow == null) return;
            bool upgraded = flow.TryUpgradeAmiableTalent(out string message);
            feedback = message;
            feedbackError = !upgraded || (!string.IsNullOrEmpty(flow.SaveStatus) && flow.SaveStatus.StartsWith("未能保存"));
            Refresh();
            if (!upgradeButton.interactable) CampaignUI.Focus(closeButton);
        }

        private void Refresh()
        {
            if (flow?.Campaign == null) return;
            StallProgressionState state = StallProgressionRules.EnsureInitialized(flow.Campaign);
            int needHalves = StallProgressionRules.XpHalvesToNextLevel(state.level);

            levelBadge.text = "摊主 Lv" + state.level + " / " + StallProgressionRules.MaxLevel;
            pointsBadge.text = "可用天赋点：" + state.availablePoints + " 点";
            pointsBadge.color = state.availablePoints > 0 ? CampaignUI.Vermilion : CampaignUI.Muted;

            if (state.level >= StallProgressionRules.MaxLevel)
            {
                xpHeadline.text = "已达等级上限 Lv" + StallProgressionRules.MaxLevel + "（每升 1 级获得 1 个天赋点）";
                progressFill.sizeDelta = new Vector2(940, 0);
            }
            else
            {
                int remainingHalves = Mathf.Max(0, needHalves - state.xpHalves);
                xpHeadline.text = "当前经验：" + StallProgressionRules.FormatXp(state.xpHalves) + " / " +
                    StallProgressionRules.FormatXp(needHalves) + " XP　·　距 Lv" + (state.level + 1) +
                    " 还差 " + StallProgressionRules.FormatXp(remainingHalves) + " XP";
                float ratio = needHalves > 0 ? Mathf.Clamp01((float)state.xpHalves / needHalves) : 0f;
                progressFill.sizeDelta = new Vector2(940f * ratio, 0);
            }

            CampaignDayLedger day = flow.Campaign.lastDay;
            if (state.level >= StallProgressionRules.MaxLevel)
            {
                daySummaryText.text = "摊主已达到满级，继续经营不会增加等级。";
            }
            else if (day != null && day.committed)
            {
                string levelNote = day.progressionLevelUps > 0 ? " · 升级 +" + day.progressionLevelUps : "";
                daySummaryText.text = "第 " + day.dayIndex + " 天日结入账：+" +
                    StallProgressionRules.FormatXp(day.progressionXpHalves) + " XP（实收出餐 +" +
                    StallProgressionRules.FormatXp(day.progressionOrderXpHalves) + " · 指定加料 +" +
                    StallProgressionRules.FormatXp(day.progressionFlavorXpHalves) + " · 现金净增 +" +
                    StallProgressionRules.FormatXp(day.progressionProfitXpHalves) + "）" + levelNote;
            }
            else
            {
                daySummaryText.text = "收摊后显示今天的成长收获。";
            }

            int rank = Mathf.Clamp(StallProgressionRules.NodeRank(state,StallProgressionRules.AmiableTalentId), 0, StallProgressionRules.AmiableMaxRank);
            int curMin = StallProgressionRules.ScalePatienceSeconds(35, rank);
            int curMax = StallProgressionRules.ScalePatienceSeconds(54, rank);
            talentTitleText.text = StallProgressionRules.AmiableTalentName + "　·　Lv" + rank + " / " +
                StallProgressionRules.AmiableMaxRank;

            if (rank == 0)
            {
                currentEffectText.text = "目前：顾客耐心约 " + curMin + "～" + curMax + " 秒";
            }
            else
            {
                currentEffectText.text = "当前：耐心 +" +
                    StallProgressionRules.PatienceBonusPercent(rank) + "%（约 " + curMin + "～" + curMax + " 秒）";
            }

            if (rank >= StallProgressionRules.AmiableMaxRank)
            {
                nextEffectText.text = "已满级 · 正式经营生效";
            }
            else
            {
                int nextRank = rank + 1;
                nextEffectText.text = "下一级：耐心 +" + StallProgressionRules.PatienceBonusPercent(nextRank) +
                    "% · 消耗 " + StallProgressionRules.AmiablePointCost + " 点";
            }

            bool canUpgrade = StallProgressionRules.CanUpgradeAmiable(flow.Campaign, out _);
            upgradeButton.interactable = canUpgrade;
            if (rank >= StallProgressionRules.AmiableMaxRank)
            {
                upgradeButtonLabel.text = "已满级";
                upgradeButtonFace.color = CampaignUI.Pale;
                upgradeButtonLabel.color = CampaignUI.Muted;
            }
            else if (canUpgrade)
            {
                upgradeButtonLabel.text = "升级并保存 (1 点)";
                upgradeButtonFace.color = CampaignUI.Vermilion;
                upgradeButtonLabel.color = CampaignUI.PaperColor;
            }
            else
            {
                upgradeButtonLabel.text = "天赋点不足";
                upgradeButtonFace.color = CampaignUI.Pale;
                upgradeButtonLabel.color = CampaignUI.Ink;
            }

            if (!string.IsNullOrEmpty(feedback))
            {
                statusText.text = feedback;
                statusText.color = feedbackError ? CampaignUI.Rust : CampaignUI.Ink;
            }
            else
            {
                statusText.text = rank > 0
                    ? "下次出摊生效。"
                    : "每次升级获得 1 个天赋点。";
                statusText.color = CampaignUI.Muted;
            }
        }

        private void Update()
        {
            if (flow?.Campaign == null || flow.Run == null || flow.Run.Phase != GamePhase.Night)
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void OnDestroy()
        {
            focusScope?.Restore();
            if (active == this) active = null;
        }
    }
}
