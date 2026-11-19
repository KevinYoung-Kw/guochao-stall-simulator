using System;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // The room remains the world behind the open ledger. Only the campaign
    // supplies amounts and ownership; this page performs no night settlement.
    public sealed class NightHubUI : MonoBehaviour
    {
        private static NightHubUI instance;
        public static bool IsOpen=>instance!=null;
        private GameFlow flow;
        private GameItemIconLibrary icons;
        private CampaignFocusScope focusScope;
        private Text saveStatus;
        private ScrollRect ledgerScroll,stockScroll;

        public static void Open(GameFlow game)
        {
            if(game==null||game.Campaign==null)return;
            Close();CampaignMenuUI.Close();CharacterCreationUI.Dismiss();
            instance=new GameObject("Night at home — campaign ledger").AddComponent<NightHubUI>();
            instance.flow=game;instance.icons=instance.gameObject.AddComponent<GameItemIconLibrary>();instance.Build();
        }
        public static void Close()
        {
            if(instance==null)return;
            var closing=instance;instance=null;closing.focusScope?.Restore();closing.gameObject.SetActive(false);Destroy(closing.gameObject);
        }
        private void Build()
        {
            CampaignState state=flow.Campaign;CampaignDayLedger day=state.lastDay;
            int dayNumber=day!=null?day.dayIndex:state.dayIndex;
            RectTransform safe=CampaignUI.Canvas(transform,"Night ledger canvas",1300,CampaignUI.WithAlpha(CampaignUI.Ink,.28f));
            CampaignUI.Text(safe,"回家了",new Vector2(-747,310),new Vector2(300,82),48,CampaignUI.PaperColor,TextAnchor.MiddleLeft,true);
            CampaignUI.Text(safe,"第 "+dayNumber+" 天 · 夜间",new Vector2(-740,240),new Vector2(314,48),25,CampaignUI.PaperColor);
            CampaignUI.Text(safe,"收好家当，明天接着干。",new Vector2(-740,183),new Vector2(314,90),23,CampaignUI.PaperColor);
            if(flow.Run!=null&&flow.Run.Phase==GamePhase.Night)CampaignUI.Button(safe,"夜间采购",new Vector2(-740,66),new Vector2(300,54),()=>OnlineProcurementUI.OpenShop(flow));
            // NIGHT9 N01：夜间重器去向选择。默认「卸回屋」＝现行安全行为；只有显式
            // 选「大件留车」才会在次日早晨按 35%~45% 基础概率做废品飞贼检定。
            // 台账「车上/家中」标签与选择后的真实位置一致。
            bool nightChoice=flow.Run!=null&&flow.Run.Phase==GamePhase.Night;
            CampaignUI.Text(safe,"今晚大件",new Vector2(-747,-8),new Vector2(300,40),25,CampaignUI.PaperColor,TextAnchor.MiddleLeft,true);
            string unloadLabel=state.nightLeaveVehicle?"　 大件卸回屋（安全）":"● 大件卸回屋（安全）";
            string leaveLabel=state.nightLeaveVehicle?"● 大件留车过夜（有风险）":"　 大件留车过夜（有风险）";
            if(nightChoice)
            {
                CampaignUI.Button(safe,unloadLabel,new Vector2(-740,-56),new Vector2(300,50),()=>{NightSecurityRules.ChooseUnloadHome(flow.Campaign);NightHubUI.Open(flow);});
                CampaignUI.Button(safe,leaveLabel,new Vector2(-740,-112),new Vector2(300,50),()=>{NightSecurityRules.ChooseLeaveOnVehicle(flow.Campaign);NightHubUI.Open(flow);});
            }
            else
            {
                CampaignUI.Text(safe,unloadLabel,new Vector2(-747,-56),new Vector2(300,50),21,CampaignUI.PaperColor);
                CampaignUI.Text(safe,leaveLabel,new Vector2(-747,-112),new Vector2(300,50),21,CampaignUI.PaperColor);
            }
            CampaignUI.Text(safe,"留车：只对收摊时实际在车上的大件逐件单独检定——\n炉具 15%~45%、气罐 35%、电瓶 42%（按目录逐项定率）；\nLv2 车减免 25%，Lv3 卷帘免疫。被偷的大件连同罐内\n能源一起消失，次日早晨提示点名丢了什么。",
                new Vector2(-747,-170),new Vector2(314,96),18,CampaignUI.PaperColor);
            if(state.securityHistory!=null&&state.securityHistory.Count>0)
            {
                NightSecurityRecord last=state.securityHistory[state.securityHistory.Count-1];
                if(last.stolen)
                {
                    string names=last.losses!=null&&last.losses.Count>0?string.Join("、",last.losses.ConvertAll(x=>"【"+x.name+"】").ToArray()):"【"+last.stolenName+"】";
                    CampaignUI.Text(safe,"已损记录：第 "+last.dayIndex+" 夜 被盗"+names+" · 损失 "+CampaignUI.Money(last.lossCents),new Vector2(-747,-268),new Vector2(314,44),18,CampaignUI.Rust);
                }
            }

            RectTransform cover=CampaignUI.Paper(safe,"Cloth ledger cover",new Vector2(1440,870),CampaignUI.Forest);cover.anchoredPosition=new Vector2(150,-10);
            RectTransform book=CampaignUI.Paper(cover,"Open account book",new Vector2(1406,836),CampaignUI.PaperColor);
            CampaignUI.Rule(book,new Vector2(182,15),new Vector2(2,608));
            CampaignUI.Text(book,"今天的账，算清楚了",new Vector2(-267,354),new Vector2(766,68),36,CampaignUI.Ink,TextAnchor.MiddleLeft,true);
            string district=day!=null&&!string.IsNullOrEmpty(day.district)?day.district:"今日经营";
            CampaignUI.Text(book,district+"  ·  第 "+dayNumber+" 天",new Vector2(-267,302),new Vector2(766,38),21,CampaignUI.Muted);
            CampaignUI.Text(book,"手头现金",new Vector2(456,358),new Vector2(400,36),21,CampaignUI.Muted,TextAnchor.MiddleRight);
            CampaignUI.Text(book,CampaignUI.Money(state.cashCents),new Vector2(456,306),new Vector2(400,64),40,CampaignUI.Ink,TextAnchor.MiddleRight,true);
            CampaignUI.Rule(book,new Vector2(0,268),new Vector2(1318,2));

            if(day!=null)
            {
                MoneyColumn(book,-526,day.resaleIncomeCents>0?"营业 / 旧货收款":"营业收款",day.revenueCents+day.resaleIncomeCents,true);
                MoneyColumn(book,-267,"采购 / 夜间支出",day.purchasesCents+day.nightFuelCents+(int)day.onlinePurchasesCents,false);
                MoneyColumn(book,-8,"本日现金变化",day.closingCashCents-day.openingCashCents,true,true);
                // A01 日终账本：漏收（坏账）与日结时未决债权直接读 CampaignDayLedger
                // 事实字段；漏收是没收到的钱，不做 −现金 扣款措辞。
                if(day.badDebtCents>0)
                {
                    CampaignUI.Text(book,"逃单漏收",new Vector2(251,220),new Vector2(246,36),20,CampaignUI.Muted);
                    CampaignUI.Text(book,CampaignUI.Money(day.badDebtCents),new Vector2(251,167),new Vector2(246,59),32,CampaignUI.Rust,TextAnchor.MiddleLeft,true);
                }
                if(day.riskPendingCents>0)
                {
                    CampaignUI.Text(book,"日结时未收回",new Vector2(251,day.badDebtCents>0?110:220),new Vector2(246,36),20,CampaignUI.Muted);
                    CampaignUI.Text(book,CampaignUI.Money(day.riskPendingCents),new Vector2(251,day.badDebtCents>0?57:167),new Vector2(246,59),32,CampaignUI.Rust,TextAnchor.MiddleLeft,true);
                }
                CampaignUI.Text(book,"早晨 "+CampaignUI.Money(day.openingCashCents)+"  →  收工 "+CampaignUI.Money(day.closingCashCents),new Vector2(-267,102),new Vector2(766,34),19,CampaignUI.Muted);
                RectTransform sales=CampaignUI.Scroll(book,"Sales and actual payments",new Vector2(-267,-83),new Vector2(770,318),out ledgerScroll);
                float used=BuildSales(sales,day);sales.sizeDelta=new Vector2(0,Mathf.Max(318,used));
                CampaignUI.Text(book,"完成 "+day.served+" 单  ·  流失 "+day.lost+" 单"+(day.integrityChange<0?"  ·  缺油盐诚信 "+day.integrityChange:""),new Vector2(-267,-271),new Vector2(766,36),21,CampaignUI.Muted);
            }
            else
            {
                CampaignUI.Text(book,"今天还没有日结账单",new Vector2(-267,40),new Vector2(740,64),30,CampaignUI.Ink,TextAnchor.MiddleCenter,true);
                CampaignUI.Text(book,"已拥有的物品仍记录在右页。",new Vector2(-267,-18),new Vector2(740,46),22,CampaignUI.Muted,TextAnchor.MiddleCenter);
            }
            string energy=string.IsNullOrWhiteSpace(state.energyNote)?"剩余能源已记入家当。":state.energyNote;
            CampaignUI.Text(book,energy,new Vector2(-267,-317),new Vector2(766,66),19,CampaignUI.Muted);

            CampaignUI.Text(book,"带回家的东西",new Vector2(456,229),new Vector2(400,46),26,CampaignUI.Ink,TextAnchor.MiddleLeft,true);
            RectTransform stock=CampaignUI.Scroll(book,"Owned stock and equipment",new Vector2(456,-49),new Vector2(414,494),out stockScroll);
            stock.sizeDelta=new Vector2(0,Mathf.Max(494,BuildInventory(stock,state)));
            CampaignUI.Text(book,"Lv"+state.vehicleLevel+" 摊车  ·  家当都在册",new Vector2(456,-321),new Vector2(400,42),19,CampaignUI.Muted);

            CampaignUI.Rule(book,new Vector2(0,-358),new Vector2(1318,2));
            CampaignUI.Button(book,"保存进度",new Vector2(-527,-393),new Vector2(242,54),()=>{flow.SaveCampaign();RefreshSaveStatus();});
            CampaignUI.Button(book,"保存并返回开局",new Vector2(-230,-393),new Vector2(300,54),()=>{flow.SaveAndReturnToTitle();if(instance!=null)RefreshSaveStatus();});
            Button next=CampaignUI.Button(book,"睡一觉 · 准备第 "+(state.dayIndex+1)+" 天",new Vector2(442,-393),new Vector2(414,58),()=>flow.StartNextDay(),true);
            saveStatus=CampaignUI.Text(safe,"",new Vector2(150,-494),new Vector2(1400,45),21,CampaignUI.PaperColor,TextAnchor.MiddleRight);
            RefreshSaveStatus();flow.Changed+=OnChanged;focusScope=new CampaignFocusScope(transform);CampaignUI.Focus(next);
        }
        private static void MoneyColumn(Transform book,float x,string heading,int cents,bool incoming,bool signed=false)
        {
            CampaignUI.Text(book,heading,new Vector2(x,220),new Vector2(246,36),20,CampaignUI.Muted);
            string amount=signed?CampaignUI.SignedMoney(cents):(incoming?"+":"−")+CampaignUI.Money(cents);
            CampaignUI.Text(book,amount,new Vector2(x,167),new Vector2(246,59),32,signed&&cents<0?CampaignUI.Rust:CampaignUI.Ink,TextAnchor.MiddleLeft,true);
        }
        private float BuildSales(RectTransform content,CampaignDayLedger day)
        {
            float y=28;
            if(day.sales==null||day.sales.Count==0)
            {TopText(content,"今天没有售出菜品",y,25,CampaignUI.Muted,750);y+=70;}
            else foreach(CampaignSaleRow row in day.sales)
            {
                int recipe=Array.FindIndex(BusinessSession.Recipes,item=>item.id==row.id);
                if(recipe>=0)
                {
                    RectTransform picture=CampaignUI.Node(content,"Dish "+row.id);picture.anchorMin=picture.anchorMax=new Vector2(0,1);picture.anchoredPosition=new Vector2(35,-y-20);picture.sizeDelta=new Vector2(62,62);
                    var drawing=picture.gameObject.AddComponent<StallUIIcon>();drawing.index=recipe;drawing.raycastTarget=false;
                }
                RowText(content,row.name,82,y-6,480,35,23,CampaignUI.Ink,true);
                RowText(content,row.quantity+" 份 × "+CampaignUI.Money(row.unitPriceCents),82,y+31,480,29,19,CampaignUI.Muted);
                RowText(content,CampaignUI.Money(row.totalCents),565,y+6,170,44,25,CampaignUI.Ink,true,TextAnchor.MiddleRight);
                y+=90;
            }
            if(day.resales!=null&&day.resales.Count>0)
            {
                y+=10;TopText(content,"旧货代收",y,22,CampaignUI.Ink,750);y+=48;
                foreach(CampaignResaleRow row in day.resales)
                {
                    RectTransform picture=CampaignUI.Node(content,"Sold equipment "+row.id);picture.anchorMin=picture.anchorMax=new Vector2(0,1);picture.anchoredPosition=new Vector2(35,-y-20);picture.sizeDelta=new Vector2(62,62);
                    CampaignUI.Icon(picture,icons.Get(row.id),Vector2.zero,new Vector2(62,62));
                    RowText(content,row.name,82,y-6,480,35,23,CampaignUI.Ink,true);
                    RowText(content,"成色 "+row.condition.ToString("0")+"% · 已交接",82,y+31,480,29,19,CampaignUI.Muted);
                    RowText(content,"+"+CampaignUI.Money(row.amountCents),565,y+6,170,44,25,CampaignUI.Ink,true,TextAnchor.MiddleRight);y+=90;
                }
            }
            y+=10;TopText(content,"今天付出的费用",y,22,CampaignUI.Ink,750);y+=48;
            PaymentRow(content,"今天采购",day.purchasesCents,ref y);
            if(day.nightFuelCents>0)PaymentRow(content,"夜间煤气补给",day.nightFuelCents,ref y);
            if(day.onlinePurchasesCents>0)PaymentRow(content,"夜间网购",day.onlinePurchasesCents,ref y);
            return y+22;
        }
        private static void PaymentRow(RectTransform content,string label,long cents,ref float y)
        {RowText(content,label,0,y,480,35,21,CampaignUI.Muted);RowText(content,"−"+CampaignUI.Money((int)cents),550,y,185,35,22,CampaignUI.Ink,false,TextAnchor.MiddleRight);y+=48;}
        private float BuildInventory(RectTransform content,CampaignState state)
        {
            float y=20;
            TopText(content,"能源余量",y,20,CampaignUI.Muted,388);y+=42;
            RowText(content,"煤气罐 "+Mathf.Max(0,state.gas-state.cartridge)+"  ·  卡式气 "+state.cartridge+"\n蜂窝煤 "+state.coal+"  ·  电量 "+state.electricity,0,y,388,60,21,CampaignUI.Ink);y+=78;
            TopText(content,"余料与包材",y,20,CampaignUI.Muted,388);y+=46;
            bool any=false;
            if(state.stock!=null)foreach(CampaignStockRow row in state.stock)
            {
                if(row.quantity<=0)continue;any=true;
                ItemRow(content,row.id,row.name,row.quantity+CommerceReceiptData.StockUnit(row.id),"留存",ref y);
            }
            if(!any){TopText(content,"暂无余料与包材",y,21,CampaignUI.Muted,388);y+=54;}
            y+=12;TopText(content,"设备家当",y,20,CampaignUI.Muted,388);y+=46;
            any=false;
            if(state.equipment!=null)foreach(CampaignGearRow row in state.equipment)
            {
                any=true;string place=row.location=="vehicle"?"车上":"家中";
                ItemRow(content,row.id,row.name,"1 件",place+" · 成色 "+Mathf.Clamp(row.condition,0,100).ToString("0")+"%",ref y);
            }
            if(!any){TopText(content,"暂无设备",y,21,CampaignUI.Muted,388);y+=54;}
            return y+16;
        }
        private void ItemRow(RectTransform content,string id,string name,string amount,string detail,ref float y)
        {
            RectTransform portrait=CampaignUI.Node(content,"Portrait "+id);portrait.anchorMin=portrait.anchorMax=new Vector2(0,1);portrait.anchoredPosition=new Vector2(31,-y-25);portrait.sizeDelta=new Vector2(68,68);
            CampaignUI.Icon(portrait,icons.Get(id),Vector2.zero,new Vector2(68,68));
            RowText(content,string.IsNullOrEmpty(name)?CommerceReceiptData.ShortName(id):name,76,y-7,315,36,21,CampaignUI.Ink,true);
            RowText(content,amount+"  ·  "+detail,76,y+33,315,34,17,CampaignUI.Muted);y+=88;
        }
        private static void TopText(RectTransform parent,string value,float y,int size,Color colour,float width)=>RowText(parent,value,0,y,width,38,size,colour);
        private static void RowText(RectTransform parent,string value,float x,float y,float width,float height,int size,Color colour,bool bold=false,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            Text text=CampaignUI.Text(parent,value,Vector2.zero,new Vector2(width,height),size,colour,alignment,bold);
            RectTransform rt=text.rectTransform;rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);
        }
        private void OnChanged(Snapshot snapshot){RefreshSaveStatus();}
        private void RefreshSaveStatus(){if(saveStatus==null||flow==null)return;string value=CampaignUI.SaveFeedback(flow.SaveStatus);if(saveStatus.text!=value)saveStatus.text=value;}
        private void Update()
        {
            // Keyboard users can inspect both long pages without losing the
            // selected action. Mouse-wheel scrolling remains local to each page.
            float pages=Input.GetKeyDown(KeyCode.PageDown)?-1:Input.GetKeyDown(KeyCode.PageUp)?1:0;
            if(pages==0)return;
            ScrollRect target=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)?ledgerScroll:stockScroll;
            if(target!=null)target.verticalNormalizedPosition=Mathf.Clamp01(target.verticalNormalizedPosition+pages*.45f);
        }
        private void OnDestroy(){if(flow!=null)flow.Changed-=OnChanged;focusScope?.Restore();if(instance==this)instance=null;}
    }
}
