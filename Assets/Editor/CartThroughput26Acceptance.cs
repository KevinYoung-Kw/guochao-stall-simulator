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
    // Actual career1 + imported CartAI + VehicleLoadout/Factory; transaction
    // commands are scripted. No fabricated money, disabled packing or save IO.
    public static class CartThroughput26Acceptance
    {
        [Serializable] public class Scenario { public string name;public bool paid,loaded;public int initialCash,quantity,totalCents,afterCash,cargoCount,previewCandidates;public long previewMs,confirmMs,paymentMs,loadMs;public string outcome="",items=""; }
        [Serializable] public class Report { public bool success;public string scope="Fresh career1, actual starter equipment, imported CartAI + VehicleLoadout; scripted shopping and checkout, not keyboard traversal. Times are native Unity Editor wall-clock, not WebGL.";public int maxRicePacks,maxCandidates,rejectedCandidates;public long maxPreviewMs,rejectedPreviewMs;public bool rejectedSearchExhausted;public string firstRejected="",rejectedCode="";public Scenario rice,starterMixed;public string[] errors; }
        static GameObject root;static VehicleLoadout packing;static readonly List<string> errors=new List<string>();
        static void Need(bool x,string m){if(!x)throw new Exception(m);}
        static void Log(string m,string s,LogType t){if(t==LogType.Exception||t==LogType.Error||t==LogType.Assert)errors.Add(m);}
        static GameObject Make(string id,Transform p){if(SupplyCatalog.IsEquipment(id)||id.StartsWith("P")||id.StartsWith("S"))return EquipmentVisualFactory.Create(id,p);return (GameObject)typeof(WorldRuntime).GetMethod("MakeFood",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{id,p,.36f});}
        static RunState Fresh(){var p=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};Need(p.Validate(out string why),why);var r=new RunState(4,p);r.Paused=false;r.LoadValidator=(g,c)=>packing.Validate(g,c,r.PackingSupplies);for(int i=0;r.HasUnloadedEquipment&&i<30;i++){Need(r.Act("StovePickup",true),r.Message);Need(r.Act("HomeCart",true),r.Message);}Need(r.Act("HomeExit",true),r.Message);return r;}
        static Scenario Pay(RunState r,string name,string[] ids)
        {var s=new Scenario{name=name,quantity=ids.Length,initialCash=r.CashCents,items=string.Join(",",ids)};var watch=System.Diagnostics.Stopwatch.StartNew();bool valid=r.PreviewCheckoutSelection(ids,out int due,out string why);watch.Stop();s.previewMs=watch.ElapsedMilliseconds;s.previewCandidates=packing.Plan?.searchedCandidates??-1;if(!valid){s.outcome=why;return s;}s.totalCents=due;watch.Restart();bool confirmed=r.ConfirmCheckoutSelection(ids);watch.Stop();s.confirmMs=watch.ElapsedMilliseconds;Need(confirmed,r.Message);watch.Restart();r.Tick(r.CheckoutDuration+.01f,true);watch.Stop();s.paymentMs=watch.ElapsedMilliseconds;s.paid=r.Paid;s.afterCash=r.CashCents;Need(s.paid&&s.afterCash==s.initialCash-due,"wrong payment: "+r.Message);watch.Restart();s.loaded=r.Act("MarketCart",true);watch.Stop();s.loadMs=watch.ElapsedMilliseconds;s.cargoCount=r.Cargo.Count;s.outcome=r.Message;if(s.loaded)Need(s.cargoCount==ids.Length&&s.afterCash==r.CashCents,"loading loses goods or money");return s;}
        public static void Run()
        {var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--throughput-output");if(at<0||at+1>=args.Length)throw new Exception("missing output");string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new Exception("preserve evidence");errors.Clear();Application.logMessageReceived+=Log;var rep=new Report();
         try{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);root=new GameObject("Actual carrier fixture");var cart=new GameObject("CartAI");cart.transform.SetParent(root.transform,false);var model=Resources.Load<GameObject>("Models/CartAI");Need(model,"missing cart");UnityEngine.Object.Instantiate(model,cart.transform,false);packing=cart.AddComponent<VehicleLoadout>();packing.Initialize(cart.transform,Make);
          var rice=Fresh();for(int i=0;i<18;i++)Need(rice.AddSupply("rice_small"),"basket "+i+" "+rice.Message);for(int n=1;n<=18;n++){string[] want=Enumerable.Repeat("rice_small",n).ToArray();var watch=System.Diagnostics.Stopwatch.StartNew();bool ok=rice.PreviewCheckoutSelection(want,out _,out string reason);watch.Stop();if(ok){rep.maxRicePacks=n;rep.maxCandidates=packing.Plan?.searchedCandidates??-1;rep.maxPreviewMs=watch.ElapsedMilliseconds;}else{rep.firstRejected=reason;rep.rejectedCandidates=packing.Plan?.searchedCandidates??-1;rep.rejectedPreviewMs=watch.ElapsedMilliseconds;rep.rejectedCode=packing.Plan?.failureCode??"";rep.rejectedSearchExhausted=packing.Plan?.searchExhausted??false;break;}}Need(rep.maxRicePacks>0,"cannot buy any rice");rep.rice=Pay(rice,"rice small packs",Enumerable.Repeat("rice_small",rep.maxRicePacks).ToArray());
          var mixed=Fresh();string[] goods={"B01","V01","E01","P02","rice_small","egg_small","seasoning_small"};foreach(string id in goods)Need(mixed.AddSupply(id),"mixed basket "+id+" "+mixed.Message);rep.starterMixed=Pay(mixed,"starter stove + pan + energy + packaging + 3 small goods",goods);rep.success=rep.rice.paid&&rep.rice.loaded&&rep.starterMixed.paid&&rep.starterMixed.loaded;
         }catch(Exception e){errors.Add(e.ToString());}finally{if(root)UnityEngine.Object.DestroyImmediate(root);Application.logMessageReceived-=Log;}
         rep.errors=errors.ToArray();rep.success=rep.success&&errors.Count==0;Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(rep,true));Debug.Log("CART_THROUGHPUT26 "+(rep.success?"PASS":"FAIL"));if(Application.isBatchMode)EditorApplication.Exit(rep.success?0:1);}
    }
}
