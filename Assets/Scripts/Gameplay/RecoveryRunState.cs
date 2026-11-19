using System;
using System.Collections.Generic;

namespace Guochao
{
    public sealed partial class RunState
    {
        public int ResaleIncomeCents { get; private set; }
        public int DisposedPurchaseCents { get; private set; }
        public readonly List<string> UsedBoughtIds=new List<string>();
        public readonly Dictionary<string,int> UsedPriceOverrides=new Dictionary<string,int>();
        public int PurchaseItemPriceCents(string id)=>UsedPriceOverrides.TryGetValue(id,out int price)?price:PriceCents(id);
        public int CargoPaidCents
        {
            get{int home=0;foreach(string id in ReturnedPaidCargo)home+=PurchaseItemPriceCents(id);return SpentCents-DisposedPurchaseCents-(Paid?CartTotalCents:0)-home;}
        }
        public bool TryBuyUsedEquipment(CampaignState campaign,string id,int quotedPriceCents)
        {
            if(Phase!=GamePhase.Market||Paused||CheckoutInProgress||Remaining<=0||!string.IsNullOrEmpty(HeldGrocery))return false;
            if(Cart.Count>0&&!Paid)return Reject("先结清或放回篮中未付款商品，再买旧货。");
            RecoveryQuote quote=RecoveryRules.UsedQuote(this,campaign,id);
            if(quote==null||quote.priceCents!=quotedPriceCents)return Reject("这件旧货已售出或你已拥有同款。");
            if(CashCents<quote.priceCents)return Reject("现金不足，旧货尚未扣款。");
            if((long)SpentCents+quote.priceCents>int.MaxValue)return false;
            if(!CanFitCart(id))return Reject("购物篮装不下，请先装车。");
            var pending=new List<string>(Cart);pending.Add(id);string loadError=CheckPendingCargo(pending);
            if(!string.IsNullOrEmpty(loadError))return Reject(loadError);
            CashCents-=quote.priceCents;SpentCents+=quote.priceCents;Cart.Add(id);Paid=true;Loaded=false;
            UsedBoughtIds.Add(id);UsedPriceOverrides[id]=quote.priceCents;campaign.cashCents=CashCents;
            campaign.equipment.Add(new CampaignGearRow{instanceId=Guid.NewGuid().ToString("N"),id=id,name=quote.name,condition=quote.condition,location="basket"});
            Message="已买下二手"+quote.name+" · ¥"+(quote.priceCents/100f).ToString("0.00")+"，物品在篮中，请到车边装载。";
            return true;
        }
        public readonly List<CampaignResaleRow> Resales=new List<CampaignResaleRow>();
        public readonly List<string> ReturnedPaidCargo=new List<string>();
        public bool PendingPaidLoad=>Phase==GamePhase.Market&&Remaining<=0&&Paid&&Cart.Count>0;
        public bool TrySellEquipment(CampaignState campaign,string id,int quotedPriceCents)
        {
            if(Phase!=GamePhase.Market||Paused||CheckoutInProgress||!string.IsNullOrEmpty(HeldGrocery))return false;
            if(Remaining<=0&&!PendingPaidLoad)return Reject("采购已结束。");
            RecoveryQuote quote=RecoveryRules.Quote(this,campaign,id);
            if(quote==null||quote.priceCents!=quotedPriceCents)return Reject("物品或报价已变化，请重新查看。");
            if((long)CashCents+quote.priceCents>int.MaxValue||(long)ResaleIncomeCents+quote.priceCents>int.MaxValue)return false;
            // All validation precedes this single synchronous transaction. Home
            // and loaded lists can be aliases of the same owned heavy object.
            bool ownedEnergy=LoadedEquipment.Contains(id)||HomeEquipment.Contains(id)||HeldEquipmentId==id;
            if(Cargo.Contains(id)||Paid&&Cart.Contains(id))DisposedPurchaseCents+=PurchaseItemPriceCents(id);
            UsedPriceOverrides.Remove(id);
            HomeEquipment.RemoveAll(x=>x==id);LoadedEquipment.RemoveAll(x=>x==id);Cargo.RemoveAll(x=>x==id);
            if(Paid){Cart.RemoveAll(x=>x==id);if(Cart.Count==0)Paid=false;}
            if(HeldEquipmentId==id){HeldEquipmentId="";HoldingStove=false;}
            campaign.equipment.RemoveAll(x=>x.id==id);
            if(ownedEnergy)
            {
                if(id=="E03"){GasPoints=CartridgePoints;campaign.gas=campaign.cartridge;}
                else if(id=="B02"){GasPoints=Math.Max(0,GasPoints-CartridgePoints);CartridgePoints=0;campaign.gas=Math.Max(0,campaign.gas-campaign.cartridge);campaign.cartridge=0;}
                else if(id=="E05"){ElectricityPoints=0;campaign.electricity=0;}
            }
            CashCents+=quote.priceCents;ResaleIncomeCents+=quote.priceCents;campaign.cashCents=CashCents;
            Resales.Add(new CampaignResaleRow{id=id,name=quote.name,source=quote.source,condition=quote.condition,amountCents=quote.priceCents});
            StoveLoaded=Profile==null?LoadedEquipment.Contains(GameCatalog.StoveId):!HasUnloadedEquipment;
            Message="已出售"+quote.name+" · 收款 ¥"+(quote.priceCents/100f).ToString("0.00");
            return true;
        }
        internal void PreservePaidCartAtSettlement()
        {
            // Older saves could already be at service with a paid, unloaded
            // basket. Preserve that ownership at the only nightly merge too.
            if(!Paid||Cart.Count==0)return;
            ReturnedPaidCargo.AddRange(Cart);Cart.Clear();Paid=false;
        }
        public bool ReturnPaidCargoHome()
        {
            if(Phase!=GamePhase.Market||Paused||CheckoutInProgress||!Paid||Cart.Count==0||!string.IsNullOrEmpty(HeldGrocery))return false;
            PreservePaidCartAtSettlement();
            Phase=GamePhase.SiteSelection;
            Message="已付款货物带回家整理；今天不开摊，不退款。";
            return true;
        }
    }
}
