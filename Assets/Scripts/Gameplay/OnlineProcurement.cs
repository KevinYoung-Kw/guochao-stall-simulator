using System;
using System.Collections.Generic;

namespace Guochao
{
    // A night online purchase: paid immediately at order time, delivered as a
    // cardboard parcel at the home door the next morning (dueDay). The frozen
    // deal price is stored on the order so old saves never re-quote.
    [Serializable] public sealed class OnlineOrder
    {
        public string id="";
        public string productId="";
        public long paidCents;
        public int placedDay;
        public int dueDay;
        public bool received;
        public int receivedDay;
    }

    // Content-library S6 night channel ("拼夕夕"-style): a small fixed shelf,
    // not the whole catalogue. Prices come straight from the original tables:
    // B03 ¥120, V02 ¥105 (next-day price), V03 ¥190, U03 ¥28, packaging cases
    // at 80% of the 50-pack retail price, and a 50-portion seasoning case at
    // the S00 unit price 0.5/portion with no invented discount. The 网购达人
    // trait grants a further 0.88 on this channel only; it never multiplies
    // physical-counter prices, and neither does 商超老会员 apply here.
    public static class OnlineCatalog
    {
        public sealed class Sku
        {
            public readonly string id,name;
            public readonly int baseCents,portions;
            public readonly bool durable;
            public Sku(string id,string name,int baseCents,int portions,bool durable)
            {this.id=id;this.name=name;this.baseCents=baseCents;this.portions=portions;this.durable=durable;}
        }
        public static readonly Sku[] Skus={
            new Sku("B03","单眼猛火灶",12000,1,true),
            new Sku("V02","章丘锻打深口铁锅",10500,1,true),
            new Sku("V03","双耳工地大生铁锅",19000,1,true),
            new Sku("U03","耐高温硅胶防粘铲",2800,1,true),
            // FLAVOR11: V16's authored channel is 次日达（¥90）, which is exactly
            // this night next-day shop — not the physical 商超五金区 counter.
            // (V15 stays a physical counter good at its authored ¥130.) The ¥9000
            // base is the authored price verbatim; 网购达人 may discount it like
            // any night sku. Claim/delivery rides the existing durable-parcel
            // path (TryClaimOnlineOrder → LoadedEquipment), and its 1×1 deck
            // vessel slot is already registered in PackingCatalog.
            new Sku("V16","古法杉木冰粉手作桶",9000,1,true),
            new Sku("P02","发泡饭盒·50个整箱",960,50,false),
            new Sku("P04","牛皮纸袋·50个整箱",800,50,false),
            new Sku("P05","PP饭盒·50个整箱",2000,50,false),
            new Sku("seasoning","散装油盐·50份整箱",2500,50,false)};
        public static Sku FindSku(string productId)=>Array.Find(Skus,x=>x.id==productId);
        public static bool IsSku(string productId)=>FindSku(productId)!=null;
        // Round half away from zero to the cent; quote is frozen at order time.
        public static int QuoteCents(RunProfile profile,string productId)
        {
            Sku sku=FindSku(productId);if(sku==null)return 0;
            double value=sku.baseCents;
            if(profile!=null&&profile.Has("网购达人"))value*=.88;
            return (int)Math.Round(value,MidpointRounding.AwayFromZero);
        }
        public static bool TryDescribe(string productId,out string name,out int portions)
        {Sku sku=FindSku(productId);name=sku?.name??"";portions=sku?.portions??0;return sku!=null;}
    }
}
