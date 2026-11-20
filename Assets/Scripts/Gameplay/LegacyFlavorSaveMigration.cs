using System;
using System.Collections.Generic;

namespace Guochao
{
    // v23 career6 saved these two starter bottles as durable equipment because
    // the flavor definitions were not yet available. This pass runs only at
    // the campaign load boundary on an isolated decoded candidate. It never
    // mutates a live RunState or an on-disk save.
    internal static class LegacyFlavorSaveMigration
    {
        internal sealed class Result { public int migrated,ambiguous; }
        static readonly string[] LegacyIds={"S13","S14"};

        static int Count(string[] values,string id)
        {int n=0;foreach(string value in values??Array.Empty<string>())if(value==id)n++;return n;}
        static bool Has(RunStockSave[] values,string id)
        {foreach(var row in values??Array.Empty<RunStockSave>())if(row!=null&&row.id==id)return true;return false;}
        static bool Has(BusinessStockSaveData[] values,string id)
        {foreach(var row in values??Array.Empty<BusinessStockSaveData>())if(row!=null&&row.id==id)return true;return false;}
        static string[] Remove(string[] values,string id)
        {var next=new List<string>();foreach(var value in values??Array.Empty<string>())if(value!=id)next.Add(value);return next.ToArray();}
        static RunStockSave[] Add(RunStockSave[] values,string id,int quantity)
        {var next=new List<RunStockSave>(values??Array.Empty<RunStockSave>());next.Add(new RunStockSave{id=id,quantity=quantity});return next.ToArray();}
        static BusinessStockSaveData[] Add(BusinessStockSaveData[] values,string id,int quantity)
        {var next=new List<BusinessStockSaveData>(values??Array.Empty<BusinessStockSaveData>());next.Add(new BusinessStockSaveData{id=id,count=quantity});return next.ToArray();}

        internal static bool HasMarkers(CampaignSaveData save)
        {
            if(save?.run==null||save.campaign==null)return false;
            foreach(string id in LegacyIds)
            {
                if(Count(save.run.homeEquipment,id)+Count(save.run.loadedEquipment,id)>0)return true;
                if(save.campaign.equipment!=null)foreach(var row in save.campaign.equipment)if(row!=null&&row.id==id)return true;
                if(save.hasBusiness&&save.business!=null&&
                    Count(save.business.equipment,id)+Count(save.business.setup?.equipment,id)>0)return true;
            }
            return false;
        }

        internal static Result Apply(CampaignSaveData save)
        {
            var result=new Result();if(save?.run==null||save.campaign==null)return result;
            RunSaveData run=save.run;CampaignState campaign=save.campaign;BusinessSaveData business=save.hasBusiness?save.business:null;
            foreach(string id in LegacyIds)
            {
                int runLoaded=Count(run.loadedEquipment,id),runHome=Count(run.homeEquipment,id);
                int current=business==null?0:Count(business.equipment,id),initial=business?.setup==null?0:Count(business.setup.equipment,id);
                int owned=0;CampaignGearRow gear=null;
                if(campaign.equipment!=null)foreach(var row in campaign.equipment)if(row!=null&&row.id==id){owned++;gear=row;}
                bool marker=runLoaded+runHome+current+initial+owned>0;
                if(!marker)continue;
                bool safe=campaign.profile?.careerId=="career6"&&run.profile?.careerId=="career6"&&
                    runLoaded==1&&runHome==0&&owned==1&&gear!=null&&!string.IsNullOrEmpty(gear.instanceId)&&
                    gear.condition==100f&&(gear.location=="home"||gear.location=="vehicle")&&campaign.stock!=null;
                safe=safe&&!Has(run.supplies,id)&&Count(run.cart,id)==0&&Count(run.cargo,id)==0&&Count(run.returnedPaidCargo,id)==0&&
                    run.heldGrocery!=id&&run.heldEquipmentId!=id;
                if(campaign.stock!=null)foreach(var row in campaign.stock)
                    if(row==null||row.id==id||(gear!=null&&!string.IsNullOrEmpty(gear.instanceId)&&row.instanceId==gear.instanceId))safe=false;
                if(campaign.equipment!=null&&gear!=null)foreach(var row in campaign.equipment)
                    if(row==null||(row!=gear&&row.instanceId==gear.instanceId))safe=false;
                if(gear!=null&&campaign.nightVehicleGear!=null&&campaign.nightVehicleGear.Contains(gear.instanceId))safe=false;
                if(save.hasBusiness)
                    safe=safe&&business?.setup!=null&&current==1&&initial==1&&
                        !Has(business.inventory,id)&&!Has(business.setup.inventory,id);
                if(!safe){result.ambiguous++;continue;}

                StallFlavor flavor=StallFlavorMenu.Find(id);
                if(flavor==null||flavor.portionsPerBottle<=0){result.ambiguous++;continue;}
                int portions=flavor.portionsPerBottle;
                // These are snapshots of the same original bottle, not four
                // bottles. Run supports returning from site selection; campaign
                // stock supports Night -> next day; business initial/current
                // retain the same consumed difference and CommitDay overwrites
                // the campaign snapshot with the actual remaining quantity.
                run.loadedEquipment=Remove(run.loadedEquipment,id);run.supplies=Add(run.supplies,id,portions);
                campaign.equipment.Remove(gear);
                campaign.stock.Add(new CampaignStockRow{instanceId=gear.instanceId,id=id,name=flavor.name,quantity=portions,acquiredDay=1});
                if(business!=null)
                {
                    business.equipment=Remove(business.equipment,id);business.setup.equipment=Remove(business.setup.equipment,id);
                    business.inventory=Add(business.inventory,id,portions);business.setup.inventory=Add(business.setup.inventory,id,portions);
                }
                result.migrated++;
            }
            return result;
        }
    }
}
