using System;
using System.Collections.Generic;

namespace Guochao
{
    /// <summary>
    /// FREEDOM19：自由锅位只读解说。只读取真实 freeBatch.inputs 与公开查询 API，
    /// 绝不修改库存/会话，也绝不猜测“正在搬运中”的食材。设备与能源可行性用
    /// FreeKitchenRules.TryBindHardware 按 FreeKitchenSession 同款口径复核
    /// （扣除其他锅位占用的容器/底座，B04 按双炉位计），无法可靠判断时只给
    /// “试试其他做法”，不做假肯定。
    /// </summary>
    public static class FreePanGuide
    {
        public sealed class Readout
        {
            public string state="",next="",reference="",method="";
            public FoodInput[] inputs=Array.Empty<FoodInput>();
            public string[] methods=Array.Empty<string>();
            public bool canStart;
        }

        // 与 FreeKitchenSession.EnergyAvailable 同口径（该函数为 private，此处按
        // 公开燃料计数等价复算，绝不臆造能源状态）。
        static bool EnergyOk(BusinessSession s,string energy)
            =>energy=="none"||energy=="gas"&&s.Gas>0||energy=="cartridge"&&s.Cartridge>0
                ||energy=="coal"&&s.Coal>0||energy=="electricity"&&s.Electricity>0;

        // OccupiedBases 的公开等价：B04 是双炉位（占用 2 才算满），其余底座 1。
        static HashSet<string> OccupiedBases(BusinessSession s,int exceptLane)
        {
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            for(int i=0;i<s.Lanes.Length;i++)
            {
                if(i==exceptLane)continue;
                var b=s.Lanes[i].freeBatch;
                if(b==null||b.baseId=="")continue;
                counts[b.baseId]=counts.TryGetValue(b.baseId,out int n)?n+1:1;
            }
            var full=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in counts)if(p.Value>=(p.Key=="B04"?2:1))full.Add(p.Key);
            return full;
        }

        // AvailableKitchenEquipment 的公开等价：全局装备集扣除其他正在干活锅位的锅具。
        static HashSet<string> AvailableEquipment(BusinessSession s,int exceptLane)
        {
            var set=new HashSet<string>(s.Equipment,StringComparer.Ordinal);
            for(int i=0;i<s.Lanes.Length;i++)
            {
                if(i==exceptLane)continue;
                var lane=s.Lanes[i];
                if(lane.step==KitchenStep.Idle||lane.step==KitchenStep.Dirty)continue;
                if(lane.freeBatch!=null)set.Remove(lane.freeBatch.vesselId);
                else if(lane.recipeIndex>=0&&lane.recipeIndex<BusinessSession.Recipes.Length)
                    foreach(string v in BusinessSession.Recipes[lane.recipeIndex].vessels)if(set.Remove(v))break;
            }
            return set;
        }

        // 用真实兼容矩阵 + 占用/能源回调复核某做法在本锅位当前是否物理可达。
        static bool TryBind(BusinessSession s,int lane,FreeKitchenRules.MethodDef def,int load,
            out string vessel,out string bottom,out string tool)
            =>FreeKitchenRules.TryBindHardware(def,AvailableEquipment(s,lane),
                e=>EnergyOk(s,e),OccupiedBases(s,lane),out vessel,out bottom,out tool,load);

        static string FuelName(string energy)
            =>energy=="gas"?"煤气":energy=="cartridge"?"卡式气":energy=="coal"?"蜂窝煤":energy=="electricity"?"电量":"能源";

        // 选一条真实参考菜：RecognizesRecipe 保证自由厨房能真实复现该菜谱；
        // 做法必须与本锅已定做法一致，或落在当前 FreeMethods 可行集合内（绝不给
        // 不兼容做法的菜）。优先“锅内材料 ⊆ 菜谱非油盐材料、仍有缺料”的近似菜。
        static string PickReference(BusinessSession s,FreeKitchenBatch batch,string[] options)
        {
            if(batch==null||batch.inputs==null)return "";
            var actual=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var input in batch.inputs)
            {
                if(input==null||input.units<=0||!FreeKitchenRules.IsEdibleMaterial(input.itemId))continue;
                actual[input.itemId]=actual.TryGetValue(input.itemId,out int n)?n+input.units:input.units;
            }
            if(actual.Count==0)return "";
            string exactName="";int exactIndex=-1;
            string nearName="";int nearIndex=-1;var nearMissing=new List<string>();
            StallRecipe[] recipes=BusinessSession.Recipes;
            for(int i=0;i<recipes.Length;i++)
            {
                if(!FreeKitchenRules.RecognizesRecipe(i))continue;
                string recipeMethod=OrderCookingGuide.RecipeMethod(i);
                if(recipeMethod=="")continue;
                if(batch.method!=""){if(recipeMethod!=batch.method)continue;}
                else if(Array.IndexOf(options,recipeMethod)<0)continue;
                var wanted=new Dictionary<string,int>(StringComparer.Ordinal);
                int portions=Math.Max(1,FreeKitchenRules.DerivePortions(batch.inputs));
                foreach(var pair in recipes[i].ingredients)
                    if(pair.Key!="seasoning"&&FreeKitchenRules.IsEdibleMaterial(pair.Key))wanted[pair.Key]=pair.Value*portions;
                if(wanted.Count==0)continue;
                bool subset=true,exact=true;var missing=new List<string>();
                foreach(var pair in actual)
                    if(!wanted.TryGetValue(pair.Key,out int need)||pair.Value>need){subset=false;break;}
                if(!subset)continue;
                foreach(var pair in wanted)
                {
                    actual.TryGetValue(pair.Key,out int have);
                    if(have<pair.Value){exact=false;missing.Add(pair.Key);}
                    else if(have>pair.Value)exact=false;
                }
                if(exact){if(exactIndex<0){exactIndex=i;exactName=recipes[i].name;}}
                else if(nearIndex<0){nearIndex=i;nearName=recipes[i].name;nearMissing=missing;}
            }
            if(nearIndex>=0)
            {
                var parts=new List<string>();
                foreach(string id in nearMissing)
                {
                    string label=FreeKitchenRules.FindMaterial(id)?.name??id;
                    parts.Add(label+(s.Count(id)>0?"（可再放）":"（缺货）"));
                }
                return "参考 "+nearName+" · 可加 "+string.Join("、",parts);
            }
            if(exactIndex>=0)return "参考 "+exactName+" · 食材齐全";
            return "";
        }

        public static Readout Read(BusinessSession session,int laneIndex)
        {
            var result=new Readout();
            if(session==null||laneIndex<0||laneIndex>=session.Lanes.Length)return result;
            KitchenLane lane=session.Lanes[laneIndex];
            FreeKitchenBatch batch=lane.freeBatch;
            bool freePan=batch!=null;

            // ── 非自由锅位 / 空锅 ───────────────────────────────────────
            if(!freePan)
            {
                if(lane.step==KitchenStep.Idle)
                {result.state="空锅";result.next="点选食材，放进锅里";}
                else if(lane.step==KitchenStep.Dirty)
                {result.state="锅具待清理";result.next="点锅清理后再使用";}
                else
                {
                    result.state="菜谱批次 · "+lane.step;
                    result.next="这口锅在按单制作，换一口空锅自由发挥。";
                }
                return result;
            }

            // 只展示真实已下锅的投料（绝不推算搬运中物品）。
            var inputs=new List<FoodInput>();
            if(batch.inputs!=null)foreach(var input in batch.inputs)
                if(input!=null&&input.units>0)inputs.Add(input.Copy());
            result.inputs=inputs.ToArray();

            if(result.inputs.Length==0)
            {result.state="空锅";result.next="先放食材";return result;}

            int load=FreeKitchenRules.InputLoad(result.inputs);
            string[] options=session.FreeMethods(laneIndex);
            var methodLabels=new List<string>();
            foreach(string id in options)
            {
                var def=FreeKitchenRules.FindMethod(id);
                if(def!=null)methodLabels.Add(def.label);
            }
            result.methods=methodLabels.ToArray();

            var chosen=FreeKitchenRules.FindMethod(batch.method);
            result.method=chosen!=null?chosen.label:"";

            // ── 按状态机给下一步 ────────────────────────────────────────
            switch(lane.step)
            {
                case KitchenStep.Prep:
                {
                    result.state="可备菜";
                    result.reference=PickReference(session,batch,options);
                    if(options.Length==0)
                    {
                        result.next="当前锅具无法制作，试试换锅";
                        result.canStart=false;
                        break;
                    }
                    if(batch.method=="")
                    {
                        if(options.Length==1)
                        {
                            var only=FreeKitchenRules.FindMethod(options[0]);
                            if(only!=null&&TryBind(session,laneIndex,only,load,out _,out string bottom,out _))
                            {
                                string energy=FreeKitchenRules.BaseEnergy(bottom);
                                if(EnergyOk(session,energy)){result.next=only.cold?"可以开始调制":"可以开火";result.canStart=true;}
                                else{result.next=FuelName(energy)+"不足，先补充";result.canStart=false;}
                            }
                            else{result.next="试试其他做法";result.canStart=false;}
                        }
                        else{result.next="选做法，再开始";result.canStart=false;}
                    }
                    else
                    {
                        if(Array.IndexOf(options,batch.method)<0)
                        {result.next="暂时做不了，试试其他做法";result.canStart=false;break;}
                        bool materialsOk=true;
                        foreach(var input in result.inputs)
                            if(!FreeKitchenRules.MethodAcceptsMaterial(chosen,input.itemId)){materialsOk=false;break;}
                        if(!materialsOk){result.next="食材不适合，试试其他做法";result.canStart=false;break;}
                        if(chosen.cold)
                        {
                            if(TryBind(session,laneIndex,chosen,load,out _,out _,out _))
                            {result.next="可以开始调制";result.canStart=true;}
                            else{result.next="试试其他做法";result.canStart=false;}
                            break;
                        }
                        if(TryBind(session,laneIndex,chosen,load,out _,out string baseId,out _))
                        {
                            string energy=FreeKitchenRules.BaseEnergy(baseId);
                            if(EnergyOk(session,energy)){result.next="可以开火";result.canStart=true;}
                            else{result.next=FuelName(energy)+"不足，先补充";result.canStart=false;}
                        }
                        else{result.next="试试其他做法";result.canStart=false;}
                    }
                    break;
                }
                case KitchenStep.Heating:
                    result.state="加热中";
                    result.next=lane.remaining>0
                        ?"加热中 · 约 "+((int)Math.Ceiling(lane.remaining))+" 秒后留意起锅时机"
                        :"加热中 · 留意起锅时机";
                    break;
                case KitchenStep.Utensil:
                    bool coldUtensil=FreeKitchenRules.IsColdMethod(batch.method);
                    result.state=coldUtensil?"待调制":"待起锅";
                    result.next=coldUtensil?"点锅完成调制":"点锅翻炒或起锅";
                    break;
                case KitchenStep.Season:
                    result.state="待调味";
                    result.next=lane.seasoned?"已调味 · 选包材装台":"点调料瓶调味，再选包材";
                    break;
                case KitchenStep.Pack:
                    result.state="待装台";
                    result.next="选适配包材装台出餐";
                    break;
                case KitchenStep.ReadyStock:
                    result.state="有余份";
                    result.next="点这口锅盛出剩余熟食";
                    break;
                case KitchenStep.Dirty:
                    result.state="糊锅待清理";
                    result.next="点这口锅清理锅位后才能再用";
                    break;
                default:
                    result.state="空闲";
                    result.next="先放食材";
                    break;
            }
            return result;
        }
    }
}
