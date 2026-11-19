// Codex independent geometry review QA; actual imported models and production constructors.
// No input simulation, no player inventory or save mutation, no claim of full play-through.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guochao.Editor
{
    [InitializeOnLoad] public static class HouseholdAssets20Validation
    {
        [Serializable] public sealed class Check { public string name; public bool passed; public string detail; }
        [Serializable] public sealed class Report
        {
            public bool success;
            public string scope = "Real PlayMode HomeWorld/WorldRuntime and CreationRoomStage construction; imported meshes, support, collider registration, rotated-menu placement and idempotence. Not hardware input or a complete play-through.";
            public List<Check> checks = new List<Check>();
            public List<string> engineErrors = new List<string>();
        }
        const string Pending = "Guochao.Household20.Pending", Output = "Guochao.Household20.Output";
        static Report report;
        static string output;
        static GameObject live, menu;
        static readonly List<Material> materials = new List<Material>();
        static Feedback3CoroutineHost frameHost;
        static double began;
        static HouseholdAssets20Validation() { EditorApplication.update += Advance; }
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "--household20-report");
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("--household20-report <new report.json> required");
            string path = Path.GetFullPath(args[at + 1]);
            if (File.Exists(path)) throw new IOException("Preserve existing evidence: " + path);
            SessionState.SetString(Output, path); SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if (EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
            if (SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false); output = SessionState.GetString(Output, "");
                report = new Report(); materials.Clear(); began = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += Capture;
                frameHost = new GameObject("Household20_FrameHost").AddComponent<Feedback3CoroutineHost>();
                frameHost.Launch(Cases(), () => Finish(""), ex => Finish(ex.ToString()));
            }
            if (frameHost != null && frameHost.IsRunning && EditorApplication.timeSinceStartup - began > 180)
            { frameHost.Abort(); Finish("Household20 exceeded 180 seconds"); }
        }
        static IEnumerator Cases()
        {
                live = new GameObject("Household20_LiveWorld");
                WorldRuntime world = live.AddComponent<WorldRuntime>();
                world.Build(GamePhase.Home);
                Transform source = Find(live.transform, "HomeWorld");
                Require(source != null, "Live HomeWorld source exists");
                var result = HouseholdAssetPresentation.Install(source, live.transform, WorldRuntime.EnvironmentLayer, world);
                Validate("home", source, live.transform, result, world);
                int colliders = world.SolidColliders.Count;
                var again = HouseholdAssetPresentation.Install(source, live.transform, WorldRuntime.EnvironmentLayer, world);
                CheckThat("home.same-instance-on-repeat", result.stoolInstance == again.stoolInstance && result.basinInstance == again.basinInstance, "Repeated install retains original imported objects");
                CheckThat("home.same-colliders-on-repeat", colliders == world.SolidColliders.Count && Count(live.transform, HouseholdAssetPresentation.WrapperName) == 1, "No duplicate wrappers or physical colliders");

                menu = new GameObject("Household20_MenuParent");
                menu.transform.position = new Vector3(24, 2, -20);
                menu.transform.rotation = Quaternion.Euler(0, 31, 0);
                menu.transform.localScale = Vector3.one * 1.1f;
                var room = CreationRoomStage.Build(menu.transform, 23, materials);
                Require(room.root != null, "Menu room exists");
                Transform menuSource = Find(room.root.transform, "HomeWorld_Menu");
                Require(menuSource != null, "Menu HomeWorld source exists");
                yield return null; // Production Destroy calls settle on a real player frame.
                Validate("rotated-menu", menuSource, room.root.transform, room.household, null);
                var menuAgain = HouseholdAssetPresentation.Install(menuSource, room.root.transform, 23);
                CheckThat("menu.same-instance-on-repeat", menuAgain.stoolInstance == room.household.stoolInstance && menuAgain.basinInstance == room.household.basinInstance, "Same public installer called from production menu constructor");
                CheckThat("menu.no-enabled-colliders", Array.TrueForAll(room.root.GetComponentsInChildren<Collider>(true), c => !c.enabled), "Menu cannot block or receive world physics");

            if (menu != null) Object.Destroy(menu);
            if (live != null) Object.Destroy(live);
            foreach (Material material in materials) if (material != null) Object.Destroy(material);
            yield return null;
        }
        static void Finish(string failure)
        {
            if (!string.IsNullOrEmpty(failure)) report.engineErrors.Add(failure);
            Application.logMessageReceived -= Capture;
            report.success = report.engineErrors.Count == 0 && report.checks.Count > 0 && report.checks.TrueForAll(c => c.passed);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("HOUSEHOLD20_VALIDATION " + report.success + " checks=" + report.checks.Count);
            if (Application.isBatchMode) EditorApplication.Exit(report.success ? 0 : 1);
        }
        static void Validate(string prefix, Transform source, Transform frame, HouseholdAssetPresentation.Result result, WorldRuntime world)
        {
            Require(result != null && result.stoolInstance != null && result.basinInstance != null && result.kettleRoot != null, prefix + " real installed objects");
            Bounds stool = MeshBounds(result.stoolInstance.transform, frame);
            Bounds basin = MeshBounds(result.basinInstance.transform, frame);
            Bounds kettle = MeshBounds(result.kettleRoot, frame);
            Renderer table = RendererNamed(source, "ordinary low table"), counter = RendererNamed(source, "kitchen counter"), sink = RendererNamed(source, "sink"), book = RendererNamed(source, "bedside book"), oldStool = RendererNamed(source, "bedside stool");
            Bounds floorB = MeshBounds(RendererNamed(source, "Collider_HomeFloor").transform, frame, true);
            Bounds tableB = MeshBounds(table.transform, frame), counterB = MeshBounds(counter.transform, frame), sinkB = MeshBounds(sink.transform, frame), bookB = MeshBounds(book.transform, frame);
            CheckThat(prefix + ".imported-stool-mesh", HasSourceMesh(result.stoolInstance, "HH02_ElmStool.fbx"), "Mesh asset comes from original HH02 FBX");
            CheckThat(prefix + ".imported-basin-mesh", HasSourceMesh(result.basinInstance, "HH01_EnamelBasin.fbx"), "Mesh asset comes from original HH01 FBX");
            CheckThat(prefix + ".natural-stool-height", Mathf.Abs(stool.size.y - .386f) < .003f, stool.ToString());
            CheckThat(prefix + ".natural-basin-size", Mathf.Abs(basin.size.y - .115f) < .003f && Mathf.Abs(basin.size.x - .4f) < .003f && Mathf.Abs(basin.size.z - .4f) < .003f, basin.ToString());
            CheckThat(prefix + ".stool-grounded", Mathf.Abs(stool.min.y - MeshBounds(oldStool.transform, frame).min.y) < .003f, stool.ToString());
            CheckThat(prefix + ".old-stool-hidden", !oldStool.enabled, "Old 54-polygon bedside cube is not layered with new stool");
            CheckThat(prefix + ".book-supported", Mathf.Abs(bookB.min.y - stool.max.y) < .003f && InsideFootprint(bookB, stool), "book=" + bookB + " stool=" + stool);
            CheckThat(prefix + ".basin-floor-supported", Mathf.Abs(basin.min.y - floorB.max.y) < .003f && InsideFootprint(basin, floorB), "basin=" + basin + " physical floor=" + floorB);
            CheckThat(prefix + ".basin-within-existing-table-footprint", InsideFootprint(basin, tableB) && basin.max.y + .02f < tableB.min.y, "Basin stays below tabletop and inside old walking obstacle: basin=" + basin + " table=" + tableB);
            int cookware = 0; var intersecting = new List<string>(); var cookwareIntersections = new List<string>();
            foreach (Renderer existing in source.GetComponentsInChildren<Renderer>(true))
            {
                if (!existing.enabled || !existing.gameObject.activeInHierarchy || existing.name.StartsWith("Collider_", StringComparison.Ordinal)) continue;
                MeshFilter mf = existing.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                bool pan = existing.name == "Vessel" || existing.name.StartsWith("Vessel.", StringComparison.Ordinal)
                    || existing.name.StartsWith("Vessel_handle", StringComparison.Ordinal) || existing.name.StartsWith("Wok_rim", StringComparison.Ordinal);
                if (pan) cookware++;
                Bounds occupied = MeshBounds(existing.transform, frame);
                // Vertical floor support touches, not penetrates; all other actual mesh
                // envelopes must stay clear, including original pans/handles/table legs.
                bool overlaps = basin.min.x < occupied.max.x - .001f && basin.max.x > occupied.min.x + .001f
                    && basin.min.z < occupied.max.z - .001f && basin.max.z > occupied.min.z + .001f
                    && basin.min.y < occupied.max.y - .001f && basin.max.y > occupied.min.y + .001f;
                if (overlaps) { intersecting.Add(existing.name); if (pan) cookwareIntersections.Add(existing.name); }
            }
            CheckThat(prefix + ".original-pans-still-visible", cookware >= 5, "Original bowls, rim and handles retained: " + cookware);
            CheckThat(prefix + ".basin-clear-of-original-cookware", cookwareIntersections.Count == 0 && cookware >= 5, "Intersecting cookware: " + string.Join(",", cookwareIntersections));
            CheckThat(prefix + ".basin-clear-of-existing-meshes", intersecting.Count == 0, "Intersecting actual objects: " + string.Join(",", intersecting));
            CheckThat(prefix + ".kettle-supported", Mathf.Abs(kettle.min.y - counterB.max.y) < .003f && InsideFootprint(kettle, counterB), "kettle=" + kettle + " counter=" + counterB);
            CheckThat(prefix + ".kettle-clear-of-sink", !OverlapFootprint(kettle, sinkB), "kettle=" + kettle + " sink=" + sinkB);
            CheckThat(prefix + ".existing-kettle-only", Count(source, HouseholdAssetPresentation.KettleRootName) == 1, "Original embedded kettle instance retained");
            CheckThat(prefix + ".existing-slippers-retained", Count(source, "ASSET_HH04_ClothSlippersPair") == 1, "Original embedded slippers unchanged");
            foreach (Transform t in result.stoolInstance.GetComponentsInChildren<Transform>(true)) Require(t.gameObject.layer == (world != null ? WorldRuntime.EnvironmentLayer : 23), prefix + " stool layer");
            foreach (Transform t in result.basinInstance.GetComponentsInChildren<Transform>(true)) Require(t.gameObject.layer == (world != null ? WorldRuntime.EnvironmentLayer : 23), prefix + " basin layer");
            if (world != null)
            {
                Physics.SyncTransforms();
                CheckThat(prefix + ".stool-physical-envelope", ColliderMatches(result.stoolCollider, result.stoolInstance, world), "Collider must match real mesh world envelope and be registered");
                CheckThat(prefix + ".basin-physical-envelope", ColliderMatches(result.basinCollider, result.basinInstance, world), "Collider must match real mesh world envelope and be registered");
                CheckThat(prefix + ".old-stool-proxy-retired", !world.SolidColliders.Exists(c => c && c.enabled && c.name == "Collider_Bedside"), "No tall old cube collision left over");
            }
        }
        static bool ColliderMatches(BoxCollider c, GameObject model, WorldRuntime world)
        {
            if (!c || !c.enabled || !world.SolidColliders.Contains(c)) return false;
            Bounds visible = WorldRuntime.RenderBounds(model);
            return Vector3.Distance(c.bounds.center, visible.center) < .004f && Vector3.Distance(c.bounds.size, visible.size) < .004f;
        }
        static bool HasSourceMesh(GameObject model, string filename)
        {
            int vertices = 0;
            foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && AssetDatabase.GetAssetPath(mf.sharedMesh).EndsWith(filename, StringComparison.Ordinal)) vertices += mf.sharedMesh.vertexCount;
            return vertices > 100;
        }
        static Bounds MeshBounds(Transform node, Transform frame, bool includeProxy = false)
        {
            Bounds result = new Bounds(); bool found = false;
            foreach (MeshFilter mf in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !includeProxy && mf.name.StartsWith("Collider_", StringComparison.Ordinal)) continue;
                Bounds b = mf.sharedMesh.bounds; Matrix4x4 matrix = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { result = new Bounds(p, Vector3.zero); found = true; } else result.Encapsulate(p);
                }
            }
            if (!found) throw new InvalidOperationException("No real mesh under " + node.name);
            return result;
        }
        static Renderer RendererNamed(Transform root, string name)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) if (r.name == name) return r;
            throw new InvalidOperationException("Missing original renderer " + name);
        }
        static Transform Find(Transform root, string name) { foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
        static int Count(Transform root, string name) { int n = 0; foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) n++; return n; }
        static bool InsideFootprint(Bounds a, Bounds b) { const float e = .002f; return a.min.x >= b.min.x - e && a.max.x <= b.max.x + e && a.min.z >= b.min.z - e && a.max.z <= b.max.z + e; }
        static bool OverlapFootprint(Bounds a, Bounds b) { return a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z; }
        static void Require(bool good, string message) { if (!good) throw new InvalidOperationException(message); }
        static void CheckThat(string name, bool good, string detail) { report.checks.Add(new Check { name = name, passed = good, detail = detail }); }
        static void Capture(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.engineErrors.Add(message + "\n" + trace); }
    }
}
