using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace Guochao.Editor
{
    // Actual Unity UI components and game rules. This is a scripted local fixture,
    // not a hardware-input or 3D walking playthrough. Real packing separately tested.
    public static class CartFlow26Acceptance
    {
        [Serializable] public class Check { public string name, detail; public bool passed; }
        [Serializable] public class Report { public bool success; public string scope="RunState public purchase commands + actual ShoppingReceiptView components, with explicit injected B04/B06 cross-page layout fixture; not pointer traversal or real packing.";public Check[] checks;public string[] errors; }
        static readonly List<Check> checks=new List<Check>();static readonly List<string> errors=new List<string>();
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<GameObject> roots=new List<GameObject>();
        static void Need(bool x,string s){if(!x)throw new InvalidOperationException(s);}
        static void Case(string n,Action f){try{f();checks.Add(new Check{name=n,detail="passed",passed=true});}catch(Exception e){checks.Add(new Check{name=n,detail=e.ToString(),passed=false});}}
        static void Capture(string m,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);}
        static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Hidden).GetValue(o);
        static RunState Market(string trait=null)
        {
            var p=new RunProfile{careerId="career1",selectedTraits=trait==null?Array.Empty<string>():new[]{trait}};Need(p.Validate(out string why),why);
            var r=new RunState(4,p);r.Paused=false;
            for(int i=0;r.HasUnloadedEquipment&&i<30;i++){Need(r.Act("StovePickup",true),r.Message);Need(r.Act("HomeCart",true),r.Message);}
            Need(r.Act("HomeExit",true),r.Message);Need(r.Phase==GamePhase.Market,"not in market");return r;
        }
        static void Fill(RunState r,int count){for(int i=0;i<count;i++)Need(r.AddSupply("rice_small"),"add "+i+" "+r.Message);}
        static Snapshot Snapshot(RunState r)=>new Snapshot{phase=r.Phase,cash=r.CashCents/100f,cartCount=r.Cart.Count,cartCellsUsed=r.CartCellsUsed,cartCapacity=r.CartCapacity,paid=r.Paid,checkoutInProgress=r.CheckoutInProgress};
        static ShoppingReceiptView Ui(RunState r,out GameFlow flow,bool crossFixture=false)
        {
            var holder=new GameObject("isolated flow");holder.SetActive(false);roots.Add(holder);flow=holder.AddComponent<GameFlow>();typeof(GameFlow).GetField("run",Hidden).SetValue(flow,r);
            var canvas=new GameObject("isolated UI",typeof(RectTransform),typeof(Canvas));roots.Add(canvas);
            var go=new GameObject("actual shopping receipt");go.transform.SetParent(canvas.transform,false);var view=go.AddComponent<ShoppingReceiptView>();
            var font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");Need(font,"font missing");view.Initialize((RectTransform)canvas.transform,font,()=>{});
            if(crossFixture){
                // B06 is test-only here and lacks a painted icon. Seed a sprite
                // to avoid the edit-mode-only runtime photography fallback; the
                // item that actually crosses the page is B04, with its real icon.
                var tex=Resources.Load<Texture2D>("UI/Icons/B04");Need(tex,"fixture icon missing");
                var library=Get<GameItemIconLibrary>(view,"iconLibrary");Get<Dictionary<string,Sprite>>(library,"cache")["B06"]=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100);
            }
            view.Present(flow,Snapshot(r));return view;
        }
        static void Grid(RunState r,int total)
        {
            Fill(r,total);var ui=Ui(r,out var flow);Get<Button>(ui,"gridTab").onClick.Invoke();var cells=Get<GameObject[]>(ui,"cells");var icons=Get<Image[]>(ui,"cellIcons");var labels=Get<Text[]>(ui,"cellLabels");var prev=Get<Button>(ui,"previous");var next=Get<Button>(ui,"next");var pager=Get<Text>(ui,"pager");
            Need(cells.Count(x=>x.activeSelf)==12,"page1 not 12 cells");Need(icons.All(x=>x.enabled),"page1 did not show 12 actual units");Need(!prev.interactable&&next.interactable,"page1 controls");
            next.onClick.Invoke();Need(cells.Count(x=>x.activeSelf)==total-12,"page2 cell count");Need(icons.Take(total-12).All(x=>x.enabled),"page2 missing actual unit icons");Need(prev.interactable&&!next.interactable&&pager.text.Contains("2 / 2"),"page2 controls/label");
            Get<Button>(ui,"basketTab").onClick.Invoke();Get<Button>(ui,"gridTab").onClick.Invoke();Need(Get<int>(ui,"page")==0,"mode did not reset page");
            Need(r.Cart.Count==total&&r.CashCents==15000,"UI changed inventory/cash");
        }
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--cart-flow-output");if(at<0||at+1>=args.Length)throw new Exception("missing output");string output=Path.GetFullPath(args[at+1]);if(File.Exists(output))throw new Exception("preserve old evidence");
            checks.Clear();errors.Clear();Application.logMessageReceived+=Capture;
            try
            {
                Case("default/organized/disorganized capacities",()=>{Need(Market().CartCapacity==18,"default");Need(Market("收纳达人").CartCapacity==24,"organized");Need(Market("丢三落四").CartCapacity==12,"disorganized");});
                Case("old v25 basket snapshot restores with expanded space",()=>{int i=Array.IndexOf(args,"--legacy-snapshot");Need(i>=0&&i+1<args.Length,"legacy file missing");var old=JsonUtility.FromJson<RunSaveData>(File.ReadAllText(args[i+1]));var run=RunState.Restore(old);Need(run.CartCapacity==18&&run.Cart.SequenceEqual(new[]{"skewer_small","prepared_small","fruit_small"})&&run.CashCents==15000,"old basket changed");});
                Case("expanded full cart, unpaid return, partial checkout, save",()=>{var run=Market();Fill(run,18);Need(!run.AddSupply("rice_small")&&run.Cart.Count==18,"19th accepted");Need(run.ReturnItem(17,"SupplyCounter",true)&&run.Cart.Count==17&&run.CashCents==15000,"return");Need(run.AddSupply("prepared_small"),"refill");var restored=RunState.Restore(JsonUtility.FromJson<RunSaveData>(JsonUtility.ToJson(run.CaptureSave())));restored.Paused=false;Need(restored.CartCapacity==18&&restored.Cart.Count==18,"restore");int price=restored.PurchaseItemPriceCents("prepared_small");Need(restored.ConfirmCheckoutSelection(new[]{"prepared_small"}),restored.Message);restored.Tick(restored.CheckoutDuration+.01f,true);Need(restored.Paid&&restored.Cart.SequenceEqual(new[]{"prepared_small"})&&restored.CashCents==15000-price,"partial checkout");});
                Case("actual 18-cell receipt page controls",()=>Grid(Market(),18));
                Case("actual 24-cell receipt page controls",()=>Grid(Market("收纳达人"),24));
                Case("multi-cell item crossing pages remains visible",()=>{
                    // Explicit isolated catalog fixture: B04/B06 are not both
                    // sold in the current counter catalog. Restore immediately.
                    var a=SupplyCatalog.Items[0];var b=SupplyCatalog.Items[1];
                    try{
                        SupplyCatalog.Items[0]=new ItemDefinition("B04","fixture B04",1,100,"SupplyCounter",Color.white);
                        SupplyCatalog.Items[1]=new ItemDefinition("B06","fixture B06",1,100,"SupplyCounter",Color.white);
                        var run=Market("收纳达人");run.Cart.Clear();run.Cart.Add("B04");run.Cart.Add("B06");var plan=ShoppingBasketLayout.Plan(run.Cart,24);Need(plan.Success,"layout");int[] owner=plan.CellOwners.ToArray();int overlap=-1;for(int i=0;i<owner.Length;i++)if(owner[i]>=0&&i<12&&owner.Skip(12).Contains(owner[i])){overlap=owner[i];break;}Need(overlap>=0,"fixture did not span page");
                        Need(run.Cart[overlap]=="B04","cross-page item changed");var ui=Ui(run,out var flow,true);Get<Button>(ui,"gridTab").onClick.Invoke();Get<Button>(ui,"next").onClick.Invoke();var images=Get<Image[]>(ui,"cellIcons");int first=Array.FindIndex(owner,12,x=>x==overlap)-12;Need(first>=0&&images[first].enabled&&images[first].sprite!=null,"cross-page first icon missing");
                    }finally{SupplyCatalog.Items[0]=a;SupplyCatalog.Items[1]=b;}
                });
            }
            catch(Exception e){checks.Add(new Check{name="fixture",detail=e.ToString(),passed=false});}
            finally{Application.logMessageReceived-=Capture;foreach(var r in roots)if(r)UnityEngine.Object.DestroyImmediate(r);roots.Clear();}
            var report=new Report{success=checks.All(x=>x.passed)&&errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray()};Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));Debug.Log("CARTFLOW26 "+(report.success?"PASS":"FAIL"));if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
    }
}
