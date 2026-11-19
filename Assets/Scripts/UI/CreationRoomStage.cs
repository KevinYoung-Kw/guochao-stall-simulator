using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Menu-only replica of the real Home room for the creation screen.
    //
    // Design choice: instead of cloning the live WorldRuntime hierarchy (which
    // would clone PlayerMotor / GameFeelAudio / CustomerCrowd MonoBehaviours
    // and risk their Awake/Update running in the menu), we instantiate the
    // very same Models/HomeWorld prefab the real Home builds, place the same
    // Stove model at the same authored anchors, and apply the same HomeBedAI
    // fine-prop replacement SceneDetailAssets uses. Nothing here carries game
    // logic: every collider, animator, camera, audio listener and stray
    // Behaviour is disabled, the whole stage sits on the private menu layer
    // far outside the playable world, and the entire hierarchy is destroyed
    // with the menu. Inventory, cash, day cycle and save positions are never
    // touched because this stage never talks to GameFlow/RunState.
    //
    // ROOM8 addition: the room also dresses the real stove top with the
    // game's own RiceBucket equipment visual (existing EquipmentVisualFactory
    // geometry — no new model is authored here) and exposes an exact
    // work point plus a lid + lid-handle tool pair so the menu actor can
    // perform an honest two-bone-IK lid lift at the counter. Every transform
    // exposed here is a real transform in the menu room.
    public static class CreationRoomStage
    {
        public const string StageRootName = "CreationRoomWorld";

        public sealed class Room
        {
            public bool valid;
            public GameObject root;
            public Vector3 stovePosition;
            public Vector3 standPoint;      // where the actor walks up to (visual approach)
            public Vector3 workStandPoint;  // closer spot the IK contact is routed to
            public Vector3 standFacing;     // horizontal direction the actor faces while working (toward the stove)
            public Vector3 idlePoint;       // where the actor idles between errands
            public Vector3 workPoint;       // exact world point the working hand is solved to (lid handle)
            public float stoveTopY;         // measured top surface of the real stove geometry
            public Vector3 prepCenter;      // centre of the supported prep stand the lid rests on
            public Transform toolLid;       // the menu-only pot lid that follows the hand
            public Transform toolLidHandle; // its named handle grip
            public GameObject toolRoot;     // the pot body sitting on the stove (stays put)
            public HouseholdAssetPresentation.Result household; // ASSETS20 native props installed in the menu room
        }

        public static Room Build(Transform parent, int layer, List<Material> menuMaterials)
        {
            var room = new Room();
            GameObject prefab = Resources.Load<GameObject>("Models/HomeWorld");
            if (prefab == null)
            {
                Debug.LogWarning("CREATION_UI 缺少 Models/HomeWorld，菜单背景退回纯人像。");
                return room;
            }

            GameObject root = new GameObject(StageRootName);
            root.transform.SetParent(parent, false);
            room.root = root;

            GameObject model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            model.name = "HomeWorld_Menu";
            StripGameplay(model, layer);
            GameArtRendering.Apply(model, false);
            ReplaceBed(model, root.transform, layer, menuMaterials);
            // ASSETS20_HOUSE: same native basin/stool props and kettle fix as the
            // live Home, menu path: no physics, same layer, dies with the stage.
            room.household = HouseholdAssetPresentation.Install(model.transform, root.transform, layer, null);

            // Same anchor vocabulary as WorldRuntime.Build.
            var anchors = new Dictionary<string, Transform>();
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                if (name.StartsWith("Anchor_", StringComparison.Ordinal)) name = name.Substring(7);
                if (!anchors.ContainsKey(name)) anchors.Add(name, t);
            }

            // Stove at the same authored placement the live Home uses.
            GameObject stove = null;
            Bounds stoveBounds = new Bounds();
            GameObject stovePrefab = Resources.Load<GameObject>("Models/Stove");
            if (stovePrefab != null && anchors.TryGetValue("StovePlacement", out Transform slot))
            {
                stove = UnityEngine.Object.Instantiate(stovePrefab, root.transform, false);
                stove.name = "Stove_Menu";
                stove.transform.position = slot.position;
                if (anchors.TryGetValue("Spawn", out Transform spawn))
                    stove.transform.rotation = slot.rotation * Quaternion.Inverse(spawn.rotation);
                StripGameplay(stove, layer);
                GameArtRendering.Apply(stove, true);
                stoveBounds = CombinedBounds(stove.GetComponentsInChildren<Renderer>(true));
            }

            Vector3 stovePos = stove != null ? stove.transform.position
                : anchors.TryGetValue("StovePlacement", out Transform s) ? s.position
                : anchors.TryGetValue("StovePickup", out Transform p) ? p.position : Vector3.zero;

            // The counter fronts toward the room: the authored Spawn sits in
            // the open floor in front of the stove, so spawn-stove is the
            // outward direction. The actor stands on that line, facing back.
            Vector3 front = Vector3.forward;
            if (anchors.TryGetValue("Spawn", out Transform spawnAnchor))
            {
                Vector3 f = spawnAnchor.position - stovePos; f.y = 0;
                if (f.sqrMagnitude > .04f) front = f.normalized;
            }
            room.stovePosition = stovePos;
            room.standFacing = -front;
            room.standPoint = stovePos + front * .92f + Vector3.up * 0f;
            // Default IK stand (overridden by DressStoveTop once the real
            // supported grip position is known): the two-bone arm must reach
            // the lid handle without ever stretching (VendorWorkActor fails
            // the action instead of lengthening a bone), so the work stand
            // is derived from measured arm geometry, not from decor.
            room.workStandPoint = stovePos + front * .52f;
            // Idle spot: near the authored spawn when it is a sane floor
            // position, otherwise a couple of metres back from the counter.
            Vector3 idle = room.standPoint + front * 1.9f;
            if (anchors.TryGetValue("Spawn", out Transform sp))
            {
                Vector3 d = sp.position - room.standPoint; d.y = 0;
                if (d.sqrMagnitude > .2f && d.sqrMagnitude < 16f) idle = new Vector3(sp.position.x, room.standPoint.y, sp.position.z);
            }
            room.idlePoint = idle;

            // Work surface: the measured top of the real stove geometry.
            room.stoveTopY = stove != null ? stoveBounds.max.y : stovePos.y + .85f;

            // ROOM8R reach audit: with the lid resting on the pot at
            // y≈.805 the shoulder-to-grip distance exceeds the measured
            // .436m two-bone arm (shoulder 1.23 − grip .805 vertical alone
            // is .425m), so joint geometry can never touch it. The lid is
            // therefore dressed onto a real, visibly supported wooden prep
            // stand beside the stove, and the IK stand point is derived from
            // that supported grip position, not from decor.
            // ROOM9CONTACT (native-r3): the low stand (plank .96, grip 1.0,
            // stand .30m) still failed the .32 commit at .084m vs the .025m
            // tolerance. The bind evidence shows why: the palm centre sits
            // ~.037m beyond the wrist along the arm line (shoulder→palm
            // .473m vs the .436m two-bone arm) and the commit pose leaves
            // the shoulder further from the grip than the naive bind-pose
            // estimate, so the clamped solve landed .084m short. The support
            // is therefore raised to real counter height (plank 1.04, grip
            // 1.08), the grip moves to the stand edge nearest the actor, and
            // the stand point moves to the outer side of the stand facing
            // the stove (the old spot sat squeezed between stove and shelf).
            Vector3 rightward = Vector3.Cross(Vector3.up, front);
            room.prepCenter = stovePos + front * .72f + rightward * .55f;

            DressStoveTop(room, root.transform, layer, front, menuMaterials);
            room.valid = true;
            return room;
        }

        // A small but honestly supported prep stand: a plank top on four
        // legs (real geometry, nothing floats). The stand is decoration for
        // the lid only; it carries no collider and no logic.
        private static GameObject BuildPrepStand(Vector3 center, float topY, Transform parent, int layer, List<Material> menuMaterials)
        {
            var wood = new Material(Shader.Find("Standard"))
            { name = "MenuPrepStand_Wood", color = new Color(.58f, .42f, .26f), hideFlags = HideFlags.DontSave };
            wood.SetFloat("_Glossiness", .12f);
            wood.SetFloat("_Metallic", 0f);
            menuMaterials.Add(wood);

            GameObject stand = new GameObject("MenuPrepStand");
            stand.transform.SetParent(parent, false);

            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(top.GetComponent<Collider>());
            top.name = "Top";
            top.transform.SetParent(stand.transform, false);
            top.transform.position = center + Vector3.up * (topY - .02f);
            top.transform.localScale = new Vector3(.46f, .04f, .34f);

            float legHeight = topY - .04f;
            for (int i = 0; i < 4; i++)
            {
                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(leg.GetComponent<Collider>());
                leg.name = "Leg" + i;
                leg.transform.SetParent(stand.transform, false);
                leg.transform.position = center + new Vector3((i % 2 == 0 ? -.19f : .19f), legHeight * .5f, (i < 2 ? -.13f : .13f));
                leg.transform.localScale = new Vector3(.04f, legHeight, .04f);
            }

            foreach (Transform node in stand.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = layer;
            foreach (Renderer renderer in stand.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = wood;
                renderer.sharedMaterials = slots;
            }
            return stand;
        }

        // Place the game's own rice-bucket visual (insulated pot + metal lid
        // + named LidHandle, exactly what EquipmentVisualFactory already
        // builds for gameplay) on the measured stove top. The handle is
        // re-parented under the lid so the whole lid+handle assembly follows
        // the hand as one rigid piece while it is lifted. No new mesh is
        // authored; every renderer here is existing game geometry.
        //
        // ROOM8R: the lid itself is then moved onto the supported prep stand
        // (BuildPrepStand) at counter-height grip ≈1.08m, at the stand edge
        // nearest the actor's IK stand point, because the stove-top rest
        // position at y≈.805m is outside the measured arm reach and
        // VendorWorkActor fails unreachable contacts instead of stretching
        // a bone.
        private static void DressStoveTop(Room room, Transform parent, int layer, Vector3 front, List<Material> menuMaterials)
        {
            room.workPoint = new Vector3(room.prepCenter.x, room.prepCenter.y + 1f, room.prepCenter.z);
            try
            {
                GameObject pot = EquipmentVisualFactory.Create("RiceBucket", parent);
                if (pot == null) { Debug.LogWarning("CREATION_UI RiceBucket 不可用，菜单灶台无盖可掀。"); return; }
                pot.name = "MenuPot_RiceBucket";
                pot.transform.position = new Vector3(room.stovePosition.x, room.stoveTopY, room.stovePosition.z) + front * .06f;
                Transform lid = null, handle = null;
                foreach (Transform child in pot.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "Lid") lid = child;
                    if (child.name == "LidHandle") handle = child;
                }
                if (lid == null || handle == null)
                {
                    Debug.LogWarning("CREATION_UI RiceBucket 缺少 Lid/LidHandle，菜单灶台无盖可掀。");
                    if (pot != null) UnityEngine.Object.Destroy(pot);
                    return;
                }
                Vector3 handleWorld = handle.position;
                Quaternion handleRotation = handle.rotation;
                handle.SetParent(lid, true);
                handle.SetPositionAndRotation(handleWorld, handleRotation);
                foreach (Transform node in pot.GetComponentsInChildren<Transform>(true))
                    node.gameObject.layer = layer;

                // Supported prep position: counter-height wooden stand (native-r3
                // evidence). Support top 1.04m with the handle grip at 1.08m
                // cuts the vertical shoulder gap to .151m (shoulder 1.231),
                // and the grip sits at the stand edge nearest the actor so
                // the .26m horizontal stays inside the honest two-bone
                // envelope: naive shoulder→grip ≈.30m against the .436m
                // measured arm, absorbing the commit-pose deficit that put
                // the previous .378m layout .084m out of tolerance.
                const float standTopY = 1.04f, gripHeight = 1.08f;
                Vector3 gripSpot = room.prepCenter + front * .16f;
                GameObject stand = BuildPrepStand(room.prepCenter, standTopY, parent, layer, menuMaterials);
                lid.SetParent(stand.transform, true);
                // Face the lid's own handle toward the approaching hand: the
                // actor works from the +front side, so yaw the lid about
                // world up until the handle offset from the lid pivot points
                // along +front. The authored lid tilt is left untouched; the
                // handle keeps its real wrist-facing frame for the IK.
                Vector3 handleDir = Vector3.ProjectOnPlane(handle.position - lid.position, Vector3.up);
                if (handleDir.sqrMagnitude > .0001f)
                    lid.rotation = Quaternion.FromToRotation(handleDir.normalized, front) * lid.rotation;
                Vector3 correction = gripSpot - handle.position; correction.y = 0f;
                lid.position += correction;
                lid.position += Vector3.up * (gripHeight - handle.position.y);

                room.toolRoot = pot;
                room.toolLid = lid;
                room.toolLidHandle = handle;
                room.workPoint = handle.position;
                // The IK stand sits .26m from the grip on the outer side of
                // the stand, facing back toward the stove: the body clears
                // the stand edge with real space (no longer wedged between
                // the stove and the shelf as in the room7-t6 capture), feet
                // stay on the floor, and the reach keeps ≥.10m of arm margin.
                room.workStandPoint = gripSpot + front * .26f;
                room.workStandPoint.y = room.standPoint.y;
            }
            catch (Exception error)
            {
                Debug.LogWarning("CREATION_UI 灶台道具摆放失败，保留无道具演出: " + error.Message);
                room.toolRoot = null; room.toolLid = null; room.toolLidHandle = null;
            }
        }

        // The room picture keeps no physics and no logic. Only renderers and
        // lights stay on; everything else is disabled, never destroyed from
        // the prefab instance in a way that would leak into the source.
        private static void StripGameplay(GameObject model, int layer)
        {
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = layer;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Behaviour behaviour in model.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is Light) continue;
                behaviour.enabled = false;
            }
            // Hide collision-proxy renderers, mirroring WorldRuntime.CreateCollision:
            // any renderer whose ancestor chain contains a "Collider_*" node is
            // a big proxy box with its own MeshRenderer, not real furniture.
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                bool proxy = false;
                for (Transform node = renderer.transform; node != null && node != model.transform.parent; node = node.parent)
                    if (node.name.StartsWith("Collider_", StringComparison.Ordinal)) { proxy = true; break; }
                if (proxy) renderer.enabled = false;
            }
        }

        // Same fine-prop swap SceneDetailAssets performs for the real Home
        // (authored low-poly bed renderers off, generated HomeBedAI mesh on),
        // minus the collider bookkeeping the menu stage does not need. Any
        // missing prefab/texture or placement mismatch simply keeps the
        // authored bed, exactly like the gameplay path.
        private static void ReplaceBed(GameObject model, Transform parent, int layer, List<Material> menuMaterials)
        {
            string[] oldNames = { "bed frame", "bed leg", "mattress", "quilt", "folded quilt edge", "pillow", "headboard" };
            GameObject prefab = Resources.Load<GameObject>("Models/HomeBedAI");
            Texture2D albedo = Resources.Load<Texture2D>("Models/Textures/HomeBedAI_Albedo");
            if (prefab == null || albedo == null) return;

            var originals = new List<Renderer>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer)) continue;
                foreach (string name in oldNames)
                    if (renderer.name == name || renderer.name.StartsWith(name + ".", StringComparison.Ordinal))
                    { originals.Add(renderer); break; }
            }
            if (originals.Count == 0) return;

            var wrapper = new GameObject("HomeBedAI_Menu");
            wrapper.transform.SetParent(parent, false);
            GameObject bed = UnityEngine.Object.Instantiate(prefab, wrapper.transform, false);
            bed.name = "HomeBedAI_Imported";
            Bounds oldB = CombinedBounds(originals), newB = CombinedBounds(bed.GetComponentsInChildren<Renderer>(true));
            if (Vector3.Distance(oldB.center, newB.center) > .8f || Mathf.Abs(oldB.min.y - newB.min.y) > .30f)
            {
                Debug.LogWarning("CREATION_UI HomeBedAI 摆位校验未通过，菜单房间保留原床。");
                UnityEngine.Object.Destroy(wrapper);
                return;
            }
            var material = new Material(Shader.Find("Standard"))
            { name = "HomeBedAI_MenuAlbedo", mainTexture = albedo, color = Color.white, hideFlags = HideFlags.DontSave };
            material.SetFloat("_Glossiness", .16f);
            material.SetFloat("_Metallic", 0f);
            menuMaterials.Add(material);
            foreach (Renderer renderer in bed.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
            GameArtRendering.Apply(bed, false);
            foreach (Renderer renderer in originals) renderer.enabled = false;
            foreach (Transform node in wrapper.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = layer;
        }

        private static Bounds CombinedBounds(IEnumerable<Renderer> renderers)
        {
            Bounds bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in renderers)
            { if (first) { bounds = renderer.bounds; first = false; } else bounds.Encapsulate(renderer.bounds); }
            return bounds;
        }
    }
}
