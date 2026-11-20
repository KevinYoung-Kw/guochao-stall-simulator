using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Guochao
{
    // HUD readouts follow the objects they describe. The scene and controller
    // own interaction; these labels never become a second cooking simulation.
    public sealed partial class BusinessWorldHUD : MonoBehaviour
    {
        private readonly Color ink=CampaignUI.Ink,paper=CampaignUI.PaperColor,progressColour=CampaignUI.Gold,danger=CampaignUI.Rust;
        private readonly Dictionary<StallInteractionTarget,Tag> tags=new Dictionary<StallInteractionTarget,Tag>();
        private BusinessController owner;
        private BusinessSession session;
        private Font font;
        private RectTransform canvasRect,safe,worldLayer,toast,packageCursor,overlay;
        private Text clock,cash,served,hint,cursorText,overlayBody,activeWork,pendingWork;
        private RectTransform workRibbon,serviceNote;
        private RectTransform objectFocus;
        private Text serviceText;
        private Button cancelPending;
        private int previousPending;
        private Sprite rounded;
        private Texture2D roundedTexture;
        private Image toastFace;
        private Image cursorDish;
        private int knownOrders=-1,knownLanes=-1;
        private StallInteractionTarget hoveredTag;
        private StallInteractionTarget lastContextTarget;
        private float nextTargetScan;
        private Rect previousSafeArea;
        private Vector2Int previousScreen;
        private int overlayKind;
        private Button menuButton,overlayClose,tipButton;
        private Text tipLabel;
        private bool? tipPreference;
        private Image headerBlocker,noteBlocker,ribbonBlocker;
        private GameObject focusBeforeOverlay;
        private bool pausedBeforeOverlay;
        private static BusinessWorldHUD activeOverlay;
        private RectTransform overlayRows;
        private ScrollRect overlayScroll;
        private Text overlayTitle;
        private GameItemIconLibrary overlayIcons;
        // 固定安全区操作卡：锅具做法/装餐（焦点或选中锅）、选中台面餐详情。
        private FixedCard lanePanel,mealCard;
        private string lanePanelKey="",mealCardKey="";
        private readonly Dictionary<Button,bool> disabledForOverlay=new Dictionary<Button,bool>();
        // Per-sale payment feedback. The session publishes each completed sale
        // exactly once; the popup pool is bounded, click-through, and fully
        // disposed with this HUD, so pauses, overlays and teardown leak nothing.
        private sealed class SalePop { public RectTransform root;public Text text;public Image coin;public CanvasGroup group;public Vector3 world;public float age;public int lane; }
        private readonly List<SalePop> salePops=new List<SalePop>();
        private const int MaxSalePops=6;
        public int ActiveSaleFeedbackCount=>salePops.Count;
        public float SaleFeedbackAgeTotal { get {float total=0;foreach(SalePop pop in salePops)total+=pop.age;return total;} }
        public bool HasOverlay=>overlay!=null&&overlay.gameObject.activeSelf;
        public static bool TryCloseActiveOverlay()
        {
            if(activeOverlay==null||!activeOverlay.HasOverlay)return false;
            activeOverlay.CloseOverlay();return true;
        }
        public Rect ReadoutScreenRect(StallInteractionTarget target)
        {
            if(target==null||!tags.TryGetValue(target,out Tag tag)||!tag.root.gameObject.activeInHierarchy)return Rect.zero;
            var corners=new Vector3[4];tag.root.GetWorldCorners(corners);Vector2 a=RectTransformUtility.WorldToScreenPoint(null,corners[0]),b=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
            return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        private sealed class Tag
        {
            public StallInteractionTarget target;
            public RectTransform root,tail,leader,portrait;
            public Text title,detail,icon;
            public StallUIIcon dishIcon;
            public Image face,bar,track;
            public Button button;
            public Vector2 size,desired;
            public RectTransform flavorChip;
            public Image flavorFace;
            public Text flavorText;
            public Image[] recipeIngredients;
            public Text recipeMethod;
        }
        public void Initialize(BusinessController controller,BusinessSession state,RectTransform canvas,Font uiFont)
        {
            if(session!=null)session.SaleCompleted-=OnSaleCompleted;
            owner=controller;session=state;font=uiFont;canvasRect=canvas;
            RectTransform host=GetComponent<RectTransform>();Stretch(host);
            rounded=StallUIArt.RoundedPaper(out roundedTexture);
            safe=Node("Safe gameplay HUD",transform);Stretch(safe);
            worldLayer=Node("Labels anchored to actual objects",safe);Stretch(worldLayer);
            BuildHeader();BuildSmallControls();BuildCursor();BuildWorkRibbon();BuildObjectFocus();BuildLanePanel();BuildMealCard();
            if(session!=null)session.SaleCompleted+=OnSaleCompleted;
            toast=Card("Context prompt",safe,new Vector2(590,42),paper,true);Anchor(toast,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,20));
            hint=Text(toast,"",Vector2.zero,new Vector2(558,36),19,ink,TextAnchor.MiddleCenter);toastFace=toast.GetComponent<Image>();toastFace.raycastTarget=false;
            BuildOverlay();BuildOrderGuide();ApplySafeArea();Refresh(false);StartCoroutine(SettleFirstTextLayout());
        }
        private IEnumerator SettleFirstTextLayout()
        {
            // The selection page is destroyed at frame end while this page adds
            // new CJK sizes/styles to the same dynamic font atlas. Rebuild once
            // after that swap; do not wait for the first customer to fix labels.
            yield return null;
            foreach(Text label in GetComponentsInChildren<Text>(true))label.SetAllDirty();
            Canvas.ForceUpdateCanvases();
        }
        private RectTransform Node(string name,Transform parent)
        {GameObject go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();}
        private static void Stretch(RectTransform rt){rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;}
        private static void Anchor(RectTransform rt,Vector2 anchor,Vector2 pivot,Vector2 position)
        {rt.anchorMin=rt.anchorMax=anchor;rt.pivot=pivot;rt.anchoredPosition=position;}
        private RectTransform Card(string name,Transform parent,Vector2 size,Color colour,bool border)
        {
            RectTransform rt=Node(name,parent);rt.sizeDelta=size;
            Image face=rt.gameObject.AddComponent<Image>();face.sprite=rounded;face.type=Image.Type.Sliced;face.color=colour;face.raycastTarget=false;
            if(border)
            {
                Outline outline=rt.gameObject.AddComponent<Outline>();outline.effectColor=CampaignUI.WithAlpha(ink,.95f);outline.effectDistance=new Vector2(1.2f,-1.2f);
                Shadow shadow=rt.gameObject.AddComponent<Shadow>();shadow.effectColor=CampaignUI.WithAlpha(ink,.24f);shadow.effectDistance=new Vector2(0,-3);
            }
            return rt;
        }
        private Text Text(Transform parent,string value,Vector2 position,Vector2 size,int sizePx,Color colour,TextAnchor align=TextAnchor.MiddleLeft)
        {
            RectTransform rt=Node("Readout",parent);rt.anchoredPosition=position;rt.sizeDelta=size;
            Text text=rt.gameObject.AddComponent<Text>();text.font=font;text.fontSize=sizePx;text.text=value;text.color=colour;text.alignment=align;text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        private Button Control(string name,Transform parent,Vector2 size,Action action,bool primary=false,bool warning=false)
        {
            RectTransform rt=Card(name,parent,size,primary?CampaignUI.Vermilion:paper,true);Image image=rt.GetComponent<Image>();image.raycastTarget=true;
            Button button=rt.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action?.Invoke());
            button.colors=CampaignUI.ButtonColours(button.colors);
            Text(rt,name,Vector2.zero,size-new Vector2(16,4),21,warning?danger:primary?paper:ink,TextAnchor.MiddleCenter);return button;
        }
        private void BuildHeader()
        {
            RectTransform header = CreationMenuArt.Panel(safe, "Time cash and served", 0, 0, 625, 66, CreationMenuArt.Sheet.Navy);
            Anchor(header, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -14));
            clock = Text(header, "", new Vector2(-169, 0), new Vector2(244, 52), 26, paper);
            var coin = SelectionSymbol.Create(header, "Cash coin", SelectionSymbolKind.MetaCoin, new Vector2(29, 29));
            ((RectTransform)coin.transform).anchoredPosition = new Vector2(-12, 0);
            coin.color = CampaignUI.Gold;
            cash = Text(header, "", new Vector2(79, 0), new Vector2(133, 54), 29, paper);
            served = Text(header, "", new Vector2(233, 0), new Vector2(113, 46), 24, paper, TextAnchor.MiddleCenter);
            clock.fontStyle = cash.fontStyle = served.fontStyle = FontStyle.Bold;
            serviceNote = Card("Last order and clearing state", safe, new Vector2(366, 47), paper, true);
            // Status belongs beside the header; it must never cover the order row.
            Anchor(serviceNote, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(30, -14));
            serviceText = Text(serviceNote, "", Vector2.zero, new Vector2(334, 41), 22, ink, TextAnchor.MiddleCenter);
            serviceNote.gameObject.SetActive(false);

            headerBlocker = header.GetComponent<Image>();
            headerBlocker.raycastTarget = true;
            noteBlocker = serviceNote.GetComponent<Image>();
            noteBlocker.raycastTarget = false;

            Button close = Control("收摊", safe, new Vector2(106, 56), owner.CloseDay, warning: true);
            Anchor((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-18, -16));
            Button pause = Control("暂停", safe, new Vector2(106, 56), () => GameFlow.Instance?.SetPaused(true));
            Anchor((RectTransform)pause.transform, Vector2.one, Vector2.one, new Vector2(-136, -16));
        }
        private void BuildSmallControls()
        {
            Button menu=Control("菜谱",safe,new Vector2(178,58),()=>OpenOverlay(1));menuButton=menu;
            menu.GetComponentInChildren<Text>().fontSize=25;Anchor((RectTransform)menu.transform,Vector2.zero,Vector2.zero,new Vector2(18,18));
            Button inventory=Control("库存 / 能源",safe,new Vector2(158,48),()=>OpenOverlay(2));Anchor((RectTransform)inventory.transform,Vector2.zero,Vector2.zero,new Vector2(208,18));
            tipButton=Control("提示",safe,new Vector2(114,48),()=>{tipPreference=!TipsOn();Refresh(owner.IsPaused);});
            Anchor((RectTransform)tipButton.transform,Vector2.zero,Vector2.zero,new Vector2(377,18));
            tipLabel=tipButton.GetComponentInChildren<Text>();
        }
        private bool TipsOn()=>tipPreference??session.Served==0;

        private string NextAction()
        {
            if (!owner.CanInteract || owner.ActiveWorkLabel!="" || owner.PendingWorkCount>0) return "";
            if (owner.ServiceUsesCounterMeal)
                return session.FindServingCounterOrder(owner.SelectedCounterMealId)>=0
                    ? "点亮起的订单，交付选中的台面餐" : "这份台面餐还没人点 · 可另选成品";
            if (session.HeldMeal!=null)
                return session.FindServingHeldOrder()>=0 ? "点亮起的订单递餐，或点取餐区放下"
                    : session.ReadyMeals.Count<BusinessSession.CounterCapacity ? "先把手里的成品放到取餐区" : "取餐区已满 · 等合适的顾客来点餐";
            string ready="", preparation="", heating="", clean="";
            for (int i=0;i<session.Lanes.Length;i++)
            {
                KitchenLane lane=session.Lanes[i];
                string method=lane.freeBatch?.method??"";
                bool cold=method==FreeKitchenRules.MethodColdMix||method==FreeKitchenRules.MethodMuddle;
                switch(lane.step)
                {
                    case KitchenStep.Utensil:
                        if (session.RequiresManualHeatAction(i)) return cold
                            ? StallUiCopy.Text("stall.cold_finish","点器具完成冷制")
                            : StallUiCopy.Text("stall.finish","点锅完成火候操作");
                        heating=StallUiCopy.Text("stall.auto_heat","正在自动加工，等完成");
                        break;
                    case KitchenStep.ReadyStock:
                    case KitchenStep.Pack:
                        bool packaging=false, usable=false;
                        foreach (string id in session.AvailableLanePackages(i))
                        {
                            packaging|=session.Count(id)>0;
                            if (session.CanPackToHand(i,id,out _)){usable=true;break;}
                        }
                        if (usable) ready="选包材装好拿起，再递给顾客或放到取餐区";
                        else if (lane.freeBatch==null&&packaging) ready="选包材，把这锅余下的餐装好递出";
                        else if (!packaging && ready=="") ready=StallUiCopy.Text("stall.pack_missing","缺合用的包材，可以看看库存");
                        break;
                    case KitchenStep.Prep:
                        preparation=cold ? StallUiCopy.Text("stall.cold_prep","在操作卡选做法，再点制作")
                            : StallUiCopy.Text("stall.prep","在操作卡选做法，再点开火");
                        break;
                    case KitchenStep.Heating:
                        heating=session.RequiresManualHeatAction(i) ? StallUiCopy.Text("stall.manual_heat","留意火候条，到点再操作")
                            : StallUiCopy.Text("stall.auto_heat","正在自动加工，等完成");
                        break;
                    case KitchenStep.Season:
                        preparation=lane.remaining>0 ? StallUiCopy.Text("stall.finishing","等这一步做完")
                            : StallUiCopy.Text("stall.season","想调味就点调味瓶");
                        break;
                    case KitchenStep.Dirty:
                        clean=StallUiCopy.Text("stall.clean","糊锅了，点锅清理");
                        break;
                }
            }
            foreach (var meal in session.ReadyMeals)
                if (session.FindServingCounterOrder(meal.id)>=0)
                    return "点取餐区的成品，再点顾客请他来取";
            if (ready!="") return ready;
            if (preparation!="") return preparation;
            if (heating!="") return heating;
            if (clean!="") return clean;
            if (session.ServicePhase==BusinessServicePhase.Clearing) return "";
            foreach (var item in session.Inventory)
                if (item.Value>0 && session.FindFreeLane(item.Key)>=0)
                    return StallUiCopy.Text("stall.empty","把食材拖到锅里，先备一点");
            return session.ReadyMeals.Count>0 ? StallUiCopy.Text("stall.no_match","这份先留在台上")
                : StallUiCopy.Text("stall.no_stock","看看库存和器具，找能做的东西");
        }
        private void BuildWorkRibbon()
        {
            workRibbon = Card("Planned stall work", safe, new Vector2(420, 98), paper, true);
            Anchor(workRibbon, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 20));
            activeWork = Text(workRibbon, "", new Vector2(-22, 24), new Vector2(340, 42), 23, ink);
            activeWork.fontStyle = FontStyle.Bold;
            pendingWork = Text(workRibbon, "", new Vector2(-44, -17), new Vector2(292, 39), 21, ink);
            cancelPending = Control("取消", workRibbon, new Vector2(63, 41), owner.CancelPendingWork, warning: true);
            ((RectTransform)cancelPending.transform).anchoredPosition = new Vector2(166, -19);
            ribbonBlocker = workRibbon.GetComponent<Image>();
            ribbonBlocker.raycastTarget = true;
            workRibbon.gameObject.SetActive(false);
        }
        private void BuildCursor()
        {
            packageCursor=Card("Carried packaging pointer",safe,new Vector2(132,42),CampaignUI.Selected,true);
            cursorText=Text(packageCursor,"",Vector2.zero,new Vector2(116,36),18,ink,TextAnchor.MiddleCenter);packageCursor.gameObject.SetActive(false);
            RectTransform picture=Node("Dragged cooked dish",packageCursor);picture.sizeDelta=new Vector2(38,38);picture.anchoredPosition=new Vector2(-44,0);cursorDish=picture.gameObject.AddComponent<Image>();cursorDish.preserveAspect=true;cursorDish.raycastTarget=false;picture.gameObject.SetActive(false);
        }
        private void BuildObjectFocus()
        {
            objectFocus=Node("Current physical selection corners",worldLayer);
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)
            {
                Vector2 corner=new Vector2(x,y);
                foreach(bool horizontal in new[]{true,false})
                {
                    RectTransform line=Node("Selection corner",objectFocus);
                    Anchor(line,corner,corner,Vector2.zero);
                    line.sizeDelta=horizontal?new Vector2(14,2.5f):new Vector2(2.5f,14);
                    Image image=line.gameObject.AddComponent<Image>();image.color=CampaignUI.Gold;image.raycastTarget=false;
                }
            }
            objectFocus.gameObject.SetActive(false);
        }
        private void UpdateObjectFocus(StallInteractionTarget target,bool paused)
        {
            bool show=!paused&&!HasOverlay&&target!=null&&target.kind!=StallInteractionKind.Order&&target.HitCollider!=null;
            objectFocus.gameObject.SetActive(show);if(!show)return;
            Bounds b=target.HitCollider.bounds;
            Vector2 low=new Vector2(float.PositiveInfinity,float.PositiveInfinity),high=-low;
            for(int i=0;i<8;i++)
            {
                Vector3 corner=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector3 screen=owner.Stage.View.WorldToScreenPoint(corner);
                if(screen.z<=0){objectFocus.gameObject.SetActive(false);return;}
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safe,screen,null,out Vector2 p);
                low=Vector2.Min(low,p);high=Vector2.Max(high,p);
            }
            objectFocus.anchoredPosition=(low+high)*.5f;
            objectFocus.sizeDelta=high-low+Vector2.one*10;
        }
        private void BuildOverlay()
        {
            overlay = Node("Inventory or menu", safe);
            Stretch(overlay);
            Image shade = overlay.gameObject.AddComponent<Image>();
            shade.color = CampaignUI.WithAlpha(ink, .65f);
            shade.raycastTarget = true;

            RectTransform sheet = CreationMenuArt.Panel(overlay, "Open stall notebook", 0, 0, 810, 772, CreationMenuArt.Sheet.Paper);
            Anchor(sheet, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero);
            overlayTitle = Text(sheet, "", new Vector2(0, 321), new Vector2(710, 72), 43, ink);
            overlayTitle.fontStyle = FontStyle.Bold;
            CampaignUI.Rule(sheet, new Vector2(0, 277), new Vector2(704, 2));

            overlayRows = CampaignUI.Scroll(sheet, "Notebook item rows", new Vector2(0, 32), new Vector2(704, 444), out overlayScroll);
            overlayBody = Text(sheet, "", new Vector2(0, -243), new Vector2(704, 76), 24, ink);
            overlayClose = Control("回到摊位", sheet, new Vector2(300, 62), CloseOverlay, primary: true);
            ((RectTransform)overlayClose.transform).anchoredPosition = new Vector2(0, -330);
            overlayClose.GetComponentInChildren<Text>().fontSize = 28;
            overlayIcons = gameObject.AddComponent<GameItemIconLibrary>();
            overlay.gameObject.SetActive(false);
        }
        public void FocusDefault(){if(EventSystem.current!=null&&menuButton!=null)EventSystem.current.SetSelectedGameObject(menuButton.gameObject);}
        private void OpenOverlay(int kind)
        {
            if(HasOverlay){overlayKind=kind;UpdateOverlay();return;}
            overlayKind=kind;focusBeforeOverlay=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            pausedBeforeOverlay=GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused;
            disabledForOverlay.Clear();foreach(Button button in safe.GetComponentsInChildren<Button>())if(!button.transform.IsChildOf(overlay)){disabledForOverlay[button]=button.interactable;button.interactable=false;}
            overlay.gameObject.SetActive(true);overlay.SetAsLastSibling();UpdateOverlay(resetScroll: true);
            activeOverlay=this;GameFlow.Instance?.SetPaused(true);
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(overlayClose.gameObject);
        }
        private void CloseOverlay()
        {
            if(!HasOverlay)return;
            overlay.gameObject.SetActive(false);foreach(var pair in disabledForOverlay)if(pair.Key!=null)pair.Key.interactable=pair.Value;disabledForOverlay.Clear();
            if(activeOverlay==this)activeOverlay=null;
            if(GameFlow.Instance!=null&&GameFlow.Instance.Business==owner)GameFlow.Instance.SetPaused(pausedBeforeOverlay);
            if(EventSystem.current!=null){if(focusBeforeOverlay!=null&&focusBeforeOverlay.activeInHierarchy)EventSystem.current.SetSelectedGameObject(focusBeforeOverlay);else FocusDefault();}
        }
        private void Update()
        {
            if(HasOverlay&&(Input.GetKeyDown(KeyCode.Backspace)||Input.GetKeyDown(KeyCode.JoystickButton1)))CloseOverlay();
            TickSalePops();
        }
        private void OnSaleCompleted(int orderId,int recipeIndex,int paidCents,bool shortSeasoning)
        {
            // Every event is one real, completed sale: display each one, even
            // two same-price sales on the same frame. Accounts stay untouched.
            if(paidCents<=0)return; // lost customers and failed serves never raise the event; guard anyway
            // RISK7SCENE：追回入账只给一个反馈——风险单（riskSerial>0）的收款浮字
            // 由 StallRiskView 的“已追回”短浮字承担，这里跳过，避免两次到账提示。
            if(owner!=null&&owner.IsRiskOrder(orderId))return;
            if(owner==null||owner.Stage==null)return;
            Vector3 world=owner.Stage.TryGetGuestAnchor(orderId,out Vector3 anchor)?anchor:owner.Stage.WorkActorPosition+Vector3.up*1.15f;
            SpawnSalePop(world,paidCents);
        }
        private void SpawnSalePop(Vector3 world,int paidCents)
        {
            while(salePops.Count>=MaxSalePops){Destroy(salePops[0].root.gameObject);salePops.RemoveAt(0);}
            RectTransform root=Card("Paid +"+CampaignUI.Money(paidCents),worldLayer,new Vector2(160,42),paper,true);
            root.gameObject.SetActive(false); // positioned on first tick once projected
            Image coin=Node("Warm gold coin",root).gameObject.AddComponent<Image>();coin.sprite=rounded;coin.type=Image.Type.Sliced;coin.color=CampaignUI.Gold;coin.raycastTarget=false;
            coin.rectTransform.anchorMin=coin.rectTransform.anchorMax=new Vector2(0,.5f);coin.rectTransform.pivot=new Vector2(.5f,.5f);coin.rectTransform.anchoredPosition=new Vector2(21,0);coin.rectTransform.sizeDelta=new Vector2(24,24);
            Text(coin.transform,"¥",Vector2.zero,new Vector2(22,24),15,ink,TextAnchor.MiddleCenter).fontStyle=FontStyle.Bold;
            Text label=Text(root,"+"+CampaignUI.Money(paidCents),new Vector2(18,0),new Vector2(118,34),23,ink,TextAnchor.MiddleCenter);label.fontStyle=FontStyle.Bold;
            CanvasGroup group=root.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            int slot=0;while(salePops.Exists(p=>p.lane==slot))slot++;
            salePops.Add(new SalePop{root=root,text=label,coin=coin,group=group,world=world,age=0f,lane=slot});
        }
        private void TickSalePops()
        {
            float dt=Time.unscaledDeltaTime;
            for(int i=salePops.Count-1;i>=0;i--)
            {
                SalePop pop=salePops[i];
                if(owner==null||owner.Stage==null||owner.Stage.View==null){Destroy(pop.root.gameObject);salePops.RemoveAt(i);continue;}
                if(owner.IsPaused||HasOverlay){pop.root.gameObject.SetActive(false);continue;}
                pop.age+=dt;const float life=1.25f;
                if(pop.age>=life){Destroy(pop.root.gameObject);salePops.RemoveAt(i);continue;}
                Vector3 screen=owner.Stage.View.WorldToScreenPoint(pop.world);
                bool onScreen=screen.z>0f;
                pop.root.gameObject.SetActive(onScreen&&!HasOverlay);
                if(!onScreen)continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safe,new Vector2(screen.x,screen.y+pop.age*34f+pop.lane*44f),null,out Vector2 point);
                pop.root.anchoredPosition=Clamped(point,pop.root.sizeDelta);
                float fade=1f-Mathf.SmoothStep(0,1,Mathf.Clamp01((pop.age-.55f)/(life-.55f)));
                pop.group.alpha=fade; // fade coin, text, outline and paper together
                pop.root.localScale=Vector3.one*(1f+.12f*(1f-Mathf.SmoothStep(0,1,Mathf.Clamp01(pop.age/.18f))));
            }
        }
        private void UpdateOverlay(bool resetScroll = false)
        {
            if (!HasOverlay) return;
            float scrollPosition = resetScroll ? 1 : overlayScroll.verticalNormalizedPosition;
            foreach (Transform child in overlayRows)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            float y = 6;
            if (overlayKind == 1)
            {
                Set(overlayTitle, "参考菜谱  ·  已记 " + session.SelectedRecipes.Count + " 道");
                var order = Enumerable.Range(0, BusinessSession.Recipes.Length)
                    .OrderBy(i => session.SelectedRecipes.Contains(i) ? 0 : session.Missing(i) == "" ? 1 : 2)
                    .ThenBy(i => i);
                foreach (int index in order)
                {
                    bool listed = session.SelectedRecipes.Contains(index);
                    string missing = session.Missing(index);
                    MenuOverlayRow(index, listed, missing, y);
                    y += 102;
                }
                Set(overlayBody, "菜单仅作偏好参考 · 不选菜也能开摊，街坊照来点单，备好什么做什么。");
            }
            else
            {
                Set(overlayTitle, "随车库存");
                foreach (var item in session.Inventory)
                    if (item.Value > 0)
                        OverlayRow(overlayIcons.Get(item.Key), ShortItem(item.Key), "随车剩余", item.Value + CommerceReceiptData.StockUnit(item.Key), ref y);
                if (y == 6) OverlayRow(null, "原料与包材已用完", "", "", ref y);
                Set(overlayBody, "煤气罐 " + session.Gas + "   卡式气 " + session.Cartridge + "\n蜂窝煤 " + session.Coal + "   电量 " + session.Electricity);
            }
            overlayRows.sizeDelta = new Vector2(0, Mathf.Max(444, y));
            Canvas.ForceUpdateCanvases();
            overlayScroll.verticalNormalizedPosition = scrollPosition;
        }

        private void MenuOverlayRow(int index, bool listed, string missing, float y)
        {
            StallRecipe recipe = BusinessSession.Recipes[index];
            RectTransform row = CreationMenuArt.Panel(overlayRows, "Menu dish " + index, 0, 0, 680, 91,
                listed ? CreationMenuArt.Sheet.Navy : CreationMenuArt.Sheet.Card);
            Anchor(row, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-5, -y));
            row.GetComponent<Image>().raycastTarget = false;
            Color foreground = listed ? paper : ink;
            Sprite dishSprite = owner.Stage?.DishIcon(index);
            if (dishSprite != null)
                CampaignUI.Icon(row, dishSprite, new Vector2(-282, 0), new Vector2(75, 75));
            else
            {
                RectTransform silhouette = Node("Dish illustration", row);
                silhouette.anchoredPosition = new Vector2(-282, 0);
                silhouette.sizeDelta = new Vector2(75, 75);
                var icon = silhouette.gameObject.AddComponent<StallUIIcon>();
                icon.index = StallMenuReadout.IconIndex(index);
                icon.raycastTarget = false;
            }
            Text(row, DishName(index), new Vector2(-85, 18), new Vector2(272, 43), 29, foreground).fontStyle = FontStyle.Bold;
            int portions = StallMenuReadout.Portions(session, index);
            string detail = missing == "" || portions > 0 && listed ? "约 " + portions + " 份" : "还缺 " + missing;
            Text(row, detail, new Vector2(-41, -21), new Vector2(360, 31), 21, foreground);
            Text(row, CampaignUI.Money(recipe.priceCents), new Vector2(114, 16), new Vector2(105, 39), 27, foreground, TextAnchor.MiddleRight).fontStyle = FontStyle.Bold;
            Button toggle = Control(listed ? "撤参考" : "＋参考", row, new Vector2(122, 54), () =>
            {
                bool before = session.SelectedRecipes.Contains(index);
                owner.ToggleRecipeManaged(index);
                UpdateOverlay();
                if (before == session.SelectedRecipes.Contains(index))
                    Set(overlayBody, session.Message);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(overlayClose.gameObject);
            }, primary: !listed);
            ((RectTransform)toggle.transform).anchoredPosition = new Vector2(253, 0);
            toggle.GetComponentInChildren<Text>().fontSize = 26;
            toggle.interactable = !session.Finished;
        }
        private void OverlayRow(Sprite sprite,string title,string detail,string amount,ref float y)
        {
            RectTransform row=Node("Notebook row "+title,overlayRows);row.anchorMin=row.anchorMax=new Vector2(.5f,1);row.pivot=new Vector2(.5f,1);row.anchoredPosition=new Vector2(0,-y);row.sizeDelta=new Vector2(546,88);
            CampaignUI.Icon(row,sprite,new Vector2(-236,7),new Vector2(68,68));
            Text(row,title,new Vector2(-1,24),new Vector2(388,38),22,ink).fontStyle=FontStyle.Bold;
            Text(row,detail,new Vector2(-46,-11),new Vector2(298,30),17,ink);
            Text(row,amount,new Vector2(201,-11),new Vector2(140,34),22,ink,TextAnchor.MiddleRight).fontStyle=FontStyle.Bold;
            y+=94;
        }
        private void ApplySafeArea()
        {
            if(previousSafeArea==Screen.safeArea&&previousScreen==new Vector2Int(Screen.width,Screen.height))return;
            previousSafeArea=Screen.safeArea;previousScreen=new Vector2Int(Screen.width,Screen.height);
            if(Screen.width<=0||Screen.height<=0)return;
            safe.anchorMin=new Vector2(previousSafeArea.xMin/Screen.width,previousSafeArea.yMin/Screen.height);
            safe.anchorMax=new Vector2(previousSafeArea.xMax/Screen.width,previousSafeArea.yMax/Screen.height);safe.offsetMin=safe.offsetMax=Vector2.zero;
        }
        private void ScanTargets()
        {
            if(owner.Stage==null)return;
            StallInteractionTarget[] found=owner.Stage.GetComponentsInChildren<StallInteractionTarget>(false);
            foreach(StallInteractionTarget target in found)
                if(!tags.ContainsKey(target))tags.Add(target,MakeTag(target));
            var remove=new List<StallInteractionTarget>();
            foreach(var pair in tags)if(pair.Key==null){Destroy(pair.Value.root.gameObject);if(pair.Value.leader!=null)Destroy(pair.Value.leader.gameObject);remove.Add(pair.Key);}
            foreach(var key in remove)tags.Remove(key);
            knownOrders=session.Orders.Count;knownLanes=session.Lanes.Length;nextTargetScan=Time.unscaledTime+.35f;
        }
        private Tag MakeTag(StallInteractionTarget target)
        {
            bool order=target.kind==StallInteractionKind.Order,cook=target.kind==StallInteractionKind.Cookware;
            bool serving=target.kind==StallInteractionKind.Serving,counterMeal=target.kind==StallInteractionKind.CounterMeal;
            Vector2 size=order?new Vector2(234,96):cook?new Vector2(194,70):serving?new Vector2(150,58):counterMeal?new Vector2(44,36):new Vector2(92,30);
            RectTransform rt=Card("Object readout "+target.kind,worldLayer,size,paper,true);
            Tag tag=new Tag{target=target,root=rt,size=size,face=rt.GetComponent<Image>()};
            if(order)
            {
                // Reuse the game's painted ticket surface, while keeping the
                // entire order as the original click target.
                Sprite ticket=CreationMenuArt.SpriteFor(CreationMenuArt.Sheet.Card);
                if(ticket!=null){tag.face.sprite=ticket;tag.face.type=Image.Type.Sliced;tag.face.pixelsPerUnitMultiplier=1.6f;}
            }
            if(counterMeal)
            {
                // 台面餐常驻只是 64x32 小标记（编号+价格），3D 餐物为主体；
                // 名称/份量/风味的详情只在选中后于固定安全区卡显示，不随 hover 展开。
                tag.title=Text(rt,"#",Vector2.zero,new Vector2(40,34),20,ink,TextAnchor.MiddleCenter);tag.title.fontStyle=FontStyle.Bold;
                tag.detail=Text(rt,"",Vector2.zero,new Vector2(40,30),18,ink,TextAnchor.MiddleCenter);tag.detail.gameObject.SetActive(false);
                tag.button=rt.gameObject.AddComponent<Button>();tag.button.targetGraphic=tag.face;tag.button.onClick.AddListener(()=>{string id=tag.target.itemId;owner.SelectCounterMeal(owner.SelectedCounterMealId==id?"":id);});
                tag.button.colors=CampaignUI.ButtonColours(tag.button.colors);
                tag.face.raycastTarget=true;
            }
            else if(serving)
            {
                // 出餐台实物入口：只打包入台，绝不自动交付。
                tag.title=Text(rt,"取餐区",new Vector2(0,17),new Vector2(130,30),19,ink,TextAnchor.MiddleCenter);tag.title.fontStyle=FontStyle.Bold;
                Button pack=Control("装餐上台",rt,new Vector2(120,26),()=>owner.UseServingCounter(tag.target.lane));
                ((RectTransform)pack.transform).anchoredPosition=new Vector2(0,-13);
                pack.GetComponentInChildren<Text>().fontSize=15;
                tag.button=pack;
            }
            else if(order||cook)
            {
                RectTransform tip=Node("Speech pointer",rt);Anchor(tip,new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(0,1));tip.sizeDelta=new Vector2(22,15);
                tag.tail=tip;
                StallSpeechTail tail=tip.gameObject.AddComponent<StallSpeechTail>();tail.color=paper;tail.raycastTarget=false;
                tag.title=Text(rt,"",new Vector2(order?23:0,order?18:12),new Vector2(order?168:180,42),order?22:19,ink,order?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter);tag.title.fontStyle=FontStyle.Bold;
                tag.detail=Text(rt,"",new Vector2(order?23:0,order?-8:-10),new Vector2(order?168:180,25),order?20:16,ink,order?TextAnchor.MiddleLeft:TextAnchor.MiddleCenter);
                RectTransform track=Card("Time track",rt,new Vector2(size.x-30,7),CampaignUI.Pale,false);track.anchoredPosition=new Vector2(0,-size.y*.5f+12);tag.track=track.GetComponent<Image>();
                RectTransform fill=Card("Time remaining",track,track.sizeDelta,progressColour,false);tag.bar=fill.GetComponent<Image>();tag.bar.type=Image.Type.Filled;tag.bar.fillMethod=Image.FillMethod.Horizontal;tag.bar.fillOrigin=0;
                if(order)
                {
                    RectTransform medallion=Node("Dish illustration",rt);tag.portrait=medallion;medallion.sizeDelta=new Vector2(54,54);medallion.anchoredPosition=new Vector2(-80,9);
                    // 订单头像：稳定可识别（有效 recipeIndex）才画对应菜图；generic
                    // 自由出餐单没有真实菜图来源，用中性餐盘图标，不假装某道菜。
                    StallOrder orderData=session.FindOrder(target.orderId);
                    int realRecipe=orderData==null?-1:OrderCookingGuide.Read(session,orderData.id).recipeIndex;
                    bool known=realRecipe>=0&&realRecipe<BusinessSession.Recipes.Length;
                    Sprite dishSprite=known?owner.Stage.DishIcon(realRecipe):null;
                    if(dishSprite!=null){Image dish=medallion.gameObject.AddComponent<Image>();dish.sprite=dishSprite;dish.preserveAspect=true;dish.raycastTarget=false;}
                    else if(known){tag.dishIcon=medallion.gameObject.AddComponent<StallUIIcon>();tag.dishIcon.index=realRecipe;tag.dishIcon.raycastTarget=false;}
                    else medallion.gameObject.AddComponent<FreeMealPlateIcon>().raycastTarget=false;
                    // FLAVOR11：点了口味的订单在头像下方挂一枚小口味牌（底色=风味色、
                    // 短名），主名称仍是大字；没点口味的订单不显示。
                    RectTransform chip=Node("Requested flavor chip",rt);tag.flavorChip=chip;chip.anchorMin=chip.anchorMax=new Vector2(.5f,.5f);chip.pivot=new Vector2(.5f,.5f);chip.sizeDelta=new Vector2(66,30);chip.anchoredPosition=new Vector2(-88,-10);
                    tag.flavorFace=chip.gameObject.AddComponent<Image>();tag.flavorFace.sprite=rounded;tag.flavorFace.type=Image.Type.Sliced;tag.flavorFace.color=paper;tag.flavorFace.raycastTarget=false;
                    tag.flavorText=Text(chip,"",Vector2.zero,new Vector2(62,30),19,paper,TextAnchor.MiddleCenter);tag.flavorText.fontStyle=FontStyle.Bold;
                    chip.gameObject.SetActive(false);
                    if(orderData!=null)BuildOrderIngredients(tag,orderData);
                    var hover=rt.gameObject.AddComponent<HUDPointerReveal>();hover.entered=()=>hoveredTag=target;hover.exited=()=>{if(hoveredTag==target)hoveredTag=null;};
                    tag.face.raycastTarget=true;tag.button=rt.gameObject.AddComponent<Button>();tag.button.targetGraphic=tag.face;tag.button.onClick.AddListener(()=>OnOrderClicked(tag.target.orderId));
                    ColorBlock colours=CampaignUI.ButtonColours(tag.button.colors);colours.disabledColor=Color.white;tag.button.colors=colours;
                }
            }
            else tag.title=Text(rt,"",Vector2.zero,size-new Vector2(8,2),16,ink,TextAnchor.MiddleCenter);
            if(target.kind==StallInteractionKind.Ingredient)
            {
                tag.leader=Node("Ingredient quantity leader",worldLayer);tag.leader.SetAsFirstSibling();
                Image line=tag.leader.gameObject.AddComponent<Image>();line.color=CampaignUI.WithAlpha(ink,.72f);line.raycastTarget=false;
            }
            SetTagExpanded(tag,false);return tag;
        }
        // 顾客点击（出餐台流程）：风险拦截优先；已选台面餐时校验并真实递餐；
        // 否则只提示，绝不 SelectOrder、绝不自动交付。
        private void OnOrderClicked(int orderId)
        {
            owner.ClickCustomerForService(orderId);
        }
        private void SetTagExpanded(Tag tag,bool expanded)
        {
            bool order=tag.target.kind==StallInteractionKind.Order,cook=tag.target.kind==StallInteractionKind.Cookware;
            if(!order&&!cook)return;
            // Stable selection surfaces: hovering must not resize the target or
            // push a neighbour away from the pointer. Details live in the footer.
            expanded=order;
            Vector2 size=order?new Vector2(250,142):new Vector2(136,48);
            if(tag.size==size)return;
            tag.size=size;tag.root.sizeDelta=size;tag.detail.gameObject.SetActive(expanded);
            tag.title.rectTransform.anchoredPosition=order?new Vector2(29,38):new Vector2(0,expanded?15:5);
            tag.title.rectTransform.sizeDelta=order?new Vector2(178,64):new Vector2(size.x-14,34);
            tag.title.fontSize=order?22:20;
            if(tag.portrait!=null){tag.portrait.anchoredPosition=new Vector2(-92,37);tag.portrait.sizeDelta=Vector2.one*48;}
            if(tag.flavorChip!=null)tag.flavorChip.anchoredPosition=new Vector2(-88,-10);
            tag.detail.rectTransform.anchoredPosition=order?new Vector2(0,-43):new Vector2(0,-11);
            tag.detail.rectTransform.sizeDelta=new Vector2(order?230:size.x-14,34);
            tag.track.rectTransform.sizeDelta=new Vector2(size.x-24,8);tag.track.rectTransform.anchoredPosition=new Vector2(0,-size.y*.5f+5);
            tag.bar.rectTransform.sizeDelta=tag.track.rectTransform.sizeDelta;
        }
        private StallInteractionTarget ContextTarget()
        {
            if(hoveredTag!=null&&hoveredTag.isActiveAndEnabled)return hoveredTag;
            var input=owner.InputHandler;
            if(input!=null&&input.Hovered!=null)return input.Hovered;
            if(input!=null&&input.FocusedTarget!=null)return input.FocusedTarget;
            // Walking past a jar is not a new pointer selection.
            return null;
        }
        public void Refresh(bool paused)
        {
            if(owner==null)return;ApplySafeArea();
            if(knownOrders!=session.Orders.Count||knownLanes!=session.Lanes.Length||Time.unscaledTime>=nextTargetScan)ScanTargets();
            int seconds=Mathf.CeilToInt(session.Remaining);
            bool lastCall=session.ServicePhase==BusinessServicePhase.LastCall,clearing=session.ServicePhase==BusinessServicePhase.Clearing;
            int collecting=session.ReadyMeals.Count(m=>owner.PickupOrderForMeal(m.id)>=0);
            string phaseLabel=clearing?"清台":lastCall?"最后接单":session.Setup.practice?"练习":"营业";
            Set(clock,phaseLabel+" · "+(seconds/60).ToString("00")+":"+(seconds%60).ToString("00"));
            serviceNote.gameObject.SetActive(!paused&&!HasOverlay&&(lastCall||clearing||collecting>0));
            Set(serviceText,clearing?"已停止接单 · 完成余单":lastCall?"末班客 · "+Mathf.CeilToInt(session.SecondsUntilLastOrder)+" 秒后停单":collecting+" 位顾客取餐中");
            Set(cash,"¥"+(session.CashCents/100f).ToString("0.##"));Set(served,"出餐 "+session.Served);
            string context=owner.InteractionHint;
            bool tips=TipsOn();
            if(tipLabel!=null)Set(tipLabel,tips
                ? StallUiCopy.Text("stall.help_on","隐藏提示") : StallUiCopy.Text("stall.help_off","操作提示"));
            if(context==""&&owner.ServiceUsesCounterMeal)
            {
                int pickup=owner.PickupOrderForMeal(owner.SelectedCounterMealId);
                context=pickup>=0?"顾客正来取餐 · 可以继续备菜":session.FindServingCounterOrder(owner.SelectedCounterMealId)>=0
                    ? "点亮起的订单，请顾客来取餐"
                    : clearing ? "这份还没人点 · 可另选成品" : "这份还没人点 · 先留在取餐区";
            }
            else if(context==""&&tips&&!paused&&!HasOverlay&&(orderGuide==null||!orderGuide.gameObject.activeSelf)&&session.HeldMeal==null)context=NextAction();
            bool guidance=owner.InteractionHint==""&&context!="";
            toastFace.color=guidance?ink:paper;
            hint.color=guidance?paper:ink;
            hint.fontSize=guidance?22:19;
            hint.fontStyle=guidance?FontStyle.Bold:FontStyle.Normal;
            Set(hint,context);toast.sizeDelta=new Vector2(590,58);hint.rectTransform.sizeDelta=new Vector2(558,50);toast.gameObject.SetActive(!paused&&!HasOverlay&&context!="");
            bool queued=owner.PendingWorkCount>0,working=owner.ActiveWorkLabel!="";
            workRibbon.gameObject.SetActive(!paused&&!HasOverlay&&(queued||working));
            Set(activeWork, working ? owner.ActiveWorkLabel : "等摊主空手");
            string queue = owner.PendingWorkLabel;
            int separator = queue.IndexOf(" → ", StringComparison.Ordinal);
            if (separator >= 0) queue = queue.Substring(0, separator);
            Set(pendingWork, queued ? "接着 " + queue + (owner.PendingWorkCount > 1 ? " +" + (owner.PendingWorkCount - 1) : "") : "");
            pendingWork.gameObject.SetActive(queued);
            cancelPending.gameObject.SetActive(queued);cancelPending.interactable=!paused&&!HasOverlay;
            previousPending=owner.PendingWorkCount;
            UpdateTags(paused);UpdateCursor();RefreshLanePanel(paused);RefreshMealCard(paused);
            // The card must describe the same meal used by customer clicks.
            if(owner.ServiceUsesCounterMeal){if(heldCard!=null)heldCard.root.gameObject.SetActive(false);}
            else RefreshHeldMealCard(paused);
            RefreshOrderGuide(paused);
        }
        private void UpdateTags(bool paused)
        {
            var orders=new List<Tag>();var pans=new List<Tag>();var packaging=new List<Tag>();
            StallInteractionTarget contextTarget=ContextTarget();
            UpdateObjectFocus(contextTarget,paused);
            // This layout is the only writer of each world tag's position.
            // HUDSoftMotion caches a previous position, so never attach it here.
            lastContextTarget=contextTarget;
            foreach(Tag tag in tags.Values)
            {
                StallInteractionTarget target=tag.target;
                if(target==null||!target.isActiveAndEnabled){tag.root.gameObject.SetActive(false);if(tag.leader!=null)tag.leader.gameObject.SetActive(false);continue;}
                Vector3 screen=owner.Stage.View.WorldToScreenPoint(target.WorldAnchor);
                bool focused=contextTarget==target;
                bool show=!paused&&!HasOverlay&&screen.z>0&&screen.x>-30&&screen.x<Screen.width+30&&screen.y>0&&screen.y<Screen.height;
                if(target.kind==StallInteractionKind.Order)
                {
                    StallOrder order=session.FindOrder(target.orderId);show&=order!=null&&!order.departed&&!order.served&&(order.arrived||focused);
                    if(show)
                    {
                        bool riskPending=session.HasPendingRiskOrder(order.id);
                        string mealId=owner.SelectedCounterMealId;
                        bool deliverable=false;string blockReason="";
                        if(owner.HasServiceMeal)deliverable=owner.CanServeFocusedMealTo(order.id,out blockReason);
                        SetTagExpanded(tag,true);
                        tag.face.color=riskPending||deliverable||focused?CampaignUI.Selected:paper;
                        Set(tag.title,session.OrderDishName(order.id)+" ×1");
                        
                        // FLAVOR11：requestedFlavorId 非空的订单显示口味小牌；空串单原样。
                        string requestedFlavor=order.requestedFlavorId;
                        if(tag.flavorChip!=null)
                        {
                            bool hasFlavor=!string.IsNullOrEmpty(requestedFlavor);
                            tag.flavorChip.gameObject.SetActive(hasFlavor);
                            if(hasFlavor){tag.flavorFace.color=FlavorColor(requestedFlavor);Set(tag.flavorText,StallInteractionInput.FlavorShortName(requestedFlavor));tag.flavorText.color=requestedFlavor=="S01"?paper:ink;}
                        }
                        // 订单只读：风险单提示追回；已选台面餐时适配单高亮显示报价、
                        // 错配单显示真实原因（不执行）；未选餐只提示先上台。
                        string detail;
                        if(riskPending)detail="点击这位顾客 · 追回款项";
                        else if(!order.arrived)detail="走近中";
                        else if(session.ReadyMeals.Any(m=>owner.PickupOrderForMeal(m.id)==order.id))detail="顾客取餐中";
                        else if(deliverable)detail=(owner.ServiceUsesCounterMeal?"点此叫客 · ":"点此递餐 · ")+CampaignUI.Money(session.OrderPriceCents(order.id));
                        else if(mealId!=""||session.HeldMeal!=null)detail=blockReason==""?"与订单不符":LimitCells(blockReason,10);
                        else detail=string.IsNullOrEmpty(requestedFlavor)?"原味 · 点开看做法":"点开看做法";
                        Set(tag.detail,detail);
                        tag.track.gameObject.SetActive(order.arrived);
                        tag.bar.fillAmount=Mathf.Clamp01(order.patience/order.maxPatience);tag.bar.color=tag.bar.fillAmount<.25f?danger:progressColour;tag.button.interactable=order.arrived&&!paused&&!HasOverlay&&owner.CanInteract;
                    }
                }
                else if(target.kind==StallInteractionKind.Cookware)
                {
                    show&=target.lane>=0&&target.lane<session.Lanes.Length;
                    if(show)
                    {
                        KitchenLane lane=session.Lanes[target.lane];
                        bool needsRice=session.LaneNeedsRice(target.lane);
                        bool freeLane=session.IsFreeLane(target.lane);
                        SetTagExpanded(tag,false);
                        // FLAVOR11：冷制饮品家族（ParentRecipeIndex==5）不烧火，文案
                        // 用舀/锤/盛，与热锅的加热/翻炒明显区分。自由锅无 recipeIndex。
                        string method=lane.freeBatch?.method??"";
                        bool coldLane=freeLane?FreeKitchenRules.IsColdMethod(method):
                            lane.recipeIndex>=0&&lane.recipeIndex<BusinessSession.Recipes.Length&&BusinessSession.Recipes[lane.recipeIndex].cold;
                        bool manualHeat=session.RequiresManualHeatAction(target.lane);
                        bool heatPlanned=owner.TryGetHeatPlan(target.lane,out BusinessController.HeatPlan heatPlan);
                        string heatNote=heatPlan.actorBusy?" · 等待摊主":heatPlan.contactEstimated?(heatPlan.feasible?" · 预计来得及":" · 可能过火"):"";
                        string title="",detail="";float progress=0;
                        switch(lane.step)
                        {
                            case KitchenStep.Idle:
                                // 旧档待下米饭锅保留真实提示；其余空锅（含自由锅）统一文案。
                                title=needsRice?"下米饭":coldLane?"备果料":"空锅";
                                detail=needsRice?"米饭拖进锅里":coldLane?"果料拖到冰槽":"空锅 · 放入食材";break;
                            case KitchenStep.Prep:title=session.LaneDishName(target.lane);detail="投料中 · 看操作卡";progress=1-lane.remaining/Mathf.Max(.01f,lane.duration);break;
                            case KitchenStep.Heating:title=coldLane?"制作中":method==FreeKitchenRules.MethodReheat?"复热中":"加热中";
                                detail=lane.remaining.ToString("0.0")+" 秒 · "+(manualHeat?(heatPlanned?"已安排":"可安排下一步"):"自动完成");
                                progress=1-lane.remaining/Mathf.Max(.01f,lane.duration);break;
                            case KitchenStep.Utensil:
                                title=freeLane?session.LaneDishName(target.lane):coldLane?"捣柠檬":heatPlanned?"已安排翻炒":"现在"+(lane.recipeIndex>=0?BusinessSession.Recipes[lane.recipeIndex].toolLabel:"操作");
                                detail=coldLane?StallUiCopy.Text("stall.cold_finish","点器具完成冷制"):
                                    lane.remaining.ToString("0.0")+" 秒"+heatNote;
                                progress=lane.remaining/Mathf.Max(.01f,lane.duration);break;
                            case KitchenStep.Season:title=lane.remaining>0?"盛装 / 沥油":"点调味瓶";detail=lane.remaining>0?lane.remaining.ToString("0.0")+" 秒":session.SeasoningAvailable?"锅内 "+lane.batchRemaining+" 份":"缺油盐 · 仍可出餐";break;
                            case KitchenStep.Pack:title=freeLane?"可以装餐":"可以打包";detail=freeLane?"选择包材，装好拿起":"把菜拖到盒袋里";break;
                            case KitchenStep.ReadyStock:
                                {var product=session.ProductAtLane(target.lane);
                                title=product!=null&&product.name!=""?"出锅 · "+product.name:(coldLane?"备好 ":"熟了 ")+lane.batchRemaining+" 份";
                                detail=product!=null?"装好拿起 · 余 "+product.remaining+" 份":"选择包材 · 或点锅盛出";break;}
                            case KitchenStep.Dirty:title="糊锅了";detail="点锅清理";break;
                        }
                        Set(tag.title,title);Set(tag.detail,detail);tag.face.color=lane.step==KitchenStep.Utensil||needsRice||focused?CampaignUI.Selected:paper;
                        if(focused&&!paused&&!HasOverlay&&owner.InteractionHint=="")
                        {Set(hint,title+(detail==""?"":" · "+detail));toast.gameObject.SetActive(true);}
                        tag.title.color=lane.step==KitchenStep.Dirty||!coldLane&&heatPlanned&&heatPlan.contactEstimated&&!heatPlan.feasible?danger:ink;tag.bar.fillAmount=Mathf.Clamp01(progress);tag.bar.color=progressColour;
                        tag.track.gameObject.SetActive(lane.step==KitchenStep.Prep||lane.step==KitchenStep.Heating||!coldLane&&lane.step==KitchenStep.Utensil);
                    }
                }
                else if(target.kind==StallInteractionKind.CounterMeal)
                {
                    // 台面餐常驻只是小标记：编号+单份价。详情在固定选中卡。
                    CounterMeal meal=session.ReadyMeals.Find(m=>m.id==target.itemId);
                    show&=meal!=null;
                    if(show)
                    {
                        bool selected=owner.SelectedCounterMealId==meal.id;
                        int slot=session.ReadyMeals.IndexOf(meal)+1;
                        Set(tag.title,"#"+slot);
                        Set(tag.detail,CampaignUI.Money(meal.priceCents));
                        tag.face.color=selected||focused||owner.PickupOrderForMeal(meal.id)>=0?CampaignUI.Selected:paper;
                        tag.button.interactable=!paused&&!HasOverlay&&owner.CanInteract;
                    }
                }
                else if(target.kind==StallInteractionKind.Serving)
                {
                    // 出餐台底座是 lane=-1 的全局入口：综合所有锅，有可装餐的锅
                    // 即亮显可点；否则显示真实短原因，不无声不可点。
                    bool held=session.HeldMeal!=null;
                    bool canPack=false;string reason="",rejected="";
                    if(held)canPack=session.CanPlaceHeldMeal(out reason);
                    for(int li=0;!held&&li<session.Lanes.Length;li++)
                    {
                        foreach(string pkg in session.AvailableLanePackages(li))
                            if(session.CanPackToCounter(li,pkg,out rejected)){canPack=true;break;}
                        if(canPack)break;
                        if(reason=="")reason=rejected;
                    }
                    if(reason=="")reason="暂无成品可装餐";
                    Set(tag.title,"取餐区 · "+session.ReadyMeals.Count+"/6");
                    tag.face.color=canPack||focused?CampaignUI.Selected:paper;
                    tag.button.interactable=canPack&&!paused&&!HasOverlay&&owner.CanInteract;
                    // 不可装餐时不无声：按钮面直接显示真实短原因。
                    var serveLabel=tag.button.GetComponentInChildren<Text>();
                    if(serveLabel!=null)Set(serveLabel,canPack?(held?"放下成品":"装餐放下"):session.ReadyMeals.Count>0?"点餐叫客":"待出餐");
                    if(focused&&!paused&&!HasOverlay&&owner.InteractionHint=="")
                    {Set(hint,canPack?(held?"取餐区 · 放下手里的成品":"取餐区 · 装餐并放下"):(session.ReadyMeals.Count>0?"点一份成品，再点顾客，请他来取餐":reason));toast.gameObject.SetActive(true);}
                }
                else
                {
                    string id=target.itemId;
                    if(target.kind==StallInteractionKind.Seasoning&&id=="")id="seasoning";
                    int amount=session.Count(id);bool carried=owner.InputHandler!=null&&owner.InputHandler.CarriedPackageId==id;
                    show&=focused||carried;
                    Set(tag.title,(target.kind==StallInteractionKind.Seasoning?(id=="seasoning"?"油盐":ChipName(id)):ChipName(id))+" "+amount);
                    float width=Mathf.Clamp(tag.title.preferredWidth+16,92,210);tag.size=new Vector2(width,34);tag.root.sizeDelta=tag.size;tag.title.rectTransform.sizeDelta=tag.size-new Vector2(12,2);
                    bool picked=focused||carried;
                    tag.face.color=target.kind==StallInteractionKind.Seasoning&&amount<=0?danger:picked?CampaignUI.Selected:paper;
                }
                tag.root.gameObject.SetActive(show);if(tag.leader!=null)tag.leader.gameObject.SetActive(show);if(!show)continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safe,screen,null,out Vector2 point);
                float rise=target.kind==StallInteractionKind.Order?tag.size.y*.5f+16:target.kind==StallInteractionKind.Cookware?tag.size.y*.5f+9:target.kind==StallInteractionKind.CounterMeal||target.kind==StallInteractionKind.Serving?tag.size.y*.5f+6:-24;
                tag.desired=point+Vector2.up*rise;tag.root.anchoredPosition=Clamped(tag.desired,tag.size);
                if(target.kind==StallInteractionKind.Order)
                {
                    // All four customer tickets share a stable reading line.
                    // Their x positions still identify the actual customer.
                    tag.desired=new Vector2(point.x,safe.rect.yMax-151);
                    tag.root.anchoredPosition=Clamped(tag.desired,tag.size);
                }
                if(target.kind==StallInteractionKind.Ingredient)
                {
                    // Put the count outside its own tray, keeping the tray's
                    // vertical anchor instead of covering the next tray below.
                    tag.desired=point+Vector2.left*90;tag.root.anchoredPosition=Clamped(tag.desired,tag.size);
                    Vector2 from=tag.root.anchoredPosition+Vector2.right*(tag.size.x*.5f-1),delta=point-from;
                    tag.leader.anchoredPosition=(from+point)*.5f;tag.leader.sizeDelta=new Vector2(delta.magnitude,1.5f);
                    tag.leader.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
                }
                if(target.kind==StallInteractionKind.Order)orders.Add(tag);
                if(target.kind==StallInteractionKind.Cookware)pans.Add(tag);
                if(target.kind==StallInteractionKind.Packaging)packaging.Add(tag);
            }
            packaging.Sort((a,b)=>a.desired.x.CompareTo(b.desired.x));
            for(int i=1;i<packaging.Count;i++)
            {Vector2 a=packaging[i-1].root.anchoredPosition,b=packaging[i].root.anchoredPosition;if(Mathf.Abs(a.y-b.y)<34&&b.x-a.x<98){float middle=(a.x+b.x)*.5f;a.x=middle-49;b.x=middle+49;packaging[i-1].root.anchoredPosition=Clamped(a,packaging[i-1].size);packaging[i].root.anchoredPosition=Clamped(b,packaging[i].size);}}
            if(pans.Count==2)
            {
                pans.Sort((a,b)=>a.desired.x.CompareTo(b.desired.x));Vector2 a=pans[0].root.anchoredPosition,b=pans[1].root.anchoredPosition;
                float need=(pans[0].size.x+pans[1].size.x)*.5f+8;
                if(Mathf.Abs(a.y-b.y)<70&&b.x-a.x<need)
                {float middle=(a.x+b.x)*.5f;a.x=middle-need*.5f;b.x=middle+need*.5f;pans[0].root.anchoredPosition=Clamped(a,pans[0].size);pans[1].root.anchoredPosition=Clamped(b,pans[1].size);}
                foreach(Tag pan in pans)pan.tail.anchoredPosition=new Vector2(Mathf.Clamp(pan.desired.x-pan.root.anchoredPosition.x,-pan.size.x*.5f+16,pan.size.x*.5f-16),1);
            }
            // Keep adjacent customers' speech bubbles distinct without detaching
            // them to a fixed top-row ticket board.
            orders.Sort((a,b)=>a.desired.x.CompareTo(b.desired.x));
            for(int i=1;i<orders.Count;i++)
            {
                Tag previous=orders[i-1],current=orders[i];Vector2 at=current.root.anchoredPosition,prior=previous.root.anchoredPosition;
                float gap=(previous.size.x+current.size.x)*.5f+8;
                if(Mathf.Abs(at.y-prior.y)<(previous.size.y+current.size.y)*.5f&&at.x-prior.x<gap)at.x=prior.x+gap;
                current.root.anchoredPosition=Clamped(at,current.size);
            }
            for(int i=orders.Count-2;i>=0;i--)
            {
                Tag current=orders[i],next=orders[i+1];Vector2 at=current.root.anchoredPosition,after=next.root.anchoredPosition;
                float gap=(next.size.x+current.size.x)*.5f+8;
                if(Mathf.Abs(at.y-after.y)<(next.size.y+current.size.y)*.5f&&after.x-at.x<gap)at.x=after.x-gap;
                current.root.anchoredPosition=Clamped(at,current.size);
            }
            foreach(Tag order in orders)
                order.tail.anchoredPosition=new Vector2(Mathf.Clamp(order.desired.x-order.root.anchoredPosition.x,-order.size.x*.5f+18,order.size.x*.5f-18),1);
        }
        private static string MethodShortName(string id)
        {switch(id){case "stir_fry":return "炒";case "griddle":return "煎";case "boil":return "煮";case "fry":return "炸";case "reheat":return "复热";case "cold_mix":return "冷调";case "muddle":return "手锤";default:return id;}}
        // 出餐台 / 台面餐选中详情 / 锅具操作卡：统一走安全区固定卡，不叠世界标签。
        private sealed class FixedCard
        {
            public RectTransform root,body;
            public Text title,detail;
            public readonly List<Button> choices=new List<Button>();
            public readonly List<Text> choiceLabels=new List<Text>();
        }
        private FixedCard BuildFixedCard(string name,Vector2 size)
        {
            FixedCard card=new FixedCard();
            card.root=Card(name,safe,size,paper,false);
            var sheet=CreationMenuArt.SpriteFor(CreationMenuArt.Sheet.Paper);
            if(sheet!=null){var face=card.root.GetComponent<Image>();face.sprite=sheet;face.type=Image.Type.Sliced;face.color=Color.white;}
            card.root.GetComponent<Image>().raycastTarget=true;
            card.title=Text(card.root,"",Vector2.zero,new Vector2(size.x-36,38),24,ink,TextAnchor.MiddleLeft);card.title.fontStyle=FontStyle.Bold;
            Anchor(card.title.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-12));
            card.detail=Text(card.root,"",Vector2.zero,new Vector2(size.x-36,72),21,ink,TextAnchor.UpperLeft);
            Anchor(card.detail.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-56));
            card.body=Node(name+" choices",card.root);card.body.sizeDelta=new Vector2(size.x-24,42);
            Anchor(card.body,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,12));
            card.root.gameObject.SetActive(false);
            return card;
        }
        private void LayoutFixedCard(FixedCard card,Vector2 anchorTopCenter)
        {
            card.root.anchorMin=card.root.anchorMax=new Vector2(.5f,1);
            card.root.pivot=new Vector2(.5f,1);
            card.root.anchoredPosition=anchorTopCenter;
        }
        private void RebuildChoices(FixedCard card,Vector2 size,int count)
        {
            foreach(Button choice in card.choices){if(choice!=null)Destroy(choice.gameObject);}
            card.choices.Clear();card.choiceLabels.Clear();
            card.body.gameObject.SetActive(count>0);
            card.body.anchoredPosition=new Vector2(0,12);
            float width=(size.x-16-8*(count-1))/Mathf.Max(1,count);
            float x=-(size.x-16)*.5f+width*.5f;
            for(int i=0;i<count;i++)
            {
                Button b=Control("",card.body,new Vector2(width,42),null);
                ((RectTransform)b.transform).anchoredPosition=new Vector2(x,0);x+=width+8;
                b.GetComponentInChildren<Text>().fontSize=18;
                card.choices.Add(b);card.choiceLabels.Add(b.GetComponentInChildren<Text>());
            }
        }
        private void SetChoice(FixedCard card,int index,string label,Action action,bool interactable,bool current)
        {
            if(index>=card.choices.Count)return;
            Button b=card.choices[index];
            b.onClick.RemoveAllListeners();
            if(action!=null)b.onClick.AddListener(()=>action());
            b.interactable=interactable;
            if(card.choiceLabels[index]!=null){Set(card.choiceLabels[index],label);card.choiceLabels[index].color=current?paper:ink;}
            if(current)b.GetComponent<Image>().color=CampaignUI.Vermilion;
        }
        // 图标只读取锅里已发生接触的 FoodInput，不显示搬运中的食材。
        private readonly List<Image> laneItemIcons=new List<Image>();
        private readonly List<Text> laneItemLabels=new List<Text>();
        private readonly List<Text> laneItemCounts=new List<Text>();
        private Text laneEmptyLabel;
        private void BuildLanePanel()
        {
            Vector2 size=new Vector2(420,280);
            lanePanel=BuildFixedCard("Lane operation card",size);
            lanePanel.root.anchorMin=lanePanel.root.anchorMax=new Vector2(1,0);lanePanel.root.pivot=new Vector2(1,0);lanePanel.root.anchoredPosition=new Vector2(-16,110);
            lanePanel.title.rectTransform.sizeDelta=new Vector2(384,36);
            lanePanel.detail.rectTransform.sizeDelta=new Vector2(384,94);
            lanePanel.detail.fontSize=22;
            Anchor(lanePanel.detail.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-120));
            laneEmptyLabel=Text(lanePanel.root,"锅里还没有食材",Vector2.zero,new Vector2(378,46),20,CampaignUI.WithAlpha(ink,.7f));
            Anchor(laneEmptyLabel.rectTransform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-52));
            // 同时兼容当前所有可组合的材料种类；不通过食材名推测锅内状态。
            for(int i=0;i<5;i++)
            {
                RectTransform cell=Node("Landed ingredient "+i,lanePanel.root);
                cell.sizeDelta=new Vector2(76,66);
                Anchor(cell,new Vector2(0,1),new Vector2(0,1),new Vector2(19+i*76,-50));
                RectTransform imageRect=Node("Ingredient icon",cell);imageRect.sizeDelta=new Vector2(44,40);
                Anchor(imageRect,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero);
                Image icon=imageRect.gameObject.AddComponent<Image>();icon.raycastTarget=false;icon.preserveAspect=true;
                RectTransform countFace=Card("Ingredient quantity",cell,new Vector2(40,30),paper,true);
                Anchor(countFace,new Vector2(1,1),new Vector2(1,1),new Vector2(-1,-13));
                Text count=Text(countFace,"",Vector2.zero,new Vector2(40,30),20,ink,TextAnchor.MiddleCenter);
                Text label=Text(cell,"",Vector2.zero,new Vector2(76,30),20,ink,TextAnchor.MiddleCenter);
                Anchor(label.rectTransform,new Vector2(.5f,0),new Vector2(.5f,0),Vector2.zero);
                laneItemIcons.Add(icon);laneItemLabels.Add(label);laneItemCounts.Add(count);cell.gameObject.SetActive(false);
            }
        }
        private void SetLaneIngredients(FoodInput[] inputs)
        {
            int count=inputs==null?0:inputs.Length;
            laneEmptyLabel.gameObject.SetActive(count==0);
            for(int i=0;i<laneItemIcons.Count;i++)
            {
                bool show=i<count;laneItemIcons[i].transform.parent.gameObject.SetActive(show);
                if(!show)continue;
                FoodInput item=inputs[i];
                var material=FreeKitchenRules.FindMaterial(item.itemId);
                laneItemIcons[i].sprite=overlayIcons!=null?overlayIcons.Get(item.itemId):null;
                laneItemIcons[i].color=Color.white;
                Set(laneItemLabels[i],material!=null?material.name:item.itemId);
                Set(laneItemCounts[i],"×"+item.units);
            }
        }
        private void BuildMealCard()
        {
            Vector2 size=new Vector2(360,196);
            mealCard=BuildFixedCard("Selected counter meal card",size);
            mealCard.root.anchorMin=mealCard.root.anchorMax=new Vector2(1,0);mealCard.root.pivot=new Vector2(1,0);mealCard.root.anchoredPosition=new Vector2(-16,110);
        }
        private void RefreshLanePanel(bool paused)
        {
            if(lanePanel==null)return;
            int laneIndex=LanePanelLane();
            bool show=laneIndex>=0&&laneIndex<session.Lanes.Length&&owner.SelectedCounterMealId==""&&!paused&&!HasOverlay;
            lanePanel.root.gameObject.SetActive(show);
            if(!show)return;
            KitchenLane lane=session.Lanes[laneIndex];
            string dish=session.LaneDishName(laneIndex),contents=session.LaneContents(laneIndex);
            var product=session.ProductAtLane(laneIndex);
            bool hasBatch=lane.freeBatch!=null;
            var guide=FreePanGuide.Read(session,laneIndex);
            string method=hasBatch?lane.freeBatch.method??"":"";
            string[] methods=hasBatch?session.FreeMethods(laneIndex):Array.Empty<string>();
            bool methodOpen=methods.Length>0&&lane.step==KitchenStep.Prep;
            bool emergency=product!=null&&session.CanUseEmergencyBag(product);
            bool canPack=false;foreach(string pkg in session.AvailableLanePackages(laneIndex))if(session.CanPackToHand(laneIndex,pkg,out _)){canPack=true;break;}
            string legacyPackage=lane.freeBatch==null&&lane.step==KitchenStep.Pack?session.LanePackages(laneIndex).FirstOrDefault(p=>session.Count(p)>0):null;
            bool freeView=hasBatch||lane.step==KitchenStep.Idle||lane.step==KitchenStep.Dirty;
            Set(lanePanel.title,(laneIndex+1)+"号锅"+(freeView&&guide.state!=""?"  ·  "+guide.state:""));
            SetLaneIngredients(guide.inputs);
            if(freeView)
            {
                string status=guide.next;
                if(emergency&&canPack)status+="\n应急薄袋 · 售出国潮 −5";
                else
                {
                    if(guide.reference!="")status+="\n"+guide.reference;
                    if(product!=null)status+="\n余 "+product.remaining+" 份 · "+CampaignUI.Money(product.priceCents);
                }
                Set(lanePanel.detail,status);
            }
            else
            {
                laneEmptyLabel.gameObject.SetActive(false);
                Set(lanePanel.detail,(dish!=""?dish:"正在制作")+(contents!=""?"\n"+contents:""));
            }
            // 不在每帧重建按钮，避免点击和焦点被刷新打断。
            string key=laneIndex+"|"+lane.step+"|"+string.Join(",",methods)+"|"+method+"|"+methodOpen+"|"+guide.canStart+"|"+canPack+"|"+emergency+"|"+legacyPackage;
            if(key!=lanePanelKey)
            {
                lanePanelKey=key;
                int choices=methodOpen?methods.Length+1:canPack||legacyPackage!=null?1:lane.step==KitchenStep.Utensil?1:0;
                RebuildChoices(lanePanel,new Vector2(420,280),choices);
                int at=0;
                if(methodOpen)
                {
                    foreach(string m in methods)
                    {
                        string captured=m;
                        SetChoice(lanePanel,at++,MethodShortName(m),()=>owner.SetFreeCookingMethod(laneIndex,captured),true,m==method);
                    }
                    bool cold=method=="cold_mix"||method=="muddle"||(method==""&&methods.Length==1&&(methods[0]=="cold_mix"||methods[0]=="muddle"));
                    SetChoice(lanePanel,at++,cold?"制作":"开火",()=>owner.UseCookware(laneIndex),guide.canStart,false);
                }
                else if(canPack)SetChoice(lanePanel,at++,emergency?"应急袋装餐":"装好拿起",()=>owner.PackMeal(laneIndex),true,false);
                else if(legacyPackage!=null)SetChoice(lanePanel,at++,"装好递餐",()=>owner.UsePackage(laneIndex,legacyPackage),true,false);
                else if(lane.step==KitchenStep.Utensil)SetChoice(lanePanel,at++,FreeKitchenRules.IsColdMethod(method)?"调制":"起锅",()=>owner.UseCookware(laneIndex),true,false);
            }
        }
        private int LanePanelLane()
        {
            var input=owner.InputHandler;
            StallInteractionTarget focus=input!=null&&input.FocusedTarget!=null&&input.FocusedTarget.kind==StallInteractionKind.Cookware?input.FocusedTarget:null;
            if(focus!=null)return focus.lane;
            if(owner.SelectedLaneIndex>=0&&owner.SelectedLaneIndex<session.Lanes.Length)return owner.SelectedLaneIndex;
            return -1;
        }
        private static string LimitCells(string value,int max)
        {return value.Length<=max?value:value.Substring(0,max-1)+"…";}
        private static string MethodStatusName(KitchenStep step,string method,bool coldMethod)
        {
            if(step==KitchenStep.Idle||step==KitchenStep.Prep)return (coldMethod?"待制作 · ":"待开火 · ")+MethodShortName(method);
            if(step==KitchenStep.Heating)return (coldMethod?"调制中 · ":"加热中 · ")+MethodShortName(method);
            return MethodShortName(method)+"中";
        }
        private void RefreshMealCard(bool paused)
        {
            if(mealCard==null)return;
            string mealId=owner.SelectedCounterMealId;
            CounterMeal meal=mealId==""?null:session.ReadyMeals.Find(m=>m.id==mealId);
            bool show=meal!=null&&!paused&&!HasOverlay;
            mealCard.root.gameObject.SetActive(show);
            if(!show)return;
            string flavor=meal.flavorId!=null&&meal.flavorId!=""?" · "+StallInteractionInput.FlavorShortName(meal.flavorId):"";
            string name=meal.product!=null&&meal.product.name!=""?meal.product.name:"出餐";
            Set(mealCard.title,name);
            int pickup=owner.PickupOrderForMeal(meal.id);
            int matched=0;
            foreach(var order in session.Orders)
                if(order.arrived&&!order.served&&!order.departed&&session.CanServeCounterMeal(meal.id,order.id,out _))matched++;
            string match=pickup>=0?"顾客正来取餐 · 可继续做菜":matched>0
                ? matched+" 位在等 · 点顾客来取" : StallUiCopy.Text("stall.unmatched","这份还没人点");
            Set(mealCard.detail,"售价 "+CampaignUI.Money(meal.priceCents)+" · 1份\n"+ChipName(meal.packageId)+(flavor!=""?flavor:" · 原味")+(meal.shortSeasoning?" · 缺油盐":"")+"\n"+match);
            string key=meal.id+"|"+name+"|"+meal.priceCents+"|"+flavor+"|"+meal.packageId+"|"+meal.shortSeasoning+"|"+pickup;
            if(key!=mealCardKey)
            {
                mealCardKey=key;
                RebuildChoices(mealCard,new Vector2(360,196),1);
                SetChoice(mealCard,0,pickup>=0?"取消取餐":"取消选餐",()=>{if(pickup>=0)owner.CancelCounterPickup(meal.id);else owner.SelectCounterMeal("");},true,false);
            }
        }
        private Vector2 Clamped(Vector2 value,Vector2 size)
        {
            Rect rect=safe.rect;return new Vector2(Mathf.Clamp(value.x,rect.xMin+size.x*.5f+10,rect.xMax-size.x*.5f-10),Mathf.Clamp(value.y,rect.yMin+size.y*.5f+76,rect.yMax-size.y*.5f-86));
        }
        private void UpdateCursor()
        {
            StallInteractionInput input=owner.InputHandler;
            bool visible=input!=null&&(input.CarriedPackageId!=""||input.DraggedDishLane>=0||input.DraggedFlavorId!=""||input.DraggedIngredientId!="")&&!HasOverlay&&!owner.IsPaused;
            packageCursor.gameObject.SetActive(visible);if(!visible)return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safe,Input.mousePosition,null,out Vector2 at);packageCursor.anchoredPosition=at+new Vector2(64,30);
            string item=input.DraggedIngredientId!=""?input.DraggedIngredientId:input.DraggedFlavorId!=""?input.DraggedFlavorId:input.CarriedPackageId;
            bool dish=input.DraggedDishLane>=0;
            int recipe=dish?session.Lanes[input.DraggedDishLane].recipeIndex:-1;
            cursorDish.sprite=dish?owner.Stage.DishIcon(recipe):overlayIcons.Get(item);
            bool illustrated=cursorDish.sprite!=null;
            cursorDish.gameObject.SetActive(illustrated);
            cursorText.rectTransform.anchoredPosition=new Vector2(illustrated?18:0,0);
            cursorText.rectTransform.sizeDelta=new Vector2(illustrated?88:116,36);
            Set(cursorText,dish?session.LaneDishName(input.DraggedDishLane):ChipName(item));
            packageCursor.SetAsLastSibling();
        }
        private static void Set(Text text,string value){if(text.text!=value)text.text=value;}
        public static string DishName(int index) => index >= 0 && index < BusinessSession.Recipes.Length ? StallMenuReadout.DishTitle(index) : "订单";
        private static string ChipName(string id){switch(id){case "P04":return "纸袋";case "P05":return "餐盒";case "P02":return "泡沫盒";case "P06":return "大餐盒";case "P07":return "汤碗";case "P09":return "醒狮袋";case "seasoning":return "油盐";case "S01":return "辣椒油";case "S02":return "蒜蓉";case "S05":return "甜辣酱";case "prepared":return "料包";case "fruit":return "茶料";default:return ShortItem(id);}}
        // FLAVOR11 订单口味小牌底色：辣油红、蒜蓉米白、甜辣橙，与瓶身一致。
        private static Color FlavorColor(string flavorId){switch(flavorId){case "S01":return CampaignUI.Rust;case "S02":return CampaignUI.Pale;case "S05":return new Color(.95f,.45f,.10f);default:return CampaignUI.PaperColor;}}
        private static string DishGlyph(int index){string[] names={"饭","饭","饼","汤","串","饮"};return index>=0&&index<names.Length?names[index]:"餐";}
        public static string ShortItem(string id)
        {
            switch(id)
            {
                case "rice":return "米饭";case "egg":return "鸡蛋";case "meat":return "鲜肉";case "dough":return "饼胚";case "skewer":return "肉串";case "prepared":return "料理包";case "fruit":return "茶饮料组";case "seasoning":return "油盐酱料";
                case "P02":return "发泡餐盒";case "P03":return "竹签托";case "P04":return "牛皮纸袋";case "P05":return "密封餐盒";case "P06":return "大餐盒";case "P07":return "防烫汤碗";case "P08":return "封口杯";case "P09":return "醒狮纸袋";case "P10":return "纸瓦罐";case "P11":return "竹食盒";case "P12":return "竹筒杯";
            }
            return BusinessSession.Name(id);
        }
        private void OnDestroy()
        {
            if(session!=null)session.SaleCompleted-=OnSaleCompleted;
            foreach(SalePop pop in salePops)if(pop.root!=null)Destroy(pop.root.gameObject);
            salePops.Clear();
            lanePanel=null;mealCard=null;
            if(rounded!=null)Destroy(rounded);if(roundedTexture!=null)Destroy(roundedTexture);
        }
    }
    public sealed class StallSpeechTail : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();Rect rect=rectTransform.rect;
            helper.AddVert(new Vector3(rect.xMin,rect.yMax),color,Vector2.zero);helper.AddVert(new Vector3(rect.xMax,rect.yMax),color,Vector2.right);helper.AddVert(new Vector3(0,rect.yMin),color,Vector2.up);helper.AddTriangle(0,1,2);
        }
    }
}
