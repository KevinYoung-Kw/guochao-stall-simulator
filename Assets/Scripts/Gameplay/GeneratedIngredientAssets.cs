using System;
using UnityEngine;

namespace Guochao
{
    // Actual Aholo G1 meshes. Both products share the existing prepared-food ID;
    // these are visual contents, not additional purchasable inventory quantities.
    public static class GeneratedIngredientAssets
    {
        public const string RiceAsset="PreparedFrozenRiceAI",PouchAsset="PreparedMealPouchAI";
        static Material riceMaterial,pouchMaterial;
        static bool warned;
        public static bool TryPopulatePrepared(Transform crate)
        {
            GameObject rice=Resources.Load<GameObject>("Models/"+RiceAsset),pouch=Resources.Load<GameObject>("Models/"+PouchAsset);
            Texture2D riceAlbedo=Resources.Load<Texture2D>("Models/Textures/"+RiceAsset+"_Albedo"),pouchAlbedo=Resources.Load<Texture2D>("Models/Textures/"+PouchAsset+"_Albedo");
            if(rice==null||pouch==null||riceAlbedo==null||pouchAlbedo==null)
            {
                if(!warned){Debug.LogWarning("Prepared-food AI assets incomplete; retaining the existing sealed-pack fallback.");warned=true;}
                return false;
            }
            riceMaterial=MaterialFor(riceMaterial,RiceAsset,riceAlbedo);pouchMaterial=MaterialFor(pouchMaterial,PouchAsset,pouchAlbedo);
            var batch=new GameObject("PreparedFood_AholoG1");batch.transform.SetParent(crate,false);
            try
            {
                for(int row=0;row<3;row++)
                {
                    float z=-.11f+row*.11f;
                    Add(rice,riceMaterial,batch.transform,new Vector3(-.095f,.05f,z),new Vector3(.16f,.06f,.09f),"FrozenRice_"+row);
                    Add(pouch,pouchMaterial,batch.transform,new Vector3(.095f,.05f,z),new Vector3(.085f,.12f,.035f),"MealPouch_"+row);
                }
                return true;
            }
            catch(Exception e){UnityEngine.Object.Destroy(batch);Debug.LogError("Prepared-food mesh envelope rejected: "+e.Message);return false;}
        }
        static Material MaterialFor(Material existing,string name,Texture texture)
        {
            if(existing!=null)return existing;
            var material=new Material(Shader.Find("Standard")){name=name+"_OriginalAlbedo",mainTexture=texture,color=Color.white};
            material.SetFloat("_Glossiness",.14f);material.SetFloat("_Metallic",0);return material;
        }
        static void Add(GameObject prefab,Material material,Transform parent,Vector3 bottom,Vector3 size,string name)
        {
            var slot=new GameObject(name);slot.transform.SetParent(parent,false);
            GameObject model=UnityEngine.Object.Instantiate(prefab,slot.transform,false);
            // Preserve the imported FBX root's axis conversion. Normalize a
            // separate wrapper in its parent's local frame, including rotated carts.
            Bounds bounds=LocalMeshBounds(slot.transform);Vector3 extent=bounds.size;
            if(extent.x<.00001f||extent.y<.00001f||extent.z<.00001f)throw new InvalidOperationException("Empty or flat source mesh: "+name);
            Vector3 factor=new Vector3(size.x/extent.x,size.y/extent.y,size.z/extent.z);
            slot.transform.localScale=factor;
            slot.transform.localPosition=bottom-Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),factor);
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {Material[] slots=renderer.sharedMaterials;for(int i=0;i<slots.Length;i++)slots[i]=material;renderer.sharedMaterials=slots;}
            // Tiny printed packages retain their textured silhouette without the
            // 9 mm object outline swallowing the label. The crate keeps its outline.
            GameArtRendering.Apply(model,false);
        }
        static Bounds LocalMeshBounds(Transform root)
        {
            Bounds result=new Bounds();bool found=false;
            foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;Bounds b=filter.sharedMesh.bounds;
                Matrix4x4 toRoot=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 point=b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    point=toRoot.MultiplyPoint3x4(point);if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);
                }
            }
            if(!found)throw new InvalidOperationException("No imported mesh renderers");return result;
        }
    }
}
