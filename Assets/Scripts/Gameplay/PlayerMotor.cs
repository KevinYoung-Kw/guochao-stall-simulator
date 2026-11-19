using System;
using UnityEngine;

namespace Guochao
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform visual;
        public bool inputEnabled = true;
        public bool carrying;
        public bool pushing;
        public bool presentationLocked;
        public float speed = 3.8f;
        public float sprintSpeed = 5.5f;
        public float heavyCarrySpeed = 1.85f;
        public float boxCarrySpeed = 2.75f;
        public float trolleySpeed = 2.75f;
        public bool IsRunning { get; private set; }
        public float CarriedMassKg => !carrying||GameFlow.Instance==null?0:
            RuntimeItemBalance.WeightKg(GameFlow.Instance.Run.HoldingStove?GameFlow.Instance.Run.HeldEquipmentId:GameFlow.Instance.Run.HeldGrocery);
        public bool HeavyCarry => carrying && CarriedMassKg>=10;
        public float EffectiveTopSpeed => carrying ? HeavyCarry ? Mathf.Lerp(2.45f,heavyCarrySpeed,Mathf.InverseLerp(10,22,CarriedMassKg)) : boxCarrySpeed : pushing ? trolleySpeed : IsRunning ? sprintSpeed : speed;
        public bool InteractionActive => interactionRemaining > 0;
        public Vector3 Velocity { get; private set; }
        public CharacterController Controller { get; private set; }
        public LinkedTrolleyMotor LinkedTrolley { get; set; }
        public BoxCollider CarryCollider=>carryHull?.Volume;
        public CarryCollisionHull CarryHull=>carryHull;
        private CarryCollisionHull carryHull;
        public void SetCarriedVisual(Transform item){carryHull?.Bind(item);}
        private float verticalSpeed;
        private Vector3 planarVelocity;
        private float interactionRemaining, interactionDuration;
        private Quaternion interactionFacing;
        private float interactionReach;
        private Vector3 visualRestPosition;
        private Animation legacy;
        private string idleClip, walkClip, carryClip, pushClip, currentClip;

        public void Initialize(Transform visualTransform, Camera camera)
        {
            visual = visualTransform; viewCamera = camera;
            visualRestPosition = visual.localPosition;
            Transform authoredOrigin = null, authoredForward = null;
            foreach (Transform marker in visual.GetComponentsInChildren<Transform>(true))
            {
                if (marker.name == "PlayerOrigin") authoredOrigin = marker;
                if (marker.name == "PlayerForward") authoredForward = marker;
            }
            if (authoredOrigin != null && authoredForward != null && visual.childCount > 0)
            {
                Vector3 forward = authoredForward.position - authoredOrigin.position; forward.y = 0;
                if (forward.sqrMagnitude > .01f)
                    visual.GetChild(0).localRotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * visual.GetChild(0).localRotation;
            }
            Controller = GetComponent<CharacterController>();
            Controller.height = 1.65f; Controller.radius = .25f;
            Controller.center = Vector3.up * .84f;
            Controller.stepOffset = .20f; Controller.slopeLimit = 45;
            Controller.skinWidth = .025f; Controller.minMoveDistance = 0;
            carryHull=gameObject.AddComponent<CarryCollisionHull>();carryHull.Initialize(this);
            legacy = visual.GetComponentInChildren<Animation>();
            var clips = new System.Collections.Generic.List<AnimationClip>(Resources.LoadAll<AnimationClip>("Models/Player"));
            foreach (string action in new[] { "Idle_Loop", "Walk_Loop", "Walk_Carry_Loop", "Push_Loop" })
                clips.AddRange(Resources.LoadAll<AnimationClip>("Models/Player@" + action));
            if (legacy == null) legacy = visual.GetComponentInChildren<Animator>()?.gameObject.AddComponent<Animation>();
            if (legacy == null) legacy = (visual.childCount > 0 ? visual.GetChild(0).gameObject : visual.gameObject).AddComponent<Animation>();
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator != null) animator.enabled = false;
            foreach (AnimationClip clip in clips)
            {
                if (!clip.legacy || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                legacy.AddClip(clip, clip.name); legacy[clip.name].wrapMode = WrapMode.Loop;
                string lower = clip.name.ToLowerInvariant();
                if (lower.Contains("idle")) idleClip = clip.name;
                if ((lower.Contains("walk") || lower.Contains("run")) && !lower.Contains("carry")) walkClip = clip.name;
                if (lower.Contains("carry")) carryClip = clip.name;
                if (lower.Contains("push")) pushClip = clip.name;
            }
            if (idleClip != null) Play(idleClip);
            Debug.Log($"PLAYER_ANIMATION idle={idleClip ?? "none"} walk={walkClip ?? "none"} carry={carryClip ?? "none"}");
        }

        private void Update()
        {
            if (Controller == null || !Controller.enabled) return;
            Vector2 input = Vector2.zero;
            if (inputEnabled && !presentationLocked)
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            }
            DriveInput(input,Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift),Time.deltaTime);
        }

        // Shared by hardware Update and deterministic movement diagnostics. The
        // latter still accelerates, brakes and collides exactly like real input.
        public void DriveInput(Vector2 input, bool sprintHeld, float deltaTime)
        {
            if(Controller==null || !Controller.enabled || deltaTime<=0)return;
            if(!inputEnabled || presentationLocked)input=Vector2.zero;
            Vector3 before = transform.position;
            if (LinkedTrolley != null && LinkedTrolley.IsAttached)
            {
                // A/D steering and W/S throttle are independent cart-relative axes.
                // Do not normalize the pair or carry screen-space foot momentum.
                input = new Vector2(Mathf.Clamp(input.x,-1f,1f),Mathf.Clamp(input.y,-1f,1f));
                IsRunning = false;
                planarVelocity = Vector3.zero;
                LinkedTrolley.TryDrive(input.y,input.x,trolleySpeed,deltaTime);
            }
            else
            {
                input=Vector2.ClampMagnitude(input,1);
                Vector3 direction = ScreenPlaneInput.Direction(viewCamera, input);
                IsRunning = inputEnabled && !presentationLocked && !carrying && !pushing && input.sqrMagnitude>.001f &&
                    sprintHeld;
                float moveSpeed = EffectiveTopSpeed;
                bool stopped = !inputEnabled || presentationLocked || direction.sqrMagnitude < .001f;
                if (stopped) planarVelocity = Vector3.zero;
                else
                {
                    // Input selects travel direction immediately. Only speed builds
                    // with load: old sideways/forward momentum must not override a
                    // new key.
                    float acceleration = HeavyCarry ? 3.5f : carrying ? 9f : pushing ? 4.5f : 18f;
                    float nextSpeed = Mathf.MoveTowards(planarVelocity.magnitude,
                        direction.magnitude * moveSpeed, acceleration * deltaTime);
                    planarVelocity = direction.normalized * nextSpeed;
                }
                if (Controller.isGrounded && verticalSpeed < 0) verticalSpeed = -1.5f;
                verticalSpeed = Mathf.Max(-20, verticalSpeed - 25 * deltaTime);
                Quaternion facing=visual.rotation;
                if(planarVelocity.sqrMagnitude>.01f)
                    facing=Quaternion.Slerp(visual.rotation,Quaternion.LookRotation(planarVelocity,Vector3.up),(HeavyCarry?5:carrying?11:13)*deltaTime);
                if(carrying&&carryHull!=null&&carryHull.Active)
                {
                    carryHull.Drive(planarVelocity*deltaTime,facing);
                    Controller.Move(Vector3.up*verticalSpeed*deltaTime);carryHull.SyncPose();
                }
                else
                {Controller.Move((planarVelocity+Vector3.up*verticalSpeed)*deltaTime);visual.rotation=facing;}
            }
            Velocity = deltaTime > 0 ? (transform.position-before)/deltaTime : Vector3.zero;
            if (legacy != null)
            {
                legacy.enabled = inputEnabled;
                bool moving = new Vector2(Velocity.x,Velocity.z).sqrMagnitude > .01f;
                Play((carrying || InteractionActive) && carryClip != null ? carryClip : moving ? pushing && pushClip != null ? pushClip : walkClip : idleClip);
                if (currentClip != null && legacy[currentClip] != null)
                    legacy[currentClip].speed = InteractionActive || (carrying && !moving) ? 0 : moving ? Mathf.Clamp(new Vector2(Velocity.x, Velocity.z).magnitude / 3.2f, .35f, 1.7f) : 1;
            }
            if (!InteractionActive) visual.localPosition = visualRestPosition;
        }

        // GameFlow advances this pose with exactly the same paused timeline that
        // commits the inventory transaction. No root or collider is repositioned.
        public void AdvanceInteraction(float deltaTime, float normalized, float contactFraction)
        {
            if (!InteractionActive || !inputEnabled) return;
            interactionRemaining = Mathf.Max(.0001f, interactionDuration*(1-normalized));
            bool recovering = normalized > contactFraction;
            Quaternion desired = recovering && LinkedTrolley != null ? LinkedTrolley.Facing : interactionFacing;
            Vector3 currentForward=visual.forward; currentForward.y=0;
            Quaternion current=currentForward.sqrMagnitude>.001f?Quaternion.LookRotation(currentForward):desired;
            float turnRate=Mathf.Max(780f,180f/Mathf.Max(.05f,interactionDuration*contactFraction));
            Quaternion heading=Quaternion.RotateTowards(current,desired,turnRate*deltaTime);
            float phase = recovering ? (1-normalized)/(1-contactFraction) : normalized/contactFraction;
            interactionReach=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase));
            visual.rotation=heading*Quaternion.Euler(5f*interactionReach,0,0);
            Vector3 lean=transform.InverseTransformDirection(heading*Vector3.forward)*(.035f*interactionReach);
            visual.localPosition=visualRestPosition+lean+Vector3.down*(.055f*interactionReach);
            carryHull?.SyncPose();
        }

        public bool IsFacing(Vector3 target, float tolerance = 18f)
        {
            Vector3 direction=target-transform.position; direction.y=0;
            Vector3 forward=visual.forward; forward.y=0;
            return direction.sqrMagnitude<.0025f || Vector3.Angle(forward,direction)<=tolerance;
        }

        public void EndInteraction()
        {
            interactionRemaining=0; interactionReach=0;
            if (visual==null) return;
            visual.localPosition=visualRestPosition;
            Vector3 forward=visual.forward; forward.y=0;
            if (forward.sqrMagnitude>.001f)visual.rotation=Quaternion.LookRotation(forward);
        }

        private void Play(string clip)
        {
            if (legacy == null || clip == null || currentClip == clip) return;
            legacy.CrossFade(clip, .12f); currentClip = clip;
        }

        public void Teleport(Vector3 point)
        {
            planarVelocity = Vector3.zero;
            LinkedTrolley?.ResetDriveSpeed();
            Controller.enabled = false; transform.position = point;
            verticalSpeed = 0; Controller.enabled = true;
        }

        public void BeginInteraction(Vector3 target, float duration)
        {
            StopMotion();
            interactionDuration = Mathf.Max(.01f,duration); interactionRemaining=interactionDuration;
            Vector3 direction=target-transform.position; direction.y=0;
            interactionFacing=direction.sqrMagnitude>.01f?Quaternion.LookRotation(direction):visual.rotation;
            if(carryClip!=null)
            {
                Play(carryClip);
                if(legacy[carryClip]!=null){legacy[carryClip].time=legacy[carryClip].length*.1f;legacy[carryClip].speed=0;}
            }
        }

        public void StopMotion() { planarVelocity=Vector3.zero; Velocity=Vector3.zero; IsRunning=false; LinkedTrolley?.ResetDriveSpeed(); }
    }
}
