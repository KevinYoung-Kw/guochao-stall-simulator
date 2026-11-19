using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // 营业计划面板：左列多选"想做这些"的菜与份数（纯意向，写入
    // RunState.MealPlan，不动 SelectedRecipes、不自动购买），右列按真实
    // MealPlan.Build 模型显示"采购清单"——每项的物品图、所需数量、
    // 现有/已装/已付待装/篮中未付/缺少状态与简短去向。点击市场渠道条目
    // 在真实市场地图上标记对应货架或采购柜台。Esc 由 GameFlow 的
    // Escape 链经 TryCloseActive() 统一关闭。
    public sealed class MealPlanPanel : MonoBehaviour
    {
        // 当前可见面板：GameFlow.Update 的 Escape 链首调用 TryCloseActive。
        private static MealPlanPanel active;

        public static MealPlanPanel Attach(GameFlow flow, RectTransform parent, Font font)
        {
            var host = new GameObject("Meal plan panel", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            // 宿主必须 stretch 填满 parent，底罩才能覆盖整屏。
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = Vector2.zero; hostRect.anchorMax = Vector2.one;
            hostRect.offsetMin = hostRect.offsetMax = Vector2.zero;
            var panel = host.AddComponent<MealPlanPanel>();
            panel.Initialize(flow, font);
            host.SetActive(false);
            return panel;
        }

        // Escape 链入口：存在可见面板则关闭并返回 true，否则交给后续处理。
        public static bool TryCloseActive()
        {
            if (active != null && active.Visible) { active.Hide(); return true; }
            return false;
        }

        public bool Visible { get; private set; }

        private GameFlow flow;
        private Font font;
        private GameItemIconLibrary itemIcons;
        private CampaignFocusScope focusScope;

        private RectTransform card;
        private RectTransform leftContent, rightContent;
        private ScrollRect leftScroll, rightScroll;
        private Text rightSummary, hintLine;
        private bool hadShoppingLines;
        private Button proceedButton, clearButton;
        private readonly List<RecipeRow> recipeRows = new List<RecipeRow>();
        private readonly List<GameObject> lineRows = new List<GameObject>();

        // Pause bookkeeping: only ever restore the run we paused ourselves.
        private RunState pausedRun;
        private bool heldPause;
        private bool originalPaused;

        // 1420x800 纸板，左右两列共用同一中心坐标系：
        // 左列宽 600 中心 -370（-670..-70），右列宽 680 中心 +310（-30..+650）。
        private const float CardW = 1420f, CardH = 800f;
        private const float LeftColX = -370f, RightColX = 310f;
        private const float LeftColW = 600f, RightColW = 680f;
        // Scroll 内容宽 = 列宽 - 16（CampaignUI.Scroll 右侧留滚动条轨道）。
        private const float LeftContentW = 584f, RightContentW = 664f;
        private const float RecipeRowHeight = 96f, LineRowHeight = 184f;
        private const float HeaderY = 303f, SummaryY = 250f;
        private const float ScrollY = -40f, ScrollH = 508f;
        private const float FooterY = -344f;
        private static readonly Color Ink = CampaignUI.Ink, Paper = CampaignUI.PaperColor,
            Vermilion = CampaignUI.Vermilion, Gold = CampaignUI.Gold, Muted = CampaignUI.Muted,
            Cream = CampaignUI.PaperColor;

        private void Initialize(GameFlow game, Font useFont)
        {
            flow = game;
            font = useFont != null ? useFont : Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            itemIcons = gameObject.AddComponent<GameItemIconLibrary>();
            Build();
            FitCard();
        }

        // 文本一律 Wrap + Truncate：长菜名/长备注截断，绝不横向溢出。
        private Text Label(Transform parent, string value, Vector2 at, Vector2 size, int pixels, Color colour,
            TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var host = new GameObject("Label", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchoredPosition = at;
            // The bundled CJK font needs ~1.45 em even for one visible line.
            rect.sizeDelta = new Vector2(size.x,Mathf.Max(size.y,pixels*1.5f));
            var label = host.AddComponent<Text>();
            label.font = font; label.fontSize = pixels; label.color = colour;
            label.alignment = align; label.text = value;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Image Block(Transform parent, Vector2 at, Vector2 size, Color colour, bool raycast = false)
        {
            var host = new GameObject("Block", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchoredPosition = at; rect.sizeDelta = size;
            var image = host.AddComponent<Image>();
            image.color = colour; image.raycastTarget = raycast;
            return image;
        }

        // content 内统一行容器：anchor 顶中、pivot 中心，
        // y = -(index*height + height/2)，首行贴内容顶部而非中点。
        private static RectTransform RowAt(Transform parent, string name, float x, float index, float width, float height)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, -(index * height + height * .5f));
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private void Build()
        {
            // 模态底罩：stretch 满屏，挡掉后景一切输入。
            var shade = new GameObject("Shade", typeof(RectTransform));
            shade.transform.SetParent(transform, false);
            var shadeRect = (RectTransform)shade.transform;
            shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one;
            shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
            var shadeImage = shade.AddComponent<Image>();
            shadeImage.color = CampaignUI.WithAlpha(Ink, .62f); shadeImage.raycastTarget = true;
            var shadeButton = shade.AddComponent<Button>();
            shadeButton.transition = Selectable.Transition.None;
            shadeButton.navigation = new Navigation { mode = Navigation.Mode.None };
            shadeButton.onClick.AddListener(Hide);

            // 纸张主卡。CreationMenuArt.Panel 默认左上锚点，这里显式居中。
            card = CreationMenuArt.Panel(shade.transform, "Meal plan card", 0, 0, 0, 0, CreationMenuArt.Sheet.Paper);
            card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
            card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(CardW, CardH);
            // Consume blank-card clicks here, so they cannot bubble to the shade.
            var cardBlocker = card.gameObject.AddComponent<Button>();
            cardBlocker.targetGraphic = card.GetComponent<Image>();
            cardBlocker.transition = Selectable.Transition.None;
            cardBlocker.navigation = new Navigation { mode = Navigation.Mode.None };

            Label(card, "今天想做什么", new Vector2(-530, 368), new Vector2(280, 46), 34, Ink, TextAnchor.MiddleLeft, true);
            Label(card, "记下想做的菜，去市场自由挑货。", new Vector2(-90, 370), new Vector2(520, 38), 21, Muted);

            // 右上关闭（显式居中坐标）。
            RectTransform close = CreationMenuArt.Panel(card, "Close", 0, 0, 56, 56, CreationMenuArt.Sheet.Card);
            close.anchorMin = close.anchorMax = new Vector2(.5f, .5f);
            close.pivot = new Vector2(.5f, .5f);
            close.anchoredPosition = new Vector2(668, 360);
            close.GetComponent<Image>().raycastTarget = true;
            Label(close, "×", Vector2.zero, new Vector2(56, 56), 34, Ink, TextAnchor.MiddleCenter, true);
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            closeButton.colors = CampaignUI.ButtonColours(closeButton.colors);
            closeButton.onClick.AddListener(Hide);

            CampaignUI.Rule(card, new Vector2(0, 330), new Vector2(1356, 3));

            // 左右栏标题与右栏汇总（同一中心坐标系）。
            Label(card, "想做这些", new Vector2(LeftColX, HeaderY), new Vector2(LeftColW, 36), 27, Ink, TextAnchor.MiddleLeft, true);
            Label(card, "采购清单", new Vector2(RightColX, HeaderY), new Vector2(RightColW, 36), 27, Ink, TextAnchor.MiddleLeft, true);
            rightSummary = Label(card, "", new Vector2(RightColX, SummaryY), new Vector2(RightColW, 70), 19, Muted, TextAnchor.UpperLeft);

            // 左右独立 ScrollRect：正文区固定，不压底部 footer。
            leftContent = CampaignUI.Scroll(card, "Dish scroll", new Vector2(LeftColX, ScrollY), new Vector2(LeftColW, ScrollH), out leftScroll);
            rightContent = CampaignUI.Scroll(card, "List scroll", new Vector2(RightColX, ScrollY), new Vector2(RightColW, ScrollH), out rightScroll);

            BuildRecipeRows();

            // 底部 footer 独立空间：提示行 + Esc + 清空计划 + 继续准备。
            CampaignUI.Rule(card, new Vector2(0, -300), new Vector2(1356, 2));
            hintLine = Label(card, "", new Vector2(LeftColX, FooterY), new Vector2(LeftColW, 40), 21, Muted);
            Label(card, "Esc 关闭", new Vector2(40, FooterY), new Vector2(110, 36), 18, Muted, TextAnchor.MiddleRight);

            RectTransform clear = CreationMenuArt.Panel(card, "Clear plan", 0, 0, 150, 56, CreationMenuArt.Sheet.Card);
            clear.anchorMin = clear.anchorMax = new Vector2(.5f, .5f);
            clear.pivot = new Vector2(.5f, .5f);
            clear.anchoredPosition = new Vector2(240, FooterY);
            clear.GetComponent<Image>().raycastTarget = true;
            Label(clear, "清空计划", Vector2.zero, new Vector2(150, 56), 22, Ink, TextAnchor.MiddleCenter, true);
            clearButton = clear.gameObject.AddComponent<Button>();
            clearButton.targetGraphic = clear.GetComponent<Image>();
            clearButton.colors = CampaignUI.ButtonColours(clearButton.colors);
            clearButton.onClick.AddListener(ClearPlan);

            RectTransform proceed = CreationMenuArt.Panel(card, "Proceed", 0, 0, 240, 64, CreationMenuArt.Sheet.Red);
            proceed.anchorMin = proceed.anchorMax = new Vector2(.5f, .5f);
            proceed.pivot = new Vector2(.5f, .5f);
            proceed.anchoredPosition = new Vector2(520, FooterY);
            proceed.GetComponent<Image>().raycastTarget = true;
            Label(proceed, "继续准备", Vector2.zero, new Vector2(240, 64), 26, Cream, TextAnchor.MiddleCenter, true);
            proceedButton = proceed.gameObject.AddComponent<Button>();
            proceedButton.targetGraphic = proceed.GetComponent<Image>();
            proceedButton.colors = CampaignUI.ButtonColours(proceedButton.colors);
            proceedButton.onClick.AddListener(Hide);
        }

        // 左列一次性建 12 行，之后只刷新文字、份数与选中态，不整列重建。
        private void BuildRecipeRows()
        {
            recipeRows.Clear();
            int count = BusinessSession.Recipes.Length;
            leftContent.sizeDelta = new Vector2(0, count * RecipeRowHeight);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                StallRecipe recipe = BusinessSession.Recipes[i];

                var row = RowAt(leftContent, "Dish row " + recipe.id, 0, i, LeftContentW, RecipeRowHeight - 8);
                row.anchoredPosition = new Vector2(0, -i * RecipeRowHeight - RecipeRowHeight * .5f);
                var rowImage = row.gameObject.AddComponent<Image>();
                rowImage.color = CampaignUI.Background;
                // 行 face 即点选热区：raycast + toggle 的 targetGraphic。
                rowImage.raycastTarget = true;

                // 左侧选中条（金）。
                var bar = Block(row, new Vector2(-(LeftContentW * .5f - 3), 0), new Vector2(6, 72), CampaignUI.WithAlpha(Gold, 0f));

                // 菜图：与 BusinessHUD 相同的 StallUIIcon 矢量画法（非写实图）。
                var iconHost = new GameObject("Dish icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(StallUIIcon));
                iconHost.transform.SetParent(row, false);
                var iconRect = (RectTransform)iconHost.transform;
                iconRect.anchoredPosition = new Vector2(-LeftContentW * .5f + 40, 0);
                iconRect.sizeDelta = new Vector2(62, 62);
                var dishIcon = iconHost.GetComponent<StallUIIcon>();
                dishIcon.index = StallMenuReadout.IconIndex(index);
                dishIcon.raycastTarget = false;

                // 菜名 + 单价（子节点以行矩形中心为原点定位）。
                float textX = -75f;
                Label(row, StallMenuReadout.DishTitle(index), new Vector2(textX, 14), new Vector2(240, 32), 23, Ink, TextAnchor.MiddleLeft, true);
                Label(row, CampaignUI.Money(recipe.priceCents) + " / 份", new Vector2(textX, -20), new Vector2(240, 28), 20, Muted);

                // 步进：− 份数 ＋（子按钮接自己的点击，不与整行点选冲突）。
                var minus = StepperButton(row, "−", new Vector2(96, 0), () => ChangePortions(index, -1));
                Text countText = Label(row, "0", new Vector2(160, 0), new Vector2(60, 44), 24, Ink, TextAnchor.MiddleCenter, true);
                var plus = StepperButton(row, "＋", new Vector2(224, 0), () => ChangePortions(index, +1));

                var toggle = row.gameObject.AddComponent<Button>();
                toggle.transition = Selectable.Transition.None;
                toggle.targetGraphic = rowImage;
                toggle.onClick.AddListener(() => ToggleRecipe(index));

                recipeRows.Add(new RecipeRow
                {
                    index = index,
                    face = rowImage,
                    bar = bar,
                    count = countText,
                    minus = minus,
                    plus = plus,
                });
            }
        }

        // 步进按钮：Image 与 Text 分属父子节点（同 GO 共享 CanvasRenderer
        // 是非法的），Text 放 child。
        private Button StepperButton(Transform parent, string glyph, Vector2 center, Action pressed)
        {
            var host = new GameObject("Stepper " + glyph, typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rect = (RectTransform)host.transform;
            rect.anchoredPosition = center; rect.sizeDelta = new Vector2(48, 48);
            var image = host.AddComponent<Image>();
            image.color = CampaignUI.Pale; image.raycastTarget = true;
            var labelHost = new GameObject("Glyph", typeof(RectTransform));
            labelHost.transform.SetParent(host.transform, false);
            var labelRect = (RectTransform)labelHost.transform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var label = labelHost.AddComponent<Text>();
            label.font = font; label.fontSize = 26; label.color = Ink;
            label.alignment = TextAnchor.MiddleCenter; label.text = glyph;
            label.raycastTarget = false;
            var button = host.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CampaignUI.ButtonColours(button.colors);
            button.onClick.AddListener(() => pressed());
            return button;
        }

        private sealed class RecipeRow
        {
            public int index;
            public Image face, bar;
            public Text count;
            public Button minus, plus;
        }

        private int PortionsOf(string recipeId)
        {
            var run = flow != null ? flow.Run : null;
            if (run == null || run.MealPlan == null) return 0;
            foreach (var choice in run.MealPlan)
                if (choice != null && choice.recipeId == recipeId) return choice.portions;
            return 0;
        }

        private void ToggleRecipe(int index)
        {
            string id = BusinessSession.Recipes[index].id;
            int current = PortionsOf(id);
            ChangePortions(index, current > 0 ? 0 : 1, true);
        }

        private void ChangePortions(int index, int value, bool absolute = false)
        {
            var run = flow != null ? flow.Run : null;
            if (run == null) return;
            string id = BusinessSession.Recipes[index].id;
            int current = PortionsOf(id);
            int next = absolute ? value : current + value;
            next = Mathf.Clamp(next, 0, 99);
            if (next == current) return;
            // 数据工位合同：合法 true / 0 删除 / 非法 false 且不改动其它状态。
            // 只写 MealPlan 意向，不动 SelectedRecipes、不触发购买。
            if (run.SetMealPlan(id, next)) Refresh();
        }

        // 轻量清空：对计划里已有的每个 id 调 SetMealPlan(id, 0)。空计划仍可玩。
        private void ClearPlan()
        {
            var run = flow != null ? flow.Run : null;
            if (run == null) return;
            for (int i = run.MealPlan.Count - 1; i >= 0; i--)
            {
                var choice = run.MealPlan[i];
                if (choice != null) run.SetMealPlan(choice.recipeId, 0);
            }
            Refresh();
        }

        // —— 显示生命周期 ——

        public void Show()
        {
            if (Visible || flow == null || flow.Run == null) return;
            Visible = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FitCard();
            hintLine.text = "";
            hadShoppingLines=false;
            pausedRun = flow != null ? flow.Run : null;
            originalPaused = pausedRun != null && pausedRun.Paused;
            if(CampaignMenuUI.IsOpen)CampaignMenuUI.Close();
            // 先置 Visible 再暂停，root ArtHUD 消费 Changed 时可用 Visible
            // 屏蔽普通暂停层。
            if (flow != null) flow.SetPaused(true);
            heldPause = pausedRun != null;
            active = this;
            focusScope = new CampaignFocusScope(transform);
            Refresh();
            // 默认焦点落到可见的"继续准备"上，键盘/手柄可直接操作。
            CampaignUI.Focus(proceedButton);
        }

        public void Hide()
        {
            if (!Visible) return;
            Visible = false;
            if (active == this) active = null;
            gameObject.SetActive(false);
            if (focusScope != null) { focusScope.Restore(); focusScope = null; }
            // 只还原自己暂停过的那一个 run，绝不反向改动别的 run。
            if (heldPause && pausedRun != null && flow != null && flow.Run == pausedRun)
                flow.SetPaused(originalPaused);
            heldPause = false;
            pausedRun = null;
        }

        private void OnDestroy()
        {
            Visible = false;
            if (active == this) active = null;
            if (focusScope != null) { focusScope.Restore(); focusScope = null; }
            if (heldPause && pausedRun != null && flow != null && flow.Run == pausedRun)
                flow.SetPaused(originalPaused);
            heldPause = false;
            pausedRun = null;
        }

        // —— 刷新 ——

        private void Refresh()
        {
            RefreshLeft();
            RefreshRight();
        }

        private void RefreshLeft()
        {
            foreach (RecipeRow row in recipeRows)
            {
                StallRecipe recipe = BusinessSession.Recipes[row.index];
                int portions = PortionsOf(recipe.id);
                row.count.text = portions.ToString();
                bool chosen = portions > 0;
                row.face.color = chosen ? CampaignUI.Selected : CampaignUI.Background;
                row.bar.color = CampaignUI.WithAlpha(Gold, chosen ? 1f : 0f);
                // 0 份禁用 −，99 份禁用 ＋。
                row.minus.interactable = portions > 0;
                row.plus.interactable = portions < 99;
            }
        }

        private void RefreshRight()
        {
            // 重建右列前保护焦点与滚动位置。
            Vector2 keep = rightScroll != null ? rightScroll.normalizedPosition : Vector2.one;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool selectedWasInList = selected != null && selected.transform.IsChildOf(rightContent);
            foreach (GameObject row in lineRows) { row.SetActive(false); Destroy(row); }
            lineRows.Clear();

            var run = flow != null ? flow.Run : null;
            MealPlanReport report = run != null ? MealPlan.Build(run) : null;
            var lines = report != null ? report.lines : null;

            if (lines == null || lines.Count == 0)
            {
                rightContent.sizeDelta = new Vector2(0, 240);
                // 空态同样顶锚，出现在内容顶部而非中点。
                var note = RowAt(rightContent, "Empty note", 0, 0, RightContentW, 40);
                Label(note, "选几道想做的菜，帮你记下要带的货。", Vector2.zero, new Vector2(RightContentW, 40), 23, Ink, TextAnchor.MiddleCenter, true);
                var note2 = RowAt(rightContent, "Empty note 2", 0, 1, RightContentW, 56);
                Label(note2, "也可以直接继续准备。", Vector2.zero, new Vector2(RightContentW, 40), 20, Muted, TextAnchor.MiddleCenter);
                lineRows.Add(note.gameObject);
                lineRows.Add(note2.gameObject);
                rightSummary.text = SummaryText(report);
            }
            else
            {
                rightContent.sizeDelta = new Vector2(0, lines.Count * LineRowHeight);
                for (int i = 0; i < lines.Count; i++)
                {
                    MealPlanLine line = lines[i];
                    if (line == null) continue;
                    BuildLineRow(run, line, i);
                }
                // summary 已含"建议采购约 ¥x"，不再与 estimatedCents 重复合计。
                rightSummary.text = SummaryText(report);
            }
            bool hasShoppingLines=lines!=null&&lines.Count>0;
            if (rightScroll != null) rightScroll.normalizedPosition = hadShoppingLines&&hasShoppingLines?keep:new Vector2(0,1);
            hadShoppingLines=hasShoppingLines;
            // 焦点若在被销毁的行上，回退到"继续准备"。
            if (selectedWasInList || selected == null || !selected.activeInHierarchy) CampaignUI.Focus(proceedButton);
            else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected);
        }

        private string SummaryText(MealPlanReport report)
        {
            if (report == null) return "";
            int planned = flow != null && flow.Run != null ? flow.Run.MealPlan.Count : 0;
            string first = StallUiCopy.Format("plan.cooked", "车上齐料 {0} 道 · 想做 {1} 道", report.cookableNow.Count, planned);
            var methods = new List<string>();
            foreach (string id in report.methodsNow)
                methods.Add(StallUiCopy.Text("plan.method." + id, FreeKitchenRules.FindMethod(id).label));
            string second = methods.Count == 0 ? StallUiCopy.Text("plan.no_methods", "车上暂时做不了菜") :
                StallUiCopy.Format("plan.methods", "按车上食材可做：{0}", string.Join("、", methods));
            return first + "\n" + second + (planned == 0 ? "" : "\n已计价补货 " + CampaignUI.Money(report.estimatedCents));
        }

        private void BuildLineRow(RunState run, MealPlanLine line, int i)
        {
            var row = RowAt(rightContent, "List row " + line.key, 0, i, RightContentW, LineRowHeight - 8);
            row.anchoredPosition = new Vector2(0, -i * LineRowHeight - LineRowHeight * .5f);
            var face = row.gameObject.AddComponent<Image>();
            face.color = i % 2 == 0 ? CampaignUI.WithAlpha(Paper, .55f) : CampaignUI.Background;
            face.raycastTarget = true;

            // Energy lines use the matching real container model for their portrait.
            string iconId = line.productId;
            if (string.IsNullOrEmpty(iconId)&&!line.estimateUncertain)
                iconId = line.key == "gas" ? "E03" : line.key == "cartridge" ? "E02"
                    : line.key == "coal" ? "E01" : line.key == "electricity" ? "E05" : line.key;
            Sprite sprite = !string.IsNullOrEmpty(iconId) ? itemIcons.Get(iconId) : null;
            if (sprite != null) CampaignUI.Icon(row, sprite, new Vector2(-RightContentW * .5f + 32, 0), new Vector2(56, 56));

            // 三层分开：清单名 / 状态 / 说明，全部 Wrap+Truncate。
            float textW = RightContentW - 78 - 6;
            float textX = -RightContentW * .5f + 78 + textW * .5f;
            Label(row, line.name, new Vector2(textX, 64), new Vector2(textW, 40), 23, Ink, TextAnchor.MiddleLeft, true);
            Label(row, StatusText(run, line), new Vector2(textX, 12), new Vector2(textW, 62), 21, Muted, TextAnchor.MiddleLeft);
            string where = WhereText(line);
            string note = string.IsNullOrEmpty(line.note) ? "" : line.note;
            string third = where == "" ? note : where + (note == "" ? "" : "　" + note);
            Label(row, third, new Vector2(textX, -50), new Vector2(textW, 60), 19,
                line.missing > 0 ? Vermilion : Muted, TextAnchor.MiddleLeft);

            var button = row.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = face;
            string capturedChannel = line.channel, capturedId = line.productId;
            button.onClick.AddListener(() => OnLineClicked(capturedChannel, capturedId));

            lineRows.Add(row.gameObject);
        }

        // The data layer supplies each quantity in the line's own unit (portions,
        // pieces or heat points). Do not repeat inventory/fuel conversion in UI.
        private static void SplitPaidCargo(MealPlanLine line, out int loadedUnits, out int awaitingUnits)
        {
            awaitingUnits = Mathf.Clamp(line.paidWaiting, 0, line.paidCargo);
            loadedUnits = Mathf.Max(0, line.paidCargo - awaitingUnits);
        }

        private static bool IsFuelLine(MealPlanLine line)
            => line.key == "gas" || line.key == "cartridge" || line.key == "coal" || line.key == "electricity";

        private static string StatusText(RunState run, MealPlanLine line)
        {
            if(line.estimateUncertain)return "还需核对 · 本项未计价";
            // 家中已有、未装车的装备：真实去向，不算已备。
            if (line.channel == "home" || line.equipment && run != null && run.HomeEquipment.Contains(line.key) && !run.LoadedEquipment.Contains(line.key))
                return "需 " + line.required + " · 家中已有 · 待装";
            SplitPaidCargo(line, out int loadedUnits, out int awaitingUnits);
            string text = "需 " + line.required;
            int available = Mathf.Max(0, line.owned);
            if (available > 0) text += " · " + (IsFuelLine(line) ? "余 " : "现有 ") + available;
            if (loadedUnits > 0) text += " · 已装 " + loadedUnits;
            if (awaitingUnits > 0) text += " · 已付待装 " + awaitingUnits;
            if (line.inBasket > 0) text += " · 篮中未付 " + line.inBasket;
            if (line.missing > 0) text += " · 缺 " + line.missing;
            return text;
        }

        private static string WhereText(MealPlanLine line)
        {
            switch (line.channel)
            {
                case "shelf": return "市场货架";
                case "counter": return "采购柜台";
                case "night": return "夜间补给";
                case "home": return "家中已有";
                default: return "";
            }
        }

        private void OnLineClicked(string channel, string productId)
        {
            bool inMarket = flow != null && flow.Run != null && flow.Run.Phase == GamePhase.Market;
            if ((channel == "shelf" || channel == "counter") && !string.IsNullOrEmpty(productId))
            {
                if (inMarket && MarketRecoveryMap.TryPinProduct(productId))
                {
                    // 定位成功：收起计划，让玩家直接看到地图目标。
                    Hide();
                    return;
                }
                hintLine.text = inMarket ? "地图还没就绪，稍后再点一次。"
                    : "这件在市场上，到了市场再点就能直接定位。";
                hintLine.color = Muted;
                return;
            }
            hintLine.text = channel == "home" ? "这项家里已经有了，不用买。"
                : channel == "night" ? "这项走夜间补给，市场上没有现货。"
                : "这项去向还不确定，看备注。";
            hintLine.color = Muted;
        }

        // Apply only when the host changes size or the sheet opens.
        private void OnRectTransformDimensionsChange() { FitCard(); }
        private void FitCard()
        {
            var host = transform as RectTransform;
            if (host == null || card == null) return;
            Vector2 available = host.rect.size;
            if (available.x < 20f || available.y < 20f) return;
            float scale = Mathf.Min(available.x / 1600f, available.y / 900f);
            card.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
