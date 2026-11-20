using System;

namespace Guochao
{
    public sealed partial class BusinessSession
    {
        // Reserve wrapper is a delivery fact, never a purchased inventory SKU.
        public const string EmergencyBagId="EBAG";
        public const int EmergencyPackagingScorePerSale=-5;
        public bool CanUseEmergencyBag(FoodProduct product)
        {
            if(product==null||product.packages==null)return false;
            foreach(string id in product.packages)if(Count(id)>0)return false;
            return true;
        }
        public int EmergencyPackagesSold
        {
            get {int n=0;foreach(var order in Orders)if(order.served&&order.emergencyPackage)n++;return n;}
        }
        public int EmergencyPackagingScore=>EmergencyPackagesSold*EmergencyPackagingScorePerSale;
        public string[] AvailableLanePackages(int laneIndex)
        {
            var normal=LanePackages(laneIndex);var product=ProductAtLane(laneIndex);
            if(!CanUseEmergencyBag(product))return normal;
            var options=new string[normal.Length+1];Array.Copy(normal,options,normal.Length);options[normal.Length]=EmergencyBagId;return options;
        }
    }
}
