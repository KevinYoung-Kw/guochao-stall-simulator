using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Claude ASSETS20_HOUSE implementation, followed by Codex review corrections:
    // use the common upright parent frame, preserve FBX conversion roots, measure
    // source mesh bounds directly, and retain one installed instance on repeat calls.
    public static class HouseholdAssetPresentation
    {
        public const string WrapperName = "HouseholdAssets20_Runtime";
        public const string KettleRootName = "ASSET_HH03_AluminiumKettle";
        public sealed class Result
        {
            public bool installed, stoolReplaced, bookRestacked, basinPlaced, kettleMoved, kettleSupported;
            public GameObject stoolInstance, basinInstance;
            public Transform kettleRoot;
            // All bounds use the caller's upright parent frame, never the imported FBX root.
            public Bounds stoolBounds, basinBounds, kettleBounds;
            public BoxCollider stoolCollider, basinCollider;
        }
        public static Result Install(Transform sourceHome, Transform parent, int layer, WorldRuntime world = null)
        {
            if (!sourceHome || !parent) return new Result();
            Transform previous = parent.Find(WrapperName);
            if (previous)
            {
                HouseholdAssetInstallation state = previous.GetComponent<HouseholdAssetInstallation>();
                if (state && state.Source == sourceHome && state.Value != null) return state.Value;
                // Only an incomplete abandoned installation can enter this branch.
                if (world != null) world.SolidColliders.RemoveAll(c => c && c.transform.IsChildOf(previous));
                previous.gameObject.SetActive(false);
                Dispose(previous.gameObject);
            }
            var result = new Result();
            Renderer counter = Named(sourceHome, "kitchen counter"), sink = Named(sourceHome, "sink"),
                oldStool = Named(sourceHome, "bedside stool"), book = Named(sourceHome, "bedside book"),
                table = Named(sourceHome, "ordinary low table"), floor = Named(sourceHome, "Collider_HomeFloor");
            Transform kettle = Find(sourceHome, KettleRootName);
            if (kettle && counter && sink && TryBounds(kettle, parent, out Bounds kettleB))
            {
                Bounds counterB = MeshBounds(counter, parent), sinkB = MeshBounds(sink, parent);
                Renderer well = Named(sourceHome, "sink well");
                if (well) sinkB.Encapsulate(MeshBounds(well, parent));
                const float pad = .02f;
                float left = sinkB.min.x - counterB.min.x, right = counterB.max.x - sinkB.max.x;
                float freeMin = left >= right ? counterB.min.x + pad : sinkB.max.x + pad;
                float freeMax = left >= right ? sinkB.min.x - pad : counterB.max.x - pad;
                if (freeMax - freeMin >= kettleB.size.x && counterB.size.z >= kettleB.size.z + pad * 2)
                {
                    float cx = (freeMin + freeMax) * .5f;
                    float cz = Mathf.Clamp(kettleB.center.z, counterB.min.z + kettleB.extents.z + pad, counterB.max.z - kettleB.extents.z - pad);
                    Vector3 delta = new Vector3(cx - kettleB.center.x, counterB.max.y - kettleB.min.y, cz - kettleB.center.z);
                    kettle.position += parent.TransformVector(delta);
                    result.kettleMoved = delta.sqrMagnitude > .000001f;
                    result.kettleBounds = BoundsOf(kettle, parent);
                    result.kettleSupported = true;
                    result.kettleRoot = kettle;
                }
                else Debug.LogWarning("Household assets: existing kettle cannot fit the measured free counter end.");
            }
            GameObject stoolPrefab = Resources.Load<GameObject>("Models/HH02_ElmStool");
            GameObject basinPrefab = Resources.Load<GameObject>("Models/HH01_EnamelBasin");
            if (!stoolPrefab || !basinPrefab || !oldStool || !table || !floor)
            {
                Debug.LogWarning("Household assets: original props or support surfaces unavailable; existing furniture retained.");
                return result;
            }
            GameObject wrapper = new GameObject(WrapperName);
            wrapper.transform.SetParent(parent, false);
            GameObject stool = CreateProp(stoolPrefab, wrapper.transform, "HH02_ElmStool_Installed");
            GameObject basin = CreateProp(basinPrefab, wrapper.transform, "HH01_EnamelBasin_Installed");
            if (!stool || !basin)
            {
                wrapper.SetActive(false); Dispose(wrapper); return result;
            }
            Bounds oldB = MeshBounds(oldStool, parent), tableB = MeshBounds(table, parent), floorB = MeshBounds(floor, parent);
            stool.transform.localPosition = new Vector3(oldB.center.x, oldB.min.y, oldB.center.z);
            // The tabletop is occupied by the two original pans. Store the basin
            // on the real floor below the middle of the low table, between its
            // four legs. Its footprint remains inside the existing table obstacle.
            basin.transform.localPosition = new Vector3(tableB.center.x, floorB.max.y, tableB.center.z);
            Bounds stoolB = BoundsOf(stool.transform, parent), basinB = BoundsOf(basin.transform, parent);
            if (basinB.size.x > tableB.size.x || basinB.size.z > tableB.size.z || basinB.max.y > tableB.min.y - .02f
                || !ClearOfSourceMeshes(basinB, sourceHome, parent))
            {
                wrapper.SetActive(false); Dispose(wrapper);
                Debug.LogWarning("Household assets: basin cannot fit the clear floor space underneath the low table."); return result;
            }
            // Source meshes are confirmed before replacing any existing furniture.
            oldStool.enabled = false;
            result.stoolReplaced = true; result.basinPlaced = true;
            result.stoolInstance = stool; result.basinInstance = basin;
            result.stoolBounds = stoolB; result.basinBounds = basinB;
            if (book)
            {
                Bounds bookB = MeshBounds(book, parent);
                Vector3 delta = new Vector3(stoolB.center.x - bookB.center.x, stoolB.max.y - bookB.min.y, stoolB.center.z - bookB.center.z);
                book.transform.position += parent.TransformVector(delta);
                result.bookRestacked = true;
            }
            if (world != null)
            {
                foreach (Collider c in new List<Collider>(world.SolidColliders))
                    if (c && c.name == "Collider_Bedside") { c.enabled = false; world.SolidColliders.Remove(c); }
                result.stoolCollider = AddEnvelope(stool, world);
                result.basinCollider = AddEnvelope(basin, world);
            }
            foreach (Transform node in wrapper.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = layer;
            GameArtRendering.Apply(wrapper, false);
            Physics.SyncTransforms();
            result.installed = true;
            var installation = wrapper.AddComponent<HouseholdAssetInstallation>();
            installation.Source = sourceHome; installation.Value = result;
            return result;
        }
        // Visible mesh envelopes, not the deliberately solid table proxy, tell
        // whether a stored prop intersects pans, handles, legs or nearby equipment.
        // Floor contact is allowed; a centimetre of horizontal breathing room is kept.
        static bool ClearOfSourceMeshes(Bounds placed, Transform sourceHome, Transform frame)
        {
            foreach (Renderer renderer in sourceHome.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.name.StartsWith("Collider_", StringComparison.Ordinal)) continue;
                MeshFilter mesh = renderer.GetComponent<MeshFilter>();
                if (!mesh || !mesh.sharedMesh) continue;
                Bounds other = MeshBounds(renderer, frame);
                if (other.max.y <= placed.min.y + .002f || other.min.y >= placed.max.y + .002f) continue;
                if (other.min.x < placed.max.x + .01f && other.max.x > placed.min.x - .01f
                    && other.min.z < placed.max.z + .01f && other.max.z > placed.min.z - .01f) return false;
            }
            return true;
        }
        static GameObject CreateProp(GameObject prefab, Transform parent, string name)
        {
            GameObject prop = new GameObject(name); prop.transform.SetParent(parent, false);
            var offset = new GameObject("AuthoredPivotOffset"); offset.transform.SetParent(prop.transform, false);
            // Imported root rotation and scale remain exactly as Unity imported them.
            GameObject model = UnityEngine.Object.Instantiate(prefab, offset.transform, false);
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Dispose(c); }
            if (!TryBounds(model.transform, prop.transform, out Bounds b) || b.size.x <= .0001f || b.size.y <= .0001f || b.size.z <= .0001f)
            { prop.SetActive(false); Dispose(prop); return null; }
            offset.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            return prop;
        }
        static BoxCollider AddEnvelope(GameObject prop, WorldRuntime world)
        {
            Bounds b = BoundsOf(prop.transform, prop.transform);
            BoxCollider collider = prop.AddComponent<BoxCollider>();
            collider.center = b.center; collider.size = b.size;
            world.SolidColliders.Add(collider); return collider;
        }
        static Renderer Named(Transform root, string name)
        { foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r; return null; }
        static Transform Find(Transform root, string name)
        { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
        static Bounds MeshBounds(Renderer renderer, Transform frame)
        {
            MeshFilter mesh = renderer.GetComponent<MeshFilter>();
            if (!mesh || !mesh.sharedMesh) throw new InvalidOperationException("Missing original mesh for " + renderer.name);
            return TransformBounds(mesh.sharedMesh.bounds, frame.worldToLocalMatrix * mesh.transform.localToWorldMatrix);
        }
        static Bounds BoundsOf(Transform root, Transform frame)
        { if (!TryBounds(root, frame, out Bounds b)) throw new InvalidOperationException("No imported mesh under " + root.name); return b; }
        static bool TryBounds(Transform root, Transform frame, out Bounds result)
        {
            result = new Bounds(); bool found = false;
            foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || mesh.name.StartsWith("Collider_", StringComparison.Ordinal)) continue;
                Bounds b = TransformBounds(mesh.sharedMesh.bounds, frame.worldToLocalMatrix * mesh.transform.localToWorldMatrix);
                if (!found) { result = b; found = true; } else result.Encapsulate(b);
            }
            return found;
        }
        static Bounds TransformBounds(Bounds b, Matrix4x4 matrix)
        {
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }
        static void Dispose(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
    // Runtime-only cache belongs to the room hierarchy and vanishes when the room closes.
    public sealed class HouseholdAssetInstallation : MonoBehaviour
    {
        [NonSerialized] public Transform Source;
        [NonSerialized] public HouseholdAssetPresentation.Result Value;
    }
}
