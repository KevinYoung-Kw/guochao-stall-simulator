using System;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Guochao
{
    // Generated shopper and staff sources; the retiree is registered for later scene use.
    public static class CharacterNpcV15Assets
    {
        public static readonly ReadOnlyCollection<CharacterArchetypeAsset> All=Array.AsReadOnly(new[]{
            new CharacterArchetypeAsset("customer_student_v15","女大学生","Models/CustomerStudentV15","Models/Textures/CustomerStudentV15_Albedo",
                "CustomerOrigin","CustomerForward","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json",8400686,8404221,8405444,
                1.580000f,new Vector3(1.549419f,1.580000f,0.492045f),CharacterRole.Customer,
                "Independent Aholo G1 character with original Idle/Walk samples; baked clothing, other actions missing. Full-frame FBX footprint measured; Unity scene/ground-contact acceptance remains pending.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerStudentV15","Idle_Loop",2.500000f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerStudentV15","Walk_Loop",1.333333f)),
            new CharacterArchetypeAsset("market_cashier_v15","惠民超市收银员","Models/MarketCashierV15","Models/Textures/MarketCashierV15_Albedo",
                "CustomerOrigin","CustomerForward","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json",8400695,8404235,8405453,
                1.62f,new Vector3(1.599278f,1.62f,.450077f),CharacterRole.MarketStaff,
                "Independent Aholo G1 staff model with real Idle/Walk clips; scanning/receipt gestures are runtime presentation, not authored clips. Modular outfits are not implemented.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/MarketCashierV15","Idle_Loop",2.5f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/MarketCashierV15","Walk_Loop",1.333333f)),
            new CharacterArchetypeAsset("customer_retiree_v20","退休老先生","Models/CustomerRetireeV20","Models/Textures/CustomerRetireeV20_Albedo",
                "CustomerOrigin","CustomerForward","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json","Assets/Resources/Models/GeneratedProvenance/recharge-batch.json",8400690,8404230,8405448,
                1.62f,new Vector3(1.618091f,1.62f,.461082f),CharacterRole.Customer,
                "Aholo-generated independent character with idle and walking; other activities remain to be authored.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerRetireeV20","Idle_Loop",2.5f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerRetireeV20","Walk_Loop",1.333333f))
        });
    }
}
