using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    /// <summary>Native UGUI presentation of GameFlow snapshots. All actions return to GameFlow.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class ArtHUD : MonoBehaviour
    {
        private static readonly Color Ink = CampaignUI.Ink, Cream = CampaignUI.PaperColor, Rust = CampaignUI.Vermilion, Muted = CampaignUI.Muted;
        private static readonly Color Gold = CampaignUI.Gold, Selected = CampaignUI.Selected, WarmGray = CampaignUI.Pale;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly List<Sprite> ownedSprites = new List<Sprite>();
        private GameFlow flow;
        private Snapshot snapshot;
        private Font font;
        private Canvas canvas;
        private RectTransform root;
        private GameObject timerRoot, cashRoot, heldRoot, cartRoot, bagRoot, bagPanel, inventoryPanel, actionRoot;
        private GameObject modalShade, pausePanel, helpPanel, completePanel, errorPanel, checkoutProgressRoot, feedbackRoot;
        private GameObject marketGuideRoot, homeGuideRoot, openingRoot;
        private readonly Image[] receiptIcons = new Image[3];
        private Text marketObjective, receiptEmptyText, receiptKey;
        private Text locationText, objectiveText, timerTitle, timerValue, timerInside, openingText, cashText, heldTitle, heldMeta;
        private Text cartCountText, cartStateText, cartAmountText, inventoryTitle, inventoryTotal, inventoryStatus, actionText;
        private Text resultTitle, resultCargo, resultCash, resultSpent, resultStart, resultStove, resultNote, errorText, feedbackText;
        private Text pauseTitle, pauseSubtitle, resumeText;
        private Image heldIcon, actionImage, checkoutProgressFill;
        private Text bagCount,heldFullName;
        private GameObject heldTooltip;
        private string heldArtworkId="",heldDisplayedName="";
        private Button actionButton, cartButton, resumeButton;
        private HUDArcGraphic progress;
        private ShoppingReceiptView shoppingReceipt;
        private GameItemIconLibrary itemIconLibrary;
        private CheckoutSelectionSheet checkoutSelection;
        private MealPlanPanel mealPlan;
        private Button planButton;
        private GameObject beginnerGuideRoot,cartHandlingRoot;
        private Text beginnerTitle,beginnerBody,cartHandlingText;
        private readonly HashSet<GamePhase> dismissedGuides=new HashSet<GamePhase>();
        public CommerceReceiptAudit CaptureShoppingReceipt(bool loaded) => flow?.Run == null ? null : CommerceReceiptData.Shopping(flow.Run,loaded);
        public CommerceReceiptAudit VisibleShoppingReceipt=>shoppingReceipt?.CaptureAudit();
        public int VisibleShoppingProductIconCount=>shoppingReceipt!=null?shoppingReceipt.VisibleProductIconCount:0;
        public void ShowShoppingReceipt(bool loaded=false)
        {if(snapshot==null||snapshot.phase!=GamePhase.Market||snapshot.pause)return;inventoryOpen=true;shoppingReceipt.ShowLoadedPage(loaded);UpdatePanels();}
        private bool inventoryOpen, bagOpen, helpOpen, pausedBeforeHelp, introOpen;
        private string lastMessage = "", lastItemStamp = "";
        private float feedbackUntil;
        public bool Ready => root != null && font != null;
        public void SetGameplayVisible(bool visible)
        { introOpen=false;inventoryOpen=false;bagOpen=false;helpOpen=false;if(canvas!=null)canvas.enabled=visible;enabled=visible; }

        private void Awake()
        {
            introOpen = false;
            font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            if (font == null) Debug.LogError("ArtHUD: bundled Noto Sans CJK SC font is missing.");
            LoadArt();
            BuildCanvas();
            itemIconLibrary=gameObject.AddComponent<GameItemIconLibrary>();
            checkoutSelection=gameObject.AddComponent<CheckoutSelectionSheet>();
            BuildPermanentHUD();
            BuildInventory();
            BuildBagPanel();
            BuildOverlays();
        }

        private void Start() { BindFlow(); }
        private void Update()
        {
            if (flow == null) BindFlow();
            if (flow != null && flow.IsChoosingCheckout) return;
            if(SupplyShop.IsOpen||OnlineProcurementUI.IsOpen)return;
            if(mealPlan!=null&&mealPlan.Visible)return;
            if (Input.GetKeyDown(KeyCode.Tab) && snapshot != null && snapshot.phase == GamePhase.Market && !snapshot.pause)
                ToggleInventory();
            if (Input.GetKeyDown(KeyCode.F1)) ToggleHelp();
            if (feedbackRoot != null && feedbackRoot.activeSelf && Time.unscaledTime >= feedbackUntil)
                feedbackRoot.SetActive(false);
        }

        private void BindFlow()
        {
            GameFlow next = GameFlow.Instance;
            if (next == flow) return;
            if (flow != null) flow.Changed -= Apply;
            flow = next;
            checkoutSelection.Bind(flow);
            if (flow == null) return;
            if(mealPlan==null)mealPlan=MealPlanPanel.Attach(flow,root,font);
            flow.Changed += Apply;
            if (flow.State != null) Apply(flow.State);
            if (introOpen && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        private void OnDestroy()
        {
            if (flow != null) flow.Changed -= Apply;
            foreach (Sprite sprite in ownedSprites) if (sprite != null) Destroy(sprite);
        }

        private void LoadArt()
        {
            foreach (string name in new[] { "timer", "held", "satchel", "receipt", "button" })
                sprites[name] = LoadSprite("UI/Skins/" + name, name == "receipt" || name == "button");
            foreach (string name in new[] { "B04", "rice", "meat", "egg" })
                sprites[name] = LoadSprite("UI/Icons/" + name, false);
            Texture2D cloth = Resources.Load<Texture2D>("UI/Skins/held");
            if (cloth != null)
            {
                Rect area = new Rect(cloth.width * .42f, cloth.height * .25f, cloth.width * .51f, cloth.height * .48f);
                Sprite tile = Sprite.Create(cloth, area, new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect,
                    new Vector4(area.width * .08f, area.height * .12f, area.width * .08f, area.height * .12f));
                ownedSprites.Add(tile); sprites["cloth"] = tile;
            }
        }

        private Sprite LoadSprite(string path, bool sliced)
        {
            Texture2D texture = Resources.Load<Texture2D>(path);
            if (texture == null) { Debug.LogError("ArtHUD: missing art " + path); return null; }
            Vector4 border = Vector4.zero;
            if (sliced && path.EndsWith("receipt", StringComparison.Ordinal))
                border = new Vector4(texture.width * .1036f, texture.height * .1381f, texture.width * .1036f, texture.height * .3223f);
            else if (sliced)
                border = new Vector4(texture.width * .0921f, texture.height * .2486f, texture.width * .0921f, texture.height * .2486f);
            Sprite sprite = Sprite.Create(texture, new Rect(0,0,texture.width,texture.height), new Vector2(.5f,.5f),
                100, 0, SpriteMeshType.FullRect, border);
            sprite.name = path; ownedSprites.Add(sprite); return sprite;
        }

        private void BuildCanvas()
        {
            GameObject go = new GameObject("ArtHUD_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false); root = go.GetComponent<RectTransform>(); canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100; canvas.pixelPerfect = true;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = .5f; scaler.referencePixelsPerUnit = 100;
            if (EventSystem.current == null)
                new GameObject("HUD_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private void BuildPermanentHUD()
        {
            RectTransform homeGuide = CreationMenuArt.Panel(root, "Home preparation", 24, 22, 617, 144, CreationMenuArt.Sheet.Navy);
            homeGuideRoot = homeGuide.gameObject;
            locationText = Label(homeGuide, "PhaseTitle", 26, 12, 440, 54, 34, Cream, FontStyle.Bold);
            objectiveText = Label(homeGuide, "Objective", 27, 71, 564, 53, 27, Cream);
            TextButton(homeGuide, "Help", 475, 20, 108, 45, "操作 · F1", 23, Cream, ToggleHelp);
            BuildMarketGuide();
            planButton=SkinButton(root,"Preparation plan",24,180,260,72,"备货单",()=>{if(mealPlan!=null){inventoryOpen=bagOpen=helpOpen=false;mealPlan.Show();UpdatePanels();}});
            RectTransform guide=CreationMenuArt.Panel(root,"First steps",24,269,596,155,CreationMenuArt.Sheet.Paper);
            beginnerGuideRoot=guide.gameObject;
            beginnerTitle=Label(guide,"Guide title",23,12,490,43,27,Ink,FontStyle.Bold);
            beginnerBody=Label(guide,"Guide step",23,57,546,83,24,Ink);
            TextButton(guide,"Dismiss step",542,11,34,38,"×",28,Ink,()=>{if(snapshot!=null)dismissedGuides.Add(snapshot.phase);guide.gameObject.SetActive(false);});
            RectTransform handling=CreationMenuArt.Panel(root,"Cart handling",300,180,340,72,CreationMenuArt.Sheet.Card);
            cartHandlingRoot=handling.gameObject;
            handling.GetComponent<Image>().raycastTarget=false;
            cartHandlingText=Label(handling,"Handling state",14,10,312,52,24,Ink,FontStyle.Bold,TextAnchor.MiddleCenter);

            RectTransform timer = Corner(root, "EnamelTimer", new Vector2(1,1), new Vector2(1,1), new Vector2(-24,-24), new Vector2(440,147));
            timerRoot = timer.gameObject; Art(timer, "timer", 0,0,440,147);
            RectTransform ringTrack = Rect(timer, "TimerTrack", 57,29,104,104);
            HUDArcGraphic track = ringTrack.gameObject.AddComponent<HUDArcGraphic>(); track.color = new Color(200/255f,148/255f,61/255f,.24f); track.Fill = 1;
            RectTransform ring = Rect(timer, "LiveTimerArc", 57,29,104,104);
            progress = ring.gameObject.AddComponent<HUDArcGraphic>(); progress.color = Gold;
            timerInside = Label(timer, "TimerMeaning", 73,62,72,38,19,Ink,FontStyle.Bold,TextAnchor.MiddleCenter); SingleLine(timerInside);
            timerTitle = Label(timer, "TimerPhase", 176,42,220,36,20,Ink,FontStyle.Bold); SingleLine(timerTitle);
            // Noto CJK's ascender/descender span is 1.448 em. A truncated 64 px
            // box cannot fit the previous 46 px line; UGUI drops that entire line.
            timerValue = Label(timer, "TimerValue", 174,66,220,66,42,Ink,FontStyle.Bold); SingleLine(timerValue);
            RectTransform opening = Rect(timer,"OpeningTimeTag",109,146,316,58); openingRoot=opening.gameObject;
            Art(opening,"button",0,0,316,58,true,5);
            openingText = Label(opening,"StartTime",18,8,280,40,21,Ink,FontStyle.Bold,TextAnchor.MiddleCenter); SingleLine(openingText);

            RectTransform held = Corner(root,"HeldItem",Vector2.zero,Vector2.zero,new Vector2(25,25),new Vector2(490,163.333f));
            heldRoot = held.gameObject; Image heldFace=Art(held,"held",0,0,490,163.333f);heldFace.raycastTarget=true;
            heldIcon = Art(held,"B04",17,17,147,122);
            // Keep both lines and the action hint inside the opaque cloth,
            // above its lower stitched edge (the texture has transparent margins).
            heldTitle = Label(held,"HeldTitle",196,32,262,60,24,Cream,FontStyle.Bold);
            heldMeta = Label(held,"HeldMeta",196,96,262,26,18,Cream); SingleLine(heldMeta);
            RectTransform full=Corner(root,"HeldItemFullName",Vector2.zero,Vector2.zero,new Vector2(25,204),new Vector2(490,116));
            heldTooltip=full.gameObject;Image namePaper=Flat(full,"NamePaper",0,0,490,116,Cream);Outline(namePaper.gameObject,Ink,1);
            heldFullName=Label(full,"CompleteItemName",20,12,450,92,23,Ink,FontStyle.Bold);
            heldFullName.resizeTextForBestFit=true;heldFullName.resizeTextMinSize=18;heldFullName.resizeTextMaxSize=23;heldTooltip.SetActive(false);
            var heldHover=held.gameObject.AddComponent<HUDPointerReveal>();heldHover.entered=()=>{if(snapshot!=null&&!snapshot.pause&&heldRoot.activeSelf){heldTooltip.SetActive(true);heldTooltip.transform.SetAsLastSibling();}};heldHover.exited=()=>{if(heldTooltip!=null)heldTooltip.SetActive(false);};

            RectTransform cart = CreationMenuArt.Panel(root, "CartReceipt", 0, 0, 408, 235, CreationMenuArt.Sheet.Paper);
            cart.anchorMin = cart.anchorMax = Vector2.zero;
            cart.pivot = Vector2.zero;
            cart.anchoredPosition = new Vector2(26, 24);
            cartRoot = cart.gameObject;
            Image cartArt = cart.GetComponent<Image>();
            cartButton = AddButton(cart, cartArt, ToggleInventory);
            Label(cart, "CartTitle", 24, 10, 153, 47, 29, Ink, FontStyle.Bold).text = "购物车";
            cartCountText = Label(cart, "CartSlots", 207, 10, 171, 47, 24, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            cashText = Label(cart, "CashValue", 24, 57, 355, 35, 22, Muted);
            cashRoot = cashText.gameObject;
            Flat(cart, "ReceiptRule", 24, 96, 355, 1, CampaignUI.WithAlpha(Ink, .23f));
            for (int i = 0; i < receiptIcons.Length; i++)
            {
                receiptIcons[i] = Art(cart, "rice", 27 + i * 73, 102, 64, 49);
                receiptIcons[i].enabled = false;
            }
            receiptEmptyText = Label(cart, "EmptyBasket", 24, 101, 350, 45, 22, Muted);
            cartStateText = Label(cart, "CartState", 24, 151, 170, 43, 23, Rust, FontStyle.Bold);
            cartAmountText = Label(cart, "CartAmount", 188, 144, 190, 54, 31, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            receiptKey = Label(cart, "CartKey", 24, 194, 354, 35, 21, Muted);
 
            RectTransform bag = Corner(root,"FoldedSatchel",new Vector2(1,0),new Vector2(1,0),new Vector2(0,18),new Vector2(285,190));
            bagRoot = bag.gameObject; Image bagArt = Art(bag,"satchel",0,0,285,190); bagArt.raycastTarget = true;
            AddButton(bag,bagArt,()=>{ bagOpen = !bagOpen; UpdatePanels(); });
            // The bundled CJK font needs ~1.448 em; 35px at 25px font
            // dropped the entire line. Give the count its own readable paper.
            RectTransform countTag=Rect(bag,"SatchelCountPaper",136,125,100,49);Image countPaper=Flat(countTag,"Paper",0,0,100,49,Cream);Outline(countPaper.gameObject,Ink,1);
            bagCount=Label(countTag,"SatchelCount",6,3,88,43,23,Ink,FontStyle.Bold,TextAnchor.MiddleCenter);SingleLine(bagCount);bagCount.text="0 / 6";

            RectTransform action = Corner(root,"NearbyAction",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,46),new Vector2(590,94));
            actionRoot = action.gameObject; actionImage = Art(action,"button",0,0,590,94,true,5); actionImage.color=Rust;
            actionImage.raycastTarget = true; actionButton = AddButton(action,actionImage,()=>flow?.PrimaryAction());
            var keycap = CreationMenuArt.Panel(action, "Interact key", 40, 23, 58, 49, CreationMenuArt.Sheet.Navy);
            Label(keycap, "Interact key text", 2, 2, 54, 44, 28, Cream, FontStyle.Bold, TextAnchor.MiddleCenter).text = "E";
            actionText = Label(action, "NearbyActionText", 118, 9, 420, 70, 29, Cream, FontStyle.Bold);
            SingleLine(actionText);
            Image checkoutTrack = Flat(action,"CheckoutProgress",48,77,494,5,new Color(35/255f,56/255f,77/255f,.22f));
            checkoutProgressRoot = checkoutTrack.gameObject;
            checkoutProgressFill = Flat(checkoutTrack.rectTransform,"ServiceProgress",0,0,0,5,Gold);
            checkoutProgressRoot.SetActive(false);
            RectTransform feedback = Corner(root,"Feedback",new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,154),new Vector2(820,101));
            feedbackRoot = feedback.gameObject;
            RectTransform feedbackPlate = CreationMenuArt.Panel(feedback, "Prompt paper", 0, 0, 820, 101, CreationMenuArt.Sheet.Paper);
            feedbackPlate.GetComponent<Image>().raycastTarget = false;
            feedbackText = Label(feedbackPlate, "FeedbackText", 30, 8, 760, 85, 25, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            feedbackRoot.SetActive(false);
            actionRoot.SetActive(false);
        }

        private void BuildMarketGuide()
        {
            RectTransform guide = CreationMenuArt.Panel(root, "MarketPickList", 24, 22, 617, 144, CreationMenuArt.Sheet.Paper);
            guide.GetComponent<Image>().raycastTarget = false;
            marketGuideRoot = guide.gameObject;
            Text title = Label(guide, "MarketTitle", 26, 12, 430, 54, 34, Ink, FontStyle.Bold);
            title.text = "惠民仓储超市";
            TextButton(guide, "MarketHelp", 475, 20, 108, 45, "操作 · F1", 23, Ink, ToggleHelp);
            marketObjective = Label(guide, "MarketNext", 27, 71, 564, 53, 27, Ink);
            marketGuideRoot.SetActive(false);
        }

        private void UpdateMarketGuide(Snapshot state)
        {
            marketGuideRoot.SetActive(state.phase == GamePhase.Market);
            if (state.phase != GamePhase.Market) return;
            if (state.checkoutInProgress) marketObjective.text = "收银中 · 留在柜台";
            else if (state.paid) marketObjective.text = "出门到摊车装货";
            else if (state.cartCount > 0) marketObjective.text = "去收银台 · 勾选要付的货";
            else if (state.loaded) marketObjective.text = "出门选摊位";
            else marketObjective.text = "挑货 · 整箱上货架，小份到采购柜台";
        }

        private void BuildInventory()
        {
            inventoryPanel=new GameObject("OpenShoppingReceipt");
            shoppingReceipt=inventoryPanel.AddComponent<ShoppingReceiptView>();
            shoppingReceipt.Initialize(root,font,ToggleInventory);
            inventoryPanel.SetActive(false);
        }

        private void BuildBagPanel()
        {
            RectTransform panel = Corner(root,"OpenSatchel",new Vector2(1,0),new Vector2(1,0),new Vector2(-43,224),new Vector2(259,246));
            bagPanel = panel.gameObject; Art(panel,"cloth",0,0,259,246,true,5);
            Label(panel,"BagTitle",22,16,174,38,22,Cream,FontStyle.Bold).text = "随身挎包";
            TextButton(panel,"CloseBag",211,14,34,38,"×",27,Cream,()=>{bagOpen=false;UpdatePanels();});
            for (int i=0;i<6;i++)
            {
                Image cell=Flat(panel,"SatchelSlot_"+i,24+i%3*72,68+i/3*64,63,55,new Color(35/255f,56/255f,77/255f,.65f));
                Outline(cell.gameObject,new Color(200/255f,148/255f,61/255f,.6f),1);
            }
            Label(panel,"BagStatus",21,201,217,36,18,Cream,FontStyle.Normal,TextAnchor.MiddleCenter).text="空着 · 0 / 6 格";
            bagPanel.SetActive(false);
        }

        private void BuildOverlays()
        {
            RectTransform shade = Stretch(root,"ModalShade"); modalShade=shade.gameObject;
            Image dim=shade.gameObject.AddComponent<Image>(); dim.color=new Color(222/255f,216/255f,205/255f,.60f); dim.raycastTarget=true;
            modalShade.SetActive(false);
            RectTransform pause=PaperModal("Pause",600,420);pausePanel=pause.gameObject;
            pauseTitle=Label(pause,"PauseTitle",54,57,492,60,34,Ink,FontStyle.Bold,TextAnchor.MiddleCenter); SingleLine(pauseTitle);
            pauseSubtitle=Label(pause,"PauseSubtitle",54,127,492,100,21,Ink,FontStyle.Normal,TextAnchor.MiddleCenter);
            pauseSubtitle.verticalOverflow=VerticalWrapMode.Overflow;
            resumeButton=SkinButton(pause,"Resume",148,240,304,90,"继续准备",ResumeOrBegin);
            resumeText=resumeButton.GetComponentInChildren<Text>();
            TextButton(pause,"PauseHelp",165,350,270,35,"查看操作说明",21,Ink,ToggleHelp);

            RectTransform help=PaperModal("ControlHelp",640,570);helpPanel=help.gameObject;
            Label(help,"HelpTitle",55,65,530,54,32,Ink,FontStyle.Bold).text="准备出摊 · 操作说明";
            Label(help,"HelpBody",55,137,530,298,23,Ink).text=
                "空手：WASD 移动 · Shift 跑步\n推车：W 前推 · S 后退 · A/D 转向\nF　接手 / 停放推车\nE　拿取、放下、收银或装车\nR　放回未付商品 · Tab　购物明细\nM　市场地图 · Esc　暂停\n备货单可记菜、查缺料、定位货架。\n按需挑选结账，付款后到车边装载。";
            SkinButton(help,"CloseHelp",168,446,304,90,"知道了",ToggleHelp);

            RectTransform result=PaperModal("PreparationResult",580,630);completePanel=result.gameObject;
            Label(result,"ResultEyebrow",54,53,474,28,17,Muted,FontStyle.Bold).text="出摊准备 · 本轮记录";
            resultTitle=Label(result,"ResultTitle",54,87,474,54,31,Ink,FontStyle.Bold);
            resultCargo=ResultRow(result,"已装原料",160);
            resultCash=ResultRow(result,"剩余现金",213);
            resultSpent=ResultRow(result,"实际支出",266);
            resultStart=ResultRow(result,"预计开摊",319);
            resultStove=Label(result,"StoveState",54,381,474,37,22,Ink,FontStyle.Bold);
            resultNote=Label(result,"ResultScope",54,427,474,65,19,Muted);
            SkinButton(result,"RestartRun",126,511,328,94,"重新开始",Restart);

            RectTransform error=PaperModal("Error",620,470);errorPanel=error.gameObject;
            Label(error,"ErrorTitle",56,76,506,55,30,Rust,FontStyle.Bold).text="场景暂时无法继续";
            errorText=Label(error,"ErrorDetails",56,148,506,171,20,Ink); errorText.verticalOverflow=VerticalWrapMode.Truncate;
            SkinButton(error,"RestartAfterError",151,344,318,92,"重新开始",Restart);
            pausePanel.SetActive(false); helpPanel.SetActive(false); completePanel.SetActive(false); errorPanel.SetActive(false);
        }

        private void Apply(Snapshot next)
        {
            if(next==null || root==null)return;
            // SetPaused publishes synchronously, before GameFlow's Update ticks.
            // This also prevents Escape from starting the clock behind the intro.
            if(introOpen && next.phase==GamePhase.Home && !next.pause)
            { flow.SetPaused(true); return; }
            if(next.phase!=GamePhase.Home)introOpen=false;
            bool phaseChanged=snapshot==null || snapshot.phase!=next.phase;
            Snapshot previous=snapshot;
            snapshot=next;
            if(phaseChanged){inventoryOpen=false;bagOpen=false;lastItemStamp="";}
            bool home=next.phase==GamePhase.Home, market=next.phase==GamePhase.Market;
            homeGuideRoot.SetActive(home || next.phase == GamePhase.Error);
            objectiveText.gameObject.SetActive(home);
            UpdateMarketGuide(next);
            locationText.text=home?"第 "+next.dayIndex+" 天 · 清晨整备":next.phase==GamePhase.Error?"准备中断":"惠民仓储超市";
            objectiveText.text = !string.IsNullOrEmpty(next.heldItem) ? "抱去门外，放上摊车"
                : next.stoveLoaded ? "家当上车了，走到门口出发" : "把今天要用的家当搬上摊车";
            int seconds=Mathf.CeilToInt(Mathf.Max(0,next.remaining));
            timerValue.text=$"{seconds/60:00}:{seconds%60:00}";
            timerTitle.text=home?"清晨 · 整备":market?"采购剩余":"采购结束";
            bool urgent=seconds<=10 && (home||market);
            timerValue.color=urgent?Rust:Ink; timerInside.text=seconds==0?"时间到":urgent?"抓紧":"剩余";timerInside.color=urgent?Rust:Ink;
            progress.Fill=Mathf.Clamp01(next.remaining/(home?60f:120f)); progress.color=urgent?Rust:Gold;
            openingRoot.SetActive(market);
            openingText.text="预计开摊  "+next.startTime;
            cashRoot.SetActive(!home);cashText.text="随身现金   "+Money(next.cash);
            bool holding=!string.IsNullOrEmpty(next.heldItem);
            // 空手不展示持物卡：装车完成状态由引导条、反馈与结算页表达。
            heldRoot.SetActive(holding);
            heldIcon.enabled=holding;
            if(holding&&heldArtworkId!=next.heldItem){heldArtworkId=next.heldItem;heldIcon.sprite=itemIconLibrary.Get(next.heldItem);}
            if(!holding)heldArtworkId="";
            string fullName=holding?(GameCatalog.Find(next.heldItem)?.name??CharacterCatalog.EquipmentName(next.heldItem)):next.stoveLoaded?"核心家当已装车":"双手空着";
            if(fullName!=heldDisplayedName){heldDisplayedName=fullName;FitHeldTitle(fullName);heldFullName.text=fullName;}
            heldMeta.text=holding?(home?"抱持中 · 慢行":"抱持中 · 回篮按 E 放下"):"双手空出 · 可以出发";
            if(!heldRoot.activeSelf||next.pause)heldTooltip.SetActive(false);
            cartCountText.text=$"{next.cartCellsUsed}/{next.cartCapacity} 格";
            bool basketPresent=next.cartCount>0;
            string[] basketIds=basketPresent?(next.itemIds??Array.Empty<string>()):flow?.Run?.Cargo.ToArray()??Array.Empty<string>();
            for(int i=0;i<receiptIcons.Length;i++)
            {
                bool occupied=i<basketIds.Length;receiptIcons[i].enabled=occupied;
                if(occupied)receiptIcons[i].sprite=itemIconLibrary.Get(basketIds[i]);
            }
            receiptEmptyText.gameObject.SetActive(basketIds.Length==0);
            receiptEmptyText.text = "购物车空着";
            cartStateText.text=basketPresent?(next.paid?"本批已付":"本批待付"):next.loaded?"已付已装车":"尚未取货";
            receiptKey.text = basketPresent ? "Tab 查看明细" : "Tab 查看已装车";
            cartStateText.color=next.paid||!basketPresent&&next.loaded?Ink:Rust;
            cartAmountText.text=Money(basketPresent?next.cartTotal:next.amountSpent);
            cartButton.interactable=market&&!next.pause;
            bool showAction=(home||market)&&next.canInteract&&!next.pause&&!helpOpen&&(next.primaryEnabled||next.checkoutInProgress||!string.IsNullOrEmpty(next.actionBlockedReason));
            actionRoot.SetActive(showAction);
            actionButton.interactable=next.primaryEnabled;
            string targetName="";
            foreach(ItemDefinition item in GameCatalog.Items)if(item.anchor==next.targetId){targetName=ShortName(item.id)+" · ";break;}
            actionText.text=next.checkoutInProgress
                ? "人工收银　"+Mathf.Max(0,next.checkoutRemaining).ToString("0.0",CultureInfo.InvariantCulture)+" 秒"
                : !string.IsNullOrEmpty(next.actionBlockedReason)?next.actionBlockedReason:targetName + next.contextPrompt;
            bool longAction=actionText.text.Length>14;
            actionText.horizontalOverflow=HorizontalWrapMode.Wrap;
            actionText.verticalOverflow=VerticalWrapMode.Truncate;
            actionText.resizeTextForBestFit=false;
            actionText.fontSize=longAction?23:29;
            string itemStamp=string.Join("|",next.itemIds??Array.Empty<string>())+":"+next.paid+":"+next.targetId+":"+next.loaded+":"+next.primaryEnabled+":"+next.checkoutInProgress;
            if(itemStamp!=lastItemStamp){lastItemStamp=itemStamp;UpdateInventory(next);}
            UpdateCheckoutPresentation(next);
            if(!string.IsNullOrEmpty(next.message)&&next.message!=lastMessage)
            {
                lastMessage=next.message;
                string feedback=CompactFeedback(next,previous,phaseChanged);
                feedbackText.text=feedback;feedbackUntil=string.IsNullOrEmpty(feedback)?0:Time.unscaledTime+Mathf.Clamp(2.5f+feedback.Length*.045f,3f,5f);
                if(!string.IsNullOrEmpty(feedback))Reveal(feedbackRoot);
            }
            if(next.phase==GamePhase.Complete)
            {
                resultTitle.text=next.stoveLoaded?"本轮出摊准备完成":"本轮整备结束";
                resultCargo.text=next.loadedCount+" 箱";resultCash.text=Money(next.cash);resultSpent.text=Money(next.amountSpent);resultStart.text=next.startTime;
                resultStove.text=next.stoveLoaded?"B04 灶台 · 已装车":"B04 灶台 · 仍在家中";
                resultStove.color=next.stoveLoaded?Ink:Rust;
                resultNote.text=(next.timeoutSettlement?"采购时间到，已按本轮条件代结装车。\n":"")+"本次采购结束，可重新创建出身档案。";
                feedbackRoot.SetActive(false);
            }
            if(next.phase==GamePhase.Error)errorText.text=next.message;
            if(helpOpen&&!next.pause)helpOpen=false;
            UpdateBeginnerGuide(next);
            cartHandlingRoot.SetActive(market&&!next.pause&&(next.pushingCart||next.canToggleCart));
            cartHandlingText.text=next.cartPrompt;
            UpdatePanels();
        }

        private void UpdateBeginnerGuide(Snapshot state)
        {
            bool visible=(state.phase==GamePhase.Home||state.phase==GamePhase.Market)&&!state.pause&&!dismissedGuides.Contains(state.phase);
            beginnerGuideRoot.SetActive(visible);if(!visible)return;
            if(state.phase==GamePhase.Home)
            {
                beginnerTitle.text="先看看今天的家当";
                beginnerBody.text=!string.IsNullOrEmpty(state.heldItem)?"抱到门外摊车前，按 E 放上车。":flow.Run.HasUnloadedEquipment?"想做什么，可先记在备货单里。\n带上今天用得着的设备。":"家当已在车上。可看备货单，\n也可直接出门挑想买的食材。";
            }
            else if(!string.IsNullOrEmpty(state.heldItem))
            {beginnerTitle.text="把手里的货放好";beginnerBody.text="走到购物车旁按 E 放入。\n不想买了，可回原货架按 E 放回。";}
            else if(state.paid&&state.cartCount>0)
            {beginnerTitle.text="这批已经付过钱";beginnerBody.text="到门外摊车旁按 E 装货。\nM 打开地图看装车点。";}
            else if(state.cartCount>0)
            {beginnerTitle.text="只买这次想要的";beginnerBody.text="到收银台按 E，勾选要付款的货。\n没勾的可以放回，不用全买。";}
            else
            {beginnerTitle.text="按自己的想法挑货";beginnerBody.text="备货单能查缺料、标记采购位置。\nM 看地图，也可以自由逛逛。";}
        }

        private void FitHeldTitle(string value)
        {
            heldTitle.horizontalOverflow=HorizontalWrapMode.Wrap;heldTitle.verticalOverflow=VerticalWrapMode.Truncate;
            heldTitle.text=value;heldTitle.fontSize=24;
            while(heldTitle.fontSize>20&&heldTitle.preferredHeight>heldTitle.rectTransform.rect.height)heldTitle.fontSize--;
            // Preserve the full name in the hover panel, and never draw beyond
            // the cloth if a future equipment name exceeds two readable lines.
            string visible=value;
            while(visible.Length>1&&heldTitle.preferredHeight>heldTitle.rectTransform.rect.height)
            {visible=visible.Substring(0,visible.Length-1);heldTitle.text=visible+"…";}
        }

        private void UpdateCheckoutPresentation(Snapshot s)
        {
            checkoutProgressRoot.SetActive(s.checkoutInProgress);
            if(s.checkoutInProgress)
            {
                float completed=s.checkoutDuration>0?1-Mathf.Clamp01(s.checkoutRemaining/s.checkoutDuration):0;
                checkoutProgressFill.rectTransform.sizeDelta=new Vector2(494*completed,5);
            }
            shoppingReceipt.Present(flow,s);
        }

        private void UpdateInventory(Snapshot s) => shoppingReceipt.Present(flow,s);

        private void ToggleInventory(){if(snapshot==null||snapshot.phase!=GamePhase.Market||snapshot.pause)return;inventoryOpen=!inventoryOpen;UpdatePanels();}
        private void ToggleHelp()
        {
            if(flow==null||snapshot==null||snapshot.phase==GamePhase.Error||snapshot.phase==GamePhase.Complete)return;
            if(helpOpen){helpOpen=false;flow.SetPaused(pausedBeforeHelp);}
            else{dismissedGuides.Remove(snapshot.phase);pausedBeforeHelp=snapshot.pause;helpOpen=true;flow.SetPaused(true);}
            UpdatePanels();
        }
        private void Restart(){helpOpen=false;inventoryOpen=false;bagOpen=false;lastItemStamp="";flow?.RestartRun();}
        private void ResumeOrBegin(){introOpen=false;flow?.SetPaused(false);}
        private static bool IsAutomatedRun()
        {
            foreach(string argument in Environment.GetCommandLineArgs())
                foreach(string flag in new[]{"--capture-dir","--smoke-test","--traversal-test"})
                    if(argument==flag||argument.StartsWith(flag+"=",StringComparison.Ordinal))return true;
            return false;
        }
        private void UpdatePanels()
        {
            if(snapshot==null)return;
            bool selecting = flow != null && flow.IsChoosingCheckout;
            root.gameObject.SetActive(!selecting);
            if (selecting)
            {
                inventoryOpen = bagOpen = false;
                return;
            }
            bool market=snapshot.phase==GamePhase.Market,complete=snapshot.phase==GamePhase.Complete,error=snapshot.phase==GamePhase.Error;
            bool planning=mealPlan!=null&&mealPlan.Visible;
            planButton.gameObject.SetActive((snapshot.phase==GamePhase.Home||market)&&!snapshot.pause&&!helpOpen);
            bool wasInventory=inventoryPanel.activeSelf;
            inventoryPanel.SetActive(inventoryOpen&&market&&!snapshot.pause);
            if(inventoryPanel.activeSelf&&!wasInventory)Reveal(inventoryPanel);
            cartRoot.SetActive((market||complete)&&!inventoryPanel.activeSelf&&!snapshot.checkoutInProgress&&string.IsNullOrEmpty(snapshot.heldItem));
            // Satchel accounting is not connected; never show a fabricated 0/6.
            bagPanel.SetActive(false);bagRoot.SetActive(false);
            completePanel.SetActive(complete);errorPanel.SetActive(error);helpPanel.SetActive(helpOpen&&!complete&&!error);
            bool campaignPause=snapshot.pause&&!planning&&!introOpen&&!helpOpen&&!complete&&!error&&(snapshot.phase==GamePhase.Home||market)&&flow.Campaign!=null;
            if(campaignPause)CampaignMenuUI.Open(flow);
            pausePanel.SetActive(snapshot.pause&&!planning&&!helpOpen&&!complete&&!error&&!campaignPause);
            pauseTitle.text=introOpen?"准备出摊":"歇一会儿";
            pauseSubtitle.text=introOpen
                ? "天亮了，先把灶台搬上车。\n开始后有 60 秒整备，别落下家当。"
                : "已暂停，准备时间不会流逝";
            resumeText.text=introOpen?"开始整备":"继续准备";
            modalShade.SetActive(!planning&&(complete||error||snapshot.pause||helpOpen));
            if(snapshot.pause||complete||error||inventoryPanel.activeSelf){beginnerGuideRoot.SetActive(false);cartHandlingRoot.SetActive(false);}
            feedbackRoot.SetActive(Time.unscaledTime<feedbackUntil&&!snapshot.pause&&!helpOpen&&!complete&&!error&&!inventoryPanel.activeSelf&&!snapshot.checkoutInProgress);
            if(snapshot.pause||complete||error)actionRoot.SetActive(false);
        }

        private static string CompactFeedback(Snapshot next,Snapshot previous,bool phaseChanged)
        {
            if(phaseChanged||previous==null)return "";
            if(next.stoveLoaded&&!previous.stoveLoaded)return "灶台已装车";
            if(next.heldItem!=previous.heldItem&&!string.IsNullOrEmpty(next.heldItem))return "已抱起 "+ShortName(next.heldItem);
            if(next.loaded&&!previous.loaded)return "装车完成";
            if(next.paid&&!previous.paid)return "付款 −"+Money(previous.cash-next.cash);
            if(next.checkoutInProgress&&!previous.checkoutInProgress)return "人工收银中";
            if(next.cartCount>previous.cartCount)
                foreach(string id in next.itemIds)
                    if(Array.IndexOf(previous.itemIds??Array.Empty<string>(),id)<0)return "+ "+ShortName(id)+" ×1";
            if(next.cartCount<previous.cartCount&&!next.paid)return "已放回货架";
            string message=next.message??"";
            return message;
        }
        private static void Pop(GameObject target)
        {if(target!=null)(target.GetComponent<HUDSoftMotion>()??target.AddComponent<HUDSoftMotion>()).Play(false);}
        private static void Reveal(GameObject target)
        {if(target!=null)(target.GetComponent<HUDSoftMotion>()??target.AddComponent<HUDSoftMotion>()).Play(true);}

        private RectTransform PaperModal(string name,float width,float height)
        {
            RectTransform rect=Corner(root,name,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(width,height));
            Art(rect,"receipt",0,0,width,height,true,5.5f);return rect;
        }
        private Text ResultRow(RectTransform parent,string title,float top)
        {
            Label(parent,title+"_Label",54,top,250,38,22,Ink).text=title;
            return Label(parent,title+"_Value",303,top,223,38,25,Ink,FontStyle.Bold,TextAnchor.MiddleRight);
        }
        private Button SkinButton(RectTransform parent,string name,float x,float y,float w,float h,string title,Action click)
        {
            RectTransform rect=Rect(parent,name,x,y,w,h);Image art=Art(rect,"button",0,0,w,h,true,5);art.color=Rust;art.raycastTarget=true;
            Button button=AddButton(rect,art,click);Label(rect,"Label",20,8,w-40,h-16,24,Cream,FontStyle.Bold,TextAnchor.MiddleCenter).text=title;return button;
        }
        private Button TextButton(RectTransform parent,string name,float x,float y,float w,float h,string title,int size,Color color,Action click)
        {
            RectTransform rect=Rect(parent,name,x,y,w,h);Text text=Label(rect,"Label",0,0,w,h,size,color,FontStyle.Normal,TextAnchor.MiddleCenter);
            text.text=title;text.color=Color.white;text.raycastTarget=true;SingleLine(text);Button button=AddButton(rect,text,click);
            ColorBlock colors=button.colors;colors.normalColor=color;colors.highlightedColor=Gold;colors.selectedColor=Gold;colors.pressedColor=Rust;colors.disabledColor=Muted;button.colors=colors;
            return button;
        }
        private static Button AddButton(RectTransform rect,Graphic graphic,Action click)
        {
            Button button=rect.gameObject.AddComponent<Button>();button.targetGraphic=graphic;
            ColorBlock colors=CampaignUI.ButtonColours(button.colors);colors.fadeDuration=.08f;button.colors=colors;
            button.onClick.AddListener(()=>click?.Invoke());return button;
        }
        private Image Art(RectTransform parent,string key,float x,float y,float w,float h,bool sliced=false,float density=1)
        {
            RectTransform rect=Rect(parent,key+"_Art",x,y,w,h);Image image=rect.gameObject.AddComponent<Image>();
            sprites.TryGetValue(key,out Sprite sprite);image.sprite=sprite;image.raycastTarget=false;
            image.type=sliced?Image.Type.Sliced:Image.Type.Simple;image.preserveAspect=!sliced;image.pixelsPerUnitMultiplier=density;return image;
        }
        private static Image Flat(RectTransform parent,string name,float x,float y,float w,float h,Color color)
        {
            Image image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;
        }
        private Text Label(RectTransform parent,string name,float x,float y,float w,float h,int size,Color color,FontStyle style=FontStyle.Normal,TextAnchor align=TextAnchor.MiddleLeft)
        {
            Text text=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Text>();text.font=font;text.fontSize=size;text.color=color;text.fontStyle=style;text.alignment=align;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;text.raycastTarget=false;text.supportRichText=false;text.lineSpacing=1f;return text;
        }
        private static void SingleLine(Text text)
        {text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;}
        private static RectTransform Rect(RectTransform parent,string name,float x,float y,float w,float h)
        {return Corner(parent,name,new Vector2(0,1),new Vector2(0,1),new Vector2(x,-y),new Vector2(w,h));}
        private static RectTransform Corner(RectTransform parent,string name,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
        {
            RectTransform rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
        }
        private static RectTransform Stretch(RectTransform parent,string name)
        {
            RectTransform rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return rect;
        }
        private static void ShadowText(Text text)
        {Shadow shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(35/255f,56/255f,77/255f,.78f);shadow.effectDistance=new Vector2(0,-1);}
        private static void Outline(GameObject go,Color color,float width)
        {UnityEngine.UI.Outline outline=go.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=color;outline.effectDistance=new Vector2(width,-width);}
        private static string Money(float amount)=>"¥"+amount.ToString("0.00",CultureInfo.InvariantCulture);
        private static string ShortName(string id)=>id=="rice"?"米面主食":id=="meat"?"鲜肉海鲜":id=="egg"?"鸡蛋青菜":GameCatalog.Find(id)?.name??CharacterCatalog.EquipmentName(id);
        private static Color Hex(string hex){ColorUtility.TryParseHtmlString("#"+hex,out Color color);return color;}
    }

    // Hover only changes presentation, never selects orders or submits actions.
    public sealed class HUDPointerReveal : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler
    {
        public Action entered,exited;
        public void OnPointerEnter(PointerEventData data)=>entered?.Invoke();
        public void OnPointerExit(PointerEventData data)=>exited?.Invoke();
        private void OnDisable()=>exited?.Invoke();
    }

    // Small event-triggered UI motion. Ends at exact rest; never touches simulation time.
    public sealed class HUDSoftMotion : MonoBehaviour
    {
        private RectTransform rect;
        private CanvasGroup group;
        private float elapsed=1;
        private bool reveal;
        private Vector2 rest;
        public void Play(bool appearing)
        {
            if(rect==null){rect=GetComponent<RectTransform>();rest=rect.anchoredPosition;group=GetComponent<CanvasGroup>()??gameObject.AddComponent<CanvasGroup>();}
            elapsed=0;reveal=appearing;
        }
        private void Update()
        {
            if(rect==null||elapsed>=.24f)return;
            elapsed=Mathf.Min(.24f,elapsed+Time.unscaledDeltaTime);float t=elapsed/.24f;
            float settle=1-Mathf.Pow(1-t,3);
            rect.anchoredPosition=rest+(reveal?Vector2.down*10*(1-settle):Vector2.zero);
            rect.localScale=Vector3.one*(1+(reveal?-.025f:.025f)*Mathf.Sin(Mathf.PI*t));
            if(group!=null)group.alpha=reveal?Mathf.Lerp(.35f,1,settle):1;
            if(t>=1){rect.anchoredPosition=rest;rect.localScale=Vector3.one;if(group!=null)group.alpha=1;}
        }
        private void OnDisable()
        {if(rect!=null){rect.anchoredPosition=rest;rect.localScale=Vector3.one;}if(group!=null)group.alpha=1;elapsed=1;}
    }

    /// <summary>A live mesh arc over the blank hand-painted timer face; never bakes time into art.</summary>
    public sealed class HUDArcGraphic : MaskableGraphic
    {
        [SerializeField,Range(0,1)] private float fill=1;
        public float Fill { get=>fill;set{float next=Mathf.Clamp01(value);if(Mathf.Abs(fill-next)>.0001f){fill=next;SetVerticesDirty();}} }
        protected override void Awake(){base.Awake();raycastTarget=false;}
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();if(fill<=0)return;
            Rect bounds=rectTransform.rect;Vector2 center=bounds.center;float radius=Mathf.Min(bounds.width,bounds.height)*.365f;
            const float thickness=2.8f;int count=Mathf.Max(1,Mathf.CeilToInt(96*fill));
            for(int i=0;i<=count;i++)
            {
                float angle=(90-360*fill*i/count)*Mathf.Deg2Rad;Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                helper.AddVert(center+direction*(radius-thickness*.5f),color,Vector2.zero);
                helper.AddVert(center+direction*(radius+thickness*.5f),color,Vector2.zero);
                if(i>0){int n=i*2;helper.AddTriangle(n-2,n-1,n);helper.AddTriangle(n,n-1,n+1);}
            }
        }
    }
}
