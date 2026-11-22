using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Guochao
{
    public sealed class WorldRuntime : MonoBehaviour
    {
        public const int EnvironmentLayer = 24;
        public const int EnvironmentMask = 1 << EnvironmentLayer;
        public PlayerMotor Player { get; private set; }
        public Camera View { get; private set; }
        public LinkedTrolleyMotor ShoppingMotor { get; private set; }
        public CustomerCrowd Crowd { get; private set; }
        public Vector3 StoveVisualPosition => GameFlow.Instance?.Run.Profile!=null&&homeGear.TryGetValue(GameFlow.Instance.Run.HeldEquipmentId,out var held)?held.transform.position:stove ? stove.transform.position : Vector3.zero;
        public readonly Dictionary<string, Transform> Anchors = new Dictionary<string, Transform>();
        public readonly List<Collider> SolidColliders = new List<Collider>();
        private GameObject environment, cart, stove, trolley;
        private GameObject recoveryTable;
        private GameObject[] shelfSamples = new GameObject[GameCatalog.Items.Length];
        private readonly Dictionary<string,GameObject> focusedStockLabels = new Dictionary<string,GameObject>();
        private readonly Dictionary<string,GameObject> homeGear = new Dictionary<string,GameObject>();
        private readonly Dictionary<string,string> homeGearState = new Dictionary<string,string>();
        // FREEDOM19: groceries is keyed by physical-unit instance key (see
        // ProcurementInstanceKeys), NOT by SKU — repeated consumables keep one
        // real model per unit. grocerySkus recovers the catalog SKU per key.
        private readonly Dictionary<string, GameObject> groceries = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, string> grocerySkus = new Dictionary<string, string>();
        private readonly Dictionary<string, Vector3> groceryNaturalScales = new Dictionary<string, Vector3>();
        private string stoveVisualState;
        private LineRenderer targetRing;
        private GamePhase phase;
        private Vector3 cameraOffset, cameraCenter;
        private Vector3 cameraLookAhead;
        private float cameraSize;
        private string visualKey;
        private float floorY;
        private VehicleLoadout loadout;
        public string ValidateLoad(IReadOnlyList<string> equipment,IReadOnlyList<string> cargo,IEnumerable<string> packages)=>loadout?.Validate(equipment,cargo,packages)??"";

        // Static collider attribution per model instance. Evaluating a candidate
        // must not re-run OverlapBox + component walks for every query.
        private readonly Dictionary<GameObject, Collider[]> modelColliderCache = new Dictionary<GameObject, Collider[]>();

        public void Build(GamePhase newPhase)
        {
            phase = newPhase;
            modelColliderCache.Clear();
            environment = RequiredModel(phase == GamePhase.Home ? "HomeWorld" : "MarketWorld", transform);
            foreach (Transform t in environment.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                if (name.StartsWith("Anchor_", StringComparison.Ordinal)) name = name.Substring(7);
                if (!Anchors.ContainsKey(name)) Anchors.Add(name, t);
            }
            string[] required = phase == GamePhase.Home
                ? new[] { "Spawn", "CameraTarget", "StovePickup", "HomeCart", "HomeExit" }
                : new[] { "Spawn", "CameraTarget", "RiceShelf", "MeatShelf", "EggShelf", "Cashier", "MarketCart", "MarketExit" };
            foreach (string name in required)
                if (!Anchors.ContainsKey(name)) throw new InvalidOperationException($"{phase} 场景缺少锚点：{name}");
            CreateCollision();
            SceneDetailAssets.Install(this,newPhase);
            // Scene decoration is not owned inventory. Profiles spawn their own gas tank.
            if(newPhase==GamePhase.Home&&GameFlow.Instance?.Run.Profile!=null)
                foreach(Transform node in environment.GetComponentsInChildren<Transform>(true))
                    if(node.name=="E03_Gas"||node.name.StartsWith("E03_Gas.")||node.name=="Collider_Gas")node.gameObject.SetActive(false);
            SetupCameraAndLight();
            Vector3 spawn = Anchor("Spawn");
            floorY = spawn.y;
            Physics.SyncTransforms();
            if (Physics.Raycast(spawn + Vector3.up * 3, Vector3.down, out RaycastHit hit, 10, EnvironmentMask)) floorY = hit.point.y;
            GameObject playerRoot = new GameObject("Playable_Aholo");
            playerRoot.transform.SetParent(transform); playerRoot.transform.position = new Vector3(spawn.x, floorY + .03f, spawn.z);
            GameObject playerVisual = AuthoredModel("Player", playerRoot.transform);
            Player = playerRoot.AddComponent<PlayerMotor>();
            Player.Initialize(playerVisual.transform, View);

            string cartAnchor = phase == GamePhase.Home ? "HomeCartPlacement" : "MarketCartPlacement";
            cart = AuthoredModel("CartAI", transform);
            if (Anchors.TryGetValue(cartAnchor, out Transform cartTransform))
            { cart.transform.position = cartTransform.position; cart.transform.rotation = RelativeAnchorRotation(cartTransform); }
            else throw new InvalidOperationException($"{phase} 场景缺少车辆落位锚点：{cartAnchor}");
            CreateCollision(cart, false);
            if(GameFlow.Instance?.Run.Profile!=null)
            {
                loadout=gameObject.AddComponent<VehicleLoadout>();
                loadout.Initialize(cart.transform,CreateLoadoutVisual);
            }
            stove = AuthoredModel("Stove", transform);
            if (phase == GamePhase.Market)
            {
                trolley = AuthoredModel("ShoppingCart", transform);
                Transform forwardMarker = FindChild(trolley.transform, "ShoppingCartForward");
                Transform grip = FindChild(trolley.transform, "HandleGrip");
                ShoppingMotor = gameObject.AddComponent<LinkedTrolleyMotor>();
                Physics.SyncTransforms();
                ShoppingMotor.Initialize(Player, trolley.transform, forwardMarker, grip);
                Anchors["ShoppingCart"] = grip;
                for (int i = 0; i < 3; i++)
                {
                    string stockName = "Stock_" + GameCatalog.Items[i].id + "_01";
                    if (Anchors.TryGetValue(stockName, out Transform stock)) shelfSamples[i] = stock.gameObject;
                    else throw new InvalidOperationException("货架缺少真实可取样品：" + stockName);
                    if(i>0)
                    {
                        // Put the selectable cold-storage box at the front of its
                        // display, within arm reach of the authored aisle marker.
                        Vector3 front=Anchor(GameCatalog.Items[i].anchor)-RenderBounds(shelfSamples[i]).center;
                        front.y=0;stock.position+=front.normalized*.35f;
                        // EggShelf measurement (measure-r2): the sample is authored
                        // on top of the chest freezer but 0.40m behind the Collider_01
                        // front lip (z=-17.30) and floats 0.16m above the cabinet top,
                        // leaving exactly two reachable standing spots. MeatShelf shows
                        // the same authored offset (base 0.32m inside the Collider_01
                        // top, front face 0.6m+ behind the lip). Rest both on the
                        // support top and set the front face flush to the lip minus a
                        // small inset, so the real reachable face is the visible tray
                        // front. Placement fix only: the 1.15m contact limit and the
                        // full CanPickUpAt envelope check are unchanged.
                        if(GameCatalog.Items[i].anchor=="EggShelf"||GameCatalog.Items[i].anchor=="MeatShelf") SnapStockToSupportFront(stock.gameObject, front.normalized);
                    }
                }
                // Rice sample (index 0) is authored at RicePlacement on top of the
                // Collider_04 gondola but floats 0.10m above the board and sits with
                // its front face 0.6m behind the west-end lip the RiceShelf aisle
                // marker faces. Rest it on the real board at that lip; the aisle
                // marker itself is a good standing spot, so no anchor move is needed.
                if(shelfSamples[0]!=null)
                {
                    Vector3 front=Anchor(GameCatalog.Items[0].anchor)-RenderBounds(shelfSamples[0]).center;
                    front.y=0;
                    if(front.sqrMagnitude>.001f)SnapStockToSupportFront(shelfSamples[0], front.normalized);
                }
            }
            if(phase==GamePhase.Market)BuildExtraStock();
            CreateTargetRing();
            gameObject.AddComponent<GameFeelAudio>();
            if (phase == GamePhase.Market)
            {
                BuildMarketLighting();
                Crowd=gameObject.AddComponent<CustomerCrowd>();Crowd.Initialize(this);
                // Recovery visibility pass: real authored stall corner + the two
                // character resources, then the collapsible market minimap.
                // Trade itself is untouched: E at SupplyCounter still opens the
                // SupplyShop "旧货代收" tab.
                MarketRecoveryScene recoveryScene=MarketRecoveryScene.Install(this);
                gameObject.AddComponent<MarketRecoveryMap>().Initialize(this,recoveryScene);
            }
            GameArtRendering.Attach(View,transform);
            Debug.Log($"WORLD_READY {phase} colliders={SolidColliders.Count} anchors={Anchors.Count} spawn={Player.transform.position}");
        }

        public Vector3 Anchor(string id) => Anchors[id].position;
        public bool HasAnchor(string id) => Anchors.ContainsKey(id);

        // RECOVERY12: the recovery corner's own trade gate. MarketRecoveryScene
        // measures the authored "18_回收台" face toward CameraTarget, offsets a
        // real standing-side anchor 0.85 m off that face and registers it here
        // together with the table model, so discovery, contact rays and the
        // highlight ring resolve to the physical recovery table, not to a bare
        // marker. The procurement SupplyCounter anchor is untouched.
        public void RegisterRecoveryCounter(Transform anchor, GameObject table)
        {
            if (anchor == null) return;
            Anchors["RecoveryCounter"] = anchor;
            recoveryTable = table;
        }
        public bool TryInteractionContact(string target, out Vector3 contact)
            =>TryInteractionContactAt(target,Player!=null?Player.transform.position:Vector3.zero,out contact);

        // Read-only prediction uses the candidate body position. Runtime callers
        // retain the wrapper above and therefore the exact current-player contact.
        public bool TryInteractionContactAt(string target,Vector3 bodyPosition,out Vector3 contact)
        {
            contact = Vector3.zero;
            if (target == "StovePickup" && stove != null && GameFlow.Instance?.Run.HoldingStove==true)
            {
                var state=GameFlow.Instance.Run;int i=state.HomeEquipment.IndexOf(state.HeldEquipmentId);
                string rest=HasAnchor("EquipmentSlot"+i)?"EquipmentSlot"+i:HasAnchor("StovePlacement")?"StovePlacement":"StovePickup";
                contact=Anchor(rest)+Vector3.up*.25f;return true;
            }
            // The reachable face of the physical model is the contact, shared
            // with the candidate verdict so discovery, action and facing agree.
            if (TryResolveSurface(target, bodyPosition, out Vector3 surface))
            { contact = surface; return true; }
            // Authored fallbacks for models without physics proxies.
            if (target == "StovePickup" && stove != null)
            {
                // Fallback for models without a physics proxy: the nearest
                // reachable face of the CURRENT visible bounds to the probing
                // body — never a point high above the centre, which shrinks
                // small objects to one exact alignment circle.
                Vector3 probe = bodyPosition + Vector3.up * .85f;
                if(GameFlow.Instance?.Run.Profile!=null&&homeGear.TryGetValue(GameFlow.Instance.Run.ActiveHomeEquipment,out var gear))contact=RenderBounds(gear).ClosestPoint(probe);
                else contact = RenderBounds(stove).ClosestPoint(probe);
                return true;
            }
            if ((target == "HomeCart" || target == "MarketCart") && cart != null)
            {
                contact = RenderBounds(cart).ClosestPoint(bodyPosition+Vector3.up*.85f); return true;
            }
            if (target == "ShoppingCart" && trolley != null)
            {
                if (ShoppingMotor != null)
                    contact = ShoppingMotor.BasketContactPointAt(bodyPosition);
                else contact = RenderBounds(trolley).center + Vector3.up*.25f;
                return true;
            }
            for (int i=0;i<GameCatalog.Items.Length;i++)
                if (GameCatalog.Items[i].anchor==target && i<shelfSamples.Length && shelfSamples[i]!=null)
                {
                    // The stock sample can sit ahead of its original authored
                    // Collider_ proxy (Build nudges it toward the aisle), so
                    // the nearest face of the CURRENT visible bounds is the
                    // honest contact fallback — not a point above the centre
                    // that a standing body can never actually touch.
                    contact = RenderBounds(shelfSamples[i]).ClosestPoint(bodyPosition + Vector3.up * .85f); return true;
                }
            if (!HasAnchor(target)) return false;
            contact=Anchor(target)+Vector3.up*.8f; return true;
        }

        // Single contact resolution shared by discovery (IsNearAt), the
        // candidate verdict (EvaluateInteraction) and the runtime interaction
        // path (GameFlow.TryContact). Returns the nearest point on the target
        // model's own colliders to the probing body, or false when the model
        // has no physics proxy.
        public bool TryResolveContact(string target, Vector3 bodyPosition, out Vector3 contact)
        {
            if (TryResolveSurface(target, bodyPosition, out contact)) return true;
            return TryInteractionContactAt(target, bodyPosition, out contact);
        }

        private bool TryResolveSurface(string target, Vector3 bodyPosition, out Vector3 surface)
        {
            surface = Vector3.zero;
            Collider[] own = TargetColliders(target);
            if (own.Length == 0) return false;
            Vector3 probe = bodyPosition + Vector3.up * .85f;
            float bestSq = float.MaxValue; Vector3 best = Vector3.zero;
            foreach (Collider c in own)
            {
                if (c == null || !c.enabled) continue;
                Vector3 p = c.ClosestPoint(probe);
                float d = (p - probe).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = p; }
            }
            if (bestSq == float.MaxValue) return false;
            surface = best; return true;
        }
        public bool IsNear(string target, float range = 1.55f)
            => Player != null && IsNearAt(target, Player.transform.position, range);

        // Position-parameterised sight/radius gate so read-only candidate
        // evaluation can be sampled at any body position without moving the
        // player; production IsNear keeps the exact current-player semantics.
        public bool IsNearAt(string target, Vector3 bodyPosition, float range = 1.55f)
        {
            if (string.IsNullOrEmpty(target) || !Anchors.TryGetValue(target, out Transform anchor)) return false;
            Vector3 a = bodyPosition; Vector3 b = anchor.position;
            a.y = 0; b.y = 0;
            if ((a - b).sqrMagnitude > range * range) return false;
            // Physical item models share the actual surface contact as their
            // sight target, so the shelf's own face never blocks the reach to
            // itself. Doors, cashier and exits resolve to the authored anchor
            // and keep the original anchor ray untouched.
            Vector3 sight = anchor.position + Vector3.up * .8f;
            if (TryResolveContact(target, bodyPosition, out Vector3 sightContact)) sight = sightContact;
            return !HasObstruction(bodyPosition + Vector3.up * .8f, sight, TargetColliders(target));
        }

        // Physical model backing an interaction target. The anchor is only a
        // standing marker; the renderers and Collider_ proxies of this model
        // define the face the player actually reaches.
        private GameObject TargetModel(string target)
        {
            if (target == "StovePickup" && stove != null)
            {
                if (GameFlow.Instance?.Run.Profile != null &&
                    homeGear.TryGetValue(GameFlow.Instance?.Run.ActiveHomeEquipment ?? "", out var gear)) return gear;
                return stove;
            }
            if ((target == "HomeCart" || target == "MarketCart") && cart != null) return cart;
            if (target == "RecoveryCounter" && recoveryTable != null) return recoveryTable;
            int index = Array.FindIndex(GameCatalog.Items, item => item.anchor == target);
            if (index >= 0 && index < shelfSamples.Length) return shelfSamples[index];
            return null;
        }

        // Environment colliders that belong to the target model itself. The
        // contact ray must ignore these — a reach is obstructed by what stands
        // between player and shelf, never by the shelf face being touched.
        // Collider_ proxies in the authored worlds are not always children of
        // the model root, so attribution also uses geometric containment.
        // Attribution is cached per model instance for the world lifetime;
        // queries never mutate the world.
        public Collider[] TargetOwnColliders(string target)
        {
            GameObject model = TargetModel(target);
            if (model == null) return Array.Empty<Collider>();
            if (modelColliderCache.TryGetValue(model, out Collider[] cached)) return cached;
            Bounds bounds = RenderBounds(model);
            var found = new List<Collider>();
            if (bounds.size.sqrMagnitude >= .0001f)
            {
                Bounds search = bounds; search.Expand(.30f);
                foreach (Collider hit in Physics.OverlapBox(search.center, search.extents,
                    Quaternion.identity, EnvironmentMask, QueryTriggerInteraction.Ignore))
                    if (hit != null && hit.enabled &&
                        (hit.transform.IsChildOf(model.transform) || ColliderBelongsToModel(hit, bounds)))
                        found.Add(hit);
            }
            modelColliderCache[model] = found.ToArray();
            return modelColliderCache[model];
        }

        private Collider[] TargetColliders(string target) => TargetOwnColliders(target);

        // A proxy belongs to the model when one bounds contains the other.
        // Guard against long structural colliders (walls, floor strips) that
        // may geometrically contain a small model — those must keep blocking.
        private static bool ColliderBelongsToModel(Collider c, Bounds modelBounds)
        {
            Bounds cb = c.bounds; Bounds mb = modelBounds;
            bool contains = cb.Contains(mb.min) && cb.Contains(mb.max);
            bool contained = mb.Contains(cb.min) && mb.Contains(cb.max);
            if (!contains && !contained) return false;
            if (cb.size.x > mb.size.x * 4f + 1f || cb.size.z > mb.size.z * 4f + 1f) return false;
            return true;
        }

        private static bool HasObstruction(Vector3 from, Vector3 to, ICollection<Collider> ignore)
        {
            Vector3 ray = to - from;
            float dist = ray.magnitude;
            if (dist < .001f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(from, ray.normalized, dist, EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                if (ignore != null && ignore.Contains(hit.collider)) continue;
                if (hit.distance > dist - .18f) continue;
                return true;
            }
            return false;
        }

        // Single read-only candidate verdict shared by highlight, primary
        // validation and external queries. Commit still revalidates changing
        // state and facing at actual contact. All body-dependent checks use
        // the supplied position against the current world and basket poses.
        public InteractionCandidate EvaluateInteraction(string target, Vector3 bodyPosition)
        {
            var result = new InteractionCandidate { target = target };
            RunState state = GameFlow.Instance?.Run;
            if (string.IsNullOrEmpty(target) || !Anchors.TryGetValue(target, out Transform anchorTransform))
            { result.tier = InteractionCandidate.TierUnreachable; result.blockedBy = "no-anchor"; return result; }
            result.hasAnchor = true;
            result.anchor = anchorTransform.position;
            Vector3 flat = bodyPosition - anchorTransform.position; flat.y = 0;
            result.distance = flat.magnitude;
            result.standDistance = result.distance;
            if (target == "ShoppingCart")
            {
                if (ShoppingMotor == null) { result.tier = InteractionCandidate.TierUnreachable; result.blockedBy = "no-trolley"; return result; }
                // Stowing a box reaches the basket, not the handle. A parked
                // cart remains usable from any unobstructed side in reach.
                result.contact = ShoppingMotor.BasketContactPointAt(bodyPosition);
                Vector3 toBasket = result.contact-bodyPosition;toBasket.y=0;
                result.distance = result.contactDistance = toBasket.magnitude;
                result.near = result.inRange = ShoppingMotor.IsNearBasketAt(bodyPosition);
                if (!result.inRange)
                { result.tier = InteractionCandidate.TierUnreachable; result.blockedBy = "basket-away"; return result; }
            }
            else
            {
                result.near = IsNearAt(target, bodyPosition);
                if (!result.near) { result.tier = InteractionCandidate.TierUnreachable; result.blockedBy = "out-of-range"; return result; }
                // Doors and counters retain their 1.55 m anchor gate. Physical
                // pickup/put-down targets are gated by the reachable model
                // surface: the player stands at a natural spot in front of the
                // object and reaches its near face, instead of hunting a single
                // .95 m anchor point that can sit half a shelf deep.
                bool itemTarget = target == "StovePickup" || target == "HomeCart" ||
                    target == "MarketCart" || Array.Exists(GameCatalog.Items, item => item.anchor == target);
                if (!itemTarget) result.inRange = true;
                else
                {
                    result.inRange = true;
                    Collider[] own = TargetColliders(target);
                    Vector3 surface;
                    if (!TryResolveSurface(target, bodyPosition, out surface) &&
                        !TryInteractionContactAt(target, bodyPosition, out surface))
                    { result.inRange = false; result.blockedBy = "no-contact"; }
                    else
                    {
                        result.contact = surface;
                        Vector3 toContact = surface - bodyPosition; toContact.y = 0;
                        result.contactDistance = toContact.magnitude;
                        // Arm reach measured to the touched face, so a normal
                        // standing spot at the shelf edge stays legal while a
                        // distant or through-shelf reach stays illegal.
                        if (toContact.sqrMagnitude > 1.15f * 1.15f)
                        { result.inRange = false; result.blockedBy = "contact-far"; }
                        else
                        {
                            // The reachable face must face the player: the
                            // outward direction from the model centre to the
                            // touched face has to point back at the body.
                            // Contacts at the model centre (parked-cart rim on
                            // the player's own side, put-back slots) skip this
                            // orientation gate.
                            GameObject model = TargetModel(target);
                            Vector3 outward = model != null ? surface - RenderBounds(model).center : Vector3.zero;
                            outward.y = 0;
                            if (outward.sqrMagnitude > .15f * .15f &&
                                Vector3.Dot(bodyPosition - surface, outward.normalized) < .05f)
                            { result.inRange = false; result.blockedBy = "wrong-side"; }
                            else if (HasObstruction(bodyPosition + Vector3.up * 1.55f, surface, own))
                            { result.inRange = false; result.blockedBy = "blocked-ray"; }
                            else if (!CanPickUpAt(target, bodyPosition)) { result.inRange = false; result.blockedBy = "envelope"; }
                        }
                    }
                }
                if (result.inRange && (target == "Cashier" || target == "MarketCart") && state != null && state.Cart.Count > 0 &&
                    (ShoppingMotor == null || !ShoppingMotor.IsNearBasketAt(bodyPosition,1.65f)))
                { result.inRange = false; result.blockedBy = "basket-away"; }
            }
            if (!result.inRange)
            {
                if (string.IsNullOrEmpty(result.blockedBy)) result.blockedBy = "not-in-range";
                result.tier = InteractionCandidate.TierGuidance;
                return result;
            }
            result.primary = state != null && !state.Paused && !state.CheckoutInProgress &&
                state.CanPrimary(target, true, ShoppingMotor != null && ShoppingMotor.IsAttached);
            if (!result.primary) result.blockedBy = "state";
            else if ((target == "SupplyCounter" || target == "RecoveryCounter") &&
                (ShoppingMotor == null || !ShoppingMotor.IsNearBasketAt(bodyPosition,1.8f)))
            { result.primary = false; result.blockedBy = "basket-away"; }
            result.tier = result.primary ? InteractionCandidate.TierActionable : InteractionCandidate.TierInRangeButBlocked;
            return result;
        }

        // Read-only lift preflight, also queried by the diagnostic route planner.
        // A successful interaction must not create a newly embedded held object.
        public bool CanPickUpAt(string target,Vector3 bodyPosition)
        {
            RunState state=GameFlow.Instance?.Run;
            if(state==null||Player==null||state.HoldingStove||!string.IsNullOrEmpty(state.HeldGrocery))return true;
            GameObject model=null;Vector3 grip=Vector3.zero;
            if(target=="StovePickup"&&state.Phase==GamePhase.Home)
            {
                if(state.Profile!=null){homeGear.TryGetValue(state.ActiveHomeEquipment,out model);grip=new Vector3(0,.60f,.48f);}
                else{model=stove;grip=new Vector3(0,.72f,.53f);}
            }
            else if(state.Phase==GamePhase.Market)
            {
                if(ShoppingMotor!=null&&ShoppingMotor.IsAttached)return true;
                int index=Array.FindIndex(GameCatalog.Items,x=>x.anchor==target);
                // FREEDOM19: owning basket/cargo units of this SKU no longer
                // skips the held-envelope prediction — a repeat purchase must
                // still prove its carried box fits the approach contact.
                if(index<0)return true;
                model=shelfSamples[index];grip=new Vector3(0,.85f,.50f);
            }
            // Candidate-position contact: read-only prediction keeps using the
            // sampled body position, never the live player transform.
            if(model==null||!TryInteractionContactAt(target,bodyPosition,out Vector3 contact))return true;
            Vector3 direction=contact-bodyPosition;direction.y=0;
            Quaternion facing=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction):Player.visual.rotation;
            if(!CarryCollisionHull.TryPredictHeldEnvelope(model,grip,Player.visual,out Vector3 envelopeCenter,out Vector3 envelopeHalf))return false;
            Vector3 center=bodyPosition+(Player.visual.position-Player.transform.position)+facing*envelopeCenter;
            Collider[] own = TargetColliders(target);
            foreach(Collider hit in Physics.OverlapBox(center,envelopeHalf+Vector3.one*.012f,facing,EnvironmentMask,QueryTriggerInteraction.Ignore))
                if(hit.enabled&&!hit.transform.IsChildOf(Player.transform)&&Array.IndexOf(own,hit)<0)return false;
            return true;
        }

        // Read-only nearest selection for highlight and planners. Ranking is
        // actionable-first (tier 0/1 beats guidance tier 2), then distance, so
        // a close but currently inoperable target never hides an operable one.
        public string NearestAt(IEnumerable<string> ids, Vector3 bodyPosition)
        {
            string nearest = ""; int nearestTier = int.MaxValue; float best = float.MaxValue;
            foreach (string id in ids)
            {
                InteractionCandidate candidate = EvaluateInteraction(id, bodyPosition);
                if (!candidate.hasAnchor || candidate.tier > InteractionCandidate.TierGuidance) continue;
                if (candidate.tier < nearestTier || candidate.tier == nearestTier && candidate.distance < best)
                { nearest = id; nearestTier = candidate.tier; best = candidate.distance; }
            }
            return nearest;
        }

        public void SetFocus(string id)
        {
            // Product names belong to the current interaction, not a permanent
            // wall of floating text across every aisle. Physical shop signage stays.
            foreach(var label in focusedStockLabels)
                if(label.Value!=null)label.Value.SetActive(label.Key==id);
            if (targetRing == null) return;
            targetRing.enabled = !string.IsNullOrEmpty(id) && Anchors.ContainsKey(id);
            if (!targetRing.enabled) return;
            // The marker hugs the reachable contact face, not a bare floor
            // anchor that can sit inside the shelf or half a metre away.
            Vector3 spot = Anchor(id);
            if (Player != null)
            {
                InteractionCandidate candidate = EvaluateInteraction(id, Player.transform.position);
                if (candidate.hasAnchor && candidate.contact.sqrMagnitude > .0001f) spot = candidate.contact;
            }
            targetRing.transform.position = new Vector3(spot.x, floorY + .04f, spot.z);
        }

        public void Sync(RunState state)
        {
            if (Player == null) return;
            Player.inputEnabled = !state.Paused && (state.Phase == GamePhase.Home || state.Phase == GamePhase.Market);
            Player.carrying = state.HoldingStove || !string.IsNullOrEmpty(state.HeldGrocery);
            if(!Player.carrying)Player.SetCarriedVisual(null);
            Player.pushing = phase == GamePhase.Market && ShoppingMotor!=null && ShoppingMotor.IsAttached;
            if (stove == null) return;
            if(state.Profile!=null)
            {
                loadout?.Validate(state.LoadedEquipment,state.Cargo,state.PackingSupplies);
                SyncProfileEquipment(state);loadout?.SyncMounted(state);
            }
            else
            {
            string stoveState = state.HoldingStove ? "held" : state.StoveLoaded ? "loaded" : "home";
            bool transferStove = stoveVisualState != null && stoveVisualState != stoveState;
            Vector3 stoveFrom = stove.transform.position; Quaternion stoveFromRotation = stove.transform.rotation;
            stove.SetActive(state.HoldingStove || state.StoveLoaded || state.Phase == GamePhase.Home);
            if (state.HoldingStove)
            {
                stove.transform.SetParent(Player.visual, false);
                stove.transform.localPosition = new Vector3(0, .72f, .53f);
                stove.transform.localRotation = Quaternion.identity;
                Player.SetCarriedVisual(stove.transform);
            }
            else if (state.StoveLoaded)
            {
                stove.transform.SetParent(transform, true);
                PlaceLoaded(stove, "StoveLoadedPlacement", Vector3.zero);
            }
            else if (phase == GamePhase.Home)
            {
                stove.transform.SetParent(transform, true);
                stove.transform.position = HasAnchor("StovePlacement") ? Anchor("StovePlacement") : Anchor("StovePickup");
                stove.transform.rotation = HasAnchor("StovePlacement") ? RelativeAnchorRotation(Anchors["StovePlacement"]) : Quaternion.identity;
            }

            if (transferStove) ItemTransferMotion.Begin(stove, stoveFrom, stoveFromRotation);
            stoveVisualState = stoveState;

            }
            string key = string.Join(",", state.Cart) + "/" + string.Join(",", state.Cargo)+"/"+state.HeldGrocery;
            if (key == visualKey) return;
            visualKey = key;
            if (phase != GamePhase.Market) return;
            // Desired physical units: held unit first, then every basket slot,
            // then every cargo slot — each keyed by its stable instance key.
            var desired = new Dictionary<string,string>();
            if(!string.IsNullOrEmpty(state.HeldGrocery))
                desired[ProcurementInstanceKeys.Key(state.HeldGrocery,ProcurementInstanceKeys.HeldOrdinal(state.Cargo,state.Cart,state.HeldGrocery))]=state.HeldGrocery;
            for(int i=0;i<state.Cart.Count;i++)
                desired[ProcurementInstanceKeys.Key(state.Cart[i],ProcurementInstanceKeys.OrdinalOf(state.Cargo,state.Cart,state.Cart[i],i))]=state.Cart[i];
            for(int i=0;i<state.Cargo.Count;i++)
                desired[ProcurementInstanceKeys.Key(state.Cargo[i],ProcurementInstanceKeys.CargoOrdinal(state.Cargo,i))]=state.Cargo[i];
            var remove = new List<string>();
            foreach (var pair in groceries)
            {
                if (desired.ContainsKey(pair.Key)) continue;
                GameObject item = pair.Value;
                string sku;grocerySkus.TryGetValue(pair.Key,out sku);
                int index = Array.FindIndex(GameCatalog.Items, definition => definition.id == sku);
                GameObject stock = index >= 0 ? shelfSamples[index] : null;
                Vector3 from = item.transform.position; Quaternion rotation = item.transform.rotation;
                if (stock != null)
                {
                    item.transform.SetParent(transform,true);
                    if (groceryNaturalScales.TryGetValue(pair.Key, out Vector3 savedScale)) item.transform.localScale = savedScale;
                    item.transform.SetPositionAndRotation(stock.transform.position,stock.transform.rotation);
                    ItemTransferMotion.Begin(item,from,rotation,()=>{if(stock)stock.SetActive(true);if(item)Destroy(item);});
                }
                else Destroy(item);
                remove.Add(pair.Key);
            }
            foreach (string instanceKey in remove){ groceries.Remove(instanceKey); grocerySkus.Remove(instanceKey); groceryNaturalScales.Remove(instanceKey); }
            if(!string.IsNullOrEmpty(state.HeldGrocery))PlaceHeldFood(state);
            ShoppingBasketPlan basketLayout=ShoppingBasketLayout.Plan(state.Cart,state.CartCapacity);
            for (int i=0;i<state.Cart.Count;i++) PlaceFood(state,i,false,basketLayout);
            for (int i=0;i<state.Cargo.Count;i++) PlaceFood(state,i,true,null);
            for(int i=0;i<shelfSamples.Length;i++)
            {
                // FREEDOM19: market shelf stock is not depleted by purchases;
                // the sample stays (or reappears) so the next identical unit
                // remains visible and purchasable.
                if(shelfSamples[i]) shelfSamples[i].SetActive(true);
            }
        }

        private void PlaceFood(RunState state,int index,bool onVehicle,ShoppingBasketPlan basketLayout)
        {
            string sku=onVehicle?state.Cargo[index]:state.Cart[index];
            int ordinal=onVehicle?ProcurementInstanceKeys.CargoOrdinal(state.Cargo,index):ProcurementInstanceKeys.OrdinalOf(state.Cargo,state.Cart,sku,index);
            string instanceKey=ProcurementInstanceKeys.Key(sku,ordinal);
            bool exists=groceries.TryGetValue(instanceKey,out GameObject item);
            int definition=Array.FindIndex(GameCatalog.Items, entry=>entry.id==sku);
            if(!exists)
            {
                item=MakeFood(sku,transform,.36f);groceries.Add(instanceKey,item);grocerySkus[instanceKey]=sku;groceryNaturalScales[instanceKey]=item.transform.localScale;
                if(definition>=0 && shelfSamples[definition])
                    item.transform.SetPositionAndRotation(shelfSamples[definition].transform.position,shelfSamples[definition].transform.rotation);
                else item.transform.position=HasAnchor("SupplyCounter")?Anchor("SupplyCounter")+Vector3.up*.9f:Player.transform.position+Vector3.up*.9f;
            }
            if(!groceryNaturalScales.TryGetValue(instanceKey,out Vector3 naturalScale))
            { naturalScale=item.transform.localScale; groceryNaturalScales[instanceKey]=naturalScale; }
            Vector3 from=item.transform.position; Quaternion rotation=item.transform.rotation;
            if(onVehicle)
            {
                item.transform.SetParent(transform,true);
                item.transform.localScale=naturalScale;
                if(loadout!=null){item.SetActive(loadout.Apply(ProcurementInstanceKeys.LoadoutKey(state.Cargo,index),item));}
                else if(SupplyCatalog.IsEquipment(sku)&&EquipmentVisualFactory.IsHeavy(sku))
                    PlaceLoaded(item,"StoveLoadedPlacement",new Vector3(.22f,0,-.45f));
                else PlaceLoaded(item,"CargoSlot"+(index%3),Vector3.up*(index/3)*.27f);
            }
            else
            {
                item.transform.SetParent(trolley.transform,true);
                item.transform.localScale=naturalScale;
                var layout=basketLayout;
                if(layout.Success && index<layout.Placements.Count)
                {
                    var placement=layout.Placements[index];
                    float cellWidth=.8f/3f, cellDepth=1.3f/layout.Rows;
                    item.transform.rotation=trolley.transform.rotation*Quaternion.Euler(0,placement.Rotated?90f:0f,0);
                    item.transform.localPosition=Vector3.zero;
                    Bounds naturalBounds=BasketVisualBounds(item,trolley.transform);
                    float fitWidth=(placement.Width*cellWidth-.01f)/Mathf.Max(.0001f,naturalBounds.size.x);
                    float fitDepth=(placement.Height*cellDepth-.01f)/Mathf.Max(.0001f,naturalBounds.size.z);
                    float fit=Mathf.Clamp(Mathf.Min(1f,Mathf.Min(fitWidth,fitDepth)),.001f,1f);
                    item.transform.localScale=naturalScale*fit;
                    Bounds visible=BasketVisualBounds(item,trolley.transform);
                    float centerX=-.4f+(placement.X+placement.Width*.5f)*cellWidth;
                    float centerZ=-.65f+(placement.Y+placement.Height*.5f)*cellDepth;
                    item.transform.localPosition+=new Vector3(centerX-visible.center.x,.575f-visible.min.y,centerZ-visible.center.z);
                }
                else
                {
                    Transform slot=FindChild(trolley.transform,"ShoppingSlot"+(index%3));
                    if(slot!=null)item.transform.position=slot.position+Vector3.up*(index/3)*.27f;
                }
            }
            if((item.transform.position-from).sqrMagnitude>.0025f)ItemTransferMotion.Begin(item,from,rotation);
        }

        private void PlaceHeldFood(RunState state)
        {
            string sku=state.HeldGrocery;
            string instanceKey=ProcurementInstanceKeys.Key(sku,ProcurementInstanceKeys.HeldOrdinal(state.Cargo,state.Cart,sku));
            if(!groceries.TryGetValue(instanceKey,out GameObject item))
            {
                item=MakeFood(sku,transform,.36f);groceries[instanceKey]=item;grocerySkus[instanceKey]=sku;groceryNaturalScales[instanceKey]=item.transform.localScale;
                int i=Array.FindIndex(GameCatalog.Items,x=>x.id==sku);
                if(i>=0&&shelfSamples[i])item.transform.SetPositionAndRotation(shelfSamples[i].transform.position,shelfSamples[i].transform.rotation);
            }
            Vector3 from=item.transform.position;Quaternion rotation=item.transform.rotation;
            item.transform.SetParent(Player.visual,false);
            if(groceryNaturalScales.TryGetValue(instanceKey,out Vector3 naturalScale))item.transform.localScale=naturalScale;
            item.transform.localPosition=new Vector3(0,.85f,.50f);item.transform.localRotation=Quaternion.identity;
            Player.SetCarriedVisual(item.transform);
            ItemTransferMotion.Begin(item,from,rotation);
        }

        private void SyncProfileEquipment(RunState state)
        {
            stove.SetActive(false);
            for(int i=0;i<state.HomeEquipment.Count;i++)
            {
                string id=state.HomeEquipment[i];
                if(!homeGear.TryGetValue(id,out GameObject model)){model=EquipmentVisualFactory.Create(id,transform);homeGear[id]=model;}
                string mode=state.HoldingStove&&state.HeldEquipmentId==id?"held":state.LoadedEquipment.Contains(id)?"loaded":"home";
                Vector3 from=model.transform.position;Quaternion rotation=model.transform.rotation;
                model.SetActive(mode!="home"||phase==GamePhase.Home);
                if(mode=="held")
                {model.transform.SetParent(Player.visual,false);model.transform.localPosition=new Vector3(0,.60f,.48f);model.transform.localRotation=Quaternion.identity;Player.SetCarriedVisual(model.transform);}
                else if(mode=="loaded")
                {model.transform.SetParent(transform,true);model.SetActive(loadout!=null&&loadout.Apply("gear_"+id,model));}
                else if(phase==GamePhase.Home)
                {
                    Transform slot=Anchors[HasAnchor("EquipmentSlot"+i)?"EquipmentSlot"+i:"StovePlacement"];
                    model.transform.SetParent(transform,true);model.transform.SetPositionAndRotation(slot.position,RelativeAnchorRotation(slot));
                    if(state.ActiveHomeEquipment==id&&!state.HoldingStove&&HasAnchor("EquipmentPickup"+i))
                        Anchors["StovePickup"]=Anchors["EquipmentPickup"+i];
                }
                if(homeGearState.TryGetValue(id,out string previous)&&previous!=mode)ItemTransferMotion.Begin(model,from,rotation);
                homeGearState[id]=mode;
            }
        }

        // Pull a shelf-top stock sample to the front lip of the physical
        // support board beneath it (measured against the real Collider_
        // proxies) and rest its base on the board top. Used for the
        // freezer-top egg/meat boxes, the gondola-top rice box and every
        // procedurally placed extra-stock sample, so the visible goods, the
        // focused label and the reach contact all describe the same physical
        // box at the support's visible front lip.
        //
        // The support is chosen by real geometry — NEVER by "the first
        // collider under an unbounded downward ray": goods authored below a
        // tall cabinet top (or in front of it) used to hit the floor slab
        // and then treat the whole floor edge as the cabinet lip, dragging
        // boxes 20 m out of the shop. A candidate must be a plausible board
        // (not a floor slab, wall or room shell), must contain the goods
        // LATERALLY along the aisle-facing dominant axis (rules out every
        // neighbouring cabinet and the back wall), and its front lip must
        // actually face the goods within arm's distance. The front plane and
        // the lateral span use the dominant-axis projection of the real box,
        // so a diagonal aisle direction cannot inflate the lip distance the
        // way an AABB corner projection does. When no board qualifies the
        // authored position is kept. Placement fix only: contact limits,
        // envelope checks and colliders are unchanged.
        static bool SnapStockToSupportFront(GameObject stock, Vector3 frontDir)
        {
            Bounds b = RenderBounds(stock);
            Vector3 c = b.center;
            bool domX = Mathf.Abs(frontDir.x) >= Mathf.Abs(frontDir.z);
            Vector3 axis = domX ? new Vector3(Mathf.Sign(frontDir.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(frontDir.z));
            Bounds best = default; bool found = false;
            foreach (Collider hit in Physics.OverlapBox(c,
                        new Vector3(Mathf.Max(b.extents.x, .35f), 1.4f, Mathf.Max(b.extents.z, .35f)),
                        Quaternion.identity, EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                if (hit == null || !hit.enabled) continue;
                Bounds sb = hit.bounds;
                if (sb.max.y < .30f) continue;                  // floor slabs
                if (sb.size.y > 2.6f) continue;                 // walls / room shells
                if (sb.size.x * sb.size.z > 40f) continue;      // floor platforms
                if (Mathf.Abs(sb.max.y - b.min.y) > 1.2f) continue; // not this goods' level
                float lateral = domX ? c.z : c.x;
                float latMin = domX ? sb.min.z : sb.min.x;
                float latMax = domX ? sb.max.z : sb.max.x;
                if (lateral < latMin - .30f || lateral > latMax + .30f) continue; // different cabinet
                float lip = Vector3.Dot(sb.center, axis) + (domX ? sb.extents.x : sb.extents.z);
                float gap = lip - Vector3.Dot(c, axis);
                if (gap < -.50f || gap > 1.20f) continue;       // lip not facing the goods / too far
                if (!found || sb.max.y > best.max.y) { best = sb; found = true; }
            }
            if (!found) return false;
            float halfDepth = Mathf.Abs(Vector3.Dot(b.extents, axis));
            float supportFront = Vector3.Dot(best.center, axis) + (domX ? best.extents.x : best.extents.z);
            Vector3 pos = stock.transform.position;
            pos += axis * (supportFront - halfDepth - .04f - Vector3.Dot(b.center, axis));
            pos.y += best.max.y - b.min.y;
            stock.transform.position = pos;
            return true;
        }

        private void BuildExtraStock()
        {
            Vector3 x=(Anchor("MeatShelf")-Anchor("EggShelf"))/3.5f;x.y=0;
            Vector3 z=(Anchor("RiceShelf")-Anchor("MeatShelf")+x*.6f)/-3.02f;z.y=0;
            Vector3 origin=Anchor("EggShelf")-x*3-z*16.5f;
            Vector3 Point(float a,float b,float height)=>origin+x*a+z*b+Vector3.up*height;
            // Fruit stands in front of the tall Collider_03 cabinet (front lip
            // z=-18.00); its marker must sit in the real aisle band 0.85-1.0m
            // from that lip or the 1.15m arm reach to the touched face fails.
            Vector2[] stand={new Vector2(5.9f,9),new Vector2(5.9f,11.1f),new Vector2(12,16.3f),new Vector2(20,17.05f),new Vector2(5.9f,7.1f)};
            Vector2[] goods={new Vector2(5.03f,9),new Vector2(5.03f,11.1f),new Vector2(12,17.4f),new Vector2(20,17.4f),new Vector2(5.03f,7.1f)};
            for(int i=3;i<GameCatalog.Items.Length;i++)
            {
                var def=GameCatalog.Items[i];var mark=new GameObject(def.anchor).transform;mark.SetParent(transform);mark.position=Point(stand[i-3].x,stand[i-3].y,0);Anchors[def.anchor]=mark;
                var stock=MakeFood(def.id,transform,.36f);stock.name="Stock_"+def.id;stock.transform.position=Point(goods[i-3].x,goods[i-3].y,1.10f);
                // Rest the visible goods on the physical shelf board beneath
                // them and pull them flush to that board's front lip facing
                // the aisle marker, instead of leaving them at a hardcoded
                // height that can float above or sink into the cabinet. The
                // label is then hung over the REAL box position, so the sign
                // and the reachable contact belong to the goods actually seen.
                Vector3 aisle=Point(stand[i-3].x,stand[i-3].y,1.10f)-stock.transform.position;aisle.y=0;
                if(aisle.sqrMagnitude>.001f)SnapStockToSupportFront(stock,aisle.normalized);
                shelfSamples[i]=stock;
                string shelfName=def.id=="prepared"?"速冻饭 · 料理包":def.name.Replace("标准", "").Replace("预制", "");
                Bounds placed=RenderBounds(stock);
                AddStockLabel(shelfName,placed.center+Vector3.up*(placed.extents.y+.35f),def.anchor);
            }
            var counter=new GameObject("SupplyCounter").transform;counter.SetParent(transform);counter.position=Point(15.4f,5.1f,0);Anchors["SupplyCounter"]=counter;
            // One spatial name; the map and contextual action describe its services.
            AddStockLabel("采购柜台",Point(15.4f,6.3f,2.15f));
        }
        private void AddStockLabel(string words,Vector3 at,string target=null,Color? tint=null)
        {
            var go=new GameObject("ShelfLabel",typeof(TextMesh));go.transform.SetParent(transform);go.transform.position=at;
            go.transform.rotation=View.transform.rotation;var label=go.GetComponent<TextMesh>();label.text=words;label.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");label.fontSize=48;label.characterSize=.065f;label.anchor=TextAnchor.MiddleCenter;label.color=tint??new Color(.96f,.89f,.72f);
            go.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
            if(!string.IsNullOrEmpty(target)){focusedStockLabels[target]=go;go.SetActive(false);}
        }

        private void LateUpdate()
        {
            if (Player == null) return;
            if (GameFlow.Instance != null && (GameFlow.Instance.Run.Paused || GameFlow.Instance.IsTransitioning)) return;
            if (Player.inputEnabled && !Player.presentationLocked)
                cameraSize = Mathf.Clamp(cameraSize-Input.mouseScrollDelta.y*.28f,phase==GamePhase.Home?4.2f:5.5f,phase==GamePhase.Home?6.5f:9f);
            View.orthographicSize = Mathf.Lerp(View.orthographicSize,cameraSize,1-Mathf.Exp(-9*Time.deltaTime));
            // Fixed oblique projection with gentle tracking keeps a person readable in the 24 m shop.
            Vector3 focus = Player.transform.position;
            focus.y = cameraCenter.y;
            Vector3 lead = Vector3.ClampMagnitude(new Vector3(Player.Velocity.x,0,Player.Velocity.z)*.1f,.35f);
            cameraLookAhead = Vector3.Lerp(cameraLookAhead,lead,1-Mathf.Exp(-5*Time.deltaTime));
            Vector3 desiredCenter = Vector3.Lerp(cameraCenter, focus, phase == GamePhase.Home ? .45f : .72f)+cameraLookAhead;
            View.transform.position = Vector3.Lerp(View.transform.position, desiredCenter + cameraOffset, 1-Mathf.Exp(-4*Time.deltaTime));
        }

        private void PlaceLoaded(GameObject item, string anchor, Vector3 slotOffset)
        {
            Transform slot = FindChild(cart.transform, anchor);
            Transform origin = FindChild(cart.transform, "CartOrigin");
            if (slot != null && origin != null)
            {
                Quaternion rotation = slot.rotation * Quaternion.Inverse(origin.rotation) * cart.transform.rotation;
                item.transform.SetPositionAndRotation(slot.position + rotation * slotOffset, rotation);
                return;
            }
            if (Anchors.TryGetValue(anchor, out Transform point))
            { item.transform.position = point.position + RelativeAnchorRotation(point) * slotOffset; item.transform.rotation = RelativeAnchorRotation(point); return; }
            Transform deck = FindChild(cart.transform, "CartDeck");
            if (slot != null) { item.transform.position = slot.position + cart.transform.rotation * slotOffset; item.transform.rotation = cart.transform.rotation; return; }
            if (deck != null) { item.transform.position = deck.position + cart.transform.rotation * slotOffset; item.transform.rotation = cart.transform.rotation; return; }
            Bounds bounds = RenderBounds(cart);
            item.transform.position = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * .55f, bounds.center.z) + slotOffset;
            item.transform.rotation = cart.transform.rotation;
        }

        private void CreateCollision(GameObject source = null, bool required = true)
        {
            if (source == null) source = environment;
            int before = SolidColliders.Count;
            foreach (MeshFilter mesh in source.GetComponentsInChildren<MeshFilter>(true))
            {
                Transform parent = mesh.transform;
                bool proxy = false;
                while (parent != null && parent != source.transform.parent)
                {
                    if (parent.name.StartsWith("Collider_", StringComparison.Ordinal)) { proxy = true; break; }
                    parent = parent.parent;
                }
                if (!proxy || mesh.sharedMesh == null) continue;
                MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
                mesh.gameObject.layer = EnvironmentLayer;
                BoxCollider box = mesh.gameObject.AddComponent<BoxCollider>();
                box.center = mesh.sharedMesh.bounds.center; box.size = mesh.sharedMesh.bounds.size;
                SolidColliders.Add(box);
            }
            if (required && SolidColliders.Count == before) throw new InvalidOperationException($"{phase} 缺少 Collider_ 碰撞代理，无法开始可玩场景。");
        }

        private void SetupCameraAndLight()
        {
            View = Camera.main;
            if (View == null)
            {
                GameObject cameraObject = new GameObject("GameplayCamera"); cameraObject.tag = "MainCamera";
                View = cameraObject.AddComponent<Camera>();
                if (!GameFlow.PresentationEnabled) cameraObject.AddComponent<AudioListener>();
            }
            cameraCenter = Anchor("CameraTarget");
            cameraOffset = HasAnchor("CameraPosition") ? Anchor("CameraPosition") - cameraCenter : new Vector3(-13, 19, -17);
            View.transform.position = cameraCenter + cameraOffset;
            View.transform.rotation = Quaternion.LookRotation(-cameraOffset, Vector3.up);
            View.orthographic = true; cameraSize = phase == GamePhase.Home ? 5.90f : 7.15f; View.orthographicSize = cameraSize;
            View.nearClipPlane = .1f; View.farClipPlane = 250;
            View.clearFlags = CameraClearFlags.SolidColor; View.backgroundColor = new Color(.65f,.63f,.60f);
            View.allowHDR = false; View.allowMSAA = true;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.75f,.79f,.81f);
            RenderSettings.ambientEquatorColor = new Color(.59f,.63f,.64f);
            RenderSettings.ambientGroundColor = new Color(.39f,.38f,.37f);
            // These worlds are instantiated at runtime and have no baked light probes.
            // Supply the ambient probe explicitly so shadowed interiors retain their colours.
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(.26f, .30f, .32f));
            RenderSettings.ambientProbe = ambient;
            GameObject lightObject = new GameObject("MorningSun"); lightObject.transform.SetParent(transform);
            Light sun = lightObject.AddComponent<Light>(); sun.type = LightType.Directional;
            sun.color = new Color(1,.91f,.73f); sun.intensity = 1.12f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .72f; sun.shadowBias = .025f; sun.shadowNormalBias = .12f;
            lightObject.transform.rotation = Quaternion.Euler(52, -32, 0);
            GameObject fillObject = new GameObject("SoftSkyFill"); fillObject.transform.SetParent(transform);
            Light fill = fillObject.AddComponent<Light>(); fill.type = LightType.Directional;
            fill.color = new Color(.67f,.81f,1f); fill.intensity = .30f; fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(48, 145, 0);
            QualitySettings.shadowDistance = 80;
        }

        private void CreateTargetRing()
        {
            GameObject ring = new GameObject("Nearby_Target_Marker"); ring.transform.SetParent(transform);
            targetRing = ring.AddComponent<LineRenderer>(); targetRing.loop = true; targetRing.useWorldSpace = false;
            targetRing.positionCount = 40; targetRing.widthMultiplier = .04f;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            targetRing.material = new Material(shader); targetRing.startColor = targetRing.endColor = new Color(.92f,.68f,.25f,.95f);
            targetRing.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 40; i++) { float angle = i / 40f * Mathf.PI * 2; targetRing.SetPosition(i, new Vector3(Mathf.Cos(angle)*.43f,0,Mathf.Sin(angle)*.43f)); }
            targetRing.enabled = false;
        }

        private void BuildMarketLighting()
        {
            // Derive the source metre axes from imported anchors, including FBX handedness.
            Vector3 xAxis=(Anchor("MeatShelf")-Anchor("EggShelf"))/3.5f; xAxis.y=0;
            Vector3 yAxis=(Anchor("MeatShelf")-Anchor("RiceShelf")-xAxis*.6f)/3.02f; yAxis.y=0;
            Vector3 origin=Anchor("RiceShelf")-xAxis*5.9f-yAxis*13.48f;
            foreach (float x in new[]{4.2f,12f,20f})
            {
                GameObject housing=GameObject.CreatePrimitive(PrimitiveType.Cube);
                housing.name="ColdWall_LightFixture"; housing.transform.SetParent(transform,false);
                housing.transform.position=origin+xAxis*x+yAxis*19.82f+Vector3.up*2.35f;
                housing.transform.localScale=new Vector3(1.25f,.12f,.14f);
                if(Application.isPlaying)Destroy(housing.GetComponent<Collider>());else DestroyImmediate(housing.GetComponent<Collider>());
                Material fixture=new Material(Shader.Find("Standard"));fixture.color=new Color(.92f,.87f,.75f);
                fixture.EnableKeyword("_EMISSION");fixture.SetColor("_EmissionColor",new Color(.69f,.58f,.39f));
                housing.GetComponent<Renderer>().material=fixture;
                GameArtRendering.Apply(housing,false);
                GameObject lamp=new GameObject("ColdWall_PracticalLight");lamp.transform.SetParent(transform,false);
                lamp.transform.position=origin+xAxis*x+yAxis*18.2f+Vector3.up*2.8f;
                Light light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.94f,.77f);
                light.intensity=.85f;light.range=6;light.shadows=LightShadows.None;
            }
        }

        private static GameObject RequiredModel(string name, Transform parent)
        {
            GameObject prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null) throw new InvalidOperationException($"缺少真实三维资源 Models/{name}。请检查资源导入，当前无法开始。" );
            GameObject model = Instantiate(prefab, parent, false); model.name = name;
            GameArtRendering.Apply(model,name.Contains("Player")||name.Contains("Stove")||name.EndsWith("Box")||name.Contains("Customer"));
            return model;
        }

        private static GameObject AuthoredModel(string name, Transform parent)
        {
            GameObject wrapper = new GameObject(name + "_Runtime"); wrapper.transform.SetParent(parent, false);
            RequiredModel(name, wrapper.transform);
            return wrapper;
        }

        internal static GameObject CreateLoadoutVisual(string id, Transform parent)
            => id.StartsWith("B")||id.StartsWith("V")||id.StartsWith("U")||id.StartsWith("E")||id.StartsWith("P")||id.StartsWith("S")||id=="RiceBucket"?EquipmentVisualFactory.Create(id,parent):MakeFood(id,parent,.36f);

        private static GameObject MakeFood(string id, Transform parent, float size)
        {
            if(SupplyCatalog.IsEquipment(id)||id.StartsWith("P"))return EquipmentVisualFactory.Create(id,parent);
            const float smallScale = .65f;
            bool small = id.EndsWith("_small", StringComparison.Ordinal);
            id=SupplyCatalog.IngredientId(id);
            GameObject created;
            if(id!="rice"&&id!="meat"&&id!="egg")created=EquipmentVisualFactory.Ingredient(id,parent);
            else
            {
                string model = id == "rice" ? "RiceBox" : id == "meat" ? "MeatBox" : "EggBox";
                if (Resources.Load<GameObject>("Models/" + model) != null) created = AuthoredModel(model, parent);
                else
                {
                    // Only loose ingredient boxes may use explicit placeholders; worlds and Aholo never do.
                    GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = "IngredientBox_" + id;
                    box.transform.SetParent(parent, false); box.transform.localScale = new Vector3(size,size*.58f,size);
                    Destroy(box.GetComponent<Collider>());
                    Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    mat.color = GameCatalog.Find(id).color; box.GetComponent<Renderer>().material = mat;
                    created = box;
                }
            }
            // Explicit prototype balancing: loose *_small packs scale the real
            // root model to 0.65x (~27% volume) in the shared factory so player
            // visuals and VehicleLoadout renderer-bounds measurements match.
            if (small && created != null) created.transform.localScale *= smallScale;
            return created;
        }

        private static Bounds BasketVisualBounds(GameObject model, Transform frame)
        {
            Bounds result = new Bounds(); bool any = false;
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                Bounds local = renderer.localBounds;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 sign = new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1);
                    Vector3 point = frame.InverseTransformPoint(renderer.transform.TransformPoint(local.center+Vector3.Scale(local.extents,sign)));
                    if(!any){result=new Bounds(point,Vector3.zero);any=true;} else result.Encapsulate(point);
                }
            }
            return any ? result : new Bounds(frame.InverseTransformPoint(model.transform.position),Vector3.zero);
        }

        public static Bounds RenderBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(model.transform.position, Vector3.zero); bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (!hasBounds) { b = renderer.bounds; hasBounds = true; } else b.Encapsulate(renderer.bounds);
            }
            return b;
        }
        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        private Quaternion RelativeAnchorRotation(Transform target)
        {
            // Spawn is authored with identity rotation. Taking a relative rotation
            // cancels FBX's common axis transform without guessing its up/forward axes.
            return target.rotation * Quaternion.Inverse(Anchors["Spawn"].rotation);
        }
    }
}
