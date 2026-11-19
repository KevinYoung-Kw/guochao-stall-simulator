using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Guochao.Editor
{
    [InitializeOnLoad] public static class CinematicTravelAutoplayValidation
    {
        [Serializable] public sealed class Pass
        {public float elapsed,distance;public int frames,bumps;public bool complete,pauseHeld;public List<Vector3> vehiclePositions=new List<Vector3>();}
        [Serializable] public sealed class Report
        {
            public string scope="Production CinematicTravelStage.Initialize/Advance, 15s simulated in ordinary fixed dt increments. No keyboard-event injection or native visual claim. Source has no Unity Input reads; two identical timelines must produce identical vehicle positions, including an inserted pause.";
            public bool success,noDrivingInputReads,identicalRoute;public string failure="";public Pass first,second;
        }
        const string Pending="Guochao.TravelAutoplay.Pending",Output="Guochao.TravelAutoplay.Output";
        static CinematicTravelAutoplayValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--travel-autoplay-output");if(at<0)throw new ArgumentException("Specify travel autoplay output");
            SessionState.SetString(Output,args[at+1]);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);string path=SessionState.GetString(Output,"");var report=new Report();
            try
            {
                string source=File.ReadAllText(Path.Combine(Application.dataPath,"Scripts/Gameplay/CinematicTravelStage.cs"));
                report.noDrivingInputReads=!Regex.IsMatch(source,@"\bInput\s*\.");
                report.first=Execute(false);report.second=Execute(true);
                report.identicalRoute=report.first.vehiclePositions.Count==report.second.vehiclePositions.Count;
                for(int i=0;i<report.first.vehiclePositions.Count&&report.identicalRoute;i++)
                    if(Vector3.Distance(report.first.vehiclePositions[i],report.second.vehiclePositions[i])>1e-6f)report.identicalRoute=false;
                report.success=report.noDrivingInputReads&&report.identicalRoute&&report.first.complete&&report.second.complete&&report.second.pauseHeld&&
                    Mathf.Abs(report.first.elapsed-15)<1e-6f&&Mathf.Abs(report.second.elapsed-15)<1e-6f&&report.first.distance>50&&report.first.bumps==report.second.bumps;
            }catch(Exception e){report.failure=e.ToString();}
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("TRAVEL_AUTOPLAY_"+(report.success?"PASS":"FAIL")+" "+path+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
        static Pass Execute(bool pause)
        {
            var root=new GameObject("AutomaticTravelValidation");var travel=root.AddComponent<CinematicTravelStage>();var result=new Pass();
            try
            {
                travel.Initialize(true,null,new[]{"B03","E03"},15);
                Transform vehicle=(Transform)typeof(CinematicTravelStage).GetField("vehicle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(travel);
                while(!travel.Complete&&result.frames<1000)
                {
                    if(pause&&result.frames==300)
                    {float t=travel.Elapsed;Vector3 p=vehicle.position;travel.Advance(3,true);result.pauseHeld=travel.Elapsed==t&&Vector3.Distance(p,vehicle.position)<1e-6f;}
                    travel.Advance(1f/60f,false);result.frames++;
                    if(result.frames%30==0||travel.Complete)result.vehiclePositions.Add(vehicle.position);
                }
                result.elapsed=travel.Elapsed;result.complete=travel.Complete;result.bumps=travel.Bumps;
                result.distance=(float)typeof(CinematicTravelStage).GetField("distance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(travel);
            }
            finally{travel.Release();UnityEngine.Object.DestroyImmediate(root);}
            return result;
        }
    }
}
