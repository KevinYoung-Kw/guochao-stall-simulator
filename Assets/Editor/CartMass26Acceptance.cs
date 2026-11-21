using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Isolated production VehicleLoadout + actual factory models + RunState.
    // Exercises transaction commands directly, not physical UI/input/traversal.
    public static class CartMass26Acceptance
    {
        [Serializable] public class Case { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool success; public string scope="Production CartAI/VehicleLoadout/factory bounds and RunState commands, fresh career1 per purchase; no hardware input or UI."; public Case[] cases; public string[] errors; }
        static readonly List<Case> cases=new List<Case>(); static readonly List<string> errors=new List<string>();
        static GameObject root; static VehicleLoadout packing;
        static void Need(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
        static void CaseRun(string name,Action action) { try{action();cases.Add(new Case{name=name,passed=true,detail="passed"});}catch(Exception e){cases.Add(new Case{name=name,passed=false,detail=e.ToString()});} }
        static void Capture(string condition,string stack,LogType type) { if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(condition); }
        static GameObject Make(string id,Transform parent)
        { if(SupplyCatalog.IsEquipment(id)||id.StartsWith("P")||id.StartsWith("S"))return EquipmentVisualFactory.Create(id,parent);return (GameObject)typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{id,parent,.36f}); }
        static RunState Fresh()
        {
            var p=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};Need(p.Validate(out string why),why);
            var run=new RunState(4,p);run.Paused=false;run.LoadValidator=(gear,cargo)=>packing.Validate(gear,cargo,run.PackingSupplies);
            for(int guard=0;run.HasUnloadedEquipment&&guard<30;guard++){Need(run.Act("StovePickup",true),"home pickup "+run.Message);Need(run.Act("HomeCart",true),"home loading "+run.Message);}
            Need(!run.HasUnloadedEquipment,"starter still home");Need(run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"depart "+run.Message);return run;
        }
        static void Purchase(string id)
        {
            var run=Fresh();int initial=run.CashCents;int price=run.PurchaseItemPriceCents(id);
            Need(price>0&&initial>=price,"invalid initial price/cash");Need(run.AddSupply(id),"basket "+run.Message);
            Need(run.ConfirmCheckoutSelection(new[]{id}),"checkout preflight "+run.Message+" plan="+packing.Plan?.failureCode);
            run.Tick(run.CheckoutDuration+.01f,true);Need(run.Paid&&run.CashCents==initial-price,"payment "+run.Message);
            Need(run.Act("MarketCart",true),"vehicle load "+run.Message);Need(run.Cart.Count==0&&run.Cargo.Contains(id),"load lost item");Need(run.CashCents==initial-price&&run.SpentCents==price,"wrong amount/double charge");
            string key=ProcurementInstanceKeys.LoadoutKey(run.Cargo,run.Cargo.IndexOf(id));
            var placement=packing.Plan?.Find(key);Need(placement!=null,"paid item has no real packed placement: "+key);
            Need(placement.boundsInPackingFrame.size.x>0&&placement.boundsInPackingFrame.size.y>0&&placement.boundsInPackingFrame.size.z>0,"packed bounds absent");
        }
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--cart-mass-output");if(at<0||at+1>=args.Length)throw new Exception("Missing --cart-mass-output");string output=Path.GetFullPath(args[at+1]);if(File.Exists(output))throw new Exception("Preserve old evidence");
            cases.Clear();errors.Clear();Application.logMessageReceived+=Capture;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);root=new GameObject("CartMass26Fixture");
                var cart=new GameObject("MeasuredCartAI");cart.transform.SetParent(root.transform,false);var asset=Resources.Load<GameObject>("Models/CartAI");Need(asset!=null,"CartAI missing");UnityEngine.Object.Instantiate(asset,cart.transform,false);
                packing=cart.AddComponent<VehicleLoadout>();packing.Initialize(cart.transform,Make);
                CaseRun("all current shelf + counter SKU have positive configured transport mass",()=>{
                    foreach(var x in GameCatalog.Items.Concat(SupplyCatalog.Items)){float weight=RuntimeItemBalance.WeightKg(x.id);Need(weight>0,x.id+" missing mass: "+weight);}
                });
                CaseRun("all loose packs are actually smaller than their visible full-size factory models",()=>{
                    var parent=new GameObject("Model size samples");parent.transform.SetParent(root.transform,false);parent.SetActive(false);
                    foreach(var x in SupplyCatalog.Items){if(!x.id.EndsWith("_small",StringComparison.Ordinal))continue;
                        string full=SupplyCatalog.IngredientId(x.id);var large=Make(full,parent.transform);var small=Make(x.id,parent.transform);
                        var a=PackingItem.Measure("full",full,large.transform).bounds.size;var b=PackingItem.Measure("small",x.id,small.transform).bounds.size;
                        Need(a.x>0&&a.y>0&&a.z>0&&Vector3.Distance(b,a*.65f)<.002f,x.id+" actual bounds "+b+" expected "+a*.65f);
                        UnityEngine.Object.DestroyImmediate(large);UnityEngine.Object.DestroyImmediate(small);
                    }
                    UnityEngine.Object.DestroyImmediate(parent);
                });
                foreach(string id in new[]{"skewer_small","prepared_small","fruit_small","E13"}){string actual=id;CaseRun("actual real-fit transaction "+actual,()=>Purchase(actual));}
                CaseRun("v25 snapshot with loose packs restores and checks out with real vehicle",()=>{
                    int legacyAt=Array.IndexOf(args,"--legacy-snapshot");Need(legacyAt>=0&&legacyAt+1<args.Length,"legacy fixture missing");
                    var snapshot=JsonUtility.FromJson<RunSaveData>(File.ReadAllText(args[legacyAt+1]));var run=RunState.Restore(snapshot);run.Paused=false;
                    Need(run.Phase==GamePhase.Market&&run.Cart.SequenceEqual(new[]{"skewer_small","prepared_small","fruit_small"}),"v25 basket not preserved");
                    int cash=run.CashCents,price=run.PurchaseItemPriceCents("prepared_small");run.LoadValidator=(gear,cargo)=>packing.Validate(gear,cargo,run.PackingSupplies);
                    Need(run.ConfirmCheckoutSelection(new[]{"prepared_small"}),"legacy checkout "+run.Message);
                    run.Tick(run.CheckoutDuration+.01f,true);Need(run.Paid&&run.CashCents==cash-price&&run.Cart.SequenceEqual(new[]{"prepared_small"}),"legacy selection charged or retained incorrectly");
                    Need(run.Act("MarketCart",true)&&run.Cargo.Contains("prepared_small")&&run.CashCents==cash-price,"legacy load "+run.Message);
                });
                CaseRun("weights preserve real rejection",()=>{
                    var sample=Make("E03",root.transform);var item=PackingItem.Measure("unknown","E03",sample.transform);
                    item.measuredWeightKg=-1; // Use real render bounds, but deliberately unknown gross mass.
                    var g=VehiclePackingGeometry.FromCartAI(cart.transform);var plan=VehiclePacking.Plan(new[]{item},g);Need(plan.failureCode=="UnspecifiedWeight","missing mass should remain rejected, got "+plan.failureCode);
                    UnityEngine.Object.DestroyImmediate(sample);
                });
            }
            catch(Exception e){cases.Add(new Case{name="fixture",passed=false,detail=e.ToString()});}
            finally{if(root)UnityEngine.Object.DestroyImmediate(root);Application.logMessageReceived-=Capture;}
            var report=new Report{success=cases.All(x=>x.passed)&&errors.Count==0,cases=cases.ToArray(),errors=errors.ToArray()};Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));Debug.Log("CART_MASS26 "+(report.success?"PASS":"FAIL")+" "+output);if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
    }
}
