using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // The operating counter is a physical workspace. HUD labels and transactions
    // address the same targets; only a reached hand contact commits a command.
    public sealed partial class StreetStallStage : MonoBehaviour
    {
        // RISK7SCENE：事件世界 UI（StallRiskView）必须与经营相机同层可见，
        // 从 private 提为 public；不改层号，渲染/遮挡行为不变。
        public const int Layer=29;
        public const int SolidLayer=28;
        public const int SolidMask=1<<SolidLayer;
        // Mouse-query-only occluders (the full 4.6 m visual shop fronts). The
        // real facades are 4.6 m tall but the navigation SolidBox is only
        // 2.4 m so characters can pass; the high orthographic camera looks
        // over that low solid and clicks would pass through the visual wall.
        // These boxes exist only for pointer picking — they are not in
        // SolidMask, so vendor/customer physics, routes and every original
        // SolidCollider envelope are unchanged.
        public const int QueryVisualLayer=27;
        public const int QueryVisualMask=1<<QueryVisualLayer;
        public event Action<int> CustomerArrived;
        public Camera View { get; private set; }
        public bool WorkActorBusy=>sequenceBusy || workActor!=null&&workActor.IsBusy;
        public Vector3 WorkActorPosition=>player?player.position:Vector3.zero;
        public event Action<string> WorkFailed;
        public VendorWorkAudit WorkAudit=>workActor==null?null:workActor.CaptureAudit();
        public int VisibleCustomerCount { get {int n=0;foreach(var g in guests.Values)if(g.root.gameObject.activeInHierarchy)n++;return n;} }
        public int WalkingCustomerCount { get {int n=0;foreach(var g in guests.Values)if(g.root.gameObject.activeInHierarchy&&g.walking)n++;return n;} }
        private readonly Dictionary<int,Guest> guests=new Dictionary<int,Guest>();
        private readonly Dictionary<string,Station> ingredients=new Dictionary<string,Station>(),packages=new Dictionary<string,Station>();
        private readonly List<Material> owned=new List<Material>();
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly List<Transform> dishes=new List<Transform>(),foodGroups=new List<Transform>(),steam=new List<Transform>(),flames=new List<Transform>(),eggPrepGroups=new List<Transform>();
        private readonly List<Station> panStations=new List<Station>();
        private readonly List<Light> oldLights=new List<Light>();
        private readonly List<LoadedCharacterAsset> customerAssets=new List<LoadedCharacterAsset>();
        private readonly Dictionary<string,Material> customerMaterials=new Dictionary<string,Material>();
        private Transform cooking,player,cart,primaryEquipment,carriedProp,readySauceJar;
        private Action sequenceFinished;
        private readonly Dictionary<int,Sprite> dishIcons=new Dictionary<int,Sprite>();
        private readonly List<Texture2D> iconTextures=new List<Texture2D>();
        public Sprite DishIcon(int recipe)=>dishIcons.TryGetValue(recipe,out Sprite icon)?icon:null;
        private VendorWorkActor workActor;
        private Camera oldCamera;
        private float operatingLeft=-1.85f,operatingRight=3.95f,framedAspect=-1f;
        private bool oldCameraEnabled,released,sequenceBusy;
        private Material cream,forest,foliage,rust,ink,steel,wood,rice,egg,green,sausage,dough,flame,white;
        private float time,cookingHeight=1.22f;
        private Station seasoning;
        // FLAVOR11：三种可点单风味（S01 街头辣椒油 / S02 现剁蒜蓉 / S05 番茄甜
        // 辣酱）各自独立的可视瓶与幕后工位。瓶身常驻前缘调料架、真实可点（不
        // 是文字标签），侧面视窗液芯随真实库存伸缩；拿起时隐藏原瓶、手持同款
        // 小瓶走到锅边，接触帧才由 Controller 再验一次并 commit。
        private static readonly string[] FlavorIds={ "S01", "S02", "S05" };
        private static readonly Color ChiliRed=new Color(.78f,.10f,.06f),GarlicCream=new Color(.96f,.93f,.82f),SweetOrange=new Color(.95f,.45f,.10f),OilAmber=new Color(.93f,.72f,.20f),JellyBrown=new Color(.45f,.16f,.10f),RosePink=new Color(.86f,.45f,.56f);
        private readonly Dictionary<string,Station> flavorStations=new Dictionary<string,Station>();
        private readonly Dictionary<string,Transform> flavorBottles=new Dictionary<string,Transform>();
        private readonly Dictionary<string,FillGauge> flavorFills=new Dictionary<string,FillGauge>();
        private Transform hiddenCarriedSource;
        private Material teaAmber,icePale,jellySyrup,rosePetal;
        private Material TeaAmber=>teaAmber??(teaAmber=Paint("Lemon tea amber",new Color(.85f,.62f,.16f),.5f));
        private Material IcePale=>icePale??(icePale=Paint("Ice pale blue",new Color(.88f,.94f,.97f),.8f));
        private Material JellySyrup=>jellySyrup??(jellySyrup=Paint("Brown sugar syrup",JellyBrown,.5f));
        private Material RosePetal=>rosePetal??(rosePetal=Paint("Rose petal",RosePink,.35f));
        private sealed class FillGauge{public Transform node;public float bottom,fullHeight;}
        private BusinessSession activeSession;
        private sealed class Station { public Vector3 stand; public Vector3? facingPoint; public Transform right,left,tool,toolGrip; public Vector3 work=>right.position; }
        private sealed class Guest { public Transform root; public Animation animation;public string playing;public bool leaving,walking,arrived,departureStarted;public Vector3 target;public int id,slot,routeAt;public float flowWaitSeconds;public NpcNavigationAgent agent;public CustomerServicePresentation presentation;public RiskGuestAdapter riskAdapter; }
        private bool guestOutflow;
        private readonly NpcDepartureBatch guestExitBatch=new NpcDepartureBatch();
        private BusinessSession guestAuditSession;
        [Serializable] public sealed class GuestNavigationAudit
        {
            public int id,slot,routeAt,batchId;public string phase,trafficPhase,blockedReason;
            public float[] position,target;public float radius,blockedSeconds,flowWaitSeconds,travelled,spawnedAt,arrivedAt,waitingToArriveSeconds;
            public bool arrived,leaving,walking,active;
        }
        public GuestNavigationAudit[] CaptureGuestAudit()
        {
            var result=new List<GuestNavigationAudit>();
            foreach(var g in guests.Values)
            {
                StallOrder record=guestAuditSession?.FindOrder(g.id);
                string phase=!g.root.gameObject.activeInHierarchy?"exited":g.departureStarted?(g.routeAt==0?"leave-slot":"exit-left"):g.arrived?(g.leaving?(guestExitBatch.Contains(g.id)?"waiting-merge-reservation":"waiting-exit-batch"):"at-slot"):(g.routeAt==0?"enter-rear-lane":"enter-slot");
                result.Add(new GuestNavigationAudit{id=g.id,slot=g.slot,routeAt=g.routeAt,batchId=guestExitBatch.Contains(g.id)?guestExitBatch.Serial:0,phase=phase,trafficPhase=guestOutflow?"outflow":"inflow",position=new[]{g.root.position.x,g.root.position.y,g.root.position.z},target=new[]{g.target.x,g.target.y,g.target.z},spawnedAt=record?.spawnedAt??0,arrivedAt=record?.arrivedAt??-1,waitingToArriveSeconds=record==null?0:Mathf.Max(0,(record.arrived?record.arrivedAt:guestAuditSession.Elapsed)-record.spawnedAt),radius=g.agent.Profile.Radius,blockedSeconds=g.agent.BlockedSeconds,blockedReason=g.agent.BlockedReason,flowWaitSeconds=g.flowWaitSeconds,travelled=g.agent.Travelled,arrived=g.arrived,leaving=g.leaving,walking=g.walking,active=g.root.gameObject.activeInHierarchy});
            }
            if(guestAuditSession!=null)foreach(var order in guestAuditSession.Orders)
                if(!guests.ContainsKey(order.id)&&!order.served&&!order.departed)
                    result.Add(new GuestNavigationAudit{id=order.id,slot=-1,routeAt=-1,phase="outside-awaiting-safe-entry",trafficPhase=guestOutflow?"outflow":"inflow",position=Array.Empty<float>(),target=new[]{6.15f,0,3.22f},spawnedAt=order.spawnedAt,arrivedAt=order.arrivedAt,waitingToArriveSeconds=Mathf.Max(0,guestAuditSession.Elapsed-order.spawnedAt),blockedReason="entry-or-traffic-reservation",active=false});
            return result.ToArray();
        }
        public string[] CaptureGuestOverlaps()=>guestNavigation?.CaptureOverlaps()??Array.Empty<string>();
        private NpcNavigationSpace guestNavigation;
        private readonly Guest[] queueSlots=new Guest[4];
        // Every slot reserves the largest measured customer radius (.78 m), so
        // changing archetype cannot invalidate a neighbour's reserved clearance.
        private static readonly Vector3[] QueuePoints={new Vector3(-1.52f,0,1.48f),new Vector3(.14f,0,1.48f),new Vector3(1.80f,0,1.48f),new Vector3(3.46f,0,1.48f)};
        public void Initialize(BusinessSetup setup,Action<int> unusedLegacyAction)
        {
            oldCamera=Camera.main;if(oldCamera){oldCameraEnabled=oldCamera.enabled;oldCamera.enabled=false;}
            foreach(Light light in FindObjectsByType<Light>())if(light.enabled){oldLights.Add(light);light.enabled=false;}
            cream=Paint("Enamel cream",new Color(.91f,.85f,.70f));forest=Paint("Ink-blue painted wood",CampaignUI.Ink);foliage=Paint("Natural foliage",new Color(.21f,.46f,.40f));
            rust=Paint("Vermilion paint",new Color(.76f,.28f,.17f));ink=Paint("Seasoned iron",new Color(.13f,.14f,.14f));
            steel=Paint("Brushed steel",new Color(.68f,.72f,.73f),.5f);wood=Paint("Honey wood",new Color(.66f,.42f,.21f));
            rice=Paint("Rice",new Color(.99f,.92f,.73f));egg=Paint("Egg",new Color(1,.70f,.17f));green=Paint("Scallion",new Color(.31f,.58f,.13f));
            sausage=Paint("Ham",new Color(.85f,.38f,.25f));dough=Paint("Golden pastry",new Color(.91f,.64f,.29f));white=Paint("Foam packaging",new Color(.96f,.96f,.90f));
            flame=Paint("Gas blue",new Color(.13f,.52f,.97f));flame.SetColor("_EmissionColor",new Color(.02f,.16f,.50f));
            foreach(var def in CharacterAssetCatalog.All)if(def.IsCustomerSource)
            {if(CharacterAssetCatalog.TryLoad(def.Id,out LoadedCharacterAsset asset,out string failure,CharacterMotion.Idle,CharacterMotion.Walk))customerAssets.Add(asset);else Debug.LogWarning(failure);}
            Environment();BuildCart(setup);BuildCamera();SetLayers(transform);Physics.SyncTransforms();
            float navRight=Mathf.Max(7.8f,operatingRight+2.2f),navLeft=-6.2f;
            guestNavigation=new NpcNavigationSpace(new Bounds(new Vector3((navRight+navLeft)*.5f,1,2.4f),new Vector3(navRight-navLeft,3,4.3f)),SolidMask);
            Transform vendorBody=Group("Vendor occupied body",player);vendorBody.gameObject.layer=SolidLayer;var vendorCollider=vendorBody.gameObject.AddComponent<CapsuleCollider>();vendorCollider.radius=.25f;vendorCollider.height=1.61f;vendorCollider.center=Vector3.up*.845f;
            BuildDishIcons();
        }
        private Material Paint(string name,Color colour,float gloss=.12f)
        {var m=new Material(Resources.Load<Shader>("Shaders/StallSoftLit")){name="Stall_"+name,color=colour};m.SetFloat("_Gloss",gloss);owned.Add(m);return m;}
        private void SoftMaterials(GameObject model)
        {
            var cache=new Dictionary<Material,Material>();
            foreach(Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!(r is MeshRenderer)&&!(r is SkinnedMeshRenderer))continue;
                Material[] mats=r.sharedMaterials;
                for(int i=0;i<mats.Length;i++)if(mats[i]!=null)
                {Material src=mats[i];if(!cache.TryGetValue(src,out Material m)){m=Paint(src.name,src.HasProperty("_Color")?src.color:Color.white);if(src.HasProperty("_MainTex")){m.mainTexture=src.mainTexture;m.mainTextureScale=src.mainTextureScale;m.mainTextureOffset=src.mainTextureOffset;}cache[src]=m;}mats[i]=m;}
                r.sharedMaterials=mats;
            }
        }
        private Transform Group(string name,Transform parent){var go=new GameObject(name);go.layer=Layer;go.transform.SetParent(parent,false);return go.transform;}
        private GameObject Shape(string name,Transform parent,Vector3 at,Vector3 scale,Material mat,PrimitiveType type=PrimitiveType.Cube,bool interactive=false)
        {var go=GameObject.CreatePrimitive(type);go.name=name;go.layer=Layer;go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;if(!interactive)Destroy(go.GetComponent<Collider>());return go;}
        private void Between(string name,Transform parent,Vector3 a,Vector3 b,float thickness,Material material)
        {
            Transform bar=Shape(name,parent,(a+b)*.5f,new Vector3(thickness,Vector3.Distance(a,b)*.5f,thickness),material,PrimitiveType.Cylinder).transform;
            bar.localRotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);
        }
        private GameObject Model(string name,Transform parent,bool ignored)
        {
            var prefab=Resources.Load<GameObject>("Models/"+name);if(!prefab)throw new InvalidOperationException("Missing actual model: "+name);
            Transform root=Group(name,parent);Instantiate(prefab,root,false);
            foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name.StartsWith("Collider_",StringComparison.Ordinal))t.gameObject.SetActive(false);
            foreach(Animator a in root.GetComponentsInChildren<Animator>())a.enabled=false;
            foreach(Animation a in root.GetComponentsInChildren<Animation>())a.enabled=false;
            SoftMaterials(root.gameObject);return root.gameObject;
        }
        private static Transform Find(Transform root,string name){foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name==name)return t;return null;}
        private static void Face(Transform root,string prefix,Vector3 direction)
        {
            Transform origin=Find(root,prefix+"Origin"),forward=Find(root,prefix+"Forward");if(!origin||!forward)return;
            Vector3 from=forward.position-origin.position;Transform deck=prefix=="Cart"?Find(root,"CartDeck"):null;
            if(deck){Vector3 up=Vector3.ProjectOnPlane(deck.position-origin.position,from.normalized).normalized;root.rotation=Quaternion.LookRotation(direction,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(from.normalized,up))*root.rotation;}
            else{from.y=0;root.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(from,direction,Vector3.up),Vector3.up)*root.rotation;}
        }
        private void Environment()
        {
            districtPaving=Paint("Warm grey pavers",new Color(.53f,.51f,.50f));districtSeam=Paint("Paver mortar",new Color(.39f,.39f,.38f));Material paving=districtPaving,seam=districtSeam;
            Shape("Pavement foundation",transform,new Vector3(0,-.12f,0),new Vector3(30,.2f,22),seam);
            // Cover the expanded fixed operating view, including the direct
            // handoff bay and foreground. Keep the original brick/mortar size,
            // material and foundation; merge these decorative tiles to one draw.
            Transform streetPaving=Group("Continuous street paving",transform);
            for(int x=-23;x<=23;x++)for(int z=-22;z<=22;z++)
                Shape("Street paving",streetPaving,new Vector3(x*.72f+(z%2)*.36f,-.004f,z*.55f),new Vector3(.70f,.025f,.53f),paving);
            CombineDecorations(streetPaving);
            for(int n=-1;n<=1;n++)
            {
                Transform shopVisual=Group("District original shop "+n,transform);originalShopVisuals[n+1]=shopVisual;
                float x=n*4.3f;
                Shape("Neighbouring shop",shopVisual,new Vector3(x,2.3f,5.6f),new Vector3(4.2f,4.6f,2),cream);
                SolidBox("Shop facade "+n,new Vector3(x,1.2f,4.51f),new Vector3(4.2f,2.4f,.13f));
                // Query-only occluder hugging the 4.6 m visual front so a
                // camera ray crossing the visual face above the 2.4 m solid
                // still refuses targets hidden behind the facade. Navigation
                // and character physics never see this layer.
                QueryBox("Shop facade "+n,new Vector3(x,2.3f,4.55f),new Vector3(4.2f,4.6f,.9f));
                Shape("Rolled shop shutter",shopVisual,new Vector3(x,1.22f,4.56f),new Vector3(3.45f,2.44f,.12f),forest);
                for(int k=0;k<17;k++)Shape("Shutter rib",shopVisual,new Vector3(x,.10f+k*.14f,4.47f),new Vector3(3.45f,.018f,.028f),steel);
                Shape("Shop sign",shopVisual,new Vector3(x,2.88f,4.42f),new Vector3(3.85f,.64f,.18f),rust);
                WorldText(n==0?"早点 · 热食":n<0?"街坊杂货":"日用百货",shopVisual,new Vector3(x,2.88f,4.31f),.28f);
                Shape("Canvas sunshade",shopVisual,new Vector3(x,2.43f,4.18f),new Vector3(3.88f,.10f,.80f),cream);
                for(int b=0;b<8;b++)Shape("Sunshade colour stripe",shopVisual,new Vector3(x-1.7f+b*.48f,2.49f,4.18f),new Vector3(.24f,.012f,.80f),rust);
            }
            for(int side=-1;side<=1;side+=2)
            {
                // The right planter belongs in the foreground, outside both the
                // vendor work aisle and customer pickup stands. Move the visible
                // pot and its unchanged solid together; never route through it.
                Vector3 p=new Vector3(side*5.5f,0,side>0?-2.3f:1.6f);
                Transform planterVisual=Group("District original planter "+side,transform);originalPlanterVisuals[side<0?0:1]=planterVisual;
                Shape("Street planter",planterVisual,p+Vector3.up*.24f,new Vector3(.7f,.48f,.66f),cream);
                SolidBox("Planter and foliage",p+Vector3.up*.55f,new Vector3(.86f,1.1f,.76f));
                for(int leaf=0;leaf<7;leaf++)
                {float a=leaf*2.4f;Shape("Broad leaves",planterVisual,p+new Vector3(Mathf.Sin(a)*.22f,.70f+(leaf%3)*.1f,Mathf.Cos(a)*.19f),new Vector3(.40f,.52f,.31f),foliage,PrimitiveType.Sphere);}
            }
            Transform board=Group("Breakfast chalkboard",transform);board.position=new Vector3(-4.7f,0,1.1f);board.rotation=Quaternion.Euler(0,12,0);
            SolidBox("Menu chalkboard",board.position+new Vector3(0,.8f,.02f),new Vector3(.86f,1.6f,.34f));
            Shape("Menu board wood frame",board,new Vector3(0,1.05f,0),new Vector3(.85f,1.1f,.08f),wood);
            Shape("Menu board enamel",board,new Vector3(0,1.05f,-.05f),new Vector3(.76f,1.01f,.03f),rust);
            WorldText("早点小吃\n蛋炒饭 · 手抓饼\n现做现卖",board,new Vector3(0,1.08f,-.08f),.15f);
            for(int s=-1;s<=1;s+=2)Shape("Board stand",board,new Vector3(s*.31f,.28f,.10f),new Vector3(.06f,.56f,.08f),wood);
        }
        private void BuildCart(BusinessSetup setup)
        {
            cart=Model("CartAI",transform,false).transform;Face(cart,"Cart",Vector3.left);Transform origin=Find(cart,"CartOrigin");if(origin)cart.position-=origin.position;
            cooking=Group("Deployed counter attachments",transform);
            // Supported folding leaves make the working surface larger than transport deck.
            Shape("Stainless worktop",cooking,new Vector3(1.05f,.935f,0),new Vector3(2.95f,.075f,1.04f),steel);
            for(int side=-1;side<=1;side+=2)
            {Shape("Folding leaf rim",cooking,new Vector3(1.05f,.985f,side*.51f),new Vector3(2.96f,.05f,.035f),steel);}
            for(float x=2.35f;x<2.5f;x+=1)
            {Shape("Fold-out support",cooking,new Vector3(x,.51f,.37f),new Vector3(.065f,1.02f,.065f),steel);Shape("Support foot",cooking,new Vector3(x,.03f,.37f),new Vector3(.20f,.05f,.20f),ink);}
            Shape("Front red fascia",cooking,new Vector3(1.07f,.85f,-.53f),new Vector3(2.86f,.30f,.06f),rust);
            WorldText("流动早点车",cooking,new Vector3(1.05f,.86f,-.57f),.18f);
            if(setup.stoveLoaded&&setup.equipment.Contains("B04"))
            {
                Transform stove=Model("Stove",cooking,false).transform,slot=Find(cart,"StoveLoadedPlacement");primaryEquipment=stove;
                if(slot&&origin)stove.SetPositionAndRotation(slot.position,slot.rotation*Quaternion.Inverse(origin.rotation)*cart.rotation);
                Bounds b=BoundsOf(stove);stove.position+=new Vector3(.92f-b.center.x,.974f-b.min.y,-b.center.z);cookingHeight=Top(stove);
            }
            else foreach(string id in new[]{"B03","B02","B01","B05","B06","B07","B08"})if(setup.equipment.Contains(id))
            {Transform eq=EquipmentVisualFactory.Create(id,cooking).transform;eq.position=new Vector3(.92f,.974f,0);SoftMaterials(eq.gameObject);primaryEquipment=eq;cookingHeight=Top(eq);break;}
            Transform rack=Group("Rack_Utensils six physical hooks",cooking);
            for(int side=0;side<2;side++)Between("Utensil rack upright",rack,new Vector3(.10f+side*1.55f,.99f,.50f),new Vector3(.10f+side*1.55f,1.87f,.50f),.038f,steel);
            Between("Utensil rack crossbar",rack,new Vector3(.08f,1.87f,.50f),new Vector3(1.67f,1.87f,.50f),.044f,steel);
            for(int hook=0;hook<6;hook++)
            {
                float hx=.22f+hook*.26f;
                Between("Tool hook",rack,new Vector3(hx,1.87f,.50f),new Vector3(hx,1.71f,.50f),.012f,ink);
                Between("Tool hook toe",rack,new Vector3(hx,1.71f,.50f),new Vector3(hx,1.71f,.46f),.012f,ink);
            }
            player=Model("Player",transform,false).transform;Face(player,"Player",Vector3.forward);player.position=new Vector3(.8f,0,-1.12f);
            workActor=gameObject.AddComponent<VendorWorkActor>();workActor.automaticTick=false;workActor.Bind(player,()=>false,SolidMask);
            workActor.Failed+=reason=>{CompleteWork();Debug.LogWarning("STALL_WORK_REJECTED "+reason);WorkFailed?.Invoke(reason);};
            string[] foodIds={"dough","rice","egg","skewer","meat","prepared","fruit"};int at=0;
            foreach(string id in foodIds)if(setup.inventory.TryGetValue(id,out int quantity)&&quantity>0)
            {float x=.12f-at*.46f;Transform tray=IngredientTray(id,new Vector3(x,.985f,-.65f));ingredientTrayNodes[id]=tray;ingredientTrayFull[id]=true;ingredients[id]=MakeStation(new Vector3(x,1.115f,-.79f),new Vector3(x-.14f,0,-1.12f),false);Target(tray,StallInteractionKind.Ingredient,id,-1,tray.position+new Vector3(0,.11f,-.19f),new Vector3(.44f,.22f,.27f));at++;}
            if(at>0)
            {
                float width=at*.46f+.04f,mid=.12f-(at-1)*.23f;
                Shape("Fold-out ingredient shelf",cooking,new Vector3(mid,.935f,-.65f),new Vector3(width,.075f,.34f),steel);
                SolidBox("Ingredient shelf",new Vector3(mid,.935f,-.65f),new Vector3(width,.075f,.34f));
            }
            Transform condiments=Group("Sauce caddy",cooking);condiments.position=new Vector3(1.97f,.995f,.08f);
            Shape("Sauce tray",condiments,Vector3.zero,new Vector3(.92f,.035f,.36f),steel);
            // FLAVOR11：风味瓶挂在前缘小架上，摊主站在前排（z=-.97）即可真实
            // 够到，与基础油盐瓶（前方工作槽）互不遮挡。
            Shape("Front flavor shelf",cooking,new Vector3(1.97f,.985f,-.40f),new Vector3(.96f,.035f,.30f),steel);
            SolidBox("Front flavor shelf",new Vector3(1.97f,.985f,-.40f),new Vector3(.96f,.035f,.30f));
            for(int i=0;i<FlavorIds.Length;i++)
            {
                string flavor=FlavorIds[i];float x=1.70f+i*.27f;
                Transform bottle=BuildFlavorBottle(flavor,cooking,new Vector3(x,1.005f,-.44f),true);
                flavorBottles[flavor]=bottle;
                Station station=MakeStation(new Vector3(x,1.18f,-.60f),new Vector3(x,0,-.97f),false);
                flavorStations[flavor]=station;
                Target(bottle,StallInteractionKind.Seasoning,flavor,-1,bottle.position+new Vector3(0,.27f,0),new Vector3(.23f,.35f,.23f),26);
            }
            seasoning=MakeStation(new Vector3(.925f,1.16f,-.64f),new Vector3(.925f,0,-.97f),false);
            readySauceJar=Group("Seasoning bottle at reachable front-edge work slot",cooking);readySauceJar.position=seasoning.work;
            Shape("Ready sauce jar",readySauceJar,new Vector3(0,.04f,.035f),new Vector3(.09f,.08f,.09f),rust,PrimitiveType.Cylinder);
            Shape("Ready sauce lid",readySauceJar,new Vector3(0,.13f,.035f),new Vector3(.095f,.015f,.095f),wood,PrimitiveType.Cylinder);
            Shape("Front seasoning cup support",cooking,new Vector3(.925f,1.13f,-.605f),new Vector3(.15f,.025f,.17f),steel);
            Between("Seasoning cup bracket",cooking,new Vector3(.925f,1.12f,-.60f),new Vector3(.925f,.98f,-.49f),.02f,steel);
            SolidBox("Seasoning support",new Vector3(.925f,1.13f,-.605f),new Vector3(.15f,.025f,.17f));
            Target(readySauceJar,StallInteractionKind.Seasoning,"seasoning",-1,readySauceJar.position+new Vector3(0,.20f,-.05f),new Vector3(.20f,.29f,.20f),26);
            int pk=0;foreach(string id in new[]{"P02","P03","P04","P05","P06","P07","P08","P09","P10","P11","P12"})if(setup.inventory.TryGetValue(id,out int count)&&count>0)
            {
                // One visible front edge per pack: the previous 0.23 m rows
                // overlapped 0.30 m boxes and their 0.37 m picking volumes.
                Transform pack=Group("Package "+id,cooking);pack.position=new Vector3(2.72f+pk*.48f,1.01f,-.62f);
                Transform stacks=Group("Visible package stock "+id,pack);
                var stockNodes=new List<Transform>();
                Transform topNode=BuildPackageStock(id,stacks,stockNodes);
                packageStockNodes[id]=stockNodes;packageTopNodes[id]=topNode;
                Shape("Magazine support tray",pack,new Vector3(0,-.075f,0),new Vector3(.40f,.045f,.36f),steel);
                SolidBox("Package magazine "+id,new Vector3(pack.position.x,.935f,pack.position.z),new Vector3(.40f,.045f,.36f));
                packages[id]=MakeStation(pack.position+new Vector3(0,.14f,-.10f),new Vector3(pack.position.x,0,-1.08f),true);
                Target(pack,StallInteractionKind.Packaging,id,-1,pack.position+new Vector3(0,.04f,-.18f),new Vector3(.39f,.29f,.37f));pk++;
            }
            if(pk>0)
            {
                float width=.50f+.48f*(pk-1),mid=2.72f+.24f*(pk-1);
                Vector3 top=new Vector3(mid,.89f,-.40f),size=new Vector3(width,.045f,.80f);
                Shape("Fold-out packaging counter",cooking,top,size,steel);
                SolidBox("Packaging counter support",top,size);
                for(int leg=0;leg<2;leg++)
                {
                    Vector3 legAt=new Vector3(mid+(leg==0?-1:1)*(width*.5f-.10f),.44f,-.12f);
                    Shape("Packaging counter leg",cooking,legAt,new Vector3(.055f,.88f,.055f),steel);
                    SolidBox("Packaging counter leg "+leg,legAt,new Vector3(.055f,.88f,.055f));
                }
            }
            BuildCounter();
            RestoreVehicleCollision();
            SolidBox("Worktop",new Vector3(1.05f,.935f,0),new Vector3(2.95f,.075f,1.04f));
            SolidBox("Counter fascia",new Vector3(1.07f,.85f,-.53f),new Vector3(2.86f,.30f,.06f));
            SolidBox("Folding support",new Vector3(2.35f,.51f,.37f),new Vector3(.065f,1.02f,.065f));
            Physics.SyncTransforms();
        }
        // 每种包材的堆叠层造型：一层 = 一个可显隐 Transform（Counter.cs 只按
        // 数量切显隐，层内 1–4 个 Shape 仅为轮廓细节）。packageStockNodes 仍恰
        // 4 个、packageTopNodes 仍恰 1 个；层高度与 .022 层距匹配，整体不越出
        // 原 .39×.29×.37 点选判定附近。P08 是竖直封口膜饮料杯，P05 是矩形卡扣
        // 饭盒，P04 是牛皮纸袋——三者轮廓绝不混同。
        private Transform BuildPackageStock(string id,Transform stacks,List<Transform> stockNodes)
        {
            for(int n=0;n<4;n++){Transform layer=PackageStockLayer(id,stacks);layer.localPosition=new Vector3(0,n*.022f,0);stockNodes.Add(layer);}
            bool bag=id=="P04"||id=="P09";
            Transform topNode;
            if(bag)topNode=Shape("Paper bag fold",stacks,new Vector3(0,.1f,.10f),new Vector3(.31f,.02f,.065f),id=="P04"?wood:dough).transform;
            else if(id=="P08")
            {
                Transform film=Group("Sealed film top",stacks);film.localPosition=new Vector3(0,.148f,0);
                Shape("Film sheet",film,Vector3.zero,new Vector3(.15f,.008f,.15f),white,PrimitiveType.Cylinder);
                Shape("Straw",film,new Vector3(.03f,.018f,0),new Vector3(.014f,.018f,.014f),rust,PrimitiveType.Cylinder);
                topNode=film;
            }
            else if(id=="P12")topNode=Shape("Bamboo cup rim",stacks,new Vector3(0,.16f,0),new Vector3(.15f,.004f,.15f),dough,PrimitiveType.Cylinder).transform;
            else if(id=="P07"||id=="P10")topNode=Shape("Raised tub lid",stacks,new Vector3(0,.12f,.14f),new Vector3(.24f,.012f,.24f),white,PrimitiveType.Cylinder).transform;
            else topNode=Shape("Raised box lid",stacks,new Vector3(0,.12f,.16f),new Vector3(.35f,.03f,.30f),white).transform;
            if(!bag&&id!="P08"&&id!="P12")topNode.localRotation=Quaternion.Euler(-62,0,0);
            return topNode;
        }
        private Transform PackageStockLayer(string id,Transform parent)
        {
            Transform layer=Group("Stacked "+id,parent);
            switch(id)
            {
                case "P02": // 发泡盒：白盒+外凸盖沿
                    Shape("Foam box",layer,new Vector3(0,.011f,0),new Vector3(.28f,.022f,.23f),white);
                    Shape("Foam lid lip",layer,new Vector3(0,.026f,0),new Vector3(.30f,.008f,.25f),white);
                    break;
                case "P03": // 防油纸托与竹签
                    Shape("Paper skewer tray",layer,new Vector3(0,.008f,0),new Vector3(.25f,.016f,.18f),dough);
                    for(int i=0;i<3;i++)Shape("Bamboo skewer",layer,new Vector3(-.07f+i*.07f,.02f,0),new Vector3(.01f,.008f,.21f),wood);
                    break;
                case "P04": // 牛皮纸袋（扁、牛皮色）
                    Shape("Flat kraft bag",layer,new Vector3(0,.007f,0),new Vector3(.24f,.014f,.19f),wood);
                    Shape("Kraft fold",layer,new Vector3(0,.016f,.08f),new Vector3(.20f,.005f,.03f),dough);
                    break;
                case "P05": // 方形 PP 卡扣密封餐盒：深色盒身+浅盖+两侧朱红卡扣
                    Shape("Clip box base",layer,new Vector3(0,.009f,0),new Vector3(.27f,.018f,.21f),ink);
                    Shape("Clip box lid",layer,new Vector3(0,.022f,0),new Vector3(.29f,.007f,.23f),cream);
                    Shape("Seal clip L",layer,new Vector3(-.148f,.016f,0),new Vector3(.018f,.016f,.06f),rust);
                    Shape("Seal clip R",layer,new Vector3(.148f,.016f,0),new Vector3(.018f,.016f,.06f),rust);
                    break;
                case "P06": // 分格大餐盒：最大 footprint+盖面分格条
                    Shape("Compartment base",layer,new Vector3(0,.010f,0),new Vector3(.33f,.020f,.27f),white);
                    Shape("Compartment lid",layer,new Vector3(0,.024f,0),new Vector3(.35f,.007f,.29f),white);
                    Shape("Divider long",layer,new Vector3(0,.030f,0),new Vector3(.010f,.008f,.24f),cream);
                    Shape("Divider cross",layer,new Vector3(0,.030f,0),new Vector3(.30f,.008f,.010f),cream);
                    break;
                case "P07": // 圆纸汤碗：矮圆碗+碗盖
                    Shape("Soup bowl",layer,new Vector3(0,.009f,0),new Vector3(.23f,.018f,.23f),cream,PrimitiveType.Cylinder);
                    Shape("Bowl lid",layer,new Vector3(0,.022f,0),new Vector3(.235f,.006f,.235f),white,PrimitiveType.Cylinder);
                    break;
                case "P08": // 封口饮料杯：竖直圆杯+封口膜，一眼不是饭盒
                    Shape("Nested cup",layer,new Vector3(0,.04f,0),new Vector3(.13f,.04f,.13f),IcePale,PrimitiveType.Cylinder);
                    Shape("Cup rim",layer,new Vector3(0,.08f,0),new Vector3(.15f,.004f,.15f),white,PrimitiveType.Cylinder);
                    break;
                case "P09": // 大纸袋（扁）
                    Shape("Flat carrier bag",layer,new Vector3(0,.007f,0),new Vector3(.23f,.014f,.18f),dough);
                    Shape("Carrier crease",layer,new Vector3(0,.016f,.075f),new Vector3(.19f,.005f,.03f),wood);
                    Shape("Red sleeve",layer,new Vector3(0,.018f,0),new Vector3(.07f,.005f,.18f),rust);
                    break;
                case "P10": // 青花纹纸瓦罐
                    Shape("Porcelain jar",layer,new Vector3(0,.024f,0),new Vector3(.18f,.024f,.18f),white,PrimitiveType.Cylinder);
                    Shape("Blue band",layer,new Vector3(0,.035f,0),new Vector3(.185f,.006f,.185f),ink,PrimitiveType.Cylinder);
                    break;
                case "P11": // 竹编食盒
                    Shape("Bamboo tray",layer,new Vector3(0,.008f,0),new Vector3(.28f,.016f,.20f),wood);
                    Shape("Bamboo lid",layer,new Vector3(0,.019f,0),new Vector3(.30f,.006f,.22f),dough);
                    break;
                case "P12": // 竹筒杯
                    Shape("Bamboo cup",layer,new Vector3(0,.044f,0),new Vector3(.14f,.044f,.14f),wood,PrimitiveType.Cylinder);
                    Shape("Bamboo rim",layer,new Vector3(0,.089f,0),new Vector3(.15f,.006f,.15f),dough,PrimitiveType.Cylinder);
                    break;
                default:
                    Shape("Stacked pack",layer,new Vector3(0,.012f,0),new Vector3(.30f,.025f,.25f),white);
                    break;
            }
            return layer;
        }
        private void SolidBox(string name,Vector3 center,Vector3 size)
        {
            Transform solid=Group("Solid_"+name,transform);solid.gameObject.layer=SolidLayer;solid.position=center;
            solid.gameObject.AddComponent<BoxCollider>().size=size;
        }
        private void QueryBox(string name,Vector3 center,Vector3 size)
        {
            Transform solid=Group("Query_"+name,transform);solid.gameObject.layer=QueryVisualLayer;solid.position=center;
            solid.gameObject.AddComponent<BoxCollider>().size=size;
        }
        private void RestoreVehicleCollision()
        {
            foreach(MeshFilter mesh in cart.GetComponentsInChildren<MeshFilter>(true))
            {
                Transform ancestor=mesh.transform;bool proxy=false;
                while(ancestor!=cart.parent&&ancestor!=null){if(ancestor.name.StartsWith("Collider_",StringComparison.Ordinal)){proxy=true;ancestor.gameObject.SetActive(true);}ancestor=ancestor.parent;}
                if(!proxy||!mesh.sharedMesh)continue;
                mesh.gameObject.SetActive(true);mesh.gameObject.layer=SolidLayer;
                Renderer renderer=mesh.GetComponent<Renderer>();if(renderer)renderer.enabled=false;
                BoxCollider box=mesh.gameObject.AddComponent<BoxCollider>();box.center=mesh.sharedMesh.bounds.center;box.size=mesh.sharedMesh.bounds.size;
            }
        }
        // 三种风味瓶的真实造型，彼此一眼可辨：
        // S01 辣椒油=红色玻璃瓶+琥珀浮油层+底部辣椒沉淀+软木塞；
        // S02 蒜蓉=乳白粗瓷罐+隆起粗颗粒蒜茸+斜靠木柄小刷；
        // S05 甜辣酱=橙色挤压瓶+尖嘴翻盖。余量由侧面视窗液芯伸缩表达
        // （随 Present 里 session.Count(flavor) 更新，空瓶一目了然）。
        private Transform BuildFlavorBottle(string flavor,Transform parent,Vector3 at,bool withGauge)
        {
            Material body=flavor=="S01"?Paint("Chili oil red glass",ChiliRed,.55f):flavor=="S02"?Paint("Garlic cream ceramic",GarlicCream,.2f):Paint("Sweet chili orange bottle",SweetOrange,.35f);
            Transform bottle=Group("Flavor bottle "+flavor,parent);bottle.position=at;
            Transform generatedFlavor=Group("Generated flavor visual",bottle);
            bool generated=(flavor=="S01"||flavor=="S02")&&GeneratedEquipmentAssets.TryPopulate(flavor,generatedFlavor);
            if(generated)generatedFlavor.localScale=Vector3.one*(.18f/.115f);
            if(!generated)
            {
            if(flavor=="S01")
            {
                Shape("Chili oil glass body",bottle,new Vector3(0,.065f,0),new Vector3(.075f,.13f,.075f),body,PrimitiveType.Cylinder);
                Shape("Chili sediment",bottle,new Vector3(0,.014f,0),new Vector3(.077f,.026f,.077f),rust,PrimitiveType.Cylinder);
                Shape("Floating oil layer",bottle,new Vector3(0,.121f,0),new Vector3(.077f,.014f,.077f),Paint("Oil amber",OilAmber,.7f),PrimitiveType.Cylinder);
                Shape("Bottle shoulder",bottle,new Vector3(0,.143f,0),new Vector3(.046f,.028f,.046f),body,PrimitiveType.Cylinder);
                Shape("Bottle neck",bottle,new Vector3(0,.173f,0),new Vector3(.028f,.034f,.028f),body,PrimitiveType.Cylinder);
                Shape("Cork stopper",bottle,new Vector3(0,.196f,0),new Vector3(.031f,.02f,.031f),wood,PrimitiveType.Cylinder);
                Shape("Paper label",bottle,new Vector3(0,.06f,0),new Vector3(.078f,.045f,.078f),cream,PrimitiveType.Cylinder);
                var flecks=new System.Random(7);for(int i=0;i<6;i++)Shape("Chili flake in glass",bottle,new Vector3(((float)flecks.NextDouble()-.5f)*.05f,.03f+((float)flecks.NextDouble()-.5f)*.04f,.038f),new Vector3(.012f,.008f,.006f),rust);
            }
            else if(flavor=="S02")
            {
                Shape("Garlic ceramic jar",bottle,new Vector3(0,.05f,0),new Vector3(.085f,.10f,.085f),body,PrimitiveType.Cylinder);
                Shape("Jar foot",bottle,new Vector3(0,.006f,0),new Vector3(.09f,.014f,.09f),body,PrimitiveType.Cylinder);
                var bits=new System.Random(11);for(int i=0;i<14;i++){float a=(float)bits.NextDouble()*Mathf.PI*2,r=.01f+(float)bits.NextDouble()*.045f;Shape("Coarse minced garlic",bottle,new Vector3(Mathf.Sin(a)*r,.105f+(float)bits.NextDouble()*.02f,Mathf.Cos(a)*r),new Vector3(.012f,.009f,.012f),rice,PrimitiveType.Sphere);}
                Transform brush=Group("Garlic brush",bottle);brush.localPosition=new Vector3(.085f,.02f,.03f);brush.localRotation=Quaternion.Euler(0,0,-38);
                Shape("Brush handle",brush,new Vector3(0,.07f,0),new Vector3(.02f,.14f,.02f),wood,PrimitiveType.Cylinder);
                Shape("Brush ferrule",brush,new Vector3(0,.148f,0),new Vector3(.024f,.02f,.024f),steel,PrimitiveType.Cylinder);
                Shape("Brush bristles",brush,new Vector3(0,.175f,0),new Vector3(.026f,.032f,.026f),cream,PrimitiveType.Cylinder);
            }
            else
            {
                Shape("Squeeze bottle body",bottle,new Vector3(0,.06f,0),new Vector3(.07f,.12f,.07f),body,PrimitiveType.Cylinder);
                Shape("Squeeze shoulder",bottle,new Vector3(0,.132f,0),new Vector3(.05f,.028f,.05f),body,PrimitiveType.Cylinder);
                Shape("Squeeze nozzle",bottle,new Vector3(0,.158f,0),new Vector3(.016f,.028f,.016f),body,PrimitiveType.Cylinder);
                Shape("Flip top cap",bottle,new Vector3(0,.176f,0),new Vector3(.034f,.014f,.034f),cream,PrimitiveType.Cylinder);
                Shape("Bottle label",bottle,new Vector3(0,.055f,0),new Vector3(.073f,.05f,.073f),cream,PrimitiveType.Cylinder);
            }
            }
            if(withGauge)
            {
                float radius=generated?.095f:flavor=="S02"?.085f:flavor=="S01"?.075f:.07f;
                float bottom=.02f,top=flavor=="S02"?.085f:.105f;
                Material fillMat=flavor=="S01"?Paint("Chili oil fill",new Color(.60f,.05f,.03f),.6f):flavor=="S02"?Paint("Garlic fill",new Color(.93f,.90f,.78f),.3f):Paint("Sweet chili fill",new Color(.88f,.38f,.07f),.4f);
                Shape("Sight glass back",bottle,new Vector3(radius+.005f,(bottom+top)*.5f,0),new Vector3(.014f,top-bottom,.010f),cream);
                Transform fill=Group("Visible remaining "+flavor,bottle);fill.localPosition=new Vector3(radius+.005f,bottom,0);
                fill.localScale=new Vector3(.009f,.004f,.006f);
                Shape("Fill strip",fill,Vector3.zero,Vector3.one,fillMat,PrimitiveType.Cube);
                flavorFills[flavor]=new FillGauge{node=fill,bottom=bottom,fullHeight=top-bottom};
            }
            SetLayers(bottle);
            return bottle;
        }
        private Transform IngredientTray(string id,Vector3 at)
        {
            Transform tray=Group("Ingredient "+id,cooking);tray.position=at;
            PopulateIngredientTray(tray,id,true);
            CombineDecorations(tray);return tray;
        }
        private void CombineDecorations(Transform root)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();var remove=new List<GameObject>();
            foreach(MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {Renderer r=mf.GetComponent<Renderer>();if(!r||!mf.sharedMesh)continue;Material mat=r.sharedMaterial;if(!groups.ContainsKey(mat))groups[mat]=new List<CombineInstance>();groups[mat].Add(new CombineInstance{mesh=mf.sharedMesh,transform=root.worldToLocalMatrix*mf.transform.localToWorldMatrix});remove.Add(mf.gameObject);}
            foreach(var pair in groups){Mesh m=new Mesh{name=root.name+" combined"};m.CombineMeshes(pair.Value.ToArray(),true,true);meshes.Add(m);Transform g=Group(m.name,root);g.gameObject.AddComponent<MeshFilter>().sharedMesh=m;g.gameObject.AddComponent<MeshRenderer>().sharedMaterial=pair.Key;}
            foreach(GameObject go in remove){go.SetActive(false);Destroy(go);}
        }
        private Station MakeStation(Vector3 work,Vector3 stand,bool twoHands)
        {
            Transform right=Group("Right palm contact",cooking);right.position=work;right.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
            var s=new Station{stand=stand,right=right};
            if(twoHands){s.left=Group("Left palm contact",cooking);s.left.position=work+Vector3.left*.18f;s.left.rotation=right.rotation;}
            return s;
        }
        private StallInteractionTarget Target(Transform root,StallInteractionKind kind,string id,int lane,Vector3 label,Vector3 hitSize,float screenTolerance=-1)
        {
            var target=root.gameObject.AddComponent<StallInteractionTarget>();target.kind=kind;target.itemId=id;target.lane=lane;target.screenTolerance=screenTolerance;
            target.anchor=Group("HUD anchor",root);target.anchor.position=label;
            BoxCollider collider=root.gameObject.AddComponent<BoxCollider>();collider.size=hitSize;collider.center=new Vector3(0,hitSize.y*.35f,0);collider.isTrigger=true;target.BindHitCollider(collider);return target;
        }
        public void Configure(BusinessSession session)
        {
            if(activeSession!=null)activeSession.SaleCompleted-=OnSaleCompleted; // symmetric rebind; a stage serves one session
            activeSession=session;session.SaleCompleted+=OnSaleCompleted;
            RefreshDistrictEnvironment(session.DistrictIndex);
            for(int i=0;i<session.Lanes.Length;i++)
            {
                var lane=session.Lanes[i];float x=session.Lanes.Length==1?.91f:.64f+i*.57f;
                Transform dish=Group(BusinessSession.Recipes[lane.recipeIndex].name+" cookware",cooking);dish.position=new Vector3(x,cookingHeight+.005f,0);dishes.Add(dish);
                // PANTRY9：锅深按母菜系口径——现炒系（含简谱素炒饭/滑蛋/鲜肉小炒）
                // 与蛋炒饭同深，煎烙素饼与手抓饼同浅；汤/饮深锅不变。
                bool deep=lane.recipeIndex==3||lane.recipeIndex==5;float depth=deep?.23f:BusinessSession.ParentRecipeIndex(lane.recipeIndex)==0?.14f:.045f;
                bool useGeneratedWok=BusinessSession.ParentRecipeIndex(lane.recipeIndex)==0 && session.Equipment.Contains("V02");
                BuildCookwareVisual(dish,depth,useGeneratedWok,.25f);
                Transform f=Group("Actual food in cookware",dish);foodGroups.Add(f);BuildFood(f,lane.recipeIndex,depth);f.gameObject.SetActive(false);
                Transform flameGroup=Group("Lit gas ring",dish);flames.Add(flameGroup);
                if(!BusinessSession.Recipes[lane.recipeIndex].cold) // 冷制饮品（酸梅汤/柠檬茶/冰粉）不点火烧杯
                    for(int a=0;a<14;a++)
                    {float angle=a*Mathf.PI*2/14;Shape("Gas flame",flameGroup,new Vector3(Mathf.Sin(angle)*.15f,-.045f,Mathf.Cos(angle)*.15f),new Vector3(.027f,.06f,.027f),flame,PrimitiveType.Sphere);}CombineDecorations(flameGroup);flameGroup.gameObject.SetActive(false);
                for(int s=0;s<3;s++){var puff=Shape("Steam wisp",dish,new Vector3(0,.2f,0),Vector3.one*.035f,white,PrimitiveType.Sphere).transform;steam.Add(puff);puff.gameObject.SetActive(false);}
                // PREP12（合并组统一接口）：两拍现炒（BusinessSession.RequiresEggCrack，
                // 含蛋+米面的现炒菜）敲蛋下锅后进入“待下米饭”中间态——锅里只画
                // 少量明显蛋白+金黄蛋黄的生蛋液，一眼是“已下蛋、还没下米”，绝不
                // 画成成品炒饭。状态只读公开 KitchenLane.eggPrepPortions，不动计时。
                Transform eggPrep=Group("Egg prep in cookware",dish);eggPrepGroups.Add(eggPrep);
                // COLD12 机械修：每个已存在的 egg prep 根都预建隐藏小食品几何（不再
                // 仅限初始配置是蛋炒饭的锅），营业中换菜到两拍现炒菜后 Present 才有
                // 几何可切；不在此把 Controller 独立可视重新加回。
                BuildEggPrep(eggPrep);
                eggPrep.gameObject.SetActive(false);
                if(BusinessSession.Recipes[lane.recipeIndex].cold)
                {
                    // 冷制工位（柠檬茶/冰粉/酸梅汤）：锅具垫在杯托冰槽上（泡沫杯托+
                    // 冰蓝衬垫），不用火灶呈现冷饮调制；与热锅一眼可辨，冰槽常驻不随火。
                    Shape("Cold drink cup holder tray",dish,new Vector3(0,-.032f,0),new Vector3(.54f,.02f,.54f),white);
                    Shape("Ice trough insert",dish,new Vector3(0,-.021f,0),new Vector3(.44f,.012f,.44f),IcePale);
                }
                var station=MakeStation(new Vector3(x+.14f,1.20f,-.69f),new Vector3(x,0,-.97f),true);
                station.left.position=new Vector3(x-.05f,1.20f,-.69f);
                station.tool=Group("Spatula at cookware "+i,cooking);station.tool.position=station.right.position;
                if(BusinessSession.Recipes[lane.recipeIndex].id=="lemon_tea")
                {
                    // COLD12：柠檬茶明确冷制工位——冰槽杯托上斜靠真实木锤（手锤柠檬的
                    // 锤，不是锅铲），WorldHUD 状态沿用现有口径、冷制不点火。
                    Between("Wooden muddler handle",station.tool,new Vector3(0,0,-.06f),new Vector3(0,.05f,.16f),.022f,wood);
                    Shape("Wooden muddler head",station.tool,new Vector3(0,.05f,.20f),new Vector3(.05f,.09f,.05f),wood,PrimitiveType.Cylinder);
                }
                else
                {
                    Shape("Wood spatula grip",station.tool,Vector3.zero,new Vector3(.045f,.035f,.18f),wood);
                    Between("Steel spatula stem",station.tool,new Vector3(0,0,.08f),new Vector3(0,.15f,.49f),.025f,steel);
                    Shape("Steel spatula head",station.tool,new Vector3(0,.17f,.53f),new Vector3(.12f,.018f,.11f),steel);
                }
                station.toolGrip=Group("Actual spatula palm grip",station.tool);panStations.Add(station);
                Target(dish,StallInteractionKind.Cookware,"",i,dish.position+new Vector3(0,depth+.08f,.12f),new Vector3(.54f,depth+.12f,.54f));
            }
            SetLayers(transform);Physics.SyncTransforms();
        }
        // MENUFLEX10：换菜只重建该锅的锅内食物造型与菜名（锅体/站位/火焰/工具
        // 不变，物理工作位与场景布局完全不动）。新建食物组再销毁旧的，避免同帧
        // CombineDecorations 采到已标记销毁的网格；depth 口径与 Configure 一致。
        public void RebuildLaneFood(int laneIndex,BusinessSession session)
        {
            if(laneIndex<0||laneIndex>=foodGroups.Count||session==null||laneIndex>=session.Lanes.Length)return;
            int recipe=session.Lanes[laneIndex].recipeIndex;
            Transform oldFood=foodGroups[laneIndex];if(oldFood==null)return;
            Transform fresh=Group("Actual food in cookware",dishes[laneIndex]);
            bool deep=recipe==3||recipe==5;float depth=deep?.23f:BusinessSession.ParentRecipeIndex(recipe)==0?.14f:.045f;
            BuildFood(fresh,recipe,depth);fresh.gameObject.SetActive(false);
            foodGroups[laneIndex]=fresh;
            Destroy(oldFood.gameObject);
            if(laneIndex<dishes.Count&&dishes[laneIndex]!=null)dishes[laneIndex].name=BusinessSession.Recipes[recipe].name+" cookware";
        }
        public BusinessPresentationSaveData CaptureSavePresentation()
        {
            var seats=new List<BusinessGuestPlacementSaveData>();
            foreach(var guest in guests.Values)
            {
                StallOrder order=activeSession?.FindOrder(guest.id);
                // RISK7SCENE：已交付未收款（A01 待追回）的已 served 顾客必须随档保留
                // 其真实站位，读档后重建同一 Guest，债权锚点不丢。
                if(order!=null&&order.arrived&&!order.departed&&(!order.served||activeSession.HasPendingRiskOrder(order.id))&&guest.root&&guest.root.gameObject.activeInHierarchy)
                    seats.Add(new BusinessGuestPlacementSaveData{orderId=guest.id,slot=guest.slot});
            }
            seats.Sort((a,b)=>a.orderId.CompareTo(b.orderId));
            return new BusinessPresentationSaveData{hasVendorPose=player!=null,vendorPosition=player?player.position:Vector3.zero,vendorRotation=player?player.rotation:Quaternion.identity,arrivedGuests=seats.ToArray()};
        }
        // Restore is a scene-construction operation, never an in-play movement
        // shortcut. Completed departures are omitted; admitted customers retain
        // the exact model patience and their stable physical queue slots.
        public void RestoreSession(BusinessSession session,BusinessPresentationSaveData saved=null)
        {
            if(session==null||dishes.Count!=0||guests.Count!=0)throw new InvalidOperationException("RestoreSession requires a fresh initialized stage");
            Configure(session);guestAuditSession=session;
            if(saved!=null&&saved.hasVendorPose)
            {
                Vector3 p=saved.vendorPosition;Quaternion q=saved.vendorRotation;
                if(!FinitePose(p.x)||!FinitePose(p.y)||!FinitePose(p.z)||!FinitePose(q.x)||!FinitePose(q.y)||!FinitePose(q.z)||!FinitePose(q.w)||Quaternion.Dot(q,q)<.0001f)
                    throw new ArgumentException("Invalid saved vendor pose");
                player.SetPositionAndRotation(p,q.normalized);workActor.Cancel();
            }
            var seats=new Dictionary<int,int>();var occupied=new HashSet<int>();
            if(saved?.arrivedGuests!=null)foreach(var seat in saved.arrivedGuests)
            {
                StallOrder order=seat==null?null:session.FindOrder(seat.orderId);
                // 与下方重建过滤同口径：已 served 但待收款（A01）的顾客是合法存档座位。
                if(order==null||!order.arrived||order.departed||order.served&&!session.HasPendingRiskOrder(order.id)||seat.slot<0||seat.slot>=queueSlots.Length||seats.ContainsKey(seat.orderId)||!occupied.Add(seat.slot))throw new ArgumentException("Invalid saved customer queue slot");
                seats.Add(seat.orderId,seat.slot);
            }
            foreach(StallOrder order in session.Orders)
            {
                if(!order.arrived||order.departed)continue;
                // RISK7SCENE：原 Restore 过滤会丢掉已 served 但待收款的顾客（债权无锚点）。
                if(order.served&&!session.HasPendingRiskOrder(order.id))continue;
                int slot;if(!seats.TryGetValue(order.id,out slot)){slot=-1;for(int i=0;i<queueSlots.Length;i++)if(!occupied.Contains(i)){slot=i;occupied.Add(i);break;}}
                if(slot<0)throw new InvalidOperationException("Saved arrived customers exceed physical queue capacity");
                Guest guest=AddGuest(order.id,slot,true);if(guest==null)throw new InvalidOperationException("Saved customer queue position is obstructed: "+order.id);
                guests.Add(order.id,guest);
            }
            Present(session,0,true);Physics.SyncTransforms();
        }
        private static bool FinitePose(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        // PREP12：“待下米饭”中间态锅内容物——一颗金黄蛋黄+少量明显蛋白小块+
        // 搅开的蛋液粒孪生物体。 unmistakably 生蛋液下锅，不是成品，不带米饭堆。
        private void BuildEggPrep(Transform parent)
        {
            Shape("Golden egg yolk",parent,new Vector3(.045f,.032f,.02f),new Vector3(.09f,.026f,.09f),egg,PrimitiveType.Sphere);
            var rand=new System.Random(37);
            for(int i=0;i<5;i++){float a=(float)rand.NextDouble()*Mathf.PI*2,r=.055f+(float)rand.NextDouble()*.085f;Shape("Clear egg white blob",parent,new Vector3(Mathf.Sin(a)*r,.020f,Mathf.Cos(a)*r),new Vector3(.055f,.012f,.048f),white,PrimitiveType.Sphere);}
            for(int i=0;i<6;i++){float a=i*2.4f+.4f;Shape("Stirred egg curd bit",parent,new Vector3(Mathf.Sin(a)*.105f,.024f,Mathf.Cos(a)*.105f),new Vector3(.03f,.011f,.026f),egg,PrimitiveType.Sphere);}
        }
        // Replace the visual only; actual cookware targets, food and work positions remain stable.
        private void BuildCookwareVisual(Transform dish,float depth,bool useGeneratedWok,float fallbackRadius)
        {
            foreach(Transform old in dish)
                if(old.name=="Cookware visual"){old.gameObject.SetActive(false);Destroy(old.gameObject);}
            Transform visual=Group("Cookware visual",dish);
            Transform wokView=Group("Generated wok placement",visual);
            if(useGeneratedWok&&GeneratedEquipmentAssets.TryPopulate("V02",wokView))
            {wokView.localRotation=Quaternion.Euler(0,-90,0);wokView.localPosition=new Vector3(0,0,-.10f);SetLayers(visual);return;}
            Bowl(visual,fallbackRadius,depth,ink);
            Vector3 handleAt=new Vector3(0,1.20f-dish.position.y,-.69f);
            Between("Wood pan grip",visual,handleAt+new Vector3(0,.025f,.05f),handleAt+new Vector3(0,-.025f,-.05f),.055f,wood);
            Between("Angled steel pan handle",visual,new Vector3(0,.045f,-.20f),handleAt+new Vector3(0,.025f,.05f),.038f,steel);
        }
        private void Bowl(Transform parent,float radius,float depth,Material mat)
        {
            const int segments=40;var verts=new List<Vector3>();var tris=new List<int>();
            Vector2[] profile={new Vector2(.0f,0),new Vector2(.10f,0),new Vector2(radius*.72f,depth*.25f),new Vector2(radius,depth),new Vector2(radius+.014f,depth),new Vector2(radius*.78f,depth*.20f-.018f),new Vector2(.1f,-.02f),new Vector2(0,-.02f)};
            for(int j=0;j<profile.Length;j++)for(int i=0;i<=segments;i++){float a=i*Mathf.PI*2/segments;verts.Add(new Vector3(Mathf.Sin(a)*profile[j].x,profile[j].y,Mathf.Cos(a)*profile[j].x));}
            for(int j=0;j<profile.Length-1;j++)for(int i=0;i<segments;i++){int a=j*(segments+1)+i,b=a+segments+1;tris.Add(a);tris.Add(b);tris.Add(a+1);tris.Add(b);tris.Add(b+1);tris.Add(a+1);}
            Mesh mesh=new Mesh{name="Concave cookware"};mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
            Transform node=Group("Concave iron vessel",parent);node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;
        }
        // PANTRY9 简谱菜食物造型：素炒饭是纯米饭+葱花（绝不带蛋点，避免把素锅
        // 显示成带蛋成品）；素饼只有葱香烙点；滑蛋是蛋底+青菜；鲜肉小炒是肉块
        // 堆+葱花。与母菜共用锅型/材质，仅配料差异。
        private void BuildFood(Transform parent,int recipe,float depth)
        {
            if(recipe==0||recipe==1)
            {
                Shape("Rice mound",parent,new Vector3(0,.06f,0),new Vector3(.36f,.07f,.34f),rice,PrimitiveType.Sphere);
                var rand=new System.Random(22);for(int i=0;i<100;i++){float a=(float)rand.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rand.NextDouble())*.17f;Shape("Rice and toppings",parent,new Vector3(Mathf.Sin(a)*r,.085f+(1-r/.2f)*.025f,Mathf.Cos(a)*r),i%4==0?new Vector3(.035f,.02f,.035f):new Vector3(.017f,.013f,.031f),i%7==0?green:i%4==0?egg:rice,PrimitiveType.Sphere);}
            }
            else if(recipe==6)
            {
                Shape("Plain rice mound",parent,new Vector3(0,.06f,0),new Vector3(.36f,.07f,.34f),rice,PrimitiveType.Sphere);
                var rand=new System.Random(24);for(int i=0;i<70;i++){float a=(float)rand.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rand.NextDouble())*.17f;Shape("Plain rice grain",parent,new Vector3(Mathf.Sin(a)*r,.085f+(1-r/.2f)*.025f,Mathf.Cos(a)*r),new Vector3(.017f,.013f,.031f),rice,PrimitiveType.Sphere);}
                for(int i=0;i<14;i++){float a=i*2.4f;Shape("Scallion only",parent,new Vector3(Mathf.Sin(a)*.13f,.105f,Mathf.Cos(a)*.12f),new Vector3(.023f,.009f,.018f),green);}
            }
            else if(recipe==8)
            {
                Shape("Soft egg base",parent,new Vector3(0,.055f,0),new Vector3(.35f,.06f,.33f),egg,PrimitiveType.Sphere);
                for(int i=0;i<16;i++){float a=i*2.4f;Shape("Greens in egg",parent,new Vector3(Mathf.Sin(a)*.14f,.105f,Mathf.Cos(a)*.13f),new Vector3(.026f,.012f,.020f),green);}
                for(int i=0;i<8;i++){float a=i*2.4f+.6f;Shape("Egg curd",parent,new Vector3(Mathf.Sin(a)*.09f,.095f,Mathf.Cos(a)*.09f),new Vector3(.05f,.02f,.045f),rice,PrimitiveType.Sphere);}
            }
            else if(recipe==9)
            {
                var rand=new System.Random(29);for(int i=0;i<22;i++){float a=(float)rand.NextDouble()*Mathf.PI*2,r=Mathf.Sqrt((float)rand.NextDouble())*.15f;Shape("Bite size meat",parent,new Vector3(Mathf.Sin(a)*r,.075f+(1-r/.18f)*.03f,Mathf.Cos(a)*r),new Vector3(.05f,.035f,.045f),sausage,PrimitiveType.Sphere);}
                for(int i=0;i<10;i++){float a=i*2.4f;Shape("Scallion on meat",parent,new Vector3(Mathf.Sin(a)*.12f,.115f,Mathf.Cos(a)*.11f),new Vector3(.022f,.009f,.017f),green);}
            }
            else if(recipe==2)
            {
                Shape("Folded golden flatbread",parent,new Vector3(0,.055f,0),new Vector3(.36f,.035f,.32f),dough,PrimitiveType.Cylinder);
                for(int n=0;n<18;n++){float a=n*2.4f,r=.04f+(n%4)*.032f;Shape("Flatbread scallion and toast marks",parent,new Vector3(Mathf.Sin(a)*r,.096f,Mathf.Cos(a)*r),new Vector3(.023f,.008f,.038f),n%3==0?green:sausage);}
            }
            else if(recipe==7)
            {
                Shape("Folded plain flatbread",parent,new Vector3(0,.055f,0),new Vector3(.36f,.035f,.32f),dough,PrimitiveType.Cylinder);
                for(int n=0;n<12;n++){float a=n*2.4f,r=.04f+(n%4)*.032f;Shape("Scallion toast mark only",parent,new Vector3(Mathf.Sin(a)*r,.096f,Mathf.Cos(a)*r),new Vector3(.023f,.008f,.038f),green);}
            }
            else if(recipe==3)
            {
                Shape("Golden soup",parent,new Vector3(0,depth*.64f,0),new Vector3(.37f,.017f,.37f),dough,PrimitiveType.Cylinder);
                for(int i=0;i<6;i++){float a=i*2.4f;Shape("Fish ball",parent,new Vector3(Mathf.Sin(a)*.125f,depth*.64f+.025f,Mathf.Cos(a)*.12f),new Vector3(.075f,.065f,.075f),rice,PrimitiveType.Sphere);}
                for(int i=0;i<9;i++)Shape("Soup scallion",parent,new Vector3(Mathf.Sin(i*2.4f)*.12f,depth*.64f+.055f,Mathf.Cos(i*2.4f)*.12f),new Vector3(.023f,.009f,.018f),green);
            }
            else if(recipe==4)
            {
                for(int row=0;row<3;row++)
                {
                    float x=(row-1)*.09f;Between("Bamboo skewer",parent,new Vector3(x,.065f,-.21f),new Vector3(x,.065f,.16f),.008f,wood);
                    for(int piece=0;piece<4;piece++)Shape("Fried sausage on skewer",parent,new Vector3(x,.085f,-.09f+piece*.065f),new Vector3(.07f,.065f,.058f),sausage,PrimitiveType.Sphere);
                }
            }
            else if(recipe==10)
            {
                // FLAVOR11 暴打手锤柠檬茶：P08 封口杯+琥珀茶汤+方冰+柠檬片，
                // 木锤斜靠杯边（hammer 手感），与酸梅汤/冰粉一眼可辨。
                Shape("Seal cup",parent,new Vector3(0,.075f,0),new Vector3(.13f,.15f,.13f),white,PrimitiveType.Cylinder);
                Shape("Lemon tea liquid",parent,new Vector3(0,.148f,0),new Vector3(.115f,.014f,.115f),TeaAmber,PrimitiveType.Cylinder);
                for(int i=0;i<3;i++)Shape("Ice cube",parent,new Vector3((i-1)*.04f,.163f,(i%2-.5f)*.05f),new Vector3(.035f,.03f,.035f),IcePale,PrimitiveType.Cube);
                Shape("Lemon slice",parent,new Vector3(.05f,.168f,.04f),new Vector3(.05f,.008f,.05f),egg,PrimitiveType.Cylinder);
                Between("Wooden muddler",parent,new Vector3(-.16f,.045f,-.07f),new Vector3(-.05f,.045f,.11f),.018f,wood);
            }
            else if(recipe==11)
            {
                // FLAVOR11 红糖玫瑰冰粉：冰碗+红糖浆+冰粉小块+玫瑰花瓣+小勺，
                // 与柠檬茶的封口杯器型完全不同。
                Bowl(parent,.14f,.075f,cream);
                Shape("Brown sugar syrup",parent,new Vector3(0,.048f,0),new Vector3(.115f,.012f,.115f),JellySyrup,PrimitiveType.Cylinder);
                var jb=new System.Random(31);for(int i=0;i<7;i++){float a=(float)jb.NextDouble()*Mathf.PI*2,r=.02f+(float)jb.NextDouble()*.07f;Shape("Ice jelly cube",parent,new Vector3(Mathf.Sin(a)*r,.062f+(float)jb.NextDouble()*.02f,Mathf.Cos(a)*r),new Vector3(.03f,.024f,.03f),IcePale,PrimitiveType.Cube);}
                for(int i=0;i<6;i++){float a=i*2.4f;Shape("Rose petal",parent,new Vector3(Mathf.Sin(a)*.06f,.082f,Mathf.Cos(a)*.06f),new Vector3(.018f,.005f,.026f),RosePetal,PrimitiveType.Sphere);}
                Between("Dessert spoon",parent,new Vector3(.15f,.04f,-.10f),new Vector3(.05f,.09f,.02f),.012f,steel);
            }
            else
            {
                Shape("Plum drink",parent,new Vector3(0,depth*.66f,0),new Vector3(.37f,.022f,.37f),rust,PrimitiveType.Cylinder);
                for(int i=0;i<5;i++){float a=i*2.4f;Shape("Floating sour plum",parent,new Vector3(Mathf.Sin(a)*.115f,depth*.66f+.023f,Mathf.Cos(a)*.11f),new Vector3(.047f,.035f,.045f),wood,PrimitiveType.Sphere);}
                for(int i=0;i<18;i++){float a=i*2.4f;Shape("Osmanthus petals",parent,new Vector3(Mathf.Sin(a)*(.06f+i%4*.028f),depth*.66f+.025f,Mathf.Cos(a)*(.06f+i%4*.028f)),new Vector3(.009f,.004f,.014f),egg);}
            }
            if(recipe==1)for(int strip=0;strip<10;strip++)
                Shape("Shredded pork topping",parent,new Vector3((strip%5-2)*.048f,.125f,-.035f+(strip/5)*.06f),new Vector3(.035f,.026f,.13f),sausage).transform.localRotation=Quaternion.Euler(0,(strip%3-1)*18,0);
            CombineDecorations(parent);
        }
        private void BuildCamera()
        {
            View=Group("Operating scene camera",transform).gameObject.AddComponent<Camera>();View.orthographic=true;
            View.nearClipPlane=.1f;View.farClipPlane=60;View.cullingMask=1<<Layer;View.clearFlags=CameraClearFlags.SolidColor;View.backgroundColor=CampaignUI.Pale;
            foreach(StallInteractionTarget target in GetComponentsInChildren<StallInteractionTarget>())
            {operatingLeft=Mathf.Min(operatingLeft,target.WorldAnchor.x-.30f);operatingRight=Mathf.Max(operatingRight,target.WorldAnchor.x+.30f);}
            FrameOperatingCounter();View.allowMSAA=true;
            Light key=Group("Soft morning sun",transform).gameObject.AddComponent<Light>();districtKeyLight=key;key.type=LightType.Directional;key.transform.rotation=Quaternion.Euler(48,-32,0);key.color=new Color(1,.93f,.83f);key.intensity=1.1f;key.shadows=LightShadows.Soft;key.shadowStrength=.55f;key.shadowBias=.04f;key.shadowNormalBias=.25f;key.cullingMask=1<<Layer;
        }
        private void FrameOperatingCounter()
        {
            if(View==null)return;
            float aspect=View.aspect;
            if(Mathf.Abs(aspect-framedAspect)<.0001f)return;
            framedAspect=aspect;
            // Fixed 2.5D workbench view. Reframe only on viewport changes;
            // neither the cursor, an order nor a cooking action moves the camera.
            Vector3 look=new Vector3((operatingLeft+operatingRight)*.5f,.80f,.90f);
            View.transform.position=look+new Vector3(-.70f,7.80f,-8.60f);
            View.transform.LookAt(look);
            View.orthographicSize=Mathf.Max(2.20f,(operatingRight-operatingLeft+.55f)/(2f*Mathf.Max(.6f,aspect)*.91f));
        }
        public bool IsAtCookware(int lane)
        {
            if(lane<0||lane>=panStations.Count||!player)return false;Station p=panStations[lane];
            Vector3 facing=p.work-player.position;facing.y=0;
            Transform origin=Find(player,"PlayerOrigin"),forward=Find(player,"PlayerForward");
            Vector3 direction=forward&&origin?forward.position-origin.position:player.forward;direction.y=0;
            return Vector3.Distance(player.position,p.stand)<.12f&&Vector3.Angle(direction,facing)<15;
        }
        public void ApproachCookware(int lane,Action finished=null)
        {
            if(WorkActorBusy||lane<0||lane>=panStations.Count){finished?.Invoke();return;}
            if(IsAtCookware(lane)){finished?.Invoke();return;}
            sequenceBusy=true;Station p=panStations[lane];
            if(!workActor.MoveTo(p.stand,p.work,()=>{sequenceBusy=false;finished?.Invoke();},WorkRoute(player.position,p.stand)))sequenceBusy=false;
        }
        private Vector3[] WorkRoute(Vector3 from,Vector3 to)
        {
            Vector3 delta=to-from;delta.y=0;
            if(delta.magnitude<.001f||!Physics.CapsuleCast(from+Vector3.up*.29f,from+Vector3.up*1.40f,.25f,delta.normalized,delta.magnitude+.012f,SolidMask,QueryTriggerInteraction.Ignore))return null;
            float front=Mathf.Min(-1.15f,Mathf.Min(from.z,to.z));
            return new[]{new Vector3(from.x,0,front),new Vector3(to.x,0,front)};
        }
        private float TravelEstimate(Vector3 from,Station to)
        {
            Vector3[] route=WorkRoute(from,to.stand);float distance=0;Vector3 previous=from;
            if(route!=null)foreach(Vector3 point in route){distance+=Vector3.Distance(previous,point);previous=point;}
            distance+=Vector3.Distance(previous,to.stand);
            // Includes starting/stopping and the approach-to-contact turn. Kept
            // conservative for reserving a genuine green-window hand contact.
            return distance<.015f?.18f:distance/workActor.walkSpeed+workActor.walkSpeed/workActor.acceleration+.85f;
        }
        public float EstimateApproachSeconds(int lane)
        {return lane<0||lane>=panStations.Count?0:TravelEstimate(WorkActorPosition,panStations[lane]);}
        public float EstimateInteractionSeconds(StallInteractionKind kind,int lane,string itemId)
        {
            if(lane<0||lane>=panStations.Count)return 0;Station source=null;
            if(kind==StallInteractionKind.Ingredient)ingredients.TryGetValue(itemId??"",out source);
            else if(kind==StallInteractionKind.Packaging)packages.TryGetValue(itemId??"",out source);
            else if(kind==StallInteractionKind.Seasoning)
            {
                if(itemId!=null&&!flavorStations.TryGetValue(itemId,out source))source=seasoning; // S01/S02/S05 各有独立工位，基础油盐走原槽
            }
            Station pan=panStations[lane];
            if(source==null)return TravelEstimate(WorkActorPosition,pan)+1.05f;
            if(kind==StallInteractionKind.Packaging)return TravelEstimate(WorkActorPosition,pan)+.70f+TravelEstimate(pan.stand,source)+.70f;
            return TravelEstimate(WorkActorPosition,source)+(kind==StallInteractionKind.Seasoning?.45f:.55f)+TravelEstimate(source.stand,pan)+.70f;
        }
        public float EstimateReturnAfterInteractionSeconds(StallInteractionKind kind,int lane,string itemId,int destinationLane)
        {
            if(lane<0||lane>=panStations.Count||destinationLane<0||destinationLane>=panStations.Count)return 0;
            Station end=panStations[lane];
            if(kind==StallInteractionKind.Packaging&&packages.TryGetValue(itemId??"",out Station package))end=package;
            return TravelEstimate(end.stand,panStations[destinationLane]);
        }
        // serveOrderId/serveOrderResolver/deliveryFailed 构成封装出餐的交付合同：
        // Scoop（盛出）与 Pack（装袋）接触帧都不 commit；装袋完成后解析实际
        // 顾客（优先 serveOrderId，已离开则由 resolver 改绑等待最久的同菜订
        // 单），摊主真实走到柜台面向该顾客的服务侧，VendorWorkAction.Serve
        // 的递餐接触帧才触发一次性 commit（Session.Serve）。解析不到合法顾客
        // 时走 deliveryFailed（不扣包材、不收钱、食物留在锅里），序列正常收束。
        // 旧 5 参 PerformInteraction 是既有公开契约（旧测试/调用按此签名），
        // 保持原样；带 3 个交付参数的新逻辑走 PerformDeliveryInteraction。
        public void PerformInteraction(StallInteractionKind kind,int lane,string itemId,Action contact,Action finished=null)
        {PerformDeliveryInteraction(kind,lane,itemId,contact,finished,-1,null,null);}
        public void PerformDeliveryInteraction(StallInteractionKind kind,int lane,string itemId,Action contact,Action finished,int serveOrderId,Func<int> serveOrderResolver,Action deliveryFailed)
        {
            if(WorkActorBusy||lane<0||lane>=panStations.Count){finished?.Invoke();return;}
            sequenceBusy=true;sequenceFinished=finished;Station pan=panStations[lane];
            Action done=CompleteWork;
            Action panWork=()=>
            {
                // PANTRY9：翻面动作按母菜系——煎烙系（手抓饼/素饼）用 Flip，
                // 其余手动火候（含现炒系简谱菜）用 StirFry。FLAVOR11：冷制饮品
                // 家族（ParentRecipeIndex==5，酸梅汤/柠檬茶/冰粉）无翻炒，接触
                // 动作用 Scoop（舀/锤/盛），与热锅手感明显区分。
                VendorWorkAction action=PanWorkAction(kind,lane);
                Execute(action,pan,kind==StallInteractionKind.Cookware?1.05f:.70f,()=>{contact?.Invoke();workActor.ReleaseVisualProp();},done);
            };
            if(kind==StallInteractionKind.Ingredient&&ingredients.TryGetValue(itemId,out Station ingredient))PickProp(kind,itemId,ingredient,.55f,panWork);
            else if(kind==StallInteractionKind.Packaging&&packages.TryGetValue(itemId,out Station pack))
            {
                Execute(VendorWorkAction.Scoop,pan,.70f,()=>
                {
                    carriedProp=Group("Cooked portion to packaging",cooking);carriedProp.position=pan.left.position;
                    Bowl(carriedProp,.115f,.025f,cream);
                    Transform portion=Group("Portion",carriedProp);BuildFood(portion,activeSession.Lanes[lane].recipeIndex,.04f);portion.localScale=Vector3.one*.55f;
                    Transform grip=Group("Left serving grip",carriedProp);grip.rotation=pan.left.rotation;
                    if(!workActor.AttachVisualProp(carriedProp,grip,true))workActor.Abort("Cooked serving was not at the left palm contact");
                },()=>Execute(VendorWorkAction.Pack,pack,.70f,null,()=>DeliverToCustomer(lane,serveOrderId,serveOrderResolver,contact,deliveryFailed,done)));
            }
            else if(kind==StallInteractionKind.Seasoning)
            {
                Station source=seasoning;
                if(itemId!=null&&!flavorStations.TryGetValue(itemId,out source))source=seasoning;
                PickProp(kind,itemId,source,.45f,panWork);
            }
            else panWork();
        }
        // 装袋完成后的交付段：解析顾客 → 走到其柜台服务侧 → Serve 递餐接触帧
        // commit。顾客已走/解析失败：收束序列、交回道具、不 commit。
        private void DeliverToCustomer(int lane,int serveOrderId,Func<int> serveOrderResolver,Action contact,Action deliveryFailed,Action done)
        {
            int orderId=serveOrderId;
            if(!IsOrderServable(orderId)&&serveOrderResolver!=null)orderId=serveOrderResolver();
            if(orderId>=0&&TryGetGuestServicePoint(orderId,out Vector3 stand,out Vector3 hand))
            {
                Station serve=TransientServiceStation(stand,hand);
                Execute(VendorWorkAction.Serve,serve,.75f,()=>{contact?.Invoke();workActor.ReleaseVisualProp();},()=>{DisposeTransientServiceStation(serve);done();});
                return;
            }
            workActor.ReleaseVisualProp();
            deliveryFailed?.Invoke();
            done();
        }
        private bool IsOrderServable(int orderId)
        {
            if(orderId<0||activeSession==null)return false;
            StallOrder order=activeSession.FindOrder(orderId);
            return order!=null&&order.arrived&&!order.served&&!order.departed;
        }
        // 顾客柜台服务侧：摊主站在柜台前（与锅位同一排），递餐手点落在
        // 台面朝向该顾客的边缘——真实递餐距离，不缩碰撞、不传送演员。
        // 站位 z 必须让演员胶囊（半径 .25）避开 Solid_Ingredient shelf 前立面
        // （z=-0.82，见 Cook3RouteDiagnosis diag4 证据：z=-1.02 时胶囊嵌入
        // 货架 5cm 导致 Serve 腿永远 blocked）；手点按 diag5 的接触误差证据
        // （right=0.109m 超伸）向右侧手掌可及方向回移，保持肩-手 3D 距离在
        // 臂展（VENDOR_RIG_DIMENSIONS 实测 .436）与接触容差 .025 之内。
        public bool TryGetGuestServicePoint(int orderId,out Vector3 stand,out Vector3 hand)
        {
            stand=Vector3.zero;hand=Vector3.zero;
            if(!guests.TryGetValue(orderId,out Guest guest)||guest.root==null||!guest.root.gameObject.activeInHierarchy)return false;
            float x=Mathf.Clamp(guest.root.position.x,-.30f,2.50f);
            stand=new Vector3(x,0,-1.10f);
            hand=new Vector3(x+.06f,1.08f,-.78f);
            return true;
        }
        private Station TransientServiceStation(Vector3 stand,Vector3 work)
        {
            Transform right=Group("Transient serve hand contact",cooking);right.position=work;right.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);
            return new Station{stand=stand,right=right};
        }
        private static void DisposeTransientServiceStation(Station station)
        {if(station!=null&&station.right!=null)Destroy(station.right.gameObject);}
        public void CancelWork()
        {
            CancelVendorService();
            if(workActor)workActor.Cancel();sequenceFinished=null;CompleteWork();
        }
        private void CompleteWork()
        {
            sequenceBusy=false;bool retainHeld=activeSession?.HeldMeal!=null&&HasHeldMealInHand;
            if(workActor&&!retainHeld)workActor.ReleaseVisualProp();RestoreCounterMealVisual();if(readySauceJar)foreach(Renderer renderer in readySauceJar.GetComponentsInChildren<Renderer>())renderer.enabled=true;
            if(hiddenCarriedSource!=null){foreach(Renderer renderer in hiddenCarriedSource.GetComponentsInChildren<Renderer>())renderer.enabled=true;hiddenCarriedSource=null;}if(carriedProp){Destroy(carriedProp.gameObject);carriedProp=null;}
            RestoreHeldMealVisual(activeSession);
            Action callback=sequenceFinished;sequenceFinished=null;callback?.Invoke();
        }
        private void PickProp(StallInteractionKind kind,string item,Station station,float seconds,Action then)
        {
            carriedProp=Group("Held "+item,cooking);carriedProp.position=station.work;
            if(kind==StallInteractionKind.Ingredient)
            {Bowl(carriedProp,.095f,.055f,cream);Shape("Small portion in prep bowl",carriedProp,new Vector3(0,.04f,.015f),new Vector3(.15f,.025f,.14f),item=="egg"?egg:item=="dough"?dough:rice,PrimitiveType.Sphere);}
            else if(kind==StallInteractionKind.Packaging)
            {Shape("Handheld package",carriedProp,new Vector3(0,.035f,.055f),new Vector3(.23f,.055f,.17f),item=="P04"?dough:white);}
            else
            {
                // FLAVOR11：拿起哪瓶就隐藏哪瓶原瓶（风味瓶或基础油盐瓶），手里是
                // 同款小瓶；CompleteWork 统一恢复显示。替玩家选择的自动调味仍走
                // 原基础 jar，不猜具体风味。
                Transform source=item!=null&&flavorBottles.TryGetValue(item,out Transform b)?b:readySauceJar;
                if(source!=null){hiddenCarriedSource=source;foreach(Renderer renderer in source.GetComponentsInChildren<Renderer>())renderer.enabled=false;}
                if(item!=null&&flavorBottles.ContainsKey(item)){Transform held=BuildFlavorBottle(item,carriedProp,Vector3.zero,false);held.localScale=Vector3.one*.8f;}
                else {Shape("Handheld sauce jar",carriedProp,new Vector3(0,.04f,.035f),new Vector3(.09f,.08f,.09f),rust,PrimitiveType.Cylinder);Shape("Jar top",carriedProp,new Vector3(0,.13f,.035f),new Vector3(.095f,.015f,.095f),wood,PrimitiveType.Cylinder);}
            }
            Transform grip=Group("Handheld contact",carriedProp);grip.rotation=station.right.rotation;
            Execute(VendorWorkAction.PickIngredient,station,seconds,()=>{if(!workActor.AttachVisualProp(carriedProp,grip))workActor.Abort("Held prop was not at the reached palm contact");},then);
        }
        private void BuildDishIcons()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
            Transform preview=Group("Isolated menu dish render",transform);preview.position=new Vector3(40,0,0);
            Camera camera=Group("Dish icon camera",transform).gameObject.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=.30f;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.transform.position=preview.position+new Vector3(-.3f,.85f,-.85f);camera.transform.LookAt(preview.position+Vector3.up*.07f);camera.nearClipPlane=.01f;camera.farClipPlane=5;
            var target=new RenderTexture(192,192,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;
            foreach(Light light in GetComponentsInChildren<Light>())light.cullingMask|=1<<30;
            for(int i=0;i<BusinessSession.Recipes.Length;i++)
            {
                Transform plate=Group("Dish icon "+i,preview);Bowl(plate,.25f,.055f,cream);BuildFood(plate,i,.09f);
                foreach(Transform t in plate.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                camera.Render();RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
                var texture=new Texture2D(192,192,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,192,192),0,0);texture.Apply();RenderTexture.active=previous;
                iconTextures.Add(texture);dishIcons[i]=Sprite.Create(texture,new Rect(0,0,192,192),new Vector2(.5f,.5f),100);
                plate.gameObject.SetActive(false);Destroy(plate.gameObject);
            }
            camera.targetTexture=null;target.Release();Destroy(target);Destroy(camera.gameObject);Destroy(preview.gameObject);
            foreach(Light light in GetComponentsInChildren<Light>())light.cullingMask=1<<Layer;
        }
        private void Execute(VendorWorkAction action,Station station,float duration,Action contact,Action finished)
        {
            bool tool=action==VendorWorkAction.StirFry||action==VendorWorkAction.Flip||action==VendorWorkAction.Clean||action==VendorWorkAction.Scoop;
            var target=new VendorWorkTarget{standPoint=station.stand,workPoint=station.facingPoint??station.work,rightGrip=station.right,leftGrip=station.left,useLeftHand=station.left!=null,route=WorkRoute(player.position,station.stand),heldTool=tool?station.tool:null,toolGrip=tool?station.toolGrip:null};
            if(!workActor.Perform(action,target,duration,contact,()=>{if(workActor.LastFailure!=""){sequenceBusy=false;return;}finished?.Invoke();}))sequenceBusy=false;
        }
        // A completed sale is the only legitimate receipt signal: it fires once
        // per Serve, after the accounts and Revision are final. Lost customers,
        // failed/repeated Serve and restored saves never raise it, so nothing
        // here replays or guesses from cash deltas.
        private void OnSaleCompleted(int orderId,int recipeIndex,int paidCents,bool shortSeasoning)
        {
            if(guests.TryGetValue(orderId,out Guest g)&&g.root!=null&&g.root.gameObject.activeInHierarchy&&g.presentation!=null&&!g.leaving)
                g.presentation.BeginReceive(shortSeasoning);
        }
        // ── RISK7SCENE：A01 复用已拿到餐的真实顾客 ──
        // Stage 拥有的顾客适配器：Director 经 spawn 回调拿到它；成功/失败表现走
        // 既有顾客流（SaleCompleted 重放 → BeginReceive 接餐后正常离场；坏账后
        // leaving 恢复 → 走既有离场批次与通道，绝不穿台）。Dispose 只解绑归还，
        // body 由 Stage 统一销毁（Director 是唯一 Dispose 调用方，View 不碰）。
        private sealed class RiskGuestAdapter : IStallRiskActor
        {
            public int OrderId { get; }
            private Guest guest; // Dispose 后置空（归还语义），不作 readonly
            private Transform anchor;
            public bool Disposed { get; private set; }
            // 类私有、构造 public：与 private 嵌套类 Guest 的可访问域一致，不再
            // 出现 CS0051；外部类经 public 构造实例化后一律以 IStallRiskActor
            // 返回（Stage.TakeRiskActor 工厂是唯一出口）。
            public RiskGuestAdapter(int orderId,Guest guest){OrderId=orderId;this.guest=guest;if(guest!=null)guest.riskAdapter=this;}
            public Transform Anchor
            {
                get
                {
                    if(guest==null||guest.root==null)return null;
                    if(anchor==null)
                    {
                        var go=new GameObject("RiskAnchor");go.transform.SetParent(guest.root,false);
                        go.transform.position=guest.root.position+Vector3.up*1.62f;anchor=go.transform;
                    }
                    return anchor;
                }
            }
            public void OnInterceptSucceeded(int collectCents){} // 接餐/到账浮字由 SaleCompleted 重放统一驱动
            public void OnEscaped(){} // 坏账后由 Present 的 leaving 恢复走既有离场流
            public void Tick(float dt){} // Stage 拥有的真实顾客由 Stage 驱动
            public void Dispose()
            {
                Disposed=true; // 只归还：不 Destroy body（Stage 统一销毁）
                if(guest!=null&&guest.riskAdapter==this)guest.riskAdapter=null;
                guest=null;anchor=null;
            }
        }
        // 最近交给 Director 的适配器（Controller 在 Attach 成功后据此 PresentExistingActor）。
        public IStallRiskActor LastRiskActor { get; private set; }
        // RISK7SCENE：风险采纳闸门前置条件——该订单背后必须有一个真实在场、
        // 可呈现的顾客，否则不采纳（绝不为一个风险事件去新生第二个同 ID 顾客）。
        public bool HasLiveGuest(int orderId)
            =>guests.TryGetValue(orderId,out Guest g)&&g.root!=null&&g.root.gameObject.activeInHierarchy;
        // 采纳即让顾客真实拿到餐（接餐表现只播一次；追回时 SaleCompleted 重放
        // 会被 presentation 的一次性守卫挡住）。shortSeasoning 取订单保存的事实。
        public bool BeginGuestReceive(int orderId)
        {
            if(!guests.TryGetValue(orderId,out Guest g)||g.presentation==null)return false;
            StallOrder order=activeSession!=null?activeSession.FindOrder(orderId):null;
            g.presentation.BeginReceive(order!=null&&order.shortSeasoning);
            return true;
        }
        // 读档恢复：只恢复拿餐后的静态表现（包挂手上），不补播实际接餐动作。
        public bool RestoreGuestReceive(int orderId)
            =>guests.TryGetValue(orderId,out Guest g)&&g.presentation!=null&&g.presentation.RestoreStaticReceive();
        // 验收/断言用：该顾客手上是否已有收到的餐包。
        public bool GuestHasReceivedPackage(int orderId)
            =>guests.TryGetValue(orderId,out Guest g)&&g.presentation!=null&&g.presentation.HasReceivedPackage;
        // Director.spawn 回调：返回收到餐的真实顾客适配器；顾客不在场 → null（Attach 假，走 View fallback）。
        public IStallRiskActor TakeRiskActor(StallRiskEvent e)
        {
            if(e==null)return null;
            var last=LastRiskActor as RiskGuestAdapter;
            if(last!=null&&!last.Disposed&&last.OrderId==e.OrderId)return last;
            if(!guests.TryGetValue(e.OrderId,out Guest g)||g.root==null||!g.root.gameObject.activeInHierarchy)return null;
            var adapter=new RiskGuestAdapter(e.OrderId,g);LastRiskActor=adapter;return adapter;
        }
        // View fallback（仅 spawn 返回 null 的异常路径）用：入场脚印与柜台参考点。
        // RISK7SCENE：生产接线已不再使用 fallback 演员（禁止为风险事件新生第二个
        // 同 ID 顾客）；保留坐标仅为兼容旧测试/调试入口。
        public Vector3 RiskFallbackSpawn=>new Vector3(6.15f,0,3.22f);
        public Vector3 CounterPoint=>new Vector3(1.05f,.95f,0);
        // World anchor used by the HUD's per-sale payment popup; false when the
        // guest body is already gone (fallback is the vendor counter).
        public bool TryGetGuestAnchor(int orderId,out Vector3 world)
        {
            world=Vector3.zero;
            if(guests.TryGetValue(orderId,out Guest g)&&g.root!=null&&g.root.gameObject.activeInHierarchy){world=g.root.position+Vector3.up*1.18f;return true;}
            return false;
        }
        public void Present(BusinessSession session,float dt,bool paused)
        {
            FrameOperatingCounter();
            if(!paused)time+=dt;workActor.Tick(dt,paused);
            guestAuditSession=session;
            if(guestNavigation!=null)guestNavigation.BeginFrame();
            bool entering=false,departing=false;
            // Opposing slot merges cannot share this physically narrow rear lane.
            // Finish an inflow batch, then let all departures flow left together.
            foreach(StallOrder order in session.Orders)
                if(guests.TryGetValue(order.id,out Guest existing)&&existing.root.gameObject.activeInHierarchy)
                {
                    // RISK7SCENE：待收款（A01）的已 served 顾客必须留在原地可点，
                    // 不能因 served=true 直接退场；追回/坏账后由 SaleCompleted/债权
                    // 核销恢复 leaving，走既有离场批次与通道。
                    bool pendingRisk=session.HasPendingRiskOrder(order.id);
                    existing.leaving=(order.served&&!pendingRisk)||order.departed;
                    entering|=!existing.arrived;departing|=existing.leaving;
                }
            if(!paused)
            {
                if(guestExitBatch.FinishIfClear(DepartureState))guestOutflow=false;
                if(!guestOutflow&&departing&&!entering)
                {
                    var batch=new List<NpcDepartureCandidate>();
                    foreach(var candidate in guests.Values)
                        if(candidate.root.gameObject.activeInHierarchy&&candidate.arrived&&candidate.leaving)
                            batch.Add(new NpcDepartureCandidate(candidate.id,QueuePoints[candidate.slot].x,candidate.agent.Profile.Radius));
                    guestOutflow=guestExitBatch.TryBegin(batch);
                }
                if(guestOutflow&&guestExitBatch.TryGrant(DepartureState,out int departureId))
                {Guest permitted=guests[departureId];permitted.departureStarted=true;permitted.routeAt=0;}
                if(!guestOutflow&&!departing&&guestNavigation!=null)
                    foreach(StallOrder order in session.Orders)
                    {
                        if(order.served||order.departed||guests.ContainsKey(order.id))continue;
                        Guest added=AddGuest(order.id);if(added!=null)guests[order.id]=added;
                        break; // A second body cannot be born on the same entry footprint.
                    }
            }
            if(!paused)TickServiceVisit(dt);
            foreach(StallOrder order in session.Orders)
            {
                if(!guests.TryGetValue(order.id,out Guest g)||!g.root.gameObject.activeInHierarchy)continue;
                g.animation.enabled=!paused;
                if(IsVisiting(g))continue;
                g.walking=false;
                if(g.presentation!=null)g.presentation.Tick(paused?0f:dt);
                if(paused)continue;
                bool canDepart=guestOutflow&&guestExitBatch.HasGranted(g.id)&&(g.presentation==null||g.presentation.CanDepart);
                bool canEnter=!guestOutflow&&!g.arrived;
                if(!canDepart&&!canEnter)
                {
                    if(g.leaving)g.flowWaitSeconds+=dt;
                    if(g.arrived){Vector3 gaze=player.position-g.root.position;gaze.y=0;g.root.rotation=Quaternion.RotateTowards(g.root.rotation,Quaternion.LookRotation(gaze),dt*100);}
                    Play(g,false);continue;
                }
                Vector3 slot=QueuePoints[g.slot];
                g.target=canDepart?(g.routeAt==0?new Vector3(slot.x,0,3.22f):new Vector3(-5.3f,0,3.22f)):
                    g.routeAt==0?new Vector3(slot.x,0,3.22f):slot;
                float distance=guestNavigation.Step(g.agent,g.target,1.32f,dt);g.walking=distance>.0002f;
                if(guestNavigation.HasArrived(g.agent,g.target))
                {
                    if(g.routeAt==0)g.routeAt=1;
                    else if(canDepart)
                    {
                        if(queueSlots[g.slot]==g)queueSlots[g.slot]=null;
                        guestNavigation.Remove(g.agent);g.root.gameObject.SetActive(false);
                    }
                    else if(!g.arrived){g.arrived=true;if(!g.leaving)CustomerArrived?.Invoke(order.id);}
                }
                Play(g,g.walking);
            }
            for(int i=0;i<dishes.Count;i++)
            {
                KitchenLane lane=session.Lanes[i];UpdateFreeLaneVisual(i,lane);
                bool cold=LaneIsCold(lane);bool heat=!cold&&(lane.step==KitchenStep.Heating||lane.step==KitchenStep.Utensil);
                bool hasFreeInputs=FreeLaneHasInputs(lane);
                foodGroups[i].gameObject.SetActive(lane.batchRemaining>0||hasFreeInputs);flames[i].gameObject.SetActive(heat);
                // PREP12：公开 KitchenLane.eggPrepPortions>0 = 已扣蛋、待下米饭中间态；
                // 只驱动这组生蛋液可视，不碰批次/计时。与 batchRemaining 互斥（校验保证）。
                if(i<eggPrepGroups.Count)eggPrepGroups[i].gameObject.SetActive(lane.eggPrepPortions>0);
                for(int s=0;s<3;s++){Transform puff=steam[i*3+s];puff.gameObject.SetActive(heat);if(paused)continue;float t=(time*.65f+s/3f)%1;puff.localPosition=new Vector3(Mathf.Sin(t*5+s)*.07f,.18f+t*.45f,Mathf.Cos(t*4+s)*.05f);puff.localScale=new Vector3(.025f+t*.035f,.04f+t*.06f,.025f+t*.035f);}
            }
            // FLAVOR11：风味瓶视窗液芯跟随真实库存（S01/S05 满瓶 40 份、S02 30 份，
            // 与采购B表份数一致）；库存为 0 时液芯只剩一条底痕。
            foreach(var pair in flavorFills)
            {
                int stock=activeSession!=null?activeSession.Count(pair.Key):0;
                float frac=Mathf.Clamp01(stock/(pair.Key=="S02"?30f:40f));
                float h=Mathf.Max(.004f,pair.Value.fullHeight*frac);
                pair.Value.node.localScale=new Vector3(.009f,h,.006f);
                pair.Value.node.localPosition=new Vector3(pair.Value.node.localPosition.x,pair.Value.bottom+h*.5f,0);
            }
            UpdateStockDisplays(session);
            RefreshCounterMeals(session);
            RestoreHeldMealVisual(session);
        }
        private NpcDepartureState DepartureState(int id)
        {
            if(!guests.TryGetValue(id,out Guest g)||!g.root||!g.root.gameObject.activeInHierarchy)return new NpcDepartureState(true,true,0);
            return new NpcDepartureState(false,g.departureStarted&&g.routeAt>=1,g.root.position.x);
        }
        private Guest AddGuest(int id,int restoredSlot=-1,bool restoreArrived=false)
        {
            if(customerAssets.Count==0)throw new InvalidOperationException("No real rigged customer asset is available");
            int slot=restoredSlot>=0?restoredSlot:Array.FindIndex(queueSlots,g=>g==null);if(slot<0||slot>=queueSlots.Length||queueSlots[slot]!=null)return null;
            LoadedCharacterAsset asset=customerAssets[(id-1)%customerAssets.Count];
            Vector3 entry=restoreArrived?QueuePoints[slot]:new Vector3(6.15f,0,3.22f);
            if(!guestNavigation.CanOccupy(entry,NpcBodyProfile.ForArchetype(asset.Definition.Id)))return null;
            Transform root=Group(asset.Definition.DisplayName+" "+id,transform);root.position=entry;
            GameObject visual=Model(asset.Definition.ModelResourcePath.Substring("Models/".Length),root,false);Face(visual.transform,"Customer",Vector3.forward);
            // Establish the mesh's local facing before orienting its actor.
            // Face works in world space; doing this in reverse cancels the actor's gaze.
            if(restoreArrived){Vector3 facing=player.position-entry;facing.y=0;if(facing.sqrMagnitude>.001f)root.rotation=Quaternion.LookRotation(facing,Vector3.up);}
            if(!customerMaterials.TryGetValue(asset.Definition.Id,out Material mat))
            {mat=Paint(asset.Definition.DisplayName,Color.white);if(asset.Definition.AlbedoResourcePath!=null)mat.mainTexture=Resources.Load<Texture2D>(asset.Definition.AlbedoResourcePath);customerMaterials[asset.Definition.Id]=mat;}
            if(mat.mainTexture)foreach(Renderer r in visual.GetComponentsInChildren<Renderer>()){var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=mat;r.sharedMaterials=mats;}
            Animation animation=visual.GetComponentInChildren<Animation>();if(!animation)animation=visual.AddComponent<Animation>();animation.enabled=true;
            foreach(var pair in asset.Clips){string alias=pair.Key==CharacterMotion.Walk?"stall_walk":"stall_idle";animation.AddClip(pair.Value,alias);animation[alias].wrapMode=WrapMode.Loop;}
            var guest=new Guest{id=id,root=root,animation=animation,slot=slot,arrived=restoreArrived,routeAt=restoreArrived?1:0,target=restoreArrived?QueuePoints[slot]:entry};
            Transform clickTarget=Group("Order target "+id,root);
            var target=Target(clickTarget,StallInteractionKind.Order,"",-1,root.position+Vector3.up*(asset.Definition.RestHeightMetres+.12f),new Vector3(.5f,1.6f,.5f));target.orderId=id;
            SetLayers(root);
            if(!guestNavigation.TryRegister("stall-customer-"+id,asset.Definition.Id,root,SolidLayer,out guest.agent)){Destroy(root.gameObject);return null;}
            guest.agent.AllowDynamicDetours=false; // Controlled same-direction lane: wait instead of cutting across another slot.
            guest.presentation=root.gameObject.AddComponent<CustomerServicePresentation>();
            guest.presentation.Bind(animation,player,dough,white);
            queueSlots[slot]=guest;Play(guest,!restoreArrived);return guest;
        }
        private static void Play(Guest g,bool walking){string clip=walking?"stall_walk":"stall_idle";if(clip==g.playing)return;g.animation.CrossFade(clip,.15f);g.playing=clip;}
        private void WorldText(string value,Transform parent,Vector3 at,float height)
        {Transform root=Group(value,parent);root.localPosition=at;TextMesh text=root.gameObject.AddComponent<TextMesh>();text.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");text.text=value;text.fontSize=64;text.characterSize=height*.16f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(1,.93f,.77f);var label=new Material(Resources.Load<Shader>("Shaders/StallWorldLabel"));label.mainTexture=text.font.material.mainTexture;owned.Add(label);root.GetComponent<Renderer>().sharedMaterial=label;}
        private static void SetLayers(Transform root){foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.gameObject.layer!=SolidLayer&&t.gameObject.layer!=QueryVisualLayer)t.gameObject.layer=Layer;}
        private static Bounds BoundsOf(Transform root){var b=new Bounds(root.position,Vector3.zero);bool first=true;foreach(Renderer r in root.GetComponentsInChildren<Renderer>()){if(first){b=r.bounds;first=false;}else b.Encapsulate(r.bounds);}return b;}
        private static float Top(Transform root)=>BoundsOf(root).max.y;
        public BusinessModelAudit[] CaptureModelAudit()
        {
            var models=new List<BusinessModelAudit>{ModelAudit("cart-ai",cart,"CartAI_Runtime")};
            if(primaryEquipment!=null)models.Add(ModelAudit("primary-equipment",primaryEquipment));
            for(int i=0;i<dishes.Count;i++)models.Add(ModelAudit("cookware-"+i,dishes[i]));
            return models.ToArray();
        }
        private BusinessModelAudit ModelAudit(string id,Transform model,string filter="")
        {
            var audit=new BusinessModelAudit{model=id};if(model==null)return audit;
            Bounds bounds=new Bounds(model.position,Vector3.zero);bool first=true;Plane[] planes=GeometryUtility.CalculateFrustumPlanes(View);
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||!renderer.name.Contains(filter))continue;
                audit.activeRenderers++;if(GeometryUtility.TestPlanesAABB(planes,renderer.bounds)&&(View.cullingMask&(1<<renderer.gameObject.layer))!=0)audit.inFrustumRenderers++;
                if(renderer.isVisible&&!renderer.forceRenderingOff)audit.renderedLastFrame++;
                if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
            }
            Vector3 min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
            {
                Vector3 corner=new Vector3(x==0?bounds.min.x:bounds.max.x,y==0?bounds.min.y:bounds.max.y,z==0?bounds.min.z:bounds.max.z);
                Vector3 point=View.WorldToViewportPoint(corner);min=Vector3.Min(min,point);max=Vector3.Max(max,point);
            }
            audit.worldCenter=new[]{bounds.center.x,bounds.center.y,bounds.center.z};audit.worldSize=new[]{bounds.size.x,bounds.size.y,bounds.size.z};audit.viewportMinMax=new[]{min.x,min.y,min.z,max.x,max.y,max.z};return audit;
        }
        public void Release()
        {
            if(released)return;released=true;CancelAllCustomerPickups();if(workActor)workActor.Cancel();sequenceBusy=false;ClearHeldProp();
            if(activeSession!=null){activeSession.SaleCompleted-=OnSaleCompleted;activeSession=null;}
            if(View)View.enabled=false;if(oldCamera)oldCamera.enabled=oldCameraEnabled;
            foreach(Light light in oldLights)if(light)light.enabled=true;
            foreach(Material m in owned)if(m)Destroy(m);foreach(Mesh m in meshes)if(m)Destroy(m);
            foreach(Sprite sprite in dishIcons.Values)if(sprite)Destroy(sprite);foreach(Texture2D t in iconTextures)if(t)Destroy(t);
        }
        private void OnDestroy(){Release();}
    }
}
