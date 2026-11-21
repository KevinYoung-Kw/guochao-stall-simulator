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
    public static class BucketContact26Acceptance
    {
        private static StringBuilder log=new StringBuilder();
        private static GameObject fixture;
        private static Transform prototypes;
        private static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        private static bool Close(float a,float b,float tol=0.0001f)=>Mathf.Abs(a-b)<=tol;
        private static void Near(Vector3 a,Vector3 b,string label){Need(Close(a.x,b.x)&&Close(a.y,b.y)&&Close(a.z,b.z),label+" got="+a.ToString("F5")+" want="+b.ToString("F5"));}
        private static GameObject Model(string id,Transform parent)
        {
            if(id.StartsWith("B")||id.StartsWith("V")||id.StartsWith("U")||id.StartsWith("E")||id.StartsWith("P")||id.StartsWith("S")||id=="RiceBucket")return EquipmentVisualFactory.Create(id,parent);
            return (GameObject)typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{id,parent,.36f});
        }
        private static void CheckScaleAndFallback()
        {
            var model=Model("RiceBucket",prototypes);var one=PackingItem.Measure("bucket","RiceBucket",model.transform,7f);
            Need(one.hasBottomSupport,"Actual bucket missing measured support");
            Need(one.bottomSupportBounds.size.x>.4f&&one.bottomSupportBounds.size.z>.4f,"Actual support dimensions suspicious");
            log.AppendLine("ACTUAL bbox="+one.bounds+" bottom="+one.bottomSupportBounds);
            model.transform.localScale*=1.25f;model.transform.localRotation=Quaternion.Euler(0,90,0);model.transform.localPosition=new Vector3(2,.5f,-3);
            var scaled=PackingItem.Measure("bucket_scaled","RiceBucket",model.transform,7f);
            Need(scaled.hasBottomSupport,"Scaled rotated bucket missing support");
            Near(scaled.bounds.size,one.bounds.size*1.25f,"scaled bbox size");
            Near(scaled.bounds.center,one.bounds.center*1.25f,"scaled bbox center");
            Near(scaled.bottomSupportBounds.size,one.bottomSupportBounds.size*1.25f,"scaled footprint size");
            Near(scaled.bottomSupportBounds.center,one.bottomSupportBounds.center*1.25f,"scaled footprint center");
            log.AppendLine("SCALED_ROTATED factor=1.25 angle=90 bbox="+scaled.bounds+" bottom="+scaled.bottomSupportBounds+" pass=true");
            var unreadable=new GameObject("UnreadableBucket");unreadable.transform.SetParent(prototypes,false);
            var filter=unreadable.AddComponent<MeshFilter>();unreadable.AddComponent<MeshRenderer>();
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-.1f,0,-.1f),new Vector3(.1f,0,-.1f),new Vector3(.1f,0,.1f),new Vector3(-.1f,0,.1f),new Vector3(0,.2f,0)};mesh.triangles=new[]{0,1,4,1,2,4,2,3,4,3,0,4,0,3,2,0,2,1};mesh.RecalculateBounds();filter.sharedMesh=mesh;mesh.UploadMeshData(true);
            Need(!mesh.isReadable,"Test mesh should be unreadable");
            var fallback=PackingItem.Measure("unreadable","RiceBucket",unreadable.transform,7f);
            Need(!fallback.hasBottomSupport,"Unreadable mesh must use existing full-bounds fallback");
            log.AppendLine("UNREADABLE_MESH fallback=whole_bounds no_exception=true bbox="+fallback.bounds);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        private static void CheckCareer3Runtime()
        {
            var go=new GameObject("RuntimeLoadout");go.transform.SetParent(fixture.transform,false);
            var cart=new GameObject("ActualCartAI");cart.transform.SetParent(go.transform,false);
            var prefab=Resources.Load<GameObject>("Models/CartAI");Need(prefab,"missing actual vehicle");UnityEngine.Object.Instantiate(prefab,cart.transform,false);
            var loadout=go.AddComponent<VehicleLoadout>();loadout.Initialize(cart.transform,Model);
            CareerDefinition career=null;foreach(var entry in CharacterCatalog.Data.careers)if(entry.id=="career3")career=entry;
            Need(career!=null,"career3 missing");
            var profileMethod=typeof(V10PackingValidation).GetMethod("Profile",BindingFlags.Static|BindingFlags.NonPublic);
            var run=new RunState(4,(RunProfile)profileMethod.Invoke(null,new object[]{career}));
            run.LoadValidator=(equipment,cargo)=>loadout.Validate(equipment,cargo,run.PackingSupplies);
            while(run.HasUnloadedEquipment)Need(run.Act("StovePickup",true)&&run.Act("HomeCart",true),"load home gear failed "+run.Message);
            Need(run.Act("HomeExit",true),"leave home failed");
            var rec=OpeningProcurementPlan.For(run);var owned=new HashSet<string>(run.LoadedEquipment);owned.UnionWith(run.Cargo);owned.UnionWith(run.Cart);owned.UnionWith(run.Supplies.Keys);
            var buy=new List<string>();foreach(string id in rec.supplies)if(!owned.Contains(id))buy.Add(id);foreach(string id in rec.shelves)if(!owned.Contains(id))buy.Add(id);
            foreach(string id in buy)Need(SupplyCatalog.Find(id)!=null?run.AddSupply(id):run.Act(GameCatalog.Find(id).anchor,true,true),"basket failed "+id+" "+run.Message);
            int before=run.CashCents,total=run.CartTotalCents;Need(before>=total,"starter cannot afford checkout");
            Need(run.Act("Cashier",true),"cashier failed "+run.Message);run.Tick(run.CheckoutDuration);
            Need(run.Paid&&run.CashCents==before-total,"payment failed "+run.Message);
            Need(run.Act("MarketCart",true),"loading failed "+run.Message);
            Need(loadout.Validate(run.LoadedEquipment,run.Cargo,run.PackingSupplies)=="","real cached loadout rejected");
            PackingPlan plan=loadout.Plan;Need(plan!=null&&plan.FullyValidated,"runtime plan not validated");
            var bucket=plan.placements.Find(p=>p.itemId=="RiceBucket");Need(bucket!=null&&bucket.zoneId=="cabinet_transport_top"&&!string.IsNullOrEmpty(bucket.supportInstanceId),"bucket not supported by cabinet");
            for(int i=0;i<plan.placements.Count;i++)for(int j=0;j<i;j++)Need(!VehiclePacking.Overlap(plan.placements[i].boundsInPackingFrame,plan.placements[j].boundsInPackingFrame,.005f),"placement overlap");
            loadout.PresentTransport(run.LoadedEquipment,run.Cargo,run.PackingSupplies);
            Need(run.Act("MarketExit",true)&&run.Phase==GamePhase.SiteSelection,"could not reach site selection");
            log.AppendLine("CAREER3 realVehicleLoadout+realCheckout paid="+total+" before="+before+" after="+run.CashCents+" buy="+string.Join(",",buy)+" support="+bucket.supportInstanceId+" turn="+bucket.quarterTurns+" candidates="+plan.searchedCandidates+" collision=false phase="+run.Phase);
            log.AppendLine("NOT_COVERED: rendered visual inspection, traversal, physical tipping, COM, full WebGL build, next-day saves.");
        }
        public static void Run()
        {
            string[] argv=Environment.GetCommandLineArgs();int at=Array.IndexOf(argv,"--bucket-accept-output");string output=Path.GetFullPath(argv[at+1]);bool passed=false;
            try{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);fixture=new GameObject("ContactAcceptance");var hidden=new GameObject("Prototypes");hidden.transform.SetParent(fixture.transform,false);hidden.SetActive(false);prototypes=hidden.transform;CheckScaleAndFallback();CheckCareer3Runtime();log.AppendLine("BUCKET_ACCEPT_PASS");passed=true;}
            catch(Exception ex){log.AppendLine("BUCKET_ACCEPT_FAIL "+ex);}
            finally{if(fixture)UnityEngine.Object.DestroyImmediate(fixture);}
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,log.ToString());UnityEngine.Debug.Log(log.ToString());EditorApplication.Exit(passed?0:1);
        }
    }
}
