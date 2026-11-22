using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // The data and validation belong to RunProfile/CharacterCatalog. This screen
    // only edits a draft and sends a copy after the player explicitly starts.
    // Visual structure follows the creation-screen art direction: real 3D
    // protagonist portrait on the left, 2x4 career identity cards, and one
    // paper mainboard with the trait market, navy detail strip, ledger card
    // and the vermilion opening button. Surfaces come from the generated
    // CreationV16 material atlas; all wording stays data-driven.
    public sealed class CharacterCreationUI : MonoBehaviour
    {
        public static bool IsOpen => instance != null;
        private static CharacterCreationUI instance;
        private static readonly Color Ink = CampaignUI.Ink, Paper = CampaignUI.PaperColor, Rust = CampaignUI.Rust,
            Muted = CampaignUI.Muted, Cream = CampaignUI.PaperColor;
        private RunProfile profile = new RunProfile();
        private Action<RunProfile> start, preview;
        private Action practice;
        private Font font, handFont;
        private RectTransform root, layout, traitContent, selectedContent;
        private ScrollRect traitScroll;
        private Text pointValue, status, detail, detailName, detailEffect;
        private Button startButton, continueButton, clearTraitsButton;
        private readonly List<Button> careerButtons = new List<Button>();
        private readonly List<Text> careerNumbers = new List<Text>();
        private readonly List<SelectionSymbol> careerSymbols = new List<SelectionSymbol>();
        private readonly List<int> careerSymbolSlots = new List<int>();
        private readonly List<Image> careerPaintedIcons = new List<Image>();
        private readonly List<SelectionSymbol> careerTicks = new List<SelectionSymbol>();
        private readonly List<Image> careerFills = new List<Image>();
        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<Text> categoryLabels = new List<Text>();
        private readonly List<SelectionSymbol> categoryMarks = new List<SelectionSymbol>();
        private readonly List<Button> debtButtons = new List<Button>();
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private Texture2D scrimTexture;
        private GameObject debtRoot;
        private Text debtNote;
        private GameItemIconLibrary itemIcons;
        private CreationMenuIcons paintedIcons;
        private CreationPortraitStage portrait;
        private RectTransform gearRow;
        private SelectionSymbol headerIcon;
        private Text cashText, skillsText, innateText;
        private string category = "positive", hoveredTrait = "";
        private bool closing;
        private bool replacementConfirmed;
        private GameObject replacementConfirm;
        private CampaignFocusScope replacementFocus;

        public static void Open(Action<RunProfile> onStart, Action onPractice, Action<RunProfile> onPreview = null)
        {
            if (instance != null) Destroy(instance.gameObject);
            var go = new GameObject("CharacterCreation");
            instance = go.AddComponent<CharacterCreationUI>();
            instance.start = onStart; instance.practice = onPractice; instance.preview = onPreview;
            GameFlow.Instance?.SetPaused(true);
            instance.Build(); instance.Refresh();
        }
        public static void Dismiss()
        {
            if (instance == null) return;
            CharacterCreationUI closingUI = instance; instance = null; closingUI.replacementFocus?.Restore();
            closingUI.gameObject.SetActive(false); Destroy(closingUI.gameObject);
        }

        private void Build()
        {
            font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            if (font == null) throw new InvalidOperationException("选人界面缺少已打包的中文字体。");
            handFont = Resources.Load<Font>("UI/Fonts/MaShanZheng-Regular") ?? font;
            var canvasObject = new GameObject("CreationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); root = canvasObject.GetComponent<RectTransform>();
            Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1200;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Expand fits the whole 1920x1080 layout inside any aspect: scale
            // follows the larger dimension factor, so ultrawide (e.g.
            // 2560x1080) keeps the full 1080 logical height and the painted
            // backdrop stretches over the extra width. MatchWidthOrHeight
            // would scale by width and crop the 1016px board vertically.
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (EventSystem.current == null) new GameObject("CreationEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // The background is the REAL Home room rendered live into the
            // RawImage below (room + protagonist errand on layer 31, unscaled
            // time); the painted rental-room backdrop and the floating
            // portrait sticker are gone. The paper board and career cards sit
            // on top of the live room picture.
            portrait = CreationPortraitStage.Create(root, "Portrait", 0, 0, 1920, 1080);
            LeftScrim();
            Icons();
            // Keep the 1920×1080 interface centred when an ultrawide canvas
            // expands, while the live room behind it still fills the display.
            layout = Rect(root, "CreationFrame", 0, 0, 1920, 1080);
            layout.anchorMin = layout.anchorMax = new Vector2(.5f, .5f);
            layout.pivot = new Vector2(.5f, .5f); layout.anchoredPosition = Vector2.zero;

            // Warm white ink with a soft dark drop shadow so the line stays
            // legible against the bright painted room behind it.
            RectTransform careerHeading = CreationMenuArt.Panel(layout, "CareerHeading", 452, 94, 352, 44, CreationMenuArt.Sheet.Navy);
            careerHeading.GetComponent<Image>().raycastTarget = false;
            Text columnHint = TextAt(careerHeading, "CareerColumnHint", 16, 1, 320, 42, 26, Cream, "①  选择你的出身", true, TextAnchor.MiddleLeft);
            var hintShadow = columnHint.gameObject.AddComponent<Shadow>();
            hintShadow.effectColor = CampaignUI.WithAlpha(CampaignUI.Ink, .55f);
            hintShadow.effectDistance = new Vector2(1, -1);
            int index = 0;
            foreach (CareerDefinition career in CharacterCatalog.Data.careers)
            {
                string id = career.id;
                RectTransform card = CreationMenuArt.Panel(layout, "Career_" + id, 452 + (index % 2) * 182, 146 + (index / 2) * 166, 172, 156, CreationMenuArt.Sheet.Card);
                var fill = card.GetComponent<Image>();
                fill.raycastTarget = true;
                var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = fill;
                ColorBlock colours = button.colors; colours = CampaignUI.ButtonColours(colours); colours.fadeDuration = .08f; button.colors = colours;
                CreationHoverLift.Attach(card, CreationMenuArt.Frame(card));
                Text number = TextAt(card, "Number", 10, 8, 56, 28, 19, Muted, "", false, TextAnchor.MiddleLeft);
                // Painted atlas icons take precedence; only a missing painted cell
                // creates a vector fallback, which is tracked separately.
                Sprite careerSprite = Icons().Career(id);
                Image paintedCareer = null;
                SelectionSymbol icon = null;
                if (careerSprite != null)
                {
                    RectTransform paintedRect = Rect(card, "CareerIcon", 45, 8, 82, 82);
                    paintedCareer = paintedRect.gameObject.AddComponent<Image>();
                    paintedCareer.sprite = careerSprite; paintedCareer.preserveAspect = true; paintedCareer.raycastTarget = false;
                }
                else
                {
                    icon = SelectionSymbol.Create(card, "CareerIcon", CareerKind(id), new Vector2(82, 82));
                    RectTransform iconRect = (RectTransform)icon.transform;
                    iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 1); iconRect.pivot = new Vector2(0, 1);
                    iconRect.anchoredPosition = new Vector2(45, -8);
                    careerSymbols.Add(icon); careerSymbolSlots.Add(index);
                }
                careerPaintedIcons.Add(paintedCareer);
                // 64px tall: two Noto lines at size 20 need ~60px, so long
                // career names keep both lines inside the 156px card.
                TextAt(card, "CareerName", 8, 79, 156, 73, 24, Ink, ReadableCareer(career.name), true, TextAnchor.MiddleCenter);
                SelectionSymbol tick = SelectionSymbol.Create(card, "CareerTick", SelectionSymbolKind.MarkTick, new Vector2(30, 28));
                RectTransform tickRect = (RectTransform)tick.transform;
                tickRect.anchorMin = tickRect.anchorMax = new Vector2(0, 1); tickRect.pivot = new Vector2(0, 1);
                tickRect.anchoredPosition = new Vector2(134, -8);
                tick.color = CampaignUI.Vermilion; tick.enabled = false;
                button.onClick.AddListener(() => ChooseCareer(id));
                careerButtons.Add(button); careerNumbers.Add(number);
                careerTicks.Add(tick); careerFills.Add(fill); index++;
            }

            // Main paper board: title, balance badge, trait market, navy detail
            // strip, ledger card and the action row.
            RectTransform board = CreationMenuArt.Panel(layout, "MainBoard", 812, 32, 1072, 1016, CreationMenuArt.Sheet.Paper);
            Text title = TextAt(board, "Title", 46, 16, 661, 82, 58, Ink, "② 搭配开局特质", false, TextAnchor.MiddleLeft);
            title.font = handFont;

            RectTransform badge = CreationMenuArt.Panel(board, "PointsBadge", 724, 26, 300, 62, CreationMenuArt.Sheet.Navy);
            badge.GetComponent<Image>().raycastTarget = false;
            SelectionSymbol star = SelectionSymbol.Create(badge, "BadgeStar", SelectionSymbolKind.MetaStar, new Vector2(30, 30));
            RectTransform starRect = (RectTransform)star.transform;
            starRect.anchorMin = starRect.anchorMax = new Vector2(0, .5f); starRect.pivot = new Vector2(0, .5f);
            starRect.anchoredPosition = new Vector2(18, 0);
            pointValue = TextAt(badge, "Points", 53, 7, 241, 48, 30, Cream, "", true, TextAnchor.MiddleLeft);

            string[] categories = { "positive", "negative", "hobby" };
            string[] titles = { "长处", "短板", "爱好" };
            SelectionSymbolKind[] marks = { SelectionSymbolKind.TraitUp, SelectionSymbolKind.TraitDown, SelectionSymbolKind.TraitHeart };
            for (int i = 0; i < 3; i++)
            {
                string id = categories[i];
                Button tab = ArtButton(board, "Category_" + id, 46 + i * 196, 108, 184, 60, titles[i], () => { category = id; hoveredTrait = ""; BuildTraits(); traitScroll.verticalNormalizedPosition = 1f; RefreshCategories(); RefreshDetail(); },
                    CreationMenuArt.Sheet.Card, Ink, 21);
                SelectionSymbol mark = SelectionSymbol.Create(tab.transform, "CategoryIcon", marks[i], new Vector2(24, 24));
                RectTransform markRect = (RectTransform)mark.transform;
                markRect.anchorMin = markRect.anchorMax = new Vector2(0, .5f); markRect.pivot = new Vector2(0, .5f);
                markRect.anchoredPosition = new Vector2(14, 0);
                Text label = null;
                foreach (Text child in tab.GetComponentsInChildren<Text>()) if (child.name == "Label") label = child;
                // Short titles ("长处"/"短板"/"爱好") sit right of the kept
                // category icon instead of overlapping it.
                if (label != null)
                {
                    label.fontSize = 28;
                    RectTransform labelRect = (RectTransform)label.transform;
                    labelRect.anchoredPosition = new Vector2(44, labelRect.anchoredPosition.y);
                    labelRect.sizeDelta = new Vector2(184 - 54, labelRect.sizeDelta.y);
                }
                categoryButtons.Add(tab); categoryLabels.Add(label); categoryMarks.Add(mark);
            }

            TextAt(board, "TraitBrowseHint", 655, 112, 360, 52, 23, Muted, "滚动查看 · 点击特质加入", false, TextAnchor.MiddleRight);
            traitScroll = CreateScroll(board, "Traits", 46, 178, 976, 326, out traitContent);

            RectTransform detailBar = CreationMenuArt.Panel(board, "DetailBar", 46, 515, 976, 78, CreationMenuArt.Sheet.Navy);
            detailBar.GetComponent<Image>().raycastTarget = false;
            SelectionSymbol detailIcon = SelectionSymbol.Create(detailBar, "DetailIcon", SelectionSymbolKind.MetaStar, new Vector2(46, 46));
            RectTransform detailIconRect = (RectTransform)detailIcon.transform;
            detailIconRect.anchorMin = detailIconRect.anchorMax = new Vector2(0, .5f); detailIconRect.pivot = new Vector2(0, .5f);
            detailIconRect.anchoredPosition = new Vector2(20, 0);
            detailName = TextAt(detailBar, "DetailName", 82, 7, 224, 62, 29, Cream, "", true, TextAnchor.MiddleLeft);
            Flat(detailBar, "DetailDivider", 318, 15, 2, 48, CampaignUI.WithAlpha(Cream, .35f));
            detailEffect = TextAt(detailBar, "DetailEffect", 336, 8, 612, 60, 24, Cream, "", false, TextAnchor.MiddleLeft);

            CreateScroll(board, "TraitDetails", 46, 599, 976, 60, out RectTransform detailContent);
            detail = FlowText(detailContent, "FullTraitDescription", 942, 22, Ink);

            RectTransform selectedBar = CreationMenuArt.Panel(board, "SelectedTraitsBar", 46, 665, 976, 58, CreationMenuArt.Sheet.Card);
            selectedBar.GetComponent<Image>().raycastTarget = true;
            TextAt(selectedBar, "SelectedTitle", 14, 5, 102, 46, 22, Ink, "已选", true, TextAnchor.MiddleCenter);
            RectTransform selectedViewport = Rect(selectedBar, "SelectedViewport", 122, 5, 838, 48);
            selectedViewport.gameObject.AddComponent<RectMask2D>();
            selectedContent = Rect(selectedViewport, "SelectedContent", 0, 0, 838, 48);
            ScrollRect chosenScroll = selectedBar.gameObject.AddComponent<ScrollRect>();
            chosenScroll.viewport = selectedViewport; chosenScroll.content = selectedContent;
            chosenScroll.horizontal = true; chosenScroll.vertical = false; chosenScroll.movementType = ScrollRect.MovementType.Clamped;
            chosenScroll.scrollSensitivity = 42;

            // 开局家底 ledger: cash / skills / debt on one line, real gear
            // portraits in the middle, innate gifts along the bottom.
            RectTransform ledger = CreationMenuArt.Panel(board, "LedgerCard", 46, 730, 976, 158, CreationMenuArt.Sheet.Card);
            ledger.GetComponent<Image>().raycastTarget = false;
            RectTransform ledgerHead = CreationMenuArt.Panel(ledger, "LedgerHeader", 16, 9, 172, 42, CreationMenuArt.Sheet.Navy);
            ledgerHead.GetComponent<Image>().raycastTarget = false;
            headerIcon = SelectionSymbol.Create(ledgerHead, "HeaderCareerIcon", SelectionSymbolKind.CareerWhistle, new Vector2(24, 24));
            RectTransform headerIconRect = (RectTransform)headerIcon.transform;
            headerIconRect.anchorMin = headerIconRect.anchorMax = new Vector2(0, .5f); headerIconRect.pivot = new Vector2(0, .5f);
            headerIconRect.anchoredPosition = new Vector2(10, 0);
            headerIcon.color = Cream;
            TextAt(ledgerHead, "LedgerTitle", 40, 3, 130, 36, 21, Cream, "开局家底", true, TextAnchor.MiddleLeft);

            SelectionSymbol coin = SelectionSymbol.Create(ledger, "CashIcon", SelectionSymbolKind.MetaCoin, new Vector2(24, 24));
            RectTransform coinRect = (RectTransform)coin.transform;
            coinRect.anchorMin = coinRect.anchorMax = new Vector2(0, 1); coinRect.pivot = new Vector2(0, 1);
            coinRect.anchoredPosition = new Vector2(206, -18);
            cashText = TextAt(ledger, "CashValue", 236, 9, 200, 42, 22, Ink, "", true, TextAnchor.MiddleLeft);
            SelectionSymbol wrench = SelectionSymbol.Create(ledger, "SkillsIcon", SelectionSymbolKind.MetaWrench, new Vector2(24, 24));
            RectTransform wrenchRect = (RectTransform)wrench.transform;
            wrenchRect.anchorMin = wrenchRect.anchorMax = new Vector2(0, 1); wrenchRect.pivot = new Vector2(0, 1);
            wrenchRect.anchoredPosition = new Vector2(438, -18);
            skillsText = TextAt(ledger, "SkillsLine", 468, 8, 182, 44, 18, Ink, "", false, TextAnchor.MiddleLeft);

            debtRoot = Rect(ledger, "DebtChoice", 650, 8, 308, 44).gameObject;
            debtNote = TextAt(debtRoot.transform, "DebtNote", 0, 4, 210, 34, 19, Ink, "无开局债务", false, TextAnchor.MiddleLeft);
            debtNote.horizontalOverflow = HorizontalWrapMode.Overflow;
            string[] debts = { "宽限", "标准", "地狱" };
            for (int i = 0; i < 3; i++)
            {
                string difficulty = debts[i];
                debtButtons.Add(ArtButton(debtRoot.transform, "Debt_" + difficulty, i * 100, 0, 94, 42, difficulty,
                    () => { profile.debtDifficulty = difficulty; Refresh(); }, CreationMenuArt.Sheet.Card, Ink, 19));
            }
            gearRow = Rect(ledger, "GearTiles", 16, 50, 944, 66);
            innateText = TextAt(ledger, "CareerInnate", 16, 119, 944, 33, 19, Ink, "", false, TextAnchor.MiddleLeft);
            innateText.verticalOverflow = VerticalWrapMode.Overflow;

            status = TextAt(board, "Validation", 46, 897, 930, 35, 22, Ink, "", false, TextAnchor.MiddleLeft);
            clearTraitsButton = ArtButton(board, "ClearTraits", 46, 936, 180, 68, "清空特质", () => { profile.selectedTraits = Array.Empty<string>(); hoveredTrait = ""; BuildTraits(); Refresh(); },
                CreationMenuArt.Sheet.Card, Ink, 25);
            ArtButton(board, "Practice", 242, 936, 274, 68, "晚高峰练习", () => Close(false),
                CreationMenuArt.Sheet.Navy, Cream, 32);
            startButton = ArtButton(board, "BeginRun", 550, 936, 472, 68, "开始第一天", () => Close(true),
                CreationMenuArt.Sheet.Red, Cream, 41, true);

            if (GameFlow.Instance != null && GameFlow.Instance.HasCampaignSave)
                continueButton = ArtButton(layout, "ContinueCampaign", 452, 830, 372, 58, "继续上一次经营 · 保留旧账", () =>
                {
                    GameFlow game = GameFlow.Instance;
                    if (game != null && !game.ContinueCampaign() && instance == this) { status.text = game.SaveStatus; status.color = Ink; }
                }, CreationMenuArt.Sheet.Navy, Cream, 20);

            BuildTraits();
            if (continueButton != null) CampaignUI.Focus(continueButton);
            else if (careerButtons.Count > 0) EventSystem.current.SetSelectedGameObject(careerButtons[0].gameObject);
        }

        private Button ArtButton(Transform parent, string name, float x, float y, float width, float height, string title,
            Action action, CreationMenuArt.Sheet sheet, Color labelColor, int labelSize, bool handWritten = false)
        {
            RectTransform rect = CreationMenuArt.Panel(parent, name, x, y, width, height, sheet);
            var image = rect.GetComponent<Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            ColorBlock colours = button.colors; colours = CampaignUI.ButtonColours(colours); colours.fadeDuration = .08f; button.colors = colours;
            CreationHoverLift.Attach(rect, CreationMenuArt.Frame(rect));
            Text label = TextAt(rect, "Label", 10, 6, width - 20, height - 12, labelSize, labelColor, title, !handWritten, TextAnchor.MiddleCenter);
            if (handWritten) label.font = handFont;
            button.onClick.AddListener(() => action());
            return button;
        }

        private static string ReadableCareer(string name)
        {
            switch (name)
            {
                case "被裁大厂程序员": return "被裁大厂\n程序员";
                case "破产奶茶店老板": return "破产奶茶店\n老板";
                case "退休食堂大师傅": return "退休食堂\n大师傅";
                default: return name;
            }
        }

        private void ChooseCareer(string id)
        {
            profile.careerId = id;
            // Keep compatible player choices. Career-gifted traits are free and
            // may never be charged again; new innate opposites are also removed.
            var retained = new List<string>();
            foreach (string name in profile.selectedTraits)
            {
                TraitDefinition trait = Array.Find(CharacterCatalog.Data.traits, x => x.id == name);
                if (trait == null || IsInnate(name) || ConflictsWithInnate(trait)) continue;
                retained.Add(name);
            }
            profile.selectedTraits = retained.ToArray(); hoveredTrait = ""; BuildTraits(); Refresh();
        }

        private bool IsInnate(string name) => profile.Career != null && profile.Career.innate.Contains("【" + name + "】");
        private bool ConflictsWithInnate(TraitDefinition trait)
        {
            foreach (TraitDefinition other in CharacterCatalog.Data.traits)
                if (IsInnate(other.id) && (Opposes(trait, other.id) || Opposes(other, trait.id))) return true;
            return false;
        }
        private static bool Opposes(TraitDefinition trait, string name)
        {
            if (string.IsNullOrEmpty(trait.opposite)) return false;
            foreach (string part in trait.opposite.Replace("、", ",").Split(',')) if (part.Trim() == name) return true;
            return false;
        }

        private void ToggleTrait(string id)
        {
            hoveredTrait = id;
            if (IsInnate(id)) { Refresh(); return; }
            var selected = new List<string>(profile.selectedTraits);
            if (selected.Contains(id)) selected.Remove(id); else selected.Add(id);
            RunProfile candidate = Copy(profile); candidate.selectedTraits = selected.ToArray();
            bool valid = candidate.Validate(out string reason);
            // A temporary point deficit is editable; structural conflicts are
            // rejected with the authoritative validator's actual explanation.
            if (!valid && !reason.StartsWith("还缺 ", StringComparison.Ordinal))
            { status.text = reason; status.color = Ink; RefreshDetail(); return; }
            profile = candidate; BuildTraits(); Refresh();
        }

        private void BuildTraits()
        {
            // A click/keypress selects the trait row; this rebuild destroys that
            // row, which would silently break keyboard navigation. Remember a
            // trait-subtree focus by its Trait_<id> row name and reselect the
            // rebuilt row afterwards. Any focus outside the trait list (career
            // cards, tabs, clear/practice buttons) is left untouched.
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            string focusTrait = focus != null && focus.transform.IsChildOf(traitContent) ? focus.name : null;
            for (int i = traitContent.childCount - 1; i >= 0; i--)
            { GameObject old = traitContent.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
            const float viewportWidth = 958f; // 976-frame minus the 18px reserved scrollbar gutter
            const float tileWidth = 462f, columnGap = 34f, rowHeight = 108f;
            int slot = 0;
            // List usable effects first without hiding the remaining catalogue.
            var displayedTraits = new List<TraitDefinition>();
            foreach (TraitDefinition item in CharacterCatalog.Data.traits) if (item.category == category && LiveTraitEffect(item.id) != null) displayedTraits.Add(item);
            foreach (TraitDefinition item in CharacterCatalog.Data.traits) if (item.category == category && LiveTraitEffect(item.id) == null) displayedTraits.Add(item);
            foreach (TraitDefinition trait in displayedTraits)
            {
                string id = trait.id;
                bool selected = Array.IndexOf(profile.selectedTraits, id) >= 0, innate = IsInnate(id);
                float x = (slot % 2) * (tileWidth + columnGap), y = (slot / 2) * rowHeight;
                RectTransform tile = CreationMenuArt.Panel(traitContent, "Trait_" + id, x, y, tileWidth, 102, CreationMenuArt.Sheet.Card);
                var fill = tile.GetComponent<Image>(); fill.raycastTarget = true;
                if (selected) fill.color = new Color32(255, 239, 207, 255);
                var button = tile.gameObject.AddComponent<Button>(); button.targetGraphic = fill;
                ColorBlock colours = button.colors; colours = CampaignUI.ButtonColours(colours); colours.fadeDuration = .08f; button.colors = colours;
                Image frame = CreationMenuArt.Frame(tile);
                frame.color = CampaignUI.WithAlpha(selected ? CampaignUI.Vermilion : CampaignUI.Gold, selected ? .95f : 0f);
                var lift = CreationHoverLift.Attach(tile, frame);
                lift.pinned = selected; // selected tiles hold a steady vermilion frame
                // Semantic pictogram: painted atlas first, existing vector as fallback.
                SelectionSymbolKind mark = category == "hobby" ? HobbyKind(id) : TraitKind(id);
                Sprite traitSprite = Icons().Trait(id);
                SelectionSymbol icon = null;
                if (traitSprite != null)
                {
                    RectTransform paintedRect = Rect(tile, "TraitIcon", 14, 20, 56, 56);
                    var painted = paintedRect.gameObject.AddComponent<Image>();
                    painted.sprite = traitSprite; painted.preserveAspect = true; painted.raycastTarget = false;
                }
                else
                {
                    icon = SelectionSymbol.Create(tile, "TraitIcon", mark, new Vector2(56, 56));
                    RectTransform iconRect = (RectTransform)icon.transform;
                    iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 1); iconRect.pivot = new Vector2(0, 1);
                    iconRect.anchoredPosition = new Vector2(14, -20);
                }
                string effect = LiveTraitEffect(id);
                if (effect == null && !selected && !innate) fill.color = new Color32(231, 227, 218, 255);
                TextAt(tile, "Name", 82, 7, 250, 45, 27, innate || effect == null ? Muted : Ink, id, true);
                TextAt(tile, "EffectSummary", 82, 54, 245, 34, 21, effect == null ? Muted : Ink,
                    CompactEffect(effect ?? "效果待接入", 12), false, TextAnchor.MiddleLeft);
                string points = innate ? "免费" : (trait.points > 0 ? "+" : "") + trait.points + " 点";
                RectTransform costTag = CreationMenuArt.Panel(tile, "CostTag", 338, 10, 112, 42, CreationMenuArt.Sheet.Navy);
                costTag.GetComponent<Image>().raycastTarget = false;
                if (effect == null && !selected) costTag.GetComponent<Image>().color = new Color32(177, 177, 177, 255);
                TextAt(costTag, "PointCost", 2, 1, 108, 40, 24, Cream, points, true, TextAnchor.MiddleCenter);
                string marker = innate ? "职业自带" : selected ? "已选" : ConflictsWithInnate(trait) ? "与出身冲突" : "";
                TextAt(tile, "State", 322, 60, 98, 30, 19, selected ? CampaignUI.Vermilion : Muted, marker, false, TextAnchor.MiddleRight);
                // A vermilion tick accompanies the highlight frame, so selection
                // never relies on colour alone. The tick sits at the tile's
                // bottom-right corner, clear of the (shortened, right-aligned)
                // State label to its left.
                SelectionSymbol tick = SelectionSymbol.Create(tile, "TraitTick", SelectionSymbolKind.MarkTick, new Vector2(24, 22));
                RectTransform tickRect = (RectTransform)tick.transform;
                tickRect.anchorMin = tickRect.anchorMax = new Vector2(0, 1); tickRect.pivot = new Vector2(0, 1);
                tickRect.anchoredPosition = new Vector2(426, -64);
                tick.color = CampaignUI.Vermilion; tick.enabled = selected;
                var hint = tile.gameObject.AddComponent<CharacterTraitHint>(); hint.Show = () => { hoveredTrait = id; RefreshDetail(); };
                button.onClick.AddListener(() => ToggleTrait(id));
                button.interactable = !innate && !ConflictsWithInnate(trait);
                if (!button.interactable && icon != null) icon.color = Muted;
                if (innate) fill.color = new Color32(238, 233, 222, 255);
                slot++;
            }
            traitContent.sizeDelta = new Vector2(viewportWidth, Mathf.Max(326, Mathf.CeilToInt(slot / 2f) * rowHeight));
            if (!string.IsNullOrEmpty(focusTrait))
            {
                // Destroy is deferred, so old rows still exist as inactive
                // children; only an active rebuilt row with the same Trait_<id>
                // name is a valid focus target.
                for (int i = 0; i < traitContent.childCount; i++)
                {
                    Transform child = traitContent.GetChild(i);
                    if (child.name != focusTrait || !child.gameObject.activeSelf) continue;
                    var rebuilt = child.GetComponent<Button>();
                    if (rebuilt != null && rebuilt.interactable) EventSystem.current.SetSelectedGameObject(child.gameObject);
                    break;
                }
            }
            RefreshCategories();
        }

        private void Refresh()
        {
            pointValue.text = "可用特质点  " + (profile.Balance > 0 ? "+" : "") + profile.Balance;
            bool valid = profile.Validate(out string reason); startButton.interactable = valid;
            if (clearTraitsButton != null) clearTraitsButton.interactable = profile.selectedTraits.Length > 0;
            status.text = reason; status.color = valid ? Muted : Ink;
            for (int i = 0; i < careerButtons.Count; i++)
            {
                bool active = CharacterCatalog.Data.careers[i].id == profile.careerId;
                careerFills[i].color = active ? CampaignUI.Selected : Color.white;
                // Explicit named fields: the icon children are graphics, never
                // Text, and the career name must survive selection unchanged.
                careerNumbers[i].text = active ? "选定" : "";
                careerNumbers[i].color = active ? CampaignUI.Vermilion : Muted;
                careerTicks[i].enabled = active;
                if (i < careerPaintedIcons.Count && careerPaintedIcons[i] != null)
                    careerPaintedIcons[i].color = Color.white;
            }
            // Vector fallbacks still tint with selection; the painted cells do not.
            for (int i = 0; i < careerSymbols.Count; i++)
            {
                int slot = careerSymbolSlots[i];
                bool active = CharacterCatalog.Data.careers[slot].id == profile.careerId;
                careerSymbols[i].color = active ? CampaignUI.Vermilion : CampaignUI.Muted;
            }
            CareerDefinition career = profile.Career;
            headerIcon.SetKind(CareerKind(career.id));
            cashText.text = "开局资金  ¥" + career.cash.ToString("N0");
            skillsText.text = (profile.InitialDebtCents > 0 ? "" : "手艺设定  ") + career.skills;
            BuildGearTiles(career);
            innateText.text = LiveCareerEffect(career.id);
            bool hasDebt = profile.InitialDebtCents > 0;
            debtRoot.SetActive(hasDebt);
            debtNote.gameObject.SetActive(false);
            skillsText.rectTransform.sizeDelta = new Vector2(hasDebt ? 182 : 488, skillsText.rectTransform.sizeDelta.y);
            foreach (Button debtButton in debtButtons)
            {
                debtButton.gameObject.SetActive(hasDebt);
                debtButton.GetComponent<Image>().color = debtButton.name == "Debt_" + profile.debtDifficulty ? CampaignUI.Selected : Color.white;
            }
            RebuildSelectedTraits(); RefreshCategories(); RefreshDetail(); preview?.Invoke(Copy(profile));
        }

        // Compact game short names for the 26 common career-gifted devices.
        // Catalogue names and inventory are untouched; anything missing here
        // falls back to the full formal EquipmentName.
        private static string GearShortName(string id)
        {
            switch (id)
            {
                case "U14": return "料理剪";
                case "U15": return "抹布";
                case "B03": return "单眼灶";
                case "V01": return "薄铁锅";
                case "V02": return "章丘锅";
                case "V05": return "平底鏊";
                case "U02": return "铁锅铲";
                case "E03": return "气罐";
                case "P04": return "牛皮纸袋";
                case "S01": return "辣椒油";
                case "B07": return "微波仓";
                case "E05": return "蓄电瓶";
                case "P05": return "PP餐盒";
                case "S02": return "蒜蓉";
                case "E08": return "冷藏箱";
                case "E15": return "大黑伞";
                case "V15": return "冰槽台";
                case "U13": return "暴打锤";
                case "P08": return "注塑杯";
                case "S13": return "珍珠";
                case "S14": return "椰果";
                case "B02": return "卡式炉";
                case "V08": return "砂锅架";
                case "U12": return "长柄勺";
                case "P07": return "纸汤碗";
                case "E10": return "大铁锁";
                default: return CharacterCatalog.EquipmentName(id);
            }
        }

        private void BuildGearTiles(CareerDefinition career)
        {
            if (itemIcons == null) itemIcons = gameObject.AddComponent<GameItemIconLibrary>();
            for (int i = gearRow.childCount - 1; i >= 0; i--) { GameObject old = gearRow.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
            float x = 0;
            foreach (string id in career.gearIds)
            {
                RectTransform tile = Rect(gearRow, "Gear " + id, x, 0, 124, 64);
                // Raycast-transparent hit zone over the tile so the parent's
                // GearHoverHint receives hover events from the icon area.
                Image hit = tile.gameObject.AddComponent<Image>();
                hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
                if (id == "U14" || id == "U15")
                {
                    Sprite gearSprite = Icons().Gear(id);
                    if (gearSprite != null)
                    {
                        RectTransform paintedRect = Rect(tile, "GearIcon", 42, 0, 40, 38);
                        var painted = paintedRect.gameObject.AddComponent<Image>();
                        painted.sprite = gearSprite; painted.preserveAspect = true; painted.raycastTarget = false;
                    }
                    else
                    {
                        SelectionSymbol symbol = SelectionSymbol.Create(tile, "GearIcon",
                            id == "U14" ? SelectionSymbolKind.GearScissors : SelectionSymbolKind.GearCloth, new Vector2(40, 40));
                        RectTransform symbolRect = (RectTransform)symbol.transform;
                        symbolRect.anchorMin = symbolRect.anchorMax = new Vector2(.5f, 1); symbolRect.pivot = new Vector2(.5f, 1);
                        symbolRect.anchoredPosition = new Vector2(0, 0);
                    }
                }
                else
                {
                    Sprite sprite = itemIcons.Get(id);
                    if (sprite != null)
                    {
                        RectTransform photo = Rect(tile, "GearIcon", 0, 0, 124, 40);
                        var image = photo.gameObject.AddComponent<Image>(); image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
                    }
                }
                // 22px tall so size-14 Noto glyphs are not clipped; icon above
                // was shortened to keep the row inside its 64px height.
                Text gearName = TextAt(tile, "GearName", 0, 38, 124, 30, 18, Ink, GearShortName(id), false, TextAnchor.MiddleCenter);
                gearName.verticalOverflow = VerticalWrapMode.Overflow;
                var gearHint = tile.gameObject.AddComponent<GearHoverHint>();
                string gearId = id; // capture for the closure
                gearHint.Show = () =>
                {
                    detailName.text = GearShortName(gearId);
                    detailEffect.text = CharacterCatalog.EquipmentName(gearId);
                    string gift = Useful(career.gearDescription) ? career.gearDescription : "这份家底随职业赠送，开局直接可用，不是交易来的。";
                    detail.text = "<b>" + CharacterCatalog.EquipmentName(gearId) + "</b>\n" + gift +
                        "\n\n<b>出身设定</b>　" + career.innate;
                };
                x += 130;
            }
        }

        private void RefreshCategories()
        {
            string[] categories = { "positive", "negative", "hobby" };
            string[] titles = { "长处", "短板", "爱好" };
            for (int i = 0; i < categoryButtons.Count; i++)
            {
                bool active = category == categories[i];
                var image = categoryButtons[i].GetComponent<Image>();
                Sprite sprite = CreationMenuArt.SpriteFor(active ? CreationMenuArt.Sheet.Navy : CreationMenuArt.Sheet.Card);
                if (sprite != null) { image.sprite = sprite; image.type = Image.Type.Sliced; }
                else image.color = active ? CampaignUI.Ink : CampaignUI.PaperColor;
                int chosen = 0;
                foreach (string name in profile.selectedTraits)
                {
                    TraitDefinition t = Array.Find(CharacterCatalog.Data.traits, item => item.id == name);
                    if (t != null && t.category == categories[i]) chosen++;
                }
                categoryLabels[i].text = titles[i] + (chosen > 0 ? " " + chosen : "");
                categoryLabels[i].color = active ? Cream : Ink;
                categoryMarks[i].color = active ? CampaignUI.Gold : CampaignUI.Muted;
            }
        }

        private void RebuildSelectedTraits()
        {
            if (selectedContent == null) return;
            for (int i = selectedContent.childCount - 1; i >= 0; i--)
            { GameObject old = selectedContent.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
            float x = 0;
            foreach (string selected in profile.selectedTraits)
            {
                string name = selected;
                float width = Mathf.Clamp(60 + name.Length * 23, 148, 270);
                ArtButton(selectedContent, "Selected_" + name, x, 2, width, 42, name + "  ×", () =>
                {
                    ToggleTrait(name);
                    int current = category == "positive" ? 0 : category == "negative" ? 1 : 2;
                    if (EventSystem.current != null && current < categoryButtons.Count)
                        EventSystem.current.SetSelectedGameObject(categoryButtons[current].gameObject);
                }, CreationMenuArt.Sheet.Navy, Cream, 20);
                x += width + 8;
            }
            if (profile.selectedTraits.Length == 0)
                TextAt(selectedContent, "SelectedEmpty", 2, 1, 560, 44, 22, Ink, "选择特质后会放在这里", false, TextAnchor.MiddleLeft);
            selectedContent.sizeDelta = new Vector2(Mathf.Max(838, x), 48);
        }

        // Only effects with actual runtime consumers are presented as active.
        // The remaining catalogue text is kept as the planned design in the detail panel.
        private static string LiveTraitEffect(string id)
        {
            switch (id)
            {
                case "收纳达人": return "购物车 18→24 格";
                case "丢三落四": return "购物车 18→12 格";
                case "心灵手巧": return "搬运耗时减少约 1/3";
                case "笨手笨脚": return "搬运耗时翻倍";
                case "嗜睡": return "早晨整备减少 10 秒";
                case "飙车党": return "前往超市时间 -25%";
                case "龟速司机": return "前往超市时间 +40%";
                case "商超老会员": return "超市购物 85 折";
                case "暴躁脾气": return "拦下逃单可双倍收款";
                case "科技与狠活大师": return "微波设备回收价 +20%";
                case "嘴甜": return "旧货回收估价提高";
                default: return null;
            }
        }

        private static string LiveCareerEffect(string id)
        {
            switch (id)
            {
                case "career1": return "出身能力 · 拦截逃单多 1.5 秒";
                case "career2": return "出身能力 · 初始口碑 +1 星";
                case "career3": return "出身能力 · 夜间网购 88 折";
                case "career5": return "出身能力 · 前往超市耗时 -25%";
                case "career8": return "开局负债 ¥3,000 · 还款规则待接入";
                default: return "出身专属能力待接入";
            }
        }

        private static string CompactEffect(string value, int limit)
        {
            value = (value ?? "").Replace('\n', ' ').Replace('\r', ' ');
            return value.Length <= limit ? value : value.Substring(0, limit - 1) + "…";
        }

        private void RefreshDetail()
        {
            TraitDefinition trait = Array.Find(CharacterCatalog.Data.traits, x => x.id == hoveredTrait);
            if (trait == null)
            {
                detailName.text = "搭配特质";
                detailEffect.text = "点选自选特质，再点一次取消。";
                detail.text = category == "positive" ? "长处消耗点数，选择适合自己的起步优势。"
                    : category == "negative" ? "短板补充点数，留意它会带来的影响。"
                    : "爱好规划为起步手艺，本版效果待接入。";
                return;
            }
            string live = LiveTraitEffect(trait.id);
            var text = new StringBuilder(live == null ? "<b>本版尚未生效</b>" : "<b>本版生效：</b>" + live);
            if (IsInnate(trait.id)) text.Append(" · 出身自带，不重复扣点");
            if (Useful(trait.purchase)) text.Append("\n设定 · 采购：").Append(trait.purchase);
            if (Useful(trait.business)) text.Append("\n设定 · 营业：").Append(trait.business);
            if (Useful(trait.night)) text.Append("\n设定 · 夜间：").Append(trait.night);
            if (Useful(trait.opposite)) text.Append("\n互斥：").Append(trait.opposite);
            if (Useful(trait.note)) text.Append("\n").Append(trait.note);
            detail.text = text.ToString();
            detailName.text = trait.id;
            detailEffect.text = live ?? "效果待接入";
        }

        private static bool Useful(string text) => !string.IsNullOrWhiteSpace(text) && text != "—" && text != "-";
        private void Close(bool begin)
        {
            if (closing) return;
            if (begin && !profile.Validate(out string reason)) { status.text = reason; return; }
            if (begin && !replacementConfirmed && GameFlow.Instance != null && GameFlow.Instance.HasCampaignSave)
            { ConfirmReplacement(); return; }
            closing = true; RunProfile chosen = Copy(profile); Action<RunProfile> beginCallback = start; Action practiceCallback = practice;
            replacementFocus?.Restore();
            instance = null; gameObject.SetActive(false); Destroy(gameObject);
            if (begin) beginCallback?.Invoke(chosen); else practiceCallback?.Invoke();
        }
        private void ConfirmReplacement()
        {
            if (replacementConfirm != null) return;
            RectTransform modal = CampaignUI.Node(root, "Confirm new campaign"); CampaignUI.Stretch(modal); replacementConfirm = modal.gameObject;
            Image shade = modal.gameObject.AddComponent<Image>(); shade.color = CampaignUI.WithAlpha(Ink, .84f); shade.raycastTarget = true;
            RectTransform paper = CampaignUI.Paper(modal, "A fresh account book", new Vector2(760, 430), Paper);
            CampaignUI.Text(paper, "另开一本新的账", new Vector2(0, 133), new Vector2(654, 66), 34, Ink, TextAnchor.MiddleCenter, true);
            CampaignUI.Text(paper, "开始第一天将启用新进度，旧进度会备份。", new Vector2(0, 43), new Vector2(640, 94), 24, Muted, TextAnchor.MiddleCenter);
            Button back = CampaignUI.Button(paper, "返回 · 保留当前经营", new Vector2(-181, -111), new Vector2(322, 64), CancelReplacement);
            CampaignUI.Button(paper, "新建经营 · 从第一天开始", new Vector2(181, -111), new Vector2(322, 64), () => { replacementConfirmed = true; Close(true); }, true);
            replacementFocus = new CampaignFocusScope(modal); CampaignUI.Focus(back);
        }
        private void CancelReplacement()
        {
            if (replacementConfirm == null) return; replacementFocus?.Restore(); replacementFocus = null;
            replacementConfirm.SetActive(false); Destroy(replacementConfirm); replacementConfirm = null; CampaignUI.Focus(startButton);
        }
        private void Update()
        { if (replacementConfirm != null && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))) CancelReplacement(); }
        private static RunProfile Copy(RunProfile source) => new RunProfile { careerId = source.careerId, selectedTraits = (string[])source.selectedTraits.Clone(), debtDifficulty = source.debtDifficulty };
        private void OnDestroy()
        {
            replacementFocus?.Restore(); if (instance == this) instance = null;
            foreach (Sprite sprite in ownedSprites) if (sprite) Destroy(sprite);
            if (scrimTexture != null) Destroy(scrimTexture);
        }

        // Soft ink scrim over the live room's left band so the career column
        // stays legible without hiding the room; it fades to transparent well
        // before the paper board begins. Texture and sprite are generated and
        // owned here for cleanup.
        private void LeftScrim()
        {
            const int w = 256, h = 4;
            scrimTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "CreationLeftScrim", hideFlags = HideFlags.DontSave };
            var ink = (Color32)CampaignUI.Ink;
            var pixels = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                float a = Mathf.Pow(Mathf.Clamp01(1f - u / .45f), 1.4f) * .62f;
                var c = new Color32(ink.r, ink.g, ink.b, (byte)(a * 255f));
                for (int y = 0; y < h; y++) pixels[y * w + x] = c;
            }
            scrimTexture.SetPixels32(pixels);
            scrimTexture.Apply(false, false);
            Sprite sprite = Sprite.Create(scrimTexture, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
            sprite.name = "CreationLeftScrim";
            sprite.hideFlags = HideFlags.DontSave;
            ownedSprites.Add(sprite);
            RectTransform rect = Rect(root, "LeftScrim", 0, 0, 1920, 1080);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
        }

        private CreationMenuIcons Icons()
        {
            if (paintedIcons == null) paintedIcons = gameObject.AddComponent<CreationMenuIcons>();
            return paintedIcons;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Image Flat(Transform parent, string name, float x, float y, float width, float height, Color color)
        { Image image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        private Text TextAt(Transform parent, string name, float x, float y, float width, float height, int size, Color color, string content, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            Text text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.color = color; text.text = content;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = true; text.raycastTarget = false; return text;
        }
        private ScrollRect CreateScroll(Transform parent, string name, float x, float y, float width, float height, out RectTransform content)
        {
            RectTransform frame = Rect(parent, name, x, y, width, height); Image hit = frame.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .001f);
            var viewport = Rect(frame, "Viewport", 0, 0, width - 18, height); viewport.gameObject.AddComponent<RectMask2D>();
            content = Rect(viewport, "Content", 0, 0, width - 18, height);
            var scroll = frame.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 36; scroll.inertia = true; scroll.decelerationRate = .1f;
            Image track = Flat(frame, "ScrollTrack", width - 8, 0, 6, height, CampaignUI.WithAlpha(Ink, .12f));
            Scrollbar bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
            RectTransform handle = Rect(track.transform, "ScrollHandle", 0, 0, 6, height); Image thumb = handle.gameObject.AddComponent<Image>(); thumb.color = CampaignUI.Gold;
            // Scrollbar sizes its handle through anchors. A fixed height here
            // would be added again and push the thumb below the list viewport.
            handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.pivot = new Vector2(.5f, .5f);
            handle.anchoredPosition = Vector2.zero; handle.sizeDelta = Vector2.zero;
            bar.handleRect = handle; bar.targetGraphic = thumb; bar.size = .25f;
            scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }
        private Text FlowText(RectTransform content, string name, float width, int fontSize, Color color)
        {
            Text text = TextAt(content, name, 0, 0, width, 10, fontSize, color, ""); text.verticalOverflow = VerticalWrapMode.Overflow;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childControlWidth = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            var contentFit = content.gameObject.AddComponent<ContentSizeFitter>(); contentFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var element = text.gameObject.AddComponent<LayoutElement>(); element.preferredWidth = width;
            return text;
        }

        private static SelectionSymbolKind CareerKind(string id)
        {
            switch (id)
            {
                case "career2": return SelectionSymbolKind.CareerWok;
                case "career3": return SelectionSymbolKind.CareerKeyboard;
                case "career4": return SelectionSymbolKind.CareerChefHat;
                case "career5": return SelectionSymbolKind.CareerHelmet;
                case "career6": return SelectionSymbolKind.CareerBoba;
                case "career7": return SelectionSymbolKind.CareerLadle;
                case "career8": return SelectionSymbolKind.CareerLedger;
                default: return SelectionSymbolKind.CareerWhistle;
            }
        }
        private static SelectionSymbolKind HobbyKind(string id)
        {
            switch (id)
            {
                case "夜宵摊常客": return SelectionSymbolKind.HobbySkewer;
                case "奶茶控": return SelectionSymbolKind.HobbyBoba;
                case "嘴甜": return SelectionSymbolKind.HobbyCandy;
                case "健身爱好者": return SelectionSymbolKind.HobbyDumbbell;
                case "面食爱好者": return SelectionSymbolKind.HobbyNoodles;
                case "煲汤达人": return SelectionSymbolKind.HobbySoup;
                default: return SelectionSymbolKind.TraitHeart;
            }
        }
        // Semantic trait pictograms, mirrored for opposite traits so the
        // positive/negative pages read as the same object gone right/wrong.
        // Ids match CharacterCatalog.json exactly; unknown traits fall back
        // to the plain category arrow.
        private SelectionSymbolKind TraitKind(string id)
        {
            switch (id)
            {
                case "速学者": case "迟钝学者": case "速读": case "慢读": case "文盲": return SelectionSymbolKind.TraitBook;
                case "收纳达人": case "丢三落四": return SelectionSymbolKind.TraitBag;
                case "幸运儿": case "倒霉蛋": return SelectionSymbolKind.TraitDice;
                case "眼尖": case "高度近视": return SelectionSymbolKind.TraitEye;
                case "顺风耳": case "耳背": return SelectionSymbolKind.TraitEar;
                case "夜猫子": case "嗜睡": return SelectionSymbolKind.TraitMoon;
                case "低调": case "招摇": return SelectionSymbolKind.TraitMoon;
                case "胆大": case "胆小": return SelectionSymbolKind.TraitDice;
                case "身手敏捷": case "毛手毛脚": return SelectionSymbolKind.TraitDice;
                case "不慌不忙": return SelectionSymbolKind.TraitMoon;
                case "心灵手巧": case "笨手笨脚": case "修车学徒": return SelectionSymbolKind.MetaWrench;
                case "天生神力": case "细狗体虚": return SelectionSymbolKind.HobbyDumbbell;
                case "飙车党": case "龟速司机": return SelectionSymbolKind.CareerHelmet;
                case "家常厨艺": case "料理洁癖": return SelectionSymbolKind.CareerWok;
                case "大胃王": return SelectionSymbolKind.HobbyNoodles;
                case "勤俭": case "烟瘾": return SelectionSymbolKind.MetaCoin;
                case "商超老会员": case "国潮审美达人": return SelectionSymbolKind.MetaGift;
                case "科技与狠活大师": return SelectionSymbolKind.CareerKeyboard;
                case "暴躁脾气": case "老寒腿": case "信息闭塞": case "社恐摊主": return SelectionSymbolKind.TraitEar;
                default: return category == "positive" ? SelectionSymbolKind.TraitUp : SelectionSymbolKind.TraitDown;
            }
        }
    }

    // Gear tile hover: reuses the navy detail strip instead of a popup.
    public sealed class GearHoverHint : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public Action Show;
        public void OnPointerEnter(PointerEventData data) => Show?.Invoke();
        public void OnSelect(BaseEventData data) => Show?.Invoke();
    }

    public sealed class CharacterTraitHint : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public Action Show;
        public void OnPointerEnter(PointerEventData data) => Show?.Invoke();
        public void OnSelect(BaseEventData data)
        {
            Show?.Invoke();
            ScrollRect scroll = GetComponentInParent<ScrollRect>();
            RectTransform row = transform as RectTransform;
            if (scroll == null || row == null) return;
            float top = -row.anchoredPosition.y, bottom = top + row.rect.height;
            float viewTop = scroll.content.anchoredPosition.y, viewBottom = viewTop + scroll.viewport.rect.height;
            if (top < viewTop) scroll.content.anchoredPosition = new Vector2(0, top);
            else if (bottom > viewBottom) scroll.content.anchoredPosition = new Vector2(0, bottom - scroll.viewport.rect.height);
        }
    }
}
