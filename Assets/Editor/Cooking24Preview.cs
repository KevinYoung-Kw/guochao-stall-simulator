using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao.Editor
{
    // Controlled real-stage checks, with scripted controller/button calls.
    // Does not assert OS-pointer input or a complete campaign.
    [InitializeOnLoad]
    public static class Cooking24Preview
    {
        [Serializable] public sealed class Frame { public string name,path,text;public int width,height;public bool cardInside,criticalCentersClear,buttonsReachable,textFits; }
        [Serializable] public sealed class Report
        {
            public string scope="Graphical Editor PlayMode; real session, stage and actor contacts; scripted commands; isolated save; not full campaign or OS pointer input.";
            public bool success,emptySelection,pendingNotCommitted,eggContact,optionalRecipe,missingRecipeStock,secondPanIsolation,onlyEggCanCook,heatingStatus,coldCanStart,emptyFuelCorrect,saveUntouched;
            public string failure="";public string[] engineErrors;public List<Frame> frames=new List<Frame>();
        }
        const string Pending="Guochao.Cooking24.Pending",Output="Guochao.Cooking24.Output";
        const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static Feedback3CoroutineHost host;static string output,directory,priorSave;static Report report;
        static readonly List<string> errors=new List<string>();static bool finishing;static double started;
        static GameFlow flow;static BusinessController owner;static EditorWindow gameView;
        static Cooking24Preview(){EditorApplication.update-=Watch;EditorApplication.update+=Watch;}

        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--cooking24-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Use --cooking24-output <new report.json>");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Report already exists");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Watch()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
                report=new Report();finishing=false;errors.Clear();started=EditorApplication.timeSinceStartup;priorSave=CampaignSaveStore.TestDirectory;
                CampaignSaveStore.TestDirectory=Path.Combine(directory,"isolated-save");Application.logMessageReceived+=OnLog;
                host=new GameObject("Cooking24_TestHost").AddComponent<Feedback3CoroutineHost>();host.Launch(Flatten(Execute()),()=>Finish(null),Finish);
            }
            if(!finishing&&host!=null&&host.IsRunning&&EditorApplication.timeSinceStartup-started>180){host.Abort();Finish(new TimeoutException("Cooking UI check exceeded 180 seconds"));}
        }
        static void Need(bool value,string detail){if(!value)throw new InvalidOperationException(detail);}
        static void OnLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(type+": "+message+"\n"+trace);}
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try{while(stack.Count>0){var top=stack.Peek();if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator child){stack.Push(child);continue;}yield return top.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        static IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Graphical Editor required");
            Need(GameFlow.Instance==null,"Expected an isolated scene");yield return Resize(1280,720);
            var flowObject=new GameObject("Cooking24_IsolatedFlow");flow=flowObject.AddComponent<GameFlow>();flow.enabled=false;
            var events=new GameObject("Cooking24_EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));events.GetComponent<StandaloneInputModule>().enabled=false;
            var setup=BusinessSetup.MidgamePractice();setup.inventory=new Dictionary<string,int>{{"egg",4},{"rice",1},{"seasoning",10},{"P05",8},{"P04",8}};
            setup.equipment=new HashSet<string>{"B04","V01","V05","U01","U04"};setup.stoveLoaded=true;setup.gasPoints=45;
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();Need(session.Open(),"Could not start fixture");
            owner=BusinessController.BeginRestored(session.CaptureSave(),null,null,null);typeof(GameFlow).GetField("business",Hidden).SetValue(flow,owner);
            owner.enabled=false;owner.InputHandler.enabled=false;Need(owner.CanInteract,"Controller not ready");
            owner.UseCookware(0);owner.HUD.Refresh(false);yield return Settle(.3f);
            RectTransform card=Panel();report.emptySelection=card!=null&&card.gameObject.activeInHierarchy;
            Need(report.emptySelection,"Selecting an empty pan did not retain its card");
            Need(CardText(card).Contains("空锅")||CardText(card).Contains("先放"),"Empty pan has no next-step guidance");
            yield return Capture("empty",card);

            int originalEgg=owner.Session.Count("egg");Need(owner.UseIngredientAtLane(0,"egg"),"Could not enqueue egg");
            owner.HUD.Refresh(false);card=Panel();
            report.pendingNotCommitted=owner.Session.Lanes[0].freeBatch==null&&owner.Session.Count("egg")==originalEgg;
            Need(report.pendingNotCommitted,"Egg was counted before actor contact");
            Need(VisibleIngredientCells(card)==0&&!CardText(card).Contains("蛋菜 ×1"),"Card showed uncommitted egg");
            yield return Capture("egg-in-transit",card);
            yield return Until(()=>Units(0,"egg")==1,45,"Egg never reached pan at the actor contact");
            owner.HUD.Refresh(false);card=Panel();string oneEgg=CardText(card);
            report.eggContact=owner.Session.Count("egg")==originalEgg-1&&oneEgg.Contains("蛋");
            Need(report.eggContact,"Real egg or count absent after contact");
            report.optionalRecipe=oneEgg.Contains("参考")||oneEgg.Contains("可选")||oneEgg.Contains("米");
            Need(report.optionalRecipe,"No optional recipe or missing ingredient guidance");yield return Capture("one-egg",card);
            owner.Session.Inventory["rice"]=0;owner.HUD.Refresh(false);card=Panel();
            report.missingRecipeStock=CardText(card).Contains("米面")&&CardText(card).Contains("缺货");
            Need(report.missingRecipeStock,"Missing rice not disclosed in optional recipe hint");yield return Capture("one-egg-missing-rice",card);
            owner.Session.Inventory["rice"]=1;owner.HUD.Refresh(false);

            Need(owner.UseIngredientAtLane(0,"rice"),"Could not enqueue rice");
            Need(Units(0,"rice")==0&&owner.Session.Count("rice")==1,"Rice counted before contact");
            yield return Until(()=>Units(0,"rice")==1,45,"Rice never reached pan at the actor contact");
            owner.HUD.Refresh(false);card=Panel();string mixed=CardText(card);
            Need(Units(0,"egg")==1&&Units(0,"rice")==1&&owner.Session.Count("rice")==0&&mixed.Contains("蛋")&&mixed.Contains("米"),"Pot did not show both actual inputs");
            yield return Capture("egg-and-rice",card);

            owner.UseCookware(1);owner.HUD.Refresh(false);yield return Settle(.2f);
            string second=CardText(Panel());report.secondPanIsolation=second.Contains("2号锅")&&VisibleIngredientCells(Panel())==0&&!second.Contains("蛋菜 ×1");
            Need(report.secondPanIsolation,"Second empty pan inherited first pan contents");
            Need(owner.UseIngredientAtLane(1,"egg"),"Could not enqueue egg in second pan");
            yield return Until(()=>Units(1,"egg")==1,45,"Second-pan egg never contacted");
            owner.HUD.Refresh(false);yield return Settle(.2f);card=Panel();yield return Capture("one-egg-no-rice",card);
            yield return Resize(1920,1080);owner.HUD.Refresh(false);yield return Settle(.4f);yield return Capture("one-egg-1920",Panel());
            yield return Resize(2560,1080);owner.HUD.Refresh(false);yield return Settle(.4f);yield return Capture("one-egg-ultrawide",Panel());
            yield return Resize(1280,720);owner.HUD.Refresh(false);yield return Settle(.2f);
            card=Panel();ClickButton(card,"煎");owner.HUD.Refresh(false);yield return Settle(.2f);
            Need(owner.Session.Lanes[1].freeBatch.method==FreeKitchenRules.MethodGriddle,"Method button did not select griddle");
            card=Panel();ClickButton(card,"开火");
            yield return Until(()=>owner.Session.Lanes[1].step==KitchenStep.Heating,45,"One egg did not begin cooking");
            report.onlyEggCanCook=Units(1,"egg")==1&&owner.Session.Count("rice")==0;
            owner.HUD.Refresh(false);card=Panel();report.heatingStatus=CardText(card).Contains("加热")||CardText(card).Contains("受热")||CardText(card).Contains("炒");
            Need(report.onlyEggCanCook&&report.heatingStatus,"Single-egg cook or heating state incorrect");yield return Capture("heating",card);
            var coldSetup=BusinessSetup.MidgamePractice();coldSetup.gasPoints=0;coldSetup.inventory=new Dictionary<string,int>{{"fruit",1}};coldSetup.equipment=new HashSet<string>{"V15","U13"};
            var cold=new BusinessSession(coldSetup);Need(cold.Open()&&cold.AddFreeIngredient(0,"fruit"),"Cold fixture could not accept fruit");
            var coldGuide=FreePanGuide.Read(cold,0);report.coldCanStart=coldGuide.canStart&&coldGuide.next.Contains("调制")&&cold.StartFreeCooking(0)&&cold.Lanes[0].step==KitchenStep.Utensil;
            var coldAfterStart=FreePanGuide.Read(cold,0);report.coldCanStart=report.coldCanStart&&coldAfterStart.state=="待调制"&&coldAfterStart.next.Contains("调制");
            Need(report.coldCanStart,"Cold drink was incorrectly shown as needing a flame");
            var fuelSetup=BusinessSetup.MidgamePractice();fuelSetup.gasPoints=0;fuelSetup.inventory=new Dictionary<string,int>{{"egg",1}};fuelSetup.equipment=new HashSet<string>{"B04","V05","U04"};
            var fuel=new BusinessSession(fuelSetup);Need(fuel.Open()&&fuel.AddFreeIngredient(0,"egg"),"No-fuel fixture could not accept egg");
            var fuelGuide=FreePanGuide.Read(fuel,0);report.emptyFuelCorrect=!fuelGuide.canStart&&fuelGuide.next.Contains("煤气")&&!fuel.StartFreeCooking(0);
            Need(report.emptyFuelCorrect,"Empty fuel was incorrectly shown as ready to cook");
            report.saveUntouched=!Directory.Exists(CampaignSaveStore.TestDirectory)||Directory.GetFiles(CampaignSaveStore.TestDirectory,"*",SearchOption.AllDirectories).Length==0;
            Need(report.saveUntouched,"Unexpected save written");
        }
        static int Units(int lane,string id){var b=owner.Session.Lanes[lane].freeBatch;if(b?.inputs==null)return 0;return b.inputs.Where(x=>x!=null&&x.itemId==id).Sum(x=>x.units);}
        static IEnumerator Until(Func<bool> condition,float seconds,string detail)
        {
            int max=Mathf.CeilToInt(seconds*60);for(int i=0;i<max&&!condition();i++)
            {owner.AdvanceSimulation(1f/60f);if(i%4==0)yield return null;}
            Need(condition(),detail);
        }
        static RectTransform Panel()
        {
            var hud=owner.HUD.GetComponentInChildren<BusinessWorldHUD>(true);Need(hud!=null,"Actual world HUD missing");
            return hud.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(x=>x.name=="Lane operation card");
        }
        static int VisibleIngredientCells(RectTransform panel)=>panel.GetComponentsInChildren<RectTransform>(false).Count(x=>x.name.StartsWith("Landed ingredient "));
        static string CardText(RectTransform panel)=>panel==null?"":string.Join(" | ",panel.GetComponentsInChildren<Text>(false).Select(x=>x.text).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray());
        static void ClickButton(RectTransform panel,string label)
        {
            Button b=panel.GetComponentsInChildren<Button>(false).FirstOrDefault(x=>x.GetComponentInChildren<Text>()?.text==label);
            Need(b!=null&&b.interactable,"Missing enabled button: "+label+" in "+CardText(panel));b.onClick.Invoke();
        }
        static Rect ScreenRect(RectTransform root)
        {
            var p=new Vector3[4];root.GetWorldCorners(p);Vector2 a=RectTransformUtility.WorldToScreenPoint(null,p[0]),b=RectTransformUtility.WorldToScreenPoint(null,p[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        static IEnumerator Capture(string name,RectTransform card)
        {
            Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();Rect r=ScreenRect(card);
            var f=new Frame{name=name,path=Path.Combine(directory,name+".png"),width=Screen.width,height=Screen.height,text=CardText(card),
                cardInside=r.xMin>=0&&r.yMin>=0&&r.xMax<=Screen.width&&r.yMax<=Screen.height,criticalCentersClear=true,buttonsReachable=true,textFits=true};
            Physics.SyncTransforms();foreach(var target in owner.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target.HitCollider==null||(target.kind!=StallInteractionKind.Cookware&&target.kind!=StallInteractionKind.Ingredient))continue;
                Vector3 p=owner.Stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                if(p.z>0&&p.x>=0&&p.y>=0&&p.x<Screen.width&&p.y<Screen.height&&r.Contains(p))f.criticalCentersClear=false;
            }
            foreach(Button b in card.GetComponentsInChildren<Button>(false))
            {
                if(!b.interactable)continue;Rect br=ScreenRect((RectTransform)b.transform);
                var data=new PointerEventData(EventSystem.current){position=br.center};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
                if(!hits.Any(x=>x.gameObject==b.gameObject||x.gameObject.transform.IsChildOf(b.transform)))f.buttonsReachable=false;
            }
            foreach(Text t in card.GetComponentsInChildren<Text>(false))
            {
                if(string.IsNullOrWhiteSpace(t.text))continue;
                bool isIngredient=t.transform.parent!=null&&t.transform.parent.name.StartsWith("Landed ingredient ");
                if(t.preferredHeight>t.rectTransform.rect.height+1f||(isIngredient&&t.preferredWidth>t.rectTransform.rect.width+1f))
                {f.textFits=false;Debug.LogWarning("Card text overflows: "+t.text+" preferred="+t.preferredWidth+"x"+t.preferredHeight+" rect="+t.rectTransform.rect);}
            }
            Need(f.cardInside&&f.criticalCentersClear&&f.buttonsReachable&&f.textFits,"Card outside screen, occludes a pan/ingredient, has clipped text, or button cannot receive raycasts: "+name);
            ScreenCapture.CaptureScreenshot(f.path);double deadline=Time.realtimeSinceStartupAsDouble+10;
            while(!File.Exists(f.path)||new FileInfo(f.path).Length==0){Need(Time.realtimeSinceStartupAsDouble<deadline,"Screenshot not created");yield return null;}
            report.frames.Add(f);
        }
        static IEnumerator Settle(float seconds){double until=Time.realtimeSinceStartupAsDouble+seconds;while(Time.realtimeSinceStartupAsDouble<until){gameView?.Repaint();yield return null;}Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();}
        static Type EditorType(string name){var type=Type.GetType(name+", UnityEditor");if(type!=null)return type;foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){type=assembly.GetType(name,false);if(type!=null)return type;}return null;}
        static IEnumerator Resize(int width,int height)
        {
            Type viewType=EditorType("UnityEditor.GameView"),sizesType=EditorType("UnityEditor.GameViewSizes"),sizeType=EditorType("UnityEditor.GameViewSize"),sizeKind=EditorType("UnityEditor.GameViewSizeType");
            Need(viewType!=null&&sizesType!=null&&sizeType!=null&&sizeKind!=null,"GameView API unavailable");gameView=EditorWindow.GetWindow(viewType);gameView.Show();gameView.Focus();
            var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);object sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
            object groupType=sizesType.GetProperty("currentGroupType",Hidden).GetValue(sizes),group=sizesType.GetMethod("GetGroup",Hidden).Invoke(sizes,new[]{groupType});
            Type groupClass=group.GetType();int builtin=(int)groupClass.GetMethod("GetBuiltinCount",Hidden).Invoke(group,null),custom=(int)groupClass.GetMethod("GetCustomCount",Hidden).Invoke(group,null),selected=-1;
            for(int i=0;i<builtin+custom;i++){object candidate=groupClass.GetMethod("GetGameViewSize",Hidden).Invoke(group,new object[]{i});int w=(int)sizeType.GetProperty("width",Hidden).GetValue(candidate),h=(int)sizeType.GetProperty("height",Hidden).GetValue(candidate);if(w==width&&h==height&&sizeType.GetProperty("sizeType",Hidden).GetValue(candidate).ToString()=="FixedResolution"){selected=i;break;}}
            if(selected<0){object size=Activator.CreateInstance(sizeType,Hidden,null,new object[]{Enum.Parse(sizeKind,"FixedResolution"),width,height,"Cooking24 "+width+"x"+height},null);groupClass.GetMethod("AddCustomSize",Hidden).Invoke(group,new[]{size});selected=builtin+custom;}
            viewType.GetProperty("selectedSizeIndex",Hidden).SetValue(gameView,selected);var low=viewType.GetProperty("lowResolutionForAspectRatios",Hidden);if(low!=null&&low.CanWrite)low.SetValue(gameView,false);
            double deadline=Time.realtimeSinceStartupAsDouble+10;while(Screen.width!=width||Screen.height!=height){Need(Time.realtimeSinceStartupAsDouble<deadline,"Resolution mismatch");gameView.Repaint();yield return null;}yield return null;yield return null;
        }
        static void Finish(Exception failure)
        {
            if(finishing)return;finishing=true;if(failure!=null)report.failure=failure.ToString();report.engineErrors=errors.ToArray();
            report.success=report.failure==""&&errors.Count==0&&report.emptySelection&&report.pendingNotCommitted&&report.eggContact&&report.optionalRecipe&&report.missingRecipeStock&&report.secondPanIsolation&&report.onlyEggCanCook&&report.heatingStatus&&report.coldCanStart&&report.emptyFuelCorrect&&report.saveUntouched&&report.frames.Count==9;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));CampaignSaveStore.TestDirectory=priorSave;Application.logMessageReceived-=OnLog;
            EditorApplication.delayCall+=()=>EditorApplication.Exit(report.success?0:1);
        }
    }
}
