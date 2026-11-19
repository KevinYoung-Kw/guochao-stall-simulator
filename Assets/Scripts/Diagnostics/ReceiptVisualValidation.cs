using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // Opt-in presentation fixture: real RunState transactions + live Market/ArtHUD.
    // It intentionally sets observation poses and steps model time. It is NOT
    // keyboard traversal, cashier proximity, carry animation or pointer validation.
    public sealed class ReceiptVisualValidation : MonoBehaviour
    {
        [Serializable] public sealed class Checkpoint
        {
            public string name,screenshot;
            public Snapshot snapshot;
            public CommerceReceiptAudit visible,basket,loaded;
            public int visibleProductIcons;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.receipt.visual-fixture.v12";
            public string inputSource="QA state fixture: actual RunState.Act/AddSupply/Tick transactions; explicit observation poses and frozen GameFlow.Update; no hardware input or traversal claim";
            public bool success;
            public string failure="";
            public int width,height;
            public string[] engineErrors;
            public List<Checkpoint> checkpoints=new List<Checkpoint>();
            public string[] excluded={"Hardware keyboard/mouse gameplay","Movement/carry/cashier proximity","Real-time checkout waiting","Aesthetic acceptance (screenshots require human/agent review)"};
        }
        private readonly Report report=new Report();
        private readonly List<string> errors=new List<string>();
        private GameFlow flow;private ArtHUD hud;private string directory,output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--receipt-visual-test")>=0)
                new GameObject("ReceiptVisualValidation").AddComponent<ReceiptVisualValidation>();
        }
        private void Awake(){Application.logMessageReceived+=Log;}
        private void Log(string condition,string trace,LogType kind)
        {if(kind==LogType.Error||kind==LogType.Exception)errors.Add(condition+"\n"+trace);}
        private IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--receipt-visual-output");
            output=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"receipt-visual-validation.json");
            if(!output.EndsWith(".json",StringComparison.OrdinalIgnoreCase))output=Path.Combine(output,"receipt-visual-validation.json");
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                object current=null;bool moved=false;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception error){report.failure=error.ToString();break;}
                if(!moved){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(report.failure!="")
            {
                yield return new WaitForEndOfFrame();string failure=Path.Combine(directory,"receipt-failure.png");ScreenCapture.CaptureScreenshot(failure);
                float deadline=Time.realtimeSinceStartup+3;while(!File.Exists(failure)&&Time.realtimeSinceStartup<deadline)yield return null;
            }
            report.width=Screen.width;report.height=Screen.height;report.engineErrors=errors.ToArray();report.success=report.failure==""&&errors.Count==0;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.success)Debug.Log("RECEIPT_VISUAL_FIXTURE_PASS "+output);else Debug.LogError("RECEIPT_VISUAL_FIXTURE_FAIL "+report.failure);
            yield return null;if(!Application.isEditor)Application.Quit(report.success?0:1);
        }
        private IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&!Application.isEditor,"Run this fixture in a graphical standalone Player.");
            foreach(string flag in new[]{"--business-test","--interaction-test","--smoke-test","--traversal-test","--capture-dir","--business-throughput-test"})
                Need(Array.IndexOf(Environment.GetCommandLineArgs(),flag)<0,"Do not combine receipt fixture with "+flag);
            float deadline=Time.realtimeSinceStartup+30;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            flow=GameFlow.Instance;Need(flow!=null&&flow.World!=null,"GameFlow failed to initialize.");
            yield return null;
            int width=ArgumentInt("--receipt-width",1600),height=ArgumentInt("--receipt-height",900);Screen.SetResolution(width,height,false);yield return null;yield return null;
            Need(!CharacterCreationUI.IsOpen,"Receipt validation must start without the creator overlay.");
            // Career 1 is a real low-cash opening; this fixture does not grant stock
            // or money. Its two light starter tools leave space for the four goods.
            var run=new RunState(4,new RunProfile());
            while(run.HasUnloadedEquipment){Need(run.Act("StovePickup",true),"Starter pickup");Need(run.Act("HomeCart",true),"Starter load");}
            Need(run.Act("HomeExit",true)&&run.Phase==GamePhase.Market,"Fixture could not enter Market.");
            flow.PresentReceiptValidationState(run);flow.enabled=false;
            Need(flow.Run==run&&flow.World!=null&&flow.Run.Phase==GamePhase.Market,"Fixture hook failed.");
            hud=flow.GetComponent<ArtHUD>();Need(hud!=null&&hud.Ready,"ArtHUD missing.");
            flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            BuildFixtureLabel();yield return null;
            foreach(string shelf in new[]{"RiceShelf","EggShelf","DoughShelf"})Need(run.Act(shelf,true,true),"Fixture pickup: "+shelf);
            Need(run.AddSupply("P04"),"Fixture package purchase");Need(run.Cart.Count==4,"Expected four actual purchasable goods.");
            PlaceObservation("Cashier");Publish(false);yield return Capture("01-unpaid-four-products",false);
            int cash=run.CashCents;Need(run.Act("Cashier",true),"Begin real payment service");run.Tick(1.25f,true);
            Need(!run.Paid&&run.CashCents==cash,"Cash changed before service completion.");Publish(false);yield return Capture("02-cashier-service-unpaid",false);
            run.Tick(run.CheckoutRemaining,true);Need(run.Paid,"Payment did not complete.");Publish(false);yield return Capture("03-paid-awaiting-load",false);
            PlaceObservation("MarketCart");Need(run.Act("MarketCart",true),"Real paid load rejected.");Need(run.Cart.Count==0&&run.Cargo.Count==4,"Load did not transfer the four goods.");
            Publish(true);yield return Capture("04-paid-and-loaded",true);
            PlaceObservation("Cashier");Need(run.Act("MeatShelf",true,true)&&run.Act("SeasoningShelf",true,true),"Second actual batch pickup.");
            Publish(false);yield return Capture("05-second-batch-unpaid",false);
            Publish(true);yield return Capture("06-first-batch-still-on-tricycle",true);
            Need(run.Cargo.Count==4&&run.Cart.Count==2,"Observation altered cargo/basket state.");
            Need(run.CashCents==cash-CommerceReceiptData.Shopping(run,true).totalCents,"Second unpaid batch was charged.");
        }
        private void Publish(bool loaded)
        {flow.SetPaused(false);hud.ShowShoppingReceipt(loaded);}
        private IEnumerator Capture(string name,bool loaded)
        {
            yield return null;yield return new WaitForSecondsRealtime(.35f);Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
            CommerceReceiptAudit basket=hud.CaptureShoppingReceipt(false),cargo=hud.CaptureShoppingReceipt(true),visible=hud.VisibleShoppingReceipt;
            Need(basket.totalsMatch&&cargo.totalsMatch,"Displayed prices disagree with actual transaction totals.");
            Need(visible!=null&&visible.kind==(loaded?"loaded-purchases":"current-basket"),"Wrong receipt page is visible.");
            int expectedIcons=Mathf.Min(4,visible.lines.Length);Need(hud.VisibleShoppingProductIconCount==expectedIcons,"A visible product row is missing its actual icon.");
            string file=Path.Combine(directory,name+".png");if(File.Exists(file))file=Path.Combine(directory,name+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".png");
            ScreenCapture.CaptureScreenshot(file);float deadline=Time.realtimeSinceStartup+5;while(!File.Exists(file)&&Time.realtimeSinceStartup<deadline)yield return null;
            Need(File.Exists(file),"Screenshot was not written: "+file);
            report.checkpoints.Add(new Checkpoint{name=name,screenshot=file,snapshot=flow.State,visible=visible,basket=basket,loaded=cargo,visibleProductIcons=hud.VisibleShoppingProductIconCount});
        }
        private void PlaceObservation(string anchor)
        {
            WorldRuntime world=flow.World;Need(world.HasAnchor(anchor),"Missing observation anchor: "+anchor);Vector3 at=world.Anchor(anchor);
            if(world.ShoppingMotor!=null)
            {
                for(int i=0;i<16;i++)
                {Quaternion rotation=Quaternion.Euler(0,i*22.5f,0);if(!world.ShoppingMotor.IsPoseClear(at,rotation))continue;world.ShoppingMotor.ResetPoseForValidation(at,rotation);flow.World.Player.inputEnabled=false;return;}
                throw new InvalidOperationException("No clear player/cart observation pose at "+anchor);
            }
            world.Player.Teleport(at);Physics.SyncTransforms();
        }
        private void BuildFixtureLabel()
        {
            var canvas=new GameObject("Explicit QA fixture label",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));canvas.transform.SetParent(transform,false);
            Canvas face=canvas.GetComponent<Canvas>();face.renderMode=RenderMode.ScreenSpaceOverlay;face.sortingOrder=950;
            CanvasScaler scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            RectTransform r=new GameObject("Fixture provenance",typeof(RectTransform),typeof(Text)).GetComponent<RectTransform>();r.SetParent(canvas.transform,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(1,0);r.anchoredPosition=new Vector2(-14,8);r.sizeDelta=new Vector2(390,30);
            Text text=r.GetComponent<Text>();text.font=Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");text.fontSize=16;text.alignment=TextAnchor.MiddleRight;text.color=new Color(1,.95f,.80f);text.text="QA 状态夹具 · 非键鼠流程";text.raycastTarget=false;
            var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(0,0,0,.8f);shadow.effectDistance=new Vector2(1,-1);
        }
        private static int ArgumentInt(string flag,int fallback)
        {string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,flag);return at>=0&&at+1<args.Length&&int.TryParse(args[at+1],out int value)?value:fallback;}
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private void OnDestroy(){Application.logMessageReceived-=Log;}
    }
}
