using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Declared presentation fixture, isolated from the player's saves. It uses
    // actual UI callbacks and model transactions; positioning is a fixture,
    // not evidence of player navigation or earned campaign progress.
    public sealed class OnlineProcurementVisualValidation : MonoBehaviour
    {
        [Serializable] public sealed class Shot
        {public string name,path,phase;public int cash,day,pending,seasoning,boxes;public float remaining;}
        [Serializable] public sealed class Report
        {
            public string scope="Declared empty-vehicle career6 night fixture, isolated save directory, actual UI callbacks; body teleported near parcel solely for presentation. No earned-economy or navigation claim.";
            public bool success;public string failure="",saveDirectory;public List<Shot> screenshots=new List<Shot>();
            public bool cancellationPreservedCash,nightDebitOnce,nextDayUnclaimed,claimedExactlyOnePack;
            public float openingSeconds;public string[] errors;
        }
        private readonly Report report=new Report();
        private readonly List<string> errors=new List<string>();
        private string output,directory,previousSaveDirectory;
        private GameFlow flow;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--online-procurement-visual-test")>=0)new GameObject("OnlineProcurementVisualFixture").AddComponent<OnlineProcurementVisualValidation>();}
        void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--online-procurement-visual-output");
            output=Path.GetFullPath(i>=0&&i+1<args.Length?args[i+1]:Path.Combine(Application.persistentDataPath,"online-procurement-visual.json"));
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            previousSaveDirectory=CampaignSaveStore.TestDirectory;
            report.saveDirectory=Path.Combine(directory,"isolated-online-save-"+Guid.NewGuid().ToString("N"));CampaignSaveStore.TestDirectory=report.saveDirectory;
            Application.logMessageReceived+=Log;
        }
        void Log(string message,string trace,LogType type)
        {if(type==LogType.Error||type==LogType.Exception)errors.Add(message+"\n"+trace);}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception e){report.failure=e.ToString();break;}
                if(!moved){stack.Pop();continue;}
                if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(report.failure!="")
            {yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(directory,"online-failure.png"));yield return null;}
            report.errors=errors.ToArray();report.success=report.failure==""&&errors.Count==0;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            Debug.Log((report.success?"ONLINE_VISUAL_FIXTURE_PASS ":"ONLINE_VISUAL_FIXTURE_FAIL ")+output+" "+report.failure);
            yield return null;if(!Application.isEditor)Application.Quit(report.success?0:1);
        }
        IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&!Application.isEditor,"Use a graphical standalone player for screenshots.");
            float limit=Time.realtimeSinceStartup+30;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<limit)yield return null;
            flow=GameFlow.Instance;Need(flow?.World!=null,"GameFlow initialization failed.");
            Need(CampaignSaveStore.Save(Fixture(),out string saved),saved);Need(flow.ContinueCampaign(),flow.SaveStatus);
            Screen.SetResolution(1920,1080,false);for(int i=0;i<5;i++)yield return null;
            var labelRoot=CampaignUI.Canvas(transform,"Fixture label",2000,Color.clear);
            // Canvas helper creates an input blocker; this diagnostic ribbon must not block real controls.
            labelRoot.parent.GetComponent<Image>().raycastTarget=false;
            CampaignUI.Text(labelRoot,"隔离验收夹具 · 未使用玩家存档",new Vector2(0,510),new Vector2(900,42),20,CampaignUI.PaperColor,TextAnchor.MiddleCenter);
            yield return Capture("01-night-online-entry");
            int cash=flow.Run.CashCents;
            Click("夜间采购");yield return Capture("02-online-shop");
            ProductButton("P02").onClick.Invoke();yield return Capture("03-online-payment-confirmation");
            Click("再想想");report.cancellationPreservedCash=flow.Run.CashCents==cash&&flow.Campaign.onlineOrders.Count==0;Need(report.cancellationPreservedCash,"Cancelled order mutated cash.");
            ProductButton("P02").onClick.Invoke();Click("确认付款");yield return null;
            report.nightDebitOnce=flow.Run.CashCents==cash-960&&flow.Campaign.lastDay.onlinePurchasesCents==960&&flow.Campaign.onlineOrders.Count==1;
            Need(report.nightDebitOnce,"Night purchase debit mismatch.");
            Need(!flow.Run.Supplies.ContainsKey("P02")&&flow.Run.Cargo.Count==0,"Night granted usable goods.");
            Click("在途 (1)");yield return Capture("04-paid-order-in-transit");Click("返回账本");yield return Capture("05-night-ledger-after-order");
            Click("睡一觉 · 准备第 2 天");yield return null;
            Need(flow.Run.Phase==GamePhase.Home&&flow.World.HasAnchor("ParcelBox"),"Next morning parcel missing.");
            report.nextDayUnclaimed=!flow.Campaign.onlineOrders[0].received&&!flow.Run.Supplies.ContainsKey("P02");Need(report.nextDayUnclaimed,"Arrival auto-claimed the goods.");
            Vector3 anchor=flow.World.Anchor("ParcelBox");bool placed=false;
            for(int i=0;i<16;i++)
            {
                float a=i*Mathf.PI/8;Vector3 at=anchor+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.75f;
                if(!flow.World.IsNearAt("ParcelBox",at)||flow.World.NearestAt(new[]{"ParcelBox","HomeExit"},at)!="ParcelBox")continue;
                flow.World.Player.Teleport(at);flow.World.Player.visual.rotation=Quaternion.LookRotation(anchor-at,Vector3.up);Physics.SyncTransforms();placed=true;break;
            }
            Need(placed,"No clear parcel presentation stand point.");yield return Capture("06-next-morning-parcel");
            flow.PrimaryAction();Need(OnlineProcurementUI.IsOpen,"PrimaryAction did not open parcel page.");yield return Capture("07-parcel-page");
            float before=flow.Run.Remaining;ProductButton("P02").onClick.Invoke();yield return null;yield return Capture("08-opening-progress");
            limit=Time.realtimeSinceStartup+8;
            while(!flow.Campaign.onlineOrders[0].received&&flow.Run.Phase==GamePhase.Home&&Time.realtimeSinceStartup<limit)yield return null;
            report.openingSeconds=before-flow.Run.Remaining;
            report.claimedExactlyOnePack=flow.Campaign.onlineOrders[0].received&&flow.Run.Supplies.TryGetValue("P02",out int amount)&&amount==50&&flow.Run.SpentCents==0&&flow.Run.CashCents==cash-960;
            Need(report.claimedExactlyOnePack,"Parcel did not produce exactly one paid pack.");Need(report.openingSeconds>=2.8f,"Opening skipped its three-second clock cost.");
            yield return Capture("09-parcel-loaded");Click("放下包裹");Need(flow.SaveCampaign(),flow.SaveStatus);
        }
        static CampaignSaveData Fixture()
        {
            var profile=new RunProfile{careerId="career6",selectedTraits=Array.Empty<string>()};Need(profile.Validate(out string reason),reason);
            var seed=new RunState(.25f,profile).CaptureSave();seed.homeEquipment=seed.loadedEquipment=Array.Empty<string>();seed.supplies=Array.Empty<RunStockSave>();seed.gas=seed.cartridge=seed.coal=seed.electricity=0;
            var run=RunState.Restore(seed);run.Paused=false;var campaign=CampaignState.New(profile,run);
            var service=new BusinessSession(ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run));service.Close();campaign.CommitDay(run,service,service.SessionId);
            run=RunState.FromCampaign(campaign,.25f);run.SetBusinessPhase(GamePhase.Night);run.Paused=true;
            return new CampaignSaveData{campaign=campaign,run=run.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
        }
        static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static void Click(string name)
        {
            foreach(var button in FindObjectsOfType<Button>())if(button.gameObject.activeInHierarchy&&button.interactable&&
                (button.gameObject.name==name||button.GetComponentInChildren<Text>()?.text==name)){button.onClick.Invoke();return;}
            throw new InvalidOperationException("Active button missing: "+name);
        }
        static Button ProductButton(string id)
        {
            foreach(var ui in FindObjectsOfType<OnlineProcurementUI>())
                foreach(var node in ui.GetComponentsInChildren<RectTransform>())if(node.gameObject.activeInHierarchy&&node.name=="Product "+id)
                {var button=node.GetComponentInChildren<Button>();if(button!=null&&button.interactable)return button;}
            throw new InvalidOperationException("Active product action missing: "+id);
        }
        IEnumerator Capture(string name)
        {
            Canvas.ForceUpdateCanvases();yield return null;yield return new WaitForEndOfFrame();
            string path=Path.Combine(directory,name+".png");ScreenCapture.CaptureScreenshot(path);
            float until=Time.realtimeSinceStartup+5;while(!File.Exists(path)&&Time.realtimeSinceStartup<until)yield return null;
            Need(File.Exists(path),"Screenshot missing: "+path);
            flow.Run.Supplies.TryGetValue("P02",out int boxes);flow.Run.Supplies.TryGetValue("seasoning",out int seasoning);
            report.screenshots.Add(new Shot{name=name,path=path,phase=flow.Run.Phase.ToString(),cash=flow.Run.CashCents,day=flow.Campaign.dayIndex,pending=flow.Campaign.onlineOrders.FindAll(x=>!x.received).Count,boxes=boxes,seasoning=seasoning,remaining=flow.Run.Remaining});
        }
        void OnDestroy(){Application.logMessageReceived-=Log;CampaignSaveStore.TestDirectory=previousSaveDirectory;}
    }
}
