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
    [InitializeOnLoad] public static class StallProgression24UiValidation
    {
        [Serializable] public sealed class Frame
        {
            public int width,height;public bool entryClickable,upgradeClickable,closeRestoresFocus,savePersisted;
            public string rayDetail="",nightScreenshot="",growthScreenshot="";public List<string> clipped=new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public bool success;public string failure="",scope="Real graphical Unity Play Mode, actual UI callbacks and graphic raycasts; staged campaign and no physical mouse input.";
            public List<Frame> frames=new List<Frame>();public List<string> engineErrors=new List<string>();
        }
        const string Pending="Guochao.Progression24.UI.Pending",Output="Guochao.Progression24.UI.Output";
        static Report report;static string output,directory;static Feedback3CoroutineHost host;
        static StallProgression24UiValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--progression24-ui-output");
            if(i<0||i+1>=args.Length)throw new ArgumentException("--progression24-ui-output required");
            string path=Path.GetFullPath(args[i+1]);if(File.Exists(path))throw new IOException("Preserve previous report");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");directory=Path.GetDirectoryName(output);
            Directory.CreateDirectory(directory);report=new Report();Application.logMessageReceived+=Log;
            host=new GameObject("Progression UI Test").AddComponent<Feedback3CoroutineHost>();
            host.Launch(Flatten(Execute()),()=>Finish(null),Finish);
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.engineErrors.Add(text+"\n"+trace);}
        static void Finish(Exception e)
        {
            if(e!=null)report.failure=e.ToString();Application.logMessageReceived-=Log;
            report.success=report.failure==""&&report.engineErrors.Count==0&&report.frames.Count==3&&report.frames.All(f=>f.entryClickable&&f.upgradeClickable&&f.closeRestoresFocus&&f.savePersisted&&f.clipped.Count==0);
            File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);
        }
        static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try{while(stack.Count>0){var top=stack.Peek();if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator nested){stack.Push(nested);continue;}yield return top.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        static IEnumerator Resize(int w,int h)
        {
            var method=typeof(BusinessLayout22Preview).GetMethod("Resize",BindingFlags.Static|BindingFlags.NonPublic);
            Need(method!=null,"GameView resize helper missing");yield return (IEnumerator)method.Invoke(null,new object[]{w,h});
        }
        static bool RayHits(Button b,Frame f)
        {
            Canvas.ForceUpdateCanvases();var at=RectTransformUtility.WorldToScreenPoint(null,b.transform.position);
            var eventData=new PointerEventData(EventSystem.current){position=at};var results=new List<RaycastResult>();EventSystem.current.RaycastAll(eventData,results);
            f.rayDetail="screen="+Screen.width+"x"+Screen.height+" position="+at+" results="+string.Join(",",results.Select(r=>r.gameObject.name).ToArray());
            return results.Count>0&&results[0].gameObject.GetComponentInParent<Button>()==b;
        }
        static IEnumerator Capture(string path)
        {
            yield return new WaitForSecondsRealtime(.4f);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
            Need(!File.Exists(path),"Screenshot exists");ScreenCapture.CaptureScreenshot(path);
            double until=Time.realtimeSinceStartupAsDouble+10;while(!File.Exists(path)&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Need(File.Exists(path),"Screenshot not created");
        }
        static IEnumerator Execute()
        {
            Need(!Application.isBatchMode,"Graphical Editor required");
            var camera=new GameObject("Fixture background camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.16f,.14f);camera.cullingMask=0;
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768)})
            {
                yield return Resize(size.x,size.y);var frame=new Frame{width=Screen.width,height=Screen.height};report.frames.Add(frame);
                var profile=new RunProfile{careerId="career1",selectedTraits=Array.Empty<string>()};var run=new RunState(4,profile);var campaign=CampaignState.New(profile,run);run.SetBusinessPhase(GamePhase.Night);
                campaign.progression=StallProgressionState.CreateDefault();campaign.progression.level=2;campaign.progression.availablePoints=1;
                CampaignSaveStore.TestDirectory=Path.Combine(directory,"saves-"+size.x);Need(!Directory.Exists(CampaignSaveStore.TestDirectory),"Test saves exist");
                var obj=new GameObject("UI flow");obj.SetActive(false);var flow=obj.AddComponent<GameFlow>();
                typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);
                typeof(GameFlow).GetProperty("Campaign").GetSetMethod(true).Invoke(flow,new object[]{campaign});
                NightHubUI.Open(flow);yield return null;Canvas.ForceUpdateCanvases();
                var growth=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x=>x.name.StartsWith("摊主成长"));
                Need(growth!=null&&growth.interactable,"Growth entry missing");frame.entryClickable=RayHits(growth,frame);
                frame.nightScreenshot=Path.Combine(directory,"night-"+size.x+"x"+size.y+".png");yield return Capture(frame.nightScreenshot);
                growth.onClick.Invoke();yield return null;Need(StallProgressionUI.IsOpen,"Growth page did not open");Canvas.ForceUpdateCanvases();
                var page=GameObject.Find("Night owner progression");Need(page!=null,"Growth page missing");
                foreach(var label in page.GetComponentsInChildren<Text>())
                    if(!string.IsNullOrWhiteSpace(label.text)&&label.preferredHeight>label.rectTransform.rect.height+2.5f)frame.clipped.Add(label.text.Substring(0,Math.Min(24,label.text.Length)));
                var upgrade=page.GetComponentsInChildren<Button>().FirstOrDefault(x=>x.name=="UpgradeAmiable");Need(upgrade!=null&&upgrade.interactable,"Unlocked talent missing");
                frame.upgradeClickable=RayHits(upgrade,frame);frame.growthScreenshot=Path.Combine(directory,"growth-"+size.x+"x"+size.y+".png");yield return Capture(frame.growthScreenshot);
                upgrade.onClick.Invoke();Need(StallProgressionRules.AmiableRank(campaign)==1&&campaign.progression.availablePoints==0,"Talent click failed");
                frame.savePersisted=CampaignSaveStore.Load(out var saved,out _)&&StallProgressionRules.AmiableRank(saved.campaign)==1;
                var close=page.GetComponentsInChildren<Button>().First(x=>x.name=="CloseProgression");close.onClick.Invoke();
                frame.closeRestoresFocus=!StallProgressionUI.IsOpen&&NightHubUI.IsOpen&&growth.interactable;
                NightHubUI.Close();UnityEngine.Object.Destroy(obj);yield return null;
            }
        }
    }
}
