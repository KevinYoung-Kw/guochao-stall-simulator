using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Guochao
{
    public readonly struct BasketFootprint
    {
        public readonly int Columns,Rows;
        public BasketFootprint(int columns,int rows){Columns=columns;Rows=rows;}
    }
    public sealed class ShoppingBasketTuning
    {
        // Original documents specify hooks/sockets/bundles, not their shopping-cart rectangles.
        // These are explicit transport tuning, never claimed as authored source dimensions.
        public BasketFootprint SmallItemCells=new BasketFootprint(1,1);
        public BasketFootprint SpareVesselCells=new BasketFootprint(1,1);
        public readonly Dictionary<string,BasketFootprint> Overrides=new Dictionary<string,BasketFootprint>(StringComparer.Ordinal)
        { {"RiceBucket",new BasketFootprint(1,1)} };
    }
    public sealed class BasketPlacement
    {
        public int InputIndex { get; internal set; }
        public string ItemId { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public bool Rotated { get; internal set; }
        public bool UsesTunedFootprint { get; internal set; }
        public string FootprintSource { get; internal set; }
    }
    public sealed class ShoppingBasketPlan
    {
        public bool Success { get; internal set; }
        public int Columns=>3;
        public int Rows { get; internal set; }
        public int Capacity { get; internal set; }
        public int UsedCells { get; internal set; }
        public string FailureCode { get; internal set; }="";
        public string Reason { get; internal set; }="";
        public ReadOnlyCollection<BasketPlacement> Placements { get; internal set; }=Array.AsReadOnly(Array.Empty<BasketPlacement>());
        // Row-major. -1 is empty; occupied cells identify original input index (duplicates remain distinct).
        public ReadOnlyCollection<int> CellOwners { get; internal set; }=Array.AsReadOnly(Array.Empty<int>());
        public bool PhysicalVolumeValidated=>false;
    }

    // Exact 2D logical fit for the 3-column shopping basket. This deliberately does
    // not infer kilogram limits, 3D basket volume, stack heights or mesh scale.
    public static class ShoppingBasketLayout
    {
        private sealed class Item
        {
            public int index,width,height;public string id,source;public bool tuned;
            public int Area=>width*height;
        }
        public static bool CanFit(IEnumerable<string> itemIds,int capacity,ShoppingBasketTuning tuning=null)=>Plan(itemIds,capacity,tuning).Success;
        // -1 means an unknown item/unconfigured footprint, never zero-cost capacity.
        public static int UsedCells(IEnumerable<string> itemIds,ShoppingBasketTuning tuning=null)
        {
            return ReadItems(itemIds,tuning??new ShoppingBasketTuning(),out var items,out int area,out _,out _)?area:-1;
        }
        public static ShoppingBasketPlan Plan(IEnumerable<string> itemIds,int capacity,ShoppingBasketTuning tuning=null)
        {
            var plan=new ShoppingBasketPlan{Capacity=capacity,Rows=capacity>0?capacity/3:0};
            if(capacity<=0||capacity%3!=0)return Reject(plan,"InvalidCapacity","购物篮容量必须是正的 3 倍数（3 列 × 行数）。");
            if(!ReadItems(itemIds,tuning??new ShoppingBasketTuning(),out var items,out int area,out string code,out string reason))return Reject(plan,code,reason);
            plan.UsedCells=area;
            if(area>capacity)return Reject(plan,"CellCapacityExceeded","物品共占 "+area+" 格，超过购物篮 "+capacity+" 格。");
            foreach(var item in items)
                if(!FitsDimensions(item.width,item.height,3,plan.Rows)&&!FitsDimensions(item.height,item.width,3,plan.Rows))
                    return Reject(plan,"ItemTooLarge",item.id+" 的 "+item.width+"×"+item.height+" 占格旋转后仍放不进 3×"+plan.Rows+" 购物篮。");
            // Global repacking, largest first, then exhaustive alternatives. Input order
            // cannot make a valid layout fail merely because small items were added first.
            items.Sort((a,b)=>{int size=b.Area.CompareTo(a.Area);if(size!=0)return size;
                int side=Math.Max(b.width,b.height).CompareTo(Math.Max(a.width,a.height));return side!=0?side:a.index.CompareTo(b.index);});
            var cells=new int[capacity];for(int i=0;i<cells.Length;i++)cells[i]=-1;
            var placements=new BasketPlacement[items.Count];
            if(!Place(0,items,plan.Rows,cells,placements,new HashSet<string>()))
                return Reject(plan,"NoRectangleLayout","总格数虽够，但这些矩形无法共同排进 3×"+plan.Rows+" 购物篮；旋转与全局重排已检查。");
            plan.Success=true;plan.Placements=Array.AsReadOnly(placements);plan.CellOwners=Array.AsReadOnly(cells);return plan;
        }
        private static ShoppingBasketPlan Reject(ShoppingBasketPlan plan,string code,string reason)
        {plan.FailureCode=code;plan.Reason=reason;return plan;}
        private static bool FitsDimensions(int width,int height,int columns,int rows)=>width<=columns&&height<=rows;
        private static bool ReadItems(IEnumerable<string> ids,ShoppingBasketTuning tuning,out List<Item> items,out int area,out string code,out string reason)
        {
            items=new List<Item>();area=0;code="";reason="";
            if(ids==null){code="MissingItems";reason="缺少购物篮物品清单。";return false;}
            foreach(string id in ids)
            {
                bool small=id!=null&&id.EndsWith("_small",StringComparison.Ordinal);
                string canonical=small?id.Substring(0,id.Length-6):id;
                ItemPackingSpec spec=PackingCatalog.Find(canonical);
                if(spec==null){code="UnknownItem";reason="物品 "+(id??"<null>")+" 未登记占格类型。";return false;}
                BasketFootprint footprint;bool tuned=false;string source=spec.source;
                if(tuning.Overrides.TryGetValue(id,out footprint)){tuned=true;source="购物篮显式逐项调参；原文未给该储运占格";}
                else if(small){footprint=tuning.SmallItemCells;tuned=true;source="散称小包装购物篮占格调参，不宣称原文标准筐尺寸";}
                else if(spec.HasGridFootprint)footprint=new BasketFootprint(spec.columns,spec.rows);
                else if((spec.tags&PackingTag.Vessel)!=0){footprint=tuning.SpareVesselCells;tuned=true;source="备用锅购物篮储运占格调参；炉上安装槽不等于购物篮占格";}
                else if((spec.tags&(PackingTag.Tool|PackingTag.Packaging|PackingTag.Seasoning|PackingTag.Small))!=0)
                {footprint=tuning.SmallItemCells;tuned=true;source="小工具/包材/调味等购物篮占格调参；原文未给通用储运矩形";}
                else {code="UnspecifiedFootprint";reason=id+" 未给购物篮占格，请提供显式运输尺寸配置。";return false;}
                long itemArea=(long)footprint.Columns*footprint.Rows;
                if(footprint.Columns<=0||footprint.Rows<=0||itemArea>int.MaxValue-area)
                {code="InvalidFootprint";reason=id+" 的购物篮占格配置无效。";return false;}
                items.Add(new Item{index=items.Count,id=id,width=footprint.Columns,height=footprint.Rows,tuned=tuned,source=source});area+=(int)itemArea;
            }
            return true;
        }
        private static bool Place(int at,List<Item> items,int rows,int[] cells,BasketPlacement[] placements,HashSet<string> failed)
        {
            if(at==items.Count)return true;
            var occupied=new char[cells.Length];for(int i=0;i<cells.Length;i++)occupied[i]=cells[i]<0?'0':'1';
            string state=at+":"+new string(occupied);if(failed.Contains(state))return false;
            Item item=items[at];
            for(int rotation=0;rotation<(item.width==item.height?1:2);rotation++)
            {
                int width=rotation==0?item.width:item.height,height=rotation==0?item.height:item.width;
                for(int y=0;y<=rows-height;y++)for(int x=0;x<=3-width;x++)
                {
                    if(!Clear(cells,x,y,width,height))continue;
                    Fill(cells,x,y,width,height,item.index);
                    placements[item.index]=new BasketPlacement{InputIndex=item.index,ItemId=item.id,X=x,Y=y,Width=width,Height=height,Rotated=rotation==1,UsesTunedFootprint=item.tuned,FootprintSource=item.source};
                    if(Place(at+1,items,rows,cells,placements,failed))return true;
                    placements[item.index]=null;Fill(cells,x,y,width,height,-1);
                }
            }
            failed.Add(state);return false;
        }
        private static bool Clear(int[] cells,int x,int y,int width,int height)
        {for(int row=y;row<y+height;row++)for(int col=x;col<x+width;col++)if(cells[row*3+col]>=0)return false;return true;}
        private static void Fill(int[] cells,int x,int y,int width,int height,int owner)
        {for(int row=y;row<y+height;row++)for(int col=x;col<x+width;col++)cells[row*3+col]=owner;}
    }
}
