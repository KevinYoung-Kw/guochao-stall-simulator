using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    public sealed class NpcNavigationAgent
    {
        public string Id {get;internal set;}
        public Transform Body {get;internal set;}
        public CharacterController Controller {get;internal set;}
        public NpcBodyProfile Profile {get;internal set;}
        public float Travelled {get;internal set;}
        public float BlockedSeconds {get;internal set;}
        public string BlockedReason {get;internal set;}="";
        public bool Blocked=>BlockedSeconds>0;
        public int Replans {get;internal set;}
        public Vector3 Velocity {get;internal set;}
        public float TurnRate=190;
        public bool AllowDynamicDetours=true;
        // Optional scene circulation rule, never used to relax collision checks.
        // Street guests leave this unset; market traffic restricts its west aisle.
        public Func<Vector3,Vector3,bool> TraversalAllowed;
        internal readonly List<Vector3> Path=new List<Vector3>();
        internal int PathIndex,Priority;
        internal bool HasGoal;
        internal Vector3 Goal;
        internal float RetryIn;
    }

    // One navigation space per physical scene. Destinations/queue slots are owned
    // by behaviour, while this layer owns swept motion and obstacle-aware paths.
    // It never warps, shrinks colliders, pushes through a neighbour, or skips a goal.
    public sealed class NpcNavigationSpace
    {
        private readonly Bounds area;
        private readonly int solidMask;
        private int queryMask;
        private readonly List<NpcNavigationAgent> agents=new List<NpcNavigationAgent>();
        private readonly Dictionary<Collider,bool> external=new Dictionary<Collider,bool>();
        private readonly Dictionary<BoxCollider,float> boxClearances=new Dictionary<BoxCollider,float>();
        private readonly Collider[] overlaps=new Collider[192];
        private readonly RaycastHit[] hits=new RaycastHit[192];
        private readonly List<NpcPathPoint> searchResult=new List<NpcPathPoint>();
        private const float GroundClearance=.03f;
        public int Count=>agents.Count;
        public string[] CaptureOverlaps()
        {
            BeginFrame();var failures=new List<string>();
            foreach(var agent in agents)
                if(agent.Body&&agent.Body.gameObject.activeInHierarchy&&!ClearPoint(agent.Body.position,agent.Profile,agent,true,out string reason,false))
                    failures.Add(agent.Id+": "+reason);
            return failures.ToArray();
        }
        public int DeniedMoves {get;private set;}
        public int SearchFailures {get;private set;}
        public bool QueryOverflow {get;private set;}
        public NpcNavigationSpace(Bounds walkableBounds,int solidMask)
        {area=walkableBounds;this.solidMask=solidMask;queryMask=solidMask;}
        public void AddObstacle(Collider collider,bool includeTrigger=false,float planarClearance=0)
        {
            if(!collider)return;external[collider]=includeTrigger;queryMask|=1<<collider.gameObject.layer;
            if(planarClearance>0)
            {
                if(!(collider is BoxCollider box))throw new ArgumentException("Planar clearance requires an upright box");
                boxClearances[box]=planarClearance;
            }
            foreach(var agent in agents)ConfigureLayers(agent.Controller);
        }
        public void BeginFrame(){Physics.SyncTransforms();}
        private void ConfigureLayers(CharacterController controller)
        {
            // Physics queries AND CharacterController.Move must use the same scene.
            controller.includeLayers=queryMask;controller.excludeLayers=~queryMask;
        }
        public bool TryRegister(string instanceId,string archetypeId,Transform root,int solidLayer,out NpcNavigationAgent agent)
        {
            agent=null;var shape=NpcBodyProfile.ForArchetype(archetypeId);
            foreach(var existing in agents)if(existing.Id==instanceId)throw new ArgumentException("Duplicate NPC instance "+instanceId);
            if(!CanOccupy(root.position,shape))return false;
            root.gameObject.layer=solidLayer;
            var controller=root.gameObject.AddComponent<CharacterController>();controller.radius=shape.Radius;
            controller.height=shape.Height;controller.center=Vector3.up*(GroundClearance+shape.Height*.5f);
            controller.stepOffset=0;controller.slopeLimit=0;controller.skinWidth=.015f;controller.minMoveDistance=0;
            controller.enableOverlapRecovery=false;ConfigureLayers(controller);
            agent=new NpcNavigationAgent{Id=instanceId,Body=root,Controller=controller,Profile=shape,Priority=agents.Count};agents.Add(agent);Physics.SyncTransforms();return true;
        }
        public bool OverlapsBox(Vector3 center,Vector3 halfExtents,Quaternion rotation,out string blocker)
        {
            foreach(var agent in agents)
            {
                if(!agent.Body||!agent.Body.gameObject.activeInHierarchy)continue;
                Capsule(agent.Body.position,agent.Profile,out Vector3 lower,out Vector3 upper);
                if(NpcCapsuleGeometry.IntersectsBox(lower,upper,agent.Profile.Radius,center,halfExtents,rotation))
                {blocker=agent.Id;return true;}
            }
            blocker="";return false;
        }
        public void Remove(NpcNavigationAgent agent)
        {if(agent==null)return;agents.Remove(agent);if(agent.Controller)UnityEngine.Object.Destroy(agent.Controller);}
        public void ResetAfterSaveRestore(NpcNavigationAgent agent)
        {
            // Rebuild cached paths from the restored pose. This method never
            // changes a transform and is not a stuck-agent recovery mechanism.
            agent.Path.Clear();agent.PathIndex=0;agent.HasGoal=false;agent.RetryIn=0;
            agent.BlockedSeconds=0;agent.BlockedReason="";agent.Velocity=Vector3.zero;
        }
        public bool CanOccupy(Vector3 point,NpcBodyProfile shape,NpcNavigationAgent ignore=null)
        {return ClearPoint(point,shape,ignore,true,out _);}
        public bool CanReachDirect(NpcNavigationAgent agent,Vector3 destination)
        {return Sweep(agent.Body.position,destination,agent.Profile,agent,true,out _);}
        public bool HasArrived(NpcNavigationAgent agent,Vector3 destination,float tolerance=.07f)
        {Vector3 delta=destination-agent.Body.position;delta.y=0;return delta.sqrMagnitude<=tolerance*tolerance;}
        private bool InArea(Vector3 p,NpcBodyProfile shape)=>
            p.x-shape.Radius>=area.min.x&&p.x+shape.Radius<=area.max.x&&p.z-shape.Radius>=area.min.z&&p.z+shape.Radius<=area.max.z;
        private static void Capsule(Vector3 p,NpcBodyProfile shape,out Vector3 lower,out Vector3 upper)
        {
            lower=p+Vector3.up*(GroundClearance+shape.Radius);
            upper=p+Vector3.up*(GroundClearance+Mathf.Max(shape.Radius,shape.Height-shape.Radius));
        }
        private bool IsSolid(Collider collider,float footY)
        {
            if(!collider||!collider.enabled||!collider.gameObject.activeInHierarchy)return false;
            foreach(var other in agents)if(collider==other.Controller)return false; // Analytic live swept footprints below.
            if(collider.bounds.max.y<=footY+GroundClearance+.005f)return false; // Standing floor, not furniture.
            if(external.TryGetValue(collider,out bool allowTrigger))return allowTrigger||!collider.isTrigger;
            return !collider.isTrigger&&(solidMask&(1<<collider.gameObject.layer))!=0;
        }
        private bool ClearPoint(Vector3 p,NpcBodyProfile shape,NpcNavigationAgent ignore,bool includeAgents,out string reason,bool checkClearance=true)
        {
            reason="";if(!InArea(p,shape)){reason="walkable-boundary";return false;}
            if(includeAgents)foreach(var other in agents)
            {
                if(other==ignore||!other.Body||!other.Body.gameObject.activeInHierarchy)continue;
                if(!NpcGridPathfinding.DiscSweepClear(p.x,p.z,p.x,p.z,shape.Radius,other.Body.position.x,other.Body.position.z,other.Profile.Radius))
                {reason=other.Id;return false;}
            }
            Capsule(p,shape,out Vector3 lower,out Vector3 upper);
            int count=Physics.OverlapCapsuleNonAlloc(lower,upper,shape.Radius,overlaps,queryMask,QueryTriggerInteraction.Collide);
            if(count==overlaps.Length){QueryOverflow=true;reason="query-overflow";return false;}
            for(int i=0;i<count;i++)if(IsSolid(overlaps[i],p.y)){reason=overlaps[i].name;return false;}
            if(checkClearance&&!PaddedBoxSweepClear(p,p,shape,out reason))return false;
            return true;
        }
        private bool Sweep(Vector3 from,Vector3 to,NpcBodyProfile shape,NpcNavigationAgent ignore,bool includeAgents,out string reason)
        {
            if(ignore!=null&&ignore.TraversalAllowed!=null&&!ignore.TraversalAllowed(from,to))
            {reason="scene-right-of-way";return false;}
            if(!ClearPoint(to,shape,ignore,includeAgents,out reason,false))return false;
            if(!PaddedBoxSweepClear(from,to,shape,out reason))return false;
            if(includeAgents)foreach(var other in agents)
            {
                if(other==ignore||!other.Body||!other.Body.gameObject.activeInHierarchy)continue;
                if(!NpcGridPathfinding.DiscSweepClear(from.x,from.z,to.x,to.z,shape.Radius,other.Body.position.x,other.Body.position.z,other.Profile.Radius))
                {reason=other.Id;return false;}
            }
            Vector3 delta=to-from;delta.y=0;float distance=delta.magnitude;if(distance<.00001f)return true;
            Capsule(from,shape,out Vector3 lower,out Vector3 upper);
            int count=Physics.CapsuleCastNonAlloc(lower,upper,shape.Radius,delta/distance,hits,distance,queryMask,QueryTriggerInteraction.Collide);
            if(count==hits.Length){QueryOverflow=true;reason="query-overflow";return false;}
            for(int i=0;i<count;i++)if(IsSolid(hits[i].collider,from.y)){reason=hits[i].collider.name;return false;}
            return true;
        }
        private bool PaddedBoxSweepClear(Vector3 from,Vector3 to,NpcBodyProfile shape,out string reason)
        {
            reason="";
            foreach(var entry in boxClearances)
            {
                BoxCollider box=entry.Key;if(!IsSolid(box,from.y))continue;
                Vector3 scale=box.transform.lossyScale;scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                Vector3 half=Vector3.Scale(box.size*.5f,scale)+new Vector3(entry.Value,0,entry.Value);
                Capsule(from,shape,out Vector3 lower,out Vector3 upper);
                if(!NpcCapsuleGeometry.UprightSweepClear(lower,upper,to-from,shape.Radius,box.transform.TransformPoint(box.center),half,box.transform.rotation))
                {reason=box.name+"-clearance";return false;}
            }
            return true;
        }

        private bool Plan(NpcNavigationAgent agent,bool includeAgents)
        {
            agent.Replans++;agent.Path.Clear();agent.PathIndex=0;Vector3 start=agent.Body.position,goal=agent.Goal;goal.y=start.y;
            bool okay=NpcGridPathfinding.TryFind(new NpcPathPoint(start.x,start.z),new NpcPathPoint(goal.x,goal.z),
                new NpcPathPoint(area.min.x,area.min.z),new NpcPathPoint(area.max.x,area.max.z),.35f,
                (x,z)=>ClearPoint(new Vector3(x,start.y,z),agent.Profile,agent,includeAgents,out _),
                (ax,az,bx,bz)=>Sweep(new Vector3(ax,start.y,az),new Vector3(bx,start.y,bz),agent.Profile,agent,includeAgents,out _),searchResult,out _);
            if(!okay){SearchFailures++;return false;}
            foreach(var point in searchResult)agent.Path.Add(new Vector3(point.X,start.y,point.Z));return true;
        }
        public float Step(NpcNavigationAgent agent,Vector3 destination,float speed,float dt)
        {
            if(agent==null||!agent.Body||!agent.Body.gameObject.activeInHierarchy||dt<=0)return 0;
            speed=Mathf.Max(0,speed);dt=Mathf.Min(dt,.2f);Vector3 frameStart=agent.Body.position;
            destination.y=frameStart.y;
            if(!agent.HasGoal||(agent.Goal-destination).sqrMagnitude>.0064f)
            {agent.HasGoal=true;agent.Goal=destination;agent.Path.Clear();agent.PathIndex=0;agent.RetryIn=0;agent.BlockedSeconds=0;}
            if(HasArrived(agent,destination)){agent.Velocity=Vector3.zero;agent.BlockedSeconds=0;agent.BlockedReason="";return 0;}
            float left=dt;
            while(left>.00001f)
            {
                float tick=Mathf.Min(left,1f/30f);left-=tick;agent.RetryIn-=tick;
                if(agent.PathIndex>=agent.Path.Count)
                {
                    if(agent.RetryIn<=0){Plan(agent,agent.AllowDynamicDetours&&agent.BlockedSeconds>.25f);agent.RetryIn=.65f+.07f*(agent.Priority%3);}
                    if(agent.PathIndex>=agent.Path.Count){Block(agent,tick,"no-clear-path");continue;}
                }
                Vector3 point=agent.Body.position,target=agent.Path[agent.PathIndex],delta=target-point;delta.y=0;
                if(delta.magnitude<.055f){agent.PathIndex++;continue;}
                Vector3 proposed=Vector3.MoveTowards(point,target,speed*tick);proposed.y=point.y;
                if(!Sweep(point,proposed,agent.Profile,agent,true,out string reason))
                {
                    Block(agent,tick,reason);
                    // Delayed, staggered replans avoid oscillation. A blocked queue
                    // target stays blocked; it is never reassigned or skipped here.
                    if(agent.AllowDynamicDetours&&agent.BlockedSeconds>.35f+.12f*(agent.Priority%3)&&agent.RetryIn<=0)
                    {Plan(agent,true);agent.RetryIn=.75f+.09f*(agent.Priority%3);}
                    continue;
                }
                agent.Controller.Move(proposed-point);Physics.SyncTransforms();
                Vector3 moved=agent.Body.position-point;moved.y=0;
                if(moved.sqrMagnitude<.0000001f){Block(agent,tick,"controller-blocked");continue;}
                agent.Body.rotation=Quaternion.RotateTowards(agent.Body.rotation,Quaternion.LookRotation(moved),agent.TurnRate*tick);
                agent.Travelled+=moved.magnitude;agent.BlockedSeconds=0;agent.BlockedReason="";
            }
            Vector3 displacement=agent.Body.position-frameStart;displacement.y=0;agent.Velocity=displacement/dt;return displacement.magnitude;
        }
        private void Block(NpcNavigationAgent agent,float dt,string reason)
        {agent.BlockedSeconds+=dt;agent.BlockedReason=reason;agent.Velocity=Vector3.zero;DeniedMoves++;}
    }
}
