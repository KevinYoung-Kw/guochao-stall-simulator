using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class CareerDefinition
    {
        public string id,name,skills,innate,gearDescription;
        public int cash,points;
        public string[] gearIds;
    }
    [Serializable] public sealed class TraitDefinition
    {
        public string id,category,purchase,business,night,opposite,note;
        public int points;
    }
    [Serializable] public sealed class EquipmentDefinition { public string id,name,description; }
    [Serializable] public sealed class CharacterCatalog
    {
        public CareerDefinition[] careers;
        public TraitDefinition[] traits;
        public EquipmentDefinition[] equipment;
        private static CharacterCatalog cached;
        public static CharacterCatalog Data => cached ?? (cached=JsonUtility.FromJson<CharacterCatalog>(Resources.Load<TextAsset>("Data/CharacterCatalog").text));
        public static string EquipmentName(string id) => Array.Find(Data.equipment,x=>x.id==id)?.name ?? id;
    }
    [Serializable] public sealed class RunProfile
    {
        public string careerId="career1";
        public string[] selectedTraits=Array.Empty<string>();
        public string debtDifficulty="标准";
        public CareerDefinition Career => Array.Find(CharacterCatalog.Data.careers,x=>x.id==careerId);
        public bool Has(string trait) => Array.IndexOf(selectedTraits,trait)>=0 || (Career!=null && Career.innate.Contains("【"+trait+"】"));
        public int Balance
        {
            get { int value=Career?.points??0;foreach(string name in selectedTraits){var t=Array.Find(CharacterCatalog.Data.traits,x=>x.id==name);if(t!=null)value+=t.points;}return value; }
        }
        public bool Validate(out string reason)
        {
            if(Career==null){reason="请选择出身";return false;}
            var names=new HashSet<string>();
            foreach(string name in selectedTraits)
            {
                var t=Array.Find(CharacterCatalog.Data.traits,x=>x.id==name);
                if(t==null||!names.Add(name)){reason="特质配置无效";return false;}
                if(Career.innate.Contains("【"+name+"】")){reason=name+"是职业自带特质，无需重复选择";return false;}
            }
            foreach(var t in CharacterCatalog.Data.traits)
                if(!string.IsNullOrEmpty(t.opposite)&&Has(t.id))
                    foreach(string opposed in t.opposite.Replace("、",",").Split(','))
                        if(Has(opposed.Trim())){reason=t.id+"与"+opposed+"互斥";return false;}
            if(Balance<0){reason="还缺 "+(-Balance)+" 点：减少长处或选择一项短板";return false;}
            reason="特质确定后，本局保持不变";return true;
        }
        public int InitialCashCents => (Career?.cash??320)*100;
        public int InitialDebtCents => careerId=="career8"?300000:0;
        public float HomeSeconds => Has("嗜睡")?50:60;
        public int CartCapacity => Has("收纳达人")?24:Has("丢三落四")?12:18;
        public float TransferSeconds => .55f*(Has("心灵手巧")?1f/1.5f:Has("笨手笨脚")?2f:1f);
        public float TravelSeconds => 15*(Has("飙车党")?.75f:Has("龟速司机")?1.4f:1f);
        public float PurchaseMultiplier => Has("商超老会员")?.85f:1;
        public float InitialReputation => careerId=="career2"?2.5f:1.5f;
        public string[] Gear => Career?.gearIds??Array.Empty<string>();
    }
}
