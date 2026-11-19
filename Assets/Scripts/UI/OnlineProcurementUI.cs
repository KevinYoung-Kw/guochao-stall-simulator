using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public sealed class OnlineProcurementUI : MonoBehaviour
    {
        private static OnlineProcurementUI active;
        public static bool IsOpen=>active!=null;
        private GameFlow flow;
        private bool parcelsMode,showOrders;
        private GameItemIconLibrary icons;
        private RectTransform safe,list,confirmation;
        private Text balance,status,subtitle;
        private Button orderTab;
        private CampaignFocusScope focusScope,confirmationFocus;
        private readonly List<GameObject> cards=new List<GameObject>();
        private string feedback="",openingOrderId="";
        private float openingRemaining;
        private Text openingLabel;

        public static void OpenShop(GameFlow game)
        {
            if(game?.Campaign==null||game.Run.Phase!=GamePhase.Night)return;
            Close();active=new GameObject("Night online procurement").AddComponent<OnlineProcurementUI>();
            active.flow=game;active.Build();
        }
        public static void OpenParcels(GameFlow game)
        {
            if(game?.Campaign==null||game.Run.Phase!=GamePhase.Home||!game.HasReadyOnlineParcels)return;
            Close();active=new GameObject("Delivered parcels").AddComponent<OnlineProcurementUI>();
            active.flow=game;active.parcelsMode=true;active.Build();
        }
        public static void Close()
        {
            if(active==null)return;
            var old=active;active=null;old.CloseConfirmation();old.focusScope?.Restore();
            if(old.parcelsMode&&old.flow?.World?.Player!=null)old.flow.World.Player.presentationLocked=old.flow.IsBusy;
            old.gameObject.SetActive(false);Destroy(old.gameObject);
            if(!old.parcelsMode&&old.flow?.Run?.Phase==GamePhase.Night)NightHubUI.Open(old.flow);
        }
        private void Build()
        {
            icons=gameObject.AddComponent<GameItemIconLibrary>();
            safe=CampaignUI.Canvas(transform,"Online procurement canvas",1450,CampaignUI.WithAlpha(CampaignUI.Ink,.62f));
            var sheet=CampaignUI.Paper(safe,"Online order sheet",new Vector2(1140,850),CampaignUI.PaperColor);
            CampaignUI.Text(sheet,parcelsMode?"家门口的包裹":"夜间采购",new Vector2(-145,355),new Vector2(760,62),38,CampaignUI.Ink,TextAnchor.MiddleLeft,true);
            balance=CampaignUI.Text(sheet,"",new Vector2(400,355),new Vector2(260,48),26,CampaignUI.Ink,TextAnchor.MiddleRight,true);
            subtitle=CampaignUI.Text(sheet,"",new Vector2(-110,303),new Vector2(840,42),21,CampaignUI.Muted);
            if(!parcelsMode)orderTab=CampaignUI.Button(sheet,"在途订单",new Vector2(425,303),new Vector2(190,42),()=>{showOrders=!showOrders;Rebuild();});
            CampaignUI.Rule(sheet,new Vector2(0,279),new Vector2(1070,2));
            list=CampaignUI.Scroll(sheet,"Online merchandise",new Vector2(0,-14),new Vector2(1080,580),out _);
            status=CampaignUI.Text(sheet,"",new Vector2(-105,-350),new Vector2(840,66),21,CampaignUI.Rust);
            var close=CampaignUI.Button(sheet,parcelsMode?"放下包裹":"返回账本",new Vector2(425,-355),new Vector2(190,58),Close);
            Rebuild();RefreshText();focusScope=new CampaignFocusScope(transform);CampaignUI.Focus(close);
        }
        private List<OnlineOrder> Pending(bool onlyReady)
        {
            var result=new List<OnlineOrder>();
            foreach(var row in flow.Campaign.onlineOrders??new List<OnlineOrder>())
                if(!row.received&&(!onlyReady||row.dueDay<=flow.Campaign.dayIndex))result.Add(row);
            return result;
        }
        private void Rebuild()
        {
            foreach(var card in cards)if(card!=null){card.SetActive(false);Destroy(card);}cards.Clear();
            float y=10;
            if(parcelsMode||showOrders)
            {
                var pending=Pending(parcelsMode);
                foreach(var order in pending)
                {
                    var sku=OnlineCatalog.FindSku(order.productId);if(sku==null)continue;
                    var card=Card(y,sku.name,"已付 "+CampaignUI.Money((int)order.paidCents),"第 "+order.dueDay+" 天 05:00 到货",sku.id);
                    if(parcelsMode)
                    {
                        var button=CampaignUI.Button(card,"开箱装车",new Vector2(400,0),new Vector2(180,56),()=>BeginOpening(order));
                        button.interactable=string.IsNullOrEmpty(openingOrderId);
                        if(openingOrderId==order.id)openingLabel=button.GetComponentInChildren<Text>();
                    }
                    else CampaignUI.Text(card,order.dueDay<=flow.Campaign.dayIndex?"待收货":"配送中",new Vector2(400,0),new Vector2(180,50),22,CampaignUI.Muted,TextAnchor.MiddleCenter);
                    y+=122;
                }
                if(pending.Count==0)
                {
                    var empty=CampaignUI.Text(list,parcelsMode?"包裹都收好了。":"还没有待收订单。",new Vector2(0,-75),new Vector2(1000,60),27,CampaignUI.Muted,TextAnchor.MiddleCenter);
                    empty.rectTransform.anchorMin=empty.rectTransform.anchorMax=new Vector2(.5f,1);cards.Add(empty.gameObject);y=120;
                }
            }
            else foreach(var sku in OnlineCatalog.Skus)
            {
                int price=OnlineCatalog.QuoteCents(flow.Run.Profile,sku.id);
                string note=sku.durable?"新品 · 成色 100%":sku.id.StartsWith("P")?sku.portions+" 个 / 捆 · 整箱 8 折":sku.portions+" 份 / 箱";
                bool owned=sku.durable&&flow.Campaign.equipment.Exists(x=>x.id==sku.id);
                bool pending=sku.durable&&Pending(false).Exists(x=>x.productId==sku.id);
                bool affordable=flow.Run.CashCents>=price;
                if(owned)note="已拥有同款";else if(pending)note="已下单 · 等待到货";else if(!affordable)note="还差 "+CampaignUI.Money(price-flow.Run.CashCents);
                var card=Card(y,sku.name,CampaignUI.Money(price),note,sku.id);
                var button=CampaignUI.Button(card,owned?"已拥有":pending?"已下单":affordable?"选购":"现金不足",new Vector2(400,0),new Vector2(180,56),()=>ConfirmOrder(sku,price),affordable&&!owned&&!pending);
                button.interactable=affordable&&!owned&&!pending;y+=122;
            }
            list.sizeDelta=new Vector2(0,Mathf.Max(580,y));RefreshText();
        }
        private RectTransform Card(float y,string name,string price,string detail,string iconId)
        {
            var card=CampaignUI.Paper(list,"Product "+iconId,new Vector2(1055,108),CampaignUI.Background);
            card.anchorMin=card.anchorMax=new Vector2(.5f,1);card.pivot=new Vector2(.5f,1);card.anchoredPosition=new Vector2(0,-y);
            CampaignUI.Icon(card,icons.Get(iconId),new Vector2(-465,0),new Vector2(78,78));
            CampaignUI.Text(card,name,new Vector2(-85,22),new Vector2(650,38),24,CampaignUI.Ink,TextAnchor.MiddleLeft,true);
            CampaignUI.Text(card,price,new Vector2(-285,-21),new Vector2(250,34),23,CampaignUI.Vermilion,TextAnchor.MiddleLeft,true);
            CampaignUI.Text(card,detail,new Vector2(65,-21),new Vector2(410,34),19,CampaignUI.Muted);
            cards.Add(card.gameObject);return card;
        }
        private void ConfirmOrder(OnlineCatalog.Sku sku,int price)
        {
            if(confirmation!=null)return;
            confirmation=CampaignUI.Node(safe,"Confirm online order");CampaignUI.Stretch(confirmation);
            var shade=confirmation.gameObject.AddComponent<Image>();shade.color=CampaignUI.WithAlpha(CampaignUI.Ink,.68f);shade.raycastTarget=true;
            var sheet=CampaignUI.Paper(confirmation,"Confirm payment",new Vector2(650,410),CampaignUI.PaperColor);
            CampaignUI.Text(sheet,sku.name,new Vector2(0,140),new Vector2(580,56),30,CampaignUI.Ink,TextAnchor.MiddleLeft,true);
            CampaignUI.Text(sheet,"付款 "+CampaignUI.Money(price)+" · 明早 05:00 送到家门口\n开箱后装上摊车；装不下的包裹可继续保留。",new Vector2(0,52),new Vector2(580,100),23,CampaignUI.Ink);
            var error=CampaignUI.Text(sheet,"",new Vector2(0,-52),new Vector2(580,68),21,CampaignUI.Rust);
            var cancel=CampaignUI.Button(sheet,"再想想",new Vector2(-155,-145),new Vector2(260,58),CloseConfirmation);
            Button buy=null;buy=CampaignUI.Button(sheet,"确认付款",new Vector2(155,-145),new Vector2(260,58),()=>{
                buy.interactable=false;
                if(flow.TryOrderOnline(sku.id,price,out string reason))
                {CloseConfirmation();feedback="已付款 · "+sku.name+"，明早送达。";Rebuild();}
                else{error.text=reason;buy.interactable=true;}
            },true);
            confirmationFocus=new CampaignFocusScope(confirmation);CampaignUI.Focus(cancel);
        }
        private void CloseConfirmation()
        {
            confirmationFocus?.Restore();confirmationFocus=null;
            if(confirmation!=null){confirmation.gameObject.SetActive(false);Destroy(confirmation.gameObject);confirmation=null;}
        }
        private void BeginOpening(OnlineOrder order)
        {
            if(openingOrderId!=""||flow.Run.Paused||flow.Run.HoldingStove||!string.IsNullOrEmpty(flow.Run.HeldGrocery)||flow.IsBusy)return;
            bool shears=flow.Run.LoadedEquipment.Contains("U14")||flow.Run.HomeEquipment.Contains("U14");
            openingOrderId=order.id;openingRemaining=shears?.5f:3f;feedback=shears?"用料理剪拆开封条……":"正在拆开封条……";Rebuild();
        }
        private void RefreshText()
        {
            if(balance!=null)balance.text="现金 "+CampaignUI.Money(flow.Run.CashCents);
            if(subtitle!=null)subtitle.text=parcelsMode?"整备剩余 "+Mathf.CeilToInt(flow.Run.Remaining)+" 秒 · 开箱后检查摊车空间":
                "夜间下单 · 次晨 05:00 送到家门口"+(flow.Run.Profile?.Has("网购达人")==true?" · 网购 8.8 折":"");
            if(orderTab!=null)orderTab.GetComponentInChildren<Text>().text=showOrders?"继续选购":"在途 ("+Pending(false).Count+")";
            if(status!=null)status.text=feedback;
        }
        private void Update()
        {
            if(flow?.Run==null){Close();return;}
            if(parcelsMode&&flow.Run.Phase!=GamePhase.Home||!parcelsMode&&flow.Run.Phase!=GamePhase.Night){Close();return;}
            if(Input.GetKeyDown(KeyCode.Escape)){if(confirmation!=null)CloseConfirmation();else Close();return;}
            if(parcelsMode&&openingOrderId!="")
            {
                if(flow.Run.Paused||flow.IsBusy||flow.Run.HoldingStove||!string.IsNullOrEmpty(flow.Run.HeldGrocery))
                {openingOrderId="";openingLabel=null;feedback="开箱已停下，包裹仍在这里。";Rebuild();}
                else
                {
                    openingRemaining-=Time.deltaTime;
                    if(openingLabel!=null)openingLabel.text="开箱 "+Mathf.Max(0,openingRemaining).ToString("0.0")+" 秒";
                    if(openingRemaining<=0)
                    {
                        string id=openingOrderId;openingOrderId="";openingLabel=null;
                        feedback=flow.TryCollectOnline(id,out string reason)?"已装上摊车。":reason;Rebuild();
                    }
                }
            }
            RefreshText();
        }
        private void OnDestroy(){confirmationFocus?.Restore();focusScope?.Restore();if(active==this)active=null;}
    }
}
