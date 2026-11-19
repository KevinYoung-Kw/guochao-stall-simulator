using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Guochao
{
    // Opt-in diagnostics. Production movement/colliders are never replaced or relaxed.
    // Launch with --traversal-test --traversal-output /absolute/path/report.txt.
    public sealed class TraversalValidation : MonoBehaviour
    {
        [Serializable] public sealed class Report
        {
            public bool success;
            public string failure = "";
            public bool usedTeleport;
            public float trolleyWidth;
            public float pathMetres;
            public float planningSeconds;
            public float marketUsedSeconds;
            public int movementCalls;
            public int blockedMoves;
            public float maxMovementDeltaTime;
            public float maxPlanningDeltaTime;
            public string[] reached = Array.Empty<string>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--traversal-test") < 0) return;
            new GameObject("ActualMovementTraversalValidation").AddComponent<TraversalValidation>();
        }

        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = Path.Combine(Application.persistentDataPath, "traversal-validation.txt");
            int index = Array.IndexOf(args, "--traversal-output");
            if (index >= 0 && index + 1 < args.Length) output = args[index + 1];
            float deadline = Time.realtimeSinceStartup + 20;
            while ((GameFlow.Instance == null || GameFlow.Instance.World == null) && Time.realtimeSinceStartup < deadline) yield return null;
            bool passed = false;
            yield return ValidateRun(GameFlow.Instance, output, result => passed = result);
            Application.Quit(passed ? 0 : 1);
        }

        // Full fresh run: physically carry B04 out of Home, then traverse Market.
        public static IEnumerator ValidateRun(GameFlow flow, string outputPath, Action<bool> completed = null)
        { return Guard(new Session(flow, outputPath), true, completed); }

        // Caller must provide a fresh Market at its real Spawn, with an empty basket.
        public static IEnumerator ValidateMarket(GameFlow flow, string outputPath, Action<bool> completed = null)
        { return Guard(new Session(flow, outputPath), false, completed); }

        [Serializable] public sealed class KeyboardHint
        { public string target; public bool ready, blocked; public string reason; public int axis; public float seconds, distance,searchComputeSeconds,searchWallSeconds; public Vector3 plannedEnd; public int explored; }

        // Plan from the browser's current pose while the tester pauses via Esc.
        // This returns keyboard instructions only; it never moves or changes state.
        public static string ReadOnlyKeyboardHint(GameFlow flow, string target)
        {
            Need(flow != null && flow.World != null && flow.Run.Paused, "Pause before querying a keyboard route.");
            WorldRuntime world = flow.World;
            if (IsRequestedFocus(world, flow.Run, target, world.Player.transform.position, true))
                return JsonUtility.ToJson(new KeyboardHint { target = target, ready = true });
            var probe = world.ShoppingMotor != null && world.ShoppingMotor.IsAttached ? new TrolleyProbe(world) : null;
            var search = new Search(world, flow.Run, target, true, probe, new List<BlockedCommand>());
            float start = Time.realtimeSinceStartup;
            while (!search.Done)
            { search.Expand(100); Need(Time.realtimeSinceStartup - start < 20, "Keyboard route planning budget exceeded"); }
            Need(search.Path != null && search.Path.Count > 1, "No keyboard route found from the current pose");
            Node first = search.Path[1]; float seconds = Search.ActionDuration;
            for (int i = 2; i < search.Path.Count && search.Path[i].axis == first.axis && seconds < .47f; i++)
                seconds += Search.ActionDuration;
            return JsonUtility.ToJson(new KeyboardHint { target = target, axis = first.axis, seconds = seconds, explored = search.Expanded });
        }

        // Native campaign diagnostics use the same read-only search, but yield
        // between query batches while the user-visible game pause is held.
        // Neither this method nor Search assigns player/cart transforms.
        public static IEnumerator PlanKeyboardHint(GameFlow flow,string target,Action<KeyboardHint> completed)
        {
            Need(flow!=null&&flow.World!=null&&flow.Run.Paused,"Pause before planning a physical input route");
            WorldRuntime world=flow.World;
            if(IsRequestedFocus(world,flow.Run,target,world.Player.transform.position,true))
            {completed(new KeyboardHint{target=target,ready=true});yield break;}
            var probe=world.ShoppingMotor!=null&&world.ShoppingMotor.IsAttached?new TrolleyProbe(world):null;
            var search=new Search(world,flow.Run,target,true,probe,new List<BlockedCommand>());
            float start=Time.realtimeSinceStartup;double computeSeconds=0;
            // Budget actual synchronous query work, not time spent waiting for
            // rendered frames. Background frame throttling is recorded separately.
            // Eight-state batches bound overshoot of the 8ms UI-friendly time slice.
            while(!search.Done)
            {
                long slice=System.Diagnostics.Stopwatch.GetTimestamp();
                do
                {
                    long workStart=System.Diagnostics.Stopwatch.GetTimestamp();search.Expand(8);
                    computeSeconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-workStart)/(double)System.Diagnostics.Stopwatch.Frequency;
                    Need(computeSeconds<60,"Campaign planner exceeded60 seconds of active search work after "+search.Expanded+" states; not proof of physical unreachability: "+target);
                }
                while(!search.Done&&(System.Diagnostics.Stopwatch.GetTimestamp()-slice)/(double)System.Diagnostics.Stopwatch.Frequency<.008);
                if(!search.Done)yield return null;
            }
            if(search.Path==null||search.Path.Count<=1)
            {completed(new KeyboardHint{target=target,blocked=true,reason="Campaign planner found no full-envelope input route after "+search.Expanded+" states",explored=search.Expanded,
                searchComputeSeconds=(float)computeSeconds,searchWallSeconds=Time.realtimeSinceStartup-start});yield break;}
            Node first=search.Path[1],last=first;float seconds=Search.ActionDuration;
            for(int i=2;i<search.Path.Count&&search.Path[i].axis==first.axis&&seconds<1.27f;i++)
            {seconds+=Search.ActionDuration;last=search.Path[i];}
            // Search is geometric and uses top speed. The real input motor starts
            // from rest after pause, so execution follows this checked segment's
            // distance instead of mistaking top-speed time for acceleration time.
            completed(new KeyboardHint{target=target,axis=first.axis,seconds=seconds,
                distance=FlatDistance(search.Path[0].position,last.position),plannedEnd=last.position,explored=search.Expanded,
                searchComputeSeconds=(float)computeSeconds,searchWallSeconds=Time.realtimeSinceStartup-start});
        }

        [Serializable] public sealed class RouteColliderAudit
        {public string name;public Bounds bounds;public bool npc;}
        [Serializable] public sealed class RouteGoalAudit
        {public Vector3 position;public bool focusAccepted,bodyClear;public int clearCartHeadings;public string bodyBlockers,firstCartBlocker,focusRayBlocker;}
        [Serializable] public sealed class RouteFailureAudit
        {
            public string target,nearest,scope="Read-only live physics and pose capture; no relocation, no disabled obstacles";
            public Vector3 anchor;public bool actualCartPoseClear;public float marketUsed;
            public WorldSaveData world;public string[] commandSweeps;
            public List<RouteGoalAudit> goals=new List<RouteGoalAudit>();public List<RouteColliderAudit> colliders=new List<RouteColliderAudit>();
        }
        public static string CaptureRouteFailure(GameFlow flow,string target)
        {
            WorldRuntime w=flow.World;Vector3 body=w.Player.transform.position;
            var a=new RouteFailureAudit{target=target,nearest=flow.State.targetId,anchor=w.Anchor(target),marketUsed=flow.Run.MarketUsed,
                world=new WorldSaveData{hasPlayer=true,playerPosition=body,playerRotation=w.Player.visual.rotation,crowdJson=w.Crowd?.CaptureSaveJson()}};
            w.ShoppingMotor?.CapturePose(a.world);
            var probe=w.ShoppingMotor!=null&&w.ShoppingMotor.IsAttached?new TrolleyProbe(w):null;
            a.actualCartPoseClear=probe==null||w.ShoppingMotor.IsPoseClear(body,w.ShoppingMotor.Facing);
            Vector3 forward=w.View.transform.forward,right=w.View.transform.right;forward.y=0;right.y=0;forward.Normalize();right.Normalize();
            Vector2[] axes={Vector2.up,new Vector2(1,1).normalized,Vector2.right,new Vector2(1,-1).normalized,Vector2.down,new Vector2(-1,-1).normalized,Vector2.left,new Vector2(-1,1).normalized};
            a.commandSweeps=new string[axes.Length];
            for(int i=0;i<axes.Length;i++)
                a.commandSweeps[i]=probe==null?"No attached trolley":probe.DescribeFrameBlock(body,w.ShoppingMotor.Facing,forward*axes[i].y+right*axes[i].x,w.Player.EffectiveTopSpeed,.16f);
            for(int x=-6;x<=6;x++)for(int z=-6;z<=6;z++)
            {
                if(x*x+z*z>36)continue;Vector3 p=a.anchor+new Vector3(x*.25f,0,z*.25f);p.y=body.y;
                bool focus=IsRequestedFocus(w,flow.Run,target,p,true);
                Collider[] bodyHits=Physics.OverlapCapsule(p+Vector3.up*.29f,p+Vector3.up*1.40f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                var goal=new RouteGoalAudit{position=p,focusAccepted=focus,bodyClear=bodyHits.Length==0,bodyBlockers=ColliderNames(bodyHits)};
                if(Physics.Linecast(p+Vector3.up*.8f,a.anchor+Vector3.up*.8f,out RaycastHit rayHit,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))
                    goal.focusRayBlocker=ColliderNames(new[]{rayHit.collider});
                if(probe!=null)for(int yaw=0;yaw<360;yaw+=45)
                {
                    Quaternion q=Quaternion.Euler(0,yaw,0);
                    if(w.ShoppingMotor.IsPoseClear(p,q))goal.clearCartHeadings++;
                    else if(goal.firstCartBlocker==null)goal.firstCartBlocker=probe.DescribeFrameBlock(p,q,Vector3.forward,0,0);
                }
                a.goals.Add(goal);
            }
            foreach(Collider c in Physics.OverlapSphere(body+Vector3.up*.6f,10,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))
                a.colliders.Add(new RouteColliderAudit{name=ColliderNames(new[]{c}),bounds=c.bounds,npc=c is CharacterController});
            return JsonUtility.ToJson(a,true);
        }
        private static string ColliderNames(Collider[] values)
        {
            var labels=new List<string>();foreach(Collider c in values){string path=c.name;for(Transform p=c.transform.parent;p;p=p.parent)path=p.name+"/"+path;labels.Add(path);}return string.Join(" | ",labels);
        }

        private static IEnumerator Guard(Session session, bool includeHome, Action<bool> completed)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>();
            stack.Push(session.Execute(includeHome));
            Exception failure = null;
            while (stack.Count > 0)
            {
                bool next = false; object current = null;
                try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null) break;
                if (!next) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
            session.Finish(failure);
            completed?.Invoke(failure == null);
        }

        private sealed class Session
        {
            private readonly GameFlow flow;
            private readonly string output;
            private readonly StringBuilder log = new StringBuilder("ACTUAL_TRAVERSAL_VALIDATION\n");
            private readonly StringBuilder trace = new StringBuilder("game_seconds,phase,target,x,y,z,heading,cart_x,cart_y,cart_z,input_dt,blocked\n");
            private readonly List<string> reached = new List<string>();
            private readonly Report report = new Report();
            private PlayerMotor controlledPlayer;
            private bool originalEnabled;
            private float nextTrace;
            private float homeVerticalSpeed;
            private string currentTarget = "";

            public Session(GameFlow game, string path) { flow = game; output = Path.GetFullPath(path); }

            public IEnumerator Execute(bool includeHome)
            {
                Need(flow != null && flow.World != null, "GameFlow/world is unavailable.");
                Need(!includeHome || flow.Run.Phase==GamePhase.Home,"ValidateRun must start in Home; use ValidateMarket for a Market-only run.");
                Need(Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-dir") < 0, "Do not run traversal and the teleport-based capture driver together.");
                flow.SetPaused(false);
                Note("Revision 3: no Teleport, ResetPoseForValidation or player-position assignment. Planning and two idle frames pause the clock; movement and cashier service use the normal clock. Per target: at most 3 blocked-input recoveries, 40 route segments, 60 seconds/80000 states per search.");
                if (includeHome && flow.Run.Phase == GamePhase.Home)
                {
                    Control(flow.World.Player);
                    yield return Visit("StovePickup");
                    flow.PrimaryAction(); Need(flow.Run.HoldingStove,"Home pickup failed.");
                    yield return Visit("HomeCart");
                    flow.PrimaryAction(); Need(flow.Run.StoveLoaded,"Home loading failed.");
                    yield return Visit("HomeExit");
                    flow.PrimaryAction();
                    Need(flow.Run.Phase==GamePhase.Market,"Home exit did not enter Market.");
                    yield return null;
                }
                Need(flow.Run.Phase==GamePhase.Market,"ValidateMarket requires Market phase.");
                Need(flow.Run.Cart.Count==0 && flow.Run.Cargo.Count==0 && !flow.Run.Paid && flow.Run.CashCents==32000,"Market must start with the untouched three-box sample.");
                Need(FlatDistance(flow.World.Player.transform.position,flow.World.Anchor("Spawn"))<.30f,"Market validation must begin at the actual Spawn.");
                Need(flow.World.ShoppingMotor!=null,"Missing linked trolley motor.");
                report.trolleyWidth=flow.World.ShoppingMotor.Width;
                Need(report.trolleyWidth>.84f && report.trolleyWidth<.92f,"Unexpected trolley width; expected the authored 0.88m body.");
                bool stoveWasLoaded=flow.Run.StoveLoaded;
                Control(flow.World.Player);
                // Read-only regression probe of the r2 log's failing segment. These
                // are historical diagnostic coordinates, never a movement destination.
                // The old log omitted dt, so the large-frame case is explicitly assumed.
                TrolleyProbe historicalProbe=new TrolleyProbe(flow.World);
                Vector3 historicalDirection=flow.World.View.transform.forward;
                historicalDirection.y=0;historicalDirection.Normalize();
                foreach(float assumedDt in new[]{.16f,1f/3f})
                    Note($"R2_READ_ONLY_REPLAY assumedDt={assumedDt:0.000000}, rounded log pose=(-10.49,0.03,-4.37)/98.13deg: "+historicalProbe.DescribeFrameBlock(new Vector3(-10.49f,.03f,-4.37f),Quaternion.Euler(0,98.13f,0),historicalDirection,flow.World.Player.speed,assumedDt));
                foreach (ItemDefinition item in GameCatalog.Items)
                {
                    yield return Visit(item.anchor);
                    flow.PrimaryAction();
                    Need(flow.Run.Cart.Contains(item.id),"Failed to pick up "+item.id+" at its reached shelf.");
                }
                Need(flow.Run.CartTotalCents==6120 && flow.Run.CashCents==32000,"Unpaid basket accounting changed during traversal.");
                yield return Visit("Cashier");
                flow.PrimaryAction(); Need(flow.Run.CheckoutInProgress,"Cashier did not begin manual service.");
                float waitDeadline=Time.realtimeSinceStartup+flow.Run.CheckoutDuration+5;
                while(flow.Run.CheckoutInProgress)
                {
                    Need(Time.realtimeSinceStartup<waitDeadline,"Cashier service did not finish.");
                    Need(flow.World.IsNear("Cashier"),"Assembly left the cashier during service.");
                    yield return null;
                }
                Need(flow.Run.Paid && flow.Run.CashCents==25880,"Manual service failed or charged the wrong amount.");
                yield return Visit("MarketCart");
                flow.PrimaryAction();
                Need(flow.Run.Cargo.Count==3 && flow.Run.Cart.Count==0 && flow.Run.StoveLoaded==stoveWasLoaded,"Loading failed or changed B04 ownership.");
                yield return Visit("MarketExit");
                flow.PrimaryAction();
                Need(flow.Run.Phase==GamePhase.Complete && !flow.Run.TimeoutSettlement,"Exit was not reached by actual movement before timeout.");
                Need(flow.Run.MarketUsed<=120,"Actual procurement travel/service exceeded 120 seconds.");
                report.marketUsedSeconds=flow.Run.MarketUsed;
                Note($"ACTUAL_TRAVERSAL_PASS: 3 shelves → manual service → truck → exit; {report.pathMetres:0.00}m, market {report.marketUsedSeconds:0.00}s, cart {report.trolleyWidth:0.00}m.");
            }

            private void Control(PlayerMotor player)
            {
                if(controlledPlayer!=null)controlledPlayer.enabled=originalEnabled;
                controlledPlayer=player;originalEnabled=player.enabled;
                homeVerticalSpeed=0;
                // Disable only the keyboard driver while this diagnostic owns commands.
                // CharacterController, trolley swept collision and all world colliders remain enabled.
                player.enabled=false;
            }

            private IEnumerator Visit(string target)
            {
                currentTarget=target;
                WorldRuntime world=flow.World;
                GamePhase phase=flow.Run.Phase;
                Need(world.HasAnchor(target),"Missing target "+target);
                int replans=0,recoveries=0;
                List<BlockedCommand> blockedCommands=new List<BlockedCommand>();
                TrolleyProbe probe=world.ShoppingMotor!=null?new TrolleyProbe(world):null;
                while(!IsRequestedFocus(world,flow.Run,target,world.Player.transform.position))
                {
                    AssertLive(phase,target);
                    Need(++replans<=40,"Too many route segments without reaching "+target);
                    float started=Time.realtimeSinceStartup;
                    bool actionAllowed=flow.Run.CanPrimary(target,true);
                    flow.SetPaused(true);
                    Search search=new Search(world,flow.Run,target,actionAllowed,probe,blockedCommands);
                    while(!search.Done)
                    {
                        // Batch physics queries while paused. The idle frames below
                        // isolate this diagnostic work from real input/clock timing.
                        search.Expand(100);
                        report.maxPlanningDeltaTime=Mathf.Max(report.maxPlanningDeltaTime,Time.deltaTime);
                        Need(Time.realtimeSinceStartup-started<60,"PLANNER_TIME_LIMIT: planning exceeded 60 seconds for "+target+"; no claim of physical unreachability is made.");
                        yield return null;
                    }
                    report.planningSeconds+=Time.realtimeSinceStartup-started;
                    Need(search.Path!=null&&search.Path.Count>1,"PLANNER_NO_ROUTE to "+target+" after "+search.Expanded+" states; this search failure alone does not prove physical unreachability.");
                    Node first=search.Path[1];
                    float duration=Search.ActionDuration;
                    for(int i=2;i<search.Path.Count&&search.Path[i].axis==first.axis&&duration<2;i++)duration+=Search.ActionDuration;
                    Note($"PLAN {phase}/{target}: {search.Path.Count} input poses, {search.Expanded} explored; executing axis {first.axis} for {duration:0.00}s.");
                    // Coroutine resumption sees the PREVIOUS frame's work in deltaTime.
                    // Two idle frames while still paused flush the planner's expensive frame.
                    yield return null;
                    yield return null;
                    float idleDt=Time.deltaTime;
                    flow.SetPaused(false);
                    yield return null;
                    Note($"EXECUTION_TIMING {phase}/{target}: idleDt={idleDt:0.000000}, firstDt={Time.deltaTime:0.000000}, maxPlanningDt={report.maxPlanningDeltaTime:0.000000}.");
                    float elapsed=0,windowSeconds=0,windowActual=0,windowExpected=0;
                    while(elapsed<duration&&!IsRequestedFocus(world,flow.Run,target,world.Player.transform.position))
                    {
                        AssertLive(phase,target);
                        float dt=Time.deltaTime;if(dt<=0){yield return null;continue;}
                        Vector3 before=world.Player.transform.position;
                        Quaternion beforeFacing=world.Player.visual.rotation;
                        float allowedSpeed=world.Player.EffectiveTopSpeed;
                        bool free=true;
                        if(world.ShoppingMotor!=null)
                        {
                            free=world.ShoppingMotor.TryMove(first.direction,allowedSpeed,dt);
                        }
                        else
                        {
                            if(world.Player.Controller.isGrounded&&homeVerticalSpeed<0)homeVerticalSpeed=-1.5f;
                            homeVerticalSpeed=Mathf.Max(-20,homeVerticalSpeed-25*dt);
                            world.Player.Controller.Move((first.direction*allowedSpeed+Vector3.up*homeVerticalSpeed)*dt);
                            world.Player.visual.rotation=Quaternion.Slerp(world.Player.visual.rotation,Quaternion.LookRotation(first.direction),16*dt);
                        }
                        elapsed+=dt;
                        report.movementCalls++;
                        report.maxMovementDeltaTime=Mathf.Max(report.maxMovementDeltaTime,dt);
                        float moved=FlatDistance(before,world.Player.transform.position);report.pathMetres+=moved;
                        // Headless input can run at thousands of frames per second;
                        // a sub-millimetre frame is normal, not evidence of stalling.
                        windowSeconds+=dt;windowActual+=moved;
                        windowExpected+=allowedSpeed*first.direction.magnitude*dt;
                        if(windowSeconds>=.5f)
                        {
                            float ratio=windowExpected>0?windowActual/windowExpected:1;
                            Note($"MOVEMENT_WINDOW {target}: seconds={windowSeconds:0.000000}, actual={windowActual:0.000000}m, expected={windowExpected:0.000000}m, speed={windowActual/windowSeconds:0.0000}m/s, ratio={ratio:0.0000}.");
                            // A Motor-reported block is handled below with the exact
                            // collider diagnostic and bounded recovery, even at this boundary.
                            Need(!free||ratio>=.10f,"Actual movement made less than 10% of expected progress across a >=0.5s window at "+before+" on route to "+target);
                            windowSeconds=0;windowActual=0;windowExpected=0;
                        }
                        Record(world,dt,!free);
                        if(!free)
                        {
                            report.blockedMoves++;
                            string reason=probe.DescribeFrameBlock(before,beforeFacing,first.direction,allowedSpeed,dt);
                            if(reason.StartsWith("CLEAR_FRAME",StringComparison.Ordinal))reason="PROBE_MISMATCH: Motor blocked but "+reason;
                            Vector3 accepted=world.Player.transform.position;
                            Note($"INPUT_MOVE_BLOCKED {target}: dt={dt:0.000000}, axis={first.axis}, before={before:F4}/{beforeFacing.eulerAngles.y:0.00}, accepted={accepted:F4}/{world.Player.visual.eulerAngles.y:0.00}, moved={moved:0.0000}, {reason}.");
                            Need(++recoveries<=3,"RECOVERY_LIMIT: "+target+" encountered a fourth blocked move. Actual collision remains enabled; this is a diagnostic route failure, not proof of physical unreachability.");
                            blockedCommands.Add(new BlockedCommand{position=accepted,facing=world.Player.visual.rotation,axis=first.axis});
                            Note($"RECOVERY {recoveries}/3: replanning from the actual partially accepted pose; excluding the same nearby blocked input.");
                            // No yield before the next planning pause: blocker diagnostics must
                            // not contribute a long unpaused frame to the game's clock.
                            break;
                        }
                        yield return null;
                    }
                }
                Need(world.IsNear(target),"Route ended outside interaction range for "+target);
                // Let the normal flow publish focus, then invoke its same local action.
                yield return new WaitForSeconds(.15f);
                Need(flow.State.targetId==target && flow.State.primaryEnabled,"Reached "+target+" but its normal primary action is unavailable: "+flow.State.contextPrompt);
                reached.Add(phase+"/"+target);
                Note($"REACHED {phase}/{target}: player={world.Player.transform.position}, remaining={flow.Run.Remaining:0.00}s, cartWidth={(world.ShoppingMotor!=null?world.ShoppingMotor.Width:0):0.00}m.");
            }

            private void AssertLive(GamePhase expected,string target)
            { Need(flow.Run.Phase==expected&&!flow.Run.TimeoutSettlement,"Phase ended before physically reaching "+target+": "+flow.Run.Message); }

            private void Record(WorldRuntime world,float dt,bool blocked)
            {
                if(!blocked&&Time.realtimeSinceStartup<nextTrace)return;nextTrace=Time.realtimeSinceStartup+.2f;
                Vector3 p=world.Player.transform.position,c=world.ShoppingMotor!=null?world.ShoppingMotor.Trolley.position:Vector3.zero;
                trace.AppendFormat(CultureInfo.InvariantCulture,"{0:F3},{1},{2},{3:F3},{4:F3},{5:F3},{6:F2},{7:F3},{8:F3},{9:F3},{10:F6},{11}\n",flow.Run.MarketUsed,flow.Run.Phase,currentTarget,p.x,p.y,p.z,world.Player.visual.eulerAngles.y,c.x,c.y,c.z,dt,blocked?1:0);
            }

            private void Note(string value){log.AppendLine(value);Debug.Log("TRAVERSAL "+value);}
            public void Finish(Exception failure)
            {
                if(controlledPlayer!=null)controlledPlayer.enabled=originalEnabled;
                if(flow!=null)flow.SetPaused(false);
                report.success=failure==null;report.failure=failure?.ToString()??"";report.reached=reached.ToArray();
                if(flow!=null)report.marketUsedSeconds=flow.Run.MarketUsed;
                if(failure!=null){log.AppendLine("ACTUAL_TRAVERSAL_FAIL: "+failure);Debug.LogError(log.ToString());}
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output,log.ToString());
                File.WriteAllText(Path.ChangeExtension(output,"json"),JsonUtility.ToJson(report,true));
                File.WriteAllText(Path.ChangeExtension(output,"csv"),trace.ToString());
            }
        }

        private sealed class Node
        {
            public int axis;
            public Vector3 position;
            public Vector3 direction;
            public Quaternion facing;
            public float g,f;
            public Node parent;
        }

        private sealed class BlockedCommand
        {
            public Vector3 position;
            public Quaternion facing;
            public int axis;
        }

        // Read-only reconstruction from the live trolley's collider and handle pose.
        // Uses the same 0.015m skin and exact sweep as LinkedTrolleyMotor; additional
        // clearance below is a PLANNING preference, never a change to live collision.
        private sealed class TrolleyProbe
        {
            private readonly LinkedTrolleyMotor motor;
            private readonly CustomerCrowd crowd;
            private readonly Vector3 offset,center,half;
            private readonly Quaternion relativeRotation;
            public TrolleyProbe(WorldRuntime world)
            {
                motor=world.ShoppingMotor;crowd=world.Crowd;
                BoxCollider box=motor.Trolley.GetComponent<BoxCollider>();
                Need(box!=null,"Trolley body collider is unavailable for sweep diagnostics.");
                Quaternion inverse=Quaternion.Inverse(motor.Facing);
                offset=inverse*(motor.Trolley.position-world.Player.transform.position);
                relativeRotation=inverse*motor.Trolley.rotation;
                center=box.center;half=box.size*.5f;
            }
            private void Box(Vector3 p,Quaternion q,out Vector3 c,out Quaternion rotation)
            {
                rotation=q*relativeRotation;
                c=p+q*offset+rotation*center;
            }
            public bool PoseClear(Vector3 p,Quaternion q,float extra)
            {
                if(!motor.IsPoseClear(p,q))return false;
                if(extra<=0)return true;
                Box(p,q,out Vector3 c,out Quaternion rotation);
                if(crowd!=null&&crowd.OverlapsBox(c,half+new Vector3(.015f+extra,0,.015f+extra),rotation))return false;
                return !Physics.CheckBox(c,half+new Vector3(.015f+extra,0,.015f+extra),rotation,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore)
                    && !Physics.CheckCapsule(p+Vector3.up*(.29f+extra),p+Vector3.up*(1.40f-extra),.25f+extra,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            }
            public bool SweepClear(Vector3 a,Quaternion qa,Vector3 b,Quaternion qb,float extra,out RaycastHit hit)
            {
                Box(a,qa,out Vector3 from,out Quaternion rotation);
                Box(b,qb,out Vector3 to,out _);
                Vector3 displacement=to-from;hit=default;
                return displacement.sqrMagnitude<.000001f||!Physics.BoxCast(from,half+new Vector3(.015f+extra,0,.015f+extra),displacement.normalized,out hit,rotation,displacement.magnitude,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            }
            public string DescribeFrameBlock(Vector3 start,Quaternion facing,Vector3 direction,float speed,float dt)
            {
                direction.y=0;direction=Vector3.ClampMagnitude(direction,1);
                bool reversing=Vector3.Dot(direction.normalized,facing*Vector3.forward)<-.5f;
                Quaternion desiredFacing=reversing?facing:Quaternion.RotateTowards(facing,Quaternion.LookRotation(direction),motor.turnDegreesPerSecond*dt);
                Vector3 desired=start+direction*speed*dt,previous=start;Quaternion previousFacing=facing;
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max((desired-start).magnitude/.025f,Quaternion.Angle(facing,desiredFacing)/.25f)));
                for(int i=1;i<=steps;i++)
                {
                    float t=i/(float)steps;Vector3 next=Vector3.Lerp(start,desired,t);
                    if(!Physics.Raycast(next+Vector3.up*.35f,Vector3.down,out RaycastHit ground,1,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))return "NO_GROUND at "+next.ToString("F4");
                    if(Mathf.Abs(ground.point.y-(start.y-.03f))>.20f)return "STEP_HEIGHT "+ColliderLabel(ground.collider)+" at "+next.ToString("F4");
                    next.y=ground.point.y+.03f;Quaternion q=Quaternion.Slerp(facing,desiredFacing,t);
                    Box(next,q,out Vector3 c,out Quaternion rotation);
                    if(crowd!=null&&crowd.OverlapsBox(c,half+new Vector3(.015f,0,.015f),rotation,out string npc))
                        return "NPC_NOMINAL_ENVELOPE "+npc+" sample="+i+"/"+steps+" center="+c.ToString("F4");
                    Collider[] boxHits=Physics.OverlapBox(c,half+new Vector3(.015f,0,.015f),rotation,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                    if(boxHits.Length>0)return "TROLLEY_OVERLAP "+ColliderLabels(boxHits)+" sample="+i+"/"+steps+" center="+c.ToString("F4");
                    Collider[] personHits=Physics.OverlapCapsule(next+Vector3.up*.29f,next+Vector3.up*1.40f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                    if(personHits.Length>0)return "PLAYER_OVERLAP "+ColliderLabels(personHits)+" sample="+i+"/"+steps;
                    if(!SweepClear(previous,previousFacing,next,q,0,out RaycastHit hit))return "TROLLEY_SWEEP "+ColliderLabel(hit.collider)+" sample="+i+"/"+steps+" point="+hit.point.ToString("F4");
                    previous=next;previousFacing=q;
                }
                return "CLEAR_FRAME: no blocker found in the reconstructed full frame";
            }
            private static string ColliderLabels(Collider[] colliders)
            {
                StringBuilder result=new StringBuilder();
                foreach(Collider collider in colliders){if(result.Length>0)result.Append(" | ");result.Append(ColliderLabel(collider));}
                return result.ToString();
            }
            private static string ColliderLabel(Collider collider)
            {
                string path=collider.name;for(Transform p=collider.transform.parent;p!=null;p=p.parent)path=p.name+"/"+path;
                return path+" bounds="+collider.bounds.min.ToString("F4")+".."+collider.bounds.max.ToString("F4");
            }
        }

        private sealed class Search
        {
            public const float ActionDuration=.16f;
            private const float Grid=.30f;
            private readonly WorldRuntime world;
            private readonly RunState run;
            private readonly string target;
            private readonly Vector3 origin,goal;
            private readonly Dictionary<string,float> best=new Dictionary<string,float>();
            private readonly List<Node> heap=new List<Node>();
            private readonly Vector3[] directions=new Vector3[8];
            private readonly float speed;
            private readonly bool primaryAllowed;
            private readonly TrolleyProbe probe;
            private readonly CarryCollisionHull carry;
            private readonly List<BlockedCommand> blockedCommands;
            private readonly float minX,maxX,minZ,maxZ;
            public bool Done{get;private set;}
            public List<Node> Path{get;private set;}
            public int Expanded{get;private set;}

            public Search(WorldRuntime runtime,RunState state,string targetId,bool actionAllowed,TrolleyProbe trolleyProbe,List<BlockedCommand> blockedInputs)
            {
                world=runtime;run=state;target=targetId;origin=world.Player.transform.position;goal=world.Anchor(target);
                primaryAllowed=actionAllowed;
                probe=trolleyProbe;blockedCommands=blockedInputs;
                carry=world.Player.carrying&&world.Player.CarryHull!=null&&world.Player.CarryHull.Active?world.Player.CarryHull:null;
                speed=world.Player.EffectiveTopSpeed;
                Vector3 forward=world.View.transform.forward,right=world.View.transform.right;
                forward.y=0;right.y=0;forward.Normalize();right.Normalize();
                Vector2[] axes={Vector2.up,new Vector2(1,1).normalized,Vector2.right,new Vector2(1,-1).normalized,Vector2.down,new Vector2(-1,-1).normalized,Vector2.left,new Vector2(-1,1).normalized};
                for(int i=0;i<axes.Length;i++)directions[i]=forward*axes[i].y+right*axes[i].x;
                minX=Mathf.Min(origin.x,goal.x)-30;maxX=Mathf.Max(origin.x,goal.x)+30;
                minZ=Mathf.Min(origin.z,goal.z)-30;maxZ=Mathf.Max(origin.z,goal.z)+30;
                Node start=new Node{axis=-1,position=origin,facing=probe!=null?world.ShoppingMotor.Facing:world.Player.visual.rotation,g=0};
                Push(start);
            }

            public void Expand(int budget)
            {
                while(budget-->0 && heap.Count>0)
                {
                    Node current=Pop();
                    if(best.TryGetValue(Key(current),out float known)&&current.g>known+.0001f)continue;
                    Expanded++;
                    if(IsGoal(current))
                    {
                        Path=new List<Node>();for(Node n=current;n!=null;n=n.parent)Path.Add(n);Path.Reverse();Done=true;return;
                    }
                    if(Expanded>80000){Done=true;return;}
                    for(int axis=0;axis<8;axis++)Add(current,axis);
                }
                if(heap.Count==0)Done=true;
            }

            private void Add(Node from,int axis)
            {
                foreach(BlockedCommand blocked in blockedCommands)
                    if(axis==blocked.axis&&FlatDistance(from.position,blocked.position)<.35f&&Quaternion.Angle(from.facing,blocked.facing)<20)return;
                Vector3 direction=directions[axis];
                Vector3 position=from.position+direction*speed*ActionDuration;
                Quaternion facing=Quaternion.LookRotation(direction);
                if(probe!=null)
                {
                    bool reversing=Vector3.Dot(direction,from.facing*Vector3.forward)<-.5f;
                    facing=reversing?from.facing:Quaternion.RotateTowards(from.facing,facing,world.ShoppingMotor.turnDegreesPerSecond*ActionDuration);
                }
                else if(carry!=null)
                    facing=Quaternion.Slerp(from.facing,facing,1-Mathf.Exp(-(world.Player.HeavyCarry?5:8)*ActionDuration));
                Node node=new Node{axis=axis,position=position,facing=facing,direction=direction,g=from.g+ActionDuration,parent=from};
                string key=Key(node);if(best.TryGetValue(key,out float old)&&node.g>=old-.0001f)return;
                if(position.x<minX||position.x>maxX||position.z<minZ||position.z>maxZ)return;
                if(!Ground(position,out float floor))return;position.y=floor+.03f;
                node.position=position;
                if(!PoseClear(position,facing)||!TransitionClear(from.position,from.facing,position,facing,direction))return;
                Push(node);
            }

            private bool TransitionClear(Vector3 a,Quaternion qa,Vector3 b,Quaternion qb,Vector3 direction)
            {
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(FlatDistance(a,b)/.08f,Quaternion.Angle(qa,qb)/1f)));
                Vector3 previous=a;Quaternion previousFacing=qa;
                bool reversing=Vector3.Dot(direction,qa*Vector3.forward)<-.5f;
                for(int i=1;i<=steps;i++)
                {
                    float t=i/(float)steps;Vector3 p=Vector3.Lerp(a,b,t);
                    if(!Ground(p,out float floor)||Mathf.Abs(floor-(a.y-.03f))>.2f)return false;
                    p.y=floor+.03f;
                    // Real repeated input finishes a short turn early; an entire-action
                    // Slerp would keep steering past that point and approve a different arc.
                    Quaternion q=probe==null?Quaternion.Slerp(qa,qb,t):reversing?qa:Quaternion.RotateTowards(qa,Quaternion.LookRotation(direction),world.ShoppingMotor.turnDegreesPerSecond*ActionDuration*t);
                    if(!PoseClear(p,q))return false;
                    if(probe!=null&&!probe.SweepClear(previous,previousFacing,p,q,Mathf.Min(Clearance(previous),Clearance(p)),out _))return false;
                    if(carry!=null&&!carry.CanSweep(previous,previousFacing,p,q))return false;
                    previous=p;previousFacing=q;
                }
                return true;
            }

            private bool PoseClear(Vector3 p,Quaternion q)
            {
                if(probe!=null)return probe.PoseClear(p,q,Clearance(p));
                if(carry!=null&&!carry.IsPoseClear(p,q))return false;
                // CharacterController can climb its existing stepOffset. This is
                // only the planner; actual Controller.Move must still cross the step.
                float bottom=.29f+world.Player.Controller.stepOffset;
                return !Physics.CheckCapsule(p+Vector3.up*bottom,p+Vector3.up*1.40f,.25f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            }
            // Leave room for normal frame-boundary overshoot and small steering
            // differences. Ramp from the REAL origin so a blocked pose can back out.
            private float Clearance(Vector3 p)=>.08f*Mathf.Clamp01(FlatDistance(origin,p)/.40f);
            private bool Ground(Vector3 p,out float floor)
            {
                if(Physics.Raycast(p+Vector3.up*.35f,Vector3.down,out RaycastHit hit,1f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore)){floor=hit.point.y;return true;}
                floor=0;return false;
            }
            private bool IsGoal(Node n)
            {
                return IsRequestedFocus(world,run,target,n.position,primaryAllowed);
            }
            private string Key(Node n)=>Mathf.RoundToInt((n.position.x-origin.x)/Grid)+"/"+Mathf.RoundToInt((n.position.z-origin.z)/Grid)+"/"+(probe==null&&carry==null?0:Mathf.RoundToInt(n.facing.eulerAngles.y/15f)%24);
            private void Push(Node n)
            {
                best[Key(n)]=n.g;n.f=n.g+Mathf.Max(0,FlatDistance(n.position,goal)-1.55f)/speed;
                heap.Add(n);int i=heap.Count-1;
                while(i>0){int parent=(i-1)/2;if(heap[parent].f<=n.f)break;heap[i]=heap[parent];i=parent;}heap[i]=n;
            }
            private Node Pop()
            {
                Node result=heap[0],last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);
                if(heap.Count==0)return result;int i=0;
                while(i*2+1<heap.Count){int child=i*2+1;if(child+1<heap.Count&&heap[child+1].f<heap[child].f)child++;if(heap[child].f>=last.f)break;heap[i]=heap[child];i=child;}heap[i]=last;return result;
            }
        }

        private static bool IsRequestedFocus(WorldRuntime world,RunState run,string target,Vector3 position,bool? primaryAllowed=null)
        {
            if(!(primaryAllowed??run.CanPrimary(target,true)))return false;
            var all=new List<string>();
            if(run.Phase==GamePhase.Market)
            {
                if(!string.IsNullOrEmpty(run.HeldGrocery))all.AddRange(new[]{GameCatalog.Find(run.HeldGrocery).anchor,"ShoppingCart"});
                else {foreach(var item in GameCatalog.Items)all.Add(item.anchor);all.AddRange(new[]{"Cashier","MarketCart","MarketExit","SupplyCounter"});}
            }
            else all.AddRange(run.HoldingStove?new[]{"HomeCart","StovePickup"}:run.StoveLoaded?new[]{"HomeExit"}:new[]{"StovePickup","HomeCart","HomeExit"});
            string[] candidates=all.ToArray();
            string nearest="";float best=float.MaxValue;
            foreach(string candidate in candidates)
            {
                if(!world.HasAnchor(candidate))continue;
                Vector3 anchor=world.Anchor(candidate);float distance=FlatDistance(position,anchor);
                if(distance>1.55f||distance>=best||Physics.Linecast(position+Vector3.up*.8f,anchor+Vector3.up*.8f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))continue;
                nearest=candidate;best=distance;
            }
            bool needsContact=target=="StovePickup"||target=="HomeCart"||target=="MarketCart"||target=="ShoppingCart"||Array.Exists(GameCatalog.Items,i=>i.anchor==target);
            if(nearest!=target||needsContact&&FlatDistance(position,world.Anchor(target))>.70f)return false;
            if(!world.CanPickUpAt(target,position))return false;
            if(!needsContact)return true;
            if(!world.TryInteractionContactAt(target,position,out Vector3 contact))return false;
            Vector3 to=contact-position;to.y=0;if(to.sqrMagnitude>1.65f*1.65f)return false;
            Vector3 side=world.Anchor(target)-contact;side.y=0;
            if(side.sqrMagnitude>.04f&&Vector3.Dot(position-contact,side.normalized)<.1f)return false;
            Vector3 ray=contact-(position+Vector3.up*1.55f);
            return !Physics.Raycast(position+Vector3.up*1.55f,ray.normalized,Mathf.Max(0,ray.magnitude-.28f),WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
        }
        private static float FlatDistance(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
