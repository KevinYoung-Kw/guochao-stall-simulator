using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Real imported/factory renderer bounds, authoritative transaction preflight,
    // and static contact fixtures. Does not certify input traversal or visual art.
    public static class V10PackingValidation
    {
        private static readonly StringBuilder log=new StringBuilder();
        private static int failures;
        private static GameObject fixture;
        private static Transform prototypes;
        private static VehiclePackingGeometry geometry;
        private static readonly Dictionary<string,PackingItem> measured=new Dictionary<string,PackingItem>();
        [MenuItem("Guochao/Validate v10 real vehicle packing")]
        public static void Run()
        {
            log.Clear();failures=0;measured.Clear();
            log.AppendLine("V10_PACKING_VALIDATION actual renderer bounds + source grid + gross mass + transaction preflight");
            log.AppendLine("Transport mode: source-unspecified loose pan footprint is explicit 1x1 tuning. Under-bay remains locked. Unknown mass is rejected.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            fixture=new GameObject("V10PackingFixture");
            try
            {
                var cartRoot=new GameObject("CartAI_Measurement");cartRoot.transform.SetParent(fixture.transform,false);
                GameObject asset=Resources.Load<GameObject>("Models/CartAI");Need(asset!=null,"Missing actual CartAI");
                UnityEngine.Object.Instantiate(asset,cartRoot.transform,false);
                VehicleContainerModules.Install(cartRoot.transform);geometry=VehiclePackingGeometry.FromCartAI(cartRoot.transform);
                var hidden=new GameObject("MeasurementOnly");hidden.transform.SetParent(fixture.transform,false);hidden.SetActive(false);prototypes=hidden.transform;
                foreach(var zone in geometry.zones)log.AppendLine("ZONE "+zone.id+" "+zone.interior);
                foreach(CareerDefinition career in CharacterCatalog.Data.careers)Check(career.id+" initial + stocked opening",()=>CheckCareer(career));
                Check("physical rejects / scales / source capacities",CheckRejections);
                cartRoot.SetActive(false);
                Check("HomeCart standing and contact with all career starter loads",CheckHomeContact);
            }
            catch(Exception ex){failures++;log.AppendLine("FAIL fixture "+ex);}
            finally{if(fixture)UnityEngine.Object.DestroyImmediate(fixture);}
            log.AppendLine("NOT_COVERED: hardware movement, camera readability, rendered placement appearance, or working-kitchen socket animation. Static stand fixtures are not traversal.");
            log.AppendLine(failures==0?"V10_PACKING_PASS":"V10_PACKING_FAIL count="+failures);
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v10/review/packing-validation.txt"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--packing-output");if(at>=0&&at+1<args.Length)output=Path.GetFullPath(args[at+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,log.ToString());
            Debug.Log(log.ToString());if(Application.isBatchMode)EditorApplication.Exit(failures==0?0:1);
        }
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        private static void Check(string label,Action action)
        {try{action();log.AppendLine("PASS "+label);}catch(Exception ex){failures++;log.AppendLine("FAIL "+label+" "+ex.Message);}}
        private static RunProfile Profile(CareerDefinition career)
        {
            var p=new RunProfile{careerId=career.id};var selected=new List<string>();
            foreach(var trait in CharacterCatalog.Data.traits)
            {
                if(p.Balance>=0)break;if(trait.points<=0)continue;
                selected.Add(trait.id);p.selectedTraits=selected.ToArray();
                if(!p.Validate(out string reason)&&!reason.StartsWith("还缺 ",StringComparison.Ordinal)){selected.RemoveAt(selected.Count-1);p.selectedTraits=selected.ToArray();}
            }
            Need(p.Validate(out string error),error);return p;
        }
        private static PackingItem Item(string key,string id)
        {
            if(!measured.TryGetValue(id,out var value))
            {
                GameObject model;
                if(id.StartsWith("B")||id.StartsWith("V")||id.StartsWith("U")||id.StartsWith("E")||id.StartsWith("P")||id.StartsWith("S")||id=="RiceBucket")model=EquipmentVisualFactory.Create(id,prototypes);
                else model=(GameObject)typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{id,prototypes,.36f});
                value=PackingItem.Measure(id,id,model.transform,RuntimeItemBalance.WeightKg(id));RuntimeItemBalance.Configure(value);measured[id]=value;
                log.AppendLine("MODEL "+id+" metres="+value.bounds.size.ToString("F4")+" rootOffset="+value.bounds.center.ToString("F4")+" grossKg="+value.WeightKg.ToString("0.##")+" source="+value.Spec.source);
            }
            return new PackingItem{instanceId=key,itemId=id,bounds=value.bounds,measuredWeightKg=value.measuredWeightKg,specification=value.specification,hasTopSupport=value.hasTopSupport,topSupportBounds=value.topSupportBounds};
        }
        private static List<PackingItem> Items(IReadOnlyList<string> equipment,IReadOnlyList<string> cargo,IEnumerable<string> packages)
        {
            var items=new List<PackingItem>();foreach(string id in equipment)items.Add(Item("gear_"+id,id));
            foreach(string id in packages)items.Add(Item("gear_"+id,id));int i=0;foreach(string id in cargo)items.Add(Item("cargo_"+(i++)+"_"+id,id));return items;
        }
        private static PackingPlan Plan(string label,IReadOnlyList<string> equipment,IReadOnlyList<string> cargo,IEnumerable<string> packages,bool required=true)
        {
            PackingPlan plan=VehiclePacking.Plan(Items(equipment,cargo,packages),geometry);
            log.AppendLine("PLAN "+label+" success="+plan.FullyValidated+" weightKg="+plan.knownWeightKg+" candidates="+plan.searchedCandidates+" code="+plan.failureCode+" reason="+plan.reason);
            foreach(var p in plan.placements)log.AppendLine("  PLACE "+p.instanceId+" in="+p.zoneId+" grid="+p.logicalCells+" bounds="+p.boundsInPackingFrame+" parent="+p.parentInstanceId+" support="+p.supportInstanceId);
            if(required)Need(plan.FullyValidated,label+" failed: "+plan.failureCode+" / "+plan.reason);
            for(int i=0;i<plan.placements.Count;i++)for(int j=0;j<i;j++)Need(!VehiclePacking.Overlap(plan.placements[i].boundsInPackingFrame,plan.placements[j].boundsInPackingFrame,.005f),"Intersecting actual bounds");
            return plan;
        }
        private static void CheckCareer(CareerDefinition career)
        {
            var run=new RunState(4,Profile(career));
            run.LoadValidator=(equipment,cargo)=>VehiclePacking.Plan(Items(equipment,cargo,run.Supplies.Keys),geometry).FullyValidated?"":"Measured vehicle packing rejected";
            Plan(career.id+" baseline",run.LoadedEquipment,run.Cargo,run.Supplies.Keys);
            while(run.HasUnloadedEquipment)
            {
                Need(run.Act("StovePickup",true),"Starter pickup failed");string id=run.HeldEquipmentId;
                if(!run.Act("HomeCart",true))
                {
                    var proposed=new List<string>(run.LoadedEquipment){id};Plan(career.id+" blocked loading "+id,proposed,run.Cargo,run.Supplies.Keys);
                    throw new InvalidOperationException("Initial load rejected "+id);
                }
            }
            Plan(career.id+" all initial",run.LoadedEquipment,run.Cargo,run.Supplies.Keys);
            Need(run.Act("HomeExit",true),"Departure failed");
            OpeningProcurementPlan recommendation=OpeningProcurementPlan.For(run);
            var owned=new HashSet<string>(run.LoadedEquipment);owned.UnionWith(run.Cargo);owned.UnionWith(run.Cart);owned.UnionWith(run.Supplies.Keys);
            var buy=new List<string>();foreach(string id in recommendation.supplies)if(!owned.Contains(id))buy.Add(id);
            foreach(string id in recommendation.shelves)if(!owned.Contains(id))buy.Add(id);
            int recipe=recommendation.menu=="料理盖饭"?1:recommendation.menu=="冰镇酸梅汤"?5:recommendation.menu=="鱼蛋热汤"?3:0;
            foreach(string id in buy)
            {
                bool ok=SupplyCatalog.Find(id)!=null?run.AddSupply(id):run.Act(GameCatalog.Find(id).anchor,true,true);
                Need(ok,"Cannot legally basket "+id+": "+run.Message);
            }
            int before=run.CashCents,total=run.CartTotalCents;Need(total<=before,"Cannot afford actual opening recipe");
            // RunState checks pending equipment and packages as cargo before committing.
            Plan(career.id+" pending procurement",run.LoadedEquipment,buy,run.Supplies.Keys);
            Need(run.Act("Cashier",true),"Cannot enter checkout");run.Tick(run.CheckoutDuration);
            Need(run.Paid&&run.CashCents==before-total,"Payment/preflight failed: "+run.Message);
            Need(run.Act("MarketCart",true),"Final loading failed: "+run.Message);
            Plan(career.id+" stocked",run.LoadedEquipment,run.Cargo,run.Supplies.Keys);
            Need(run.Act("MarketExit",true)&&run.Phase==GamePhase.SiteSelection,"No site selection");
            var mapping=new GameObject("RealBusinessMapping");
            try
            {
                var flow=mapping.AddComponent<GameFlow>();typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);
                var session=new BusinessSession(flow.BuildBusinessSetup());
                Need(session.Missing(recipe)=="","Chosen menu still missing "+session.Missing(recipe));
                Need(session.Open(),"Mapped stocked menu cannot open");log.AppendLine("  MENU "+BusinessSession.Recipes[recipe].id+" missing=none opened=true");
            }
            finally{UnityEngine.Object.DestroyImmediate(mapping);}
            log.AppendLine("  TRANSACTION cashBefore="+before+" paid="+total+" cashAfter="+run.CashCents+" buy="+string.Join(",",buy));
        }
        private static void CheckRejections()
        {
            Need(ContainerSpec.Bag().columns==2&&ContainerSpec.Bag().rows==3&&ContainerSpec.ShoppingCart().columns==3&&ContainerSpec.ShoppingCart().rows==3,"Source backpack/cart grids");
            Need(!ContainerSpec.Bag().Allows(PackingCatalog.Find("B04")),"Heavy core must not fit bag tag");
            Need(ContainerSpec.UnderBay().requiresUpgrade,"Under-bay falsely free");
            var tooHeavy=Item("heavy","B04");tooHeavy.measuredWeightKg=101;
            Need(VehiclePacking.Plan(new[]{tooHeavy},geometry).failureCode=="Overweight","Overweight accepted");
            var unknown=Item("unknown","E03");unknown.measuredWeightKg=-1;
            Need(VehiclePacking.Plan(new[]{unknown},geometry).failureCode=="UnspecifiedWeight","Unknown gross mass accepted");
            PackingPlan twin=VehiclePacking.Plan(new[]{Item("a","B04"),Item("b","B04")},geometry);
            Need(!twin.FullyValidated,"Two real B04s share a too-small deck");
            var go=new GameObject("PoseScaleFixture");go.transform.localScale=new Vector3(1.1f,.9f,1.2f);
            Vector3 scale=go.transform.localScale;var one=VehiclePacking.Plan(new[]{Item("single","B04")},geometry);Need(one.FullyValidated,"One actual B04 rejected");one.placements[0].Apply(go.transform);Need(go.transform.localScale==scale,"Apply modified model scale");UnityEngine.Object.DestroyImmediate(go);
        }
        private static void CheckHomeContact()
        {
            GameObject root=new GameObject("HomeModuleReachFixture");root.transform.SetParent(fixture.transform,false);
            try
            {
                var flow=root.AddComponent<GameFlow>();
                // Edit mode does not invoke MonoBehaviour.Awake automatically.
                typeof(GameFlow).GetProperty("Instance").SetValue(null,flow);
                foreach(var career in CharacterCatalog.Data.careers)
                {
                    flow.StartProfile(Profile(career));Need(flow.Run.Phase!=GamePhase.Error,"Home build error");
                    while(flow.Run.HasUnloadedEquipment){Need(flow.Run.Act("StovePickup",true),"Home pickup");Need(flow.Run.Act("HomeCart",true),"Home load "+flow.Run.Message);}
                    flow.World.Sync(flow.Run);Physics.SyncTransforms();
                    Transform player=flow.World.Player.transform;Vector3 old=player.position;var controller=flow.World.Player.Controller;controller.enabled=false;
                    Vector3 anchor=flow.World.Anchor("HomeCart");int legal=0;string atAnchor="";
                    for(int x=-6;x<=6;x++)for(int z=-6;z<=6;z++)
                    {
                        Vector3 offset=new Vector3(x*.15f,0,z*.15f);if(offset.sqrMagnitude>.95f*.95f)continue;
                        Vector3 point=anchor+offset;
                        if(!Physics.Raycast(point+Vector3.up*.35f,Vector3.down,out RaycastHit floor,1,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))continue;
                        point.y=floor.point.y+.03f;
                        Collider[] blockers=Physics.OverlapCapsule(point+Vector3.up*.29f,point+Vector3.up*1.40f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                        if(x==0&&z==0)atAnchor=blockers.Length==0?"clear":string.Join(",",Array.ConvertAll(blockers,b=>b.name));
                        if(blockers.Length!=0)continue;
                        player.position=point;Physics.SyncTransforms();if(flow.IsActionInRange("HomeCart"))legal++;
                    }
                    player.position=old;controller.enabled=true;Physics.SyncTransforms();
                    log.AppendLine("HOME_CONTACT "+career.id+" exactAnchor="+atAnchor+" legalStaticPoses="+legal);
                    Need(legal>0,career.id+" new modules block all tested HomeCart poses");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(root);typeof(GameFlow).GetProperty("Instance").SetValue(null,null);}
        }
    }
}
