using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class CartVisual26SceneAcceptance
    {
        [Serializable] public class Report { public bool success;public string scope="Real MarketWorld + ShoppingCart + visible models, scripted legal AddSupply commands for 18, mixed goods and 24; actual receipt UI callbacks and raycast checks; close verification camera, not hardware-input playthrough.";public int count,overlaps,outside,mixedCount,mixedOverlaps,mixedOutside,maxCount,maxOverlaps,maxOutside;public bool nextHit,previousHit;public long mixedSyncMs,maxSyncMs;public bool originalScaleRestored,nonUniformScaleRestored,loadedRotationRestored,returnScaleRestored,returnRotationRestored;public string failure="",normalScreenshot="",closeScreenshot="",basketPage1="",basketPage2="",mixedScreenshot="",maxScreenshot="";public string[] engineErrors; }
        const string Pending="Guochao.CartVisual26.Pending",Output="Guochao.CartVisual26.Output";static string output;static Report report;static Feedback3CoroutineHost host;static GameObject fixture;static readonly List<string> errors=new List<string>();
        static CartVisual26SceneAcceptance(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"--cart-visual-output");if(i<0||i+1>=a.Length)throw new Exception("missing --cart-visual-output");string p=Path.GetFullPath(a[i+1]);if(File.Exists(p))throw new Exception("preserve previous result");SessionState.SetString(Output,p);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();Application.logMessageReceived+=Log;
                host=new GameObject("CartVisual26 coroutine").AddComponent<Feedback3CoroutineHost>();host.Launch(Execute(),()=>Finish(""),e=>Finish(e.ToString()));
            }
        }
        static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
        static void Log(string m,string trace,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m+"\n"+trace);}
        static IEnumerator Execute()
        {
            var size=Resize(1280,720);while(size.MoveNext())yield return size.Current;
            var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};Need(profile.Validate(out string why),why);var run=new RunState(4,profile);run.Paused=false;
            for(int i=0;run.HasUnloadedEquipment&&i<30;i++){Need(run.Act("StovePickup",true),run.Message);Need(run.Act("HomeCart",true),run.Message);}
            Need(run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"cannot enter market");for(int i=0;i<18;i++)Need(run.AddSupply("rice_small"),"basket "+i+": "+run.Message);
            fixture=new GameObject("Actual market 18 item fixture");var flow=fixture.AddComponent<GameFlow>();flow.enabled=false;typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);flow.Initialize();
            var world=flow.World;Need(world!=null&&run.Phase==GamePhase.Market&&world.ShoppingMotor!=null,"market world failed: "+run.Message);
            world.Player.enabled=false;world.ShoppingMotor.enabled=false;world.enabled=false;
            yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            var values=(Dictionary<string,GameObject>)typeof(WorldRuntime).GetField("groceries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);report.count=values.Count;Need(values.Count==18,"visible item count="+values.Count);
            var items=new List<GameObject>(values.Values);foreach(var item in items)Need(item!=null&&item.activeInHierarchy,"missing or hidden item");
            for(int i=0;i<items.Count;i++)for(int j=0;j<i;j++){
                Bounds a=WorldRuntime.RenderBounds(items[i]),b=WorldRuntime.RenderBounds(items[j]);
                float x=Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x),y=Mathf.Min(a.max.y,b.max.y)-Mathf.Max(a.min.y,b.min.y),z=Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z);
                if(x>.005f&&y>.005f&&z>.005f)report.overlaps++;
            }
            var cart=(GameObject)typeof(WorldRuntime).GetField("trolley",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);Need(cart!=null,"shopping trolley missing");
            Bounds bottom=new Bounds();bool found=false;foreach(var r in cart.GetComponentsInChildren<Renderer>(true))if(r.name=="Shopping_basket_bottom"){bottom=r.bounds;found=true;break;}Need(found,"basket floor missing");report.outside=Outside(values,cart.transform);Need(report.outside==0,"18 items outside basket: "+report.outside);
            for(int i=0;i<items.Count;i++){Bounds b=WorldRuntime.RenderBounds(items[i]);Need(b.min.y>=bottom.max.y-.04f,"item below basket floor");}
            for(int i=0;i<items.Count;i++){
                float gap=WorldRuntime.RenderBounds(items[i]).min.y-bottom.max.y;
                Need(gap>=-.015f&&gap<=.025f,"item unsupported by actual basket floor: gap="+gap);
            }
            string dir=Path.GetDirectoryName(output);report.normalScreenshot=Path.Combine(dir,"market-18-normal.png");ScreenCapture.CaptureScreenshot(report.normalScreenshot);yield return new WaitForEndOfFrame();yield return null;
            var cam=world.View;Vector3 center=cart.transform.position+Vector3.up*.8f;cam.orthographic=true;cam.orthographicSize=2.1f;cam.transform.position=center+new Vector3(2.7f,3.1f,3.1f);cam.transform.LookAt(center);
            yield return null;yield return new WaitForEndOfFrame();report.closeScreenshot=Path.Combine(dir,"market-18-close.png");ScreenCapture.CaptureScreenshot(report.closeScreenshot);yield return new WaitForEndOfFrame();yield return null;
            Need(File.Exists(report.normalScreenshot)&&File.Exists(report.closeScreenshot),"screenshot not written");Need(report.overlaps==0,"models with overlapping actual bounds: "+report.overlaps);
            var ui=new GameObject("Receipt canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));ui.transform.SetParent(fixture.transform,false);
            var canvas=ui.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;var scaler=ui.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            if(EventSystem.current==null){var e=new GameObject("Receipt EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));e.transform.SetParent(fixture.transform,false);}
            var receiptGo=new GameObject("Actual receipt");receiptGo.transform.SetParent(ui.transform,false);var receipt=receiptGo.AddComponent<ShoppingReceiptView>();var font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");Need(font!=null,"receipt font missing");receipt.Initialize((RectTransform)ui.transform,font,()=>{});
            receipt.Present(flow,new Snapshot{phase=run.Phase,cash=run.CashCents/100f,cartCount=run.Cart.Count,cartCellsUsed=run.CartCellsUsed,cartCapacity=run.CartCapacity});
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var grid=(Button)typeof(ShoppingReceiptView).GetField("gridTab",flags).GetValue(receipt);var next=(Button)typeof(ShoppingReceiptView).GetField("next",flags).GetValue(receipt);var previous=(Button)typeof(ShoppingReceiptView).GetField("previous",flags).GetValue(receipt);
            grid.onClick.Invoke();Canvas.ForceUpdateCanvases();yield return null;yield return new WaitForEndOfFrame();report.nextHit=Hit(next);Need(report.nextHit&&next.interactable,"next-page button cannot be hit");
            report.basketPage1=Path.Combine(dir,"receipt-18-page1.png");ScreenCapture.CaptureScreenshot(report.basketPage1);yield return new WaitForEndOfFrame();yield return null;
            next.onClick.Invoke();Canvas.ForceUpdateCanvases();yield return null;yield return new WaitForEndOfFrame();report.previousHit=Hit(previous);Need(report.previousHit&&previous.interactable,"previous-page button cannot be hit");
            report.basketPage2=Path.Combine(dir,"receipt-18-page2.png");ScreenCapture.CaptureScreenshot(report.basketPage2);yield return new WaitForEndOfFrame();yield return null;
            Need(File.Exists(report.basketPage1)&&File.Exists(report.basketPage2),"receipt screenshots not written");
            foreach(Button b in new[]{previous,next}){var label=b.GetComponentInChildren<Text>(true);Need(label.preferredWidth<=label.rectTransform.rect.width+1f&&label.preferredHeight<=label.rectTransform.rect.height+1f,"720p clipped page button: "+label.text);}
            foreach(string field in new[]{"gridCaption","pager","footnote"}){var t=(Text)typeof(ShoppingReceiptView).GetField(field,flags).GetValue(receipt);Need(t.preferredHeight<=t.rectTransform.rect.height+1f,"720p clipped: "+field+" preferred="+t.preferredHeight+" height="+t.rectTransform.rect.height);}
            var note=(Text)typeof(ShoppingReceiptView).GetField("footnote",flags).GetValue(receipt);string savedNote=note.text;
            foreach(string message in new[]{"购物篮用于选货 · 摊车容量另算","货物已在三轮车上 · 可继续采购或出发","付款完成 · 到门外三轮车旁按 E 装车","先挑选商品 · 只有付款才会扣现金","单价为实际结账价 · 到对应货架可放回","请留在柜台 · 剩余 99.9 秒\n离开会中断，本次不扣款"}){note.text=message;Canvas.ForceUpdateCanvases();Need(note.preferredHeight<=note.rectTransform.rect.height+1f,"clipped footer: "+message+" preferred="+note.preferredHeight+" height="+note.rectTransform.rect.height);}note.text=savedNote;
            var size1080=Resize(1920,1080);while(size1080.MoveNext())yield return size1080.Current;
            Canvas.ForceUpdateCanvases();yield return null;yield return new WaitForEndOfFrame();
            Need(Hit(previous),"1080p previous-page button cannot be hit");
            foreach(Button b in new[]{previous,next}){var label=b.GetComponentInChildren<Text>(true);Need(label.preferredWidth<=label.rectTransform.rect.width+1f&&label.preferredHeight<=label.rectTransform.rect.height+1f,"1080p clipped page button: "+label.text);}
            foreach(string field in new[]{"gridCaption","pager","footnote"}){var t=(Text)typeof(ShoppingReceiptView).GetField(field,flags).GetValue(receipt);Need(t.preferredHeight<=t.rectTransform.rect.height+1f,"1080p clipped: "+field+" preferred="+t.preferredHeight+" height="+t.rectTransform.rect.height);}
            string receipt1080=Path.Combine(dir,"receipt-18-page2-1080.png");ScreenCapture.CaptureScreenshot(receipt1080);yield return new WaitForEndOfFrame();yield return null;Need(File.Exists(receipt1080),"1080p screenshot missing");
            var size720=Resize(1280,720);while(size720.MoveNext())yield return size720.Current;
            ui.SetActive(false);
            while(run.Cart.Count>0){int last=run.Cart.Count-1;Need(run.ReturnItem(last,GameCatalog.Find(run.Cart[last]).anchor,true),"return before mixed fixture "+run.Message);}
            foreach(string id in new[]{"B03","V15","rice_small","egg_small","meat_small","dough_small","skewer_small","prepared_small","fruit_small","seasoning_small","P02","S01"})Need(run.AddSupply(id),"mixed purchase "+id+" "+run.Message);
            var syncWatch=System.Diagnostics.Stopwatch.StartNew();world.Sync(run);syncWatch.Stop();report.mixedSyncMs=syncWatch.ElapsedMilliseconds;yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            report.mixedCount=values.Count;report.mixedOverlaps=Overlaps(values);report.mixedOutside=Outside(values,cart.transform);report.mixedScreenshot=Path.Combine(dir,"market-mixed-close.png");ScreenCapture.CaptureScreenshot(report.mixedScreenshot);yield return new WaitForEndOfFrame();yield return null;
            var scaleBook=(Dictionary<string,Vector3>)typeof(WorldRuntime).GetField("groceryNaturalScales",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
            int prepIndex=run.Cart.IndexOf("prepared_small"),eggIndex=run.Cart.IndexOf("egg_small");
            Need(prepIndex>=0&&eggIndex>=0,"mixed fixture items missing");
            string prepKey=ProcurementInstanceKeys.Key("prepared_small",ProcurementInstanceKeys.OrdinalOf(run.Cargo,run.Cart,"prepared_small",prepIndex));
            string eggKey=ProcurementInstanceKeys.Key("egg_small",ProcurementInstanceKeys.OrdinalOf(run.Cargo,run.Cart,"egg_small",eggIndex));
            Vector3 prepNatural=scaleBook[prepKey],eggNatural=scaleBook[eggKey];
            Vector3 eggNonUniform=Vector3.Scale(eggNatural,new Vector3(.8f,.7f,.9f));
            scaleBook[eggKey]=eggNonUniform;
            int cashBefore=run.CashCents,due=run.PurchaseItemPriceCents("prepared_small")+run.PurchaseItemPriceCents("egg_small");
            Need(run.ConfirmCheckoutSelection(new[]{"prepared_small","egg_small"}),"actual partial checkout: "+run.Message);
            run.Tick(run.CheckoutDuration+.01f,true);Need(run.Paid&&run.CashCents==cashBefore-due,"partial payment was incorrect");
            world.Sync(run);yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            Vector3 eggBasket=values[eggKey].transform.localScale;
            Need(Mathf.Abs(eggBasket.x/eggNonUniform.x-eggBasket.y/eggNonUniform.y)<.001f&&Mathf.Abs(eggBasket.y/eggNonUniform.y-eggBasket.z/eggNonUniform.z)<.001f,"basket changed nonuniform proportions");
            Need(run.Act("MarketCart",true),"actual load: "+run.Message);world.Sync(run);yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            report.originalScaleRestored=Vector3.Distance(values[prepKey].transform.localScale,prepNatural)<.0001f;
            report.nonUniformScaleRestored=Vector3.Distance(values[eggKey].transform.localScale,eggNonUniform)<.0001f;
            var realLoadout=(VehicleLoadout)typeof(WorldRuntime).GetField("loadout",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
            report.loadedRotationRestored=true;
            for(int i=0;i<run.Cargo.Count;i++){
                string sku=run.Cargo[i],key=ProcurementInstanceKeys.Key(sku,ProcurementInstanceKeys.CargoOrdinal(run.Cargo,i));
                var packed=realLoadout.Plan.Find(ProcurementInstanceKeys.LoadoutKey(run.Cargo,i));
                if(packed==null||!values[key].activeInHierarchy||Quaternion.Angle(values[key].transform.rotation,packed.rotation)>.1f)report.loadedRotationRestored=false;
            }
            Need(report.originalScaleRestored&&report.nonUniformScaleRestored&&report.loadedRotationRestored,"loaded scale or orientation not restored");
            Need(run.Act("RiceShelf",true,true),"take shelf rice: "+run.Message);world.Sync(run);yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            int riceIndex=run.Cart.IndexOf("rice");string riceKey=ProcurementInstanceKeys.Key("rice",ProcurementInstanceKeys.OrdinalOf(run.Cargo,run.Cart,"rice",riceIndex));
            var riceObject=values[riceKey];Vector3 riceNatural=scaleBook[riceKey];
            Need(run.ReturnItem(riceIndex,"RiceShelf",true),"return rice: "+run.Message);world.Sync(run);
            report.returnScaleRestored=Vector3.Distance(riceObject.transform.localScale,riceNatural)<.0001f;
            var motion=riceObject.GetComponent<ItemTransferMotion>();var shelf=(GameObject[])typeof(WorldRuntime).GetField("shelfSamples",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
            report.returnRotationRestored=motion!=null&&Quaternion.Angle(riceObject.transform.parent.rotation*(Quaternion)typeof(ItemTransferMotion).GetField("destinationRotation",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(motion),shelf[0].transform.rotation)<.1f;
            Need(report.returnScaleRestored&&report.returnRotationRestored,"return-to-shelf scale or destination orientation not restored");
            var expanded=new RunProfile{careerId="career1",selectedTraits=new[]{"收纳达人"}};Need(expanded.Validate(out why),why);var full=new RunState(4,expanded);full.Paused=false;
            for(int i=0;full.HasUnloadedEquipment&&i<30;i++){Need(full.Act("StovePickup",true),full.Message);Need(full.Act("HomeCart",true),full.Message);}Need(full.Act("HomeExit",true),"expanded fixture market");for(int i=0;i<24;i++)Need(full.AddSupply("rice_small"),"expanded basket "+i+": "+full.Message);
            typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,full);syncWatch.Restart();world.Sync(full);syncWatch.Stop();report.maxSyncMs=syncWatch.ElapsedMilliseconds;yield return new WaitForSecondsRealtime(.7f);yield return new WaitForEndOfFrame();
            report.maxCount=values.Count;report.maxOverlaps=Overlaps(values);report.maxOutside=Outside(values,cart.transform);report.maxScreenshot=Path.Combine(dir,"market-24-close.png");ScreenCapture.CaptureScreenshot(report.maxScreenshot);yield return new WaitForEndOfFrame();yield return null;
            Need(report.mixedCount==12&&report.mixedOverlaps==0&&report.mixedOutside==0,"mixed goods overlap or missing: "+report.mixedCount+"/"+report.mixedOverlaps);Need(report.maxCount==24&&report.maxOverlaps==0&&report.maxOutside==0,"24 goods overlap or missing: "+report.maxCount+"/"+report.maxOverlaps);
        }
        static int Outside(Dictionary<string,GameObject> values,Transform frame)
        {
            int outside=0;
            foreach(var item in values.Values)
            {
                bool any=false;Bounds b=new Bounds();
                foreach(Renderer r in item.GetComponentsInChildren<Renderer>(true))
                {
                    if(!(r is MeshRenderer)&&!(r is SkinnedMeshRenderer))continue;
                    Bounds local=r.localBounds;
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 sign=new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                        Vector3 point=frame.InverseTransformPoint(r.transform.TransformPoint(local.center+Vector3.Scale(local.extents,sign)));
                        if(!any){b=new Bounds(point,Vector3.zero);any=true;}else b.Encapsulate(point);
                    }
                }
                if(!any||b.min.x<-.405f||b.max.x>.405f||b.min.z<-.655f||b.max.z>.655f||b.min.y<.555f||b.max.y>1.085f)outside++;
            }
            return outside;
        }
        static int Overlaps(Dictionary<string,GameObject> values)
        {int n=0;var items=new List<GameObject>(values.Values);for(int i=0;i<items.Count;i++)for(int j=0;j<i;j++){Bounds a=WorldRuntime.RenderBounds(items[i]),b=WorldRuntime.RenderBounds(items[j]);float x=Mathf.Min(a.max.x,b.max.x)-Mathf.Max(a.min.x,b.min.x),y=Mathf.Min(a.max.y,b.max.y)-Mathf.Max(a.min.y,b.min.y),z=Mathf.Min(a.max.z,b.max.z)-Mathf.Max(a.min.z,b.min.z);if(x>.005f&&y>.005f&&z>.005f)n++;}return n;}
        static bool Hit(Button button)
        {var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);var p=RectTransformUtility.WorldToScreenPoint(null,(corners[0]+corners[2])*.5f);var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},hits);return hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform));}
        static Type EditorType(string name){var t=Type.GetType(name+", UnityEditor");if(t!=null)return t;foreach(var a in AppDomain.CurrentDomain.GetAssemblies()){t=a.GetType(name,false);if(t!=null)return t;}return null;}
        static IEnumerator Resize(int width,int height)
        {
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            Type viewType=EditorType("UnityEditor.GameView"),sizesType=EditorType("UnityEditor.GameViewSizes"),sizeType=EditorType("UnityEditor.GameViewSize"),sizeKind=EditorType("UnityEditor.GameViewSizeType");
            Need(viewType!=null&&sizesType!=null&&sizeType!=null&&sizeKind!=null,"GameView API unavailable");var view=EditorWindow.GetWindow(viewType);view.Show();view.Focus();
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);object sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            object groupType=sizesType.GetProperty("currentGroupType",flags).GetValue(sizes),group=sizesType.GetMethod("GetGroup",flags).Invoke(sizes,new[]{groupType});
            Type groupClass=group.GetType();int builtin=(int)groupClass.GetMethod("GetBuiltinCount",flags).Invoke(group,null),custom=(int)groupClass.GetMethod("GetCustomCount",flags).Invoke(group,null),selected=-1;
            for(int i=0;i<builtin+custom;i++){object candidate=groupClass.GetMethod("GetGameViewSize",flags).Invoke(group,new object[]{i});int w=(int)sizeType.GetProperty("width",flags).GetValue(candidate),h=(int)sizeType.GetProperty("height",flags).GetValue(candidate);if(w==width&&h==height&&sizeType.GetProperty("sizeType",flags).GetValue(candidate).ToString()=="FixedResolution"){selected=i;break;}}
            if(selected<0){object size=Activator.CreateInstance(sizeType,flags,null,new object[]{Enum.Parse(sizeKind,"FixedResolution"),width,height,"CartVisual26 720p"},null);groupClass.GetMethod("AddCustomSize",flags).Invoke(group,new[]{size});selected=builtin+custom;}
            viewType.GetProperty("selectedSizeIndex",flags).SetValue(view,selected);var low=viewType.GetProperty("lowResolutionForAspectRatios",flags);if(low!=null&&low.CanWrite)low.SetValue(view,false);
            double deadline=Time.realtimeSinceStartupAsDouble+10;while(Screen.width!=width||Screen.height!=height){Need(Time.realtimeSinceStartupAsDouble<deadline,"resolution mismatch");view.Repaint();yield return null;}yield return null;yield return null;
        }
        static void Finish(string why)
        {if(fixture)UnityEngine.Object.DestroyImmediate(fixture);Application.logMessageReceived-=Log;report.failure=why;report.engineErrors=errors.ToArray();report.success=why==""&&errors.Count==0;Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);}
    }
}
