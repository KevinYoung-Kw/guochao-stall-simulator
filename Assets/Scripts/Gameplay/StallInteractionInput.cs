using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Guochao
{
    // Mouse input addresses physical objects. A drag is only an intent until the
    // vendor reaches the dish and the controller validates the transaction again.
    public sealed class StallInteractionInput : MonoBehaviour
    {
        public string CarriedPackageId { get; private set; }="";
        public string DraggedFlavorId { get; private set; }="";
        public string DraggedIngredientId { get; private set; }="";
        public bool IsDragging { get; private set; }
        public int DraggedDishLane { get; private set; }=-1;
        public StallInteractionTarget Hovered { get; private set; }
        public StallInteractionTarget FocusedTarget { get; private set; }
        private BusinessController owner;
        private StallInteractionTarget pressed;
        private Vector2 downPosition;
        private bool dragHintShown;
        public void Initialize(BusinessController controller){owner=controller;}
        public void CancelPackage(){CarriedPackageId="";DraggedFlavorId="";DraggedIngredientId="";IsDragging=false;DraggedDishLane=-1;pressed=null;}
        private void OnDisable(){CancelPackage();Hovered=null;}
        // Physical occlusion comes from the deliberately built solid envelope
        // layer (counter, shelves, cart body, walls). A target whose trigger is
        // reached only through such a solid is behind it and must not be picked.
        private const float OcclusionSkin=.01f,DepthTieWindow=.06f,TieBreakPixelMargin=12f;
        public const float ReferenceScreenHeight=900f;
        public static float DefaultScreenTolerance(StallInteractionKind kind)
        {
            switch(kind)
            {
                case StallInteractionKind.Order:return 30f;
                case StallInteractionKind.Cookware:return 26f;
                case StallInteractionKind.Seasoning:return 26f;
                case StallInteractionKind.Packaging:return 22f;
                case StallInteractionKind.Serving:return 26f;
                case StallInteractionKind.CounterMeal:return 24f;
                default:return 20f;
            }
        }
        public float ScreenToleranceFor(StallInteractionTarget target)
        {
            if(target==null)return 0f;
            float value=target.screenTolerance>0?target.screenTolerance:DefaultScreenTolerance(target.kind);
            // Scale by the actual view the query runs against: with a
            // camera.targetTexture assigned (resolution validation) the view
            // pixel height differs from Screen.height, and tolerance must
            // follow the view being picked, not the global game view.
            float viewHeight=owner!=null&&owner.Stage!=null&&owner.Stage.View!=null?owner.Stage.View.pixelHeight:Screen.height;
            return value*Mathf.Max(.6f,viewHeight/ReferenceScreenHeight);
        }
        // Fresh per-call UI gate shared by production hover / press / drag
        // release and editor validation. IsPointerOverGameObject() trusts a
        // cached per-frame EventSystem state that can lag the exact release
        // point, so this queries the real EventSystem raycasters at the exact
        // screen position instead. A single PointerEventData and result list
        // are reused — no per-frame allocation. Decorative graphics keep
        // raycastTarget=false and therefore never appear here; only real
        // opaque HUD chrome, buttons and modal faces intercept.
        private PointerEventData uiQueryData;
        private readonly List<RaycastResult> uiQueryResults=new List<RaycastResult>(16);
        public bool IsScreenPointOverUI(Vector2 screenPosition)
        {
            if(EventSystem.current==null)return false;
            if(uiQueryData==null)uiQueryData=new PointerEventData(EventSystem.current);
            uiQueryData.position=screenPosition;
            uiQueryResults.Clear();
            EventSystem.current.RaycastAll(uiQueryData,uiQueryResults);
            foreach(RaycastResult result in uiQueryResults)
            {
                Graphic graphic=result.gameObject.GetComponentInParent<Graphic>();
                if(graphic!=null&&graphic.canvas!=null)return true;
            }
            return false;
        }
        // Read-only screen-coordinate picking entry shared by production input
        // and editor validation: UI/modal gating stays with the caller.
        // A solid blocks a candidate only when it belongs to someone else. The
        // customer's own body capsule stands between the camera and its order
        // trigger (a child of the same guest root) and must not occlude its own
        // order, while third-party walls, counters and other customers still do.
        // Ownership is matched explicitly through the transform hierarchy — the
        // solid's transform is the target's own or an ancestor of it — never by
        // blanket-ignoring NPCs or solid colliders.
        private static bool IsOwnSolid(Collider solid,StallInteractionTarget target)
        {
            if(target==null||solid==null)return false;
            return target.transform==solid.transform||target.transform.IsChildOf(solid.transform);
        }
        private bool IsOccludedByForeignSolid(RaycastHit[] hits,float distance,StallInteractionTarget target)
        {
            foreach(RaycastHit solid in hits)
            {
                if(solid.distance>=distance-OcclusionSkin)break; // sorted: nothing closer can follow
                if(solid.collider.gameObject.layer!=StreetStallStage.SolidLayer&&solid.collider.gameObject.layer!=StreetStallStage.QueryVisualLayer||solid.collider.isTrigger)continue;
                if(IsOwnSolid(solid.collider,target))continue; // own/owner body never blocks its own trigger
                return true;
            }
            return false;
        }
        public StallInteractionTarget PickScreenPoint(Vector2 screenPosition)
        {
            if(owner==null||owner.Stage==null||owner.Stage.View==null)return null;
            Camera view=owner.Stage.View;
            Ray ray=view.ScreenPointToRay(screenPosition);
            // SolidLayer carries the low navigation envelope (counter, shelves,
            // 2.4 m facade). QueryVisualLayer carries mouse-query-only boxes
            // that follow the full 4.6 m visual shop fronts: the high ortho
            // camera looks over the low solid yet still through the visual
            // facade, so picking must see the visual face. Physics/navigation
            // masks stay untouched.
            int mask=view.cullingMask|StreetStallStage.SolidMask|StreetStallStage.QueryVisualMask;
            RaycastHit[] hits=Physics.RaycastAll(ray,60,mask,QueryTriggerInteraction.Collide);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            StallInteractionTarget best=null;float bestDistance=0f;
            foreach(RaycastHit hit in hits)
            {
                StallInteractionTarget target=hit.collider.GetComponentInParent<StallInteractionTarget>();
                if(target==null||!target.isActiveAndEnabled)continue;
                if(IsOccludedByForeignSolid(hits,hit.distance,target))continue; // third-party wall/solid in the way
                if(best==null){best=target;bestDistance=hit.distance;continue;}
                if(hit.distance-bestDistance<=DepthTieWindow)
                {
                    // Overlapping trigger envelopes (neighbouring pan/package
                    // boxes): when the pointer sits clearly nearer another
                    // candidate's anchor, that owner wins instead of the
                    // front-most big box.
                    float current=Vector2.Distance(screenPosition,(Vector2)view.WorldToScreenPoint(target.WorldAnchor));
                    float leading=Vector2.Distance(screenPosition,(Vector2)view.WorldToScreenPoint(best.WorldAnchor));
                    if(leading-current>=TieBreakPixelMargin){best=target;bestDistance=hit.distance;}
                }
                else break;
            }
            if(best!=null)return best;
            // Screen-space fallback: fine objects (seasoning bottle, small
            // trays) keep tight colliders and get a limited pixel tolerance.
            return PickByScreenProximity(screenPosition,ray,view);
        }
        private StallInteractionTarget PickByScreenProximity(Vector2 screenPosition,Ray ray,Camera view)
        {
            StallInteractionTarget nearest=null;float nearestDistance=float.PositiveInfinity;
            foreach(StallInteractionTarget target in owner.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target==null||!target.isActiveAndEnabled)continue;
                Vector3 world=target.WorldAnchor;
                Vector3 onScreen=view.WorldToScreenPoint(world);
                if(onScreen.z<=0)continue;
                Vector3 span=world-ray.origin;bool blocked=false;
                foreach(RaycastHit solid in Physics.RaycastAll(ray.origin,span,span.magnitude-.02f,StreetStallStage.SolidMask|StreetStallStage.QueryVisualMask,QueryTriggerInteraction.Ignore))
                    if(!IsOwnSolid(solid.collider,target)){blocked=true;break;} // foreign solid only; own body never blocks its own anchor
                if(blocked)continue;
                float distance=Vector2.Distance(screenPosition,(Vector2)onScreen);
                if(distance>ScreenToleranceFor(target)||distance>=nearestDistance)continue;
                nearest=target;nearestDistance=distance;
            }
            return nearest;
        }
        private void Update()
        {
            if(owner==null||!owner.Session.Started||owner.Session.Finished||owner.IsPaused||owner.HUD.HasBusinessOverlay)
            {CancelPackage();Hovered=null;return;}
            if(Input.GetMouseButtonDown(1)||Input.GetKeyDown(KeyCode.JoystickButton1)){CancelPackage();owner.ShowInteractionHint("已放回包材");}
            if(Input.GetKeyDown(KeyCode.Tab)||Input.GetKeyDown(KeyCode.JoystickButton5)||Input.GetKeyDown(KeyCode.JoystickButton4))
                CycleFocus(Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)||Input.GetKeyDown(KeyCode.JoystickButton4)?-1:1);
            bool uiFocus=EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null;
            if(FocusedTarget!=null&&!uiFocus&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter)||Input.GetKeyDown(KeyCode.JoystickButton0)))Activate(FocusedTarget);
            bool overUI=IsScreenPointOverUI(Input.mousePosition);
            Hovered=overUI?null:PickScreenPoint(Input.mousePosition);
            if(Input.GetMouseButtonDown(0))
            {pressed=overUI?null:Hovered;downPosition=Input.mousePosition;dragHintShown=false;}
            if(pressed!=null&&pressed.kind==StallInteractionKind.Packaging&&Input.GetMouseButton(0)&&Vector2.Distance(downPosition,Input.mousePosition)>9)
            {
                if(owner.Session.Count(pressed.itemId)>0&&owner.CanInteract&&owner.Session.HeldMeal==null)
                {IsDragging=true;CarriedPackageId=pressed.itemId;}
                else if(!dragHintShown){dragHintShown=true;owner.ShowInteractionHint(owner.Session.HeldMeal!=null?"手上拿着餐 · 先放台或递给顾客":owner.Session.Count(pressed.itemId)<=0?"这种包材已用完":"摊主正在忙活 · 稍后再拿");}
            }
            if(pressed!=null&&pressed.kind==StallInteractionKind.Seasoning&&Input.GetMouseButton(0)&&Vector2.Distance(downPosition,Input.mousePosition)>9)
            {
                // FLAVOR11：按住风味瓶/油盐瓶拖到锅上=指定工位调味；拖走则视为放回。
                if(owner.CanInteract&&owner.Session.HeldMeal==null){IsDragging=true;DraggedFlavorId=pressed.itemId;}
                else if(!dragHintShown){dragHintShown=true;owner.ShowInteractionHint(owner.Session.HeldMeal!=null?"手上拿着餐 · 先放台或递给顾客":"摊主正在忙活 · 稍后再拿");}
            }
            if(pressed!=null&&pressed.kind==StallInteractionKind.Ingredient&&Input.GetMouseButton(0)&&Vector2.Distance(downPosition,Input.mousePosition)>9)
            {
                // PREP12：按住鸡蛋/米面/果料等实物拖到目标锅位=指定工位下锅，
                // 与点选入口共用同一生产接触帧；拖到别处/松手都=放回，不扣货。
                if(owner.CanInteract&&owner.Session.HeldMeal==null){IsDragging=true;DraggedIngredientId=pressed.itemId;}
                else if(!dragHintShown){dragHintShown=true;owner.ShowInteractionHint(owner.Session.HeldMeal!=null?"手上拿着餐 · 先放台或递给顾客":"摊主正在忙活 · 稍后再拿");}
            }
            if(pressed!=null&&pressed.kind==StallInteractionKind.Cookware&&Input.GetMouseButton(0)&&Vector2.Distance(downPosition,Input.mousePosition)>9)
            {
                int lane=pressed.lane;
                if(owner.CanInteract&&owner.Session.HeldMeal==null&&lane>=0&&lane<owner.Session.Lanes.Length&&owner.Session.Lanes[lane].step==KitchenStep.Pack)
                {IsDragging=true;DraggedDishLane=lane;CarriedPackageId="";}
            }
            if(!Input.GetMouseButtonUp(0)||pressed==null)return;
            StallInteractionTarget start=pressed;pressed=null;
            if(IsDragging)
            {
                StallInteractionTarget end=overUI?null:PickScreenPoint(Input.mousePosition);string package=CarriedPackageId;int dish=DraggedDishLane;string flavor=DraggedFlavorId;string ingredient=DraggedIngredientId;CancelPackage();
                if(flavor!="")
                {
                    if(end!=null&&end.kind==StallInteractionKind.Cookware)UseSeasoningFlavor(end.lane,flavor);
                    else owner.ShowInteractionHint("调料瓶已放回 · 按住瓶滑到要调味的锅上");
                }
                else if(ingredient!="")
                {
                    if(end!=null&&end.kind==StallInteractionKind.Cookware)owner.UseIngredientAtLane(end.lane,ingredient);
                    else owner.ShowInteractionHint("食材已放回 · 按住滑到对应的锅位下锅");
                }
                else if(dish>=0)
                {
                    if(end!=null&&end.kind==StallInteractionKind.Packaging)owner.UsePackage(dish,end.itemId);
                    else owner.ShowInteractionHint("菜还在锅里 · 将熟菜拖入对应包材槽");
                }
                else if(end!=null&&end.kind==StallInteractionKind.Cookware)owner.UsePackage(end.lane,package);
                else owner.ShowInteractionHint("包材已放回 · 拖到做好菜的锅上封装");
                return;
            }
            if(overUI)return;
            StallInteractionTarget target=Hovered;
            if(target==null||target!=start)return;
            Activate(target);
        }
        private void CycleFocus(int direction)
        {
            var targets=new List<StallInteractionTarget>();
            foreach(var target in owner.Stage.GetComponentsInChildren<StallInteractionTarget>(false))
            {
                if(target.kind==StallInteractionKind.Order)
                {
                    // 出餐台流程：普通订单只是只读信息，不作为生产操作焦点；
                    // 只有风险追款单、或已选台面餐时的适配顾客才进入焦点轮换。
                    if(owner.Session.HasPendingRiskOrder(target.orderId)){targets.Add(target);continue;}
                    string mealId=owner.SelectedCounterMealId;
                    if(owner.Session.HeldMeal!=null)
                    {if(owner.Session.CanServeHeldMeal(target.orderId,out _))targets.Add(target);continue;}
                    if(mealId==""){targets.Add(target);continue;} // Read-only recipe guide is keyboard accessible.
                    if(!owner.Session.CanServeCounterMeal(mealId,target.orderId,out _))continue;
                    targets.Add(target);continue;
                }
                if(target.kind==StallInteractionKind.CounterMeal||target.kind==StallInteractionKind.Serving)
                {targets.Add(target);continue;}
                targets.Add(target);
            }
            targets.Sort((a,b)=>a.kind!=b.kind?a.kind.CompareTo(b.kind):a.kind==StallInteractionKind.Order?a.orderId.CompareTo(b.orderId):a.lane!=b.lane?a.lane.CompareTo(b.lane):string.CompareOrdinal(a.itemId,b.itemId));
            if(targets.Count==0)return;int index=targets.IndexOf(FocusedTarget);index=(index+direction+targets.Count)%targets.Count;FocusedTarget=targets[index];
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);
            string name=FocusedTarget.kind==StallInteractionKind.Order?"顾客订单":FocusedTarget.kind==StallInteractionKind.Cookware?(FocusedTarget.lane+1)+"号锅":FocusedTarget.kind==StallInteractionKind.Serving?"出餐台":FocusedTarget.kind==StallInteractionKind.CounterMeal?"台面餐":FocusedTarget.kind==StallInteractionKind.Seasoning?FlavorShortName(FocusedTarget.itemId):BusinessWorldHUD.ShortItem(FocusedTarget.itemId);
            owner.ShowInteractionHint(name+" · Enter / 手柄 A 操作 · Tab / 肩键切换");
        }
        // FLAVOR11：基础油盐走原 UseSeasoning(int)（保持默认行为），S01/S02/S05
        // 走新重载 UseSeasoning(int,string)。target.lane 为 -1 时由 Controller 按
        // 选中订单解析锅位，语义与旧版一致。
        private void UseSeasoningFlavor(int lane,string flavorId)
        {
            if(flavorId==null||flavorId==""||flavorId=="seasoning")owner.UseSeasoning(lane);
            else owner.UseSeasoning(lane,flavorId);
        }
        public static string FlavorShortName(string flavorId)
        {switch(flavorId){case "S01":return "辣椒油";case "S02":return "蒜蓉";case "S05":return "甜辣酱";default:return "油盐";}}
        private void Activate(StallInteractionTarget target)
        {
            switch(target.kind)
            {
                // 出餐台流程：顾客点击先走 A01 风险拦截；已选台面餐时点击适配
                // 顾客=真实递餐（DeliverCounterMealTo）；否则订单只是只读信息，
                // 不 SelectOrder、不自动交付、不替玩家挑顾客。
                case StallInteractionKind.Order:
                    if(owner.TryRiskIntercept(target.orderId))break;
                    if(owner.Session.HeldMeal!=null){owner.DeliverHeldMealTo(target.orderId);break;}
                    string mealId=owner.SelectedCounterMealId;
                    if(mealId!="")
                    {
                        if(owner.Session.CanServeCounterMeal(mealId,target.orderId,out string reason))owner.DeliverCounterMealTo(target.orderId);
                        else owner.ShowInteractionHint(reason==""?"这份餐与订单不符":reason);
                        break;
                    }
                    owner.ShowCustomerGuide(target.orderId);
                    break;
                // 台面成品：点选/再点取消（Controller 约定空串清除选择）。
                case StallInteractionKind.CounterMeal:
                    owner.SelectCounterMeal(owner.SelectedCounterMealId==target.itemId?"":target.itemId);
                    break;
                // 出餐台实物入口：只打包入台，绝不自动交付给顾客。
                case StallInteractionKind.Serving:owner.UseServingCounter(target.lane);break;
                case StallInteractionKind.Ingredient:owner.UseIngredient(target.itemId);break;
                case StallInteractionKind.Cookware:
                    if(CarriedPackageId!="")
                    {if(owner.UsePackage(target.lane,CarriedPackageId))CancelPackage();}
                    else owner.UseCookware(target.lane);
                    break;
                case StallInteractionKind.Seasoning:UseSeasoningFlavor(target.lane,target.itemId);break;
                case StallInteractionKind.Packaging:
                    if(!owner.CanInteract)break;
                    if(owner.Session.HeldMeal!=null){owner.ShowInteractionHint("手上拿着餐 · 先放台或递给顾客");break;}
                    if(owner.Session.Count(target.itemId)<=0){owner.ShowInteractionHint("这种包材已用完");break;}
                    CarriedPackageId=target.itemId;
                    owner.ShowInteractionHint("已拿起包材 · 点做好菜的锅封装，右键放回");
                    break;
            }
        }
    }
}
