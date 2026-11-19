using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class CampaignResaleRow
    { public string id,name,source;public float condition;public int amountCents; }
    [Serializable] public sealed class UsedPurchasePrice { public string id;public int priceCents; }
    public sealed class RecoveryQuote
    { public string id,name,source;public float condition;public int priceCents; }
    // Ordinary procurement-market resale, not debt pawn or delayed night listings.
    public static class RecoveryRules
    {
        // Content-library original catalogue prices for owned gear not yet sold
        // by the prototype's limited counter. No guessed prices for unknown IDs.
        static readonly Dictionary<string,int> OtherPrices=new Dictionary<string,int>{
            {"B04",78000},{"B05",28000},{"B06",42000},{"B07",26000},{"B08",140000},
            {"V02",14000},{"V03",19000},{"V04",220000},{"V06",65000},{"V07",195000},{"V10",26000},{"V11",4500},{"V12",58000},{"V14",98000},{"V16",9000},
            {"U01",500},{"U02",3500},{"U03",2800},{"U05",3800},{"U07",4500},{"U09",4800},{"U15",1200},
            {"E04",18000},{"E05",30000},{"E06",180000},{"E07",3500},{"E08",18000},{"E09",8500},{"E10",5500}};
        public static readonly string[] GuaranteedUsedIds={"B01","V01","U01"};
        static readonly ItemDefinition UsedWoodSpatula=new ItemDefinition("U01","起毛旧木铲",1,500,"SupplyCounter",CampaignUI.Gold);
        public static ItemDefinition UsedDefinition(string id)=>id=="U01"?UsedWoodSpatula:null;
        public static RecoveryQuote UsedQuote(RunState run,CampaignState campaign,string id)
        {
            if(run==null||campaign==null||run.Phase!=GamePhase.Market||Array.IndexOf(GuaranteedUsedIds,id)<0)return null;
            if(run.UsedBoughtIds.Contains(id)||run.HomeEquipment.Contains(id)||run.LoadedEquipment.Contains(id)||run.Cart.Contains(id)||run.Cargo.Contains(id)||run.ReturnedPaidCargo.Contains(id)||campaign.equipment.Exists(x=>x.id==id))return null;
            int original=SupplyCatalog.Find(id)?.priceCents??0;if(original<=0)return null;
            return new RecoveryQuote{id=id,name=CharacterCatalog.EquipmentName(id),source="旧货保底",condition=50,
                priceCents=(int)Math.Round(original*.5*.65,MidpointRounding.AwayFromZero)};
        }
        public static bool IsDurable(string id)=>!string.IsNullOrEmpty(id)&&SupplyCatalog.IsEquipment(id)&&id!="E01"&&id!="E02";
        public static int HagglingLevel(RunProfile p)
        {if(p==null)return 0;int level=p.careerId=="career2"?2:p.careerId=="career1"||p.careerId=="career5"||p.careerId=="career8"?1:0;return level+(p.Has("嘴甜")?1:0);}
        public static RecoveryQuote Quote(RunState run,CampaignState campaign,string id)
        {
            if(run==null||campaign==null||run.Phase!=GamePhase.Market||!IsDurable(id))return null;
            bool inPaidCart=run.Paid&&run.Cart.Contains(id),inCargo=run.Cargo.Contains(id),aboard=run.LoadedEquipment.Contains(id),held=run.HoldingStove&&run.HeldEquipmentId==id;
            if(!inPaidCart&&!inCargo&&!aboard&&!held)return null;
            CampaignGearRow row=campaign.equipment.Find(x=>x.id==id);
            // New purchases have an explicit paid-cart/cargo receipt. Other gear
            // must already belong to this campaign, not just be a catalogue ID.
            if(row==null&&!inPaidCart&&!inCargo)return null;
            int original=SupplyCatalog.Find(id)?.priceCents??(OtherPrices.TryGetValue(id,out int known)?known:0);
            if(original<=0)return null;
            float condition=row?.condition??100;
            if(float.IsNaN(condition)||float.IsInfinity(condition)||condition<0||condition>100)return null;
            double multiplier=1+.015*HagglingLevel(run.Profile);
            if((id=="B07"||id=="B08")&&run.Profile?.Has("科技与狠活大师")==true)multiplier*=1.2;
            int quote=(int)Math.Round(original*(condition/100d)*.45*multiplier,MidpointRounding.AwayFromZero);
            if(quote<=0)return null;
            return new RecoveryQuote{id=id,name=CharacterCatalog.EquipmentName(id),source=inPaidCart?"已付款篮中":inCargo?"已付货舱":held?"手中自有":"已装车",condition=condition,priceCents=quote};
        }
    }
}
