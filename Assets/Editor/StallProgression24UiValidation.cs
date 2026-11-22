using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao.Editor
{
    [InitializeOnLoad] public static class StallProgression24UiValidation
    {
        [Serializable] public sealed class Frame
        {
            public int width,height;public bool entryClickable,upgradeClickable,closeRestoresFocus,savePersisted;
            public string rayDetail="",nightScreenshot="",growthScreenshot="",vehicleUpgradedScreenshot="";public List<string> clipped=new List<string>();
        }
        [Serializable] public sealed class Report
        {
            public bool success;public string failure="",scope="Real graphical Unity Play Mode, actual UI callbacks and graphic raycasts; staged campaign and no physical mouse input.";
            public List<Frame> frames=new List<Frame>();public List<string> engineErrors=new List<string>();
        }
        const string Pending="Guochao.Progression24.UI.Pending",Output="Guochao.Progression24.UI.Output";
        static Report report;static string output,directory;static Feedback3CoroutineHost host;
        static StallProgression24UiValidation(){EditorApplication.update+=Advance;}
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--progression24-ui-output");
            if(i<0||i+1>=args.Length)throw new ArgumentException("--progression24-ui-output required");
            string path=Path.GetFullPath(args[i+1]);if(File.Exists(path))throw new IOException("Preserve previous report");
            SessionState.SetString(Output,path);SessionState.SetBool(Pending,true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.EnterPlaymode();
        }
        static void Advance()
        {
            if(EditorApplication.isCompiling||!EditorApplication.isPlaying||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);output=SessionState.GetString(Output,"");directory=Path.GetDirectoryName(output);
            Directory.CreateDirectory(directory);report=new Report();Application.logMessageReceived+=Log;
            host=new GameObject("Progression UI Test").AddComponent<Feedback3CoroutineHost>();
            host.Launch(Flatten(Execute()),()=>Finish(null),Finish);
        }
        static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)report.engineErrors.Add(text+"\n"+trace);}
        static void Finish(Exception e)
        {
            if(e!=null)report.failure=e.ToString();Application.logMessageReceived-=Log;
            report.success=report.failure==""&&report.engineErrors.Count==0&&report.frames.Count==3&&report.frames.All(f=>f.entryClickable&&f.upgradeClickable&&f.closeRestoresFocus&&f.savePersisted&&f.clipped.Count==0);
            File.WriteAllText(output,JsonUtility.ToJson(report,true));EditorApplication.Exit(report.success?0:1);
        }
        static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static IEnumerator Flatten(IEnumerator root)
        {
            var stack=new Stack<IEnumerator>();stack.Push(root);
            try{while(stack.Count>0){var top=stack.Peek();if(!top.MoveNext()){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator nested){stack.Push(nested);continue;}yield return top.Current;}}
            finally{while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();}
        }
        static IEnumerator Resize(int w,int h)
        {
            var method=typeof(BusinessLayout22Preview).GetMethod("Resize",BindingFlags.Static|BindingFlags.NonPublic);
            Need(method!=null,"GameView resize helper missing");yield return (IEnumerator)method.Invoke(null,new object[]{w,h});
        }
        static bool RayHits(Button b,Frame f)
        {
            Canvas.ForceUpdateCanvases();var at=RectTransformUtility.WorldToScreenPoint(null,b.transform.position);
            var eventData=new PointerEventData(EventSystem.current){position=at};var results=new List<RaycastResult>();EventSystem.current.RaycastAll(eventData,results);
            f.rayDetail="screen="+Screen.width+"x"+Screen.height+" position="+at+" results="+string.Join(",",results.Select(r=>r.gameObject.name).ToArray());
            return results.Count>0&&results[0].gameObject.GetComponentInParent<Button>()==b;
        }
        static void CheckTravelAccessoryClearance(CinematicTravelStage stage)
        {
            var loadout=stage.GetComponentInChildren<VehicleLoadout>(true);
            Need(loadout!=null && loadout.Plan!=null && loadout.Plan.FullyValidated,"Accessory travel layout missing");
            var tray=stage.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Lv2 车斗扩展托板");
            Need(tray!=null && tray.gameObject.activeInHierarchy,"Accessory travel tray missing");
            var trayCollider=tray.GetComponent<BoxCollider>(); Need(trayCollider!=null && trayCollider.enabled,"Accessory travel tray collider missing");
            var mounted=(Dictionary<string,GameObject>)typeof(VehicleLoadout).GetField("mounted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(loadout);
            int parts=0;
            foreach(string id in new[]{"E10","E15"})
            {
                Need(mounted.TryGetValue("gear_"+id,out var model) && model.activeInHierarchy,"Missing actual accessory model "+id);
                foreach(var mesh in model.GetComponentsInChildren<MeshFilter>())
                {
                    if(mesh.sharedMesh==null || mesh.GetComponent<Renderer>()==null)continue;
                    var box=mesh.gameObject.AddComponent<BoxCollider>(); box.center=mesh.sharedMesh.bounds.center; box.size=mesh.sharedMesh.bounds.size;
                    Physics.SyncTransforms();
                    bool overlap=Physics.ComputePenetration(trayCollider,trayCollider.transform.position,trayCollider.transform.rotation,
                        box,box.transform.position,box.transform.rotation,out Vector3 direction,out float depth);
                    parts++; UnityEngine.Object.Destroy(box);
                    Need(!overlap,"Accessory intersects tray: "+id+"/"+mesh.name+" depth="+depth);
                }
            }
            Need(parts>0,"No accessory mesh parts checked");
            Debug.Log("LV2_TRAVEL_ACCESSORY_CLEAR parts="+parts);
        }
        static IEnumerator Capture(string path)
        {
            yield return new WaitForSecondsRealtime(.4f);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
            Need(!File.Exists(path),"Screenshot exists");ScreenCapture.CaptureScreenshot(path);
            double until=Time.realtimeSinceStartupAsDouble+10;while(!File.Exists(path)&&Time.realtimeSinceStartupAsDouble<until)yield return null;
            Need(File.Exists(path),"Screenshot not created");
        }
        static IEnumerator Execute()
        {
            Need(!Application.isBatchMode,"Graphical Editor required");
            var camera=new GameObject("Fixture background camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.16f,.14f);camera.cullingMask=0;
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768)})
            {
                yield return Resize(size.x,size.y);var frame=new Frame{width=Screen.width,height=Screen.height};report.frames.Add(frame);
                var career=CharacterCatalog.Data.careers.First(c=>c.id=="career2");
                var profile=(RunProfile)typeof(V10PackingValidation).GetMethod("Profile",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{career});
                var run=new RunState(4,profile);run.Supplies["P04"]=100;run.Supplies["rice_small"]=1;var campaign=CampaignState.New(profile,run);run.SetBusinessPhase(GamePhase.Night);
                run.SetBusinessCash(150000);campaign.cashCents=150000;
                var ledger=new CampaignDayLedger{dayIndex=1,sessionId="ui-fixture",committed=true,closingCashCents=150000};
                campaign.lastDay=ledger;campaign.history.Add(ledger);
                campaign.progression=StallProgressionState.CreateDefault();campaign.progression.level=2;campaign.progression.availablePoints=1;
                CampaignSaveStore.TestDirectory=Path.Combine(directory,"saves-"+size.x);Need(!Directory.Exists(CampaignSaveStore.TestDirectory),"Test saves exist");
                var obj=new GameObject("UI flow");obj.SetActive(false);var flow=obj.AddComponent<GameFlow>();flow.enabled=false;obj.SetActive(true);
                typeof(GameFlow).GetField("run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flow,run);
                typeof(GameFlow).GetProperty("Campaign").GetSetMethod(true).Invoke(flow,new object[]{campaign});
                NightHubUI.Open(flow);yield return null;Canvas.ForceUpdateCanvases();
                var growth=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x=>x.name.StartsWith("摊主成长"));
                Need(growth!=null&&growth.interactable,"Growth entry missing");frame.entryClickable=RayHits(growth,frame);
                frame.nightScreenshot=Path.Combine(directory,"night-"+size.x+"x"+size.y+".png");yield return Capture(frame.nightScreenshot);
                growth.onClick.Invoke();yield return null;Need(StallProgressionUI.IsOpen,"Growth page did not open");Canvas.ForceUpdateCanvases();
                var page=GameObject.Find("Night owner progression");Need(page!=null,"Growth page missing");
                foreach(var label in page.GetComponentsInChildren<Text>())
                    if(!string.IsNullOrWhiteSpace(label.text)&&label.preferredHeight>label.rectTransform.rect.height+2.5f)frame.clipped.Add(label.text.Substring(0,Math.Min(24,label.text.Length)));
                var upgrade=page.GetComponentsInChildren<Button>().FirstOrDefault(x=>x.name=="UpgradeAmiable");Need(upgrade!=null&&upgrade.interactable,"Unlocked talent missing");
                frame.upgradeClickable=RayHits(upgrade,frame);frame.growthScreenshot=Path.Combine(directory,"growth-"+size.x+"x"+size.y+".png");yield return Capture(frame.growthScreenshot);
                upgrade.onClick.Invoke();Need(StallProgressionRules.AmiableRank(campaign)==1&&campaign.progression.availablePoints==0,"Talent click failed");
                frame.savePersisted=CampaignSaveStore.Load(out var saved,out _)&&StallProgressionRules.AmiableRank(saved.campaign)==1;
                var close=page.GetComponentsInChildren<Button>().First(x=>x.name=="CloseProgression");close.onClick.Invoke();
                frame.closeRestoresFocus=!StallProgressionUI.IsOpen&&NightHubUI.IsOpen&&growth.interactable;
                var night=UnityEngine.Object.FindObjectsByType<NightHubUI>(FindObjectsSortMode.None).FirstOrDefault();
                Need(night!=null,"Night hub missing");
                var vehicleButton=night.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.GetComponentsInChildren<Text>().Any(t=>!string.IsNullOrEmpty(t.text)&&t.text.Contains("改电瓶三轮")));
                Need(vehicleButton!=null&&vehicleButton.interactable,"Vehicle upgrade button missing or disabled");
                Need(RayHits(vehicleButton,frame),"Vehicle upgrade button raycast blocked: "+frame.rayDetail);
                vehicleButton.onClick.Invoke();yield return null;
                Need(campaign.vehicleLevel==2,"Vehicle level did not upgrade to 2");
                Need(campaign.cashCents==50000,"Campaign cash after vehicle upgrade mismatch");
                Need(run.CashCents==50000,"Run cash after vehicle upgrade mismatch");
                Need(campaign.lastDay!=null&&campaign.lastDay.vehicleUpgradeCents==100000&&campaign.history!=null&&campaign.history.Count>0&&campaign.history[0].vehicleUpgradeCents==100000,"Day ledger vehicleUpgradeCents mismatch");
                Need(CampaignSaveStore.Load(out var vehicleSaved,out string vehicleLoadStatus),"Vehicle upgrade save load failed: "+vehicleLoadStatus);
                Need(vehicleSaved.campaign.vehicleLevel==2,"Saved vehicle level mismatch");
                Need(vehicleSaved.campaign.cashCents==50000,"Saved campaign cash mismatch");
                Need(vehicleSaved.run.cashCents==50000,"Saved run cash mismatch");
                Need(vehicleSaved.campaign.lastDay!=null&&vehicleSaved.campaign.lastDay.vehicleUpgradeCents==100000&&vehicleSaved.campaign.history!=null&&vehicleSaved.campaign.history.Count>0&&vehicleSaved.campaign.history[0].vehicleUpgradeCents==100000,"Saved day ledger vehicleUpgradeCents mismatch");
                frame.vehicleUpgradedScreenshot=Path.Combine(directory,"vehicle-upgraded-"+size.x+"x"+size.y+".png");yield return Capture(frame.vehicleUpgradedScreenshot);
                flow.StartNextDay();yield return null;
                Need(flow.Run!=null&&flow.Run.Phase==GamePhase.Home,"Next day run phase mismatch");
                Need(flow.Campaign!=null&&flow.Campaign.vehicleLevel==2,"Next day campaign vehicle level mismatch");
                Need(flow.World!=null,"Next day world missing");
                var tray=flow.World.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Lv2 车斗扩展托板");
                Need(tray!=null,"Lv2 车斗扩展托板 missing in next-day world");
                Need(tray.gameObject.activeInHierarchy,"Lv2 车斗扩展托板 not activeInHierarchy");
                var trayRenderer=tray.GetComponent<Renderer>();
                Need(trayRenderer!=null&&trayRenderer.enabled,"Lv2 车斗扩展托板 Renderer missing or disabled");
                var trayCollider=tray.GetComponent<BoxCollider>();
                Need(trayCollider!=null&&trayCollider.enabled,"Lv2 车斗扩展托板 BoxCollider missing or disabled");
                Need(CampaignSaveStore.Load(out var nextDaySaved,out string nextDayLoadStatus),"Next day save load failed: "+nextDayLoadStatus);
                Need(nextDaySaved.campaign.vehicleLevel==2,"Saved next day vehicle level mismatch");
                Need(nextDaySaved.run.phase==GamePhase.Home,"Saved next day run phase mismatch");
                if(size.x==1280)
                {
                    var accessoryObject=new GameObject("Lv2 accessory travel check");
                    var accessoryStage=accessoryObject.AddComponent<CinematicTravelStage>();
                    accessoryStage.Initialize(true,null,new[]{"E10","E15"},1f);
                    CheckTravelAccessoryClearance(accessoryStage);
                    UnityEngine.Object.Destroy(accessoryObject);yield return null;

                    while(flow.Run.HasUnloadedEquipment)
                    {
                        Need(flow.Run.Act("StovePickup",true),"Cannot pick up starter: "+flow.Run.Message);
                        Need(flow.Run.Act("HomeCart",true),"Cannot load starter on Lv2: "+flow.Run.Message);
                    }
                    Need(flow.SaveCampaign(),"Cannot save loaded next day: "+flow.SaveStatus);
                    Need(CampaignSaveStore.Load(out var loadedTravelSave,out string travelSaveStatus),"Cannot reload travel save: "+travelSaveStatus);
                    var restored=RunState.Restore(loadedTravelSave.run);
                    var carried=new List<string>(restored.LoadedEquipment);carried.AddRange(restored.Cargo);carried.AddRange(restored.PackingSupplies);
                    Need(carried.FindAll(id=>id=="P04").Count==2,"Duplicate packaging did not survive the actual save");
                    int arrivals=0;
                    Action atArrival=()=>{
                        Need(flow.Run.Act("HomeExit",true),"Home departure failed: "+flow.Run.Message);
                        typeof(GameFlow).GetMethod("CreateWorld",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(flow,new object[]{null});
                        arrivals++;
                    };
                    typeof(GameFlow).GetMethod("BeginTransition",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(flow,new object[]{"超市","出发采购",atArrival,true});
                    var transition=flow.GetComponent<SceneTransition>(); Need(transition!=null,"Real transition was not created");
                    var travelField=typeof(SceneTransition).GetField("travel",BindingFlags.Instance|BindingFlags.NonPublic);
                    double travelDeadline=Time.realtimeSinceStartupAsDouble+10;
                    CinematicTravelStage travelStage=null;
                    while(travelStage==null && Time.realtimeSinceStartupAsDouble<travelDeadline)
                    { travelStage=(CinematicTravelStage)travelField.GetValue(transition); if(travelStage==null)yield return null; }
                    Need(travelStage!=null,"Actual GameFlow/SceneTransition did not create the travel stage");
                    var travelLoadout=travelStage.GetComponentInChildren<VehicleLoadout>(true);
                    Need(travelLoadout!=null && travelLoadout.Plan!=null && travelLoadout.Plan.FullyValidated,"Saved cargo travel layout not validated");
                    Need(travelLoadout.Plan.placements.Count==carried.Count,"Travel did not display every saved cargo item");
                    foreach(var group in carried.GroupBy(id=>id))
                        Need(travelLoadout.Plan.placements.Count(p=>p.itemId==group.Key)==group.Count(),"Travel item count differs from save: "+group.Key);
                    var travelModels=(Dictionary<string,GameObject>)typeof(VehicleLoadout).GetField("mounted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(travelLoadout);
                    foreach(var placement in travelLoadout.Plan.placements)
                        Need(travelModels.TryGetValue(placement.instanceId,out var model) && model.activeInHierarchy && model.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"Travel item is missing or invisible: "+placement.instanceId);
                    Need(travelModels.ContainsKey("gear_P04") && travelModels.ContainsKey("gear_P04_1"),"Duplicate packages did not get distinct visible instances");
                    Need(travelModels.TryGetValue("gear_rice_small",out var actualRice),"Saved small rice model is missing from travel");
                    var expectedParent=new GameObject("Expected loadout visual");
                    var factory=typeof(WorldRuntime).GetMethod("CreateLoadoutVisual",BindingFlags.Static|BindingFlags.NonPublic);
                    Need(factory!=null,"Shared loadout visual factory missing");
                    var expectedRice=(GameObject)factory.Invoke(null,new object[]{"rice_small",expectedParent.transform});
                    var actualMeshes=actualRice.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh!=null?m.sharedMesh.name:"").OrderBy(n=>n).ToArray();
                    var expectedMeshes=expectedRice.GetComponentsInChildren<MeshFilter>(true).Select(m=>m.sharedMesh!=null?m.sharedMesh.name:"").OrderBy(n=>n).ToArray();
                    Need(actualMeshes.Length>0 && actualMeshes.SequenceEqual(expectedMeshes) && Vector3.Distance(actualRice.transform.localScale,expectedRice.transform.localScale)<.0001f,"Travel small rice model differs from the real loadout factory");
                    UnityEngine.Object.Destroy(expectedParent);
                    Debug.Log("LV2_SAVED_TRAVEL_ITEMS "+string.Join(",",carried));
                    travelStage.SkipToEnd();
                    double arrivalDeadline=Time.realtimeSinceStartupAsDouble+10;
                    while(flow.IsTransitioning && Time.realtimeSinceStartupAsDouble<arrivalDeadline)yield return null;
                    Need(!flow.IsTransitioning && arrivals==1 && flow.Run.Phase==GamePhase.Market,"Travel did not arrive exactly once at the market");
                    var marketTray=flow.World.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Lv2 车斗扩展托板");
                    Need(marketTray!=null && marketTray.gameObject.activeInHierarchy,"Lv2 tray missing after arriving at market");
                    Need(flow.SaveCampaign(),"Cannot save market arrival: "+flow.SaveStatus);
                    Need(CampaignSaveStore.Load(out var marketSave,out string marketStatus),"Cannot reload market arrival: "+marketStatus);
                    var marketRun=RunState.Restore(marketSave.run);
                    Need(marketSave.campaign.vehicleLevel==2 && marketRun.Phase==GamePhase.Market && marketRun.PackingSupplies.Count(id=>id=="P04")==2,"Market save lost the upgraded vehicle or packaging");
                }
                NightHubUI.Close();UnityEngine.Object.Destroy(obj);yield return null;
            }
        }
    }
}
