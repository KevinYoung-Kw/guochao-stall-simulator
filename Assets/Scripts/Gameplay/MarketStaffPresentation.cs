using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Real Aholo staff model and authored Idle, with small procedural checkout
    // gestures. This component observes the transaction; it cannot charge money.
    [DefaultExecutionOrder(50)]
    public sealed class MarketStaffPresentation : MonoBehaviour
    {
        public const string ArchetypeId="market_cashier_v15";
        private const float ReceiptSeconds=1.05f;
        private WorldRuntime world;
        private RunState run;
        private Transform body,upper,lower,hand,head,receipt;
        private Animation idle;
        private readonly Transform[] bones=new Transform[3];
        private readonly Quaternion[] basePose=new Quaternion[3];
        private readonly bool[] applied=new bool[3];
        private readonly List<Renderer> originalRenderers=new List<Renderer>();
        private readonly List<bool> originalVisibility=new List<bool>();
        private Material sourceMaterial,receiptMaterial;
        private float scanTime,receiptAge=-1,gestureBlend;
        private bool paused,scanning;
        public bool IsReady=>body!=null&&idle!=null;
        public int PaymentPresentationCount { get; private set; }
        public int LastPaidCents { get; private set; }
        public bool IsPresentingReceipt=>receiptAge>=0&&receiptAge<ReceiptSeconds;
        public float ReceiptElapsed=>Mathf.Max(0,receiptAge);

        public static MarketStaffPresentation Install(WorldRuntime world)
        {
            if(world==null)return null;
            var existing=world.GetComponent<MarketStaffPresentation>();if(existing!=null)return existing;
            Transform old=Find(world.transform,"NPC_cashier");
            if(old==null){Debug.LogWarning("Market staff: authored cashier location missing; original scene retained.");return null;}
            if(!CharacterAssetCatalog.TryLoad(ArchetypeId,out var asset,out string failure,CharacterMotion.Idle))
            {Debug.LogWarning("Market staff: "+failure);return null;}
            Texture2D albedo=Resources.Load<Texture2D>(asset.Definition.AlbedoResourcePath);
            if(albedo==null){Debug.LogWarning("Market staff: original albedo missing; original cashier retained.");return null;}
            var presentation=world.gameObject.AddComponent<MarketStaffPresentation>();
            try{presentation.Build(world,old,asset,albedo);return presentation;}
            catch(Exception error){Debug.LogException(error);Destroy(presentation);return null;}
        }

        private void Build(WorldRuntime runtime,Transform original,LoadedCharacterAsset asset,Texture2D albedo)
        {
            world=runtime;
            body=new GameObject("MarketStaff_ActualCashierV15").transform;body.SetParent(world.transform,false);
            body.position=original.position;
            Vector3 facing=world.Anchor("CashierCounter")-body.position;facing.y=0;
            if(facing.sqrMagnitude<.001f){facing=world.Anchor("Cashier")-body.position;facing.y=0;}
            if(facing.sqrMagnitude<.001f)facing=Vector3.forward;
            body.rotation=Quaternion.LookRotation(facing.normalized,Vector3.up);
            GameObject model=Instantiate(asset.Prefab,body,false);model.name=ArchetypeId+"_Model";
            Transform origin=Find(model.transform,asset.Definition.OriginNode),forward=Find(model.transform,asset.Definition.ForwardNode);
            Vector3 authoredDirection=forward.position-origin.position;authoredDirection.y=0;
            if(authoredDirection.sqrMagnitude<.001f)throw new InvalidOperationException("Cashier forward marker has no horizontal direction");
            model.transform.rotation=Quaternion.FromToRotation(authoredDirection.normalized,body.forward)*model.transform.rotation;
            // Idle inspection has a minimum of -0.022 m. Preserve the full 1.62 m
            // model scale and lift the authored origin 0.025 m, never shrink it.
            model.transform.position+=body.position+Vector3.up*.025f-origin.position;
            foreach(Transform node in model.GetComponentsInChildren<Transform>(true))node.gameObject.layer=0;
            foreach(Collider collider in model.GetComponentsInChildren<Collider>(true)){collider.enabled=false;Destroy(collider);}
            foreach(Animator animator in model.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            sourceMaterial=new Material(Shader.Find("Standard")){name="CashierV15_OriginalAlbedo",color=Color.white,mainTexture=albedo};
            sourceMaterial.SetFloat("_Glossiness",.18f);
            int renderers=0;
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;
                if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
                Material[] materials=renderer.sharedMaterials;for(int n=0;n<materials.Length;n++)materials[n]=sourceMaterial;renderer.sharedMaterials=materials;renderers++;
            }
            if(renderers==0)throw new InvalidOperationException("Cashier model has no renderers");
            GameArtRendering.Apply(model,true);
            idle=model.GetComponentInChildren<Animation>();if(idle==null)idle=model.AddComponent<Animation>();
            idle.enabled=true;idle.AddClip(asset.Clips[CharacterMotion.Idle],"cashier_idle");idle["cashier_idle"].wrapMode=WrapMode.Loop;
            idle.Play("cashier_idle");idle.Sample();
            upper=Bone(model.transform,"RightArm");lower=Bone(model.transform,"RightForeArm");hand=Bone(model.transform,"RightHand");head=Bone(model.transform,"Head");
            bones[0]=upper;bones[1]=lower;bones[2]=head;
            CreateReceipt();
            // Preserve the old colliders, transforms and interaction anchors.
            // Hide only after the real model, albedo and Idle all succeeded.
            foreach(Renderer renderer in original.GetComponentsInChildren<Renderer>(true))
            {originalRenderers.Add(renderer);originalVisibility.Add(renderer.enabled);renderer.enabled=false;}
            Observe(GameFlow.Instance?.Run);
            Debug.Log("MARKET_STAFF_READY actual_asset="+ArchetypeId+" authored_idle=true procedural_checkout=true");
        }

        private void CreateReceipt()
        {
            GameObject paper=GameObject.CreatePrimitive(PrimitiveType.Cube);paper.name="Cashier paid receipt";
            Collider collider=paper.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            receipt=paper.transform;receipt.SetParent(body,false);receipt.localScale=new Vector3(.12f,.18f,.004f);
            receiptMaterial=new Material(Resources.Load<Shader>("Shaders/GuochaoToon")){name="CashierReceipt",color=new Color(1f,.91f,.72f)};
            paper.GetComponent<Renderer>().sharedMaterial=receiptMaterial;paper.SetActive(false);
        }
        private void Observe(RunState next)
        {
            if(ReferenceEquals(run,next))return;
            if(run!=null)run.CheckoutCompleted-=OnCheckoutCompleted;
            run=next;receiptAge=-1;scanTime=0;gestureBlend=0;
            if(receipt!=null)receipt.gameObject.SetActive(false);
            if(run!=null)run.CheckoutCompleted+=OnCheckoutCompleted;
        }
        private void OnCheckoutCompleted(int actualPaidCents)
        {
            if(run==null||run.Phase!=GamePhase.Market||!isActiveAndEnabled||!IsReady)return;
            LastPaidCents=actualPaidCents;PaymentPresentationCount++;receiptAge=0;
            if(receipt!=null)receipt.gameObject.SetActive(true);
        }
        private void Update()
        {
            UndoPose();
            Observe(GameFlow.Instance?.Run);
            if(!IsReady)return;
            paused=run==null||run.Paused||GameFlow.Instance.IsTransitioning;
            idle["cashier_idle"].speed=paused?0:1;
            if(paused)return;
            float dt=Mathf.Max(0,Time.deltaTime);
            scanning=run.Phase==GamePhase.Market&&run.CheckoutInProgress;
            if(scanning)scanTime+=dt;
            gestureBlend=Mathf.MoveTowards(gestureBlend,scanning?1:0,dt*5);
            if(IsPresentingReceipt){receiptAge=Mathf.Min(ReceiptSeconds,receiptAge+dt);if(!IsPresentingReceipt)receipt.gameObject.SetActive(false);}
        }
        private void LateUpdate()
        {
            if(!IsReady)return;
            float receive=IsPresentingReceipt?Mathf.Sin(Mathf.PI*receiptAge/ReceiptSeconds):0;
            float weight=Mathf.Max(gestureBlend*.75f,receive);
            if(weight>.001f&&upper!=null&&lower!=null&&hand!=null)
            {
                float sweep=IsPresentingReceipt?0:Mathf.Sin(scanTime*5.5f)*.10f;
                Vector3 target=body.position+Vector3.up*1.02f+body.forward*(.30f+.08f*receive)+body.right*(.15f+sweep);
                PoseArm(Vector3.Lerp(hand.position,target,weight));
            }
            if(head!=null&&receive>.001f)Apply(2,Quaternion.AngleAxis(Mathf.Sin(receiptAge/ReceiptSeconds*Mathf.PI*2)*6,body.right)*head.rotation);
            if(receipt!=null&&IsPresentingReceipt)
            {
                receipt.position=hand!=null?hand.position+body.forward*.04f:body.position+Vector3.up+body.forward*.35f;
                receipt.rotation=body.rotation*Quaternion.Euler(12,0,0);
            }
        }
        private void PoseArm(Vector3 target)
        {
            float first=Vector3.Distance(upper.position,lower.position),second=Vector3.Distance(lower.position,hand.position);
            Vector3 delta=target-upper.position;if(first<.001f||second<.001f||delta.sqrMagnitude<.000001f)return;
            float d=Mathf.Clamp(delta.magnitude,.001f,first+second-.001f);Vector3 axis=delta.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(Vector3.down,axis);if(bend.sqrMagnitude<.0001f)bend=Vector3.ProjectOnPlane(body.right,axis);bend.Normalize();
            float along=(first*first+d*d-second*second)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Vector3 elbow=upper.position+axis*along+bend*height;
            Apply(0,Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation);
            Apply(1,Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation);
        }
        private void Apply(int index,Quaternion worldRotation)
        {Transform bone=bones[index];basePose[index]=bone.localRotation;bone.rotation=worldRotation;applied[index]=true;}
        private void UndoPose()
        {for(int n=0;n<bones.Length;n++)if(applied[n]){if(bones[n]!=null)bones[n].localRotation=basePose[n];applied[n]=false;}}
        private static Transform Find(Transform root,string name)
        {foreach(Transform node in root.GetComponentsInChildren<Transform>(true))if(node.name==name)return node;return null;}
        private static Transform Bone(Transform root,string suffix)
        {foreach(Transform node in root.GetComponentsInChildren<Transform>(true))if(node.name==suffix||node.name.EndsWith(":"+suffix,StringComparison.Ordinal)||node.name=="mixamorig"+suffix)return node;return null;}
        private void OnDisable(){UndoPose();Observe(null);if(idle!=null)idle.Stop();}
        private void OnEnable(){if(idle!=null)idle.Play("cashier_idle");}
        private void OnDestroy()
        {
            UndoPose();Observe(null);
            for(int n=0;n<originalRenderers.Count;n++)if(originalRenderers[n]!=null)originalRenderers[n].enabled=originalVisibility[n];
            if(body!=null)Destroy(body.gameObject);if(sourceMaterial!=null)Destroy(sourceMaterial);if(receiptMaterial!=null)Destroy(receiptMaterial);
        }
    }
}
