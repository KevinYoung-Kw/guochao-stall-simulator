using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Recovery-station pass for the market. It locates the REAL authored
    // scene elements of the flea/recovery corner — the "旧货摊牌" stall signs,
    // the "18_回收台" recovery table and the "NPC_旧货摊主" spot — inside the
    // instantiated MarketWorld. RECOVERY12 turns the corner into a real trade
    // gate: a "RecoveryCounter" anchor is measured off the table's actual
    // render-bounds face toward CameraTarget (+0.85 m standing room, floor
    // projected), registered into WorldRuntime anchors, signed with a short
    // "回收站 · E" counter label, and consumed by GameFlow/RunState/
    // WorldRuntime like any other interaction target. The two independent
    // Aholo characters (小摊贩 / 摊贩伯爵) stand at the authored spots with
    // their real albedo textures and Legacy Idle_Loop clips, facing by their
    // own CustomerForward markers. This pass never adds colliders, never
    // joins the customer crowd, and never touches judgement queries.
    public sealed class MarketRecoveryScene : MonoBehaviour
    {
        public const string VendorResource = "Models/MarketRecoveryVendorV17";
        public const string EarlResource = "Models/MarketStallEarlV17";

        private WorldRuntime world;
        private readonly List<Renderer> hiddenOriginals = new List<Renderer>();
        private readonly List<bool> originalVisibility = new List<bool>();
        private readonly List<Material> ownedMaterials = new List<Material>();

        // Real-world centre of the authored recovery area (signs + table).
        public Vector3 RecoveryAreaCenter { get; private set; }
        public bool HasRecoveryArea { get; private set; }

        // The real trade gate: the measured RecoveryCounter anchor at the
        // authored "18_回收台". The procurement SupplyCounter anchor is the
        // fallback so the map stays truthful even if the table is missing.
        public Vector3 TradeEntryPoint => world != null && world.HasAnchor("RecoveryCounter")
            ? world.Anchor("RecoveryCounter")
            : world != null && world.HasAnchor("SupplyCounter") ? world.Anchor("SupplyCounter") : RecoveryAreaCenter;

        public static MarketRecoveryScene Install(WorldRuntime runtime)
        {
            if (runtime == null) return null;
            var existing = runtime.GetComponent<MarketRecoveryScene>();
            if (existing != null) return existing;
            var scene = runtime.gameObject.AddComponent<MarketRecoveryScene>();
            scene.Build(runtime);
            return scene;
        }

        private void Build(WorldRuntime runtime)
        {
            world = runtime;
            Transform table = null, authoredVendor = null;
            var signs = new List<Vector3>();
            foreach (Transform node in world.transform.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == "18_回收台") table = node;
                else if (node.name.StartsWith("旧货摊牌", StringComparison.Ordinal)) signs.Add(node.position);
                else if (node.name == "NPC_旧货摊主") authoredVendor = node;
            }
            // Recovery area = real average of the authored stall signs; the
            // recovery table is the fallback, the SupplyCounter anchor the
            // last resort. No invented coordinates anywhere.
            if (signs.Count > 0)
            {
                Vector3 sum = Vector3.zero; foreach (Vector3 p in signs) sum += p;
                RecoveryAreaCenter = sum / signs.Count; HasRecoveryArea = true;
            }
            else if (table != null)
            { RecoveryAreaCenter = WorldRuntime.RenderBounds(table.gameObject).center; HasRecoveryArea = true; }
            else
            { RecoveryAreaCenter = TradeEntryPoint; HasRecoveryArea = false; }

            // Face into the shop: from the stall corner toward the authored
            // camera focus, which is the market's real interior centre.
            Vector3 interior = world.Anchor("CameraTarget") - RecoveryAreaCenter; interior.y = 0;
            if (interior.sqrMagnitude < .001f) interior = Vector3.forward;
            interior.Normalize();

            // The real E gate at the authored recovery table: standing side on
            // the shop-facing face, measured from the table's render bounds.
            if (table != null) CreateRecoveryCounterGate(table, interior);
            else Debug.LogWarning("[MarketRecovery] 场景缺少 18_回收台节点，回收站办理点暂不注册；采购柜台入口不受影响。");

            // 小摊贩: at the authored NPC spot when present, otherwise just
            // behind the stall signs, with working room in front of the table.
            Vector3 vendorSpot = authoredVendor != null ? authoredVendor.position
                : RecoveryAreaCenter - interior * .55f;
            vendorSpot.y = FloorY(vendorSpot);
            GameObject vendor = PlaceCharacter("MarketRecoveryVendorV17_Actual", VendorResource, vendorSpot, interior);
            if (vendor != null && authoredVendor != null) HideOriginal(authoredVendor);

            // 摊贩伯爵: behind the real recovery table so the counter keeps
            // its clearance; skipped (with a warning) when either the table
            // or the resource is missing — never a flat stand-in icon.
            if (table != null)
            {
                Bounds tableBounds = WorldRuntime.RenderBounds(table.gameObject);
                float depth = Mathf.Abs(Vector3.Dot(tableBounds.size, interior));
                Vector3 earlSpot = tableBounds.center - interior * (depth * .5f + .42f);
                earlSpot.y = FloorY(earlSpot);
                PlaceCharacter("MarketStallEarlV17_Actual", EarlResource, earlSpot, interior);
            }
            else Debug.LogWarning("[MarketRecovery] 场景缺少 18_回收台节点，摊贩伯爵暂不摆放；其余不受影响。");
        }

        // Register the measured standing anchor into WorldRuntime so the
        // normal interaction pipeline (focus, prompt, primary, contact,
        // highlight ring) treats the recovery table like any real target.
        private void CreateRecoveryCounterGate(Transform table, Vector3 towardInterior)
        {
            Bounds b = WorldRuntime.RenderBounds(table.gameObject);
            Vector3 dir = towardInterior; dir.y = 0;
            if (dir.sqrMagnitude < .001f) dir = Vector3.forward; else dir.Normalize();
            // Exact AABB face distance along the facing direction: no guessed
            // half-extents, no corner inflation.
            float face = Mathf.Abs(b.extents.x * dir.x) + Mathf.Abs(b.extents.z * dir.z);
            Vector3 spot = b.center + dir * (face + .85f);
            spot.y = FloorY(spot);
            var anchor = new GameObject("RecoveryCounter").transform;
            anchor.SetParent(world.transform, false);
            anchor.position = spot;
            world.RegisterRecoveryCounter(anchor, table.gameObject);
            AddCounterSign("回收站 · E", new Vector3(spot.x, b.max.y + .32f, spot.z));
            Debug.Log("[MarketRecovery] 回收站办理点 RecoveryCounter @ " + spot);
        }

        // Short permanent sign at the recovery table itself, so the operable
        // spot is legible on site — the same TextMesh conventions as the
        // shelf signage in WorldRuntime.
        private void AddCounterSign(string words, Vector3 at)
        {
            Font font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            if (font == null) { Debug.LogWarning("[MarketRecovery] 缺少 UI 字体，回收站标牌未放置。"); return; }
            var go = new GameObject("RecoveryCounterSign", typeof(TextMesh));
            go.transform.SetParent(world.transform, false);
            go.transform.position = at;
            if (world.View != null) go.transform.rotation = world.View.transform.rotation;
            var label = go.GetComponent<TextMesh>();
            label.text = words; label.font = font; label.fontSize = 48; label.characterSize = .065f;
            label.anchor = TextAnchor.MiddleCenter; label.color = new Color(.85f, .42f, .30f);
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        private float FloorY(Vector3 at)
        {
            Physics.SyncTransforms();
            if (Physics.Raycast(at + Vector3.up * 3, Vector3.down, out RaycastHit hit, 10, WorldRuntime.EnvironmentMask))
                return hit.point.y;
            return world.HasAnchor("Spawn") ? world.Anchor("Spawn").y : 0;
        }

        private GameObject PlaceCharacter(string bodyName, string resource, Vector3 position, Vector3 facing)
        {
            GameObject prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
            {
                // The scene must remain playable and truthful without the
                // asset; never a placeholder icon.
                Debug.LogWarning("[MarketRecovery] 角色资源尚未到货：" + resource + "；保留原场景，不放置占位图标。");
                return null;
            }
            var body = new GameObject(bodyName).transform;
            body.SetParent(world.transform, false);
            body.position = position;
            body.rotation = facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;
            GameObject model = Instantiate(prefab, body, false);
            model.name = resource.Substring(resource.LastIndexOf('/') + 1) + "_Model";
            // Orient by the model's own authored markers (CustomerOrigin /
            // CustomerForward), the same convention the cashier and the
            // customer crowd use — never a guessed FBX axis.
            Transform origin = FindNode(model.transform, "CustomerOrigin");
            Transform forward = FindNode(model.transform, "CustomerForward");
            if (origin != null && forward != null)
            {
                Vector3 authored = forward.position - origin.position; authored.y = 0;
                if (authored.sqrMagnitude > .001f)
                    model.transform.rotation = Quaternion.FromToRotation(authored.normalized, body.forward) * model.transform.rotation;
                model.transform.position += body.position - origin.position;
            }
            else Debug.LogWarning("[MarketRecovery] " + resource + " 缺 CustomerOrigin/CustomerForward 标记，按原朝向摆放。");
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
            { collider.enabled = false; Destroy(collider); }
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                animator.enabled = false; // Legacy Animation drives the pose
            // Real albedo for this instance only; the material is owned here
            // and released on destroy, never written into shared assets.
            string basename = resource.Substring(resource.LastIndexOf('/') + 1);
            Texture2D albedo = Resources.Load<Texture2D>("Models/Textures/" + basename + "_Albedo");
            if (albedo != null)
            {
                var material = new Material(Shader.Find("Standard")) { name = basename + "_OriginalAlbedo", color = Color.white, mainTexture = albedo };
                material.SetFloat("_Glossiness", .18f);
                ownedMaterials.Add(material);
                int painted = 0;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials; painted++;
                }
                if (painted == 0) Debug.LogWarning("[MarketRecovery] 角色无可渲染网格：" + resource);
            }
            else Debug.LogWarning("[MarketRecovery] 角色 albedo 未到货：Models/Textures/" + basename + "_Albedo；暂用画风渲染。");
            GameArtRendering.Apply(model, true);
            PlayRealIdle(model, resource);
            Debug.Log("[MarketRecovery] 已摆放 " + bodyName + " @ " + position);
            return body.gameObject;
        }

        // Only the REAL imported clips are ever used: the Idle whose imported
        // name ends with "Idle_Loop" (Blender action
        // "<Rig>|<Rig>|Idle_Loop"), Legacy-imported like every other market
        // character. No authored or placeholder motion.
        private static AnimationClip FindIdleClip(string resource)
        {
            AnimationClip fallback = null;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(resource))
            {
                if (!clip.name.EndsWith("Idle_Loop", StringComparison.Ordinal)) continue;
                if (clip.legacy) return clip;
                fallback = clip;
            }
            return fallback;
        }

        private void PlayRealIdle(GameObject model, string resource)
        {
            Animation animation = model.GetComponentInChildren<Animation>(true);
            if (animation == null) animation = model.AddComponent<Animation>();
            animation.enabled = true;
            AnimationClip clip = FindIdleClip(resource);
            if (clip == null)
            { Debug.LogWarning("[MarketRecovery] 缺真实 Idle_Loop 剪辑：" + resource + "；角色保持绑定姿态。"); return; }
            if (!clip.legacy)
            { Debug.LogWarning("[MarketRecovery] Idle 剪辑非 Legacy 导入：" + clip.name + "；角色保持绑定姿态。"); return; }
            animation.AddClip(clip, "idle_loop");
            animation["idle_loop"].wrapMode = WrapMode.Loop;
            animation.Play("idle_loop");
            animation.Sample();
        }

        private static Transform FindNode(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private void HideOriginal(Transform original)
        {
            foreach (Renderer renderer in original.GetComponentsInChildren<Renderer>(true))
            { hiddenOriginals.Add(renderer); originalVisibility.Add(renderer.enabled); renderer.enabled = false; }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < hiddenOriginals.Count; i++)
                if (hiddenOriginals[i] != null) hiddenOriginals[i].enabled = originalVisibility[i];
            foreach (Material material in ownedMaterials)
                if (material != null) Destroy(material);
            ownedMaterials.Clear();
        }
    }
}
