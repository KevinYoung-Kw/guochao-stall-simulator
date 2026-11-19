using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // FEEDBACK3 sale-visual acceptance: real BusinessController.BeginRestored
    // fixtures (real StreetStallStage, rigged guests, HUD/world HUD), real
    // Session.Serve, real player frames. Cases execute through a diagnostic
    // MonoBehaviour coroutine host (Feedback3CoroutineHost) so every
    // "yield return null" is a real player frame; EditorApplication.update is
    // used ONLY to enter PlayMode and enforce the 180s watchdog, never to
    // advance the cases. Explicit fixtures: explicit stock, restored arrived
    // guests, one finite patience raise to survive the offline cooking
    // checkpoint, and an isolated minimal GameFlow used ONLY as the real
    // Run.Paused gate (restored to null afterwards). No key/mouse injection,
    // no SetActive(false) departure claims, no player-save writes, no
    // DestroyImmediate, no disabled UI/raycasts.
    [InitializeOnLoad]
    public static class Feedback3Validation
    {
        [Serializable] public sealed class CaseResult
        {
            public string id,status="running",detail="",failure="",fixture="";
            public int frameStart,frameEnd; // real Time.frameCount span of this case
            public List<string> observations=new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.feedback3.v2";
            public string scope="Five isolated production sale-feedback regressions. Real restored controllers, stage, rigged customers, HUD and real player frames (MonoBehaviour.StartCoroutine host); real Session.Serve and the one-shot SaleCompleted event. Fixtures: explicit inventory, restored arrived guests, a single finite patience raise for the offline cooking checkpoint, and an isolated GameFlow instance used purely as the real Run.Paused gate (restored to null afterwards). No hardware input, no collision changes, no player save writes, no complete-campaign claim.";
            public bool success,hardwareInput=false,usesFixtureInventory=true,noPlayerSaveWrites=true,collisionDisabled=false,infinitePatience=false,patienceRaised=true;
            public bool screenshotsTaken=false,screenshotsSkipped=false;
            public string screenshotsSkippedReason="",runner="",screenshotsBackend="none";
            public int passed,failed,pending;
            public string failure="";
            public List<CaseResult> cases=new List<CaseResult>();
            public string[] engineErrors;
        }
        sealed class PendingCaseException:Exception { public PendingCaseException(string message):base(message){} }
        // Real production pause gate: a bare GameFlow component. Awake installs
        // Instance and a real RunState; the component is disabled before its
        // first frame so Start()/CreateWorld() never run. Static Instance is
        // restored to null on dispose. This fixture only exercises the real
        // Run.Paused gate; it is not a user-key claim.
        sealed class PauseGate : IDisposable
        {
            public readonly GameObject root; public readonly GameFlow flow;
            public PauseGate()
            {
                root=new GameObject("FXV3_PauseGateFixture");
                flow=root.AddComponent<GameFlow>();flow.enabled=false; // Awake ran; Start/CreateWorld never will
                Need(GameFlow.Instance==flow,"Pause gate fixture did not install GameFlow.Instance.");
                Need(flow.Run!=null,"Pause gate fixture has no real RunState.");
                flow.Run.Paused=true;
                RegisterOwned(root); // root survives Dispose so Finalize can wait for the real destroy
            }
            public void Dispose()
            {
                try{if(flow!=null)flow.Run.Paused=false;CampaignMenuUI.Close();}
                finally
                {
                    if(root!=null)UnityEngine.Object.Destroy(root);
                    typeof(GameFlow).GetProperty("Instance",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
                        ?.GetSetMethod(true)?.Invoke(null,new object[]{null});
                }
            }
        }
        const string PendingKey="Guochao.Feedback3.Pending",OutputKey="Guochao.Feedback3.Output",ShotsKey="Guochao.Feedback3.Shots";
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> errors=new List<string>();
        static Report report;
        static Feedback3CoroutineHost host;
        static string output,shotsDir;
        static bool shotsRequested,shotsAllowed,finalizing,gameViewReady;
        static double startedAt;
        static int saleEvents;
        static Action<int,int,int,bool> saleCounter;
        static readonly List<BusinessController> fixtures=new List<BusinessController>();
        // Every controller root and PauseGate root this diagnostic created.
        // These references MUST survive Cancel/Destroy (which only schedules
        // the destroy) so Finalize can wait for the real destruction on real
        // frames. Never pre-Clear them and then claim the wait happened.
        static readonly List<UnityEngine.Object> ownedObjects=new List<UnityEngine.Object>();
        static bool finished; // one-shot report/exit guard inside Finish
        static void RegisterOwned(UnityEngine.Object o){if(o!=null)ownedObjects.Add(o);}

        static Feedback3Validation(){EditorApplication.update+=Watchdog;}
        // Entry: unchanged CLI contract.
        //   -executeMethod Guochao.Editor.Feedback3Validation.Run
        //   --feedback3-output <new.json> [--feedback3-screenshots <dir>]
        // Do NOT pass -quit for batch runs: the runner self-exits with
        // EditorApplication.Exit(code) after the report is written.
        // Self-exits (batch: EditorApplication.Exit; interactive: ExitPlaymode only).
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();
            int at=Array.IndexOf(args,"--feedback3-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --feedback3-output <new report.json>");
            SessionState.SetString(OutputKey,Path.GetFullPath(args[at+1]));SessionState.SetBool(PendingKey,true);
            SessionState.SetString(ShotsKey,"");
            int st=Array.IndexOf(args,"--feedback3-screenshots");
            if(st>=0&&st+1<args.Length)SessionState.SetString(ShotsKey,Path.GetFullPath(args[st+1]));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        // Editor callback responsibilities ONLY: (1) once PlayMode starts,
        // build the real coroutine host and hand it the cases; (2) watchdog.
        // It never advances case enumerators, so no fake-frame races remain.
        static void Watchdog()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(PendingKey,false))
            {
                SessionState.SetBool(PendingKey,false);output=SessionState.GetString(OutputKey,"");shotsDir=SessionState.GetString(ShotsKey,"");
                shotsRequested=shotsDir!="";
                shotsAllowed=shotsRequested&&!Application.isBatchMode
                    &&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
                errors.Clear();report=new Report();report.runner="MonoBehaviour.StartCoroutine (Feedback3CoroutineHost, real player frames)";
                startedAt=EditorApplication.timeSinceStartup;saleEvents=0;
                finalizing=false;finished=false;gameViewReady=false;
                ownedObjects.Clear(); // previous PlayMode session's refs are stale; this run re-registers its own
                saleCounter=(a,b,c,d)=>saleEvents++;
                Application.logMessageReceived+=CaptureError;
                var go=new GameObject("FXV3_ValidationRunner");
                host=go.AddComponent<Feedback3CoroutineHost>();
                host.Launch(RunCases(),()=>RequestFinish(null),e=>RequestFinish(e));
            }
            if(finalizing)return; // one-shot finalization already owns the exit path
            if(host==null||!host.IsRunning)return;
            if(EditorApplication.timeSinceStartup-startedAt>180)
            {
                // Bounded real-time watchdog. EXACTLY ONE termination, on the
                // SAME path as every other exit: first Abort the original case
                // chain (normal StopAllCoroutines + Dispose, never
                // DestroyImmediate), folding any Dispose error into the
                // report, then RequestFinish sets `finalizing` and launches
                // the single shared Finalize coroutine. Never Launch a second
                // finalization directly: Launch makes IsRunning true again,
                // so without the finalizing guard this branch would re-enter
                // forever.
                var aborted=host;aborted.Abort();
                if(aborted.PendingCleanupError!=null)
                    errors.Add("Cleanup exception (case chain dispose on watchdog abort):\n"+aborted.PendingCleanupError);
                RequestFinish(new TimeoutException("Feedback3 watchdog exceeded 180 wall-clock seconds."));
            }
        }
        // One-shot termination for ALL exit paths (normal completion, case
        // exception, watchdog timeout). Runs as a real host coroutine. The
        // case chain is already disposed before Finalize ever runs (the
        // host's Run() finally on completion/failure; Abort() on watchdog).
        // Finalize itself: Cancel/Destroy only the objects this diagnostic
        // owns and close only our own pause-menu fixture, then the HOST STAYS
        // ALIVE and waits REAL coroutine frames (bounded 5s unscaled) for
        // those owned roots to actually disappear — a coroutine can never
        // wait for its own GameObject to finish destroying, so the host is
        // scheduled for destroy only AFTER the report is written, and that
        // destroy is never awaited. The error listener stays attached until
        // cleanup completed on real frames; a cleanup/Dispose error is
        // recorded, never swallowed and never allowed to silently kill the
        // host iterator before the report exists.
        static void RequestFinish(Exception error)
        {
            if(finalizing)return;finalizing=true; // the single one-shot gate for every exit path
            if(host==null){Finish(error==null?"":error.ToString());return;}
            host.Launch(Finalize(error),()=>{},e=>{errors.Add("Finalize exception:\n"+e);Finish("Finalize failed:\n"+e);});
        }
        static IEnumerator Finalize(Exception error)
        {
            ClearFixtures(); // normal Cancel on only the controllers this test owns
            try{CampaignMenuUI.Close();} // only our own pause-menu fixture
            catch(Exception cleanup){errors.Add("Cleanup exception (CampaignMenuUI.Close):\n"+cleanup);} // recorded, not swallowed
            // REAL-frame wait for every diagnostic-owned root to disappear.
            // ownedObjects was never pre-cleared, so each reference still
            // points at the real (possibly pending-destroy) object.
            double until=Time.unscaledTimeAsDouble+5f;
            while(Time.unscaledTimeAsDouble<until)
            {
                bool anyAlive=false;
                foreach(var owned in ownedObjects)
                    if(owned!=null){anyAlive=true;break;}
                if(!anyAlive)break;
                yield return null; // one real player frame per iteration, on the still-alive host
            }
            foreach(var owned in ownedObjects)
                if(owned!=null)
                    errors.Add("Owned diagnostic object was not destroyed within 5s of real player frames: "+owned.name);
            ownedObjects.Clear(); // only NOW, after the wait genuinely happened
            // Error listener stays up through the whole cleanup so teardown-
            // time engine errors are captured; unsubscribe only now, then
            // write the report exactly once. Host destroy is scheduled after
            // the report and intentionally not awaited.
            Application.logMessageReceived-=CaptureError;
            var self=host!=null?host.gameObject:null;host=null; // Finish's defensive fallback must not self-destruct this coroutine
            Finish(error==null?"":error.ToString());
            if(self!=null)UnityEngine.Object.Destroy(self); // scheduled post-report; never awaited
        }
        // Show and focus a REAL Editor GameView when (and only when) visual
        // acceptance was explicitly requested in a non-batchmode editor with
        // a graphics device. GetMainGameView merely checks that a window
        // object exists, which does not guarantee it is visible/active; a
        // hidden or never-created GameView would make ScreenCapture write
        // nothing. EditorWindow.GetWindow is the legal editor API to create
        // or show it, and Focus() makes it the active view. If the GameView
        // type is unavailable we fail explicitly instead of pretending.
        static bool EnsureEditorGameView()
        {
            if(gameViewReady)return true;
            try
            {
                var type=Type.GetType("UnityEditor.GameView, UnityEditor");
                if(type==null)
                {
                    report.screenshotsSkippedReason="Editor GameView type is unavailable in this editor; a real GameView window cannot be shown, so no screenshots were taken.";
                    return false;
                }
                var window=EditorWindow.GetWindow(type); // creates or shows the real GameView
                window.Focus();
                gameViewReady=true;
                return true;
            }
            catch(Exception error)
            {
                report.screenshotsSkippedReason="Could not create/show/focus the real Editor GameView: "+error.Message;
                return false;
            }
        }
        static void Finish(Exception error){Finish(error==null?"":error.ToString());}
        static IEnumerator RunCases()
        {
            if(GameFlow.Instance!=null)throw new PendingCaseException("An active GameFlow is present; this entry requires an isolated empty PlayMode scene.");
            // Non-batch visual runs: create/show/focus the real Editor GameView
            // ONCE, before any real sale transaction, so the potentially slow
            // (>0.86s) window setup never lands inside a timed gesture/sale
            // window. Screenshot keeps its own idempotent EnsureEditorGameView.
            if(shotsAllowed)EnsureEditorGameView();
            string[] ids={"single-sale-feedback","pause-freezes-receive-and-coin","double-sale-same-price","teardown-unbinds-events","restore-does-not-replay-sale"};
            Func<CaseResult,IEnumerator>[] tests={SingleSale,PauseFreeze,DoubleSale,Teardown,RestoreNoReplay};
            for(int i=0;i<tests.Length;i++)
            {
                var result=new CaseResult{id=ids[i]};report.cases.Add(result);
                result.frameStart=Time.frameCount;
                IEnumerator test=Drive(tests[i](result));bool finished=false;
                try
                {
                    while(!finished)
                    {
                        object yielded=null;
                        try
                        {
                            if(test.MoveNext())yielded=test.Current;
                            else{result.status="passed";finished=true;}
                        }
                        catch(PendingCaseException error){result.status="pending";result.failure=error.Message;finished=true;}
                        catch(Exception error){result.status="failed";result.failure=error.ToString();finished=true;}
                        if(!finished)yield return yielded; // real player frames via host
                    }
                }
                finally
                {
                    // This finally is the ONE place that runs on every exit —
                    // normal loop, case failure, and an outer Abort that stops
                    // this coroutine without ever reaching the tail below.
                    // Disposing Drive(test) here cascades into the nested
                    // PauseGate using, so the pause menu and gate root are
                    // always released, not only on the happy path.
                    try{(test as IDisposable)?.Dispose();}
                    catch(Exception cleanup){errors.Add("Cleanup exception (case enumerator dispose):\n"+cleanup);} // recorded, not swallowed
                }
                result.frameEnd=Time.frameCount;
                ClearFixtures();
                yield return null; // let teardown destroys/finalizers process on real frames
                Debug.Log("FEEDBACK3_CASE "+result.id+" "+result.status+" "+result.failure);
                yield return null;
            }
        }
        // Flatten nested child iterators explicitly so timeouts/departure
        // checks actually execute; drives real-time budgets via unscaled time.
        static IEnumerator Drive(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try
            {
                while(stack.Count>0)
                {
                    var top=stack.Peek();
                    if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}
                    if(top.Current is IEnumerator nested){stack.Push(nested);continue;}
                    yield return top.Current;
                }
            }
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        static void ClearFixtures()
        {
            // Idempotent per controller: each controller is cancelled at most
            // once and leaves the active list immediately, while its root
            // stays registered in ownedObjects so Finalize can still wait for
            // the actual destroy instead of claiming a wait after the
            // references were cleared.
            for(int i=fixtures.Count-1;i>=0;i--)
            {
                var controller=fixtures[i];
                fixtures.RemoveAt(i);
                if(controller==null)continue; // already destroyed
                try{controller.Session.SaleCompleted-=saleCounter;controller.Cancel();}
                catch(Exception cleanup){errors.Add("Cleanup exception (controller cancel):\n"+cleanup);} // recorded, not swallowed
            }
        }
        // ---- fixture --------------------------------------------------------
        static BusinessSaveData Disk(BusinessSaveData data)=>JsonUtility.FromJson<BusinessSaveData>(JsonUtility.ToJson(data));
        // Deterministic offline checkpoint: explicit stock, real order
        // generation, one finite patience raise (declared fixture), real
        // BeginOrder debit, real Tick cooking to Pack through real Session.Action
        // utensil/season steps. V02 gives the rice a real 2-portion batch so a
        // second same-recipe order can be served from ReadyStock.
        static BusinessSaveData ModelCheckpoint(bool secondOrder)
        {
            var setup=new BusinessSetup{practice=true,canReturnMarket=false,stoveLoaded=true,cashCents=120000,seed=12017,dayDuration=300,gasPoints=45,
                equipment=new HashSet<string>{"B04","V02","V09","U01","U12"},
                inventory=new Dictionary<string,int>{{"rice",60},{"egg",60},{"skewer",60},{"seasoning",60},{"P02",50},{"P05",50},{"P07",50}}};
            var session=new BusinessSession(setup);session.SelectedRecipes.Clear();session.SelectedRecipes.Add(0);
            Need(session.Open(),"Fixture menu could not open: "+session.Message);
            var wanted=new List<StallOrder>();
            for(int tick=0;tick<600&&wanted.Count<(secondOrder?2:1);tick++)
            {
                session.Tick(.25f);
                foreach(var order in session.Orders)
                    if(order.recipeIndex==0&&order.arrived==false&&order.departed==false&&!wanted.Exists(x=>x.id==order.id))
                        wanted.Add(order);
            }
            Need(wanted.Count>0,"No finite seed produced a fixture order; none was fabricated.");
            Need(!secondOrder||wanted.Count>1,"No second same-recipe order arrived; none was fabricated.");
            foreach(var order in wanted){Need(session.MarkCustomerArrived(order.id),"Fixture arrived transition failed.");order.patience=120f;} // finite, declared; survives the offline cooking checkpoint
            Need(session.BeginOrder(wanted[0].id,0),"Fixture batch debit failed.");
            CookToPack(session);
            return Disk(session.CaptureSave());
        }
        static void CookToPack(BusinessSession session)
        {
            for(int guard=0;guard<400;guard++)
            {
                KitchenLane lane=session.Lanes[0];
                if(lane.step==KitchenStep.Pack)return;
                Need(lane.step!=KitchenStep.Dirty,"Fixture pan burned during checkpoint cooking.");
                if(lane.step==KitchenStep.Utensil){Need(session.Action(0),"Fixture utensil step rejected.");continue;}
                if(lane.step==KitchenStep.Season&&lane.remaining<=0f){Need(session.Action(0),"Fixture season step rejected.");continue;}
                session.Tick(.25f);
            }
            throw new InvalidOperationException("Fixture lane never reached Pack through production steps.");
        }
        static BusinessController RestoreController(BusinessSaveData data)
        {
            saleEvents=0;
            var controller=BusinessController.BeginRestored(Disk(data),null,null,null);
            controller.Session.SaleCompleted+=saleCounter;fixtures.Add(controller);
            RegisterOwned(controller); // survives Cancel so Finalize can wait for the real destroy
            return controller;
        }
        // Real teardown: production Cancel/Destroy, then wait for the actual
        // destroy to process on real player frames, with a bounded real-time
        // budget. Never a fixed yield count (two editor-callback "frames" were
        // not real frames and let destroyed-but-alive objects leak into the
        // next case) and never DestroyImmediate.
        static IEnumerator TeardownController(BusinessController controller)
        {
            if(controller!=null)
            {
                fixtures.Remove(controller); // teardown already cancels it; ClearFixtures must not Cancel twice
                controller.Session.SaleCompleted-=saleCounter;controller.Cancel();
            }
            double until=Time.unscaledTimeAsDouble+5f;
            while(controller!=null&&Time.unscaledTimeAsDouble<until)yield return null;
            Need(controller==null,"Teardown controller GameObject did not destroy within 5s of real player frames.");
        }
        static BusinessWorldHUD WorldHUDOf(BusinessController controller)
        {
            var hud=controller.GetComponentInChildren<BusinessWorldHUD>(true);
            Need(hud!=null,"World HUD was not created by the restored controller.");
            return hud;
        }
        // Read-only reflection into the stage's private guest table; the
        // presentation component itself is used through its public surface only.
        static CustomerServicePresentation PresentationOf(BusinessController controller,int orderId)
        {
            var field=typeof(StreetStallStage).GetField("guests",Hidden);
            Need(field!=null,"Stage guest table is unavailable.");
            var table=(System.Collections.IDictionary)field.GetValue(controller.Stage);
            Need(table!=null&&table.Contains(orderId),"Served order has no physical guest in the stage.");
            object guest=table[orderId];
            var pf=guest.GetType().GetField("presentation",Hidden|BindingFlags.Public);
            Need(pf!=null,"Guest record has no presentation slot.");
            var presentation=(CustomerServicePresentation)pf.GetValue(guest);
            Need(presentation!=null,"Physical guest has no CustomerServicePresentation.");
            return presentation;
        }
        static StreetStallStage.GuestNavigationAudit GuestAudit(BusinessController controller,int orderId)
        {
            foreach(var audit in controller.Stage.CaptureGuestAudit())if(audit.id==orderId)return audit;
            return null;
        }
        // Real-frame wait: each yield is one player frame; budget is real
        // (unscaled) wall time, so it is exact even under the pause gate.
        static IEnumerator WaitUntil(Func<bool> condition,float timeout,string label)
        {
            double deadline=Time.unscaledTimeAsDouble+timeout;
            while(!condition()){Need(Time.unscaledTimeAsDouble<deadline,"Timed out after "+timeout.ToString("0.#")+"s waiting for: "+label);yield return null;}
        }
        // Visual acceptance, strictly separated from functional acceptance.
        // Executes ONLY when --feedback3-screenshots was given AND we are not
        // in batchmode AND a graphics device exists AND a real Editor GameView
        // exists. Captures after a genuine WaitForEndOfFrame on the main
        // render loop, then waits for a real, non-empty file. Batchmode and
        // headless runs record an explicit skip reason instead of pretending.
        static IEnumerator Screenshot(string name)
        {
            if(!shotsAllowed)
            {
                if(!report.screenshotsSkipped)
                {
                    report.screenshotsSkipped=true;
                    report.screenshotsSkippedReason=shotsRequested
                        ?(Application.isBatchMode
                            ?"batchmode has no GameView even with a graphics device; visual acceptance must run non-batchmode with a visible Editor GameView."
                            :"no graphics device; visual acceptance requires a real GameView window.")
                        :"--feedback3-screenshots not provided; visual acceptance not requested.";
                }
                yield break;
            }
            // Non-batchmode with graphics: the actual GameView window must be
            // created/shown/focused before capture. Metal (or any graphics
            // device) is NOT a GameView; if the real window cannot be shown
            // we record an explicit visual failure instead of faking it.
            if(!EnsureEditorGameView())
            {
                report.screenshotsSkipped=true;
                yield break;
            }
            Directory.CreateDirectory(shotsDir);
            string path=Path.Combine(shotsDir,name+".png");Need(!File.Exists(path),"Use a new screenshot directory: "+path);
            yield return new WaitForEndOfFrame(); // real rendered frame must exist first
            ScreenCapture.CaptureScreenshot(path);
            double until=Time.unscaledTimeAsDouble+5;
            while(Time.unscaledTimeAsDouble<until&&(!File.Exists(path)||new FileInfo(path).Length==0))yield return null;
            Need(File.Exists(path)&&new FileInfo(path).Length>0,"Screenshot was not written (visual acceptance failure): "+path);
            report.screenshotsTaken=true;report.screenshotsBackend="UnityEditor GameView (ScreenCapture after WaitForEndOfFrame, non-batchmode)";
        }
        // ---- cases ----------------------------------------------------------
        static IEnumerator SingleSale(CaseResult result)
        {
            result.fixture="Restored real controller (V02 two-portion fried-rice batch at Pack, one arrived rigged guest, finite patience raised to 120s as a declared fixture). Sale is performed with real Session.Serve, not a money injection. Visuals run on real frames through the production controller Update.";
            BusinessSaveData data=ModelCheckpoint(false);
            BusinessController controller=RestoreController(data);yield return null;
            var hud=WorldHUDOf(controller);
            yield return WaitUntil(()=>controller.Stage.VisibleCustomerCount>0,10,"physical arrived guest");
            int orderId=controller.Session.Lanes[0].orderId;
            var guest=GuestAudit(controller,orderId);Need(guest!=null&&guest.arrived&&guest.active,"Fixture order has no physically arrived guest.");
            int price=BusinessSession.Recipes[0].priceCents,cashBefore=controller.Session.CashCents,revenueBefore=controller.Session.RevenueCents;
            var presentation=PresentationOf(controller,orderId);
            Need(presentation.HasArmRig,"Physical guest has no arm rig for the receive gesture.");
            Need(controller.Session.Serve(0,"P02"),"Real Serve of the Pack lane was rejected.");
            Need(controller.Session.CashCents-cashBefore==price&&controller.Session.RevenueCents-revenueBefore==price,
                "Cash/revenue increment does not equal recipe.priceCents ("+price+").");
            Need(saleEvents==1,"SaleCompleted did not fire exactly once for one Serve.");
            Need(controller.Session.Served==1,"Model served count is not 1.");
            Need(presentation.ReceiveElapsed==0f&&!presentation.CanDepart,"Receive gesture did not start blocking departure.");
            Need(presentation.HasReceivedPackage,"Served guest did not receive the meal package.");
            Need(hud.ActiveSaleFeedbackCount==1,"World HUD does not show exactly one coin popup for the one sale.");
            result.observations.Add("serve: cash+"+price+" revenue+"+price+" events=1 pops=1 package=yes armRig=yes");
            // Failed/repeated serves must not mint anything.
            int cashAfter=controller.Session.CashCents;bool rejectedPackage=controller.Session.Serve(0,"P02");bool rejectedLane=controller.Session.Serve(0,"P07");
            Need(!rejectedPackage&&!rejectedLane,"Repeated/invalid Serve unexpectedly succeeded.");
            Need(saleEvents==1&&controller.Session.CashCents==cashAfter&&controller.Session.Served==1,"A rejected Serve repeated an event or a transaction.");
            result.observations.Add("repeated/invalid serve: no event, no cash change");
            // Assert the gesture is genuinely in progress BEFORE the
            // (potentially slow, >0.86s) screenshot: taking the shot first
            // could let the gesture finish and make a normal completion look
            // like a mid-gesture departure violation.
            float frozen=Time.unscaledTime;yield return WaitUntil(()=>presentation.ReceiveElapsed>.1f,5,"ReceiveElapsed advancing on real frames");
            Need(!presentation.CanDepart,"Guest was allowed to depart mid-gesture.");
            IEnumerator shot=Screenshot("01_single_sale_popup");while(shot.MoveNext())yield return shot.Current;
            // Gesture runs on real frames, then departure is permitted.
            yield return WaitUntil(()=>presentation.CanDepart,6,"gesture completing (CanDepart true)");
            result.observations.Add("gesture elapsed "+(Time.unscaledTime-frozen).ToString("0.##")+"s wall to CanDepart");
            // Real navigation exit, not a deactivation: the body must walk out.
            yield return WaitUntil(()=>{var a=GuestAudit(controller,orderId);return a!=null&&a.leaving;},6,"guest marked leaving");
            float travelled0=GuestAudit(controller,orderId).travelled;
            yield return WaitUntil(()=>GuestAudit(controller,orderId).walking||GuestAudit(controller,orderId).travelled>travelled0+.01f,6,"guest physically walking away");
            yield return WaitUntil(()=>!GuestAudit(controller,orderId).active,15,"guest exiting scene by navigation");
            var exited=GuestAudit(controller,orderId);
            Need(exited.travelled>0f,"Guest left without any real navigation travel (deactivation is not departure).");
            Vector3 anchor;Need(!controller.Stage.TryGetGuestAnchor(orderId,out anchor),"Departed guest still offers a HUD anchor.");
            result.observations.Add("departure: leaving=true walkedAway=true travelled="+exited.travelled.ToString("0.##")+"m active=false anchor=false");
            controller.Session.SaleCompleted-=saleCounter;
            yield return null;
            result.detail="One real Serve settles +¥"+(price/100f).ToString("0.00")+" once, raises exactly one event and one coin popup, starts the package receive gesture that blocks departure, and the guest then really walks out of the scene.";
            IEnumerator teardown=TeardownController(controller);while(teardown.MoveNext())yield return teardown.Current;
        }
        static IEnumerator PauseFreeze(CaseResult result)
        {
            result.fixture="Same restored Pack-stage fixture with one arrived guest. Pause uses an isolated minimal GameFlow component purely as the real GameFlow.Run.Paused gate (component disabled before Start so no world is created; static Instance restored to null afterwards). This verifies the pause protection path, not any user key.";
            BusinessSaveData data=ModelCheckpoint(false);
            BusinessController controller=RestoreController(data);yield return null;
            var hud=WorldHUDOf(controller);
            yield return WaitUntil(()=>controller.Stage.VisibleCustomerCount>0,10,"physical arrived guest");
            int orderId=controller.Session.Lanes[0].orderId;
            Need(controller.Session.Serve(0,"P02"),"Real Serve before pause was rejected.");
            Need(saleEvents==1,"Sale event count is not 1 before pause.");
            var presentation=PresentationOf(controller,orderId);
            yield return WaitUntil(()=>presentation.ReceiveElapsed>.05f,5,"gesture started before pause");
            float elapsedBefore=presentation.ReceiveElapsed,ageBefore=hud.SaleFeedbackAgeTotal;int cashBefore=controller.Session.CashCents;
            using(var gate=new PauseGate())
            {
                Need(controller.IsPaused,"Controller did not observe the real pause gate.");
                double until=Time.unscaledTimeAsDouble+.7f;while(Time.unscaledTimeAsDouble<until)yield return null;
                Need(Mathf.Approximately(presentation.ReceiveElapsed,elapsedBefore)&&!presentation.CanDepart,"ReceiveElapsed advanced while paused.");
                Need(Mathf.Approximately(hud.SaleFeedbackAgeTotal,ageBefore),"Coin popup age advanced while paused.");
                Need(controller.Session.CashCents==cashBefore,"Cash changed while paused.");
                // Read-only evidence about the pause menu / "Clock stopped"
                // text BEFORE any teardown destroy: GetComponent never adds a
                // component, so this cannot mask a real MissingComponent.
                try
                {
                    var menu=UnityEngine.Object.FindObjectOfType<CampaignMenuUI>(true);
                    if(menu==null)result.observations.Add("pause-menu: no CampaignMenuUI instance; frame="+Time.frameCount);
                    else foreach(var cr in menu.GetComponentsInChildren<CanvasRenderer>(true))
                        result.observations.Add("pause-menu CanvasRenderer on '"+cr.gameObject.name+"': activeInHierarchy="+cr.gameObject.activeInHierarchy
                            +" readOnlyGetComponent="+(cr.GetComponent<CanvasRenderer>()!=null)+" frame="+Time.frameCount);
                }
                catch(Exception probe){result.observations.Add("pause-menu CanvasRenderer probe error: "+probe.Message);}
                result.observations.Add("paused 0.7s: receiveElapsed frozen at "+elapsedBefore.ToString("0.###")+", pop age frozen at "+ageBefore.ToString("0.###"));
                IEnumerator shot=Screenshot("02_paused_coin_frozen");while(shot.MoveNext())yield return shot.Current;
            }
            Need(GameFlow.Instance==null,"Pause gate fixture left a static GameFlow.Instance behind.");
            Need(!controller.IsPaused,"Controller is still paused after the gate was disposed.");
            yield return WaitUntil(()=>presentation.CanDepart,6,"gesture completing after unpause");
            result.observations.Add("resumed: gesture completed to CanDepart");
            controller.Session.SaleCompleted-=saleCounter;
            result.detail="Under the real Run.Paused gate the receive gesture and the coin popup age stay frozen with no cash movement; after unpausing the gesture completes normally and the static GameFlow.Instance is restored to null.";
            IEnumerator teardown=TeardownController(controller);while(teardown.MoveNext())yield return teardown.Current;
        }
        static IEnumerator DoubleSale(CaseResult result)
        {
            result.fixture="Restored Pack-stage fixture with TWO arrived same-recipe guests and a real V02 two-portion batch. First Serve, then a real ReadyStock BeginOrder for the second order, quick season Action and second Serve: two same-price sales close within about a second.";
            BusinessSaveData data=ModelCheckpoint(true);
            BusinessController controller=RestoreController(data);yield return null;
            var hud=WorldHUDOf(controller);
            yield return WaitUntil(()=>controller.Stage.VisibleCustomerCount>=2,10,"two physical arrived guests");
            var orders=new List<int>();foreach(var order in controller.Session.Orders)if(order.recipeIndex==0&&order.arrived&&!order.served&&!order.departed)orders.Add(order.id);
            Need(orders.Count>=2,"Fixture does not hold two arrived same-recipe orders.");
            int price=BusinessSession.Recipes[0].priceCents,cashBefore=controller.Session.CashCents;
            Need(controller.Session.Serve(0,"P02"),"First real Serve rejected.");
            Need(controller.Session.Served==1,"Model served count is not 1 after first Serve.");
            Need(controller.Session.Lanes[0].step==KitchenStep.ReadyStock,"Two-portion batch did not become ReadyStock for the second order.");
            Need(controller.Session.BeginOrder(orders[1],0),"Second order could not begin from real ReadyStock.");
            if(controller.Session.Lanes[0].step==KitchenStep.Season)
                Need(controller.Session.Action(0),"ReadyStock season step rejected.");
            Need(controller.Session.Lanes[0].step==KitchenStep.Pack,"Second order did not reach Pack through production steps.");
            Need(controller.Session.Serve(0,"P02"),"Second real Serve rejected.");
            Need(saleEvents==2,"Two Serves did not raise exactly two events.");
            Need(controller.Session.CashCents-cashBefore==price*2&&controller.Session.RevenueCents==price*2,"Two same-price sales did not settle exactly 2x priceCents.");
            Need(controller.Session.Served==2,"Model served count is not 2.");
            Need(hud.ActiveSaleFeedbackCount==2,"World HUD does not show two simultaneous coin popups for the two quick sales.");
            // Both pops must carry the same amount.
            var popsField=typeof(BusinessWorldHUD).GetField("salePops",Hidden);
            var pops=(System.Collections.IList)popsField.GetValue(hud);
            Need(pops.Count==2,"Popup pool does not hold exactly two entries.");
            var first=pops[0].GetType().GetField("text").GetValue(pops[0]) as UnityEngine.UI.Text;
            var second=pops[1].GetType().GetField("text").GetValue(pops[1]) as UnityEngine.UI.Text;
            Need(first!=null&&second!=null&&first.text==second.text,"The two same-price popups show different amounts.");
            result.observations.Add("two quick sales: events=2 cash+"+(price*2)+" pops=2 amount="+first.text);
            IEnumerator shot=Screenshot("03_double_sale_popups");while(shot.MoveNext())yield return shot.Current;
            controller.Session.SaleCompleted-=saleCounter;
            result.detail="Two same-price sales each fire one event and both coin popups coexist on the HUD showing identical amounts; the ledger moves exactly 2x priceCents.";
            IEnumerator teardown=TeardownController(controller);while(teardown.MoveNext())yield return teardown.Current;
        }
        static IEnumerator Teardown(CaseResult result)
        {
            result.fixture="Restored fixture; one real Serve, then full controller teardown (production Cancel/Destroy). Verifies the old session has no orphaned SaleCompleted subscribers, and proves it with a fresh restored controller performing a new legal sale with only live subscribers.";
            BusinessSaveData data=ModelCheckpoint(false);
            BusinessController first=RestoreController(data);yield return null;
            yield return WaitUntil(()=>first.Stage.VisibleCustomerCount>0,10,"physical arrived guest");
            Need(first.Session.Serve(0,"P02"),"Real Serve on first controller rejected.");
            Need(saleEvents==1,"First controller did not raise exactly one sale event.");
            var oldSession=first.Session;
            yield return TeardownController(first);
            var evt=typeof(BusinessSession).GetField("SaleCompleted",Hidden);
            Need(evt.GetValue(oldSession)==null,"Torn-down stage/HUD left orphaned SaleCompleted subscribers on the old session.");
            result.observations.Add("after teardown: old session SaleCompleted invocation list is empty");
            // Prove it with a new legal sale on a fresh restored controller.
            BusinessController second=RestoreController(data);yield return null;
            var hud=WorldHUDOf(second);
            yield return WaitUntil(()=>second.Stage.VisibleCustomerCount>0,10,"physical arrived guest on second controller");
            var subscribers=(Delegate)typeof(BusinessSession).GetField("SaleCompleted",Hidden).GetValue(second.Session);
            Need(subscribers!=null,"Fresh controller has no SaleCompleted subscribers at all.");
            foreach(Delegate callback in subscribers.GetInvocationList())
                if(callback.Target is UnityEngine.Object target)
                    Need(target!=null,"Fresh controller carries a dead subscriber from the torn-down controller.");
            Need(second.Session.Serve(0,"P02"),"New legal Serve on the fresh controller rejected.");
            Need(saleEvents==1,"Fresh legal sale did not raise exactly one event (orphan replay suspected).");
            Need(hud.ActiveSaleFeedbackCount==1,"Fresh controller did not show exactly one popup for its own sale.");
            result.observations.Add("fresh controller: 1 event, 1 popup, no dead subscribers in invocation list");
            yield return TeardownController(second);
            result.detail="Full teardown leaves the old session's SaleCompleted empty; a fresh restored controller performs a legal sale with only live subscribers and exactly one event/popup.";
        }
        static IEnumerator RestoreNoReplay(CaseResult result)
        {
            result.fixture="Restored fixture; one real Serve; controller.CaptureForSave() serialises the post-sale state; a brand-new BeginRestored loads it and runs real frames. The old sale must not replay: no event, no popup, cash/revenue/served frozen.";
            BusinessSaveData data=ModelCheckpoint(false);
            BusinessController first=RestoreController(data);yield return null;
            yield return WaitUntil(()=>first.Stage.VisibleCustomerCount>0,10,"physical arrived guest");
            int price=BusinessSession.Recipes[0].priceCents;
            Need(first.Session.Serve(0,"P02"),"Real Serve before capture rejected.");
            Need(saleEvents==1,"Pre-capture sale did not raise exactly one event.");
            int settledCash=first.Session.CashCents,settledRevenue=first.Session.RevenueCents;
            BusinessSaveData afterSale=Disk(first.CaptureForSave());
            Need(afterSale.cashCents==settledCash&&afterSale.revenueCents==settledRevenue&&afterSale.served==1,"Post-sale save does not match the settled ledger.");
            yield return TeardownController(first);
            saleEvents=0;
            var restored=RestoreController(afterSale);
            yield return null;
            var hud=WorldHUDOf(restored);
            Need(restored.Session.CashCents==settledCash&&restored.Session.RevenueCents==settledRevenue&&restored.Session.Served==1,
                "Restore changed the settled cash/revenue/served ledger.");
            double until=Time.unscaledTimeAsDouble+1.5f;while(Time.unscaledTimeAsDouble<until)yield return null;
            Need(saleEvents==0,"Restore replayed the old sale as a new SaleCompleted event.");
            Need(hud.ActiveSaleFeedbackCount==0,"Restore spawned a coin popup for the historical sale.");
            result.observations.Add("after restore: events=0 pops=0 cash="+settledCash+" revenue="+settledRevenue+" served=1");
            restored.Session.SaleCompleted-=saleCounter;
            result.detail="Loading the post-sale save keeps the settled ledger byte-for-byte, raises no new sale event and shows no popup: the sold order never replays.";
            yield return TeardownController(restored);
        }
        static void CaptureError(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static void Finish(string failure)
        {
            if(finished)return;finished=true; // exactly one report and one exit, whatever raced here
            if(host!=null){UnityEngine.Object.Destroy(host.gameObject);host=null;} // defensive fallback only; Finalize nulls host before calling Finish so it never self-destructs mid-coroutine
            ClearFixtures();
            Application.logMessageReceived-=CaptureError;
            report.failure=failure;report.engineErrors=errors.ToArray();
            foreach(var item in report.cases)
            {if(item.status=="passed")report.passed++;else if(item.status=="pending")report.pending++;else report.failed++;}
            if(shotsDir!=""&&!shotsAllowed&&!report.screenshotsTaken)
            {
                report.screenshotsSkipped=true;
                if(report.screenshotsSkippedReason=="")
                    report.screenshotsSkippedReason=Application.isBatchMode
                        ?"batchmode has no GameView even with a graphics device; visual acceptance must run non-batchmode with a visible Editor GameView."
                        :"visual acceptance never reached capture (case failure or a GameView that could not be shown).";
            }
            // Success requires zero engine errors: the 341 "Clock stopped"
            // MissingComponentException and the destroyed-Image exception from
            // r1 are recorded and reported verbatim, never filtered as "known".
            report.success=failure==""&&report.engineErrors.Length==0&&report.passed==report.cases.Count&&report.failed==0&&report.pending==0;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("FEEDBACK3_"+(report.success?"PASS":"FAIL")+" "+output+" "+failure);
            int code=report.success?0:report.failed==0&&failure==""&&errors.Count==0&&report.pending>0?2:1;
            if(Application.isBatchMode)EditorApplication.Exit(code);else EditorApplication.ExitPlaymode(); // leave the user's Editor open
        }
    }
}
