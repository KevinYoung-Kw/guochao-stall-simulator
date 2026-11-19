using System;
using System.Collections.Generic;
using System.Globalization;

namespace Guochao
{
    [Serializable] public sealed class CommerceReceiptLine
    {
        public string id,name,unit,contents;
        public int quantity,unitCents,subtotalCents,sourceIndex=-1,recipeIndex=-1;
        public int quotedCents=-1;
    }
    [Serializable] public sealed class CommerceReceiptAudit
    {
        public string schema="guochao.receipt.v1",kind,status;
        public int cashCents,totalCents,ledgerCents;
        public bool totalsMatch;
        public CommerceReceiptLine[] lines;
    }
    // Read-only projections. Transaction authority stays in RunState/BusinessSession.
    public static class CommerceReceiptData
    {
        public static CommerceReceiptAudit Shopping(RunState run,bool loaded)
        {
            var lines=new List<CommerceReceiptLine>();var lookup=new Dictionary<string,CommerceReceiptLine>();
            IReadOnlyList<string> ids=loaded?run.Cargo:run.Cart;int total=0;
            for(int i=0;i<ids.Count;i++)
            {
                string id=ids[i];ItemDefinition item=GameCatalog.Find(id);if(item==null)continue;
                if(!lookup.TryGetValue(id,out CommerceReceiptLine line))
                {
                    line=new CommerceReceiptLine{id=id,name=ShortName(id),unit=PurchaseUnit(id),contents=Contents(id,item.portions),unitCents=run.PurchaseItemPriceCents(id),sourceIndex=i};
                    lines.Add(line);lookup.Add(id,line);
                }
                line.quantity++;line.subtotalCents+=run.PurchaseItemPriceCents(id);total+=run.PurchaseItemPriceCents(id);
            }
            int ledger=loaded?run.CargoPaidCents:run.CartTotalCents;
            return new CommerceReceiptAudit{kind=loaded?"loaded-purchases":"current-basket",status=loaded?"已付款 · 已装车":run.CheckoutInProgress?"收银中 · 尚未扣款":run.Paid?"已付款 · 待装车":"未付款",cashCents=run.CashCents,totalCents=total,ledgerCents=ledger,totalsMatch=total==ledger,lines=lines.ToArray()};
        }
        public static CommerceReceiptAudit Sales(BusinessSession session)
        {
            // RISK7：按逐单“实际实收”拼账（含暴躁双倍追回），不再 served × 固定菜价——
            // A01 未收回债权/坏账一文不进 totalCents。
            // FREEDOM19 (contract art-v19/contracts/core-api.md): the actual
            // free-product identity is BusinessSession.OrderDishName(order.id)
            // and order.paidCents is the authoritative receipt money. Each
            // served order is its own row (quantity=1); order.recipeIndex is
            // only a legacy icon adapter, never a grouping key, so custom /
            // free-made products are never collapsed into fixed recipes and
            // the advertised price never replaces the actual receipt.
            var lines=new List<CommerceReceiptLine>();int total=0;
            foreach(StallOrder order in session.Orders)
            {
                if(!order.served)continue;
                string name=session.OrderDishName(order.id);
                if(string.IsNullOrEmpty(name))name="现做餐品";
                total+=order.paidCents;
                lines.Add(new CommerceReceiptLine{id="sale-"+order.id,name=name,unit="份",quantity=1,
                    unitCents=order.paidCents,subtotalCents=order.paidCents,recipeIndex=order.recipeIndex,
                    quotedCents=session.OrderPriceCents(order.id)});
            }
            int pending=session.PendingRiskCents;
            return new CommerceReceiptAudit{kind="day-sales",status=pending>0?"部分待收 · 逃单追回中":"已收款",cashCents=session.CashCents,totalCents=total,ledgerCents=session.RevenueCents,totalsMatch=total==session.RevenueCents,lines=lines.ToArray()};
        }
        public static string Money(int cents)=>"¥"+(cents/100m).ToString("0.00",CultureInfo.InvariantCulture);
        public static string PurchaseUnit(string id)=>id=="E02"?"组":id.StartsWith("P")?"捆":id.Length==3&&id[0]=='S'?"瓶":SupplyCatalog.IsEquipment(id)?"件":id.EndsWith("_small")?"包":id=="seasoning"?"桶":"箱";
        public static string Contents(string id,int amount)=>id=="E02"?"3 罐丁烷气 · 45 热能":id.Length==3&&id[0]=='S'?"每瓶 "+amount+" 份 · 风味调味，随餐扣份":SupplyCatalog.IsEquipment(id)?"设备 / 用具":id.StartsWith("P")?"每捆 "+amount+" 个":"内含 "+amount+" 份原料";
        public static string StockUnit(string id)=>id.StartsWith("P")?"个":"份";
        public static string ShortName(string id)
        {
            bool small=id.EndsWith("_small");string key=SupplyCatalog.IngredientId(id);string name;
            switch(key)
            {
                case "rice":name="米面主食";break;case "egg":name="鸡蛋青菜";break;case "meat":name="鲜肉海鲜";break;
                case "dough":name="煎烙饼胚";break;case "skewer":name="串串半成品";break;case "prepared":name="预制料理包";break;
                case "fruit":name="鲜果茶饮料组";break;case "seasoning":name="油盐酱料";break;
                case "P02":return "发泡饭盒";case "P03":return "竹签纸托";case "P04":return "牛皮纸袋";case "P05":return "PP 密封餐盒";
                // 冰粉/热汤用防烫汤碗（P07）与茶饮封口杯（P08）在账单、货架上
                // 是两种不同的包材，名称不共用，各自对应不同的菜品包材槽。
                case "P06":return "大号餐盒";case "P07":return "防烫汤碗";case "P08":return "高透封口杯";case "P09":return "醒狮纸袋";case "P10":return "纸瓦罐";case "P11":return "竹编食盒";case "P12":return "竹筒杯";
                case "S01":return "辣椒油";case "S02":return "蒜蓉";case "S05":return "甜辣酱";
                default:return (GameCatalog.Find(id)?.name??CharacterCatalog.EquipmentName(id)).Split('·')[0];
            }
            return small?name+" · 散装":name;
        }
    }
}
