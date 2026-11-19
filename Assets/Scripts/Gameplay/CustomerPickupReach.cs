using System;
using UnityEngine;

namespace Guochao
{
    // Measured receive pose: articulated torso and planted-foot weight shift.
    // Root movement remains exclusively owned by NpcNavigationSpace.
    [DefaultExecutionOrder(120)]
    public sealed class CustomerPickupReach : MonoBehaviour
    {
        const float ReachSeconds=.45f,RetractSeconds=.45f,ReachTimeout=1.4f,Tolerance=.025f;
        const float MaximumLeanDegrees=22f,MaximumWeightShift=.20f;
        Transform upper,lower,hand,head,spine,hips,target,package;
        Vector3 palmOffset,packageOffset;Quaternion palmFrame,packageFrame;
        Quaternion upperBefore,lowerBefore,handBefore,headBefore,spineBefore;
        Vector3 hipsBefore;float a,b,elapsed,afterContact,leanDegrees,weightShift;
        Vector3 shiftDirection;bool posed,headPosed,bodyPosed,active,committed,left,advanced;
        Action contact,finished,failed;CustomerServicePresentation presentation;
        sealed class Leg
        {public Transform upper,lower,foot;public float a,b;public Quaternion upperBefore,lowerBefore,footBefore;public Vector3 goal;public Quaternion rotation;}
        Leg leftLeg,rightLeg;
        public string ReachDiagnostic {get;private set;}="";
        public float LastContactError {get;private set;}=float.PositiveInfinity;
        public float LastFootError {get;private set;}
        public bool IsBusy=>active;
        public bool HasPackage=>package!=null;
        public Vector3 PalmPosition=>hand?hand.TransformPoint(palmOffset):transform.position;
        public Quaternion PalmRotation=>hand?hand.rotation*palmFrame:Quaternion.identity;
        public Vector3 ShoulderPosition=>upper?upper.position:transform.position+Vector3.up;
        public float MaximumReach=>a+b+(hand?Vector3.Scale(palmOffset,hand.lossyScale).magnitude:0)-.005f;
        public bool BindSide(bool useLeft)
        {
            UndoPose();left=useLeft;string side=left?"Left":"Right";
            upper=Bone(side+"Arm");lower=Bone(side+"ForeArm");hand=Bone(side+"Hand");head=Bone("Head");spine=Bone("Spine");hips=Bone("Hips");
            Transform index=Bone(side+"HandIndex1"),middle=Bone(side+"HandMiddle1"),pinky=Bone(side+"HandPinky1");
            if(!upper||!lower||!hand||!index||!middle||!pinky||!spine||!hips)return false;
            a=Vector3.Distance(upper.position,lower.position);b=Vector3.Distance(lower.position,hand.position);
            Vector3 finger=hand.InverseTransformDirection(middle.position-hand.position).normalized,span=hand.InverseTransformDirection(index.position-pinky.position).normalized;
            Vector3 normal=Vector3.Cross(finger,span).normalized;if(left)normal=-normal;
            palmOffset=hand.InverseTransformPoint((index.position+middle.position+pinky.position+hand.position)/4);
            palmFrame=Quaternion.LookRotation(finger,normal);presentation=GetComponent<CustomerServicePresentation>();
            leftLeg=BindLeg("Left");rightLeg=BindLeg("Right");return a>0&&b>0&&leftLeg!=null&&rightLeg!=null;
        }
        Leg BindLeg(string side)
        {
            var leg=new Leg{upper=Bone(side+"UpLeg"),lower=Bone(side+"Leg"),foot=Bone(side+"Foot")};
            if(!leg.upper||!leg.lower||!leg.foot)return null;
            leg.a=Vector3.Distance(leg.upper.position,leg.lower.position);leg.b=Vector3.Distance(leg.lower.position,leg.foot.position);return leg;
        }
        Vector3 LocalShoulder(float degrees)
        {
            Vector3 pivot=transform.InverseTransformPoint(spine.position),shoulder=transform.InverseTransformPoint(upper.position);
            return pivot+Quaternion.AngleAxis(degrees,Vector3.right)*(shoulder-pivot);
        }
        static Vector3 Shift(Vector3 direction,float amount)=>direction*amount-Vector3.up*(amount*.40f);
        bool LegsPermit(Vector3 displacement,Quaternion facing)
        {
            foreach(Leg leg in new[]{leftLeg,rightLeg})
            {
                Vector3 from=facing*transform.InverseTransformPoint(leg.upper.position)+displacement;
                Vector3 to=facing*transform.InverseTransformPoint(leg.foot.position);
                if(Vector3.Distance(from,to)>leg.a+leg.b-.004f)return false;
            }
            return true;
        }
        private bool PlanPose(Vector3 point,Quaternion palmRotation,Vector3 stand,Quaternion facing,out float degrees,out float shift,out Vector3 direction)
        {
            degrees=shift=0;direction=point-stand;direction.y=0;direction=direction.normalized;
            if(!upper||!spine||leftLeg==null||rightLeg==null)return false;
            Quaternion wristRotation=palmRotation*Quaternion.Inverse(palmFrame);
            Vector3 wrist=point-wristRotation*Vector3.Scale(palmOffset,hand.lossyScale);
            float nearest=float.PositiveInfinity;
            for(int step=0;step<=10;step++)
            {
                float amount=MaximumWeightShift*step/10f;Vector3 displacement=Shift(direction,amount);
                if(!LegsPermit(displacement,facing))continue;
                for(float angle=0;angle<=MaximumLeanDegrees;angle+=2f)
                {
                    Vector3 shoulder=stand+facing*LocalShoulder(angle)+displacement;
                    float distance=Vector3.Distance(shoulder,wrist);nearest=Mathf.Min(nearest,distance);
                    if(distance<=a+b-.012f)
                    {degrees=angle;shift=amount;ReachDiagnostic="wrist="+distance.ToString("F3")+" arm="+(a+b).ToString("F3")+" lean="+angle+" weightShift="+amount.ToString("F2");return true;}
                }
            }
            ReachDiagnostic="nearestWrist="+nearest.ToString("F3")+" arm="+(a+b).ToString("F3")+" maxLean="+MaximumLeanDegrees+" maxWeightShift="+MaximumWeightShift;return false;
        }
        public bool TryPlanApproach(Transform grip,float standZ,Func<Vector3,bool> clear,out Vector3 stand,out Quaternion facing)
        {
            stand=Vector3.zero;facing=Quaternion.identity;if(!upper||!grip)return false;
            Quaternion wristRotation=grip.rotation*Quaternion.Inverse(palmFrame);
            Vector3 wrist=grip.position-wristRotation*Vector3.Scale(palmOffset,hand.lossyScale);
            float best=float.PositiveInfinity;string diagnosis="";
            // Face obliquely, placing the real receiving shoulder next to the
            // container. Recheck collision for every candidate; no body shrink.
            foreach(float yaw in new[]{180f,160f,140f,120f,100f,200f,220f,240f,260f})
            {
                Quaternion rotation=Quaternion.Euler(0,yaw,0);
                Vector3 shoulder=rotation*LocalShoulder(12f);
                Vector3 candidate=new Vector3(wrist.x-shoulder.x,0,standZ);
                if(!clear(candidate))continue;
                if(!PlanPose(grip.position,grip.rotation,candidate,rotation,out float lean,out float shift,out _))continue;
                float score=shift*200+lean+Mathf.Abs(yaw-180)*.06f;
                if(score>=best)continue;
                best=score;stand=candidate;facing=rotation;diagnosis=ReachDiagnostic;
            }
            if(float.IsPositiveInfinity(best))return false;ReachDiagnostic=diagnosis;return true;
        }
        public bool Begin(Transform grip,bool useLeft,Action onContact,Action onFinished,Action onFailed)
        {
            if(active||grip==null||!BindSide(useLeft))return false;
            if(!PlanPose(grip.position,grip.rotation,transform.position,transform.rotation,out leanDegrees,out weightShift,out shiftDirection))return false;
            leftLeg.goal=leftLeg.foot.position;rightLeg.goal=rightLeg.foot.position;
            target=grip;contact=onContact;finished=onFinished;failed=onFailed;elapsed=afterContact=0;committed=false;active=true;LastContactError=float.PositiveInfinity;LastFootError=0;
            if(presentation){presentation.ExternalReceive=true;presentation.ExternalReceiveBusy=true;}return true;
        }
        public void Tick(float dt){if(active&&dt>0){advanced=true;elapsed+=dt;if(committed)afterContact+=dt;}}
        public void AcceptPackage(Transform actualPackage,Transform grip)
        {
            if(!active||actualPackage==null||grip==null)return;
            package=actualPackage;packageOffset=Quaternion.Inverse(package.rotation)*(grip.position-package.position);packageFrame=Quaternion.Inverse(package.rotation)*grip.rotation;
            package.SetParent(transform,true);package.gameObject.SetActive(true);UpdatePackage();
        }
        public void Cancel()
        {
            active=advanced=false;contact=finished=failed=null;target=null;UndoPose();
            if(presentation){presentation.ExternalReceiveBusy=false;if(package==null)presentation.ExternalReceive=false;}
        }
        void Update(){UndoPose();}
        void CaptureLeg(Leg leg)
        {leg.upperBefore=leg.upper.localRotation;leg.lowerBefore=leg.lower.localRotation;leg.footBefore=leg.foot.localRotation;leg.rotation=leg.foot.rotation;}
        void PlantLeg(Leg leg)
        {
            VendorWorkActor.SolveTwoBone(leg.upper,leg.lower,leg.foot,leg.goal,leg.upper.position+transform.forward*.7f,leg.a,leg.b);
            leg.foot.rotation=leg.rotation;LastFootError=Mathf.Max(LastFootError,Vector3.Distance(leg.foot.position,leg.goal));
        }
        void LateUpdate()
        {
            if(!active){UpdatePackage();return;}
            if(!target){Fail();return;}
            bool ticked=advanced;advanced=false;UndoPose();
            float weight=committed?1-Mathf.SmoothStep(0,1,Mathf.Clamp01(afterContact/RetractSeconds)):Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/ReachSeconds));
            hipsBefore=hips.localPosition;spineBefore=spine.localRotation;CaptureLeg(leftLeg);CaptureLeg(rightLeg);bodyPosed=true;
            hips.position+=Shift(shiftDirection,weightShift)*weight;
            PlantLeg(leftLeg);PlantLeg(rightLeg);
            spine.rotation=Quaternion.AngleAxis(leanDegrees*weight,transform.right)*spine.rotation;
            upperBefore=upper.localRotation;lowerBefore=lower.localRotation;handBefore=hand.localRotation;posed=true;
            Vector3 wanted=committed?Vector3.Lerp(target.position,PalmPosition,1-weight):Vector3.Lerp(PalmPosition,target.position,weight);
            Quaternion handRotation=target.rotation*Quaternion.Inverse(palmFrame);
            Vector3 wrist=wanted-handRotation*Vector3.Scale(palmOffset,hand.lossyScale);
            VendorWorkActor.SolveTwoBone(upper,lower,hand,wrist,transform.position+(left?-transform.right:transform.right)*.65f+Vector3.up*.8f,a,b);
            hand.rotation=handRotation;
            if(ticked&&!committed&&elapsed>=ReachSeconds)
            {
                float error=Vector3.Distance(PalmPosition,target.position);
                if(error<=Tolerance&&LastFootError<=Tolerance)
                {LastContactError=error;committed=true;afterContact=0;Action callback=contact;contact=null;callback?.Invoke();if(!active)return;}
                else if(elapsed>=ReachTimeout){LastContactError=error;ReachDiagnostic+=" measuredPalm="+error.ToString("F3")+" plantedFoot="+LastFootError.ToString("F3");Fail();return;}
            }
            if(committed&&head){headBefore=head.localRotation;headPosed=true;head.localRotation=headBefore*Quaternion.Euler(Mathf.Sin(Mathf.Clamp01(afterContact/RetractSeconds)*Mathf.PI)*7,0,0);}
            UpdatePackage();
            if(ticked&&committed&&afterContact>=RetractSeconds)
            {active=false;UndoPose();if(presentation)presentation.ExternalReceiveBusy=false;Action callback=finished;contact=finished=failed=null;callback?.Invoke();}
        }
        void UpdatePackage()
        {if(package&&hand){Quaternion rotation=PalmRotation*Quaternion.Inverse(packageFrame);package.SetPositionAndRotation(PalmPosition-rotation*packageOffset,rotation);}}
        void Fail(){Action callback=failed;Cancel();callback?.Invoke();}
        void RestoreLeg(Leg leg)
        {leg.upper.localRotation=leg.upperBefore;leg.lower.localRotation=leg.lowerBefore;leg.foot.localRotation=leg.footBefore;}
        void UndoPose()
        {
            if(posed){if(upper)upper.localRotation=upperBefore;if(lower)lower.localRotation=lowerBefore;if(hand)hand.localRotation=handBefore;posed=false;}
            if(headPosed&&head){head.localRotation=headBefore;headPosed=false;}
            if(bodyPosed){if(hips)hips.localPosition=hipsBefore;if(spine)spine.localRotation=spineBefore;RestoreLeg(leftLeg);RestoreLeg(rightLeg);bodyPosed=false;}
        }
        Transform Bone(string suffix)
        {foreach(var t in GetComponentsInChildren<Transform>(true)){string n=t.name;int at=n.LastIndexOf(':');if(at>=0)n=n.Substring(at+1);if(n==suffix||n=="mixamorig"+suffix)return t;}return null;}
        void OnDisable(){Cancel();}
    }
}
