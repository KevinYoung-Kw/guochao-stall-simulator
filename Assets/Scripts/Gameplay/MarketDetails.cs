using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Precise authored shop fixtures complement the separately generated Aholo
    // fruit display. Renderer replacement preserves checkout collision/anchors.
    public sealed class MarketDetails : MonoBehaviour
    {
        public int CheckoutCount { get; private set; }
        public int ReplacedRendererCount { get; private set; }
        private static readonly string[] OldParts={"10_台面","11_台面","12_台面","10_扫码机","11_扫码机","12_扫码机","结账传送带","传送辊","扫码台面","收银屏立杆","收银显示框","收银显示面","收银屏字"};
        public static MarketDetails Install(WorldRuntime world)
        {
            MarketStaffPresentation.Install(world);
            var existing=world.GetComponent<MarketDetails>();if(existing!=null)return existing;
            var prefab=Resources.Load<GameObject>("Models/MarketCheckoutDetails");
            if(prefab==null){Debug.LogWarning("MarketDetails: checkout details missing; original counters retained.");return null;}
            var original=new List<Renderer>();
            foreach(Renderer renderer in world.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)||renderer.name.StartsWith("Collider_",StringComparison.Ordinal))continue;
                foreach(string name in OldParts)
                    if(renderer.name==name||renderer.name.StartsWith(name+".",StringComparison.Ordinal)){original.Add(renderer);break;}
            }
            if(original.Count==0){Debug.LogWarning("MarketDetails: original checkout group absent; no duplicate created.");return null;}
            var detail=world.gameObject.AddComponent<MarketDetails>();
            var wrapper=new GameObject("MarketCheckoutDetails_Runtime");wrapper.transform.SetParent(world.transform,false);
            GameObject model=Instantiate(prefab,wrapper.transform,false);model.name="MarketCheckoutDetails_Imported";
            GameArtRendering.Apply(model,false);
            foreach(Renderer renderer in original)renderer.enabled=false;
            detail.CheckoutCount=3;detail.ReplacedRendererCount=original.Count;
            Debug.Log("MARKET_CHECKOUT_DETAILS_READY lanes=3 replaced_renderers="+original.Count+" original_colliders_and_cashier_anchor_retained");
            return detail;
        }
    }
}
