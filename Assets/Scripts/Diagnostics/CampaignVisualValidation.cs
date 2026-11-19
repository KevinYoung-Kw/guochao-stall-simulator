using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // Explicit presentation fixture. It loads a real isolated campaign through
    // ContinueCampaign, then invokes the actual UI callbacks. It does not claim
    // that the fixture's money/stock were earned through keyboard gameplay.
    public sealed class CampaignVisualValidation : MonoBehaviour
    {
        [Serializable] public sealed class Checkpoint
        {
            public string name,screenshot,phase,focus,saveStatus;
            public int width,height,day,cash,actualInventoryIcons,visibleTexts;
            public int supplyProductIcons,visibleSupplyProductIcons,satchelRenderedCharacters;
            public int recoveryCards,recoveryIcons;
            public string supplySurface,tradeStatus;
            public bool tradeConfirmation,tradePrimaryEnabled;
            public string heldItem,heldTitle,satchelCount;
            public float inventoryScroll,supplyScroll=-1;
            public bool night,paused;
            public string[] layoutIssues;
        }
        [Serializable] public sealed class PreparedAssetCheck
        {
            public string checkpoint,root;
            public int parts,prefabMeshFilters,vertices;
            public bool exactSixChildren,noSealedMealFallback,allMeshesMatchImportedPrefabs;
        }
        [Serializable] public sealed class RecoveryUICheck
        { public string name,detail;public bool passed; }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.campaign.visual-fixture.p1";
            public string inputSource="Isolated declared campaign fixture loaded by actual GameFlow.ContinueCampaign; actual UI onClick callbacks and native screenshots; not pointer/keyboard gameplay or earned economy evidence";
            public bool success;public string failure="",isolatedSaveDirectory;
            public List<Checkpoint> checkpoints=new List<Checkpoint>();
            public List<Checkpoint> supplementalCheckpoints=new List<Checkpoint>();
            public List<PreparedAssetCheck> preparedAssetChecks=new List<PreparedAssetCheck>();
            public List<RecoveryUICheck> recoveryUIChecks=new List<RecoveryUICheck>();
            public string recoveryFixture="";
            public string[] engineErrors;
            public string[] excluded={"Two-day economy/gameplay reachability","Hardware pointer travel and input timing","Collision/animation quality","Aesthetic approval without inspecting screenshots"};
        }
        private readonly Report report=new Report();
        private readonly List<string> errors=new List<string>();
        private GameFlow flow;private string output,directory,saveDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"--campaign-visual-test")>=0)new GameObject("CampaignVisualValidation").AddComponent<CampaignVisualValidation>();}
        private void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--campaign-visual-output");
            output=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"campaign-visual-validation.json");
            if(!output.EndsWith(".json",StringComparison.OrdinalIgnoreCase))output=Path.Combine(output,"campaign-visual-validation.json");
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            saveDirectory=Path.Combine(directory,"isolated-visual-save-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            CampaignSaveStore.TestDirectory=saveDirectory;report.isolatedSaveDirectory=saveDirectory;
            Application.logMessageReceived+=Log;
        }
        private void Log(string message,string trace,LogType kind)
        {if(kind==LogType.Error||kind==LogType.Exception)errors.Add(message+"\n"+trace);}
        private IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception error){report.failure=error.ToString();break;}
                if(!moved){stack.Pop();continue;}if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(report.failure!="")
            {
                yield return new WaitForEndOfFrame();string failure=Path.Combine(directory,"campaign-failure.png");ScreenCapture.CaptureScreenshot(failure);
                float until=Time.realtimeSinceStartup+3;while(!File.Exists(failure)&&Time.realtimeSinceStartup<until)yield return null;
            }
            report.engineErrors=errors.ToArray();report.success=report.failure==""&&errors.Count==0;
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            if(report.success)Debug.Log("CAMPAIGN_VISUAL_FIXTURE_PASS "+output);else Debug.LogError("CAMPAIGN_VISUAL_FIXTURE_FAIL "+report.failure);
            yield return null;if(!Application.isEditor)Application.Quit(report.success?0:1);
        }
        private IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&!Application.isEditor,"Run in a graphical standalone Player.");
            foreach(string other in new[]{"--business-test","--receipt-visual-test","--interaction-test","--smoke-test","--capture-dir","--business-throughput-test"})
                Need(Array.IndexOf(Environment.GetCommandLineArgs(),other)<0,"Do not combine campaign visual fixture with "+other);
            float until=Time.realtimeSinceStartup+30;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<until)yield return null;
            flow=GameFlow.Instance;Need(flow!=null&&flow.World!=null,"GameFlow failed to initialize.");yield return null;
            CampaignSaveData data=BuildDeclaredFixture();Need(CampaignSaveStore.Save(data,out string saved),saved);
            Need(flow.ContinueCampaign(),flow.SaveStatus);yield return null;
            Need(flow.Run.Phase==GamePhase.Night&&NightHubUI.IsOpen&&flow.Run.Paused,"Fixture did not restore the real night phase.");
            Need(flow.World.Player!=null&&flow.World.Player.presentationLocked,"Night room did not lock exploration input.");
            BuildFixtureLabel();
            foreach(Vector2Int size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(2560,1080)})
            {yield return Resolution(size.x,size.y);yield return Capture("night-"+size.x+"x"+size.y,true);}
            yield return Resolution(1920,1080);
            ScrollRect inventory=FindScroll("Owned stock and equipment");Need(inventory!=null,"Inventory scroll is missing.");
            inventory.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();yield return null;
            yield return Capture("night-inventory-last-page",true);
            Need(inventory.content.rect.height>inventory.viewport.rect.height&&inventory.verticalNormalizedPosition<.001f,"The long inventory did not reach its final page.");

            int cash=flow.Campaign.cashCents,day=flow.Campaign.dayIndex;
            Click("保存进度");Need(flow.SaveStatus.Contains("已保存"),"Night save did not report actual success.");
            Need(flow.Campaign.cashCents==cash&&flow.Campaign.dayIndex==day,"Saving changed settled money/date.");
            // Deliberately unwritable path: a normal file cannot contain a save
            // directory. Only this fixture's isolated path is affected.
            string blocked=Path.Combine(directory,"save-target-is-a-file-"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocked,"fixture");
            CampaignSaveStore.TestDirectory=blocked;Click("保存并返回开局");
            Need(NightHubUI.IsOpen&&!CharacterCreationUI.IsOpen&&flow.Run.Phase==GamePhase.Night,"A failed save exited the current night.");
            Need(flow.SaveStatus.StartsWith("未能保存",StringComparison.Ordinal),"Failed save was shown as success.");
            yield return Capture("night-save-failed-still-here",true);
            CampaignSaveStore.TestDirectory=saveDirectory;Click("保存进度");
            Click("睡一觉 · 准备第 "+(day+1)+" 天");yield return null;yield return null;
            Need(!NightHubUI.IsOpen&&flow.Campaign.dayIndex==day+1&&flow.Run.Phase==GamePhase.Home,"Next-day button did not enter the real next home state.");
            flow.SetPaused(true);yield return null;Need(CampaignMenuUI.IsOpen,"Home pause did not open the shared save menu.");
            yield return Capture("day-two-paused",false);
            Click("保存并回到开局");yield return null;Need(CharacterCreationUI.IsOpen&&flow.HasCampaignSave,"Saved return did not expose continue.");
            yield return Capture("continue-existing-campaign",false);
            Click("BeginRun");yield return null;
            Need(FindButton("返回 · 保留当前经营")!=null,"Starting over did not request a concrete new-run choice.");
            yield return Capture("new-campaign-keeps-old-backup-confirmation",false);
            Click("返回 · 保留当前经营");Click("ContinueCampaign");yield return null;
            Need(!CharacterCreationUI.IsOpen&&flow.Campaign.dayIndex==day+1&&flow.Campaign.cashCents==cash,"Continue changed the saved day or cash.");
            yield return Capture("day-two-restored-paused",false);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--campaign-visual-world-hud")>=0)
                yield return CaptureWorldHUDs();
        }
        private IEnumerator CaptureWorldHUDs()
        {
            // Supplemental presentation evidence, kept separate from the nine
            // campaign checkpoints. No keyboard traversal or earned money claim.
            Need(flow.Run.Phase==GamePhase.Home,"Home HUD fixture requires the restored next-day home.");
            flow.SetPaused(false);flow.enabled=false;
            flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            Need(!CampaignMenuUI.IsOpen&&!NightHubUI.IsOpen&&!CharacterCreationUI.IsOpen,"A modal obscures the Home HUD.");
            yield return Capture("supplement-home-hud",false,false,true);

            Need(flow.Run.HasUnloadedEquipment,"Home carry portrait fixture has no owned equipment.");
            Need(flow.Run.Act("StovePickup",true),"Home carry portrait fixture could not pick up owned equipment.");
            flow.SetPaused(false);flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            Need(flow.Run.HoldingStove,"Home carry portrait fixture did not bind a held item.");
            yield return Capture("supplement-home-held-equipment",false,false,true);

            // Build a separate legal Market fixture via actual RunState
            // transactions, then normal save restoration constructs the scene.
            // This is not a claimed player traversal route.
            CampaignSaveStore.TestDirectory=Path.Combine(saveDirectory,"supplement-market");
            var profile=new RunProfile();var market=new RunState(4,profile);
            while(market.HasUnloadedEquipment)
            {Need(market.Act("StovePickup",true),"Market fixture starter pickup.");Need(market.Act("HomeCart",true),"Market fixture starter load.");}
            Need(market.Act("HomeExit",true)&&market.Phase==GamePhase.Market,"Market HUD fixture did not enter Market.");
            foreach(string shelf in new[]{"RiceShelf","EggShelf","DoughShelf"})Need(market.Act(shelf,true,true),"Market fixture product: "+shelf);
            Need(market.AddSupply("P04"),"Market fixture paper-bag purchase.");
            var data=new CampaignSaveData{campaign=CampaignState.New(profile,market),run=market.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
            Need(CampaignSaveStore.Save(data,out string saved),saved);Need(flow.ContinueCampaign(),flow.SaveStatus);
            flow.SetPaused(false);flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            Need(flow.Run.Phase==GamePhase.Market&&!CampaignMenuUI.IsOpen&&!CharacterCreationUI.IsOpen,"A modal obscures the Market HUD.");
            ArtHUD hud=flow.GetComponent<ArtHUD>();Need(hud!=null&&hud.Ready,"Market HUD is not ready.");
            yield return Capture("supplement-market-hud",false,false,true);
            hud.ShowShoppingReceipt(false);Need(hud.VisibleShoppingReceipt!=null,"Expanded Market receipt is missing.");
            yield return Capture("supplement-market-receipt",false,false,true);
            ShoppingReceiptView receipt=FindAnyObjectByType<ShoppingReceiptView>();
            Need(receipt!=null,"The open receipt view is missing.");Click("×");
            Need(!receipt.gameObject.activeInHierarchy,"The receipt's actual close callback did not close it.");
            yield return CaptureSupplyShop();
            yield return CapturePreparedFood(hud);
            yield return CaptureRecoveryUI();
            CampaignSaveStore.TestDirectory=saveDirectory;
        }
        private IEnumerator CaptureSupplyShop()
        {
            // Declared placement fixture only: put the shopper and the full
            // shopping cart at a real clear counter pose before opening the
            // panel. The shared helper only re-poses bodies through the same
            // IsPoseClear/CheckCapsule/CanUseRecoveryCounter gates a walker
            // would face; it never edits stock, capacity, cash or the clock,
            // and it is not a claim of keyboard traversal to the counter.
            PlaceRecoveryCounterFixture();
            Need(flow.CanUseRecoveryCounter,"Supply fixture pose failed the real counter gates (IsNear SupplyCounter + IsNearBasket 1.8m). Player="+flow.World.Player.transform.position+" basket="+flow.World.ShoppingMotor.BasketContactPoint);
            SupplyShop.Open(flow);yield return null;
            Need(flow.World.IsNear("SupplyCounter")&&flow.World.ShoppingMotor.IsNearBasket(1.8f),"SupplyShop fixture drifted from the counter after opening. Player="+flow.World.Player.transform.position+" basket="+flow.World.ShoppingMotor.BasketContactPoint);
            SupplyShop shop=FindAnyObjectByType<SupplyShop>();Need(shop!=null&&SupplyShop.IsOpen,"SupplyShop fixture did not open.");
            ScrollRect scroll=FindScroll("Browse current merchandise");Need(scroll!=null,"SupplyShop has no merchandise scroll.");
            float until=Time.realtimeSinceStartup+15;
            while(CountSupplyPortraits(shop,false)<SupplyCatalog.Items.Length&&Time.realtimeSinceStartup<until)yield return null;
            Need(CountSupplyPortraits(shop,false)==SupplyCatalog.Items.Length,"SupplyShop did not load every product photograph.");
            Button first=FindButton("Pick rice_small");Need(first!=null&&first.IsInteractable(),"Default bulk rice offer cannot receive focus.");
            EventSystem.current.SetSelectedGameObject(first.gameObject);Canvas.ForceUpdateCanvases();yield return null;
            Need(IsVisible(first.GetComponent<RectTransform>()),"Default bulk rice row is not visible after focus.");
            yield return Capture("supplement-supply-first-page",false,false,true);
            // V15 separates shelves into actual categories. The default five
            // bulk offers fit one page; exercise scrolling on the longer shelf.
            foreach(string category in new[]{"灶具锅具","工具","能源包材"})
            {Click("Category "+category);Canvas.ForceUpdateCanvases();yield return null;yield return Capture("supplement-supply-category-"+category,false,false,true);}
            scroll.StopMovement();scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1000)});
            Canvas.ForceUpdateCanvases();yield return null;
            Need(scroll.verticalNormalizedPosition<.001f,"SupplyShop wheel handler did not reach the last page.");
            ItemDefinition lastProduct=Array.FindLast(SupplyCatalog.Items,x=>SupplyCatalog.Category(x.id)=="能源包材");
            Text last=FindText(shop.transform,"Name",lastProduct.name.Split('·')[0]);
            Need(last!=null&&IsVisible(last.rectTransform),"The final supply row is not visible at the scroll end.");
            yield return Capture("supplement-supply-last-page",false,false,true);
            Click("收起 ×");yield return null;Need(!SupplyShop.IsOpen,"SupplyShop's actual close callback did not close it.");
        }
        private IEnumerator CaptureRecoveryUI()
        {
            // Separate declared state and initial pose fixture. Inventory comes
            // from a fresh career plus one unpaid retail item; no earned cash,
            // hardware input, walking route or collision-path claim is made.
            CampaignSaveStore.TestDirectory=Path.Combine(saveDirectory,"supplement-recovery");
            var profile=new RunProfile();var market=new RunState(4,profile);
            while(market.HasUnloadedEquipment)
            {Need(market.Act("StovePickup",true),"Recovery fixture starter pickup.");Need(market.Act("HomeCart",true),"Recovery fixture starter load.");}
            Need(market.Act("HomeExit",true)&&market.Phase==GamePhase.Market,"Recovery fixture did not enter Market.");
            Need(market.LoadedEquipment.Contains("U01")&&market.AddSupply("P04"),"Recovery fixture needs its owned starter spatula and one unpaid paper-bag pack.");
            var data=new CampaignSaveData{campaign=CampaignState.New(profile,market),run=market.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
            Need(CampaignSaveStore.Save(data,out string saved),saved);Need(flow.ContinueCampaign(),flow.SaveStatus);yield return null;
            flow.enabled=false;flow.SetPaused(false);flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            PlaceRecoveryCounterFixture();
            report.recoveryFixture="Fresh career1 starter gear loaded by RunState transactions; P04 unpaid in basket; restored through ContinueCampaign. Player/cart explicitly placed at a full-size clear counter pose; actual SupplyShop callbacks below. Not keyboard traversal or earned economy. The earlier supplement-supply-* captures reused this same pose-placement helper before SupplyShop.Open. Player="+flow.World.Player.transform.position+", basket="+flow.World.ShoppingMotor.BasketContactPoint;
            SupplyShop.Open(flow);yield return null;
            SupplyShop shop=FindAnyObjectByType<SupplyShop>();Need(shop!=null&&SupplyShop.IsOpen,"Recovery SupplyShop did not open.");
            float until=Time.realtimeSinceStartup+15;
            while(CountSupplyPortraits(shop,false)<SupplyCatalog.Items.Length&&Time.realtimeSinceStartup<until)yield return null;
            Need(CountSupplyPortraits(shop,false)==SupplyCatalog.Items.Length,"Recovery retail portraits did not load.");

            Click("ResaleTab");yield return null;
            RecoveryQuote sale=Array.Find(flow.GetResaleQuotes(),q=>q.id=="U01");Need(sale!=null,"Owned starter spatula has no real resale quote.");
            yield return Capture("supplement-recovery-sell-page",false,false,true);
            string before=RecoveryFingerprint();ClickRecoveryOffer(shop,sale.id);
            Need(FindButton("Confirm sale")?.IsInteractable()==true,"Valid sale quote is not confirmable.");
            yield return Capture("supplement-recovery-sale-quote",false,false,true);
            Click("Cancel sale");yield return null;
            RecoveryCheck("sale-cancel-no-mutation",RecoveryFingerprint()==before,"Closing the real sale confirmation preserves run and campaign money, stock, ownership and receipts.");
            int cash=flow.Run.CashCents,spent=flow.Run.SpentCents;
            ClickRecoveryOffer(shop,sale.id);Click("Confirm sale");
            until=Time.realtimeSinceStartup+8;
            while(ActiveNode(shop.transform,"Confirm trade shade")!=null&&Time.realtimeSinceStartup<until)yield return null;
            Need(ActiveNode(shop.transform,"Confirm trade shade")==null,"Sale hand-over did not finish within 8s wall time.");
            RecoveryCheck("sale-confirm-quoted-amount",flow.Run.CashCents==cash+sale.priceCents&&flow.Run.SpentCents==spent&&!flow.Run.LoadedEquipment.Contains(sale.id)&&flow.Campaign.equipment.Find(x=>x.id==sale.id)==null,"Actual Confirm sale callback added "+sale.priceCents+" cents once and removed owned "+sale.id+".");

            Click("Used offer tab");yield return null;
            RecoveryQuote buy=Array.Find(flow.GetUsedPurchaseQuotes(),q=>q.id=="U01");Need(buy!=null,"Unowned spatula is not offered as a 50% used item.");
            yield return Capture("supplement-recovery-buy-page",false,false,true);
            before=RecoveryFingerprint();ClickRecoveryOffer(shop,buy.id);yield return null;
            Text status=FindText(shop.transform,"Trade status");Button commit=FindButton("Buy used purchase");
            RecoveryCheck("unpaid-basket-explains-blocker",flow.Run.Cart.Count==1&&!flow.Run.Paid&&commit!=null&&!commit.IsInteractable()&&status!=null&&status.text.Contains("未付款商品"),"The real buy confirmation disables payment and names the unpaid basket, with no mutation.");
            Need(RecoveryFingerprint()==before,"Opening a blocked purchase mutated run/campaign state.");
            yield return Capture("supplement-recovery-unpaid-buy-blocked",false,false,true);
            Click("Cancel used purchase");yield return null;
            RecoveryCheck("blocked-buy-cancel-no-mutation",RecoveryFingerprint()==before,"Cancel used purchase preserves the unpaid basket and economy.");

            // Return the unpaid pack through its actual visible merchandise row.
            Click("ResaleTab");yield return null;Click("Category 能源包材");yield return null;Button pack=FindButton("Pick P04");Need(pack!=null&&pack.IsInteractable(),"Unpaid paper bags cannot be returned through their retail row.");
            EventSystem.current.SetSelectedGameObject(pack.gameObject);Canvas.ForceUpdateCanvases();yield return null;
            Need(IsVisible(pack.GetComponent<RectTransform>()),"Paper-bag row focus did not reveal the return button.");
            Click("Pick P04");yield return null;
            RecoveryCheck("return-unpaid-through-retail",flow.Run.Cart.Count==0&&!flow.Run.Paid&&flow.Run.CashCents==cash+sale.priceCents,"Actual visible P04 button returned the unpaid pack without a refund or charge.");
            Click("ResaleTab");Click("Used offer tab");yield return null;
            buy=Array.Find(flow.GetUsedPurchaseQuotes(),q=>q.id=="U01");Need(buy!=null,"Used quote disappeared after returning the unpaid pack.");
            before=RecoveryFingerprint();ClickRecoveryOffer(shop,buy.id);
            Need(FindButton("Buy used purchase")?.IsInteractable()==true,"Empty basket cannot confirm the affordable used quote.");
            yield return Capture("supplement-recovery-buy-quote",false,false,true);
            Click("Cancel used purchase");yield return null;
            RecoveryCheck("affordable-buy-cancel-no-mutation",RecoveryFingerprint()==before,"Cancelling an enabled payment confirmation leaves money and ownership unchanged.");
            cash=flow.Run.CashCents;spent=flow.Run.SpentCents;
            ClickRecoveryOffer(shop,buy.id);Click("Buy used purchase");
            until=Time.realtimeSinceStartup+8;
            while(ActiveNode(shop.transform,"Confirm trade shade")!=null&&Time.realtimeSinceStartup<until)yield return null;
            Need(ActiveNode(shop.transform,"Confirm trade shade")==null,"Used purchase hand-over did not finish within 8s wall time.");
            CampaignGearRow bought=flow.Campaign.equipment.Find(x=>x.id==buy.id);
            RecoveryCheck("buy-confirm-paid-basket",flow.Run.CashCents==cash-buy.priceCents&&flow.Run.SpentCents==spent+buy.priceCents&&flow.Run.Paid&&flow.Run.Cart.Count==1&&flow.Run.Cart.Contains(buy.id)&&bought!=null&&Mathf.Approximately(bought.condition,50),"Actual payment callback charged "+buy.priceCents+" cents once; the 50% used item remains in the paid basket, not remotely loaded.");
            Click("ResaleTab");yield return null;
            Need(FindButton("Paid goods home")?.IsInteractable()==true,"Paid goods cannot open the return-home confirmation.");
            yield return Capture("supplement-recovery-paid-home-row",false,false,true);
            yield return Resolution(1280,720);
            ScrollRect browse=FindScroll("Browse current merchandise");Need(browse!=null,"Paid goods retail viewport is missing.");
            browse.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();yield return null;
            yield return Capture("supplement-recovery-paid-home-last-page-1280x720",false,false,true);
            before=RecoveryFingerprint();Click("Paid goods home");
            Need(FindButton("Return paid goods home")?.IsInteractable()==true,"Return-home confirmation cannot receive the real action.");
            yield return Capture("supplement-recovery-return-home-confirmation",false,false,true);
            Click("Keep shopping");yield return null;
            RecoveryCheck("return-home-cancel-keeps-paid-goods",RecoveryFingerprint()==before&&flow.Run.Phase==GamePhase.Market,"Continue sorting cancels return home and preserves all paid goods; this visual fixture does not claim a completed return-home route.");
            Click("收起 ×");yield return null;Need(!SupplyShop.IsOpen,"Recovery shop close callback failed.");
            yield return Resolution(1920,1080);
        }
        private void PlaceRecoveryCounterFixture()
        {
            var world=flow.World;var motor=world.ShoppingMotor;Need(motor!=null,"Recovery fixture has no real shopping cart.");
            Vector3 anchor=world.Anchor("SupplyCounter");float y=world.Player.transform.position.y;motor.Detach();
            foreach(float radius in new[]{.4f,.7f,1f,1.35f})for(int angle=0;angle<360;angle+=30)
            {
                float radians=angle*Mathf.Deg2Rad;Vector3 body=new Vector3(anchor.x+Mathf.Cos(radians)*radius,y,anchor.z+Mathf.Sin(radians)*radius);
                if(!world.IsNearAt("SupplyCounter",body))continue;
                for(int heading=0;heading<360;heading+=30)
                {
                    Quaternion rotation=Quaternion.Euler(0,heading,0);if(!motor.IsPoseClear(body,rotation))continue;
                    CharacterController capsule=world.Player.Controller;Vector3 center=body+capsule.center;float offset=Mathf.Max(0,capsule.height*.5f-capsule.radius);
                    if(Physics.CheckCapsule(center-Vector3.up*offset,center+Vector3.up*offset,capsule.radius,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore))continue;
                    motor.ResetPoseForValidation(body,rotation);motor.Detach();world.Player.StopMotion();Physics.SyncTransforms();
                    if(flow.CanUseRecoveryCounter)return;
                }
            }
            Need(false,"No full-size clear player/cart pose satisfies the real recovery-counter gates; fixture cannot bypass them.");
        }
        private string RecoveryFingerprint()=>JsonUtility.ToJson(flow.Run.CaptureSave())+"|"+JsonUtility.ToJson(flow.Campaign);
        private void RecoveryCheck(string name,bool passed,string detail)
        {report.recoveryUIChecks.Add(new RecoveryUICheck{name=name,passed=passed,detail=detail});Need(passed,"Recovery UI check failed: "+name+". "+detail);}
        private static Transform ActiveNode(Transform root,string name)
        {foreach(Transform node in root.GetComponentsInChildren<Transform>())if(node.name==name&&node.gameObject.activeInHierarchy)return node;return null;}
        private static void ClickRecoveryOffer(SupplyShop shop,string id)
        {
            Transform card=ActiveNode(shop.transform,"Sell "+id);Need(card!=null,"No visible recovery offer card for "+id);
            Button review=card.GetComponentInChildren<Button>();Need(review!=null&&review.IsInteractable(),"No enabled review callback for "+id);review.onClick.Invoke();
        }
        private IEnumerator CapturePreparedFood(ArtHUD hud)
        {
            Transform stock=FindNode(flow.World.transform,"Stock_prepared");
            Need(stock!=null&&stock.gameObject.activeInHierarchy,"Prepared shelf sample is not present.");
            ValidatePreparedAsset(stock,"shelf");
            // Explicit presentation pose. Keep the production oblique camera and
            // scale; this does not claim keyboard travel or pickup animation QA.
            flow.World.ShoppingMotor.Detach();
            flow.World.Player.Teleport(flow.World.Anchor("PreparedShelf"));
            Vector3 facing=WorldRuntime.RenderBounds(stock.gameObject).center-flow.World.Player.transform.position;facing.y=0;
            Need(facing.sqrMagnitude>.001f,"Prepared shelf fixture has no facing direction.");
            flow.World.Player.visual.rotation=Quaternion.LookRotation(facing);
            flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();flow.SetPaused(false);flow.World.SetFocus("PreparedShelf");
            yield return new WaitForSecondsRealtime(1.2f);
            Vector3 viewport=flow.World.View.WorldToViewportPoint(WorldRuntime.RenderBounds(stock.gameObject).center);
            Need(viewport.z>0&&viewport.x>.02f&&viewport.x<.98f&&viewport.y>.02f&&viewport.y<.98f,"Prepared shelf is outside the production camera frame.");
            yield return Capture("supplement-prepared-shelf",false,false,true);
            Need(flow.Run.Act("PreparedShelf",true,false)&&flow.Run.HeldGrocery=="prepared","Declared prepared pickup transaction failed.");
            flow.SetPaused(false);flow.World.Player.inputEnabled=false;flow.World.Player.StopMotion();
            yield return new WaitForSecondsRealtime(.4f);
            Need(!stock.gameObject.activeInHierarchy,"Prepared sample stayed on the shelf after pickup.");
            Transform held=FindNode(flow.World.Player.visual,"Ingredient_prepared");
            Need(held!=null&&held.gameObject.activeInHierarchy,"Prepared model was not bound to the character.");
            ValidatePreparedAsset(held,"held");
            yield return Capture("supplement-prepared-held-long-name",false,false,true);
            Transform card=FindNode(hud.transform,"HeldItem");Need(card!=null&&card.gameObject.activeInHierarchy,"Long held-item card is missing.");
            ExecuteEvents.Execute(card.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
            Text complete=FindText(hud.transform,"CompleteItemName");
            Need(complete!=null&&complete.gameObject.activeInHierarchy&&complete.text==GameCatalog.Find("prepared").name,"Actual hover handler did not reveal the complete product name.");
            yield return Capture("supplement-prepared-held-full-name",false,false,true);
            ExecuteEvents.Execute(card.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);
            Need(!complete.gameObject.activeInHierarchy,"Actual hover exit did not dismiss the complete name.");
        }
        private void ValidatePreparedAsset(Transform crate,string checkpoint)
        {
            Transform batch=FindNode(crate,"PreparedFood_AholoG1");
            Need(batch!=null&&batch.parent==crate,"PreparedFood_AholoG1 is missing from the actual "+checkpoint+" item; an old fallback cannot pass.");
            var audit=new PreparedAssetCheck{checkpoint=checkpoint,root=crate.name,parts=batch.childCount,exactSixChildren=batch.childCount==6,noSealedMealFallback=true,allMeshesMatchImportedPrefabs=true};
            foreach(Transform node in crate.GetComponentsInChildren<Transform>(true))if(node.name.StartsWith("SealedMeal",StringComparison.Ordinal))audit.noSealedMealFallback=false;
            Need(audit.exactSixChildren&&audit.noSealedMealFallback,"Prepared item does not contain exactly six imported parts without SealedMeal fallback.");
            foreach(string kind in new[]{"FrozenRice","MealPouch"})
            {
                string asset=kind=="FrozenRice"?"PreparedFrozenRiceAI":"PreparedMealPouchAI";
                GameObject prefab=Resources.Load<GameObject>("Models/"+asset);Texture2D albedo=Resources.Load<Texture2D>("Models/Textures/"+asset+"_Albedo");
                Need(prefab!=null&&albedo!=null,"Prepared AI mesh/albedo is missing: "+asset);
                var imported=new HashSet<Mesh>();foreach(MeshFilter source in prefab.GetComponentsInChildren<MeshFilter>(true))if(source.sharedMesh!=null)imported.Add(source.sharedMesh);
                Need(imported.Count>0,"Prepared prefab has no actual mesh: "+asset);
                for(int i=0;i<3;i++)
                {
                    Transform part=batch.Find(kind+"_"+i);Need(part!=null,"Missing prepared child "+kind+"_"+i);
                    MeshFilter[] meshes=part.GetComponentsInChildren<MeshFilter>(true);Need(meshes.Length>0,"Prepared child has no imported MeshFilter.");
                    foreach(MeshFilter mesh in meshes)
                    {
                        bool matches=mesh.sharedMesh!=null&&imported.Contains(mesh.sharedMesh);audit.allMeshesMatchImportedPrefabs&=matches;
                        Need(matches,"Prepared child mesh is not from its declared imported prefab: "+part.name);
                        audit.prefabMeshFilters++;audit.vertices+=mesh.sharedMesh.vertexCount;
                    }
                }
            }
            report.preparedAssetChecks.Add(audit);
        }
        private static CampaignSaveData BuildDeclaredFixture()
        {
            var profile=new RunProfile();var initial=new RunState(4,profile);CampaignState campaign=CampaignState.New(profile,initial);
            campaign.dayIndex=1;campaign.vehicleLevel=1;campaign.cashCents=47000;campaign.dayOpeningCashCents=45000;campaign.gas=45;campaign.coal=12;campaign.electricity=45;
            campaign.energyNote="煤气罐补满 45 点 · ¥20\n蓄电瓶回家充电 · 免费补满 45 点";
            campaign.stock.Clear();string[] stock={"egg","meat","rice","dough","skewer","prepared","fruit","seasoning","P02","P04","P05"};
            for(int i=0;i<stock.Length;i++)campaign.stock.Add(new CampaignStockRow{instanceId="visual-stock-"+i,id=stock[i],name=CommerceReceiptData.ShortName(stock[i]),quantity=8+i*3,acquiredDay=1});
            campaign.equipment.Clear();string[] gear={"B04","V01","V05","U01","U04","E03","E05","E09"};
            foreach(string id in gear)campaign.equipment.Add(new CampaignGearRow{instanceId="visual-gear-"+id,id=id,name=CharacterCatalog.EquipmentName(id),condition=100,location=EquipmentVisualFactory.IsHeavy(id)?"home":"vehicle"});
            var day=new CampaignDayLedger{dayIndex=1,openingCashCents=45000,purchasesCents=12400,nightFuelCents=2000,revenueCents=16400,closingCashCents=47000,served=18,lost=2,district=BusinessSession.Districts[0].name,sessionId="declared-visual-fixture",committed=true};
            foreach(int index in new[]{0,2})
            {StallRecipe recipe=BusinessSession.Recipes[index];int count=index==0?10:8;day.sales.Add(new CampaignSaleRow{id=recipe.id,name=recipe.name,quantity=count,unitPriceCents=recipe.priceCents,totalCents=count*recipe.priceCents});}
            campaign.lastDay=day;campaign.history.Add(day);RunState night=RunState.FromCampaign(campaign,4);night.SetBusinessPhase(GamePhase.Night);night.Paused=true;
            return new CampaignSaveData{campaign=campaign,run=night.CaptureSave(),savedUtc=DateTime.UtcNow.ToString("O")};
        }
        private IEnumerator Resolution(int width,int height)
        {
            Screen.SetResolution(width,height,false);float until=Time.realtimeSinceStartup+5;
            while((Screen.width!=width||Screen.height!=height)&&Time.realtimeSinceStartup<until)yield return null;
            Need(Screen.width==width&&Screen.height==height,"Requested resolution was not applied: "+width+"x"+height);yield return null;yield return null;
        }
        private IEnumerator Capture(string name,bool night,bool requireFocus=true,bool supplemental=false)
        {
            yield return SettleTextForCapture();yield return new WaitForEndOfFrame();
            GameObject focus=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            if(requireFocus)Need(focus!=null&&focus.activeInHierarchy,"No active keyboard/controller focus at "+name);
            var issues=new List<string>();int visible=0,portraits=0,supplyPortraits=0,visibleSupplyPortraits=0,satchelCharacters=0;
            string heldCaption="",satchelCaption="";float supplyScroll=-1;
            int recoveryCards=0,recoveryIcons=0;string supplySurface="",tradeStatus="";bool tradeConfirmation=false,tradePrimaryEnabled=false;
            if(night)
            {
                NightHubUI ui=FindAnyObjectByType<NightHubUI>();Need(ui!=null,"Night UI missing.");
                foreach(Image portrait in ui.GetComponentsInChildren<Image>())if(portrait.name=="Actual item portrait"&&portrait.sprite!=null)portraits++;
                Need(portraits==flow.Campaign.stock.Count+flow.Campaign.equipment.Count,"Inventory is missing actual item portraits.");
                foreach(Text label in ui.GetComponentsInChildren<Text>())
                {
                    if(!IsVisible(label.rectTransform))continue;visible++;
                    if(label.preferredHeight>label.rectTransform.rect.height+2)issues.Add("Text overflows: "+label.text);
                    if(label.GetComponentInParent<ScrollRect>()==null&&!InsideScreen(label.rectTransform))issues.Add("Text outside screen: "+label.text);
                }
                Need(focus.transform.IsChildOf(ui.transform),"Keyboard focus escaped the night surface.");
            }
            if(supplemental)
            {
                SupplyShop shop=FindAnyObjectByType<SupplyShop>();
                if(shop!=null&&SupplyShop.IsOpen)
                {
                    supplyPortraits=CountSupplyPortraits(shop,false);visibleSupplyPortraits=CountSupplyPortraits(shop,true);
                    ScrollRect browse=FindScroll("Browse current merchandise");supplyScroll=browse!=null?browse.verticalNormalizedPosition:-1;
                    Transform resale=ActiveNode(shop.transform,"Owned equipment resale");
                    if(resale!=null)
                    {
                        Text heading=FindText(resale,"Resale heading");supplySurface=heading!=null?heading.text:"recovery";
                        foreach(Transform card in resale)
                        {
                            if(!card.gameObject.activeInHierarchy||!card.name.StartsWith("Sell ",StringComparison.Ordinal))continue;
                            recoveryCards++;Transform icon=card.Find("Equipment icon");Image image=icon!=null?icon.GetComponent<Image>():null;
                            if(image!=null&&image.enabled&&image.sprite!=null&&image.color.a>.99f&&image.canvasRenderer.GetAlpha()>.99f&&InsideScreen(image.rectTransform))recoveryIcons++;
                        }
                        if(recoveryCards==0||recoveryIcons!=recoveryCards)issues.Add("Visible recovery offer cards are missing their actual equipment portraits.");
                        if(ActiveNode(shop.transform,"Starter")!=null||ActiveNode(shop.transform,"BrowseHint")!=null)issues.Add("Retail starter/browse controls remain visible on the recovery surface.");
                    }
                    else
                    {
                        supplySurface="retail";
                        if(supplyPortraits!=SupplyCatalog.Items.Length||visibleSupplyPortraits==0)issues.Add("Supply product photographs are missing.");
                        Button home=FindButton("Paid goods home");
                        if(home!=null)
                        {
                            if(browse==null||browse.viewport==null)issues.Add("Paid return-home row has no merchandise viewport.");
                            else if(ScreenRect(browse.viewport).Overlaps(ScreenRect(home.GetComponent<RectTransform>())))issues.Add("Paid return-home button overlaps the clipped merchandise viewport.");
                            if(!InsideScreen(home.GetComponent<RectTransform>()))issues.Add("Paid return-home button is outside the screen.");
                        }
                    }
                    Transform confirmation=ActiveNode(shop.transform,"Confirm trade shade");tradeConfirmation=confirmation!=null;
                    if(confirmation!=null)
                    {
                        Text status=FindText(confirmation,"Trade status");tradeStatus=status!=null?status.text:"";
                        Button primary=FindButton("Confirm sale")??FindButton("Buy used purchase")??FindButton("Return paid goods home");
                        tradePrimaryEnabled=primary!=null&&primary.IsInteractable();
                    }
                    foreach(Text label in shop.GetComponentsInChildren<Text>())
                    {
                        if(!IsVisible(label.rectTransform)||string.IsNullOrEmpty(label.text))continue;
                        visible++;CheckRenderedText(label,issues);
                    }
                }
                else
                {
                    ArtHUD hud=flow.GetComponent<ArtHUD>();
                    Text satchel=FindText(hud.transform,"SatchelCount");
                    if(satchel==null||!satchel.gameObject.activeInHierarchy)issues.Add("Satchel count is missing.");
                    else
                    {
                        satchelCaption=satchel.text;satchelCharacters=satchel.cachedTextGenerator.characterCountVisible;
                        CheckRenderedText(satchel,issues);
                        if(satchel.text.Replace(" ","")!="0/6")issues.Add("Empty satchel fixture does not show 0/6.");
                    }
                    Text held=FindText(hud.transform,"HeldTitle");
                    if(held!=null&&held.gameObject.activeInHierarchy){heldCaption=held.text;CheckRenderedText(held,issues);}
                    Text complete=FindText(hud.transform,"CompleteItemName");
                    if(complete!=null&&complete.gameObject.activeInHierarchy)CheckRenderedText(complete,issues);
                }
            }
            string file=Path.Combine(directory,name+".png");if(File.Exists(file))file=Path.Combine(directory,name+"-"+DateTime.UtcNow.ToString("HHmmss-fff")+".png");
            ScreenCapture.CaptureScreenshot(file);float until=Time.realtimeSinceStartup+5;while(!File.Exists(file)&&Time.realtimeSinceStartup<until)yield return null;
            Need(File.Exists(file),"Screenshot missing: "+file);
            ScrollRect stock=FindScroll("Owned stock and equipment");
            (supplemental?report.supplementalCheckpoints:report.checkpoints).Add(new Checkpoint{name=name,screenshot=file,width=Screen.width,height=Screen.height,phase=flow.Run.Phase.ToString(),day=flow.Campaign?.dayIndex??0,cash=flow.Campaign?.cashCents??0,night=NightHubUI.IsOpen,paused=flow.Run.Paused,focus=focus!=null?focus.name:"world-input",saveStatus=flow.SaveStatus,inventoryScroll=stock!=null?stock.verticalNormalizedPosition:-1,actualInventoryIcons=portraits,visibleTexts=visible,layoutIssues=issues.ToArray(),supplyProductIcons=supplyPortraits,visibleSupplyProductIcons=visibleSupplyPortraits,supplyScroll=supplyScroll,satchelCount=satchelCaption,satchelRenderedCharacters=satchelCharacters,heldItem=flow.Run.HeldGrocery,heldTitle=heldCaption,recoveryCards=recoveryCards,recoveryIcons=recoveryIcons,supplySurface=supplySurface,tradeStatus=tradeStatus,tradeConfirmation=tradeConfirmation,tradePrimaryEnabled=tradePrimaryEnabled});
            Need(issues.Count==0,"Layout failed at "+name+": "+string.Join(" | ",issues));
        }
        private static int CountSupplyPortraits(SupplyShop shop,bool visibleOnly)
        {
            int count=0;foreach(Image image in shop.GetComponentsInChildren<Image>(!visibleOnly))
                if(image.name.StartsWith("Portrait ",StringComparison.Ordinal)&&image.enabled&&image.sprite!=null&&(!visibleOnly||IsVisible(image.rectTransform)))count++;
            return count;
        }
        private static Transform FindNode(Transform root,string name)
        {foreach(Transform node in root.GetComponentsInChildren<Transform>(true))if(node.name==name)return node;return null;}
        private static Text FindText(Transform root,string name,string content=null)
        {foreach(Text label in root.GetComponentsInChildren<Text>(true))if(label.name==name&&(content==null||label.text==content))return label;return null;}
        private static void CheckRenderedText(Text label,List<string> issues)
        {
            if(string.IsNullOrWhiteSpace(label.text))return;
            if(label.cachedTextGenerator.characterCountVisible==0)issues.Add("Text produced no visible characters: "+label.name+" = "+label.text);
            if(!label.resizeTextForBestFit&&label.preferredHeight>label.rectTransform.rect.height+2)issues.Add("Text overflows: "+label.name+" = "+label.text);
            if(label.GetComponentInParent<ScrollRect>()==null&&!InsideScreen(label.rectTransform))issues.Add("Text outside screen: "+label.name);
        }
        private static IEnumerator SettleTextForCapture()
        {
            // A resolution change alters CanvasScaler and dynamic-font glyph
            // requests on separate frames. Refresh presentation geometry once,
            // then require a quiet atlas before recording this diagnostic frame.
            // This does not change production fonts, sizes or rendering settings.
            int rebuiltFrame=Time.frameCount;float rebuiltAt=Time.realtimeSinceStartup;
            Action<Font> rebuilt=font=>{rebuiltFrame=Time.frameCount;rebuiltAt=Time.realtimeSinceStartup;};
            Font.textureRebuilt+=rebuilt;
            try
            {
                foreach(Text label in FindObjectsByType<Text>())if(label.isActiveAndEnabled)label.SetAllDirty();
                Canvas.ForceUpdateCanvases();float deadline=Time.realtimeSinceStartup+3;
                while(Time.realtimeSinceStartup<deadline&&(Time.frameCount-rebuiltFrame<3||Time.realtimeSinceStartup-rebuiltAt<.30f))yield return null;
                Need(Time.frameCount-rebuiltFrame>=3&&Time.realtimeSinceStartup-rebuiltAt>=.30f,"Dynamic font atlas did not settle before capture.");
                Canvas.ForceUpdateCanvases();yield return null;
            }
            finally{Font.textureRebuilt-=rebuilt;}
        }
        private static bool IsVisible(RectTransform rect)
        {
            ScrollRect scroll=rect.GetComponentInParent<ScrollRect>();if(scroll==null)return true;
            return ScreenRect(rect).Overlaps(ScreenRect(scroll.viewport));
        }
        private static bool InsideScreen(RectTransform rect)
        {Rect r=ScreenRect(rect);return r.xMin>=-1&&r.yMin>=-1&&r.xMax<=Screen.width+1&&r.yMax<=Screen.height+1;}
        private static Rect ScreenRect(RectTransform rect)
        {var corners=new Vector3[4];rect.GetWorldCorners(corners);return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y);}
        private static ScrollRect FindScroll(string name)
        {foreach(ScrollRect scroll in FindObjectsByType<ScrollRect>())if(scroll.name==name&&scroll.gameObject.activeInHierarchy)return scroll;return null;}
        private static Button FindButton(string title)
        {foreach(Selectable selectable in Selectable.allSelectablesArray)if(selectable is Button button&&button.gameObject.activeInHierarchy&&(button.name==title||button.GetComponentInChildren<Text>()?.text==title))return button;return null;}
        private static void Click(string title)
        {Button button=FindButton(title);Need(button!=null&&button.IsInteractable(),"No enabled button: "+title);button.onClick.Invoke();}
        private void BuildFixtureLabel()
        {
            RectTransform safe=CampaignUI.Canvas(transform,"Explicit fixture provenance",1600,Color.clear);
            safe.parent.GetComponent<Image>().raycastTarget=false;
            Text label=CampaignUI.Text(safe,"QA 显式状态夹具 · 非实玩收益",Vector2.zero,new Vector2(500,32),17,CampaignUI.PaperColor,TextAnchor.MiddleLeft);
            RectTransform rt=label.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=Vector2.zero;rt.anchoredPosition=new Vector2(16,7);
            Shadow shadow=label.gameObject.AddComponent<Shadow>();shadow.effectColor=CampaignUI.WithAlpha(CampaignUI.Ink,.9f);shadow.effectDistance=new Vector2(1,-1);
        }
        private static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private void OnDestroy(){Application.logMessageReceived-=Log;CampaignSaveStore.TestDirectory=null;}
    }
}
