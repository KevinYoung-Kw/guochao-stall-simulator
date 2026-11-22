using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Boundary between authoritative inventory and replaceable visual assets.
    // The same plan gates transactions and supplies every visible loaded pose.
    public sealed class VehicleLoadout : MonoBehaviour
    {
        private VehiclePackingGeometry geometry;
        private Transform prototypes;
        private readonly Dictionary<string,PackingItem> measured=new Dictionary<string,PackingItem>();
        private readonly Dictionary<string,GameObject> mounted=new Dictionary<string,GameObject>();
        private Func<string,Transform,GameObject> make;
        private string signature;
        private Transform upgradeVehicle;
        private GameObject upgradeDeck;
        private Material upgradeDeckMaterial;
        private int upgradeLevel=-1;
        private Func<int> upgradeLevelProvider;
        public PackingPlan Plan {get;private set;}
        public void Initialize(Transform vehicle,Func<string,Transform,GameObject> factory,Func<int> levelProvider=null)
        {
            make=factory;
            upgradeVehicle=vehicle;
            upgradeLevelProvider=levelProvider;
            VehicleContainerModules.Install(vehicle);
            geometry=VehiclePackingGeometry.FromCartAI(vehicle);
            int level=upgradeLevelProvider!=null?upgradeLevelProvider():(GameFlow.Instance?.Campaign?.vehicleLevel??1);
            ConfigureVehicleLevel(level,out _);
            var hidden=new GameObject("MeasuredLoadoutPrototypes");hidden.transform.SetParent(transform,false);hidden.SetActive(false);prototypes=hidden.transform;
        }
        private PackingItem Item(string key,string id)
        {
            if(!measured.TryGetValue(id,out var value))
            {
                var model=make(id,prototypes);
                value=PackingItem.Measure(id,id,model.transform,RuntimeItemBalance.WeightKg(id));
                RuntimeItemBalance.Configure(value);measured[id]=value;
            }
            return new PackingItem{instanceId=key,itemId=id,bounds=value.bounds,measuredWeightKg=value.measuredWeightKg,specification=value.specification,hasTopSupport=value.hasTopSupport,topSupportBounds=value.topSupportBounds,hasBottomSupport=value.hasBottomSupport,bottomSupportBounds=value.bottomSupportBounds};
        }
        public string Validate(IReadOnlyList<string> equipment,IReadOnlyList<string> cargo,IEnumerable<string> packages)
        {
            int level=upgradeLevelProvider!=null?upgradeLevelProvider():(GameFlow.Instance?.Campaign?.vehicleLevel??1);
            if(!ConfigureVehicleLevel(level,out string levelError))
            {
                Plan=null;
                signature=null;
                return levelError;
            }
            var all=new List<PackingItem>();var keys=new List<string>();
            foreach(string id in equipment){all.Add(Item("gear_"+id,id));keys.Add("g:"+id);}
            var counts=new Dictionary<string,int>();
            foreach(string id in packages){counts.TryGetValue(id,out int n);counts[id]=n+1;string key="gear_"+id+(n==0?"":"_"+n);all.Add(Item(key,id));keys.Add("p:"+key);}
            // FREEDOM19: every cargo entry is a real physical unit, so each
            // occurrence needs its own unique packing instance id — duplicate
            // "cargo_SKU" ids were rejected by VehiclePacking as duplicate
            // instance IDs, which made a legitimately bought second unit
            // unloadable. Keys stay in sync with ProcurementInstanceKeys.
            for(int i=0;i<cargo.Count;i++)
            {
                string id=cargo[i];
                string instance=ProcurementInstanceKeys.LoadoutKey(cargo,i);
                all.Add(Item(instance,id));keys.Add("c:"+instance);
            }
            string next=string.Join("|",keys);
            if(next!=signature){Plan=VehiclePacking.Plan(all,geometry,new PackingOptions{underBayUnlocked=false});signature=next;}
            if(Plan.FullyValidated)return "";
            if(Plan.failureCode=="SearchBudgetExceeded")return "这批物品暂时没找到装车方案，先少带几件再试。";
            return Plan.failureCode=="Overweight"?"这批家当超过三轮车的 "+geometry.vehicleWeightLimitKg.ToString("0.##")+" kg 承重，请减少装载。":"车上没有适合放置这批物品的空位，请减少采购或调整家当。";
        }
        public bool Apply(string instanceId,GameObject model)
        {
            var p=Plan!=null&&Plan.FullyValidated?Plan.Find(instanceId):null;
            if(p==null)return false;p.Apply(model.transform);return true;
        }
        public void SyncMounted(RunState state)
        {
            var ids=new Dictionary<string,string>();
            foreach(string id in state.LoadedEquipment)if(!state.HomeEquipment.Contains(id))ids["gear_"+id]=id;
            var counts=new Dictionary<string,int>();
            foreach(string id in state.PackingSupplies){counts.TryGetValue(id,out int n);counts[id]=n+1;ids["gear_"+id+(n==0?"":"_"+n)]=id;}
            // Market cargo models belong to WorldRuntime, including their
            // basket-to-vehicle motion. PresentTransport creates its own
            // models only in the separate transport presentation.
            foreach(var entry in ids)
            {
                if(!mounted.TryGetValue(entry.Key,out var model)){model=make(entry.Value,transform);mounted[entry.Key]=model;}
                model.SetActive(Apply(entry.Key,model));
            }
            foreach(var pair in mounted)if(!ids.ContainsKey(pair.Key))pair.Value.SetActive(false);
        }
        public void PresentTransport(IReadOnlyList<string> equipment,IReadOnlyList<string> cargo,IEnumerable<string> packages)
        {
            string error=Validate(equipment,cargo,packages);
            if(!string.IsNullOrEmpty(error))throw new InvalidOperationException(error);
            foreach(var p in Plan.placements)
            {
                if(!mounted.TryGetValue(p.instanceId,out var model)){model=make(p.itemId,transform);mounted[p.instanceId]=model;}
                p.Apply(model.transform);model.SetActive(true);
            }
        }
        private bool ConfigureVehicleLevel(int level,out string error)
        {
            if(!VehicleUpgradeRules.TryGet(level,out var tier,out error))return false;
            if(tier.Level==upgradeLevel)return true;
            geometry=VehiclePackingGeometry.FromCartAI(upgradeVehicle,level);
            Plan=null;
            signature=null;
            if(tier.Level==2)
            {
                if(upgradeDeck==null)
                {
                    upgradeDeck=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    upgradeDeck.name="Lv2 车斗扩展托板";
                    upgradeDeck.layer=WorldRuntime.EnvironmentLayer;
                    var renderer=upgradeDeck.GetComponent<Renderer>();
                    if(renderer!=null)
                    {
                        if(upgradeDeckMaterial==null)
                        {
                            var shader=Shader.Find("Standard");
                            if(shader!=null)
                            {
                                upgradeDeckMaterial=new Material(shader){name="Lv2DeckMaterial",color=new Color(0.64f,0.64f,0.66f,1f)};
                                upgradeDeckMaterial.SetFloat("_Metallic",0.35f);
                                upgradeDeckMaterial.SetFloat("_Glossiness",0.4f);
                            }
                        }
                        if(upgradeDeckMaterial!=null)renderer.sharedMaterial=upgradeDeckMaterial;
                    }
                }
                if(upgradeDeck.transform.parent!=null)upgradeDeck.transform.SetParent(null,false);
                upgradeDeck.transform.position=geometry.ToWorld(new Vector3(-.525f,-.025f,0f));
                upgradeDeck.transform.rotation=geometry.rotation;
                upgradeDeck.transform.localScale=new Vector3(0.21f,0.05f,1.14f);
                if(upgradeVehicle!=null)upgradeDeck.transform.SetParent(upgradeVehicle,true);
                upgradeDeck.SetActive(true);
            }
            else if(tier.Level==1&&upgradeDeck!=null)
            {
                upgradeDeck.SetActive(false);
            }
            upgradeLevel=tier.Level;
            return true;
        }
        private void OnDestroy()
        {
            if(upgradeDeckMaterial==null)return;
            if(Application.isPlaying)Destroy(upgradeDeckMaterial);
            else DestroyImmediate(upgradeDeckMaterial);
            upgradeDeckMaterial=null;
        }
    }
}
