using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Guochao.Editor
{
    // Real stage, HUD, scene cameras and picking. Uses real model events and
    // application Activate callback, not an OS-level mouse or full-day test.
    [InitializeOnLoad] public static class BikeBlock24VisualValidation
    {
        [Serializable] private sealed class Report
        {
            public bool success,twoBikeTargets,bikesInFrame,bikesNotUnderUI,eachBikePicked,existingTargetsPreserved,firstClickRemovesOnlyTarget,secondClickClears;
            public int originalTargets,preservedTargets,arrivedCustomers,pickableCustomerTargets;
            public string scope="Unity graphical PlayMode; real stage/HUD raycast, scripted Activate and natural-session event. Not OS hardware input or a full shift.";
            public string failure="",screenshot="";
            public string[] issues=Array.Empty<string>(),engineErrors=Array.Empty<string>();
        }
        private const string Pending="Guochao.Bike24.Pending",Output="Guochao.Bike24.Output";
        private static readonly List<string> errors=new List<string>(),issues=new List<string>();
        private static GameObject root;
        private static Feedback3CoroutineHost host;
        private static Report report;
        private static string output;
        private static double started;
        static BikeBlock24VisualValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--bike24-visual-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --bike24-visual-output <new path>");
            string path=Path.GetFullPath(args[at+1]);if(File.Exists(path))throw new IOException("Refusing to overwrite evidence");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        private static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();issues.Clear();started=EditorApplication.timeSinceStartup;Application.logMessageReceived+=Log;
                host=new GameObject("BikeVisualValidationHost").AddComponent<Feedback3CoroutineHost>();
                host.Launch(Execute(),()=>Finish(""),error=>Finish(error.ToString()));
            }
            if(host!=null&&host.IsRunning&&EditorApplication.timeSinceStartup-started>240){host.Abort();Finish("Visual validation timed out");}
        }
        private static IEnumerator Execute()
        {
            Need(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Graphical editor required");
            var session=BikeBlock24Validation.MakeSession(12017);BikeBlock24Validation.Sale(session);
            root=new GameObject("BikeVisualValidation");
            var controller=root.AddComponent<BusinessController>();controller.enabled=false;
            var stage=root.AddComponent<StreetStallStage>();stage.Initialize(session.Setup,null);stage.Configure(session);
            typeof(BusinessController).GetProperty("Session").SetValue(controller,session);
            typeof(BusinessController).GetProperty("Stage").SetValue(controller,stage);
            var input=root.AddComponent<StallInteractionInput>();input.Initialize(controller);input.enabled=false;
            typeof(BusinessController).GetProperty("InputHandler").SetValue(controller,input);
            if(EventSystem.current==null){var events=new GameObject("Bike_EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));events.transform.SetParent(root.transform);}
            var hud=root.AddComponent<BusinessHUD>();typeof(BusinessController).GetProperty("HUD").SetValue(controller,hud);
            hud.Initialize(controller,session);hud.ShowBusiness();
            stage.CustomerArrived+=id=>session.MarkCustomerArrived(id);
            MethodInfo resize=typeof(Map19VisualPreview).GetMethod("Resize",BindingFlags.Static|BindingFlags.NonPublic);
            Need(resize!=null,"Could not access GameView sizing helper");
            IEnumerator sizing=(IEnumerator)resize.Invoke(null,new object[]{1280,720});while(sizing.MoveNext())yield return sizing.Current;
            for(int i=0;i<5;i++){stage.Present(session,0,false);hud.Refresh(false);Canvas.ForceUpdateCanvases();yield return null;}
            Physics.SyncTransforms();
            var originallyPickable=new List<StallInteractionTarget>();
            foreach(var target in root.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target.kind==StallInteractionKind.Bicycle||target.kind==StallInteractionKind.Order||target.HitCollider==null)continue;
                Vector3 p=stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                if(InFrame(p)&&!input.IsScreenPointOverUI(p)&&input.PickScreenPoint(p)==target)originallyPickable.Add(target);
            }
            report.originalTargets=originallyPickable.Count;Need(report.originalTargets>0,"No existing targets were pickable in baseline");
            // Let customers use their actual navigation and arrival callbacks.
            // Wait until the real event overlaps an arrived customer, without
            // forcing arrival or disabling normal timers/collisions.
            for(int i=0;i<5000&&!session.Finished;i++)
            {
                controller.AdvanceSimulation(.05f);
                report.arrivedCustomers=0;
                foreach(var order in session.Orders)if(order.arrived&&!order.departed&&!order.served)report.arrivedCustomers++;
                if(session.BikeBlocked&&report.arrivedCustomers>0)break;
                if(i%100==0)yield return null;
            }
            Need(session.BikeBlocked&&report.arrivedCustomers>0,"No natural bike event with an arrived customer for known seed");
            for(int i=0;i<5;i++){stage.Present(session,0,false);hud.Refresh(false);Canvas.ForceUpdateCanvases();yield return null;}
            Physics.SyncTransforms();
            var bikes=new List<StallInteractionTarget>();
            foreach(var target in root.GetComponentsInChildren<StallInteractionTarget>(false))if(target.kind==StallInteractionKind.Bicycle)bikes.Add(target);
            report.twoBikeTargets=bikes.Count==2;
            report.bikesInFrame=report.bikesNotUnderUI=report.eachBikePicked=true;
            foreach(var target in bikes)
            {
                Vector3 p=stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                bool frame=InFrame(p),under=input.IsScreenPointOverUI(p),picked=input.PickScreenPoint(p)==target;
                report.bikesInFrame&=frame;report.bikesNotUnderUI&=!under;report.eachBikePicked&=picked;
                if(!frame||under||!picked)issues.Add("Bike "+target.lane+" point="+p+" inFrame="+frame+" underUI="+under+" picked="+(input.PickScreenPoint(p)?.name??"null"));
            }
            foreach(var target in root.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target.kind!=StallInteractionKind.Order||target.HitCollider==null)continue;
                Vector3 p=stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                if(InFrame(p)&&!input.IsScreenPointOverUI(p)&&input.PickScreenPoint(p)==target)report.pickableCustomerTargets++;
            }
            if(report.pickableCustomerTargets==0)issues.Add("No customer could be picked while bicycles were present");
            foreach(var target in originallyPickable)
            {
                if(!target||!target.gameObject.activeInHierarchy)continue;
                Vector3 p=stage.View.WorldToScreenPoint(target.HitCollider.bounds.center);
                if(InFrame(p)&&!input.IsScreenPointOverUI(p)&&input.PickScreenPoint(p)==target)report.preservedTargets++;
                else issues.Add("Previously pickable target obstructed: "+target.kind+" "+target.itemId+" at "+p);
            }
            report.existingTargetsPreserved=report.preservedTargets==report.originalTargets;
            string dir=Path.GetDirectoryName(output);Directory.CreateDirectory(dir);report.screenshot=Path.Combine(dir,"bike-event-1280x720.png");
            ScreenCapture.CaptureScreenshot(report.screenshot);double deadline=EditorApplication.timeSinceStartup+20;
            while(!File.Exists(report.screenshot)||new FileInfo(report.screenshot).Length==0){Need(EditorApplication.timeSinceStartup<deadline,"Screenshot not saved");yield return null;}
            Need(report.twoBikeTargets&&report.bikesInFrame&&report.bikesNotUnderUI&&report.eachBikePicked&&report.existingTargetsPreserved,"Bike hitboxes overlap or cannot be picked");
            MethodInfo activate=typeof(StallInteractionInput).GetMethod("Activate",BindingFlags.Instance|BindingFlags.NonPublic);Need(activate!=null,"Input activation method unavailable");
            activate.Invoke(input,new object[]{bikes[0]});report.firstClickRemovesOnlyTarget=session.BikeVisibleMask==(3&~(1<<bikes[0].lane));
            activate.Invoke(input,new object[]{bikes[1]});report.secondClickClears=!session.BikeBlocked;
            Need(report.firstClickRemovesOnlyTarget&&report.secondClickClears,"Real input route did not clear two separate bikes");
        }
        private static bool InFrame(Vector3 p)=>p.z>0&&p.x>0&&p.x<Screen.width&&p.y>0&&p.y<Screen.height;
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private static void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
        private static void Finish(string failure)
        {
            Application.logMessageReceived-=Log;report.failure=failure;report.issues=issues.ToArray();report.engineErrors=errors.ToArray();
            report.success=failure==""&&issues.Count==0&&errors.Count==0&&report.secondClickClears;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(root)UnityEngine.Object.DestroyImmediate(root);if(host)UnityEngine.Object.DestroyImmediate(host.gameObject);
            EditorApplication.Exit(report.success?0:1);
        }
    }
}
