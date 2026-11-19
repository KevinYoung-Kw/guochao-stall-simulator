using System;

namespace Guochao
{
    // Source-space circulation, separate from physical clearance. Based on the
    // existing west wall and first shelf, not an invented smaller body shape.
    public static class MarketAisleDirectionRule
    {
        public const float Width=2.4f, ShelfSouth=6.5f, ShelfNorth=13.7f;
        public static bool AllowsSouthbound(float fromX,float fromZ,float toX,float toZ)
        {
            if(toZ<=fromZ+.00001f)return true;
            float lo=Math.Max(0,(ShelfSouth-fromZ)/(toZ-fromZ));
            float hi=Math.Min(1,(ShelfNorth-fromZ)/(toZ-fromZ));
            if(lo>hi)return true;
            return Math.Min(fromX+(toX-fromX)*lo,fromX+(toX-fromX)*hi)>=Width;
        }
    }
}
