using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class CarryCollisionAudit
    {
        public string item="",blockedBy="";
        public bool active,queryOverflow;
        public Vector3 localCenter,fullSize;
        public int blockedSteps,retreatSteps;
        public string[] penetrations=Array.Empty<string>();
    }

    // One stable volume for the object in the player's hands. The complete real
    // renderer envelope is measured in the final carry pose, before transfer tweening.
    // Both translation AND the swept corners of turning are constrained.
    public sealed class CarryCollisionHull : MonoBehaviour
    {
        public BoxCollider Volume {get;private set;}
        public bool Active=>payload&&Volume&&Volume.enabled;
        private PlayerMotor owner;
        private Transform payload;
        private BoxCollider paddedProbe;
        private Vector3 localCenter,halfSize;
        private readonly Collider[] overlaps=new Collider[128];
        private readonly RaycastHit[] hits=new RaycastHit[128];
        private readonly Dictionary<Collider,Penetration> initial=new Dictionary<Collider,Penetration>();
        private readonly Dictionary<Collider,Penetration> candidate=new Dictionary<Collider,Penetration>();
        private const float Skin=.012f;
        private bool overflow;
        private int blockedSteps,retreatSteps;
        private string blockedBy="";
        private struct Penetration {public float depth;public Vector3 separation;}

        public void Initialize(PlayerMotor player)
        {
            owner=player;
            var root=new GameObject("Held object full collision envelope");root.layer=25;
            root.transform.SetParent(player.transform,false);
            Volume=root.AddComponent<BoxCollider>();Volume.isTrigger=true;Volume.enabled=false;
            var probe=new GameObject("Query-only carry sweep probe");probe.layer=2;probe.transform.SetParent(player.transform,false);
            paddedProbe=probe.AddComponent<BoxCollider>();paddedProbe.isTrigger=true;paddedProbe.enabled=false;
        }

        // Predict the full held envelope for WorldRuntime.CanPickUpAt before
        // parenting the model. Bind below independently measures the actual
        // parented payload; neither path reduces the rendered bounds.
        public static bool TryPredictHeldEnvelope(GameObject model, Vector3 gripLocal, Transform visual, out Vector3 center, out Vector3 halfSize)
        {
            center=Vector3.zero;halfSize=Vector3.zero;
            if(model==null||visual==null)return false;
            Bounds bounds=new Bounds();bool found=false;
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled||(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer)))continue;
                bool proxy=false;for(Transform node=renderer.transform;node&&node!=model.transform.parent;node=node.parent)
                    if(node.name.StartsWith("Collider_",StringComparison.Ordinal)){proxy=true;break;}
                if(proxy)continue;
                Bounds local=renderer.localBounds;
                Matrix4x4 relative=model.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                for(int c=0;c<8;c++)
                {
                    Vector3 corner=local.center+Vector3.Scale(local.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));
                    Vector3 p=Vector3.Scale(gripLocal+Vector3.Scale(relative.MultiplyPoint3x4(corner),model.transform.localScale),visual.lossyScale);
                    if(found)bounds.Encapsulate(p);else{bounds=new Bounds(p,Vector3.zero);found=true;}
                }
            }
            if(!found)return false;
            center=bounds.center;halfSize=bounds.extents;
            return true;
        }
        public void Bind(Transform item)
        {
            if(payload==item){SyncPose();return;}
            payload=item;blockedBy="";initial.Clear();candidate.Clear();
            if(!item){Volume.enabled=false;paddedProbe.enabled=false;return;}
            bool any=false;Bounds bounds=new Bounds();
            Quaternion inverse=Quaternion.Inverse(owner.visual.rotation);
            foreach(Renderer renderer in item.GetComponentsInChildren<Renderer>(false))
            {
                if(!renderer.enabled||(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer)))continue;
                bool proxy=false;for(Transform node=renderer.transform;node&&node!=item.parent;node=node.parent)
                    if(node.name.StartsWith("Collider_",StringComparison.Ordinal)){proxy=true;break;}
                if(proxy)continue;
                Bounds source=renderer.localBounds;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 p=source.center+Vector3.Scale(source.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    p=inverse*(renderer.transform.TransformPoint(p)-owner.visual.position);
                    if(!any){bounds=new Bounds(p,Vector3.zero);any=true;}else bounds.Encapsulate(p);
                }
            }
            if(!any){payload=null;Volume.enabled=false;paddedProbe.enabled=false;return;}
            localCenter=bounds.center;halfSize=bounds.extents;
            Volume.center=localCenter;Volume.size=bounds.size;Volume.enabled=true;
            paddedProbe.center=localCenter;paddedProbe.size=bounds.size+Vector3.one*(Skin*2);paddedProbe.enabled=true;
            SyncPose();Physics.SyncTransforms();
        }
        public void SyncPose()
        {if(Volume&&owner&&owner.visual){Volume.transform.SetPositionAndRotation(owner.visual.position,owner.visual.rotation);if(paddedProbe)paddedProbe.transform.SetPositionAndRotation(owner.visual.position,owner.visual.rotation);}}
        private Vector3 Offset=>owner.visual.position-owner.transform.position;
        private Vector3 Center(Vector3 body,Quaternion facing)=>body+Offset+facing*localCenter;
        private bool Solid(Collider collider)=>collider&&collider.enabled&&!collider.isTrigger&&collider.gameObject.activeInHierarchy&&!collider.transform.IsChildOf(owner.transform);

        private bool ReadPenetrations(Vector3 body,Quaternion facing,Dictionary<Collider,Penetration> result,bool padded=true)
        {
            result.Clear();int count=Physics.OverlapBoxNonAlloc(Center(body,facing),halfSize+(padded?Vector3.one*Skin:Vector3.zero),overlaps,facing,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length){overflow=true;blockedBy="overlap-query-capacity";return false;}
            for(int i=0;i<count;i++)
            {
                Collider other=overlaps[i];if(!Solid(other))continue;
                if(Physics.ComputePenetration(padded?paddedProbe:Volume,body+Offset,facing,other,other.transform.position,other.transform.rotation,out Vector3 separation,out float depth)&&depth>.00001f)
                    result[other]=new Penetration{depth=depth,separation=separation};
            }
            return true;
        }
        public bool IsPoseClear(Vector3 body,Quaternion facing)
        {return !Active||ReadPenetrations(body,facing,candidate)&&candidate.Count==0;}

        // A newly picked crate can already touch its shelf. Such a state may ONLY
        // decrease every existing penetration, and may never acquire a new one.
        // This is an escape condition, not an exemption lasting until drop-off.
        private bool TryStep(Vector3 from,Quaternion facing,Vector3 displacement,Quaternion nextFacing,out Vector3 normal,bool allowEscape)
        {
            normal=Vector3.zero;Vector3 next=from+displacement;
            if(!ReadPenetrations(from,facing,initial)||!ReadPenetrations(next,nextFacing,candidate))return false;
            foreach(var pair in candidate)
            {
                if(!allowEscape||!initial.TryGetValue(pair.Key,out Penetration old)||pair.Value.depth>=old.depth-.000001f)
                {blockedBy=pair.Key.name;normal=pair.Value.separation;return false;}
            }
            foreach(var pair in initial)
                if(displacement.sqrMagnitude>.00000001f&&Vector3.Dot(displacement,pair.Value.separation)<-.000001f)
                {blockedBy=pair.Key.name;normal=pair.Value.separation;return false;}
            Vector3 a=Center(from,facing),b=Center(next,nextFacing),delta=b-a;float length=delta.magnitude;
            if(length>.000001f)
            {
                int count=Physics.BoxCastNonAlloc(a,halfSize+Vector3.one*Skin,delta/length,hits,facing,length,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                if(count==hits.Length){overflow=true;blockedBy="cast-query-capacity";return false;}
                for(int i=0;i<count;i++)
                {
                    Collider other=hits[i].collider;if(!Solid(other)||initial.ContainsKey(other))continue;
                    // A zero-distance skin contact is only allowed when moving
                    // away. This permits retreat from a clear resting contact.
                    if(hits[i].distance<=.0001f)
                    {
                        Vector3 away=a-other.ClosestPoint(a);
                        if(Vector3.Dot(delta,away)>.00000001f)continue;
                    }
                    blockedBy=other.name;normal=hits[i].normal;return false;
                }
            }
            // Predict the body's horizontal sweep too: CharacterController.Move
            // must not redirect the accepted payload into an untested corner.
            if(displacement.sqrMagnitude>.00000001f)
            {
                var body=owner.Controller;Vector3 center=from+body.center;
                float half=Mathf.Max(0,body.height*.5f-body.radius);
                int count=Physics.CapsuleCastNonAlloc(center+Vector3.up*half,center-Vector3.up*half,body.radius,
                    displacement.normalized,hits,displacement.magnitude+Skin,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore);
                if(count==hits.Length){overflow=true;blockedBy="body-query-capacity";return false;}
                for(int i=0;i<count;i++)
                {
                    Collider other=hits[i].collider;if(!Solid(other))continue;
                    // Low thresholds retain the controller's existing step rule;
                    // the held volume is above them and remains independently swept.
                    if(other.bounds.max.y<=from.y+body.stepOffset)continue;
                    if(hits[i].distance<=.0001f&&Vector3.Dot(displacement,hits[i].normal)>.00001f)continue;
                    blockedBy=other.name;normal=hits[i].normal;return false;
                }
            }
            if(allowEscape&&initial.Count>0)retreatSteps++;
            return true;
        }
        private int Steps(Vector3 translation,Quaternion from,Quaternion to)
        {
            // At most 0.008 m of corner-arc motion per yaw sample, covered by the
            // unchanged .012 m skin; checking only the two end rotations is unsafe.
            float orbit=localCenter.magnitude+halfSize.magnitude;
            float degrees=Mathf.Min(.75f,.008f/Mathf.Max(.05f,orbit)*Mathf.Rad2Deg);
            return Mathf.Max(1,Mathf.CeilToInt(Mathf.Max(translation.magnitude/.02f,Quaternion.Angle(from,to)/degrees)));
        }
        public bool CanSweep(Vector3 from,Quaternion facing,Vector3 to,Quaternion nextFacing)
        {
            if(!Active)return true;int steps=Steps(to-from,facing,nextFacing);Vector3 previous=from;Quaternion previousFacing=facing;
            for(int i=1;i<=steps;i++)
            {
                Vector3 next=Vector3.Lerp(from,to,i/(float)steps);Quaternion rotation=Quaternion.Slerp(facing,nextFacing,i/(float)steps);
                if(!TryStep(previous,previousFacing,next-previous,rotation,out _,false))return false;
                previous=next;previousFacing=rotation;
            }
            return true;
        }
        public void Drive(Vector3 displacement,Quaternion desiredFacing)
        {
            if(!Active)return;SyncPose();Physics.SyncTransforms();blockedBy="";
            Quaternion startFacing=owner.visual.rotation;int steps=Steps(displacement,startFacing,desiredFacing);
            Vector3 translation=displacement/steps;float turn=Quaternion.Angle(startFacing,desiredFacing)/steps;
            for(int i=0;i<steps;i++)
            {
                Vector3 at=owner.transform.position;Quaternion facing=owner.visual.rotation;
                Quaternion nextFacing=Quaternion.RotateTowards(facing,desiredFacing,turn);Vector3 accepted=translation;Quaternion acceptedFacing=nextFacing;
                if(!TryStep(at,facing,translation,nextFacing,out Vector3 normal,true))
                {
                    blockedSteps++;
                    if(TryStep(at,facing,translation,facing,out Vector3 slideNormal,true))acceptedFacing=facing;
                    else
                    {
                        if(slideNormal.sqrMagnitude>.001f)normal=slideNormal;
                        Vector3 slide=Vector3.ProjectOnPlane(translation,normal);slide.y=0;
                        if(slide.sqrMagnitude>.00000001f&&Vector3.Dot(slide,translation)>0&&TryStep(at,facing,slide,facing,out _,true))
                        {accepted=slide;acceptedFacing=facing;}
                        else if(Quaternion.Angle(facing,nextFacing)>.001f&&TryStep(at,facing,Vector3.zero,nextFacing,out _,true))
                        {accepted=Vector3.zero;acceptedFacing=nextFacing;}
                        else break;
                    }
                }
                owner.Controller.Move(accepted);owner.visual.rotation=acceptedFacing;SyncPose();Physics.SyncTransforms();
            }
            // Repeated float RotateTowards calls may leave a fraction of one
            // degree. Complete only that tiny residual after its complete sweep.
            if(Quaternion.Angle(owner.visual.rotation,desiredFacing)<.5f&&CanSweep(owner.transform.position,owner.visual.rotation,owner.transform.position,desiredFacing))
            {owner.visual.rotation=desiredFacing;SyncPose();Physics.SyncTransforms();}
        }
        public CarryCollisionAudit CaptureAudit()
        {
            var list=new List<string>();if(Active&&ReadPenetrations(owner.transform.position,owner.visual.rotation,candidate,false))
                foreach(var pair in candidate)list.Add(pair.Key.name+":"+pair.Value.depth.ToString("F5"));
            return new CarryCollisionAudit{item=payload?payload.name:"",active=Active,queryOverflow=overflow,localCenter=localCenter,fullSize=halfSize*2,blockedBy=blockedBy,blockedSteps=blockedSteps,retreatSteps=retreatSteps,penetrations=list.ToArray()};
        }
    }
}
