using System;
using UnityEngine;

namespace Guochao
{
    // The shopper and the complete basket footprint move as one swept assembly.
    // Parking preserves that world pose and makes the basket a normal solid obstacle.
    public sealed class LinkedTrolleyMotor : MonoBehaviour
    {
        public float turnDegreesPerSecond = 180f;
        public float gripReach = .40f;
        // Stationary (zero-throttle) caster pivot uses a modest fraction of the
        // full push-turn rate; rolling steering uses the full bounded rate.
        public float stationaryTurnFraction = .45f;
        public bool Blocked { get; private set; }
        public bool IsAttached { get; private set; }
        public float Width => localHalfExtents.x * 2f;
        public Vector3 HalfExtents => localHalfExtents;
        public Transform Trolley => trolley;
        public Quaternion Facing => facing;
        public Vector3 HandleStandingPoint => parkedStandingPoint;
        public Vector3 LastPredictedPosition { get; private set; }
        public Vector3 LastActualPosition { get; private set; }
        public CollisionFlags LastMoveCollisionFlags { get; private set; }
        public Quaternion LastAcceptedFacing { get; private set; }
        private PlayerMotor player;
        private WorldRuntime world;
        private Transform trolley;
        private Vector3 authoredForward, gripLocal, localCenter, localHalfExtents;
        private Vector3 attachmentOffset, parkedStandingPoint;
        private Quaternion facing = Quaternion.identity;
        private BoxCollider volume;
        private float driveSpeed;
        public const float ClearanceSkin = .015f;

        public void Initialize(PlayerMotor shopper, Transform cart, Transform forwardMarker, Transform handle)
        {
            if (forwardMarker == null || handle == null) throw new InvalidOperationException("购物车缺少方向或把手锚点。");
            player = shopper; world=shopper.GetComponentInParent<WorldRuntime>(); trolley = cart;
            authoredForward = forwardMarker.position - trolley.position; authoredForward.y = 0;
            authoredForward.Normalize();
            gripLocal = trolley.InverseTransformPoint(handle.position);
            Bounds bounds = WorldRuntime.RenderBounds(cart.gameObject);
            localCenter = cart.InverseTransformPoint(bounds.center);
            localHalfExtents = bounds.extents;
            // Retain the entire authored width/length. Only exclude the wheel's
            // ground-contact sliver, as in the existing curb collision model.
            float top = localCenter.y + localHalfExtents.y;
            localHalfExtents.y = (top - .04f) * .5f;
            localCenter.y = .04f + localHalfExtents.y;
            volume = cart.gameObject.AddComponent<BoxCollider>();
            volume.center = localCenter; volume.size = localHalfExtents * 2;
            volume.isTrigger = true; cart.gameObject.layer = 25;
            facing = shopper.visual.rotation;
            bool placed = false;
            for (int i = 0; i < 16; i++)
            {
                Quaternion candidate = Quaternion.Euler(0, i * 22.5f, 0);
                if (!IsPoseClear(player.transform.position, candidate)) continue;
                facing = candidate; placed = true; break;
            }
            if (!placed) throw new InvalidOperationException("购物车真实包络无法在采购出生点放置。");
            ApplyPose(player.transform.position, facing);
            IsAttached = true; player.LinkedTrolley = this;
            parkedStandingPoint = player.transform.position;
        }

        public bool CanAttach
        {
            get
            {
                if (IsAttached || player == null || volume == null) return false;
                Vector3 delta = player.transform.position - parkedStandingPoint; delta.y = 0;
                if (delta.sqrMagnitude > .55f * .55f || Mathf.Abs(player.transform.position.y-parkedStandingPoint.y)>.2f) return false;
                return ClearToPoint(parkedStandingPoint + Vector3.up * .8f);
            }
        }

        public void Detach()
        {
            if (!IsAttached) return;
            parkedStandingPoint = player.transform.position + facing*attachmentOffset;
            IsAttached = false; Blocked = false; driveSpeed = 0;
            player.StopMotion(); player.LinkedTrolley = null; player.pushing = false;
            volume.isTrigger = false; trolley.gameObject.layer = WorldRuntime.EnvironmentLayer;
            Physics.SyncTransforms();
        }

        public bool TryAttach()
        {
            if (!CanAttach) return false;
            // Preserve both transforms. The small accepted handle offset belongs to
            // this grip until the next release; neither participant is snapped.
            Vector3 delta = player.transform.position - parkedStandingPoint; delta.y = 0;
            attachmentOffset = Quaternion.Inverse(facing) * (-delta);
            volume.isTrigger = true; trolley.gameObject.layer = 25;
            IsAttached = true; player.LinkedTrolley = this; player.pushing = true;
            player.StopMotion(); Blocked = false; driveSpeed = 0;
            Physics.SyncTransforms();
            return true;
        }

        public Vector3 BasketContactPoint => player != null ? BasketContactPointAt(player.transform.position) : Vector3.zero;

        // Query the current basket pose from the supplied body position. This
        // does not move the basket or predict a new attached-cart heading.
        public Vector3 BasketContactPointAt(Vector3 bodyPosition)
        {
            if (volume == null || trolley == null) return Vector3.zero;
            Vector3 point = volume.ClosestPoint(bodyPosition + Vector3.up * .9f);
            // The rim, not a wheel/ground point, is the visible box destination.
            point.y = trolley.TransformPoint(localCenter).y;
            return point;
        }

        public bool IsNearBasket(float range = 1.10f)
            => player != null && IsNearBasketAt(player.transform.position, range);

        public bool IsNearBasketAt(Vector3 bodyPosition, float range = 1.10f)
        {
            if (volume == null || trolley == null) return false;
            Vector3 point = BasketContactPointAt(bodyPosition);
            Vector3 delta = point-bodyPosition; delta.y=0;
            return delta.sqrMagnitude <= range*range && ClearToPointAt(bodyPosition, point);
        }

        private bool ClearToPoint(Vector3 point)
            => player != null && ClearToPointAt(player.transform.position, point);

        private bool ClearToPointAt(Vector3 bodyPosition, Vector3 point)
        {
            Vector3 origin = bodyPosition+Vector3.up*.9f;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, (point-origin).normalized, (point-origin).magnitude,
                WorldRuntime.EnvironmentMask, QueryTriggerInteraction.Ignore))
                if (hit.collider != volume) return false;
            return true;
        }

        // Explicit scalar drive for attached real input. throttle: signed travel
        // along cart Facing (+forward / -reverse, never a heading flip). steering:
        // +right / -left yaw at the bounded turn rate (same sign at reverse, a
        // human-pushed cart pivots either way). Zero-throttle pivot is slower.
        // Each swept substep translates along the midpoint accepted heading so
        // W+A/D traces a curvature; blocked yaw falls back to translation along
        // the accepted heading, blocked translation to a swept pure turn. No
        // lateral strafe or per-axis x/z slide exists on this path.
        public bool TryDrive(float throttle, float steering, float speed, float deltaTime)
        {
            if (!IsAttached) { Blocked = false; return false; }
            if (deltaTime <= 0) return true;
            throttle = Mathf.Clamp(throttle, -1f, 1f);
            steering = Mathf.Clamp(steering, -1f, 1f);
            bool wantsMove = Mathf.Abs(throttle) > .001f;
            bool wantsTurn = Mathf.Abs(steering) > .001f;
            if (!wantsMove && !wantsTurn)
            {
                driveSpeed = 0; Blocked = false;
                LastPredictedPosition = player.transform.position;
                LastActualPosition = player.transform.position;
                LastAcceptedFacing = facing;
                LastMoveCollisionFlags = CollisionFlags.None;
                return true;
            }
            // Signed speed: releasing stops on the next step and reversing never
            // retains opposite momentum; same-sign travel ramps with load.
            float targetSpeed = wantsMove ? throttle * Mathf.Max(0, speed) : 0f;
            if (!wantsMove) driveSpeed = 0f;
            else
            {
                if (driveSpeed * targetSpeed < 0f) driveSpeed = 0f;
                driveSpeed = Mathf.MoveTowards(driveSpeed, targetSpeed, 4.5f * deltaTime);
            }
            float turnRate = Mathf.Max(0f,turnDegreesPerSecond) * (wantsMove ? 1f : Mathf.Clamp01(stationaryTurnFraction));
            float requestedYaw = steering * turnRate * deltaTime;
            Vector3 start = player.transform.position;
            Quaternion startFacing = facing;
            Vector3 requested = (facing * Vector3.forward) * driveSpeed * deltaTime;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(requested.magnitude / .025f,
                Mathf.Abs(requestedYaw) / .25f)));
            Vector3 accepted = start;
            Quaternion acceptedFacing = startFacing;
            Vector3 translationStep = requested / steps;
            float yawPerStep = requestedYaw / steps;
            Quaternion yawStep = Quaternion.Euler(0,yawPerStep,0);
            float acceptedYaw = 0f;
            for (int i = 0; i < steps; i++)
            {
                // Keep unit rotations: accumulated norm drift otherwise makes
                // Quaternion.Angle report a nonzero tiny turn as zero.
                Quaternion nextFacing = Quaternion.Normalize(acceptedFacing * yawStep);
                // Move along the midpoint of the accepted and next heading so a
                // combined push+steer follows an arc, not a chord.
                Quaternion midHeading = Quaternion.Slerp(acceptedFacing, nextFacing, .5f);
                Vector3 stepMove = midHeading * Vector3.forward * translationStep.magnitude *
                                   Mathf.Sign(driveSpeed == 0f ? 1f : driveSpeed);
                if (stepMove.sqrMagnitude > .0000000001f &&
                    TryStep(accepted, acceptedFacing, stepMove, nextFacing, out Vector3 next, out _))
                { accepted = next; acceptedFacing = nextFacing; acceptedYaw += Mathf.Abs(yawPerStep); continue; }
                // Yaw constrained: casters can still roll the full cart along the
                // currently accepted heading without changing it.
                Vector3 acceptedHeadingMove = acceptedFacing * Vector3.forward * (driveSpeed * deltaTime / steps);
                if (acceptedHeadingMove.sqrMagnitude > .0000000001f &&
                    TryStep(accepted, acceptedFacing, acceptedHeadingMove, acceptedFacing, out next, out _))
                { accepted = next; continue; }
                // Translation blocked: a swept pure turn is still legal escape.
                if (Quaternion.Angle(acceptedFacing, nextFacing) > .001f &&
                    IsPoseClear(accepted, nextFacing) &&
                    SweepClear(accepted, acceptedFacing, accepted, nextFacing, out _))
                { acceptedFacing = nextFacing; acceptedYaw += Mathf.Abs(yawPerStep); continue; }
                break;
            }
            LastPredictedPosition = accepted;
            LastAcceptedFacing = acceptedFacing;
            LastMoveCollisionFlags = player.Controller.Move(accepted - start);
            LastActualPosition = player.transform.position;
            facing = acceptedFacing;
            ApplyPose(player.transform.position, facing);
            Vector3 actual = player.transform.position - start; actual.y = 0;
            float actualYaw = acceptedYaw;
            bool blockedMove = requested.sqrMagnitude > .0000001f && actual.magnitude < requested.magnitude * .1f;
            bool blockedTurn = Mathf.Abs(requestedYaw) > .01f && actualYaw < Mathf.Abs(requestedYaw) * .1f;
            Blocked = blockedMove || blockedTurn;
            return !Blocked;
        }

        public void ResetDriveSpeed() => driveSpeed = 0;

        public bool TryMove(Vector3 direction, float speed, float deltaTime)
        {
            if (!IsAttached) { Blocked=false; return false; }
            direction.y = 0;
            if (direction.sqrMagnitude < .0001f || deltaTime <= 0) { Blocked = false; return true; }
            direction = Vector3.ClampMagnitude(direction, 1);
            Vector3 start = player.transform.position;
            Quaternion startFacing = facing;
            bool reversing = Vector3.Dot(direction.normalized, facing*Vector3.forward) < -.5f;
            Quaternion desiredFacing = reversing ? facing : Quaternion.RotateTowards(facing, Quaternion.LookRotation(direction), turnDegreesPerSecond * deltaTime);
            Vector3 requested = direction * Mathf.Max(0,speed) * deltaTime;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(requested.magnitude/.025f, Quaternion.Angle(startFacing,desiredFacing)/.25f)));
            Vector3 accepted = start;
            Quaternion acceptedFacing = startFacing;
            Vector3 translationStep = requested/steps;
            float turnStep = Quaternion.Angle(startFacing,desiredFacing)/steps;
            for (int i = 0; i < steps; i++)
            {
                Quaternion nextFacing = Quaternion.RotateTowards(acceptedFacing, desiredFacing, turnStep);
                if (TryStep(accepted, acceptedFacing, translationStep, nextFacing, out Vector3 next, out _))
                { accepted=next; acceptedFacing=nextFacing; continue; }
                // If steering is constrained, the casters can still move the full
                // cart in a clear direction without changing the current yaw.
                if (TryStep(accepted, acceptedFacing, translationStep, acceptedFacing, out next, out Vector3 normal))
                { accepted=next; continue; }
                Vector3 slide = Vector3.ProjectOnPlane(translationStep, normal); slide.y=0;
                if (normal.sqrMagnitude>.01f && Vector3.Dot(slide,translationStep)>0 &&
                    slide.sqrMagnitude>.0000001f && TryStep(accepted,acceptedFacing,slide,acceptedFacing,out next,out _))
                { accepted=next; continue; }
                // Corner contact may have no useful cast normal (an overlap at the
                // end pose). Test both planar components; every candidate still
                // uses the same complete box/capsule and sweep, never a smaller box.
                Vector3 x = new Vector3(translationStep.x,0,0), z = new Vector3(0,0,translationStep.z);
                Vector3 first = x.sqrMagnitude>=z.sqrMagnitude?x:z, second=x.sqrMagnitude>=z.sqrMagnitude?z:x;
                if ((first.sqrMagnitude>.0000001f && TryStep(accepted,acceptedFacing,first,acceptedFacing,out next,out _)) ||
                    (second.sqrMagnitude>.0000001f && TryStep(accepted,acceptedFacing,second,acceptedFacing,out next,out _)))
                { accepted=next; continue; }
                if (Quaternion.Angle(acceptedFacing,nextFacing)>.001f && IsPoseClear(accepted,nextFacing) &&
                    SweepClear(accepted,acceptedFacing,accepted,nextFacing,out _))
                { acceptedFacing=nextFacing; continue; }
                break;
            }
            LastPredictedPosition=accepted;LastAcceptedFacing=acceptedFacing;
            LastMoveCollisionFlags=player.Controller.Move(accepted - start);
            LastActualPosition=player.transform.position;
            facing = acceptedFacing;
            ApplyPose(player.transform.position, facing);
            Vector3 actual=player.transform.position-start; actual.y=0;
            Blocked=requested.sqrMagnitude>.0000001f && actual.magnitude<requested.magnitude*.1f;
            return !Blocked;
        }

        private bool TryStep(Vector3 previous, Quaternion previousFacing, Vector3 displacement, Quaternion nextFacing,
            out Vector3 next, out Vector3 normal)
        {
            next=previous+displacement; normal=Vector3.zero;
            if (!TryGround(next,out float ground)) return false;
            next.y=ground+.03f;
            bool sweepClear=SweepClear(previous,previousFacing,next,nextFacing,out normal);
            return sweepClear && IsPoseClear(next,nextFacing);
        }

        public bool IsPoseClear(Vector3 shopperPosition, Quaternion shopperFacing)
        {
            Pose pose = CartPose(shopperPosition, shopperFacing);
            Vector3 center = pose.position + pose.rotation * localCenter;
            Vector3 paddedHalf=localHalfExtents+new Vector3(ClearanceSkin,0,ClearanceSkin);
            // CharacterController queries can miss the nominal capsule edge.
            // Share the crowd's full body envelope for every swept sub-step.
            if(world!=null&&world.Crowd!=null&&world.Crowd.OverlapsBox(center,paddedHalf,pose.rotation))return false;
            if (Physics.CheckBox(center, paddedHalf, pose.rotation, WorldRuntime.EnvironmentMask, QueryTriggerInteraction.Ignore)) return false;
            return !Physics.CheckCapsule(shopperPosition+Vector3.up*.29f, shopperPosition+Vector3.up*1.40f, .25f, WorldRuntime.EnvironmentMask, QueryTriggerInteraction.Ignore);
        }

        private bool SweepClear(Vector3 previous, Quaternion previousFacing, Vector3 next, Quaternion nextFacing, out Vector3 normal)
        {
            normal=Vector3.zero;
            Pose from = CartPose(previous, previousFacing), to = CartPose(next, nextFacing);
            Vector3 a = from.position + from.rotation * localCenter;
            Vector3 b = to.position + to.rotation * localCenter;
            Vector3 displacement = b-a;
            // Fine angular sampling and the unchanged skin cover each corner arc.
            if (displacement.sqrMagnitude >= .000001f && Physics.BoxCast(a, localHalfExtents + new Vector3(ClearanceSkin,0,ClearanceSkin),
                displacement.normalized, out RaycastHit boxHit, from.rotation, displacement.magnitude,
                WorldRuntime.EnvironmentMask, QueryTriggerInteraction.Ignore))
            { normal=boxHit.normal; return false; }
            Vector3 shopperDelta=next-previous;
            if (shopperDelta.sqrMagnitude>=.000001f && Physics.CapsuleCast(previous+Vector3.up*.29f,
                previous+Vector3.up*1.40f,.25f,shopperDelta.normalized,out RaycastHit bodyHit,shopperDelta.magnitude,
                WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))
            { normal=bodyHit.normal; return false; }
            return true;
        }

        private bool TryGround(Vector3 position, out float height)
        {
            if (Physics.Raycast(position+Vector3.up*.35f, Vector3.down, out RaycastHit hit, 1f, WorldRuntime.EnvironmentMask, QueryTriggerInteraction.Ignore))
            { height = hit.point.y; return Mathf.Abs(height-(player.transform.position.y-.03f)) <= .20f; }
            height = position.y-.03f; return false;
        }

        private Pose CartPose(Vector3 shopperPosition, Quaternion shopperFacing)
        {
            Vector3 forward = shopperFacing * Vector3.forward;
            Quaternion rotation = Quaternion.FromToRotation(authoredForward, forward);
            Vector3 position = shopperPosition + forward*gripReach - rotation*new Vector3(gripLocal.x,0,gripLocal.z) + shopperFacing*attachmentOffset;
            position.y = shopperPosition.y - .03f + .025f;
            return new Pose(position,rotation);
        }

        private void ApplyPose(Vector3 shopperPosition, Quaternion shopperFacing)
        {
            Pose pose = CartPose(shopperPosition,shopperFacing);
            trolley.SetPositionAndRotation(pose.position,pose.rotation);
            // Interaction temporarily releases the visible upper-body pose; the
            // trolley's physical yaw remains unchanged until movement resumes.
            if (!player.InteractionActive) player.visual.rotation = shopperFacing;
            parkedStandingPoint=shopperPosition+shopperFacing*attachmentOffset;
        }

        public void ResetPoseForValidation(Vector3 shopperPosition, Quaternion shopperFacing)
        {
            player.Teleport(shopperPosition); facing = shopperFacing; attachmentOffset=Vector3.zero;
            ApplyPose(shopperPosition, facing); Physics.SyncTransforms();
        }

        public void CapturePose(WorldSaveData data)
        {
            data.hasCart=trolley!=null;if(!data.hasCart)return;
            data.cartPosition=trolley.position;data.cartRotation=trolley.rotation;data.cartFacing=facing;
            data.cartAttached=IsAttached;data.cartAttachmentOffset=attachmentOffset;data.cartStandingPoint=parkedStandingPoint;
        }
        public void RestorePose(WorldSaveData data)
        {
            if(data==null||!data.hasCart||trolley==null)return;
            trolley.SetPositionAndRotation(data.cartPosition,data.cartRotation);facing=data.cartFacing;
            attachmentOffset=data.cartAttachmentOffset;parkedStandingPoint=data.cartStandingPoint;
            IsAttached=data.cartAttached;Blocked=false;driveSpeed=0;player.LinkedTrolley=IsAttached?this:null;player.pushing=IsAttached;
            volume.isTrigger=IsAttached;trolley.gameObject.layer=IsAttached?25:WorldRuntime.EnvironmentLayer;
            player.StopMotion();Physics.SyncTransforms();
        }
    }
}
