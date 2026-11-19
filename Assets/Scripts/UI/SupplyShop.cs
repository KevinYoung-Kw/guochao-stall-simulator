using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public sealed class SupplyShop : MonoBehaviour
    {
        public static bool IsOpen => active!=null;
        private static SupplyShop active;
        private GameFlow flow;
        private Text total,advice,timeLeft,subtitle,browseHint;
        private RectTransform list;
        private Font font;
        private GameItemIconLibrary icons;
        private float nextRefresh;
        private RectTransform browseFrame,browseRail,resalePanel,confirmation;
        private Text confirmationStatus;
        private Button confirmationCommit;
        private RecoveryQuote pendingBuyQuote;
        private Button resaleTab,returnHome,starter;
        private bool selling,buying,handingOver;
        private int resalePage;
        private string resaleSignature="";
        private string tradeFeedback="";
        // Player-legible outcome of the last 补货参考 click. Shown in the
        // advice line until the player changes category, edits the basket, or
        // leaves the counter; a silent partial fill was one of the SHOP7
        // "bought it all but the basket disagreed" breakpoints.
        private string starterFeedback="";
        private string category="补小份";
        // BILL9 精选分类：补小份 / 包材 / 五金工具 / 灶具锅具 / 能源。
        // 整箱原料不在账单里——它们走货架亲手搬那条路。
        // FLAVOR11: 调味酱料 joins as the sixth tab (S01/S02/S05 bottles); tab
        // spacing is tightened so all six stay inside the 990px sheet.
        private static readonly string[] Categories={"补小份","包材","五金工具","灶具锅具","能源","调味酱料"};
        private readonly List<Button> categoryTabs=new List<Button>();
        private readonly List<ProductRow> rows=new List<ProductRow>();
        private readonly Color ink=CampaignUI.Ink,paper=CampaignUI.PaperColor;
        private sealed class ProductRow
        {
            public ItemDefinition item;
            public Image portrait,face;
            public Text price,quantity,state,action;
            public Button button;
            // FREEDOM19: repeatable goods get a second, independent
            // remove-one action next to the add button.
            public Button minus;
            public Text minusLabel;
        }
        public static void Open(GameFlow flow)
        {
            Open(flow,false);
        }
        // RECOVERY12: E at the authored recovery table enters the same sheet
        // straight into the 旧货代收 page; the procurement counter keeps the
        // normal browse landing. Pricing, receipt and save rules are shared.
        public static void Open(GameFlow flow,bool openResale)
        {
            if(active)return;active=new GameObject("HardwareSupplyShop").AddComponent<SupplyShop>();active.flow=flow;active.Build();
            if(openResale&&active!=null)active.ShowResale(true);
        }
        public static void Close(){if(!active)return;var old=active;active=null;old.gameObject.SetActive(false);Destroy(old.gameObject);}
        private void Build()
        {
            font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");icons=gameObject.AddComponent<GameItemIconLibrary>();
            var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=410;
            gameObject.AddComponent<GraphicRaycaster>();var scale=gameObject.AddComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1600,900);
            RectTransform root=GetComponent<RectTransform>();var shade=Box(root,"Shade",0,0,1600,900,CampaignUI.WithAlpha(CampaignUI.Pale,220/255f));shade.GetComponent<Image>().raycastTarget=true;
            Box(root,"SheetShadow",270,85,1080,750,new Color(0,0,0,.3f));
            var panel=Box(root,"OrderSheet",260,75,1080,750,paper);
            Box(panel,"HeaderRule",52,150,976,2,CampaignUI.Gold);
            Label(panel,"Title","采购柜台 · 账单直购",52,43,560,56,32,ink).fontStyle=FontStyle.Bold;
            // Two-channel legend, readable the moment the panel opens: a shelf
            // carton icon marks the physical shelf route, a packaging icon marks
            // this bill route. One glance, not a wall of text.
            LegendChip(panel,"Shelf channel hint","rice","货架整箱",632,47);
            LegendChip(panel,"Counter channel hint","P02","柜台直购",744,47);
            subtitle=Label(panel,"Subtitle","此处账单直购五金/包材/散称小份，收银台统一结账；整箱原料在货架亲手搬。",52,105,740,38,20,ink);
            timeLeft=Label(panel,"ProcurementClock","",800,105,220,38,20,ink);timeLeft.alignment=TextAnchor.MiddleRight;
            Button(panel,"Close","收起 ×",865,40,150,50,Close);
            for(int i=0;i<Categories.Length;i++)
            {
                string next=Categories[i];
                categoryTabs.Add(Button(panel,"Category "+next,next,55+i*160,170,152,38,()=>{category=next;starterFeedback="";ApplyCategory();Refresh();}));
            }
            var frame=Box(panel,"Browse current merchandise",45,222,990,365,Color.clear);browseFrame=frame;
            var viewport=Node(frame,"Clipped merchandise viewport");viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=Vector2.zero;viewport.offsetMax=new Vector2(-20,0);viewport.gameObject.AddComponent<RectMask2D>();
            list=Node(viewport,"Merchandise rows");list.anchorMin=new Vector2(0,1);list.anchorMax=Vector2.one;list.pivot=new Vector2(0,1);list.anchoredPosition=Vector2.zero;list.sizeDelta=new Vector2(0,Mathf.CeilToInt(SupplyCatalog.Items.Length/2f)*120);
            var scroll=frame.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=list;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=45;scroll.inertia=true;
            var rail=Box(frame,"Browse rail",978,0,10,365,CampaignUI.WithAlpha(ink,.12f));browseRail=rail;
            var thumb=Node(rail,"Browse thumb");thumb.anchorMin=Vector2.zero;thumb.anchorMax=Vector2.one;thumb.offsetMin=thumb.offsetMax=Vector2.zero;
            Image thumbImage=thumb.gameObject.AddComponent<Image>();thumbImage.color=CampaignUI.Gold;
            var scrollbar=rail.gameObject.AddComponent<Scrollbar>();scrollbar.handleRect=thumb;scrollbar.targetGraphic=thumbImage;scrollbar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            for(int i=0;i<SupplyCatalog.Items.Length;i++)BuildRow(SupplyCatalog.Items[i],i,scroll);
            ApplyCategory();
            resalePanel=Box(panel,"Owned equipment resale",45,167,990,420,CampaignUI.Background);resalePanel.gameObject.SetActive(false);
            resaleTab=Button(panel,"ResaleTab","旧货代收",55,595,210,48,()=>ShowResale(!selling));
            browseHint=Label(panel,"BrowseHint","滚轮浏览 · "+SupplyCatalog.Items.Length+" 件商品",282,600,285,35,18,CampaignUI.Muted);
            starter=Button(panel,"Starter","补货参考",580,595,360,48,AddStarter);
            total=Label(panel,"Total","",55,650,950,40,23,ink);
            advice=Label(panel,"Advice","",55,694,950,52,17,ink);advice.horizontalOverflow=HorizontalWrapMode.Wrap;advice.verticalOverflow=VerticalWrapMode.Truncate;
            returnHome=Button(panel,"Paid goods home","带已付货物回家",55,540,290,46,ConfirmReturnHome);returnHome.gameObject.SetActive(false);
            Refresh();StartCoroutine(LoadPortraits());
        }
        private void LegendChip(Transform parent,string name,string iconId,string text,float x,float y)
        {
            var chip=Box(parent,name,x,y,104,42,CampaignUI.Pale);
            var iconBox=Node(chip,"Channel icon");iconBox.anchorMin=iconBox.anchorMax=iconBox.pivot=new Vector2(0,.5f);iconBox.anchoredPosition=new Vector2(5,0);iconBox.sizeDelta=new Vector2(32,32);
            Image icon=iconBox.gameObject.AddComponent<Image>();icon.preserveAspect=true;icon.raycastTarget=false;
            Sprite sprite=icons.Get(iconId);icon.sprite=sprite;icon.enabled=sprite!=null;
            var label=Label(chip,"Channel label",text,40,5,62,32,15,ink);label.alignment=TextAnchor.MiddleLeft;
        }
        private void BuildRow(ItemDefinition item,int index,ScrollRect scroll)
        {
            float x=index%2*485,y=index/2*120;
            RectTransform card=Box(list,"Product "+item.id,x,y,475,112,CampaignUI.Background);card.GetComponent<Image>().raycastTarget=false;
            var portraitBox=Box(card,"Product photograph",10,13,86,86,paper);portraitBox.GetComponent<Image>().raycastTarget=false;
            var imageRect=Node(portraitBox,"Portrait "+item.id);imageRect.anchorMin=Vector2.zero;imageRect.anchorMax=Vector2.one;imageRect.offsetMin=Vector2.one*3;imageRect.offsetMax=Vector2.one*-3;
            Image portrait=imageRect.gameObject.AddComponent<Image>();portrait.preserveAspect=true;portrait.raycastTarget=false;portrait.enabled=false;
            string title=item.id.StartsWith("P")||item.id.EndsWith("_small")?item.name.Split('·')[0]:item.name;
            Text name=Label(card,"Name",title,108,8,350,38,21,ink);name.fontStyle=FontStyle.Bold;name.resizeTextForBestFit=true;name.resizeTextMinSize=18;name.resizeTextMaxSize=21;
            var row=new ProductRow{item=item,portrait=portrait,face=card.GetComponent<Image>()};
            row.quantity=Label(card,"Unit size","",108,44,226,29,17,CampaignUI.Muted);
            row.price=Label(card,"Actual price","",108,76,126,31,21,ink);row.price.fontStyle=FontStyle.Bold;
            row.state=Label(card,"Owned or basket state","",238,76,106,30,16,CampaignUI.Muted);
            // FREEDOM19: repeatable goods always ADD one more real unit with
            // the primary button (removal is the separate − button); durable
            // hardware keeps its legacy toggle flow (add when absent, put
            // back the one box when already in the basket).
            row.button=Button(card,"Pick "+item.id,"放入车",350,62,112,42,()=>{starterFeedback="";bool repeatable=RunState.IsRepeatableSupply(item.id);flow.ChangeSupply(item.id,!repeatable&&flow.Run.Cart.Contains(item.id));Refresh();});row.action=row.button.GetComponentInChildren<Text>();
            row.minus=Button(card,"RemoveOne "+item.id,"− 放回一件",350,12,112,42,()=>{starterFeedback="";flow.ChangeSupply(item.id,true);Refresh();});
            row.minusLabel=row.minus.GetComponentInChildren<Text>();
            var reveal=row.button.gameObject.AddComponent<SupplyRowFocus>();reveal.scroll=scroll;reveal.row=card;
            rows.Add(row);
        }
        private void ApplyCategory()
        {
            int visible=0;
            foreach(ProductRow row in rows)
            {
                bool show=SupplyCatalog.Category(row.item.id)==category;
                row.face.gameObject.SetActive(show);
                if(!show)continue;
                row.face.rectTransform.anchoredPosition=new Vector2(visible%2*485,-(visible/2*120));
                visible++;
            }
            list.sizeDelta=new Vector2(0,Mathf.CeilToInt(visible/2f)*120);list.anchoredPosition=Vector2.zero;
            if(browseFrame!=null)browseFrame.GetComponent<ScrollRect>().velocity=Vector2.zero;
            for(int i=0;i<categoryTabs.Count;i++)
            {bool selected=Categories[i]==category;categoryTabs[i].GetComponent<Image>().color=selected?CampaignUI.Vermilion:CampaignUI.Pale;categoryTabs[i].GetComponentInChildren<Text>().color=selected?paper:ink;}
            if(browseHint!=null)browseHint.text=category+" · "+visible+" 种";
        }
        private IEnumerator LoadPortraits()
        {
            // Fill the first visible viewport before its first paint; spread the
            // remaining photographs across frames. No inventory/clock mutation.
            for(int index=0;index<rows.Count;index++)
            {
                if(!IsOpen)yield break;
                ProductRow row=rows[index];Sprite sprite=icons.Get(row.item.id);row.portrait.sprite=sprite;row.portrait.enabled=sprite!=null;
                if(index>=7)yield return null;
            }
        }
        private void Update()
        {
            if(flow==null||flow.Run==null)return;
            int seconds=Mathf.CeilToInt(Mathf.Max(0,flow.Run.Remaining));timeLeft.text="采购 "+(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");timeLeft.color=seconds<=10?CampaignUI.Rust:ink;
            if(Time.unscaledTime>=nextRefresh){nextRefresh=Time.unscaledTime+.20f;Refresh();}
        }
        private void Refresh()
        {
            bool atCounter=flow.World!=null&&flow.World.IsNear("SupplyCounter")&&flow.World.ShoppingMotor!=null&&flow.World.ShoppingMotor.IsNearBasket(1.8f);
            var preview=ProcurementPlan.PreviewPlan(flow.Run);
            var recommendation=preview.plan;
            var plannedIds=new HashSet<string>(recommendation.supplies);
            int visibleCount=0;foreach(var row in rows)if(SupplyCatalog.Category(row.item.id)==category)visibleCount++;
            browseHint.text=category+" · "+visibleCount+" 种";
            foreach(ProductRow row in rows)
            {
                ItemDefinition item=row.item;
                // FREEDOM19: repeatable consumables track how many real units
                // of this SKU are already in the basket; durable hardware
                // stays on the legacy present/absent toggle.
                int basketCount=0;foreach(string entry in flow.Run.Cart)if(entry==item.id)basketCount++;
                bool basket=basketCount>0;
                bool repeatable=RunState.IsRepeatableSupply(item.id);
                bool emptyCoal=item.id=="E01"&&flow.Run.CoalPoints==0;
                bool atHome=!repeatable&&flow.Run.HomeEquipment.Contains(item.id)&&!emptyCoal;
                bool aboard=!repeatable&&(flow.Run.Cargo.Contains(item.id)||(flow.Run.LoadedEquipment.Contains(item.id)&&!emptyCoal));
                bool locked=flow.Run.Paid||flow.Run.CheckoutInProgress||flow.Run.Paused||flow.Run.Remaining<=0;
                string key=SupplyCatalog.IngredientId(item.id);flow.Run.Supplies.TryGetValue(key,out int amount);
                row.quantity.text=item.id=="E13"?"五金挂件 · 1 件":item.id=="E02"?"3 罐 / 组 · 45 热能":item.id.Length==3&&item.id[0]=='S'?item.portions+" 份 / 瓶":item.id.StartsWith("P")?item.portions+" 个 / 包":item.id.EndsWith("_small")?item.portions+" 份 / 袋":SupplyCatalog.IsEquipment(item.id)?"1 件 · 成色 100%":"1 件";
                int displayedPrice=basket&&flow.Run.Paid?flow.Run.PurchaseItemPriceCents(item.id):flow.Run.PriceCents(item.id);
                row.price.text=CommerceReceiptData.Money(displayedPrice);
                bool paidUsed=basket&&flow.Run.Paid&&flow.Run.UsedPriceOverrides.ContainsKey(item.id);
                bool planned=plannedIds.Contains(item.id)&&!basket&&!atHome&&!aboard;
                row.state.text=basket?(flow.Run.Paid?(paidUsed?"旧货已付":"已付款"):repeatable?"已入篮 ×"+basketCount:"已入篮"):atHome?"已在家":aboard?"已装车":amount>0?"现有 "+amount:"";
                if(planned)row.state.text="推荐 · "+row.state.text;
                row.state.color=basket||planned?CampaignUI.Vermilion:CampaignUI.Muted;row.face.color=basket?CampaignUI.Selected:CampaignUI.Background;
                string action=basket?(flow.Run.Paid?"已付款":repeatable?"＋ 加一件":"放回"):atHome||aboard?"已备妥":item.id.StartsWith("P")&&amount>0?"加购1包":repeatable?"＋ 加入":"放入车";
                row.action.text=action;
                bool primary=basket?!flow.Run.Paid&&repeatable:!atHome&&!aboard;
                row.button.GetComponent<Image>().color=primary?CampaignUI.Vermilion:CampaignUI.Pale;row.action.color=primary?paper:ink;
                bool canAdd=flow.Run.CanAddSupply(item.id,out string addReason);
                row.button.interactable=!locked&&atCounter&&(repeatable?canAdd:basket||canAdd);
                // FREEDOM19: the remove-one action only exists for repeatable
                // goods with a real unit in the basket, under the same
                // cash/paid/busy/proximity/capacity discipline as adding.
                row.minus.gameObject.SetActive(repeatable);
                row.minus.interactable=repeatable&&basketCount>0&&!locked&&atCounter;
                row.minusLabel.text="− 放回一件";
                if(!basket&&!atHome&&!aboard&&!locked&&!canAdd&&addReason.Contains("空格"))row.state.text="空格不足";
                if(RecoveryRules.IsDurable(item.id))
                {var owned=flow.Campaign?.equipment.Find(x=>x.id==item.id);float condition=(basket&&flow.Run.Paid||atHome||aboard)?owned?.condition??100:100;row.quantity.text=(item.id=="E13"?"五金挂件 · ":"")+"1 件 · 成色 "+condition.ToString("0")+"%";}
            }
            total.text="购物车 "+flow.Run.CartCellsUsed+"/"+flow.Run.CartCapacity+" 格　 ·　 "+(flow.Run.Paid?"已付款 ¥":"待付 ¥")+(flow.Run.CartTotalCents/100f).ToString("0.00")+"　 ·　随身 ¥"+(flow.Run.CashCents/100f).ToString("0.00");
            if(!flow.Run.Paid&&flow.Run.CartTotalCents>flow.Run.CashCents)total.text+="　现金不足";
            starter.gameObject.SetActive(!selling);browseHint.gameObject.SetActive(!selling);
            // The starter button itself states the dish, the price and whether
            // the cash covers it, so "今天至少能卖哪一道、钱够不够" is visible
            // before the player commits anything.
            string starterLabel="补货参考 · 「"+recommendation.menu+"」";
            if(recommendation.gaps==""&&recommendation.supplies.Length>0)starterLabel+=" · "+CommerceReceiptData.Money(recommendation.totalCents);
            starter.GetComponentInChildren<Text>().text=starterLabel;
            bool starterUsable=!flow.Run.Paid&&!flow.Run.CheckoutInProgress&&!flow.Run.Paused&&flow.Run.Remaining>0&&atCounter&&recommendation.supplies.Length>0;
            starter.interactable=starterUsable;
            if(recommendation.supplies.Length==0&&!selling)starter.GetComponentInChildren<Text>().text=recommendation.shelves.Length>0?"补货参考 · 到货架取货":recommendation.gaps==""?"补货参考 · 已有物资足够":"补货参考 · 查看还缺什么";
            subtitle.text=selling?(buying?"旧货当场付款，入篮后到三轮车旁装载。":"查看成色和报价，确认后当场交售收款。"):"此处账单直购「"+category+"」；整箱原料在货架亲手搬，不入本账单。";
            advice.text=ComposeAdvice(preview,recommendation);
            if(selling&&!string.IsNullOrEmpty(tradeFeedback))advice.text=tradeFeedback;
            bool showReturnHome=!selling&&flow.Run.Paid&&flow.Run.Cart.Count>0;
            returnHome.gameObject.SetActive(showReturnHome);
            // Reserve a separate action row above the footer. Paid goods stay
            // visible in the shorter scroll viewport rather than behind it.
            float browseHeight=showReturnHome?305:365;
            browseFrame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,browseHeight);
            browseRail.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,browseHeight);
            returnHome.interactable=flow.CanUseRecoveryCounter&&!handingOver;
            if(flow.Run.PendingPaidLoad&&(!selling||string.IsNullOrEmpty(tradeFeedback)))advice.text="已付款货物保留在篮中。卖旧物腾空间再装车，或带回家整理，今天不开摊。";
            if(!selling)
            {
                // Real lockout reasons: a paid basket, the cashier queue, pause
                // or the clock, never a silent disable.
                if(flow.Run.Paid)advice.text="先把已付款商品装上摊车，再继续采购。";
                else if(flow.Run.CheckoutInProgress)advice.text="正在人工收银，请留在柜台附近；离开会中断排队。";
                else if(flow.Run.Paused)advice.text="游戏暂停中，先继续游戏再采购。";
                else if(flow.Run.Remaining<=0)advice.text="采购时间已结束，今天不能再买入。";
                else if(!atCounter)advice.text="把购物车带到采购柜台旁，再挑选商品。";
                if(flow.Run.PendingPaidLoad)advice.text="已付款货物留在篮中。先腾出车上空间再装车，或带回家整理，今天不开摊。";
            }
            RefreshPurchaseConfirmation();
            if(selling&&!handingOver&&confirmation==null)
            {
                string signature=ResaleSignature();if(signature!=resaleSignature)RebuildResale();
            }
        }
        // One advice line with an explicit precedence: outcome of the player's
        // own last action first; then what the CURRENT basket can already make
        // (BILL9: 补货参考 is optional understanding, never a forced full buy);
        // then the concrete remaining gap / cheapest way out. No silent dead ends.
        private string ComposeAdvice(ProcurementPlan.Preview preview,OpeningProcurementPlan recommendation)
        {
            if(selling)return "旧货代收在回收站（旧货摊回收台）或采购柜台办理，购物车需在身旁";
            if(!string.IsNullOrEmpty(starterFeedback))return starterFeedback;
            if(recommendation.supplies.Length==0&&recommendation.shelves.Length>0)
                return "柜台不用补货；还想做「"+recommendation.menu+"」，可去货架取剩下的原料。";
            if(recommendation.supplies.Length==0&&recommendation.shelves.Length==0&&recommendation.gaps=="")
                return flow.Run.Cart.Count>0?"已有货物能做「"+recommendation.menu+"」；篮中商品按需结账，也可以继续挑。":"车上已有货能做「"+recommendation.menu+"」；想添什么，再按需购买。";
            if(recommendation.supplies.Length==0)
                return "「"+recommendation.menu+"」今天尚不成套"+(recommendation.gaps!=""?"，还缺："+recommendation.gaps:"")+"。最省钱的出路：先补一件最缺的散称小份，或卖旧物回血；也可以只买想要的单品，收工前直接去出摊。";
            if(recommendation.gaps!="")return "还缺："+recommendation.gaps;
            bool basketCoversPlan=true;
            foreach(string id in recommendation.supplies)if(!flow.Run.Cart.Contains(id)){basketCoversPlan=false;break;}
            if(basketCoversPlan)return "篮中已能成「"+recommendation.menu+"」· 可直接去收银台结账出摊；想加货继续挑，不必求全。";
            if(preview.cashShortCents>0&&!flow.Run.Paid)return "这套约 "+CommerceReceiptData.Money(recommendation.totalCents)+"，现金还差 "+CommerceReceiptData.Money(preview.cashShortCents)+"；可先只买能卖的一道，或卖旧物回血。";
            if(!preview.fitsBasket)return "这套一次放不进购物车；先装车或放回部分商品，再点补货参考。";
            foreach(var blocker in preview.blockers)
                if(blocker.Value.Contains("网购"))return "其中 "+BusinessWorldHUD.ShortItem(blocker.Key)+" 需夜间网购，其余可先补齐。";
            var names=new List<string>();
            foreach(string id in recommendation.shelves)if(!flow.Run.Cart.Contains(id)&&!flow.Run.Cargo.Contains(id))names.Add(BusinessWorldHUD.ShortItem(id));
            if(names.Count>0)return "补货参考只加购「"+recommendation.menu+"」缺的账单商品；整箱部分需到货架拿："+string.Join("、",names);
            return "「补货参考」只把「"+recommendation.menu+"」缺的加入篮中，已有的不重复买；也可只挑单品，买完直接去出摊。";
        }
        private void AddStarter()
        {
            var recommendation=OpeningProcurementPlan.For(flow.Run);
            if(recommendation.supplies.Length==0)
            {
                starterFeedback=recommendation.shelves.Length>0?"柜台不用补货；剩下的原料在货架取。":string.IsNullOrEmpty(recommendation.gaps)?"已有货物能做「"+recommendation.menu+"」，无需重复加购。":"还缺："+recommendation.gaps;
                Refresh();return;
            }
            var failures=new List<KeyValuePair<string,string>>();
            var before=new List<string>(flow.Run.Cart);
            // Go through the production ChangeSupply path so world sync, basket
            // proximity and HUD publish stay identical to a manual pick; every
            // failure keeps its real RunState reason. Only ids missing from the
            // basket are touched — the player's own picks are never removed.
            var added=new List<string>();
            foreach(string id in recommendation.supplies)
            {
                if(flow.Run.Cart.Contains(id))continue;
                if(flow.ChangeSupply(id,false)&&flow.Run.Cart.Contains(id))added.Add(id);
                else failures.Add(new KeyValuePair<string,string>(id,string.IsNullOrEmpty(flow.Run.Message)?"现在不能放入购物车。":flow.Run.Message));
            }
            var parts=new List<string>();
            if(added.Count>0)parts.Add("已入篮 "+added.Count+" 件 · 待付 "+CommerceReceiptData.Money(flow.Run.CartTotalCents));
            foreach(string id in added)if(!flow.Run.Cart.Contains(id))parts.Add(BusinessWorldHUD.ShortItem(id)+" 未入篮");
            if(failures.Count>0)parts.Add("未能入篮："+string.Join("；",FailuresText(failures)));
            var kept=before.FindAll(id=>!flow.Run.Cart.Contains(id)&&!added.Contains(id));
            if(kept.Count>0)parts.Add("原有 "+kept.Count+" 件保留在篮中");
            starterFeedback=string.Join(" · ",parts);
            if(string.IsNullOrEmpty(starterFeedback))starterFeedback="没有可补的账单商品；可直接结账或去货架搬整箱。";
            Refresh();
        }
        private static List<string> FailuresText(List<KeyValuePair<string,string>> failures)
        {
            var text=new List<string>();
            foreach(var pair in failures)text.Add(BusinessWorldHUD.ShortItem(pair.Key)+"（"+pair.Value+"）");
            return text;
        }
        private string ResaleSignature()
        {
            var parts=new List<string>();foreach(var q in flow.GetResaleQuotes())parts.Add(q.id+":"+q.priceCents);
            foreach(var q in flow.GetUsedPurchaseQuotes())parts.Add("buy:"+q.id+":"+q.priceCents);
            return string.Join("|",parts)+":"+flow.CanUseRecoveryCounter+":"+resalePage+":"+buying+":"+flow.Run.Paid+":"+flow.Run.Cart.Count;
        }
        private void ShowResale(bool show)
        {
            if(handingOver)return;CloseConfirmation();selling=show;buying=false;resalePage=0;tradeFeedback="";starterFeedback="";
            browseFrame.gameObject.SetActive(!show);resalePanel.gameObject.SetActive(show);foreach(var tab in categoryTabs)tab.gameObject.SetActive(!show);
            resaleTab.GetComponentInChildren<Text>().text=show?"返回买家当":"旧货代收";
            if(show)RebuildResale();Refresh();
        }
        private void RebuildResale()
        {
            foreach(Transform child in resalePanel){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            var quotes=buying?flow.GetUsedPurchaseQuotes():flow.GetResaleQuotes();int pages=Mathf.Max(1,Mathf.CeilToInt(quotes.Length/6f));resalePage=Mathf.Clamp(resalePage,0,pages-1);
            Label(resalePanel,"Resale heading",buying?"今日旧货 · 成色50%":"带到市场的自有设备",18,8,650,40,24,ink).fontStyle=FontStyle.Bold;
            Button(resalePanel,"Used offer tab","淘旧货",730,7,112,38,()=>{buying=true;resalePage=0;tradeFeedback="";RebuildResale();});
            Button(resalePanel,"Owned sale tab","卖设备",854,7,112,38,()=>{buying=false;resalePage=0;tradeFeedback="";RebuildResale();});
            if(quotes.Length==0)Label(resalePanel,"No owned gear",buying?"今天的基础旧货已买过，或你已拥有同款。":"没有可交售的设备。家中未带出的物品不会在这里出售。",24,115,920,80,24,CampaignUI.Muted);
            for(int i=resalePage*6;i<Mathf.Min(quotes.Length,(resalePage+1)*6);i++)
            {
                RecoveryQuote quote=quotes[i];int at=i-resalePage*6;float x=at%2*485+10,y=52+at/2*101;
                var card=Box(resalePanel,"Sell "+quote.id,x,y,473,93,paper);
                var icon=Box(card,"Equipment icon",8,8,76,76,Color.white);var image=icon.GetComponent<Image>();image.sprite=icons.Get(quote.id);image.preserveAspect=true;image.raycastTarget=false;
                Label(card,"Owned item",quote.name,96,6,350,30,20,ink).fontStyle=FontStyle.Bold;
                Label(card,"Condition",quote.source+" · 成色 "+quote.condition.ToString("0")+"%",96,36,244,26,16,CampaignUI.Muted);
                Label(card,"Offer","¥"+(quote.priceCents/100f).ToString("0.00"),96,62,200,30,19,CampaignUI.Vermilion);
                Button(card,"Review offer",buying?"看货":"看报价",348,46,112,36,()=>{if(buying)ConfirmUsedBuy(quote);else ConfirmSale(quote);}).interactable=flow.CanUseRecoveryCounter;
            }
            Button(resalePanel,"Previous resale","‹",350,367,64,42,()=>{resalePage--;RebuildResale();}).interactable=resalePage>0;
            Label(resalePanel,"Resale page",(resalePage+1)+" / "+pages,425,373,130,36,20,ink).alignment=TextAnchor.MiddleCenter;
            Button(resalePanel,"Next resale","›",560,367,64,42,()=>{resalePage++;RebuildResale();}).interactable=resalePage+1<pages;
            resaleSignature=ResaleSignature();
        }
        private void CloseConfirmation()
        {pendingBuyQuote=null;confirmationCommit=null;confirmationStatus=null;if(confirmation==null)return;confirmation.gameObject.SetActive(false);Destroy(confirmation.gameObject);confirmation=null;}
        private RectTransform Confirmation(string title,string description)
        {
            CloseConfirmation();confirmation=Box(transform,"Confirm trade shade",0,0,1600,900,CampaignUI.WithAlpha(ink,.64f));
            var sheet=Box(confirmation,"Confirm trade",470,285,660,330,paper);
            Label(sheet,"Confirm heading",title,32,25,596,55,28,ink).fontStyle=FontStyle.Bold;
            Label(sheet,"Confirm details",description,32,90,596,112,23,ink);
            confirmationStatus=Label(sheet,"Trade status","",32,207,596,32,18,CampaignUI.Rust);
            return sheet;
        }
        private void ConfirmSale(RecoveryQuote quote)
        {
            var sheet=Confirmation("交售 · "+quote.name,"成色 "+quote.condition.ToString("0")+"% · "+quote.source+"\n收购报价 ¥"+(quote.priceCents/100f).ToString("0.00")+"\n出售后，今天的配装和可售菜单可能改变。");
            var cancel=Button(sheet,"Cancel sale","再想想",32,248,244,54,CloseConfirmation);
            var commit=Button(sheet,"Confirm sale","确认交售",326,248,300,54,()=>StartCoroutine(HandOver(quote)));
            commit.interactable=flow.CanUseRecoveryCounter;
        }
        private void ConfirmUsedBuy(RecoveryQuote quote)
        {
            var sheet=Confirmation("淘到 · "+quote.name,"成色 50% · ¥"+(quote.priceCents/100f).ToString("0.00")+"\n当场付款后放进购物篮，再推到车边装载。\n同款每天限购1件，已有的设备不重复购买。");
            Button(sheet,"Cancel used purchase","先不买",32,248,244,54,CloseConfirmation);
            pendingBuyQuote=quote;
            confirmationCommit=Button(sheet,"Buy used purchase","付款买下",326,248,300,54,()=>StartCoroutine(HandOver(quote,true)));
            RefreshPurchaseConfirmation();
        }
        private string UsedBuyBlocker(RecoveryQuote quote)
        {
            if(flow.Run.Paused)return "请先继续游戏，再办理交易。";
            if(flow.Run.CheckoutInProgress)return "正在收银，请完成结账后再来。";
            if(!string.IsNullOrEmpty(flow.Run.HeldGrocery))return "先把手里的商品放进篮中或放回货架。";
            if(!flow.CanUseRecoveryCounter)return "请带购物车靠近回收站或采购柜台。";
            if(flow.Run.Remaining<=0)return "采购时间已结束，今天不能再买入。";
            if(flow.Run.Cart.Count>0&&!flow.Run.Paid)return "篮中还有未付款商品，请先结账或放回。";
            if(flow.Run.CashCents<quote.priceCents)return "现金不足，还差 "+CommerceReceiptData.Money(quote.priceCents-flow.Run.CashCents)+"。";
            return "";
        }
        private void RefreshPurchaseConfirmation()
        {
            if(pendingBuyQuote==null||confirmationCommit==null||confirmationStatus==null||handingOver)return;
            string blocker=UsedBuyBlocker(pendingBuyQuote);
            confirmationCommit.interactable=blocker=="";confirmationStatus.text=blocker;
        }
        private IEnumerator HandOver(RecoveryQuote quote,bool buy=false)
        {
            if(handingOver)yield break;handingOver=true;
            if(confirmation!=null)foreach(Button button in confirmation.GetComponentsInChildren<Button>())button.interactable=false;
            if(confirmationStatus!=null)confirmationStatus.text="正在交接，请稍候…";
            float elapsed=0;
            while(elapsed<1.1f)
            {
                if(flow==null||flow.Run.Phase!=GamePhase.Market){handingOver=false;CloseConfirmation();yield break;}
                if(!flow.Run.Paused)elapsed+=Time.unscaledDeltaTime;yield return null;
            }
            bool completed=buy?flow.BuyUsedEquipment(quote.id,quote.priceCents):flow.SellEquipment(quote.id,quote.priceCents);
            string blocker=buy?UsedBuyBlocker(quote):"";
            tradeFeedback=completed?flow.Run.Message:blocker!=""?blocker:flow.Run.Message;
            if(string.IsNullOrEmpty(tradeFeedback))tradeFeedback="交易未完成，请重新查看物品和报价。";
            handingOver=false;CloseConfirmation();RebuildResale();Refresh();
        }
        private void ConfirmReturnHome()
        {
            var sheet=Confirmation("带已付货物回家整理","已付款的货物全部保留，明天可重新装车。\n今天不开摊，没有营业收入；采购款不退。 ");
            Button(sheet,"Keep shopping","继续整理",32,248,244,54,CloseConfirmation);
            Button(sheet,"Return paid goods home","确认回家",326,248,300,54,()=>flow.ReturnPaidPurchasesHome()).interactable=flow.CanUseRecoveryCounter;
        }
        private RectTransform Node(Transform parent,string name)
        {var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);return rt;}
        private RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color)
        {var rt=Node(parent,name);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);rt.gameObject.AddComponent<Image>().color=color;return rt;}
        private Text Label(Transform parent,string name,string text,float x,float y,float w,float h,int size,Color color)
        {var rt=Node(parent,name);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);var t=rt.gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.color=color;t.text=text;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
        private Button Button(Transform parent,string name,string text,float x,float y,float w,float h,Action action)
        {bool primary=name=="Starter"||text=="放入车";var rt=Box(parent,name,x,y,w,h,primary?CampaignUI.Vermilion:CampaignUI.Pale);var b=rt.gameObject.AddComponent<Button>();b.colors=CampaignUI.ButtonColours(b.colors);b.onClick.AddListener(()=>action());var t=Label(rt,"Label",text,8,4,w-16,h-8,19,primary?paper:ink);t.alignment=TextAnchor.MiddleCenter;return b;}
    }
    public sealed class SupplyRowFocus : MonoBehaviour,UnityEngine.EventSystems.ISelectHandler
    {
        public ScrollRect scroll;
        public RectTransform row;
        public void OnSelect(UnityEngine.EventSystems.BaseEventData data)
        {
            if(scroll==null||row==null)return;
            float top=-row.anchoredPosition.y,visible=scroll.viewport.rect.height,at=scroll.content.anchoredPosition.y;
            if(top<at)at=top;else if(top+row.rect.height>at+visible)at=top+row.rect.height-visible;
            scroll.StopMovement();Vector2 position=scroll.content.anchoredPosition;position.y=Mathf.Clamp(at,0,Mathf.Max(0,scroll.content.rect.height-visible));scroll.content.anchoredPosition=position;
        }
    }
}
