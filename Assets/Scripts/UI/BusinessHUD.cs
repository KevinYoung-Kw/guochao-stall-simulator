using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Guochao
{
    public sealed class BusinessHUD : MonoBehaviour
    {
        private readonly Color ink=CampaignUI.Ink,cream=CampaignUI.PaperColor,muted=CampaignUI.Muted,rust=CampaignUI.Rust;
        private readonly List<Sprite> sprites=new List<Sprite>();
        private Font font, displayFont;
        private Sprite receipt,button,held;
        private Sprite roundedPaper;
        private readonly Sprite[] districtPictures=new Sprite[6];
        private Texture2D roundedTexture;
        private int inspectedRecipe=-1;
        private ScrollRect selectionMenuScroll;
        private RectTransform root;
        private GameObject page,pauseShade;
        private CanvasGroup pageInteraction;
        private Button resumeButton;
        private GameObject focusBeforePause;
        private bool wasPaused;
        private CanvasGroup curtain;
        private BusinessController owner;
        private BusinessSession session;
        private Text summary;
        private GameItemIconLibrary itemIcons;
        private RectTransform summaryStock;
        private int stockPage;
        // RISK7 A01：日结实收面板（菜品实收/出餐/漏收 + 钱章），与单据 CommerceReceiptData.Sales 同源。
        private RectTransform summarySales;
        private CommerceReceiptAudit summaryAudit;
        private int salesPage;
        private readonly List<GameObject> stockRows=new List<GameObject>();
        public CommerceReceiptAudit CaptureSettlementReceipt()=>session==null?null:CommerceReceiptData.Sales(session);
        private BusinessWorldHUD worldHUD;
        public bool HasBusinessOverlay=>worldHUD!=null&&worldHUD.HasOverlay;
        public Rect ReadoutScreenRect(StallInteractionTarget target)=>worldHUD!=null?worldHUD.ReadoutScreenRect(target):Rect.zero;
        private readonly Button[] orderButtons=new Button[5];
        private readonly Text[] orderLabels=new Text[5];
        private readonly Image[] patience=new Image[5];
        private readonly int[] orderIds=new int[5];
        private Text[] laneLabels,actionLabels,stepLabels;
        private Button[] laneActions;
        private Image[] cookingProgress;
        private readonly List<Button>[] packButtons={new List<Button>(),new List<Button>()};
        private readonly List<Text>[] packLabels={new List<Text>(),new List<Text>()};
        private int view;
        public void Initialize(BusinessController controller,BusinessSession state)
        {
            owner=controller;session=state;font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            displayFont=Resources.Load<Font>("UI/Fonts/MaShanZheng-Regular") ?? font;
            itemIcons=gameObject.AddComponent<GameItemIconLibrary>();
            roundedPaper=StallUIArt.RoundedPaper(out roundedTexture);sprites.Add(roundedPaper);receipt=button=roundedPaper;held=ClothSkin();
            GameObject go=new GameObject("BusinessCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));go.transform.SetParent(transform,false);
            Canvas canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=350;
            CanvasScaler scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;root=go.GetComponent<RectTransform>();
            ShowSelection();
            pauseShade=Panel("Pause",root,Vector2.zero,new Vector2(1600,900),null,CampaignUI.WithAlpha(ink,.86f)).gameObject;
            Label(pauseShade.transform,"营业已暂停",new Vector2(0,28),new Vector2(800,70),38,cream,TextAnchor.MiddleCenter);
            Label(pauseShade.transform,"按 Esc 继续",new Vector2(0,-40),new Vector2(800,55),23,cream,TextAnchor.MiddleCenter);
            resumeButton=Button(pauseShade.transform,"继续营业",new Vector2(0,-116),new Vector2(290,60),()=>{if(GameFlow.Instance!=null)GameFlow.Instance.SetPaused(false);},out _,false);pauseShade.SetActive(false);
            RectTransform fade=Panel("Street cut",root,Vector2.zero,new Vector2(1600,900),null,ink);curtain=fade.gameObject.AddComponent<CanvasGroup>();curtain.alpha=0;curtain.blocksRaycasts=false;
        }
        private Sprite Skin(string name,bool sliced)
        {
            Texture2D texture=Resources.Load<Texture2D>("UI/Skins/"+name);if(texture==null)return null;
            Sprite sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,sliced?new Vector4(32,32,32,32):Vector4.zero);
            sprites.Add(sprite);return sprite;
        }
        private Sprite ClothSkin()
        {
            Texture2D texture=Resources.Load<Texture2D>("UI/Skins/held");if(texture==null)return null;
            // Use the dark cloth section only; white lettering must never sit on
            // the cream item-thumbnail patch of the original held-item skin.
            Sprite sprite=Sprite.Create(texture,new Rect(texture.width*.40f,texture.height*.12f,texture.width*.59f,texture.height*.76f),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(32,32,32,32));
            sprites.Add(sprite);return sprite;
        }
        private RectTransform Panel(string name,Transform parent,Vector2 at,Vector2 size,Sprite skin,Color color)
        {
            GameObject go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=at;rt.sizeDelta=size;
            Image image=go.GetComponent<Image>();image.type=skin==roundedPaper?Image.Type.Sliced:Image.Type.Simple;
            // These source paintings include transparent margins and a clip. They
            // are decorative assets, not calibrated nine-slice reading surfaces.
            // Keep every card opaque and full-size; never stretch a painted clip.
            image.sprite=skin==roundedPaper?roundedPaper:null;image.color=skin==roundedPaper?(color==Color.white?cream:color):skin==null?color:skin==held?ink:cream;
            if(skin!=null)
            {
                Shadow shadow=go.AddComponent<Shadow>();shadow.effectColor=CampaignUI.WithAlpha(ink,.40f);shadow.effectDistance=new Vector2(3,-5);shadow.useGraphicAlpha=true;
                Outline outline=go.AddComponent<Outline>();outline.effectColor=ink;outline.effectDistance=new Vector2(1.5f,-1.5f);outline.useGraphicAlpha=true;
            }
            return rt;
        }
        private Text Label(Transform parent,string value,Vector2 at,Vector2 size,int fontSize,Color color,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            GameObject go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);RectTransform rt=go.GetComponent<RectTransform>();rt.anchoredPosition=at;rt.sizeDelta=size;
            Text text=go.GetComponent<Text>();text.font=font;text.fontSize=fontSize;text.color=color;text.text=value;text.alignment=alignment;text.raycastTarget=false;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        private Button Button(Transform parent,string value,Vector2 at,Vector2 size,Action pressed,out Text text,bool pale=true)
        {
            RectTransform rt=Panel(value,parent,at,size,button,pale?CampaignUI.Pale:CampaignUI.Vermilion);UnityEngine.UI.Button b=rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic=rt.GetComponent<Image>();b.onClick.AddListener(()=>pressed?.Invoke());
            b.colors=CampaignUI.ButtonColours(b.colors);
            text=Label(rt,value,Vector2.zero,size-new Vector2(32,8),22,pale?ink:cream,TextAnchor.MiddleCenter);text.fontStyle=FontStyle.Bold;return b;
        }
        private void NewPage(string name)
        {if(page!=null){page.SetActive(false);Destroy(page);}page=new GameObject(name,typeof(RectTransform),typeof(CanvasGroup));page.transform.SetParent(root,false);pageInteraction=page.GetComponent<CanvasGroup>();}
        private Sprite DistrictImage(int index)
        {
            if(districtPictures[index]!=null)return districtPictures[index];
            Texture2D atlas=Resources.Load<Texture2D>("UI/Locations/district-atlas-v1");
            if(atlas==null)return null;
            float width=atlas.width/3f,height=atlas.height/2f;
            Sprite sprite=Sprite.Create(atlas,new Rect(index%3*width+3,(1-index/3)*height+3,width-6,height-6),new Vector2(.5f,.5f),100);
            districtPictures[index]=sprite;sprites.Add(sprite);return sprite;
        }
        private Image Picture(Transform parent,string name,Sprite sprite,Vector2 at,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchoredPosition=at;rect.sizeDelta=size;
            Image image=rect.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;return image;
        }
        public void ShowSelection()
        {
            float scrollPosition = view == 0 && selectionMenuScroll != null ? selectionMenuScroll.verticalNormalizedPosition : 1;
            view = 0;
            NewPage("Site and menu");
            RectTransform backdrop = Panel("Street shade", page.transform, Vector2.zero, Vector2.zero, null, CampaignUI.WithAlpha(ink, .87f));
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            RectTransform frame = (RectTransform)page.transform;
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;

            StallDistrict district = session.District;
            string chapter = session.Setup.practice ? "第 12 天 · 晚高峰练习" : "出摊准备";
            Label(page.transform, chapter, new Vector2(-410, 389), new Vector2(676, 36), 24, CampaignUI.Gold).fontStyle = FontStyle.Bold;
            Text title = Label(page.transform, "选个街口，开摊", new Vector2(-412, 324), new Vector2(680, 79), 59, cream);
            title.font = displayFont;
            RectTransform count = StreetSheet(page.transform, "Expected customers", new Vector2(486, 355), new Vector2(536, 86), CreationMenuArt.Sheet.Navy);
            float crowdPerMinute = 60 / Mathf.Max(1, session.Setup.customerIntervalSeconds);
            Label(count, "客流", new Vector2(-171, 12), new Vector2(142, 52), 27, cream).fontStyle = FontStyle.Bold;
            Label(count, "约 " + crowdPerMinute.ToString("0.#") + " 人／分钟", new Vector2(75, 12), new Vector2(308, 51), 26, cream, TextAnchor.MiddleRight);
            Label(count, district.regulation, new Vector2(75, -22), new Vector2(308, 36), 22, CampaignUI.Gold, TextAnchor.MiddleRight);

            Button districtFocus = null;
            for (int i = 0; i < BusinessSession.Districts.Length; i++)
            {
                int index = i;
                bool selected = session.DistrictIndex == index;
                Vector2 center = new Vector2(-606 + i % 3 * 307, 170 - i / 3 * 202);
                RectTransform card = StreetSheet(page.transform, "District " + index, center, new Vector2(290, 187), selected ? CreationMenuArt.Sheet.Red : CreationMenuArt.Sheet.Card);
                Button districtButton = StreetButton(card, () => owner.SelectDistrict(index));
                if (selected) districtFocus = districtButton;
                RectTransform pictureFrame = Panel("Illustration window", card, new Vector2(0, 24), new Vector2(264, 112), null, new Color32(221, 191, 144, 255));
                pictureFrame.GetComponent<Image>().raycastTarget = false;
                pictureFrame.gameObject.AddComponent<RectMask2D>();
                Picture(pictureFrame, "District illustration", DistrictImage(index), Vector2.zero, new Vector2(264, 264));
                Label(card, BusinessSession.Districts[index].name, new Vector2(0, -58), new Vector2(272, 44), 28, selected ? cream : ink, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
                if (selected)
                {
                    RectTransform seal = StreetSheet(card, "Selected district seal", new Vector2(93, 61), new Vector2(81, 38), CreationMenuArt.Sheet.Navy);
                    Label(seal, "在这里", Vector2.zero, new Vector2(74, 33), 21, cream, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
                }
            }
            RectTransform postcard = StreetSheet(page.transform, "District preferences", new Vector2(-299, -222), new Vector2(904, 132), CreationMenuArt.Sheet.Paper);
            Label(postcard, "街坊口味", new Vector2(-330, 30), new Vector2(182, 48), 30, ink).fontStyle = FontStyle.Bold;
            Label(postcard, district.description, new Vector2(-24, 29), new Vector2(385, 52), 24, ink);
            Label(postcard, "手头  " + CampaignUI.Money(session.CashCents), new Vector2(284, 29), new Vector2(265, 48), 25, ink, TextAnchor.MiddleRight).fontStyle = FontStyle.Bold;
            Label(postcard, "菜单只是参考，备好什么做什么。", new Vector2(-24, -28), new Vector2(794, 41), 24, muted);

            RectTransform menu = StreetSheet(page.transform, "Stall menu board", new Vector2(486, -5), new Vector2(536, 563), CreationMenuArt.Sheet.Paper);
            Label(menu, "参考菜谱", new Vector2(-115, 229), new Vector2(260, 56), 36, ink).fontStyle = FontStyle.Bold;
            Label(menu, "参考 " + session.SelectedRecipes.Count + " 道", new Vector2(144, 226), new Vector2(196, 42), 24, rust, TextAnchor.MiddleRight).fontStyle = FontStyle.Bold;
            CampaignUI.Rule(menu, new Vector2(0, 193), new Vector2(468, 2));
            RectTransform dishes = CampaignUI.Scroll(menu, "Available dishes", new Vector2(0, 20), new Vector2(480, 327), out selectionMenuScroll);
            var order = Enumerable.Range(0, BusinessSession.Recipes.Length)
                .OrderBy(i => session.SelectedRecipes.Contains(i) ? 0 : session.Missing(i) == "" ? 1 : 2).ThenBy(i => i).ToArray();
            Button dishFocus = null;
            for (int rowIndex = 0; rowIndex < order.Length; rowIndex++)
            {
                int index = order[rowIndex];
                StallRecipe recipe = BusinessSession.Recipes[index];
                string missing = session.Missing(index);
                bool listed = session.SelectedRecipes.Contains(index);
                var row = StreetSheet(dishes, "Recipe " + index, Vector2.zero, new Vector2(456, 75), listed ? CreationMenuArt.Sheet.Navy : CreationMenuArt.Sheet.Card);
                row.anchorMin = row.anchorMax = new Vector2(.5f, 1);
                row.pivot = new Vector2(.5f, 1);
                row.anchoredPosition = new Vector2(-6, -rowIndex * 82f - 4);
                Button dishButton = StreetButton(row, () =>
                {
                    inspectedRecipe = index;
                    owner.ToggleRecipe(index);
                });
                if (inspectedRecipe == index) dishFocus = dishButton;
                DrawDish(row, index, new Vector2(-179, 0), 61);
                Color colour = listed ? cream : ink;
                Label(row, StallMenuReadout.DishTitle(index), new Vector2(-27, 15), new Vector2(230, 43), 26, colour).fontStyle = FontStyle.Bold;
                string status = missing == "" ? "约 " + StallMenuReadout.Portions(session, index) + " 份" : "待补货";
                Label(row, status + "    " + CampaignUI.Money(recipe.priceCents), new Vector2(-27, -16), new Vector2(230, 34), 21, listed ? cream : muted);
                Text state = Label(row, listed ? "撤参考" : "＋ 参考", new Vector2(143, 0), new Vector2(123, 50), 23, listed ? CampaignUI.Gold : rust, TextAnchor.MiddleCenter);
                state.name = "DishState";
                state.fontStyle = FontStyle.Bold;
            }
            dishes.sizeDelta = new Vector2(0, Mathf.Max(327, order.Length * 82 + 8));
            selectionMenuScroll.verticalNormalizedPosition = scrollPosition;
            if (dishFocus != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                // A changed menu moves the card to its section. Retain focus
                // only if the resulting card is in view.
                float offset = -((RectTransform)dishFocus.transform).anchoredPosition.y;
                float viewedFrom = (dishes.rect.height - 327) * (1 - scrollPosition);
                if (offset < viewedFrom || offset + 75 > viewedFrom + 327) dishFocus = null;
            }

            // 菜单只是偏好参考：开门不设任何选菜门槛，街坊照来点单。
            string detail = session.SelectedRecipes.Count == 0 ? "菜单只是参考 · 备好食材就能开门迎客。" : "选几道作参考 · 街坊照来点单。";
            if (inspectedRecipe >= 0 && inspectedRecipe < BusinessSession.Recipes.Length)
            {
                string missing = session.Missing(inspectedRecipe);
                detail = missing == "" ? session.Message : StallMenuReadout.DishTitle(inspectedRecipe) + "还缺 " + missing;
                if (string.IsNullOrWhiteSpace(detail)) detail = "菜单仅作参考 · 不选菜也能开摊。";
            }
            Text note = Label(menu, detail, new Vector2(0, -194), new Vector2(458, 92), 23, ink);
            note.lineSpacing = 1.12f;

            var back = FootButton(session.Setup.practice ? "返回开局" : session.Setup.canReturnMarket ? "回超市补货" : "采购已结束",
                new Vector2(-635, -359), 238, false, session.Setup.practice ? (Action)(() => GameFlow.Instance?.OpenCharacterCreation()) : owner.ReturnMarket);
            back.interactable = session.Setup.practice ? GameFlow.Instance != null : session.Setup.canReturnMarket;
            if (!session.Setup.practice) FootButton("今天不开摊", new Vector2(-375, -359), 236, false, owner.CloseDay);
            Button open = FootButton(session.Setup.practice ? "开始练习" : "开门迎客", new Vector2(490, -359), 532, true, owner.Open);
            RestoreOverlays();
            CampaignUI.Focus(dishFocus ?? districtFocus ?? open);
        }

        private RectTransform StreetSheet(Transform parent, string name, Vector2 at, Vector2 size, CreationMenuArt.Sheet material)
        {
            RectTransform rect = CreationMenuArt.Panel(parent, name, 0, 0, size.x, size.y, material);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = at;
            return rect;
        }

        private static Button StreetButton(RectTransform rect, Action pressed)
        {
            Image image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CampaignUI.ButtonColours(button.colors);
            button.onClick.AddListener(() => pressed());
            CreationHoverLift.Attach(rect, CreationMenuArt.Frame(rect));
            return button;
        }

        private Button FootButton(string text, Vector2 at, float width, bool primary, Action pressed)
        {
            RectTransform face = StreetSheet(page.transform, text, at, new Vector2(width, 70), primary ? CreationMenuArt.Sheet.Red : CreationMenuArt.Sheet.Card);
            Label(face, text, Vector2.zero, new Vector2(width - 24, 58), primary ? 35 : 28, primary ? cream : ink, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            return StreetButton(face, pressed);
        }

        private void DrawDish(Transform parent, int index, Vector2 at, float size)
        {
            Sprite sprite = owner.Stage?.DishIcon(index);
            if (sprite != null)
            {
                Picture(parent, "Dish illustration", sprite, at, new Vector2(size, size));
                return;
            }
            RectTransform shape = new GameObject("Dish illustration", typeof(RectTransform), typeof(StallUIIcon)).GetComponent<RectTransform>();
            shape.SetParent(parent, false);
            shape.anchoredPosition = at;
            shape.sizeDelta = new Vector2(size, size);
            StallUIIcon icon = shape.GetComponent<StallUIIcon>();
            icon.index = StallMenuReadout.IconIndex(index);
            icon.raycastTarget = false;
        }

        public void ShowBusiness()
        {
            view = 1;
            NewPage("Operating objects HUD");
            worldHUD = page.AddComponent<BusinessWorldHUD>();
            worldHUD.Initialize(owner, session, root, font);
            if (session.Elapsed < .1f) StartCoroutine(ShowStreetArrival(page.transform));
            RestoreOverlays();
            Refresh(false);
        }

        private IEnumerator ShowStreetArrival(Transform host)
        {
            // The same 3D street remains visible through the sign's entrance and exit.
            RectTransform sign = StreetSheet(host, "Street arrival", new Vector2(0, 103), new Vector2(714, 190), CreationMenuArt.Sheet.Navy);
            var group = sign.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0;
            Label(sign, session.Setup.practice ? "晚高峰练习" : "开摊啦", new Vector2(0, 48), new Vector2(640, 49), 28, CampaignUI.Gold, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            Text title = Label(sign, session.District.name, new Vector2(0, -19), new Vector2(664, 91), 62, cream, TextAnchor.MiddleCenter);
            title.font = displayFont;
            float age = 0;
            while (sign != null && age < 2.05f)
            {
                float fadeIn = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .25f) / .31f));
                float fadeOut = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - 1.51f) / .54f));
                group.alpha = fadeIn * fadeOut;
                sign.anchoredPosition = new Vector2(0, 103 + (1 - fadeIn) * 18 + (1 - fadeOut) * 12);
                age += Time.unscaledDeltaTime;
                yield return null;
            }
            if (sign != null) Destroy(sign.gameObject);
        }
        public void ShowSummary()
        {
            view=2;stockPage=0;salesPage=0;NewPage("Day settlement");
            Panel("Shade",page.transform,Vector2.zero,new Vector2(2400,1500),null,CampaignUI.WithAlpha(ink,.72f));
            RectTransform paper=Panel("Closing receipt",page.transform,Vector2.zero,new Vector2(1160,750),receipt,Color.white);
            Label(paper,session.Setup.practice?"第 12 天 · 试营业账本":"今天，收摊啦",new Vector2(-200,310),new Vector2(650,65),34,ink).fontStyle=FontStyle.Bold;
            // A01：已出餐待收款 / 漏收时不能说“全部收款已入账”——按真实债权状态写账期提示。
            int pendingRisk=session.PendingRiskCents,lostRisk=session.LostRevenueCents;
            string collectStatus=pendingRisk>0?"部分账款待追回":lostRisk>0?"有账款没能收回":"账款已结清";
            Color collectColor=pendingRisk>0||lostRisk>0?rust:muted;
            Label(paper,session.District.name+"  ·  "+collectStatus,new Vector2(-200,258),new Vector2(650,40),20,collectColor);
            CommerceReceiptAudit audit=CommerceReceiptData.Sales(session);
            summaryAudit=audit;
            RectTransform sales=Panel("What we sold",paper,new Vector2(-215,-10),new Vector2(650,460),roundedPaper,CampaignUI.Background);
            summarySales=sales;
            RefreshSummarySales();
            RectTransform amount=Panel("Revenue actually received",paper,new Vector2(347,244),new Vector2(365,175),roundedPaper,ink);
            Label(amount,"营业实收",new Vector2(0,60),new Vector2(315,34),21,cream);
            Label(amount,CommerceReceiptData.Money(session.RevenueCents),new Vector2(0,14),new Vector2(315,62),42,cream).fontStyle=FontStyle.Bold;
            Label(amount,"出餐 "+session.Served+" 单 · 实收入账",new Vector2(0,-38),new Vector2(315,32),18,cream);
            if(pendingRisk>0||lostRisk>0)
            {
                string riskNote=pendingRisk>0?"待追回 "+CommerceReceiptData.Money(pendingRisk):"";
                if(lostRisk>0)riskNote+=(riskNote==""?"":"  ·  ")+"漏收 "+CommerceReceiptData.Money(lostRisk);
                // 只陈述未收/漏收面额，不做负现金扣款措辞。
                Label(amount,riskNote,new Vector2(0,-68),new Vector2(315,28),16,CampaignUI.Gold);
            }
            summaryStock=Panel("Stock carried home",paper,new Vector2(347,-65),new Vector2(365,413),roundedPaper,CampaignUI.Background);
            RefreshSummaryStock();
            Label(paper,"开摊现金  "+CommerceReceiptData.Money(session.Setup.cashCents)+"   +   营业实收  "+CommerceReceiptData.Money(session.RevenueCents),new Vector2(-215,-268),new Vector2(650,34),18,muted);
            Label(paper,"营业结束现金  "+CommerceReceiptData.Money(session.CashCents),new Vector2(-215,-306),new Vector2(650,46),29,ink).fontStyle=FontStyle.Bold;
            string summaryNote=session.UnseasonedPortionsServed>0?"缺油盐成交 "+session.UnseasonedPortionsServed+" 份 · 诚信 "+session.IntegrityChange
                : session.RevenueCents>0?StallUiCopy.Text("closing.sold_flavor","收好账，今天收工"):"";
            if(session.EmergencyPackagesSold>0)
            {
                string emergencyNote="应急薄袋 "+session.EmergencyPackagesSold+" 份 · 国潮 "+session.EmergencyPackagingScore;
                summaryNote=session.UnseasonedPortionsServed>0?summaryNote+" · "+emergencyNote:emergencyNote;
            }
            if(summaryNote==""&&session.Served==0&&session.WastedPortions==0)
            {
                bool usedAnything=false;
                foreach(var item in session.Setup.inventory) if(session.Count(item.Key)<item.Value){usedAnything=true;break;}
                if(!usedAnything)summaryNote=StallUiCopy.Text("closing.no_sales_flavor","今天先收到这儿");
            }
            if(summaryNote!="")Label(paper,summaryNote,new Vector2(-215,-347),new Vector2(650,30),18,session.UnseasonedPortionsServed>0?rust:muted);
            Button continueButton=Button(paper,session.Setup.practice?"重开练习":"收好账本 · 回家",new Vector2(347,-320),new Vector2(365,58),owner.Continue,out _,false);continueButton.interactable=owner.CanContinue;
            if(!audit.totalsMatch)Debug.LogError("Settlement receipt sales do not match authoritative revenue.");
            RestoreOverlays();
            if(EventSystem.current!=null&&continueButton.IsInteractable())EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
            var motion=paper.gameObject.AddComponent<HUDSoftMotion>();motion.Play(true);
        }
        private void SummaryIcon(Transform parent,Sprite sprite,Vector2 position,Vector2 size)
        {
            RectTransform rt=new GameObject("Actual item portrait",typeof(RectTransform),typeof(Image)).GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchoredPosition=position;rt.sizeDelta=size;
            Image image=rt.GetComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.raycastTarget=false;image.enabled=sprite!=null;
        }
        // The receipt shows the real dish and cash from each delivery. A fixed
        // totals row leaves room for paging through sold and unpaid orders.
        private void RefreshSummarySales()
        {
            bool restoreFocus=EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(summarySales);
            foreach(Transform child in summarySales){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            Label(summarySales,"今日出餐",new Vector2(-12,195),new Vector2(570,43),23,ink).fontStyle=FontStyle.Bold;
            Label(summarySales,"出餐 · 实收",new Vector2(70,195),new Vector2(280,36),17,muted,TextAnchor.MiddleRight);
            CommerceReceiptAudit audit=summaryAudit;
            const int perPage=5;
            if(audit.lines.Length==0)
            {
                Label(summarySales,"今天还没卖出菜品",new Vector2(0,35),new Vector2(560,48),25,ink,TextAnchor.MiddleCenter).fontStyle=FontStyle.Bold;
                Label(summarySales,"没有产生营业实收",new Vector2(0,-12),new Vector2(560,40),20,muted,TextAnchor.MiddleCenter);
                return;
            }
            int pages=Mathf.Max(1,(audit.lines.Length+perPage-1)/perPage);salesPage=Mathf.Clamp(salesPage,0,pages-1);
            for(int row=0;row<perPage;row++)
            {
                int i=salesPage*perPage+row;if(i>=audit.lines.Length)break;
                CommerceReceiptLine line=audit.lines[i];float y=132-row*59;
                bool known=line.recipeIndex>=0&&line.recipeIndex<BusinessSession.Recipes.Length;
                var icon=known?owner.Stage.DishIcon(line.recipeIndex):null;
                if(icon!=null) SummaryIcon(summarySales,icon,new Vector2(-273,y),new Vector2(52,52));
                else
                {
                    SelectionSymbol bowl=SelectionSymbol.Create(summarySales,"餐品",SelectionSymbolKind.HobbySoup,new Vector2(49,49));
                    ((RectTransform)bowl.transform).anchoredPosition=new Vector2(-273,y);
                }
                Text dish=Label(summarySales,line.name,new Vector2(-58,y+11),new Vector2(360,36),21,ink);
                dish.fontStyle=FontStyle.Bold;dish.horizontalOverflow=HorizontalWrapMode.Overflow;
                dish.resizeTextForBestFit=true;dish.resizeTextMinSize=15;dish.resizeTextMaxSize=21;
                // Keep a single predictable row even when future combinations
                // produce names longer than today's standard recipes.
                if(dish.preferredWidth>dish.rectTransform.rect.width) { dish.horizontalOverflow=HorizontalWrapMode.Wrap; }
                int expected=line.quotedCents<0?line.subtotalCents:line.quotedCents*line.quantity;
                bool exact=line.subtotalCents==expected;
                string state=exact?StallUiCopy.Text("closing.paid","已收款")
                    : line.subtotalCents==0?StallUiCopy.Text("closing.unpaid","未收到款")
                    : line.subtotalCents>expected?StallUiCopy.Text("closing.extra","追回款已入账")
                    : StallUiCopy.Text("closing.partial","只收到部分款");
                string remark=line.quantity+" 份 · "+state+(exact?"":" · 应收 "+CommerceReceiptData.Money(expected));
                Label(summarySales,remark,new Vector2(-40,y-16),new Vector2(396,27),18,exact?muted:rust);
                Label(summarySales,CommerceReceiptData.Money(line.subtotalCents),new Vector2(235,y),new Vector2(138,42),24,exact?ink:rust,TextAnchor.MiddleRight).fontStyle=FontStyle.Bold;
            }
            const float totalY=-158;
            SelectionSymbol stamp=SelectionSymbol.Create(summarySales,"Revenue total stamp",SelectionSymbolKind.MetaCoin,new Vector2(30,30));
            RectTransform stampRect=(RectTransform)stamp.transform;stampRect.anchorMin=stampRect.anchorMax=new Vector2(.5f,.5f);stampRect.anchoredPosition=new Vector2(-236,totalY+8);
            stamp.color=CampaignUI.Gold;stamp.raycastTarget=false;
            Label(summarySales,"实收合计",new Vector2(-116,totalY+8),new Vector2(240,32),20,muted);
            Label(summarySales,CommerceReceiptData.Money(audit.totalCents),new Vector2(235,totalY),new Vector2(138,42),26,ink,TextAnchor.MiddleRight).fontStyle=FontStyle.Bold;
            if(pages>1)
            {
                Button(summarySales,"‹",new Vector2(-150,-205),new Vector2(48,35),()=>{salesPage--;RefreshSummarySales();},out _).interactable=salesPage>0;
                Button(summarySales,"›",new Vector2(150,-205),new Vector2(48,35),()=>{salesPage++;RefreshSummarySales();},out _).interactable=salesPage+1<pages;
                Label(summarySales,"菜品 "+(salesPage+1)+" / "+pages,new Vector2(0,-205),new Vector2(180,35),16,muted,TextAnchor.MiddleCenter);
            }
            if(restoreFocus&&EventSystem.current!=null)foreach(Button choice in summarySales.GetComponentsInChildren<Button>())if(choice.IsInteractable()){EventSystem.current.SetSelectedGameObject(choice.gameObject);break;}
        }
        private void RefreshSummaryStock()
        {
            bool restoreFocus=EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(summaryStock);
            foreach(Transform child in summaryStock){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            Label(summaryStock,"收摊结余",new Vector2(0,174),new Vector2(317,39),23,ink).fontStyle=FontStyle.Bold;
            Label(summaryStock,"未完成订单 "+session.Lost+" 单 · 糊锅 "+session.WastedPortions+" 份",new Vector2(0,136),new Vector2(317,36),17,muted);
            var stocks=new List<System.Collections.Generic.KeyValuePair<string,int>>();
            foreach(var item in session.Inventory)if(item.Value>0)stocks.Add(item);
            stocks.Sort((a,b)=>string.CompareOrdinal(a.Key,b.Key));int pages=Mathf.Max(1,(stocks.Count+3)/4);stockPage=Mathf.Clamp(stockPage,0,pages-1);
            for(int i=0;i<4&&stockPage*4+i<stocks.Count;i++)
            {
                var item=stocks[stockPage*4+i];float y=83-i*49;
                SummaryIcon(summaryStock,itemIcons.Get(item.Key),new Vector2(-133,y),new Vector2(43,43));
                Label(summaryStock,CommerceReceiptData.ShortName(item.Key),new Vector2(-4,y),new Vector2(193,39),18,ink);
                Label(summaryStock,item.Value+CommerceReceiptData.StockUnit(item.Key),new Vector2(120,y),new Vector2(74,38),19,ink,TextAnchor.MiddleRight).fontStyle=FontStyle.Bold;
            }
            if(stocks.Count==0)Label(summaryStock,"原料与包材已经用完",new Vector2(0,22),new Vector2(317,40),20,muted,TextAnchor.MiddleCenter);
            if(pages>1)
            {
                Button(summaryStock,"‹",new Vector2(-128,-117),new Vector2(48,35),()=>{stockPage--;RefreshSummaryStock();},out _).interactable=stockPage>0;
                Button(summaryStock,"›",new Vector2(128,-117),new Vector2(48,35),()=>{stockPage++;RefreshSummaryStock();},out _).interactable=stockPage+1<pages;
            }
            Label(summaryStock,"原料 / 包材  "+(stockPage+1)+" / "+pages,new Vector2(0,-117),new Vector2(220,35),16,muted,TextAnchor.MiddleCenter);
            Label(summaryStock,"罐 "+session.Gas+" · 卡式 "+session.Cartridge+" · 煤 "+session.Coal+" · 电 "+session.Electricity,new Vector2(0,-157),new Vector2(317,33),18,muted,TextAnchor.MiddleCenter);
            int cooked=0;
            foreach(KitchenLane lane in session.Lanes)
                if(lane.freeBatch?.product!=null)cooked+=lane.freeBatch.product.remaining;
                else if(lane.freeBatch==null&&(lane.step==KitchenStep.Pack||lane.step==KitchenStep.ReadyStock||lane.step==KitchenStep.Season))cooked+=lane.batchRemaining;
            string unsold=StallUiCopy.Format("closing.unsold","没卖完 · 锅里 {0} 份 / 已装盒 {1} 份",cooked,session.ReadyMeals.Count+(session.HeldMeal!=null?1:0));
            Label(summaryStock,unsold,new Vector2(0,-185),new Vector2(330,28),16,muted,TextAnchor.MiddleCenter);
            if(restoreFocus&&EventSystem.current!=null)foreach(Button choice in summaryStock.GetComponentsInChildren<Button>())if(choice.IsInteractable()){EventSystem.current.SetSelectedGameObject(choice.gameObject);break;}
        }
        private void RestoreOverlays()
        {
            if(pauseShade!=null)pauseShade.transform.SetAsLastSibling();if(curtain!=null)curtain.transform.SetAsLastSibling();
            if(EventSystem.current!=null)
            {
                if(view==1&&worldHUD!=null)worldHUD.FocusDefault();
                else foreach(Selectable selectable in page.GetComponentsInChildren<Selectable>())if(selectable.IsInteractable()){EventSystem.current.SetSelectedGameObject(selectable.gameObject);break;}
            }
        }
        public void Refresh(bool paused)
        {
            bool managing=worldHUD!=null&&worldHUD.HasOverlay;
            if(pauseShade!=null)pauseShade.SetActive(false);
            if(pageInteraction!=null){pageInteraction.interactable=!paused||managing;pageInteraction.blocksRaycasts=!paused||managing;}
            if(paused&&!managing)CampaignMenuUI.Open(GameFlow.Instance);
            else if(!paused)CampaignMenuUI.Close();
            wasPaused=paused;
            if(view==1&&worldHUD!=null)worldHUD.Refresh(paused);
        }
        private static string ShortPack(string id)
        {switch(id){case "P02":return "发泡盒";case "P03":return "竹签托";case "P04":return "牛皮袋";case "P05":return "密封盒";case "P06":return "大餐盒";case "P07":return "防烫碗";case "P08":return "封口杯";case "P09":return "醒狮袋";case "P10":return "纸瓦罐";case "P11":return "竹食盒";case "P12":return "竹筒杯";}return id;}
        public void SetCurtain(float alpha){curtain.alpha=alpha;curtain.blocksRaycasts=alpha>.01f;}
        private void OnDestroy(){foreach(Sprite sprite in sprites)if(sprite!=null)Destroy(sprite);if(roundedTexture!=null)Destroy(roundedTexture);}
    }
    // A compact semantic arrival pictogram, separate from generated place art.
    public sealed class BusinessArrivalIcon : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper h)
        {
            h.Clear();Rect r=rectTransform.rect;
            for(int person=0;person<3;person++)
            {
                float x=r.xMin+r.width*(.18f+person*.32f),y=r.center.y;
                Color colour=person==1?CampaignUI.Vermilion:CampaignUI.Ink;
                for(int i=0;i<18;i++)
                {float a=i*Mathf.PI/9,b=(i+1)*Mathf.PI/9;Tri(h,new Vector2(x,y+9),new Vector2(x+Mathf.Cos(a)*6,y+9+Mathf.Sin(a)*6),new Vector2(x+Mathf.Cos(b)*6,y+9+Mathf.Sin(b)*6),colour);}
                Tri(h,new Vector2(x-8,y-15),new Vector2(x+8,y-15),new Vector2(x+7,y),colour);
                Tri(h,new Vector2(x-8,y-15),new Vector2(x+7,y),new Vector2(x-7,y),colour);
            }
        }
        private static void Tri(VertexHelper h,Vector2 a,Vector2 b,Vector2 c,Color colour)
        {int n=h.currentVertCount;h.AddVert(a,colour,Vector2.zero);h.AddVert(b,colour,Vector2.zero);h.AddVert(c,colour,Vector2.zero);h.AddTriangle(n,n+1,n+2);}
    }


    internal static class StallMenuReadout
    {
        public static int IconIndex(int index)
        {
            switch (BusinessSession.Recipes[index].id)
            {
                case "plain_rice": case "egg_greens": case "meat_bites": return 0;
                case "plain_flatbread": return 2;
                default: return index;
            }
        }

        public static string DishTitle(int index)
        {
            switch (BusinessSession.Recipes[index].id)
            {
                case "plain_rice": return "素炒饭";
                case "plain_flatbread": return "葱香手抓饼";
                case "egg_greens": return "青菜滑蛋";
                case "meat_bites": return "鲜肉小炒";
                case "fried_rice": return "蛋炒饭";
                case "prepared_rice": return "料理盖饭";
                case "flatbread": return "手抓饼";
                case "soup": return "鱼蛋热汤";
                case "fried_skewer": return "炸串";
                case "plum_drink": return "桂花酸梅汤";
                case "lemon_tea": return "手锤柠檬茶";
                case "rose_jelly": return "玫瑰冰粉";
                default: return BusinessSession.Recipes[index].name;
            }
        }

        public static int Portions(BusinessSession session, int index)
        {
            StallRecipe recipe = BusinessSession.Recipes[index];
            int raw = int.MaxValue;
            foreach (var item in recipe.ingredients)
                if (!SeasoningShortageRules.IsBaseSeasoning(item.Key))
                    raw = Mathf.Min(raw, session.Count(item.Key) / item.Value);
            if (raw == int.MaxValue) raw = 0;
            int packaging = 0;
            foreach (string item in recipe.packages) packaging += session.Count(item);
            int cooked = 0;
            foreach (KitchenLane lane in session.Lanes)
                if (lane.recipeIndex == index && lane.step != KitchenStep.Dirty)
                    cooked += lane.batchRemaining;
            return Mathf.Min(raw + cooked, packaging);
        }
    }

}
