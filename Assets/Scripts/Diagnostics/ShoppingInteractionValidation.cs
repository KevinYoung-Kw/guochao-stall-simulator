using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao
{
    // Opt-in in a fresh Market. Uses the same DriveInput as hardware Update;
    // it does not set player/cart transforms or bypass collision/acceleration.
    public static class ShoppingInteractionValidation
    {
        [Serializable] public sealed class Report
        {
            public bool success, usedTeleport;
            public string failure="";
            public string inputSource="Shared PlayerMotor.DriveInput; scripted axes, not hardware keyboard";
            public float sprintPeak, carryPeak, cartWidth, pathMetres;
            public int movementFrames;
            public string[] completed=Array.Empty<string>();
        }
        private sealed class Node { public Vector3 position; public float cost,score; public Node parent; }
        private sealed class Session
        {
            public readonly GameFlow flow;
            public readonly Report report=new Report();
            public readonly List<string> completed=new List<string>();
            public PlayerMotor player;
            public bool oldEnabled;
            public GameObject testWall;
            public Session(GameFlow game){flow=game;}
            public void Need(bool value,string error){if(!value)throw new InvalidOperationException(error);}
            public void Mark(string step){completed.Add(step);Debug.Log("INTERACTION_CHECK "+step);}
            public IEnumerator Execute()
            {
                Need(flow!=null && flow.World!=null && flow.Run.Phase==GamePhase.Market,"Requires fresh Market, not a Home/operation scene.");
                Need(flow.Run.Cart.Count==0 && flow.Run.Cargo.Count==0 && string.IsNullOrEmpty(flow.Run.HeldGrocery),"Requires an empty fresh basket and hands.");
                player=flow.World.Player;oldEnabled=player.enabled;player.enabled=false;
                flow.SetPaused(false);flow.World.Sync(flow.Run);
                LinkedTrolleyMotor motor=flow.World.ShoppingMotor;
                Need(motor!=null && motor.IsAttached,"The fresh shopper must start at the trolley handle.");
                report.cartWidth=motor.Width;Need(report.cartWidth>=.87f,"Trolley footprint was reduced.");
                Vector3 cartAtStart=motor.Trolley.position;
                flow.ToggleShoppingCart();
                Need(!motor.IsAttached && player.LinkedTrolley==null && Vector3.Distance(cartAtStart,motor.Trolley.position)<.001f,"Parking changed the trolley pose.");
                Need(motor.Trolley.GetComponent<BoxCollider>().isTrigger==false,"Parked basket must remain solid.");
                Vector3 handle=motor.HandleStandingPoint;
                Mark("parked-solid-without-snap");
                string shelf=GameCatalog.Items[0].anchor;
                yield return Travel(flow.World.Anchor(shelf),true,false);
                Need(Vector3.Distance(cartAtStart,motor.Trolley.position)<.001f,"An unattended cart followed the shopper.");
                Need(report.sprintPeak>4.2f,"The free trip did not establish sprint speed; inspect route/acceleration.");
                Need(flow.IsActionInRange(shelf),"Reached shelf standing point but actual front/reach/LOS gate rejects it.");
                flow.PrimaryAction();
                if(GameFlow.PresentationEnabled)
                {
                    Need(string.IsNullOrEmpty(flow.Run.HeldGrocery),"Inventory changed before the contact frame.");
                    flow.PrimaryAction();flow.SetPaused(true);
                    float clock=flow.Run.Remaining;Vector3 position=player.transform.position;
                    yield return new WaitForSecondsRealtime(.3f);
                    Need(flow.Run.Remaining==clock && player.transform.position==position && string.IsNullOrEmpty(flow.Run.HeldGrocery),"Pause advanced pending pickup.");
                    flow.SetPaused(false);
                }
                yield return WaitAction();
                Need(flow.Run.HeldGrocery==GameCatalog.Items[0].id && flow.Run.Cart.Count==0,"Free pickup must hold exactly one box, not remotely add it to basket.");
                Mark("sprint-to-shelf-contact-once-and-hold");
                yield return Travel(handle,false,true);
                Need(report.carryPeak>1f && report.carryPeak<=player.boxCarrySpeed+.15f,"Held-box travel did not respect its speed limit.");
                Need(flow.IsActionInRange("ShoppingCart"),"Returned to handle but basket edge is not reachable.");
                flow.PrimaryAction();yield return WaitAction();
                Need(string.IsNullOrEmpty(flow.Run.HeldGrocery) && flow.Run.Cart.Count==1,"Basket deposit must preserve exactly one box.");
                Vector3 parked=motor.Trolley.position;
                flow.ToggleShoppingCart();
                Need(motor.IsAttached && Vector3.Distance(parked,motor.Trolley.position)<.001f,"Regripping snapped the cart or failed near its handle.");
                Mark("slow-carry-return-deposit-regrip-one-box");
                yield return RelocateLoadedToTurnSpace();
                // Select an actually clear 90-degree moving turn from the current
                // loaded pose; the check is bounded and never relaxes geometry.
                Vector3 forward=motor.Facing*Vector3.forward;
                Vector3 left=Quaternion.Euler(0,-90,0)*forward;
                Vector3 right=Quaternion.Euler(0,90,0)*forward;
                Vector3 turn=PreviewTurn(left,.75f)?left:PreviewTurn(right,.75f)?right:Vector3.zero;
                Need(turn.sqrMagnitude>.1f,"No loaded 90-degree arc fits beside the parked handle; move the test start to an open lane.");
                Quaternion beforeTurn=motor.Facing;
                yield return DriveFor(turn,.75f);
                Need(Quaternion.Angle(beforeTurn,motor.Facing)>35f,"Loaded trolley failed to steer along a previewed clear arc.");
                Need(motor.IsPoseClear(player.transform.position,motor.Facing),"Loaded turn ended overlapping environment.");
                Mark("loaded-moving-turn-full-footprint");
                forward=motor.Facing*Vector3.forward;
                Vector3 beforeReverse=player.transform.position;
                yield return DriveFor(-forward,.7f);
                Need(Vector3.Dot(player.transform.position-beforeReverse,-forward)>.35f,"Loaded trolley could not reverse away.");
                Need(flow.Run.Cart.Count==1 && string.IsNullOrEmpty(flow.Run.HeldGrocery),"Turn/reverse changed inventory.");
                Mark("loaded-reverse-and-inventory-conservation");
                Need(player.heavyCarrySpeed<player.boxCarrySpeed && player.boxCarrySpeed<player.speed && player.speed<player.sprintSpeed,
                    "Heavy stove / box / free / sprint speed tiers are not ordered.");
            }
            private IEnumerator WaitAction()
            {
                float deadline=Time.realtimeSinceStartup+5;
                while(flow.IsBusy){Need(Time.realtimeSinceStartup<deadline,"Interaction did not finish.");yield return null;}
                yield return null;
            }
            private void Drive(Vector3 direction,bool sprint)
            {
                Vector3 f=player.viewCamera.transform.forward,r=player.viewCamera.transform.right;f.y=0;r.y=0;f.Normalize();r.Normalize();
                Vector2 axes=new Vector2(Vector3.Dot(direction,r),Vector3.Dot(direction,f));
                Vector3 previous=player.transform.position;
                player.DriveInput(axes,sprint,Time.deltaTime);
                float speed=new Vector2(player.Velocity.x,player.Velocity.z).magnitude;
                if(sprint)report.sprintPeak=Mathf.Max(report.sprintPeak,speed);
                if(player.carrying)report.carryPeak=Mathf.Max(report.carryPeak,speed);
                report.pathMetres+=Vector3.Distance(previous,player.transform.position);report.movementFrames++;
            }
            private IEnumerator DriveFor(Vector3 direction,float seconds)
            {
                float elapsed=0;
                while(elapsed<seconds){Drive(direction,false);elapsed+=Time.deltaTime;yield return null;}
                Drive(Vector3.zero,false);
            }
            private IEnumerator RelocateLoadedToTurnSpace()
            {
                // The handle is parked near a doorway. A full-width cart need not
                // turn through 90 degrees there; drive it into an actual open aisle.
                // Reuse the existing read-only full trolley planner, then execute
                // its input axes through the production accelerated player driver.
                var motor=flow.World.ShoppingMotor;int noProgress=0;
                Vector2[] axes={Vector2.up,new Vector2(1,1).normalized,Vector2.right,new Vector2(1,-1).normalized,Vector2.down,new Vector2(-1,-1).normalized,Vector2.left,new Vector2(-1,1).normalized};
                string destination="Cashier";
                for(int attempt=0;attempt<20;attempt++)
                {
                    yield return DriveFor(Vector3.zero,.4f);
                    Vector3 forward=motor.Facing*Vector3.forward;
                    if(PreviewTurn(Quaternion.Euler(0,-90,0)*forward,.75f)||PreviewTurn(Quaternion.Euler(0,90,0)*forward,.75f))
                    {Mark("loaded-relocated-by-real-input-to-clear-turn-space");yield break;}
                    Need(flow.Run.Phase==GamePhase.Market,"Market ended before loaded turning test.");
                    flow.SetPaused(true);
                    var hint=JsonUtility.FromJson<TraversalValidation.KeyboardHint>(TraversalValidation.ReadOnlyKeyboardHint(flow,destination));
                    // Isolate planning work from the next formal movement frame.
                    yield return null;yield return null;flow.SetPaused(false);
                    if(hint.ready){destination=destination=="Cashier"?"RiceShelf":"Cashier";continue;}
                    Need(hint.axis>=0&&hint.axis<axes.Length,"Loaded relocation planner returned no legal input.");
                    Vector3 f=player.viewCamera.transform.forward,r=player.viewCamera.transform.right;f.y=r.y=0;f.Normalize();r.Normalize();
                    Vector3 direction=f*axes[hint.axis].y+r*axes[hint.axis].x;
                    Vector3 before=player.transform.position;
                    yield return DriveFor(direction,Mathf.Min(.48f,hint.seconds));
                    float moved=Horizontal(player.transform.position-before).magnitude;
                    noProgress=moved<.04f?noProgress+1:0;
                    Need(noProgress<3,"Loaded aisle relocation blocked three consecutive input segments; collision was not relaxed.");
                }
                Need(false,"Loaded relocation exhausted 20 bounded input segments without a clear turning pose; this is a diagnostic route failure, not proof the market is impassable.");
            }
            private bool PreviewTurn(Vector3 direction,float duration)
            {
                var motor=flow.World.ShoppingMotor;
                Vector3 p=player.transform.position;Quaternion q=motor.Facing;
                float speed=0;
                for(float t=0;t<duration;t+=.01f)
                {
                    speed=Mathf.MoveTowards(speed,player.trolleySpeed,4.5f*.01f);
                    p+=direction*speed*.01f;
                    q=Quaternion.RotateTowards(q,Quaternion.LookRotation(direction),motor.turnDegreesPerSecond*.01f);
                    if(!motor.IsPoseClear(p,q))return false;
                }
                return true;
            }
            private IEnumerator Travel(Vector3 goal,bool sprint,bool holding)
            {
                // Planning is paused so its CPU time does not consume shop time.
                flow.SetPaused(true);Physics.SyncTransforms();
                Vector3 origin=player.transform.position;
                var open=new List<Node>();var best=new Dictionary<Vector2Int,float>();
                Node root=new Node{position=origin};root.score=Horizontal(origin-goal).magnitude;open.Add(root);
                Node found=null;int expanded=0;
                while(open.Count>0 && expanded<12000)
                {
                    int at=0;for(int i=1;i<open.Count;i++)if(open[i].score<open[at].score)at=i;
                    Node node=open[at];open.RemoveAt(at);expanded++;
                    if(Horizontal(node.position-goal).magnitude<.30f){found=node;break;}
                    for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                    {
                        if(x==0&&z==0)continue;
                        Vector3 p=node.position+new Vector3(x*.4f,0,z*.4f);
                        if(Horizontal(p-origin).magnitude>40 || !FreeSegment(node.position,p,out p))continue;
                        Vector2Int key=new Vector2Int(Mathf.RoundToInt((p.x-origin.x)/.4f),Mathf.RoundToInt((p.z-origin.z)/.4f));
                        float cost=node.cost+Horizontal(p-node.position).magnitude;
                        if(best.TryGetValue(key,out float previous)&&previous<=cost)continue;
                        best[key]=cost;open.Add(new Node{position=p,cost=cost,score=cost+Horizontal(p-goal).magnitude,parent=node});
                    }
                    if(expanded%180==0)yield return null;
                }
                Need(found!=null,"Free shopper path planner failed (not proof of physical impossibility), expanded="+expanded);
                var path=new List<Vector3>();for(Node n=found;n!=null;n=n.parent)path.Add(n.position);path.Reverse();
                yield return null;yield return null;flow.SetPaused(false);
                int point=1;float started=flow.Run.MarketUsed,windowTime=0,windowDistance=0;
                while(point<path.Count)
                {
                    Need(flow.Run.Phase==GamePhase.Market,"Market timer ended during interaction route.");
                    Need(flow.Run.MarketUsed-started<45,"Free shopper route exceeded 45 game seconds.");
                    // Follow the furthest clear waypoint, so open aisles exercise
                    // sustained acceleration rather than braking at each grid cell.
                    int look=point;
                    for(int i=point+1;i<path.Count;i++)
                    {if(!FreeSegment(player.transform.position,path[i],out _))break;look=i;}
                    // Once a clear segment is chosen, earlier waypoints are behind
                    // the route cursor. Re-selecting them after rounding a corner
                    // made the diagnostic run back and forth along the same aisle.
                    point=look;
                    Vector3 delta=Horizontal(path[look]-player.transform.position);
                    if(delta.magnitude<.25f){point=look+1;continue;}
                    Vector3 before=player.transform.position;
                    bool final=look==path.Count-1;
                    float throttle=final?Mathf.Clamp(delta.magnitude/.9f,.12f,1):1;
                    Drive(delta.normalized*throttle,sprint&&!holding);
                    windowTime+=Time.deltaTime;windowDistance+=Horizontal(player.transform.position-before).magnitude;
                    if(windowTime>=.7f){Need(windowDistance/windowTime>.08f,"Free shopper movement stalled against real collision.");windowTime=windowDistance=0;}
                    yield return null;
                }
                // Stop through the production braking path, not a position reset.
                for(float t=0;t<.6f;t+=Time.deltaTime){Drive(Vector3.zero,false);yield return null;}
            }
            private bool FreeSegment(Vector3 from,Vector3 wanted,out Vector3 to)
            {
                to=wanted;
                if(!Physics.Raycast(wanted+Vector3.up*.35f,Vector3.down,out RaycastHit ground,1,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))return false;
                to.y=ground.point.y+.03f;if(Mathf.Abs(to.y-from.y)>.2f)return false;
                if(Physics.CheckCapsule(to+Vector3.up*.29f,to+Vector3.up*1.40f,.29f,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))return false;
                Vector3 delta=to-from;
                return !Physics.CapsuleCast(from+Vector3.up*.29f,from+Vector3.up*1.40f,.29f,delta.normalized,delta.magnitude,
                    WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            }
            private static Vector3 Horizontal(Vector3 value){value.y=0;return value;}
        }

        public static IEnumerator ValidateFreshMarket(GameFlow flow,string outputPath,Action<bool> completed=null)
        {
            var session=new Session(flow);var stack=new Stack<IEnumerator>();stack.Push(session.Execute());Exception failure=null;
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception error){failure=error;}
                if(failure!=null)break;
                if(!moved){stack.Pop();continue;}
                if(current is IEnumerator nested){stack.Push(nested);continue;}
                yield return current;
            }
            if(session.player!=null)session.player.enabled=session.oldEnabled;
            if(session.testWall!=null)UnityEngine.Object.Destroy(session.testWall);
            if(flow!=null)flow.SetPaused(false);
            session.report.success=failure==null;session.report.failure=failure?.ToString()??"";session.report.completed=session.completed.ToArray();
            string json=JsonUtility.ToJson(session.report,true);
            if(!string.IsNullOrEmpty(outputPath)){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));File.WriteAllText(outputPath,json);}
            if(failure==null)Debug.Log("SHOPPING_INTERACTION_PASS "+json);else Debug.LogError("SHOPPING_INTERACTION_FAIL "+json);
            completed?.Invoke(failure==null);
        }
    }
}
