using System;
using System.Collections.Generic;

namespace Guochao
{
    public sealed partial class RunState
    {
        public bool TryPlaceOnlineOrder(CampaignState campaign,string productId,int quotedCents,out string reason)
        {
            reason="";
            if(campaign==null||Phase!=GamePhase.Night){reason="夜间才能上网下单。";return false;}
            // NightHub deliberately pauses the world while the ledger is open.
            // That is not a pause-menu lock on night shopping.
            var day=campaign.lastDay;
            var twin=day==null?null:campaign.history.Find(x=>x.sessionId==day.sessionId&&x.dayIndex==day.dayIndex);
            if(day==null||!day.committed||day.dayIndex!=campaign.dayIndex||twin==null||!twin.committed)
            {reason="先完成今天的日结，再夜间下单。";return false;}
            var sku=OnlineCatalog.FindSku(productId);int quote=OnlineCatalog.QuoteCents(Profile,productId);
            if(sku==null||quote<=0){reason="这里暂时没有这件商品。";return false;}
            if(quote!=quotedCents){reason="报价已变化，请按当前价格重新确认。";return false;}
            var orders=campaign.onlineOrders??new List<OnlineOrder>();
            if(sku.durable&&(campaign.equipment.Exists(x=>x.id==productId)||HomeEquipment.Contains(productId)||LoadedEquipment.Contains(productId)))
            {reason="这件设备已经拥有。";return false;}
            if(sku.durable&&orders.Exists(x=>x.productId==productId&&!x.received))
            {reason="这件设备已下单，等包裹送来吧。";return false;}
            if(CashCents!=campaign.cashCents||day.closingCashCents!=campaign.cashCents||twin.closingCashCents!=campaign.cashCents)
            {reason="账本现金已变化，请重新打开夜间采购。";return false;}
            if(quote>CashCents){reason="现金还差 ¥"+((quote-CashCents)/100f).ToString("0.00")+"。";return false;}
            if(campaign.dayIndex==int.MaxValue||day.onlinePurchasesCents<0||day.onlinePurchasesCents>int.MaxValue-quote||twin.onlinePurchasesCents!=day.onlinePurchasesCents)
            {reason="今天暂时不能再下单。";return false;}
            var order=new OnlineOrder{id=Guid.NewGuid().ToString("N"),productId=productId,paidCents=quote,placedDay=campaign.dayIndex,dueDay=campaign.dayIndex+1};
            int nextCash=CashCents-quote;
            CashCents=nextCash;campaign.cashCents=nextCash;campaign.onlineOrders=orders;orders.Add(order);
            day.onlinePurchasesCents+=quote;day.closingCashCents=nextCash;
            if(!ReferenceEquals(day,twin)){twin.onlinePurchasesCents+=quote;twin.closingCashCents=nextCash;}
            Message="已付款 · "+sku.name+"，明早 05:00 送到家门口。";return true;
        }
        public bool TryClaimOnlineOrder(CampaignState campaign,string orderId,out string reason)
        {
            reason="";
            if(campaign==null||Phase!=GamePhase.Home){reason="包裹在家门口。";return false;}
            if(Paused){reason="先继续游戏，再拆包裹。";return false;}
            if(Remaining<=0){reason="该出门了，包裹留待明早再拆。";return false;}
            if(HoldingStove||!string.IsNullOrEmpty(HeldGrocery)){reason="先腾出双手，再拆包裹。";return false;}
            var order=campaign.onlineOrders?.Find(x=>x.id==orderId);
            if(order==null){reason="没有找到这件包裹。";return false;}
            if(order.received){reason="这件包裹已经收好了。";return false;}
            if(order.dueDay>campaign.dayIndex){reason="包裹还在路上。";return false;}
            var sku=OnlineCatalog.FindSku(order.productId);
            if(sku==null){reason="这件包裹暂时不能拆开。";return false;}
            string id=order.productId,key=SupplyCatalog.IngredientId(id);
            int quantity=0;
            if(sku.durable)
            {
                if(HomeEquipment.Contains(id)||LoadedEquipment.Contains(id)||Cargo.Contains(id)||campaign.equipment.Exists(x=>x.id==id))
                {reason="已带着同款设备，先整理现有家当再装这件。";return false;}
                var proposed=new List<string>(LoadedEquipment){id};
                string error=LoadValidator?.Invoke(proposed,Cargo);
                if(!string.IsNullOrEmpty(error)){reason=error;return false;}
            }
            else
            {
                Supplies.TryGetValue(key,out quantity);
                if(quantity<0||sku.portions<=0||quantity>int.MaxValue-sku.portions){reason="这类物资已经太多了，先用完一些再拆。";return false;}
                // The capacity check includes existing PackingSupplies through
                // GameFlow's validator. The newly delivered box is the only
                // temporary cargo item; after success its portions join the
                // quantity ledger rather than the unique-ID market cargo list.
                var proposed=new List<string>(Cargo){id};
                string error=LoadValidator?.Invoke(LoadedEquipment,proposed);
                if(!string.IsNullOrEmpty(error)){reason=error;return false;}
            }
            if(sku.durable)
            {
                LoadedEquipment.Add(id);
                campaign.equipment.Add(new CampaignGearRow{instanceId=Guid.NewGuid().ToString("N"),id=id,name=sku.name,condition=100,location="vehicle"});
                StoveLoaded=Profile==null?LoadedEquipment.Contains(GameCatalog.StoveId):!HasUnloadedEquipment;
            }
            else Supplies[key]=quantity+sku.portions;
            order.received=true;order.receivedDay=campaign.dayIndex;
            Message="已装上摊车 · "+sku.name;return true;
        }
    }
}
