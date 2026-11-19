using System.Collections.Generic;

namespace Guochao
{
    public sealed partial class GameFlow
    {
        // RECOVERY12: the trade gate is the authored "18_回收台" recovery table
        // (RecoveryCounter anchor); the original procurement SupplyCounter
        // entry stays valid, same basket-at-side 1.8 m rule, same pricing,
        // receipt and autosave judgements underneath.
        private bool AtRecoveryCounter()=>world!=null&&run!=null&&!run.Paused&&run.Phase==GamePhase.Market&&
            (world.IsNear("RecoveryCounter")||world.IsNear("SupplyCounter"))&&world.ShoppingMotor!=null&&world.ShoppingMotor.IsNearBasket(1.8f);
        public bool CanUseRecoveryCounter=>AtRecoveryCounter()&&!run.CheckoutInProgress&&string.IsNullOrEmpty(run.HeldGrocery);
        public RecoveryQuote[] GetResaleQuotes()
        {
            var result=new List<RecoveryQuote>();if(Campaign==null||run==null)return result.ToArray();
            var ids=new SortedSet<string>(run.LoadedEquipment);ids.UnionWith(run.Cargo);if(run.Paid)ids.UnionWith(run.Cart);
            if(run.HoldingStove)ids.Add(run.HeldEquipmentId);
            foreach(string id in ids){var quote=RecoveryRules.Quote(run,Campaign,id);if(quote!=null)result.Add(quote);}
            return result.ToArray();
        }
        public RecoveryQuote[] GetUsedPurchaseQuotes()
        {
            var list=new List<RecoveryQuote>();foreach(string id in RecoveryRules.GuaranteedUsedIds)
            {var q=RecoveryRules.UsedQuote(run,Campaign,id);if(q!=null)list.Add(q);}return list.ToArray();
        }
        public bool BuyUsedEquipment(string id,int quoteCents)
        {
            if(!AtRecoveryCounter())return false;
            bool ok=run.TryBuyUsedEquipment(Campaign,id,quoteCents);world.Sync(run);Publish();if(ok)QueueAutoSave();return ok;
        }
        public bool SellEquipment(string id,int quoteCents)
        {
            if(!AtRecoveryCounter()){run?.SetMessage("请带着购物车到回收站（旧货摊回收台）或采购柜台办理旧货代收。");return false;}
            bool ok=run.TrySellEquipment(Campaign,id,quoteCents);world.Sync(run);Publish();if(ok)QueueAutoSave();return ok;
        }
        public bool ReturnPaidPurchasesHome()
        {
            if(!AtRecoveryCounter()||!run.ReturnPaidCargoHome())return false;
            SupplyShop.Close();world.Sync(run);EnterBusiness();business.CloseDay();Publish();QueueAutoSave();return true;
        }
    }
}
