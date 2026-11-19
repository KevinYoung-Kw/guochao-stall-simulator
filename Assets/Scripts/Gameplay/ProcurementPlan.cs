using System;
using System.Collections.Generic;

namespace Guochao
{
    // Pure procurement-plan rules shared by the supply-counter UI and the
    // SHOP7 editor validation. Read-only: no money, stock, cart or world state
    // moves here. The plan itself still comes from OpeningProcurementPlan,
    // which is the single compatibility authority; this class only renders
    // that plan into player-legible facts (price, cash shortfall, per-item
    // blockers, basket fit) and turns it into a verified one-click basket fill.
    public static class ProcurementPlan
    {
        public sealed class Preview
        {
            public OpeningProcurementPlan plan;
            public int totalCents;
            public int cashShortCents;
            public bool fitsBasket;
            // One entry per plan supply id that cannot currently enter the cart.
            public readonly List<KeyValuePair<string,string>> blockers=new List<KeyValuePair<string,string>>();
            public bool CanStart=>plan!=null&&plan.supplies.Length>0;
        }

        // Everything the counter needs to show "今天能卖哪一道 / 还缺什么 /
        // 钱够不够" before the player commits a single cent.
        public static Preview PreviewPlan(RunState run)
        {
            if(run==null)throw new ArgumentNullException(nameof(run));
            var result=new Preview{plan=OpeningProcurementPlan.For(run)};
            result.totalCents=result.plan.totalCents;
            result.cashShortCents=Math.Max(0,result.totalCents+(run.Paid?0:run.CartTotalCents)-run.CashCents);
            var pending=new List<string>(run.Cart);
            foreach(string id in result.plan.supplies)
            {
                if(run.Cart.Contains(id))continue;
                string reason;
                if(!run.CanAddSupply(id,out reason))result.blockers.Add(new KeyValuePair<string,string>(id,reason));
                else pending.Add(id);
            }
            result.fitsBasket=ShoppingBasketLayout.CanFit(pending,run.CartCapacity);
            return result;
        }

        // Apply the plan through the real AddSupply path, one item at a time,
        // returning the ids that actually entered the cart and the real
        // rejection reason for each id that did not. Callers must hold the
        // same preconditions as a manual purchase (at the counter, basket near,
        // unpaid basket); this never overrides CanAddSupply.
        public static List<string> ApplyToCart(RunState run,OpeningProcurementPlan plan,List<KeyValuePair<string,string>> failures)
        {
            if(run==null)throw new ArgumentNullException(nameof(run));
            var added=new List<string>();
            if(plan==null)return added;
            foreach(string id in plan.supplies)
            {
                if(run.Cart.Contains(id))continue;
                if(run.AddSupply(id))added.Add(id);
                else if(failures!=null)failures.Add(new KeyValuePair<string,string>(id,string.IsNullOrEmpty(run.Message)?"现在不能放入购物车。":run.Message));
            }
            return added;
        }
    }
}
