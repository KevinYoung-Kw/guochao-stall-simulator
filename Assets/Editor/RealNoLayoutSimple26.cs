using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    public static class RealNoLayoutSimple26
    {
        private static void Need(bool value, string why) { if (!value) throw new Exception(why); }
        private static GameObject Make(string id, Transform parent)
        {
            if (id == "RiceBucket" || id.StartsWith("B") || id.StartsWith("V") || id.StartsWith("U") || id.StartsWith("E") || id.StartsWith("P") || id.StartsWith("S"))
                return EquipmentVisualFactory.Create(id, parent);
            return (GameObject)typeof(WorldRuntime).GetMethod("MakeFood", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { id, parent, .36f });
        }
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs(); int arg=Array.IndexOf(args,"--no-layout-output"); string output=Path.GetFullPath(args[arg+1]);
            GameObject fixture=null; bool found=false; string result="";
            try
            {
                Need(!File.Exists(output),"Report already exists");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                fixture=new GameObject("RealNoLayoutSimple26"); var vehicle=new GameObject("ActualCartAI"); vehicle.transform.SetParent(fixture.transform,false);
                var prefab=Resources.Load<GameObject>("Models/CartAI"); Need(prefab,"Missing actual CartAI"); UnityEngine.Object.Instantiate(prefab,vehicle.transform,false);
                int level=1;
                var loadout=fixture.AddComponent<VehicleLoadout>(); loadout.Initialize(vehicle.transform,Make,()=>level);
                CareerDefinition career=null; foreach(var c in CharacterCatalog.Data.careers) if(c.id=="career2") career=c;
                Need(career!=null,"career2 missing");
                var profile=(RunProfile)typeof(V10PackingValidation).GetMethod("Profile",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{career});
                var run=new RunState(4,profile); run.LoadValidator=(equipment,cargo)=>loadout.Validate(equipment,cargo,run.PackingSupplies);
                while(run.HasUnloadedEquipment) Need(run.Act("StovePickup",true)&&run.Act("HomeCart",true),"Home load: "+run.Message);
                Need(run.Act("HomeExit",true),"Home exit: "+run.Message);
                Need(SupplyCatalog.IsCounterItem("RiceBucket"),"RiceBucket not actually sold");
                Need(run.AddSupply("RiceBucket"),"Actual AddSupply rejected: "+run.Message);
                int cashBefore=run.CashCents; Need(run.CartTotalCents<=cashBefore,"Insufficient funds");
                bool preview=run.PreviewCheckoutSelection(run.Cart.ToArray(),out int price,out string reason); var plan=loadout.Plan;
                Need(plan!=null,"No actual packing plan");
                found=!preview && plan.failureCode=="NoSupportedLayout" && !plan.searchExhausted && plan.knownWeightKg<=100f;
                result="career=career2\nstartEquipment="+string.Join(",",run.LoadedEquipment)+"\nstartPackages="+string.Join(",",run.PackingSupplies)+"\nactualBasket="+string.Join(",",run.Cart)+"\nsold=true\npriceCents="+price+"\ncashBefore="+cashBefore+"\ncheckoutPreview="+preview+"\nreason="+reason+"\nfailureCode="+plan.failureCode+"\nsearchExhausted="+plan.searchExhausted+"\nsearchedCandidates="+plan.searchedCandidates+"\nknownWeightKg="+plan.knownWeightKg+"\ncandidateFound="+found+"\npreviewScope=real RunState basket and real VehicleLoadout preflight\n";
                bool lv1Found=found; found=false;
                level=2;
                bool lv2Preview=run.PreviewCheckoutSelection(run.Cart.ToArray(),out int lv2Price,out string lv2Reason); var lv2Plan=loadout.Plan;
                Need(lv2Plan!=null,"No actual Lv2 packing plan");
                found=lv1Found && lv2Preview && lv2Plan.FullyValidated;
                result+="lv2Preview="+lv2Preview+"\nlv2FullyValidated="+lv2Plan.FullyValidated+"\nlv2FailureCode="+lv2Plan.failureCode+"\nlv2SearchedCandidates="+lv2Plan.searchedCandidates+"\nlv2KnownWeightKg="+lv2Plan.knownWeightKg+"\nfinalFound="+found+"\n";
                if(lv2Plan.placements!=null) foreach(var p in lv2Plan.placements) result+="lv2Placement=itemId="+p.itemId+"|zoneId="+p.zoneId+"|boundsInPackingFrame="+p.boundsInPackingFrame+"\n";
                Need(found,"The same basket did not become loadable on Lv2: "+lv2Reason);
                Need(lv2Price==price,"Price changed when the vehicle was upgraded");
                var geometry=VehiclePackingGeometry.FromCartAI(vehicle.transform,2);
                bool usedExtension=false;
                for(int i=0;i<lv2Plan.placements.Count;i++)
                {
                    var placed=lv2Plan.placements[i];
                    if(placed.zoneId=="main_deck" && placed.boundsInPackingFrame.min.x<-.42f) usedExtension=true;
                    for(int j=0;j<i;j++) Need(!VehiclePacking.Overlap(placed.boundsInPackingFrame,lv2Plan.placements[j].boundsInPackingFrame,.005f),"Cargo bounds overlap: "+placed.itemId+" / "+lv2Plan.placements[j].itemId);
                    foreach(var obstacle in geometry.obstacles) Need(!VehiclePacking.Overlap(placed.boundsInPackingFrame,obstacle,.005f),"Cargo intersects a vehicle attachment: "+placed.itemId);
                }
                Need(usedExtension,"The Lv2 plan did not use the added deck");
                Transform tray=null; foreach(Transform t in vehicle.GetComponentsInChildren<Transform>(true)) if(t.name=="Lv2 车斗扩展托板") {tray=t;break;}
                Need(tray!=null && tray.gameObject.activeInHierarchy,"Lv2 support tray is not active");
                Need(tray.GetComponent<Renderer>()!=null && tray.GetComponent<Renderer>().enabled && tray.GetComponent<BoxCollider>()!=null && tray.GetComponent<BoxCollider>().enabled,"Lv2 support tray is not visible and collidable");
                int paymentEvents=0; run.CheckoutCompleted+=_=>paymentEvents++;
                Need(run.ConfirmCheckoutSelection(run.Cart.ToArray()),"Cannot confirm Lv2 checkout: "+run.Message);
                run.Tick(run.CheckoutDuration+.01f,true);
                Need(run.Paid && run.CashCents==cashBefore-lv2Price && run.SpentCents==lv2Price && paymentEvents==1,"Lv2 payment incorrect: "+run.Message);
                Need(run.Act("MarketCart",true),"Paid goods could not be loaded: "+run.Message);
                Need(run.Cargo.Contains("RiceBucket") && run.Cart.Count==0,"Paid RiceBucket was not loaded");
                Need(run.CashCents==cashBefore-lv2Price && run.SpentCents==lv2Price && paymentEvents==1,"Loading charged the player again");
                level=1; string downgradeError=loadout.Validate(run.LoadedEquipment,run.Cargo,run.PackingSupplies);
                Need(!string.IsNullOrEmpty(downgradeError) && loadout.Plan!=null && loadout.Plan.failureCode=="NoSupportedLayout" && !tray.gameObject.activeSelf,"Lv1 cache or tray was not restored");
                level=2; string restoredError=loadout.Validate(run.LoadedEquipment,run.Cargo,run.PackingSupplies);
                Need(string.IsNullOrEmpty(restoredError) && loadout.Plan.FullyValidated && tray.gameObject.activeSelf,"Lv2 cache or tray was not restored");
                result+="\npaid=true\nloadedRiceBucket=true\npriceCents="+lv2Price+"\ncashAfter="+run.CashCents+"\npaymentEvents="+paymentEvents+"\nusedExtension="+usedExtension+"\nfullBoundsAndObstaclesClear=true\nlevelChangeCacheAndTray=true\nscope=scripted real RunState checkout and vehicle load; no manual movement or transition claim\n";
            }
            catch(Exception ex) { found=false; result="PROBE_ERROR "+ex+"\n"; }
            finally { if(fixture) UnityEngine.Object.DestroyImmediate(fixture); }
            Directory.CreateDirectory(Path.GetDirectoryName(output)); if(!File.Exists(output)) File.WriteAllText(output,result);
            Debug.Log(result); if(Application.isBatchMode) EditorApplication.Exit(found?0:1);
        }
    }
}
