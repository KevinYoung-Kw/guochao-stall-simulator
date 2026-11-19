using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    /// <summary>
    /// Current Built-in implementation of the document's visual treatment.
    /// Albedo-only two-tone lighting + object silhouettes + camera depth/normal ink and 4% paper.
    /// This is deliberately not labelled an installed URP pipeline.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class GameArtRendering : MonoBehaviour
    {
        private static readonly Dictionary<(Material source, int paletteScope), Material> plain = new Dictionary<(Material, int), Material>();
        private static readonly Dictionary<(Material source, int paletteScope), Material> outlined = new Dictionary<(Material, int), Material>();
        private static readonly Dictionary<Mesh, Mesh> softenedHeads = new Dictionary<Mesh, Mesh>();
        private static Shader toon;
        private Material paper;
        private Camera view;

        public static GameArtRendering Attach(Camera camera, Transform worldRoot = null)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var effect = camera.GetComponent<GameArtRendering>() ?? camera.gameObject.AddComponent<GameArtRendering>();
            effect.Configure();
            Debug.Log("GAME_ART_RENDERING Albedo-only two-tone; object ink + screen depth/normal ink; paper=0.012; pipeline=Built-in");
            return effect;
        }

        /// <summary>Call immediately after instantiation, including new shoppers and goods.</summary>
        public static void Apply(GameObject model, bool objectOutline = false)
        {
            if (model == null) return;
            if (toon == null) toon = Resources.Load<Shader>("Shaders/GuochaoToon");
            if (toon == null) throw new InvalidOperationException("Missing GuochaoToon shader resource");
            Dictionary<(Material source, int paletteScope), Material> cache = objectOutline ? outlined : plain;
            int paletteScope = AuthoredWorldPalette.Scope(model.name);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (IsCollisionProxy(renderer.transform)) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null) continue;
                    if (source.shader == toon && Mathf.Approximately(source.GetFloat("_OutlineWidth"), objectOutline ? .009f : 0)) continue;
                    var cacheKey = (source, paletteScope);
                    if (!cache.TryGetValue(cacheKey, out Material target) || target == null)
                    {
                        target = new Material(toon) { name = source.name + (objectOutline ? "_ToonObject" : "_ToonWorld") };
                        // Preserve Unity's imported/runtime colour values. Do not gamma-convert twice.
                        target.color = AuthoredWorldPalette.Resolve(model, source, source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                        if (source.HasProperty("_MainTex"))
                        {
                            target.mainTexture = source.GetTexture("_MainTex");
                            if(target.mainTexture is Texture2D albedo){albedo.anisoLevel=8;albedo.filterMode=FilterMode.Trilinear;albedo.mipMapBias=-.35f;}
                            target.mainTextureScale = source.GetTextureScale("_MainTex");
                            target.mainTextureOffset = source.GetTextureOffset("_MainTex");
                        }
                        target.SetColor("_LightTint", new Color(1.08f, 1.01f, .88f));
                        target.SetColor("_ShadeTint", new Color(.75f, .78f, .84f));
                        target.SetColor("_OutlineColor", new Color(.012f, .020f, .030f));
                        target.SetFloat("_OutlineWidth", objectOutline ? .009f : 0);
                        target.SetShaderPassEnabled("OBJECT_INK", objectOutline);
                        target.SetFloat("_RampThreshold", .44f);
                        target.SetFloat("_RampSoftness", .025f);
                        target.SetColor("_EmissionColor", source.HasProperty("_EmissionColor") && source.IsKeywordEnabled("_EMISSION")
                            ? source.GetColor("_EmissionColor") : Color.black);
                        cache[cacheKey] = target;
                    }
                    materials[i] = target;
                }
                renderer.sharedMaterials = materials;
                if (objectOutline && renderer is SkinnedMeshRenderer skin) SoftenHeadNormals(skin);
            }
        }

        private static bool IsCollisionProxy(Transform node)
        {
            while (node != null)
            {
                if (node.name.StartsWith("Collider_", StringComparison.Ordinal)) return true;
                node = node.parent;
            }
            return false;
        }

        // Runtime normal-transfer equivalent: only vertices influenced by the Head bone.
        // The mesh positions, bone weights, hands, animations and source FBX remain unchanged.
        private static void SoftenHeadNormals(SkinnedMeshRenderer renderer)
        {
            Mesh source = renderer.sharedMesh;
            if (source == null || !source.isReadable || source.name.EndsWith("_SoftHead", StringComparison.Ordinal)) return;
            if (softenedHeads.TryGetValue(source, out Mesh cached)) { renderer.sharedMesh = cached; return; }
            var headBones = new HashSet<int>();
            for (int i = 0; i < renderer.bones.Length; i++)
                if (renderer.bones[i] != null && renderer.bones[i].name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                    headBones.Add(i);
            if (headBones.Count == 0) return;
            Vector3[] vertices = source.vertices, normals = source.normals;
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != vertices.Length || normals.Length != vertices.Length) return;
            float[] influence = new float[vertices.Length];
            Vector3 low = Vector3.one * float.PositiveInfinity, high = Vector3.one * float.NegativeInfinity;
            int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight w = weights[i];
                float k = (headBones.Contains(w.boneIndex0) ? w.weight0 : 0) + (headBones.Contains(w.boneIndex1) ? w.weight1 : 0)
                    + (headBones.Contains(w.boneIndex2) ? w.weight2 : 0) + (headBones.Contains(w.boneIndex3) ? w.weight3 : 0);
                influence[i] = k;
                if (k < .5f) continue;
                low = Vector3.Min(low, vertices[i]); high = Vector3.Max(high, vertices[i]); count++;
            }
            if (count < 8) return;
            Vector3 center = (low + high) * .5f;
            Vector3 radius = (high - low) * .5f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (influence[i] < .5f) continue;
                Vector3 d = vertices[i] - center;
                Vector3 sphereNormal = new Vector3(d.x / Mathf.Max(radius.x, .0001f), d.y / Mathf.Max(radius.y, .0001f), d.z / Mathf.Max(radius.z, .0001f)).normalized;
                normals[i] = Vector3.Slerp(normals[i], sphereNormal, .72f * influence[i]).normalized;
            }
            Mesh copy = Instantiate(source); copy.name = source.name + "_SoftHead"; copy.normals = normals;
            softenedHeads[source] = copy; renderer.sharedMesh = copy;
        }

        private void OnEnable() { Configure(); }
        private void Configure()
        {
            view = GetComponent<Camera>();
            view.depthTextureMode |= DepthTextureMode.DepthNormals;
            if (paper != null) return;
            Shader shader = Resources.Load<Shader>("Shaders/GuochaoInkPaper");
            if (shader == null) throw new InvalidOperationException("Missing GuochaoInkPaper shader resource");
            paper = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            paper.SetFloat("_InkWidth", .55f);
            paper.SetColor("_InkColour", new Color(.018f, .026f, .038f));
            paper.SetFloat("_PaperAmount", .012f);
        }
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (paper == null) { Graphics.Blit(source, destination); return; }
            Graphics.Blit(source, destination, paper);
        }
        private void OnDestroy() { if (paper != null) Destroy(paper); }
    }
}
