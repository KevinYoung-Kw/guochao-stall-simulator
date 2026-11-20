using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Each HUD owns its cache. Missing painted icons are photographed from the
    // same runtime objects used by the shop/loadout; there are no text-square stand-ins.
    public sealed class GameItemIconLibrary : MonoBehaviour
    {
        private readonly Dictionary<string,Sprite> cache=new Dictionary<string,Sprite>();
        private readonly List<Texture2D> generated=new List<Texture2D>();
        public Sprite Get(string id)
        {
            string key=SupplyCatalog.IngredientId(id);
            if(cache.TryGetValue(key,out Sprite sprite))return sprite;
            Texture2D existing=Resources.Load<Texture2D>("UI/Icons/"+key);
            if(existing!=null)sprite=Sprite.Create(existing,new Rect(0,0,existing.width,existing.height),new Vector2(.5f,.5f),100);
            else sprite=Photograph(key);
            cache[key]=sprite;return sprite;
        }
        private Sprite Photograph(string id)
        {
            var booth=new GameObject("Receipt product photograph");booth.transform.position=new Vector3(10000,10000,10000);
            // FLAVOR11: S-prefixed flavor seasonings photograph the condiment-jar
            // equipment model (the generic ingredient crate would paint them as
            // sealed meal boxes). Each flavor then gets its own sauce tint so
            // every bottle reads as a different condiment at a glance.
            bool flavor=id.Length==3&&id[0]=='S';
            GameObject item=SupplyCatalog.IsEquipment(id)||id.StartsWith("P")||flavor?EquipmentVisualFactory.Create(id,booth.transform):EquipmentVisualFactory.Ingredient(id,booth.transform);
            if(flavor)
            {
                Color sauce=id=="S02"?new Color(.89f,.86f,.70f):id=="S05"?new Color(.78f,.26f,.07f):id=="S13"?new Color(.42f,.20f,.08f):id=="S14"?new Color(.95f,.97f,.98f):new Color(.55f,.11f,.05f);
                foreach(Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                    if(renderer.name=="CondimentJar")
                    {Material tinted=new Material(renderer.sharedMaterial){color=sauce};renderer.sharedMaterial=tinted;}
            }
            foreach(Transform t in booth.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            foreach(Collider c in booth.GetComponentsInChildren<Collider>())c.enabled=false;
            Renderer[] renderers=item.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0){Destroy(booth);return null;}
            Bounds bounds=renderers[0].bounds;foreach(Renderer renderer in renderers)bounds.Encapsulate(renderer.bounds);
            var cameraObject=new GameObject("Receipt icon camera");Camera camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
            float extent=Mathf.Max(.08f,bounds.extents.magnitude);camera.orthographicSize=extent*1.10f;
            camera.transform.position=bounds.center+new Vector3(-.7f,.8f,-1f).normalized*extent*4;camera.transform.LookAt(bounds.center);
            camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.nearClipPlane=.01f;camera.farClipPlane=extent*8;camera.allowHDR=false;
            var lightObject=new GameObject("Receipt photograph softbox");Light light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.94f,.82f);light.intensity=1.05f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(38,-28,0);
            var target=new RenderTexture(160,160,24,RenderTextureFormat.ARGB32){antiAliasing=4};RenderTexture previous=RenderTexture.active;
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var texture=new Texture2D(160,160,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,160,160),0,0);texture.Apply();generated.Add(texture);
            RenderTexture.active=previous;camera.targetTexture=null;target.Release();Destroy(target);booth.SetActive(false);light.enabled=false;Destroy(cameraObject);Destroy(lightObject);Destroy(booth);
            return Sprite.Create(texture,new Rect(0,0,160,160),new Vector2(.5f,.5f),100);
        }
        private void OnDestroy(){foreach(Sprite sprite in cache.Values)if(sprite)Destroy(sprite);foreach(Texture2D texture in generated)if(texture)Destroy(texture);}
    }
}
