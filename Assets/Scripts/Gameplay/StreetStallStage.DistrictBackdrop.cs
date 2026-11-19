using System;
using UnityEngine;

namespace Guochao
{
    public sealed partial class StreetStallStage
    {
        // The decorative backdrop and planter meshes are deliberately separate
        // from the existing solid/query colliders and guest/interaction anchors.
        private readonly Transform[] originalShopVisuals=new Transform[3];
        private readonly Transform[] originalPlanterVisuals=new Transform[2];
        private Transform districtBackdrop,generatedPlanters;
        private Material districtPaving,districtSeam;
        private Light districtKeyLight;
        private int visualDistrict=-1;
        private static readonly Color[] DistrictGround={new Color(.53f,.51f,.50f),new Color(.49f,.49f,.47f),new Color(.60f,.52f,.48f),new Color(.49f,.51f,.54f),new Color(.49f,.44f,.43f),new Color(.61f,.53f,.47f)};
        private static readonly string[] DistrictSceneModels={null,"DistrictConstructionAI","DistrictSchoolAI","DistrictCbdAI","DistrictNightmarketAI","DistrictHeritageAI"};

        private void RefreshDistrictEnvironment(int index)
        {
            index=Mathf.Clamp(index,0,DistrictSceneModels.Length-1);
            if(visualDistrict==index)return;
            visualDistrict=index;
            if(districtPaving)districtPaving.color=DistrictGround[index];
            if(districtSeam)districtSeam.color=DistrictGround[index]*.74f;
            if(districtKeyLight){districtKeyLight.color=index==4?new Color(1f,.79f,.62f):index==3?new Color(.96f,.97f,1f):new Color(1f,.93f,.83f);}
            if(districtBackdrop){districtBackdrop.gameObject.SetActive(false);Destroy(districtBackdrop.gameObject);districtBackdrop=null;}
            foreach(var original in originalShopVisuals)if(original)original.gameObject.SetActive(true);

            var candidate=Group("District backdrop "+index,transform);
            bool ready=false;
            if(index==0)
            {
                ready=StreetShopfrontAssets.TryCreateShopfront(candidate,new Vector3(0,0,5.50f),4.2f,1.95f,4.6f,0f,owned,out GameObject shop);
                if(ready&&originalShopVisuals[1])originalShopVisuals[1].gameObject.SetActive(false);
            }
            else
            {
                string asset=DistrictSceneModels[index];
                // Office and market frontages are modular bays; the gates and
                // arch are a single coherent landmark spanning the backdrop.
                int count=index==3||index==4?3:1;ready=true;
                for(int i=0;i<count;i++)
                {
                    float center=(operatingLeft+operatingRight)*.5f;
                    Vector3 at=new Vector3(center+(count==1?0:(i-1)*3.3f),0,count==1?5.0f:5.50f);
                    Vector3 fit=count==1?new Vector3(8.0f,3.15f,1.20f):new Vector3(3.25f,3.5f,1.95f);
                    if(!TryPlaceDistrictModel(asset,candidate,at,fit,index==3||index==4?180f:0f)){ready=false;break;}
                }
                if(ready)foreach(var original in originalShopVisuals)if(original)original.gameObject.SetActive(false);
            }
            if(ready){districtBackdrop=candidate;SetLayers(candidate);}
            else{candidate.gameObject.SetActive(false);Destroy(candidate.gameObject);}
            BuildPlanterModels();
        }
        private void BuildPlanterModels()
        {
            if(generatedPlanters)return;
            var candidate=Group("Generated street flowerbeds",transform);bool ready=true;
            for(int i=0;i<2;i++)if(!TryPlaceDistrictModel("DistrictFlowerbedAI",candidate,new Vector3(i==0?-5.5f:5.5f,0,i==0?1.6f:-2.3f),new Vector3(.80f,.95f,.70f),0)){ready=false;break;}
            if(!ready){candidate.gameObject.SetActive(false);Destroy(candidate.gameObject);return;}
            generatedPlanters=candidate;SetLayers(candidate);
            foreach(var original in originalPlanterVisuals)if(original)original.gameObject.SetActive(false);
        }
        private bool TryPlaceDistrictModel(string asset,Transform parent,Vector3 center,Vector3 limit,float yaw)
        {
            var prefab=Resources.Load<GameObject>("Models/"+asset);
            var texture=Resources.Load<Texture2D>("Models/Textures/"+asset+"_Albedo");
            if(!prefab||!texture)return false;
            Transform wrapper=Group(asset,parent);
            try
            {
                GameObject instance=Instantiate(prefab,wrapper,false);wrapper.localRotation=Quaternion.Euler(0,yaw,0);
                foreach(var collider in instance.GetComponentsInChildren<Collider>(true))collider.enabled=false;
                foreach(var animator in instance.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                Bounds b=DistrictMeshBounds(parent,instance.transform);
                if(b.size.x<.00001f||b.size.y<.00001f||b.size.z<.00001f)throw new InvalidOperationException("Empty district mesh: "+asset);
                float scale=Mathf.Min(limit.x/b.size.x,limit.y/b.size.y,limit.z/b.size.z);
                if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<=0)throw new InvalidOperationException("Invalid district scale: "+asset);
                wrapper.localScale=Vector3.one*scale;
                wrapper.localPosition=new Vector3(center.x-b.center.x*scale,center.y-b.min.y*scale,center.z-b.center.z*scale);
                var mat=Paint(asset+"_Albedo",Color.white,.18f);mat.mainTexture=texture;
                foreach(var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {var slots=renderer.sharedMaterials;if(slots.Length==0)slots=new Material[1];for(int i=0;i<slots.Length;i++)slots[i]=mat;renderer.sharedMaterials=slots;}
                return true;
            }
            catch(Exception error)
            {
                Debug.LogWarning("District model rejected "+asset+": "+error.Message);
                wrapper.gameObject.SetActive(false);Destroy(wrapper.gameObject);return false;
            }
        }
        private static Bounds DistrictMeshBounds(Transform parent,Transform instance)
        {
            Bounds result=new Bounds();bool found=false;
            foreach(var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!filter.sharedMesh)continue;Bounds b=filter.sharedMesh.bounds;
                Matrix4x4 m=parent.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 q=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));q=m.MultiplyPoint3x4(q);
                    if(!found){result=new Bounds(q,Vector3.zero);found=true;}else result.Encapsulate(q);
                }
            }
            if(!found)throw new InvalidOperationException("No district mesh filters");return result;
        }
    }
}
