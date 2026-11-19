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
    // Editor-only controlled visual fixture. Does not replay human shopping,
    // run a campaign, or save to the player's real save path.
    // Graphical Editor (no -batchmode/-nographics/-quit):
    // -executeMethod Guochao.Editor.MealPlan18VisualPreview.Run
    // --meal-plan18-output /absolute/new-directory/report.json
    // Requires final PLAN18_DATA and PLAN18_UI to be integrated first.
    [InitializeOnLoad]
    public static class MealPlan18VisualPreview
    {
        const string Pending="Guochao.MealPlan18.Pending",Output="Guochao.MealPlan18.Output";
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        [Serializable] public sealed class Frame
        {
            public string name,screenshot,phase,pinnedProductId;
            public int width,height,plannedDishes;
            public bool exactResolution,panelVisible,paused,proceedInteractable,mapOpen,mapCanvasVisible,cardInsideScreen;
            public string[] text;
        }
        [Serializable] public sealed class Check { public string name,detail; public bool passed; }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.meal-plan-visual.v18.1",failure="",isolatedSaveDirectory;
            public string scope="Controlled graphical Editor GameView: actual GameFlow.Initialize, real Home/Market WorldRuntime, ArtHUD and MealPlanPanel. GameFlow.Update disabled before Start to avoid character creation/autosave and keep the timer fixed. Market fixture uses Run.Act(HomeExit,true) then the real CreateWorld method, without claiming human travel or interaction-range acceptance. UI clicks use EventSystem raycasts and production click handlers. Escape tests the production TryCloseActive route, not OS keyboard injection. Screenshots require separate human visual review.";
            public bool success,captureComplete,controlledFixture=true,humanInput=false,formalSaveWrites=false,visualReviewComplete=false;
            public List<Frame> snapshots=new List<Frame>();
            public List<Check> checks=new List<Check>();
            public string[] engineErrors;
        }
        static readonly List<string> errors=new List<string>();
        static Report report;
        static string output,directory,previousSaveDirectory;
        static Feedback3CoroutineHost host;
        static GameFlow flow;
        static ArtHUD hud;
        static MealPlanPanel panel;
        static EditorWindow gameView;
        static double startedAt;
        static bool finishing;
        static MealPlan18VisualPreview(){EditorApplication.update-=Watch;EditorApplication.update+=Watch;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--meal-plan18-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --meal-plan18-output <new-directory/report.json>");
            string path=Path.GetFullPath(args[at+1]);
            if(!path.EndsWith(".json",StringComparison.OrdinalIgnoreCase))path=Path.Combine(path,"report.json");
            Need(!File.Exists(path),"Keep previous reports; choose a new output directory.");
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
                report.isolatedSaveDirectory=Path.Combine(directory,"isolated-meal-plan-save");
                CampaignSaveStore.TestDirectory=report.isolatedSaveDirectory;
                Application.logMessageReceived+=OnLog;
                host=new GameObject("MealPlan18_RealFrameHost").AddComponent<Feedback3CoroutineHost>();
                host.Launch(Flatten(Execute()),()=>Finish(null),Finish);
            }
            if(!finishing&&host!=null&&host.IsRunning&&EditorApplication.timeSinceStartup-startedAt>180)
            {host.Abort();Finish(new TimeoutException("Meal plan preview exceeded 180 real seconds."));}
        }
        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        static void Checkpoint(string name,bool value,string detail="")
        {report.checks.Add(new Check{name=name,passed=value,detail=detail});Need(value,name+": "+detail);}
        static void OnLog(string message,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(type+": "+message+"\n"+trace);}
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try {while(stack.Count>0){IEnumerator top=stack.Peek();if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator child){stack.Push(child);continue;}yield return top.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        static T Read<T>(object target,string name)
        {
            FieldInfo field=target.GetType().GetField(name,Hidden);Need(field!=null,"Preview adapter no longer matches actual field: "+name);
            return (T)field.GetValue(target);
        }
        static Transform Child(Transform parent,string name)
        {foreach(Transform t in parent.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;throw new InvalidOperationException("Actual UI node absent: "+name);}
        static string Goods()
        {
            var run=flow.Run;var stocks=new List<string>();foreach(string id in new SortedSet<string>(run.Supplies.Keys))stocks.Add(id+"="+run.Supplies[id]);
            return run.CashCents+"|"+run.SpentCents+"|"+run.GasPoints+"|"+run.CoalPoints+"|"+run.CartridgePoints+"|"+run.ElectricityPoints+"|"+
                string.Join(",",run.Cart)+"|"+string.Join(",",run.Cargo)+"|"+string.Join(",",run.HomeEquipment)+"|"+string.Join(",",run.LoadedEquipment)+"|"+string.Join(",",stocks);
        }
        static void Click(Vector2 point,string expectedAncestor)
        {
            Canvas.ForceUpdateCanvases();var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
            Need(hits.Count>0,"No real UI raycast hit at "+point);
            Transform picked=hits[0].gameObject.transform;bool expected=false;
            for(Transform t=picked;t!=null;t=t.parent)if(t.name==expectedAncestor)expected=true;
            Checkpoint("Raycast "+expectedAncestor,expected,TransformPath(picked));
            Need(ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,data,ExecuteEvents.pointerClickHandler)!=null,"No click handler for "+expectedAncestor);
        }
        static void ClickNode(string name)
        {var target=(RectTransform)Child(panel.transform,name);Click(ScreenRect(target).center,name);}
        static IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Graphical Editor required for actual overlay HUD capture.");
            Need(GameFlow.Instance==null,"Refusing to replace an existing flow.");
            yield return Resize(1920,1080);
            var events=new GameObject("MealPlan18_EventSystem").AddComponent<EventSystem>();
            events.gameObject.AddComponent<StandaloneInputModule>().enabled=false;
            var owner=new GameObject("MealPlan18_ControlledFlow");flow=owner.AddComponent<GameFlow>();flow.enabled=false;
            flow.Initialize();Need(flow.Run.Phase==GamePhase.Home&&flow.World!=null,"Real home initialization failed.");
            hud=owner.AddComponent<ArtHUD>();hud.SetGameplayVisible(true);
            yield return Settle(.5f);Need(hud.Ready,"Real ArtHUD is not ready.");
            panel=Read<MealPlanPanel>(hud,"mealPlan");Need(panel!=null,"ArtHUD did not attach its real MealPlanPanel.");
            if(flow.World.Player!=null)flow.World.Player.inputEnabled=false;
            string goods=Goods(),recipe=BusinessSession.Recipes[0].id;
            foreach(int width in new[]{1920,1280})
            {
                yield return Resize(width,width==1920?1080:720);
                Need(flow.Run.SetMealPlan(recipe,0),"Cannot clear known fixture plan.");
                panel.Show();yield return Settle(.3f);
                Checkpoint("Empty plan proceeds "+width,Read<Button>(panel,"proceedButton").interactable);
                yield return Capture("home-empty",width,width==1920?1080:720);
                // Blank title/card region, away from Close/rows/buttons.
                var card=Read<RectTransform>(panel,"card");
                Vector2 blank=RectTransformUtility.WorldToScreenPoint(null,card.TransformPoint(new Vector3(100,368,0)));
                Click(blank,"Meal plan card");Checkpoint("Blank card stays open "+width,panel.Visible&&flow.Run.Paused);
                ClickNode("Proceed");Checkpoint("Continue restores unpaused "+width,!panel.Visible&&!flow.Run.Paused);
                panel.Show();yield return Settle(.1f);ClickNode("Dish row "+recipe);yield return Settle(.2f);
                Checkpoint("Dish click selects one "+width,flow.Run.MealPlan.Count==1&&flow.Run.MealPlan[0].recipeId==recipe&&flow.Run.MealPlan[0].portions==1);
                yield return Capture("home-planned",width,width==1920?1080:720);
                Checkpoint("Escape route closes "+width,MealPlanPanel.TryCloseActive()&&!panel.Visible&&!flow.Run.Paused);
                flow.SetPaused(true);panel.Show();panel.Hide();Checkpoint("Prior pause preserved "+width,flow.Run.Paused);flow.SetPaused(false);
                panel.Show();yield return Settle(.1f);Click(new Vector2(5,Screen.height-5),"Shade");Checkpoint("Shade closes "+width,!panel.Visible&&!flow.Run.Paused);
                Checkpoint("Plan UI leaves money and goods untouched "+width,Goods()==goods);
            }
            // Controlled phase fixture: use native Run transition, real scene builder.
            Need(flow.Run.Act("HomeExit",true),"Native HomeExit fixture failed.");
            Need(flow.Run.Phase==GamePhase.Market,"Native HomeExit did not enter Market.");
            MethodInfo build=typeof(GameFlow).GetMethod("CreateWorld",Hidden);Need(build!=null,"Real CreateWorld adapter missing.");
            build.Invoke(flow,new object[]{null});yield return Settle(.8f);
            Need(flow.World!=null&&flow.Run.Phase==GamePhase.Market,"Real market build failed.");
            if(flow.World.Player!=null)flow.World.Player.inputEnabled=false;
            goods=Goods();
            foreach(int width in new[]{1920,1280})
            {
                yield return Resize(width,width==1920?1080:720);panel.Show();yield return Settle(.2f);
                yield return Capture("market-planned",width,width==1920?1080:720);
                MealPlanReport plan=MealPlan.Build(flow.Run);MealPlanLine target=null;
                foreach(var line in plan.lines)if((line.channel=="shelf"||line.channel=="counter")&&!string.IsNullOrEmpty(line.productId)){target=line;break;}
                Need(target!=null,"Real planned recipe has no purchasable market target.");
                // Scroll the real list, then click its production row through the raycaster.
                // The actual OnLineClicked handler calls TryPinProduct and hides the sheet.
                ScrollRect list=Read<ScrollRect>(panel,"rightScroll");
                int row=plan.lines.IndexOf(target);
                float viewportHeight=list.viewport!=null?list.viewport.rect.height:((RectTransform)list.transform).rect.height;
                float travel=Mathf.Max(0,list.content.rect.height-viewportHeight);
                float rowPitch=plan.lines.Count>0?list.content.rect.height/plan.lines.Count:0;
                list.verticalNormalizedPosition=travel>.01f?Mathf.Clamp01(1-row*rowPitch/travel):1;
                yield return Settle(.15f);ClickNode("List row "+target.key);yield return Settle(.4f);
                Checkpoint("Shopping row closes onto map "+width,!panel.Visible&&!flow.Run.Paused);
                MarketRecoveryMap map=flow.World.GetComponentInChildren<MarketRecoveryMap>(true);Need(map!=null,"Real market map not found.");
                Checkpoint("Map pin resolves "+width,Read<string>(map,"pinnedProductId")==target.productId&&Read<bool>(map,"open"),target.productId);
                Checkpoint("Pin leaves money and goods untouched "+width,Goods()==goods);
                yield return Capture("market-pinned",width,width==1920?1080:720);
            }
            report.captureComplete=report.snapshots.Count==8;
            Checkpoint("No formal save writes",!Directory.Exists(report.isolatedSaveDirectory)||Directory.GetFiles(report.isolatedSaveDirectory,"*",SearchOption.AllDirectories).Length==0);
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
                    new object[]{Enum.Parse(sizeKind,"FixedResolution"),width,height,"MealPlan18 "+width+"x"+height},null);
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

        static IEnumerator Capture(string state,int width,int height)
        {
            Canvas.ForceUpdateCanvases();yield return Settle(.2f);yield return new WaitForEndOfFrame();
            string name=state+"-"+width+"x"+height,path=Path.Combine(directory,name+".png");
            Need(!File.Exists(path),"Preserve prior screenshot: "+path);
            var text=new List<string>();foreach(Text t in panel.GetComponentsInChildren<Text>(false))if(!string.IsNullOrEmpty(t.text))text.Add(t.text);
            Rect card=ScreenRect(Read<RectTransform>(panel,"card"));
            var frame=new Frame{name=name,screenshot=path,phase=flow.Run.Phase.ToString(),width=Screen.width,height=Screen.height,
                exactResolution=Screen.width==width&&Screen.height==height,plannedDishes=flow.Run.MealPlan.Count,panelVisible=panel.Visible,paused=flow.Run.Paused,
                proceedInteractable=Read<Button>(panel,"proceedButton").interactable,cardInsideScreen=card.xMin>=-.5f&&card.yMin>=-.5f&&card.xMax<=Screen.width+.5f&&card.yMax<=Screen.height+.5f,text=text.ToArray()};
            var map=flow.World.GetComponentInChildren<MarketRecoveryMap>(true);
            if(map!=null){frame.pinnedProductId=Read<string>(map,"pinnedProductId");frame.mapOpen=Read<bool>(map,"open");frame.mapCanvasVisible=Read<Canvas>(map,"canvas").enabled;}
            Need(frame.exactResolution,"Real GameView size differs from requested size.");
            if(panel.Visible)Checkpoint("Card stays onscreen "+name,frame.cardInsideScreen);
            if(state=="market-pinned")Checkpoint("Actual pinned map visible "+name,frame.mapOpen&&frame.mapCanvasVisible&&!frame.panelVisible&&!frame.paused);
            ScreenCapture.CaptureScreenshot(path);double deadline=Time.realtimeSinceStartupAsDouble+8;
            while(!File.Exists(path)||new FileInfo(path).Length==0){Need(Time.realtimeSinceStartupAsDouble<deadline,"Screenshot not written: "+path);yield return null;}
            byte[] png=File.ReadAllBytes(path);Need(png.Length>=24,"Incomplete PNG header.");
            int w=(png[16]<<24)|(png[17]<<16)|(png[18]<<8)|png[19],h=(png[20]<<24)|(png[21]<<16)|(png[22]<<8)|png[23];
            Need(w==width&&h==height,"PNG pixel dimensions do not match requested GameView.");
            report.snapshots.Add(frame);File.WriteAllText(Path.Combine(directory,name+".json"),JsonUtility.ToJson(frame,true));
        }
        static void Finish(Exception failure)
        {
            if(finishing)return;finishing=true;if(failure!=null)report.failure=failure.ToString();
            CampaignSaveStore.TestDirectory=previousSaveDirectory;report.engineErrors=errors.ToArray();
            report.success=report.failure==""&&report.captureComplete&&errors.Count==0;
            foreach(Check check in report.checks)report.success&=check.passed;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));Application.logMessageReceived-=OnLog;
            Debug.Log("MEAL_PLAN18 capture="+report.success+" visualReview=false "+output);
            int exit=report.success?0:1;EditorApplication.delayCall+=()=>EditorApplication.Exit(exit);
        }
    }
}
