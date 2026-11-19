using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Explicitly authored runtime attachments. They are not part of the Aholo
    // body asset; installation never edits or rescales that original mesh.
    public sealed class VehicleContainerModules : MonoBehaviour
    {
        private Material steel,paperTray;
        private Transform moduleRoot;
        public static VehicleContainerModules Install(Transform vehicle)
        {
            var existing=vehicle.GetComponent<VehicleContainerModules>();if(existing!=null)return existing;
            var module=vehicle.gameObject.AddComponent<VehicleContainerModules>();module.Build(vehicle);return module;
        }
        public static void ContributeGeometry(VehiclePackingGeometry geometry)
        {
            // Coordinates are in the shared upright frame: x across the deck,
            // z toward the rear basket, y relative to the actual CartDeck plane.
            for(int i=0;i<6;i++)
            {
                float x=-.625f+i*.25f;
                geometry.AddZone(new PackingZone("tool_rack_"+i,"tool_rack",BoundsOf(x-.12f,x+.12f,.60f,1.12f,.605f,.735f),PackingTag.Tool),ContainerSpec.ToolRack());
                geometry.AddZone(new PackingZone("seasoning_rack_"+i,"seasoning_rack",BoundsOf(x-.12f,x+.12f,.035f,.56f,.605f,.735f),PackingTag.Seasoning),ContainerSpec.Seasoning());
            }
            float[] edges={-.52f,-.173f,.173f,.52f};
            for(int i=0;i<3;i++)geometry.AddZone(new PackingZone("packaging_"+i,"packaging",BoundsOf(.445f,.795f,.035f,.34f,edges[i]+.014f,edges[i+1]-.014f),PackingTag.Packaging),ContainerSpec.Packaging());
            var locks=new ContainerSpec("lock_slots","车体锁槽",2,1,PackingTag.Lock,"内容库E10：2把锁；外缘独立挂点");
            geometry.AddZone(new PackingZone("lock_slots","lock_slots",BoundsOf(-.735f,-.445f,-.22f,.30f,-.50f,-.10f),PackingTag.Lock),locks);
            var umbrella=new ContainerSpec("umbrella_slot","车侧伞架",1,1,PackingTag.Umbrella,"内容库E15：车侧独立伞架；运输时折叠");
            geometry.AddZone(new PackingZone("umbrella_slot","umbrella_slot",BoundsOf(-.735f,-.445f,-.10f,1.20f,.18f,.48f),PackingTag.Umbrella),umbrella);
            foreach(Part part in Parts())if(part.solid)geometry.obstacles.Add(new Bounds(part.center,part.size));
        }
        private static Bounds BoundsOf(float minX,float maxX,float minY,float maxY,float minZ,float maxZ)
        {var bounds=new Bounds();bounds.SetMinMax(new Vector3(minX,minY,minZ),new Vector3(maxX,maxY,maxZ));return bounds;}
        private void Build(Transform vehicle)
        {
            VehiclePackingGeometry g=VehiclePackingGeometry.FromCartAI(vehicle);
            var root=new GameObject("Authored_ContainerModules");moduleRoot=root.transform;moduleRoot.SetParent(vehicle,false);
            steel=Material("Container module steel",CampaignUI.Ink,.35f);
            paperTray=Material("Container module enamel",CampaignUI.Pale,.08f);
            foreach(Part part in Parts())Box(g,part.name,part.center,part.size,part.enamel?paperTray:steel,part.solid);
            GameArtRendering.Apply(root,false);
        }
        private struct Part
        {
            public string name;public Vector3 center,size;public bool solid,enamel;
            public Part(string n,Vector3 c,Vector3 s,bool physical=true,bool pale=false){name=n;center=c;size=s;solid=physical;enamel=pale;}
        }
        private static List<Part> Parts()
        {
            var parts=new List<Part>();
            foreach(float x in new[]{-.78f,.78f})
            {
                parts.Add(new Part("ToolRack_Upright",new Vector3(x,.57f,.67f),new Vector3(.026f,1.16f,.026f)));
                parts.Add(new Part("Rack_ToDeckBracket",new Vector3(x*.5f,.012f,.585f),new Vector3(Mathf.Abs(x),.025f,.04f)));
            }
            parts.Add(new Part("ToolRack_TopBar",new Vector3(0,1.15f,.67f),new Vector3(1.59f,.026f,.026f)));
            for(int i=0;i<6;i++)parts.Add(new Part("ToolHook_"+i,new Vector3(-.625f+i*.25f,1.117f,.67f),new Vector3(.018f,.06f,.08f),false));
            for(int i=0;i<=6;i++)parts.Add(new Part("SeasoningDivider_"+i,new Vector3(-.75f+i*.25f,.09f,.67f),new Vector3(.008f,.12f,.145f)));
            parts.Add(new Part("SeasoningShelf",new Vector3(0,.02f,.67f),new Vector3(1.54f,.018f,.16f),true,true));
            parts.Add(new Part("SeasoningBackRail",new Vector3(0,.16f,.75f),new Vector3(1.54f,.025f,.015f)));
            parts.Add(new Part("SeasoningFrontLip",new Vector3(0,.05f,.59f),new Vector3(1.54f,.055f,.015f)));
            parts.Add(new Part("PackagingTrayFloor",new Vector3(.62f,.02f,0),new Vector3(.38f,.02f,1.04f),true,true));
            foreach(float z in new[]{-.52f,-.173f,.173f,.52f})parts.Add(new Part("PackagingTrayDivider",new Vector3(.62f,.09f,z),new Vector3(.38f,.14f,.012f)));
            parts.Add(new Part("PackagingOuterLip",new Vector3(.815f,.095f,0),new Vector3(.018f,.15f,1.04f)));
            for(int i=0;i<2;i++)parts.Add(new Part("LockHook_"+i,new Vector3(-.59f,.30f,-.40f+i*.20f),new Vector3(.30f,.018f,.04f),false));
            parts.Add(new Part("UmbrellaSupport",new Vector3(-.765f,.13f,.33f),new Vector3(.045f,.48f,.045f)));
            parts.Add(new Part("UmbrellaClamp",new Vector3(-.59f,.52f,.33f),new Vector3(.30f,.025f,.05f),false));
            return parts;
        }
        private void Box(VehiclePackingGeometry geometry,string name,Vector3 center,Vector3 size,Material material,bool solid)
        {
            GameObject box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.name=name;box.transform.SetParent(moduleRoot,false);
            box.transform.SetPositionAndRotation(geometry.ToWorld(center),geometry.rotation);box.transform.localScale=size;
            box.GetComponent<Renderer>().sharedMaterial=material;
            if(!solid){Collider collider=box.GetComponent<Collider>();if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);}
            else box.layer=WorldRuntime.EnvironmentLayer;
        }
        private static Material Material(string name,Color colour,float metal)
        {var material=new Material(Shader.Find("Standard"));material.name=name;material.color=colour;material.SetFloat("_Metallic",metal);material.SetFloat("_Glossiness",.18f);return material;}
        private void OnDestroy()
        {
            if(Application.isPlaying){if(steel)Destroy(steel);if(paperTray)Destroy(paperTray);}
            else{if(steel)DestroyImmediate(steel);if(paperTray)DestroyImmediate(paperTray);}
        }
    }
}
