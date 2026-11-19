using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Loads the AI-generated street shopfront (in production by the art
    // pipeline) and swaps it in for the middle block-built shop of each 20m
    // street tile. Assets are probed once; any missing or invalid asset falls
    // back cleanly so the original blockout is never doubled up.
    public static class StreetShopfrontAssets
    {
        // Contract: root delivers a static FBX, Y-up, storefront facing +Z.
        // The road on this stage lies on the -Z side of the shops, so the
        // wrapper spins 180° around Y. If real footage shows the door facing
        // away, flip only this constant — never reset the FBX child's own
        // axis conversion.
        public const float FaceYawDegrees = 180f;

        private const string ModelPath = "Models/StreetShopfrontAI";
        private const string TexturePath = "Models/Textures/StreetShopfrontAI_Albedo";
        private const string ShaderPath = "Shaders/GuochaoToon";

        private static GameObject cachedModel;
        private static Texture2D cachedTexture;
        private static Shader cachedShader;
        private static bool probed;

        public static int ReplacedCount { get; private set; }
        public static int FallbackCount { get; private set; }

        public static void Reset()
        {
            probed = false; cachedModel = null; cachedTexture = null; cachedShader = null;
            ReplacedCount = 0; FallbackCount = 0;
        }

        // footprintCenter: XZ centre of the shop footprint (Z=6.2), Y ignored
        // except as reference; footprintX/footprintZ: max 6.4 / 3.6; height:
        // the original block's authored height; groundY: pavement top (.13).
        // disposableMaterials receives each created toon material so the
        // stage's Release() destroys them exactly like its palette.
        public static bool TryCreateShopfront(Transform parent, Vector3 footprintCenter,
            float footprintX, float footprintZ, float height, float groundY,
            List<Material> disposableMaterials, out GameObject result)
        {
            result = null;
            Probe();
            if (parent == null || cachedModel == null || cachedTexture == null || cachedShader == null || disposableMaterials == null
                || !Finite(footprintCenter) || !Finite(new Vector3(footprintX, height, footprintZ)) || !Finite(groundY)
                || footprintX <= 0 || footprintZ <= 0 || height <= 0)
            {
                FallbackCount++;
                return false;
            }

            GameObject wrapper = new GameObject("StreetShopfrontAI_Wrapper");
            Material material = null;
            try
            {
                wrapper.transform.SetParent(parent, false);
                GameObject instance = UnityEngine.Object.Instantiate(cachedModel, wrapper.transform, false);
                Sanitize(instance);
                wrapper.transform.localRotation = Quaternion.Euler(0f, FaceYawDegrees, 0f);

                Bounds bounds;
                if (!ComputeTileBounds(wrapper, parent, out bounds) || !IsFinite(bounds) || bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f)
                    throw new InvalidOperationException("沿街AI店面无有效网格或尺寸");

                // Uniform scale only, fitting the authored footprint in every axis.
                float scale = Mathf.Min(footprintX / bounds.size.x, footprintZ / bounds.size.z, height / bounds.size.y);
                if (!(scale > 0f) || float.IsNaN(scale) || float.IsInfinity(scale))
                    throw new InvalidOperationException("沿街AI店面尺寸不可用");
                wrapper.transform.localScale = new Vector3(scale, scale, scale);

                // Centre on the original X, Z=6.2 footprint and rest the lowest
                // renderer point on the pavement top, matching the blockout.
                Vector3 scaledCenter = bounds.center * scale;
                Vector3 local = footprintCenter - scaledCenter;
                local.y = groundY - bounds.min.y * scale;
                if (!Finite(local))throw new InvalidOperationException("沿街AI店面位置不可用");
                wrapper.transform.localPosition = local;

                material = new Material(cachedShader) { name = "Travel_StreetShopfrontAI", mainTexture = cachedTexture };
                Color albedo = QualitySettings.activeColorSpace == ColorSpace.Linear ? Color.white.linear : Color.white;
                material.color = albedo;
                material.SetColor("_LightTint", new Color(1.08f, 1.01f, .88f));
                material.SetColor("_ShadeTint", new Color(.76f, .79f, .84f));
                material.SetFloat("_OutlineWidth", 0);
                material.SetShaderPassEnabled("OBJECT_INK", false);
                material.SetFloat("_RampThreshold", .44f);
                material.SetFloat("_RampSoftness", .025f);
                foreach (Renderer renderer in wrapper.GetComponentsInChildren<Renderer>(true))
                {
                    if(!RenderableMesh(renderer, wrapper.transform))continue;
                    Material[] slots=renderer.sharedMaterials;
                    if(slots.Length==0)slots=new Material[1];
                    for(int i=0;i<slots.Length;i++)slots[i]=material;
                    renderer.sharedMaterials=slots;
                }
                disposableMaterials.Add(material);

                ReplacedCount++;
                result = wrapper;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("沿街AI店面替换失败，回退原方块形体: " + exception.Message);
                wrapper.SetActive(false);
                if(material!=null)UnityEngine.Object.Destroy(material);
                UnityEngine.Object.Destroy(wrapper);
                FallbackCount++;
                return false;
            }
        }

        private static void Probe()
        {
            if (probed) return;
            probed = true;
            cachedModel = Resources.Load<GameObject>(ModelPath);
            cachedTexture = Resources.Load<Texture2D>(TexturePath);
            cachedShader = Resources.Load<Shader>(ShaderPath);
            if (cachedModel == null) Debug.Log("沿街AI店面模型缺失，全部回退: " + ModelPath);
            if (cachedTexture == null) Debug.Log("沿街AI店面贴图缺失，全部回退: " + TexturePath);
            if (cachedShader == null) Debug.Log("沿街AI店面shader缺失，全部回退: " + ShaderPath);
        }

        private static void Sanitize(GameObject root)
        {
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node.name.StartsWith("Collider_", StringComparison.Ordinal)) node.gameObject.SetActive(false);
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) { collider.enabled=false;UnityEngine.Object.Destroy(collider); }
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) { body.detectCollisions=false;body.isKinematic=true;UnityEngine.Object.Destroy(body); }
            foreach (Light light in root.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))camera.enabled=false;
        }

        // Measure in the moving tile's local space after facing the road;
        // imported child axes and arbitrary parent transforms remain intact.
        private static bool ComputeTileBounds(GameObject root, Transform tile, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool found = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!RenderableMesh(renderer,root.transform))continue;
                Matrix4x4 toTile = tile.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = toTile.MultiplyPoint3x4(corner);
                    if(!Finite(point))return false;
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        private static bool RenderableMesh(Renderer renderer,Transform root)
        {
            if(!renderer.enabled)return false;
            for(Transform node=renderer.transform;node!=null;node=node.parent)
            {
                if(!node.gameObject.activeSelf||node.name.StartsWith("Collider_",StringComparison.Ordinal))return false;
                if(node==root)break;
            }
            Mesh mesh=null;
            if(renderer is SkinnedMeshRenderer skin)mesh=skin.sharedMesh;
            else if(renderer is MeshRenderer){var filter=renderer.GetComponent<MeshFilter>();if(filter!=null)mesh=filter.sharedMesh;}
            return mesh!=null&&mesh.vertexCount>0;
        }

        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);

        private static bool IsFinite(Bounds bounds)
        {
            Vector3 size = bounds.size, center = bounds.center;
            return !(float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsNaN(size.z)
                || float.IsInfinity(size.x) || float.IsInfinity(size.y) || float.IsInfinity(size.z)
                || float.IsNaN(center.x) || float.IsNaN(center.y) || float.IsNaN(center.z)
                || float.IsInfinity(center.x) || float.IsInfinity(center.y) || float.IsInfinity(center.z));
        }
    }
}
