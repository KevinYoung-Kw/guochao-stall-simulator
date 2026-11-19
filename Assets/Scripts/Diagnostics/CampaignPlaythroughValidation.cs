using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Guochao
{
    // Opt-in standalone acceptance. All movement uses the same DriveInput as
    // keyboard Update; actions use public production UI/Controller commands.
    // No fixture money, inventory, phase changes, model Act or pose assignments.
    public sealed class CampaignPlaythroughValidation : MonoBehaviour
    {
        [Serializable] public sealed class Checkpoint
        {
            public string name,phase,held,focus,message;
            public int day,cash,served;public float wallSeconds,remaining,marketUsed;
            public Vector3 player;public string[] loaded,cart,cargo;public BusinessAuditState business;
        }
        [Serializable] public sealed class DayResult
        {
            public int day,served,openingCash,nightCash,purchases,revenue,nightFuel;
            public float homeUsed,marketUsed,travelWallSeconds,businessSeconds;
            public string[] heavyMoved,purchased;public BusinessAuditState business;
        }
        [Serializable] public sealed class Report
        {
            public string schema="guochao.p1.native-two-day.v2",failure="",isolatedSaveDirectory;
            public string inputSource="Scripted PlayerMotor.DriveInput + production GameFlow and BusinessController commands. Game clock runs normally during movement, transfer, checkout, travel and service. Diagnostic route search pauses the game; not human keyboard/pointer input or a human completion-time claim.";
            public bool diagnosticActivityLeaseActive,diagnosticActivityLeaseReleased;
            public bool success,diagnosticAssignedTransforms,navigationTeleports,fixtureMoneyOrInventory,productionSaveRestorationUsed,nightSaveRoundTripPassed;
            public int movementFrames,planningCalls,overlapFrames,arrivalTrafficWaits;public float pathMetres,planningWallSeconds,planningComputeSeconds,maxMovementDelta,wallSeconds;
            public string[] engineErrors;public List<Checkpoint> checkpoints=new List<Checkpoint>();public List<DayResult> days=new List<DayResult>();
        }
        readonly Report report=new Report();readonly List<string> errors=new List<string>();
        readonly StringBuilder trace=new StringBuilder("wall,day,phase,target,x,y,z,yaw,input_x,input_y,dt,remaining,market_used\n");
        GameFlow flow;PlayerMotor controlled;string output,directory,currentTarget="";float started,nextTrace;
        MacDiagnosticActivityLease activityLease;
        bool controlledWasEnabled;static readonly Vector2[] axes={Vector2.up,new Vector2(1,1).normalized,Vector2.right,new Vector2(1,-1).normalized,Vector2.down,new Vector2(-1,-1).normalized,Vector2.left,new Vector2(-1,1).normalized};

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Boot()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--campaign-playthrough")>=0)
                new GameObject("ProductionTwoDayPlaythrough").AddComponent<CampaignPlaythroughValidation>();
        }
        void Awake()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--campaign-playthrough-output");
            output=at>=0&&at+1<args.Length?Path.GetFullPath(args[at+1]):Path.Combine(Application.persistentDataPath,"campaign-playthrough.json");
            if(!output.EndsWith(".json",StringComparison.OrdinalIgnoreCase))output=Path.Combine(output,"campaign-playthrough.json");
            directory=Path.GetDirectoryName(output);Directory.CreateDirectory(directory);
            // Installed before any first-frame title interaction/autosave. Never
            // delete or overwrite a user's existing campaign directory.
            report.isolatedSaveDirectory=Path.Combine(directory,"isolated-playthrough-save-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            CampaignSaveStore.TestDirectory=report.isolatedSaveDirectory;
            activityLease=MacDiagnosticActivityLease.Acquire();report.diagnosticActivityLeaseActive=activityLease.Active;
            Application.logMessageReceived+=Log;started=Time.realtimeSinceStartup;
        }
        void Log(string message,string stack,LogType kind)
        {if(kind==LogType.Error||kind==LogType.Exception)errors.Add(message+"\n"+stack);}
        IEnumerator Start()
        {
            try
            {
            var stack=new Stack<IEnumerator>();stack.Push(Execute());
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}
                catch(Exception error){report.failure=error.ToString();break;}
                if(!moved){stack.Pop();continue;}
                if(current is IEnumerator nested){stack.Push(nested);continue;}yield return current;
            }
            if(controlled)controlled.enabled=controlledWasEnabled;
            report.wallSeconds=Time.realtimeSinceStartup-started;report.engineErrors=errors.ToArray();
            report.success=report.failure==""&&errors.Count==0&&report.days.Count==2&&report.nightSaveRoundTripPassed;
            if(!report.success)
            {
                if(flow!=null)flow.SetPaused(true);Mark("failure");
                if(flow?.World!=null&&!string.IsNullOrEmpty(currentTarget)&&flow.World.HasAnchor(currentTarget))
                    File.WriteAllText(Path.ChangeExtension(output,"failure-scene.json"),TraversalValidation.CaptureRouteFailure(flow,currentTarget));
                yield return new WaitForEndOfFrame();string picture=Path.ChangeExtension(output,"failure.png");ScreenCapture.CaptureScreenshot(picture);
                float until=Time.realtimeSinceStartup+3;while(!File.Exists(picture)&&Time.realtimeSinceStartup<until)yield return null;
            }
            ReleaseActivityLease();
            File.WriteAllText(output,JsonUtility.ToJson(report,true));File.WriteAllText(Path.ChangeExtension(output,"csv"),trace.ToString());
            Debug.Log((report.success?"CAMPAIGN_PLAYTHROUGH_PASS ":"CAMPAIGN_PLAYTHROUGH_FAIL ")+output+" "+report.failure);
            yield return null;if(!Application.isEditor)Application.Quit(report.success?0:1);
            }
            finally{ReleaseActivityLease();}
        }
        IEnumerator Execute()
        {
            Need(!Application.isBatchMode&&!Application.isEditor,"Run this acceptance in the graphical native Player; travel and normal clock must remain enabled");
            foreach(string flag in new[]{"--smoke-test","--traversal-test","--interaction-test","--capture-dir","--business-test","--campaign-visual"})
                Need(Array.IndexOf(Environment.GetCommandLineArgs(),flag)<0,"Do not combine real two-day input with another driver: "+flag);
            Application.targetFrameRate=60;
            float deadline=Time.realtimeSinceStartup+30;
            while((GameFlow.Instance==null||GameFlow.Instance.World==null)&&Time.realtimeSinceStartup<deadline)yield return null;
            flow=GameFlow.Instance;Need(flow!=null&&flow.World!=null,"GameFlow did not initialize");
            Need(GameFlow.PresentationEnabled,"Normal15-second travel presentation is disabled");
            var profile=new RunProfile{careerId="career2",selectedTraits=new[]{"耳背"},debtDifficulty="标准"};
            Need(profile.Validate(out string reason),"Legitimate profile failed: "+reason);Need(profile.Balance==0,"Profile must pay its four-point deficit");
            CharacterCreationUI.Dismiss();flow.StartProfile(profile);yield return null;yield return null;
            Need(flow.Campaign!=null&&flow.Campaign.dayIndex==1&&flow.Run.CashCents==profile.InitialCashCents,"Production profile start did not create the expected campaign");
            Mark("legal-profile-career2");
            yield return Day(1,2,true);
            Need(flow.Run.Phase==GamePhase.Night&&NightHubUI.IsOpen,"Day1 did not reach the real night hub");
            string campaignBefore=JsonUtility.ToJson(flow.Campaign);int cash=flow.Campaign.cashCents;
            Need(flow.SaveCampaign(),"Night save failed: "+flow.SaveStatus);
            flow.SaveAndReturnToTitle();yield return null;yield return null;
            Need(CharacterCreationUI.IsOpen,"Save and return did not open title/character selection");
            Need(flow.ContinueCampaign(),"Continue failed: "+flow.SaveStatus);yield return null;yield return null;
            Need(flow.Run.Phase==GamePhase.Night&&flow.Campaign.dayIndex==1&&flow.Campaign.cashCents==cash,"Continue did not preserve night/day/cash");
            Need(JsonUtility.ToJson(flow.Campaign)==campaignBefore,"Continue changed the campaign inventory/equipment/day ledger");
            report.productionSaveRestorationUsed=true;report.nightSaveRoundTripPassed=true;Mark("night1-save-title-continue");
            flow.StartNextDay();yield return null;yield return null;
            Need(flow.Run.Phase==GamePhase.Home&&flow.Campaign.dayIndex==2,"Production next-day command failed");
            yield return Day(2,1,false);
            Need(flow.Run.Phase==GamePhase.Night&&flow.Campaign.dayIndex==2&&flow.Campaign.history.Count==2,"Two actual service days did not settle into night2");
            Need(flow.SaveCampaign(),"Final night save failed: "+flow.SaveStatus);Mark("night2-complete");
        }
        IEnumerator Day(int day,int servedTarget,bool fullProcurement)
        {
            var result=new DayResult{day=day,openingCash=flow.Run.CashCents};report.days.Add(result);
            Need(flow.Run.Phase==GamePhase.Home,"Day must begin in real Home");Control();flow.SetPaused(false);
            var moved=new List<string>();
            while(flow.Run.HasUnloadedEquipment)
            {
                Need(moved.Count<4,"Unexpected repeated heavy equipment pickup");string id=flow.Run.ActiveHomeEquipment;
                yield return Visit("StovePickup");yield return Press("StovePickup");
                Need(flow.Run.HoldingStove&&flow.Run.HeldEquipmentId==id,"Reached pickup did not produce held "+id+": "+flow.Run.Message);
                Mark("day"+day+"-held-"+id);
                yield return Visit("HomeCart");yield return Press("HomeCart");
                Need(!flow.Run.HoldingStove&&flow.Run.LoadedEquipment.Contains(id),"Real loading did not place "+id+": "+flow.Run.Message);moved.Add(id);
            }
            result.heavyMoved=moved.ToArray();Need(moved.Contains("B03")&&moved.Contains("E03"),"Both inherited heavy items must actually be carried on each day");
            result.homeUsed=flow.Run.HomeDuration-flow.Run.Remaining;Mark("day"+day+"-home-loaded");
            yield return Visit("HomeExit");float travelStart=Time.realtimeSinceStartup;yield return Press("HomeExit");
            result.travelWallSeconds=Time.realtimeSinceStartup-travelStart;
            Need(result.travelWallSeconds>=14.5f,"Travel was skipped or shortened");
            Need(flow.Run.Phase==GamePhase.Market&&!flow.IsTransitioning&&!flow.Run.TimeoutSettlement,"Actual home exit did not reach the Market");
            Control();Need(flow.World.ShoppingMotor!=null&&flow.World.ShoppingMotor.IsAttached,"Market must start connected to the actual shopping cart");
            var bought=new List<string>();string[] groceries=fullProcurement?new[]{"rice","egg","seasoning"}:new[]{"rice"};
            foreach(string id in groceries)
            {
                string shelf=GameCatalog.Find(id).anchor;yield return Visit(shelf);yield return Press(shelf);
                Need(flow.Run.Cart.Contains(id)&&string.IsNullOrEmpty(flow.Run.HeldGrocery),"Actual push-cart pickup failed for "+id+": "+flow.Run.Message);bought.Add(id);
            }
            if(fullProcurement)
            {
                yield return Visit("SupplyCounter");yield return Press("SupplyCounter");
                Need(SupplyShop.IsOpen,"Reached supply counter did not open the production shop");
                Need(flow.ChangeSupply("P05",false),"Production supply purchase rejected P05: "+flow.Run.Message);
                Need(flow.Run.Cart.Contains("P05"),"P05 was not in unpaid basket");bought.Add("P05");SupplyShop.Close();yield return null;
            }
            Need(!flow.Run.Paid,"Basket was silently paid before cashier");int beforeCash=flow.Run.CashCents,bill=flow.Run.CartTotalCents;
            Mark("day"+day+"-unpaid-basket");
            yield return Visit("Cashier");yield return Press("Cashier");
            Need(flow.Run.CheckoutInProgress||flow.Run.Paid,"Real cashier did not begin service");
            float checkoutDeadline=Time.realtimeSinceStartup+flow.Run.CheckoutDuration+10;
            while(flow.Run.CheckoutInProgress){Need(Time.realtimeSinceStartup<checkoutDeadline,"Manual cashier stalled");yield return null;}
            Need(flow.Run.Paid&&flow.Run.CashCents==beforeCash-bill,"Manual checkout charged incorrectly");
            yield return Visit("MarketCart");yield return Press("MarketCart");
            foreach(string id in bought)Need(flow.Run.Cargo.Contains(id),"Paid item not physically loaded: "+id);
            Need(flow.Run.Cart.Count==0&&flow.Run.Loaded,"Cart was not emptied into owned vehicle");
            result.purchased=bought.ToArray();result.purchases=flow.Run.SpentCents;result.marketUsed=flow.Run.MarketUsed;Mark("day"+day+"-market-loaded");
            yield return Visit("MarketExit");yield return Press("MarketExit");
            Need(flow.Run.Phase==GamePhase.SiteSelection&&flow.Business!=null,"Actual market exit did not open site selection");
            BusinessController business=flow.Business;Need(!business.Session.Setup.practice,"Two-day acceptance must never enter practice setup");
            business.SelectDistrict(1);
            foreach(int recipe in new List<int>(business.Session.SelectedRecipes))if(recipe!=0)business.ToggleRecipe(recipe);
            if(!business.Session.SelectedRecipes.Contains(0))business.ToggleRecipe(0);
            Need(business.Session.Missing(0)=="","Actual purchases cannot cook fried rice: "+business.Session.Missing(0));
            business.Open();yield return null;
            Need(business.Session.Started&&flow.Run.Phase==GamePhase.Business,"Production Open failed");Mark("day"+day+"-open");
            float decisionAt=Time.realtimeSinceStartup,serviceDeadline=Time.realtimeSinceStartup+180;
            while(business.Session.Served<servedTarget)
            {
                Need(!business.Session.Finished&&Time.realtimeSinceStartup<serviceDeadline,"Could not serve required orders on normal clock");
                if(Time.realtimeSinceStartup>=decisionAt){DriveBusiness(business);decisionAt=Time.realtimeSinceStartup+.3f;}
                var audit=business.Session.CaptureAudit();Need(audit.cashConserved&&audit.packagesConserved&&audit.nonNegativeInventory&&audit.nonNegativeEnergy,"Actual service failed conservation");
                Need(business.Stage.CaptureGuestOverlaps().Length==0,"Actual service guests overlap");yield return null;
            }
            float drainDeadline=Time.realtimeSinceStartup+20;
            while(business.Stage.WorkActorBusy||business.PendingWorkCount>0)
            {Need(Time.realtimeSinceStartup<drainDeadline,"Production work queue did not drain after the served target");yield return null;}
            result.served=business.Session.Served;result.businessSeconds=business.Session.Elapsed;result.business=business.Session.CaptureAudit();
            Mark("day"+day+"-served-"+result.served);business.CloseDay();
            float closingDeadline=Time.realtimeSinceStartup+8;
            while(flow.Run.Phase!=GamePhase.DaySummary){Need(Time.realtimeSinceStartup<closingDeadline,"Early close failed to settle");yield return null;}
            Need(business.Session.Finished,"Close command did not finish actual service");business.Continue();yield return null;yield return null;
            Need(flow.Run.Phase==GamePhase.Night&&flow.Campaign.lastDay!=null,"Settled service did not enter night");
            result.nightCash=flow.Campaign.cashCents;result.revenue=flow.Campaign.lastDay.revenueCents;result.nightFuel=flow.Campaign.lastDay.nightFuelCents;
            Need(flow.Campaign.lastDay.served>=servedTarget,"Night ledger lost real served orders");Mark("day"+day+"-night");
        }
        void Control()
        {
            if(controlled)controlled.enabled=controlledWasEnabled;
            controlled=flow.World.Player;controlledWasEnabled=controlled.enabled;controlled.enabled=false;
            // Only replace the hardware-input caller. Physics, character motor,
            // trolley, carry hull, NPCs and GameFlow clocks remain production code.
        }
        IEnumerator Visit(string target)
        {
            currentTarget=target;WorldRuntime world=flow.World;GamePhase phase=flow.Run.Phase;
            Need(world.HasAnchor(target),"Missing real target "+target);int calls=0,stalls=0,noRouteWaits=0,arrivalWaits=0;float targetStartRemaining=flow.Run.Remaining;float travelledSinceCheck=0,activeSinceCheck=0;
            while(true)
            {
                Need(flow.World==world&&flow.Run.Phase==phase&&!flow.Run.TimeoutSettlement,"Phase changed before actual arrival at "+target+": "+flow.Run.Message);
                Need(targetStartRemaining-flow.Run.Remaining<90,"Target approach exhausted90 seconds of normal game time: "+target);
                if(AtTarget(target))
                {
                    yield return HoldStill(.20f,phase);
                    if(AtTarget(target))yield break;
                    Need(arrivalWaits++<2,"Target did not remain usable after two normal arrival waits: "+target);
                    report.arrivalTrafficWaits++;Mark("arrival-traffic-wait-"+target+"-"+arrivalWaits);
                    yield return HoldStill(2,phase);continue;
                }
                if(arrivalWaits<2&&CustomerBlocksNearbyTarget(world,target))
                {
                    arrivalWaits++;report.arrivalTrafficWaits++;Mark("nearby-traffic-wait-"+target+"-"+arrivalWaits);
                    yield return HoldStill(2,phase);continue;
                }
                Need(++calls<=140,"Input planner did not reach "+target+"; not evidence of physical impossibility");
                flow.SetPaused(true);float planningStart=Time.realtimeSinceStartup;TraversalValidation.KeyboardHint hint=null;
                yield return TraversalValidation.PlanKeyboardHint(flow,target,value=>hint=value);
                report.planningCalls++;report.planningWallSeconds+=Time.realtimeSinceStartup-planningStart;
                Need(hint!=null,"Planner returned no command");report.planningComputeSeconds+=hint.searchComputeSeconds;
                if(hint.blocked)
                {
                    string auditFile=Path.ChangeExtension(output,"route-"+target+"-"+calls+".json");
                    File.WriteAllText(auditFile,TraversalValidation.CaptureRouteFailure(flow,target));
                    Debug.Log("CAMPAIGN_ROUTE_BLOCKED target="+target+" position="+controlled.transform.position.ToString("F4")+" reason="+hint.reason+" audit="+auditFile);
                    Mark("blocked-"+target+"-"+calls);
                    Need(noRouteWaits++==0,"Input route unavailable after one normal2-second traffic wait: "+target+"; "+hint.reason+"; inspect exact live-physics audit "+auditFile);
                    yield return null;yield return null;flow.SetPaused(false);yield return null;
                    yield return HoldStill(2,phase);continue;
                }
                yield return null;yield return null;flow.SetPaused(false);yield return null;
                if(hint.ready)
                {
                    // Publish focus after normal Update, then require the actual
                    // action gate; never accept a planner's approximation alone.
                    yield return new WaitForSeconds(.12f);
                    // A moving passerby can occlude a formerly valid endpoint.
                    // Re-check through the same bounded arrival/traffic loop.
                    continue;
                }
                Need(hint.axis>=0&&hint.axis<axes.Length&&hint.distance>0,"Invalid input segment");
                Vector3 forward=world.View.transform.forward,right=world.View.transform.right;
                forward.y=0;right.y=0;forward.Normalize();right.Normalize();
                Vector3 commandDirection=forward*axes[hint.axis].y+right*axes[hint.axis].x;
                Vector3 segmentStart=controlled.transform.position;
                float elapsed=0,segmentProgress=0,blockedWindow=0,blockedDistance=0;
                float acceleration=controlled.HeavyCarry?3.5f:controlled.carrying?6f:controlled.pushing?4.5f:18f;
                float inputBudget=hint.seconds+controlled.EffectiveTopSpeed/acceleration+.8f;
                if(calls%10==1)
                {
                    Debug.Log("CAMPAIGN_INPUT_SEGMENT target="+target+" call="+calls+" start="+segmentStart.ToString("F3")+
                        " predictedEnd="+hint.plannedEnd.ToString("F3")+" checkedDistance="+hint.distance.ToString("F3")+
                        " axis="+hint.axis+" searchStates="+hint.explored+" searchComputeSeconds="+hint.searchComputeSeconds.ToString("F3")+
                        " searchWallSeconds="+hint.searchWallSeconds.ToString("F3"));
                    Mark("input-"+target+"-"+calls);
                }
                while(segmentProgress<hint.distance-.015f&&elapsed<inputBudget)
                {
                    Need(flow.Run.Phase==phase&&!flow.Run.TimeoutSettlement,"Timer expired while physically moving toward "+target);
                    float dt=Time.deltaTime;if(dt<=0){yield return null;continue;}
                    Vector3 before=controlled.transform.position;controlled.DriveInput(axes[hint.axis],false,dt);
                    float moved=FlatDistance(before,controlled.transform.position);report.pathMetres+=moved;report.movementFrames++;report.maxMovementDelta=Mathf.Max(report.maxMovementDelta,dt);
                    travelledSinceCheck+=moved;activeSinceCheck+=dt;elapsed+=dt;Record(axes[hint.axis],dt);
                    segmentProgress=Vector3.Dot(controlled.transform.position-segmentStart,commandDirection);
                    blockedWindow+=dt;blockedDistance+=moved;
                    if(world.Crowd!=null){string[] overlaps=world.Crowd.CaptureOverlaps();if(overlaps.Length>0){report.overlapFrames++;Need(false,"Market physical overlap during actual movement: "+string.Join(";",overlaps));}}
                    if(flow.State.targetId==target&&flow.IsActionInRange(target)&&flow.State.primaryEnabled)break;
                    // A dynamic obstacle can invalidate an advisory route. Let the
                    // production sweep stop us, then replan from that actual pose.
                    if(blockedWindow>=.5f)
                    {if(blockedDistance<.035f)break;blockedWindow=0;blockedDistance=0;}
                    yield return null;
                }
                if(activeSinceCheck>=1.2f)
                {if(travelledSinceCheck<.08f)stalls++;else stalls=0;Need(stalls<3,"Repeated real DriveInput blockage at "+target+"; inspect trace/colliders instead of moving the player");activeSinceCheck=0;travelledSinceCheck=0;}
            }
        }
        bool AtTarget(string target)=>flow.State.targetId==target&&flow.IsActionInRange(target)&&flow.State.primaryEnabled;
        static bool CustomerBlocksNearbyTarget(WorldRuntime world,string target)
        {
            Vector3 p=world.Player.transform.position,anchor=world.Anchor(target);
            if(FlatDistance(p,anchor)>1.55f)return false;
            return Physics.Linecast(p+Vector3.up*.8f,anchor+Vector3.up*.8f,out RaycastHit hit,WorldRuntime.EnvironmentMask,QueryTriggerInteraction.Ignore)&&hit.collider is CharacterController;
        }
        IEnumerator HoldStill(float seconds,GamePhase phase)
        {
            float elapsed=0;
            while(elapsed<seconds)
            {
                Need(flow.Run.Phase==phase&&!flow.Run.TimeoutSettlement,"Normal waiting exhausted the active phase");
                float dt=Time.deltaTime;Vector3 before=controlled.transform.position;controlled.DriveInput(Vector2.zero,false,dt);
                report.pathMetres+=FlatDistance(before,controlled.transform.position);report.movementFrames++;report.maxMovementDelta=Mathf.Max(report.maxMovementDelta,dt);
                elapsed+=dt;Record(Vector2.zero,dt);
                if(flow.World.Crowd!=null)Need(flow.World.Crowd.CaptureOverlaps().Length==0,"Physical overlap during normal braking/traffic wait");
                yield return null;
            }
        }
#if UNITY_EDITOR
        // Uses precisely the same Visit coroutine for a captured-scene arrival
        // regression. This is explicitly not a campaign/economy acceptance run.
        public static void BeginReachProbe(GameFlow game,string target,Action<string,int> completed)
        {
            var driver=new GameObject("CapturedSceneReachProbe").AddComponent<CampaignPlaythroughValidation>();driver.enabled=false;
            driver.flow=game;driver.Control();driver.StartCoroutine(driver.ReachProbe(target,completed));
        }
        IEnumerator ReachProbe(string target,Action<string,int> completed)
        {
            var stack=new Stack<IEnumerator>();stack.Push(Visit(target));string failure="";
            while(stack.Count>0)
            {
                bool moved=false;object current=null;
                try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception error){failure=error.ToString();break;}
                if(!moved){stack.Pop();continue;}if(current is IEnumerator child){stack.Push(child);continue;}yield return current;
            }
            if(controlled)controlled.enabled=controlledWasEnabled;ReleaseActivityLease();completed(failure,report.arrivalTrafficWaits);Destroy(gameObject);
        }
#endif
        IEnumerator Press(string target)
        {
            Need(flow.State.targetId==target&&flow.IsActionInRange(target)&&flow.State.primaryEnabled,"Cannot press an out-of-range action: "+target);
            flow.PrimaryAction();yield return null;float until=Time.realtimeSinceStartup+45;
            while(flow.IsBusy){Need(Time.realtimeSinceStartup<until,"Production transfer/transition timed out: "+target);yield return null;}
            yield return null;
        }
        static void DriveBusiness(BusinessController c)
        {
            if(!c.CanInteract||c.PendingWorkCount>=3)return;BusinessSession s=c.Session;
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Utensil&&!c.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){c.UseCookware(lane.index);return;}
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Season&&!c.HasPendingIntent(StallInteractionKind.Seasoning,lane.index)){c.UseSeasoning(lane.index);return;}
            foreach(var lane in s.Lanes)if(lane.step==KitchenStep.Pack&&!c.HasPendingIntent(StallInteractionKind.Packaging,lane.index)){c.UsePackage(lane.index,"P05");return;}
            foreach(var lane in s.Lanes)if((lane.step==KitchenStep.Heating||lane.step==KitchenStep.Prep||lane.step==KitchenStep.Dirty)&&!c.HasPendingIntent(StallInteractionKind.Cookware,lane.index)){c.UseCookware(lane.index);return;}
            StallOrder best=null;KitchenLane pan=null;
            foreach(var order in s.Orders)
            {
                if(!order.arrived||order.served||order.departed||order.lane>=0||order.reservedLane>=0)continue;
                foreach(var lane in s.Lanes)
                    if(lane.recipeIndex==order.recipeIndex&&lane.reservedOrderId<0&&(lane.step==KitchenStep.Idle||lane.step==KitchenStep.ReadyStock)&&(best==null||order.patience<best.patience)){best=order;pan=lane;}
            }
            if(best==null)return;
            if(c.SelectedOrderId!=best.id){c.SelectOrder(best.id);return;}
            if(pan.step==KitchenStep.ReadyStock)c.UseCookware(pan.index);else c.UseIngredient("rice");
        }
        void Mark(string name)
        {
            if(flow==null)return;
            var c=new Checkpoint{name=name,phase=flow.Run.Phase.ToString(),day=flow.Campaign?.dayIndex??0,cash=flow.Run.CashCents,held=flow.Run.HeldEquipmentId+flow.Run.HeldGrocery,
                focus=flow.State.targetId,message=flow.Run.Message,wallSeconds=Time.realtimeSinceStartup-started,remaining=flow.Run.Remaining,marketUsed=flow.Run.MarketUsed,
                loaded=flow.Run.LoadedEquipment.ToArray(),cart=flow.Run.Cart.ToArray(),cargo=flow.Run.Cargo.ToArray(),served=flow.Business?.Session.Served??0,business=flow.Business?.Session.CaptureAudit()};
            if(flow.World!=null&&flow.World.Player!=null)c.player=flow.World.Player.transform.position;
            report.checkpoints.Add(c);Debug.Log("CAMPAIGN_PLAYTHROUGH_CHECK "+name+" day="+c.day+" phase="+c.phase+" cash="+c.cash);
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
            File.WriteAllText(Path.ChangeExtension(output,"csv"),trace.ToString());
        }
        void Record(Vector2 input,float dt)
        {
            if(Time.realtimeSinceStartup<nextTrace)return;nextTrace=Time.realtimeSinceStartup+.20f;
            Vector3 p=controlled.transform.position;trace.AppendFormat(CultureInfo.InvariantCulture,"{0:F3},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F2},{8:F3},{9:F3},{10:F6},{11:F3},{12:F3}\n",
                Time.realtimeSinceStartup-started,(flow.Campaign?.dayIndex??0),flow.Run.Phase,currentTarget,p.x,p.y,p.z,controlled.visual.eulerAngles.y,input.x,input.y,dt,flow.Run.Remaining,flow.Run.MarketUsed);
        }
        static float FlatDistance(Vector3 a,Vector3 b){a.y=0;b.y=0;return Vector3.Distance(a,b);}
        static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        void ReleaseActivityLease()
        {if(activityLease==null)return;activityLease.Dispose();activityLease=null;report.diagnosticActivityLeaseReleased=true;}
        void OnDestroy(){ReleaseActivityLease();Application.logMessageReceived-=Log;CampaignSaveStore.TestDirectory=null;}
    }
}
