using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Menu-only stage that renders the REAL Home room (same Models/HomeWorld
    // prefab, stove placement and HomeBedAI swap the live Home uses, built by
    // CreationRoomStage) with the real Aholo Player model working at the
    // preparation counter, into a RenderTexture for the creation screen.
    //
    // ROOM8: the work performance is no longer a decorative bone wiggle. The
    // actor is driven by the SAME VendorWorkActor component the gameplay
    // cooking loop uses: real 52-bone rig (RightArm/RightForeArm/RightHand…),
    // analytic two-bone IK with measured bone lengths (bones never stretch,
    // the hand never passes through the prop), and the room's real pot lid +
    // named LidHandle as the tool, so the actor walks up and honestly lifts
    // the lid off the stove. The menu drives VendorWorkActor.Tick with
    // UNSCALED time itself (automaticTick=false) because opening this menu
    // sets Time.timeScale to 0. If the rig or clips cannot be bound, the
    // stage falls back to a lean-only performance and says so through
    // VendorDriven so the validation fixture can fail the IK evidence.
    //
    // The stage lives far outside the playable world on layer 31, so gameplay
    // cameras, colliders and the WorldRuntime never see it. Everything here
    // is created and destroyed with the menu; closing the screen releases the
    // render texture, the room, the actor and the menu-only materials. On
    // resolution change the render texture is rebuilt at the new back-buffer
    // size and the camera is reframed — no stretched or stale-surface output.
    public sealed class CreationPortraitStage : MonoBehaviour
    {
        public const int PortraitLayer = 31;
        private static readonly Vector3 StagePosition = new Vector3(12000f, 0f, 12000f);

        private enum ActorState { Idle, WalkToCounter, Work, WalkBack }

        private RenderTexture target;
        private Camera portraitCamera;
        private GameObject stageRoot;
        private RawImage view;
        private Material displayMaterial;
        private readonly List<Material> menuMaterials = new List<Material>();
        private CreationRoomStage.Room room;
        private GameObject actor;
        private Animation legacyAnimation;
        private AnimationState idleState, walkState;
        private AnimationClip idleClip, walkClip;
        private float idleTime, walkTime;
        private float idleWeight = 1f, walkWeight;

        // Procedural errand state.
        private ActorState state = ActorState.Idle;
        private float stateTimer;
        private Transform spineBone, headBone, armR, armL;
        private Quaternion spineRest, headRest, armRRest, armLRest;
        private bool bonesReady;

        // ROOM8 real work execution (shared gameplay component, menu-driven).
        private VendorWorkActor vendorWork;
        private VendorWorkTarget workTarget;
        private bool vendorDriven;
        private Transform rightHandBone, leftHandBone;
        private float errandGuard;
        private bool pendingWork;
        private bool vendorContactReached;
        private float vendorContactErrorMax;
        private string vendorFailureReason = "";

        // Public, read-only evidence surface for CreationRoom7Validation.
        public string ActorStageName => state.ToString();
        public bool VendorDriven => vendorDriven;
        public Transform RightHandBone => rightHandBone;
        public Transform LeftHandBone => leftHandBone;
        public Transform ToolLid => room != null ? room.toolLid : null;
        public Vector3 WorkPointWorld => room != null && room.valid ? room.workPoint : Vector3.zero;
        // ROOM8R contact/failure evidence: palm-to-grip error at commit and
        // the rig's own failure text, if any.
        public bool VendorContactReached => vendorContactReached;
        public float VendorContactErrorMax => vendorContactErrorMax;
        public string VendorFailureReason => vendorFailureReason;

        // Camera framing parameters (kept so a resolution change can reframe).
        private Vector3 frameFocus, frameOffset = new Vector3(-13f, 19f, -17f);
        private float frameSize = 4.2f;
        private bool roomFraming;
        private int builtScreenWidth, builtScreenHeight;

        public static CreationPortraitStage Create(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            // Full-stretch so the live room picture always covers the whole
            // screen: on ultrawide (canvas Expand keeps 1080 ref-height, the
            // layout only spans x0..1920) a fixed 1920x1080 top-left rect
            // would leave an uncovered strip, and a fixed-size rect would
            // letterbox other aspect ratios. The RenderTexture size and the
            // camera aspect are taken from the ACTUAL screen at Build time.
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;
            var stage = go.AddComponent<CreationPortraitStage>();
            stage.view = raw;
            stage.Build(width, height);
            return stage;
        }

        private void Build(float width, float height)
        {
            // Render at the real back-buffer resolution so the portrait is
            // never stretched or soft: 1280x720, 1920x1080 and 2560x1080 all
            // get a 1:1 texel mapping. The fallback only guards degenerate
            // headless values.
            int rtWidth = Screen.width > 0 ? Screen.width : (int)width;
            int rtHeight = Screen.height > 0 ? Screen.height : (int)height;
            builtScreenWidth = rtWidth; builtScreenHeight = rtHeight;
            target = NewRenderTexture(rtWidth, rtHeight);
            stageRoot = new GameObject("CreationPortraitWorld");
            stageRoot.transform.position = StagePosition;

            // The real room is built first so the actor and camera compose
            // against the actual counter, not against empty space.
            room = CreationRoomStage.Build(stageRoot.transform, PortraitLayer, menuMaterials);

            GameObject model = null;
            GameObject prefab = Resources.Load<GameObject>("Models/Player");
            if (prefab != null)
            {
                Vector3 start = room.valid ? room.idlePoint : Vector3.zero;
                // ROOM8R root-wrapper split (same pattern StreetStallStage's
                // live portrait uses): an EMPTY, non-animated "Player_Portrait"
                // root owns the world position and facing the work rig moves,
                // while the real FBX prefab — whose root carries the legacy
                // Animation component — hangs underneath at local zero.
                // VendorWorkActor.Bind picks animationRoot = the Animation
                // host and SamplePose resets every pose node's local position
                // and rotation to the captured rest on EVERY sub-step; when
                // the animated root and the walked root were the same object
                // (native-r2: actorX/Y/Z frozen for all 8 samples) each gait
                // application restored the walk parent's coordinates and the
                // actor could never leave idlePoint. The wrapper is outside
                // the sampled subtree, so the reset can no longer pin it.
                // The validation fixture keeps locating "Player_Portrait" as
                // a direct child of CreationPortraitWorld.
                GameObject wrapper = new GameObject("Player_Portrait");
                wrapper.transform.SetParent(stageRoot.transform, false);
                wrapper.transform.position = start;
                wrapper.layer = PortraitLayer;
                model = Instantiate(prefab, wrapper.transform, false);
                model.name = "Player_PortraitModel";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // The errand drives THIS menu-only clone; without the
                // assignment the field stayed null and the errand never ran.
                // The gameplay Player prefab itself is untouched.
                actor = wrapper;
                PrepareModel(model);
            }
            else Debug.LogWarning("CREATION_UI 缺少 Resources/Models/Player，左侧改用纯背景。");

            SetupLights();
            SetupCamera(model, width, height);
            BindWorkRig(model);

            view.texture = target;
            Shader shader = Resources.Load<Shader>("Shaders/CreationPortrait");
            if (shader != null)
            {
                displayMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                displayMaterial.SetFloat("_Softness", .16f);
                view.material = displayMaterial;
            }
        }

        private static RenderTexture NewRenderTexture(int w, int h)
            => new RenderTexture(Mathf.Max(16, w), Mathf.Max(16, h), 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "CreationPortraitRT" };

        // Mount the legacy idle/walk clips with the same rules as
        // PlayerMotor.Initialize. When the shared VendorWorkActor takes over
        // (vendorDriven) the legacy Animation component stays DISABLED: the
        // vendor resets every pose node and samples Idle_Loop/Walk_Loop clips
        // itself, so a second enabled legacy mixer would fight it. The legacy
        // mixer below only runs on the explicit fallback path.
        private void PrepareModel(GameObject model)
        {
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = PortraitLayer;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null) animator.enabled = false;

            // Same authored-facing correction PlayerMotor.Initialize applies.
            Transform origin = null, forward = null;
            foreach (Transform marker in model.GetComponentsInChildren<Transform>(true))
            {
                if (marker.name == "PlayerOrigin") origin = marker;
                if (marker.name == "PlayerForward") forward = marker;
            }
            if (origin != null && forward != null && model.transform.childCount > 0)
            {
                Vector3 facing = forward.position - origin.position;
                facing.y = 0;
                if (facing.sqrMagnitude > .01f)
                    model.transform.GetChild(0).localRotation =
                        Quaternion.FromToRotation(facing.normalized, Vector3.forward) * model.transform.GetChild(0).localRotation;
            }
            // The wrapper holds the world facing; the FBX child keeps local
            // identity so VendorWorkActor.Bind measures the PlayerOrigin/
            // PlayerForward markers against the wrapper rotation exactly
            // once and derives its forwardCorrection from that — no double
            // rotation, no re-correction of the visual between gaits.
            actor.transform.rotation = room.valid
                ? Quaternion.LookRotation(-room.standFacing, Vector3.up)
                : Quaternion.identity;

            GameArtRendering.Apply(model, false);
            ApplyMenuToon(model);

            var clips = new List<AnimationClip>(Resources.LoadAll<AnimationClip>("Models/Player"));
            clips.AddRange(Resources.LoadAll<AnimationClip>("Models/Player@Idle_Loop"));
            clips.AddRange(Resources.LoadAll<AnimationClip>("Models/Player@Walk_Loop"));
            Animation legacy = model.GetComponentInChildren<Animation>();
            if (legacy == null) legacy = model.GetComponentInChildren<Animator>()?.gameObject.AddComponent<Animation>();
            if (legacy == null) legacy = (model.transform.childCount > 0 ? model.transform.GetChild(0).gameObject : model).AddComponent<Animation>();
            foreach (AnimationClip clip in clips)
            {
                if (!clip.legacy || clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                legacy.AddClip(clip, clip.name);
                AnimationState state = legacy[clip.name];
                if (state != null) state.wrapMode = WrapMode.Loop;
                string lower = clip.name.ToLowerInvariant();
                if (idleClip == null && lower.Contains("idle"))
                { idleClip = clip; idleState = state; }
                if (walkClip == null && (lower.Contains("walk") || lower.Contains("run")) && !lower.Contains("carry"))
                { walkClip = clip; walkState = state; }
            }
            if (idleState == null && walkState != null)
            { idleState = walkState; idleClip = walkClip; } // degenerate fallback: never leave the mixer empty
            if (walkState == null && idleState != null)
            { walkState = idleState; walkClip = idleClip; }
            legacyAnimation = legacy;
            legacy.playAutomatically = false;
            if (legacyAnimation != null && idleClip != null)
            {
                foreach (AnimationState s in legacyAnimation)
                { s.enabled = true; s.speed = 0f; s.weight = s == idleState ? 1f : 0f; s.time = 0f; }
                legacyAnimation.Play(idleClip.name);
                idleState.weight = 1;
            }
        }

        // Bind the shared gameplay work rig (52-bone analytic two-bone IK).
        // The menu drives Tick itself with unscaled time because the whole
        // creation screen runs under Time.timeScale = 0. Any binding failure
        // (missing rig node or clip) falls back to the lean-only performance
        // and is reported through VendorDriven=false.
        private void BindWorkRig(GameObject model)
        {
            vendorDriven = false;
            if (model == null || !room.valid) { CacheBones(model); return; }
            try
            {
                // Bind the WRAPPER, not the FBX subtree: the vendor rig then
                // moves a node no pose reset can touch, while the animation
                // host inside the FBX subtree stays exclusively sampled.
                vendorWork = actor.AddComponent<VendorWorkActor>();
                vendorWork.automaticTick = false;
                vendorWork.walkSpeed = 1.15f; // calm kitchen pace
                vendorWork.Bind(actor.transform);
                vendorWork.Failed += OnVendorFailed;
                vendorWork.ContactReached += OnVendorContact;
                if (room.toolLid != null && room.toolLidHandle != null)
                {
                    workTarget = new VendorWorkTarget
                    {
                        standPoint = room.workStandPoint,
                        workPoint = room.workPoint,
                        heldTool = room.toolLid,
                        toolGrip = room.toolLidHandle,
                    };
                }
                else
                {
                    workTarget = new VendorWorkTarget
                    {
                        standPoint = room.workStandPoint,
                        workPoint = room.workPoint,
                    };
                }
                rightHandBone = FindBone(model.transform, "RightHand");
                leftHandBone = FindBone(model.transform, "LeftHand");
                if (legacyAnimation != null) legacyAnimation.enabled = false;
                vendorDriven = true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("CREATION_UI VendorWorkActor 绑定失败，退回纯演出: " + error.Message);
                if (vendorWork != null) { try { Destroy(vendorWork); } catch { } vendorWork = null; }
                if (legacyAnimation != null) legacyAnimation.enabled = true;
                CacheBones(model);
            }
        }

        private void OnVendorFailed(string reason)
        {
            // A failed contact must never strand the errand: walk back and
            // idle. The failure itself stays visible in the log for QA.
            vendorFailureReason = reason;
            pendingWork = false;
            if (state == ActorState.Work || state == ActorState.WalkToCounter)
                BeginWalkBack();
        }

        // Honest contact evidence (palm-to-grip error at the commit
        // timestamp, measured by the shared rig — never wrist or a fake work
        // pose) for the native acceptance fixture.
        private void OnVendorContact(VendorWorkAudit audit)
        {
            vendorContactReached = true;
            vendorContactErrorMax = Mathf.Max(vendorContactErrorMax, audit.maxContactError);
        }

        private static Transform FindBone(Transform root, string suffix)
        {
            if (root == null) return null;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            {
                string name = node.name;
                int colon = name.LastIndexOf(':');
                if (colon >= 0) name = name.Substring(colon + 1);
                if (name == suffix || name == "mixamorig" + suffix) return node;
            }
            return null;
        }

        // Bones for the fallback lean-only performance, resolved once by name.
        // Side matching must never accept "Armature"/"arm" generics: a node is
        // an arm bone only when its name carries an explicit right/left token.
        private void CacheBones(GameObject model)
        {
            if (model == null) { bonesReady = false; return; }
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            {
                string n = bone.name.ToLowerInvariant();
                if (n.Contains("armature")) continue;
                if (spineBone == null && (n.Contains("spine") || n.Contains("chest") || n.Contains("upperbody")))
                { spineBone = bone; spineRest = bone.localRotation; }
                if (headBone == null && n.Contains("head"))
                { headBone = bone; headRest = bone.localRotation; }
                bool right = n.Contains("right") || n.EndsWith("_r") || n.EndsWith(".r");
                bool left = n.Contains("left") || n.EndsWith("_l") || n.EndsWith(".l");
                if (armR == null && right && (n.Contains("arm") || n.Contains("shoulder")))
                { armR = bone; armRRest = bone.localRotation; }
                if (armL == null && left && (n.Contains("arm") || n.Contains("shoulder")))
                { armL = bone; armLRest = bone.localRotation; }
            }
            bonesReady = spineBone != null || headBone != null;
        }

        private void SetupLights()
        {
            // Same morning key + cool fill the Home world lights itself with.
            var keyObject = new GameObject("CreationPortraitKey");
            keyObject.transform.SetParent(stageRoot.transform, false);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, .91f, .73f);
            key.intensity = 1.12f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .72f;
            key.cullingMask = 1 << PortraitLayer;
            keyObject.transform.rotation = Quaternion.Euler(52f, -32f, 0f);

            var fillObject = new GameObject("CreationPortraitFill");
            fillObject.transform.SetParent(stageRoot.transform, false);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(.67f, .81f, 1f);
            fill.intensity = .30f;
            fill.shadows = LightShadows.None;
            fill.cullingMask = 1 << PortraitLayer;
            fillObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
        }

        private void SetupCamera(GameObject model, float width, float height)
        {
            var cameraObject = new GameObject("CreationPortraitCamera");
            cameraObject.transform.SetParent(stageRoot.transform, false);
            portraitCamera = cameraObject.AddComponent<Camera>();
            portraitCamera.enabled = false;
            portraitCamera.orthographic = true;
            portraitCamera.cullingMask = 1 << PortraitLayer;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(.65f, .63f, .60f); // the Home world's own backdrop tone
            portraitCamera.nearClipPlane = .1f;
            portraitCamera.farClipPlane = 250f;
            portraitCamera.allowHDR = false;
            portraitCamera.targetTexture = target;

            if (room.valid)
            {
                // 2.5D building view identical in spirit to WorldRuntime's Home
                // camera: fixed oblique offset looking at the counter area.
                // Focus between the stove and the supported prep stand so the
                // actor's whole walk-and-lift errand stays in frame.
                roomFraming = true;
                frameFocus = Vector3.Lerp(room.stovePosition, room.prepCenter, .5f);
                Vector3 offset = new Vector3(-13f, 19f, -17f);
                Transform envCamTarget = FindInRoom("CameraTarget"), envCamPos = FindInRoom("CameraPosition");
                if (envCamTarget != null && envCamPos != null) offset = envCamPos.position - envCamTarget.position;
                frameOffset = offset;
                frameSize = 4.2f;
                ReframeCamera();
                portraitCamera.enabled = true;
                return;
            }
            if (model == null) { portraitCamera.aspect = (float)target.width / Mathf.Max(1, target.height); portraitCamera.enabled = true; return; }

            // Fallback (no HomeWorld prefab): old full-body portrait framing.
            portraitCamera.aspect = (float)target.width / Mathf.Max(1, target.height);
            SamplePose(0f, 0f);
            if (!ComputeBodyBounds(model, out Bounds body))
            {
                Debug.LogWarning("CREATION_UI 未找到可用SkinnedMeshRenderer，人像相机保持默认取景。");
                portraitCamera.enabled = true;
                return;
            }
            float bodyHeight = body.size.y;
            float bodyWidth = body.size.x;
            float aspect = portraitCamera.aspect;
            float frameHeight = bodyHeight / 0.80f;
            float size2 = frameHeight * .5f;
            float halfWidthNeed = bodyWidth * .5f * 1.10f;
            size2 = Mathf.Max(size2, halfWidthNeed / aspect);
            portraitCamera.orthographicSize = size2;
            float camY = body.min.y + size2 - 0.07f * frameHeight;
            portraitCamera.transform.position = new Vector3(body.center.x, camY, body.max.z + 4f);
            portraitCamera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            portraitCamera.enabled = true;
        }

        // Compose the room camera against the CURRENT viewport: the actor
        // stays anchored to a FIXED canvas-reference x instead of a fixed
        // viewport fraction (the career cards occupy ref x452..824 and the
        // paper x812+, so the only reliably clear band is x0..440). The
        // canvas scaler uses Expand (scaleFactor = min(sw/1920, sh/1080)), so
        // a reference x maps to viewport u = refX*scaleFactor/screenWidth.
        // Called again after any RenderTexture resize.
        private void ReframeCamera()
        {
            if (portraitCamera == null || target == null || !roomFraming) return;
            portraitCamera.aspect = (float)target.width / Mathf.Max(1, target.height);
            Vector3 focus = frameFocus;
            Vector3 right = Vector3.Cross(Vector3.up, -frameOffset).normalized;
            float scaleFactor = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            const float RefTargetX = 240f; // middle of the x0..440 clear band
            float uTarget = Mathf.Clamp01(RefTargetX * scaleFactor / Mathf.Max(1, Screen.width));
            float viewWidth = 2f * frameSize * portraitCamera.aspect;
            focus += right * ((0.5f - uTarget) * viewWidth);
            portraitCamera.orthographicSize = frameSize;
            portraitCamera.transform.position = focus + frameOffset;
            portraitCamera.transform.rotation = Quaternion.LookRotation(-frameOffset, Vector3.up);
        }

        // Resolution change (e.g. the native fixture switching from
        // 1920x1080 to 1280x720): rebuild the render texture at the new
        // back-buffer size and reframe. References are nulled before release
        // so neither the RawImage nor the camera keeps a released surface.
        private void SyncRenderTargetToScreen()
        {
            if (target == null || view == null || portraitCamera == null) return;
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0 || (w == builtScreenWidth && h == builtScreenHeight)) return;
            builtScreenWidth = w; builtScreenHeight = h;
            portraitCamera.targetTexture = null;
            view.texture = null;
            target.Release();
            Destroy(target);
            target = NewRenderTexture(w, h);
            view.texture = target;
            portraitCamera.targetTexture = target;
            ReframeCamera();
        }

        private Transform FindInRoom(string name)
        {
            if (room.root == null) return null;
            foreach (Transform t in room.root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name == "Anchor_" + name) return t;
            return null;
        }

        // ------------------------------------------------------------------
        // Per-frame: unscaled-time drive + the errand state machine.
        // ------------------------------------------------------------------
        // MENU-ONLY bounded step. VendorWorkActor.Tick re-samples the 52-bone
        // animation once per ~1/60s of supplied delta, so feeding it a raw
        // unscaledDeltaTime on a slow frame does more sub-samples, which
        // makes the frame slower — the feedback loop that pinned one core at
        // 100% in native-r1. Clamp to 2/60s: at most 2 real IK sub-steps per
        // frame, no unbounded catch-up, and nothing accumulates across big
        // frames. Non-finite or negative deltas are rejected outright.
        private const float MaxMenuStep = 2f / 60f;

        private static float MenuDeltaTime(float raw)
        {
            if (!float.IsFinite(raw) || raw <= 0f) return 0f;
            return Mathf.Min(raw, MaxMenuStep);
        }

        private void Update()
        {
            float dt = MenuDeltaTime(Time.unscaledDeltaTime);
            SyncRenderTargetToScreen();

            if (vendorDriven && vendorWork != null)
            {
                vendorWork.Tick(dt, false);
                if (room.valid && actor != null) TickVendorErrand(dt);
            }
            else
            {
                TickLegacyMixer(dt);
                if (room.valid && actor != null) TickErrand(dt);
            }
        }

        private void TickLegacyMixer(float dt)
        {
            if (legacyAnimation == null || idleState == null || !legacyAnimation.enabled) return;
            bool walking = state == ActorState.WalkToCounter || state == ActorState.WalkBack;
            idleWeight = Mathf.MoveTowards(idleWeight, walking ? 0f : 1f, dt * 4f);
            walkWeight = Mathf.MoveTowards(walkWeight, walking ? 1f : 0f, dt * 4f);
            if (walking) walkTime += dt * .4f;
            else idleTime += dt;
            if (idleClip != null && idleState != null && !ReferenceEquals(idleState, walkState))
            { idleState.weight = idleWeight; idleState.time = Mathf.Repeat(idleTime, Mathf.Max(.01f, idleClip.length)); }
            if (walkClip != null && walkState != null && !ReferenceEquals(walkState, idleState))
            { walkState.weight = walkWeight; walkState.time = Mathf.Repeat(walkTime, Mathf.Max(.01f, walkClip.length)); }
            if (ReferenceEquals(idleState, walkState)) { idleState.weight = 1; idleState.time = Mathf.Repeat(idleTime, Mathf.Max(.01f, idleClip.length)); }
            legacyAnimation.Sample();
        }

        // Vendor-driven errand: Idle → walk to the counter → real IK lid lift
        // (VendorWorkAction.Flip on the room's pot lid) → walk back. All
        // motion, facing, foot planting and the tool follow the shared
        // gameplay work rig; a guard timer recovers the loop if a request is
        // ever lost.
        private void TickVendorErrand(float dt)
        {
            stateTimer += dt;
            errandGuard += dt;
            switch (state)
            {
                case ActorState.Idle:
                    if (stateTimer > 2.6f && !vendorWork.IsBusy)
                    {
                        errandGuard = 0f;
                        pendingWork = true;
                        SwitchTo(ActorState.WalkToCounter);
                        vendorWork.MoveTo(room.standPoint, room.stovePosition);
                    }
                    break;

                case ActorState.WalkToCounter:
                    if (!pendingWork) { BeginWalkBack(); break; }
                    if (!vendorWork.IsBusy && errandGuard < 12f)
                    {
                        // Arrived and turned: commit the real work action.
                        SwitchTo(ActorState.Work);
                        errandGuard = 0f;
                        VendorWorkAction action = workTarget != null && workTarget.heldTool != null
                            ? VendorWorkAction.Flip
                            : VendorWorkAction.Clean;
                        if (!vendorWork.Perform(action, workTarget, 3.6f))
                        { pendingWork = false; BeginWalkBack(); }
                    }
                    else if (errandGuard >= 12f) { pendingWork = false; BeginWalkBack(); }
                    break;

                case ActorState.Work:
                    if (!vendorWork.IsBusy || errandGuard >= 10f)
                    { pendingWork = false; BeginWalkBack(); }
                    break;

                case ActorState.WalkBack:
                    if (!vendorWork.IsBusy && errandGuard > .5f) SwitchTo(ActorState.Idle);
                    else if (errandGuard >= 12f) SwitchTo(ActorState.Idle);
                    break;
            }
        }

        private void BeginWalkBack()
        {
            if (vendorWork != null && vendorWork.IsBusy) vendorWork.Cancel();
            SwitchTo(ActorState.WalkBack);
            errandGuard = 0f;
            vendorWork.MoveTo(room.idlePoint, room.stovePosition);
        }

        // Legacy fallback errand (lean-only, used only when the shared rig
        // could not be bound). Kept so the menu never loses all motion.
        private void TickErrand(float dt)
        {
            stateTimer += dt;
            switch (state)
            {
                case ActorState.Idle:
                    if (stateTimer > 2.6f) SwitchTo(ActorState.WalkToCounter);
                    break;
                case ActorState.WalkToCounter:
                    if (WalkToward(room.standPoint, dt)) SwitchTo(ActorState.Work);
                    break;
                case ActorState.Work:
                    {
                        FaceToward(room.stovePosition, dt, 260f);
                        float t = Mathf.Clamp01(stateTimer / 3.8f);
                        ApplyWorkPose(t, dt);
                        if (t >= 1f) { ClearBonePose(); SwitchTo(ActorState.WalkBack); }
                    }
                    break;
                case ActorState.WalkBack:
                    if (WalkToward(room.idlePoint, dt)) SwitchTo(ActorState.Idle);
                    break;
            }
        }

        private void SwitchTo(ActorState next)
        {
            state = next; stateTimer = 0f;
        }

        private bool WalkToward(Vector3 goal, float dt)
        {
            Vector3 pos = actor.transform.position;
            Vector3 flat = goal - pos; flat.y = 0;
            float dist = flat.magnitude;
            if (dist < .03f) return true;
            float step = Mathf.Min(dist, 1.15f * dt);
            actor.transform.position = pos + flat.normalized * step;
            Quaternion look = Quaternion.LookRotation(flat.normalized, Vector3.up);
            actor.transform.rotation = Quaternion.Slerp(actor.transform.rotation, look, 1f - Mathf.Exp(-10f * dt));
            return dist - step < .03f;
        }

        private void FaceToward(Vector3 point, float dt, float degreesPerSecond)
        {
            Vector3 dir = point - actor.transform.position; dir.y = 0;
            if (dir.sqrMagnitude < .001f) return;
            Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            actor.transform.rotation = Quaternion.RotateTowards(actor.transform.rotation, want, degreesPerSecond * dt);
        }

        // Fallback additive lean envelope over the sampled animation. This is
        // procedural motion on the real skeleton — never presented as an
        // authored work clip.
        private void ApplyWorkPose(float t, float dt)
        {
            if (!bonesReady) return;
            const float reachIn = .22f, workEnd = .82f;
            float reach;
            if (t < reachIn) reach = Mathf.SmoothStep(0f, 1f, t / reachIn);
            else if (t > workEnd) reach = Mathf.SmoothStep(1f, 0f, (t - workEnd) / (1f - workEnd));
            else reach = 1f;

            float workT = Mathf.InverseLerp(reachIn, workEnd, t);
            float wipe = Mathf.Sin(workT / .55f * Mathf.PI * 2f) * 10f;
            bool flipping = workT > .55f && workT < .8f;
            float flip = flipping ? Mathf.Sin((workT - .55f) / .25f * Mathf.PI) * 16f : 0f;

            if (spineBone != null)
                spineBone.localRotation = spineRest * Quaternion.Euler(14f * reach, wipe * reach, 0f);
            if (headBone != null)
                headBone.localRotation = headRest * Quaternion.Euler(8f * reach, wipe * .5f * reach, 0f);
            if (armR != null)
                armR.localRotation = armRRest * Quaternion.Euler((38f + flip) * reach, wipe * reach, 0f);
            if (armL != null)
                armL.localRotation = armLRest * Quaternion.Euler(12f * reach, -wipe * .6f * reach, 0f);
        }

        private void ClearBonePose()
        {
            if (spineBone != null) spineBone.localRotation = spineRest;
            if (headBone != null) headBone.localRotation = headRest;
            if (armR != null) armR.localRotation = armRRest;
            if (armL != null) armL.localRotation = armLRest;
        }

        private void SamplePose(float idleT, float walkT)
        {
            if (idleState != null) { idleState.time = idleT; }
            if (walkState != null) { walkState.time = walkT; }
            legacyAnimation?.Sample();
        }

        // Menu-only toon pass, unchanged from the previous menu stage: every
        // renderer gets an independent material on the menu toon shader so the
        // teal apron remaps to navy in-shader. The shared GameArtRendering
        // cache is never touched; all copies live in menuMaterials.
        private void ApplyMenuToon(GameObject model)
        {
            Shader menuToon = Resources.Load<Shader>("Shaders/CreationPortraitToon");
            if (menuToon == null)
            {
                Debug.LogWarning("CREATION_UI 缺少 Shaders/CreationPortraitToon，菜单人像保留 GameArtRendering 默认材质。");
                return;
            }
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null) continue;
                    var copy = new Material(menuToon) { name = source.name + "_MenuToon", hideFlags = HideFlags.DontSave };
                    if (source.HasProperty("_MainTex"))
                    {
                        copy.mainTexture = source.GetTexture("_MainTex");
                        copy.mainTextureScale = source.GetTextureScale("_MainTex");
                        copy.mainTextureOffset = source.GetTextureOffset("_MainTex");
                    }
                    copy.color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                    if (source.HasProperty("_EmissionColor") && source.IsKeywordEnabled("_EMISSION"))
                        copy.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                    menuMaterials.Add(copy);
                    materials[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        // Only real enabled SkinnedMeshRenderers, hand-weighted vertices — the
        // honest visible body, not the bind-pose envelope. Kept for the
        // no-room fallback framing.
        private static bool ComputeBodyBounds(GameObject model, out Bounds bounds)
        {
            bounds = new Bounds(model.transform.position, Vector3.zero);
            bool found = false;
            int boneCount = 0;
            int skinCount = 0;
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                Mesh mesh = skin.sharedMesh;
                if (mesh.vertexCount <= 0 || mesh.bindposes.Length == 0 || skin.bones.Length == 0) continue;
                skinCount++;

                Vector3[] vertices = mesh.vertices;
                Matrix4x4[] bindposes = mesh.bindposes;
                var matrices = new Matrix4x4[Mathf.Min(skin.bones.Length, bindposes.Length)];
                for (int b = 0; b < matrices.Length; b++)
                {
                    matrices[b] = skin.bones[b].localToWorldMatrix * bindposes[b];
                    boneCount++;
                }

                NativeArray<byte> counts = mesh.GetBonesPerVertex();
                NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
                try
                {
                    if (counts.Length != vertices.Length)
                    {
                        Bounds fallback = skin.bounds;
                        if (!found) { bounds = fallback; found = true; }
                        else bounds.Encapsulate(fallback);
                        continue;
                    }
                    int cursor = 0;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        Vector3 p = Vector3.zero;
                        int influenceCount = counts[v];
                        for (int w = 0; w < influenceCount; w++)
                        {
                            BoneWeight1 weight = weights[cursor++];
                            if (weight.boneIndex < matrices.Length)
                                p += matrices[weight.boneIndex].MultiplyPoint3x4(vertices[v]) * weight.weight;
                        }
                        if (influenceCount == 0) p = skin.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[v]);
                        if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                        else bounds.Encapsulate(p);
                    }
                }
                finally
                {
                    if (counts.IsCreated) counts.Dispose();
                    if (weights.IsCreated) weights.Dispose();
                }
            }
            return found;
        }

        private void OnDestroy()
        {
            if (vendorWork != null)
            {
                try { vendorWork.Failed -= OnVendorFailed; vendorWork.ContactReached -= OnVendorContact; vendorWork.Cancel(); } catch { }
            }
            if (view != null) view.texture = null;
            if (portraitCamera != null) portraitCamera.targetTexture = null;
            if (target != null) { target.Release(); Destroy(target); }
            foreach (Material material in menuMaterials) if (material != null) Destroy(material);
            if (displayMaterial != null) Destroy(displayMaterial);
            if (stageRoot != null) Destroy(stageRoot);
        }
    }
}
