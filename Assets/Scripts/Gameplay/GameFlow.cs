using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Guochao
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class GameFlow : MonoBehaviour
    {
        public static GameFlow Instance { get; private set; }
        public Snapshot State { get; private set; } = new Snapshot();
        public event Action<Snapshot> Changed;
        // CHECKOUT10: raised when the cashier primary action opens the checkout
        // selection screen. The UI subscribes; headless and presentation-off
        // runs keep using the legacy full-basket Act("Cashier") path.
        public event Action CheckoutSelectionRequested;
        public bool IsChoosingCheckout { get; private set; }
        public WorldRuntime World => world;
        public RunState Run => run;
        public BusinessController Business => business;
        public bool IsTransitioning => transition != null && transition.Active;
        internal bool MorningTravelPlaying => transition != null && transition.MorningTravelPlaying;
        public bool IsBusy => IsTransitioning || actionRemaining > 0;
        private BusinessController business;
        public static bool PresentationEnabled => Application.isPlaying && !Application.isBatchMode &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") < 0 &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "--traversal-test") < 0 &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "--capture-dir") < 0;
        [Min(.25f)] public float checkoutServiceSeconds = 4f;
        private RunState run;
        private WorldRuntime world;
        private string focus = "";
        private float nextPublish;
        private bool initialized;
        private static bool HardwareInputEnabled => Array.IndexOf(Environment.GetCommandLineArgs(),"--interaction-test")<0&&Array.IndexOf(Environment.GetCommandLineArgs(),"--business-test")<0;
        private float actionRemaining;
        private float ActionDuration => run?.Profile?.TransferSeconds??.55f;
        private float ContactTime => ActionDuration*(.26f/.55f);
        private string pendingTarget = "", pendingItem = "";
        private int pendingReturnIndex = -1;
        private bool contactCommitted;
        private Vector3 pendingContact;
        private SceneTransition transition;
        private bool holdSnapshot;
        private static readonly string[] HomeTargets = { "StovePickup", "HomeCart", "HomeExit" };
        private static readonly string[] HomeCarryTargets = { "HomeCart", "StovePickup" };
        private static readonly string[] HomeLoadedTargets = { "HomeExit" };
        private static string[] MarketTargets
        {
            get
            {
                var targets=new List<string>();
                foreach(ItemDefinition item in GameCatalog.Items)if(!targets.Contains(item.anchor))targets.Add(item.anchor);
                targets.AddRange(new[]{"Cashier","MarketCart","MarketExit","SupplyCounter","RecoveryCounter"});return targets.ToArray();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            run = new RunState(checkoutServiceSeconds);
            Publish();
        }

        private void Start()
        {
            Initialize();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0) StartCoroutine(SmokeRoutine());
            if(PresentationEnabled&&Array.IndexOf(Environment.GetCommandLineArgs(),"--interaction-test")<0&&Array.IndexOf(Environment.GetCommandLineArgs(),"--receipt-visual-test")<0)OpenCharacterCreation();
        }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (run == null) run = new RunState(checkoutServiceSeconds);
            CreateWorld();
        }

        private void Update()
        {
            if (!initialized || run == null) return;
            if(CharacterCreationUI.IsOpen)return;
            if(run.Phase==GamePhase.Night)return;
            if(OnlineProcurementUI.IsOpen)
            {
                AdvanceSimulation(Time.deltaTime);world?.Sync(run);
                if(world?.Player!=null)world.Player.presentationLocked=OnlineProcurementUI.IsOpen||IsBusy;
                Publish();return;
            }
            if(SupplyShop.IsOpen)
            {
                if(HardwareInputEnabled&&Input.GetKeyDown(KeyCode.Escape))SupplyShop.Close();
                AdvanceSimulation(Time.deltaTime);world?.Sync(run);if(world?.Player!=null)world.Player.presentationLocked=true;Publish();return;
            }
            // CHECKOUT10: the selection screen is modal but not a global pause:
            // the market timer and player are frozen, animations keep running.
            if(IsChoosingCheckout)
            {
                if(HardwareInputEnabled&&Input.GetKeyDown(KeyCode.Escape))CancelCheckoutSelection();
                AdvanceSimulation(Time.deltaTime);world?.Sync(run);
                if(world?.Player!=null)world.Player.presentationLocked=true;
                Publish();return;
            }
            if (HardwareInputEnabled&&Input.GetKeyDown(KeyCode.Escape))
            {
                if(MealPlanPanel.TryCloseActive())return;
                if(BusinessWorldHUD.TryCloseActiveOverlay())return;
                if(CampaignMenuUI.IsOpen){CampaignMenuUI.Resume();return;}
                SetPaused(!run.Paused);
            }
            if(run.Phase==GamePhase.SiteSelection||run.Phase==GamePhase.Business||run.Phase==GamePhase.DaySummary){Publish();return;}
            if (IsTransitioning)
            {
                if (world != null && world.Player != null) world.Player.presentationLocked = true;
                return;
            }
            AdvanceSimulation(Time.deltaTime);
            if (IsTransitioning) return;
            UpdateFocus();
            if (HardwareInputEnabled&&!run.Paused && !IsBusy && Input.GetKeyDown(KeyCode.F)) ToggleShoppingCart();
            if (HardwareInputEnabled&&!run.Paused && !IsBusy && Input.GetKeyDown(KeyCode.E)) PrimaryAction();
            if (HardwareInputEnabled&&!run.Paused && !IsBusy && Input.GetKeyDown(KeyCode.R)) ReturnFocusedItem();
            if (IsTransitioning) return;
            if (world != null) { world.Sync(run); if(world.Player!=null)world.Player.presentationLocked = IsBusy; }
            if (Time.unscaledTime >= nextPublish)
            { nextPublish = Time.unscaledTime + .1f; Publish(); }
        }

        public void PrimaryAction()
        {
            if (world?.Player == null || run == null || IsBusy || run.Paused || IsChoosingCheckout) return;
            UpdateFocus();
            string target = focus;
            InteractionCandidate candidate = world.EvaluateInteraction(target,world.Player.transform.position);
            if (!candidate.primary)
            {
                // Distinguish "walk closer" from "in range but this action is
                // not legal right now", so a highlighted target never refuses
                // silently or with the wrong instruction.
                if (!string.IsNullOrEmpty(target))
                    run.SetMessage(target=="Cashier"&&candidate.blockedBy=="basket-away"
                        ? "把购物车推到收银台旁，再按 E 结账。"
                        : target=="RecoveryCounter"&&candidate.blockedBy=="basket-away"
                        ? "把购物车带到回收站旁，再按 E 办理旧货代收。"
                        : target=="SupplyCounter"&&candidate.blockedBy=="basket-away"
                        ? "把购物车带到采购柜台旁，再挑选直购商品。"
                        : candidate.inRange ? "当前状态不能执行，请检查手中物品、购物车和付款状态。"
                        : "走到目标前方再操作。");
                Publish(); return;
            }
            if(target=="SupplyCounter")
            {
                SupplyShop.Open(this);world.Player.StopMotion();world.Player.presentationLocked=true;
                Publish();return;
            }
            // RECOVERY12: the authored 小摊贩回收站 corner is its own trade gate.
            // E at the real "18_回收台" recovery table opens the resale page
            // directly; the procurement counter entry above is unchanged.
            if(target=="RecoveryCounter")
            {
                SupplyShop.Open(this,true);world.Player.StopMotion();world.Player.presentationLocked=true;
                Publish();return;
            }
            if(target=="ParcelBox")
            {
                OpenOnlineParcels();Publish();return;
            }
            // CHECKOUT10: with presentation on and a UI subscribed, the cashier
            // primary action opens the selection screen instead of queueing the
            // legacy full-basket checkout.
            if(target=="Cashier"&&PresentationEnabled&&CheckoutSelectionRequested!=null&&
                run.CanOpenCheckoutSelection(IsActionInRange("Cashier")))
            {
                IsChoosingCheckout=true;run.CheckoutSelectionOpen=true;
                world.Player.StopMotion();world.Player.presentationLocked=true;
                CheckoutSelectionRequested?.Invoke();
                Publish();return;
            }
            if (PresentationEnabled && (target=="HomeExit" || target=="MarketExit"))
            {
                bool market = target=="HomeExit";
                BeginTransition(market ? "惠民仓储超市" : "本轮整备结果", market ? "整备结束 · 开始清晨采购" : "核对货物与本轮支出", () => {
                    run.Act(target,true,BasketAtHand);
                    if (market) CreateWorld(); else if(run.Phase==GamePhase.SiteSelection)EnterBusiness();else world?.Sync(run);
                },market);
                return;
            }
            if (PresentationEnabled && IsItemTarget(target))
            { BeginItemAction(target); return; }
            CommitAction(target,-1);
        }

        private bool BasketAtHand => world?.ShoppingMotor != null && world.ShoppingMotor.IsAttached;

        // CHECKOUT10 selection screen lifecycle. Open only from the real cashier
        // spot with an unpaid, non-empty basket; Esc or any exit cancels safely.
        public void CancelCheckoutSelection()
        {
            if(!IsChoosingCheckout)return;
            IsChoosingCheckout=false;
            if(run!=null){run.CheckoutSelectionOpen=false;run.CancelPendingCheckout();run.SetMessage("已取消结账选择：未扣款，商品仍在购物车。");}
            CancelItemAction();world?.Sync(run);Publish();
        }

        // The UI re-checks through PreviewCheckoutSelection before calling this.
        // The real cashier position is re-verified here: walking away before
        // confirming cancels safely, exactly like Esc. Failure deducts nothing
        // and leaves the basket untouched.
        public bool ConfirmCheckoutSelection(IReadOnlyList<string> ids)
        {
            if(!IsChoosingCheckout||run==null)return false;
            bool atCounter=world!=null&&world.HasAnchor("Cashier")&&IsActionInRange("Cashier");
            if(!atCounter){CancelCheckoutSelection();return false;}
            bool ok=run.ConfirmCheckoutSelection(ids,atCounter);
            if(ok){IsChoosingCheckout=false;run.CheckoutSelectionOpen=false;CancelItemAction();}
            world?.Sync(run);Publish();return ok;
        }

        public bool CanReturnItem(int index)
        {
            if(world==null||run==null||IsBusy||!run.CanReturnItem(index,BasketAtHand))return false;
            ItemDefinition item=GameCatalog.Find(run.Cart[index]);
            return item!=null&&item.anchor==focus&&IsActionInRange(focus);
        }

        public void ReturnItem(int index)
        {
            if(world==null||run==null)return;
            UpdateFocus();
            if(!CanReturnItem(index))return;
            if(PresentationEnabled)BeginItemAction(focus,index);
            else CommitAction(focus,index);
        }

        public void ToggleShoppingCart()
        {
            if (run==null || run.Paused || IsBusy || run.Phase!=GamePhase.Market || world?.ShoppingMotor==null) return;
            if (!string.IsNullOrEmpty(run.HeldGrocery))
            {
                run.SetMessage(world.ShoppingMotor.IsAttached ? "先放下手中的货物，再按 F 停车。" : "先放下手中的货物，再按 F 接车。");
                Publish();
                return;
            }
            if (world.ShoppingMotor.IsAttached)
            {
                world.ShoppingMotor.Detach();
                run.SetMessage("购物车已停放，走近把手按 F 接车。");
            }
            else
            {
                world.ShoppingMotor.TryAttach();
                run.SetMessage(world.ShoppingMotor.IsAttached
                    ? "已接手购物车：W 前进，S 后退，A / D 转向。"
                    : "走到购物车把手旁，再按 F 接车。");
            }
            world.Sync(run); UpdateFocus(); Publish();
        }

        private static bool IsItemTarget(string target) => target=="StovePickup" || target=="HomeCart" ||
            target=="MarketCart" || target=="ShoppingCart" || Array.Exists(GameCatalog.Items,item=>item.anchor==target);

        // The anchor defines a usable standing side; the real model defines what
        // is being touched. A generous interaction marker alone is not reach.
        // Read-only sampling uses this same spatial verdict. The primary
        // verdict additionally includes business and supply-counter rules.
        public bool IsActionInRange(string target)
        {
            if(world?.Player==null || string.IsNullOrEmpty(target))return false;
            return world.EvaluateInteraction(target, world.Player.transform.position).inRange;
        }

        private bool TryContact(string target,out Vector3 contact)
        {
            contact=Vector3.zero;
            if(world==null || world.Player==null)return false;
            if(target=="ShoppingCart" && world.ShoppingMotor!=null)
            {contact=world.ShoppingMotor.BasketContactPoint;return true;}
            // Same contact resolution the candidate verdict used for discovery
            // and range, so presentation facing and the commit check agree with
            // the highlighted face instead of a stale bounds centre.
            return world.TryResolveContact(target, world.Player.transform.position, out contact);
        }

        private void BeginItemAction(string target,int returnIndex=-1)
        {
            if(world?.Player==null || !TryContact(target,out pendingContact))return;
            pendingTarget=target;pendingReturnIndex=returnIndex;
            pendingItem=returnIndex>=0?run.Cart[returnIndex]:"";
            contactCommitted=false;actionRemaining=ActionDuration;
            world.Player.presentationLocked=true;
            world.Player.BeginInteraction(pendingContact,ActionDuration);
            Publish();
        }

        // Split a frame at the contact boundary. A timeout before contact cancels
        // the pending operation; an earlier contact is committed once before the
        // remaining frame time is charged. Pause advances neither timeline.
        private void AdvanceSimulation(float deltaTime)
        {
            if(run.Paused)return;
            float left=Mathf.Max(0,deltaTime);
            while(left>.000001f)
            {
                bool acting=actionRemaining>0;
                float elapsed=ActionDuration-actionRemaining;
                float untilBoundary=acting?(contactCommitted?actionRemaining:Mathf.Max(0,ContactTime-elapsed)):left;
                float step=Mathf.Min(left,untilBoundary);
                if(acting)
                {
                    actionRemaining=Mathf.Max(0,actionRemaining-step);
                    world?.Player?.AdvanceInteraction(step,1-actionRemaining/ActionDuration,ContactTime/ActionDuration);
                }
                GamePhase before=run.Phase;
                run.Tick(step,world!=null && world.HasAnchor("Cashier") && IsActionInRange("Cashier"));
                left-=step;
                if(before!=run.Phase){HandlePhaseChange(before);return;}
                if(acting && !contactCommitted && ActionDuration-actionRemaining>=ContactTime-.00001f)
                {
                    contactCommitted=true;
                    bool sameItem=pendingReturnIndex<0 || (pendingReturnIndex<run.Cart.Count && run.Cart[pendingReturnIndex]==pendingItem);
                    bool actionReady=pendingReturnIndex>=0
                        // FREEDOM19: readiness for a return uses the return
                        // gate (never the purchase-oriented CanPrimary), plus
                        // the same-shelf anchor check.
                        ? pendingReturnIndex<run.Cart.Count &&
                          run.CanReturnItem(pendingReturnIndex,BasketAtHand) &&
                          GameCatalog.Find(run.Cart[pendingReturnIndex]).anchor==pendingTarget
                        : run.CanPrimary(pendingTarget,true,BasketAtHand);
                    bool ready=sameItem && IsActionInRange(pendingTarget) &&
                        world.Player.IsFacing(pendingContact) && actionReady;
                    if(ready)CommitAction(pendingTarget,pendingReturnIndex);
                    else { run.SetMessage("请站到物品前方再操作。"); CancelItemAction(); Publish(); }
                }
                if(acting && actionRemaining<=.00001f)CancelItemAction();
                if(!acting)break;
            }
        }

        private void CommitAction(string target,int returnIndex)
        {
            GamePhase before=run.Phase;
            bool accepted=returnIndex>=0?run.ReturnItem(returnIndex,target,IsActionInRange(target)):
                run.Act(target,IsActionInRange(target),BasketAtHand);
            if(before!=run.Phase){HandlePhaseChange(before);return;}
            world?.Sync(run);
            if(accepted && IsItemTarget(target))world?.GetComponent<GameFeelAudio>()?.NotifyInteraction(target);
            Publish();
        }

        private void CancelItemAction()
        {
            actionRemaining=0;pendingTarget="";pendingItem="";pendingReturnIndex=-1;contactCommitted=false;
            if(world?.Player!=null){world.Player.EndInteraction();world.Player.presentationLocked=IsTransitioning;}
        }

        private void HandlePhaseChange(GamePhase before)
        {
            CancelItemAction();
            // CHECKOUT10: any phase change (timeout, load, departure) safely
            // retracts an unconfirmed selection without touching the basket.
            if(IsChoosingCheckout){IsChoosingCheckout=false;run.CheckoutSelectionOpen=false;}
            run?.CancelPendingCheckout();
            SupplyShop.Close();
            if(!PresentationEnabled)
            { if(run.Phase==GamePhase.Market)CreateWorld(); return; }
            // Timeout has already committed in RunState; retain the old displayed
            // snapshot until the screen is opaque, just like a manual departure.
            holdSnapshot=true;
            bool market=run.Phase==GamePhase.Market;
            BeginTransition(market?"惠民仓储超市":"本轮采购结束",market?"整备时间到 · 带上已装物品":"整理本轮采购结果",()=>{
                if(market)CreateWorld();else if(run.Phase==GamePhase.SiteSelection)EnterBusiness();else world?.Sync(run);
            },market);
        }

        private void BeginTransition(string title,string subtitle,Action atBlack,bool travel=false)
        {
            if(transition==null)transition=gameObject.AddComponent<SceneTransition>();
            actionRemaining=0;
            if(world?.Player!=null){world.Player.StopMotion();world.Player.presentationLocked=true;world.Player.inputEnabled=false;}
            Action arrive=()=>{
                holdSnapshot=false;
                atBlack();
                if(world?.Player!=null){world.Player.presentationLocked=true;world.Player.inputEnabled=false;}
                Publish();
            };
            Action finish=()=>{
                holdSnapshot=false;
                world?.Sync(run);
                if(world?.Player!=null)world.Player.presentationLocked=false;
                UpdateFocus();Publish();
            };
            if(travel)
            {
                string[] equipment=run.Profile==null?null:run.LoadedEquipment.ToArray();
                string[] cargo=run.Cargo.ToArray();
                string[] packages=new List<string>(run.PackingSupplies).ToArray();
                transition.PlayTravel(run.StoveLoaded,arrive,finish,equipment,run.Profile?.TravelSeconds??15,cargo,packages);
            }
            else transition.Play(title,subtitle,arrive,finish);
            Publish();
        }

        public void RestartRun()
        {
            if(PresentationEnabled){OpenCharacterCreation();return;}
            transition?.Cancel(); holdSnapshot=false; CancelItemAction();
            run = new RunState(checkoutServiceSeconds); focus = ""; initialized = true;
            CreateWorld();
        }

        public void OpenCharacterCreation()
        {
            NightHubUI.Close();CampaignMenuUI.Close();
            business?.Cancel();business=null;transition?.Cancel();SupplyShop.Close();
            GetComponent<ArtHUD>()?.SetGameplayVisible(false);
            CharacterCreationUI.Open(StartProfile,StartPractice,PreviewProfile);
        }
        private void PreviewProfile(RunProfile profile)
        {
            run=new RunState(checkoutServiceSeconds,profile);run.Paused=true;CreateWorld();
        }
        public void StartProfile(RunProfile profile)
        {
            if(!profile.Validate(out _))return;
            if(!CampaignSaveStore.ArchiveBeforeNewRun(out string status)){SaveStatus=status;OpenCharacterCreation();return;}
            CancelItemAction();run=new RunState(checkoutServiceSeconds,profile);focus="";
            Campaign=CampaignState.New(profile,run);
            GetComponent<ArtHUD>()?.SetGameplayVisible(true);CreateWorld();
            QueueAutoSave();
        }
        // Explicit visual QA fixture only. Production input cannot replace a run.
        public void PresentReceiptValidationState(RunState fixture)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--receipt-visual-test")<0||fixture==null||fixture.Phase!=GamePhase.Market)
                throw new InvalidOperationException("Receipt fixture requires its opt-in diagnostic and a Market state.");
            business?.Cancel();business=null;transition?.Cancel();SupplyShop.Close();
            holdSnapshot=false;CancelItemAction();run=fixture;focus="";initialized=true;
            GetComponent<ArtHUD>()?.SetGameplayVisible(true);CreateWorld();Publish();
        }
        public void StartPractice()
        {
            Campaign=null;NightHubUI.Close();CampaignMenuUI.Close();
            run=new RunState(checkoutServiceSeconds);run.SetBusinessPhase(GamePhase.SiteSelection);
            EnterBusiness(BusinessSetup.MidgamePractice());
        }
        public bool ChangeSupply(string id,bool remove)
        {
            if(world==null||run.Paused||!world.IsNear("SupplyCounter")||world.ShoppingMotor==null||!world.ShoppingMotor.IsNearBasket(1.8f))return false;
            // BILL9: the bill channel never admits shelf cartons; keep the real
            // AddSupply guard as the single gate so UI shortcuts cannot bypass it.
            bool ok=remove?run.ReturnItem(run.Cart.IndexOf(id),"SupplyCounter",true):run.AddSupply(id);
            world.Sync(run);Publish();return ok;
        }
        public BusinessSetup BuildBusinessSetup()
        {
            // Procurement suggestions and the actual service share one inventory
            // mapper; only loaded/cargo goods are usable for business. An unpaid
            // or paid-but-unloaded basket and returned-home goods stay excluded.
            var setup=ProcurementPlanCheck.Project(run,includeBasket:false).ToSetup(run);
            setup.canReturnMarket=run.MarketUsed<120;
            setup.seed=Campaign==null?12017:Campaign.seed+Campaign.dayIndex*7919;
            StallProgressionRules.ApplyBusinessSetup(Campaign,setup);
            return setup;
        }
        private void EnterBusiness(BusinessSetup setup=null)
        {
            SupplyShop.Close();CancelItemAction();
            if(setup==null)setup=BuildBusinessSetup();
            run.SetBusinessPhase(GamePhase.SiteSelection);
            if(world!=null){world.gameObject.SetActive(false);world.View.enabled=false;}
            GetComponent<ArtHUD>()?.SetGameplayVisible(false);
            business=BusinessController.Begin(setup,OnBusinessPhase,OnBusinessCash,()=>{
                business=null;run.ReturnToMarket();CreateWorld();world.View.enabled=true;GetComponent<ArtHUD>()?.SetGameplayVisible(true);
                QueueAutoSave();
            },()=>{if(setup.practice){business=null;StartPractice();}else EnterNightHub();});
            Publish();
        }

        public void SetPaused(bool paused)
        {
            if (run == null || run.Phase == GamePhase.Error) return;
            run.Paused = paused;
            if(!paused)CampaignMenuUI.Close();
            if(!IsTransitioning)world?.Sync(run);
            if(world?.Player!=null){world.Player.presentationLocked=IsBusy; if(paused)world.Player.StopMotion();}
            Publish();
        }

        private void ReturnFocusedItem()
        {
            for (int i = 0; i < run.Cart.Count; i++)
                if (GameCatalog.Find(run.Cart[i]).anchor == focus) { ReturnItem(i); return; }
        }

        private void CreateWorld(GamePhase? displayedPhase=null)
        {
            if (world != null)
            {
                world.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(world.gameObject); else DestroyImmediate(world.gameObject);
            }
            focus = "";
            try
            {
                GameObject stage = new GameObject("Playable_" + run.Phase);
                stage.transform.SetParent(transform);
                world = stage.AddComponent<WorldRuntime>(); world.Build(displayedPhase??run.Phase);
                run.LoadValidator=run.Profile==null?null:(equipment,cargo)=>world.ValidateLoad(equipment,cargo,run.PackingSupplies);
                world.Sync(run);
                InstallOnlineParcelWorld();
                UpdateFocus();
            }
            catch (Exception exception)
            {
                run.Fail("场景无法启动：" + exception.Message);
                Debug.LogError(exception.ToString());
            }
            Publish();
        }

        private bool IsCurrentActionLegal()
            => !string.IsNullOrEmpty(focus) && world != null && world.Player != null &&
                world.EvaluateInteraction(focus, world.Player.transform.position).primary;

        private void UpdateFocus()
        {
            if (world == null || world.Player == null || run.Paused || (run.Phase != GamePhase.Home && run.Phase != GamePhase.Market))
            { focus = ""; world?.SetFocus(""); return; }
            Vector3 bodyPosition = world.Player.transform.position;
            string[] targets;
            if(run.Phase==GamePhase.Market && !string.IsNullOrEmpty(run.HeldGrocery))
            {
                string shelf=GameCatalog.Find(run.HeldGrocery).anchor;
                targets=new[]{shelf,"ShoppingCart"};
            }
            else
            {
                targets = MarketTargets;
                if (run.Phase == GamePhase.Home) targets = run.HoldingStove ? HomeCarryTargets : run.StoveLoaded ? HomeLoadedTargets : HomeTargets;
                if(run.Phase==GamePhase.Home&&HasReadyOnlineParcels&&!run.HoldingStove&&string.IsNullOrEmpty(run.HeldGrocery))
                {var homeTargets=new List<string>(targets){"ParcelBox"};targets=homeTargets.ToArray();}
            }
            // Apply the same actionable-first ordering while carrying a box:
            // a nearer shelf outside its contact gate cannot hide a usable
            // basket. Do not retain a stale target over a legal alternative.
            string next = world.NearestAt(targets, bodyPosition);
            // Hysteresis: when the current focus is still usable and the new
            // pick is no better and not clearly closer, keep the current one
            // so the highlight does not flicker between two adjacent objects
            // on frames where their distances straddle a boundary.
            if (!string.IsNullOrEmpty(focus) && !string.IsNullOrEmpty(next) && focus != next)
            {
                InteractionCandidate current = world.EvaluateInteraction(focus, bodyPosition);
                InteractionCandidate challenger = world.EvaluateInteraction(next, bodyPosition);
                if (current.tier <= InteractionCandidate.TierInRangeButBlocked &&
                    challenger.tier >= current.tier &&
                    current.standDistance - challenger.standDistance < .35f)
                    next = focus;
            }
            focus = next;
            world.SetFocus(focus);
        }

        private void Publish()
        {
            if (run == null || holdSnapshot) return;
            var liveBusiness=(run.Phase==GamePhase.Business||run.Phase==GamePhase.DaySummary)?business?.Session:null;
            State = new Snapshot
            {
                phase = run.Phase, dayIndex=Campaign?.dayIndex??1, remaining = liveBusiness?.Remaining??run.Remaining, cash = (liveBusiness?.CashCents??run.CashCents) / 100f,
                cartCount = run.Cart.Count,cartCellsUsed=run.CartCellsUsed,cartCapacity=run.CartCapacity,careerName=run.Profile?.Career.name??"", cartTotal = run.CartTotalCents / 100f,
                paid = run.Paid, loaded = run.Loaded, heldItem = run.HoldingStove ? run.HeldEquipmentId : run.HeldGrocery,
                contextPrompt = Prompt(), actionBlockedReason=ActionBlockedReason(), canInteract = !run.Paused && !IsBusy && !string.IsNullOrEmpty(focus),
                primaryEnabled = !IsBusy && IsCurrentActionLegal(),
                checkoutInProgress = run.CheckoutInProgress, checkoutRemaining = run.CheckoutRemaining, checkoutDuration = run.CheckoutDuration,
                startTime = run.StartTime, itemIds = run.Cart.ToArray(), message = run.Message, pause = run.Paused,
                stoveLoaded = run.StoveLoaded, loadedCount = run.Cargo.Count, amountSpent = run.SpentCents / 100f,
                marketUsedSeconds = run.MarketUsed, targetId = focus,
                contextWorldPosition = world != null && !string.IsNullOrEmpty(focus) ? world.Anchor(focus) + Vector3.up * 1.45f : Vector3.zero,
                objective = Objective(), timeoutSettlement = run.TimeoutSettlement,
                busy=IsBusy, transitioning=IsTransitioning,
                pushingCart=BasketAtHand,
                canToggleCart=!run.Paused && !IsBusy && string.IsNullOrEmpty(run.HeldGrocery) && world?.ShoppingMotor!=null &&
                    (world.ShoppingMotor.IsAttached || world.ShoppingMotor.CanAttach),
                isRunning=world?.Player!=null && world.Player.IsRunning,
                cartPrompt=world?.ShoppingMotor==null?"":BasketAtHand?"F · 停放购物车":world.ShoppingMotor.CanAttach?"F · 接手购物车":"走近把手可接手购物车；空手按 Shift 跑步"
            };
            Changed?.Invoke(State);
        }

        private string ActionBlockedReason()
        {
            if(world?.Player==null||run==null||run.Paused||IsBusy||string.IsNullOrEmpty(focus))return "";
            InteractionCandidate candidate=world.EvaluateInteraction(focus,world.Player.transform.position);
            if(candidate.primary||!candidate.near)return "";
            switch(candidate.blockedBy)
            {
                case "basket-away":return "把购物车带到身边，再操作";
                case "wrong-side":return "走到货物的正面拿取";
                case "no-contact":case "contact-far":return "再靠近物品前沿一些";
                case "blocked-ray":case "envelope":return "前方有遮挡，换到空着的一侧";
            }
            if(run.HoldingStove)return "先把手中的家当放上摊车";
            if(!string.IsNullOrEmpty(run.HeldGrocery))
                return focus=="ShoppingCart"&&!run.CanFitCart(run.HeldGrocery)?"购物车放不下这箱，可先放回或整理已取商品":"先把手中货物放进购物车，或放回货架";
            if(run.Paid&&run.Cart.Count>0)return "这批货已付款，先到摊车边装货";
            if(focus=="Cashier"&&run.Cart.Count==0)return "购物车空着，先取想买的货";
            if(focus=="MarketCart"&&!run.Paid)return "未付款商品先到收银台勾选结账";
            if(focus=="MarketExit"&&run.Cart.Count>0)return "先处理购物车里的货，可只结账其中一部分";
            if(focus=="HomeCart"&&!run.HoldingStove)return "手中没有家当，可回屋拿取或走向出口";
            foreach(ItemDefinition item in GameCatalog.Items)if(item.anchor==focus)
            {
                bool repeatable=RunState.IsRepeatableSupply(item.id);
                if(!repeatable&&run.Cargo.Contains(item.id))return "这箱货已经装上摊车";
                if(!repeatable&&run.Cart.Contains(item.id)&&!BasketAtHand)return "这箱已在购物车中，把车带来可放回";
                if(!run.CanFitCart(item.id))return "购物车放不下这箱，可先结账装车或放回其他货";
                if(run.Remaining<=0)return "采购时间已到，先处理已取的货";
            }
            return "";
        }
        private string Prompt()
        {
            switch (focus)
            {
                case "ParcelBox": return "拆开网购包裹";
                case "SupplyCounter": return "账单直购 · 五金 / 包材 / 散称小份";
                case "RecoveryCounter": return "回收站 · 旧货代收";
                case "ShoppingCart": return "把手中这一箱放入购物车";
                case "StovePickup": return run.HoldingStove ? "把设备放回原位" : run.StoveLoaded ? "灶台已装车" : "抱起 "+CharacterCatalog.EquipmentName(run.ActiveHomeEquipment);
                case "HomeCart": return run.HoldingStove ? "装上车" : run.StoveLoaded ? "灶台已装车" : "先拿取屋内灶台";
                case "HomeExit": return "出发 · 前往惠民超市";
                case "Cashier": return run.CheckoutInProgress ? $"人工服务中 · {run.CheckoutRemaining:0.0}秒" : run.Paid ? "已付款" : $"人工收银 · ¥{run.CartTotalCents / 100f:0.00}";
                case "MarketCart": return run.Loaded ? "原料已装车" : run.Paid ? "装车" : "请先结账";
                case "MarketExit": return "准备出摊 · 查看结果";
            }
            foreach (ItemDefinition item in GameCatalog.Items)
                if (item.anchor == focus)
                {
                    // FREEDOM19: never tell the player a repeat consumable is
                    // "already loaded" or offer to "return this box" when E
                    // actually takes another unit; only durable hardware
                    // keeps the return/loaded wording.
                    bool repeatable=RunState.IsRepeatableSupply(item.id);
                    if(run.HeldGrocery==item.id||(!repeatable&&run.Cart.Contains(item.id)&&!run.Paid))return "放回这一箱";
                    if(run.Paid)return "已完成本次采购";
                    return repeatable&&run.Cart.Contains(item.id)?$"再拿一箱 · ¥{item.Price:0.00}":$"拿取一箱 · ¥{item.Price:0.00}";
                }
            return "";
        }

        private string Objective()
        {
            if (run.Phase == GamePhase.Error) return "资源导入错误";
            if (run.Phase == GamePhase.Complete) return "采购准备完成";
            if (run.Phase == GamePhase.Home) return run.StoveLoaded ? "走近门外出发点，前往超市" : run.HoldingStove ? "把手中的灶台搬到车边" : "走到设备前，抱起 "+CharacterCatalog.EquipmentName(run.ActiveHomeEquipment);
            if (!string.IsNullOrEmpty(run.HeldGrocery)) return "抱着原料走回购物车，靠近篮边按 E 放入";
            if (run.Loaded) return "原料已装车，走近出口准备出摊";
            if (run.CheckoutInProgress) return "人工收银服务中；离开柜台会中断排队";
            if (run.Paid) return "已付款待装车，走到室外摊车旁";
            return run.Cart.Count == 0 ? "货架整箱亲手搬 · 或到采购柜台账单直购" : "可继续取货，或走到人工收银处结账";
        }

        private IEnumerator SmokeRoutine()
        {
            yield return null;
            string report;
            int code = 0;
            try { report = GameplaySmokeChecks.RunAll(this); Debug.Log(report); }
            catch (Exception exception) { report = "GAMEPLAY_SMOKE_FAIL\n" + exception; Debug.LogError(report); code = 1; }
            string[] args = Environment.GetCommandLineArgs();
            int pathArg = Array.IndexOf(args, "--smoke-output");
            if (pathArg >= 0 && pathArg + 1 < args.Length)
            {
                string path = args[pathArg + 1];
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, report);
            }
            yield return null;
            Application.Quit(code);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
