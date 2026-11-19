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
    [InitializeOnLoad] public static class Batch1CandidateValidation
    {
        [Serializable] public sealed class Check { public string name,detail;public bool passed; }
        [Serializable] public sealed class Grid
        {public string phase,target;public int sampled,bodyClear,near,inRange,primary;}
        [Serializable] public sealed class Report
        {
            public string scope="Actual HomeWorld/MarketWorld, full-size player and basket colliders; read-only interaction queries and idle published UI verdicts. Explicit fixture pose/state construction, not navigation, hardware input or two-day completion.";
            public bool success,fixturePosesAssigned=true;public string failure="",testSaveDirectory;
            public List<Check> checks=new List<Check>();public List<Grid> grids=new List<Grid>();public string[] errors;
        }
        const string Pending="Guochao.Batch1Candidate.Pending",Output="Guochao.Batch1Candidate.Output";
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly List<string> errors=new List<string>();
        static IEnumerator running;static string output,previousSaveDirectory;static Report report;static GameObject host;static GameFlow flow;
        static Batch1CandidateValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--batch1-candidate-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --batch1-candidate-output");
            SessionState.SetString(Output,Path.GetFullPath(args[at+1]));SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();errors.Clear();
                previousSaveDirectory=CampaignSaveStore.TestDirectory;
                report.testSaveDirectory=Path.Combine(Path.GetDirectoryName(output),"isolated-candidate-"+Guid.NewGuid().ToString("N"));
                CampaignSaveStore.TestDirectory=report.testSaveDirectory;
                Application.logMessageReceived+=Log;running=Execute();
            }
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}
            catch(Exception e){report.failure=e.ToString();running=null;Finish();}
        }
        static IEnumerator Execute()
        {
            Build(false);yield return null;Physics.SyncTransforms();
            Case("home exit retains 1.55m rather than 0.95m",()=>NonItemRange("HomeExit"));
            Case("home query grid is read-only",()=>GridQueries(new[]{"StovePickup","HomeCart","HomeExit"}));
            DestroyFixture();yield return null;
            Build(true);yield return null;Physics.SyncTransforms();
            Case("market counters and exit retain 1.55m",()=>
            {NonItemRange("Cashier");NonItemRange("SupplyCounter");return NonItemRange("MarketExit");});
            var targets=new List<string>();foreach(var item in GameCatalog.Items)targets.Add(item.anchor);
            targets.AddRange(new[]{"Cashier","SupplyCounter","MarketCart","MarketExit","ShoppingCart"});
            Case("eight shelves and market query grid are read-only",()=>GridQueries(targets));
            Case("basket candidate near/far positions do not use live player",BasketCandidates);
            Case("counter basket-away is spatially near but disabled",CounterBasketAway);
            Case("held box prefers legal basket over nearer guidance shelf",HeldBoxPriority);
            DestroyFixture();yield return null;
        }
        static void Build(bool market)
        {
            host=new GameObject(market?"Batch1CandidateMarketFixture":"Batch1CandidateHomeFixture");
            flow=host.AddComponent<GameFlow>();flow.enabled=false;
            if(market)Need(flow.Run.Act("HomeExit",true),"Fixture could not enter market");
            flow.Initialize();Need(flow.Run.Phase!=GamePhase.Error,flow.Run.Message);
            Need(flow.World!=null&&flow.World.Player!=null,"Production world did not build");
            flow.World.Player.enabled=false;
            // NPCs, their colliders, and the full cart/player shapes remain
            // enabled. Each read-only query group runs in one editor callback.
        }
        static void DestroyFixture()
        {if(host!=null)UnityEngine.Object.DestroyImmediate(host);host=null;flow=null;}
        static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Case(string name,Func<string> test)
        {
            try{report.checks.Add(new Check{name=name,passed=true,detail=test()});}
            catch(Exception error){report.checks.Add(new Check{name=name,passed=false,detail=error.ToString()});}
        }
        static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static string Fingerprint()
        {
            var pose=new WorldSaveData{hasPlayer=true,playerPosition=flow.World.Player.transform.position,
                playerRotation=flow.World.Player.visual.rotation,crowdJson=flow.World.Crowd?.CaptureSaveJson()};
            flow.World.ShoppingMotor?.CapturePose(pose);
            return JsonUtility.ToJson(flow.Run.CaptureSave())+"|"+JsonUtility.ToJson(pose)+"|"+flow.State.targetId;
        }
        static bool BodyClear(Vector3 position)
        {
            CharacterController body=flow.World.Player.Controller;
            Vector3 centre=position+body.center;float offset=Mathf.Max(0,body.height*.5f-body.radius);
            return !Physics.CheckCapsule(centre-Vector3.up*offset,centre+Vector3.up*offset,body.radius,
                WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
        }
        static IEnumerable<Vector3> Ring(string target,float[] radii)
        {
            Vector3 anchor=flow.World.Anchor(target);float y=flow.World.Player.transform.position.y;
            foreach(float radius in radii)for(int angle=0;angle<360;angle+=15)
            {float radians=angle*Mathf.Deg2Rad;yield return new Vector3(anchor.x+Mathf.Cos(radians)*radius,y,anchor.z+Mathf.Sin(radians)*radius);}
        }
        static string NonItemRange(string target)
        {
            string before=Fingerprint();bool found=false;Vector3 sample=Vector3.zero;
            foreach(Vector3 position in Ring(target,new[]{1.05f,1.2f,1.4f,1.5f}))
            {
                if(!BodyClear(position)||!flow.World.IsNearAt(target,position))continue;
                var candidate=flow.World.EvaluateInteraction(target,position);
                Need(candidate.distance>.95f,"Ring fixture is not outside item radius");
                Need(candidate.inRange,target+" incorrectly narrowed to item radius at "+position+" reason="+candidate.blockedBy);
                found=true;sample=position;break;
            }
            Need(found,"No full-body clear, visible outer-ring sample for "+target);
            Need(before==Fingerprint(),"Non-item query mutated world/state");
            return target+" at "+sample+" is within original 1.55m range and outside 0.95m item range; state unchanged";
        }
        static string GridQueries(IEnumerable<string> targets)
        {
            string before=Fingerprint();int count=0;
            foreach(string target in targets)
            {
                var result=new Grid{phase=flow.Run.Phase.ToString(),target=target};
                foreach(Vector3 position in Ring(target,new[]{.4f,.7f,.9f,1.15f,1.5f}))
                {
                    result.sampled++;if(!BodyClear(position))continue;result.bodyClear++;
                    var candidate=flow.World.EvaluateInteraction(target,position);
                    if(candidate.near)result.near++;if(candidate.inRange)result.inRange++;if(candidate.primary)result.primary++;
                    Need(candidate.primary== (candidate.tier==InteractionCandidate.TierActionable),"Candidate tier disagrees with primary");
                    Need(!candidate.primary||candidate.inRange,"Primary true outside spatial gate");
                }
                report.grids.Add(result);count++;
            }
            Need(before==Fingerprint(),"Grid query changed run, transforms, crowd or focus");
            return count+" target grids recorded; no query side effects. Zero primary samples may be business-state gated; this is not a path-coverage pass.";
        }
        static string BasketCandidates()
        {
            var w=flow.World;var motor=w.ShoppingMotor;
            motor.Detach();Physics.SyncTransforms(); // Declared initial parked-basket fixture.
            Vector3 near=Vector3.zero;bool found=false;
            foreach(Vector3 point in Ring("ShoppingCart",new[]{.4f,.65f,.9f,1.2f}))
                if(BodyClear(point)&&motor.IsNearBasketAt(point)){near=point;found=true;break;}
            Need(found,"No valid basket-near candidate around actual parked basket");
            Vector3 far=near+Vector3.right*8;string before=Fingerprint();
            Need(motor.IsNearBasketAt(near),"Near candidate rejected");
            Need(!motor.IsNearBasketAt(far),"Far candidate reused live player basket distance");
            Need(w.EvaluateInteraction("ShoppingCart",near).inRange,"Near basket candidate blocked by unrelated handle gate");
            Need(!w.EvaluateInteraction("ShoppingCart",far).inRange,"Far basket candidate accepted");
            Need(w.TryInteractionContactAt("ShoppingCart",near,out Vector3 contact)&&Vector3.Distance(contact,motor.BasketContactPointAt(near))<.000001f,"Candidate contact is not queried basket rim");
            Need(motor.IsNearBasket()==motor.IsNearBasketAt(w.Player.transform.position),"Live wrapper disagrees with same-position query");
            Need(before==Fingerprint(),"Basket queries changed money/stock/player/cart/NPC/focus");
            return "Fixed live pose; near="+near+", far="+far+" independently classified; wrappers and contacts match; no mutation";
        }
        static string CounterBasketAway()
        {
            var w=flow.World;Vector3 sample=Vector3.zero;bool found=false;
            foreach(Vector3 position in Ring("SupplyCounter",new[]{.4f,.7f,1.1f,1.4f}))
                if(BodyClear(position)&&w.IsNearAt("SupplyCounter",position)&&!w.ShoppingMotor.IsNearBasketAt(position,1.8f))
                {sample=position;found=true;break;}
            Need(found,"No real counter-near/basket-far fixture sample");
            string before=Fingerprint();var candidate=w.EvaluateInteraction("SupplyCounter",sample);
            Need(candidate.inRange&&!candidate.primary&&candidate.blockedBy=="basket-away","Counter query did not include required basket gate");
            Need(before==Fingerprint(),"Counter query mutated world/state");
            // Fixture pose and focus assignment tests publication only, not
            // real pointer focus acquisition or player traversal.
            w.Player.Teleport(sample);Physics.SyncTransforms();
            typeof(GameFlow).GetField("focus",Hidden).SetValue(flow,"SupplyCounter");
            typeof(GameFlow).GetMethod("Publish",Hidden).Invoke(flow,null);
            Need(!flow.IsBusy&&flow.IsActionInRange("SupplyCounter")&&!flow.State.primaryEnabled,"Idle published counter button disagrees with same query");
            return "Counter spatial gate true, basket >1.8m -> primary/button disabled; exact fixture="+sample;
        }
        static string HeldBoxPriority()
        {
            var w=flow.World;var motor=w.ShoppingMotor;
            var original=new WorldSaveData();motor.CapturePose(original);original.cartAttached=true;
            motor.RestorePose(original); // Keep the cart as its original query-only attached footprint while choosing an initial fixture.
            string shelf=null,itemId=null;Vector3 body=Vector3.zero;Quaternion heading=Quaternion.identity;bool found=false;
            foreach(var item in GameCatalog.Items)
            {
                foreach(Vector3 position in Ring(item.anchor,new[]{1.02f,1.08f,1.22f}))
                {
                    if(!BodyClear(position)||!w.IsNearAt(item.anchor,position))continue;
                    for(int angle=0;angle<360;angle+=30)
                    {
                        Quaternion yaw=Quaternion.Euler(0,angle,0);
                        foreach(float offset in new[]{0f,.3f,.5f})
                        {
                            Vector3 handleStand=position+yaw*Vector3.forward*offset;
                            if(!motor.IsPoseClear(handleStand,yaw))continue;
                            motor.ResetPoseForValidation(handleStand,yaw);motor.Detach();w.Player.Teleport(position);Physics.SyncTransforms();
                            var guidance=w.EvaluateInteraction(item.anchor,position);
                            // Reproduce the old held-focus ordering: it compared
                            // 3D basket-rim distance with ground-anchor distance,
                            // choosing this nearer-looking but unusable shelf.
                            bool oldWouldChooseShelf=(w.Anchor(item.anchor)-position).sqrMagnitude<
                                (motor.BasketContactPointAt(position)-position).sqrMagnitude;
                            bool candidate=oldWouldChooseShelf&&guidance.tier==InteractionCandidate.TierGuidance&&motor.IsNearBasketAt(position)&&BodyClear(position);
                            if(candidate){found=true;shelf=item.anchor;itemId=item.id;body=position;heading=yaw;break;}
                            // Reset only fixture construction; no movement/path success is inferred.
                            motor.RestorePose(original);
                        }
                        if(found)break;
                    }
                    if(found)break;
                }
                if(found)break;
            }
            Need(found,"No full-envelope initial basket/shelf priority fixture could be placed");
            RunSaveData data=flow.Run.CaptureSave();data.heldGrocery=itemId;data.paid=false;data.cart=Array.Empty<string>();
            RunState held=RunState.Restore(data);held.Paused=false;
            typeof(GameFlow).GetField("run",Hidden).SetValue(flow,held);w.Sync(held);Physics.SyncTransforms();
            string before=Fingerprint();var basket=w.EvaluateInteraction("ShoppingCart",body);var nearbyShelf=w.EvaluateInteraction(shelf,body);
            Need(basket.primary&&nearbyShelf.tier==InteractionCandidate.TierGuidance,"Declared held-box fixture is not legal basket plus guidance shelf");
            Need(w.NearestAt(new[]{shelf,"ShoppingCart"},body)=="ShoppingCart","Guidance shelf hides legal basket");
            Need(before==Fingerprint(),"Priority query mutated run or pose");
            typeof(GameFlow).GetMethod("UpdateFocus",Hidden).Invoke(flow,null);
            typeof(GameFlow).GetMethod("Publish",Hidden).Invoke(flow,null);
            Need(flow.State.targetId=="ShoppingCart"&&flow.State.primaryEnabled&&!flow.IsBusy,"Production held-box focus/publication bypasses actionable-first query");
            Need(flow.IsActionInRange("ShoppingCart")==basket.inRange,"Live spatial gate differs from same-position query");
            return "Held "+itemId+"; shelf guidance, basket actionable; production focus is basket at "+body+" yaw="+heading.eulerAngles.y+". Initial fixture assignment only.";
        }
        static void Finish()
        {
            DestroyFixture();Application.logMessageReceived-=Log;CampaignSaveStore.TestDirectory=previousSaveDirectory;
            report.errors=errors.ToArray();report.success=report.failure==""&&errors.Count==0&&report.checks.Count==7;
            foreach(var check in report.checks)report.success&=check.passed;
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("BATCH1_CANDIDATE_"+(report.success?"PASS":"FAIL")+" "+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);else EditorApplication.ExitPlaymode();
        }
    }
}
