using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Original Aholo mesh, retopologized and texture-baked for real-time use.
    public static class GeneratedEquipmentAssets
    {
        static readonly Dictionary<string,Material> materialsByAsset=new Dictionary<string,Material>();
        public static bool TryPopulate(string id,Transform parent)
        {
            string asset=id=="S01"?"SauceChiliAI":id=="S02"?"SauceGarlicAI":id=="B01"?"StoveB01AI":id=="B05"?"InductionB05AI":id=="V13"?"FryerV13AI":id=="V09"?"SoupV09AI":id=="V15"?"IceboxV15AI":id=="S05"?"SauceSweetAI":id=="U02"?"SpatulaU02AI":id=="RiceBucket"?"RiceBucketAI":id=="V02"?"WokV02AI":null;
            if(asset==null)return false;
            float targetHeight=id=="B01"?.45f:id=="B05"?.20f:id=="V13"?.24f:id=="V09"?.40f:id=="V15"?.37f:id=="U02"?.42f:id=="RiceBucket"?.47f:id=="V02"?.24f:.115f;
            var prefab=Resources.Load<GameObject>("Models/"+asset);
            var texture=Resources.Load<Texture2D>("Models/Textures/"+asset+"_Albedo");
            if(!prefab||!texture)return false;
            var slot=new GameObject(id+"_"+asset+"_Aholo");slot.transform.SetParent(parent,false);
            try
            {
                GameObject model=UnityEngine.Object.Instantiate(prefab,slot.transform,false);
                if(id=="U02")model.transform.localRotation=Quaternion.Euler(0,0,-90);
                Bounds bounds=LocalBounds(slot.transform);
                if(bounds.size.y<.0001f)throw new InvalidOperationException("Empty generated mesh");
                float scale=targetHeight/bounds.size.y;
                slot.transform.localScale=Vector3.one*scale;
                slot.transform.localPosition=-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
                if(!materialsByAsset.TryGetValue(asset,out Material generatedMaterial)||!generatedMaterial)
                {
                    generatedMaterial=new Material(Shader.Find("Standard")){name=asset+"_Albedo",color=Color.white,mainTexture=texture};
                    generatedMaterial.SetFloat("_Glossiness",.18f);generatedMaterial.SetFloat("_Metallic",0);materialsByAsset[asset]=generatedMaterial;
                }
                foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)materials[i]=generatedMaterial;
                    renderer.sharedMaterials=materials;
                }
                GameArtRendering.Apply(model,id=="B01"||id=="B05");
                return true;
            }
            catch(Exception error)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(slot);else UnityEngine.Object.DestroyImmediate(slot);
                Debug.LogWarning("Generated "+id+" mesh unavailable: "+error.Message);return false;
            }
        }
        static Bounds LocalBounds(Transform root)
        {
            Bounds result=new Bounds();bool found=false;
            foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh)continue;Bounds mesh=filter.sharedMesh.bounds;
                Matrix4x4 toRoot=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=mesh.center+Vector3.Scale(mesh.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    p=toRoot.MultiplyPoint3x4(p);if(!found){result=new Bounds(p,Vector3.zero);found=true;}else result.Encapsulate(p);
                }
            }
            if(!found)throw new InvalidOperationException("No imported mesh");return result;
        }
    }
}
