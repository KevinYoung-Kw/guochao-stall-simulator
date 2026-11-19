using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    // Authored walking loops with real skinned Aholo characters. Coordinates are
    // reconstructed from imported landmarks, never guessed from FBX axis signs.
    [DefaultExecutionOrder(40)]
    public sealed class CustomerCrowd : MonoBehaviour
    {
        [Serializable] public sealed class CrowdSave
        {public int version=1;public ShopperSave[] shoppers;}
        [Serializable] public sealed class ShopperSave
        {
            public string instanceId,archetype,playing;
            public Vector3 position,yieldPoint,resumePoint,currentTarget;
            public Quaternion rotation;
            public int next,waypointsReached,completedLaps;
            public float wait,speed,travelled,animationTime;
            public bool yielding,returningToRoute;
            public MarketAisleRightOfWay.SaveData passage;
        }
        private sealed class Shopper
        {
            public Transform body;
            public NpcNavigationAgent navigation;
            public Animation animation;
            public string idle, walk, playing;
            public Vector3[] route;
            public int next;
            public float wait, speed;
            public Vector3 yieldPoint;
            public bool yielding;
            public Vector3 resumePoint;
            public bool returningToRoute;
            public string instanceId, archetype, activity;
            public float[] stops;
            public Vector3 waitingGaze;
            public float cruiseSpeed;
            public MarketAisleRightOfWay passage;
            public int waypointsReached,completedLaps;
            public Vector3 currentTarget;
        }
        private readonly List<Shopper> shoppers = new List<Shopper>();
        private NpcNavigationSpace navigation;
        private WorldRuntime world;
        private Vector3 origin, xAxis, yAxis;
        private readonly Dictionary<string,Material> customerMaterials=new Dictionary<string,Material>();

        public int Count => shoppers.Count;
        public float[] TravelledByGuest { get {var result=new float[Count];for(int i=0;i<Count;i++)result[i]=shoppers[i].navigation.Travelled;return result;} }
        public int[] CompletedLaps {get{var a=new int[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].completedLaps;return a;}}
        public int[] WaypointsReached {get{var a=new int[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].waypointsReached;return a;}}
        public Vector3[] CurrentTargets {get{var a=new Vector3[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].currentTarget;return a;}}
        public string[] RightOfWayStates {get{var a=new string[Count];for(int i=0;i<Count;i++){var p=shoppers[i].passage;a[i]=p==null?"southbound-rule":p.State+":"+p.BlockingUser;}return a;}}
        public bool[] IntendingToMove { get {var result=new bool[Count];for(int i=0;i<Count;i++){var s=shoppers[i];result[i]=s.passage!=null&&s.passage.Active?s.passage.State!=MarketAisleRightOfWay.Phase.WaitInRefuge:s.wait<=0||s.yielding;}return result;} }
        public string[] MovementStates { get {var result=new string[Count];for(int i=0;i<Count;i++){var s=shoppers[i];result[i]=s.passage!=null&&s.passage.Active?"right-of-way:"+s.passage.State:s.yielding?"yielding":s.returningToRoute?"returning":s.wait>0?"dwell":s.navigation.Blocked?"blocked":"route";}return result;} }
        public int BlockedCount { get {int n=0;foreach(var s in shoppers)if(s.navigation.Blocked)n++;return n;} }
        public string[] CaptureOverlaps()=>navigation?.CaptureOverlaps()??Array.Empty<string>();
        public int DeniedMoves=>navigation?.DeniedMoves??0;
        public int NavigationSearchFailures=>navigation?.SearchFailures??0;
        public bool NavigationQueryOverflow=>navigation!=null&&navigation.QueryOverflow;
        public float[] CollisionRadii { get {var result=new float[Count];for(int i=0;i<Count;i++)result[i]=shoppers[i].navigation.Profile.Radius;return result;} }
        public string[] BlockedReasons { get {var result=new string[Count];for(int i=0;i<Count;i++)result[i]=shoppers[i].navigation.BlockedReason;return result;} }
        public int WalkingCount { get; private set; }
        public int WaitingCount => Count - WalkingCount;
        public int ArchetypeCount { get; private set; }
        public int IndependentNpcArchetypeCount { get; private set; }
        public bool AssetSetComplete => false;
        public string[] InstanceIds { get {var a=new string[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].instanceId;return a;} }
        public string[] ArchetypeIds { get {var a=new string[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].archetype;return a;} }
        public string[] Activities { get {var a=new string[Count];for(int i=0;i<Count;i++)a[i]=shoppers[i].activity;return a;} }
        public Vector3[] Positions
        {
            get { var result = new Vector3[shoppers.Count]; for (int i=0;i<result.Length;i++) result[i]=shoppers[i].body.position; return result; }
        }
        public float TravelledMetres
        {
            get { float distance=0; foreach(var shopper in shoppers) distance+=shopper.navigation.Travelled; return distance; }
        }

        // Moving props use this nominal envelope rather than PhysX's potentially
        // reduced CharacterController query shape. No live pose is changed.
        public bool OverlapsBox(Vector3 center,Vector3 halfExtents,Quaternion rotation)
        {return OverlapsBox(center,halfExtents,rotation,out _);}
        public bool OverlapsBox(Vector3 center,Vector3 halfExtents,Quaternion rotation,out string blocker)
        {blocker="";return navigation!=null&&navigation.OverlapsBox(center,halfExtents,rotation,out blocker);}

        public string CaptureSaveJson()
        {
            var saved=new CrowdSave{shoppers=new ShopperSave[Count]};
            for(int i=0;i<Count;i++)
            {
                var s=shoppers[i];saved.shoppers[i]=new ShopperSave{
                    instanceId=s.instanceId,archetype=s.archetype,position=s.body.position,rotation=s.body.rotation,
                    next=s.next,wait=s.wait,speed=s.speed,yielding=s.yielding,yieldPoint=s.yieldPoint,
                    returningToRoute=s.returningToRoute,resumePoint=s.resumePoint,currentTarget=s.currentTarget,
                    waypointsReached=s.waypointsReached,completedLaps=s.completedLaps,travelled=s.navigation.Travelled,
                    playing=s.playing,animationTime=s.animation[s.playing]!=null?s.animation[s.playing].time:0,
                    passage=s.passage?.CaptureSave()};
            }
            return JsonUtility.ToJson(saved);
        }
        public void RestoreSaveJson(string json)
        {
            if(string.IsNullOrEmpty(json))return;
            CrowdSave saved=JsonUtility.FromJson<CrowdSave>(json);
            if(saved==null||saved.version!=1||saved.shoppers==null||saved.shoppers.Length!=Count)
                throw new InvalidOperationException("Market crowd save does not match this scene's actor contract");
            var records=new Dictionary<string,ShopperSave>();
            foreach(var r in saved.shoppers)
            {
                if(r==null||string.IsNullOrEmpty(r.instanceId)||records.ContainsKey(r.instanceId))throw new InvalidOperationException("Duplicate/missing saved shopper identity");
                records.Add(r.instanceId,r);
            }
            foreach(var s in shoppers)
            {
                // V15 gives the fifth existing route an independent student visual.
                // Route, clip names and durations are unchanged; its measured envelope
                // is smaller than the old worker's. Preserve the saved pose/progress.
                if(s.instanceId=="market_customer_5" && s.archetype=="customer_student_v15" &&
                   records.TryGetValue(s.instanceId,out var legacy) && legacy.archetype=="customer_worker_v11")
                    legacy.archetype=s.archetype;
                if(!records.TryGetValue(s.instanceId,out var r)||r.archetype!=s.archetype||r.next<0||r.next>=s.route.Length||!Finite(r.position)||!Finite(r.yieldPoint)||!Finite(r.resumePoint)||!Finite(r.currentTarget)||!Finite(r.wait)||!Finite(r.speed)||!Finite(r.animationTime)||!Finite(r.travelled)||!Finite(r.rotation)||
                    (r.passage!=null&&(r.passage.phase<0||r.passage.phase>(int)MarketAisleRightOfWay.Phase.ReturnToEntry||!Finite(r.passage.clearTime)||!Finite(r.passage.yieldSeconds))))
                    throw new InvalidOperationException("Invalid saved shopper route or pose: "+s.instanceId);
            }
            // Load-time reconstruction only. All controllers are disabled together
            // so one actor's old spawn cannot obstruct another actor's saved pose.
            foreach(var s in shoppers)s.navigation.Controller.enabled=false;
            foreach(var s in shoppers)
            {
                var r=records[s.instanceId];s.body.SetPositionAndRotation(r.position,r.rotation);
                s.next=r.next;s.wait=Mathf.Max(0,r.wait);s.speed=Mathf.Max(0,r.speed);s.yielding=r.yielding;s.yieldPoint=r.yieldPoint;
                s.returningToRoute=r.returningToRoute;s.resumePoint=r.resumePoint;s.currentTarget=r.currentTarget;
                s.waypointsReached=r.waypointsReached;s.completedLaps=r.completedLaps;s.navigation.Travelled=r.travelled;
                s.passage?.RestoreSave(r.passage);navigation.ResetAfterSaveRestore(s.navigation);
                Play(s,r.playing==s.walk,s.speed);if(s.animation[s.playing]!=null){s.animation[s.playing].time=r.animationTime;s.animation.Sample();}
            }
            foreach(var s in shoppers)s.navigation.Controller.enabled=true;
            navigation.BeginFrame();string[] overlap=navigation.CaptureOverlaps();
            if(overlap.Length>0)throw new InvalidOperationException("Restored market crowd has overlapping physical footprints: "+string.Join("; ",overlap));
        }
        static bool Finite(Vector3 value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z);
        static bool Finite(Quaternion value)=>Finite(value.x)&&Finite(value.y)&&Finite(value.z)&&Finite(value.w)&&value.x*value.x+value.y*value.y+value.z*value.z+value.w*value.w>.5f;
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

        public void Initialize(WorldRuntime runtime)
        {
            if(world!=null)return;
            world=runtime;
            xAxis=(world.Anchor("MeatShelf")-world.Anchor("EggShelf"))/3.5f;xAxis.y=0;
            yAxis=(world.Anchor("RiceShelf")-world.Anchor("MeatShelf")+xAxis*.60f)/-3.02f;yAxis.y=0;
            origin=world.Anchor("EggShelf")-xAxis*3-yAxis*16.5f;origin.y=world.Anchor("Spawn").y+.005f;
            var navigationArea=new Bounds(P(-1,-2),Vector3.zero);
            foreach(var corner in new[]{P(-1,21),P(25,-2),P(25,21)})navigationArea.Encapsulate(corner);
            navigation=new NpcNavigationSpace(navigationArea,WorldRuntime.EnvironmentMask);
            navigation.AddObstacle(world.Player.Controller);
            navigation.AddObstacle(world.Player.CarryCollider,true);
            if(world.ShoppingMotor!=null)navigation.AddObstacle(world.ShoppingMotor.Trolley.GetComponent<Collider>(),true,LinkedTrolleyMotor.ClearanceSkin);
            navigation.BeginFrame();
            // Five independent NPC archetypes. Duplicate actors retain explicit instance IDs;
            // the player model is no longer used to fill customer routes.
            // South cross-aisle is 2.8 m between checkout z=3.7 and shelf z=6.5.
            // Auntie uses z=5.65; Courier uses z=4.25: 1.40 m separation exceeds
            // their measured .78+.51 radii plus .035 navigation clearance.
            AddFromCatalog("customer_auntie_v11",new[]{P(6.60f,10.7f),P(6.60f,14.60f),P(5.65f,15.45f),P(1.25f,15.45f),P(1.25f,5.65f),P(6.6f,5.65f)},0,1.8f,"browse");
            AddFromCatalog("customer_worker_v11",new[]{P(12.45f,11),P(12.45f,15.2f),P(16.0f,15.7f),P(18.25f,14.7f),P(18.25f,5.15f),P(12.45f,5.15f)},1,3.2f,"browse");
            AddFromCatalog(CharacterAssetCatalog.JadeId,new[]{P(18.35f,10),P(18.35f,14.8f),P(17.15f,16.65f),P(18.35f,14.8f),P(18.35f,6.0f)},2,5.1f,"browse");
            // Checkout return remains east of the counter. It never sends an
            // opposing actor into the narrow west-side through aisle.
            AddFromCatalog("customer_courier_v11",new[]{P(3.95f,4.25f),P(3.95f,2.30f),P(3.95f,4.25f),P(5.50f,4.25f)},3,1.1f,"queue",new[]{.6f,8.4f,.3f,.6f});
            AddFromCatalog("customer_student_v15",new[]{P(11.90f,4.90f),P(11.90f,2.30f),P(11.90f,4.90f),P(9.40f,4.90f),P(9.40f,2.30f),P(9.40f,4.90f)},4,4.5f,"queue",new[]{.6f,10.2f,.3f,.3f,.6f,.3f});
            AddFromCatalog("customer_courier_v11",new[]{P(14.50f,4.50f),P(19.70f,4.50f),P(20.20f,8.50f),P(19.0f,11.20f),P(19.0f,5.80f),P(14.50f,5.80f)},5,2.7f,"passerby",new[]{.2f,.4f,1.5f,2.6f,.2f,.2f});
            if(shoppers.Count>0)
            {
                foreach(string name in new[]{"NPC_queue","NPC_drygoods","NPC_cold","NPC_第二通道顾客"})
                    if(world.Anchors.TryGetValue(name,out Transform old)) old.gameObject.SetActive(false);
            }
            var types=new HashSet<string>();foreach(var shopper in shoppers)types.Add(shopper.archetype);ArchetypeCount=types.Count;
            foreach(string id in types)if(CharacterAssetCatalog.Find(id).IsCustomerSource)IndependentNpcArchetypeCount++;
            Debug.Log("CUSTOMERS_READY instances="+Count+" archetypes="+ArchetypeCount+" independent_npc_archetypes="+IndependentNpcArchetypeCount+" asset_set_complete=false catalog="+CharacterAssetCatalog.ContractVersion);
        }
        private void AddFromCatalog(string id,Vector3[] route,int index,float wait,string activity,float[] stops=null)
        {
            if(!CharacterAssetCatalog.TryLoad(id,out var asset,out string failure,CharacterMotion.Idle,CharacterMotion.Walk))
            {Debug.LogWarning("CustomerCrowd: "+failure+"; this actor is omitted, not replaced with a player copy.");return;}
            Material material=null;
            if(!string.IsNullOrEmpty(asset.Definition.AlbedoResourcePath)&&!customerMaterials.TryGetValue(id,out material))
            {
                var texture=Resources.Load<Texture2D>(asset.Definition.AlbedoResourcePath);
                if(texture==null){Debug.LogError("CustomerCrowd: missing original albedo for "+id);return;}
                material=new Material(Shader.Find("Standard")){name=id+"_OriginalAlbedo",mainTexture=texture,color=Color.white};
                material.SetFloat("_Glossiness",.18f);material.SetFloat("_Metallic",0);customerMaterials.Add(id,material);
            }
            Add(asset,route,index,wait,activity,material,stops);
        }
        private Vector3 P(float x,float y)=>origin+xAxis*x+yAxis*y;
        private void Add(LoadedCharacterAsset asset,Vector3[] route,int index,float wait,string activity,Material material,float[] stops=null)
        {
            var definition=asset.Definition;string instanceId="market_customer_"+(index+1);
            var root=new GameObject(instanceId);root.transform.SetParent(transform,false);root.transform.position=route[0];
            if(!navigation.TryRegister(instanceId,definition.Id,root.transform,WorldRuntime.EnvironmentLayer,out var navigationAgent))
            {Debug.LogError("CustomerCrowd: measured full footprint has no clear spawn for "+instanceId);Destroy(root);return;}
            GameObject visual=Instantiate(asset.Prefab,root.transform);visual.name=definition.Id+"_Model";
            Transform authoredOrigin=null,authoredForward=null;
            foreach(Transform t in visual.GetComponentsInChildren<Transform>(true))
            { if(t.name==definition.OriginNode)authoredOrigin=t; if(t.name==definition.ForwardNode)authoredForward=t; }
            if(authoredOrigin!=null && authoredForward!=null)
            {
                Vector3 forward=authoredForward.position-authoredOrigin.position;forward.y=0;
                visual.transform.rotation=Quaternion.FromToRotation(forward.normalized,Vector3.forward)*visual.transform.rotation;
            }
            foreach(Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer is SkinnedMeshRenderer skin)skin.updateWhenOffscreen=true;
                if(material!=null){var materials=renderer.sharedMaterials;for(int m=0;m<materials.Length;m++)materials[m]=material;renderer.sharedMaterials=materials;}
            }
            Animator animator=visual.GetComponentInChildren<Animator>();if(animator)animator.enabled=false;
            GameArtRendering.Apply(visual,true);
            Animation animation=visual.GetComponentInChildren<Animation>();if(!animation)animation=visual.AddComponent<Animation>();
            var shopper=new Shopper{navigation=navigationAgent,body=root.transform,animation=animation,route=route,next=1,wait=wait,instanceId=instanceId,archetype=definition.Id,activity=activity,stops=stops,waitingGaze=activity=="queue"?-xAxis:Vector3.zero,cruiseSpeed=activity=="passerby"?1.12f:.92f};
            if(definition.Id=="customer_auntie_v11")
            {
                var users=new List<Collider>{world.Player.Controller,world.Player.CarryCollider};
                if(world.ShoppingMotor!=null)users.Add(world.ShoppingMotor.Trolley.GetComponent<Collider>());
                shopper.passage=new MarketAisleRightOfWay(origin,xAxis,yAxis,navigation,navigationAgent,users);
                navigationAgent.TraversalAllowed=shopper.passage.AllowsSegment;
            }
            else navigationAgent.TraversalAllowed=(from,to)=>MarketAisleRightOfWay.AllowsSouthboundSegment(from,to,origin,xAxis,yAxis);
            foreach(var pair in asset.Clips)
            {
                var clip=pair.Value;
                string clipAlias=definition.Clip(pair.Key).ClipName;
                animation.AddClip(clip,clipAlias);animation[clipAlias].wrapMode=WrapMode.Loop;
                if(pair.Key==CharacterMotion.Idle)shopper.idle=clipAlias;
                if(pair.Key==CharacterMotion.Walk)shopper.walk=clipAlias;
            }
            if(shopper.idle==null || shopper.walk==null){navigation.Remove(navigationAgent);Destroy(root);return;}
            root.transform.rotation=Quaternion.LookRotation(route[1]-route[0],Vector3.up);
            shoppers.Add(shopper);Play(shopper,false,0);
        }
        private void Update()=>AdvanceNavigation(Time.deltaTime);
        // Explicit diagnostics use this same production path, with the ordinary
        // pause/transition guards. It does not alter the game clock or player pose.
        public void AdvanceNavigation(float deltaTime)
        {
            bool frozen=world==null || GameFlow.Instance==null || GameFlow.Instance.Run.Paused ||
                GameFlow.Instance.IsTransitioning || GameFlow.Instance.State.phase!=GamePhase.Market;
            WalkingCount=0;
            if(!frozen)navigation.BeginFrame();
            foreach(var shopper in shoppers)
            {
                shopper.animation.enabled=!frozen;
                if(frozen) {shopper.speed=0;continue;}
                Advance(shopper,Mathf.Min(deltaTime,.05f));
            }
        }
        private void Advance(Shopper shopper,float dt)
        {
            Vector3 point=shopper.body.position;
            Vector3 routeTarget=shopper.route[shopper.next];
            bool passageHold=false;
            bool passageOverride=shopper.passage!=null&&shopper.passage.Decide(routeTarget,dt,out routeTarget,out passageHold);
            if(passageOverride)
            {
                shopper.currentTarget=routeTarget;shopper.yielding=false;shopper.returningToRoute=false;
                if(passageHold){shopper.speed=0;Play(shopper,false,0);return;}
                shopper.speed=Mathf.MoveTowards(shopper.speed,shopper.cruiseSpeed,2.8f*dt);
                float yielded=navigation.Step(shopper.navigation,routeTarget,shopper.speed,dt);
                if(yielded>.0001f)WalkingCount++;
                Play(shopper,yielded>.0001f,dt>0?yielded/dt:0);return;
            }
            if(shopper.wait>0&&!shopper.yielding)
            {
                shopper.wait-=dt;shopper.speed=0;
                if(!shopper.returningToRoute&&PlayerNear(shopper))TryYield(shopper,shopper.body.forward);
                if(!shopper.yielding)
                {
                    Vector3 gaze=shopper.waitingGaze.sqrMagnitude>.01f?shopper.waitingGaze:(Vector3.Dot(point-origin,xAxis)<10?-xAxis:xAxis);
                    shopper.body.rotation=Quaternion.RotateTowards(shopper.body.rotation,Quaternion.LookRotation(gaze),60*dt);
                    Play(shopper,false,0);return;
                }
            }
            Vector3 target=shopper.yielding?shopper.yieldPoint:shopper.returningToRoute?shopper.resumePoint:shopper.route[shopper.next];
            shopper.currentTarget=target;
            if(navigation.HasArrived(shopper.navigation,target))
            {
                if(shopper.yielding){shopper.yielding=false;shopper.returningToRoute=true;shopper.wait=1.2f;}
                else if(shopper.returningToRoute){shopper.returningToRoute=false;shopper.wait=.2f;}
                else {int arrived=shopper.next;shopper.waypointsReached++;if(arrived==0)shopper.completedLaps++;shopper.next=(shopper.next+1)%shopper.route.Length;shopper.wait=shopper.stops!=null?shopper.stops[arrived]:2.2f+(shopper.next%3)*.65f;}
                shopper.speed=0;Play(shopper,false,0);return;
            }
            shopper.speed=Mathf.MoveTowards(shopper.speed,shopper.cruiseSpeed,2.8f*dt);
            float moved=navigation.Step(shopper.navigation,target,shopper.speed,dt);
            bool walking=moved>.0001f;
            if(walking)WalkingCount++;
            else if(shopper.navigation.BlockedSeconds>.7f&&!shopper.yielding&&PlayerNear(shopper))TryYield(shopper,(target-point).normalized);
            Play(shopper,walking,dt>0?moved/dt:0);
        }
        private bool PlayerNear(Shopper shopper)
        {
            Vector3 delta=world.Player.transform.position-shopper.body.position;delta.y=0;
            float safe=shopper.navigation.Profile.Radius+world.Player.Controller.radius+.75f;
            if(delta.sqrMagnitude<safe*safe)return true;
            if(world.ShoppingMotor==null)return false;
            Collider volume=world.ShoppingMotor.Trolley.GetComponent<Collider>();
            return volume&&Vector3.Distance(volume.ClosestPoint(shopper.body.position+Vector3.up*.8f),shopper.body.position+Vector3.up*.8f)<shopper.navigation.Profile.Radius+.5f;
        }
        private void TryYield(Shopper shopper,Vector3 forward)
        {
            Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;if(side.sqrMagnitude<.1f)return;
            foreach(float distance in new[]{1.4f,1.1f,.85f,.60f,.40f})foreach(float sign in new[]{1f,-1f})
            {
                Vector3 candidate=shopper.body.position+side*sign*distance;
                if(!navigation.CanOccupy(candidate,shopper.navigation.Profile,shopper.navigation)||!navigation.CanReachDirect(shopper.navigation,candidate))continue;
                // A blocked return must retain the original route rejoin point,
                // rather than repeatedly replacing it with the newest refuge.
                if(!shopper.returningToRoute)shopper.resumePoint=shopper.body.position;
                shopper.yieldPoint=candidate;shopper.yielding=true;shopper.returningToRoute=false;return;
            }
        }
        private static void Play(Shopper shopper,bool moving,float speed)
        {
            string name=moving?shopper.walk:shopper.idle;
            if(shopper.playing!=name){shopper.animation.CrossFade(name,.18f);shopper.playing=name;}
            if(shopper.animation[name]!=null)shopper.animation[name].speed=moving?Mathf.Clamp(speed/.92f,.35f,1.15f):1;
        }
        private void OnDestroy(){foreach(var material in customerMaterials.Values)if(material)Destroy(material);}
    }
}
