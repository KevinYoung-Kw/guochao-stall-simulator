using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Guochao
{
    // STAFFV15 diagnostic coroutine host. All acceptance steps are scheduled by
    // this MonoBehaviour's StartCoroutine, so every `yield return null` is a real
    // player loop frame. The editor entry (Guochao.Editor.MarketStaffV15Validation)
    // only creates the empty PlayMode scene, enters playmode and runs a global
    // watchdog; this host performs the checks and writes the JSON report.
    public sealed class MarketStaffV15Host : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public string schema="guochao.marketstaff.v15";
            public string scope="Real empty PlayMode scene; real GameFlow instance (disabled before Start, no save I/O); Run.Act(HomeExit) fixture enters Market; GameFlow.Initialize builds the real MarketWorld. Real RiceShelf purchase and Cashier checkout ticked once per real frame through Run.Tick; pause, CaptureSave/Json/Restore and event teardown all on real frames. Fixtures: declared inRange/basketAtHand for shelf and cashier acts, player inputEnabled=false. No collision changes, no money injection, no CompletePayment direct calls.";
            public bool success,hardwareInput=false,noCollisionChanges=true,noPlayerSaveWrites=true,usesDeclaredRangeFixtures=true;
            public bool screenshotsTaken=false,screenshotsSkipped=false;public string screenshotsSkippedReason="";
            public string screenshotBackend="Camera.Render -> RenderTexture -> ReadPixels -> PNG (offscreen; 3D scene only, no ScreenSpaceOverlay HUD claim)";
            public string failure="";
            public List<string> observations=new List<string>(),fixtures=new List<string>(),limits=new List<string>();
            public int checkoutEvents,staffPaymentCount,lastPaidCents,frameCount;
            public float staffHeight,staffLowY,staffToCounter,staffFacingDot;
            public string[] engineErrors=Array.Empty<string>();
        }
        public static MarketStaffV15Host Current;
        public static bool Finished;public static int ExitCode=1;
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static string output,shotsDir;static bool shotsPossible;
        readonly List<string> errors=new List<string>();
        Report report;GameObject flowRoot;GameFlow flow;WorldRuntime world;MarketStaffPresentation staff;
        Transform npcOld,staffBody;RunState run;int checkoutEvents;Action<int> checkoutCounter;
        struct ColliderSnap{public Collider col;public bool enabled;public Vector3 center,size;}
        List<ColliderSnap> colliderSnaps;int solidCount;int finalStaffCount=-1,finalPaid=-1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            string[] args=Environment.GetCommandLineArgs();
            int at=Array.IndexOf(args,"--marketstaff-output");
            if(at<0||at+1>=args.Length)return;
            output=Path.GetFullPath(args[at+1]);shotsDir="";
            int st=Array.IndexOf(args,"--marketstaff-screenshots");
            if(st>=0&&st+1<args.Length)shotsDir=Path.GetFullPath(args[st+1]);
            shotsPossible=shotsDir!=""&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
            var host=new GameObject("STAFFV15_Host").AddComponent<MarketStaffV15Host>();
            Current=host;host.StartCoroutine(host.Run());
        }
        static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        void CaptureError(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        // Set by the editor watchdog to ask the host to fail and clean up on its own
        // real coroutine frames; the host owns the single report write.
        public static string FailureRequested;
        IEnumerator Run()
        {
            // First-frame clear of statics so a previous editor session's leftover
            // failure request or exit state can never leak into this run.
            Finished=false;ExitCode=1;FailureRequested=null;
            Application.logMessageReceived+=CaptureError;
            report=new Report();
            Exception failure=null;
            var stack=new Stack<IEnumerator>();
            IEnumerator root=null;
            try
            {
                root=Steps();stack.Push(root);
                while(stack.Count>0)
                {
                    if(FailureRequested!=null){failure=new InvalidOperationException("Failure cleanup requested by editor watchdog: "+FailureRequested);break;}
                    var it=stack.Peek();bool moved;object current=null;
                    try{moved=it.MoveNext();if(moved)current=it.Current;}
                    catch(Exception error){failure=error;break;}
                    if(!moved)
                    {
                        stack.Pop();
                        try{(it as IDisposable)?.Dispose();}
                        catch(Exception error){errors.Add("Dispose error: "+error);}
                        continue;
                    }
                    if(current is IEnumerator sub){stack.Push(sub);continue;}
                    yield return current; // plain frame/yield payloads (null etc.) pass through
                }
            }
            finally
            {
                while(stack.Count>0)
                {
                    var it=stack.Pop();
                    try{(it as IDisposable)?.Dispose();}
                    catch(Exception error){errors.Add("Dispose error: "+error);}
                }
            }
            if(failure!=null){report.failure=failure.ToString();report.success=false;}
            if(errors.Count>0)report.success=false;
            // Normal, exceptional and watchdog-requested paths all land here: the
            // original steps are already stopped/Disposed via the finally above. The
            // host stays alive, only this fixture's flowRoot is destroyed, and we wait
            // on real coroutine frames (max 5 s) for the actual destroy. The error
            // listener stays attached until cleanup finishes; the report is written
            // exactly once, here.
            if(flowRoot!=null)
            {
                Destroy(flowRoot);
                double deadline=Time.unscaledTimeAsDouble+5;
                while(flowRoot!=null)
                {
                    if(Time.unscaledTimeAsDouble>deadline)
                    {report.success=false;errors.Add("Destroy(flowRoot) did not complete within 5 s of real frames.");break;}
                    yield return null;
                }
            }
            // Snapshot the final summary only AFTER cleanup so teardown errors are not
            // lost; a real timeout above fails instead of silently breaking as success.
            report.engineErrors=errors.ToArray();
            report.checkoutEvents=checkoutEvents;report.staffPaymentCount=finalStaffCount;report.lastPaidCents=finalPaid;
            report.frameCount=Time.frameCount;
            Application.logMessageReceived-=CaptureError;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("MARKETSTAFF_V15 "+(report.success?"PASS":"FAIL")+" "+output+" "+report.failure);
            Finished=true;ExitCode=report.success?0:1;
        }
        IEnumerator Steps()
        {
                yield return null;
                Need(GameFlow.Instance==null,"An active GameFlow already exists; this entry requires an isolated empty PlayMode scene.");
                Need(GameObject.FindObjectOfType<WorldRuntime>()==null,"Scene is not empty: a WorldRuntime already exists.");
                // Real GameFlow; Awake installs Instance+RunState, component is disabled
                // before its first frame so Start()/CreateWorld()/UI never run.
                flowRoot=new GameObject("STAFFV15_GameFlow");
                flow=flowRoot.AddComponent<GameFlow>();flow.enabled=false;
                Need(GameFlow.Instance==flow&&flow.Run!=null,"GameFlow fixture did not install Instance/Run.");
                run=flow.Run;
                Need(run.Act("HomeExit",true),"Fixture departure HomeExit was rejected.");
                Need(run.Phase==GamePhase.Market,"HomeExit did not enter Market phase.");
                flow.Initialize(); // real MarketWorld build (MarketDetails -> MarketStaffPresentation)
                world=flow.World;
                Need(world!=null&&world.Player!=null,"GameFlow.Initialize did not build a playable world.");
                world.Player.inputEnabled=false; // fixture: no human keys can move the player
                report.fixtures.Add("Run.Act targets use declared inRange/basketAtHand=true (no physics teleport, no Cart.Add/Cash edits).");
                report.fixtures.Add("world.Player.inputEnabled=false (human keys cannot influence the run; collisions untouched).");
                yield return null;yield return null; // let presentation Update/Observe run on real frames
                staff=world.GetComponent<MarketStaffPresentation>();
                Need(staff!=null&&staff.IsReady,"MarketStaffPresentation missing or not ready.");
                yield return VerifyInstallation();
                yield return VerifySkinPlacement();
                if(shotsPossible){yield return Shot(world.View,"01_market_view");}
                else{report.screenshotsSkipped=true;report.screenshotsSkippedReason="nographics or --marketstaff-screenshots absent ("+SystemInfo.graphicsDeviceType+").";}
                yield return VerifyCheckout();
                yield return VerifyPause();
                yield return VerifyRestore();
                yield return VerifyTeardown();
                report.success=errors.Count==0;
        }
        IEnumerator VerifyInstallation()
        {
            var def=CharacterAssetCatalog.Find(MarketStaffPresentation.ArchetypeId);
            Need(def!=null,"Catalog lacks "+MarketStaffPresentation.ArchetypeId+".");
            Need(def.Role==CharacterRole.MarketStaff&&!def.IsCustomerSource,"market_cashier_v15 must be Role.MarketStaff, never a customer draw.");
            Need(CharacterAssetCatalog.TryLoad(MarketStaffPresentation.ArchetypeId,out var asset,out string fail,CharacterMotion.Idle),fail);
            Need(asset.Clips.ContainsKey(CharacterMotion.Idle),"Idle clip not loaded for cashier.");
            Need(Resources.Load<Texture2D>(def.AlbedoResourcePath)!=null,"Original albedo missing for cashier.");
            staffBody=FindDeep(world.transform,"MarketStaff_ActualCashierV15");
            Need(staffBody!=null,"Actual cashier body node missing.");
            var skins=staffBody.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Need(skins.Length>0,"No real skinned geometry on the actual cashier.");
            var bones=new HashSet<Transform>();int tris=0;
            foreach(var skin in skins){tris+=skin.sharedMesh.triangles.Length/3;foreach(var bone in skin.bones)if(bone!=null)bones.Add(bone);}
            Need(bones.Count==52,"Expected 52 runtime bones, got "+bones.Count+".");
            report.observations.Add("install: id=market_cashier_v15 role=MarketStaff bones="+bones.Count+" tris="+tris+" idleLoaded=true albedoLoaded=true");
            var anim=staffBody.GetComponentInChildren<Animation>(true);
            Need(anim!=null&&anim["cashier_idle"]!=null&&anim.isPlaying,"Authored Idle is not playing on the actual cashier.");
            npcOld=FindDeep(world.transform,"NPC_cashier");
            Need(npcOld!=null,"Original NPC_cashier node missing.");
            int oldVisible=0;foreach(var r in npcOld.GetComponentsInChildren<Renderer>(true))if(r.enabled)oldVisible++;
            Need(oldVisible==0,"Original NPC_cashier renderers must be hidden while the real model is present.");
            // Preserve collider identity/envelope for the payment/pause/restore checks.
            colliderSnaps=new List<ColliderSnap>();int enabledColliders=0;
            foreach(var node in world.GetComponentsInChildren<Transform>(true))
            {
                if(!node.name.StartsWith("Collider_",StringComparison.Ordinal))continue;
                var col=node.GetComponent<Collider>();
                if(col==null||!col.enabled)continue;
                enabledColliders++;
                colliderSnaps.Add(new ColliderSnap{col=col,enabled=col.enabled,center=col.bounds.center,size=col.bounds.size});
            }
            solidCount=world.SolidColliders.Count;
            Need(enabledColliders>0&&solidCount>0,"Collider_ proxies or World.SolidColliders are empty.");
            foreach(var col in world.SolidColliders)Need(col!=null&&col.enabled,"A SolidColliders member lost its collider or was disabled.");
            report.observations.Add("colliders: enabled Collider_="+enabledColliders+" SolidColliders="+solidCount+" identities snapshotted");
            report.limits.Add("Pre-install causality (why the authored location fits) cannot be proven from runtime; foot planting is checked against the authored NPC_cashier anchor, not a full nav-mesh audit.");
            yield return null;
        }
        void CheckCollidersUnchanged(string stage)
        {
            int i=0;
            foreach(var node in world.GetComponentsInChildren<Transform>(true))
            {
                if(!node.name.StartsWith("Collider_",StringComparison.Ordinal))continue;
                var col=node.GetComponent<Collider>();
                if(col==null||!col.enabled)continue;
                Need(i<colliderSnaps.Count,"Collider set changed during "+stage+".");
                var snap=colliderSnaps[i++];
                Need(col==snap.col&&col.enabled==snap.enabled&&(col.bounds.center-snap.center).sqrMagnitude<.0001f&&(col.bounds.size-snap.size).sqrMagnitude<.0001f,"Collider "+node.name+" changed during "+stage+".");
            }
            Need(i==colliderSnaps.Count&&world.SolidColliders.Count==solidCount,"Collider count changed during "+stage+".");
        }
        IEnumerator VerifySkinPlacement()
        {
            // Independent rendered-skin evaluation: bind pose * localToWorld * weights,
            // the same validated path as AssetsV15Validation (no second default BakeMesh,
            // so no scale compensation is applied twice).
            var skins=staffBody.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Vector3 min=new Vector3(float.MaxValue,float.MaxValue,float.MaxValue),max=-min;
            foreach(var skin in skins)
            {
                Mesh source=skin.sharedMesh;var bind=source.bindposes;
                var matrices=new Matrix4x4[skin.bones.Length];
                for(int b=0;b<matrices.Length;b++)matrices[b]=skin.bones[b].localToWorldMatrix*bind[b];
                var counts=source.GetBonesPerVertex();var weights=source.GetAllBoneWeights();var verts=source.vertices;int cursor=0;
                for(int v=0;v<source.vertexCount;v++)
                {
                    Vector3 p=Vector3.zero;
                    for(int w=0;w<counts[v];w++){var weight=weights[cursor++];p+=matrices[weight.boneIndex].MultiplyPoint3x4(verts[v])*weight.weight;}
                    Need(!float.IsNaN(p.x)&&!float.IsInfinity(p.y),"Nonfinite skin point on actual cashier.");
                    min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                }
                counts.Dispose();weights.Dispose();
            }
            float height=max.y-min.y;report.staffHeight=height;report.staffLowY=min.y;
            Need(height>1.3f&&height<2f,"Rendered cashier height "+height.ToString("0.###")+" m is not the 1.62 m class.");
            Need(Mathf.Abs(min.y-npcOld.position.y)<.25f,"Cashier feet "+min.y.ToString("0.###")+" do not sit at the authored anchor height "+npcOld.position.y.ToString("0.###")+".");
            Need((staffBody.position-npcOld.position).magnitude<.05f,"Actual cashier drifted from the authored NPC_cashier spot.");
            Vector3 counter=world.HasAnchor("CashierCounter")?world.Anchor("CashierCounter"):world.Anchor("Cashier");
            report.staffToCounter=(staffBody.position-counter).magnitude;report.staffFacingDot=Vector3.Dot(staffBody.forward,(counter-staffBody.position).normalized);
            Need(report.staffToCounter<2.5f,"Cashier is "+report.staffToCounter.ToString("0.##")+" m from the counter anchor.");
            Need(report.staffFacingDot>.5f,"Cashier does not face the counter (dot="+report.staffFacingDot.ToString("0.##")+").");
            report.observations.Add("skin: height="+height.ToString("0.###")+"m feet="+min.y.ToString("0.###")+" anchorDelta="+(staffBody.position-npcOld.position).magnitude.ToString("0.###")+" toCounter="+report.staffToCounter.ToString("0.##")+" facingDot="+report.staffFacingDot.ToString("0.##"));
            yield return null;
        }
        IEnumerator VerifyCheckout()
        {
            Need(run.Act("RiceShelf",true,true),"Declared fixture pickup RiceShelf was rejected.");
            int total=run.CartTotalCents,cashBefore=run.CashCents;checkoutEvents=0;
            checkoutCounter=_=>checkoutEvents++;
            run.CheckoutCompleted+=checkoutCounter;
            Need(run.Act("Cashier",true,true),"Declared fixture Cashier checkout was rejected.");
            Need(run.CheckoutInProgress,"Checkout did not start.");
            float duration=run.CheckoutDuration;
            Need(Mathf.Approximately(duration,4f),"CheckoutDuration "+duration+" is not the real 4 s service.");
            double deadline=Time.unscaledTimeAsDouble+20;
            while(run.CheckoutInProgress){Need(Time.unscaledTimeAsDouble<deadline,"Checkout never completed within 20 s of real frames.");run.Tick(Time.unscaledDeltaTime,true);yield return null;}
            Need(run.Paid,"Run did not settle Paid after the real 4 s service.");
            Need(cashBefore-run.CashCents==total&&run.SpentCents==total,"Ledger moved "+(cashBefore-run.CashCents)+" / spent "+run.SpentCents+" instead of CartTotalCents="+total+".");
            Need(checkoutEvents==1,"CheckoutCompleted fired "+checkoutEvents+" times, expected 1.");
            Need(staff.PaymentPresentationCount==1&&staff.LastPaidCents==total,"Staff presentation count/amount wrong: "+staff.PaymentPresentationCount+"/"+staff.LastPaidCents+".");
            Need(staff.IsPresentingReceipt,"Receipt presentation did not start after payment.");
            Need(staff.ReceiptElapsed<1.05f,"ReceiptElapsed "+staff.ReceiptElapsed.ToString("0.###")+" already past 1.05 s; the pause test would no longer cover an in-flight receipt.");
            finalStaffCount=staff.PaymentPresentationCount;finalPaid=staff.LastPaidCents;
            report.observations.Add("checkout: total="+total+" cash -"+(cashBefore-run.CashCents)+" events=1 staffPresents=1 receiptStarted=true (one real Run.Tick per real frame)");
            int cashPaid=run.CashCents;int staffCount=staff.PaymentPresentationCount;
            Need(!run.Act("Cashier",true,true),"A second Cashier act after payment must be rejected.");
            Need(run.CashCents==cashPaid&&checkoutEvents==1&&staff.PaymentPresentationCount==staffCount,"Repeat payment double-charged or re-fired.");
            report.observations.Add("repeat cashier act: rejected, no double charge, no replay");
            CheckCollidersUnchanged("payment");
            yield return null;
        }
        IEnumerator VerifyPause()
        {
            run.Paused=true;
            Need(staff.IsPresentingReceipt,"Receipt must still be presenting when the pause begins.");
            float recBefore=staff.ReceiptElapsed;int cashBefore=run.CashCents;
            Need(recBefore<1.05f,"receiptElapsed "+recBefore.ToString("0.###")+" was captured after the receipt already finished; pause test would be vacuous.");
            report.observations.Add("pause entry: presentingReceipt=true receiptElapsed="+recBefore.ToString("0.###")+" (in-flight receipt action)");
            // Close-up is captured after the pause is in effect, so VerifyPause below
            // is never starved of a presenting receipt by a slow screenshot.
            if(shotsPossible)yield return CloseShot("02_cashier_closeup");
            float waited=0;while(waited<.5f){waited+=Time.unscaledDeltaTime;yield return null;}
            Need(Mathf.Approximately(staff.ReceiptElapsed,recBefore),"ReceiptElapsed advanced while paused.");
            Need(run.CashCents==cashBefore,"Cash moved while paused.");
            report.observations.Add("pause 0.5s: receiptElapsed frozen at "+staff.ReceiptElapsed.ToString("0.###")+" (was "+recBefore.ToString("0.###")+" before wait), cash frozen");
            run.Paused=false;
            double deadline=Time.unscaledTimeAsDouble+5;
            while(staff.IsPresentingReceipt){Need(Time.unscaledTimeAsDouble<deadline,"Receipt did not finish naturally after unpause.");yield return null;}
            CheckCollidersUnchanged("pause");
            report.observations.Add("resume: receipt finished naturally");
            yield return null;
        }
        IEnumerator VerifyRestore()
        {
            var data=run.CaptureSave();
            string json=JsonUtility.ToJson(data);
            var restored=RunState.Restore(JsonUtility.FromJson<RunSaveData>(json)); // explicit load fixture, no player file
            typeof(GameFlow).GetField("run",Hidden).SetValue(flow,restored);
            restored.Paused=false;
            world.Sync(restored);
            int cashPaid=run.CashCents;int staffCount=staff.PaymentPresentationCount;int events=checkoutEvents;
            yield return null;yield return null; // real Update re-Observe via GameFlow.Instance.Run
            Need(flow.Run==restored&&restored.Paid,"Restored run is not live/Paid after the private-run replacement.");
            Need(restored.CashCents==cashPaid,"Restore changed the settled cash.");
            Need(staff.PaymentPresentationCount==staffCount&&checkoutEvents==events,"Restore replayed the payment presentation/event.");
            run.CheckoutCompleted-=checkoutCounter;
            CheckCollidersUnchanged("restore");
            report.observations.Add("restore: paid kept, presentation/event count stable, cash byte-identical");
            yield return null;
        }
        IEnumerator VerifyTeardown()
        {
            var evt=typeof(RunState).GetField("CheckoutCompleted",Hidden);
            var list=(Delegate)evt.GetValue(flow.Run);
            Need(list!=null,"Restored run lost all CheckoutCompleted subscribers.");
            int liveBefore=list.GetInvocationList().Length;
            var staffGO=staff.gameObject;Destroy(staffGO);
            yield return null;yield return null;
            list=(Delegate)evt.GetValue(flow.Run);
            int liveAfter=list==null?0:list.GetInvocationList().Length;
            Need(liveAfter==liveBefore-1,"Destroyed staff presentation did not unsubscribe exactly once ("+liveBefore+"->"+liveAfter+").");
            report.observations.Add("teardown: CheckoutCompleted subscribers "+liveBefore+"->"+liveAfter);
            yield return null;
        }
        IEnumerator Shot(Camera camera,string name,int w=960,int h=600)
        {
            yield return null; // let the frame settle before the manual render
            Directory.CreateDirectory(shotsDir);
            var rt=new RenderTexture(w,h,24);var prevTarget=camera.targetTexture;var prevActive=RenderTexture.active;
            var tex=new Texture2D(w,h,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;RenderTexture.active=rt;
                camera.Render();
                tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
                File.WriteAllBytes(Path.Combine(shotsDir,name+".png"),tex.EncodeToPNG());
                report.screenshotsTaken=true;
            }
            finally
            {
                camera.targetTexture=prevTarget;RenderTexture.active=prevActive;
                rt.Release();Destroy(rt);Destroy(tex);
            }
            yield return null;
        }
        IEnumerator CloseShot(string name)
        {
            if(!shotsPossible)yield break;
            Vector3 counter=world.HasAnchor("CashierCounter")?world.Anchor("CashierCounter"):world.Anchor("Cashier");
            Vector3 mid=(staffBody.position+counter)/2+Vector3.up*1.15f;
            Vector3 side=Vector3.Cross(Vector3.up,counter-staffBody.position).normalized;
            var go=new GameObject("STAFFV15_CloseCamera");
            var cam=go.AddComponent<Camera>();
            go.transform.position=mid+side*2.4f+Vector3.up*.35f;
            go.transform.rotation=Quaternion.LookRotation(mid-go.transform.position,Vector3.up);
            yield return Shot(cam,name);
            Destroy(go);yield return null;
        }
        static Transform FindDeep(Transform root,string name)
        {foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
    }
}
