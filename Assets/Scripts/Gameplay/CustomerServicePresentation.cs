using UnityEngine;

namespace Guochao
{
    // Presentation-only overlay for a customer whose order was just served.
    // A short procedural reach toward the vendor, a visible meal package that
    // ends up in the hand, and a nod (or a head shake when the portion was
    // short on seasoning) play before the guest is allowed to depart.
    //
    // This component never moves the body root, never touches navigation or
    // collision, and does not author or impersonate a real Pay/Receive
    // animation clip: every pose is an additive LateUpdate overlay on the
    // visual bones that is fully undone when the gesture ends, when the guest
    // is hidden, or when this object is destroyed. It coexists with the
    // Legacy Idle/Walk animation, which keeps writing the base pose every
    // frame while playing.
    public sealed class CustomerServicePresentation : MonoBehaviour
    {
        private const float ReachSeconds=.34f,GestureSeconds=.52f,TotalSeconds=ReachSeconds+GestureSeconds;
        private Vector3 handoverPoint;
        private Transform vendor,rightUpper,rightLower,rightEnd,head;
        private readonly Transform[] bones=new Transform[4];
        private readonly Quaternion[] preOverlay=new Quaternion[4];
        private readonly bool[] overlayApplied=new bool[4];
        private float elapsed=-1f;
        private bool shortSeasoning;
        private Transform bag,handBone;
        private Material bagMaterial,bandMaterial;
        private bool bagInHand;
        public bool ExternalReceive {get;set;}
        public bool ExternalReceiveBusy {get;set;}
        public bool CanDepart=>!ExternalReceiveBusy&&(elapsed<0f||elapsed>=TotalSeconds);
        public float ReceiveElapsed=>Mathf.Max(0,elapsed);
        public bool HasReceivedPackage=>bag!=null;
        public bool HasArmRig=>rightUpper!=null&&rightLower!=null&&rightEnd!=null;
        private bool GestureActive=>elapsed>=0f&&elapsed<TotalSeconds;

        public void Bind(Animation guestAnimation,Transform vendorTransform,Material paper,Material band)
        {
            vendor=vendorTransform;bagMaterial=paper;bandMaterial=band;
            rightUpper=FindBone("RightArm");rightLower=FindBone("RightForeArm");rightEnd=FindBone("RightHand");head=FindBone("Head");
            bones[0]=rightUpper;bones[1]=rightLower;bones[2]=null;bones[3]=head;
        }
        public void BeginReceive(bool wasShortOnSeasoning)
        {
            if(ExternalReceive||GestureActive||bag!=null)return; // one actual package per served order
            shortSeasoning=wasShortOnSeasoning;elapsed=0f;bagInHand=false;
            Vector3 towardVendor=vendor!=null?vendor.position-transform.position:transform.forward;towardVendor.y=0;
            if(towardVendor.sqrMagnitude<.001f)towardVendor=transform.forward;towardVendor.Normalize();
            // The exchange is on the customer side of the counter, within arm reach.
            // The package must never fly across the stall from the distant vendor root.
            float handHeight=rightEnd!=null?Mathf.Clamp(rightEnd.position.y-transform.position.y,.85f,1.15f):1.02f;
            handoverPoint=transform.position+towardVendor*.38f+Vector3.Cross(Vector3.up,towardVendor)*.16f+Vector3.up*handHeight;
            CreateBag();
        }
        // RISK7SCENE 读档恢复：直接呈现“拿餐之后”的静态状态（餐包已在手上），
        // 不重播伸手/点头动作、不驱动任何计时；后续 SaleCompleted 重放因
        // bag!=null 被 BeginReceive 的一次性守卫挡住，不会二次接餐。
        // 骨骼/材质缺失导致无法挂包时返回 false（表现层能力边界，不伪造）。
        public bool RestoreStaticReceive()
        {
            if(ExternalReceive)return true;
            if(bag!=null)return true; // 已有餐包即为已恢复
            if(rightEnd==null||bagMaterial==null)return false;
            elapsed=TotalSeconds;shortSeasoning=false;bagInHand=false;
            handoverPoint=rightEnd.position;
            CreateBag();
            if(bag==null)return false;
            handBone=rightEnd;bag.SetParent(handBone,true);bagInHand=true;UpdateBag();
            return true;
        }
        public void Tick(float deltaTime)
        {
            if(elapsed<0f)return;
            if(deltaTime>0f)elapsed=Mathf.Min(elapsed+deltaTime,TotalSeconds);
        }
        private Transform FindBone(string suffix)
        {
            foreach(Transform node in GetComponentsInChildren<Transform>(true))
            {string name=node.name;int colon=name.LastIndexOf(':');if(colon>=0)name=name.Substring(colon+1);if(name==suffix||name=="mixamorig"+suffix)return node;}
            return null;
        }
        private void CreateBag()
        {
            if(bagMaterial==null)return;
            Vector3 at=ReachPoint(1f); // starts at the vendor-side handover point
            bag=new GameObject("Received meal package").transform;bag.SetParent(transform,false);bag.position=at;
            Primitive(bag,"Paper meal bag",Vector3.zero,new Vector3(.20f,.24f,.13f),bagMaterial);
            Primitive(bag,"Folded bag top",new Vector3(0,.135f,0),new Vector3(.21f,.035f,.14f),bandMaterial!=null?bandMaterial:bagMaterial);
            int layer=rightEnd!=null?rightEnd.gameObject.layer:29;foreach(Transform node in bag.GetComponentsInChildren<Transform>(true))node.gameObject.layer=layer;
        }
        private GameObject Primitive(Transform parent,string name,Vector3 localPosition,Vector3 scale,Material material)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=localPosition;go.transform.localScale=scale;
            Collider collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        private Vector3 ReachPoint(float weight)
        {
            Vector3 rest=rightEnd!=null?rightEnd.position:transform.position+transform.forward*.25f+Vector3.up;
            return Vector3.Lerp(rest,handoverPoint,Mathf.Clamp01(weight));
        }
        // Remove our previous overlay before Unity samples the next Legacy pose.
        // Restoring it in LateUpdate would overwrite that freshly sampled pose.
        private void Update(){UndoOverlays();}
        private void LateUpdate()
        {
            if(!GestureActive){UndoOverlays();if(elapsed>=0f)UpdateBag();return;} // also attach correctly after a long frame; the bag follows departure
            float reach=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/ReachSeconds));
            float retract=1f-Mathf.SmoothStep(0,1,Mathf.Clamp01((elapsed-ReachSeconds)/GestureSeconds));
            float armWeight=reach*retract;
            if(armWeight>.001f&&rightUpper!=null&&rightLower!=null&&rightEnd!=null)PoseArm(ReachPoint(armWeight),Vector3.down,armWeight);
            if(head!=null)
            {
                float g=Mathf.Clamp01((elapsed-ReachSeconds)/GestureSeconds);
                if(g>0f)
                {
                    if(shortSeasoning)
                    {
                        float yaw=Mathf.Sin(g*Mathf.PI*2f)*16f*Mathf.Sin(g*Mathf.PI);
                        ApplyOverlay(3,head,Quaternion.Inverse(head.parent.rotation)*(Quaternion.AngleAxis(yaw,transform.up)*head.parent.rotation*head.localRotation));
                    }
                    else
                    {
                        float pitch=Mathf.Sin(g*Mathf.PI*2f)*9f*Mathf.Sin(g*Mathf.PI);
                        ApplyOverlay(3,head,Quaternion.Inverse(head.parent.rotation)*(Quaternion.AngleAxis(pitch,transform.right)*head.parent.rotation*head.localRotation));
                    }
                }
            }
            UpdateBag();
        }
        // Two-bone IK in world space, same contract as the travel-stage rider
        // pose: it only reorients visual bones, robust to rig axis naming.
        private void PoseArm(Vector3 target,Vector3 bend,float weight)
        {
            Transform lower=rightLower,end=rightEnd,upper=rightUpper;
            Vector3 endPoint=Vector3.Lerp(end.position,target,weight);
            float l1=Vector3.Distance(upper.position,lower.position),l2=Vector3.Distance(lower.position,end.position);
            if(l1<.001f||l2<.001f)return;
            Vector3 delta=endPoint-upper.position;if(delta.sqrMagnitude<.000001f)return;float d=Mathf.Clamp(delta.magnitude,.001f,l1+l2-.001f);Vector3 axis=delta.normalized;
            Vector3 side=Vector3.ProjectOnPlane(bend,axis);side=side.sqrMagnitude<.0001f?Vector3.ProjectOnPlane(Vector3.back,axis):side;side.Normalize();
            float along=(l1*l1+d*d-l2*l2)/(2*d);float height=Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            Vector3 elbow=upper.position+axis*along+side*height;
            Quaternion upperWorld=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            ApplyOverlayWorld(0,upper,upperWorld);
            Quaternion lowerWorld=Quaternion.FromToRotation(end.position-lower.position,endPoint-lower.position)*lower.rotation;
            ApplyOverlayWorld(1,lower,lowerWorld);
        }
        private void ApplyOverlayWorld(int index,Transform bone,Quaternion worldRotation)
        {if(bone==null)return;ApplyOverlay(index,bone,Quaternion.Inverse(bone.parent.rotation)*worldRotation);}
        private void ApplyOverlay(int index,Transform bone,Quaternion localRotation)
        {
            if(bone==null)return;
            if(overlayApplied[index])bone.localRotation=preOverlay[index];
            preOverlay[index]=bone.localRotation;bone.localRotation=localRotation;overlayApplied[index]=true;
        }
        private void UndoOverlays()
        {
            for(int i=0;i<bones.Length;i++)
                if(bones[i]!=null&&overlayApplied[i]){bones[i].localRotation=preOverlay[i];overlayApplied[i]=false;}
        }
        private void UpdateBag()
        {
            if(bag==null)return;
            if(!bagInHand)
            {
                if(elapsed>=ReachSeconds&&rightEnd!=null)
                {
                    handBone=rightEnd;bag.SetParent(handBone,true);bagInHand=true;
                }
                else if(rightEnd!=null)bag.position=Vector3.Lerp(ReachPoint(1f),rightEnd.position,Mathf.Clamp01(elapsed/ReachSeconds));
            }
            if(bagInHand&&handBone!=null)
            {bag.position=handBone.position+Vector3.down*.12f;bag.rotation=Quaternion.LookRotation(transform.forward,Vector3.up);}
            // After the gesture the wrist-attached package simply rides the
            // Legacy Walk clip out of the scene; nothing else to drive.
        }
        private void RestorePose()
        {
            UndoOverlays();
            if(bag!=null){Destroy(bag.gameObject);bag=null;handBone=null;bagInHand=false;}
        }
        private void OnDisable(){RestorePose();}
        private void OnDestroy(){RestorePose();}
    }
}
