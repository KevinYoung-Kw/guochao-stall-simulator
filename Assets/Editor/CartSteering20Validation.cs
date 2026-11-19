using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Isolated open-floor/one-wall fixtures with the imported player and full
    // shopping cart. Every measured movement uses production DriveInput. No
    // hardware input, market route, NPC avoidance or subjective-feel claim.
    public static class CartSteering20Validation
    {
        [Serializable] public sealed class Check
        { public string name,detail="";public bool passed;public float dt,forwardMetres,turnMetres,reverseMetres,stopDrift,pivotResidual,facingNormSquared; }
        [Serializable] public sealed class Report
        {
            public string scope="Declared isolated floor/wall/aisle/pole and initial actor/cart poses; actual imported ShoppingCart full collider, CharacterController and PlayerMotor.DriveInput. Scripted v20 throttle/steering through production DriveInput, not keyboard or market-route completion. Old attached screen-direction/side-strafe expectations are superseded; on-foot mapping is unchanged.";
            public bool success;public List<Check> checks=new List<Check>();public string[] engineErrors;
        }
        sealed class Fixture
        { public PlayerMotor player;public LinkedTrolleyMotor cart;public Camera camera;public BoxCollider volume;public Vector3 originalCartSize; }
        static readonly List<GameObject> garbage=new List<GameObject>();
        static readonly List<string> errors=new List<string>();

        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--cart-steering-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --cart-steering-output <json>");
            string output=Path.GetFullPath(args[at+1]);var report=new Report();errors.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Application.logMessageReceived+=Log;
            try
            {
                foreach(float dt in new[]{1f/30,1f/60,1f/120})
                {
                    CheckCase(report,"walking-screen-axes-and-diagonal-speed",dt,false,new Vector3(50,37,0),Axes);
                    CheckCase(report,"cart-heading-forward-reverse-camera-independent",dt,true,new Vector3(50,73,0),Heading);
                    CheckCase(report,"cart-stationary-left-right-release-gates",dt,true,new Vector3(50,-42,0),PivotAndLocks);
                    CheckCase(report,"cart-curves-independent-full-throttle",dt,true,new Vector3(50,37,0),Curves);
                    CheckCase(report,"cart-full-envelope-wall-stop-and-backout",dt,true,new Vector3(50,0,0),Wall);
                    CheckCase(report,"cart-narrow-aisle-blocks",dt,true,new Vector3(50,0,0),NarrowAisle);
                    CheckCase(report,"cart-full-width-aisle-passes",dt,true,new Vector3(50,0,0),WideAisle);
                    CheckCase(report,"cart-angular-sweep-blocks-intermediate-pole",dt,true,new Vector3(50,0,0),TurningPole);
                    CheckCase(report,"cart-park-walk-reattach-save-pose",dt,true,new Vector3(50,37,0),ParkAndAttach);
                }
                foreach(Vector3 view in new[]{new Vector3(90,20,0),new Vector3(48,-52,22)})
                    CheckCase(report,"walking-steep-or-rolled-camera-"+view,1f/60,false,view,Axes);
            }
            finally{Cleanup();Application.logMessageReceived-=Log;}
            report.engineErrors=errors.ToArray();report.success=errors.Count==0&&report.checks.TrueForAll(c=>c.passed);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("CART_STEERING20_"+(report.success?"PASS":"FAIL")+" "+output);
            if(Application.isBatchMode)EditorApplication.Exit(report.success?0:1);
        }
        static void CheckCase(Report report,string name,float dt,bool cart,Vector3 camera,Action<Fixture,Check> action)
        {
            var check=new Check{name=name,dt=dt};report.checks.Add(check);
            try{var fixture=Build(cart,camera);action(fixture,check);check.passed=true;}
            catch(Exception error){check.detail=error.ToString();}
            finally{Cleanup();}
        }
        static Fixture Build(bool withCart,Vector3 cameraEuler)
        {
            Box("Declared open floor",new Vector3(0,-.1f,0),new Vector3(100,.2f,100));
            var root=new GameObject("Declared controller fixture");garbage.Add(root);root.transform.position=new Vector3(0,.03f,0);
            var visual=new GameObject("Actual player visual");visual.transform.SetParent(root.transform,false);
            GameObject playerModel=Resources.Load<GameObject>("Models/Player");Need(playerModel!=null,"Imported Player model missing.");
            UnityEngine.Object.Instantiate(playerModel,visual.transform,false);
            var cameraObject=new GameObject("Declared fixed oblique camera");garbage.Add(cameraObject);
            var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.enabled=false;
            camera.transform.rotation=Quaternion.Euler(cameraEuler);camera.transform.position=-camera.transform.forward*15;
            Vector3 facing=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up);
            if(facing.sqrMagnitude<.001f)facing=Vector3.ProjectOnPlane(camera.transform.up,Vector3.up);
            visual.transform.rotation=Quaternion.LookRotation(facing.normalized);
            var player=root.AddComponent<PlayerMotor>();player.Initialize(visual.transform,camera);player.enabled=false;
            var fixture=new Fixture{player=player,camera=camera};
            if(withCart)
            {
                var cartRoot=new GameObject("Actual ShoppingCart wrapper");garbage.Add(cartRoot);
                GameObject prefab=Resources.Load<GameObject>("Models/ShoppingCart");Need(prefab!=null,"Imported ShoppingCart model missing.");
                UnityEngine.Object.Instantiate(prefab,cartRoot.transform,false);
                Transform forward=Find(cartRoot.transform,"ShoppingCartForward"),handle=Find(cartRoot.transform,"HandleGrip");
                Need(forward!=null&&handle!=null,"Imported shopping cart anchors missing.");
                Bounds imported=WorldRuntime.RenderBounds(cartRoot);
                fixture.cart=root.AddComponent<LinkedTrolleyMotor>();Physics.SyncTransforms();
                fixture.cart.Initialize(player,cartRoot.transform,forward,handle);player.pushing=true;
                fixture.volume=cartRoot.GetComponent<BoxCollider>();fixture.originalCartSize=fixture.volume.size;
                Need(Mathf.Abs(fixture.originalCartSize.x-imported.size.x)<.0001f&&Mathf.Abs(fixture.originalCartSize.z-imported.size.z)<.0001f,"Cart horizontal envelope differs from the complete imported model.");
                Need(fixture.cart.Width>=.87f,"Imported cart width unexpectedly reduced.");
            }
            Physics.SyncTransforms();return fixture;
        }
        static void Axes(Fixture f,Check c)
        {
            Vector2[] axes={Vector2.up,Vector2.down,Vector2.left,Vector2.right,new Vector2(1,1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(-1,-1)};
            float reference=-1;
            foreach(Vector2 axis in axes)
            {
                // Each segment begins at rest; no transforms are reset between
                // directions. The floor is deliberately unobstructed.
                f.player.StopMotion();Vector3 start=f.player.transform.position;
                for(int i=0;i<Mathf.RoundToInt(1/c.dt);i++)Drive(f,axis,c.dt);
                Vector3 delta=Flat(f.player.transform.position-start);float length=delta.magnitude;
                Need(length>.5f,"Input failed to produce meaningful movement: "+axis);
                if(reference<0)reference=length;
                Need(Mathf.Abs(length-reference)<.01f,"Cardinal/diagonal world speed differs: "+axis+" length="+length+" cardinal="+reference);
                Vector2 screen=Projection(f,delta);
                if(axis.x==0)Need(screen.y*axis.y>0&&Mathf.Abs(screen.x)<.002f,"W/S did not stay on the screen vertical axis: "+screen);
                if(axis.y==0)Need(screen.x*axis.x>0&&Mathf.Abs(screen.y)<.002f,"A/D did not stay on the screen horizontal axis: "+screen);
                Vector3 beforeStop=f.player.transform.position;Drive(f,Vector2.zero,c.dt);
                float drift=Flat(f.player.transform.position-beforeStop).magnitude;c.stopDrift=Mathf.Max(c.stopDrift,drift);
                Need(drift<.0001f,"Released keys still produced planar drift: "+drift);
            }
            c.forwardMetres=reference;c.detail="Eight directions use actual DriveInput; screen cardinals align, diagonals preserve world speed, release stops on the next input step.";
        }
        static void Heading(Fixture f,Check c)
        {
            Quaternion initial=f.cart.Facing;Vector3 forward=initial*Vector3.forward;
            Vector3 start=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(1/c.dt);i++)Drive(f,Vector2.up,c.dt);
            Vector3 delta=Flat(f.player.transform.position-start);c.forwardMetres=Vector3.Dot(delta,forward);
            Need(c.forwardMetres>1&&Vector3.Cross(delta,forward).magnitude<.002f,"W did not push along the actual cart front.");
            Need(Quaternion.Angle(initial,f.cart.Facing)<.001f,"W changed heading without steering.");
            // Change ONLY the view, without touching either physical actor.
            f.camera.transform.rotation=Quaternion.Euler(50,-104,0);
            Vector3 reverse=f.player.transform.position;Drive(f,Vector2.down,c.dt);
            Need(Vector3.Dot(Flat(f.player.transform.position-reverse),forward)<-.00001f,"S retained forward momentum instead of reversing immediately.");
            for(int i=1;i<Mathf.RoundToInt(1/c.dt);i++)Drive(f,Vector2.down,c.dt);
            c.reverseMetres=-Vector3.Dot(Flat(f.player.transform.position-reverse),forward);
            Need(c.reverseMetres>1&&Quaternion.Angle(initial,f.cart.Facing)<.001f,"S failed to reverse or turned the cart 180 degrees.");
            Vector3 stopped=f.player.transform.position;Drive(f,Vector2.zero,c.dt);
            c.stopDrift=Flat(f.player.transform.position-stopped).magnitude;
            Need(c.stopDrift<.0001f,"Released throttle drifted.");
            c.detail="Actual cart heading controls forward/reverse before and after camera rotation; immediate signed reverse and zero-key stop.";
        }
        static void PivotAndLocks(Fixture f,Check c)
        {
            Quaternion initial=f.cart.Facing;Vector3 position=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(.3f/c.dt);i++)Drive(f,Vector2.right,c.dt);
            float angle=Vector3.SignedAngle(initial*Vector3.forward,f.cart.Facing*Vector3.forward,Vector3.up);
            Need(angle>2&&angle<=f.cart.turnDegreesPerSecond*.3f+.1f,"D did not make a bounded right turn.");
            Need(Flat(f.player.transform.position-position).magnitude<.0001f,"D strafed the player instead of steering.");
            for(int i=0;i<Mathf.RoundToInt(.3f/c.dt);i++)Drive(f,Vector2.left,c.dt);
            c.pivotResidual=Quaternion.Angle(initial,f.cart.Facing);c.facingNormSquared=Quaternion.Dot(f.cart.Facing,f.cart.Facing);
            Need(c.pivotResidual<.02f,"A did not symmetrically steer left: residual="+c.pivotResidual+" normSquared="+c.facingNormSquared);
            Need(Flat(f.player.transform.position-position).magnitude<.0001f,"Stationary steering caused body translation.");
            Quaternion stopped=f.cart.Facing;
            for(int i=0;i<10;i++)Drive(f,Vector2.zero,c.dt);
            Need(Quaternion.Angle(stopped,f.cart.Facing)<.001f,"Released steering kept turning.");
            f.player.presentationLocked=true;Drive(f,Vector2.one,c.dt);f.player.presentationLocked=false;
            f.player.inputEnabled=false;Drive(f,Vector2.one,c.dt);f.player.inputEnabled=true;
            Need(Quaternion.Angle(stopped,f.cart.Facing)<.001f&&Flat(f.player.transform.position-position).magnitude<.0001f,"Presentation/input lock allowed cart motion.");
            f.player.DriveInput(Vector2.up,true,c.dt);Physics.SyncTransforms();
            Need(!f.player.IsRunning,"Pushing incorrectly enabled sprint.");
            c.turnMetres=angle;c.detail="A/D pivots without body strafe; release, presentation gate and disabled input halt both axes. turnMetres records signed pivot degrees for this case.";
        }
        static float Travel(Fixture f,Vector2 axis,float seconds,float dt)
        {
            float distance=0;
            for(int i=0;i<Mathf.RoundToInt(seconds/dt);i++)
            {
                Vector3 previous=f.player.transform.position;Quaternion oldFacing=f.cart.Facing;
                Drive(f,axis,dt);Vector3 delta=Flat(f.player.transform.position-previous);distance+=delta.magnitude;
                float yaw=Vector3.SignedAngle(oldFacing*Vector3.forward,f.cart.Facing*Vector3.forward,Vector3.up);
                if(axis.x!=0)Need(yaw*axis.x>=-.001f,"Steering turned opposite to the requested A/D axis.");
                if(axis.y!=0&&delta.sqrMagnitude>.00000001f)
                {
                    Vector3 mid=Quaternion.Slerp(oldFacing,f.cart.Facing,.5f)*Vector3.forward;
                    Need(Vector3.Dot(delta.normalized,mid)*Mathf.Sign(axis.y)>.998f,"Arc translation was not aligned with the cart's swept heading.");
                }
            }
            return distance;
        }
        static void Curves(Fixture f,Check c)
        {
            f.player.StopMotion();float straight=Travel(f,Vector2.up,1,c.dt);
            f.player.StopMotion();Quaternion start=f.cart.Facing;
            float curved=Travel(f,Vector2.one,1,c.dt);
            float right=Vector3.SignedAngle(start*Vector3.forward,f.cart.Facing*Vector3.forward,Vector3.up);
            Need(right>10||Quaternion.Angle(start,f.cart.Facing)>170,"W+D did not turn right.");
            Need(Mathf.Abs(curved-straight)<.025f,"Adding D reduced forward throttle as if axes were normalized: straight="+straight+" curved="+curved);
            f.player.StopMotion();start=f.cart.Facing;
            float left=Travel(f,new Vector2(-1,1),1,c.dt);
            Need(Quaternion.Angle(start,f.cart.Facing)>10,"W+A did not produce a steering arc.");
            Need(Mathf.Abs(left-curved)<.025f,"Left/right steering changed path speed.");
            Vector3 stopped=f.player.transform.position;Quaternion facing=f.cart.Facing;
            for(int i=0;i<10;i++)Drive(f,Vector2.zero,c.dt);
            Need(Flat(f.player.transform.position-stopped).magnitude<.0001f&&Quaternion.Angle(facing,f.cart.Facing)<.001f,"Curved motion persisted after key release.");
            c.forwardMetres=straight;c.turnMetres=curved;c.detail="Per-frame arc tangents match swept heading, full throttle survives combined steering, release has no residual movement.";
        }
        static void Wall(Fixture f,Check c)
        {
            BoxCollider wall=Box("Declared thin wall",new Vector3(0,1.5f,5),new Vector3(20,3,.12f));Physics.SyncTransforms();
            Vector3 start=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(3/c.dt);i++){Drive(f,Vector2.up,c.dt);NoPenetration(f,wall);}
            c.forwardMetres=Flat(f.player.transform.position-start).magnitude;
            Need(c.forwardMetres>.2f&&f.volume.bounds.max.z<=wall.bounds.min.z+.002f,"Cart did not stop at the full front footprint.");
            Need(f.cart.Blocked,"Blocked forward throttle was not reported.");
            Vector3 reverse=f.player.transform.position;Quaternion facing=f.cart.Facing;
            for(int i=0;i<Mathf.RoundToInt(1/c.dt);i++){Drive(f,Vector2.down,c.dt);NoPenetration(f,wall);}
            c.reverseMetres=reverse.z-f.player.transform.position.z;
            Need(c.reverseMetres>.5f&&Quaternion.Angle(facing,f.cart.Facing)<.001f,"S could not back out of contact without yawing.");
            c.detail="Real unchanged complete cart stops at wall and backs out along heading. No sideways-escape expectation from superseded controls.";
        }
        static void NarrowAisle(Fixture f,Check c)=>Aisle(f,c,.5f,false);
        static void WideAisle(Fixture f,Check c)=>Aisle(f,c,.6f,true);
        static void Aisle(Fixture f,Check c,float railX,bool passes)
        {
            var left=Box("Declared left shelf rail",new Vector3(-railX,1,6),new Vector3(.2f,2,8));
            var right=Box("Declared right shelf rail",new Vector3(railX,1,6),new Vector3(.2f,2,8));Physics.SyncTransforms();
            Vector3 start=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(3/c.dt);i++){Drive(f,Vector2.up,c.dt);NoPenetration(f,left);NoPenetration(f,right);}
            c.forwardMetres=f.player.transform.position.z-start.z;
            if(passes)Need(c.forwardMetres>5,"The 1.00 m aisle did not pass the unchanged real cart.");
            else
            {
                Need(c.forwardMetres<2&&f.cart.Blocked,"A 0.80 m aisle failed to block the full 0.88 m cart.");
                Vector3 reverse=f.player.transform.position;
                for(int i=0;i<Mathf.RoundToInt(.6f/c.dt);i++){Drive(f,Vector2.down,c.dt);NoPenetration(f,left);NoPenetration(f,right);}
                Need(reverse.z-f.player.transform.position.z>.3f,"The blocked cart could not reverse out of the narrow aisle.");
            }
            c.detail=passes?"Production heading input passes the declared 1.00 m aisle with the original full imported envelope.":"Production heading input blocks the original full envelope at the 0.80 m aisle and permits reverse retreat.";
        }
        static void TurningPole(Fixture f,Check c)
        {
            BoxCollider pole=Box("Declared intermediate swept-turn pole",new Vector3(1.1f,.65f,1.1f),new Vector3(.12f,1.3f,.12f));Physics.SyncTransforms();
            Vector3 start=f.player.transform.position;Quaternion initial=f.cart.Facing;
            Need(f.cart.IsPoseClear(start,initial)&&f.cart.IsPoseClear(start,initial*Quaternion.Euler(0,90,0)),"Pole fixture must have clear start and 90-degree endpoint.");
            for(int i=0;i<Mathf.RoundToInt(2/c.dt);i++){Drive(f,Vector2.right,c.dt);NoPenetration(f,pole);}
            float angle=Quaternion.Angle(initial,f.cart.Facing);
            Need(angle>0&&angle<85,"Rotation skipped the intermediate pole or never started.");
            Need(f.cart.Blocked,"Blocked pure-turn command did not report obstruction.");
            Need(Flat(f.player.transform.position-start).magnitude<.0001f,"Collision recovery translated the player during a pure turn.");
            c.detail="Full imported cart angular sweep stops before an intermediate pole even though its 90-degree endpoint would be clear.";
        }
        static void ParkAndAttach(Fixture f,Check c)
        {
            Travel(f,Vector2.up,.3f,c.dt);Drive(f,Vector2.zero,c.dt);
            Vector3 parked=f.cart.Trolley.position;Quaternion parkedRotation=f.cart.Trolley.rotation;
            f.cart.Detach();Need(!f.cart.IsAttached&&f.player.LinkedTrolley==null&&!f.player.pushing&&!f.volume.isTrigger,"Detach did not release player or make the parked cart solid.");
            Need(f.cart.Trolley.position==parked&&Quaternion.Angle(parkedRotation,f.cart.Trolley.rotation)<.001f,"Detaching snapped cart pose.");
            f.player.StopMotion();Vector3 walking=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(.4f/c.dt);i++)Drive(f,Vector2.down,c.dt);
            Need(Flat(f.player.transform.position-walking).magnitude>.4f&&f.cart.Trolley.position==parked,"On-foot movement remained linked to parked cart.");
            f.player.StopMotion();
            for(int i=0;i<Mathf.RoundToInt(.4f/c.dt);i++)Drive(f,Vector2.up,c.dt);
            f.player.StopMotion();Need(f.cart.CanAttach,"Walking back to the handle did not permit reattachment.");
            Vector3 playerBefore=f.player.transform.position;Need(f.cart.TryAttach(),"Handle reattachment failed.");
            Need(f.player.transform.position==playerBefore&&f.cart.Trolley.position==parked&&Quaternion.Angle(parkedRotation,f.cart.Trolley.rotation)<.001f,"Reattachment snapped player or cart.");
            Need(f.cart.IsAttached&&f.player.LinkedTrolley==f.cart&&f.player.pushing&&f.volume.isTrigger,"Reattachment did not restore linked state.");
            var saved=new WorldSaveData();f.cart.CapturePose(saved);
            var decoded=JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(saved));f.cart.RestorePose(decoded);
            Need(f.cart.Trolley.position==parked&&Quaternion.Angle(parkedRotation,f.cart.Trolley.rotation)<.001f&&f.cart.IsAttached,"Cart pose/attachment did not survive its actual JSON save-data roundtrip.");
            c.detail="Real detach/attach API used by F, solid parked cart, independent walking, no-snap reattachment and JSON pose roundtrip. This is not hardware-key or whole-campaign save validation.";
        }
        static void Drive(Fixture f,Vector2 axis,float dt)
        {
            Quaternion before=f.cart!=null?f.cart.Facing:Quaternion.identity;
            Vector3 positionBefore=f.player.transform.position;
            f.player.DriveInput(axis,false,dt);Physics.SyncTransforms();
            Need(Vector3.Distance(f.player.Velocity,(f.player.transform.position-positionBefore)/dt)<.0001f,"Reported player Velocity does not match actual controller movement.");
            Need(Mathf.Approximately(f.player.Controller.radius,.25f)&&Mathf.Approximately(f.player.Controller.height,1.65f),"Player collision dimensions changed.");
            if(f.cart==null)return;
            Need(f.volume.size==f.originalCartSize,"Cart envelope changed during input.");
            float yaw=Quaternion.Angle(before,f.cart.Facing),limit=f.cart.turnDegreesPerSecond*dt+.01f;
            Need(yaw<=limit,"Cart yaw snapped past its turn rate: actual="+yaw+" limit="+limit+" beforeNorm="+Quaternion.Dot(before,before)+" afterNorm="+Quaternion.Dot(f.cart.Facing,f.cart.Facing));
        }
        static void NoPenetration(Fixture f,BoxCollider wall)
        {Need(!Physics.ComputePenetration(f.volume,f.volume.transform.position,f.volume.transform.rotation,wall,wall.transform.position,wall.transform.rotation,out _,out float depth)||depth<.0001f,"Full cart penetrated wall: "+depth);}
        static Vector2 Projection(Fixture f,Vector3 delta)=>new Vector2(Vector3.Dot(delta,f.camera.transform.right),Vector3.Dot(delta,f.camera.transform.up));
        static Vector3 Flat(Vector3 value){value.y=0;return value;}
        static Transform Find(Transform root,string name)
        {foreach(Transform child in root.GetComponentsInChildren<Transform>(true))if(child.name==name)return child;return null;}
        static BoxCollider Box(string name,Vector3 position,Vector3 size)
        {var go=new GameObject(name);garbage.Add(go);go.layer=WorldRuntime.EnvironmentLayer;go.transform.position=position;var box=go.AddComponent<BoxCollider>();box.size=size;return box;}
        static void Cleanup()
        {foreach(GameObject go in garbage)if(go!=null)UnityEngine.Object.DestroyImmediate(go);garbage.Clear();Physics.SyncTransforms();}
        static void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        static void Need(bool value,string detail){if(!value)throw new InvalidOperationException(detail);}
    }
}
