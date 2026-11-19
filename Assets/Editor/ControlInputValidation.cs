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
    public static class ControlInputValidation
    {
        [Serializable] public sealed class Check
        { public string name,detail="";public bool passed;public float dt,forwardMetres,turnMetres,reverseMetres,stopDrift; }
        [Serializable] public sealed class Report
        {
            public string scope="Declared isolated floor/wall and initial actor/cart poses; actual imported ShoppingCart full collider, CharacterController and PlayerMotor.DriveInput. Scripted axes, not keyboard or market route.";
            public bool success;public List<Check> checks=new List<Check>();public string[] engineErrors;
        }
        sealed class Fixture
        { public PlayerMotor player;public LinkedTrolleyMotor cart;public Camera camera;public BoxCollider volume;public Vector3 originalCartSize; }
        static readonly List<GameObject> garbage=new List<GameObject>();
        static readonly List<string> errors=new List<string>();

        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--control-input-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --control-input-output <json>");
            string output=Path.GetFullPath(args[at+1]);var report=new Report();errors.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Application.logMessageReceived+=Log;
            try
            {
                foreach(float dt in new[]{1f/30,1f/60,1f/120})
                {
                    CheckCase(report,"walking-screen-axes-and-diagonal-speed",dt,false,new Vector3(50,37,0),Axes);
                    CheckCase(report,"cart-screen-axes-and-diagonal-speed",dt,true,new Vector3(50,37,0),Axes);
                    CheckCase(report,"cart-right-angle-reverse-release",dt,true,new Vector3(50,37,0),TurnAndStop);
                    CheckCase(report,"cart-wall-stop-slide-and-retreat",dt,true,new Vector3(50,0,0),Wall);
                }
                foreach(Vector3 view in new[]{new Vector3(90,20,0),new Vector3(48,-52,22)})
                    CheckCase(report,"steep-or-rolled-camera-"+view,1f/60,false,view,Axes);
            }
            finally{Cleanup();Application.logMessageReceived-=Log;}
            report.engineErrors=errors.ToArray();report.success=errors.Count==0&&report.checks.TrueForAll(c=>c.passed);
            Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log("CONTROL_INPUT_"+(report.success?"PASS":"FAIL")+" "+output);
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
        static void TurnAndStop(Fixture f,Check c)
        {
            Vector3 start=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(1.2f/c.dt);i++)Drive(f,Vector2.up,c.dt);
            c.forwardMetres=Flat(f.player.transform.position-start).magnitude;
            Vector3 corner=f.player.transform.position;Drive(f,Vector2.right,c.dt);
            Vector2 first=Projection(f,Flat(f.player.transform.position-corner));
            Need(first.x>.0001f&&Mathf.Abs(first.y)<.0001f,"90-degree change retained the old W velocity: "+first);
            for(int i=0;i<Mathf.RoundToInt(.5f/c.dt);i++)Drive(f,Vector2.right,c.dt);
            Vector2 turn=Projection(f,Flat(f.player.transform.position-corner));c.turnMetres=turn.x;
            Need(turn.x>.5f&&Mathf.Abs(turn.y)<.002f,"Right-angle segment drifted along the old screen direction.");
            Vector3 reverse=f.player.transform.position;Drive(f,Vector2.left,c.dt);
            Need(Projection(f,Flat(f.player.transform.position-reverse)).x<-.0001f,"Opposite key still advances in the old direction.");
            for(int i=0;i<Mathf.RoundToInt(.5f/c.dt);i++)Drive(f,Vector2.left,c.dt);
            c.reverseMetres=-Projection(f,Flat(f.player.transform.position-reverse)).x;
            Vector3 stopped=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(.5f/c.dt);i++)Drive(f,Vector2.zero,c.dt);
            c.stopDrift=Flat(f.player.transform.position-stopped).magnitude;
            Need(c.reverseMetres>.5f&&c.stopDrift<.0001f,"Reverse or sustained idle failed.");
            c.detail="Continuous W -> D -> A -> release, without pose resets; actual cart turns remain bounded and full-size.";
        }
        static void Wall(Fixture f,Check c)
        {
            BoxCollider wall=Box("Declared thin wall",new Vector3(0,1.5f,5),new Vector3(20,3,.12f));Physics.SyncTransforms();
            Vector3 start=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(3/c.dt);i++){Drive(f,Vector2.up,c.dt);NoPenetration(f,wall);}
            c.forwardMetres=Flat(f.player.transform.position-start).magnitude;
            Need(c.forwardMetres>.2f&&f.volume.bounds.max.z<=wall.bounds.min.z+.002f,"Cart did not stop at the full front footprint.");
            Vector3 corner=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(1/c.dt);i++){Drive(f,Vector2.right,c.dt);NoPenetration(f,wall);}
            c.turnMetres=f.player.transform.position.x-corner.x;Need(c.turnMetres>.5f,"Clear sideways input remained trapped at a resting wall contact.");
            Vector3 reverse=f.player.transform.position;
            for(int i=0;i<Mathf.RoundToInt(1/c.dt);i++){Drive(f,Vector2.down,c.dt);NoPenetration(f,wall);}
            c.reverseMetres=reverse.z-f.player.transform.position.z;Need(c.reverseMetres>.5f,"Down input could not retreat from the wall.");
            c.detail="Real full-cart swept wall stop, sideways escape and retreat; no pose assignment after fixture creation.";
        }
        static void Drive(Fixture f,Vector2 axis,float dt)
        {
            Quaternion before=f.cart!=null?f.cart.Facing:Quaternion.identity;
            f.player.DriveInput(axis,false,dt);Physics.SyncTransforms();
            Need(Mathf.Approximately(f.player.Controller.radius,.25f)&&Mathf.Approximately(f.player.Controller.height,1.65f),"Player collision dimensions changed.");
            if(f.cart==null)return;
            Need(f.volume.size==f.originalCartSize,"Cart envelope changed during input.");
            Need(Quaternion.Angle(before,f.cart.Facing)<=f.cart.turnDegreesPerSecond*dt+.01f,"Cart yaw snapped past its turn rate.");
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
