using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // A real, isolated 3D street. The authoritative run is held by GameFlow while
    // this presentation follows an authored path; it never reads driving input.
    public sealed class CinematicTravelStage : MonoBehaviour
    {
        public const float Duration=15f;
        private const int TravelLayer=30;
        public float Elapsed { get; private set; }
        public float TravelDuration { get; private set; }=Duration;
        public float PhaseTime=>Elapsed*Duration/TravelDuration;
        public bool Complete=>Elapsed>=TravelDuration;
        // Skipping only ends the presentation: arrival, cargo, money and the
        // market clock are committed exactly once by SceneTransition after the
        // loop exits, exactly as if the ride had finished on its own.
        public void SkipToEnd()
        {
            if(Complete)return;
            Elapsed=TravelDuration;
            if(captionGroup!=null)captionGroup.alpha=0;
            if(roadSound!=null)roadSound.Pause();
            if(bumpSound!=null)bumpSound.Pause();
            Debug.Log("MORNING_TRAVEL_SKIP duration="+TravelDuration.ToString("F2"));
        }
        public int Bumps { get; private set; }
        private Camera view,originalCamera;
        private bool originalCameraEnabled,released;
        private readonly List<Light> suspendedLights=new List<Light>();
        private readonly List<Canvas> suspendedCanvases=new List<Canvas>();
        private readonly List<Material> materials=new List<Material>();
        private readonly List<Transform> tiles=new List<Transform>();
        private readonly List<Transform> wheelMarks=new List<Transform>();
        private readonly List<Hazard> hazards=new List<Hazard>();
        private Transform suspension,vehicle,driver,arrival;
        private Vector3 driverRest;
        private Text instruction,caption;
        private CanvasGroup captionGroup;
        private AudioSource roadSound,bumpSound;
        private AudioClip roadClip,impactClip;
        private float distance,lane,laneVelocity,steer,bumpTime,feedbackTime,wheelAngle;
        private Material asphalt,stone,cream,brick,paintedInk,ink,metal,leaf,ochre,water;
        private sealed class Hazard { public Transform root;public float distance,lane,radius;public bool seen,puddle; }

        public void Initialize(bool stoveLoaded,Canvas transitionCanvas,string[] equipment=null,float duration=15)
        {
            TravelDuration=Mathf.Max(1,duration);
            originalCamera=Camera.main;
            if(originalCamera!=null){originalCameraEnabled=originalCamera.enabled;originalCamera.enabled=false;}
            foreach(Light light in FindObjectsByType<Light>())
                if(light.enabled){suspendedLights.Add(light);light.enabled=false;}
            foreach(Canvas canvas in FindObjectsByType<Canvas>())
                if(canvas.enabled&&canvas!=transitionCanvas){suspendedCanvases.Add(canvas);canvas.enabled=false;}
            CreatePalette();
            StreetShopfrontAssets.Reset();
            for(int i=0;i<3;i++)BuildStreetTile(i);
            Debug.Log("MORNING_TRAVEL_SHOPFRONT replaced="+StreetShopfrontAssets.ReplacedCount+" fallback="+StreetShopfrontAssets.FallbackCount);
            BuildVehicle(stoveLoaded,equipment);BuildHazard(18,-.85f,false);BuildHazard(35,1.0f,true);BuildHazard(52,-.25f,false);
            BuildArrival();BuildCamera();BuildOverlay(equipment==null?(stoveLoaded?"B04 已随车出发":"灶台留在家中 · 空车前往超市"):equipment.Length>0?"已装车 "+equipment.Length+" 件家当":"空车前往超市");BuildSound();
            SetLayerRecursively(transform);
            // A single first frame is deterministic; no input, elapsed time or cargo mutation.
            PlaceStreet();PoseVehicle(0);PlaceCamera();
            Debug.Log("MORNING_TRAVEL_BEGIN duration="+TravelDuration+" stoveLoaded="+stoveLoaded);
        }

        public void Advance(float dt,bool paused)
        {
            bool quiet=paused||GameFeelAudio.Muted;
            if(roadSound!=null)
            {
                if(quiet){roadSound.Pause();bumpSound.Pause();}
                else{if(!roadSound.isPlaying)roadSound.UnPause();bumpSound.UnPause();}
            }
            if(paused||dt<=0||Complete)return;
            dt=Mathf.Min(dt,TravelDuration-Elapsed);Elapsed=Mathf.Min(TravelDuration,Elapsed+dt);
            float phase=PhaseTime;
            // Travel is a cutscene, not a driving minigame. The gentle street
            // curve and final pull-in are identical regardless of held keys.
            float previousLane=lane;
            float route=Mathf.Clamp01((phase-.65f)/12.15f);
            lane=Mathf.Sin(route*Mathf.PI*2)*.32f*Mathf.Sin(route*Mathf.PI);
            laneVelocity=(lane-previousLane)/Mathf.Max(dt,.0001f);
            float speed=5.1f*Mathf.SmoothStep(0,1,phase/1.25f)*(1-.83f*Mathf.SmoothStep(0,1,(phase-12.7f)/2.3f))*Duration/TravelDuration;
            if(bumpTime>0)speed*=.81f;
            distance+=speed*dt;wheelAngle+=speed/.30f*Mathf.Rad2Deg*dt;
            steer=Mathf.Lerp(steer,laneVelocity*4.5f,1-Mathf.Exp(-dt*8));
            bumpTime=Mathf.Max(0,bumpTime-dt);feedbackTime=Mathf.Max(0,feedbackTime-dt);
            foreach(Hazard hazard in hazards)
            {
                hazard.root.localPosition=new Vector3(hazard.distance-distance,.012f,hazard.lane);
                if(!hazard.seen&&hazard.distance-distance<.72f)
                {
                    hazard.seen=true;
                    if(Mathf.Abs(lane-hazard.lane)<hazard.radius+.48f)
                    {
                        Bumps++;bumpTime=.65f;feedbackTime=1.4f;
                        caption.text="清晨街巷 · 前往惠民超市";
                        if(!GameFeelAudio.Muted)bumpSound.PlayOneShot(impactClip,.42f*StreetAudioLevels.Effects);
                    }
                    else{feedbackTime=1.1f;caption.text="清晨街巷 · 前往惠民超市";}
                }
            }
            if(phase>12.8f){caption.text="惠民仓储超市 · 靠边停稳";feedbackTime=.5f;}
            captionGroup.alpha=Mathf.Clamp01(feedbackTime*4);
            instruction.text=phase>12.8f?"即将到达  ·  停稳后开始采购":"前往惠民超市";
            if(roadSound!=null){roadSound.volume=.07f*Mathf.Clamp01(speed/4)*StreetAudioLevels.Effects;roadSound.pitch=.75f+speed*.07f;}
            PlaceStreet();PoseVehicle(speed);PlaceCamera();
        }

        private void PlaceStreet()
        {
            for(int i=0;i<tiles.Count;i++)
            {
                float x=i*20-distance;
                while(x< -24)x+=60;
                tiles[i].localPosition=new Vector3(x,0,0);
            }
            // The destination is a physical frontage approaching from ahead.
            if(arrival!=null)arrival.localPosition=new Vector3(71-distance,0,0);
        }
        private void PoseVehicle(float speed)
        {
            vehicle.localPosition=new Vector3(0,0,lane);
            vehicle.localRotation=Quaternion.Euler(0,-steer,0);
            float impact=bumpTime>0?Mathf.Sin((.65f-bumpTime)*24)*bumpTime*.075f:0;
            suspension.localPosition=new Vector3(0,Mathf.Sin(distance*6)*.009f*Mathf.Clamp01(speed)+Mathf.Max(0,impact),0);
            suspension.localRotation=Quaternion.Euler(steer*.16f,0,Mathf.Sin(distance*4)*.35f+impact*24);
            foreach(Transform wheel in wheelMarks)wheel.localRotation=Quaternion.AngleAxis(-wheelAngle,Vector3.forward);
            if(driver!=null)driver.localPosition=driverRest+Vector3.up*(Mathf.Sin(distance*5)*.006f);
        }
        private void PlaceCamera()
        {
            float intro=Mathf.SmoothStep(0,1,PhaseTime/2.1f),outro=Mathf.SmoothStep(0,1,(PhaseTime-12.7f)/2.3f);
            Vector3 target=new Vector3(Mathf.Lerp(1.7f,3.1f,outro),.72f,lane*.18f);
            Vector3 offset=Vector3.Lerp(new Vector3(-8.7f,12.8f,-10.4f),new Vector3(-7.4f,11.8f,-9.8f),intro);
            view.orthographicSize=Mathf.Lerp(6.15f,6.65f,intro)+outro*.28f;
            view.transform.position=target+offset;
            view.transform.rotation=Quaternion.LookRotation(target-view.transform.position,Vector3.up);
        }

        private void CreatePalette()
        {
            asphalt=Paint("Warm asphalt",new Color32(117,112,105,255));stone=Paint("Paving",new Color32(190,182,168,255));
            cream=Paint("Lime plaster",CampaignUI.Pale);brick=Paint("Terracotta",CampaignUI.Vermilion);
            paintedInk=Paint("Painted ink blue",CampaignUI.Ink);ink=Paint("Deep ink",CampaignUI.Ink);
            metal=Paint("Dull steel",new Color32(122,128,135,255));leaf=Paint("Warm foliage",new Color32(158,133,76,255));
            ochre=Paint("Awning canvas",CampaignUI.Gold);water=Paint("Puddle",new Color32(131,147,164,255));
        }
        private Material Paint(string name,Color colour)
        {
            Material mat=new Material(Resources.Load<Shader>("Shaders/GuochaoToon")){name="Travel_"+name};
            mat.color=QualitySettings.activeColorSpace==ColorSpace.Linear?colour.linear:colour;
            mat.SetColor("_LightTint",new Color(1.08f,1.01f,.88f));mat.SetColor("_ShadeTint",new Color(.76f,.79f,.84f));
            mat.SetFloat("_OutlineWidth",0);mat.SetShaderPassEnabled("OBJECT_INK",false);mat.SetFloat("_RampThreshold",.44f);
            materials.Add(mat);return mat;
        }
        private GameObject Shape(string name,Transform parent,Vector3 position,Vector3 scale,Material mat,PrimitiveType type=PrimitiveType.Cube)
        {
            GameObject go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=mat;
            Collider collider=go.GetComponent<Collider>();if(collider!=null)Destroy(collider);go.layer=TravelLayer;return go;
        }
        private Transform Group(string name,Transform parent)
        {GameObject go=new GameObject(name);go.transform.SetParent(parent,false);go.layer=TravelLayer;return go.transform;}
        private void BuildStreetTile(int index)
        {
            Transform tile=Group("Street_20m_"+index,transform);tiles.Add(tile);
            Shape("Road",tile,new Vector3(0,-.16f,0),new Vector3(20,.30f,7.2f),asphalt);
            Shape("Far pavement",tile,new Vector3(0,-.04f,4.05f),new Vector3(20,.34f,1.7f),stone);
            Shape("Near pavement",tile,new Vector3(0,-.04f,-4.05f),new Vector3(20,.34f,1.7f),stone);
            for(int j=0;j<10;j++)
            {
                Shape("Curb far",tile,new Vector3(-9+j*2,.12f,3.23f),new Vector3(1.94f,.20f,.23f),cream);
                Shape("Curb near",tile,new Vector3(-9+j*2,.12f,-3.23f),new Vector3(1.94f,.20f,.23f),cream);
            }
            for(int j=0;j<3;j++)
            {
                float x=-6.7f+j*6.65f;float height=3.6f+((j+index)%3)*.55f;
                // Only the middle shop of each 20m tile tries the AI shopfront;
                // on success the blockout below is skipped entirely, on any
                // failure it builds verbatim so no doubled shop can appear.
                bool replaced=j==1&&StreetShopfrontAssets.TryCreateShopfront(
                    tile,new Vector3(x,0,6.2f),6.4f,3.6f,height,.13f,materials,out _);
                if(!replaced)BuildBlockShop(tile,index,j,x,height);
                string[] names={"早点铺","街坊五金","新鲜蔬果","修车补胎","阿婆杂货","老街茶铺"};
                WorldLabel(names[(j+index*2)%names.Length],tile,new Vector3(x,2.37f,4.05f),.27f,cream.color);
                Shape("Delivery crate",tile,new Vector3(x+2.1f,.38f,3.8f),new Vector3(.65f,.46f,.55f),ochre);
                Shape("Crate lip",tile,new Vector3(x+2.1f,.62f,3.8f),new Vector3(.70f,.06f,.60f),brick);
                Plant(tile,new Vector3(x-2.5f,.15f,3.8f),.65f);
            }
            // Foreground objects are deliberately low so they never occlude the driver.
            Plant(tile,new Vector3(-7,.15f,-4.1f),.7f);Plant(tile,new Vector3(5,.15f,-4.1f),.5f);
            Shape("Low wall",tile,new Vector3(0,.35f,-4.9f),new Vector3(20,.7f,.22f),cream);
            Lamp(tile,new Vector3(8,.1f,3.7f));
            Shape("Drain",tile,new Vector3(-4,.004f,-2.9f),new Vector3(1.1f,.02f,.30f),ink);
        }
        // Original block-built shop, kept verbatim as the AI swap fallback.
        private void BuildBlockShop(Transform tile,int index,int j,float x,float height)
        {
            Shape("Shop plaster",tile,new Vector3(x,height/2,6.2f),new Vector3(6.4f,height,3.6f),(index+j)%2==0?cream:brick);
            Shape("Shop doorway",tile,new Vector3(x,.95f,4.34f),new Vector3(1.55f,1.9f,.08f),ink);
            Shape("Shop shutter",tile,new Vector3(x-1.85f,1.03f,4.30f),new Vector3(1.65f,1.9f,.12f),paintedInk);
            for(int s=0;s<6;s++)Shape("Shutter horizontal",tile,new Vector3(x-1.85f,.25f+s*.29f,4.21f),new Vector3(1.65f,.035f,.035f),metal);
            Shape("Signboard",tile,new Vector3(x,2.38f,4.17f),new Vector3(5.1f,.56f,.18f),paintedInk);
            Shape("Canvas awning",tile,new Vector3(x,2.03f,3.98f),new Vector3(4.8f,.12f,.72f),(j+index)%2==0?ochre:brick);
            for(int k=0;k<2;k++)
            {
                float wx=x-1.7f+k*3.4f;
                Shape("Upper window",tile,new Vector3(wx,height-.65f,4.35f),new Vector3(.9f,.8f,.06f),paintedInk);
                Shape("Window centre",tile,new Vector3(wx,height-.65f,4.29f),new Vector3(.055f,.85f,.055f),cream);
            }
            Shape("Shop step",tile,new Vector3(x,.18f,4.0f),new Vector3(1.8f,.24f,.55f),stone);
        }
        private void Plant(Transform parent,Vector3 position,float height)
        {
            Shape("Clay pot",parent,position+Vector3.up*.16f,new Vector3(.36f,.18f,.36f),brick,PrimitiveType.Cylinder);
            Shape("Foliage",parent,position+Vector3.up*(.32f+height*.30f),new Vector3(.65f,height,.6f),leaf,PrimitiveType.Sphere);
        }
        private void Lamp(Transform parent,Vector3 position)
        {
            Shape("Street lamp post",parent,position+Vector3.up*1.75f,new Vector3(.075f,1.75f,.075f),paintedInk,PrimitiveType.Cylinder);
            Shape("Lamp arm",parent,position+new Vector3(0,3.4f,-.4f),new Vector3(.08f,.08f,.85f),paintedInk);
            Shape("Lamp cap",parent,position+new Vector3(0,3.38f,-.85f),new Vector3(.38f,.12f,.50f),ochre);
        }
        private void BuildHazard(float ahead,float lateral,bool puddle)
        {
            Transform root=Group(puddle?"Street puddle":"Street drain cover",transform);
            if(puddle)
            {
                Shape("Water shape",root,Vector3.zero,new Vector3(1.8f,.025f,1.10f),water,PrimitiveType.Cylinder);
                Shape("Water highlight",root,new Vector3(-.15f,.029f,.10f),new Vector3(.62f,.01f,.08f),cream);
            }
            else
            {
                Shape("Manhole rim",root,Vector3.zero,new Vector3(1.12f,.04f,1.12f),ink,PrimitiveType.Cylinder);
                Shape("Cover",root,new Vector3(0,.045f,0),new Vector3(.94f,.016f,.94f),metal,PrimitiveType.Cylinder);
                for(int n=-2;n<=2;n++)Shape("Cover groove",root,new Vector3(n*.15f,.067f,0),new Vector3(.035f,.01f,.64f),ink);
            }
            hazards.Add(new Hazard{root=root,distance=ahead,lane=lateral,radius=puddle?.55f:.52f,puddle=puddle});
        }
        private void BuildArrival()
        {
            arrival=Group("Huimin warehouse destination",transform);
            Shape("Warehouse",arrival,new Vector3(0,2.6f,6.4f),new Vector3(11,5.2f,4.5f),cream);
            Shape("Warehouse opening",arrival,new Vector3(0,1.55f,4.1f),new Vector3(5.2f,3.1f,.12f),paintedInk);
            Shape("Warehouse sign",arrival,new Vector3(0,3.7f,3.95f),new Vector3(9,.85f,.25f),brick);
            WorldLabel("惠民仓储超市",arrival,new Vector3(0,3.72f,3.77f),.45f,cream.color);
            for(int i=-1;i<=1;i++)Shape("Arrival bollard",arrival,new Vector3(i*2.4f,.4f,3.5f),new Vector3(.12f,.4f,.12f),ochre,PrimitiveType.Cylinder);
        }

        private GameObject Model(string name,Transform parent)
        {
            GameObject source=Resources.Load<GameObject>("Models/"+name);
            if(source==null)throw new InvalidOperationException("骑行缺少真实模型："+name);
            Transform wrapper=Group(name+"_Travel",parent);Instantiate(source,wrapper,false);
            foreach(Transform node in wrapper.GetComponentsInChildren<Transform>(true))
                if(node.name.StartsWith("Collider_",StringComparison.Ordinal))node.gameObject.SetActive(false);
            foreach(Animator animator in wrapper.GetComponentsInChildren<Animator>())animator.enabled=false;
            foreach(Animation animation in wrapper.GetComponentsInChildren<Animation>())animation.enabled=false;
            GameArtRendering.Apply(wrapper.gameObject,name=="Player"||name=="Stove");return wrapper.gameObject;
        }
        private static Transform Marker(Transform root,string suffix)
        {foreach(Transform t in root.GetComponentsInChildren<Transform>(true))if(t.name==suffix||t.name.EndsWith(":"+suffix,StringComparison.Ordinal))return t;return null;}
        private static void Face(Transform root,string originName,string forwardName)
        {
            Transform origin=Marker(root,originName),forward=Marker(root,forwardName);
            if(origin==null||forward==null)throw new InvalidOperationException("骑行模型缺少朝向定位点："+root.name);
            Vector3 direction=forward.position-origin.position;Transform deck=originName=="CartOrigin"?Marker(root,"CartDeck"):null;
            if(deck!=null)
            {
                Vector3 authoredUp=Vector3.ProjectOnPlane(deck.position-origin.position,direction.normalized).normalized;
                root.rotation=Quaternion.LookRotation(Vector3.right,Vector3.up)*Quaternion.Inverse(Quaternion.LookRotation(direction.normalized,authoredUp))*root.rotation;
            }
            else{direction.y=0;root.rotation=Quaternion.FromToRotation(direction.normalized,Vector3.right)*root.rotation;}
        }
        private void BuildVehicle(bool stoveLoaded,string[] equipment)
        {
            vehicle=Group("Vehicle logical root",transform);suspension=Group("Visual suspension",vehicle);
            GameObject cart=Model("CartAI",suspension);Face(cart.transform,"CartOrigin","CartForward");
            Transform origin=Marker(cart.transform,"CartOrigin");cart.transform.position-=origin.position;
            VehicleContainerModules.Install(cart.transform);
            if(equipment!=null)
            {
                // The same measured plan that permits loading owns every visible
                // transport pose, including tools and package racks. Keep the
                // renderer roots under suspension so the whole load moves together.
                VehicleLoadout loadout=suspension.gameObject.AddComponent<VehicleLoadout>();
                loadout.Initialize(cart.transform,EquipmentVisualFactory.Create);
                loadout.PresentTransport(equipment,Array.Empty<string>(),Array.Empty<string>());
                Debug.Log("MORNING_TRAVEL_LAYOUT items="+loadout.Plan.placements.Count+" validated="+loadout.Plan.FullyValidated);
            }
            else if(stoveLoaded)
            {
                GameObject stove=Model("Stove",suspension);Transform slot=Marker(cart.transform,"StoveLoadedPlacement");
                if(slot==null)throw new InvalidOperationException("骑行车缺少灶台装载点");
                stove.transform.SetPositionAndRotation(slot.position,slot.rotation*Quaternion.Inverse(origin.rotation)*cart.transform.rotation);
            }
            // The provider's vehicle is a single mesh. Axle-mounted rim markers
            // provide genuine angular motion while preserving the source tyre mesh.
            WheelMark(new Vector3(1.30f,.30f,-.14f));WheelMark(new Vector3(-1.02f,.30f,-.68f));WheelMark(new Vector3(-1.02f,.30f,.68f));
            driver=Model("Player",suspension).transform;Face(driver,"PlayerOrigin","PlayerForward");
            Transform hips=Marker(driver,"Hips");
            if(hips!=null)
            {
                driver.position+=new Vector3(.19f,1.02f,0)-hips.position;
                Transform spine=Marker(driver,"Spine");if(spine!=null)spine.rotation=Quaternion.AngleAxis(-10,Vector3.forward)*spine.rotation;
                PoseLimb("LeftUpLeg","LeftLeg","LeftFoot",new Vector3(.53f,.43f,.15f),Vector3.right);
                PoseLimb("RightUpLeg","RightLeg","RightFoot",new Vector3(.53f,.43f,-.15f),Vector3.right);
                PoseLimb("LeftArm","LeftForeArm","LeftHand",new Vector3(.86f,1.26f,.25f),Vector3.forward);
                PoseLimb("RightArm","RightForeArm","RightHand",new Vector3(.86f,1.26f,-.25f),Vector3.back);
            }
            driverRest=driver.localPosition;
        }
        private void PoseLimb(string a,string b,string c,Vector3 target,Vector3 bend)
        {
            Transform upper=Marker(driver,a),lower=Marker(driver,b),end=Marker(driver,c);if(upper==null||lower==null||end==null)return;
            float l1=Vector3.Distance(upper.position,lower.position),l2=Vector3.Distance(lower.position,end.position);
            Vector3 delta=target-upper.position;float d=Mathf.Clamp(delta.magnitude,.001f,l1+l2-.001f);Vector3 axis=delta.normalized;
            Vector3 side=Vector3.ProjectOnPlane(bend,axis).normalized;
            float along=(l1*l1+d*d-l2*l2)/(2*d);float height=Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            Vector3 elbow=upper.position+axis*along+side*height;
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(end.position-lower.position,target-lower.position)*lower.rotation;
        }
        private void WheelMark(Vector3 position)
        {
            Transform wheel=Group("Rotating wheel rim",suspension);wheel.localPosition=position;wheelMarks.Add(wheel);
            for(int i=0;i<3;i++)
            {
                GameObject spoke=Shape("Rim spoke",wheel,Vector3.zero,new Vector3(.39f,.025f,.018f),metal);
                spoke.transform.localRotation=Quaternion.Euler(0,0,i*60);
            }
        }
        private void BuildCamera()
        {
            GameObject cameraObject=new GameObject("MorningTravelCamera");cameraObject.transform.SetParent(transform,false);
            view=cameraObject.AddComponent<Camera>();view.orthographic=true;view.nearClipPlane=.1f;view.farClipPlane=100;
            view.cullingMask=1<<TravelLayer;view.clearFlags=CameraClearFlags.SolidColor;view.backgroundColor=CampaignUI.Pale;
            view.allowMSAA=true;GameArtRendering.Attach(view,transform);
            Directional("Morning key",new Vector3(45,-30,0),new Color(1,.91f,.72f),1.1f,true);
            Directional("Sky fill",new Vector3(58,150,0),new Color(.66f,.78f,.86f),.32f,false);
        }
        private void Directional(string name,Vector3 angles,Color colour,float intensity,bool shadows)
        {
            Transform root=Group(name,transform);root.rotation=Quaternion.Euler(angles);Light light=root.gameObject.AddComponent<Light>();
            light.type=LightType.Directional;light.color=colour;light.intensity=intensity;light.cullingMask=1<<TravelLayer;
            light.shadows=shadows?LightShadows.Soft:LightShadows.None;light.shadowStrength=.5f;
        }
        private void WorldLabel(string text,Transform parent,Vector3 position,float size,Color colour)
        {
            Transform root=Group(text,parent);root.localPosition=position;
            TextMesh label=root.gameObject.AddComponent<TextMesh>();label.text=text;label.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            label.fontSize=64;label.characterSize=size*.16f;label.anchor=TextAnchor.MiddleCenter;label.color=colour;
            root.GetComponent<MeshRenderer>().sharedMaterial=label.font.material;
        }
        private void BuildOverlay(string loadCaption)
        {
            var overlay=new GameObject("Travel captions",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            Transform root=overlay.transform;root.SetParent(transform,false);Canvas canvas=overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=450;
            CanvasScaler scaler=overlay.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);
            Text title=ScreenLabel(root,"清晨采购",new Vector2(0,1),new Vector2(42,-38),new Vector2(520,48),30,TextAnchor.MiddleLeft);
            ScreenLabel(root,loadCaption,new Vector2(0,1),new Vector2(43,-85),new Vector2(550,40),19,TextAnchor.MiddleLeft);
            instruction=ScreenLabel(root,"",new Vector2(.5f,0),new Vector2(0,38),new Vector2(950,52),22,TextAnchor.MiddleCenter);
            caption=ScreenLabel(root,"",new Vector2(.5f,0),new Vector2(0,96),new Vector2(700,42),22,TextAnchor.MiddleCenter);
            captionGroup=caption.transform.parent.gameObject.AddComponent<CanvasGroup>();captionGroup.alpha=0;
        }
        private Text ScreenLabel(Transform parent,string value,Vector2 anchor,Vector2 offset,Vector2 size,int fontSize,TextAnchor alignment)
        {
            GameObject go=new GameObject("Travel caption",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            RectTransform rt=go.GetComponent<RectTransform>();rt.anchorMin=rt.anchorMax=anchor;rt.pivot=new Vector2(anchor.x,.5f);rt.anchoredPosition=offset;rt.sizeDelta=size;
            go.GetComponent<Image>().color=CampaignUI.WithAlpha(CampaignUI.PaperColor,239/255f);go.GetComponent<Image>().raycastTarget=false;
            GameObject child=new GameObject("Text",typeof(RectTransform),typeof(Text));child.transform.SetParent(go.transform,false);
            RectTransform tr=child.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(15,0);tr.offsetMax=new Vector2(-15,0);
            Text label=child.GetComponent<Text>();label.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");label.fontSize=fontSize;label.text=value;label.alignment=alignment;
            label.color=CampaignUI.Ink;label.raycastTarget=false;return label;
        }
        private void BuildSound()
        {
            roadClip=Sound("Original morning tyre roll",1.4f,false);impactClip=Sound("Original gentle cart bump",.22f,true);
            roadSound=gameObject.AddComponent<AudioSource>();roadSound.clip=roadClip;roadSound.loop=true;roadSound.volume=0;roadSound.playOnAwake=false;roadSound.Play();
            bumpSound=gameObject.AddComponent<AudioSource>();bumpSound.playOnAwake=false;bumpSound.volume=.16f;
        }
        private static AudioClip Sound(string name,float seconds,bool impact)
        {
            const int rate=22050;float[] samples=new float[(int)(rate*seconds)];System.Random random=new System.Random(6021);float previous=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)rate;previous=previous*.93f+((float)random.NextDouble()*2-1)*.07f;
                float env=impact?Mathf.Exp(-t*24):.25f+.025f*Mathf.Sin(t*18);
                samples[i]=(previous+Mathf.Sin(t*2*Mathf.PI*(impact?105:66))*.08f)*env;
            }
            AudioClip clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        private static void SetLayerRecursively(Transform root)
        {foreach(Transform node in root.GetComponentsInChildren<Transform>(true))node.gameObject.layer=TravelLayer;}
        public void Release()
        {
            if(released)return;released=true;
            if(view!=null)view.enabled=false;
            if(originalCamera!=null)originalCamera.enabled=originalCameraEnabled;
            foreach(Light light in suspendedLights)if(light!=null)light.enabled=true;
            foreach(Canvas canvas in suspendedCanvases)if(canvas!=null)canvas.enabled=true;
            if(roadSound!=null)roadSound.Stop();if(bumpSound!=null)bumpSound.Stop();
            foreach(Material material in materials)if(material!=null)Destroy(material);
            if(roadClip!=null)Destroy(roadClip);if(impactClip!=null)Destroy(impactClip);
            Debug.Log("MORNING_TRAVEL_END seconds="+Elapsed.ToString("F2")+" bumps="+Bumps);
        }
        private void OnDestroy(){Release();}
    }
}
