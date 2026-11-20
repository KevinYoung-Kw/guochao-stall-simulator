using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Guochao
{
    [Serializable] public sealed class CampaignSaveEnvelope
    {public int version=1;public string payload,sha256;}

    public static class CampaignSaveStore
    {
        const string Key="Guochao.Campaign.v1";
        public static string TestDirectory;
        public static string DirectoryPath
        {
            get
            {
                if(!string.IsNullOrEmpty(TestDirectory))return TestDirectory;
                string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--campaign-save-dir");
                return at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"Campaigns");
            }
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int GuochaoCampaignWrite(string key,string value);
        [DllImport("__Internal")] static extern string GuochaoCampaignRead(string key);
#endif
        public static bool Exists=>!string.IsNullOrEmpty(ReadRaw("current"))||!string.IsNullOrEmpty(ReadRaw("backup"));
        static string FilePath(string slot)=>Path.Combine(DirectoryPath,slot+".json");
        static string ReadRaw(string slot)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return GuochaoCampaignRead(Key+"."+slot);
#else
            try{return File.Exists(FilePath(slot))?File.ReadAllText(FilePath(slot),Encoding.UTF8):null;}catch{return null;}
#endif
        }
        static void WriteRaw(string slot,string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if(GuochaoCampaignWrite(Key+"."+slot,text)==0)throw new IOException("浏览器未能保存进度，请检查存储空间与站点权限。");
#else
            Directory.CreateDirectory(DirectoryPath);string target=FilePath(slot),temp=target+".tmp";
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
            {byte[] bytes=Encoding.UTF8.GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
#endif
        }
        static string Hash(string text)
        {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static string Encode(CampaignSaveData data)
        {
            if(data==null||data.run==null)throw new InvalidDataException("存档缺少当前流程。");
            bool service=data.run.phase==GamePhase.SiteSelection||data.run.phase==GamePhase.Business||data.run.phase==GamePhase.DaySummary;
            if(service!=(data.business!=null))throw new InvalidDataException("存档阶段与经营数据不一致。");
            // Unity inline serialization recreates a null class as an empty
            // object. Persist presence explicitly, before serializing it.
            data.hasBusiness=data.business!=null;
            string json=JsonUtility.ToJson(data);return JsonUtility.ToJson(new CampaignSaveEnvelope{payload=json,sha256=Hash(json)});
        }
        public static CampaignSaveData Decode(string json)
        {
            if(string.IsNullOrEmpty(json))throw new InvalidDataException("没有经营存档。");
            var envelope=JsonUtility.FromJson<CampaignSaveEnvelope>(json);
            if(envelope==null||envelope.version!=1||string.IsNullOrEmpty(envelope.payload)||Hash(envelope.payload)!=envelope.sha256)
                throw new InvalidDataException("存档校验失败。");
            var data=JsonUtility.FromJson<CampaignSaveData>(envelope.payload);
            if(data==null||data.schemaVersion!=1||data.campaign==null||data.run==null||data.campaign.dayIndex<1||data.campaign.profile==null)
                throw new InvalidDataException("存档格式不兼容。");
            RunState.Restore(data.run);
            long integrity=0,packagingScore=0;
            foreach(var day in data.campaign.history)
            {
                if(day==null||day.resaleIncomeCents<0||day.unseasonedPortionsServed<0||day.unseasonedPortionsServed>day.served||
                    (long)day.integrityChange!=(long)day.unseasonedPortionsServed*SeasoningShortageRules.IntegrityDeltaPerShortPortion)
                    throw new InvalidDataException("日账的缺调味记录无效。");
                if(day.emergencyPackagesUsed<0||day.emergencyPackagesUsed>day.served||(long)day.packagingScoreDelta!=(long)day.emergencyPackagesUsed*BusinessSession.EmergencyPackagingScorePerSale)
                    throw new InvalidDataException("日账的应急包材记录无效。");
                packagingScore+=day.packagingScoreDelta;
                long sold=0;foreach(var row in day.resales??new System.Collections.Generic.List<CampaignResaleRow>())
                {if(row==null||row.amountCents<=0)throw new InvalidDataException("日账的旧货收款无效。");sold+=row.amountCents;}
                if(sold!=day.resaleIncomeCents)throw new InvalidDataException("旧货日账不守恒。");
                integrity+=day.integrityChange;
            }
            if(integrity!=data.campaign.integrityChangeTotal)throw new InvalidDataException("跨日诚信记录不一致。");
            StallProgressionRules.ValidateAndNormalizeSave(data.campaign);
            if(packagingScore!=data.campaign.packagingScoreTotal)throw new InvalidDataException("跨日包装评分不一致。");
            // Night online orders: structurally valid, unique, legal days, and
            // their per-day ledger totals must match the frozen deal prices.
            // Old saves simply carry no orders and a zero ledger field.
            if(data.campaign.onlineOrders==null)data.campaign.onlineOrders=new System.Collections.Generic.List<OnlineOrder>();
            var orderIds=new System.Collections.Generic.HashSet<string>();
            var onlineSpendByDay=new System.Collections.Generic.Dictionary<int,long>();
            foreach(var order in data.campaign.onlineOrders)
            {
                if(order==null||string.IsNullOrEmpty(order.id)||!orderIds.Add(order.id))throw new InvalidDataException("网购订单记录无效。");
                if(!OnlineCatalog.IsSku(order.productId))throw new InvalidDataException("网购订单商品无效。");
                if(order.paidCents<=0||order.paidCents>int.MaxValue)throw new InvalidDataException("网购订单金额无效。");
                if(order.placedDay<1||order.placedDay>data.campaign.dayIndex||(long)order.dueDay!=(long)order.placedDay+1)
                    throw new InvalidDataException("网购订单日期无效。");
                if(order.received)
                {if(order.receivedDay<order.dueDay||order.receivedDay>data.campaign.dayIndex)throw new InvalidDataException("网购签收日期无效。");}
                else if(order.receivedDay!=0)throw new InvalidDataException("网购签收记录无效。");
                onlineSpendByDay.TryGetValue(order.placedDay,out long sum);
                if(sum>int.MaxValue-order.paidCents)throw new InvalidDataException("网购日账金额无效。");
                onlineSpendByDay[order.placedDay]=sum+order.paidCents;
            }
            foreach(var paidDay in onlineSpendByDay)
                if(data.campaign.history.FindAll(x=>x!=null&&x.committed&&x.dayIndex==paidDay.Key).Count!=1)
                    throw new InvalidDataException("网购订单缺少对应日账。");
            foreach(var day in data.campaign.history)
            {
                if(day==null)continue;
                long expected=0;bool has=onlineSpendByDay.TryGetValue(day.dayIndex,out expected);
                if(day.onlinePurchasesCents<0||day.onlinePurchasesCents>int.MaxValue||(day.onlinePurchasesCents>0)!=(has&&expected>0)||
                    (has&&day.onlinePurchasesCents!=expected))throw new InvalidDataException("网购日账不守恒。");
            }
            if(data.campaign.lastDay!=null)
            {
                long expected=0;bool has=onlineSpendByDay.TryGetValue(data.campaign.lastDay.dayIndex,out expected);
                if(data.campaign.lastDay.onlinePurchasesCents<0||data.campaign.lastDay.onlinePurchasesCents!=expected)
                    throw new InvalidDataException("网购日账不守恒。");
            }
            bool servicePhase=data.run.phase==GamePhase.SiteSelection||data.run.phase==GamePhase.Business||data.run.phase==GamePhase.DaySummary;
            if(servicePhase!=data.hasBusiness)throw new InvalidDataException("存档阶段与经营数据不一致。");
            if(data.hasBusiness)BusinessSession.Restore(data.business);else data.business=null;
            return data;
        }
        public static bool Save(CampaignSaveData data,out string status)
        {
            try
            {
                string encoded=Encode(data);Decode(encoded);
                string current=ReadRaw("current");
                if(!string.IsNullOrEmpty(current))
                {
                    bool valid=false;try{Decode(current);valid=true;}catch{}
                    if(valid)WriteRaw("backup",current);
                }
                WriteRaw("current",encoded);
                if(ReadRaw("current")!=encoded)throw new IOException("保存后的校验不一致。");
                status="第 "+data.campaign.dayIndex+" 天 · 进度已保存";return true;
            }
            catch(Exception error){status="未能保存："+error.Message;return false;}
        }
        public static bool Load(out CampaignSaveData data,out string status)
        {
            data=null;string last="没有可继续的经营记录。";
            foreach(string slot in new[]{"current","backup"})
            {
                try
                {
                    string raw=ReadRaw(slot);data=Decode(raw);
                    status=slot=="backup"?"主存档异常，已恢复上一份有效备份。":"已读取第 "+data.campaign.dayIndex+" 天的进度";
                    // Work on a second decoded copy: migration failure must not
                    // discard a valid original or silently switch save slots.
                    try
                    {
                        if(LegacyFlavorSaveMigration.HasMarkers(data))
                        {
                            var candidate=Decode(raw);var migrated=LegacyFlavorSaveMigration.Apply(candidate);
                            if(migrated.migrated>0)data=Decode(Encode(candidate));
                            if(migrated.ambiguous>0)status+="；部分旧配料记录待核对，原记录已保留";
                        }
                    }
                    catch(Exception error)
                    {
                        Debug.LogWarning("旧配料迁移未应用，原记录已保留："+error.Message);
                        status+="；旧配料暂未转换，原记录已保留";
                    }
                    return true;
                }
                catch(Exception error){last=error.Message;}
            }
            status="无法继续："+last;return false;
        }
        public static bool ArchiveBeforeNewRun(out string status)
        {
            try
            {
                if(Load(out var old,out _))
                {
                    // Preserve a previous campaign independently of rolling saves.
                    string name="previous-"+old.campaign.campaignId;
                    WriteRaw(name,Encode(old));WriteRaw("previous",Encode(old));
                }
                status="";return true;
            }
            catch(Exception error){status="原经营记录未能备份："+error.Message;return false;}
        }
    }
}
