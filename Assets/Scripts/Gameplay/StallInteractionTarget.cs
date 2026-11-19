using UnityEngine;

namespace Guochao
{
    // 旧五项数值保持不变；Serving=打包入出餐台的实物入口，CounterMeal=台面上
    // 一份已装盒成品（target.itemId 保存真实 meal id）。
    public enum StallInteractionKind { Order, Ingredient, Cookware, Seasoning, Packaging, Serving, CounterMeal }

    // Attached to a real scene object, above its hit collider. Presentation and
    // pointer input share this address; neither owns inventory or cooking state.
    public sealed class StallInteractionTarget : MonoBehaviour
    {
        public StallInteractionKind kind;
        public int lane=-1,orderId=-1;
        public string itemId="";
        public Transform anchor;
        public Vector3 WorldAnchor=>anchor!=null?anchor.position:transform.position;
        // Screen-space assist radius (px at the 1600x900 reference) for pointer
        // fallback picking near this object's visible body. <=0 uses the kind
        // default in StallInteractionInput. It never enlarges the collider.
        public float screenTolerance=-1f;
        public Collider HitCollider { get; private set; }
        public void BindHitCollider(Collider collider){HitCollider=collider;}
    }
}
