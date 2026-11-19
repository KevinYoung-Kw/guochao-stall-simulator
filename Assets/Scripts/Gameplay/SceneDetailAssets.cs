using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Fine props are actual Aholo meshes placed in the original authored metre
    // frame. Replacing renderers does not remove walls, floor or interaction data.
    public sealed class SceneDetailAssets : MonoBehaviour
    {
        private readonly List<Material> owned = new List<Material>();
        public int AppliedCount { get; private set; }
        public string[] AssetIds { get; private set; } = Array.Empty<string>();
        public Bounds[] AssetBounds { get; private set; } = Array.Empty<Bounds>();
        public static SceneDetailAssets Install(WorldRuntime world, GamePhase phase)
        {
            var detail=world.GetComponent<SceneDetailAssets>();
            if(detail!=null)return detail;
            detail=world.gameObject.AddComponent<SceneDetailAssets>();
            if(phase==GamePhase.Home)
            {
                detail.Replace(world,"HomeBedAI",new[]{"bed frame","bed leg","mattress","quilt","folded quilt edge","pillow","headboard"});
                // ASSETS20_HOUSE: native basin/stool props + floating-kettle fix,
                // using the live Home model as the anchor space.
                Transform homeModel=FindHomeModel(world.transform);
                if(homeModel!=null)
                    HouseholdAssetPresentation.Install(homeModel,world.transform,WorldRuntime.EnvironmentLayer,world);
            }
            else if(phase==GamePhase.Market)
            {
                detail.Replace(world,"MarketProduceAI",new[]{"09_花车","特价车台沿","特价车轮","花车包装货"});
                MarketDetails.Install(world);
            }
            return detail;
        }
        private void Replace(WorldRuntime world,string asset,string[] oldNames)
        {
            var prefab=Resources.Load<GameObject>("Models/"+asset);
            var albedo=Resources.Load<Texture2D>("Models/Textures/"+asset+"_Albedo");
            if(prefab==null||albedo==null){Debug.LogWarning("SceneDetailAssets: "+asset+" mesh/albedo not ready; old renderers retained.");return;}
            var originals=new List<Renderer>();
            foreach(Renderer renderer in world.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)||renderer.name.StartsWith("Collider_",StringComparison.Ordinal))continue;
                foreach(string name in oldNames)
                    if(renderer.name==name || renderer.name.StartsWith(name+".",StringComparison.Ordinal))
                    { originals.Add(renderer);break; }
            }
            if(originals.Count==0){Debug.LogWarning("SceneDetailAssets: no authored replacement group for "+asset+"; not spawning an unanchored duplicate.");return;}
            // The imported single-root FBX carries Blender-to-Unity axis conversion
            // on its own transform. Reset only a separate wrapper, never that root.
            var wrapper=new GameObject(asset+"_Runtime");wrapper.transform.SetParent(world.transform,false);
            GameObject model=Instantiate(prefab,wrapper.transform,false);model.name=asset+"_Imported";
            Bounds originalBounds=CombinedBounds(originals),modelBounds=CombinedBounds(model.GetComponentsInChildren<Renderer>(true));
            if(Vector3.Distance(originalBounds.center,modelBounds.center)>.8f || Mathf.Abs(originalBounds.min.y-modelBounds.min.y)>.30f)
            {
                Debug.LogError("SCENE_DETAIL_REJECTED "+asset+" imported placement mismatch: old="+originalBounds+" new="+modelBounds+" root="+model.transform.localEulerAngles);
                Destroy(wrapper);return;
            }
            var material=new Material(Shader.Find("Standard")){name=asset+"_OriginalAlbedo",mainTexture=albedo,color=Color.white};
            material.SetFloat("_Glossiness",.16f);material.SetFloat("_Metallic",0);owned.Add(material);
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots=renderer.sharedMaterials;for(int i=0;i<slots.Length;i++)slots[i]=material;renderer.sharedMaterials=slots;
            }
            GameArtRendering.Apply(model,false);
            foreach(Renderer renderer in originals)renderer.enabled=false;
            string proxyName=asset=="HomeBedAI"?"Collider_Bed":"Collider_09";
            // Generated meshes replace the physical envelope as well as the
            // picture. The old produce proxy was 25 cm shorter than its mesh.
            foreach(Collider previous in new List<Collider>(world.SolidColliders))
                if(previous && previous.name==proxyName){previous.enabled=false;world.SolidColliders.Remove(previous);}
            wrapper.layer=WorldRuntime.EnvironmentLayer;
            BoxCollider collision=wrapper.AddComponent<BoxCollider>();
            collision.center=wrapper.transform.InverseTransformPoint(modelBounds.center);collision.size=modelBounds.size;
            world.SolidColliders.Add(collision);Physics.SyncTransforms();
            AppliedCount++;AssetIds=new[]{asset};AssetBounds=new[]{modelBounds};
            Debug.Log("SCENE_DETAIL_APPLIED "+asset+" replaced_renderers="+originals.Count+" physical_colliders="+world.SolidColliders.Count+" imported_root="+model.transform.localEulerAngles+" world_bounds="+modelBounds);
        }
        private static Bounds CombinedBounds(IEnumerable<Renderer> renderers)
        {Bounds bounds=new Bounds();bool first=true;foreach(Renderer renderer in renderers){if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}return bounds;}
        // The HomeWorld prefab instance lives somewhere under the WorldRuntime
        // root; the household installer anchors on that model, not the runtime.
        private static Transform FindHomeModel(Transform worldRoot)
        {
            foreach(Transform t in worldRoot.GetComponentsInChildren<Transform>(true))
                if(t.name=="HomeWorld"||t.name.StartsWith("HomeWorld",StringComparison.Ordinal))return t;
            return worldRoot;
        }
        private void OnDestroy(){foreach(Material material in owned)if(material)Destroy(material);}
    }
}
