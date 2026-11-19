using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Guochao
{
    [Serializable] public sealed class BusinessWorkAudit
    {
        public int serial,orderId,lane;public string action,status;
        public float queuedAt,startedAt,contactAt,finishedAt,pathMetres;
    }
    // FULFILMENT20: free cooking -> pack into hand -> place or hand to a fixed customer.
    // Counter meals are collected by customer actors independently of vendor work.
    // Imported legacy batches retain the old final packaging path without inventing inputs.
    public sealed partial class BusinessController : MonoBehaviour
    {
        public BusinessSession Session { get; private set; }
        public StreetStallStage Stage { get; private set; }
        public BusinessHUD HUD { get; private set; }
        public StallInteractionInput InputHandler { get; private set; }
        public bool CanContinue=>continueCallback!=null;
        public int SettlementPublishCount { get; private set; }
        public bool IsPaused=>capturingSave||GameFlow.Instance!=null&&GameFlow.Instance.Run.Paused;
        public int SelectedOrderId { get; private set; }=-1;
        // Tool focus may change while an independently selected order remains.
        // A pickup belongs to that order's recipe, never to the last tool focus.
        public int SelectedLaneIndex { get; private set; }=-1;
        public int SelectedOrderLaneIndex
        {
            get
            {
                StallOrder order=Session?.FindOrder(SelectedOrderId);if(order==null)return -1;
                if(order.lane>=0)return order.lane;if(order.reservedLane>=0)return order.reservedLane;
                foreach(KitchenLane lane in Session.Lanes)if(lane.recipeIndex==order.recipeIndex)return lane.index;return -1;
            }
        }
        public string InteractionHint { get; private set; }="";
        // PREP12：控制台/UIPresenter 用的一句短状态："" | "待敲蛋" | "待下米饭"。
        public string DescribeLaneEggPrep(int laneIndex)=>Session==null?"":Session.DescribeEggPrep(laneIndex);
        public bool CanInteract=>!disposed&&!IsPaused&&!opening&&Session!=null&&Session.Started&&!Session.Finished&&Stage!=null;
        private sealed class WorkIntent
        {
            public int id,lane,order=-1;public StallInteractionKind kind;public KitchenStep step;public string item="",label="",note="";
            public bool reservation,committed;public float queuedAt,startedAt=-1,contactAt=-1,pathStart;
            // FREEDOM19/COUNTER19：freeInput=逐项投料（同锅不同 SKU 可排，同 SKU 不重复）；
            // freeStart=开火烹制（等该锅所有先入队材料实际接触后才 dispatch）；counterMeal=
            // 出餐台交付（固定 meal id 与顾客，lane=-1 不绑定任何锅批次）。
            public bool freeInput,freeStart;public string counterMeal="";
            // COUNTER19：装盒上架入队时冻结批次/产品快照 id；批次被清或换成别批
            // 时意向取消，绝不把旧打包意图挪给新食物。
            public string batchId="",productId="";
            public FulfilmentWork fulfilment;public bool placeAfterPack;
        }
        private readonly List<WorkIntent> pendingWork=new List<WorkIntent>();
        private readonly List<BusinessWorkAudit> workHistory=new List<BusinessWorkAudit>();
        private WorkIntent activeWork;private bool positioning;private int positioningLane=-1,workSerial;
        public int PendingWorkCount=>pendingWork.Count;
        public string ActiveWorkLabel=>activeWork!=null?WorkLabel(activeWork):positioning?"移步到预约锅位":"";
        public string PendingWorkLabel { get {var labels=new List<string>();foreach(var work in pendingWork)labels.Add(WorkLabel(work));return string.Join(" → ",labels);} }
        public BusinessWorkAudit[] CaptureWorkHistory()=>workHistory.ToArray();
        public event Action InteractionChanged;
        private Action<GamePhase> phaseCallback;
        private Action<int> cashCallback;
        private Action returnCallback,continueCallback;
        private bool opening,settled,disposed,capturingSave,practiceRiskBeatArmed;
        private float openingElapsed;
        // RISK7SCENE：A01 假扫码逃单客运行期（EVENT7R 冻结模块）。Director 持有
        // Stage 真实顾客适配器；View 只做头顶 UI，两者都受暂停/收摊统一控制。
        public StallRiskDirector RiskDirector{get;private set;}
        public StallRiskView RiskView{get;private set;}
        private float simulationAccumulator;
        private float hintRemaining;
        private const float SimulationStep=1f/60f;
        private static int probeSerial;
        public static BusinessController Begin(BusinessSetup setup,Action<GamePhase> onPhase,Action<int> onFinishedCash,Action onReturnMarket,Action onContinue=null)
        {
            GameObject go=new GameObject("StreetBusinessController");BusinessController controller=go.AddComponent<BusinessController>();
            controller.phaseCallback=onPhase;controller.cashCallback=onFinishedCash;controller.returnCallback=onReturnMarket;controller.continueCallback=onContinue;
            controller.Session=new BusinessSession(setup);controller.Stage=go.AddComponent<StreetStallStage>();controller.Stage.Initialize(setup,controller.LaneAction);
            controller.Stage.WorkFailed+=controller.OnWorkFailed;controller.Stage.CustomerArrived+=id=>controller.Session.MarkCustomerArrived(id);
            controller.HUD=go.AddComponent<BusinessHUD>();controller.HUD.Initialize(controller,controller.Session);
            controller.InputHandler=go.AddComponent<StallInteractionInput>();controller.InputHandler.Initialize(controller);
            controller.CreateRiskRuntime();controller.Session.RiskSalePending+=controller.OnRiskSalePending;
            onPhase?.Invoke(GamePhase.SiteSelection);return controller;
        }
        public static BusinessController BeginRestored(BusinessSaveData data,Action<GamePhase> onPhase,Action<int> onFinishedCash,Action onReturnMarket,Action onContinue=null)
        {
            // Validate the entire model before allocating a scene or publishing a callback.
            BusinessSession restored=BusinessSession.Restore(data);
            if(float.IsNaN(data.simulationAccumulator)||float.IsInfinity(data.simulationAccumulator)||data.simulationAccumulator<0||data.simulationAccumulator>=SimulationStep+.00001f)
                throw new ArgumentException("Invalid saved business simulation remainder");
            GameObject go=new GameObject("StreetBusinessController_Restored");BusinessController controller=go.AddComponent<BusinessController>();
            try
            {
                controller.phaseCallback=onPhase;controller.cashCallback=onFinishedCash;controller.returnCallback=onReturnMarket;controller.continueCallback=onContinue;
                controller.Session=restored;controller.settled=data.settlementPublished;controller.SettlementPublishCount=data.settlementPublications;controller.simulationAccumulator=data.simulationAccumulator;
                StallOrder selected=restored.FindOrder(data.selectedOrderId);controller.SelectedOrderId=selected!=null&&!selected.served&&!selected.departed?selected.id:-1;
                controller.SelectedLaneIndex=data.selectedLaneIndex>=0&&data.selectedLaneIndex<restored.Lanes.Length?data.selectedLaneIndex:-1;
                controller.Stage=go.AddComponent<StreetStallStage>();controller.Stage.Initialize(restored.Setup,controller.LaneAction);controller.Stage.RestoreSession(restored,data.presentation);
                controller.Stage.WorkFailed+=controller.OnWorkFailed;controller.Stage.CustomerArrived+=id=>controller.Session.MarkCustomerArrived(id);
                controller.HUD=go.AddComponent<BusinessHUD>();controller.HUD.Initialize(controller,restored);
                controller.InputHandler=go.AddComponent<StallInteractionInput>();controller.InputHandler.Initialize(controller);
                controller.CreateRiskRuntime();controller.Session.RiskSalePending+=controller.OnRiskSalePending;
                // RISK7SCENE：读档后尚有未决债权 → 重建已 served 顾客适配器并挂回头顶 UI，
                // 同时只恢复拿餐后的静态表现（包在手上），不补播接餐动作、不重复给钱。
                var restoredPending=restored.RiskState!=null?restored.RiskState.ActiveEvent:null;
                if(restoredPending!=null&&!restoredPending.Resolved&&controller.RiskDirector.Attach(restoredPending))
                {
                    var adapter=controller.Stage!=null?controller.Stage.LastRiskActor:null;
                    if(adapter!=null&&controller.RiskView.PresentExistingActor(restoredPending,adapter))
                        controller.Stage.RestoreGuestReceive(restoredPending.OrderId);
                }
                if(restored.Finished)
                {
                    if(!controller.settled)controller.PublishSettlement();
                    else{controller.HUD.ShowSummary();onPhase?.Invoke(GamePhase.DaySummary);}
                }
                else if(restored.Started){controller.HUD.ShowBusiness();onPhase?.Invoke(GamePhase.Business);}
                else onPhase?.Invoke(GamePhase.SiteSelection);
                controller.HUD.Refresh(controller.IsPaused);return controller;
            }
            catch{controller.Cancel();throw;}
        }
        public BusinessSaveData CaptureForSave()
        {
            if(disposed||Session==null)throw new InvalidOperationException("Cannot save a disposed business controller");
            if(capturingSave)throw new InvalidOperationException("Business save capture cannot reenter itself");
            capturingSave=true;
            try
            {
                // This synchronous critical section advances no simulation and
                // rejects input even when called by an automatic checkpoint.
                StopAllCoroutines();opening=false;HUD?.SetCurtain(0);
                ClearCustomerPickups();ClearPendingWork();
                if(activeWork!=null){RecordWork(activeWork,activeWork.committed?"saved_after_contact":"saved_cancelled_before_contact");activeWork=null;}
                positioning=false;Stage?.CancelWork();Session.ReleaseAllReservations();
                BusinessSaveData data=Session.CaptureSave();data.settlementPublished=settled;data.settlementPublications=SettlementPublishCount;
                data.selectedOrderId=SelectedOrderId;data.selectedLaneIndex=SelectedLaneIndex;data.simulationAccumulator=simulationAccumulator;
                data.presentation=Stage?.CaptureSavePresentation();InteractionChanged?.Invoke();return data;
            }
            finally{capturingSave=false;}
        }
        private void PublishSettlement()
        {
            if(settled||!Session.Finished)return;
            // RISK7SCENE：日结发布前先核销未决债权（Session.Close → ResolvePendingRiskOnClose
            // → 本方法）。保存/暂停本身不记损失，真正收摊才按坏账一次性核销。
            Session.ResolvePendingRiskOnClose();
            settled=true;ClearCustomerPickups();ClearPendingWork();if(activeWork!=null){RecordWork(activeWork,"closing");activeWork=null;}Stage.CancelWork();
            SettlementPublishCount++;cashCallback?.Invoke(Session.CashCents);phaseCallback?.Invoke(GamePhase.DaySummary);HUD?.ShowSummary();
        }
        private void Update(){AdvanceSimulation(Time.unscaledDeltaTime,IsPaused);}
        // The live Update and deterministic full-stage replay share one clock.
        public void AdvanceSimulation(float deltaTime,bool paused=false)
        {
            if(Session==null)return;paused|=IsPaused;
            if(Session.Started&&!Session.Finished&&!opening&&!paused)
            {
                simulationAccumulator+=Mathf.Max(0,deltaTime);
                while(simulationAccumulator+0.000001f>=SimulationStep&&!Session.Finished)
                {
                    // A contact and its heat deadline share the same fixed
                    // clock. Advancing all heat before any actor motion would
                    // burn a timely action whenever one rendered frame stalls.
                    Session.Tick(SimulationStep);simulationAccumulator=Mathf.Max(0,simulationAccumulator-SimulationStep);
                    TickCustomerPickups();
                    RiskDirector?.Tick(SimulationStep); // 拦截窗口/退场/ICD/轮询与营业同钟，paused 天然冻结
                    if(!Session.Finished)PumpWork();
                    Stage.Present(Session,SimulationStep,Session.Finished);
                }
                if(Session.Finished)simulationAccumulator=0;
            }
            else Stage.Present(Session,deltaTime,paused||opening||Session.Finished);
            if(!paused&&hintRemaining>0)
            {hintRemaining-=deltaTime;if(hintRemaining<=0){InteractionHint="";InteractionChanged?.Invoke();}}
            TickOpening(deltaTime,paused);
            if(!paused)RiskView?.Tick(deltaTime); // 头顶倒计时/浮字随真实帧推进，暂停冻结
            if(SelectedOrderId>=0)
            {
                StallOrder selected=Session.FindOrder(SelectedOrderId);
                if(selected==null||selected.departed||selected.served)
                {SelectedOrderId=-1;InteractionChanged?.Invoke();}
            }
            TickCounterSelection();
            HUD?.Refresh(paused);
            if(Session.Finished&&!settled)PublishSettlement();
        }
        public void SelectDistrict(int index){if(disposed||IsPaused||opening||Session.Started)return;Session.SelectDistrict(index);HUD.ShowSelection();}
        // MENUFLEX10：开摊前沿用选菜页（ShowSelection）；营业中本入口只增减后续订单
        // ——绝不 ShowSelection/重建现场，只触发 InteractionChanged/Refresh 刷新现有 HUD。
        // 普通暂停/收摊仍拒绝（IsPaused 与 Finished 守卫不变）。
        public void ToggleRecipe(int index)
        {
            if(disposed||IsPaused||opening||Session==null||Session.Finished)return;
            bool started=Session.Started;
            Session.ToggleRecipe(index);
            if(!started)HUD.ShowSelection();
            else{InteractionChanged?.Invoke();HUD?.Refresh(IsPaused);}
        }
        // 营业管理页（含暂停态）专用授权入口：只改菜单/后续订单，不动现单、锅内
        // 批次与待办；普通游玩输入仍走上面的 ToggleRecipe（暂停时被拒）。
        public bool ToggleRecipeManaged(int index)
        {
            if(disposed||opening||Session==null||!Session.Started||Session.Finished)return false;
            bool ok=Session.ToggleRecipe(index);
            InteractionChanged?.Invoke();HUD?.Refresh(IsPaused);
            return ok;
        }
        public void Open()
        {
            if(disposed||IsPaused||opening||Session.Started||Session.Finished)return;
            if(!Session.Open()){HUD.ShowSelection();return;}
            Stage.Configure(Session);HUD.ShowBusiness();phaseCallback?.Invoke(GamePhase.Business);
            // 开幕帘与营业共用同一推进时钟（原 StartCoroutine 在非播放上下文不推进，
            // 会让 opening 永远为真、CanInteract 恒假；行为在真实游玩中不变）。
            opening=true;openingElapsed=0;HUD.SetCurtain(1);
        }
        private void TickOpening(float deltaTime,bool paused)
        {
            if(!opening||paused)return;
            openingElapsed+=Mathf.Max(0,deltaTime);float t=Mathf.Clamp01(openingElapsed/.5f);
            HUD.SetCurtain(1-Mathf.SmoothStep(0,1,t));
            if(openingElapsed>=.5f){HUD.SetCurtain(0);opening=false;}
        }
        public void ShowInteractionHint(string value)
        {InteractionHint=value;hintRemaining=3.2f;InteractionChanged?.Invoke();}
        private void OnWorkFailed(string reason){ShowInteractionHint("操作没有完成 · 钱货没有重复扣除，请重新选择物件");}
        private bool InteractionRejected(string message){ShowInteractionHint(message);return false;}
        // COUNTER19 兼容入口（旧档/旧 UI）：只移动工具焦点，绝不代表"已选顾客"。
        // 新链路自由投料/装盒/递餐全部不要求本方法先行。
        public bool SelectOrder(int id)
        {
            if(!CanInteract)return false;
            StallOrder order=Session.FindOrder(id);
            if(order==null||order.departed||order.served)return InteractionRejected("这份订单已经结束");
            if(!order.arrived)return InteractionRejected("顾客正在走近，站稳后再点单");
            SelectedOrderId=id;SelectedLaneIndex=order.lane>=0?order.lane:order.reservedLane;
            if(SelectedLaneIndex<0)foreach(KitchenLane lane in Session.Lanes)if(lane.recipeIndex==order.recipeIndex){SelectedLaneIndex=lane.index;break;}
            bool cooked=SelectedLaneIndex>=0&&Session.Lanes[SelectedLaneIndex].step==KitchenStep.ReadyStock;
            ShowInteractionHint(order.reservedLane>=0?"这份订单已安排取料":order.lane>=0?"已选中这份订单 · 可安排下一步":cooked?"已选单 · 点熟食锅盛出余份":"已选单 · 点对应食材安排下料");return true;
        }
        // 两种入口共用：有已到位选中订单走订单投料（reserve，BeginOrder）；
        // 没有选中订单则找一口空闲、配方含这盒食材的锅做无订单备餐
        // （BeginBatch，同样只在 Actor 接触帧扣一次整批）。
        public bool UseIngredient(string itemId)
        {
            if(!CanInteract||!KitchenHandsAvailable())return false;
            // Only an unfinished imported egg/rice preparation retains its old second contact.
            if(SelectedLaneIndex>=0&&SelectedLaneIndex<Session.Lanes.Length&&Session.LaneNeedsRice(SelectedLaneIndex))
                return UseIngredientAtLane(SelectedLaneIndex,itemId);
            string reason=null;
            return TryFreeIngredient(itemId,-1,ref reason)||InteractionRejected(reason??"没有能下这种料的锅 · 先点一口空锅");
        }
        // PREP12：拖拽实物到指定锅位=与点选相同的生产接触入口（同一 WorkIntent/
        // Actor 接触帧，扣货只在接触帧发生）。拖错工位只给提示，绝不静默改投别锅；
        // 拖起后未落到锅位直接释放=放回，不扣任何库存。
        public bool UseIngredientAtLane(int lane,string itemId)
        {
            if(!CanInteract||!KitchenHandsAvailable())return false;
            if(string.IsNullOrEmpty(itemId)||lane<0||lane>=Session.Lanes.Length)return InteractionRejected("这里没有对应的下锅工位");
            KitchenLane target=Session.Lanes[lane];
            // FREEDOM19：空锅（含首料正在搬运）直接走自由逐项投料——IsFreeLane 只
            // 在已有 freeBatch 时为真，空锅不能用它当门槛。新批绝不绕回整 BOM。
            if(Session.IsFreeLane(lane)||target.step==KitchenStep.Idle&&!Session.LaneNeedsRice(lane))
            {
                string reason=null;
                if(TryFreeIngredientAtLane(lane,itemId,ref reason))return true;
                return InteractionRejected(reason??"这口锅现在下不了这种料");
            }
            if(LaneHasQueuedWork(lane))return InteractionRejected("这口锅正在操作 · 等这一步完成");
            if(!BusinessSession.Recipes[target.recipeIndex].ingredients.ContainsKey(itemId))return InteractionRejected("这口锅不做用这盒食材的菜 · 换个工位");
            // PREP12：两拍现炒按真实顺序投料——空锅先敲蛋，锅里有蛋液只收米面。
            string nextPrep=Session.NextPrepIngredient(lane);
            if(nextPrep!=""&&itemId!=nextPrep)return InteractionRejected(nextPrep=="egg"?"这道现炒先敲蛋下锅，再下米面":"蛋液已在锅里 · 现在需要下米面");
            // PREP12：无订单绑定的待下米饭锅，任何同菜订单拖来米面都推进这锅备餐。
            if(Session.LaneNeedsRice(lane)&&target.orderId<0)
                return Enqueue(new WorkIntent{kind=StallInteractionKind.Ingredient,lane=lane,order=-1,item=itemId,step=KitchenStep.Idle,label="下米面开炒"},false);
            if(target.step==KitchenStep.ReadyStock)return InteractionRejected("锅里已有熟食 · 点这口锅盛出");
            if(target.step==KitchenStep.Dirty)return InteractionRejected((lane+1)+"号锅糊了 · 点这口锅清理");
            if(target.step!=KitchenStep.Idle||target.reservedOrderId>=0)return InteractionRejected("这口锅已有制作安排");
            if(nextPrep==""&&Session.Missing(target.recipeIndex)!="")return InteractionRejected("暂时缺少："+Session.Missing(target.recipeIndex));
            // 同菜已到位选中订单则绑定该订单投料（与点选入口同 reserve 语义），
            // 否则就是无订单备餐下料；两种情况都只在 Actor 接触帧扣一次整批。
            StallOrder order=Session.FindOrder(SelectedOrderId);
            bool bound=order!=null&&order.arrived&&!order.departed&&!order.served&&order.recipeIndex==target.recipeIndex
                &&(order.lane==lane||order.reservedLane==lane||order.lane<0&&order.reservedLane<0);
            string prepLabel=nextPrep=="egg"?"敲蛋下锅":nextPrep!=""?"下米面开炒":"取料下锅";
            SelectedLaneIndex=lane;
            return Enqueue(new WorkIntent{kind=StallInteractionKind.Ingredient,lane=lane,order=bound?order.id:-1,item=itemId,step=KitchenStep.Idle,label=prepLabel},bound);
        }
        // 无订单备餐：点食材下锅。选第一口"配方含该食材、Idle、无预留"的锅。
        private bool UseIngredientBatch(string itemId)
        {
            int dirtyLane=-1;bool eggPrepMismatch=false;
            for(int i=0;i<Session.Lanes.Length;i++)
            {
                KitchenLane lane=Session.Lanes[i];
                if(!BusinessSession.Recipes[lane.recipeIndex].ingredients.ContainsKey(itemId))continue;
                if(lane.step==KitchenStep.Dirty){dirtyLane=i;continue;}
                if(lane.step!=KitchenStep.Idle||lane.reservedOrderId>=0)continue;
                // PREP12：两拍现炒的空锅先敲蛋、有蛋液只收米面；不符记住并给出可读提示。
                string nextPrep=Session.NextPrepIngredient(i);
                if(nextPrep!=""&&itemId!=nextPrep){eggPrepMismatch=true;continue;}
                string missing=Session.Missing(lane.recipeIndex);if(nextPrep==""&&missing!="")return InteractionRejected("暂时缺少："+missing);
                SelectedLaneIndex=i;
                string prepLabel=nextPrep=="egg"?"敲蛋下锅":nextPrep!=""?"下米面开炒":"备餐下料";
                return Enqueue(new WorkIntent{kind=StallInteractionKind.Ingredient,lane=i,order=-1,item=itemId,step=KitchenStep.Idle,label=prepLabel},false);
            }
            // MENUFLEX10：没有任何一口锅在做含这盒食材的菜 → 按菜单找一口可换菜的
            // 空锅换过去备餐（同订单路径的冻结条件；库存由开工接触帧复验，排队时
            // 先查整锅缺失，避免排一个注定失败的取料）。
            foreach(int recipeIndex in Session.SelectedRecipes)
            {
                if(!BusinessSession.Recipes[recipeIndex].ingredients.ContainsKey(itemId))continue;
                // PREP12：两拍现炒的空锅第一拍只收蛋，米面不能隔空下锅。
                if(BusinessSession.RequiresEggCrack(recipeIndex)&&itemId!="egg"){eggPrepMismatch=true;continue;}
                if(Session.Missing(recipeIndex)!="")continue;
                int lane=Session.FindSwitchableIdleLane(recipeIndex);
                if(lane<0||LaneHasQueuedWork(lane))continue;
                if(!Session.SwitchLaneRecipe(lane,recipeIndex))continue;
                Stage?.RebuildLaneFood(lane,Session);SelectedLaneIndex=lane;
                return Enqueue(new WorkIntent{kind=StallInteractionKind.Ingredient,lane=lane,order=-1,item=itemId,step=KitchenStep.Idle,label="备餐下料"},false);
            }
            if(dirtyLane>=0)return InteractionRejected((dirtyLane+1)+"号锅糊了 · 点这口锅清理");
            if(eggPrepMismatch)return InteractionRejected("蛋炒饭先敲蛋下锅，再下米面");
            return InteractionRejected("没有适配这盒食材的空闲锅位 · 点空锅看看或等锅位腾空");
        }
        // MENUFLEX10：选中的真实订单没有对应锅位时，借一口真正空闲、无排队的锅
        // 换菜（Session 侧再验冻结条件/设备资格/菜单或现单约束）。换菜后重建该锅
        // 食物造型；借不到返回 -1，调用方按原拒绝文案处理。排队工作（含预约）
        // 在本侧冻结：绝不让已排队的接触帧落到换菜后的锅上。
        private int TryBorrowIdleLane(int recipeIndex)
        {
            int lane=Session.FindSwitchableIdleLane(recipeIndex);
            if(lane<0||LaneHasQueuedWork(lane))return -1;
            if(!Session.SwitchLaneRecipe(lane,recipeIndex))return -1;
            Stage?.RebuildLaneFood(lane,Session);
            ShowInteractionHint((lane+1)+"号锅换做 "+BusinessSession.Recipes[recipeIndex].name);
            return lane;
        }
        private bool LaneHasQueuedWork(int lane)
        {
            if(activeWork!=null&&activeWork.lane==lane)return true;
            foreach(WorkIntent work in pendingWork)if(work.lane==lane)return true;
            return false;
        }
        public bool UseCookware(int laneIndex)
        {
            if(!CanInteract||!KitchenHandsAvailable()||laneIndex<0||laneIndex>=Session.Lanes.Length)return false;
            KitchenLane lane=Session.Lanes[laneIndex];SelectedLaneIndex=laneIndex;
            // PREP12：锅里有蛋液时锅具点击只提示下米面，不排火候/盛出等动作。
            if(Session.LaneNeedsRice(laneIndex))return InteractionRejected("蛋液已在锅里 · 点米面下锅再翻炒");
            // FREEDOM19：自由批次、或无绑定订单的空锅（含首料搬运中）一律走自由链；
            // 绝不自动抓某标准配方第一料。旧档制作中的 legacy 锅维持原路径。
            if(lane.freeBatch!=null||lane.step==KitchenStep.Idle&&!TryGetLaneOrder(lane,out _))
                return UseCookwareFree(laneIndex);
            if(lane.step==KitchenStep.ReadyStock)
            {
                StallOrder order=Session.FindOrder(SelectedOrderId);
                if(order!=null&&order.arrived&&!order.departed&&!order.served&&order.lane<0&&order.reservedLane<0&&order.recipeIndex==lane.recipeIndex)
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=order.id,step=KitchenStep.ReadyStock,label="盛出熟食"},true);
                // 无订单备餐份：不绑定顾客直接盛出，后续调味/封装/售出再走
                // 自动匹配；批次守恒不变（盛出不重复扣料）。
                if(lane.batchRemaining>0)
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.ReadyStock,label="盛出熟食"},false);
                return InteractionRejected("先选一份同菜订单，再点锅盛出");
            }
            if(lane.step==KitchenStep.Idle)
            {
                // FREEDOM19：空锅不再自动下料，一律提示先投料（自由链路在
                // UseCookwareFree 处理；此处只剩有预约订单的空锅，交给下方火候路径）。
                if(!TryGetLaneOrder(lane,out _))return UseCookwareFree(laneIndex);
            }
            if(lane.step==KitchenStep.Season)return InteractionRejected(lane.remaining>0?"正在盛装 / 沥油":"点调味瓶安排调味");
            if(lane.step==KitchenStep.Pack)return InteractionRejected("将熟菜拖入对应包材槽；也可点包材再点锅");
            if(lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.Prep&&lane.step!=KitchenStep.Heating&&lane.step!=KitchenStep.Utensil&&lane.step!=KitchenStep.Dirty)return false;
            if(lane.step==KitchenStep.Dirty)return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=lane.orderId,step=KitchenStep.Dirty,label="清理锅位"},false);
            if(!Session.RequiresManualHeatAction(laneIndex))return InteractionRejected("这口锅自动恒温完成，无需手动火候");
            // An Idle pan here already passed TryGetLaneOrder: the appointment
            // belongs to the order whose real pickup walk is still underway.
            return Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=lane.orderId>=0?lane.orderId:lane.reservedOrderId,step=KitchenStep.Utensil,label=BusinessSession.Recipes[lane.recipeIndex].toolLabel},false);
        }
        // The order a lane's future chain belongs to: the physically bound
        // order after its pickup contact, or the explicitly reserved order
        // still waiting for that real pickup walk. SelectedOrderId may move
        // on; an accepted appointment never follows it.
        private bool TryGetLaneOrder(KitchenLane lane,out StallOrder order)
        {
            order=null;int id=lane.orderId>=0?lane.orderId:lane.reservedOrderId;
            if(id<0)return false;order=Session.FindOrder(id);
            if(order==null||!order.arrived||order.served||order.departed||order.recipeIndex!=lane.recipeIndex)return false;
            if(lane.orderId==id)return order.lane==lane.index;
            if(order.reservedLane!=lane.index||lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.ReadyStock)return false;
            // A future step must have a real pickup/scoop intention to follow,
            // not merely an orphaned model reservation.
            if(activeWork!=null&&activeWork.reservation&&!activeWork.committed&&activeWork.order==id&&activeWork.lane==lane.index)return true;
            foreach(WorkIntent work in pendingWork)if(work.reservation&&work.order==id&&work.lane==lane.index)return true;
            return false;
        }
        public bool UseSeasoning(int laneIndex=-1)=>UseSeasoningCore(laneIndex,"seasoning","调味");
        // FLAVOR11 真实选瓶：指定风味（S01/S02/S05）才走本重载；原味仍走
        // UseSeasoning(int) 一步点击，不替用户猜口味。包材自动补的前置调味
        // 也保持原味（ChainPackPrereqs 不传风味）。风味份数只在递餐帧一次性扣。
        public bool UseSeasoning(int laneIndex,string flavorId)
        {
            if(!CanInteract||!KitchenHandsAvailable())return false;
            if(!StallFlavorMenu.IsFlavor(flavorId))return InteractionRejected("没有这种风味调料");
            if(laneIndex<0)laneIndex=SelectedLaneIndex;
            if(laneIndex<0||laneIndex>=Session.Lanes.Length)return InteractionRejected("先选中需要调味的锅");
            KitchenLane target=Session.Lanes[laneIndex];
            // FREEDOM19：自由批风味兼容由 Session.Action(lane,flavor) 在接触帧
            // 按真实做法/产品家族校验，Controller 不用旧 recipeIndex 预拒。
            if(!Session.IsFreeLane(laneIndex)&&!Session.CanUseFlavor(target.recipeIndex,flavorId))return InteractionRejected(StallFlavorMenu.Find(flavorId).name+"不适合"+BusinessSession.Recipes[target.recipeIndex].name);
            if(Session.Count(flavorId)<=0)return InteractionRejected(StallFlavorMenu.Find(flavorId).name+"已经用完 · 先补货");
            return UseSeasoningCore(laneIndex,flavorId,"加"+StallFlavorMenu.Find(flavorId).name);
        }
        private bool UseSeasoningCore(int laneIndex,string item,string label)
        {
            if(!CanInteract||!KitchenHandsAvailable())return false;if(laneIndex<0)laneIndex=SelectedLaneIndex;
            if(laneIndex<0||laneIndex>=Session.Lanes.Length)return InteractionRejected("先选中需要调味的锅");
            KitchenLane lane=Session.Lanes[laneIndex];
            // An explicit click may book this step for the order the pan already
            // belongs to — bound after pickup, or reserved while the real
            // pickup walk is still underway. It executes only when the pan
            // genuinely reaches a drained Season step for that same order.
            // Season completes without stock (A1 contract); Serve charges per
            // portion and records the integrity cost. Warn at queue time instead
            // of implying a fully seasoned dish.
            string note=Session.SeasoningAvailable?"":Session.SoftMissing(lane.recipeIndex);
            // 无订单批次同样可预约调味：食物在锅里就合法，卖出时在 Serve 逐份
            // 扣油盐；不因顾客未到位而拒绝。
            if(!TryGetLaneOrder(lane,out StallOrder order))
            {
                // FREEDOM19：自由批同样可预约调味（兼容由接触帧 Session.Action 复验）。
                if(Session.IsFreeLane(laneIndex)&&lane.freeBatch!=null&&lane.step!=KitchenStep.Dirty)
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Seasoning,lane=laneIndex,order=-1,item=item,step=KitchenStep.Season,label=label,note=note},false);
                if(lane.batchRemaining>0&&lane.step!=KitchenStep.Dirty&&lane.step!=KitchenStep.Pack&&!(lane.step==KitchenStep.ReadyStock&&lane.seasoned))
                    return Enqueue(new WorkIntent{kind=StallInteractionKind.Seasoning,lane=laneIndex,order=-1,item=item,step=KitchenStep.Season,label=label,note=note},false);
                return InteractionRejected("这口锅还没有批次 · 点空锅或对应食材先下锅");
            }
            if(lane.step==KitchenStep.Pack||lane.step==KitchenStep.Dirty||lane.step==KitchenStep.ReadyStock&&lane.seasoned)return InteractionRejected("这口锅不需要再次调味");
            return Enqueue(new WorkIntent{kind=StallInteractionKind.Seasoning,lane=laneIndex,order=order.id,item=item,step=KitchenStep.Season,label=label,note=note},false);
        }
        public bool UsePackage(int laneIndex,string itemId)
        {
            if(Session!=null&&laneIndex>=0&&laneIndex<Session.Lanes.Length&&Session.Lanes[laneIndex].freeBatch==null)
                return UseLegacyPackage(laneIndex,itemId); // Imported unfinished legacy batch only.
            return PackMeal(laneIndex,itemId);
        }
        private bool UseLegacyPackage(int laneIndex,string itemId)
        {
            if(!CanInteract||!KitchenHandsAvailable()||laneIndex<0||laneIndex>=Session.Lanes.Length)return false;
            KitchenLane lane=Session.Lanes[laneIndex];
            // Like seasoning, an explicit package click may be booked anywhere
            // on the same order's chain — reserved, prepping, heating, green
            // window, or already seasoned; stock and the sale are settled by
            // the single Serve commit, so nothing is debited up front.
            // 无订单批次同样可预约封装：包材与成交都在 Serve 接触帧一次性结清，
            // 排队本身零扣减；顾客未到位时到时再提示，食物留在锅里。
            // PACK9R：玩家明确拿包材点熟锅（或把熟锅拖进适配包材）时，把缺的
            // 合法前置（盛出 / 调味）静默补进同一口锅的待办，再排封装。只在
            // 锅里确有熟食时补（空锅绝不一键走完整产线）；已有同步骤待办不重复、
            // 已 Seasoned 跳过调味；每项仍是独立真实动作，在各自 Actor 接触帧
            // 一次性 commit，无额外点击、无重复动画、无二次扣减。
            if(!TryGetLaneOrder(lane,out StallOrder order))
            {
                if(lane.batchRemaining>0&&lane.step!=KitchenStep.Dirty)
                {
                    if(Array.IndexOf(BusinessSession.Recipes[lane.recipeIndex].packages,itemId)<0)return InteractionRejected("这份菜需要其他包材");
                    if(Session.Count(itemId)<=0)return InteractionRejected("这种包材已用完");
                    SelectedLaneIndex=laneIndex;
                    ChainPackPrereqs(laneIndex,-1);
                    bool queued=Enqueue(new WorkIntent{kind=StallInteractionKind.Packaging,lane=laneIndex,order=-1,item=itemId,step=KitchenStep.Pack,label="封装出餐"},false);
                    // 暂无顾客：Pack 意向留在队列等单（WorkReady 在接触帧前再校验），
                    // 食物留在锅里，绝不假扣包材/收款。
                    if(queued&&Session.FindWaitingOrder(lane.recipeIndex)==null)
                        ShowInteractionHint((laneIndex+1)+"号锅已安排封装出餐 · 前置就绪后自动操作 · 暂无等这份菜的顾客，顾客到位后自动完成，食物留在锅里");
                    return queued;
                }
                return InteractionRejected("这口锅没有待出餐的熟食 · 点空锅或食材先下锅");
            }
            if(lane.step==KitchenStep.Dirty)return InteractionRejected("这口锅还没到封装步骤");
            if(Array.IndexOf(BusinessSession.Recipes[lane.recipeIndex].packages,itemId)<0)return InteractionRejected("这份菜需要其他包材");
            if(Session.Count(itemId)<=0)return InteractionRejected("这种包材已用完");
            SelectedLaneIndex=laneIndex;ChainPackPrereqs(laneIndex,order.id);
            return Enqueue(new WorkIntent{kind=StallInteractionKind.Packaging,lane=laneIndex,order=order.id,item=itemId,step=KitchenStep.Pack,label="封装出餐"},false);
        }

        // 仅补"缺的"前置：盛出只在 ReadyStock 且有批次时补（点锅盛出的真实
        // 入口本来就用 order=-1 的 ScoopBatch，绑定订单不受影响）；调味只在未
        // seasoned 且无同锅调味待办时补。HasPendingIntent 去重，重复入口安全。
        private void ChainPackPrereqs(int laneIndex,int orderId)
        {
            KitchenLane lane=Session.Lanes[laneIndex];
            if(lane.step==KitchenStep.ReadyStock&&lane.batchRemaining>0&&!HasScoopIntent(laneIndex))
                Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.ReadyStock,label="盛出熟食"},false);
            if(!lane.seasoned&&lane.step!=KitchenStep.Pack&&lane.step!=KitchenStep.Dirty&&!HasPendingIntent(StallInteractionKind.Seasoning,laneIndex))
                Enqueue(new WorkIntent{kind=StallInteractionKind.Seasoning,lane=laneIndex,order=orderId,item="seasoning",step=KitchenStep.Season,label="调味"},false);
        }
        // 步骤感知的盛出判重：只认真正预约/执行中的「盛出熟食」（Cookware +
        // ReadyStock）。翻面收尾帧 activeWork 仍是 Cookware 种类的旧热动作，
        // HasPendingIntent 按 kind+lane 判重会把它误当已预约盛出而漏补。
        public bool HasScoopIntent(int lane)
        {if(activeWork!=null&&activeWork.kind==StallInteractionKind.Cookware&&activeWork.step==KitchenStep.ReadyStock&&activeWork.lane==lane)return true;foreach(WorkIntent work in pendingWork)if(work.kind==StallInteractionKind.Cookware&&work.step==KitchenStep.ReadyStock&&work.lane==lane)return true;return false;}
        // PACK9R 根因修复：只要同锅仍有有效封装预约且锅确实在 ReadyStock 有熟食，
        // 而没有任何同锅真实盛出动作/待办（步骤感知，旧热动作不算），就在统一
        // 调度里补上确实缺失的盛出。借用真实 Enqueue 路径（同 HasPendingIntent
        // 去重、每锅 4 项容量、同一待办列表），不直接改集合、不递归 PumpWork。
        private void EnsurePackChainScoop()
        {
            for(int laneIndex=0;laneIndex<Session.Lanes.Length;laneIndex++)
            {
                KitchenLane lane=Session.Lanes[laneIndex];
                if(lane.step!=KitchenStep.ReadyStock||lane.batchRemaining<=0)continue;
                bool packPending=false;
                foreach(WorkIntent work in pendingWork)if(work.lane==laneIndex&&(work.kind==StallInteractionKind.Packaging||work.fulfilment==FulfilmentWork.Pack&&!lane.seasoned)){packPending=true;break;}
                if(!packPending||HasScoopIntent(laneIndex))continue;
                Enqueue(new WorkIntent{kind=StallInteractionKind.Cookware,lane=laneIndex,order=-1,step=KitchenStep.ReadyStock,label="盛出熟食"},false);
            }
        }
        public bool HasPendingIntent(StallInteractionKind kind,int lane)
        {if(activeWork!=null&&activeWork.kind==kind&&activeWork.lane==lane)return true;foreach(WorkIntent work in pendingWork)if(work.kind==kind&&work.lane==lane)return true;return false;}
        public void CancelPendingWork()
        {
            if(IsPaused||disposed)return;ClearCustomerPickups();ClearPendingWork();ShowInteractionHint("已取消待办，正在接触的动作照常收回");
        }
        private void ClearPendingWork()
        {
            foreach(WorkIntent work in pendingWork){if(work.reservation)Session.ReleaseOrderReservation(work.order);RecordWork(work,"cancelled_pending");}
            pendingWork.Clear();if(positioning){Stage.CancelWork();positioning=false;}InteractionChanged?.Invoke();
        }
        // RISK7SCENE：双锅共用全局 3 项太小。改为每口锅 4 项的预约容量
        // （总数随锅数），去重规则不变（HasPendingIntent 按 kind+lane 判重）。
        public int PendingWorkCapacity=>Mathf.Max(1,Session!=null&&Session.Lanes!=null?Session.Lanes.Length:1)*4;
        private bool Enqueue(WorkIntent work,bool reserve)
        {
            if(work.fulfilment==FulfilmentWork.None&&!KitchenHandsAvailable())return false;
            if(HasPendingIntent(work.kind,work.lane))return InteractionRejected("这一步已在待办 · 到时自动操作");
            if(work.fulfilment!=FulfilmentWork.Place&&work.fulfilment!=FulfilmentWork.Direct&&pendingWork.Count>=PendingWorkCapacity)return InteractionRejected("待办已满（每口锅至多 4 项）· 先完成或取消一项");
            if(reserve&&!Session.ReserveOrder(work.order,work.lane))return InteractionRejected("订单或锅位已有安排");
            work.id=++workSerial;work.queuedAt=Session.Elapsed;work.reservation=reserve;pendingWork.Add(work);
            bool ahead=work.lane>=0&&work.lane<Session.Lanes.Length&&Session.Lanes[work.lane].step!=work.step;
            string timing=work.step==KitchenStep.Utensil&&ahead?" · 先就位，火候到时操作":ahead?" · 前置就绪后自动操作":"";
            ShowInteractionHint("已安排 "+WorkLabel(work)+timing+(work.note!=""?" · "+work.note:""));
            if(!pumping)PumpWork();return true;
        }
        // A pan-side flip (utensil) or clean serves the pan, not one customer:
        // the attached order may leave mid-heat while the food is still
        // saveable, so those intents must survive order departure. Order-bound
        // intents (pickup reservations, seasoning for the current customer)
        // keep their order checks.
        private static bool OrderBindingRequired(WorkIntent work)
        {
            // COUNTER19：装盒上架与出餐台交付都不绑定旧订单双向预约。
            if(work.kind==StallInteractionKind.Serving||work.kind==StallInteractionKind.CounterMeal)return false;
            return work.kind!=StallInteractionKind.Cookware||work.step!=KitchenStep.Utensil&&work.step!=KitchenStep.Dirty;
        }
        // A real batch pickup (orderId=-1 Ingredient intent) still queued or
        // walking for this pan: the anchor an unbound green-window appointment
        // waits on before the batch physically starts.
        private bool HasBatchPickup(int lane)
        {
            if(activeWork!=null&&activeWork.kind==StallInteractionKind.Ingredient&&activeWork.order<0&&activeWork.lane==lane)return true;
            foreach(WorkIntent work in pendingWork)if(work.kind==StallInteractionKind.Ingredient&&work.order<0&&work.lane==lane)return true;
            return false;
        }
        // 顾客离开但锅里还有同菜批次：排队的调味/封装不清除，改绑为无订单
        // 批次意图（order=-1），由 Serve 的自动匹配找下一位同菜顾客。
        private bool TryRebindToBatch(WorkIntent work)
        {
            if(work.order<0||work.step!=KitchenStep.Season&&work.step!=KitchenStep.Pack||work.lane<0||work.lane>=Session.Lanes.Length)return false;
            KitchenLane lane=Session.Lanes[work.lane];
            if(lane.batchRemaining<=0||lane.step==KitchenStep.Dirty)return false;
            if(work.reservation)Session.ReleaseOrderReservation(work.order);
            work.order=-1;work.reservation=false;RecordWork(work,"rebound_to_next_same_recipe");
            ShowInteractionHint((work.lane+1)+"号锅顾客离开 · 熟食留在锅里，已改绑下一位同菜顾客");
            InteractionChanged?.Invoke();return true;
        }
        private bool WorkStillValid(WorkIntent work)
        {
            if(work.fulfilment==FulfilmentWork.Place||work.fulfilment==FulfilmentWork.Direct)return HeldWorkValid(work);
            // COUNTER19：出餐台交付只校验固定 meal 仍在台面 + 固定顾客仍存活，
            // 绝不回退改发另一位；lane=-1 不触碰任何锅状态。
            if(work.kind==StallInteractionKind.CounterMeal)
            {
                if(string.IsNullOrEmpty(work.item))return false;
                StallOrder order=Session.FindOrder(work.order);
                return order!=null&&!order.served&&!order.departed&&FindCounterMeal(work.item)!=null;
            }
            // COUNTER19：装盒上架不依赖顾客。冻结了批次 id 时，批次被清走或换成
            // 别批即取消（桌上尚无 meal 可丢）；未冻结的旧批只看锅没糊。
            if(work.kind==StallInteractionKind.Serving)
            {
                if(work.lane<0||work.lane>=Session.Lanes.Length)return false;
                KitchenLane serveLane=Session.Lanes[work.lane];
                if(serveLane.step==KitchenStep.Dirty)return false;
                if(work.batchId=="")return true;
                return serveLane.freeBatch!=null&&serveLane.freeBatch.id==work.batchId
                    &&(work.productId==""||serveLane.freeBatch.product!=null&&serveLane.freeBatch.product.id==work.productId);
            }
            if(work.lane<0||work.lane>=Session.Lanes.Length)return false;KitchenLane lane=Session.Lanes[work.lane];
            // FREEDOM19：逐项投料只看 CanAddFreeIngredient 的实际许可（空锅也可下
            // 首料），不用 IsFreeLane 当门槛；接触帧 AddFreeIngredient 再复验扣 1。
            if(work.kind==StallInteractionKind.Seasoning&&Session.IsFreeLane(work.lane))
                return lane.step!=KitchenStep.Idle&&lane.step!=KitchenStep.Dirty;
            if(work.freeInput)return Session.CanAddFreeIngredient(work.lane,work.item,out _);
            // FREEDOM19：开火意向——料搬运中的空锅或真实 Prep 都有效；锅进入
            // 加热/成品/清理即失效。能量只在接触帧 StartFreeCooking 扣一次。
            if(work.freeStart)
            {
                if(lane.step==KitchenStep.Prep)return true;
                return lane.step==KitchenStep.Idle&&HasPendingFreeInputs(work.lane);
            }
            if(work.step==KitchenStep.Utensil)
            {
                if(!Session.RequiresManualHeatAction(work.lane))return false;
                // An existing batch's green-window appointment survives order
                // departure (the paid food is still saveable). Before pickup
                // contact the pan is still Idle: an order-bound appointment is
                // valid only while the same reserved order is honestly waiting
                // for it; an unbound batch appointment is valid only while a
                // real batch pickup for that pan is still queued or walking.
                if(lane.step==KitchenStep.Prep||lane.step==KitchenStep.Heating||lane.step==KitchenStep.Utensil)return true;
                if(work.order<0)return Session.IsFreeLane(work.lane)||HasBatchPickup(work.lane);
                return lane.step==KitchenStep.Idle&&TryGetLaneOrder(lane,out StallOrder reserved)&&reserved.id==work.order;
            }
            // Order-bound chain steps (seasoning/packaging) and pickup/scoop
            // reservations keep their order checks; a pan-serving utensil or
            // clean appointment never reaches this block.
            if(work.order>=0&&OrderBindingRequired(work))
            {
                StallOrder order=Session.FindOrder(work.order);if(order==null||order.served||order.departed)return false;
                if(work.reservation)
                {
                    if(order.reservedLane!=work.lane||lane.reservedOrderId!=work.order)return false;
                    return lane.step==work.step; // pickup waits for a free pan; scoop, for stock
                }
                // Chain identity crosses the actual pickup/scoop contact:
                // before it, require the same live bidirectional reservation;
                // after it, require the same bound order. ReadyStock may wait
                // for a reserved scoop without repeating the batch debit.
                if(!TryGetLaneOrder(lane,out StallOrder bound)||bound.id!=work.order)return false;
            }
            if(work.step==KitchenStep.Season)return lane.step==KitchenStep.ReadyStock&&!lane.seasoned||lane.step==KitchenStep.Idle||lane.step==KitchenStep.Prep||lane.step==KitchenStep.Heating||lane.step==KitchenStep.Utensil||lane.step==KitchenStep.Season;
            if(work.step==KitchenStep.Pack)return lane.step==KitchenStep.ReadyStock||lane.step==KitchenStep.Idle||lane.step==KitchenStep.Prep||lane.step==KitchenStep.Heating||lane.step==KitchenStep.Utensil||lane.step==KitchenStep.Season||lane.step==KitchenStep.Pack;
            return lane.step==work.step;
        }
        private bool WorkReady(WorkIntent work)
        {
            if(work.fulfilment==FulfilmentWork.Place)return HeldWorkValid(work)&&Session.CanPlaceHeldMeal(out _);
            if(work.fulfilment==FulfilmentWork.Direct)return HeldWorkValid(work)&&Session.CanServeHeldMeal(work.order,out _);
            if(Session.HeldMeal!=null)return false;
            KitchenLane lane=work.lane>=0&&work.lane<Session.Lanes.Length?Session.Lanes[work.lane]:null;
            // COUNTER19：交付就绪 = 台面 meal 仍在 + 固定顾客此时可服务（只读预检，
            // 真正移除与收款在接触帧 ServeCounterMeal 复验）。
            if(work.kind==StallInteractionKind.CounterMeal)
                return !string.IsNullOrEmpty(work.item)&&FindCounterMeal(work.item)!=null&&Session.CanServeCounterMeal(work.item,work.order,out _);
            // COUNTER19：装盒就绪 = 核心判定可装且该锅已无在途投料/开火前置。
            if(work.kind==StallInteractionKind.Serving)
                return lane!=null&&Session.CanPackToHand(work.lane,work.item,out _)&&!HasPendingFreeInputs(work.lane)&&!HasPendingFreeStart(work.lane)&&!HasPendingSeasoning(work.lane);
            if(work.kind==StallInteractionKind.Seasoning&&Session.IsFreeLane(work.lane))
                return lane.step==KitchenStep.Season&&lane.remaining<=0||lane.step==KitchenStep.Pack||lane.step==KitchenStep.ReadyStock&&lane.seasoned;
            if(work.freeInput)return Session.CanAddFreeIngredient(work.lane,work.item,out _);
            // FREEDOM19：开火必须等该锅所有先入队材料实际下锅（无未提交投料），
            // 且锅真实处于 Prep；接触帧 StartFreeCooking 一次性扣能源，不重复。
            if(work.freeStart)return lane!=null&&lane.step==KitchenStep.Prep&&lane.freeBatch!=null&&!HasPendingFreeInputs(work.lane);
            if(work.step==KitchenStep.Utensil)return lane.step==KitchenStep.Utensil;
            if(work.step==KitchenStep.Season)return lane.step==KitchenStep.Season&&lane.remaining<=0;
            if(work.step==KitchenStep.Pack)
            {
                if(lane.step!=KitchenStep.Pack)return false;
                // RISK7SCENE 修复 1：无订单（order=-1）封装批次必须等到有匹配的合法
                // 等待顾客再预约 Actor；没有顾客时意向与食物都留在原地，Actor 可先做
                // 别锅，绝不执行注定失败的 Pack（Serve 侧同样在接触帧再校验一次）。
                if(work.order<0)return Session.FindWaitingOrder(lane.recipeIndex)!=null;
                return true;
            }
            return lane.step==work.step;
        }
        // Shared by the idle wait and the pure-approach reconsideration: among
        // every ready non-utensil candidate, take the shortest task whose
        // authored estimate still finishes and returns to the appointment pan
        // before its heat deadline. Null means nothing provably fits — the
        // caller waits instead of promising a save the schedule cannot make.
        private WorkIntent SelectGapFill(int appointmentLane,float deadline)
        {
            if(float.IsPositiveInfinity(deadline))return null;
            WorkIntent fill=null;float fillCost=float.PositiveInfinity;
            foreach(WorkIntent work in pendingWork)
            {
                if(work.step==KitchenStep.Utensil||work.kind==StallInteractionKind.Serving||work.kind==StallInteractionKind.CounterMeal||work.lane<0||!WorkReady(work))continue;
                float cost=Stage.EstimateInteractionSeconds(work.kind,work.lane,work.item)+Stage.EstimateReturnAfterInteractionSeconds(work.kind,work.lane,work.item,appointmentLane)+1.05f*.32f+.35f;
                if(cost<deadline&&cost<fillCost){fill=work;fillCost=cost;}
            }
            return fill;
        }
        private bool pumping;
        private void PumpWork()
        {
            if(!CanInteract)return;
            if(pumping)return; // EnsurePackChainScoop 经 Enqueue 入队时防 PumpWork 递归
            pumping=true;
            try
            {
            for(int i=pendingWork.Count-1;i>=0;i--)if(!WorkStillValid(pendingWork[i]))
            {
                WorkIntent stale=pendingWork[i];
                // COUNTER19：台面 meal 没了或固定顾客走了 → 意向取消但 meal 保留在台面。
                if(stale.kind==StallInteractionKind.CounterMeal)
                {
                    pendingWork.RemoveAt(i);RecordWork(stale,"counter_meal_or_customer_gone");
                    ShowInteractionHint(Session.HeldMeal!=null?"餐还拿在手上 · 可以放台或另选顾客":"这份餐的安排已取消");InteractionChanged?.Invoke();continue;
                }
                // 断点 2/3 修复：调味/封装意图随顾客离开改绑到锅内批次，
                // 食物还在就绝不静默消失。
                if(TryRebindToBatch(stale))continue;
                pendingWork.RemoveAt(i);if(stale.reservation)Session.ReleaseOrderReservation(stale.order);RecordWork(stale,"expired_or_state_changed");
                KitchenLane staleLane=stale.lane>=0&&stale.lane<Session.Lanes.Length?Session.Lanes[stale.lane]:null;
                if(stale.step==KitchenStep.Utensil&&staleLane!=null&&staleLane.step==KitchenStep.Dirty)ShowInteractionHint((stale.lane+1)+"号锅错过火候已糊 · 点锅清理后再接单");
                else if(staleLane!=null&&staleLane.step==KitchenStep.Dirty)ShowInteractionHint((stale.lane+1)+"号锅已糊 · 点锅清理后再继续");
                else if(stale.reservation)ShowInteractionHint("取料安排已取消：顾客离开或锅位变化 · 食物仍在锅里");
                else if(stale.order>=0)ShowInteractionHint((stale.lane+1)+"号锅的「"+stale.label+"」已取消：这份订单已离开");
                else ShowInteractionHint((stale.lane+1)+"号锅的「"+stale.label+"」已取消：锅状态变化");
                InteractionChanged?.Invoke();
            }
            // An uncommitted action whose customer has left stops taking the
            // vendor's time; its visual prop is returned, with no transaction.
            if(activeWork!=null&&!activeWork.committed&&!WorkStillValid(activeWork))
            {WorkIntent stale=activeWork;activeWork=null;Stage.CancelWork();if(stale.reservation)Session.ReleaseOrderReservation(stale.order);RecordWork(stale,"cancelled_stale_before_contact");
                ShowInteractionHint(stale.fulfilment==FulfilmentWork.Direct||stale.fulfilment==FulfilmentWork.Place?"动作已收回 · 餐仍拿在手上":stale.order>=0?"顾客离开或状态变化 · 动作收回，熟食留在锅里":(stale.lane+1)+"号锅状态变化 · 动作已收回");InteractionChanged?.Invoke();}
            pendingWork.Sort(CompareWork);
            TickCounterSelection();
            EnsurePackChainScoop(); // 清理/排序后补确实缺失的盛出，再统一派发
            // Only an approach without a held prop/committed transaction may
            // be redirected. Re-evaluate it when a more urgent pan is queued,
            // and also when a later-queued ready task provably fits inside the
            // waiting window: do that task, then walk back to the appointment.
            if(positioning&&Session.HeldMeal!=null){Stage.CancelWork();positioning=false;positioningLane=-1;}
            if(positioning&&activeWork==null)
            {
                WorkIntent anchor=null;bool currentExists=false;
                foreach(WorkIntent work in pendingWork)
                {
                    if(work.step!=KitchenStep.Utensil)continue;
                    if(work.lane==positioningLane)currentExists=true;
                    if(anchor==null||SecondsUntilHeatDeadline(work.lane)<SecondsUntilHeatDeadline(anchor.lane))anchor=work;
                }
                if(!currentExists||anchor!=null&&anchor.lane!=positioningLane&&SecondsUntilHeatDeadline(anchor.lane)+.05f<SecondsUntilHeatDeadline(positioningLane))
                {Stage.CancelWork();positioning=false;positioningLane=-1;}
                else if(anchor!=null)
                {
                    WorkIntent fill=SelectGapFill(anchor.lane,SecondsUntilHeatDeadline(anchor.lane));
                    if(fill!=null){Stage.CancelWork();positioning=false;positioningLane=-1;Dispatch(fill);return;}
                }
            }
            if(Stage.WorkActorBusy||activeWork!=null)return;
            WorkIntent next=null,waitingHeat=null;float heatDeadline=float.PositiveInfinity;
            foreach(WorkIntent work in pendingWork)
            {
                KitchenLane lane=work.lane>=0&&work.lane<Session.Lanes.Length?Session.Lanes[work.lane]:null;
                if(Session.HeldMeal==null&&work.step==KitchenStep.Utensil&&!WorkReady(work))
                {
                    float deadline=SecondsUntilHeatDeadline(work.lane);
                    if(deadline<heatDeadline){heatDeadline=deadline;waitingHeat=work;}
                    continue;
                }
                if(!WorkReady(work))continue;
                if(next==null)next=work;
                else if(lane!=null&&work.step==KitchenStep.Utensil&&(next.step!=KitchenStep.Utensil||lane.remaining<Session.Lanes[next.lane].remaining))next=work;
            }
            // Work deliberately requested for a heating pan gets an actual
            // approach during idle time. Another queued task may use that heat
            // time only when the authored route estimate proves it finishes
            // and returns before the appointment deadline; compare every ready
            // task and take the shortest fitting one rather than letting one
            // long job block a feasible short one. Nothing provably fitting
            // means waiting at the pan, not a guaranteed save.
            if(waitingHeat!=null&&next!=null&&next.step!=KitchenStep.Utensil)
            {
                WorkIntent fill=SelectGapFill(waitingHeat.lane,heatDeadline);
                if(fill!=null){Dispatch(fill);return;}
                next=null;
            }
            if(waitingHeat!=null&&(next==null||next.step!=KitchenStep.Utensil))
            {
                if(!Stage.IsAtCookware(waitingHeat.lane))
                {
                    positioning=true;positioningLane=waitingHeat.lane;Stage.ApproachCookware(waitingHeat.lane,()=>{positioning=false;positioningLane=-1;InteractionChanged?.Invoke();});
                    InteractionChanged?.Invoke();
                }
                return;
            }
            if(next==null)return;Dispatch(next);
            }
            finally{pumping=false;}
        }
        // Single dispatch path for the queue and for a gap-filling task that
        // interrupted a pure approach: identical audit fields and callbacks.
        // Packaging additionally receives the delivery contract: after the real
        // Scoop and Pack contacts the stage walks to the resolved customer's
        // service side and fires exactly one Serve commit at the hand contact.
        private void Dispatch(WorkIntent work)
        {
            pendingWork.Remove(work);activeWork=work;work.startedAt=Session.Elapsed;work.pathStart=Stage.WorkAudit?.pathMetres??0;
            InteractionChanged?.Invoke();WorkIntent executing=work;
            // Packing fills the actual hand at the physical package contact; no customer is needed.
            if(work.kind==StallInteractionKind.Serving)
            {
                Stage.PerformPackToHand(work.lane,work.item,()=>CommitWork(executing),()=>FinishWork(executing));
                return;
            }
            // Held actions freeze the physical meal and the intended customer.
            // Counter pickup is a separate customer action, never vendor delivery.
            if(work.fulfilment==FulfilmentWork.Place)
            {Stage.PerformPlaceHeldMeal(()=>CommitWork(executing),()=>FinishWork(executing));return;}
            if(work.fulfilment==FulfilmentWork.Direct)
            {
                Stage.PerformHeldMealDelivery(work.order,()=>CommitWork(executing),()=>FinishWork(executing),
                    ()=>ShowInteractionHint("没有递成 · 餐还拿在手上，可以放台或另选顾客"));return;
            }
            Func<int> serveResolver=null;
            if(work.kind==StallInteractionKind.Packaging)
                serveResolver=()=>
                {
                    StallOrder target=Session.FindWaitingOrder(Session.Lanes[work.lane].recipeIndex);
                    return target!=null?target.id:-1;
                };
            Action deliveryFailed=()=>ShowInteractionHint("还没有等这份菜的顾客 · 熟食留在锅里，顾客到位后再封装出餐");
            Stage.PerformDeliveryInteraction(work.kind,work.lane,work.item,()=>CommitWork(executing),()=>FinishWork(executing),executing.order,serveResolver,deliveryFailed);
        }
        private int CompareWork(WorkIntent a,WorkIntent b)
        {
            int priority=WorkPriority(a).CompareTo(WorkPriority(b));if(priority!=0)return priority;
            if(a.step==KitchenStep.Utensil&&b.step==KitchenStep.Utensil)
            {int heat=SecondsUntilHeatDeadline(a.lane).CompareTo(SecondsUntilHeatDeadline(b.lane));if(heat!=0)return heat;}
            float pa=Session.FindOrder(a.order)?.patience??float.PositiveInfinity,pb=Session.FindOrder(b.order)?.patience??float.PositiveInfinity;
            int deadline=pa.CompareTo(pb);return deadline!=0?deadline:a.id.CompareTo(b.id);
        }
        private int WorkPriority(WorkIntent work)
        {
            if(work.kind==StallInteractionKind.CounterMeal)return 0; // 固定顾客的递餐最急
            if(work.step==KitchenStep.Utensil&&work.lane>=0&&work.lane<Session.Lanes.Length&&Session.Lanes[work.lane].step==KitchenStep.Utensil)return 1;
            if(work.kind==StallInteractionKind.Serving||work.step==KitchenStep.Pack)return 2;
            if(work.step==KitchenStep.Season)return 3;
            if(work.step==KitchenStep.ReadyStock)return 4;
            if(work.kind==StallInteractionKind.Ingredient)return 5;
            if(work.step==KitchenStep.Dirty)return 6;
            return 7; // A future green appointment uses a real deadline, not FIFO.
        }
        private void CommitWork(WorkIntent work)
        {
            if(activeWork!=work||work.committed||IsPaused||!WorkStillValid(work)||!WorkReady(work))return;
            // 接触帧一次性 commit：无订单投料/盛出走批次入口，封装出餐把订单
            // 解析推迟到 Serve（显式 orderId 或自动匹配等待最久的同菜顾客）。
            bool accepted;
            // COUNTER19/FREEDOM19 接触帧 commit：台面交付固定 meal+顾客；装盒上架
            // 扣 1 包材+1 份成品并放空锅；逐项投料每接触仅扣 1；开火一次性能量。
            if(work.fulfilment==FulfilmentWork.Place)accepted=Session.PlaceHeldMealOnCounter();
            else if(work.fulfilment==FulfilmentWork.Direct)accepted=Session.ServeHeldMeal(work.order);
            else if(work.fulfilment==FulfilmentWork.Pack)
            {accepted=Session.PackToHand(work.lane,work.item);if(accepted)work.counterMeal=HeldMealId;}
            else if(work.freeInput)accepted=Session.AddFreeIngredient(work.lane,work.item);
            else if(work.freeStart)accepted=Session.StartFreeCooking(work.lane);
            // PREP12：待下米饭锅的 Ingredient 接触推进第二拍（米面下锅 → 原 Prep/加热）。
            else if(work.kind==StallInteractionKind.Ingredient&&Session.LaneNeedsRice(work.lane))
                accepted=work.order<0?Session.AddRiceBatch(work.lane):Session.AddRiceForOrder(work.order,work.lane);
            else if(work.kind==StallInteractionKind.Ingredient&&work.order<0)accepted=Session.BeginBatch(work.lane);
            else if(work.kind==StallInteractionKind.Cookware&&work.step==KitchenStep.ReadyStock&&work.order<0)accepted=Session.ScoopBatch(work.lane);
            else if(work.reservation)accepted=Session.BeginOrder(work.order,work.lane);
            else if(work.kind==StallInteractionKind.Packaging)accepted=Session.ServeToOrder(work.lane,work.item,work.order);
            // FLAVOR11：调味意向带真实风味瓶（item=风味 Id）时走选瓶重载——
            // 接触帧再次校验兼容与库存；原味意向（item="seasoning"）行为不变。
            else if(work.kind==StallInteractionKind.Seasoning&&(Session.IsFreeLane(work.lane)||StallFlavorMenu.IsFlavor(work.item)))accepted=Session.Action(work.lane,work.item);
            else accepted=Session.Action(work.lane);
            work.committed=accepted;if(accepted)work.contactAt=Session.Elapsed;ShowInteractionHint(Session.Message);
        }
        private void FinishWork(WorkIntent work)
        {
            if(activeWork!=work)return;activeWork=null;
            if(work.reservation)Session.ReleaseOrderReservation(work.order);
            RecordWork(work,Stage.WorkAudit!=null&&!string.IsNullOrEmpty(Stage.WorkAudit.lastFailure)?"actor_failed":work.committed?"committed":"not_committed");
            InteractionChanged?.Invoke();
            FinishFulfilmentWork(work);
        }
        private void RecordWork(WorkIntent work,string status)
        {
            workHistory.Add(new BusinessWorkAudit{serial=work.id,orderId=work.order,lane=work.lane,action=work.label,status=status,queuedAt=work.queuedAt,startedAt=work.startedAt,contactAt=work.contactAt,finishedAt=Session.Elapsed,
                pathMetres=work.startedAt>=0?Mathf.Max(0,(Stage.WorkAudit?.pathMetres??0)-work.pathStart):0});
        }
        private string WorkLabel(WorkIntent work)
        {
            if(work==null)return "";
            if(work.fulfilment==FulfilmentWork.Place||work.fulfilment==FulfilmentWork.Direct)return work.label;
            if(work.kind==StallInteractionKind.CounterMeal)return "出餐台 · "+work.label;
            if(work.kind==StallInteractionKind.Serving)return (work.lane+1)+"号锅 · "+work.label;
            return (work.lane+1)+"号锅 · "+work.label;
        }
        public void AcceptOrder(int id)
        {
            if(!SelectOrder(id))return;StallOrder order=Session.FindOrder(id);
            if(order.lane>=0){ShowInteractionHint("这份订单已在制作中");return;}
            if(order.reservedLane>=0){ShowInteractionHint("这份订单已排入待办 · 无需重复点选");return;}
            if(SelectedLaneIndex>=0&&Session.Lanes[SelectedLaneIndex].step==KitchenStep.ReadyStock){UseCookware(SelectedLaneIndex);return;}
            // PREP12：两拍现炒接单第一拍是敲蛋（再下米面），不能只下米面。
            bool eggCrack=BusinessSession.RequiresEggCrack(order.recipeIndex);
            if(eggCrack){UseIngredient("egg");return;}
            foreach(string ingredient in BusinessSession.Recipes[order.recipeIndex].ingredients.Keys)if(ingredient!="seasoning"){UseIngredient(ingredient);return;}
        }
        // Conservative feasibility screen for a queued green-window
        // appointment. "预计来得及" is reported only when contact provably
        // stays inside the real remaining heat + green window; any occupation
        // we cannot schedule deterministically (busy actor, an approach in
        // progress, another pan's appointment ahead) downgrades the answer to
        // queued/waiting, so the label never promises a contact the queue
        // cannot guarantee. The business clock and the window are untouched.
        public readonly struct HeatPlan
        {
            public readonly bool planned,actorBusy,contactEstimated,feasible;
            public readonly float deadlineIn,contactIn;
            internal HeatPlan(bool planned,bool actorBusy,bool contactEstimated,bool feasible,float deadlineIn,float contactIn)
            {this.planned=planned;this.actorBusy=actorBusy;this.contactEstimated=contactEstimated;this.feasible=feasible;this.deadlineIn=deadlineIn;this.contactIn=contactIn;}
        }
        private const float QueueContactMargin=.5f,UtensilContactSeconds=.85f;
        public float SecondsUntilHeatDeadline(int laneIndex)
        {
            if(Session==null||laneIndex<0||laneIndex>=Session.Lanes.Length)return float.PositiveInfinity;
            KitchenLane lane=Session.Lanes[laneIndex];
            if(lane.step==KitchenStep.Utensil)return Mathf.Max(0,lane.remaining);
            return Session.SecondsUntilUtensil(laneIndex)+Session.GreenWindowSeconds(laneIndex);
        }
        public bool TryGetHeatPlan(int laneIndex,out HeatPlan plan)
        {
            plan=default;
            if(Session==null||Stage==null||laneIndex<0||laneIndex>=Session.Lanes.Length)return false;
            WorkIntent target=null;
            foreach(WorkIntent work in pendingWork)
                if(work.lane==laneIndex&&work.kind==StallInteractionKind.Cookware&&work.step==KitchenStep.Utensil){target=work;break;}
            if(target==null)return false;
            float deadline=SecondsUntilHeatDeadline(laneIndex);
            bool uncertain=Stage.WorkActorBusy||activeWork!=null||positioning;
            var ordered=new List<WorkIntent>(pendingWork);ordered.Sort(CompareWork);
            float elapsed=0;
            foreach(WorkIntent work in ordered)
            {
                if(ReferenceEquals(work,target))break;
                if(Session.HeldMeal==null&&work.step==KitchenStep.Utensil&&!WorkReady(work))uncertain=true; // another pan's appointment is also waiting
                else if(!WorkReady(work))continue;
                if(!uncertain)elapsed+=Stage.EstimateInteractionSeconds(work.kind,work.lane,work.item)+QueueContactMargin;
            }
            if(uncertain){plan=new HeatPlan(true,true,false,false,deadline,float.PositiveInfinity);return true;}
            if(float.IsPositiveInfinity(deadline)){plan=new HeatPlan(true,false,false,true,deadline,float.PositiveInfinity);return true;}
            float contact=elapsed+Stage.EstimateApproachSeconds(laneIndex)+UtensilContactSeconds+QueueContactMargin;
            plan=new HeatPlan(true,false,true,contact<=deadline,deadline,contact);return true;
        }
        // Stable, machine-readable status for independent acceptance runs:
        // "none" | "queued-busy" | "queued" | "queued-feasible" | "queued-late".
        public string DescribeHeatPlan(int laneIndex)
        {
            if(!TryGetHeatPlan(laneIndex,out HeatPlan plan))return "none";
            if(plan.actorBusy)return "queued-busy";
            if(!plan.contactEstimated)return float.IsPositiveInfinity(plan.deadlineIn)?"queued":"queued-unknown";
            return plan.feasible?"queued-feasible":"queued-late";
        }
        public void LaneAction(int index)
        {
            if(!CanInteract||index<0||index>=Session.Lanes.Length)return;
            if(Session.Lanes[index].step==KitchenStep.Season)UseSeasoning(index);else UseCookware(index);
        }
        public void Pack(int index,string id){UsePackage(index,id);}
        public void CloseDay(){if(!disposed&&!IsPaused&&!opening)Session.Close();}
        // ── RISK7SCENE：A01 假扫码逃单客接线（EVENT7R 冻结模块，Attach 只发生一次）──
        private void CreateRiskRuntime()
        {
            Session.ServeRiskGate=(orderId,recipeIndex,priceCents)=>EvaluateServeRiskGate(orderId,recipeIndex,priceCents);
            RiskDirector=new StallRiskDirector(Session.RiskState,
                (serial,cents)=>Session.TryCompleteRiskSale(serial,cents),
                serial=>Session.TryWriteOffRiskSale(serial),
                e=>Stage!=null?Stage.TakeRiskActor(e):null); // 复用已拿到餐的真实顾客
            RiskView=StallRiskView.Create(transform,RiskDirector,Stage!=null?Stage.View:null);
            // View 不再直调 Director：拦截入口由控制器把关（暂停/收摊时无效）。
            RiskView.CanIntercept=()=>!disposed&&!IsPaused&&!opening&&RiskDirector!=null&&RiskDirector.HasActiveEvent;
            RiskView.InterceptHandler=TryRiskIntercept;
        }
        // ServeRiskGate 接线层实现（RISK7SESSION 冻结文档示例为准；重复 Attach 的
        // 旧文档示例是笔误，不照抄）。正式模式：免疫先行 → ICD/激活检查 → 20s 轮询
        // → 保守概率；练习(day12) 非隐藏 fixture：前两单正常到账后安排一次显式
        // 教学节拍（下一单的假扫码），用短提示引导看收款/点顾客；其余时刻不乱来。
        private StallRiskGateDecision EvaluateServeRiskGate(int orderId,int recipeIndex,int priceCents)
        {
            var session=Session;var state=session.RiskState;var setup=session.Setup;
            if(state==null)return null;
            if(StallRiskRules.IsImmune(setup))return null;
            // RISK7SCENE：采纳前置条件——该订单背后必须有真实在场、可呈现的活顾客；
            // 否则不采纳（也绝不 fallback 新生第二个同 ID 顾客）。
            if(Stage==null||!Stage.HasLiveGuest(orderId))return null;
            if(setup.practice)
            {
                if(state.ForceNextAdoptA01)
                {
                    state.ForceNextAdoptA01=false;
                    ShowInteractionHint("对方扫码没有到账 · 点击这位顾客追回款项");
                    return new StallRiskGateDecision(true,StallRiskRules.ComputeCollectCents(setup,priceCents),StallRiskRules.ComputeInterceptSeconds(setup));
                }
                // 教学节拍（Serve 在扣减前调用本闸门，Served 尚为本单之前值）：
                // 第 2 单（Served==1）这次 Serve 正常成交，同时给第 3 单置位假扫码——
                // 前两单正常实收、第三单自然教学，无需任何调试开关。
                if(!practiceRiskBeatArmed&&session.Served==1&&!state.HasActiveEvent)
                {
                    practiceRiskBeatArmed=true;StallRiskRules.ForceAdoptNextServe(state);
                    ShowInteractionHint("下一位顾客会假装扫码付款 · 留意收款，及时点击对方追回");
                }
                return null; // 练习其余时刻不随机触发
            }
            if(state.ForceNextAdoptA01){state.ForceNextAdoptA01=false;
                return new StallRiskGateDecision(true,StallRiskRules.ComputeCollectCents(setup,priceCents),StallRiskRules.ComputeInterceptSeconds(setup));}
            if(state.HasActiveEvent||state.IcdRemaining>0f)return null; // ICD 期间不消耗轮询
            if(!StallRiskRules.TryBeginPoll(state))return null;          // 20s 轮询槽
            double draw=state.Draw();
            if(!StallRiskRules.ShouldAdoptServe(setup,session.Served,draw))return null; // 纯概率 + 首单保护
            return new StallRiskGateDecision(true,StallRiskRules.ComputeCollectCents(setup,priceCents),StallRiskRules.ComputeInterceptSeconds(setup));
        }
        // 采纳后恰好一次：Attach（spawn=真实顾客适配器）→ 挂头顶 UI → 顾客真实拿餐
        // （接餐表现只一次）。Attach 失败（顾客异常不在场）不新生 fallback 第二个顾客：
        // 事件仍在账上，可经无实体入口拦截或由窗口超时一次性核销。
        private void OnRiskSalePending(StallRiskEvent e)
        {
            if(RiskDirector==null||RiskView==null||e==null)return;
            if(RiskDirector.Attach(e))
            {
                var adapter=Stage!=null?Stage.LastRiskActor:null;
                if(adapter!=null)RiskView.PresentExistingActor(e,adapter);
                if(Stage!=null)Stage.BeginGuestReceive(e.OrderId);
                return;
            }
            Debug.LogWarning("[RISK7] A01 adopted without a presentable live guest; no fallback actor spawned (serial "+e.Serial+")");
        }
        // 实体点击入口（带 orderId）：该顾客无未决债权 → false 交还原 SelectOrder 流。
        public bool TryRiskIntercept(int orderId)
        {
            if(Session==null||!Session.HasPendingRiskOrder(orderId))return false;
            if(disposed||IsPaused||opening){ShowInteractionHint("已暂停 · 继续营业后再追回款项");return true;}
            return InterceptActiveEvent();
        }
        // 头顶拦截按钮入口（控制器受控回调，暂停/收摊时 CanIntercept 为假、点击无效）。
        public bool TryRiskIntercept()
        {
            if(disposed||IsPaused||opening||RiskDirector==null||!RiskDirector.HasActiveEvent)return false;
            return InterceptActiveEvent();
        }
        private bool InterceptActiveEvent()
        {
            bool ok=RiskDirector.TryIntercept();
            if(ok)ShowInteractionHint(Session.Message); // “追回逃单款 ¥x（菜名）”
            return ok;
        }
        // RISK7SCENE：追回入账只给一个反馈——HUD 跳过风险单（riskSerial>0）的
        // SaleCompleted 收款浮字，由 StallRiskView 的“已追回”短浮字统一承担；
        // 正常成交单行为不变。
        public bool IsRiskOrder(int orderId)
        {
            var order=Session!=null?Session.FindOrder(orderId):null;
            return order!=null&&order.riskSerial>0;
        }
        // 收摊/销毁：只清表现，不核销债权（未决债权随存档保留；真正收摊由
        // PublishSettlement → ResolvePendingRiskOnClose 统一核销）。View 先摘
        // 事件订阅，Director 后 Shutdown（适配器 Dispose=归还 body，Stage 统一销毁）。
        private void CleanupRiskRuntime()
        {
            if(RiskView!=null){RiskView.Dispose();RiskView=null;}
            if(RiskDirector!=null){RiskDirector.Shutdown();RiskDirector=null;}
        }
        public void ReturnMarket(){if(disposed||Session.Started||IsPaused||!Session.Setup.canReturnMarket)return;disposed=true;StopAllCoroutines();CleanupRiskRuntime();Stage.Release();gameObject.SetActive(false);returnCallback?.Invoke();Destroy(gameObject);}
        public void Continue(){if(disposed||!Session.Finished||continueCallback==null)return;disposed=true;StopAllCoroutines();CleanupRiskRuntime();Stage.Release();gameObject.SetActive(false);continueCallback.Invoke();Destroy(gameObject);}
        public void Cancel(){if(disposed)return;ClearCustomerPickups();disposed=true;StopAllCoroutines();CleanupRiskRuntime();if(Stage!=null)Stage.Release();gameObject.SetActive(false);Destroy(gameObject);}
        public BusinessAuditState CaptureAudit()
        {
            BusinessAuditState state=Session.CaptureAudit();state.paused=IsPaused;state.opening=opening;state.settlementPublished=settled;state.settlementPublications=SettlementPublishCount;
            state.cameraActive=Stage!=null&&Stage.View!=null&&Stage.View.isActiveAndEnabled;
            state.visibleCustomerActors=Stage!=null?Stage.VisibleCustomerCount:0;state.walkingCustomerActors=Stage!=null?Stage.WalkingCustomerCount:0;state.sceneModels=Stage!=null?Stage.CaptureModelAudit():Array.Empty<BusinessModelAudit>();return state;
        }
        [UnityEngine.Scripting.Preserve]
        public void InspectBusinessInteraction()
        {
            string json=JsonUtility.ToJson(BusinessInteractionProbe.Capture(this));const int chunkSize=2500;
            int serial=++probeSerial,count=(json.Length+chunkSize-1)/chunkSize;string id="business-"+serial+"-"+Time.frameCount;
            for(int part=0;part<count;part++)
            {
                int start=part*chunkSize;
                Debug.Log("QA_BUSINESS_INTERACTION_PART "+JsonUtility.ToJson(new BusinessInteractionProbe.Part{serial=serial,id=id,part=part,count=count,data=json.Substring(start,Math.Min(chunkSize,json.Length-start))}));
            }
        }
    }
}
