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
    // Editor-only, explicitly staged layout photographs. Does not replay a
    // campaign, write a real save, or claim human input / gameplay acceptance.
    // Run graphically, WITHOUT -batchmode / -nographics / -quit:
    // -executeMethod Guochao.Editor.BusinessLayout20Preview.Run
    // --business-layout20-output /absolute/new-directory/report.json
    [InitializeOnLoad]
    public static class BusinessLayout20Preview
    {
        [Serializable] public sealed class RectSample
        {
            public string name,path;
            public bool active,insideScreen,rootHasHUDSoftMotion;
            public int descendantHUDSoftMotion;
            public float x,y,width,height;
            public Vector2 anchoredPosition;
            public Vector3 localScale;
            public string[] text;
        }
        [Serializable] public sealed class TargetSample
        {
            public string name,kind,itemId,recipe,step,bodyPickedPath,bodyPickedKind,bodyPickedItem,visualPickedPath;
            public int lane,orderId;
            public bool active,anchorInFront,anchorInsideScreen,hasHitCollider,bodyPickMatchesTarget,bodyBlockedByUI,hasVisibleGeometry,visualPickMatchesTarget,visualBlockedByUI;
            public Vector3 worldAnchor,anchorScreen,bodyCenter,bodyScreen,visualCenter,visualScreen;
            public RectSample label;
        }
        [Serializable] public sealed class FrameSample
        {
            public string name,screenshot;
            public int requestedWidth,requestedHeight,width,height,frame,visibleCustomerActors,visibleOrderCards,hotCookware,rootLabelHUDSoftMotion;
            public float sessionElapsed,maximumLabelDriftPixels;
            public bool exactResolution,layoutChecksPassed;
            public Vector3 cameraPosition,cameraEuler;
            public float orthographicSize;
            public List<RectSample> panels=new List<RectSample>();
            public List<TargetSample> targets=new List<TargetSample>();
            public List<string> layoutIssues=new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.business-layout.v20.1";
            public string scope="Controlled staged layout preview, not human gameplay: production MidgamePractice model and real restored BusinessController/StreetStallStage/rigged customers/HUD. Four generated orders are placed at real queue slots via the production restore path. Production egg/rice and flatbread preparation creates hot cooking states; finite staged patience fractions make four order states readable. BusinessController simulation/input are then disabled only for this editor fixture so both resolutions photograph the same model checkpoint. No changes to production colliders or label components.";
            public string captureBackend="Real graphical Editor GameView, fixed pixel resolutions, ScreenCapture after WaitForEndOfFrame";
            public string failure="",isolatedSaveDirectory;
            public bool success,captureComplete,layoutChecksPassed,controlledFixture=true,humanInput=false,fullBusinessRun=false,formalSaveWrites=false;
            public int requestedOrderCount=4;
            public List<FrameSample> snapshots=new List<FrameSample>();
            public string[] engineErrors;
        }

        const string Pending="Guochao.BusinessLayout20.Pending",Output="Guochao.BusinessLayout20.Output";
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly List<string> errors=new List<string>();
        static Report report;
        static string output,directory,previousSaveDirectory;
        static Feedback3CoroutineHost host;
        static GameObject flowObject;
        static GameFlow flow;
        static BusinessController controller;
        static EditorWindow gameView;
        static double startedAt;
        static bool finishing;

        static BusinessLayout20Preview(){EditorApplication.update-=Watch;EditorApplication.update+=Watch;}

        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--business-layout20-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --business-layout20-output <new-directory/report.json>");
            string path=Path.GetFullPath(args[at+1]);
            if(!path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))path=Path.Combine(path,"report.json");
            Need(!File.Exists(path),"Preserve previous preview reports; use a new output directory.");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void Watch()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");directory=Path.GetDirectoryName(output);
                Directory.CreateDirectory(directory);report=new Report();errors.Clear();finishing=false;
                startedAt=EditorApplication.timeSinceStartup;previousSaveDirectory=CampaignSaveStore.TestDirectory;
                report.isolatedSaveDirectory=Path.Combine(directory,"isolated-layout-save");
                CampaignSaveStore.TestDirectory=report.isolatedSaveDirectory;
                Application.logMessageReceived+=OnLog;
                host=new GameObject("BusinessLayout20_RealFrameHost").AddComponent<Feedback3CoroutineHost>();
                host.Launch(Flatten(Execute()),()=>Finish(null),Finish);
            }
            if(!finishing&&host!=null&&host.IsRunning&&EditorApplication.timeSinceStartup-startedAt>120)
            {host.Abort();Finish(new TimeoutException("Business layout preview exceeded 120 real seconds."));}
        }

        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void OnLog(string message,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(type+": "+message+"\n"+trace);}

        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try
            {
                while(stack.Count>0)
                {
                    IEnumerator top=stack.Peek();
                    if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(top.Current is IEnumerator child){stack.Push(child);continue;}
                    yield return top.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }

        static IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Use graphical Editor, without -batchmode/-nographics; camera-only renders do not include the actual overlay HUD.");
            Need(GameFlow.Instance==null,"Expected isolated empty play scene; refusing to replace an existing GameFlow.");
            yield return Resize(1920,1080);
            // Awake supplies the real Run/Instance; disabled before Start so it
            // cannot initialize a campaign, open creation UI, or autosave.
            flowObject=new GameObject("BusinessLayout20_IsolatedPracticeFlow");
            flow=flowObject.AddComponent<GameFlow>();flow.enabled=false;
            Need(flow.Run!=null&&GameFlow.Instance==flow,"Isolated GameFlow did not initialize.");
            // Match the normal scene's graphic raycaster context while keeping
            // this static fixture independent of the user's live mouse cursor.
            var events=new GameObject("BusinessLayout20_EventSystem").AddComponent<EventSystem>();
            events.gameObject.AddComponent<StandaloneInputModule>().enabled=false;
            BusinessSaveData checkpoint=Checkpoint();
            controller=BusinessController.BeginRestored(checkpoint,phase=>flow.Run.SetBusinessPhase(phase),null,null);
            typeof(GameFlow).GetField("business",Hidden).SetValue(flow,controller);
            controller.enabled=false;controller.InputHandler.enabled=false;
            controller.Stage.Present(controller.Session,0,true);
            controller.HUD.Refresh(false);
            controller.HUD.GetComponentInChildren<BusinessWorldHUD>(true).ShowOrderGuide(controller.Session.Orders[0].id);
            yield return Settle(.8f);
            Need(controller.Stage.VisibleCustomerCount==4,"Controlled fixture did not produce four real visible customer actors.");
            yield return Capture(1920,1080);
            yield return Resize(1280,720);
            controller.Stage.Present(controller.Session,0,true);controller.HUD.Refresh(false);
            yield return Settle(.8f);
            yield return Capture(1280,720);
            report.captureComplete=report.snapshots.Count==2;
            report.layoutChecksPassed=report.captureComplete;
            foreach(FrameSample frame in report.snapshots)report.layoutChecksPassed&=frame.layoutChecksPassed;
            Need(!Directory.Exists(report.isolatedSaveDirectory)||Directory.GetFiles(report.isolatedSaveDirectory,"*",SearchOption.AllDirectories).Length==0,
                "Unexpected save write during controlled visual preview.");
        }

        static BusinessSaveData Checkpoint()
        {
            var session=new BusinessSession(BusinessSetup.MidgamePractice());
            session.SelectedRecipes.Clear();
            foreach(int index in new[]{0,2,10,11})
            {Need(session.Missing(index)=="","Practice recipe unavailable: "+BusinessSession.Recipes[index].name+" / "+session.Missing(index));session.SelectedRecipes.Add(index);}
            Need(session.Open(),"Practice fixture could not open: "+session.Message);
            for(int i=0;i<480&&session.Orders.Count<4;i++)session.Tick(.25f);
            Need(session.Orders.Count==4,"Expected exactly four seed-generated orders, not fabricated order IDs.");
            foreach(StallOrder order in session.Orders)Need(session.MarkCustomerArrived(order.id),"Fixture customer could not arrive.");
            // Warm both real hot vessels by the actual production ingredient
            // transactions, including separate egg and rice steps.
            Need(session.BeginBatch(0),"Egg preparation failed.");
            Need(session.AddRiceBatch(0),"Rice preparation failed.");
            Need(session.BeginBatch(1),"Flatbread preparation failed.");
            for(int i=0;i<30&&(session.Lanes[0].step!=KitchenStep.Heating||session.Lanes[1].step!=KitchenStep.Heating);i++)session.Tick(.1f);
            Need(session.Lanes[0].step==KitchenStep.Heating&&session.Lanes[1].step==KitchenStep.Heating,"Fixture did not reach two real heating states.");
            session.Tick(1.1f);
            float[] fractions={.86f,.66f,.43f,.23f};
            for(int i=0;i<4;i++)session.Orders[i].patience=session.Orders[i].maxPatience*fractions[i];
            BusinessSaveData saved=session.CaptureSave();
            var seats=new BusinessGuestPlacementSaveData[4];
            for(int i=0;i<4;i++)seats[i]=new BusinessGuestPlacementSaveData{orderId=session.Orders[i].id,slot=i};
            saved.presentation=new BusinessPresentationSaveData{arrivedGuests=seats};
            return JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(saved));
        }

        static IEnumerator Settle(float seconds)
        {
            double until=Time.realtimeSinceStartupAsDouble+seconds;
            while(Time.realtimeSinceStartupAsDouble<until){gameView.Repaint();yield return null;}
            Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
        }

        static IEnumerator Resize(int width,int height)
        {
            Type viewType=EditorType("UnityEditor.GameView"),sizesType=EditorType("UnityEditor.GameViewSizes"),sizeType=EditorType("UnityEditor.GameViewSize"),sizeKind=EditorType("UnityEditor.GameViewSizeType");
            Need(viewType!=null&&sizesType!=null&&sizeType!=null&&sizeKind!=null,"This Unity Editor has no supported GameView size API.");
            gameView=EditorWindow.GetWindow(viewType);gameView.Show();gameView.Focus();
            Type singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            object sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            object groupType=sizesType.GetProperty("currentGroupType",Hidden).GetValue(sizes);
            object group=sizesType.GetMethod("GetGroup",Hidden).Invoke(sizes,new[]{groupType});
            Type groupClass=group.GetType();
            int builtin=(int)groupClass.GetMethod("GetBuiltinCount",Hidden).Invoke(group,null);
            int custom=(int)groupClass.GetMethod("GetCustomCount",Hidden).Invoke(group,null);
            int selected=-1;
            for(int i=0;i<builtin+custom;i++)
            {
                object candidate=groupClass.GetMethod("GetGameViewSize",Hidden).Invoke(group,new object[]{i});
                int w=(int)sizeType.GetProperty("width",Hidden).GetValue(candidate),h=(int)sizeType.GetProperty("height",Hidden).GetValue(candidate);
                object kind=sizeType.GetProperty("sizeType",Hidden).GetValue(candidate);
                if(w==width&&h==height&&kind.ToString()=="FixedResolution"){selected=i;break;}
            }
            if(selected<0)
            {
                object size=Activator.CreateInstance(sizeType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,
                    new object[]{Enum.Parse(sizeKind,"FixedResolution"),width,height,"BusinessLayout20 "+width+"x"+height},null);
                groupClass.GetMethod("AddCustomSize",Hidden).Invoke(group,new[]{size});selected=builtin+custom;
            }
            viewType.GetProperty("selectedSizeIndex",Hidden).SetValue(gameView,selected);
            PropertyInfo low=viewType.GetProperty("lowResolutionForAspectRatios",Hidden);if(low!=null&&low.CanWrite)low.SetValue(gameView,false);
            gameView.Repaint();
            double deadline=Time.realtimeSinceStartupAsDouble+10;
            while(Screen.width!=width||Screen.height!=height)
            {Need(Time.realtimeSinceStartupAsDouble<deadline,"GameView size mismatch: wanted "+width+"x"+height+", got "+Screen.width+"x"+Screen.height);gameView.Repaint();yield return null;}
            yield return null;yield return null;
        }

        static Type EditorType(string name)
        {
            Type type=Type.GetType(name+", UnityEditor");if(type!=null)return type;
            foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {type=assembly.GetType(name,false);if(type!=null)return type;}
            return null;
        }

        static string TransformPath(Transform node)
        {string result=node.name;while(node.parent!=null){node=node.parent;result=node.name+"/"+result;}return result;}
        static Rect ScreenRect(RectTransform root)
        {
            Vector3[] corners=new Vector3[4];root.GetWorldCorners(corners);
            Canvas canvas=root.GetComponentInParent<Canvas>();Camera camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            Vector2 min=new Vector2(float.PositiveInfinity,float.PositiveInfinity),max=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(Vector3 corner in corners){Vector2 p=RectTransformUtility.WorldToScreenPoint(camera,corner);min=Vector2.Min(min,p);max=Vector2.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        static RectSample RectInfo(RectTransform root)
        {
            Rect r=ScreenRect(root);var texts=new List<string>();
            foreach(Text text in root.GetComponentsInChildren<Text>(false))if(!string.IsNullOrEmpty(text.text))texts.Add(text.text);
            return new RectSample{name=root.name,path=TransformPath(root),active=root.gameObject.activeInHierarchy,
                x=r.x,y=r.y,width=r.width,height=r.height,insideScreen=r.xMin>=-.5f&&r.yMin>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f,
                anchoredPosition=root.anchoredPosition,localScale=root.localScale,rootHasHUDSoftMotion=root.GetComponent<HUDSoftMotion>()!=null,
                descendantHUDSoftMotion=root.GetComponentsInChildren<HUDSoftMotion>(true).Length,text=texts.ToArray()};
        }

        static Dictionary<StallInteractionTarget,RectTransform> TagRoots(BusinessWorldHUD hud)
        {
            var result=new Dictionary<StallInteractionTarget,RectTransform>();
            FieldInfo field=typeof(BusinessWorldHUD).GetField("tags",Hidden);
            Need(field!=null,"Actual production HUD tag dictionary not found; update preview adapter.");
            var dictionary=field.GetValue(hud) as IDictionary;Need(dictionary!=null,"Actual HUD tag dictionary is unavailable.");
            foreach(DictionaryEntry entry in dictionary)
            {
                var target=entry.Key as StallInteractionTarget;if(target==null||entry.Value==null)continue;
                FieldInfo root=entry.Value.GetType().GetField("root",Hidden);
                Need(root!=null,"Actual HUD tag root not found; no guessed tag association allowed.");
                result[target]=(RectTransform)root.GetValue(entry.Value);
            }
            return result;
        }

        static IEnumerator Capture(int width,int height)
        {
            var hud=controller.GetComponentInChildren<BusinessWorldHUD>(true);Need(hud!=null,"Live BusinessWorldHUD missing.");
            controller.HUD.Refresh(false);Canvas.ForceUpdateCanvases();yield return Settle(.35f);
            var initial=new Dictionary<StallInteractionTarget,Vector2>();
            foreach(var pair in TagRoots(hud))if(pair.Value!=null&&pair.Value.gameObject.activeInHierarchy)initial[pair.Key]=ScreenRect(pair.Value).center;
            float maxDrift=0;
            for(int i=0;i<8;i++)
            {
                yield return null;
                foreach(var pair in TagRoots(hud))if(pair.Value!=null&&pair.Value.gameObject.activeInHierarchy&&initial.TryGetValue(pair.Key,out Vector2 first))
                    maxDrift=Mathf.Max(maxDrift,Vector2.Distance(first,ScreenRect(pair.Value).center));
            }
            yield return new WaitForEndOfFrame();
            string name="business-layout-"+width+"x"+height,path=Path.Combine(directory,name+".png");
            Need(!File.Exists(path),"Refusing to overwrite previous screenshot: "+path);
            var frame=new FrameSample{name=name,screenshot=path,requestedWidth=width,requestedHeight=height,width=Screen.width,height=Screen.height,
                exactResolution=Screen.width==width&&Screen.height==height,frame=Time.frameCount,visibleCustomerActors=controller.Stage.VisibleCustomerCount,
                sessionElapsed=controller.Session.Elapsed,maximumLabelDriftPixels=maxDrift,cameraPosition=controller.Stage.View.transform.position,
                cameraEuler=controller.Stage.View.transform.eulerAngles,orthographicSize=controller.Stage.View.orthographicSize};
            var roots=TagRoots(hud);
            Physics.SyncTransforms();
            foreach(StallInteractionTarget target in controller.Stage.GetComponentsInChildren<StallInteractionTarget>(true))
            {
                Vector3 screen=controller.Stage.View.WorldToScreenPoint(target.WorldAnchor);
                var row=new TargetSample{name=target.name,kind=target.kind.ToString(),itemId=target.itemId,lane=target.lane,orderId=target.orderId,
                    active=target.gameObject.activeInHierarchy,worldAnchor=target.WorldAnchor,anchorScreen=screen,
                    anchorInFront=screen.z>0,anchorInsideScreen=screen.z>0&&screen.x>=0&&screen.y>=0&&screen.x<=Screen.width&&screen.y<=Screen.height,
                    hasHitCollider=target.HitCollider!=null};
                row.bodyCenter=target.HitCollider!=null?target.HitCollider.bounds.center:target.transform.position;
                row.bodyScreen=controller.Stage.View.WorldToScreenPoint(row.bodyCenter);
                StallInteractionTarget picked=controller.InputHandler.PickScreenPoint(row.bodyScreen);
                row.bodyPickMatchesTarget=picked==target;
                row.bodyBlockedByUI=controller.InputHandler.IsScreenPointOverUI(row.bodyScreen);
                row.bodyPickedPath=picked!=null?TransformPath(picked.transform):"";
                row.bodyPickedKind=picked!=null?picked.kind.ToString():"none";
                row.bodyPickedItem=picked!=null?picked.itemId:"";
                Bounds visualBounds=new Bounds();
                foreach(Renderer renderer in target.GetComponentsInChildren<Renderer>(false))
                {
                    if(!renderer.enabled||renderer.forceRenderingOff)continue;
                    if(!row.hasVisibleGeometry){visualBounds=renderer.bounds;row.hasVisibleGeometry=true;}
                    else visualBounds.Encapsulate(renderer.bounds);
                }
                row.visualCenter=row.hasVisibleGeometry?visualBounds.center:row.bodyCenter;
                row.visualScreen=controller.Stage.View.WorldToScreenPoint(row.visualCenter);
                StallInteractionTarget visualPick=controller.InputHandler.PickScreenPoint(row.visualScreen);
                row.visualPickMatchesTarget=visualPick==target;
                row.visualBlockedByUI=controller.InputHandler.IsScreenPointOverUI(row.visualScreen);
                row.visualPickedPath=visualPick!=null?TransformPath(visualPick.transform):"";
                if(target.kind==StallInteractionKind.Packaging&&row.active)
                {
                    if(!row.anchorInsideScreen)frame.layoutIssues.Add("Packaging center outside view: "+row.itemId);
                    if(!row.bodyPickMatchesTarget)frame.layoutIssues.Add("Packaging center picked "+row.bodyPickedKind+" / "+row.bodyPickedItem+" instead of "+row.itemId);
                    if(row.bodyBlockedByUI)frame.layoutIssues.Add("Packaging center is blocked by actual HUD raycaster: "+row.itemId);
                    if(!row.hasVisibleGeometry)frame.layoutIssues.Add("Packaging has no active rendered geometry: "+row.itemId);
                    if(!row.visualPickMatchesTarget)frame.layoutIssues.Add("Visible packaging center picked another object: "+row.itemId+" -> "+row.visualPickedPath);
                    if(row.visualBlockedByUI)frame.layoutIssues.Add("Visible packaging center is blocked by actual HUD raycaster: "+row.itemId);
                }
                if(target.lane>=0&&target.lane<controller.Session.Lanes.Length)
                {KitchenLane lane=controller.Session.Lanes[target.lane];row.recipe=BusinessSession.Recipes[lane.recipeIndex].id;row.step=lane.step.ToString();
                 if(target.kind==StallInteractionKind.Cookware&&!BusinessSession.Recipes[lane.recipeIndex].cold&&lane.step==KitchenStep.Heating)frame.hotCookware++;}
                if(roots.TryGetValue(target,out RectTransform tag)&&tag!=null)
                {
                    row.label=RectInfo(tag);
                    if(row.label.rootHasHUDSoftMotion)frame.rootLabelHUDSoftMotion++;
                    if(row.label.active&&!row.label.insideScreen)frame.layoutIssues.Add("Label outside screen: "+row.kind+" / "+row.itemId+" / "+row.orderId);
                    if(target.kind==StallInteractionKind.Order&&row.label.active)frame.visibleOrderCards++;
                }
                frame.targets.Add(row);
            }
            foreach(RectTransform rt in hud.GetComponentsInChildren<RectTransform>(true))
                if(rt.parent!=null&&rt.parent.name=="Safe gameplay HUD"&&rt.name!="Labels anchored to actual objects")frame.panels.Add(RectInfo(rt));
            foreach(RectSample panel in frame.panels)if(panel.active&&!panel.insideScreen)frame.layoutIssues.Add("HUD panel outside screen: "+panel.name);
            foreach(Text text in hud.GetComponentsInChildren<Text>(false))
            {
                bool relevant=false;
                for(Transform t=text.transform;t!=null;t=t.parent)
                    if(t.name=="Customer cooking guide"||t.name=="Object readout Order"){relevant=true;break;}
                if(relevant&&!string.IsNullOrEmpty(text.text)&&text.preferredHeight>text.rectTransform.rect.height+1)
                    frame.layoutIssues.Add("Text clipped: "+text.text.Replace("\n"," / ")+" needs "+text.preferredHeight+" in "+text.rectTransform.rect.height);
            }
            var visible=new List<TargetSample>();foreach(TargetSample row in frame.targets)if(row.label!=null&&row.label.active)visible.Add(row);
            for(int i=0;i<visible.Count;i++)for(int j=i+1;j<visible.Count;j++)
            {
                RectSample a=visible[i].label,b=visible[j].label;
                float overlapX=Mathf.Min(a.x+a.width,b.x+b.width)-Mathf.Max(a.x,b.x),overlapY=Mathf.Min(a.y+a.height,b.y+b.height)-Mathf.Max(a.y,b.y);
                if(overlapX>2&&overlapY>2)frame.layoutIssues.Add("Readout overlap: "+visible[i].kind+"#"+visible[i].orderId+" / "+visible[j].kind+"#"+visible[j].orderId+" ("+(overlapX*overlapY).ToString("0")+" px²)");
            }
            if(frame.rootLabelHUDSoftMotion>0)frame.layoutIssues.Add("HUDSoftMotion remains on "+frame.rootLabelHUDSoftMotion+" actual label root(s)");
            if(maxDrift>.75f)frame.layoutIssues.Add("Stationary checkpoint label drift: "+maxDrift.ToString("0.00")+" px");
            if(frame.visibleOrderCards!=4)frame.layoutIssues.Add("Visible order cards: "+frame.visibleOrderCards+" / 4");
            if(frame.visibleCustomerActors!=4)frame.layoutIssues.Add("Visible customer actors: "+frame.visibleCustomerActors+" / 4");
            if(frame.hotCookware<1)frame.layoutIssues.Add("No visible hot cooking target state recorded");
            frame.layoutChecksPassed=frame.exactResolution&&frame.layoutIssues.Count==0;
            ScreenCapture.CaptureScreenshot(path);
            double deadline=Time.realtimeSinceStartupAsDouble+8;
            while(!File.Exists(path)||new FileInfo(path).Length==0){Need(Time.realtimeSinceStartupAsDouble<deadline,"GameView screenshot not written: "+path);yield return null;}
            byte[] png=File.ReadAllBytes(path);Need(png.Length>=24,"Screenshot is not a complete PNG header.");
            int pngWidth=(png[16]<<24)|(png[17]<<16)|(png[18]<<8)|png[19],pngHeight=(png[20]<<24)|(png[21]<<16)|(png[22]<<8)|png[23];
            Need(pngWidth==width&&pngHeight==height,"Actual PNG resolution mismatch: "+pngWidth+"x"+pngHeight);
            report.snapshots.Add(frame);File.WriteAllText(Path.Combine(directory,name+".json"),JsonUtility.ToJson(frame,true));
        }

        static void Finish(Exception failure)
        {
            if(finishing)return;finishing=true;
            if(failure!=null)report.failure=failure.ToString();
            CampaignSaveStore.TestDirectory=previousSaveDirectory;
            report.engineErrors=errors.ToArray();report.success=report.failure==""&&report.captureComplete&&report.layoutChecksPassed&&errors.Count==0;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));Application.logMessageReceived-=OnLog;
            Debug.Log("BUSINESS_LAYOUT20 capture="+report.success+" layout="+report.layoutChecksPassed+" "+output);
            // Leave PlayMode cleanup to normal Unity teardown; no coroutine
            // waits for destruction of its own host, and no real save is opened.
            int exit=report.success?0:1;
            EditorApplication.delayCall+=()=>EditorApplication.Exit(exit);
        }
    }
}
