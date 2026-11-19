using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Guochao.Editor
{
    // Actual production home geometry photographs. Camera-only images are not HUD
    // screenshots or evidence of human gameplay; the close-ups use inspection cameras.
    [InitializeOnLoad] public static class Household20Visual
    {
        const string Pending="Guochao.H20Visual.Pending",Output="Guochao.H20Visual.Output";
        [Serializable] class Report { public bool success;public string scope="Production home camera render and inspection close-ups; no HUD or gameplay claim.";public List<string> images=new List<string>();public List<string> errors=new List<string>(); }
        static Report report;static string directory;static Feedback3CoroutineHost host;static GameObject root;
        static Household20Visual(){EditorApplication.update+=Watch;}
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--household-visual-output");if(at<0)throw new ArgumentException("--household-visual-output required");
            string dir=Path.GetFullPath(args[at+1]);if(Directory.Exists(dir))throw new IOException("Preserve previous images");
            SessionState.SetString(Output,dir);SessionState.SetBool(Pending,true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Watch()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);directory=SessionState.GetString(Output,"");Directory.CreateDirectory(directory);report=new Report();
            Application.logMessageReceived+=Log;host=new GameObject("H20 visual frame host").AddComponent<Feedback3CoroutineHost>();host.Launch(Capture(),()=>Finish(null),Finish);
        }
        static IEnumerator Capture()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)throw new InvalidOperationException("Graphical Editor required");
            root=new GameObject("H20 actual home");var world=root.AddComponent<WorldRuntime>();world.Build(GamePhase.Home);world.Player.inputEnabled=false;
            for(int i=0;i<12;i++)yield return new WaitForEndOfFrame();
            Render(world.View,"home-overview",1920,1080);
            Quaternion authoredView=world.View.transform.rotation;
            foreach(string name in new[]{"HH02_ElmStool_Installed","HH01_EnamelBasin_Installed",HouseholdAssetPresentation.KettleRootName})
            {
                Transform model=null;foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name==name){model=t;break;}
                if(model==null)throw new InvalidOperationException("Installed model missing: "+name);
                Bounds bounds=WorldRuntime.RenderBounds(model.gameObject);Camera camera=world.View;
                yield return new WaitForEndOfFrame();
                // WorldRuntime updates its production camera in LateUpdate; set
                // the inspection pose only after that, immediately before Render.
                camera.transform.rotation=authoredView;camera.transform.position=bounds.center-camera.transform.forward*3;camera.orthographic=true;camera.orthographicSize=.64f;
                Vector3 projected=camera.WorldToViewportPoint(bounds.center);
                if(Vector2.Distance(new Vector2(projected.x,projected.y),new Vector2(.5f,.5f))>.01f)throw new InvalidOperationException("Inspection target not centered");
                Render(camera,name,1000,800);
            }
        }
        static void Render(Camera camera,string name,int width,int height)
        {
            RenderTexture previous=camera.targetTexture,active=RenderTexture.active;var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();string path=Path.Combine(directory,name+".png");File.WriteAllBytes(path,image.EncodeToPNG());report.images.Add(path);}
            finally{camera.targetTexture=previous;RenderTexture.active=active;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(target);}
        }
        static void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.errors.Add(message+"\n"+trace);}
        static void Finish(Exception ex)
        {
            if(ex!=null)report.errors.Add(ex.ToString());Application.logMessageReceived-=Log;
            report.success=report.errors.Count==0&&report.images.Count==4;File.WriteAllText(Path.Combine(directory,"report.json"),JsonUtility.ToJson(report,true));
            EditorApplication.delayCall+=()=>EditorApplication.Exit(report.success?0:1);
        }
    }
}
