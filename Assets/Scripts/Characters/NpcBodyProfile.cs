using System;
namespace Guochao
{
    public readonly struct NpcBodyProfile
    {
        public readonly float Radius,Height;
        public NpcBodyProfile(float radius,float height){Radius=radius;Height=Math.Max(height,radius*2);}
        // Maximum horizontal distance of EVERY evaluated mesh vertex in every
        // idle/walk frame, plus .025m, rounded up. Not a guessed torso-only radius.
        // Evidence: art-v12/navigation/character-collision-measurements.json.
        public static NpcBodyProfile ForArchetype(string id)
        {
            switch(id)
            {
                case "customer_jade_v9":return new NpcBodyProfile(.53f,1.67f);
                case "customer_courier_v11":return new NpcBodyProfile(.51f,1.62f);
                case "customer_worker_v11":return new NpcBodyProfile(.58f,1.79f);
                case "customer_auntie_v11":return new NpcBodyProfile(.78f,1.62f);
                // Full-frame FBX measurement: art-v15/assets/student/collision-measurements.json.
                case "customer_student_v15":return new NpcBodyProfile(0.530000f,1.640000f);
                // Staff is stationary; this measured envelope is available to future navigation.
                case "market_cashier_v15":return new NpcBodyProfile(.52f,1.67f);
                default:throw new ArgumentException("NPC collision profile is not measured: "+id);
            }
        }
    }
}
