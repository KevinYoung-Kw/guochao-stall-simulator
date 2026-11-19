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
    // Read-only pointer-picking validation for the street-stall business
    // scene. Projects the real cookware / ingredient / seasoning / packaging /
    // customer anchors through the production stage camera and asserts that
    // the shared StallInteractionInput.PickScreenPoint entry classifies centre,
    // edge, occlusion, blank-space and neighbour-ownership samples correctly.
    // Drag-across-UI is verified through the production gating path
    // (EventSystem raycast + overUI nulling) with an explicit note that no
    // synthetic hardware input is injected. No economic transaction, movement
    // or state mutation is performed; fixtures are DestroyImmediate'd.
    [InitializeOnLoad] public static class StallPickingV15Validation
    {
        [Serializable] public sealed class Check { public string name,detail;public bool passed; }
        [Serializable] public sealed class Sample { public string target,kind;public float[] screen;public string pickedKind,pickedItem;public int pickedLane;public bool ok; }
        [Serializable] public sealed class Report
        {
            public string scope="Actual BusinessController + StreetStallStage orthographic camera; read-only StallInteractionInput.PickScreenPoint queries at projected object centres/bounds. Occlusion/blank/neighbour and UI-gating fixtures; no synthetic mouse input, no transactions.";
            public bool success;public string failure="";public int screenWidth,screenHeight;
            public List<Check> checks=new List<Check>();public List<Sample> samples=new List<Sample>();public string[] errors;
        }
        const string Pending="Guochao.StallPickingV15.Pending",Output="Guochao.StallPickingV15.Output";
        const float GlobalTimeoutSeconds=150f; // hard watchdog over the whole hosted run
        static readonly List<string> errors=new List<string>();
        static string output;static Report report;static BusinessController business;static GameObject eventSystem;
        static GameObject hostObject;static Feedback3CoroutineHost host;static float launchedAt;static bool gameViewReady;
        static StallPickingV15Validation(){EditorApplication.update+=Advance;}
        // Non-batch visual runs need a REAL Editor GameView before any UGUI
        // raycast: without one (batch mode / headless) Graphic.depth is not
        // authoritative and EventSystem raycasts are silently skipped, which
        // r4 misreported as a button-hit failure. Mirrors the proven
        // Feedback3Validation.EnsureEditorGameView implementation; only
        // creates/shows/focuses a window, touches no production UI.
        static bool EnsureEditorGameView()
        {
            if(gameViewReady)return true;
            try
            {
                var type=Type.GetType("UnityEditor.GameView, UnityEditor");
                if(type==null)return false;
                var window=EditorWindow.GetWindow(type);
                window.Focus();
                gameViewReady=true;
                return true;
            }
            catch{return false;}
        }
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--stall-picking-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --stall-picking-output");
            SessionState.SetString(Output,Path.GetFullPath(args[at+1]));SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        // Editor callback only boots the run and acts as a real-time watchdog.
        // It never advances the test enumerator itself: the suite runs inside
        // the production Feedback3CoroutineHost, so every yield (including
        // nested WaitUntil enumerators) waits real Player frames.
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();
                Application.logMessageReceived+=Log;
                hostObject=new GameObject("StallPickingV15CoroutineHost");
                host=hostObject.AddComponent<Feedback3CoroutineHost>();
                launchedAt=Time.time;
                host.Launch(Execute(),OnRunCompleted,OnRunError);
            }
            if(host!=null&&host.IsRunning&&Time.time-launchedAt>GlobalTimeoutSeconds)
            {
                host.Abort();
                report.failure="Global timeout "+GlobalTimeoutSeconds+"s exceeded; hosted validation aborted";
                Finish();
            }
        }
        static void OnRunCompleted(){Finish();}
        static void OnRunError(Exception error){report.failure=error.ToString();Finish();}
        static IEnumerator Execute()
        {
            var setup=BusinessSetup.MidgamePractice();
            setup.customerIntervalSeconds=2f;setup.patienceMinSeconds=300;setup.patienceMaxSeconds=320;setup.dayDuration=900;
            business=BusinessController.Begin(setup,phase=>{},cash=>{},()=>{});
            if(EventSystem.current==null){eventSystem=new GameObject("StallPickingEventSystem",typeof(EventSystem),typeof(StandaloneInputModule));}
            business.Open();
            float deadline=Time.time+8;while(!business.Session.Started&&Time.time<deadline)yield return null;
            Need(business.Session.Started,"Production Open did not start the session");
            Physics.SyncTransforms();
            report.screenWidth=Screen.width;report.screenHeight=Screen.height;
            // Bring up the real Editor GameView up front so later UGUI raycasts
            // run against an authoritative depth buffer. In batch/headless mode
            // there is no GameView: UI-raycast cases below explicitly report
            // PENDING instead of being faked or silently skipped.
            if(!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)EnsureEditorGameView();
            Case("cookware ingredient seasoning packaging anchors pick their own targets",AnchorOwnership);
            Case("neighbouring cookware and packages do not steal each other",NeighbourOwnership);
            Case("blank pavement and space behind solids pick nothing",BlankAndBehind);
            Case("solid occlusion hides a target placed behind the shop wall",OcclusionReject);
            Case("fine seasoning jar has a limited screen-pixel tolerance",PixelTolerance);
            Case("resolution coverage 1280x720 and 1920x1080",ResolutionCoverage);
            Case("HUD readout rect sits on its object; decorations never block, chrome and buttons do",HudRaycastLayout);
            // Let the real HUD canvas lay out a few genuine frames (the freshly
            // shown GameView needs 2-3 player frames before RectTransforms and
            // UGUI depths settle) before any UI raycast: the shared
            // IsScreenPointOverUI gate reads live positions. No EventSystem is
            // disabled or faked for this.
            for(int i=0;i<3;i++)yield return null;
            Canvas.ForceUpdateCanvases();
            Case("drag release over a real UI button is cancelled by production gating",DragAcrossUiGating);
            // Order targets exist as soon as a customer spawns and walks, but
            // the HUD bubble/readout only appear after Session.MarkArrived.
            // Wait for a genuinely arrived, still-valid order, then give the
            // late HUD layout a couple of real frames before measuring rects.
            yield return WaitUntil(()=>ArrivedOrderTarget()!=null,30,"No customer order target arrived at the counter");
            yield return null;yield return null;
            Canvas.ForceUpdateCanvases();
            Case("arrived customer order anchor picks the order target",OrderOwnership);
            yield return null;
        }
        // An order target only counts once its Session order has really
        // arrived (walking-in customers already carry 3D targets). Never
        // MarkArrived, teleport, or force-activate the bubble — the run just
        // waits within a bounded timeout.
        static StallInteractionTarget ArrivedOrderTarget()
        {
            foreach(var target in business.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target.kind!=StallInteractionKind.Order||!target.isActiveAndEnabled)continue;
                var order=business.Session.FindOrder(target.orderId);
                if(order!=null&&order.arrived&&!order.departed&&!order.served)return target;
            }
            return null;
        }
        static StallInteractionTarget OrderTarget()
        {
            foreach(var target in business.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
                if(target.kind==StallInteractionKind.Order&&target.isActiveAndEnabled)return target;
            return null;
        }
        static Vector2 Project(StallInteractionTarget target)
        {
            Vector3 p=business.Stage.View.WorldToScreenPoint(target.WorldAnchor);
            Need(p.z>0,target.kind+" anchor is behind the camera");
            return p;
        }
        static void SampleAt(StallInteractionTarget target,Vector2 screen)
        {
            var picked=business.InputHandler.PickScreenPoint(screen);
            report.samples.Add(new Sample{target=target.kind+"/"+target.itemId+"/"+target.lane,kind=target.kind.ToString(),
                screen=new[]{screen.x,screen.y},pickedKind=picked==null?"<null>":picked.kind.ToString(),pickedItem=picked==null?"":picked.itemId,pickedLane=picked==null?-1:picked.lane,
                ok=picked==target});
        }
        static void SampleAnchor(StallInteractionTarget target){SampleAt(target,Project(target));}
        static IEnumerable<StallInteractionTarget> Targets(StallInteractionKind kind)
        {
            foreach(var target in business.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
                if(target.kind==kind&&target.isActiveAndEnabled)yield return target;
        }
        static string AnchorOwnership()
        {
            int count=0;
            foreach(var target in Targets(StallInteractionKind.Cookware)){SampleAnchor(target);Need(report.samples[report.samples.Count-1].ok," cookware lane "+target.lane+" anchor did not pick its own pan");count++;}
            foreach(var target in Targets(StallInteractionKind.Ingredient)){SampleAnchor(target);Need(report.samples[report.samples.Count-1].ok," ingredient "+target.itemId+" anchor did not pick its tray");count++;}
            foreach(var target in Targets(StallInteractionKind.Seasoning)){SampleAnchor(target);Need(report.samples[report.samples.Count-1].ok," seasoning anchor did not pick the jar");count++;}
            foreach(var target in Targets(StallInteractionKind.Packaging)){SampleAnchor(target);Need(report.samples[report.samples.Count-1].ok," packaging "+target.itemId+" anchor did not pick its stack");count++;}
            Need(count>=6,"Expected cookware, ingredient, seasoning and packaging targets, got "+count);
            return count+" anchors at native "+Screen.width+"x"+Screen.height+" all resolve to their own targets";
        }
        static string NeighbourOwnership()
        {
            var cookware=new List<StallInteractionTarget>(Targets(StallInteractionKind.Cookware));
            cookware.Sort((a,b)=>a.lane.CompareTo(b.lane));
            for(int i=1;i<cookware.Count;i++)
            {
                SampleAt(cookware[i],Project(cookware[i])); // anchor still owns itself next to a neighbour
                Vector2 mid=(Project(cookware[i-1])+Project(cookware[i]))*.5f;
                var midPick=business.InputHandler.PickScreenPoint(mid);
                Need(midPick==null||midPick==cookware[i-1]||midPick==cookware[i],"Midpoint between pans picked a third target: "+(midPick==null?"<null>":midPick.kind+"/"+midPick.lane));
            }
            var packs=new List<StallInteractionTarget>(Targets(StallInteractionKind.Packaging));
            for(int i=1;i<packs.Count;i++)SampleAt(packs[i],Project(packs[i]));
            foreach(var sample in report.samples)Need(sample.ok,"Neighbour projected anchor picked the wrong target: "+sample.target+" -> "+sample.pickedKind+"/"+sample.pickedItem+"/"+sample.pickedLane);
            return cookware.Count+" pans and "+packs.Count+" packages keep ownership beside their neighbours";
        }
        static string BlankAndBehind()
        {
            // Pavement far in front of the stall: no trigger, no anchor nearby.
            Vector3 blank=business.Stage.View.WorldToScreenPoint(new Vector3(-3.4f,.05f,-2.6f));
            var picked=business.InputHandler.PickScreenPoint(blank);
            Need(picked==null,"Blank pavement picked "+(picked==null?"<null>":picked.kind+"/"+picked.itemId));
            // Shop shutter far behind the counter: solid facade in the way, no target.
            Vector3 shutter=business.Stage.View.WorldToScreenPoint(new Vector3(0f,1.4f,4.45f));
            picked=business.InputHandler.PickScreenPoint(shutter);
            Need(picked==null,"Point on the shop shutter picked "+(picked==null?"<null>":picked.kind+"/"+picked.itemId));
            return "blank pavement and occluded shutter both resolve to null";
        }
        static string OcclusionReject()
        {
            // Test-only target with a large trigger box placed behind the shop
            // facade solid. Production code must refuse it through the wall.
            var ghost=new GameObject("OcclusionProbeTarget",typeof(BoxCollider),typeof(StallInteractionTarget));
            try
            {
                int stageLayer=Mathf.RoundToInt(Mathf.Log(business.Stage.View.cullingMask,2));
                ghost.layer=stageLayer;
                ghost.transform.position=new Vector3(0f,1.6f,5.6f);
                var box=ghost.GetComponent<BoxCollider>();box.size=new Vector3(1.6f,1.6f,1.6f);box.isTrigger=true;
                var target=ghost.GetComponent<StallInteractionTarget>();target.kind=StallInteractionKind.Packaging;target.itemId="P02";target.BindHitCollider(box);
                Physics.SyncTransforms();
                Vector3 screen=business.Stage.View.WorldToScreenPoint(ghost.transform.position);
                var picked=business.InputHandler.PickScreenPoint(screen);
                Need(picked==null,"Target behind the shop facade was picked through the wall: "+(picked==null?"<null>":picked.kind+"/"+picked.itemId));
                // Same trigger moved to open air must be picked: proves the
                // refusal above is occlusion, not the collider or kind.
                ghost.transform.position=new Vector3(-3.2f,1.4f,-2.2f);Physics.SyncTransforms();
                screen=business.Stage.View.WorldToScreenPoint(ghost.transform.position);
                picked=business.InputHandler.PickScreenPoint(screen);
                Need(picked==target,"Identical trigger in open air was not picked");
                return "wall blocks, open air accepts the same trigger box";
            }
            finally{UnityEngine.Object.DestroyImmediate(ghost);Physics.SyncTransforms();}
        }
        static string PixelTolerance()
        {
            var seasoning=new List<StallInteractionTarget>(Targets(StallInteractionKind.Seasoning));
            Need(seasoning.Count==1,"Expected exactly one seasoning target");
            var jar=seasoning[0];Vector2 anchor=Project(jar);
            float tolerance=business.InputHandler.ScreenToleranceFor(jar);
            var picked=business.InputHandler.PickScreenPoint(anchor+Vector2.left*tolerance*.5f);
            Need(picked==jar,"Half-tolerance offset near the jar missed");
            picked=business.InputHandler.PickScreenPoint(anchor+Vector2.left*(tolerance+30f));
            Need(picked==null,"Offset beyond tolerance+30 still picked the seasoning jar");
            Need(tolerance<46f*Mathf.Max(.6f,Screen.height/StallInteractionInput.ReferenceScreenHeight),"Seasoning tolerance grew into a whole-stall hit zone");
            return "seasoning tolerance "+tolerance.ToString("0.0")+"px at height "+Screen.height+": inside hits, outside misses";
        }
        static string ResolutionCoverage()
        {
            // The editor game view is only 640x480, so the two larger reference
            // resolutions are exercised as read-only projection queries by
            // assigning a temporary camera.targetTexture of each size: the
            // stage camera then projects at exactly 1280x720 / 1920x1080 while
            // Screen stays untouched. No screenshot is claimed or captured —
            // only picking queries run. ScreenToleranceFor scales from
            // view.pixelHeight, so tolerances follow the queried view.
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                return FailAsPending("GraphicsDeviceType.Null: no render environment for targetTexture; 1280x720 and 1920x1080 projection queries pending, NOT counted as pass");
            var notes=new List<string>();
            foreach(var emulated in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080)})
            {
                var rt=new RenderTexture(emulated.x,emulated.y,24,RenderTextureFormat.ARGB32);
                RenderTexture previousTarget=business.Stage.View.targetTexture;
                business.Stage.View.targetTexture=rt;Physics.SyncTransforms();
                try
                {
                    int before=report.samples.Count;
                    foreach(var target in Targets(StallInteractionKind.Cookware))SampleAnchor(target);
                    foreach(var target in Targets(StallInteractionKind.Seasoning))SampleAnchor(target);
                    foreach(var target in Targets(StallInteractionKind.Ingredient))SampleAnchor(target);
                    foreach(var target in Targets(StallInteractionKind.Packaging))SampleAnchor(target);
                    for(int i=before;i<report.samples.Count;i++)Need(report.samples[i].ok,emulated.x+"x"+emulated.y+" sample failed: "+report.samples[i].target+" -> "+report.samples[i].pickedKind+"/"+report.samples[i].pickedItem);
                }
                finally{business.Stage.View.targetTexture=previousTarget;rt.Release();UnityEngine.Object.Destroy(rt);Physics.SyncTransforms();}
                notes.Add(emulated.x+"x"+emulated.y+" read-only projection queries passed (targetTexture assigned for the query only; no screenshot claimed; game view remains "+Screen.width+"x"+Screen.height+")");
            }
            return string.Join("; ",notes);
        }
        static string FailAsPending(string reason)
        {
            // Explicitly NOT a pass: the case fails the run until a real render
            // environment can execute it, so a skip is never reported as green.
            throw new InvalidOperationException("PENDING: "+reason);
        }
        static string HudRaycastLayout()
        {
            var worldHud=typeof(BusinessHUD).GetField("worldHUD",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(business.HUD) as BusinessWorldHUD;
            Need(worldHud!=null,"BusinessWorldHUD was not created");
            // Decorations must never intercept: icons, tails, leaders, toast face.
            foreach(Graphic graphic in worldHud.GetComponentsInChildren<Graphic>(true))
            {
                if(graphic is Image&&graphic.GetComponent<Button>()==null&&graphic.name.Contains("Coin"))Need(!graphic.raycastTarget,"Sale coin must not block clicks");
                if(graphic.name.Contains("Speech pointer")||graphic.name.Contains("leader")||graphic.name.Contains("Dragged cooked dish"))Need(!graphic.raycastTarget,"Decorative "+graphic.name+" must not block clicks");
            }
            Need(!worldHud.HasOverlay,"Overlay must start closed");
            return "decorations non-blocking verified; button paths exercised in UI gating check";
        }
        static string DragAcrossUiGating()
        {
            // Production release path: end = overUI ? null : PickScreenPoint,
            // with overUI answered by the shared fresh
            // StallInteractionInput.IsScreenPointOverUI gate. Verify, without
            // injecting synthetic input, that the exact button-centre screen
            // point classifies as UI so a drag released there cancels.
            Need(EventSystem.current!=null,"EventSystem missing for UI gating check");
            Need(business.HUD!=null,"BusinessHUD missing");
            // r4 ran -batchmode with Metal but no real GameView: UGUI
            // Graphic.depth stays -1 and EventSystem raycasts are skipped, so a
            // button centre reported "no hit". Without an authoritative render
            // environment this case is explicitly PENDING (fails the run until
            // a non-batch run with a real GameView verifies it) — never a fake
            // pass. 3D projection cases above/below still execute for real.
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)
                return FailAsPending("No real Editor GameView (batch/headless run): UGUI raycast depth is not authoritative; button-centre UI gating unverified");
            var canvas=business.HUD.GetComponentInChildren<Canvas>();Need(canvas!=null,"Business canvas missing");
            Button target=null;
            foreach(Button button in canvas.GetComponentsInChildren<Button>(true))
                if(button.name=="今日菜单"&&button.gameObject.activeInHierarchy){target=button;break;}
            Need(target!=null,"今日菜单 button not found");
            Need(target.GetComponent<Image>().raycastTarget,"Real button face must intercept clicks");
            Canvas.ForceUpdateCanvases(); // raycast against settled live rects
            Vector2 centre=RectTransformUtility.WorldToScreenPoint(null,target.transform.position);
            // Fresh direct raycast as ground truth for this exact point.
            var data=new PointerEventData(EventSystem.current){position=centre};
            var results=new List<RaycastResult>();EventSystem.current.RaycastAll(data,results);
            Need(results.Count>0&&results[0].gameObject.GetComponentInParent<Button>()!=null,"Button centre did not raycast to UI first");
            // The production gate must agree at the same point...
            Need(business.InputHandler.IsScreenPointOverUI(centre),"Production IsScreenPointOverUI did not classify the button centre as UI");
            // ...while a plain world point (blank pavement) must not be UI, so
            // the gate cannot be a blanket true. Decorations stay transparent.
            Vector3 blank=business.Stage.View.WorldToScreenPoint(new Vector3(-3.4f,.05f,-2.6f));
            Need(!business.InputHandler.IsScreenPointOverUI(blank),"Blank pavement point was classified as UI by the production gate");
            return "今日菜单 centre raycasts to a Button and production IsScreenPointOverUI(centre)==true, pavement==false; release here nulls the target (no synthetic input injected)";
        }
        static string OrderOwnership()
        {
            var order=ArrivedOrderTarget();Need(order!=null,"No active arrived order target");
            Vector2 anchor=Project(order);
            var picked=business.InputHandler.PickScreenPoint(anchor);
            Need(picked==order,"Customer anchor picked "+(picked==null?"<null>":picked.kind+"/"+picked.orderId)+" instead of order "+order.orderId);
            // Body click, not just the overhead bubble: the order trigger is a
            // child of the guest root, so prove that clicking the customer's
            // upper torso selects the same order. Points are projected from the
            // real hit collider bounds; the customer's own solid capsule must
            // not occlude its own trigger, third-party solids still do.
            Need(order.HitCollider!=null,"Order target has no bound hit collider");
            Bounds body=order.HitCollider.bounds;
            Vector3 upperTorso=body.center-Vector3.up*(body.size.y*.25f); // upper torso, still inside the tall order trigger
            Vector3[] torsoPoints={body.center,upperTorso,upperTorso+Vector3.right*.1f,upperTorso+Vector3.left*.1f};
            int torsoHits=0;
            foreach(Vector3 point in torsoPoints)
            {
                Vector3 screen3=business.Stage.View.WorldToScreenPoint(point);
                Need(screen3.z>0,"Torso point for order "+order.orderId+" is behind the camera");
                var torsoPick=business.InputHandler.PickScreenPoint(screen3);
                SampleAt(order,screen3);
                Need(torsoPick==order,"Torso click on order "+order.orderId+" picked "+(torsoPick==null?"<null>":torsoPick.kind+"/"+torsoPick.orderId)+" instead");
                torsoHits++;
            }
            Need(torsoHits>=2,"Expected at least 2 upper-torso hit points, got "+torsoHits);
            var worldHud=typeof(BusinessHUD).GetField("worldHUD",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(business.HUD) as BusinessWorldHUD;
            Need(worldHud!=null,"BusinessWorldHUD missing for readout rect");
            // Project the tag rect so the assertion is resolution independent.
            // The speech card sits directly above its anchor; assert horizontal
            // alignment and a small vertical gap (card bottom just over the head).
            Rect rect=worldHud.ReadoutScreenRect(order);
            Need(rect.width>0&&rect.height>0,"Order readout rect is empty");
            bool horizontal=anchor.x>=rect.xMin-6&&anchor.x<=rect.xMax+6;
            float gapBelow=rect.yMin-anchor.y;
            Need(horizontal&&gapBelow>=-4&&gapBelow<=90,"Order readout rect is not attached to its anchor: gap="+gapBelow);
            return "order "+order.orderId+" anchor and "+torsoHits+" upper-torso points pick the customer; readout rect sits "+gapBelow.ToString("0.0")+"px above the anchor";
        }
        static IEnumerator WaitUntil(Func<bool> condition,float timeout,string message)
        {
            float deadline=Time.time+timeout;while(!condition()&&Time.time<deadline)yield return null;
            Need(condition(),message);
        }
        static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Case(string name,Func<string> test)
        {
            try{report.checks.Add(new Check{name=name,passed=true,detail=test()});}
            catch(Exception error){report.checks.Add(new Check{name=name,passed=false,detail=error.ToString()});}
        }
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Finish()
        {
            if(business!=null){business.Cancel();business=null;}
            if(eventSystem!=null){UnityEngine.Object.DestroyImmediate(eventSystem);eventSystem=null;}
            Application.logMessageReceived-=Log;
            report.errors=errors.ToArray();
            report.success=report.failure==""&&errors.Count==0;
            foreach(var check in report.checks)report.success&=check.passed;
            foreach(var sample in report.samples)report.success&=sample.ok;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("STALL_PICKING_V15_"+(report.success?"PASS":"FAIL")+" "+output);
            // The hosted run is over and the report is on disk: dispose of our
            // own host fixture immediately instead of waiting for it to
            // destroy itself between playmode frames.
            if(hostObject!=null){UnityEngine.Object.DestroyImmediate(hostObject);hostObject=null;host=null;}
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);else EditorApplication.ExitPlaymode();
        }
    }
}
