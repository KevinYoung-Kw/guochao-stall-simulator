using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    [InitializeOnLoad] public static class NpcMarketValidation
    {
        [Serializable] public sealed class Snapshot
        {public float elapsed;public Vector3[] positions,targets;public float[] travelled;public int[] waypoints,laps;public string[] states,rightOfWay,blockedReasons,overlaps;public int walking;}
        [Serializable] public sealed class CounterflowSnapshot
        {public float elapsed;public Vector3 southbound,northbound,target;public string phase,reason;public bool hold;}
        [Serializable] public sealed class CounterflowCase
        {
            public string name,scope="Two full-size actual NPC CharacterControllers in the real imported west aisle; shared production right-of-way and movement; staged spawn fixtures, no runtime relocation. Northbound actor represents unrestricted opposing traffic, not a claim of hardware player control.";
            public float aisleWidth=2.4f,southboundRadius,northboundRadius,opponentHeldUntil,completedAt,southboundTravel,northboundTravel;
            public int yieldCount,overlapFrames;public bool success,reachedRefuge,returnedToRoute,rightOfWaySaveRoundTripPassed;
            public List<CounterflowSnapshot> snapshots=new List<CounterflowSnapshot>();
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.market-npc.p1",scope="Actual MarketWorld/AI replacements/6 NPC CharacterControllers; same production navigation. At least600 seconds plus terminal waypoint progress, then adversarial two-way full-envelope cases in the real narrow aisle. Stationary actual player and attached trolley retained in background. Store deadline is frozen only in this environment fixture; not a shopping-flow or hardware-input test.",failure="";
            public bool success,pausePassed,usedTeleport,queryOverflow;
            public int count,independentArchetypes,environmentColliderCount,overlapFrames,deniedMoves,searchFailures;
            public float duration=600,actualDuration,totalTravelled,maxUnplannedNoProgress;
            public bool terminalWaypointsPassed,crowdSaveRoundTripPassed,legacyV14CrowdSavePassed;public int[] completedLaps,waypointsReached;
            public string[] ids,archetypes;public float[] radii,travelledByGuest,longestNoProgressByGuest;
            public Bounds aiProduceBounds,produceColliderBounds;public List<Snapshot> snapshots=new List<Snapshot>();
            public List<CounterflowCase> counterflowCases=new List<CounterflowCase>();
        }
        const string Pending="Guochao.MarketNpc.Pending",Output="Guochao.MarketNpc.Output";
        static IEnumerator running;static string output;static Report report;static GameObject host;
        static NpcMarketValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../art-v13-implementation/p1/market/market-dynamic.json"));
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--market-npc-output");if(at>=0&&at+1<args.Length)path=Path.GetFullPath(args[at+1]);
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying)return;
            if(SessionState.GetBool(Pending,false)){SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");report=new Report();running=Execute();}
            if(running==null)return;
            try{if(!running.MoveNext()){running=null;Finish();}}
            catch(Exception error){report.failure=error.ToString();running=null;Finish();}
        }
        static IEnumerator Execute()
        {
            host=new GameObject("ActualMarketNpcValidation");var flow=host.AddComponent<GameFlow>();flow.enabled=false;
            // Explicit fixture setup; no Home traversal or economy claim is made.
            flow.Run.ReturnToMarket();flow.Initialize();var world=flow.World;
            Need(world!=null&&world.Crowd!=null&&flow.Run.Phase==GamePhase.Market,"Real Market did not initialize");
            world.Player.enabled=false;var crowd=world.Crowd;crowd.enabled=false;
            yield return null;Physics.SyncTransforms();
            report.count=crowd.Count;report.independentArchetypes=crowd.IndependentNpcArchetypeCount;report.environmentColliderCount=world.SolidColliders.Count;
            report.ids=crowd.InstanceIds;report.archetypes=crowd.ArchetypeIds;report.radii=crowd.CollisionRadii;
            Need(crowd.Count==6,"All six actual shoppers must be present; actual="+crowd.Count);
            Need(crowd.IndependentNpcArchetypeCount==5,"Five actual independent shopper models must be present.");
            Need(Array.IndexOf(crowd.ArchetypeIds,"market_cashier_v15")<0,"Cashier must never enter the shopper rotation.");
            SceneDetailAssets details=world.GetComponent<SceneDetailAssets>();
            Need(details!=null&&Array.IndexOf(details.AssetIds,"MarketProduceAI")>=0,"Actual AI produce cart missing");report.aiProduceBounds=details.AssetBounds[0];
            foreach(Collider c in world.SolidColliders)if(c.enabled&&c.name=="MarketProduceAI_Runtime")report.produceColliderBounds=c.bounds;
            Need(report.produceColliderBounds.size.sqrMagnitude>.01f,"Produce physical proxy missing");
            Vector3[] lastProgress=crowd.Positions;var noProgress=new float[crowd.Count];var maxNoProgress=new float[crowd.Count];float nextSnapshot=0;bool pauseChecked=false;const float dt=1f/30f;
            int[] terminalStart=null;
            for(int frame=0;frame<21600;frame++)
            {
                float elapsed=frame*dt;
                if(!pauseChecked&&elapsed>=100)
                {
                    var before=crowd.Positions;var statesBefore=crowd.RightOfWayStates;var targetsBefore=crowd.CurrentTargets;flow.Run.Paused=true;
                    for(int p=0;p<90;p++)crowd.AdvanceNavigation(dt);
                    var after=crowd.Positions;for(int i=0;i<before.Length;i++)Need(Vector3.Distance(before[i],after[i])<.000001f,"Paused NPC moved");
                    for(int i=0;i<before.Length;i++){Need(statesBefore[i]==crowd.RightOfWayStates[i],"Paused right-of-way advanced");Need(Vector3.Distance(targetsBefore[i],crowd.CurrentTargets[i])<.000001f,"Paused NPC changed target");}
                    flow.Run.Paused=false;report.pausePassed=true;pauseChecked=true;
                }
                crowd.AdvanceNavigation(dt);Physics.SyncTransforms();
                var positions=crowd.Positions;var intent=crowd.IntendingToMove;var overlaps=crowd.CaptureOverlaps();
                if(overlaps.Length>0)report.overlapFrames++;
                for(int i=0;i<positions.Length;i++)
                {
                    if(!intent[i]||Vector3.Distance(positions[i],lastProgress[i])>=.35f){noProgress[i]=0;lastProgress[i]=positions[i];}
                    else noProgress[i]+=dt;
                    maxNoProgress[i]=Mathf.Max(maxNoProgress[i],noProgress[i]);
                }
                if(elapsed>=nextSnapshot||overlaps.Length>0&&report.overlapFrames<10)
                {nextSnapshot=elapsed+5;report.snapshots.Add(new Snapshot{elapsed=elapsed,positions=positions,targets=crowd.CurrentTargets,waypoints=crowd.WaypointsReached,laps=crowd.CompletedLaps,rightOfWay=crowd.RightOfWayStates,travelled=crowd.TravelledByGuest,states=crowd.MovementStates,blockedReasons=crowd.BlockedReasons,overlaps=overlaps,walking=crowd.WalkingCount});}
                report.actualDuration=elapsed+dt;
                if(elapsed>=report.duration&&terminalStart==null)terminalStart=crowd.WaypointsReached;
                if(terminalStart!=null)
                {
                    bool everyAgentProgressed=true;var now=crowd.WaypointsReached;
                    for(int i=0;i<now.Length;i++)everyAgentProgressed&=now[i]>terminalStart[i];
                    if(everyAgentProgressed){report.terminalWaypointsPassed=true;break;}
                }
                if(frame%150==0)yield return null;
            }
            report.travelledByGuest=crowd.TravelledByGuest;report.longestNoProgressByGuest=maxNoProgress;report.totalTravelled=crowd.TravelledMetres;report.deniedMoves=crowd.DeniedMoves;report.searchFailures=crowd.NavigationSearchFailures;
            report.completedLaps=crowd.CompletedLaps;report.waypointsReached=crowd.WaypointsReached;report.queryOverflow=crowd.NavigationQueryOverflow;
            foreach(float time in maxNoProgress)report.maxUnplannedNoProgress=Mathf.Max(report.maxUnplannedNoProgress,time);
            Need(report.overlapFrames==0,"NPC/scene or NPC/NPC footprint overlap frames="+report.overlapFrames);
            for(int i=0;i<crowd.Count;i++)Need(report.travelledByGuest[i]>30,"NPC failed to traverse scene: "+report.ids[i]+" travelled="+report.travelledByGuest[i]);
            for(int i=0;i<crowd.Count;i++)Need(report.completedLaps[i]>=2,"NPC did not finish two real route loops: "+report.ids[i]);
            Need(report.terminalWaypointsPassed,"End-of-test cutoff concealed a non-progressing route; not all NPCs reached another waypoint after600 seconds");
            Need(report.maxUnplannedNoProgress<12,"NPC blocked without planned dwell for "+report.maxUnplannedNoProgress+" seconds");
            Need(!report.queryOverflow,"Navigation physics query overflow");
            string crowdSave=crowd.CaptureSaveJson();Vector3[] savedPositions=crowd.Positions;
            int[] savedWaypoints=crowd.WaypointsReached;string[] savedTraffic=crowd.RightOfWayStates;
            crowd.RestoreSaveJson(crowdSave);
            for(int i=0;i<crowd.Count;i++)
            {Need(Vector3.Distance(savedPositions[i],crowd.Positions[i])<.000001f,"Saved crowd pose changed");Need(savedWaypoints[i]==crowd.WaypointsReached[i],"Saved route progress lost");Need(savedTraffic[i].Split(':')[0]==crowd.RightOfWayStates[i].Split(':')[0],"Saved traffic state lost");}
            report.crowdSaveRoundTripPassed=true;
            string legacyCrowdSave=crowdSave.Replace("customer_student_v15","customer_worker_v11");
            Need(legacyCrowdSave!=crowdSave,"Student missing from the real market save fixture.");
            crowd.RestoreSaveJson(legacyCrowdSave);
            for(int i=0;i<crowd.Count;i++)
            {Need(Vector3.Distance(savedPositions[i],crowd.Positions[i])<.000001f,"V14 crowd migration changed saved pose");Need(savedWaypoints[i]==crowd.WaypointsReached[i],"V14 crowd migration lost route progress");}
            Need(Array.IndexOf(crowd.ArchetypeIds,"customer_student_v15")>=0,"V14 save restored the obsolete duplicate worker visual.");
            report.legacyV14CrowdSavePassed=true;
            // Independent fixtures retain the actual scene and measured AI-cart
            // colliders. Existing actors are disabled, never moved to fake results.
            foreach(string id in crowd.InstanceIds)
            {Transform actor=world.transform.Find(id);Need(actor!=null,"Missing crowd fixture "+id);actor.gameObject.SetActive(false);}
            Physics.SyncTransforms();
            foreach(float opponentHold in new[]{0f,30f})
            {
                var test=RunCounterflow(world,opponentHold);while(test.MoveNext())yield return test.Current;
            }
            report.success=true;
        }
        static IEnumerator RunCounterflow(WorldRuntime world,float opponentHold)
        {
            var result=new CounterflowCase{name=opponentHold>0?"blocked-approach-then-clear":"simultaneous-head-on",opponentHeldUntil=opponentHold};report.counterflowCases.Add(result);
            Vector3 x=(world.Anchor("MeatShelf")-world.Anchor("EggShelf"))/3.5f;x.y=0;
            Vector3 z=(world.Anchor("RiceShelf")-world.Anchor("MeatShelf")+x*.60f)/-3.02f;z.y=0;
            Vector3 origin=world.Anchor("EggShelf")-x*3-z*16.5f;origin.y=world.Anchor("Spawn").y+.005f;
            Vector3 P(float sx,float sz)=>origin+x*sx+z*sz;
            var bounds=new Bounds(P(-1,-2),Vector3.zero);foreach(var p in new[]{P(-1,21),P(25,-2),P(25,21)})bounds.Encapsulate(p);
            var nav=new NpcNavigationSpace(bounds,WorldRuntime.EnvironmentMask);
            nav.AddObstacle(world.Player.Controller);nav.AddObstacle(world.Player.CarryCollider,true);
            if(world.ShoppingMotor!=null)nav.AddObstacle(world.ShoppingMotor.Trolley.GetComponent<Collider>(),true);
            GameObject south=new GameObject("Counterflow_Auntie"),north=new GameObject("Counterflow_Courier");
            south.transform.SetParent(host.transform);north.transform.SetParent(host.transform);
            // Initial fixture placement, before any movement/registration.
            south.transform.position=P(1.25f,12);north.transform.position=P(1.25f,7.1f);
            Need(nav.TryRegister(south.name,"customer_auntie_v11",south.transform,WorldRuntime.EnvironmentLayer,out var a),"Counterflow south spawn not clear");
            Need(nav.TryRegister(north.name,"customer_courier_v11",north.transform,WorldRuntime.EnvironmentLayer,out var b),"Counterflow north spawn not clear");
            result.southboundRadius=a.Profile.Radius;result.northboundRadius=b.Profile.Radius;
            Need((a.Profile.Radius+b.Profile.Radius)*2>result.aisleWidth,"Test must be physically unable to pass side by side");
            var priority=new MarketAisleRightOfWay(origin,x,z,nav,a,new[]{b.Controller});a.TraversalAllowed=priority.AllowsSegment;
            // The north actor must clear the ENTIRE entrance disc. Stopping at
            // (1.25,16.4) leaves it within .78+.51+.035 m of the15.45 m entry.
            // The existing northwest bay is clear of both wall and cold island.
            Vector3 southGoal=P(1.25f,5.65f),northGoal=P(.60f,16.85f);const float dt=1f/30f;float next=0;
            for(int frame=0;frame<3600;frame++)
            {
                float elapsed=frame*dt;nav.BeginFrame();
                bool overriding=priority.Decide(southGoal,dt,out Vector3 target,out bool hold);
                // Rehydrate the actual traffic controller while backing out (or
                // deliberately waiting), without changing either body's pose.
                bool restoreNow=opponentHold>0?priority.State==MarketAisleRightOfWay.Phase.WaitInRefuge:priority.State==MarketAisleRightOfWay.Phase.RetreatToEntry&&elapsed>=1;
                if(!result.rightOfWaySaveRoundTripPassed&&restoreNow)
                {
                    var saved=JsonUtility.FromJson<MarketAisleRightOfWay.SaveData>(JsonUtility.ToJson(priority.CaptureSave()));
                    var phase=priority.State;Vector3 position=a.Body.position;
                    priority=new MarketAisleRightOfWay(origin,x,z,nav,a,new[]{b.Controller});priority.RestoreSave(saved);a.TraversalAllowed=priority.AllowsSegment;nav.ResetAfterSaveRestore(a);
                    Need(priority.State==phase&&Vector3.Distance(position,a.Body.position)<.000001f,"Right-of-way rehydration changed pose or phase");
                    result.rightOfWaySaveRoundTripPassed=true;
                }
                if(!hold)nav.Step(a,overriding?target:southGoal,.92f,dt);
                if(elapsed>=opponentHold)nav.Step(b,northGoal,.92f,dt);
                var overlaps=nav.CaptureOverlaps();if(overlaps.Length>0)result.overlapFrames++;
                result.reachedRefuge|=priority.State==MarketAisleRightOfWay.Phase.WaitInRefuge;
                result.returnedToRoute|=priority.YieldCount>0&&priority.State==MarketAisleRightOfWay.Phase.Flowing;
                if(elapsed>=next){next=elapsed+1;result.snapshots.Add(new CounterflowSnapshot{elapsed=elapsed,southbound=a.Body.position,northbound=b.Body.position,target=target,phase=priority.State.ToString(),reason=priority.BlockingUser,hold=hold});}
                if(nav.HasArrived(a,southGoal)&&nav.HasArrived(b,northGoal))
                {result.completedAt=elapsed;result.success=true;break;}
                if(frame%150==0)yield return null;
            }
            result.southboundTravel=a.Travelled;result.northboundTravel=b.Travelled;result.yieldCount=priority.YieldCount;
            UnityEngine.Object.DestroyImmediate(south);UnityEngine.Object.DestroyImmediate(north);Physics.SyncTransforms();
            Need(result.overlapFrames==0,"Counterflow physical overlap: "+result.name);
            Need(result.reachedRefuge&&result.returnedToRoute&&result.yieldCount==1,"Counterflow must back out once, wait in the wide bay and resume original goal: "+result.name);
            Need(result.rightOfWaySaveRoundTripPassed,"Counterflow save/restore fixture did not run: "+result.name);
            Need(result.success,"Counterflow did not finish both original goals in120 seconds: "+result.name);
        }
        static void Finish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(host)UnityEngine.Object.DestroyImmediate(host);
            Debug.Log((report.success?"MARKET_NPC_PASS ":"MARKET_NPC_FAIL ")+output+" "+report.failure);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);else EditorApplication.ExitPlaymode();
        }
        static void Need(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);}
    }
}
