using System;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Guochao
{
    // Generated only from actual inspected FBX + successful provider provenance.
    public static class CharacterNpcV11Assets
    {
        public static readonly ReadOnlyCollection<CharacterArchetypeAsset> All=Array.AsReadOnly(new[]{
            new CharacterArchetypeAsset("customer_courier_v11","黄色骑手","Models/CustomerCourier","Models/Textures/CustomerCourier_Albedo",
                "CustomerOrigin","CustomerForward","art-v11/npcs/courier/provenance.json","art-v11/npcs/courier/model-inspection.json",8268233,8268870,8269149,
                1.580000f,new Vector3(1.089259f,1.580000f,0.490596f),CharacterRole.Customer,
                "Independent Aholo G1 character; clothing/accessories baked into original design. Actual idle/walk, other actions still missing; Unity scene acceptance tracked separately.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerCourier","Idle_Loop",2.500000f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerCourier","Walk_Loop",1.333333f)),
            new CharacterArchetypeAsset("customer_worker_v11","壮实体型工人","Models/CustomerWorker","Models/Textures/CustomerWorker_Albedo",
                "CustomerOrigin","CustomerForward","art-v11/npcs/worker-tpose/provenance.json","art-v11/npcs/worker-tpose/model-inspection.json",8269270,8269700,8269995,
                1.720000f,new Vector3(1.958009f,1.720000f,0.472187f),CharacterRole.Customer,
                "Independent Aholo G1 character; clothing/accessories baked into original design. Actual idle/walk, other actions still missing; Unity scene acceptance tracked separately.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerWorker","Idle_Loop",2.500000f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerWorker","Walk_Loop",1.333333f)),
            new CharacterArchetypeAsset("customer_auntie_v11","卷发菜篮阿姨","Models/CustomerAuntie","Models/Textures/CustomerAuntie_Albedo",
                "CustomerOrigin","CustomerForward","art-v11/npcs/auntie/provenance.json","art-v11/npcs/auntie/model-inspection.json",8268253,8268934,8269322,
                1.550000f,new Vector3(1.135359f,1.550000f,0.524453f),CharacterRole.Customer,
                "Independent Aholo G1 character; clothing/accessories baked into original design. Actual idle/walk, other actions still missing; Basket auto-skinning visibly collapses in walk; correction candidates rejected, original provider rig preserved. Unity scene acceptance tracked separately.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerAuntie","Idle_Loop",2.500000f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerAuntie","Walk_Loop",1.333333f))
        });
    }
}
