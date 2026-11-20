using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Synthetic v23-format career6 snapshots enter the real Save/Load boundary.
    // Business and second-day checks use the production state and cooking APIs.
    public static class LegacyFlavorMigration24Validation
    {
        [Serializable] sealed class Check { public string name,error="";public bool passed; }
        [Serializable] sealed class Report
        {public bool success;public string scope="Synthetic legacy snapshots; real CampaignSaveStore.Save/Load, BusinessSession and cross-day state. No player save or hardware input.";public List<Check> checks=new List<Check>();}
        static Report report;static string output;static int serial;
        static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
        static void Case(string name,Action test)
        {
            var item=new Check{name=name};report.checks.Add(item);string previous=CampaignSaveStore.TestDirectory;
            string dir=Path.Combine(Path.GetDirectoryName(output),"case-"+(++serial));Need(!Directory.Exists(dir),"preserve earlier case files");CampaignSaveStore.TestDirectory=dir;
            try{test();item.passed=true;}catch(Exception e){item.error=e.ToString();}finally{CampaignSaveStore.TestDirectory=previous;}
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--flavor24-migration-report");
            if(at<0||at+1>=args.Length)throw new ArgumentException("--flavor24-migration-report required");
            output=Path.GetFullPath(args[at+1]);Need(!File.Exists(output),"preserve previous report");Directory.CreateDirectory(Path.GetDirectoryName(output));
            report=new Report();Case("home old snapshot, raw file, save/load twice",HomeRoundtrip);
            Case("active business mirrors, consumption, settlement and second day",BusinessToSecondDay);
            Case("site selection return to market retains bottles",SiteReturn);
            Case("night save resumes next day with stock",NightToNextDay);
            Case("existing same-ID inventory remains untouched",ExistingStock);
            Case("business existing inventory and consumed difference preserved",BusinessExistingStock);
            Case("inconsistent mirror and cargo remain untouched",AmbiguousMirrors);
            Case("backup fallback migrates without touching damaged current",BackupFallback);
            Case("new-format existing stock is unchanged",CurrentStock);
            report.success=report.checks.All(x=>x.passed);File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("FLAVOR24_MIGRATION_"+(report.success?"PASS":"FAIL")+" "+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static CampaignSaveData Legacy(GamePhase phase)
        {
            var p=new RunProfile{careerId="career6",selectedTraits=Array.Empty<string>()};Need(p.Validate(out _),"career6 profile invalid");
            var run=new RunState(.05f,p);var campaign=CampaignState.New(p,run);var r=run.CaptureSave();
            r.phase=phase;r.loadedEquipment=r.loadedEquipment.Concat(new[]{"S13","S14"}).ToArray();
            r.supplies=r.supplies.Where(x=>x.id!="S13"&&x.id!="S14").ToArray();
            campaign.stock.RemoveAll(x=>x.id=="S13"||x.id=="S14");
            campaign.equipment.Add(new CampaignGearRow{instanceId="old-s13",id="S13",name="珍珠",location="vehicle"});
            campaign.equipment.Add(new CampaignGearRow{instanceId="old-s14",id="S14",name="椰果",location="vehicle"});
            return new CampaignSaveData{campaign=campaign,run=r};
        }
        static void AttachBusiness(CampaignSaveData data,bool opened)
        {
            var r=data.run;if(!r.loadedEquipment.Contains("V15"))r.loadedEquipment=r.loadedEquipment.Concat(new[]{"V15"}).ToArray();
            r.stoveLoaded=true;r.supplies=r.supplies.Concat(new[]{new RunStockSave{id="fruit",quantity=3}}).ToArray();
            var run=RunState.Restore(r);var setup=ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run);
            setup.canReturnMarket=true;setup.customerIntervalSeconds=1;setup.queueCapacity=8;
            var service=new BusinessSession(setup);service.SelectedRecipes.Clear();
            if(opened)Need(service.Open(),"legacy business failed to open: "+service.Message);
            data.business=service.CaptureSave();data.hasBusiness=true;
        }
        static CampaignSaveData Load(CampaignSaveData data,out string status)
        {
            Need(CampaignSaveStore.Save(data,out status),"cannot write legacy fixture: "+status);
            string path=Path.Combine(CampaignSaveStore.TestDirectory,"current.json"),before=File.ReadAllText(path);
            Need(CampaignSaveStore.Load(out var loaded,out status),"cannot load legacy fixture: "+status);
            Need(File.ReadAllText(path)==before,"Load unexpectedly overwrote original save");return loaded;
        }
        static int RunStock(CampaignSaveData data,string id)=>data.run.supplies?.FirstOrDefault(x=>x.id==id)?.quantity??0;
        static int Owned(CampaignSaveData data,string id)=>data.campaign.stock?.FirstOrDefault(x=>x.id==id)?.quantity??0;
        static int BusinessStock(BusinessStockSaveData[] rows,string id)=>rows?.FirstOrDefault(x=>x.id==id)?.count??0;
        static void Converted(CampaignSaveData data,string id,int amount=30)
        {
            Need(!data.run.loadedEquipment.Contains(id)&&!data.run.homeEquipment.Contains(id),"run still holds legacy equipment "+id);
            Need(!data.campaign.equipment.Any(x=>x.id==id),"campaign still holds legacy equipment "+id);
            Need(RunStock(data,id)==amount&&Owned(data,id)==amount,"wrong mirrored stock for "+id);
            Need(data.campaign.stock.Single(x=>x.id==id).instanceId=="old-"+id.ToLowerInvariant(),"lost instance identity "+id);
        }
        static void HomeRoundtrip()
        {
            var old=Legacy(GamePhase.Home);int cash=old.run.cashCents;var data=Load(old,out string status);
            Need(!status.Contains("待核对"),status);Converted(data,"S13");Converted(data,"S14");Need(data.run.cashCents==cash,"cash changed");
            string original=File.ReadAllText(Path.Combine(CampaignSaveStore.TestDirectory,"current.json"));
            for(int i=0;i<2;i++)
            {
                Need(CampaignSaveStore.Save(data,out status),"repeat save: "+status);
                if(i==0)Need(File.ReadAllText(Path.Combine(CampaignSaveStore.TestDirectory,"backup.json"))==original,"original old save not preserved in backup");
                Need(CampaignSaveStore.Load(out data,out status),"repeat load: "+status);Converted(data,"S13");Converted(data,"S14");
            }
        }
        static void BusinessToSecondDay()
        {
            var old=Legacy(GamePhase.Business);AttachBusiness(old,true);var data=Load(old,out string status);Converted(data,"S13");Converted(data,"S14");
            foreach(string id in new[]{"S13","S14"})
            {
                Need(!data.business.equipment.Contains(id)&&!data.business.setup.equipment.Contains(id),"business equipment not cleared");
                Need(BusinessStock(data.business.inventory,id)==30&&BusinessStock(data.business.setup.inventory,id)==30,"business stock mirrors incorrect");
            }
            Need(CampaignSaveStore.Save(data,out status)&&CampaignSaveStore.Load(out data,out status),"business re-save failed");
            var session=BusinessSession.Restore(data.business);Need(session.AddFreeIngredient(0,"fruit"),"fruit action");
            Need(session.SetFreeMethod(0,FreeKitchenRules.MethodMuddle)&&session.StartFreeCooking(0)&&session.Action(0),"real drink actions");
            Need(session.Action(0,"S13")&&session.PackToCounter(0,"P08"),"real S13 packing action: "+session.Message);
            Need(session.Count("S13")==29&&session.Count("S14")==30,"actual use did not consume once");session.Close();
            var run=RunState.Restore(data.run);Need(data.campaign.CommitDay(run,session,session.SessionId),"day did not settle");
            Need(data.campaign.stock.Single(x=>x.id=="S13").quantity==29&&data.campaign.stock.Single(x=>x.id=="S14").quantity==30,"day settlement lost stock");
            var next=data.campaign.CreateNextDay(.05f);Need(next.Supplies["S13"]==29&&next.Supplies["S14"]==30,"next day lost stock");
            Need(!next.LoadedEquipment.Contains("S13")&&!next.LoadedEquipment.Contains("S14"),"next day resurrected legacy gear");
        }
        static void SiteReturn()
        {
            var old=Legacy(GamePhase.SiteSelection);AttachBusiness(old,false);var data=Load(old,out _);Converted(data,"S13");Converted(data,"S14");
            var run=RunState.Restore(data.run);run.ReturnToMarket();var setup=ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run);
            Need(setup.inventory["S13"]==30&&setup.inventory["S14"]==30,"return to market discarded bottles");
        }
        static void NightToNextDay()
        {
            var old=Legacy(GamePhase.Business);AttachBusiness(old,true);var session=BusinessSession.Restore(old.business);session.Close();
            var run=RunState.Restore(old.run);Need(old.campaign.CommitDay(run,session,session.SessionId),"legacy day commit");
            var night=RunState.FromCampaign(old.campaign,.05f);night.SetBusinessPhase(GamePhase.Night);old.run=night.CaptureSave();old.business=null;old.hasBusiness=false;
            var data=Load(old,out _);Converted(data,"S13");Converted(data,"S14");
            var next=data.campaign.CreateNextDay(.05f);Need(next.Supplies["S13"]==30&&next.Supplies["S14"]==30,"night to next day discarded bottles");
        }
        static void ExistingStock()
        {
            var old=Legacy(GamePhase.Home);old.run.supplies=old.run.supplies.Concat(new[]{new RunStockSave{id="S13",quantity=7}}).ToArray();
            old.campaign.stock.Add(new CampaignStockRow{instanceId="stock-existing",id="S13",name="原有珍珠",quantity=7,acquiredDay=1});
            var data=Load(old,out string status);Need(status.Contains("待核对"),"ambiguous existing stock not reported");
            Need(RunStock(data,"S13")==7&&Owned(data,"S13")==7&&data.run.loadedEquipment.Contains("S13")&&data.campaign.equipment.Any(x=>x.id=="S13"),"existing S13 overwritten");
            Converted(data,"S14");
            for(int i=0;i<2;i++)
            {Need(CampaignSaveStore.Save(data,out status)&&CampaignSaveStore.Load(out data,out status),"ambiguous re-save");Need(RunStock(data,"S13")==7&&Owned(data,"S13")==7&&data.run.loadedEquipment.Contains("S13"),"ambiguous S13 changed on repeat");Converted(data,"S14");}
        }
        static void AmbiguousMirrors()
        {
            var old=Legacy(GamePhase.SiteSelection);AttachBusiness(old,false);old.business.equipment=old.business.equipment.Where(x=>x!="S13").ToArray();
            old.run.cargo=(old.run.cargo??Array.Empty<string>()).Concat(new[]{"S14"}).ToArray();
            var data=Load(old,out string status);Need(status.Contains("待核对"),"ambiguous mirrors not reported");
            Need(data.run.loadedEquipment.Contains("S13")&&data.campaign.equipment.Any(x=>x.id=="S13")&&!data.business.equipment.Contains("S13"),"mismatched S13 changed");
            Need(data.run.cargo.Contains("S14")&&data.run.loadedEquipment.Contains("S14")&&data.campaign.equipment.Any(x=>x.id=="S14"),"cargo S14 changed");
            Need(RunStock(data,"S13")==0&&RunStock(data,"S14")==0,"ambiguous assets synthesized stock");
        }
        static void BusinessExistingStock()
        {
            var old=Legacy(GamePhase.Business);AttachBusiness(old,true);
            old.run.supplies=old.run.supplies.Concat(new[]{new RunStockSave{id="S13",quantity=7}}).ToArray();
            old.campaign.stock.Add(new CampaignStockRow{instanceId="existing-s13",id="S13",name="原有珍珠",quantity=7,acquiredDay=1});
            old.business.setup.inventory=old.business.setup.inventory.Concat(new[]{new BusinessStockSaveData{id="S13",count=7}}).ToArray();
            old.business.inventory=old.business.inventory.Concat(new[]{new BusinessStockSaveData{id="S13",count=5}}).ToArray();
            var data=Load(old,out string status);Need(status.Contains("待核对"),"business overlap not reported");
            Need(RunStock(data,"S13")==7&&Owned(data,"S13")==7&&BusinessStock(data.business.setup.inventory,"S13")==7&&BusinessStock(data.business.inventory,"S13")==5,"existing business stock changed");
            Need(data.run.loadedEquipment.Contains("S13")&&data.business.equipment.Contains("S13")&&data.campaign.equipment.Any(x=>x.id=="S13"),"ambiguous bottle removed");
            Need(CampaignSaveStore.Save(data,out status)&&CampaignSaveStore.Load(out data,out status),"business overlap re-save");
            Need(BusinessStock(data.business.setup.inventory,"S13")==7&&BusinessStock(data.business.inventory,"S13")==5,"business consumed difference changed on reload");
            Converted(data,"S14");
        }
        static void BackupFallback()
        {
            var old=Legacy(GamePhase.Home);Need(CampaignSaveStore.Save(old,out string status),"write backup fixture");
            string current=Path.Combine(CampaignSaveStore.TestDirectory,"current.json"),backup=Path.Combine(CampaignSaveStore.TestDirectory,"backup.json");
            string bytes=File.ReadAllText(current);File.WriteAllText(backup,bytes);File.WriteAllText(current,"damaged fixture");
            Need(CampaignSaveStore.Load(out var loaded,out status)&&status.Contains("备份"),"backup not selected");
            Converted(loaded,"S13");Converted(loaded,"S14");
            Need(File.ReadAllText(current)=="damaged fixture"&&File.ReadAllText(backup)==bytes,"fallback overwrote original slots");
        }
        static void CurrentStock()
        {
            var p=new RunProfile{careerId="career6",selectedTraits=Array.Empty<string>()};var run=new RunState(.05f,p);var campaign=CampaignState.New(p,run);
            var data=Load(new CampaignSaveData{campaign=campaign,run=run.CaptureSave()},out string status);
            Need(!status.Contains("待核对")&&RunStock(data,"S13")==30&&RunStock(data,"S14")==30,"current stock changed");
        }
    }
}
