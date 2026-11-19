using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    public enum VendorWorkAction { PickIngredient,PourIngredient,StirFry,Flip,Scoop,Season,Pack,Serve,Clean }
    public enum VendorWorkPhase { Idle,Moving,Turning,Anticipation,Contact,Working,Recovery,Failed }
    [Serializable] public sealed class VendorWorkTarget
    {
        public Vector3 standPoint,workPoint;
        // Grip forward = finger/handle direction; up = back of the hand.
        public Transform leftGrip,rightGrip;
        public Transform heldTool,toolGrip;
        public Vector3[] route;
        public bool useLeftHand;
    }
    [Serializable] public sealed class VendorWorkAudit
    {
        public bool bound,busy,paused,contactCommitted,carryingVisualProp;
        public string action,phase,lastFailure,footErrorDetail;
        public int requestId,contacts;
        public float elapsed,duration,pathMetres,rightError,leftError,maxContactError,maxWorkError,maxPlantedFootError,maxPelvisDrop,maxMovingFootError,maxWorkingFootError;
        public Vector3 rootPosition,rightPalm,rightTarget,leftPalm,leftTarget;
    }

    // Visual execution only: the controller owns every inventory/order transaction.
    // Uses the existing generic/legacy clips plus analytic two-bone IK; no Avatar
    // conversion, finger deformation, bone scaling, or root teleport is performed.
    [DefaultExecutionOrder(70)]
    public sealed class VendorWorkActor : MonoBehaviour
    {
        public bool automaticTick=true;
        public float walkSpeed=1.35f,acceleration=6f,turnDegreesPerSecond=300f;
        // Light shopping/handling keeps a brisk, loose gait; the heavy stove run
        // keeps the original slow values. Only travel speed changes: collision
        // sweeps, planted feet and the contact timestamp stay on real frames.
        public float lightWalkSpeed=2.1f,lightAcceleration=14f;
        public float finalStepMetres=.07f;
        public float contactTolerance=.025f;
        public bool IsBusy=>phase!=VendorWorkPhase.Idle&&phase!=VendorWorkPhase.Failed;
        public bool IsBound=>actor!=null&&right!=null&&left!=null;
        public string LastFailure {get;private set;}="";
        public VendorWorkPhase Phase=>phase;
        public Vector3 RightPalmPosition=>right!=null?right.Palm:Vector3.zero;
        public Vector3 LeftPalmPosition=>left!=null?left.Palm:Vector3.zero;
        public Quaternion RightPalmRotation=>right!=null?right.PalmRotation:Quaternion.identity;
        public Quaternion LeftPalmRotation=>left!=null?left.PalmRotation:Quaternion.identity;
        public void Abort(string reason="Work action cancelled") {if(IsBusy)Fail(reason);}
        public event Action<VendorWorkAudit> ContactReached;
        public event Action<string> Failed;
        private Transform actor,animationRoot,spine,upperBody,hips;
        private Transform visualProp,visualPropGrip;
        private Vector3 propRestPosition,propGripOffset;
        private Quaternion propRestRotation,propGripRotation;
        private bool propInLeftHand;
        private AnimationClip idleClip,walkClip,carryClip;
        private Transform[] poseNodes;
        private Vector3[] restPositions,idlePositions;
        private Quaternion[] restRotations,idleRotations;
        private Func<bool> pauseQuery;
        private int obstacleMask;
        private Quaternion forwardCorrection;
        private Arm right,left;
        private Leg rightLeg,leftLeg;
        private VendorWorkPhase phase=VendorWorkPhase.Idle;
        private VendorWorkAction semantic;
        private VendorWorkTarget target;
        private readonly List<Vector3> route=new List<Vector3>();
        private int routeAt,requestId,contacts;
        private bool pendingAction,contactCommitted,paused,actionUsesCarryPose;
        private float elapsed,duration=.7f,moveSpeed,gait,walkWeight,idleTime,blockedTime,pathMetres;
        private float rightError,leftError,maxContactError,maxWorkError,maxPlantedFootError,maxPelvisDrop,maxMovingFootError,maxWorkingFootError;
        private float strideMetres=.8f,carryStrideMetres=.8f,pelvisDrop;
        private string footErrorDetail="";
        private float CurrentStride=>visualProp?carryStrideMetres:strideMetres;
        private Vector3 destination,facePoint,rightPoint,leftPoint,rightRest,leftRest;
        private Quaternion rightRestRotation,leftRestRotation;
        private Vector3 toolPosition,toolGripOffset;
        private Quaternion toolRotation,toolGripRotation;
        private bool toolCaptured;
        private Action contactCallback,finishedCallback,arrivedCallback;
        private const float ContactAt=.32f,RecoveryAt=.78f,ReleaseAt=.91f;
        public static float CommitFraction(VendorWorkAction action)
        {
            switch(action)
            {
                case VendorWorkAction.PourIngredient:case VendorWorkAction.Season:return .56f;
                case VendorWorkAction.Pack:case VendorWorkAction.Serve:return .64f;
                case VendorWorkAction.Clean:return .52f;
                default:return ContactAt;
            }
        }
        // Light pick/pack/serve gestures play loose and short; the two heavy
        // pan/stove actions and cleaning keep their authored length.
        private static float DurationScale(VendorWorkAction action)
        {
            switch(action)
            {
                case VendorWorkAction.PickIngredient:return .55f;
                case VendorWorkAction.Pack:case VendorWorkAction.Serve:return .6f;
                case VendorWorkAction.Clean:return .75f;
                default:return 1f;
            }
        }
        private sealed class Arm
        {
            public Transform upper,lower,hand;
            public Vector3 palmOffset;
            public Quaternion palmFrame;
            public float upperLength,lowerLength;
            public Vector3 Palm=>hand.TransformPoint(palmOffset);
            public Quaternion PalmRotation=>hand.rotation*palmFrame;
        }
        private sealed class Leg
        {
            public Transform upper,lower,foot;
            public float a,b;
            public bool planted;
            public Vector3 plant,sampledGoal;
            public Quaternion rotation,sampledRotation;
            public float groundHeight,carryGroundHeight,previousForward;public bool hasPrevious;
        }

        public void Bind(Transform player,Func<bool> pausedQuery=null,int collisionMask=WorldRuntime.EnvironmentMask)
        {
            Cancel();actor=player;pauseQuery=pausedQuery;obstacleMask=collisionMask;
            if(actor==null)throw new ArgumentNullException(nameof(player));
            var motor=actor.GetComponent<PlayerMotor>();if(motor!=null){motor.StopMotion();motor.enabled=false;}
            foreach(Animator animator in actor.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            Animation animation=actor.GetComponentInChildren<Animation>(true);
            foreach(Animation source in actor.GetComponentsInChildren<Animation>(true))source.enabled=false;
            animationRoot=animation?animation.transform:actor.childCount>0?actor.GetChild(0):actor;
            poseNodes=animationRoot.GetComponentsInChildren<Transform>(true);
            restPositions=new Vector3[poseNodes.Length];restRotations=new Quaternion[poseNodes.Length];
            idlePositions=new Vector3[poseNodes.Length];idleRotations=new Quaternion[poseNodes.Length];
            for(int i=0;i<poseNodes.Length;i++){restPositions[i]=poseNodes[i].localPosition;restRotations[i]=poseNodes[i].localRotation;}
            idleClip=FindClip("Idle_Loop");walkClip=FindClip("Walk_Loop");carryClip=FindClip("Walk_Carry_Loop");
            if(idleClip==null||walkClip==null)throw new InvalidOperationException("Vendor requires real Idle_Loop and Walk_Loop clips.");
            Transform origin=Find("PlayerOrigin"),forward=Find("PlayerForward");
            Vector3 direction=forward&&origin?forward.position-origin.position:actor.forward;direction.y=0;
            Vector3 localForward=Quaternion.Inverse(actor.rotation)*direction.normalized;
            forwardCorrection=Quaternion.Inverse(Quaternion.LookRotation(localForward,Vector3.up));
            idleClip.SampleAnimation(animationRoot.gameObject,0);
            right=BindArm("Right");left=BindArm("Left");spine=Find("Spine2");upperBody=Find("Spine");hips=Required("Hips");
            rightLeg=BindLeg("Right");leftLeg=BindLeg("Left");CalibrateStride();
            phase=VendorWorkPhase.Idle;SamplePose(0);ResetFeet();
            Debug.Log("VENDOR_RIG_DIMENSIONS root="+actor.position.ToString("F4")+" shoulderR="+right.upper.position.ToString("F4")+" shoulderL="+left.upper.position.ToString("F4")+" palmR="+right.Palm.ToString("F4")+" palmL="+left.Palm.ToString("F4"));
            Debug.Log("VENDOR_ACTOR_BOUND bones="+poseNodes.Length+" rightArm="+(right.upperLength+right.lowerLength).ToString("F3")+" leftArm="+(left.upperLength+left.lowerLength).ToString("F3")+" measuredStride="+strideMetres.ToString("F3")+" legacy="+idleClip.name+"/"+walkClip.name);
        }
        public bool MoveTo(Vector3 standPoint,Vector3 facingPoint,Action arrived=null,Vector3[] waypoints=null)
        {
            if(!IsBound||IsBusy)return false;
            BeginRequest();pendingAction=false;arrivedCallback=arrived;
            BeginRoute(standPoint,facingPoint,waypoints);return true;
        }
        public bool Perform(VendorWorkAction action,VendorWorkTarget workTarget,float seconds,Action contact=null,Action finished=null)
        {
            if(!IsBound||IsBusy||workTarget==null)return false;
            BeginRequest();semantic=action;target=workTarget;
            // Scale only the visible timeline: the contact still commits at the
            // action's authored fraction and the hand still physically touches
            // the grip that frame, so inventory timing and contact frames are
            // decoupled from animation speed. Heavy work keeps its full length.
            duration=Mathf.Max(.28f,seconds*DurationScale(action));
            contactCallback=contact;finishedCallback=finished;pendingAction=true;
            BeginRoute(workTarget.standPoint,workTarget.workPoint,workTarget.route);return true;
        }
        public bool Perform(VendorWorkAction action,Vector3 worldTarget,float seconds,Action contact=null,Action finished=null)
        {
            Vector3 away=actor?actor.position-worldTarget:Vector3.back;away.y=0;
            if(away.sqrMagnitude<.001f)away=Vector3.back;
            Vector3 stand=worldTarget+away.normalized*.44f;stand.y=actor?actor.position.y:worldTarget.y;
            return Perform(action,new VendorWorkTarget{standPoint=stand,workPoint=worldTarget},seconds,contact,finished);
        }
        // Presentation-only prop. Call from a pick contact, never to grant stock.
        public bool AttachVisualProp(Transform prop,Transform grip,bool leftHand=false)
        {
            if(!IsBound||prop==null||grip==null||carryClip==null)return false;
            Arm hand=leftHand?left:right;
            if(Vector3.Distance(hand.Palm,grip.position)>contactTolerance)return false;
            ReleaseVisualProp();visualProp=prop;visualPropGrip=grip;propInLeftHand=leftHand;
            propRestPosition=prop.position;propRestRotation=prop.rotation;
            propGripOffset=Quaternion.Inverse(prop.rotation)*(grip.position-prop.position);
            propGripRotation=Quaternion.Inverse(prop.rotation)*grip.rotation;
            prop.gameObject.SetActive(true);ApplyCarryProp();return true;
        }
        public Transform ReleaseVisualProp(bool hide=true)
        {
            Transform prop=visualProp;visualProp=null;visualPropGrip=null;
            if(prop){prop.SetPositionAndRotation(propRestPosition,propRestRotation);if(hide)prop.gameObject.SetActive(false);}
            return prop;
        }
        private void ApplyCarryProp()
        {
            if(!visualProp)return;Arm hand=propInLeftHand?left:right;
            Quaternion rotation=hand.PalmRotation*Quaternion.Inverse(propGripRotation);
            visualProp.SetPositionAndRotation(hand.Palm-rotation*propGripOffset,rotation);
        }
        public void Cancel()
        {
            if(toolCaptured&&target!=null&&target.heldTool){target.heldTool.SetPositionAndRotation(toolPosition,toolRotation);}
            ReleaseVisualProp();toolCaptured=false;target=null;contactCallback=finishedCallback=arrivedCallback=null;
            pendingAction=false;contactCommitted=false;actionUsesCarryPose=false;moveSpeed=0;route.Clear();phase=VendorWorkPhase.Idle;
            // A finished session may never tick again. Restore standing now,
            // after returning props/tools, without dispatching a transaction.
            if(IsBound){walkWeight=0;pelvisDrop=0;ResetFeet();SamplePose(0);}
        }
        private bool HeavyLoad
        {
            get
            {
                var flow=GameFlow.Instance;
                if(flow==null||flow.Run==null)return false;
                return flow.Run.HoldingStove||RuntimeItemBalance.WeightKg(flow.Run.HeldGrocery)>=10f;
            }
        }
        private bool LightRequest=>!HeavyLoad;
        private void BeginRequest()
        {
            requestId++;elapsed=0;contacts=0;contactCommitted=false;LastFailure="";blockedTime=0;
            maxContactError=0;maxWorkError=0;maxPlantedFootError=0;maxMovingFootError=0;maxWorkingFootError=0;maxPelvisDrop=0;footErrorDetail="";rightError=leftError=0;pendingAction=false;target=null;toolCaptured=false;
            contactCallback=finishedCallback=arrivedCallback=null;
        }
        private void BeginRoute(Vector3 point,Vector3 look,Vector3[] waypoints)
        {
            destination=point;facePoint=look;route.Clear();routeAt=0;
            if(waypoints!=null)foreach(Vector3 waypoint in waypoints)route.Add(waypoint);
            route.Add(point);phase=VendorWorkPhase.Moving;
        }
        private void Update(){if(automaticTick)Tick(Time.deltaTime,pauseQuery!=null&&pauseQuery());}
        public void Tick(float deltaTime,bool isPaused=false)
        {
            if(!IsBound)return;paused=isPaused||(pauseQuery!=null&&pauseQuery());if(paused||deltaTime<=0)return;
            // Preserve the full timeline while bounding collision and IK steps.
            float remaining=deltaTime;
            while(remaining>.000001f)
            {
                float dt=Mathf.Min(remaining,1f/60f);remaining-=dt;
                if(phase==VendorWorkPhase.Moving)AdvanceWalking(dt);
                else if(phase==VendorWorkPhase.Turning)AdvanceTurning(dt);
                else if(phase==VendorWorkPhase.Anticipation||phase==VendorWorkPhase.Contact||phase==VendorWorkPhase.Working||phase==VendorWorkPhase.Recovery)AdvanceAction(dt);
                else{walkWeight=Mathf.MoveTowards(walkWeight,0,dt*8);idleTime+=dt;SamplePose(dt);}
            }
        }
        private void AdvanceWalking(float dt)
        {
            // Waypoints may be rounded, but a contact stand must be reached by
            // real movement: a 2.5cm early stop materially changes short-arm reach.
            while(routeAt<route.Count&&Flat(route[routeAt]-actor.position).magnitude<(routeAt==route.Count-1?.001f:.025f))routeAt++;
            if(routeAt>=route.Count){moveSpeed=0;phase=VendorWorkPhase.Turning;return;}
            Vector3 delta=Flat(route[routeAt]-actor.position);float distance=delta.magnitude;
            bool finalLeg=routeAt==route.Count-1;
            float top=LightRequest?lightWalkSpeed:walkSpeed;
            float accel=LightRequest?lightAcceleration:acceleration;
            // Braking-distance cruise instead of continuous sqrt deceleration:
            // full speed until the stop is physically inevitable, then brake.
            // The previous model bled speed for the whole approach, so the last
            // centimetres (1 mm tolerance) crawled for seconds and read as the
            // actor idling between "arrived" and the pickup contact.
            float wanted;
            if(finalLeg&&distance<finalStepMetres)wanted=top; // tiny shuffle: quick short step into place
            else{float brake=moveSpeed*moveSpeed/(2*accel)+.02f;wanted=distance<=brake?0:top;}
            moveSpeed=Mathf.MoveTowards(moveSpeed,wanted,accel*dt);
            // Decisive start: from standstill, jump straight to cruise intent so
            // short hops between shelf and basket begin at full push-off.
            if(moveSpeed<.05f&&wanted>0)moveSpeed=Mathf.Min(top,accel*dt*2);
            Vector3 displacement=delta.normalized*Mathf.Min(distance,moveSpeed*dt);
            Quaternion desired=Quaternion.LookRotation(delta.normalized)*forwardCorrection;
            float angle=Quaternion.Angle(actor.rotation,desired);actor.rotation=Quaternion.RotateTowards(actor.rotation,desired,turnDegreesPerSecond*dt);
            if(angle>80)displacement*=.25f;
            float accepted=ClearDistance(displacement);
            if(accepted>0)actor.position+=displacement.normalized*accepted;
            pathMetres+=accepted;
            // A moving pivot takes real steps as well. Previously a tight turn
            // advanced the gait only by its slowed translation, pinning one foot
            // while the pelvis rotated through up to 180 degrees above it.
            float turned=angle-Quaternion.Angle(actor.rotation,desired);
            gait+=(accepted+Mathf.Max(0,turned)*Mathf.Deg2Rad*.22f)/CurrentStride;
            if(accepted<displacement.magnitude*.1f)blockedTime+=dt;else blockedTime=0;
            if(blockedTime>.7f){Fail("Work route blocked by real geometry; supply a clear front-edge waypoint route.");return;}
            walkWeight=Mathf.MoveTowards(walkWeight,Mathf.Clamp01(moveSpeed/.55f),dt*8);idleTime+=dt;SamplePose(dt);
        }
        private float ClearDistance(Vector3 displacement)
        {
            float length=displacement.magnitude;if(length<.000001f)return 0;
            float clear=length;
            foreach(RaycastHit hit in Physics.CapsuleCastAll(actor.position+Vector3.up*.29f,actor.position+Vector3.up*1.40f,.25f,displacement/length,length+.012f,obstacleMask,QueryTriggerInteraction.Ignore))
                if(hit.collider!=null&&!hit.collider.transform.IsChildOf(actor))clear=Mathf.Min(clear,Mathf.Max(0,hit.distance-.012f));
            return clear;
        }
        private void AdvanceTurning(float dt)
        {
            Vector3 direction=Flat(facePoint-actor.position);Quaternion desired=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction)*forwardCorrection:actor.rotation;
            float before=Quaternion.Angle(actor.rotation,desired);actor.rotation=Quaternion.RotateTowards(actor.rotation,desired,turnDegreesPerSecond*dt);
            float rotation=before-Quaternion.Angle(actor.rotation,desired);gait+=rotation*Mathf.Deg2Rad*.22f/CurrentStride;
            walkWeight=Mathf.MoveTowards(walkWeight,before>12?.65f:0,dt*8);idleTime+=dt;SamplePose(dt);
            if(Quaternion.Angle(actor.rotation,desired)>2||walkWeight>.02f)return;
            ResetFeet();
            if(pendingAction)BeginAction();
            else{phase=VendorWorkPhase.Idle;Action callback=arrivedCallback;arrivedCallback=null;callback?.Invoke();}
        }
        private void BeginAction()
        {
            phase=VendorWorkPhase.Anticipation;elapsed=0;actionUsesCarryPose=visualProp!=null;
            rightRest=right.Palm;leftRest=left.Palm;rightRestRotation=right.PalmRotation;leftRestRotation=left.PalmRotation;
            if(target.heldTool&&target.toolGrip)
            {
                toolCaptured=true;toolPosition=target.heldTool.position;toolRotation=target.heldTool.rotation;
                toolGripOffset=Quaternion.Inverse(toolRotation)*(target.toolGrip.position-toolPosition);
                toolGripRotation=Quaternion.Inverse(toolRotation)*target.toolGrip.rotation;
            }
        }
        private void AdvanceAction(float dt)
        {
            float old=elapsed;elapsed=Mathf.Min(duration,elapsed+dt);
            float commitAt=CommitFraction(semantic);
            bool crossing=!contactCommitted&&old<duration*commitAt&&elapsed>=duration*commitAt;
            float remainder=0;
            if(crossing){remainder=elapsed-duration*commitAt;elapsed=duration*commitAt;}
            float p=elapsed/duration;
            phase=p<ContactAt?VendorWorkPhase.Anticipation:p<RecoveryAt?VendorWorkPhase.Working:VendorWorkPhase.Recovery;
            idleTime+=dt;walkWeight=0;SamplePose(dt);ApplyWorkPose(p);
            if(crossing)
            {
                maxContactError=Mathf.Max(rightError,UsesLeft?leftError:0);
                if(maxContactError>contactTolerance)
                {Fail("Hand contact is outside measured reach: right="+rightError.ToString("F3")+"m left="+leftError.ToString("F3")+"m. Move the standing/hand grip nodes, do not stretch the arm.");return;}
                contactCommitted=true;contacts++;phase=VendorWorkPhase.Contact;
                int committedRequest=requestId;Action callback=contactCallback;contactCallback=null;
                ContactReached?.Invoke(CaptureAudit());
                if(target==null||!IsBusy||requestId!=committedRequest)return;
                callback?.Invoke();
                // A controller callback may cancel/rebind the visual execution.
                if(target==null||!IsBusy||requestId!=committedRequest)return;
                if(remainder>.000001f)AdvanceAction(remainder);
            }
            if(elapsed>=duration-.000001f)
            {
                if(toolCaptured&&target.heldTool)target.heldTool.SetPositionAndRotation(toolPosition,toolRotation);
                toolCaptured=false;target=null;phase=VendorWorkPhase.Idle;pendingAction=false;
                Action callback=finishedCallback;finishedCallback=null;callback?.Invoke();
            }
        }
        private bool UsesLeft=>target!=null&&(target.useLeftHand||target.leftGrip!=null||semantic==VendorWorkAction.Pack||semantic==VendorWorkAction.Serve||semantic==VendorWorkAction.PourIngredient);
        private void ApplyWorkPose(float p)
        {
            Vector3 forward=Flat(facePoint-actor.position).normalized,rightward=Vector3.Cross(Vector3.up,forward);
            if(forward.sqrMagnitude<.01f){forward=Vector3.forward;rightward=Vector3.right;}
            if(spine!=null)
            {
                float lean=6*Mathf.Sin(Mathf.PI*Mathf.Clamp01(p/ReleaseAt));
                spine.rotation=Quaternion.AngleAxis(lean,rightward)*spine.rotation;
            }
            Vector3 baseRight=target.rightGrip?target.rightGrip.position:target.workPoint;
            Quaternion rightRotation=target.rightGrip?target.rightGrip.rotation:Quaternion.LookRotation(forward,Vector3.up);
            Vector3 baseLeft=target.leftGrip?target.leftGrip.position:target.workPoint-rightward*.17f;
            Quaternion leftRotation=target.leftGrip?target.leftGrip.rotation:Quaternion.LookRotation(forward,Vector3.up);
            if(toolCaptured){baseRight=toolPosition+toolRotation*toolGripOffset;rightRotation=toolRotation*toolGripRotation;}
            float reach=p<ContactAt?Ease(p/ContactAt):1;
            Vector3 offset=Vector3.zero;Quaternion actionRotation=Quaternion.identity;
            if(p>=ContactAt&&p<RecoveryAt)
            {
                float t=(p-ContactAt)/(RecoveryAt-ContactAt),arc=Mathf.Sin(Mathf.PI*t),cycle=Mathf.Sin(2*Mathf.PI*t);
                switch(semantic)
                {
                    case VendorWorkAction.PickIngredient:offset=Vector3.up*(.13f*arc)-forward*(.10f*arc);break;
                    case VendorWorkAction.PourIngredient:offset=Vector3.up*(.14f*arc)+forward*(.09f*arc);actionRotation=Quaternion.AngleAxis(50*arc,forward);break;
                    case VendorWorkAction.StirFry:offset=rightward*(.095f*cycle)+forward*(.055f*(1-Mathf.Cos(2*Mathf.PI*t)))+Vector3.up*(.025f*arc);actionRotation=Quaternion.AngleAxis(18*cycle,forward);break;
                    case VendorWorkAction.Flip:offset=-forward*(.09f*Mathf.Sin(Mathf.PI*2*t))+Vector3.up*(.17f*arc);actionRotation=Quaternion.AngleAxis(-38*arc,rightward);break;
                    case VendorWorkAction.Scoop:offset=forward*(.11f*Mathf.Sin(2*Mathf.PI*t))+Vector3.up*(.16f*arc);actionRotation=Quaternion.AngleAxis(-27*arc,rightward);break;
                    case VendorWorkAction.Season:offset=Vector3.up*(.13f*arc)+rightward*(.035f*Mathf.Sin(6*Mathf.PI*t));actionRotation=Quaternion.AngleAxis(38*arc+8*Mathf.Sin(6*Mathf.PI*t),forward);break;
                    case VendorWorkAction.Pack:offset=rightward*(.065f*arc)-Vector3.up*(.035f*arc);actionRotation=Quaternion.AngleAxis(14*arc,forward);break;
                    case VendorWorkAction.Serve:offset=forward*(.20f*arc)+Vector3.up*(.075f*arc);break;
                    case VendorWorkAction.Clean:offset=rightward*(.12f*cycle)+forward*(.05f*(1-Mathf.Cos(2*Mathf.PI*t)));break;
                }
            }
            float release=p<ReleaseAt?1:1-Ease((p-ReleaseAt)/(1-ReleaseAt));
            // Cubic approach lifts around the counter lip; hand reaches the exact
            // authored grip at the contact timestamp, with zero endpoint velocity.
            rightPoint=p<ContactAt?Bezier(rightRest,rightRest+Vector3.up*.10f,baseRight+Vector3.up*.08f,baseRight,reach):baseRight+offset;
            leftPoint=p<ContactAt?Bezier(leftRest,leftRest+Vector3.up*.06f,baseLeft+Vector3.up*.04f,baseLeft,reach):baseLeft;
            if(semantic==VendorWorkAction.Pack&&p>=ContactAt&&p<RecoveryAt)leftPoint-=offset*.65f;
            if((semantic==VendorWorkAction.PourIngredient||semantic==VendorWorkAction.Serve)&&p>=ContactAt&&p<RecoveryAt)leftPoint+=offset;
            rightPoint=Vector3.Lerp(right.Palm,rightPoint,release);leftPoint=Vector3.Lerp(left.Palm,leftPoint,release);
            rightRotation=Quaternion.Slerp(rightRestRotation,actionRotation*rightRotation,reach*release);
            leftRotation=Quaternion.Slerp(leftRestRotation,leftRotation,reach*release);
            SolveArm(right,rightPoint,rightRotation,actor.position+rightward*.65f-forward*.15f+Vector3.up*.9f);
            if(UsesLeft)SolveArm(left,leftPoint,leftRotation,actor.position-rightward*.65f-forward*.15f+Vector3.up*.9f);
            rightError=Vector3.Distance(right.Palm,rightPoint);leftError=UsesLeft?Vector3.Distance(left.Palm,leftPoint):0;
            if(p>=ContactAt&&p<=ReleaseAt)maxWorkError=Mathf.Max(maxWorkError,Mathf.Max(rightError,leftError));
            if(toolCaptured&&p>=ContactAt&&p<=ReleaseAt)
            {
                Quaternion rotation=right.PalmRotation*Quaternion.Inverse(toolGripRotation);
                target.heldTool.SetPositionAndRotation(right.Palm-rotation*toolGripOffset,rotation);
            }
            else if(toolCaptured&&p>ReleaseAt)target.heldTool.SetPositionAndRotation(toolPosition,toolRotation);
            ApplyCarryProp();
        }
        private float CarryPoseWeight
        {
            get
            {
                bool working=phase==VendorWorkPhase.Anticipation||phase==VendorWorkPhase.Contact||phase==VendorWorkPhase.Working||phase==VendorWorkPhase.Recovery;
                if(!working)return visualProp?1:0;
                if(!actionUsesCarryPose)return 0;
                if(visualProp||elapsed/duration<ReleaseAt)return 1;
                return 1-Ease((elapsed/duration-ReleaseAt)/(1-ReleaseAt));
            }
        }
        private void SamplePose(float dt)
        {
            for(int i=0;i<poseNodes.Length;i++){poseNodes[i].localPosition=restPositions[i];poseNodes[i].localRotation=restRotations[i];}
            idleClip.SampleAnimation(animationRoot.gameObject,idleTime%idleClip.length);
            if(walkWeight>.001f)
            {
                for(int i=0;i<poseNodes.Length;i++){idlePositions[i]=poseNodes[i].localPosition;idleRotations[i]=poseNodes[i].localRotation;}
                AnimationClip locomotion=visualProp&&carryClip?carryClip:walkClip;
                locomotion.SampleAnimation(animationRoot.gameObject,(gait%1)*locomotion.length);
                for(int i=0;i<poseNodes.Length;i++){poseNodes[i].localPosition=Vector3.Lerp(idlePositions[i],poseNodes[i].localPosition,walkWeight);poseNodes[i].localRotation=Quaternion.Slerp(idleRotations[i],poseNodes[i].localRotation,walkWeight);}
            }
            float carryWeight=CarryPoseWeight;
            if(carryWeight>0&&carryClip)
            {
                // Lock the action's base posture independently of the visible prop.
                // Inventory/visual transfer cannot move the shoulder mid-contact;
                // only recovery blends a released object's pose back to idle.
                for(int i=0;i<poseNodes.Length;i++){idlePositions[i]=poseNodes[i].localPosition;idleRotations[i]=poseNodes[i].localRotation;}
                carryClip.SampleAnimation(animationRoot.gameObject,(walkWeight>.05f?gait%1:.1f)*carryClip.length);
                for(int i=0;i<poseNodes.Length;i++)
                {
                    float weight=upperBody!=null&&poseNodes[i].IsChildOf(upperBody)?carryWeight:0;
                    poseNodes[i].localPosition=Vector3.Lerp(idlePositions[i],poseNodes[i].localPosition,weight);
                    poseNodes[i].localRotation=Quaternion.Slerp(idleRotations[i],poseNodes[i].localRotation,weight);
                }
            }
            bool leftStance=IsStance(leftLeg),rightStance=IsStance(rightLeg);
            PrepareFoot(leftLeg,leftStance);PrepareFoot(rightLeg,rightStance);
            float needed=Mathf.Max(RequiredPelvisDrop(leftLeg),RequiredPelvisDrop(rightLeg));
            // A planted foot cannot be reached by stretching a straight leg.
            // Lower the pelvis by the geometrically necessary knee-flex amount;
            // recover upward smoothly. The navigation/root transform never moves.
            needed=Mathf.Clamp(needed,0,.14f);
            pelvisDrop=Mathf.Max(needed,Mathf.MoveTowards(pelvisDrop,needed,dt*.40f));
            hips.position-=Vector3.up*pelvisDrop;maxPelvisDrop=Mathf.Max(maxPelvisDrop,pelvisDrop);
            PlantFoot(leftLeg,leftStance);PlantFoot(rightLeg,rightStance);ApplyCarryProp();
        }
        private void CalibrateStride()
        {
            MeasureStride(walkClip,out strideMetres,out float leftGround,out float rightGround);
            leftLeg.groundHeight=leftGround;rightLeg.groundHeight=rightGround;
            MeasureStride(carryClip?carryClip:walkClip,out carryStrideMetres,out leftGround,out rightGround);
            leftLeg.carryGroundHeight=leftGround;rightLeg.carryGroundHeight=rightGround;
            idleClip.SampleAnimation(animationRoot.gameObject,0);
        }
        private void MeasureStride(AnimationClip clip,out float stride,out float leftGround,out float rightGround)
        {
            float minL=float.MaxValue,maxL=float.MinValue,minR=float.MaxValue,maxR=float.MinValue;
            leftGround=rightGround=float.MaxValue;
            Vector3 forward=actor.rotation*Quaternion.Inverse(forwardCorrection)*Vector3.forward;
            for(int i=0;i<64;i++)
            {
                clip.SampleAnimation(animationRoot.gameObject,clip.length*i/64f);
                float l=Vector3.Dot(leftLeg.foot.position-actor.position,forward),r=Vector3.Dot(rightLeg.foot.position-actor.position,forward);
                minL=Mathf.Min(minL,l);maxL=Mathf.Max(maxL,l);minR=Mathf.Min(minR,r);maxR=Mathf.Max(maxR,r);
                leftGround=Mathf.Min(leftGround,leftLeg.foot.position.y-actor.position.y);
                rightGround=Mathf.Min(rightGround,rightLeg.foot.position.y-actor.position.y);
            }
            // Derive cycle distance from the existing clip, not an assumed gait.
            stride=Mathf.Max(.15f,2*Mathf.Max(maxL-minL,maxR-minR));
        }
        private bool IsStance(Leg leg)
        {
            if(leg==null)return false;
            Vector3 forward=actor.rotation*Quaternion.Inverse(forwardCorrection)*Vector3.forward;
            float localForward=Vector3.Dot(leg.foot.position-actor.position,forward);
            bool backwards=!leg.hasPrevious||localForward<=leg.previousForward+.0002f;
            bool low=leg.foot.position.y-actor.position.y<=(CarryPoseWeight>.5f?leg.carryGroundHeight:leg.groundHeight)+.035f;
            leg.previousForward=localForward;leg.hasPrevious=true;
            return walkWeight<.05f||backwards&&low;
        }
        private void PrepareFoot(Leg leg,bool stance)
        {
            if(leg==null)return;
            leg.sampledGoal=leg.foot.position;leg.sampledRotation=leg.foot.rotation;
            if(!stance){leg.planted=false;return;}
            if(!leg.planted){leg.plant=leg.sampledGoal;leg.rotation=leg.sampledRotation;leg.planted=true;}
        }
        private float RequiredPelvisDrop(Leg leg)
        {
            if(leg==null||!leg.planted)return 0;
            Vector3 delta=leg.upper.position-leg.plant;float horizontal=delta.x*delta.x+delta.z*delta.z;
            float length=leg.a+leg.b-.004f;
            float maxVertical=Mathf.Sqrt(Mathf.Max(0,length*length-horizontal));
            return Mathf.Max(0,delta.y-maxVertical);
        }
        private void PlantFoot(Leg leg,bool stance)
        {
            if(leg==null)return;
            Vector3 forward=actor.rotation*Quaternion.Inverse(forwardCorrection)*Vector3.forward;
            // Preserve the clip's swing-foot arc while compensating the pelvis.
            SolveTwoBone(leg.upper,leg.lower,leg.foot,stance?leg.plant:leg.sampledGoal,leg.upper.position+forward*.7f,leg.a,leg.b);
            leg.foot.rotation=stance?leg.rotation:leg.sampledRotation;
            if(!stance)return;
            float error=Vector3.Distance(leg.foot.position,leg.plant);
            if(phase==VendorWorkPhase.Moving||phase==VendorWorkPhase.Turning)maxMovingFootError=Mathf.Max(maxMovingFootError,error);
            else maxWorkingFootError=Mathf.Max(maxWorkingFootError,error);
            if(error>maxPlantedFootError)
            {
                maxPlantedFootError=error;
                footErrorDetail="phase="+phase+" leg="+leg.foot.name+" gait="+gait.ToString("F3")+" blend="+walkWeight.ToString("F3")+" stride="+CurrentStride.ToString("F3")+
                    " hipToPlant="+Vector3.Distance(leg.upper.position,leg.plant).ToString("F4")+" lengths="+leg.a.ToString("F4")+"+"+leg.b.ToString("F4")+
                    " actual="+Vector3.Distance(leg.upper.position,leg.lower.position).ToString("F4")+"+"+Vector3.Distance(leg.lower.position,leg.foot.position).ToString("F4")+
                    " hip="+leg.upper.position.ToString("F4")+" plant="+leg.plant.ToString("F4")+" end="+leg.foot.position.ToString("F4");
            }
            // Any remaining anatomical-limit error stays visible in the audit;
            // never reset the planted point to disguise slipping.
        }
        private void ResetFeet(){if(rightLeg!=null){rightLeg.planted=false;rightLeg.hasPrevious=false;}if(leftLeg!=null){leftLeg.planted=false;leftLeg.hasPrevious=false;}}
        private Arm BindArm(string side)
        {
            Transform upper=Required(side+"Arm"),lower=Required(side+"ForeArm"),hand=Required(side+"Hand");
            Transform index=Required(side+"HandIndex1"),middle=Required(side+"HandMiddle1"),pinky=Required(side+"HandPinky1");
            Vector3 finger=hand.InverseTransformDirection(middle.position-hand.position).normalized;
            Vector3 span=hand.InverseTransformDirection(index.position-pinky.position).normalized;
            Vector3 normal=Vector3.Cross(finger,span).normalized;if(side=="Left")normal=-normal;
            return new Arm{upper=upper,lower=lower,hand=hand,upperLength=Vector3.Distance(upper.position,lower.position),lowerLength=Vector3.Distance(lower.position,hand.position),
                palmOffset=hand.InverseTransformPoint((index.position+middle.position+pinky.position+hand.position)/4),palmFrame=Quaternion.LookRotation(finger,normal)};
        }
        private Leg BindLeg(string side)
        {
            Transform a=Required(side+"UpLeg"),b=Required(side+"Leg"),c=Required(side+"Foot");
            return new Leg{upper=a,lower=b,foot=c,a=Vector3.Distance(a.position,b.position),b=Vector3.Distance(b.position,c.position)};
        }
        private void SolveArm(Arm arm,Vector3 palm,Quaternion palmRotation,Vector3 elbowPole)
        {
            Quaternion handRotation=palmRotation*Quaternion.Inverse(arm.palmFrame);
            Vector3 scaledOffset=Vector3.Scale(arm.palmOffset,arm.hand.lossyScale);
            Vector3 wrist=palm-handRotation*scaledOffset;
            SolveTwoBone(arm.upper,arm.lower,arm.hand,wrist,elbowPole,arm.upperLength,arm.lowerLength);
            arm.hand.rotation=handRotation;
        }
        public static void SolveTwoBone(Transform root,Transform joint,Transform tip,Vector3 target,Vector3 pole,float upperLength,float lowerLength)
        {
            Vector3 origin=root.position,delta=target-origin;float distance=delta.magnitude;
            if(distance<.00001f||upperLength<=0||lowerLength<=0)return;
            Vector3 forward=delta/distance;float reach=Mathf.Clamp(distance,Mathf.Abs(upperLength-lowerLength)+.0001f,upperLength+lowerLength-.0001f);
            Vector3 bend=Vector3.ProjectOnPlane(pole-origin,forward).normalized;
            if(bend.sqrMagnitude<.01f)bend=Vector3.ProjectOnPlane(Vector3.up,forward).normalized;
            if(bend.sqrMagnitude<.01f)bend=Vector3.right;
            float along=(upperLength*upperLength+reach*reach-lowerLength*lowerLength)/(2*reach);
            float height=Mathf.Sqrt(Mathf.Max(0,upperLength*upperLength-along*along));
            Vector3 elbow=origin+forward*along+bend*height;
            root.rotation=Quaternion.FromToRotation(joint.position-origin,elbow-origin)*root.rotation;
            Vector3 end=origin+forward*reach;
            joint.rotation=Quaternion.FromToRotation(tip.position-joint.position,end-joint.position)*joint.rotation;
        }
        private AnimationClip FindClip(string key)
        {
            foreach(AnimationClip clip in Resources.LoadAll<AnimationClip>("Models/Player@"+key))if(clip.legacy&&!clip.name.StartsWith("__preview__",StringComparison.Ordinal)&&clip.name.IndexOf(key,StringComparison.OrdinalIgnoreCase)>=0)return clip;
            foreach(AnimationClip clip in Resources.LoadAll<AnimationClip>("Models/Player"))if(clip.legacy&&clip.name.IndexOf(key,StringComparison.OrdinalIgnoreCase)>=0)return clip;
            return null;
        }
        private Transform Required(string suffix)=>Find(suffix)??throw new InvalidOperationException("52-bone vendor rig missing "+suffix);
        private Transform Find(string suffix)
        {
            foreach(Transform node in actor.GetComponentsInChildren<Transform>(true))
            {string name=node.name;int colon=name.LastIndexOf(':');if(colon>=0)name=name.Substring(colon+1);if(name==suffix||name=="mixamorig"+suffix)return node;}
            return null;
        }
        private void Fail(string message)
        {
            if(toolCaptured&&target!=null&&target.heldTool)target.heldTool.SetPositionAndRotation(toolPosition,toolRotation);
            ReleaseVisualProp();toolCaptured=false;LastFailure=message;phase=VendorWorkPhase.Failed;moveSpeed=0;pendingAction=false;contactCallback=null;
            Action finished=finishedCallback;finishedCallback=null;
            Debug.LogWarning("VENDOR_ACTION_FAILED "+message);Failed?.Invoke(message);
            finished?.Invoke();
        }
        public VendorWorkAudit CaptureAudit()=>new VendorWorkAudit{bound=IsBound,busy=IsBusy,paused=paused,contactCommitted=contactCommitted,carryingVisualProp=visualProp!=null,
            action=semantic.ToString(),phase=phase.ToString(),lastFailure=LastFailure,footErrorDetail=footErrorDetail,requestId=requestId,contacts=contacts,elapsed=elapsed,duration=duration,pathMetres=pathMetres,
            rightError=rightError,leftError=leftError,maxContactError=maxContactError,maxWorkError=maxWorkError,maxPlantedFootError=maxPlantedFootError,maxPelvisDrop=maxPelvisDrop,maxMovingFootError=maxMovingFootError,maxWorkingFootError=maxWorkingFootError,rootPosition=actor?actor.position:Vector3.zero,
            rightPalm=right!=null?right.Palm:Vector3.zero,rightTarget=rightPoint,leftPalm=left!=null?left.Palm:Vector3.zero,leftTarget=leftPoint};
        private static float Ease(float value){value=Mathf.Clamp01(value);return value*value*(3-2*value);}
        private static Vector3 Flat(Vector3 v){v.y=0;return v;}
        private static Vector3 Bezier(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float t)
        {float s=1-t;return s*s*s*a+3*s*s*t*b+3*s*t*t*c+t*t*t*d;}
    }
}
