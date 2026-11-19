using System;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Guochao
{
    // Independent shopper and staff sources; the failed retiree rig is not registered.
    public static class CharacterNpcV15Assets
    {
        public static readonly ReadOnlyCollection<CharacterArchetypeAsset> All=Array.AsReadOnly(new[]{
            new CharacterArchetypeAsset("customer_student_v15","女大学生","Models/CustomerStudentV15","Models/Textures/CustomerStudentV15_Albedo",
                "CustomerOrigin","CustomerForward","art-v15/assets/student/provenance.json","art-v15/assets/student/model-inspection.json",8351834,8352625,8352946,
                1.580000f,new Vector3(1.555252f,1.580000f,0.493392f),CharacterRole.Customer,
                "Independent Aholo G1 character with original Idle/Walk samples; baked clothing, other actions missing. Full-frame FBX footprint measured; Unity scene/ground-contact acceptance remains pending.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/CustomerStudentV15","Idle_Loop",2.500000f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/CustomerStudentV15","Walk_Loop",1.333333f)),
            new CharacterArchetypeAsset("market_cashier_v15","惠民超市收银员","Models/MarketCashierV15","Models/Textures/MarketCashierV15_Albedo",
                "CustomerOrigin","CustomerForward","art-v15/assets/cashier/provenance.json","art-v15/assets/cashier/model-inspection.json",8355395,8356441,8356706,
                1.62f,new Vector3(1.583639f,1.62f,.446108f),CharacterRole.MarketStaff,
                "Independent Aholo G1 staff model with real Idle/Walk clips; scanning/receipt gestures are runtime presentation, not authored clips. Modular outfits are not implemented.",
                new CharacterClipAsset(CharacterMotion.Idle,"Models/MarketCashierV15","Idle_Loop",2.5f),
                new CharacterClipAsset(CharacterMotion.Walk,"Models/MarketCashierV15","Walk_Loop",1.333333f))
        });
    }
}
